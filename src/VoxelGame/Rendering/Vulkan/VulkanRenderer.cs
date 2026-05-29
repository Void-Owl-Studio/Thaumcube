using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.Core;
using Silk.NET.Core.Native;
using Silk.NET.Shaderc;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using Silk.NET.Windowing;
using VoxelGame.Core;
using VoxelGame.World.Chunks;
using Buffer = Silk.NET.Vulkan.Buffer;
using VkSemaphore = Silk.NET.Vulkan.Semaphore;

namespace VoxelGame.Rendering.Vulkan;

public unsafe sealed class VulkanRenderer : IDisposable
{
    private const int MaxFramesInFlight = 1;

    private readonly GameSettings _settings;
    private readonly Dictionary<string, GpuChunkMesh> _gpuMeshes = new();
    private Vk _vk = null!;
    private SpirvShaderCompiler _shaderCompiler = null!;
    private KhrSurface _surfaceApi = null!;
    private KhrSwapchain _swapchainApi = null!;
    private IWindow? _window;
    private Instance _instance;
    private SurfaceKHR _surface;
    private PhysicalDevice _physicalDevice;
    private Device _device;
    private Queue _graphicsQueue;
    private Queue _presentQueue;
    private uint _graphicsQueueFamily;
    private uint _presentQueueFamily;
    private SwapchainKHR _swapchain;
    private Format _swapchainImageFormat;
    private Extent2D _swapchainExtent;
    private Image[] _swapchainImages = [];
    private ImageView[] _swapchainImageViews = [];
    private RenderPass _renderPass;
    private DescriptorSetLayout _descriptorSetLayout;
    private PipelineLayout _pipelineLayout;
    private Pipeline _graphicsPipeline;
    private Framebuffer[] _framebuffers = [];
    private CommandPool _commandPool;
    private CommandBuffer[] _commandBuffers = [];
    private DescriptorPool _descriptorPool;
    private DescriptorSet _descriptorSet;
    private Buffer _cameraUniformBuffer;
    private DeviceMemory _cameraUniformMemory;
    private Image _blockAtlasImage;
    private DeviceMemory _blockAtlasMemory;
    private ImageView _blockAtlasImageView;
    private Sampler _blockAtlasSampler;
    private Image _environmentAtlasImage;
    private DeviceMemory _environmentAtlasMemory;
    private ImageView _environmentAtlasImageView;
    private Sampler _environmentAtlasSampler;
    private Image _depthImage;
    private DeviceMemory _depthImageMemory;
    private ImageView _depthImageView;
    private readonly VkSemaphore[] _imageAvailable = new VkSemaphore[MaxFramesInFlight];
    private readonly VkSemaphore[] _renderFinished = new VkSemaphore[MaxFramesInFlight];
    private readonly Fence[] _inFlight = new Fence[MaxFramesInFlight];
    private VulkanImmediatePreview _hudPreview = null!;
    private bool _initialized;
    private bool _framebufferResized;
    private double _titleRefresh;

    public VulkanRenderer(GameSettings settings)
    {
        _settings = settings;
    }

    public void Initialize(IWindow window)
    {
        _window = window;
        _vk = Vk.GetApi();
        _shaderCompiler = new SpirvShaderCompiler();
        _hudPreview = new VulkanImmediatePreview(_vk);

        CreateInstance(window);
        CreateSurface(window);
        PickPhysicalDevice();
        CreateLogicalDevice();
        CreateCommandPool();
        CreateDescriptorSetLayout();
        CreateSwapchainResources();
        CreateCameraUniformBuffer();
        CreateBlockAtlasResources();
        CreateEnvironmentAtlasResources();
        CreateDescriptorPool();
        CreateDescriptorSet();
        CreateCommandBuffers();
        CreateSyncObjects();

        _initialized = true;
    }

    public void Resize(int width, int height)
    {
        _framebufferResized = width > 0 && height > 0;
    }

    public void Render(Rendering.RenderScene scene)
    {
        if (!_initialized || _window is null || _window.Size.X <= 0 || _window.Size.Y <= 0)
        {
            return;
        }

        UpdateWindowTitle(scene);
        _vk.WaitForFences(_device, 1, in _inFlight[0], true, ulong.MaxValue);
        SyncChunkMeshes(scene.ChunkMeshes);
        UpdateCameraUniform(scene);

        uint imageIndex = 0;
        var acquire = _swapchainApi.AcquireNextImage(_device, _swapchain, ulong.MaxValue, _imageAvailable[0], default, ref imageIndex);
        if (acquire == Result.ErrorOutOfDateKhr)
        {
            RecreateSwapchain();
            return;
        }

        ThrowIfFailed(acquire, "acquire swapchain image");
        _vk.ResetFences(_device, 1, in _inFlight[0]);

        var waitSemaphore = _imageAvailable[0];
        var signalSemaphore = _renderFinished[0];
        var waitStage = PipelineStageFlags.ColorAttachmentOutputBit;
        var commandBuffer = _commandBuffers[imageIndex];
        _vk.ResetCommandBuffer(commandBuffer, 0);
        RecordCommandBuffer(commandBuffer, _framebuffers[imageIndex], scene);

        var submitInfo = new SubmitInfo
        {
            SType = StructureType.SubmitInfo,
            WaitSemaphoreCount = 1,
            PWaitSemaphores = &waitSemaphore,
            PWaitDstStageMask = &waitStage,
            CommandBufferCount = 1,
            PCommandBuffers = &commandBuffer,
            SignalSemaphoreCount = 1,
            PSignalSemaphores = &signalSemaphore
        };

        ThrowIfFailed(_vk.QueueSubmit(_graphicsQueue, 1, &submitInfo, _inFlight[0]), "submit draw command buffer");

        var swapchain = _swapchain;
        var presentInfo = new PresentInfoKHR
        {
            SType = StructureType.PresentInfoKhr,
            WaitSemaphoreCount = 1,
            PWaitSemaphores = &signalSemaphore,
            SwapchainCount = 1,
            PSwapchains = &swapchain,
            PImageIndices = &imageIndex
        };

        var present = _swapchainApi.QueuePresent(_presentQueue, &presentInfo);
        if (present is Result.ErrorOutOfDateKhr or Result.SuboptimalKhr || _framebufferResized)
        {
            _framebufferResized = false;
            RecreateSwapchain();
            return;
        }

        ThrowIfFailed(present, "present swapchain image");
    }

    private void CreateInstance(IWindow window)
    {
        var appName = SilkMarshal.StringToPtr("VoxelGame", NativeStringEncoding.UTF8);
        var engineName = SilkMarshal.StringToPtr("NoEngine", NativeStringEncoding.UTF8);

        try
        {
            var appInfo = new ApplicationInfo
            {
                SType = StructureType.ApplicationInfo,
                PApplicationName = (byte*)appName,
                ApplicationVersion = new Version32(0, 1, 0),
                PEngineName = (byte*)engineName,
                EngineVersion = new Version32(0, 1, 0),
                ApiVersion = Vk.Version12
            };

            var surfaceSource = window.VkSurface ?? throw new InvalidOperationException("Window did not expose a Vulkan surface provider.");
            var requiredExtensions = surfaceSource.GetRequiredExtensions(out var extensionCount);

            var createInfo = new InstanceCreateInfo
            {
                SType = StructureType.InstanceCreateInfo,
                PApplicationInfo = &appInfo,
                EnabledExtensionCount = extensionCount,
                PpEnabledExtensionNames = requiredExtensions
            };

            ThrowIfFailed(_vk.CreateInstance(&createInfo, null, out _instance), "create Vulkan instance");
        }
        finally
        {
            SilkMarshal.Free(appName);
            SilkMarshal.Free(engineName);
        }

        if (!_vk.TryGetInstanceExtension(_instance, out _surfaceApi))
        {
            throw new InvalidOperationException("VK_KHR_surface is required but could not be loaded.");
        }
    }

    private void CreateSurface(IWindow window)
    {
        var handle = window.VkSurface!.Create<AllocationCallbacks>(new VkHandle(_instance.Handle), null);
        _surface = new SurfaceKHR(handle.Handle);
    }

    private void PickPhysicalDevice()
    {
        uint deviceCount = 0;
        _vk.EnumeratePhysicalDevices(_instance, &deviceCount, null);
        if (deviceCount == 0)
        {
            throw new InvalidOperationException("No Vulkan-capable GPU was found.");
        }

        var devices = stackalloc PhysicalDevice[(int)deviceCount];
        _vk.EnumeratePhysicalDevices(_instance, &deviceCount, devices);

        for (var i = 0; i < deviceCount; i++)
        {
            if (TryFindQueueFamilies(devices[i], out var graphics, out var present) && SupportsSwapchain(devices[i]))
            {
                _physicalDevice = devices[i];
                _graphicsQueueFamily = graphics;
                _presentQueueFamily = present;
                return;
            }
        }

        throw new InvalidOperationException("No suitable Vulkan device with graphics and presentation support was found.");
    }

    private void CreateLogicalDevice()
    {
        var queuePriority = 1.0f;
        Span<uint> uniqueFamilies = _graphicsQueueFamily == _presentQueueFamily
            ? stackalloc[] { _graphicsQueueFamily }
            : stackalloc[] { _graphicsQueueFamily, _presentQueueFamily };

        var queueInfos = stackalloc DeviceQueueCreateInfo[uniqueFamilies.Length];
        for (var i = 0; i < uniqueFamilies.Length; i++)
        {
            queueInfos[i] = new DeviceQueueCreateInfo
            {
                SType = StructureType.DeviceQueueCreateInfo,
                QueueFamilyIndex = uniqueFamilies[i],
                QueueCount = 1,
                PQueuePriorities = &queuePriority
            };
        }

        var swapchainExtension = SilkMarshal.StringToPtr(KhrSwapchain.ExtensionName, NativeStringEncoding.UTF8);
        try
        {
            var deviceExtensions = stackalloc byte*[1];
            deviceExtensions[0] = (byte*)swapchainExtension;

            var features = new PhysicalDeviceFeatures
            {
                FillModeNonSolid = false
            };

            var createInfo = new DeviceCreateInfo
            {
                SType = StructureType.DeviceCreateInfo,
                QueueCreateInfoCount = (uint)uniqueFamilies.Length,
                PQueueCreateInfos = queueInfos,
                PEnabledFeatures = &features,
                EnabledExtensionCount = 1,
                PpEnabledExtensionNames = deviceExtensions
            };

            ThrowIfFailed(_vk.CreateDevice(_physicalDevice, &createInfo, null, out _device), "create logical device");
        }
        finally
        {
            SilkMarshal.Free(swapchainExtension);
        }

        _vk.GetDeviceQueue(_device, _graphicsQueueFamily, 0, out _graphicsQueue);
        _vk.GetDeviceQueue(_device, _presentQueueFamily, 0, out _presentQueue);

        if (!_vk.TryGetDeviceExtension(_instance, _device, out _swapchainApi))
        {
            throw new InvalidOperationException("VK_KHR_swapchain is required but could not be loaded.");
        }
    }

    private void CreateCommandPool()
    {
        var poolInfo = new CommandPoolCreateInfo
        {
            SType = StructureType.CommandPoolCreateInfo,
            QueueFamilyIndex = _graphicsQueueFamily,
            Flags = CommandPoolCreateFlags.ResetCommandBufferBit
        };

        ThrowIfFailed(_vk.CreateCommandPool(_device, &poolInfo, null, out _commandPool), "create command pool");
    }

    private void CreateDescriptorSetLayout()
    {
        var cameraBinding = new DescriptorSetLayoutBinding
        {
            Binding = 0,
            DescriptorType = DescriptorType.UniformBuffer,
            DescriptorCount = 1,
            StageFlags = ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit
        };

        var atlasBinding = new DescriptorSetLayoutBinding
        {
            Binding = 1,
            DescriptorType = DescriptorType.CombinedImageSampler,
            DescriptorCount = 1,
            StageFlags = ShaderStageFlags.FragmentBit
        };

        var environmentBinding = new DescriptorSetLayoutBinding
        {
            Binding = 2,
            DescriptorType = DescriptorType.CombinedImageSampler,
            DescriptorCount = 1,
            StageFlags = ShaderStageFlags.FragmentBit
        };

        var bindings = stackalloc[] { cameraBinding, atlasBinding, environmentBinding };

        var layoutInfo = new DescriptorSetLayoutCreateInfo
        {
            SType = StructureType.DescriptorSetLayoutCreateInfo,
            BindingCount = 3,
            PBindings = bindings
        };

        ThrowIfFailed(_vk.CreateDescriptorSetLayout(_device, &layoutInfo, null, out _descriptorSetLayout), "create descriptor set layout");
    }

    private void CreateSwapchainResources()
    {
        CreateSwapchain();
        CreateImageViews();
        CreateRenderPass();
        CreateDepthResources();
        CreateGraphicsPipeline();
        CreateFramebuffers();
    }

    private void CreateSwapchain()
    {
        var support = QuerySwapchainSupport(_physicalDevice);
        var surfaceFormat = ChooseSurfaceFormat(support.Formats);
        var presentMode = ChoosePresentMode(support.PresentModes);
        var extent = ChooseExtent(support.Capabilities);

        var imageCount = support.Capabilities.MinImageCount + 1;
        if (support.Capabilities.MaxImageCount > 0 && imageCount > support.Capabilities.MaxImageCount)
        {
            imageCount = support.Capabilities.MaxImageCount;
        }

        var createInfo = new SwapchainCreateInfoKHR
        {
            SType = StructureType.SwapchainCreateInfoKhr,
            Surface = _surface,
            MinImageCount = imageCount,
            ImageFormat = surfaceFormat.Format,
            ImageColorSpace = surfaceFormat.ColorSpace,
            ImageExtent = extent,
            ImageArrayLayers = 1,
            ImageUsage = ImageUsageFlags.ColorAttachmentBit,
            PreTransform = support.Capabilities.CurrentTransform,
            CompositeAlpha = CompositeAlphaFlagsKHR.OpaqueBitKhr,
            PresentMode = presentMode,
            Clipped = true,
            OldSwapchain = default
        };

        var queueFamilyIndices = stackalloc[] { _graphicsQueueFamily, _presentQueueFamily };
        if (_graphicsQueueFamily != _presentQueueFamily)
        {
            createInfo.ImageSharingMode = SharingMode.Concurrent;
            createInfo.QueueFamilyIndexCount = 2;
            createInfo.PQueueFamilyIndices = queueFamilyIndices;
        }
        else
        {
            createInfo.ImageSharingMode = SharingMode.Exclusive;
        }

        ThrowIfFailed(_swapchainApi.CreateSwapchain(_device, &createInfo, null, out _swapchain), "create swapchain");

        uint actualImageCount = 0;
        _swapchainApi.GetSwapchainImages(_device, _swapchain, &actualImageCount, null);
        _swapchainImages = new Image[actualImageCount];
        fixed (Image* images = _swapchainImages)
        {
            _swapchainApi.GetSwapchainImages(_device, _swapchain, &actualImageCount, images);
        }

        _swapchainImageFormat = surfaceFormat.Format;
        _swapchainExtent = extent;
    }

    private void CreateImageViews()
    {
        _swapchainImageViews = new ImageView[_swapchainImages.Length];

        for (var i = 0; i < _swapchainImages.Length; i++)
        {
            _swapchainImageViews[i] = CreateImageView(_swapchainImages[i], _swapchainImageFormat, ImageAspectFlags.ColorBit);
        }
    }

    private void CreateRenderPass()
    {
        var colorAttachment = new AttachmentDescription
        {
            Format = _swapchainImageFormat,
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = AttachmentLoadOp.Clear,
            StoreOp = AttachmentStoreOp.Store,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.Undefined,
            FinalLayout = ImageLayout.PresentSrcKhr
        };

        var depthAttachment = new AttachmentDescription
        {
            Format = FindDepthFormat(),
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = AttachmentLoadOp.Clear,
            StoreOp = AttachmentStoreOp.DontCare,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.Undefined,
            FinalLayout = ImageLayout.DepthStencilAttachmentOptimal
        };

        var colorAttachmentRef = new AttachmentReference(0, ImageLayout.ColorAttachmentOptimal);
        var depthAttachmentRef = new AttachmentReference(1, ImageLayout.DepthStencilAttachmentOptimal);
        var subpass = new SubpassDescription
        {
            PipelineBindPoint = PipelineBindPoint.Graphics,
            ColorAttachmentCount = 1,
            PColorAttachments = &colorAttachmentRef,
            PDepthStencilAttachment = &depthAttachmentRef
        };

        var dependencies = stackalloc SubpassDependency[2];
        dependencies[0] = new SubpassDependency
        {
            SrcSubpass = Vk.SubpassExternal,
            DstSubpass = 0,
            SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit | PipelineStageFlags.EarlyFragmentTestsBit,
            DstStageMask = PipelineStageFlags.ColorAttachmentOutputBit | PipelineStageFlags.EarlyFragmentTestsBit,
            DstAccessMask = AccessFlags.ColorAttachmentWriteBit | AccessFlags.DepthStencilAttachmentWriteBit
        };

        dependencies[1] = new SubpassDependency
        {
            SrcSubpass = 0,
            DstSubpass = Vk.SubpassExternal,
            SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
            SrcAccessMask = AccessFlags.ColorAttachmentWriteBit,
            DstStageMask = PipelineStageFlags.BottomOfPipeBit
        };

        var attachments = stackalloc[] { colorAttachment, depthAttachment };
        var renderPassInfo = new RenderPassCreateInfo
        {
            SType = StructureType.RenderPassCreateInfo,
            AttachmentCount = 2,
            PAttachments = attachments,
            SubpassCount = 1,
            PSubpasses = &subpass,
            DependencyCount = 2,
            PDependencies = dependencies
        };

        ThrowIfFailed(_vk.CreateRenderPass(_device, &renderPassInfo, null, out _renderPass), "create render pass");
    }

    private void CreateDepthResources()
    {
        var depthFormat = FindDepthFormat();
        CreateImage(
            _swapchainExtent.Width,
            _swapchainExtent.Height,
            depthFormat,
            ImageTiling.Optimal,
            ImageUsageFlags.DepthStencilAttachmentBit,
            MemoryPropertyFlags.DeviceLocalBit,
            out _depthImage,
            out _depthImageMemory);

        _depthImageView = CreateImageView(_depthImage, depthFormat, ImageAspectFlags.DepthBit);
    }

    private void CreateGraphicsPipeline()
    {
        var shaderBasePath = Path.Combine(AppContext.BaseDirectory, "Rendering", "Shaders");
        var vertexShaderCode = _shaderCompiler.CompileFile(Path.Combine(shaderBasePath, "voxel.vert"), ShaderKind.VertexShader);
        var fragmentShaderCode = _shaderCompiler.CompileFile(Path.Combine(shaderBasePath, "voxel.frag"), ShaderKind.FragmentShader);

        var vertexShaderModule = CreateShaderModule(vertexShaderCode);
        var fragmentShaderModule = CreateShaderModule(fragmentShaderCode);

        var entryPointName = SilkMarshal.StringToPtr("main", NativeStringEncoding.UTF8);

        try
        {
            var shaderStages = stackalloc PipelineShaderStageCreateInfo[2];
            shaderStages[0] = new PipelineShaderStageCreateInfo
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.VertexBit,
                Module = vertexShaderModule,
                PName = (byte*)entryPointName
            };

            shaderStages[1] = new PipelineShaderStageCreateInfo
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.FragmentBit,
                Module = fragmentShaderModule,
                PName = (byte*)entryPointName
            };

            var bindingDescription = new VertexInputBindingDescription
            {
                Binding = 0,
                Stride = (uint)Marshal.SizeOf<VoxelVertex>(),
                InputRate = VertexInputRate.Vertex
            };

            var attributeDescriptions = stackalloc VertexInputAttributeDescription[5];
            attributeDescriptions[0] = new VertexInputAttributeDescription
            {
                Binding = 0,
                Location = 0,
                Format = Format.R32G32B32Sfloat,
                Offset = (uint)Marshal.OffsetOf<VoxelVertex>(nameof(VoxelVertex.Position))
            };
            attributeDescriptions[1] = new VertexInputAttributeDescription
            {
                Binding = 0,
                Location = 1,
                Format = Format.R32G32B32Sfloat,
                Offset = (uint)Marshal.OffsetOf<VoxelVertex>(nameof(VoxelVertex.Normal))
            };
            attributeDescriptions[2] = new VertexInputAttributeDescription
            {
                Binding = 0,
                Location = 2,
                Format = Format.R32G32Sfloat,
                Offset = (uint)Marshal.OffsetOf<VoxelVertex>(nameof(VoxelVertex.Uv))
            };
            attributeDescriptions[3] = new VertexInputAttributeDescription
            {
                Binding = 0,
                Location = 3,
                Format = Format.R32Uint,
                Offset = (uint)Marshal.OffsetOf<VoxelVertex>(nameof(VoxelVertex.BlockId))
            };
            attributeDescriptions[4] = new VertexInputAttributeDescription
            {
                Binding = 0,
                Location = 4,
                Format = Format.R32G32B32Sfloat,
                Offset = (uint)Marshal.OffsetOf<VoxelVertex>(nameof(VoxelVertex.Tint))
            };

            var vertexInput = new PipelineVertexInputStateCreateInfo
            {
                SType = StructureType.PipelineVertexInputStateCreateInfo,
                VertexBindingDescriptionCount = 1,
                PVertexBindingDescriptions = &bindingDescription,
                VertexAttributeDescriptionCount = 5,
                PVertexAttributeDescriptions = attributeDescriptions
            };

            var inputAssembly = new PipelineInputAssemblyStateCreateInfo
            {
                SType = StructureType.PipelineInputAssemblyStateCreateInfo,
                Topology = PrimitiveTopology.TriangleList
            };

            var viewportState = new PipelineViewportStateCreateInfo
            {
                SType = StructureType.PipelineViewportStateCreateInfo,
                ViewportCount = 1,
                ScissorCount = 1
            };

            var rasterizer = new PipelineRasterizationStateCreateInfo
            {
                SType = StructureType.PipelineRasterizationStateCreateInfo,
                PolygonMode = PolygonMode.Fill,
                CullMode = CullModeFlags.BackBit,
                FrontFace = FrontFace.CounterClockwise,
                LineWidth = 1f
            };

            var multisampling = new PipelineMultisampleStateCreateInfo
            {
                SType = StructureType.PipelineMultisampleStateCreateInfo,
                RasterizationSamples = SampleCountFlags.Count1Bit
            };

            var depthStencil = new PipelineDepthStencilStateCreateInfo
            {
                SType = StructureType.PipelineDepthStencilStateCreateInfo,
                DepthTestEnable = true,
                DepthWriteEnable = true,
                DepthCompareOp = CompareOp.Less,
                DepthBoundsTestEnable = false,
                StencilTestEnable = false
            };

            var colorBlendAttachment = new PipelineColorBlendAttachmentState
            {
                BlendEnable = true,
                SrcColorBlendFactor = BlendFactor.SrcAlpha,
                DstColorBlendFactor = BlendFactor.OneMinusSrcAlpha,
                ColorBlendOp = BlendOp.Add,
                SrcAlphaBlendFactor = BlendFactor.One,
                DstAlphaBlendFactor = BlendFactor.Zero,
                AlphaBlendOp = BlendOp.Add,
                ColorWriteMask = ColorComponentFlags.RBit | ColorComponentFlags.GBit | ColorComponentFlags.BBit | ColorComponentFlags.ABit
            };

            var colorBlending = new PipelineColorBlendStateCreateInfo
            {
                SType = StructureType.PipelineColorBlendStateCreateInfo,
                AttachmentCount = 1,
                PAttachments = &colorBlendAttachment
            };

            var dynamicStates = stackalloc[] { DynamicState.Viewport, DynamicState.Scissor };
            var dynamicState = new PipelineDynamicStateCreateInfo
            {
                SType = StructureType.PipelineDynamicStateCreateInfo,
                DynamicStateCount = 2,
                PDynamicStates = dynamicStates
            };

            fixed (DescriptorSetLayout* setLayouts = &_descriptorSetLayout)
            {
                var pipelineLayoutInfo = new PipelineLayoutCreateInfo
                {
                    SType = StructureType.PipelineLayoutCreateInfo,
                    SetLayoutCount = 1,
                    PSetLayouts = setLayouts
                };

                ThrowIfFailed(_vk.CreatePipelineLayout(_device, &pipelineLayoutInfo, null, out _pipelineLayout), "create pipeline layout");
            }

            var pipelineInfo = new GraphicsPipelineCreateInfo
            {
                SType = StructureType.GraphicsPipelineCreateInfo,
                StageCount = 2,
                PStages = shaderStages,
                PVertexInputState = &vertexInput,
                PInputAssemblyState = &inputAssembly,
                PViewportState = &viewportState,
                PRasterizationState = &rasterizer,
                PMultisampleState = &multisampling,
                PDepthStencilState = &depthStencil,
                PColorBlendState = &colorBlending,
                PDynamicState = &dynamicState,
                Layout = _pipelineLayout,
                RenderPass = _renderPass,
                Subpass = 0
            };

            ThrowIfFailed(_vk.CreateGraphicsPipelines(_device, default, 1, &pipelineInfo, null, out _graphicsPipeline), "create graphics pipeline");
        }
        finally
        {
            SilkMarshal.Free(entryPointName);
            _vk.DestroyShaderModule(_device, fragmentShaderModule, null);
            _vk.DestroyShaderModule(_device, vertexShaderModule, null);
        }
    }

    private void CreateFramebuffers()
    {
        _framebuffers = new Framebuffer[_swapchainImageViews.Length];

        for (var i = 0; i < _swapchainImageViews.Length; i++)
        {
            var attachments = stackalloc[] { _swapchainImageViews[i], _depthImageView };
            var framebufferInfo = new FramebufferCreateInfo
            {
                SType = StructureType.FramebufferCreateInfo,
                RenderPass = _renderPass,
                AttachmentCount = 2,
                PAttachments = attachments,
                Width = _swapchainExtent.Width,
                Height = _swapchainExtent.Height,
                Layers = 1
            };

            ThrowIfFailed(_vk.CreateFramebuffer(_device, &framebufferInfo, null, out _framebuffers[i]), "create framebuffer");
        }
    }

    private void CreateCameraUniformBuffer()
    {
        CreateBuffer(
            (ulong)Marshal.SizeOf<CameraUniform>(),
            BufferUsageFlags.UniformBufferBit,
            MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit,
            out _cameraUniformBuffer,
            out _cameraUniformMemory);
    }

    private void CreateBlockAtlasResources()
    {
        var atlasBuilder = new BlockTextureAtlasBuilder(Path.Combine(AppContext.BaseDirectory, "Assets", "textures"));
        var atlas = atlasBuilder.Build();
        var imageSize = checked((ulong)atlas.Pixels.Length);

        CreateBuffer(
            imageSize,
            BufferUsageFlags.TransferSrcBit,
            MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit,
            out var stagingBuffer,
            out var stagingMemory);

        UploadBufferData(stagingMemory, atlas.Pixels);

        CreateImage(
            (uint)atlas.Width,
            (uint)atlas.Height,
            Format.R8G8B8A8Srgb,
            ImageTiling.Optimal,
            ImageUsageFlags.TransferDstBit | ImageUsageFlags.SampledBit,
            MemoryPropertyFlags.DeviceLocalBit,
            out _blockAtlasImage,
            out _blockAtlasMemory);

        TransitionImageLayout(_blockAtlasImage, Format.R8G8B8A8Srgb, ImageLayout.Undefined, ImageLayout.TransferDstOptimal);
        CopyBufferToImage(stagingBuffer, _blockAtlasImage, (uint)atlas.Width, (uint)atlas.Height);
        TransitionImageLayout(_blockAtlasImage, Format.R8G8B8A8Srgb, ImageLayout.TransferDstOptimal, ImageLayout.ShaderReadOnlyOptimal);

        _vk.DestroyBuffer(_device, stagingBuffer, null);
        _vk.FreeMemory(_device, stagingMemory, null);

        _blockAtlasImageView = CreateImageView(_blockAtlasImage, Format.R8G8B8A8Srgb, ImageAspectFlags.ColorBit);

        var samplerInfo = new SamplerCreateInfo
        {
            SType = StructureType.SamplerCreateInfo,
            MagFilter = Filter.Nearest,
            MinFilter = Filter.Nearest,
            AddressModeU = SamplerAddressMode.ClampToEdge,
            AddressModeV = SamplerAddressMode.ClampToEdge,
            AddressModeW = SamplerAddressMode.ClampToEdge,
            AnisotropyEnable = false,
            MaxAnisotropy = 1f,
            BorderColor = BorderColor.IntOpaqueBlack,
            UnnormalizedCoordinates = false,
            CompareEnable = false,
            CompareOp = CompareOp.Always,
            MipmapMode = SamplerMipmapMode.Nearest,
            MinLod = 0f,
            MaxLod = 0f
        };

        ThrowIfFailed(_vk.CreateSampler(_device, &samplerInfo, null, out _blockAtlasSampler), "create block atlas sampler");
    }

    private void CreateDescriptorPool()
    {
        var poolSizes = stackalloc DescriptorPoolSize[2];
        poolSizes[0] = new DescriptorPoolSize
        {
            Type = DescriptorType.UniformBuffer,
            DescriptorCount = 1
        };
        poolSizes[1] = new DescriptorPoolSize
        {
            Type = DescriptorType.CombinedImageSampler,
            DescriptorCount = 2
        };

        var poolInfo = new DescriptorPoolCreateInfo
        {
            SType = StructureType.DescriptorPoolCreateInfo,
            PoolSizeCount = 2,
            PPoolSizes = poolSizes,
            MaxSets = 1
        };

        ThrowIfFailed(_vk.CreateDescriptorPool(_device, &poolInfo, null, out _descriptorPool), "create descriptor pool");
    }

    private void CreateDescriptorSet()
    {
        var allocInfo = new DescriptorSetAllocateInfo
        {
            SType = StructureType.DescriptorSetAllocateInfo,
            DescriptorPool = _descriptorPool,
            DescriptorSetCount = 1
        };

        fixed (DescriptorSetLayout* setLayouts = &_descriptorSetLayout)
        fixed (DescriptorSet* descriptorSet = &_descriptorSet)
        {
            allocInfo.PSetLayouts = setLayouts;
            ThrowIfFailed(_vk.AllocateDescriptorSets(_device, &allocInfo, descriptorSet), "allocate descriptor set");
        }

        var bufferInfo = new DescriptorBufferInfo
        {
            Buffer = _cameraUniformBuffer,
            Offset = 0,
            Range = (ulong)Marshal.SizeOf<CameraUniform>()
        };

        var descriptorWrite = new WriteDescriptorSet
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = _descriptorSet,
            DstBinding = 0,
            DstArrayElement = 0,
            DescriptorType = DescriptorType.UniformBuffer,
            DescriptorCount = 1,
            PBufferInfo = &bufferInfo
        };
        var imageInfo = new DescriptorImageInfo
        {
            Sampler = _blockAtlasSampler,
            ImageView = _blockAtlasImageView,
            ImageLayout = ImageLayout.ShaderReadOnlyOptimal
        };

        var environmentImageInfo = new DescriptorImageInfo
        {
            Sampler = _environmentAtlasSampler,
            ImageView = _environmentAtlasImageView,
            ImageLayout = ImageLayout.ShaderReadOnlyOptimal
        };

        var descriptorWrites = stackalloc WriteDescriptorSet[3];
        descriptorWrites[0] = descriptorWrite;
        descriptorWrites[1] = new WriteDescriptorSet
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = _descriptorSet,
            DstBinding = 1,
            DstArrayElement = 0,
            DescriptorType = DescriptorType.CombinedImageSampler,
            DescriptorCount = 1,
            PImageInfo = &imageInfo
        };
        descriptorWrites[2] = new WriteDescriptorSet
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = _descriptorSet,
            DstBinding = 2,
            DstArrayElement = 0,
            DescriptorType = DescriptorType.CombinedImageSampler,
            DescriptorCount = 1,
            PImageInfo = &environmentImageInfo
        };

        _vk.UpdateDescriptorSets(_device, 3, descriptorWrites, 0, null);
    }

    private void CreateEnvironmentAtlasResources()
    {
        var atlasBuilder = new EnvironmentTextureAtlasBuilder(Path.Combine(AppContext.BaseDirectory, "Assets", "textures"));
        var atlas = atlasBuilder.Build();
        var imageSize = checked((ulong)atlas.Pixels.Length);

        CreateBuffer(
            imageSize,
            BufferUsageFlags.TransferSrcBit,
            MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit,
            out var stagingBuffer,
            out var stagingMemory);

        UploadBufferData(stagingMemory, atlas.Pixels);

        CreateImage(
            (uint)atlas.Width,
            (uint)atlas.Height,
            Format.R8G8B8A8Srgb,
            ImageTiling.Optimal,
            ImageUsageFlags.TransferDstBit | ImageUsageFlags.SampledBit,
            MemoryPropertyFlags.DeviceLocalBit,
            out _environmentAtlasImage,
            out _environmentAtlasMemory);

        TransitionImageLayout(_environmentAtlasImage, Format.R8G8B8A8Srgb, ImageLayout.Undefined, ImageLayout.TransferDstOptimal);
        CopyBufferToImage(stagingBuffer, _environmentAtlasImage, (uint)atlas.Width, (uint)atlas.Height);
        TransitionImageLayout(_environmentAtlasImage, Format.R8G8B8A8Srgb, ImageLayout.TransferDstOptimal, ImageLayout.ShaderReadOnlyOptimal);

        _vk.DestroyBuffer(_device, stagingBuffer, null);
        _vk.FreeMemory(_device, stagingMemory, null);

        _environmentAtlasImageView = CreateImageView(_environmentAtlasImage, Format.R8G8B8A8Srgb, ImageAspectFlags.ColorBit);

        var samplerInfo = new SamplerCreateInfo
        {
            SType = StructureType.SamplerCreateInfo,
            MagFilter = Filter.Nearest,
            MinFilter = Filter.Nearest,
            AddressModeU = SamplerAddressMode.ClampToEdge,
            AddressModeV = SamplerAddressMode.ClampToEdge,
            AddressModeW = SamplerAddressMode.ClampToEdge,
            AnisotropyEnable = false,
            MaxAnisotropy = 1f,
            BorderColor = BorderColor.IntOpaqueBlack,
            UnnormalizedCoordinates = false,
            CompareEnable = false,
            CompareOp = CompareOp.Always,
            MipmapMode = SamplerMipmapMode.Nearest,
            MinLod = 0f,
            MaxLod = 0f
        };

        ThrowIfFailed(_vk.CreateSampler(_device, &samplerInfo, null, out _environmentAtlasSampler), "create environment atlas sampler");
    }

    private void CreateCommandBuffers()
    {
        _commandBuffers = new CommandBuffer[_framebuffers.Length];

        var allocInfo = new CommandBufferAllocateInfo
        {
            SType = StructureType.CommandBufferAllocateInfo,
            CommandPool = _commandPool,
            Level = CommandBufferLevel.Primary,
            CommandBufferCount = (uint)_commandBuffers.Length
        };

        fixed (CommandBuffer* commandBuffers = _commandBuffers)
        {
            ThrowIfFailed(_vk.AllocateCommandBuffers(_device, &allocInfo, commandBuffers), "allocate command buffers");
        }
    }

    private void CreateSyncObjects()
    {
        var semaphoreInfo = new SemaphoreCreateInfo
        {
            SType = StructureType.SemaphoreCreateInfo
        };

        var fenceInfo = new FenceCreateInfo
        {
            SType = StructureType.FenceCreateInfo,
            Flags = FenceCreateFlags.SignaledBit
        };

        for (var i = 0; i < MaxFramesInFlight; i++)
        {
            ThrowIfFailed(_vk.CreateSemaphore(_device, &semaphoreInfo, null, out _imageAvailable[i]), "create image-available semaphore");
            ThrowIfFailed(_vk.CreateSemaphore(_device, &semaphoreInfo, null, out _renderFinished[i]), "create render-finished semaphore");
            ThrowIfFailed(_vk.CreateFence(_device, &fenceInfo, null, out _inFlight[i]), "create in-flight fence");
        }
    }

    private void SyncChunkMeshes(IReadOnlyList<ChunkRenderMesh> sceneMeshes)
    {
        var activeKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var mesh in sceneMeshes)
        {
            if (mesh.IsEmpty)
            {
                continue;
            }

            activeKeys.Add(mesh.Key);

            if (_gpuMeshes.TryGetValue(mesh.Key, out var existing) && ReferenceEquals(existing.SourceMesh, mesh))
            {
                continue;
            }

            if (existing is not null)
            {
                DestroyGpuMesh(existing);
            }

            _gpuMeshes[mesh.Key] = CreateGpuMesh(mesh);
        }

        foreach (var key in _gpuMeshes.Keys.ToArray())
        {
            if (activeKeys.Contains(key))
            {
                continue;
            }

            DestroyGpuMesh(_gpuMeshes[key]);
            _gpuMeshes.Remove(key);
        }
    }

    private GpuChunkMesh CreateGpuMesh(ChunkRenderMesh mesh)
    {
        var vertexBufferSize = checked((ulong)(mesh.Vertices.Length * Marshal.SizeOf<VoxelVertex>()));
        var indexBufferSize = checked((ulong)(mesh.Indices.Length * sizeof(uint)));

        CreateBuffer(
            vertexBufferSize,
            BufferUsageFlags.VertexBufferBit,
            MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit,
            out var vertexBuffer,
            out var vertexMemory);

        CreateBuffer(
            indexBufferSize,
            BufferUsageFlags.IndexBufferBit,
            MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit,
            out var indexBuffer,
            out var indexMemory);

        UploadBufferData(vertexMemory, mesh.Vertices);
        UploadBufferData(indexMemory, mesh.Indices);

        return new GpuChunkMesh(
            mesh.Coord,
            mesh,
            vertexBuffer,
            vertexMemory,
            indexBuffer,
            indexMemory,
            (uint)mesh.Indices.Length);
    }

    private void DestroyGpuMesh(GpuChunkMesh mesh)
    {
        if (mesh.IndexBuffer.Handle != 0)
        {
            _vk.DestroyBuffer(_device, mesh.IndexBuffer, null);
        }

        if (mesh.IndexMemory.Handle != 0)
        {
            _vk.FreeMemory(_device, mesh.IndexMemory, null);
        }

        if (mesh.VertexBuffer.Handle != 0)
        {
            _vk.DestroyBuffer(_device, mesh.VertexBuffer, null);
        }

        if (mesh.VertexMemory.Handle != 0)
        {
            _vk.FreeMemory(_device, mesh.VertexMemory, null);
        }
    }

    private void UpdateCameraUniform(Rendering.RenderScene scene)
    {
        var camera = scene.Camera;
        var aspect = Math.Max(1f, _swapchainExtent.Width / (float)Math.Max(1u, _swapchainExtent.Height));
        var view = camera.ViewMatrix;
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(
            MathF.PI / 180f * _settings.CameraFieldOfViewDegrees,
            aspect,
            _settings.CameraNearPlane,
            _settings.CameraFarPlane);
        projection.M22 *= -1f;
        var chunkRadius = (scene.RenderDistanceChunks + 0.5f) * Chunk.SizeX;
        var visibleWorldRadius = chunkRadius * 1.4142135f;
        var fullFogDistance = MathF.Max(
            Chunk.SizeX * 2f,
            visibleWorldRadius * _settings.FogFullDistanceRatio);
        var fogStartDistance = MathF.Max(
            Chunk.SizeX * 0.75f,
            fullFogDistance * _settings.FogStartDistanceRatio);
 
        var cameraUniform = new CameraUniform(
            view,
            projection,
            new Vector4(camera.Position, 1f),
            new Vector4(_settings.FogColor, 1f),
            new Vector4(_settings.SkyLightColor, _settings.SkyLightStrength),
            new Vector4(fogStartDistance, fullFogDistance, _settings.FogHeightFalloff, _settings.AmbientLightStrength),
            new Vector4(_settings.SunLightDirection, _settings.DiffuseLightStrength));

        void* data = null;
        ThrowIfFailed(_vk.MapMemory(_device, _cameraUniformMemory, 0, (ulong)Marshal.SizeOf<CameraUniform>(), 0, &data), "map camera uniform memory");
        try
        {
            *(CameraUniform*)data = cameraUniform;
        }
        finally
        {
            _vk.UnmapMemory(_device, _cameraUniformMemory);
        }
    }

    private void RecordCommandBuffer(CommandBuffer commandBuffer, Framebuffer framebuffer, Rendering.RenderScene scene)
    {
        var beginInfo = new CommandBufferBeginInfo
        {
            SType = StructureType.CommandBufferBeginInfo
        };

        ThrowIfFailed(_vk.BeginCommandBuffer(commandBuffer, &beginInfo), "begin command buffer");

        var clearValues = stackalloc ClearValue[2];
        clearValues[0] = new ClearValue
        {
            Color = new ClearColorValue(_settings.SkyClearColor.X, _settings.SkyClearColor.Y, _settings.SkyClearColor.Z, 1.0f)
        };
        clearValues[1] = new ClearValue
        {
            DepthStencil = new ClearDepthStencilValue(1f, 0)
        };

        var renderPassInfo = new RenderPassBeginInfo
        {
            SType = StructureType.RenderPassBeginInfo,
            RenderPass = _renderPass,
            Framebuffer = framebuffer,
            RenderArea = new Rect2D(new Offset2D(0, 0), _swapchainExtent),
            ClearValueCount = 2,
            PClearValues = clearValues
        };

        _vk.CmdBeginRenderPass(commandBuffer, &renderPassInfo, SubpassContents.Inline);
        _vk.CmdBindPipeline(commandBuffer, PipelineBindPoint.Graphics, _graphicsPipeline);

        var viewport = new Viewport(0f, 0f, _swapchainExtent.Width, _swapchainExtent.Height, 0f, 1f);
        var scissor = new Rect2D(new Offset2D(0, 0), _swapchainExtent);
        _vk.CmdSetViewport(commandBuffer, 0, 1, &viewport);
        _vk.CmdSetScissor(commandBuffer, 0, 1, &scissor);

        fixed (DescriptorSet* descriptorSet = &_descriptorSet)
        {
            _vk.CmdBindDescriptorSets(commandBuffer, PipelineBindPoint.Graphics, _pipelineLayout, 0, 1, descriptorSet, 0, null);
        }

        foreach (var mesh in _gpuMeshes.Values)
        {
            var vertexBuffer = mesh.VertexBuffer;
            var vertexOffset = 0UL;
            _vk.CmdBindVertexBuffers(commandBuffer, 0, 1, &vertexBuffer, &vertexOffset);
            _vk.CmdBindIndexBuffer(commandBuffer, mesh.IndexBuffer, 0, IndexType.Uint32);
            _vk.CmdDrawIndexed(commandBuffer, mesh.IndexCount, 1, 0, 0, 0);
        }

        _hudPreview.DrawHud(commandBuffer, _swapchainExtent, scene);
        _vk.CmdEndRenderPass(commandBuffer);
        ThrowIfFailed(_vk.EndCommandBuffer(commandBuffer), "end command buffer");
    }

    private bool TryFindQueueFamilies(PhysicalDevice device, out uint graphicsFamily, out uint presentFamily)
    {
        graphicsFamily = uint.MaxValue;
        presentFamily = uint.MaxValue;

        uint queueFamilyCount = 0;
        _vk.GetPhysicalDeviceQueueFamilyProperties(device, &queueFamilyCount, null);
        var queueFamilies = stackalloc QueueFamilyProperties[(int)queueFamilyCount];
        _vk.GetPhysicalDeviceQueueFamilyProperties(device, &queueFamilyCount, queueFamilies);

        for (uint i = 0; i < queueFamilyCount; i++)
        {
            if ((queueFamilies[i].QueueFlags & QueueFlags.GraphicsBit) != 0)
            {
                graphicsFamily = i;
            }

            _surfaceApi.GetPhysicalDeviceSurfaceSupport(device, i, _surface, out var presentSupport);
            if (presentSupport)
            {
                presentFamily = i;
            }

            if (graphicsFamily != uint.MaxValue && presentFamily != uint.MaxValue)
            {
                return true;
            }
        }

        return false;
    }

    private bool SupportsSwapchain(PhysicalDevice device)
    {
        if (!_vk.IsDeviceExtensionPresent(device, KhrSwapchain.ExtensionName))
        {
            return false;
        }

        var support = QuerySwapchainSupport(device);
        return support.Formats.Length > 0 && support.PresentModes.Length > 0;
    }

    private SwapchainSupportDetails QuerySwapchainSupport(PhysicalDevice device)
    {
        _surfaceApi.GetPhysicalDeviceSurfaceCapabilities(device, _surface, out var capabilities);

        uint formatCount = 0;
        _surfaceApi.GetPhysicalDeviceSurfaceFormats(device, _surface, &formatCount, null);
        var formats = new SurfaceFormatKHR[formatCount];
        if (formatCount > 0)
        {
            fixed (SurfaceFormatKHR* formatsPtr = formats)
            {
                _surfaceApi.GetPhysicalDeviceSurfaceFormats(device, _surface, &formatCount, formatsPtr);
            }
        }

        uint presentModeCount = 0;
        _surfaceApi.GetPhysicalDeviceSurfacePresentModes(device, _surface, &presentModeCount, null);
        var presentModes = new PresentModeKHR[presentModeCount];
        if (presentModeCount > 0)
        {
            fixed (PresentModeKHR* presentModesPtr = presentModes)
            {
                _surfaceApi.GetPhysicalDeviceSurfacePresentModes(device, _surface, &presentModeCount, presentModesPtr);
            }
        }

        return new SwapchainSupportDetails(capabilities, formats, presentModes);
    }

    private SurfaceFormatKHR ChooseSurfaceFormat(IReadOnlyList<SurfaceFormatKHR> formats)
    {
        foreach (var format in formats)
        {
            if (format.Format == Format.B8G8R8A8Srgb && format.ColorSpace == ColorSpaceKHR.SpaceSrgbNonlinearKhr)
            {
                return format;
            }
        }

        return formats[0];
    }

    private static PresentModeKHR ChoosePresentMode(IReadOnlyList<PresentModeKHR> presentModes)
    {
        return presentModes.Contains(PresentModeKHR.MailboxKhr)
            ? PresentModeKHR.MailboxKhr
            : PresentModeKHR.FifoKhr;
    }

    private Extent2D ChooseExtent(SurfaceCapabilitiesKHR capabilities)
    {
        if (capabilities.CurrentExtent.Width != uint.MaxValue)
        {
            return capabilities.CurrentExtent;
        }

        var width = (uint)Math.Clamp(_window!.FramebufferSize.X, (int)capabilities.MinImageExtent.Width, (int)capabilities.MaxImageExtent.Width);
        var height = (uint)Math.Clamp(_window.FramebufferSize.Y, (int)capabilities.MinImageExtent.Height, (int)capabilities.MaxImageExtent.Height);
        return new Extent2D(width, height);
    }

    private void CreateBuffer(ulong size, BufferUsageFlags usage, MemoryPropertyFlags properties, out Buffer buffer, out DeviceMemory bufferMemory)
    {
        var bufferInfo = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = size,
            Usage = usage,
            SharingMode = SharingMode.Exclusive
        };

        ThrowIfFailed(_vk.CreateBuffer(_device, &bufferInfo, null, out buffer), "create buffer");

        _vk.GetBufferMemoryRequirements(_device, buffer, out var memoryRequirements);

        var allocInfo = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = memoryRequirements.Size,
            MemoryTypeIndex = FindMemoryType(memoryRequirements.MemoryTypeBits, properties)
        };

        ThrowIfFailed(_vk.AllocateMemory(_device, &allocInfo, null, out bufferMemory), "allocate buffer memory");
        ThrowIfFailed(_vk.BindBufferMemory(_device, buffer, bufferMemory, 0), "bind buffer memory");
    }

    private void CreateImage(
        uint width,
        uint height,
        Format format,
        ImageTiling tiling,
        ImageUsageFlags usage,
        MemoryPropertyFlags properties,
        out Image image,
        out DeviceMemory imageMemory)
    {
        var imageInfo = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Extent = new Extent3D(width, height, 1),
            MipLevels = 1,
            ArrayLayers = 1,
            Format = format,
            Tiling = tiling,
            InitialLayout = ImageLayout.Undefined,
            Usage = usage,
            SharingMode = SharingMode.Exclusive,
            Samples = SampleCountFlags.Count1Bit
        };

        ThrowIfFailed(_vk.CreateImage(_device, &imageInfo, null, out image), "create image");

        _vk.GetImageMemoryRequirements(_device, image, out var memoryRequirements);

        var allocInfo = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = memoryRequirements.Size,
            MemoryTypeIndex = FindMemoryType(memoryRequirements.MemoryTypeBits, properties)
        };

        ThrowIfFailed(_vk.AllocateMemory(_device, &allocInfo, null, out imageMemory), "allocate image memory");
        ThrowIfFailed(_vk.BindImageMemory(_device, image, imageMemory, 0), "bind image memory");
    }

    private ImageView CreateImageView(Image image, Format format, ImageAspectFlags aspectFlags)
    {
        var createInfo = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = image,
            ViewType = ImageViewType.Type2D,
            Format = format,
            Components = new ComponentMapping(ComponentSwizzle.Identity, ComponentSwizzle.Identity, ComponentSwizzle.Identity, ComponentSwizzle.Identity),
            SubresourceRange = new ImageSubresourceRange(aspectFlags, 0, 1, 0, 1)
        };

        ThrowIfFailed(_vk.CreateImageView(_device, &createInfo, null, out var imageView), "create image view");
        return imageView;
    }

    private void TransitionImageLayout(Image image, Format format, ImageLayout oldLayout, ImageLayout newLayout)
    {
        var commandBuffer = BeginSingleTimeCommands();

        var barrier = new ImageMemoryBarrier
        {
            SType = StructureType.ImageMemoryBarrier,
            OldLayout = oldLayout,
            NewLayout = newLayout,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            Image = image,
            SubresourceRange = new ImageSubresourceRange(
                format == FindDepthFormat() ? ImageAspectFlags.DepthBit : ImageAspectFlags.ColorBit,
                0,
                1,
                0,
                1)
        };

        PipelineStageFlags sourceStage;
        PipelineStageFlags destinationStage;

        if (oldLayout == ImageLayout.Undefined && newLayout == ImageLayout.TransferDstOptimal)
        {
            barrier.SrcAccessMask = 0;
            barrier.DstAccessMask = AccessFlags.TransferWriteBit;
            sourceStage = PipelineStageFlags.TopOfPipeBit;
            destinationStage = PipelineStageFlags.TransferBit;
        }
        else if (oldLayout == ImageLayout.TransferDstOptimal && newLayout == ImageLayout.ShaderReadOnlyOptimal)
        {
            barrier.SrcAccessMask = AccessFlags.TransferWriteBit;
            barrier.DstAccessMask = AccessFlags.ShaderReadBit;
            sourceStage = PipelineStageFlags.TransferBit;
            destinationStage = PipelineStageFlags.FragmentShaderBit;
        }
        else
        {
            throw new InvalidOperationException($"Unsupported image layout transition {oldLayout} -> {newLayout}.");
        }

        _vk.CmdPipelineBarrier(
            commandBuffer,
            sourceStage,
            destinationStage,
            0,
            0,
            null,
            0,
            null,
            1,
            &barrier);

        EndSingleTimeCommands(commandBuffer);
    }

    private void CopyBufferToImage(Buffer buffer, Image image, uint width, uint height)
    {
        var commandBuffer = BeginSingleTimeCommands();

        var region = new BufferImageCopy
        {
            BufferOffset = 0,
            BufferRowLength = 0,
            BufferImageHeight = 0,
            ImageSubresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, 0, 0, 1),
            ImageOffset = new Offset3D(0, 0, 0),
            ImageExtent = new Extent3D(width, height, 1)
        };

        _vk.CmdCopyBufferToImage(commandBuffer, buffer, image, ImageLayout.TransferDstOptimal, 1, &region);
        EndSingleTimeCommands(commandBuffer);
    }

    private CommandBuffer BeginSingleTimeCommands()
    {
        var allocInfo = new CommandBufferAllocateInfo
        {
            SType = StructureType.CommandBufferAllocateInfo,
            Level = CommandBufferLevel.Primary,
            CommandPool = _commandPool,
            CommandBufferCount = 1
        };

        CommandBuffer commandBuffer;
        ThrowIfFailed(_vk.AllocateCommandBuffers(_device, &allocInfo, &commandBuffer), "allocate single-time command buffer");

        var beginInfo = new CommandBufferBeginInfo
        {
            SType = StructureType.CommandBufferBeginInfo,
            Flags = CommandBufferUsageFlags.OneTimeSubmitBit
        };

        ThrowIfFailed(_vk.BeginCommandBuffer(commandBuffer, &beginInfo), "begin single-time command buffer");
        return commandBuffer;
    }

    private void EndSingleTimeCommands(CommandBuffer commandBuffer)
    {
        ThrowIfFailed(_vk.EndCommandBuffer(commandBuffer), "end single-time command buffer");

        var submitInfo = new SubmitInfo
        {
            SType = StructureType.SubmitInfo,
            CommandBufferCount = 1,
            PCommandBuffers = &commandBuffer
        };

        ThrowIfFailed(_vk.QueueSubmit(_graphicsQueue, 1, &submitInfo, default), "submit single-time command buffer");
        ThrowIfFailed(_vk.QueueWaitIdle(_graphicsQueue), "wait for single-time command buffer");
        _vk.FreeCommandBuffers(_device, _commandPool, 1, &commandBuffer);
    }

    private ShaderModule CreateShaderModule(byte[] code)
    {
        fixed (byte* codePtr = code)
        {
            var createInfo = new ShaderModuleCreateInfo
            {
                SType = StructureType.ShaderModuleCreateInfo,
                CodeSize = (nuint)code.Length,
                PCode = (uint*)codePtr
            };

            ThrowIfFailed(_vk.CreateShaderModule(_device, &createInfo, null, out var shaderModule), "create shader module");
            return shaderModule;
        }
    }

    private uint FindMemoryType(uint typeFilter, MemoryPropertyFlags properties)
    {
        _vk.GetPhysicalDeviceMemoryProperties(_physicalDevice, out var memoryProperties);

        for (uint i = 0; i < memoryProperties.MemoryTypeCount; i++)
        {
            var typeMatches = (typeFilter & (1u << (int)i)) != 0;
            var propertyMatches = (memoryProperties.MemoryTypes[(int)i].PropertyFlags & properties) == properties;
            if (typeMatches && propertyMatches)
            {
                return i;
            }
        }

        throw new InvalidOperationException($"No Vulkan memory type matched {properties}.");
    }

    private Format FindDepthFormat()
    {
        return FindSupportedFormat(
            new[] { Format.D32Sfloat, Format.D32SfloatS8Uint, Format.D24UnormS8Uint },
            ImageTiling.Optimal,
            FormatFeatureFlags.DepthStencilAttachmentBit);
    }

    private Format FindSupportedFormat(IReadOnlyList<Format> candidates, ImageTiling tiling, FormatFeatureFlags features)
    {
        foreach (var format in candidates)
        {
            _vk.GetPhysicalDeviceFormatProperties(_physicalDevice, format, out var properties);
            var supportedFeatures = tiling == ImageTiling.Linear ? properties.LinearTilingFeatures : properties.OptimalTilingFeatures;
            if ((supportedFeatures & features) == features)
            {
                return format;
            }
        }

        throw new InvalidOperationException("Failed to find a supported image format.");
    }

    private void UploadBufferData<T>(DeviceMemory memory, ReadOnlySpan<T> data)
        where T : unmanaged
    {
        var size = checked((nuint)(data.Length * sizeof(T)));
        if (size == 0)
        {
            return;
        }

        void* mappedData = null;
        ThrowIfFailed(_vk.MapMemory(_device, memory, 0, (ulong)size, 0, &mappedData), "map buffer memory");
        try
        {
            fixed (T* source = data)
            {
                global::System.Buffer.MemoryCopy(source, mappedData, size, size);
            }
        }
        finally
        {
            _vk.UnmapMemory(_device, memory);
        }
    }

    private void RecreateSwapchain()
    {
        if (_window is null || _window.Size.X <= 0 || _window.Size.Y <= 0)
        {
            return;
        }

        _vk.DeviceWaitIdle(_device);
        CleanupSwapchain();
        CreateSwapchainResources();
        CreateCommandBuffers();
    }

    private void CleanupSwapchain()
    {
        if (_commandBuffers.Length > 0)
        {
            fixed (CommandBuffer* commandBuffers = _commandBuffers)
            {
                _vk.FreeCommandBuffers(_device, _commandPool, (uint)_commandBuffers.Length, commandBuffers);
            }

            _commandBuffers = [];
        }

        foreach (var framebuffer in _framebuffers)
        {
            _vk.DestroyFramebuffer(_device, framebuffer, null);
        }

        _framebuffers = [];

        if (_graphicsPipeline.Handle != 0)
        {
            _vk.DestroyPipeline(_device, _graphicsPipeline, null);
            _graphicsPipeline = default;
        }

        if (_pipelineLayout.Handle != 0)
        {
            _vk.DestroyPipelineLayout(_device, _pipelineLayout, null);
            _pipelineLayout = default;
        }

        if (_depthImageView.Handle != 0)
        {
            _vk.DestroyImageView(_device, _depthImageView, null);
            _depthImageView = default;
        }

        if (_depthImage.Handle != 0)
        {
            _vk.DestroyImage(_device, _depthImage, null);
            _depthImage = default;
        }

        if (_depthImageMemory.Handle != 0)
        {
            _vk.FreeMemory(_device, _depthImageMemory, null);
            _depthImageMemory = default;
        }

        if (_renderPass.Handle != 0)
        {
            _vk.DestroyRenderPass(_device, _renderPass, null);
            _renderPass = default;
        }

        foreach (var imageView in _swapchainImageViews)
        {
            _vk.DestroyImageView(_device, imageView, null);
        }

        _swapchainImageViews = [];

        if (_swapchain.Handle != 0)
        {
            _swapchainApi.DestroySwapchain(_device, _swapchain, null);
            _swapchain = default;
        }
    }

    private void UpdateWindowTitle(Rendering.RenderScene scene)
    {
        if (_window is null)
        {
            return;
        }

        _titleRefresh += 1.0 / Math.Max(1, scene.Hud.Fps);
        if (_titleRefresh < 0.25)
        {
            return;
        }

        _titleRefresh = 0;
        _window.Title = $"{_settings.WindowTitle} | Vulkan chunks | FPS {scene.Hud.Fps} | Chunks {scene.Hud.LoadedChunks} | Slot {scene.Hud.SelectedSlot + 1}: {scene.Hud.SelectedBlock}";
    }

    public void Dispose()
    {
        if (!_initialized)
        {
            _shaderCompiler?.Dispose();
            _vk?.Dispose();
            return;
        }

        _vk.DeviceWaitIdle(_device);

        foreach (var mesh in _gpuMeshes.Values)
        {
            DestroyGpuMesh(mesh);
        }

        _gpuMeshes.Clear();

        for (var i = 0; i < MaxFramesInFlight; i++)
        {
            _vk.DestroyFence(_device, _inFlight[i], null);
            _vk.DestroySemaphore(_device, _renderFinished[i], null);
            _vk.DestroySemaphore(_device, _imageAvailable[i], null);
        }

        CleanupSwapchain();

        if (_descriptorPool.Handle != 0)
        {
            _vk.DestroyDescriptorPool(_device, _descriptorPool, null);
        }

        if (_cameraUniformBuffer.Handle != 0)
        {
            _vk.DestroyBuffer(_device, _cameraUniformBuffer, null);
        }

        if (_cameraUniformMemory.Handle != 0)
        {
            _vk.FreeMemory(_device, _cameraUniformMemory, null);
        }

        if (_blockAtlasSampler.Handle != 0)
        {
            _vk.DestroySampler(_device, _blockAtlasSampler, null);
        }

        if (_environmentAtlasSampler.Handle != 0)
        {
            _vk.DestroySampler(_device, _environmentAtlasSampler, null);
        }

        if (_environmentAtlasImageView.Handle != 0)
        {
            _vk.DestroyImageView(_device, _environmentAtlasImageView, null);
        }

        if (_environmentAtlasImage.Handle != 0)
        {
            _vk.DestroyImage(_device, _environmentAtlasImage, null);
        }

        if (_environmentAtlasMemory.Handle != 0)
        {
            _vk.FreeMemory(_device, _environmentAtlasMemory, null);
        }

        if (_blockAtlasImageView.Handle != 0)
        {
            _vk.DestroyImageView(_device, _blockAtlasImageView, null);
        }

        if (_blockAtlasImage.Handle != 0)
        {
            _vk.DestroyImage(_device, _blockAtlasImage, null);
        }

        if (_blockAtlasMemory.Handle != 0)
        {
            _vk.FreeMemory(_device, _blockAtlasMemory, null);
        }

        if (_descriptorSetLayout.Handle != 0)
        {
            _vk.DestroyDescriptorSetLayout(_device, _descriptorSetLayout, null);
        }

        if (_commandPool.Handle != 0)
        {
            _vk.DestroyCommandPool(_device, _commandPool, null);
        }

        _swapchainApi.Dispose();
        _vk.DestroyDevice(_device, null);
        _surfaceApi.DestroySurface(_instance, _surface, null);
        _surfaceApi.Dispose();
        _vk.DestroyInstance(_instance, null);
        _shaderCompiler.Dispose();
        _vk.Dispose();
        _initialized = false;
    }

    private static void ThrowIfFailed(Result result, string operation)
    {
        if (result != Result.Success)
        {
            throw new InvalidOperationException($"Vulkan failed to {operation}: {result}");
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct CameraUniform(
        Matrix4x4 View,
        Matrix4x4 Projection,
        Vector4 CameraPosition,
        Vector4 FogColor,
        Vector4 SkyLightColor,
        Vector4 FogSettings,
        Vector4 LightDirection);

    private readonly record struct SwapchainSupportDetails(
        SurfaceCapabilitiesKHR Capabilities,
        SurfaceFormatKHR[] Formats,
        PresentModeKHR[] PresentModes);
}
