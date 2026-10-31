<#
.SYNOPSIS
    Runs ONE unattended CoherentWarAI soak run: starts Bannerlord with the
    CoherentWarAI.TestHarness module on a brand-new campaign, waits for it to
    finish (or kills it on timeout), restores the user's config files and
    evaluates the resulting log with Check-CommandLog.ps1.

.DESCRIPTION
    Steps:
      1. Preconditions (nothing is modified if one fails): Bannerlord.exe not
         running, both modules deployed, no stale run.cfg, no leftover backups
         or markers from an interrupted earlier run, BannerlordConfig.txt
         present, neither config file read-only.
      2. Byte-copy backups (timestamped, next to the originals) of
         Configs\BannerlordConfig.txt and
         Configs\ModSettings\Global\CoherentWarAI\CoherentWarAI_v1.json.
         If the MCM JSON does not exist, an empty marker
         <json>.soakbak-<stamp>.absent records that instead.
      3. Sets StopGameOnFocusLost=False, AutoSaveInterval=-1,
         EnableTutorialHints=False in BannerlordConfig.txt and
         EnableChainOfCommand / EnableNavalOrders (true/true for WarSails and
         NoDlc, false/false for FeatureOff) plus EnableLogging=true in the MCM
         JSON. All reads and writes are explicit UTF-8; BOM and line endings of
         the originals are preserved. A missing MCM JSON is created (and
         deleted again on restore).
      4. Writes CoherentWarAI\harness\run.cfg, deletes stale harness\result.txt,
         harness\harness.log and the run-<mode>-seed<seed>.* copies of an
         earlier run with the same mode and seed, launches Bannerlord.exe directly (not via the
         launcher) with a minimal module list and waits. While waiting it polls
         the TaleWorlds watchdog log (C:\ProgramData\Mount and Blade II
         Bannerlord\logs\watchdog_log_<pid>.txt) once a second: on "Crash
         occurred" the crash dialog would wait forever, so the game is killed
         at once instead of at the timeout.
      5. finally (normal end, timeout, error, Ctrl+C): kills the game if it is
         still running, then stops (bounded wait) any other Bannerlord process
         started since the launch (e.g. a self-relaunch), deletes run.cfg,
         restores both files byte-identically from the backups (SHA-256
         compared) and prints a restore confirmation. Every step is isolated,
         so a failing step never skips the restore. If a game process is still
         alive after the kill, the files are restored anyway but the backups
         are KEPT (exit 6): close the game, then run -RestoreLeftovers.
      6. Copies CoherentWarAI.log, harness\result.txt and harness\harness.log to
         run-<mode>-seed<seed>.log / -result.txt / -harness.log in the
         CoherentWarAI log folder (only files written since the launch; older
         ones are reported as "not written by this run"), prints result.txt and
         runs Check-CommandLog.ps1 on the copied log. After a crash it also
         copies rgl_log_<pid>.txt, rgl_log_errors_<pid>.txt and
         watchdog_log_<pid>.txt of the crashed process to
         run-<mode>-seed<seed>-rgl_log.txt / -rgl_log_errors.txt /
         -watchdog_log.txt.

    If the script was terminated without its finally block (console closed,
    logoff, power loss), the config files stay modified and the backups stay
    next to them. Run the script with -RestoreLeftovers to restore them.
    -RestoreLeftovers does not remove an (empty) ModSettings\Global\CoherentWarAI
    folder that the interrupted run created - it cannot tell whether the folder
    existed before. An empty folder is harmless; delete it by hand if wanted.

    The script never touches LauncherData.xml or Game Saves.

    Exit codes:
      0  status=Completed AND Check-CommandLog.ps1 exited 0
         (-RestoreLeftovers: everything restored / nothing to restore)
      1  precondition / setup / unexpected error (see message), also an
         error after the launch (takes precedence over 2-5 and 7)
      2  launch failed - process exited within 30 s and harness.log never
         appeared (direct start / Steam?)
      3  hard timeout expired, game was killed
      4  game ended but no result.txt, or status is not Completed (Aborted...)
      5  status=Completed but Check-CommandLog.ps1 reported FAIL (non-zero)
      6  cleanup failed: a config file is NOT identical to its backup (the
         backup is kept - run -RestoreLeftovers), run.cfg could not be deleted
         or the game could not be stopped; takes precedence over 2-5 and 7
      7  game crashed: the TaleWorlds watchdog logged "Crash occurred"; the
         game was killed and its engine logs copied (see step 6). Takes
         precedence over 2-5. The crash dump itself is NOT written: the
         watchdog only writes it after the user confirms its dialog. See
         harness.log for the managed exception trail (FirstChance lines).

.PARAMETER Mode
    WarSails   - NavalDLC loaded, chain of command + naval orders on.
    NoDlc      - NavalDLC not loaded, chain of command + naval orders on.
    FeatureOff - NavalDLC loaded, chain of command + naval orders off.

.PARAMETER Seed
    Campaign seed (0 or greater). Default 1.

.PARAMETER Days
    In-game days to run (1..365). Default: 10 for WarSails, 3 otherwise.

.PARAMETER TimeoutMinutes
    Hard real-time limit in minutes; the game process tree is killed when it
    expires. Default: max(20 + 6 * Days, BudgetMinutes + 15) - i.e. always
    longer than the harness' own budget watchdog so the harness can abort
    cleanly first. An explicit value not above BudgetMinutes produces a warning.

.PARAMETER StallMinutes
    Written to run.cfg: real minutes without game-day progress before the
    harness aborts the run. Default 5 (HarnessRun API default).

.PARAMETER BudgetMinutes
    Written to run.cfg: total real-time budget of the harness. Default:
    max(60, 10 + 5 * Days). An explicit value below 10 + 5 * Days produces a
    warning (the harness would likely abort before reaching Days).

.PARAMETER GamePath
    Bannerlord install folder. Default: BannerlordPath from
    src\CoherentWarAI\paths.props.user if present, else auto-detect like
    src\CoherentWarAI\Directory.Build.props (Steam libraries on C:, D:, E:, F:).

.PARAMETER RestoreLeftovers
    Recovery mode, no game launch: restores BannerlordConfig.txt and the MCM
    JSON from leftover *.soakbak-* backups / markers of an interrupted run
    (with several stamps for one file the OLDEST is the true original and is
    used; the newer ones are deleted), verifies SHA-256, deletes the backups,
    removes a leftover run.cfg. Refuses (exit 1) while Bannerlord.exe runs.
    Exit 0 on success, 6 if anything could not be restored.

.EXAMPLE
    powershell -NoProfile -File tools\Run-SoakTest.ps1 -Mode WarSails -Seed 1 -Days 1 -TimeoutMinutes 20

.EXAMPLE
    powershell -NoProfile -File tools\Run-SoakTest.ps1 -RestoreLeftovers
#>

[CmdletBinding(DefaultParameterSetName = 'Run')]
param(
    [Parameter(Mandatory = $true, ParameterSetName = 'Run')]
    [ValidateSet('WarSails', 'NoDlc', 'FeatureOff')]
    [string]$Mode,

    [ValidateRange(0, 2147483647)]
    [int]$Seed = 1,

    [ValidateRange(0, 365)]
    [int]$Days = 0,

    [ValidateRange(0, 100000)]
    [int]$TimeoutMinutes = 0,

    [ValidateScript({ $_ -gt 0 })]
    [double]$StallMinutes = 5,

    # 0 = derive from Days (see help); an explicit value must be > 0.
    [ValidateScript({ $_ -gt 0 })]
    [double]$BudgetMinutes = 0,

    [string]$GamePath = '',

    [Parameter(Mandatory = $true, ParameterSetName = 'Restore')]
    [switch]$RestoreLeftovers
)

Set-StrictMode -Version 2
$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------------------
# Helper functions (no side effects at definition time; the dry-exercise check
# extracts them via the PowerShell AST, so keep them free of script-level state
# except where a parameter is passed in).
# ---------------------------------------------------------------------------

function Read-Utf8Text {
    # Strict UTF-8 (invalid bytes throw instead of becoming U+FFFD). A BOM is
    # consumed by ReadAllText, so the returned text never starts with U+FEFF.
    param([string]$Path)
    $enc = New-Object System.Text.UTF8Encoding($false, $true)
    return [System.IO.File]::ReadAllText($Path, $enc)
}

function Write-Utf8Text {
    param([string]$Path, [string]$Text, [bool]$WithBom)
    $enc = New-Object System.Text.UTF8Encoding($WithBom)
    [System.IO.File]::WriteAllText($Path, $Text, $enc)
}

function Test-Utf8Bom {
    param([string]$Path)
    $bytes = [System.IO.File]::ReadAllBytes($Path)
    return ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)
}

function Get-NewLine {
    # The line ending the text already uses (CRLF wins if present).
    param([string]$Text)
    if ($Text.Contains("`r`n")) { return "`r`n" }
    if ($Text.Contains("`n")) { return "`n" }
    return "`r`n"
}

function Set-ConfigKeyValue {
    # Sets "Key=Value" in a key=value text; adds the line if the key is missing.
    param([string]$Text, [string]$Key, [string]$Value)
    $pattern = '(?m)^[ \t]*' + [regex]::Escape($Key) + '[ \t]*=[^\r\n]*'
    $replacement = "$Key=$Value"
    if ([regex]::IsMatch($Text, $pattern)) {
        $evaluator = [System.Text.RegularExpressions.MatchEvaluator]{ param($m) $replacement }
        return [regex]::Replace($Text, $pattern, $evaluator)
    }
    $nl = Get-NewLine $Text
    if ($Text.Length -gt 0 -and -not $Text.EndsWith("`n")) { $Text += $nl }
    return $Text + $replacement + $nl
}

function Set-JsonBoolProperty {
    # Text-level edit of a top-level boolean JSON property so that everything
    # else in the file (formatting, number formats, escapes) stays untouched.
    param([string]$Text, [string]$Name, [bool]$Value)
    $literal = if ($Value) { 'true' } else { 'false' }
    $quoted = '"' + $Name + '"'
    $existing = '(' + [regex]::Escape($quoted) + '\s*:\s*)(true|false)(?![\w])'
    if ([regex]::IsMatch($Text, $existing)) {
        $evaluator = [System.Text.RegularExpressions.MatchEvaluator]{ param($m) $m.Groups[1].Value + $literal }
        return (New-Object System.Text.RegularExpressions.Regex($existing)).Replace($Text, $evaluator, 1)
    }
    if ([regex]::IsMatch($Text, [regex]::Escape($quoted) + '\s*:')) {
        throw "JSON property $Name exists but is not a boolean literal - refusing to guess."
    }
    $open = $Text.IndexOf('{')
    if ($open -lt 0) { throw 'MCM JSON has no opening brace.' }
    $nl = Get-NewLine $Text
    $rest = $Text.Substring($open + 1)
    $comma = if ($rest -match '^\s*\}') { '' } else { ',' }
    return $Text.Substring(0, $open + 1) + $nl + '  ' + $quoted + ': ' + $literal + $comma + $Text.Substring($open + 1)
}

function Assert-JsonBools {
    # Parses the JSON and checks that the expected boolean values are in effect.
    param([string]$Text, [hashtable]$Expected)
    $obj = $Text | ConvertFrom-Json
    foreach ($name in $Expected.Keys) {
        $prop = $obj.PSObject.Properties[$name]
        if ($null -eq $prop -or $prop.Value -ne $Expected[$name]) {
            throw "MCM JSON check failed for $name (expected $($Expected[$name]))."
        }
    }
}

function Get-Sha256 {
    param([string]$Path)
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
}

function New-UserFileBackup {
    # Byte copy next to the original, or - if the original does not exist - an
    # empty "<file>.soakbak-<stamp>.absent" marker, so that an interrupted run
    # still leaves evidence that the file must be deleted on restore.
    # Returns the state object used by Restore-UserFile.
    param([string]$Path, [string]$Stamp)
    $existed = Test-Path -LiteralPath $Path -PathType Leaf
    $state = [PSCustomObject]@{
        Path         = $Path
        Existed      = $existed
        BackupPath   = $null
        OriginalHash = $null
    }
    if ($existed) {
        $state.BackupPath = "$Path.soakbak-$Stamp"
        [System.IO.File]::Copy($Path, $state.BackupPath, $false)   # throws if it exists: never overwrite a foreign backup
        try {
            $state.OriginalHash = Get-Sha256 $Path
            if ((Get-Sha256 $state.BackupPath) -ne $state.OriginalHash) {
                throw "Backup of $Path does not match the original."
            }
        }
        catch {
            # The original is still untouched here; a bad backup left behind
            # would only block the next run as a "leftover".
            Remove-Item -LiteralPath $state.BackupPath -Force -ErrorAction SilentlyContinue
            throw
        }
    }
    else {
        $state.BackupPath = "$Path.soakbak-$Stamp.absent"
        ([System.IO.File]::Open($state.BackupPath, [System.IO.FileMode]::CreateNew)).Dispose()
    }
    return $state
}

function Restore-UserFile {
    # Restores byte-identically (or deletes a file that did not exist before)
    # and then deletes the backup / absent marker - unless -KeepBackup (e.g.
    # the game is still running and may overwrite the file again on exit).
    # Returns a result object; never throws.
    param($State, [switch]$KeepBackup)
    $result = [PSCustomObject]@{ Path = $State.Path; Ok = $false; Message = '' }
    try {
        if ($State.Existed) {
            $copied = $false
            for ($try = 1; $try -le 5 -and -not $copied; $try++) {
                try {
                    [System.IO.File]::Copy($State.BackupPath, $State.Path, $true)
                    $copied = $true
                }
                catch {
                    if ($try -eq 5) { throw }
                    Start-Sleep -Seconds 1
                }
            }
            $after = Get-Sha256 $State.Path
            if ($after -eq $State.OriginalHash) {
                if ($KeepBackup) {
                    $result.Message = "restored, SHA-256 identical ($after); backup kept: $($State.BackupPath)"
                }
                else {
                    Remove-Item -LiteralPath $State.BackupPath -Force
                    $result.Message = "restored, SHA-256 identical ($after)"
                }
                $result.Ok = $true
            }
            else {
                $result.Message = "HASH MISMATCH after restore (before $($State.OriginalHash), after $after); backup kept: $($State.BackupPath)"
            }
        }
        else {
            if (Test-Path -LiteralPath $State.Path) { Remove-Item -LiteralPath $State.Path -Force }
            if (Test-Path -LiteralPath $State.Path) {
                $result.Message = "could not remove created file; marker kept: $($State.BackupPath)"
            }
            else {
                if ($KeepBackup) {
                    $result.Message = "did not exist before; created file removed; marker kept: $($State.BackupPath)"
                }
                else {
                    if ($null -ne $State.BackupPath -and (Test-Path -LiteralPath $State.BackupPath)) {
                        Remove-Item -LiteralPath $State.BackupPath -Force
                    }
                    $result.Message = 'did not exist before; created file removed'
                }
                $result.Ok = $true
            }
        }
    }
    catch {
        $result.Message = "restore FAILED: $($_.Exception.Message); backup kept: $($State.BackupPath)"
    }
    return $result
}

function Get-LeftoverBackups {
    # Leftover "<file>.soakbak-<stamp>" backups and "<file>.soakbak-<stamp>.absent"
    # markers of one file, oldest stamp first (the stamp is fixed-width).
    param([string]$Path)
    $dir = Split-Path $Path
    $prefix = (Split-Path $Path -Leaf) + '.soakbak-'
    if (-not (Test-Path -LiteralPath $dir -PathType Container)) { return @() }
    $items = @(Get-ChildItem -LiteralPath $dir -File -Force |
        Where-Object { $_.Name.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase) })
    return @($items | Sort-Object { $_.Name.Substring($prefix.Length) -replace '\.absent$', '' })
}

function Restore-LeftoverFile {
    # Recovery after an interrupted run: restores one file from its OLDEST
    # leftover backup/marker (newer stamps can only hold already-patched
    # states). Order: delete the newer leftovers first (on failure abort
    # without touching the file), then restore from the oldest, which
    # Restore-UserFile deletes last, after the verification.
    # Returns a result object with messages; never throws.
    param([string]$Path)
    $result = [PSCustomObject]@{ Path = $Path; Ok = $true; Messages = @() }
    try {
        $leftovers = @(Get-LeftoverBackups $Path)
        if ($leftovers.Count -eq 0) {
            $result.Messages += 'no leftover backup, nothing to restore'
            return $result
        }
        $oldest = $leftovers[0]
        if ($leftovers.Count -gt 1) {
            $result.Messages += "$($leftovers.Count) leftover stamps found; restoring from the OLDEST: $($oldest.Name)"
        }
        for ($i = 1; $i -lt $leftovers.Count; $i++) {
            try {
                Remove-Item -LiteralPath $leftovers[$i].FullName -Force
            }
            catch {
                $result.Ok = $false
                $result.Messages += "could not delete newer leftover $($leftovers[$i].Name) ($($_.Exception.Message)); aborted, file NOT restored"
                return $result
            }
            $result.Messages += "deleted newer leftover: $($leftovers[$i].Name)"
        }
        $isMarker = $oldest.Name.EndsWith('.absent', [System.StringComparison]::OrdinalIgnoreCase)
        $state = [PSCustomObject]@{
            Path         = $Path
            Existed      = -not $isMarker
            BackupPath   = $oldest.FullName
            OriginalHash = $(if ($isMarker) { $null } else { Get-Sha256 $oldest.FullName })
        }
        $r = Restore-UserFile $state
        $result.Ok = $r.Ok
        $result.Messages += "from $($oldest.Name): $($r.Message)"
    }
    catch {
        $result.Ok = $false
        $result.Messages += "restore FAILED: $($_.Exception.Message)"
    }
    return $result
}

function Set-BannerlordConfigValues {
    param([string]$Path)
    $bom = Test-Utf8Bom $Path
    $text = Read-Utf8Text $Path
    $text = Set-ConfigKeyValue $text 'StopGameOnFocusLost' 'False'
    $text = Set-ConfigKeyValue $text 'AutoSaveInterval' '-1'
    $text = Set-ConfigKeyValue $text 'EnableTutorialHints' 'False'
    Write-Utf8Text $Path $text $bom
}

function Set-McmSettingsValues {
    # Creates the file with only the three keys if it does not exist.
    param([string]$Path, [bool]$FeatureOn)
    $expected = @{
        EnableChainOfCommand = $FeatureOn
        EnableNavalOrders    = $FeatureOn
        EnableLogging        = $true
    }
    if (Test-Path -LiteralPath $Path -PathType Leaf) {
        $bom = Test-Utf8Bom $Path
        $text = Read-Utf8Text $Path
    }
    else {
        $bom = $false
        $text = "{`r`n}`r`n"
    }
    foreach ($name in @('EnableChainOfCommand', 'EnableNavalOrders', 'EnableLogging')) {
        $text = Set-JsonBoolProperty $text $name $expected[$name]
    }
    Assert-JsonBools $text $expected   # never write invalid JSON: MCM would overwrite the file with defaults
    Write-Utf8Text $Path $text $bom
}

function Find-GamePath {
    # Mirrors src\CoherentWarAI\Directory.Build.props: paths.props.user first,
    # then the Steam library candidates (later candidates win, as in MSBuild).
    param([string]$PropsUserPath)
    if (Test-Path -LiteralPath $PropsUserPath) {
        try {
            $xml = New-Object System.Xml.XmlDocument
            $xml.Load($PropsUserPath)
            $node = $xml.SelectSingleNode('//*[local-name()="BannerlordPath"]')
            if ($null -ne $node -and $node.InnerText.Trim() -ne '') { return $node.InnerText.Trim() }
        }
        catch { }
    }
    $found = ''
    foreach ($root in @('C:\Program Files (x86)\Steam', 'D:\SteamLibrary', 'E:\SteamLibrary', 'F:\SteamLibrary')) {
        # [IO.Path]::Combine, not Join-Path: Join-Path throws for a drive that does not exist.
        $candidate = [System.IO.Path]::Combine($root, 'steamapps\common\Mount & Blade II Bannerlord')
        if (Test-Path -LiteralPath $candidate) { $found = $candidate }
    }
    return $found
}

function Invoke-TaskKillTree {
    # "taskkill /PID <id> /T /F" through System.Diagnostics.Process, so its
    # stderr ("process not found") never becomes a PowerShell error record
    # (with $ErrorActionPreference = 'Stop' a redirected native stderr line is
    # terminating in Windows PowerShell 5.1). The streams are deliberately NOT
    # redirected: reading them would block until taskkill exits and defeat the
    # 15 s bound; its output just goes to the console. Returns taskkill's exit
    # code, or -1 if it could not run / did not finish. Never throws.
    param([int]$ProcessId)
    try {
        $psi = New-Object System.Diagnostics.ProcessStartInfo
        $psi.FileName = 'taskkill.exe'
        $psi.Arguments = "/PID $ProcessId /T /F"
        $psi.UseShellExecute = $false
        $psi.CreateNoWindow = $true
        $tk = [System.Diagnostics.Process]::Start($psi)
        if (-not $tk.WaitForExit(15000)) { return -1 }
        return $tk.ExitCode
    }
    catch {
        return -1
    }
}

function Stop-ProcessTree {
    # Kills the process and its children and waits for the exit. Never throws
    # (a throw here would skip the config restore in finally). Returns $true if
    # the process is gone afterwards. taskkill /T runs first because
    # Process.Kill() ends only the root and would orphan child processes;
    # Kill() is the fallback if taskkill did not end it.
    param([System.Diagnostics.Process]$Process, [int]$WaitMilliseconds = 15000)
    $ErrorActionPreference = 'Continue'
    if ($null -eq $Process) { return $true }
    try {
        if ($Process.HasExited) { return $true }
        [void](Invoke-TaskKillTree $Process.Id)   # "not found" (already exited) is fine: checked below
        if (-not $Process.WaitForExit($WaitMilliseconds)) {
            try { $Process.Kill() } catch { }      # already exited or access denied: checked below
            [void]$Process.WaitForExit($WaitMilliseconds)
        }
        return $Process.HasExited
    }
    catch {
        return $false
    }
}

function Stop-ProcessesByName {
    # Stops every process with this name and waits up to $WaitSeconds until
    # none is left. Never throws; returns $true if none is left.
    param([string]$Name, [int]$WaitSeconds = 30)
    $ErrorActionPreference = 'Continue'
    try {
        $deadline = (Get-Date).AddSeconds($WaitSeconds)
        while ($true) {
            $left = @(Get-Process -Name $Name -ErrorAction SilentlyContinue)
            if ($left.Count -eq 0) { return $true }
            if ((Get-Date) -ge $deadline) { return $false }
            foreach ($p in $left) { [void](Stop-ProcessTree $p 5000) }
            Start-Sleep -Milliseconds 500
        }
    }
    catch {
        return $false
    }
}

function Invoke-RunCleanup {
    # Body of the main finally block. Every step has its own try/catch, so no
    # failing step can skip the restore of the user's files. Never throws;
    # returns the failures (nothing = all clean).
    param(
        [System.Diagnostics.Process]$Process,
        [bool]$LaunchAttempted,
        [string]$GameProcessName,
        [bool]$RunCfgWritten,
        [string]$RunCfgPath,
        [object[]]$States,
        [bool]$CreatedMcmDir,
        [string]$McmDir
    )
    $failures = @()

    try {
        if ($null -ne $Process -and -not $Process.HasExited) {
            Write-Host 'Game still running - killing it before restoring the configs.'
            if (-not (Stop-ProcessTree $Process)) { $failures += 'the game process did not exit after the kill' }
        }
    }
    catch { $failures += "killing the game failed: $($_.Exception.Message)" }

    try {
        # The preconditions guaranteed that no such process ran before the
        # launch, so any one now belongs to this run (Ctrl+C before the process
        # object was assigned, or a self-relaunch, e.g. by Steam).
        if ($LaunchAttempted -and @(Get-Process -Name $GameProcessName -ErrorAction SilentlyContinue).Count -gt 0) {
            Write-Host "Another $GameProcessName process is running - stopping it before restoring the configs."
            if (-not (Stop-ProcessesByName $GameProcessName 30)) {
                $failures += "could not stop all $GameProcessName processes within 30 s"
            }
        }
    }
    catch { $failures += "stopping $GameProcessName processes failed: $($_.Exception.Message)" }

    try {
        if ($RunCfgWritten -and (Test-Path -LiteralPath $RunCfgPath)) {
            Remove-Item -LiteralPath $RunCfgPath -Force
        }
    }
    catch { $failures += "deleting run.cfg failed: $($_.Exception.Message)" }

    # A game that survived the kill may write the configs again when it exits:
    # restore anyway, but keep the backups for a later -RestoreLeftovers.
    $gameAlive = $false
    try {
        $gameAlive = ($null -ne $Process -and -not $Process.HasExited) -or
            ($LaunchAttempted -and @(Get-Process -Name $GameProcessName -ErrorAction SilentlyContinue).Count -gt 0)
    }
    catch { $gameAlive = $true }   # unknown: assume the worst
    if ($gameAlive) {
        $failures += "the game is still running - configs restored but backups kept: close the game, then run -RestoreLeftovers"
    }

    if ($States.Count -gt 0) { Write-Host '--- Restore ---' }
    foreach ($s in $States) {
        try {
            $r = Restore-UserFile $s -KeepBackup:$gameAlive
            if (-not $r.Ok) { $failures += "restore of $($r.Path): $($r.Message)" }
            Write-Host ("{0}: {1}" -f (Split-Path $r.Path -Leaf), $r.Message)
        }
        catch { $failures += "restore of $($s.Path) failed: $($_.Exception.Message)" }
    }

    try {
        if ($CreatedMcmDir -and (Test-Path -LiteralPath $McmDir) -and @(Get-ChildItem -LiteralPath $McmDir -Force).Count -eq 0) {
            Remove-Item -LiteralPath $McmDir -Force
        }
    }
    catch { Write-Host "WARNING: could not remove the created folder $McmDir`: $($_.Exception.Message)" }

    try {
        if ($RunCfgWritten) { Write-Host ("run.cfg removed: {0}" -f (-not (Test-Path -LiteralPath $RunCfgPath))) }
    }
    catch { }

    return $failures
}

function Test-WatchdogCrash {
    # True if the TaleWorlds watchdog log at $Path reports a crash. The
    # watchdog keeps the file open, hence FileShare.ReadWrite. Never throws.
    param([string]$Path)
    try {
        if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $false }
        $share = [System.IO.FileShare]::ReadWrite -bor [System.IO.FileShare]::Delete
        $fs = New-Object System.IO.FileStream($Path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, $share)
        try {
            $text = (New-Object System.IO.StreamReader($fs)).ReadToEnd()
        }
        finally {
            $fs.Dispose()
        }
        return $text.Contains('Crash occurred')
    }
    catch {
        return $false
    }
}

function Find-CrashedWatchdogLog {
    # The watchdog log reporting "Crash occurred" for this run: first the one
    # of the launched process (watchdog_log_<pid>.txt, same PID as
    # rgl_log_<pid>.txt), else - after a relaunch under another PID - any
    # watchdog log written since $Since. Returns its FileInfo or $null.
    # Never throws.
    param([string]$LogsDir, [int]$ProcessId, $Since)
    try {
        $own = Join-Path $LogsDir "watchdog_log_$ProcessId.txt"
        if (Test-WatchdogCrash $own) { return (Get-Item -LiteralPath $own) }
        if ($null -eq $Since -or -not (Test-Path -LiteralPath $LogsDir -PathType Container)) { return $null }
        $recent = @(Get-ChildItem -LiteralPath $LogsDir -Filter 'watchdog_log_*.txt' -File |
            Where-Object { $_.LastWriteTime -ge $Since } | Sort-Object LastWriteTime -Descending)
        foreach ($f in $recent) {
            if (Test-WatchdogCrash $f.FullName) { return $f }
        }
    }
    catch { }
    return $null
}

function Get-EngineLogCopies {
    # Copy list (for Copy-RunOutput) of the engine logs of one game process.
    param([string]$LogsDir, [string]$ProcessIdText, [string]$TargetPrefix)
    return @(
        @{ Source = (Join-Path $LogsDir "rgl_log_$ProcessIdText.txt"); Target = "$TargetPrefix-rgl_log.txt" },
        @{ Source = (Join-Path $LogsDir "rgl_log_errors_$ProcessIdText.txt"); Target = "$TargetPrefix-rgl_log_errors.txt" },
        @{ Source = (Join-Path $LogsDir "watchdog_log_$ProcessIdText.txt"); Target = "$TargetPrefix-watchdog_log.txt" }
    )
}

function Copy-RunOutput {
    # Copies each Source to Target if it was written since $StartTime (an older
    # file, e.g. CoherentWarAI.log of an earlier normal session after a failed
    # launch, is reported and skipped). A locked file only produces a warning.
    # Never throws; returns the targets that were written.
    param([hashtable[]]$Copies, $StartTime)
    $copied = @()
    foreach ($c in $Copies) {
        try {
            if (-not (Test-Path -LiteralPath $c.Source)) {
                Write-Host "WARNING: not found, not copied: $($c.Source)"
                continue
            }
            $written = (Get-Item -LiteralPath $c.Source).LastWriteTime
            if ($null -eq $StartTime -or $written -lt $StartTime) {
                Write-Host "WARNING: not written by this run (last write $written), not copied: $($c.Source)"
                continue
            }
            Copy-Item -LiteralPath $c.Source -Destination $c.Target -Force
            Write-Host "Saved $($c.Target)"
            $copied += $c.Target
        }
        catch {
            Write-Host "WARNING: could not copy $($c.Source): $($_.Exception.Message)"
        }
    }
    return $copied
}

# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------

$docs = Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'Mount and Blade II Bannerlord'
$cfgPath = Join-Path $docs 'Configs\BannerlordConfig.txt'
$mcmPath = Join-Path $docs 'Configs\ModSettings\Global\CoherentWarAI\CoherentWarAI_v1.json'
$logDir = Join-Path $docs 'CoherentWarAI'
$harnessDir = Join-Path $logDir 'harness'
$runCfgPath = Join-Path $harnessDir 'run.cfg'
$resultPath = Join-Path $harnessDir 'result.txt'
$harnessLogPath = Join-Path $harnessDir 'harness.log'
$modLogPath = Join-Path $logDir 'CoherentWarAI.log'
# Engine + watchdog logs (C:\ProgramData\Mount and Blade II Bannerlord\logs).
$engineLogDir = Join-Path ([Environment]::GetFolderPath('CommonApplicationData')) 'Mount and Blade II Bannerlord\logs'
$restoreHint = 'Run "powershell -NoProfile -File tools\Run-SoakTest.ps1 -RestoreLeftovers" to restore them.'

if ($RestoreLeftovers) {
    if (@(Get-Process -Name 'Bannerlord' -ErrorAction SilentlyContinue).Count -gt 0) {
        Write-Host 'ERROR: Bannerlord.exe is running. Close it first (it would overwrite the restored config files).'
        exit 1
    }
    Write-Host '=== Restore leftovers of an interrupted soak run ==='
    $leftoverFailed = $false
    foreach ($path in @($cfgPath, $mcmPath)) {
        $r = Restore-LeftoverFile $path
        if (-not $r.Ok) { $leftoverFailed = $true }
        foreach ($m in $r.Messages) { Write-Host ("{0}: {1}" -f (Split-Path $path -Leaf), $m) }
    }
    try {
        if (Test-Path -LiteralPath $runCfgPath) {
            Remove-Item -LiteralPath $runCfgPath -Force
            Write-Host "Removed leftover run.cfg: $runCfgPath"
        }
        else {
            Write-Host 'No leftover run.cfg.'
        }
    }
    catch {
        $leftoverFailed = $true
        Write-Host "ERROR: could not delete $runCfgPath`: $($_.Exception.Message)"
    }
    $leftoverExit = if ($leftoverFailed) { 6 } else { 0 }
    Write-Host "Exit code: $leftoverExit"
    exit $leftoverExit
}

if ($Days -eq 0) { $Days = if ($Mode -eq 'WarSails') { 10 } else { 3 } }
$budgetExplicit = $PSBoundParameters.ContainsKey('BudgetMinutes')
$budgetEstimate = 10 + 5 * $Days
if (-not $budgetExplicit) { $BudgetMinutes = [Math]::Max(60, $budgetEstimate) }
if ($TimeoutMinutes -eq 0) {
    $TimeoutMinutes = [int][Math]::Ceiling([Math]::Max(20 + 6 * $Days, $BudgetMinutes + 15))
}

$inv = [System.Globalization.CultureInfo]::InvariantCulture
$featureOn = ($Mode -ne 'FeatureOff')

if ($GamePath -eq '') {
    $GamePath = Find-GamePath (Join-Path $PSScriptRoot '..\src\CoherentWarAI\paths.props.user')
}
$prefix = "run-$Mode-seed$Seed"
$checker = Join-Path $PSScriptRoot 'Check-CommandLog.ps1'
# Output copies owned by this script; deleted before the launch so that a
# crashed run can never leave an older run's files looking current.
$copiedLog = Join-Path $logDir "$prefix.log"
$copies = @(
    @{ Source = $modLogPath; Target = $copiedLog },
    @{ Source = $resultPath; Target = (Join-Path $logDir "$prefix-result.txt") },
    @{ Source = $harnessLogPath; Target = (Join-Path $logDir "$prefix-harness.log") }
)
# Engine log copies after a crash; the PID is only known then, the targets are fixed.
$engineCopyPrefix = Join-Path $logDir $prefix
$engineCopyTargets = @(Get-EngineLogCopies $engineLogDir '0' $engineCopyPrefix | ForEach-Object { $_.Target })

$exitCode = 1
$states = @()
$process = $null
$launchAttempted = $false
$startTime = $null
$runCfgWritten = $false
$restoreFailed = $false
$runError = $false
$timedOut = $false
$crashLog = $null
$launchFailed = $false
$setupOk = $false
$createdMcmDir = $false
$mcmDir = Split-Path $mcmPath

try {
    # --- Preconditions (read-only) ---------------------------------------
    if ($GamePath -eq '' -or -not (Test-Path -LiteralPath $GamePath)) {
        throw 'Bannerlord install not found. Pass -GamePath or set BannerlordPath in src\CoherentWarAI\paths.props.user.'
    }
    $exePath = Join-Path $GamePath 'bin\Win64_Shipping_Client\Bannerlord.exe'
    if (-not (Test-Path -LiteralPath $exePath)) { throw "Bannerlord.exe not found: $exePath" }
    if (@(Get-Process -Name 'Bannerlord' -ErrorAction SilentlyContinue).Count -gt 0) {
        throw 'Bannerlord.exe is already running. Close it first (a running game would overwrite the config files).'
    }
    foreach ($module in @('CoherentWarAI', 'CoherentWarAI.TestHarness')) {
        if (-not (Test-Path -LiteralPath (Join-Path $GamePath "Modules\$module"))) {
            throw "Module not deployed: $GamePath\Modules\$module (build the project first)."
        }
    }
    if (-not (Test-Path -LiteralPath $cfgPath -PathType Leaf)) {
        throw "BannerlordConfig.txt not found: $cfgPath (start the game once to create it)."
    }
    foreach ($userFile in @($cfgPath, $mcmPath)) {
        if ((Test-Path -LiteralPath $userFile -PathType Leaf) -and (Get-Item -LiteralPath $userFile).IsReadOnly) {
            throw "$userFile is read-only. Clear the read-only attribute first (the run must patch and restore it)."
        }
    }
    $stale = @(Get-LeftoverBackups $cfgPath) + @(Get-LeftoverBackups $mcmPath)
    if ($stale.Count -gt 0) {
        throw "Leftover backup(s)/marker(s) from an interrupted earlier run - the originals may still be modified: $(($stale | ForEach-Object { $_.FullName }) -join '; '). $restoreHint"
    }
    if (Test-Path -LiteralPath $runCfgPath) {
        throw "A previous run.cfg is still present: $runCfgPath (no run active? $restoreHint)"
    }
    if ($TimeoutMinutes -le $BudgetMinutes) {
        Write-Warning "TimeoutMinutes ($TimeoutMinutes) <= BudgetMinutes ($BudgetMinutes): the hard kill will fire before the harness budget watchdog."
    }
    if ($budgetExplicit -and $BudgetMinutes -lt $budgetEstimate) {
        Write-Warning "BudgetMinutes ($BudgetMinutes) looks too small for $Days day(s) (estimate 10 + 5 * Days = $budgetEstimate): the harness may abort before reaching the last day."
    }

    Write-Host "=== Soak run: Mode=$Mode Seed=$Seed Days=$Days Timeout=${TimeoutMinutes}min Stall=${StallMinutes}min Budget=${BudgetMinutes}min ==="
    Write-Host "Game: $GamePath"

    # --- Backup + edit configs -------------------------------------------
    $stamp = (Get-Date).ToString('yyyyMMdd-HHmmss')
    $states += New-UserFileBackup $cfgPath $stamp
    if (-not (Test-Path -LiteralPath $mcmDir)) {
        New-Item -ItemType Directory -Path $mcmDir -Force | Out-Null
        $createdMcmDir = $true
    }
    $states += New-UserFileBackup $mcmPath $stamp

    Set-BannerlordConfigValues $cfgPath
    Set-McmSettingsValues $mcmPath $featureOn
    Write-Host 'Config files backed up and patched.'

    # --- run.cfg + stale harness output ----------------------------------
    if (-not (Test-Path -LiteralPath $harnessDir)) { New-Item -ItemType Directory -Path $harnessDir -Force | Out-Null }
    foreach ($stalePath in @(@($resultPath, $harnessLogPath) + @($copies | ForEach-Object { $_.Target }) + $engineCopyTargets)) {
        if (Test-Path -LiteralPath $stalePath) { Remove-Item -LiteralPath $stalePath -Force }
    }
    $cfgText = @(
        ('seed=' + $Seed.ToString($inv)),
        ('days=' + $Days.ToString($inv)),
        ('mode=' + $Mode),
        ('stallMinutes=' + $StallMinutes.ToString($inv)),
        ('budgetMinutes=' + $BudgetMinutes.ToString($inv))
    ) -join "`n"
    $runCfgWritten = $true   # before the write: a partially written file must be deleted too
    Write-Utf8Text $runCfgPath ($cfgText + "`n") $false
    $setupOk = $true

    # --- Launch -----------------------------------------------------------
    $modules = @('Bannerlord.Harmony', 'Bannerlord.ButterLib', 'Bannerlord.UIExtenderEx', 'Bannerlord.MBOptionScreen', 'Native', 'SandBoxCore', 'Sandbox', 'StoryMode')
    if ($Mode -ne 'NoDlc') { $modules += 'NavalDLC' }
    $modules += @('CoherentWarAI', 'CoherentWarAI.TestHarness')
    $arguments = '/singleplayer _MODULES_*' + ($modules -join '*') + '*_MODULES_'

    $startTime = Get-Date
    Write-Host "Launching: Bannerlord.exe $arguments"
    $launchAttempted = $true   # before Start-Process: Ctrl+C before the assignment below must still stop the game
    $process = Start-Process -FilePath $exePath -ArgumentList $arguments -WorkingDirectory (Split-Path $exePath) -PassThru
    $null = $process.Handle   # keep the handle so ExitCode is readable after exit

    $deadline = $startTime.AddMinutes($TimeoutMinutes)
    $nextBeat = $startTime.AddMinutes(5)
    while (-not $process.WaitForExit(1000)) {
        $now = Get-Date
        if ($now -ge $deadline) {
            $timedOut = $true
            Write-Host "TIMEOUT after $TimeoutMinutes min - killing Bannerlord (process tree)."
            [void](Stop-ProcessTree $process)   # finally re-checks and reports if it is still alive
            break
        }
        # The crash dialog blocks the process until someone answers it: kill now, not at the timeout.
        $crashLog = Find-CrashedWatchdogLog $engineLogDir $process.Id $startTime
        if ($null -ne $crashLog) {
            Write-Host ("GAME CRASHED after {0:N0} s ({1}: 'Crash occurred') - killing Bannerlord (process tree)." -f ($now - $startTime).TotalSeconds, $crashLog.Name)
            [void](Stop-ProcessTree $process)   # finally re-checks and reports if it is still alive
            break
        }
        if ($now -ge $nextBeat) {
            $nextBeat = $now.AddMinutes(5)
            $tail = ''
            if (Test-Path -LiteralPath $harnessLogPath) {
                try { $tail = @(Get-Content -LiteralPath $harnessLogPath -Encoding UTF8 -Tail 1)[0] } catch { }
            }
            Write-Host ("[{0:N0} min] running... {1}" -f ($now - $startTime).TotalMinutes, $tail)
        }
    }
    $elapsed = (Get-Date) - $startTime
    if ($null -eq $crashLog -and -not $timedOut) {
        # A crash in the last second before the process exited on its own.
        $crashLog = Find-CrashedWatchdogLog $engineLogDir $process.Id $startTime
        if ($null -ne $crashLog) { Write-Host "GAME CRASHED ($($crashLog.Name): 'Crash occurred')." }
    }
    if (-not $timedOut -and $null -eq $crashLog) {
        Write-Host ("Game exited after {0:N0} s, exit code {1}." -f $elapsed.TotalSeconds, $process.ExitCode)
        if ($elapsed.TotalSeconds -lt 30 -and -not (Test-Path -LiteralPath $harnessLogPath)) {
            $launchFailed = $true
        }
    }
}
catch {
    Write-Host "ERROR: $($_.Exception.Message)"
    $runError = $true
    $exitCode = 1
}
finally {
    # Runs on normal end, timeout, error and Ctrl+C.
    $cleanupFailures = @(Invoke-RunCleanup -Process $process -LaunchAttempted $launchAttempted -GameProcessName 'Bannerlord' `
            -RunCfgWritten $runCfgWritten -RunCfgPath $runCfgPath -States $states -CreatedMcmDir $createdMcmDir -McmDir $mcmDir)
    if ($cleanupFailures.Count -gt 0) {
        $restoreFailed = $true
        Write-Host 'CLEANUP FAILED:'
        foreach ($f in $cleanupFailures) { Write-Host "  - $f" }
        Write-Host "Backups that were kept are listed above. Fix the cause (close the game if it still runs), then: $restoreHint"
    }
}

if (-not $setupOk) { exit $(if ($restoreFailed) { 6 } else { 1 }) }

# --- After the run: collect logs, evaluate ---------------------------------
# Every step is guarded so that a locked or stale file only produces a warning
# and the exit code still reflects the real run status.
$status = ''
$copiedTargets = @(Copy-RunOutput $copies $startTime)
$logCopied = $copiedTargets -contains $copiedLog

$crashed = $null -ne $crashLog
if ($crashed) {
    $crashPidMatch = [regex]::Match($crashLog.Name, '^watchdog_log_(\d+)\.txt$')
    if ($crashPidMatch.Success) {
        [void](Copy-RunOutput (Get-EngineLogCopies $engineLogDir $crashPidMatch.Groups[1].Value $engineCopyPrefix) $startTime)
    }
    else {
        Write-Host "WARNING: unexpected watchdog log name $($crashLog.Name); engine logs not copied."
    }
    Write-Host 'GAME CRASHED: look at the FirstChance lines at the end of the harness log copy and at the last Map tick / CampaignTick / HourlyTick lines before them.'
}

if ($launchFailed) {
    Write-Host 'LAUNCH FAILED (direct start / Steam?): the process exited within 30 s and harness\harness.log never appeared.'
}
if (Test-Path -LiteralPath $resultPath) {
    try {
        Write-Host '--- result.txt ---'
        $resultText = Read-Utf8Text $resultPath
        Write-Host $resultText
        $statusMatch = [regex]::Match($resultText, '(?m)^status=(\S+)')
        if ($statusMatch.Success) { $status = $statusMatch.Groups[1].Value }
        $mapEventsMatch = [regex]::Match($resultText, '(?m)^playerMapEvents=(\d+)')
        if ($mapEventsMatch.Success -and [long]$mapEventsMatch.Groups[1].Value -gt 0) {
            Write-Host "WARN: player was in $($mapEventsMatch.Groups[1].Value) map event(s) - AC-G5"
        }
    }
    catch {
        Write-Host "WARNING: could not read $resultPath`: $($_.Exception.Message)"
    }
}

$checkerExit = $null
if ($logCopied) {
    try {
        & $checker -LogPath $copiedLog -Mode $Mode -MinDays $Days
        $checkerExit = $LASTEXITCODE
    }
    catch {
        Write-Host "Check-CommandLog.ps1 failed: $($_.Exception.Message)"
        $checkerExit = 1
    }
}
else {
    Write-Host 'Check-CommandLog.ps1 skipped: no CoherentWarAI.log of this run copied.'
}

if ($restoreFailed) { $exitCode = 6 }
elseif ($runError) { $exitCode = 1 }
elseif ($crashed) { $exitCode = 7 }
elseif ($launchFailed) { $exitCode = 2 }
elseif ($timedOut) { $exitCode = 3 }
elseif ($status -ne 'Completed') { $exitCode = 4 }
elseif ($checkerExit -ne 0) { $exitCode = 5 }
else { $exitCode = 0 }

Write-Host "Exit code: $exitCode (status='$status', checker=$checkerExit, crashed=$crashed)"
exit $exitCode
