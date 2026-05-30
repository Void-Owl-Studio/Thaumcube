using System.Diagnostics;
using System.Text;

namespace VoxelGame.Core;

internal static class NativeFileDialog
{
    public static string? PickPngFile()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            return PickPngFileWithPowerShell();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string? PickPngFileWithPowerShell()
    {
        var script = """
            Add-Type -AssemblyName System.Windows.Forms
            $dialog = New-Object System.Windows.Forms.OpenFileDialog
            $dialog.Title = 'Select player skin PNG'
            $dialog.Filter = 'PNG skin (*.png)|*.png|All files (*.*)|*.*'
            $dialog.CheckFileExists = $true
            $dialog.CheckPathExists = $true
            $dialog.Multiselect = $false
            $dialog.RestoreDirectory = $true
            $result = $dialog.ShowDialog()
            if ($result -eq [System.Windows.Forms.DialogResult]::OK) {
                [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
                Write-Output $dialog.FileName
            }
            """;
        var encodedScript = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -STA -ExecutionPolicy Bypass -EncodedCommand {encodedScript}",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        process.Start();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        var path = output.Trim();
        return string.IsNullOrWhiteSpace(path) ? null : path;
    }
}
