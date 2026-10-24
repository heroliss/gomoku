[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = '3.0.0',
    [string]$PhotonSdkDirectory,
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $IsWindows) { throw 'Publish this Windows desktop application from Windows using PowerShell 7.' }
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if ($PhotonSdkDirectory) { $PhotonSdkDirectory = (Resolve-Path -LiteralPath $PhotonSdkDirectory).Path }
Push-Location $projectRoot
try {
    $bootstrapArguments = @{}
    if ($PhotonSdkDirectory) { $bootstrapArguments.PhotonSdkDirectory = $PhotonSdkDirectory }
    & (Join-Path $PSScriptRoot 'bootstrap.ps1') @bootstrapArguments

    $sdkProperties = @()
    if ($PhotonSdkDirectory) {
        $resolvedSdk = (Resolve-Path -LiteralPath $PhotonSdkDirectory).Path
        $sdkProperties = @("-p:PhotonSdkDirectory=$resolvedSdk")
    }
    $runId = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
    $name = "liss-gomoku-v$Version-win-x64"
    $publishRoot = Join-Path $projectRoot 'artifacts/publish'
    # The enclosing directory is unique; the folder inside the downloadable ZIP has a stable name.
    $publishDirectory = Join-Path (Join-Path $publishRoot $runId) $name
    $releaseDirectory = Join-Path $projectRoot 'artifacts/releases'
    $reportDirectory = Join-Path $projectRoot "artifacts/verification/$runId"
    New-Item -ItemType Directory -Path $publishDirectory, $releaseDirectory, $reportDirectory -Force | Out-Null

    if (-not $SkipTests) {
        foreach ($project in @('Gomoku.Core.Tests', 'Gomoku.Storage.Tests', 'Gomoku.Networking.Tests')) {
            $result = Join-Path $reportDirectory "$project.json"
            # PowerShell unwraps a one-item pipeline; retain string[] so native splatting cannot split a path into characters.
            [string[]]$testArguments = @(if ($project -eq 'Gomoku.Networking.Tests') { "--report=$result" } else { $result })
            & dotnet run --project "tests/$project" -c Release @sdkProperties -- @testArguments
            if ($LASTEXITCODE -ne 0) { throw "Acceptance failed: $project" }
        }
    }
    $arguments = @(
        'publish', 'src/Gomoku.App/Gomoku.App.csproj', '-c', 'Release', '-r', 'win-x64',
        '--self-contained', 'true', '-o', $publishDirectory, "-p:Version=$Version",
        '-p:PublishTrimmed=false', '-p:PublishSingleFile=false', '-p:DebugType=none', '-p:DebugSymbols=false'
    ) + $sdkProperties
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed; no release ZIP has been replaced.' }

    foreach ($required in @('Liss.Gomoku.exe', 'Photon3DotNet.dll', 'PhotonLoadbalancingApi.dll', 'coreclr.dll', 'hostfxr.dll')) {
        if (-not (Test-Path -LiteralPath (Join-Path $publishDirectory $required) -PathType Leaf)) { throw "Incomplete self-contained package: $required" }
    }
    Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination (Join-Path $publishDirectory 'README.md')
    $docsOutput = Join-Path $publishDirectory 'docs'
    New-Item -ItemType Directory -Path $docsOutput -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $projectRoot 'docs/BUILDING.md') -Destination (Join-Path $docsOutput 'BUILDING.md')
    $licenseOutput = Join-Path $publishDirectory 'licenses'
    New-Item -ItemType Directory -Path $licenseOutput -Force | Out-Null
    foreach ($notice in @('PHOTON-NOTICE.md', 'Photon-NET-OSS-current.pdf', 'dotnet-runtime-LICENSE.txt', 'dotnet-runtime-THIRD-PARTY-NOTICES.txt', 'dotnet-winforms-LICENSE.txt')) {
        $sourceNotice = Join-Path $projectRoot "licenses/$notice"
        if (-not (Test-Path -LiteralPath $sourceNotice -PathType Leaf)) { throw "Missing required third-party notice: $notice" }
        Copy-Item -LiteralPath $sourceNotice -Destination (Join-Path $licenseOutput $notice)
    }
    $files = @(Get-ChildItem -LiteralPath $publishDirectory -File -Recurse | Sort-Object FullName | ForEach-Object {
        [ordered]@{
            path = [System.IO.Path]::GetRelativePath($publishDirectory, $_.FullName).Replace('\', '/')
            bytes = $_.Length
            sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    })
    [ordered]@{
        application = 'Liss Gomoku'; version = $Version; runtime = 'win-x64'; selfContained = $true
        generatedUtc = [DateTimeOffset]::UtcNow.ToString('O'); testsSkipped = [bool]$SkipTests; files = $files
    } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $publishDirectory 'package-manifest.json') -Encoding utf8

    $archive = Join-Path $releaseDirectory "$name.zip"
    $temporaryArchive = Join-Path $releaseDirectory "$name-$runId.tmp.zip"
    try {
        Compress-Archive -LiteralPath $publishDirectory -DestinationPath $temporaryArchive -CompressionLevel Optimal
        [System.IO.File]::Move($temporaryArchive, $archive, $true)
    } finally {
        if (Test-Path -LiteralPath $temporaryArchive) { Remove-Item -LiteralPath $temporaryArchive }
    }
    $hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $name.zip" | Set-Content -LiteralPath "$archive.sha256" -Encoding ascii
    [ordered]@{ version = $Version; zip = [System.IO.Path]::GetRelativePath($projectRoot, $archive).Replace('\', '/'); bytes = (Get-Item -LiteralPath $archive).Length; sha256 = $hash; testsSkipped = [bool]$SkipTests; generatedUtc = [DateTimeOffset]::UtcNow.ToString('O') } |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $reportDirectory 'package.json') -Encoding utf8
    Write-Host "Release ZIP: $archive"
    Write-Host "SHA-256: $hash"
    Write-Host "Unpacked publish: $publishDirectory"
    Write-Host 'Packaging complete. Actual Windows launch and online acceptance are separate release checks.'
} finally { Pop-Location }

