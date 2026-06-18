[CmdletBinding()]
param(
    [string]$ExePath,
    [int]$TimeoutSeconds = 90,
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($ExePath)) {
    $ExePath = Join-Path $root 'artifacts\publish\win-x64\Astral.exe'
}

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $root 'artifacts\app-live-connect-smoke.txt'
}

$settingsPath = Join-Path $env:LOCALAPPDATA 'Astral\settings.json'
$profilePath = Join-Path $env:ProgramData 'Astral\profiles\discord.conf'
$logPath = Join-Path $env:LOCALAPPDATA 'Astral\logs\tunnel.log'
$hostsPath = Join-Path $env:SystemRoot 'System32\drivers\etc\hosts'
$beginMarker = '# BEGIN Astral Discord kilidi'
$ruleName = 'Astral.BlockDiscordDomains'

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
    return $content.Contains($beginMarker) -and
        $content.Contains('0.0.0.0 discord.com') -and
        $content.Contains('::1 discord.com')
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
        SelectedTargetIds = @('discord')
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
        -NotePropertyValue 1
    $settings | Add-Member `
        -Force `
        -NotePropertyName TargetSelection `
        -NotePropertyValue $targetSelection

    $settings |
        ConvertTo-Json -Depth 8 |
        Set-Content -LiteralPath $settingsPath -Encoding UTF8
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
    $adapterReady =
        $result.HealthTunnelReadiness -eq 'ready' -and
        $result.HealthWireSockAdapterDetected -eq 'True' -and
        $result.HealthWireSockAdapterUp -eq 'True' -and
        (
            $result.HealthWireSockConnectionEstablished -eq 'True' -or
            $result.HealthWireSockTrafficDeltaObserved -eq 'True'
        )
    $transparentReady =
        $result.HealthTunnelReadiness -eq 'transparent-process-running' -and
        $result.HealthWireSockMode -eq 'transparent' -and
        (
            $result.HealthWireSockConnectionEstablished -eq 'True' -or
            $result.HealthWireSockTrafficDeltaObserved -eq 'True'
        ) -and
        $result.HealthWireSockProcessExited -ne 'True'
    $result.HealthTunnelReady = $adapterReady -or $transparentReady
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
    return [System.Windows.Automation.AutomationElement]::RootElement.FindFirst(
        [System.Windows.Automation.TreeScope]::Children,
        $condition)
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
        Invoke-PrimaryWindowClick -Window $Window
        return
    }

    Invoke-AutomationElementClick -Element $button
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
    HealthTunnelReady = $false
    HostsLockRemovedWhileConnected = $false
    FirewallRuleDisabledWhileConnected = $false
    DirectDiscordTcp443WhileConnected = $false
    DirectNonTargetTcp443WhileConnected = $false
    ProfileHasPlainAllowedApps = $false
    ProfileHasPrefixedAllowedApps = $false
    ProfileUsesDirectAllowedApps = $false
    ProfileHasDiscord = $false
    ProfileHasDiscordFullPath = $false
    ProfileHasWebProxy = $false
    ProfileHasBrowserProcess = $false
    ProfileHasBrowserFullPath = $false
    ProfileExcludesBrowserProcess = $false
    ProfileExcludesBrowserFullPath = $false
    DisconnectClicked = $false
    WireSockProcessStoppedAfterDisconnect = $false
    FinalHostsLock = $false
    FinalFirewallRuleEnabled = $false
    SettingsRestored = $false
    ErrorMessage = ''
    ErrorStackTrace = ''
    SmokePassed = $false
}

$smokeError = $null

try {
    try {
        Set-AstralTargetSelection
        $result.TargetSelectionPrepared = $true

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

        Invoke-AstralToggle -Window $window
        $result.ConnectClicked = $true

        $result.WireSockProcessSeen = Wait-Until {
            @(Get-WireSockProcess -StartedAfter $startedAt).Count -gt 0
        } 45

        Start-Sleep -Seconds 8
        $result.WireSockStayedRunningAfterGrace =
            @(Get-WireSockProcess -StartedAfter $startedAt).Count -gt 0

        $result.HostsLockRemovedWhileConnected = -not (Test-AstralHostsLock)
        $result.FirewallRuleDisabledWhileConnected =
            (Get-AstralRuleEnabled) -eq 'False'
        $result.DirectDiscordTcp443WhileConnected = Test-Tcp443 'discord.com'
        $result.DirectNonTargetTcp443WhileConnected = Test-Tcp443 'www.microsoft.com'

        if (Test-Path -LiteralPath $profilePath) {
            $allowedAppsLine = Select-String `
                -Path $profilePath `
                -Pattern '^(?:#@ws:)?AllowedApps\s*=' |
                Select-Object -First 1
            $allowedAppsText = [string]$allowedAppsLine.Line
            $result.ProfileHasPlainAllowedApps =
                $allowedAppsText -match '^AllowedApps\s*='
            $result.ProfileHasPrefixedAllowedApps =
                $allowedAppsText -match '^#@ws:AllowedApps\s*='
            $result.ProfileUsesDirectAllowedApps =
                [bool]$result.ProfileHasPlainAllowedApps -and
                -not [bool]$result.ProfileHasPrefixedAllowedApps
            $result.ProfileHasDiscord =
                $allowedAppsText -match 'Discord\.exe'
            $result.ProfileHasDiscordFullPath =
                $allowedAppsText -match '\\Discord(?:PTB|Canary|Development)?\\app-[^,\\]+\\Discord(?:PTB|Canary|Development)?\.exe'
            $result.ProfileHasWebProxy =
                $allowedAppsText -match 'Astral\.WebProxy\.exe'
            $result.ProfileHasBrowserProcess =
                $allowedAppsText -match '(?:^|[,=\s"])(?:chrome|msedge|firefox|brave|opera|vivaldi)\.exe(?:[,="\s]|$)'
            $result.ProfileHasBrowserFullPath =
                $allowedAppsText -match '\\(?:BraveSoftware\\Brave-Browser\\Application\\brave|Google\\Chrome\\Application\\chrome|Microsoft\\Edge\\Application\\msedge|Mozilla Firefox\\firefox|Opera\\opera|Programs\\Opera\\opera|Programs\\Opera GX\\opera|Vivaldi\\Application\\vivaldi)\.exe'
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
        $result.HealthFresh = Wait-Until {
            $script:currentHealth = Get-CurrentHealth `
                -ProcessId $appProcess.Id `
                -NotBefore $startedAt
            if ($null -eq $script:currentHealth) {
                return $false
            }

            Copy-HealthResult -Health $script:currentHealth
            return $result.HealthTunnelReady -or
                [string]$script:currentHealth.status -eq 'hata'
        } 75
        $health = $script:currentHealth
        if ($result.HealthFresh -and $null -ne $health) {
            Copy-HealthResult -Health $health
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

        $result.FinalHostsLock = Test-AstralHostsLock
        $result.FinalFirewallRuleEnabled = (Get-AstralRuleEnabled) -eq 'True'
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

        $result.FinalHostsLock = Test-AstralHostsLock
        $result.FinalFirewallRuleEnabled = (Get-AstralRuleEnabled) -eq 'True'
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
    'HostsLockRemovedWhileConnected',
    'FirewallRuleDisabledWhileConnected',
    'DirectNonTargetTcp443WhileConnected',
    'ProfileUsesDirectAllowedApps',
    'ProfileHasDiscord',
    'ProfileHasDiscordFullPath',
    'ProfileHasWebProxy',
    'ProfileExcludesBrowserProcess',
    'ProfileExcludesBrowserFullPath',
    'DisconnectClicked',
    'WireSockProcessStoppedAfterDisconnect',
    'FinalHostsLock',
    'FinalFirewallRuleEnabled',
    'SettingsRestored'
)

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
