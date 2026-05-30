using System.Numerics;
using System.Text.Json;
using VoxelGame.World.Blocks;
using VoxelGame.World.Chunks;

namespace VoxelGame.Player;

public sealed class PlayerModel
{
    private const float TurnLag = 9.5f;
    private const float WalkFrequency = 6.4f;
    private const float ArmSwingRadians = 0.72f;
    private const float LegSwingRadians = 0.88f;
    private const float BodyWalkYawRadians = 0.07f;
    private const float BodyWalkRollRadians = 0.05f;
    private const float BodyLeanRadians = 0.1f;
    private const float HeadCounterTiltRadians = 0.03f;
    private const float BobHeight = 0.034f;
    private const float MaxHeadYawDegrees = 72f;
    private const float ModelFacingOffsetDegrees = 180f;
    private const float ModelScale = 0.95f;
    private const float SpeedSmoothing = 10f;
    private const float BlendRiseSmoothing = 8.5f;
    private const float BlendFallSmoothing = 5.5f;
    private const float CadenceSmoothing = 6.5f;
    private const float AirborneSmoothing = 9f;
    private const float BreakBlendSmoothing = 11f;
    private const float BreakFrequency = 21.75f;

    private static readonly Lazy<ModelAsset> NormalAsset = new(() => LoadAsset(PlayerBodyType.Normal));
    private static readonly Lazy<ModelAsset> SlimAsset = new(() => LoadAsset(PlayerBodyType.Slim));
    private float _bodyYawDegrees = 180f;
    private float _walkPhase;
    private float _walkBlend;
    private float _smoothedSpeed;
    private float _smoothedCadence = 0.82f;
    private float _normalizedSpeed;
    private float _airborneBlend;
    private float _smoothedVerticalSpeed;
    private float _breakBlend;
    private float _breakPhase;

    public PlayerBodyType BodyType { get; set; } = PlayerBodyType.Normal;

    public PlayerModel()
    {
    }

    public PlayerModel(PlayerBodyType bodyType)
    {
        BodyType = bodyType;
    }

    public void Reset(float yawDegrees)
    {
        _bodyYawDegrees = yawDegrees;
        _walkPhase = 0f;
        _walkBlend = 0f;
        _smoothedSpeed = 0f;
        _smoothedCadence = 0.82f;
        _normalizedSpeed = 0f;
        _airborneBlend = 0f;
        _smoothedVerticalSpeed = 0f;
        _breakBlend = 0f;
        _breakPhase = 0f;
    }

    public void Update(float dt, Vector3 horizontalVelocity, float verticalVelocity, bool grounded, float cameraYawDegrees, bool breakingBlock)
    {
        var horizontalSpeed = new Vector2(horizontalVelocity.X, horizontalVelocity.Z).Length();
        _smoothedSpeed += (horizontalSpeed - _smoothedSpeed) * Math.Clamp(dt * SpeedSmoothing, 0f, 1f);
        _smoothedVerticalSpeed += (verticalVelocity - _smoothedVerticalSpeed) * Math.Clamp(dt * 8f, 0f, 1f);
        var moving = grounded && _smoothedSpeed > 0.08f;
        var normalizedSpeed = Math.Clamp(_smoothedSpeed / 5.2f, 0f, 1.3f);
        _normalizedSpeed += (normalizedSpeed - _normalizedSpeed) * Math.Clamp(dt * 7.5f, 0f, 1f);
        var targetBlend = moving ? MathF.Pow(_normalizedSpeed, 0.78f) : 0f;
        var targetCadence = moving ? 0.78f + MathF.Min(_normalizedSpeed, 1f) * 0.36f : 0.78f;
        _smoothedCadence += (targetCadence - _smoothedCadence) * Math.Clamp(dt * CadenceSmoothing, 0f, 1f);

        if (moving)
        {
            _walkPhase += dt * WalkFrequency * _smoothedCadence;
        }

        var blendSmoothing = targetBlend > _walkBlend ? BlendRiseSmoothing : BlendFallSmoothing;
        _walkBlend += (targetBlend - _walkBlend) * Math.Clamp(dt * blendSmoothing, 0f, 1f);
        _airborneBlend += ((grounded ? 0f : 1f) - _airborneBlend) * Math.Clamp(dt * AirborneSmoothing, 0f, 1f);
        _breakBlend += ((breakingBlock ? 1f : 0f) - _breakBlend) * Math.Clamp(dt * BreakBlendSmoothing, 0f, 1f);
        if (_breakBlend > 0.01f)
        {
            _breakPhase += dt * BreakFrequency * (0.8f + MathF.Min(_normalizedSpeed, 1f) * 0.25f);
        }

        _bodyYawDegrees = ApproachAngle(_bodyYawDegrees, cameraYawDegrees, TurnLag * dt);
    }

    public IEnumerable<ChunkRenderMesh> BuildRenderMeshes(Vector3 position, float cameraYawDegrees, float cameraPitchDegrees, bool hideHead)
    {
        var asset = GetAsset(BodyType);
        var vertices = new List<VoxelVertex>(asset.VertexCount);
        var indices = new List<uint>(asset.IndexCount);
        var pose = CreatePose(cameraYawDegrees, cameraPitchDegrees);
        var bobStrength = _walkBlend * (0.55f + MathF.Min(_normalizedSpeed, 1.15f) * 0.65f);
        var modelRoot = Matrix4x4.CreateScale(ModelScale) *
            Matrix4x4.CreateRotationY(DegreesToRadians(_bodyYawDegrees + ModelFacingOffsetDegrees)) *
            Matrix4x4.CreateTranslation(position + new Vector3(0f, MathF.Abs(MathF.Sin(_walkPhase * 2f)) * BobHeight * bobStrength, 0f));

        foreach (var root in asset.RootNodes)
        {
            AppendNode(root, Matrix4x4.Identity, modelRoot, pose, hideHead, vertices, indices);
        }

        if (indices.Count == 0)
        {
            return [];
        }

        return
        [
            new ChunkRenderMesh(
                new ChunkCoord(int.MinValue + 2, int.MinValue + 2),
                vertices,
                indices,
                $"player:model:{BodyType}",
                isTransparent: false,
                alwaysVisible: true)
        ];
    }

    private static void AppendNode(
        ModelNode node,
        Matrix4x4 parent,
        Matrix4x4 modelRoot,
        PlayerPose pose,
        bool hideHead,
        List<VoxelVertex> vertices,
        List<uint> indices)
    {
        var local = ResolveAnimation(node.AnimationPart, pose) * node.LocalTransform;
        var world = local * parent;
        var skipNodeMesh = hideHead && node.RenderPart is PlayerModelPart.Head;

        if (!skipNodeMesh && node.Mesh is { } mesh)
        {
            AppendMesh(mesh, world * modelRoot, vertices, indices);
        }

        foreach (var child in node.Children)
        {
            AppendNode(child, world, modelRoot, pose, hideHead, vertices, indices);
        }
    }

    private static void AppendMesh(ModelMesh mesh, Matrix4x4 transform, List<VoxelVertex> vertices, List<uint> indices)
    {
        var normalTransform = transform;
        normalTransform.Translation = Vector3.Zero;
        var baseIndex = (uint)vertices.Count;
        var remap = new uint[mesh.Vertices.Length];

        for (var i = 0; i < mesh.Vertices.Length; i++)
        {
            var vertex = mesh.Vertices[i];
            remap[i] = (uint)vertices.Count;
            vertices.Add(new VoxelVertex(
                Vector3.Transform(vertex.Position, transform),
                Vector3.Normalize(Vector3.TransformNormal(vertex.Normal, normalTransform)),
                RemapPlayerSkinUv(vertex.Uv),
                BlockTextureAtlas.PlayerWhite,
                Vector3.One));
        }

        for (var i = 0; i + 2 < mesh.Indices.Length; i += 3)
        {
            var a = remap[mesh.Indices[i]];
            var b = remap[mesh.Indices[i + 1]];
            var c = remap[mesh.Indices[i + 2]];
            if (a == uint.MaxValue || b == uint.MaxValue || c == uint.MaxValue)
            {
                continue;
            }

            indices.Add(a);
            indices.Add(b);
            indices.Add(c);
        }
    }

    private PlayerPose CreatePose(float cameraYawDegrees, float cameraPitchDegrees)
    {
        var primarySwing = MathF.Sin(_walkPhase);
        var secondarySwing = MathF.Sin(_walkPhase * 2f);
        var speedFactor = 0.6f + MathF.Min(_normalizedSpeed, 1.15f) * 0.75f;
        var walk = (primarySwing * 0.82f + secondarySwing * 0.18f) * _walkBlend * speedFactor;
        var counterWalk = (-primarySwing * 0.82f + secondarySwing * 0.12f) * _walkBlend * speedFactor;
        var headYaw = ClampAngle(cameraYawDegrees - _bodyYawDegrees, -MaxHeadYawDegrees, MaxHeadYawDegrees);
        var bodyLean = primarySwing * BodyLeanRadians * _walkBlend * MathF.Min(_normalizedSpeed, 1f);
        var headCounterTilt = -secondarySwing * HeadCounterTiltRadians * _walkBlend * (0.4f + MathF.Min(_normalizedSpeed, 1f) * 0.6f);
        var headPitch = DegreesToRadians(cameraPitchDegrees) + bodyLean * 0.12f;
        var jumpRise = MathF.Max(_smoothedVerticalSpeed / 7.2f, 0f) * _airborneBlend;
        var jumpFall = MathF.Max(-_smoothedVerticalSpeed / 9f, 0f) * _airborneBlend;
        var jumpTuck = _airborneBlend * (0.35f + jumpRise * 0.65f);
        var breakSwing = MathF.Sin(_breakPhase) * _breakBlend;
        var breakRecover = MathF.Max(0f, MathF.Sin(_breakPhase - 0.9f)) * _breakBlend;
        var bodyAirPitch = -0.12f * jumpRise + 0.08f * jumpFall;
        var rightArmActionX = 1.05f * breakRecover - 0.35f * jumpRise + 0.28f * jumpFall;
        var rightArmActionZ = -0.22f * _breakBlend + 0.18f * breakSwing;
        var leftArmAirX = -0.22f * jumpRise + 0.14f * jumpFall;
        var leftLegAirX = -0.42f * jumpTuck + 0.12f * jumpFall;
        var rightLegAirX = -0.3f * jumpTuck + 0.18f * jumpFall;

        return new PlayerPose(
            Head: Quaternion.CreateFromYawPitchRoll(DegreesToRadians(headYaw), headPitch, headCounterTilt),
            Body: Quaternion.CreateFromYawPitchRoll(0f, 0f, secondarySwing * BodyWalkRollRadians * _walkBlend * speedFactor) *
                Quaternion.CreateFromYawPitchRoll(primarySwing * BodyWalkYawRadians * _walkBlend * speedFactor + bodyAirPitch, bodyLean, 0f),
            RightArm: Quaternion.CreateFromYawPitchRoll(0f, 0f, rightArmActionZ) *
                Quaternion.CreateFromAxisAngle(Vector3.UnitX, counterWalk * ArmSwingRadians + rightArmActionX),
            LeftArm: Quaternion.CreateFromAxisAngle(Vector3.UnitX, walk * ArmSwingRadians + leftArmAirX),
            RightLeg: Quaternion.CreateFromAxisAngle(Vector3.UnitX, walk * LegSwingRadians + rightLegAirX),
            LeftLeg: Quaternion.CreateFromAxisAngle(Vector3.UnitX, counterWalk * LegSwingRadians + leftLegAirX));
    }

    private static Matrix4x4 ResolveAnimation(PlayerModelPart part, PlayerPose pose)
    {
        var rotation = part switch
        {
            PlayerModelPart.Head => pose.Head,
            PlayerModelPart.Body => pose.Body,
            PlayerModelPart.RightArm => pose.RightArm,
            PlayerModelPart.LeftArm => pose.LeftArm,
            PlayerModelPart.RightLeg => pose.RightLeg,
            PlayerModelPart.LeftLeg => pose.LeftLeg,
            _ => Quaternion.Identity
        };

        return Matrix4x4.CreateFromQuaternion(rotation);
    }

    private static ModelAsset GetAsset(PlayerBodyType bodyType)
    {
        return bodyType == PlayerBodyType.Slim ? SlimAsset.Value : NormalAsset.Value;
    }

    private static ModelAsset LoadAsset(PlayerBodyType bodyType)
    {
        var fileName = bodyType == PlayerBodyType.Slim ? "playermodel_slim.gltf" : "playermodel.gltf";
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "models", fileName);
        if (!File.Exists(path))
        {
            path = Path.Combine(AppContext.BaseDirectory, "assets", "models", fileName);
        }

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        var buffer = LoadDataUri(root.GetProperty("buffers")[0].GetProperty("uri").GetString()!);
        var nodes = BuildNodes(root, buffer);
        var rootNodes = root.GetProperty("scenes")[root.GetProperty("scene").GetInt32()]
            .GetProperty("nodes")
            .EnumerateArray()
            .Select(nodeIndex => nodes[nodeIndex.GetInt32()])
            .ToArray();

        return new ModelAsset(rootNodes);
    }

    private static ModelNode[] BuildNodes(JsonElement root, byte[] buffer)
    {
        var nodeElements = root.GetProperty("nodes").EnumerateArray().ToArray();
        var nodes = new ModelNode[nodeElements.Length];

        for (var i = 0; i < nodeElements.Length; i++)
        {
            var node = nodeElements[i];
            nodes[i] = new ModelNode(
                node.GetProperty("name").GetString() ?? string.Empty,
                node.TryGetProperty("children", out _) ? ResolvePart(node.GetProperty("name").GetString() ?? string.Empty) : PlayerModelPart.None,
                ReadNodeTransform(node),
                node.TryGetProperty("mesh", out var meshIndex) ? ReadMesh(root, buffer, meshIndex.GetInt32()) : null);
        }

        for (var i = 0; i < nodeElements.Length; i++)
        {
            if (!nodeElements[i].TryGetProperty("children", out var children))
            {
                continue;
            }

            foreach (var child in children.EnumerateArray())
            {
                nodes[i].Children.Add(nodes[child.GetInt32()]);
            }
        }

        AssignRenderParts(nodes);
        return nodes;
    }

    private static void AssignRenderParts(ModelNode[] nodes)
    {
        foreach (var node in nodes)
        {
            AssignRenderPart(node, PlayerModelPart.None);
        }
    }

    private static void AssignRenderPart(ModelNode node, PlayerModelPart inheritedPart)
    {
        var renderPart = node.AnimationPart == PlayerModelPart.None ? inheritedPart : node.AnimationPart;
        node.RenderPart = renderPart;

        foreach (var child in node.Children)
        {
            AssignRenderPart(child, renderPart);
        }
    }

    private static Matrix4x4 ReadNodeTransform(JsonElement node)
    {
        var translation = node.TryGetProperty("translation", out var translationElement)
            ? ReadVector3(translationElement)
            : Vector3.Zero;
        var rotation = node.TryGetProperty("rotation", out var rotationElement)
            ? ReadQuaternion(rotationElement)
            : Quaternion.Identity;
        var scale = node.TryGetProperty("scale", out var scaleElement)
            ? ReadVector3(scaleElement)
            : Vector3.One;

        return Matrix4x4.CreateScale(scale) *
            Matrix4x4.CreateFromQuaternion(rotation) *
            Matrix4x4.CreateTranslation(translation);
    }

    private static ModelMesh ReadMesh(JsonElement root, byte[] buffer, int meshIndex)
    {
        var primitive = root.GetProperty("meshes")[meshIndex].GetProperty("primitives")[0];
        var attributes = primitive.GetProperty("attributes");
        var positions = ReadVector3Accessor(root, buffer, attributes.GetProperty("POSITION").GetInt32());
        var normals = ReadVector3Accessor(root, buffer, attributes.GetProperty("NORMAL").GetInt32());
        var uvs = ReadVector2Accessor(root, buffer, attributes.GetProperty("TEXCOORD_0").GetInt32());
        var indices = ReadIndexAccessor(root, buffer, primitive.GetProperty("indices").GetInt32());
        var vertices = new ModelVertex[positions.Length];

        for (var i = 0; i < vertices.Length; i++)
        {
            vertices[i] = new ModelVertex(positions[i], normals[i], uvs[i]);
        }

        return new ModelMesh(vertices, indices);
    }

    private static Vector3[] ReadVector3Accessor(JsonElement root, byte[] buffer, int accessorIndex)
    {
        var span = ReadAccessorBytes(root, buffer, accessorIndex, 12, out var count, out var stride);
        var values = new Vector3[count];
        for (var i = 0; i < count; i++)
        {
            values[i] = new Vector3(
                BitConverter.ToSingle(span.Slice(i * stride, 4)),
                BitConverter.ToSingle(span.Slice(i * stride + 4, 4)),
                BitConverter.ToSingle(span.Slice(i * stride + 8, 4)));
        }

        return values;
    }

    private static Vector2[] ReadVector2Accessor(JsonElement root, byte[] buffer, int accessorIndex)
    {
        var span = ReadAccessorBytes(root, buffer, accessorIndex, 8, out var count, out var stride);
        var values = new Vector2[count];
        for (var i = 0; i < count; i++)
        {
            values[i] = new Vector2(
                BitConverter.ToSingle(span.Slice(i * stride, 4)),
                BitConverter.ToSingle(span.Slice(i * stride + 4, 4)));
        }

        return values;
    }

    private static uint[] ReadIndexAccessor(JsonElement root, byte[] buffer, int accessorIndex)
    {
        var accessor = root.GetProperty("accessors")[accessorIndex];
        var componentType = accessor.GetProperty("componentType").GetInt32();
        var span = ReadAccessorBytes(root, buffer, accessorIndex, componentType == 5123 ? 2 : 4, out var count, out var stride);
        var indices = new uint[count];
        for (var i = 0; i < count; i++)
        {
            indices[i] = componentType == 5123
                ? BitConverter.ToUInt16(span.Slice(i * stride, 2))
                : BitConverter.ToUInt32(span.Slice(i * stride, 4));
        }

        return indices;
    }

    private static ReadOnlySpan<byte> ReadAccessorBytes(JsonElement root, byte[] buffer, int accessorIndex, int elementSize, out int count, out int stride)
    {
        var accessor = root.GetProperty("accessors")[accessorIndex];
        var bufferView = root.GetProperty("bufferViews")[accessor.GetProperty("bufferView").GetInt32()];
        count = accessor.GetProperty("count").GetInt32();
        stride = bufferView.TryGetProperty("byteStride", out var byteStride) ? byteStride.GetInt32() : elementSize;
        var byteOffset = (bufferView.TryGetProperty("byteOffset", out var viewOffset) ? viewOffset.GetInt32() : 0) +
            (accessor.TryGetProperty("byteOffset", out var accessorOffset) ? accessorOffset.GetInt32() : 0);
        return buffer.AsSpan(byteOffset, stride * count);
    }

    private static byte[] LoadDataUri(string uri)
    {
        var commaIndex = uri.IndexOf(',');
        return Convert.FromBase64String(uri[(commaIndex + 1)..]);
    }

    private static Vector3 ReadVector3(JsonElement element)
    {
        return new Vector3(
            element[0].GetSingle(),
            element[1].GetSingle(),
            element[2].GetSingle());
    }

    private static Quaternion ReadQuaternion(JsonElement element)
    {
        return new Quaternion(
            element[0].GetSingle(),
            element[1].GetSingle(),
            element[2].GetSingle(),
            element[3].GetSingle());
    }

    private static PlayerModelPart ResolvePart(string name)
    {
        return name switch
        {
            "Head" => PlayerModelPart.Head,
            "Waist" => PlayerModelPart.Body,
            "Right Arm" => PlayerModelPart.RightArm,
            "Left Arm" => PlayerModelPart.LeftArm,
            "Right Leg" => PlayerModelPart.RightLeg,
            "Left Leg" => PlayerModelPart.LeftLeg,
            _ => PlayerModelPart.None
        };
    }

    private static Vector2 RemapPlayerSkinUv(Vector2 uv)
    {
        var atlasWidth = (float)BlockTextureAtlas.GetAtlasWidth();
        var atlasHeight = (float)BlockTextureAtlas.GetAtlasHeight();
        var skinOffsetY = BlockTextureAtlas.GetBlockAtlasHeight();

        return new Vector2(
            uv.X * (BlockTextureAtlas.PlayerSkinWidth / atlasWidth),
            (skinOffsetY / atlasHeight) + uv.Y * (BlockTextureAtlas.PlayerSkinHeight / atlasHeight));
    }

    private static float ApproachAngle(float current, float target, float amount)
    {
        var delta = NormalizeAngle(target - current);
        return current + delta * Math.Clamp(amount, 0f, 1f);
    }

    private static float ClampAngle(float value, float min, float max)
    {
        return Math.Clamp(NormalizeAngle(value), min, max);
    }

    private static float NormalizeAngle(float degrees)
    {
        degrees = (degrees + 180f) % 360f;
        if (degrees < 0f)
        {
            degrees += 360f;
        }

        return degrees - 180f;
    }

    private static float DegreesToRadians(float degrees) => degrees * MathF.PI / 180f;

    private sealed class ModelNode
    {
        public ModelNode(string name, PlayerModelPart animationPart, Matrix4x4 localTransform, ModelMesh? mesh)
        {
            Name = name;
            AnimationPart = animationPart;
            LocalTransform = localTransform;
            Mesh = mesh;
        }

        public string Name { get; }
        public PlayerModelPart AnimationPart { get; }
        public PlayerModelPart RenderPart { get; set; }
        public Matrix4x4 LocalTransform { get; }
        public ModelMesh? Mesh { get; }
        public List<ModelNode> Children { get; } = [];
    }

    private sealed record ModelAsset(ModelNode[] RootNodes)
    {
        public int VertexCount { get; } = CountVertices(RootNodes);
        public int IndexCount { get; } = CountIndices(RootNodes);

        private static int CountVertices(IEnumerable<ModelNode> nodes) => nodes.Sum(node => (node.Mesh?.Vertices.Length ?? 0) + CountVertices(node.Children));

        private static int CountIndices(IEnumerable<ModelNode> nodes) => nodes.Sum(node => (node.Mesh?.Indices.Length ?? 0) + CountIndices(node.Children));
    }

    private sealed record ModelMesh(ModelVertex[] Vertices, uint[] Indices);

    private readonly record struct ModelVertex(Vector3 Position, Vector3 Normal, Vector2 Uv);

    private readonly record struct PlayerPose(Quaternion Head, Quaternion Body, Quaternion RightArm, Quaternion LeftArm, Quaternion RightLeg, Quaternion LeftLeg);

    private enum PlayerModelPart
    {
        None,
        Head,
        Body,
        RightArm,
        LeftArm,
        RightLeg,
        LeftLeg
    }
}
