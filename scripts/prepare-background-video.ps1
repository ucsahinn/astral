[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PublishDirectory,
    [switch]$AllowDownload
)

$ErrorActionPreference = 'Stop'

$videoUrl = 'https://d8j0ntlcm91z4.cloudfront.net/user_38xzZboKViGWJOttwIXH07lWA1P/hf_20260328_105406_16f4600d-7a92-4292-b96e-b19156c7830a.mp4'
$expectedSha256 = '24048C39F8E52DE3A6373500B4755588CABEC98A5BAE009D7E3351DA48572CCD'
$assetDirectory = Join-Path $PublishDirectory 'Assets'
$videoPath = Join-Path $assetDirectory 'background.mp4'
$temporaryPath = $videoPath + '.download'
$root = Split-Path -Parent $PSScriptRoot

function Copy-VerifiedBackgroundVideo {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Destination
    )

    $candidatePaths = @()
    $sourceAsset = Join-Path $root 'src\Astral.App\Assets\background.mp4'
    if (Test-Path -LiteralPath $sourceAsset) {
        $candidatePaths += $sourceAsset
    }

    $neutralArtifactSource = Join-Path $root 'artifacts\background-source.mp4'
    if (Test-Path -LiteralPath $neutralArtifactSource) {
        $candidatePaths += $neutralArtifactSource
    }

    $artifactsRoot = Join-Path $root 'artifacts'
    if (Test-Path -LiteralPath $artifactsRoot) {
        $candidatePaths += Get-ChildItem `
            -LiteralPath $artifactsRoot `
            -Recurse `
            -File `
            -Filter 'background.mp4' `
            -ErrorAction SilentlyContinue |
            Select-Object -ExpandProperty FullName
    }

    $seenCandidates = @{}
    foreach ($candidate in $candidatePaths) {
        try {
            if (-not (Test-Path -LiteralPath $candidate)) {
                continue
            }

            $candidateKey = [IO.Path]::GetFullPath($candidate).ToUpperInvariant()
            if ($seenCandidates.ContainsKey($candidateKey)) {
                continue
            }
            $seenCandidates[$candidateKey] = $true

            $candidateHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $candidate).Hash
            if ($candidateHash -ne $expectedSha256) {
                continue
            }

            Copy-Item -LiteralPath $candidate -Destination $Destination -Force
            Write-Host "Arka plan videosu yerel dogrulanmis kopyadan eklendi: $candidate"
            return $true
        }
        catch {
        }
    }

    return $false
}

New-Item -ItemType Directory -Force -Path $assetDirectory | Out-Null

if (Test-Path -LiteralPath $videoPath) {
    $existingHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $videoPath).Hash
    if ($existingHash -eq $expectedSha256) {
        Write-Host 'Arka plan videosu zaten paket icinde ve dogrulanmis.'
        return
    }

    Remove-Item -LiteralPath $videoPath -Force
}

if (Test-Path -LiteralPath $temporaryPath) {
    Remove-Item -LiteralPath $temporaryPath -Force
}

try {
    if (-not (Copy-VerifiedBackgroundVideo -Destination $videoPath)) {
        if (-not $AllowDownload) {
            throw "Dogrulanmis yerel arka plan videosu bulunamadi. Beklenen SHA-256: $expectedSha256. Release build icin src\Astral.App\Assets\background.mp4 veya artifacts\background-source.mp4 dosyasini hazirlayin; manuel indirme gerekiyorsa -AllowDownload kullanin."
        }

        $lastError = $null
        for ($attempt = 1; $attempt -le 3; $attempt++) {
            try {
                Invoke-WebRequest `
                    -Uri $videoUrl `
                    -OutFile $temporaryPath `
                    -UseBasicParsing `
                    -TimeoutSec 120
                $lastError = $null
                break
            }
            catch {
                $lastError = $_
                if (Test-Path -LiteralPath $temporaryPath) {
                    Remove-Item -LiteralPath $temporaryPath -Force
                }
                if ($attempt -lt 3) {
                    Start-Sleep -Seconds (3 * $attempt)
                }
            }
        }

        if ($null -ne $lastError) {
            throw $lastError
        }

        $downloadedHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $temporaryPath).Hash
        if ($downloadedHash -ne $expectedSha256) {
            throw "Arka plan videosu SHA-256 dogrulamasi basarisiz oldu: $downloadedHash"
        }

        Move-Item -LiteralPath $temporaryPath -Destination $videoPath -Force
        Write-Host 'Arka plan videosu release paketine eklendi.'
    }
}
finally {
    if (Test-Path -LiteralPath $temporaryPath) {
        Remove-Item -LiteralPath $temporaryPath -Force
    }
}
