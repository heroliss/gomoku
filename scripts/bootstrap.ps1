[CmdletBinding()]
param(
    [string]$PhotonSdkDirectory,
    [switch]$OpenDownloadPages
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$dotnetUrl = 'https://dotnet.microsoft.com/zh-cn/download/dotnet/10.0'
$photonUrl = 'https://www.photonengine.com/sdks'
Write-Host "Official .NET 10 SDK: $dotnetUrl"
Write-Host "Official Photon SDK: $photonUrl"

if ($OpenDownloadPages) {
    Start-Process $dotnetUrl
    Start-Process $photonUrl
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'Install the Windows x64 .NET 10 SDK from the official page, then reopen PowerShell.'
}
$sdks = @(& dotnet --list-sdks)
if ($LASTEXITCODE -ne 0 -or -not ($sdks | Where-Object { $_ -match '^10\.' })) {
    throw 'A .NET 10 SDK is required. A desktop runtime alone cannot build the source.'
}

$names = @('Photon3DotNet.dll', 'PhotonLoadbalancingApi.dll')
$expected = @{
    'Photon3DotNet.dll' = '5ce4047127715d4e8e66c409de63d284d8a9b8dce9f4368e4c15019e55ec2114'
    'PhotonLoadbalancingApi.dll' = 'cfd0cdc167ec5ebd619c4d5de2e5e98db4853b5ecdf096fd758f940537945aea'
}
$vendor = Join-Path $projectRoot 'vendor/Photon'
if ($PhotonSdkDirectory) {
    $source = (Resolve-Path -LiteralPath $PhotonSdkDirectory).Path
} elseif (Test-Path -LiteralPath (Join-Path $vendor $names[0])) {
    $source = $vendor
} else {
    $legacy = Join-Path $projectRoot 'legacy/v2.3/五子棋/Lib'
    if (-not (Test-Path -LiteralPath (Join-Path $legacy $names[0]))) {
        throw 'Obtain the compatible Photon .NET 4.0.5 SDK lawfully via the official account/download channel or your original project. Then run .\scripts\bootstrap.ps1 -PhotonSdkDirectory <directory>. The latest Photon API is not a drop-in replacement. See docs/BUILDING.md; this script never downloads or mirrors proprietary DLLs.'
    }
    $source = $legacy
}

foreach ($name in $names) {
    $file = Join-Path $source $name
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "Missing required SDK assembly: $name" }
    $version = [System.Reflection.AssemblyName]::GetAssemblyName($file).Version.ToString()
    $hash = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($version -ne '4.0.5.0' -or $hash -ne $expected[$name]) {
        throw "SDK compatibility verification failed for $name. This source release targets the unmodified 4.0.5.0 assemblies documented in licenses/PHOTON-NOTICE.md. Do not rename a newer SDK to bypass this check."
    }
}

if ($PhotonSdkDirectory) {
    New-Item -ItemType Directory -Path $vendor -Force | Out-Null
    if ([System.IO.Path]::GetFullPath($source).TrimEnd('\', '/') -ne [System.IO.Path]::GetFullPath($vendor).TrimEnd('\', '/')) {
        foreach ($name in $names) { Copy-Item -LiteralPath (Join-Path $source $name) -Destination (Join-Path $vendor $name) -Force }
    }
    Write-Host 'Compatible SDK copied into the ignored vendor/Photon directory.'
}
Write-Host '.NET 10 SDK and both compatible Photon assemblies verified.'
Write-Host 'Source build: dotnet build src/Gomoku.App/Gomoku.App.csproj -c Release'
Write-Host 'Windows ZIP: .\scripts\publish.ps1'

