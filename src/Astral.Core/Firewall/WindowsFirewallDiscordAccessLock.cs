using Astral.Core.Configuration;
using Astral.Core.Infrastructure;

namespace Astral.Core.Firewall;

public sealed class WindowsFirewallDiscordAccessLock : IDiscordAccessLock
{
    public const string RuleName = "Astral.BlockDiscordDomains";
    public const string BrowserScopeGroup = "Astral.TunnelScope.Browsers";

    private static readonly string LegacyProductName = string.Concat("Dis", "corder");
    private static readonly string LegacyRuleName =
        LegacyProductName + ".BlockDiscordDomains";
    private static readonly string LegacyBrowserScopeGroup =
        LegacyProductName + ".TunnelScope.Browsers";
    private static readonly string LegacyBeginMarker =
        "# BEGIN " + LegacyProductName + " Discord kilidi";
    private static readonly string LegacyEndMarker =
        "# END " + LegacyProductName + " Discord kilidi";

    private const string DisplayName = "Astral VPN kilidi - Discord alan adları";
    private const string BrowserScopeDisplayName =
        "Astral tünel kapsamı - tarayıcı Discord engeli";
    private static readonly TimeSpan ScriptTimeout = TimeSpan.FromSeconds(60);
    private const string DiscordDomains =
        "'discord.com'," +
        "'ptb.discord.com'," +
        "'canary.discord.com'," +
        "'status.discord.com'," +
        "'support.discord.com'," +
        "'discordapp.com'," +
        "'discordapp.net'," +
        "'discord.gg'," +
        "'discord.gift'," +
        "'discord.media'," +
        "'discordstatus.com'," +
        "'discordcdn.com'," +
        "'cdn.discordapp.com'," +
        "'dl.discordapp.net'," +
        "'updates.discord.com'," +
        "'gateway.discord.gg'," +
        "'media.discordapp.net'," +
        "'images-ext-1.discordapp.net'," +
        "'images-ext-2.discordapp.net'";

    private readonly AppPaths _paths;
    private readonly ICommandRunner _commandRunner;
    private readonly string _powerShellPath;

    public WindowsFirewallDiscordAccessLock(
        AppPaths paths,
        ICommandRunner commandRunner,
        string? powerShellPath = null)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _commandRunner = commandRunner
            ?? throw new ArgumentNullException(nameof(commandRunner));
        _powerShellPath = string.IsNullOrWhiteSpace(powerShellPath)
            ? GetDefaultPowerShellPath()
            : powerShellPath;
    }

    public Task EnableAsync(CancellationToken cancellationToken)
    {
        return RunScriptAsync(BuildEnableScript(), cancellationToken);
    }

    public Task DisableAsync(CancellationToken cancellationToken)
    {
        return RunScriptAsync(BuildDisableScript(), cancellationToken);
    }

    public Task ApplyTunnelScopeAsync(
        bool includeBrowserAccess,
        CancellationToken cancellationToken)
    {
        return RunScriptAsync(
            BuildApplyTunnelScopeScript(includeBrowserAccess),
            cancellationToken);
    }

    public Task ClearTunnelScopeAsync(CancellationToken cancellationToken)
    {
        return RunScriptAsync(BuildClearTunnelScopeScript(), cancellationToken);
    }

    public Task RemoveAsync(CancellationToken cancellationToken)
    {
        return RunScriptAsync(BuildRemoveScript(), cancellationToken);
    }

    private async Task RunScriptAsync(
        string script,
        CancellationToken cancellationToken)
    {
        _paths.EnsureDirectories();

        var result = await _commandRunner.RunAsync(
            _powerShellPath,
            [
                "-NoProfile",
                "-NonInteractive",
                "-Command",
                script
            ],
            _paths.DataDirectory,
            ScriptTimeout,
            cancellationToken);

        if (result.Succeeded)
        {
            return;
        }

        var diagnostic = string.IsNullOrWhiteSpace(result.StandardError)
            ? result.StandardOutput
            : result.StandardError;
        if (string.IsNullOrWhiteSpace(diagnostic))
        {
            diagnostic = $"PowerShell exit code {result.ExitCode}.";
        }

        throw new InvalidOperationException(
            "Discord VPN kilidi güncellenemedi: " +
            diagnostic.Trim().ReplaceLineEndings(" "));
    }

    private static string BuildEnableScript()
    {
        return string.Join(Environment.NewLine, [
            "$ErrorActionPreference = 'Stop'",
            $"$domains = @({DiscordDomains})",
            $"$ruleName = '{RuleName}'",
            $"$legacyRuleName = '{LegacyRuleName}'",
            $"$displayName = '{DisplayName}'",
            "$hostsPath = Join-Path $env:SystemRoot 'System32\\drivers\\etc\\hosts'",
            "$beginMarker = '# BEGIN Astral Discord kilidi'",
            "$endMarker = '# END Astral Discord kilidi'",
            $"$legacyBeginMarker = '{LegacyBeginMarker}'",
            $"$legacyEndMarker = '{LegacyEndMarker}'",
            $"$browserScopeGroup = '{BrowserScopeGroup}'",
            $"$legacyBrowserScopeGroup = '{LegacyBrowserScopeGroup}'",
            .. BuildHostsFileFunctions(),
            .. BuildFirewallGroupFunctions(),
            "$script:astralHostsChanged = $false",
            "foreach ($group in @($browserScopeGroup, $legacyBrowserScopeGroup)) {",
            "    Clear-AstralFirewallGroup $group",
            "}",
            "Remove-AstralHostsLock",
            "Invoke-AstralFlushDns",
            "$resolvedAddresses = foreach ($domain in $domains) {",
            "    try {",
            "        Resolve-DnsName -Name $domain -ErrorAction Stop |",
            "            Where-Object { -not [string]::IsNullOrWhiteSpace($_.IPAddress) -and $_.IPAddress -ne '0.0.0.0' -and $_.IPAddress -ne '::1' -and $_.IPAddress -notlike '127.*' } |",
            "            ForEach-Object { $_.IPAddress }",
            "    } catch {",
            "    }",
            "}",
            "$addressList = @($resolvedAddresses | Sort-Object -Unique)",
            "if ($addressList.Count -gt 0) {",
            "    foreach ($name in @($ruleName, $legacyRuleName)) {",
            "        $rule = Get-NetFirewallRule -Name $name -ErrorAction SilentlyContinue",
            "        if ($null -ne $rule) {",
            "            Remove-NetFirewallRule -Name $name | Out-Null",
            "        }",
            "    }",
            "    New-NetFirewallRule -Name $ruleName -DisplayName $displayName -Direction Outbound -Action Block -RemoteAddress $addressList -Enabled True | Out-Null",
            "}",
            "Enable-AstralHostsLock"
        ]);
    }

    private static string BuildDisableScript()
    {
        return string.Join(Environment.NewLine, [
            "$ErrorActionPreference = 'Stop'",
            $"$ruleName = '{RuleName}'",
            $"$legacyRuleName = '{LegacyRuleName}'",
            "$hostsPath = Join-Path $env:SystemRoot 'System32\\drivers\\etc\\hosts'",
            "$beginMarker = '# BEGIN Astral Discord kilidi'",
            "$endMarker = '# END Astral Discord kilidi'",
            $"$legacyBeginMarker = '{LegacyBeginMarker}'",
            $"$legacyEndMarker = '{LegacyEndMarker}'",
            .. BuildHostsFileFunctions(),
            "$script:astralHostsChanged = $false",
            "Remove-AstralHostsLock",
            "if ($script:astralHostsChanged) {",
            "    Invoke-AstralFlushDns",
            "}",
            "foreach ($name in @($ruleName, $legacyRuleName)) {",
            "    $rule = Get-NetFirewallRule -Name $name -ErrorAction SilentlyContinue",
            "    if ($null -ne $rule) {",
            "        Set-NetFirewallRule -Name $name -Enabled False | Out-Null",
            "    }",
            "}"
        ]);
    }

    private static string BuildApplyTunnelScopeScript(bool includeBrowserAccess)
    {
        var includeBrowserAccessLiteral = includeBrowserAccess ? "$true" : "$false";
        return string.Join(Environment.NewLine, [
            "$ErrorActionPreference = 'Stop'",
            $"$includeBrowserAccess = {includeBrowserAccessLiteral}",
            $"$domains = @({DiscordDomains})",
            $"$browserScopeGroup = '{BrowserScopeGroup}'",
            $"$legacyBrowserScopeGroup = '{LegacyBrowserScopeGroup}'",
            $"$browserScopeDisplayName = '{BrowserScopeDisplayName}'",
            .. BuildFirewallGroupFunctions(),
            "function Join-AstralPath([string]$root, [string]$relative) {",
            "    if ([string]::IsNullOrWhiteSpace($root)) { return $null }",
            "    return (Join-Path $root $relative)",
            "}",
            "function Clear-AstralTunnelScope {",
            "    foreach ($group in @($browserScopeGroup, $legacyBrowserScopeGroup)) {",
            "        Clear-AstralFirewallGroup $group",
            "    }",
            "}",
            "function Get-AstralAddresses {",
            "    $resolvedAddresses = foreach ($domain in $domains) {",
            "        try {",
            "            Resolve-DnsName -Name $domain -ErrorAction Stop |",
            "                Where-Object { -not [string]::IsNullOrWhiteSpace($_.IPAddress) -and $_.IPAddress -ne '0.0.0.0' -and $_.IPAddress -ne '::1' -and $_.IPAddress -notlike '127.*' } |",
            "                ForEach-Object { $_.IPAddress }",
            "        } catch {",
            "        }",
            "    }",
            "    return @($resolvedAddresses | Sort-Object -Unique)",
            "}",
            "function Get-AstralBrowserPrograms {",
            "    $programFilesX86 = ${env:ProgramFiles(x86)}",
            "    $browserNames = @(",
            "        'brave.exe',",
            "        'chrome.exe',",
            "        'chromium.exe',",
            "        'firefox.exe',",
            "        'msedge.exe',",
            "        'opera.exe',",
            "        'vivaldi.exe'",
            "    )",
            "    $candidates = @(",
            "        (Join-AstralPath $env:ProgramFiles 'Google\\Chrome\\Application\\chrome.exe'),",
            "        (Join-AstralPath $programFilesX86 'Google\\Chrome\\Application\\chrome.exe'),",
            "        (Join-AstralPath $env:LOCALAPPDATA 'Google\\Chrome\\Application\\chrome.exe'),",
            "        (Join-AstralPath $env:ProgramFiles 'Chromium\\Application\\chromium.exe'),",
            "        (Join-AstralPath $programFilesX86 'Chromium\\Application\\chromium.exe'),",
            "        (Join-AstralPath $env:LOCALAPPDATA 'Chromium\\Application\\chromium.exe'),",
            "        (Join-AstralPath $env:ProgramFiles 'Microsoft\\Edge\\Application\\msedge.exe'),",
            "        (Join-AstralPath $programFilesX86 'Microsoft\\Edge\\Application\\msedge.exe'),",
            "        (Join-AstralPath $env:ProgramFiles 'BraveSoftware\\Brave-Browser\\Application\\brave.exe'),",
            "        (Join-AstralPath $programFilesX86 'BraveSoftware\\Brave-Browser\\Application\\brave.exe'),",
            "        (Join-AstralPath $env:LOCALAPPDATA 'BraveSoftware\\Brave-Browser\\Application\\brave.exe'),",
            "        (Join-AstralPath $env:ProgramFiles 'Mozilla Firefox\\firefox.exe'),",
            "        (Join-AstralPath $programFilesX86 'Mozilla Firefox\\firefox.exe'),",
            "        (Join-AstralPath $env:LOCALAPPDATA 'Programs\\Opera\\opera.exe'),",
            "        (Join-AstralPath $env:LOCALAPPDATA 'Programs\\Opera GX\\opera.exe'),",
            "        (Join-AstralPath $env:ProgramFiles 'Opera\\opera.exe'),",
            "        (Join-AstralPath $programFilesX86 'Opera\\opera.exe'),",
            "        (Join-AstralPath $env:LOCALAPPDATA 'Vivaldi\\Application\\vivaldi.exe'),",
            "        (Join-AstralPath $env:ProgramFiles 'Vivaldi\\Application\\vivaldi.exe'),",
            "        (Join-AstralPath $programFilesX86 'Vivaldi\\Application\\vivaldi.exe')",
            "    )",
            "    function Test-AstralBrowserProgram([string]$path) {",
            "        if ([string]::IsNullOrWhiteSpace($path)) { return $false }",
            "        try {",
            "            $fullPath = [IO.Path]::GetFullPath($path)",
            "            foreach ($candidate in $candidates) {",
            "                if ([string]::IsNullOrWhiteSpace($candidate)) { continue }",
            "                $candidateFullPath = [IO.Path]::GetFullPath($candidate)",
            "                if ([string]::Equals($fullPath, $candidateFullPath, [StringComparison]::OrdinalIgnoreCase)) {",
            "                    return $true",
            "                }",
            "            }",
            "        } catch {",
            "        }",
            "        return $false",
            "    }",
            "    $registryCandidates = foreach ($name in $browserNames) {",
            "        foreach ($root in @(",
            "            'HKLM:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\App Paths',",
            "            'HKLM:\\SOFTWARE\\WOW6432Node\\Microsoft\\Windows\\CurrentVersion\\App Paths'",
            "        )) {",
            "            $key = Join-Path $root $name",
            "            try {",
            "                $item = Get-Item -LiteralPath $key -ErrorAction Stop",
            "                $item.GetValue('')",
            "            } catch {",
            "            }",
            "        }",
            "    }",
            "    $allCandidates = @($candidates) + @($registryCandidates)",
            "    return @($allCandidates | Where-Object { (Test-AstralBrowserProgram $_) -and (Test-Path -LiteralPath $_) } | Sort-Object -Unique)",
            "}",
            "Clear-AstralTunnelScope",
            "if ($includeBrowserAccess) { return }",
            "$addressList = Get-AstralAddresses",
            "if ($addressList.Count -eq 0) { return }",
            "$programs = Get-AstralBrowserPrograms",
            "$index = 0",
            "foreach ($program in $programs) {",
            "    $index++",
            "    New-NetFirewallRule -Name ($browserScopeGroup + '.' + $index) -DisplayName ($browserScopeDisplayName + ' ' + $index) -Group $browserScopeGroup -Direction Outbound -Action Block -Program $program -RemoteAddress $addressList -Enabled True | Out-Null",
            "}"
        ]);
    }

    private static string BuildClearTunnelScopeScript()
    {
        return string.Join(Environment.NewLine, [
            "$ErrorActionPreference = 'Stop'",
            $"$browserScopeGroup = '{BrowserScopeGroup}'",
            $"$legacyBrowserScopeGroup = '{LegacyBrowserScopeGroup}'",
            .. BuildFirewallGroupFunctions(),
            "foreach ($group in @($browserScopeGroup, $legacyBrowserScopeGroup)) {",
            "    Clear-AstralFirewallGroup $group",
            "}"
        ]);
    }

    private static string BuildRemoveScript()
    {
        return string.Join(Environment.NewLine, [
            "$ErrorActionPreference = 'Stop'",
            $"$ruleName = '{RuleName}'",
            $"$legacyRuleName = '{LegacyRuleName}'",
            $"$browserScopeGroup = '{BrowserScopeGroup}'",
            $"$legacyBrowserScopeGroup = '{LegacyBrowserScopeGroup}'",
            "$hostsPath = Join-Path $env:SystemRoot 'System32\\drivers\\etc\\hosts'",
            "$beginMarker = '# BEGIN Astral Discord kilidi'",
            "$endMarker = '# END Astral Discord kilidi'",
            $"$legacyBeginMarker = '{LegacyBeginMarker}'",
            $"$legacyEndMarker = '{LegacyEndMarker}'",
            .. BuildHostsFileFunctions(),
            .. BuildFirewallGroupFunctions(),
            "$script:astralHostsChanged = $false",
            "Remove-AstralHostsLock",
            "foreach ($name in @($ruleName, $legacyRuleName)) {",
            "    $rule = Get-NetFirewallRule -Name $name -ErrorAction SilentlyContinue",
            "    if ($null -ne $rule) {",
            "        Remove-NetFirewallRule -Name $name | Out-Null",
            "    }",
            "}",
            "foreach ($group in @($browserScopeGroup, $legacyBrowserScopeGroup)) {",
            "    Clear-AstralFirewallGroup $group",
            "}",
            "if ($script:astralHostsChanged) {",
            "    Invoke-AstralFlushDns",
            "}"
        ]);
    }

    private static string[] BuildFirewallGroupFunctions()
    {
        return [
            "function Clear-AstralFirewallGroup([string]$group) {",
            "    $rules = @(Get-NetFirewallRule -Group $group -ErrorAction SilentlyContinue)",
            "    foreach ($rule in $rules) {",
            "        try {",
            "            Remove-NetFirewallRule -Name $($rule.Name) -ErrorAction Stop | Out-Null",
            "        } catch {",
            "            $remaining = Get-NetFirewallRule -Name $($rule.Name) -ErrorAction SilentlyContinue",
            "            if ($null -ne $remaining) { throw }",
            "        }",
            "    }",
            "}"
        ];
    }

    private static string[] BuildHostsFileFunctions()
    {
        return [
            "function Invoke-AstralRetry([scriptblock]$action) {",
            "    for ($attempt = 1; $attempt -le 8; $attempt++) {",
            "        try {",
            "            & $action",
            "            return",
            "        } catch {",
            "            if ($attempt -eq 8) { throw }",
            "            Start-Sleep -Milliseconds (120 * $attempt)",
            "        }",
            "    }",
            "}",
            "function Invoke-AstralFlushDns {",
            "    try {",
            "        & ipconfig /flushdns *> $null",
            "    } catch {",
            "    } finally {",
            "        $global:LASTEXITCODE = 0",
            "    }",
            "}",
            "function Read-AstralText([string]$path) {",
            "    if (-not [IO.File]::Exists($path)) { return '' }",
            "    return [IO.File]::ReadAllText($path, [Text.Encoding]::ASCII)",
            "}",
            "function Write-AstralText([string]$path, [string]$value) {",
            "    Invoke-AstralRetry { [IO.File]::WriteAllText($path, $value, [Text.Encoding]::ASCII) }",
            "}",
            "function Remove-AstralHostsBlock([string]$begin, [string]$end) {",
            "    if (-not [IO.File]::Exists($hostsPath)) { return }",
            "    $content = Read-AstralText $hostsPath",
            "    $pattern = '(?ms)^' + [regex]::Escape($begin) + '\\r?\\n.*?^' + [regex]::Escape($end) + '\\r?\\n?'",
            "    $updated = [regex]::Replace($content, $pattern, '')",
            "    if ($updated -ne $content) {",
            "        Write-AstralText $hostsPath $updated",
            "        $script:astralHostsChanged = $true",
            "    }",
            "}",
            "function Remove-AstralHostsLock {",
            "    Remove-AstralHostsBlock $beginMarker $endMarker",
            "    Remove-AstralHostsBlock $legacyBeginMarker $legacyEndMarker",
            "}",
            "function Enable-AstralHostsLock {",
            "    Remove-AstralHostsLock",
            "    $entries = foreach ($domain in $domains) {",
            "        '0.0.0.0 ' + $domain",
            "        '::1 ' + $domain",
            "    }",
            "    $block = @($beginMarker) + $entries + @($endMarker)",
            "    $content = Read-AstralText $hostsPath",
            "    if ($content.Length -gt 0 -and -not $content.EndsWith([Environment]::NewLine)) {",
            "        $content += [Environment]::NewLine",
            "    }",
            "    $content += ($block -join [Environment]::NewLine) + [Environment]::NewLine",
            "    Write-AstralText $hostsPath $content",
            "    $script:astralHostsChanged = $true",
            "    Invoke-AstralFlushDns",
            "}"
        ];
    }

    private static string GetDefaultPowerShellPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");
    }
}
