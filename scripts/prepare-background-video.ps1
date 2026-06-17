[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PublishDirectory
)

$ErrorActionPreference = 'Stop'

$videoUrl = 'https://d8j0ntlcm91z4.cloudfront.net/user_38xzZboKViGWJOttwIXH07lWA1P/hf_20260606_154941_df1a96e1-a06f-450c-bd02-d863414cc1a0.mp4'
$expectedSha256 = '8DDCC4D001F91F43447103601299BAD902F761818B7DAF36B797134DFEF50ACC'
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

    foreach ($candidate in $candidatePaths | Sort-Object -Unique) {
        try {
            if (-not (Test-Path -LiteralPath $candidate)) {
                continue
            }

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
