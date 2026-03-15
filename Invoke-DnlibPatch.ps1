param(
    [Parameter(Mandatory = $false)]
    [string]$OutputDirectory = ".\out"
)

$ErrorActionPreference = "Stop"
$workspaceRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$dotnetHome = Join-Path $workspaceRoot ".dotnet"
$appDataRoot = Join-Path $workspaceRoot ".appdata"

New-Item -ItemType Directory -Force -Path $dotnetHome | Out-Null
New-Item -ItemType Directory -Force -Path $appDataRoot | Out-Null

$env:APPDATA = $appDataRoot
$env:DOTNET_CLI_HOME = $dotnetHome
$env:NUGET_PACKAGES = Join-Path $dotnetHome ".nuget\packages"
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"

$projectPath = Join-Path $workspaceRoot "tools\DnlibFunctionPatcher\DnlibFunctionPatcher.csproj"
$resolvedOutputDirectory = [System.IO.Path]::GetFullPath((Join-Path $workspaceRoot $OutputDirectory))

Write-Host "Running dnlib patcher"
dotnet run --project $projectPath -- $resolvedOutputDirectory
