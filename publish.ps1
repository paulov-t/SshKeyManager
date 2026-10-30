param(
    [switch]$SelfContained,
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$project = Join-Path $root "src/SshKeyManager"

if ($SelfContained) {
    $output = Join-Path $root "artifacts/$Runtime"
    dotnet publish $project -c Release -r $Runtime --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -o $output
}
else {
    $output = Join-Path $root "artifacts/publish"
    dotnet publish $project -c Release -o $output
}

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

Write-Host "Published to $output"
