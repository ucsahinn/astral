[CmdletBinding()]
param(
    [string]$ExePath,
    [int]$TimeoutSeconds = 90,
    [string]$OutputPath,
    [string[]]$TargetIds = @('discord'),
    [switch]$ManualTargetActionRecheck,
    [switch]$RequireTargetActionRecheck,
    [int]$ManualTargetActionTimeoutSeconds = 120
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($ExePath)) {
    $ExePath = Join-Path $root 'artifacts\publish\win-x64\Astral.exe'
}

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $root 'artifacts\app-live-connect-smoke.txt'
}

$normalizedTargetIds = @(
    $TargetIds |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        ForEach-Object {
            $_ -split ',' |
                Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
                ForEach-Object { $_.Trim().ToLowerInvariant() }
        } |
        Select-Object -Unique
)
if ($normalizedTargetIds.Count -eq 0) {
    throw 'Canli smoke testi icin en az bir hedef secilmelidir.'
}

$manualTargetActionRecheckEnabled =
    [bool]$ManualTargetActionRecheck -or [bool]$RequireTargetActionRecheck

$settingsPath = Join-Path $env:LOCALAPPDATA 'Astral\settings.json'
$profilePath = Join-Path $env:ProgramData 'Astral\profiles\discord.conf'
$logPath = Join-Path $env:LOCALAPPDATA 'Astral\logs\tunnel.log'
$hostsPath = Join-Path $env:SystemRoot 'System32\drivers\etc\hosts'
$beginMarker = '# BEGIN Astral hedef kilidi'
$ruleName = 'Astral.BlockTargetDomains'
$targetAppPatterns = @{
    'discord' = @(
        'Discord(?:PTB|Canary|Development)?\.exe'
    )
    'azar' = @(
        'Azar\.exe'
    )
    'tango' = @(
        'Tango\.exe'
    )
    'livu' = @(
        'LiVU\.exe',
        'Livu\.exe'
    )
    'imvu' = @(
        'IMVUClient\.exe',
        'IMVU\.exe'
    )
}
$targetProcessNames = @{
    'discord' = @(
        'Discord',
        'DiscordPTB',
        'DiscordCanary',
        'DiscordDevelopment'
    )
    'azar' = @('Azar')
    'tango' = @('Tango')
    'livu' = @('LiVU', 'Livu')
    'imvu' = @('IMVUClient', 'IMVU')
}
$targetLockProbeDomains = @{
    'discord' = @('discord.com', 'discord.gg')
    'wattpad' = @('wattpad.com', 'www.wattpad.com')
    'azar' = @('azarlive.com', 'www.azarlive.com')
    'bigo-live' = @('bigo.tv', 'www.bigo.tv')
    'imvu' = @('imvu.com', 'www.imvu.com')
    'livu' = @('livu.me', 'www.livu.me')
    'tango' = @('tango.me', 'www.tango.me')
    'blogspot' = @('blogspot.com', 'blogger.com')
    'radio-garden' = @('radio.garden', 'www.radio.garden')
    'deutsche-welle' = @('dw.com', 'www.dw.com')
    'voice-of-america' = @('voanews.com', 'www.voanews.com')
    'eksi-sozluk' = @('eksisozluk.com', 'www.eksisozluk.com')
    'grok' = @('grok.com', 'x.ai')
    'imgur' = @('imgur.com', 'www.imgur.com')
    'pastebin' = @('pastebin.com', 'www.pastebin.com')
}
$webTargetIds = @(
    'discord',
    'wattpad',
    'azar',
    'bigo-live',
    'imvu',
    'livu',
    'tango',
    'blogspot',
    'radio-garden',
    'deutsche-welle',
    'voice-of-america',
    'eksi-sozluk',
    'grok',
    'imgur',
    'pastebin'
)
$requiresWebProxy = @(
    $normalizedTargetIds |
        Where-Object { $webTargetIds -contains $_ }
).Count -gt 0
$requiresApplicationTunnelProof = @(
    $normalizedTargetIds |
        Where-Object { $targetAppPatterns.ContainsKey($_) }
).Count -gt 0
$requiresDiscord = $normalizedTargetIds -contains 'discord'

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Assert-Condition {
    param(
        [bool]$Condition,
        [string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Wait-Until {
    param(
        [scriptblock]$Condition,
        [int]$Seconds
    )

    $deadline = (Get-Date).AddSeconds($Seconds)
    do {
        if (& $Condition) {
            return $true
        }

        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)

    return $false
}

function Test-AstralHostsLock {
    if (-not (Test-Path -LiteralPath $hostsPath)) {
        return $false
    }

    $content = Get-Content -Raw -LiteralPath $hostsPath
    if ($null -eq $content) {
        return $false
    }

    $content = [string]$content
    if (-not $content.Contains($beginMarker)) {
        return $false
    }

    $expectedDomains = Get-ExpectedLockProbeDomains
    foreach ($domain in $expectedDomains) {
        if (-not $content.Contains('0.0.0.0 ' + $domain) -or
            -not $content.Contains('::1 ' + $domain)) {
            return $false
        }
    }

    return $true
}

function Get-ExpectedLockProbeDomains {
    $domains = New-Object System.Collections.Generic.List[string]
    foreach ($targetId in $normalizedTargetIds) {
        if (-not $targetLockProbeDomains.ContainsKey($targetId)) {
            continue
        }

        foreach ($domain in $targetLockProbeDomains[$targetId]) {
            [void]$domains.Add($domain)
        }
    }

    return @($domains | Select-Object -Unique)
}

function Get-AstralRuleEnabled {
    $rule = Get-NetFirewallRule -Name $ruleName -ErrorAction SilentlyContinue
    if ($null -eq $rule) {
        return $null
    }

    return [string]$rule.Enabled
}

function Test-Tcp443 {
    param([string]$HostName)

    $client = [Net.Sockets.TcpClient]::new()
    try {
        $task = $client.ConnectAsync($HostName, 443)
        if (-not $task.Wait(3000)) {
            return $false
        }

        return $client.Connected
    }
    catch {
        return $false
    }
    finally {
        $client.Dispose()
    }
}

$script:LastTcp443SuccessHost = ''
$script:LastTcp443FailedHosts = ''

function Test-AnyTcp443 {
    param([string[]]$HostNames)

    $failedHosts = New-Object System.Collections.Generic.List[string]
    foreach ($hostName in $HostNames) {
        if (Test-Tcp443 $hostName) {
            $script:LastTcp443SuccessHost = $hostName
            $script:LastTcp443FailedHosts = ($failedHosts -join ',')
            return $true
        }

        [void]$failedHosts.Add($hostName)
    }

    $script:LastTcp443SuccessHost = ''
    $script:LastTcp443FailedHosts = ($failedHosts -join ',')
    return $false
}

function Test-AllowedAppsPattern {
    param(
        [string]$AllowedAppsText,
        [string]$Pattern
    )

    return $AllowedAppsText -match "(?:^|[,=\s`"])(?:[^,`"]*\\)?$Pattern(?:[,`"\s]|$)"
}

function Get-ExpectedAppPatternSummary {
    $expected = New-Object System.Collections.Generic.List[string]
    foreach ($targetId in $normalizedTargetIds) {
        if (-not $targetAppPatterns.ContainsKey($targetId)) {
            continue
        }

        foreach ($pattern in $targetAppPatterns[$targetId]) {
            [void]$expected.Add($pattern)
        }
    }

    return @($expected)
}

function Get-UnexpectedAppPatternSummary {
    $unexpected = New-Object System.Collections.Generic.List[string]
    foreach ($targetId in $targetAppPatterns.Keys) {
        if ($normalizedTargetIds -contains $targetId) {
            continue
        }

        foreach ($pattern in $targetAppPatterns[$targetId]) {
            [void]$unexpected.Add($targetId + ':' + $pattern)
        }
    }

    return @($unexpected)
}

function Get-SelectedApplicationProcessStatus {
    $required = New-Object System.Collections.Generic.List[string]
    $running = New-Object System.Collections.Generic.List[string]
    $missing = New-Object System.Collections.Generic.List[string]
    $processKeys = New-Object System.Collections.Generic.List[string]

    foreach ($targetId in $normalizedTargetIds) {
        if (-not $targetProcessNames.ContainsKey($targetId)) {
            continue
        }

        $names = @($targetProcessNames[$targetId])
        [void]$required.Add($targetId + '=' + ($names -join '|'))
        $foundNames = New-Object System.Collections.Generic.List[string]
        foreach ($name in $names) {
            $processes = @(
                Get-Process -Name $name -ErrorAction SilentlyContinue
            )
            if ($processes.Count -eq 0) {
                continue
            }

            [void]$foundNames.Add($name)
            foreach ($process in $processes) {
                $startTicks = 'unknown'
                try {
                    $startTicks = [string]$process.StartTime.ToUniversalTime().Ticks
                }
                catch {
                }

                [void]$processKeys.Add(
                    ('{0}={1}:{2}:{3}' -f `
                        $targetId,
                        $process.ProcessName,
                        $process.Id,
                        $startTicks))
            }
        }

        if ($foundNames.Count -gt 0) {
            [void]$running.Add($targetId + '=' + ($foundNames -join '|'))
        }
        else {
            [void]$missing.Add($targetId + '=' + ($names -join '|'))
        }
    }

    return [pscustomobject]@{
        RequiredCount = $required.Count
        RequiredSummary = ($required -join ',')
        RunningSummary = ($running -join ',')
        MissingSummary = ($missing -join ',')
        ProcessKeys = @($processKeys)
        ProcessKeysSummary = ($processKeys -join ',')
        AllRequiredRunning = $missing.Count -eq 0
    }
}

function Copy-SelectedApplicationProcessStatus {
    param([object]$Status)

    if ($null -eq $Status) {
        $Status = Get-SelectedApplicationProcessStatus
    }

    $result.SelectedTargetProcessRequired =
        [int]$Status.RequiredCount -gt 0
    $result.SelectedTargetProcessRequiredNames =
        [string]$Status.RequiredSummary
    $result.SelectedTargetProcessRunningNames =
        [string]$Status.RunningSummary
    $result.SelectedTargetProcessMissingNames =
        [string]$Status.MissingSummary
    $result.SelectedTargetProcessKeys =
        [string]$Status.ProcessKeysSummary
    return [bool]$Status.AllRequiredRunning
}

function Set-AstralTargetSelection {
    Assert-Condition `
        (Test-Path -LiteralPath $settingsPath) `
        'Astral ayar dosyasi bulunamadi; once uygulamayi bir kez acin.'

    $settings = Get-Content -Raw -LiteralPath $settingsPath |
        ConvertFrom-Json
    Assert-Condition `
        ($settings.AcceptedWireSockVersion -eq '1.4.7.1') `
        'WireSock onayi yok; canli smoke testi onay ekranini gecemez.'
    Assert-Condition `
        ([bool]$settings.AcceptedCloudflareWarpTerms) `
        'Cloudflare WARP kosul onayi yok; canli smoke testi onay ekranini gecemez.'

    $targetSelection = [pscustomobject]@{
        SelectedTargetIds = @($normalizedTargetIds)
    }

    $settings | Add-Member `
        -Force `
        -NotePropertyName BrowserAccessEnabled `
        -NotePropertyValue $false
    $settings | Add-Member `
        -Force `
        -NotePropertyName BrowserAccessPreferenceVersion `
        -NotePropertyValue 1
    $settings | Add-Member `
        -Force `
        -NotePropertyName TargetSelectionPreferenceVersion `
        -NotePropertyValue 2
    $settings | Add-Member `
        -Force `
        -NotePropertyName TargetSelection `
        -NotePropertyValue $targetSelection

    $settings |
        ConvertTo-Json -Depth 8 |
        Set-Content -LiteralPath $settingsPath -Encoding UTF8

    $savedSettings = Get-Content -Raw -LiteralPath $settingsPath |
        ConvertFrom-Json
    $savedTargetIds = @(
        $savedSettings.TargetSelection.SelectedTargetIds |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
            ForEach-Object { $_.Trim().ToLowerInvariant() }
    )
    $expectedTargetIds = @($normalizedTargetIds)
    Assert-Condition `
        (($savedTargetIds -join ',') -eq ($expectedTargetIds -join ',')) `
        ('Astral hedef secimi ayar dosyasina dogru yazilamadi. Beklenen={0}; Kaydedilen={1}' -f `
            ($expectedTargetIds -join ','),
            ($savedTargetIds -join ','))
}

function Get-NewLogText {
    param([long]$Offset)

    if (-not (Test-Path -LiteralPath $logPath)) {
        return ''
    }

    $stream = [IO.File]::Open(
        $logPath,
        [IO.FileMode]::Open,
        [IO.FileAccess]::Read,
        [IO.FileShare]::ReadWrite)
    try {
        if ($stream.Length -le $Offset) {
            return ''
        }

        [void]$stream.Seek($Offset, [IO.SeekOrigin]::Begin)
        $reader = [IO.StreamReader]::new($stream, [Text.Encoding]::UTF8)
        try {
            return $reader.ReadToEnd()
        }
        finally {
            $reader.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }
}

function Get-CurrentHealth {
    param(
        [int]$ProcessId,
        [datetime]$NotBefore
    )

    $healthPath = Join-Path $env:LOCALAPPDATA 'Astral\logs\health.json'
    if (-not (Test-Path -LiteralPath $healthPath)) {
        return $null
    }

    try {
        $health = Get-Content -Raw -LiteralPath $healthPath |
            ConvertFrom-Json
        if ([int]$health.processId -ne $ProcessId) {
            return $null
        }

        [DateTimeOffset]$generatedAt = [DateTimeOffset]::MinValue
        if (-not [DateTimeOffset]::TryParse(
                [string]$health.generatedAt,
                [ref]$generatedAt)) {
            return $null
        }

        if ($generatedAt.LocalDateTime -lt $NotBefore.AddSeconds(-2)) {
            return $null
        }

        return $health
    }
    catch {
        return $null
    }
}

function Copy-HealthResult {
    param([object]$Health)

    if ($null -eq $Health) {
        return
    }

    $result.HealthGeneratedAt = [string]$Health.generatedAt
    $result.HealthStatus = [string]$Health.status
    $result.HealthProcessId = [string]$Health.processId
    if ($null -eq $Health.details) {
        return
    }

    $result.HealthTunnelReadiness =
        [string]$Health.details.tunnelReadiness
    $result.HealthWireSockAdapterDetected =
        [string]$Health.details.wireSockAdapterDetected
    $result.HealthWireSockAdapterUp =
        [string]$Health.details.wireSockAdapterUp
    $result.HealthWireSockAdapterStatus =
        [string]$Health.details.wireSockAdapterStatus
    $result.HealthWireSockAdapterBytesReceived =
        [string]$Health.details.wireSockAdapterBytesReceived
    $result.HealthWireSockAdapterBytesSent =
        [string]$Health.details.wireSockAdapterBytesSent
    $result.HealthWireSockConnectionEstablished =
        [string]$Health.details.wireSockConnectionEstablished
    $result.HealthWireSockMode =
        [string]$Health.details.wireSockMode
    $result.HealthWireSockProcessExited =
        [string]$Health.details.wireSockProcessExited
    $result.HealthWireSockProcessExitCode =
        [string]$Health.details.wireSockProcessExitCode
    $result.HealthWireSockHandshakeDiagnostic =
        [string]$Health.details.wireSockHandshakeDiagnostic
    $result.HealthWireSockTrafficDeltaObserved =
        [string]$Health.details.wireSockTrafficDeltaObserved
    $result.HealthWireSockTrafficDiagnostic =
        [string]$Health.details.wireSockTrafficDiagnostic
    $result.HealthWebProxyProofVerified =
        [string]$Health.details.'webProxyProof.verified'
    $result.HealthWebProxyProofHost =
        [string]$Health.details.'webProxyProof.host'
    $result.HealthWebProxyProofMessage =
        [string]$Health.details.'webProxyProof.message'
    $result.HealthWebProxyProofRequiredTargetCount =
        [string]$Health.details.'webProxyProof.requiredTargetCount'
    $result.HealthWebProxyProofVerifiedTargetCount =
        [string]$Health.details.'webProxyProof.verifiedTargetCount'
    $result.HealthWebProxyProofFailedTargets =
        [string]$Health.details.'webProxyProof.failedTargets'
    $result.HealthWebProxyProofElapsedMs =
        [string]$Health.details.'webProxyProof.elapsedMs'
    $result.HealthWebProxyProofVerifiedBool =
        (-not $requiresWebProxy) -or
        $result.HealthWebProxyProofVerified -eq 'True'
    $requiredTargetCount = 0
    $verifiedTargetCount = 0
    [void][int]::TryParse(
        $result.HealthWebProxyProofRequiredTargetCount,
        [ref]$requiredTargetCount)
    [void][int]::TryParse(
        $result.HealthWebProxyProofVerifiedTargetCount,
        [ref]$verifiedTargetCount)
    $result.HealthWebProxyProofCountsMatch =
        (-not $requiresWebProxy) -or
        (
            $requiredTargetCount -gt 0 -and
            $requiredTargetCount -eq $verifiedTargetCount
        )
    $result.HealthWebProxyProofHasNoFailedTargets =
        (-not $requiresWebProxy) -or
        [string]::IsNullOrWhiteSpace($result.HealthWebProxyProofFailedTargets)
    $result.HealthHasRequiredWebProxyProof =
        (-not $requiresWebProxy) -or
        (
            [bool]$result.HealthWebProxyProofVerifiedBool -and
            [bool]$result.HealthWebProxyProofCountsMatch -and
            [bool]$result.HealthWebProxyProofHasNoFailedTargets
        )
    $adapterReady =
        $result.HealthWireSockMode -eq 'virtual-adapter' -and
        $result.HealthTunnelReadiness -eq 'ready' -and
        $result.HealthWireSockAdapterDetected -eq 'True' -and
        $result.HealthWireSockAdapterUp -eq 'True' -and
        (
            $result.HealthWireSockConnectionEstablished -eq 'True' -or
            $result.HealthWireSockTrafficDeltaObserved -eq 'True'
        )
    $applicationProofReady =
        $result.HealthWireSockConnectionEstablished -eq 'True' -or
        $result.HealthWireSockTrafficDeltaObserved -eq 'True'
    $result.HealthHasRequiredApplicationProof =
        (-not $requiresApplicationTunnelProof) -or
        $applicationProofReady
    $result.HealthTunnelReady = $adapterReady
}

function Test-HealthTargetActionRequired {
    param([object]$Health)

    if ($null -eq $Health) {
        return $false
    }

    $status = ([string]$Health.status).ToLowerInvariant()
    $manualActionRequired = ''
    $operation = ''
    $proofRequired = ''
    $proofEnforced = ''
    if ($null -ne $Health.details) {
        $manualActionRequired =
            [string]$Health.details.'targetProcessRefresh.manualActionRequired'
        $operation = [string]$Health.details.operation
        $proofRequired =
            [string]$Health.details.applicationTunnelProofRequired
        $proofEnforced =
            [string]$Health.details.applicationTunnelProofEnforced
    }

    return $manualActionRequired -eq 'True' -or
        ($status.Contains('hedef') -and $status.Contains('aksiyon')) -or
        (
            $operation -eq 'target-action-recheck' -and
            $proofRequired -eq 'True' -and
            $proofEnforced -eq 'False'
        )
}

function Wait-AstralHealthState {
    param(
        [int]$ProcessId,
        [datetime]$NotBefore,
        [int]$Seconds,
        [bool]$AllowTargetActionRequired,
        [bool]$StrictNotBefore = $false
    )

    return Wait-Until {
        $script:currentHealth = Get-CurrentHealth `
            -ProcessId $ProcessId `
            -NotBefore $NotBefore
        if ($null -eq $script:currentHealth) {
            return $false
        }

        if ($StrictNotBefore) {
            [DateTimeOffset]$strictGeneratedAt = [DateTimeOffset]::MinValue
            if (-not [DateTimeOffset]::TryParse(
                    [string]$script:currentHealth.generatedAt,
                    [ref]$strictGeneratedAt)) {
                return $false
            }

            if ($strictGeneratedAt.LocalDateTime -lt $NotBefore) {
                return $false
            }
        }

        Copy-HealthResult -Health $script:currentHealth
        if (
            [bool]$result.HealthTunnelReady -and
            [bool]$result.HealthHasRequiredWebProxyProof
        ) {
            return $true
        }

        if (
            $AllowTargetActionRequired -and
            (Test-HealthTargetActionRequired -Health $script:currentHealth)
        ) {
            return $true
        }

        return [string]$script:currentHealth.status -eq 'hata'
    } $Seconds
}

function Stop-AstralProcess {
    param([System.Diagnostics.Process]$Process)

    if ($null -eq $Process -or $Process.HasExited) {
        return
    }

    [void]$Process.CloseMainWindow()
    [void]$Process.WaitForExit(15000)
    if (-not $Process.HasExited) {
        Stop-Process -Id $Process.Id -Force
        [void]$Process.WaitForExit(15000)
    }
}

function Get-WireSockProcess {
    param([datetime]$StartedAfter)

    @(Get-Process wiresock-client -ErrorAction SilentlyContinue |
        Where-Object {
            try {
                $_.StartTime -ge $StartedAfter.AddSeconds(-5)
            }
            catch {
                $true
            }
        })
}

function Find-AstralWindow {
    param([int]$ProcessId)

    $condition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty,
        $ProcessId)
    $windows = [System.Windows.Automation.AutomationElement]::RootElement.FindAll(
        [System.Windows.Automation.TreeScope]::Children,
        $condition)
    if ($null -eq $windows -or $windows.Count -eq 0) {
        return $null
    }

    $bestWindow = $null
    $bestArea = 0.0
    foreach ($candidate in $windows) {
        try {
            $rect = $candidate.Current.BoundingRectangle
            if ($rect.Width -le 0 -or $rect.Height -le 0) {
                continue
            }

            $area = [double]$rect.Width * [double]$rect.Height
            if ($area -gt $bestArea) {
                $bestWindow = $candidate
                $bestArea = $area
            }
        }
        catch {
        }
    }

    if ($null -ne $bestWindow) {
        return $bestWindow
    }

    return $windows[0]
}

function Find-AstralToggle {
    param([System.Windows.Automation.AutomationElement]$Window)

    $automationIdCondition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty,
        'TunnelToggleButton')
    $button = $Window.FindFirst(
        [System.Windows.Automation.TreeScope]::Descendants,
        $automationIdCondition)

    if ($null -ne $button) {
        return $button
    }

    $nameCondition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::NameProperty,
        'Seçili hedef bağlantısını aç veya kapat')
    $button = $Window.FindFirst(
        [System.Windows.Automation.TreeScope]::Descendants,
        $nameCondition)

    if ($null -ne $button) {
        return $button
    }

    return $null
}

function Find-AstralToggleForProcess {
    param([int]$ProcessId)

    $processCondition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty,
        $ProcessId)
    $automationIdCondition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty,
        'TunnelToggleButton')
    $condition = [System.Windows.Automation.AndCondition]::new(
        $processCondition,
        $automationIdCondition)

    return [System.Windows.Automation.AutomationElement]::RootElement.FindFirst(
        [System.Windows.Automation.TreeScope]::Descendants,
        $condition)
}

function Find-AstralTargetTestButton {
    param([System.Windows.Automation.AutomationElement]$Window)

    $automationIdCondition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty,
        'TargetTestButton')
    $button = $Window.FindFirst(
        [System.Windows.Automation.TreeScope]::Descendants,
        $automationIdCondition)

    if ($null -ne $button) {
        return $button
    }

    return $null
}

function Find-AstralTargetTestButtonForProcess {
    param([int]$ProcessId)

    $processCondition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty,
        $ProcessId)
    $automationIdCondition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty,
        'TargetTestButton')
    $condition = [System.Windows.Automation.AndCondition]::new(
        $processCondition,
        $automationIdCondition)

    return [System.Windows.Automation.AutomationElement]::RootElement.FindFirst(
        [System.Windows.Automation.TreeScope]::Descendants,
        $condition)
}

function Invoke-PrimaryWindowClick {
    param([System.Windows.Automation.AutomationElement]$Window)

    $rect = $Window.Current.BoundingRectangle
    Assert-Condition `
        ($rect.Width -gt 500 -and $rect.Height -gt 470) `
        'Pencere boyutu ana buton tiklamasi icin gecersiz.'

    [void][NativeWindow]::SetForegroundWindow($Window.Current.NativeWindowHandle)
    Start-Sleep -Milliseconds 250

    $x = [int]($rect.Left + ($rect.Width * 0.30))
    $y = [int]($rect.Top + ($rect.Height * 0.49))
    [void][NativeWindow]::SetCursorPos($x, $y)
    Start-Sleep -Milliseconds 100
    [NativeWindow]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 80
    [NativeWindow]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)
}

function Invoke-AutomationElementClick {
    param([System.Windows.Automation.AutomationElement]$Element)

    try {
        $togglePattern = $Element.GetCurrentPattern(
            [System.Windows.Automation.TogglePattern]::Pattern)
        if ($null -ne $togglePattern) {
            $togglePattern.Toggle()
            return
        }
    }
    catch {
    }

    try {
        $pattern = $Element.GetCurrentPattern(
            [System.Windows.Automation.InvokePattern]::Pattern)
        if ($null -ne $pattern) {
            $pattern.Invoke()
            return
        }
    }
    catch {
    }

    $rect = $Element.Current.BoundingRectangle
    Assert-Condition `
        ($rect.Width -gt 10 -and $rect.Height -gt 10) `
        'Ana buton boyutu tiklama icin gecersiz.'

    [void][NativeWindow]::SetForegroundWindow($Element.Current.NativeWindowHandle)
    Start-Sleep -Milliseconds 250

    $x = [int]($rect.Left + ($rect.Width / 2))
    $y = [int]($rect.Top + ($rect.Height / 2))
    [void][NativeWindow]::SetCursorPos($x, $y)
    Start-Sleep -Milliseconds 100
    [NativeWindow]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 80
    [NativeWindow]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)
}

function Invoke-AstralToggle {
    param([System.Windows.Automation.AutomationElement]$Window)

    $button = $null
    [void](Wait-Until {
        $script:toggleButton = Find-AstralToggle -Window $Window
        $null -ne $script:toggleButton
    } 20)
    $button = $script:toggleButton

    if ($null -eq $button) {
        $processId = [int]$Window.Current.ProcessId
        [void](Wait-Until {
            $script:toggleButton = Find-AstralToggleForProcess -ProcessId $processId
            $null -ne $script:toggleButton
        } 20)
        $button = $script:toggleButton
    }

    if ($null -eq $button) {
        Invoke-PrimaryWindowClick -Window $Window
        return
    }

    Invoke-AutomationElementClick -Element $button
}

function Invoke-AstralTargetTest {
    param([System.Windows.Automation.AutomationElement]$Window)

    $button = $null
    $script:targetTestButton = $null
    [void](Wait-Until {
        $script:targetTestButton = Find-AstralTargetTestButton -Window $Window
        $null -ne $script:targetTestButton -and
            $script:targetTestButton.Current.IsEnabled
    } 20)
    $button = $script:targetTestButton

    if ($null -eq $button) {
        $processId = [int]$Window.Current.ProcessId
        $script:targetTestButton = $null
        [void](Wait-Until {
            $script:targetTestButton =
                Find-AstralTargetTestButtonForProcess -ProcessId $processId
            $null -ne $script:targetTestButton -and
                $script:targetTestButton.Current.IsEnabled
        } 20)
        $button = $script:targetTestButton
    }

    if ($null -eq $button) {
        return $false
    }

    Invoke-AutomationElementClick -Element $button
    return $true
}

if (-not (Test-IsAdministrator)) {
    throw 'Bu script Windows yoneticisi olarak calistirilmalidir.'
}

Assert-Condition (Test-Path -LiteralPath $ExePath) "Exe bulunamadi: $ExePath"
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @"
using System;
using System.Runtime.InteropServices;

public static class NativeWindow
{
    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    public static extern void mouse_event(
        int flags,
        int dx,
        int dy,
        int data,
        UIntPtr extraInfo);
}
"@

$originalSettings = $null
if (Test-Path -LiteralPath $settingsPath) {
    $originalSettings = Get-Content -Raw -LiteralPath $settingsPath
}

$logOffset = 0
if (Test-Path -LiteralPath $logPath) {
    $logOffset = (Get-Item -LiteralPath $logPath).Length
}

$appProcess = $null
$startedAt = Get-Date
$result = [ordered]@{
    TargetIds = ($normalizedTargetIds -join ',')
    WindowFound = $false
    TargetSelectionPrepared = $false
    ConnectClicked = $false
    WireSockProcessSeen = $false
    WireSockStayedRunningAfterGrace = $false
    ConnectionEstablishedLog = $false
    HealthFresh = $false
    HealthStatus = ''
    HealthGeneratedAt = ''
    HealthProcessId = ''
    HealthTunnelReadiness = ''
    HealthWireSockAdapterDetected = ''
    HealthWireSockAdapterUp = ''
    HealthWireSockAdapterStatus = ''
    HealthWireSockAdapterBytesReceived = ''
    HealthWireSockAdapterBytesSent = ''
    HealthWireSockConnectionEstablished = ''
    HealthWireSockMode = ''
    HealthWireSockProcessExited = ''
    HealthWireSockProcessExitCode = ''
    HealthWireSockHandshakeDiagnostic = ''
    HealthWireSockTrafficDeltaObserved = ''
    HealthWireSockTrafficDiagnostic = ''
    HealthWebProxyProofVerified = ''
    HealthWebProxyProofHost = ''
    HealthWebProxyProofMessage = ''
    HealthWebProxyProofRequiredTargetCount = ''
    HealthWebProxyProofVerifiedTargetCount = ''
    HealthWebProxyProofFailedTargets = ''
    HealthWebProxyProofElapsedMs = ''
    HealthWebProxyProofVerifiedBool = -not $requiresWebProxy
    HealthWebProxyProofCountsMatch = -not $requiresWebProxy
    HealthWebProxyProofHasNoFailedTargets = -not $requiresWebProxy
    HealthHasRequiredWebProxyProof = -not $requiresWebProxy
    HealthHasRequiredApplicationProof = -not $requiresApplicationTunnelProof
    HealthTunnelReady = $false
    ManualTargetActionRecheckEnabled = [bool]$manualTargetActionRecheckEnabled
    RequireTargetActionRecheck = [bool]$RequireTargetActionRecheck
    ManualTargetActionTimeoutSeconds = $ManualTargetActionTimeoutSeconds
    SelectedTargetProcessRequired = $requiresApplicationTunnelProof
    SelectedTargetProcessRequiredNames = ''
    SelectedTargetProcessRunningNames = ''
    SelectedTargetProcessMissingNames = ''
    SelectedTargetProcessKeys = ''
    InitialSelectedTargetProcessRunning = -not $requiresApplicationTunnelProof
    InitialSelectedTargetProcessWasClosed = -not $requiresApplicationTunnelProof
    InitialSelectedTargetProcessKeys = ''
    TargetActionRequiredDetected = $false
    TargetActionProcessSeenBeforeRecheck = -not $requiresApplicationTunnelProof
    TargetActionNewProcessDetected = -not $requiresApplicationTunnelProof
    TargetActionNewProcessKeys = ''
    TargetActionRecheckButtonFound = $false
    TargetActionRecheckClicked = $false
    TargetActionRecheckHealthFresh = $false
    TargetActionRecheckPassed = $false
    TargetActionRecheckStatus = ''
    HostsLockRemovedWhileConnected = $false
    FirewallRuleDisabledWhileConnected = $false
    DirectDiscordTcp443WhileConnected = $false
    DirectNonTargetTcp443WhileConnected = $false
    DirectNonTargetTcp443Host = ''
    DirectNonTargetTcp443FailedHosts = ''
    ProfileRequiresDiscord = $requiresDiscord
    ProfileRequiresWebProxy = $requiresWebProxy
    ProfileHasPlainAllowedApps = $false
    ProfileHasPrefixedAllowedApps = $false
    ProfileUsesDirectAllowedApps = $false
    ProfileAllowedAppsDirectiveCount = 0
    ProfileActiveAllowedAppsDirectiveCount = 0
    ProfilePrefixedAllowedAppsDirectiveCount = 0
    ProfileHasDiscord = $false
    ProfileHasDiscordFullPath = $false
    ProfileHasRequiredDiscord = -not $requiresDiscord
    ProfileHasRequiredDiscordFullPath = -not $requiresDiscord
    ProfileHasWebProxy = $false
    ProfileHasRequiredWebProxy = -not $requiresWebProxy
    ProfileExpectedTargetApps = ''
    ProfileMissingExpectedApps = ''
    ProfileHasExpectedTargetApps = $false
    ProfileUnexpectedTargetApps = ''
    ProfileHasOnlySelectedTargetApps = $false
    ProfileHasBrowserProcess = $false
    ProfileHasBrowserFullPath = $false
    ProfileExcludesBrowserProcess = $false
    ProfileExcludesBrowserFullPath = $false
    DisconnectClicked = $false
    WireSockProcessStoppedAfterDisconnect = $false
    FinalHostsLock = $false
    FinalFirewallRuleEnabled = $false
    FinalTargetLockRestored = $false
    SettingsRestored = $false
    WindowTitle = ''
    WindowBounds = ''
    WindowCount = ''
    ErrorMessage = ''
    ErrorStackTrace = ''
    SmokePassed = $false
}

$smokeError = $null

try {
    try {
        Set-AstralTargetSelection
        $result.TargetSelectionPrepared = $true
        $initialProcessStatus = Get-SelectedApplicationProcessStatus
        $result.InitialSelectedTargetProcessRunning =
            Copy-SelectedApplicationProcessStatus -Status $initialProcessStatus
        $result.InitialSelectedTargetProcessWasClosed =
            -not [bool]$result.InitialSelectedTargetProcessRunning
        $result.InitialSelectedTargetProcessKeys =
            [string]$initialProcessStatus.ProcessKeysSummary
        $initialTargetProcessKeys = @($initialProcessStatus.ProcessKeys)

        $appProcess = Start-Process `
            -FilePath $ExePath `
            -WorkingDirectory (Split-Path -Parent $ExePath) `
            -PassThru

        $window = $null
        $result.WindowFound = Wait-Until {
            $script:window = Find-AstralWindow -ProcessId $appProcess.Id
            $null -ne $script:window
        } 30
        Assert-Condition $result.WindowFound 'Astral penceresi bulunamadi.'
        try {
            $rect = $window.Current.BoundingRectangle
            $result.WindowTitle = [string]$window.Current.Name
            $result.WindowBounds = (
                '{0},{1},{2},{3}' -f
                [int]$rect.Left,
                [int]$rect.Top,
                [int]$rect.Width,
                [int]$rect.Height)
            $processCondition = [System.Windows.Automation.PropertyCondition]::new(
                [System.Windows.Automation.AutomationElement]::ProcessIdProperty,
                $appProcess.Id)
            $result.WindowCount =
                [string]([System.Windows.Automation.AutomationElement]::RootElement.FindAll(
                    [System.Windows.Automation.TreeScope]::Children,
                    $processCondition).Count)
        }
        catch {
        }

        Invoke-AstralToggle -Window $window
        $result.ConnectClicked = $true

        $result.WireSockProcessSeen = Wait-Until {
            @(Get-WireSockProcess -StartedAfter $startedAt).Count -gt 0
        } 45

        Start-Sleep -Seconds 8
        $result.WireSockStayedRunningAfterGrace =
            @(Get-WireSockProcess -StartedAfter $startedAt).Count -gt 0
        if ([bool]$result.WireSockStayedRunningAfterGrace) {
            $result.WireSockProcessSeen = $true
        }

        $result.HostsLockRemovedWhileConnected = -not (Test-AstralHostsLock)
        $result.FirewallRuleDisabledWhileConnected =
            (Get-AstralRuleEnabled) -eq 'False'

        if (Test-Path -LiteralPath $profilePath) {
            $allowedAppsMatches = @(
                Select-String `
                -Path $profilePath `
                    -Pattern '^\s*(?:#@ws:)?AllowedApps\s*='
            )
            $allowedAppsLines = @(
                $allowedAppsMatches |
                    ForEach-Object { [string]$_.Line }
            )
            $activeAllowedAppsLines = @(
                $allowedAppsLines |
                    Where-Object { $_ -match '^\s*AllowedApps\s*=' }
            )
            $prefixedAllowedAppsLines = @(
                $allowedAppsLines |
                    Where-Object { $_ -match '^\s*#@ws:AllowedApps\s*=' }
            )
            $allowedAppsText = ($activeAllowedAppsLines -join ',')
            $allAllowedAppsText = ($allowedAppsLines -join ',')
            $result.ProfileAllowedAppsDirectiveCount =
                $allowedAppsLines.Count
            $result.ProfileActiveAllowedAppsDirectiveCount =
                $activeAllowedAppsLines.Count
            $result.ProfilePrefixedAllowedAppsDirectiveCount =
                $prefixedAllowedAppsLines.Count
            $result.ProfileHasPlainAllowedApps =
                $activeAllowedAppsLines.Count -gt 0
            $result.ProfileHasPrefixedAllowedApps =
                $prefixedAllowedAppsLines.Count -gt 0
            $result.ProfileUsesDirectAllowedApps =
                $activeAllowedAppsLines.Count -eq 1 -and
                $prefixedAllowedAppsLines.Count -eq 0
            $result.ProfileHasDiscord =
                $allAllowedAppsText -match 'Discord\.exe'
            $result.ProfileHasDiscordFullPath =
                $allAllowedAppsText -match '\\Discord(?:PTB|Canary|Development)?\\app-[^,\\]+\\Discord(?:PTB|Canary|Development)?\.exe'
            $result.ProfileHasRequiredDiscord =
                (-not $requiresDiscord) -or [bool]$result.ProfileHasDiscord
            $result.ProfileHasRequiredDiscordFullPath =
                (-not $requiresDiscord) -or [bool]$result.ProfileHasDiscordFullPath
            $result.ProfileHasWebProxy =
                $allAllowedAppsText -match 'Astral\.WebProxy\.exe'
            $result.ProfileHasRequiredWebProxy =
                (-not $requiresWebProxy) -or [bool]$result.ProfileHasWebProxy
            $expectedAppPatterns = @(Get-ExpectedAppPatternSummary)
            $result.ProfileExpectedTargetApps =
                ($expectedAppPatterns -join ',')
            $missingAppPatterns = @(
                $expectedAppPatterns |
                    Where-Object {
                        -not (Test-AllowedAppsPattern `
                            -AllowedAppsText $allowedAppsText `
                            -Pattern $_)
                    }
            )
            $result.ProfileMissingExpectedApps =
                ($missingAppPatterns -join ',')
            $result.ProfileHasExpectedTargetApps =
                $missingAppPatterns.Count -eq 0
            $unexpectedAppPatterns = @(
                Get-UnexpectedAppPatternSummary |
                    Where-Object {
                        $targetAndPattern = [string]$_
                        $separatorIndex = $targetAndPattern.IndexOf(':')
                        if ($separatorIndex -lt 0) {
                            $false
                        }
                        else {
                            $pattern = $targetAndPattern.Substring($separatorIndex + 1)
                            Test-AllowedAppsPattern `
                                -AllowedAppsText $allAllowedAppsText `
                                -Pattern $pattern
                        }
                    }
            )
            $result.ProfileUnexpectedTargetApps =
                ($unexpectedAppPatterns -join ',')
            $result.ProfileHasOnlySelectedTargetApps =
                $unexpectedAppPatterns.Count -eq 0
            $result.ProfileHasBrowserProcess =
                $allAllowedAppsText -match '(?:^|[,=\s"])(?:chrome|msedge|firefox|brave|opera|vivaldi)\.exe(?:[,="\s]|$)'
            $result.ProfileHasBrowserFullPath =
                $allAllowedAppsText -match '\\(?:BraveSoftware\\Brave-Browser\\Application\\brave|Google\\Chrome\\Application\\chrome|Microsoft\\Edge\\Application\\msedge|Mozilla Firefox\\firefox|Opera\\opera|Programs\\Opera\\opera|Programs\\Opera GX\\opera|Vivaldi\\Application\\vivaldi)\.exe'
            $result.ProfileExcludesBrowserProcess =
                -not [bool]$result.ProfileHasBrowserProcess
            $result.ProfileExcludesBrowserFullPath =
                -not [bool]$result.ProfileHasBrowserFullPath
        }

        $newLogText = Get-NewLogText -Offset $logOffset
        $result.ConnectionEstablishedLog =
            $result.HealthWireSockConnectionEstablished -eq 'True' -or
            $newLogText -match 'Connection established' -or
            $newLogText -match 'Wire[Gg]uard tunnel has been started\.'

        $currentHealth = $null
        $result.HealthFresh = Wait-AstralHealthState `
            -ProcessId $appProcess.Id `
            -NotBefore $startedAt `
            -Seconds $TimeoutSeconds `
            -AllowTargetActionRequired $manualTargetActionRecheckEnabled
        $health = $script:currentHealth
        if ($result.HealthFresh -and $null -ne $health) {
            Copy-HealthResult -Health $health
        }
        $result.TargetActionRequiredDetected =
            Test-HealthTargetActionRequired -Health $health

        if (
            [bool]$result.TargetActionRequiredDetected -and
            [bool]$manualTargetActionRecheckEnabled
        ) {
            Write-Host (
                'Hedef aksiyonu gerekli. Secili hedef uygulamasini acin; ' +
                'script Kontrol Et butonunu tetikleyecek.')

            $result.TargetActionProcessSeenBeforeRecheck = Wait-Until {
                $script:targetProcessStatus =
                    Get-SelectedApplicationProcessStatus
                $processReady = Copy-SelectedApplicationProcessStatus `
                    -Status $script:targetProcessStatus
                $currentTargetProcessKeys = @(
                    $script:targetProcessStatus.ProcessKeys
                )
                $newTargetProcessKeys = @(
                    $currentTargetProcessKeys |
                        Where-Object {
                            $initialTargetProcessKeys -notcontains $_
                        }
                )
                $result.TargetActionNewProcessDetected =
                    $newTargetProcessKeys.Count -gt 0
                $result.TargetActionNewProcessKeys =
                    ($newTargetProcessKeys -join ',')
                if ([bool]$RequireTargetActionRecheck) {
                    return [bool]$processReady -and
                        [bool]$result.TargetActionNewProcessDetected
                }

                return [bool]$processReady
            } $ManualTargetActionTimeoutSeconds

            if ([bool]$result.TargetActionProcessSeenBeforeRecheck) {
                $recheckStartedAt = Get-Date
                $result.TargetActionRecheckButtonFound =
                    Invoke-AstralTargetTest -Window $window
                $result.TargetActionRecheckClicked =
                    [bool]$result.TargetActionRecheckButtonFound

                if ([bool]$result.TargetActionRecheckClicked) {
                    $result.TargetActionRecheckHealthFresh =
                        Wait-AstralHealthState `
                            -ProcessId $appProcess.Id `
                            -NotBefore $recheckStartedAt `
                            -Seconds $TimeoutSeconds `
                            -AllowTargetActionRequired $true `
                            -StrictNotBefore $true
                    $health = $script:currentHealth
                    if ($result.TargetActionRecheckHealthFresh -and
                        $null -ne $health) {
                        Copy-HealthResult -Health $health
                        $result.TargetActionRecheckStatus =
                            [string]$health.status
                    }

                    $result.TargetActionRecheckPassed =
                        [bool]$result.HealthTunnelReady -and
                        [bool]$result.HealthHasRequiredWebProxyProof -and
                        [bool]$result.HealthHasRequiredApplicationProof
                }
            }
        }

        if ($result.HealthFresh) {
            $result.DirectDiscordTcp443WhileConnected =
                Test-Tcp443 'discord.com'
            $result.DirectNonTargetTcp443WhileConnected =
                Wait-Until {
                    Test-AnyTcp443 @(
                        'www.microsoft.com',
                        'example.com',
                        'cloudflare.com')
                } 15
            $result.DirectNonTargetTcp443Host =
                $script:LastTcp443SuccessHost
            $result.DirectNonTargetTcp443FailedHosts =
                $script:LastTcp443FailedHosts
        }

        if ($result.HealthTunnelReady) {
            Invoke-AstralToggle -Window $window
            $result.DisconnectClicked = $true
            $result.WireSockProcessStoppedAfterDisconnect = Wait-Until {
                @(Get-WireSockProcess -StartedAfter $startedAt).Count -eq 0
            } 45
        }
        else {
            $result.WireSockProcessStoppedAfterDisconnect =
                @(Get-WireSockProcess -StartedAfter $startedAt).Count -eq 0
        }

        Stop-AstralProcess -Process $appProcess

        $result.FinalHostsLock = Wait-Until { Test-AstralHostsLock } 20
        $result.FinalFirewallRuleEnabled = (Get-AstralRuleEnabled) -eq 'True'
        $result.FinalTargetLockRestored = [bool]$result.FinalHostsLock
    }
    finally {
        if ($null -ne $originalSettings) {
            Set-Content `
                -LiteralPath $settingsPath `
                -Value $originalSettings `
                -NoNewline `
                -Encoding UTF8
            $result.SettingsRestored = $true
        }

        Stop-AstralProcess -Process $appProcess

        if (-not [bool]$result.FinalHostsLock) {
            $result.FinalHostsLock = Wait-Until { Test-AstralHostsLock } 20
        }
        $result.FinalFirewallRuleEnabled = (Get-AstralRuleEnabled) -eq 'True'
        $result.FinalTargetLockRestored = [bool]$result.FinalHostsLock
    }
}
catch {
    $smokeError = $_
    $result.ErrorMessage = $_.Exception.Message
    $result.ErrorStackTrace = $_.ScriptStackTrace
}

$criticalChecks = @(
    'WindowFound',
    'TargetSelectionPrepared',
    'ConnectClicked',
    'WireSockProcessSeen',
    'WireSockStayedRunningAfterGrace',
    'HealthFresh',
    'HealthStatus',
    'HealthTunnelReady',
    'HealthWebProxyProofVerifiedBool',
    'HealthWebProxyProofCountsMatch',
    'HealthWebProxyProofHasNoFailedTargets',
    'HealthHasRequiredWebProxyProof',
    'HealthHasRequiredApplicationProof',
    'HostsLockRemovedWhileConnected',
    'FirewallRuleDisabledWhileConnected',
    'DirectNonTargetTcp443WhileConnected',
    'ProfileUsesDirectAllowedApps',
    'ProfileHasRequiredDiscord',
    'ProfileHasRequiredDiscordFullPath',
    'ProfileHasRequiredWebProxy',
    'ProfileHasExpectedTargetApps',
    'ProfileHasOnlySelectedTargetApps',
    'ProfileExcludesBrowserProcess',
    'ProfileExcludesBrowserFullPath',
    'DisconnectClicked',
    'WireSockProcessStoppedAfterDisconnect',
    'FinalTargetLockRestored',
    'SettingsRestored'
)

if ([bool]$RequireTargetActionRecheck) {
    $criticalChecks += @(
        'InitialSelectedTargetProcessWasClosed',
        'TargetActionRequiredDetected',
        'TargetActionProcessSeenBeforeRecheck',
        'TargetActionNewProcessDetected',
        'TargetActionRecheckButtonFound',
        'TargetActionRecheckClicked',
        'TargetActionRecheckHealthFresh',
        'TargetActionRecheckPassed'
    )
}

$result.SmokePassed =
    (($criticalChecks | Where-Object { -not [bool]$result[$_] }).Count -eq 0) -and
    [string]::IsNullOrWhiteSpace([string]$result.ErrorMessage)

$outputObject = [pscustomobject]$result
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $OutputPath) |
    Out-Null
$outputObject | Format-List | Out-String |
    Set-Content -LiteralPath $OutputPath -Encoding UTF8
$outputObject

if ($null -ne $smokeError) {
    throw $smokeError
}

foreach ($check in $criticalChecks) {
    Assert-Condition ([bool]$result[$check]) "Canli smoke testi basarisiz: $check"
}
