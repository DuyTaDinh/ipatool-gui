# Builds IpaToolGui.exe into .\dist (requires the .NET SDK 6+ on the build machine only).
#   ./build.ps1                 -> version from the .csproj
#   ./build.ps1 -Version 1.2.0  -> stamps that version into the exe (used by CI for tags)
param([string]$Version = "")

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$project = "$root\src\IpaToolGui\IpaToolGui.csproj"

$buildArgs = @($project, "-c", "Release", "-nologo")
if ($Version) { $buildArgs += "-p:Version=$Version" }
dotnet build @buildArgs
if ($LASTEXITCODE -ne 0) { throw "Build failed" }

New-Item -ItemType Directory -Force "$root\dist" | Out-Null
Copy-Item "$root\src\IpaToolGui\bin\Release\net48\IpaToolGui.exe" "$root\dist\IpaToolGui.exe" -Force
$hash = (Get-FileHash "$root\dist\IpaToolGui.exe" -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content "$root\dist\IpaToolGui.exe.sha256" "$hash  IpaToolGui.exe" -Encoding ascii
Write-Host "Done: $root\dist\IpaToolGui.exe (sha256 $hash)"
