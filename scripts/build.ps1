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

& $dotnetPath build "$repo\VoxelGame.sln" --configfile "$repo\NuGet.Config"
