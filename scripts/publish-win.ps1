$ErrorActionPreference = "Stop"

$repo = Split-Path -Parent $PSScriptRoot
$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if ($null -eq $dotnet) {
    $fallback = "C:\Program Files\dotnet\dotnet.exe"
    if (Test-Path $fallback) {
        $dotnetPath = $fallback
    } else {
        throw "dotnet was not found in PATH and fallback '$fallback' does not exist."
    }
} else {
    $dotnetPath = $dotnet.Source
}

& $dotnetPath publish "$repo\src\VoxelGame\VoxelGame.csproj" -c Release -r win-x64 --self-contained true -o "$repo\Build\Windows" --configfile "$repo\NuGet.Config"
