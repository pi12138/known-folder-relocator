[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [string]$OutputDirectory,
    [switch]$SkipTests
)

Set-StrictMode -Version 3.0
$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot (Join-Path 'artifacts' (Join-Path 'publish' $Runtime))
}

$solutionPath = Join-Path $repoRoot 'KnownFolderRelocator.sln'
$projectPath = Join-Path $repoRoot 'src\KnownFolderRelocator\KnownFolderRelocator.csproj'
$testProjectPath = Join-Path $repoRoot 'tests\KnownFolderRelocator.Tests\KnownFolderRelocator.Tests.csproj'
$configPath = Join-Path $repoRoot 'known-folders.json'

Write-Host "Building $solutionPath"
dotnet build $solutionPath -c $Configuration

if (-not $SkipTests) {
    Write-Host "Running tests"
    dotnet run --project $testProjectPath -c $Configuration
}

Write-Host "Publishing $Runtime to $OutputDirectory"
dotnet publish $projectPath `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    /p:PublishSingleFile=true `
    /p:EnableCompressionInSingleFile=true `
    -o $OutputDirectory

Copy-Item -LiteralPath $configPath -Destination (Join-Path $OutputDirectory 'known-folders.json') -Force

Write-Host "Published:"
Write-Host "  $(Join-Path $OutputDirectory 'known-folder-relocator.exe')"
Write-Host "  $(Join-Path $OutputDirectory 'known-folders.json')"
