using VoxelGame.Core;

if (args.Contains("--smoke-test", StringComparer.OrdinalIgnoreCase))
{
    return SmokeTest.Run();
}

var autoCloseMs = TryReadIntArgument(args, "--auto-close-ms");

using var game = new GameApplication(autoCloseMs is > 0 ? autoCloseMs.Value / 1000.0 : null);
game.Run();
return 0;

static int? TryReadIntArgument(string[] args, string name)
{
    for (var i = 0; i < args.Length - 1; i++)
    {
        if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase) && int.TryParse(args[i + 1], out var value))
        {
            return value;
        }
    }

    return null;
}
