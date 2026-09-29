param([switch]$SelfContained)
$ErrorActionPreference = 'Stop'
Push-Location "$PSScriptRoot/.."
try {
    $flavor = if ($SelfContained) { 'standalone' } else { 'portable' }
    $version = ([xml](Get-Content -LiteralPath 'src/Templator/Templator.csproj' -Raw)).Project.PropertyGroup.Version
    $suffix = if ($SelfContained) { '' } else { '-portable' }
    $zip = "artifacts/Templator.v$version$suffix.zip"
    $publishArgs = @('publish', 'src/Templator/Templator.csproj', '-c', 'Release', '-r', 'win-x64',
        '-p:PublishSingleFile=true', '-p:EnableSingleFileAnalyzer=false', '-p:DebugType=none',
        '-p:IncludeNativeLibrariesForSelfExtract=true', '-o', "artifacts/$flavor")
    if ($SelfContained) {
        # Downloads only Microsoft's runtime packs, not third-party app dependencies.
        $publishArgs += @('--self-contained', 'true', '--source', 'https://api.nuget.org/v3/index.json')
    } else { $publishArgs += @('--self-contained', 'false') }
    & dotnet @publishArgs
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    Copy-Item -LiteralPath README.md -Destination "artifacts/$flavor/README.md"
    Compress-Archive -LiteralPath "artifacts/$flavor/Templator.exe", "artifacts/$flavor/README.md" -DestinationPath $zip -Force
    Get-FileHash -Algorithm SHA256 $zip
} finally { Pop-Location }
