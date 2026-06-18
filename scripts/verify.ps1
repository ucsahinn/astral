[CmdletBinding()]
param(
    [string]$ArtifactsPath
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$createdArtifactsPath = $false
if ([string]::IsNullOrWhiteSpace($ArtifactsPath)) {
    $ArtifactsPath = Join-Path ([IO.Path]::GetTempPath()) (
        'astral-verify-' + [guid]::NewGuid().ToString('N'))
    $createdArtifactsPath = $true
}

Push-Location $root

try {
    dotnet build Astral.sln `
        --configuration Release `
        --artifacts-path $ArtifactsPath `
        --disable-build-servers
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build hata kodu $LASTEXITCODE ile basarisiz oldu"
    }

    $coreTests = Join-Path $ArtifactsPath 'bin\Astral.Core.Tests\release\Astral.Core.Tests.dll'
    if (-not (Test-Path -LiteralPath $coreTests)) {
        throw "Core test cikisi bulunamadi: $coreTests"
    }
    dotnet $coreTests
    if ($LASTEXITCODE -ne 0) {
        throw "cekirdek testleri hata kodu $LASTEXITCODE ile basarisiz oldu"
    }

    $windowsTests = Join-Path $ArtifactsPath 'bin\Astral.Windows.Tests\release\Astral.Windows.Tests.dll'
    if (-not (Test-Path -LiteralPath $windowsTests)) {
        throw "Windows test cikisi bulunamadi: $windowsTests"
    }
    dotnet $windowsTests
    if ($LASTEXITCODE -ne 0) {
        throw "Windows guvenlik testleri hata kodu $LASTEXITCODE ile basarisiz oldu"
    }

    $forbidden = @(
        'ServerCertificateCustomValidationCallback',
        'RemoteCertificateValidationCallback',
        'GoodbyeDPI',
        'ByeDPI',
        'Zapret',
        'ProxiFyre',
        'drover',
        'WinDivert',
        'WebCord',
        'AdvancedSplitWire',
        'Advanced SplitWire',
        'SplitWireTurkey',
        '"Update.exe"'
    )

    $productionSourceFiles = @(
        Get-ChildItem -LiteralPath 'src' -Recurse -File |
            Where-Object {
                $_.Extension -in '.cs', '.xaml' -and
                $_.FullName -notmatch '[\\/](bin|obj)[\\/]'
            }
    )

    if ($productionSourceFiles.Count -eq 0) {
        throw "Uretim kaynak taramasi icin .cs veya .xaml dosyasi bulunamadi"
    }

    foreach ($pattern in $forbidden) {
        $matches = @(
            $productionSourceFiles |
                Select-String -Pattern $pattern -SimpleMatch
        )

        if ($matches.Count -gt 0) {
            $formattedMatches = $matches |
                ForEach-Object {
                    $relativePath = Resolve-Path -LiteralPath $_.Path -Relative
                    "${relativePath}:$($_.LineNumber): $($_.Line.Trim())"
                }
            throw "Uretim kaynaklarinda yasakli desen bulundu: '$pattern'`n$($formattedMatches -join "`n")"
        }
    }

    foreach ($scriptPath in Get-ChildItem -LiteralPath 'scripts' -Filter '*.ps1') {
        $tokens = $null
        $parseErrors = $null
        [System.Management.Automation.Language.Parser]::ParseFile(
            $scriptPath.FullName,
            [ref]$tokens,
            [ref]$parseErrors) | Out-Null

        if ($parseErrors.Count -gt 0) {
            $messages = $parseErrors | ForEach-Object { $_.Message }
            throw "PowerShell script parse hatasi: $($scriptPath.Name)`n$($messages -join "`n")"
        }
    }

    $manifest = Get-Content -Raw -LiteralPath 'src\Astral.App\app.manifest'
    if ($manifest -notmatch 'requestedExecutionLevel level="requireAdministrator"') {
        throw "Astral WireSock VPN Client surecini yonetmek icin yonetici manifest'iyle derlenmelidir"
    }

    function Get-ProjectProperty {
        param(
            [string]$ProjectPath,
            [string]$PropertyName
        )

        $projectXml = [xml](Get-Content -Raw -LiteralPath $ProjectPath)
        $value = $projectXml.Project.PropertyGroup.$PropertyName |
            Select-Object -First 1
        return [string]$value
    }

    $projectVersion = Get-ProjectProperty `
        -ProjectPath 'src\Astral.App\Astral.App.csproj' `
        -PropertyName 'Version'
    if ([string]::IsNullOrWhiteSpace($projectVersion)) {
        throw "Astral uygulama surumu proje dosyasindan okunamadi"
    }

    $versionedProjects = @(
        @{ Path = 'src\Astral.App\Astral.App.csproj'; Name = 'Astral.App' },
        @{ Path = 'src\Astral.Updater\Astral.Updater.csproj'; Name = 'Astral.Updater' },
        @{ Path = 'src\Astral.WebProxy\Astral.WebProxy.csproj'; Name = 'Astral.WebProxy' }
    )
    foreach ($versionedProject in $versionedProjects) {
        $version = Get-ProjectProperty `
            -ProjectPath $versionedProject.Path `
            -PropertyName 'Version'
        if ($version -ne $projectVersion) {
            throw "$($versionedProject.Name) surumu Astral.App surumuyle eslesmiyor: $version != $projectVersion"
        }

        $fileVersion = Get-ProjectProperty `
            -ProjectPath $versionedProject.Path `
            -PropertyName 'FileVersion'
        $expectedFileVersion = "$projectVersion.0"
        if ($fileVersion -ne $expectedFileVersion) {
            throw "$($versionedProject.Name) FileVersion degeri beklenen surumle eslesmiyor: $fileVersion != $expectedFileVersion"
        }
    }

    $expectedManifestVersion = "$projectVersion.0"
    if ($manifest -notmatch "<assemblyIdentity\s+version=`"$([regex]::Escape($expectedManifestVersion))`"\s+name=`"Astral`"") {
        throw "Astral manifest surumu proje surumuyle eslesmiyor: beklenen $expectedManifestVersion"
    }

    $gitleaks = Get-Command gitleaks -ErrorAction SilentlyContinue
    if ($null -ne $gitleaks) {
        & $gitleaks.Source dir . --redact --no-banner --exit-code 1
        if ($LASTEXITCODE -ne 0) {
            throw "gitleaks secret scan hata kodu $LASTEXITCODE ile basarisiz oldu"
        }
    }
    else {
        Write-Warning 'gitleaks bulunamadi; secret scan bu makinede atlandi.'
    }

    Write-Host 'Dogrulama basariyla tamamlandi.'
}
finally {
    Pop-Location
    if ($createdArtifactsPath -and (Test-Path -LiteralPath $ArtifactsPath)) {
        Remove-Item -LiteralPath $ArtifactsPath -Recurse -Force
    }
}
