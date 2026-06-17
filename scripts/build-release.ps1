[CmdletBinding()]
param(
    [string]$Runtime = 'win-x64',
    [string]$CodeSigningCertificatePath,
    [string]$CodeSigningCertificatePassword,
    [string]$TimestampUrl = 'http://timestamp.digicert.com',
    [switch]$RequireCodeSigning
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$output = Join-Path $root "artifacts\publish\$Runtime"
$projectPath = Join-Path $root 'src\Astral.App\Astral.App.csproj'
$project = [xml](Get-Content -Raw -LiteralPath $projectPath)
$version = $project.Project.PropertyGroup.Version
if ([string]::IsNullOrWhiteSpace($version)) {
    throw "Astral surumu proje dosyasindan okunamadi"
}
if ([string]::IsNullOrWhiteSpace($CodeSigningCertificatePassword) -and
    -not [string]::IsNullOrWhiteSpace($env:ASTRAL_CODESIGN_PFX_PASSWORD)) {
    $CodeSigningCertificatePassword = $env:ASTRAL_CODESIGN_PFX_PASSWORD
}

$archive = Join-Path $root "artifacts\Astral-$version-$Runtime.zip"
$shaPath = Join-Path $root "artifacts\Astral-$version-$Runtime.sha256.txt"
$stableArchive = Join-Path $root "artifacts\Astral-$Runtime.zip"
$stableShaPath = Join-Path $root "artifacts\Astral-$Runtime.sha256.txt"
$compatOutput = Join-Path $root "artifacts\publish\$Runtime-discorder-compat"
$compatArchive = Join-Path $root "artifacts\Discorder-$version-$Runtime.zip"
$compatShaPath = Join-Path $root "artifacts\Discorder-$version-$Runtime.sha256.txt"
$compatStableArchive = Join-Path $root "artifacts\Discorder-$Runtime.zip"
$compatStableShaPath = Join-Path $root "artifacts\Discorder-$Runtime.sha256.txt"
$signingStatusPath = Join-Path $root 'artifacts\signing-status.txt'
$updateManifestPath = Join-Path $output 'astral.update-manifest.json'
$wireSockInstallerName = 'wiresock-vpn-client-x64-1.4.7.1.msi'
$wireSockInstallerHash = 'FA3F483DA7EA1AE6C234F95BECB0AA6A18E7EB18B944D3FFB4518D40F4292F40'
$wireSockInstallerSource = Join-Path $root "vendor\wiresock\$wireSockInstallerName"
$buildArtifactsPath = Join-Path ([IO.Path]::GetTempPath()) (
    'astral-release-' + [guid]::NewGuid().ToString('N'))

function New-UpdateManifest {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RootDirectory,
        [Parameter(Mandatory = $true)]
        [string]$Version,
        [Parameter(Mandatory = $true)]
        [string]$ManifestFileName
    )

    $manifestPath = Join-Path $RootDirectory $ManifestFileName
    if (Test-Path -LiteralPath $manifestPath) {
        Remove-Item -LiteralPath $manifestPath -Force
    }

    $manifestRoot = [IO.Path]::GetFullPath($RootDirectory).TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar)
    $manifestFiles = Get-ChildItem -LiteralPath $RootDirectory -File -Recurse |
        Sort-Object FullName |
        ForEach-Object {
            $relativePath = [IO.Path]::GetFullPath($_.FullName).Substring($manifestRoot.Length).TrimStart('\', '/')
            $relativePath = $relativePath -replace '\\', '/'
            $fileHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $_.FullName).Hash
            [pscustomobject]@{
                path = $relativePath
                length = $_.Length
                sha256 = $fileHash
            }
        }

    [pscustomobject]@{
        version = $Version
        files = $manifestFiles
    } |
        ConvertTo-Json -Depth 5 |
        Set-Content -LiteralPath $manifestPath -Encoding UTF8
}

function New-ReleaseArchive {
    param(
        [Parameter(Mandatory = $true)]
        [string]$SourceDirectory,
        [Parameter(Mandatory = $true)]
        [string]$ArchivePath,
        [Parameter(Mandatory = $true)]
        [string]$ShaPath
    )

    if (Test-Path -LiteralPath $ArchivePath) {
        Remove-Item -LiteralPath $ArchivePath -Force
    }

    Compress-Archive -Path (Join-Path $SourceDirectory '*') -DestinationPath $ArchivePath
    $archiveHash = Get-FileHash -Algorithm SHA256 -LiteralPath $ArchivePath
    Set-Content `
        -LiteralPath $ShaPath `
        -Value "$($archiveHash.Hash)  $(Split-Path -Leaf $ArchivePath)" `
        -Encoding ASCII

    return $archiveHash
}

Push-Location $root

try {
    & "$PSScriptRoot\verify.ps1" `
        -ArtifactsPath (Join-Path $buildArtifactsPath 'verify')

    if (Test-Path -LiteralPath $output) {
        Remove-Item -LiteralPath $output -Recurse -Force
    }
    if (Test-Path -LiteralPath $compatOutput) {
        Remove-Item -LiteralPath $compatOutput -Recurse -Force
    }

    dotnet publish src\Astral.App\Astral.App.csproj `
        --configuration Release `
        --runtime $Runtime `
        --self-contained true `
        --output $output `
        --artifacts-path (Join-Path $buildArtifactsPath 'publish') `
        --disable-build-servers `
        -p:DebugType=None `
        -p:DebugSymbols=false
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish hata kodu $LASTEXITCODE ile basarisiz oldu"
    }

    & "$PSScriptRoot\prepare-background-video.ps1" -PublishDirectory $output

    if (Test-Path -LiteralPath $wireSockInstallerSource) {
        $wireSockInstallerActualHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $wireSockInstallerSource).Hash
        if (-not [string]::Equals(
                $wireSockInstallerActualHash,
                $wireSockInstallerHash,
                [StringComparison]::OrdinalIgnoreCase)) {
            throw "WireSock kurucu SHA-256 dogrulamasi basarisiz oldu: $wireSockInstallerSource"
        }

        $wireSockInstallerOutput = Join-Path $output 'installers'
        New-Item -ItemType Directory -Path $wireSockInstallerOutput -Force | Out-Null
        Copy-Item `
            -LiteralPath $wireSockInstallerSource `
            -Destination (Join-Path $wireSockInstallerOutput $wireSockInstallerName) `
            -Force
    }

    $signed = $false
    if (-not [string]::IsNullOrWhiteSpace($CodeSigningCertificatePath) -or $RequireCodeSigning) {
        & "$PSScriptRoot\sign-release.ps1" `
            -PublishDirectory $output `
            -CertificatePath $CodeSigningCertificatePath `
            -CertificatePassword $CodeSigningCertificatePassword `
            -TimestampUrl $TimestampUrl
        $signed = $true
    }
    else {
        Write-Host 'Kod imzalama atlandi: sertifika yapilandirilmadi.'
    }

    if ($signed) {
        Set-Content -LiteralPath $signingStatusPath -Value 'signed' -Encoding ASCII
    }
    else {
        Set-Content -LiteralPath $signingStatusPath -Value 'unsigned' -Encoding ASCII
    }

    New-UpdateManifest `
        -RootDirectory $output `
        -Version $version `
        -ManifestFileName 'astral.update-manifest.json'

    if (Test-Path -LiteralPath $archive) {
        Remove-Item -LiteralPath $archive -Force
    }
    if (Test-Path -LiteralPath $stableArchive) {
        Remove-Item -LiteralPath $stableArchive -Force
    }
    if (Test-Path -LiteralPath $compatArchive) {
        Remove-Item -LiteralPath $compatArchive -Force
    }
    if (Test-Path -LiteralPath $compatStableArchive) {
        Remove-Item -LiteralPath $compatStableArchive -Force
    }

    $hash = New-ReleaseArchive `
        -SourceDirectory $output `
        -ArchivePath $archive `
        -ShaPath $shaPath
    Copy-Item -LiteralPath $archive -Destination $stableArchive -Force
    Set-Content `
        -LiteralPath $stableShaPath `
        -Value "$($hash.Hash)  $(Split-Path -Leaf $stableArchive)" `
        -Encoding ASCII

    New-Item -ItemType Directory -Path $compatOutput -Force | Out-Null
    Copy-Item `
        -Path (Join-Path $output '*') `
        -Destination $compatOutput `
        -Recurse `
        -Force
    Copy-Item `
        -LiteralPath (Join-Path $compatOutput 'Astral.exe') `
        -Destination (Join-Path $compatOutput 'Discorder.exe') `
        -Force
    Copy-Item `
        -LiteralPath (Join-Path $compatOutput 'Astral.Updater.exe') `
        -Destination (Join-Path $compatOutput 'Discorder.Updater.exe') `
        -Force
    New-UpdateManifest `
        -RootDirectory $compatOutput `
        -Version $version `
        -ManifestFileName 'discorder.update-manifest.json'

    $compatHash = New-ReleaseArchive `
        -SourceDirectory $compatOutput `
        -ArchivePath $compatArchive `
        -ShaPath $compatShaPath
    Copy-Item -LiteralPath $compatArchive -Destination $compatStableArchive -Force
    Set-Content `
        -LiteralPath $compatStableShaPath `
        -Value "$($compatHash.Hash)  $(Split-Path -Leaf $compatStableArchive)" `
        -Encoding ASCII

    $hash
    $compatHash
}
finally {
    Pop-Location
    if (Test-Path -LiteralPath $buildArtifactsPath) {
        Remove-Item -LiteralPath $buildArtifactsPath -Recurse -Force
    }
}
