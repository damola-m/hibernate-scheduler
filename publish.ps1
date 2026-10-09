# ===================================
# publish.ps1
# -----------------------------------
# - Builds a self-contained single exe and installs it to %LocalAppData%\Programs\HibernateScheduler.
# - Self-contained, so the target machine doesn't need the .NET runtime.
# ===================================

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'src\HibernateScheduler\HibernateScheduler.csproj'
$out     = Join-Path $PSScriptRoot 'publish'
$target  = Join-Path $env:LOCALAPPDATA 'Programs\HibernateScheduler'

dotnet publish $project -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $out

# Running copy locks the exe
Get-Process HibernateScheduler -ErrorAction SilentlyContinue | Stop-Process -Force

New-Item -ItemType Directory -Force $target | Out-Null
Copy-Item (Join-Path $out 'HibernateScheduler.exe') $target -Force
Write-Host "Installed to $target\HibernateScheduler.exe"
