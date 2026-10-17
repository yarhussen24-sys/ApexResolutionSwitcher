$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'ApexResolutionSwitcher.csproj'

# Prefer the SDK installation that actually has an SDK. Some PCs have both
# x64 and x86 dotnet hosts installed, and PATH may point at the wrong one.
$candidates = @()
$command = Get-Command dotnet -ErrorAction SilentlyContinue
if ($command) { $candidates += $command.Source }
$candidates += @(
    "$env:ProgramFiles\dotnet\dotnet.exe",
    "${env:ProgramFiles(x86)}\dotnet\dotnet.exe"
) | Where-Object { $_ -and (Test-Path $_) }

$dotnetExe = $null
foreach ($candidate in ($candidates | Select-Object -Unique)) {
    try {
        $sdks = & $candidate --list-sdks 2>$null
        if ($LASTEXITCODE -eq 0 -and $sdks) {
            $dotnetExe = $candidate
            break
        }
    } catch { }
}

if (-not $dotnetExe) {
    Write-Host '.NET 8 SDK is required. Install it from https://dotnet.microsoft.com/download/dotnet/8.0' -ForegroundColor Red
    Read-Host 'Press Enter to close'
    exit 1
}

Write-Host "Using SDK host: $dotnetExe" -ForegroundColor Cyan
& $dotnetExe publish $project -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
Write-Host ''
Write-Host 'Build complete:' -ForegroundColor Green
Write-Host (Join-Path $PSScriptRoot 'bin\Release\net8.0-windows\win-x64\publish\ApexResolutionSwitcher.exe')
Read-Host 'Press Enter to close'
