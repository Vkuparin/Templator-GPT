$ErrorActionPreference = 'Stop'
Push-Location "$PSScriptRoot/.."
try {
    dotnet build Templator.slnx -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }
    dotnet run --project tests/Templator.Tests -c Release --no-build
    if ($LASTEXITCODE -ne 0) { throw 'Core regression checks failed.' }
    dotnet run --project tests/Templator.UiTests -c Release --no-build -- artifacts/ui
    if ($LASTEXITCODE -ne 0) { throw 'WPF integration checks failed.' }
} finally { Pop-Location }
