using Silk.NET.Core;
using Silk.NET.Shaderc;

namespace VoxelGame.Rendering.Vulkan;

internal unsafe sealed class SpirvShaderCompiler : IDisposable
{
    private readonly Shaderc _shaderc;
    private readonly Compiler* _compiler;
    private readonly CompileOptions* _options;

    public SpirvShaderCompiler()
    {
        _shaderc = Shaderc.GetApi();
        _compiler = _shaderc.CompilerInitialize();
        _options = _shaderc.CompileOptionsInitialize();

        if (_compiler is null || _options is null)
        {
            throw new InvalidOperationException("Shaderc initialization failed.");
        }

        _shaderc.CompileOptionsSetSourceLanguage(_options, SourceLanguage.Glsl);
        _shaderc.CompileOptionsSetTargetEnv(_options, TargetEnv.Vulkan, 0);
        _shaderc.CompileOptionsSetTargetSpirv(_options, SpirvVersion.Shaderc10);
        _shaderc.CompileOptionsSetOptimizationLevel(_options, OptimizationLevel.Performance);
#if DEBUG
        _shaderc.CompileOptionsSetGenerateDebugInfo(_options);
#endif
    }

    public byte[] CompileFile(string path, ShaderKind kind)
    {
        var source = File.ReadAllText(path);
        var fileName = Path.GetFileName(path);
        var entryPoint = "main";

        var result = _shaderc.CompileIntoSpv(
            _compiler,
            source,
            (nuint)source.Length,
            kind,
            fileName,
            entryPoint,
            _options);

        if (result is null)
        {
            throw new InvalidOperationException($"Shaderc returned no result for {fileName}.");
        }

        try
        {
            var status = _shaderc.ResultGetCompilationStatus(result);
            if (status != CompilationStatus.Success)
            {
                var error = _shaderc.ResultGetErrorMessageS(result);
                throw new InvalidOperationException($"Failed to compile {fileName}: {status}. {error}");
            }

            var length = checked((int)_shaderc.ResultGetLength(result));
            var bytes = new byte[length];

            fixed (byte* destination = bytes)
            {
                Buffer.MemoryCopy(_shaderc.ResultGetBytes(result), destination, length, length);
            }

            return bytes;
        }
        finally
        {
            _shaderc.ResultRelease(result);
        }
    }

    public void Dispose()
    {
        if (_options is not null)
        {
            _shaderc.CompileOptionsRelease(_options);
        }

        if (_compiler is not null)
        {
            _shaderc.CompilerRelease(_compiler);
        }

        _shaderc.Dispose();
    }
}
