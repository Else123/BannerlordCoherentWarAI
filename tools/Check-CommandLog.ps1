<#
.SYNOPSIS
    Mechanically checks a CoherentWarAI.log against the pending Chain-of-Command
    acceptance criteria (COMMAND-SLICE-1.md, COMMAND-SLICE-2A.md).

.DESCRIPTION
    Parses the log into in-game days (grouped by the "=== Year X, Season D - ... ==="
    section headers - several sections share one day) and prints one verdict row per
    acceptance criterion: PASS, FAIL, INSUFFICIENT DATA, N/A for mode, WARN - verify
    fallback, or MANUAL. Exits 0 unless at least one row is FAIL, so the result can be
    consumed by a wrapper script via $LASTEXITCODE.

    This script only reads the log; it never touches the game or the mod.

.PARAMETER LogPath
    Path to the CoherentWarAI.log to check. Defaults to the live game log location.

.PARAMETER Mode
    Which in-game session this log came from - changes how AC1/AC2 (model chain
    naming) and AC10 (feature-off config) are interpreted. AC9, AC21 and the
    liveness part of AC13/AC23 (tracked > 0, interior-day flush, MinDays) need the
    naval chain, so they apply to WarSails and FeatureOff and report "N/A for mode"
    for NoDlc and ChainOff. AC20 applies to every mode but ChainOff (no delegations with
    the chain off); FeatureOff only requires a Defense delegation (with the doctrine
    off only lords already leading a host become Offense holders, maybe none). AC1/AC2
    is N/A for ChainOff. "non-zero contention" is a FAIL in every mode (a real
    double-write is always a bug):
      WarSails   - War Sails (NavalDLC) enabled, everything on.
      NoDlc      - War Sails disabled, everything on.
      FeatureOff - War Sails enabled, ONLY the marshal doctrine off (EnableMarshalDoctrine
                   = false); chain of command and naval orders stay on, so the chain
                   runs exactly as in WarSails. AC10 proves the doctrine is off from the init line.
      ChainOff   - War Sails enabled, marshal doctrine ON, chain of command and naval orders
                   off: the pre-chain-of-command baseline (AC25a, doctrine only) of CW-1. AC10
                   proves that config from the init line and that no daily [Command] lines appear.

.PARAMETER MinDays
    Minimum number of in-game days the log must cover before day-count-dependent
    criteria (AC13/AC23) are allowed to PASS rather than report INSUFFICIENT DATA.
    Defaults to 10 for -Mode WarSails, 3 otherwise (matching the three-run protocol
    in INGAME-TEST-PROTOCOL.md).

.PARAMETER ResultPath
    Optional. When set, the verdict table is also written to this path as UTF-8 JSON
    (no BOM) for machine consumers such as the test rig runner:
      { "schema": 1, "mode": "<Mode>", "days": <parsed days>,
        "rows": [ { "ac": "AC1", "verdict": "PASS", "evidence": "..." }, ... ] }
    The console table and the exit codes are unchanged. Nothing is written when the
    log file is missing (exit 1 before parsing).

.EXAMPLE
    powershell -NoProfile -File tools\Check-CommandLog.ps1 -Mode WarSails

.EXAMPLE
    powershell -NoProfile -File tools\Check-CommandLog.ps1 -LogPath <job>\r1\CoherentWarAI.log -Mode FeatureOff -MinDays 30 -ResultPath <job>\r1\command-log.json
#>

param(
    [string]$LogPath = (Join-Path $env:UserProfile 'Documents\Mount and Blade II Bannerlord\CoherentWarAI\CoherentWarAI.log'),

    [Parameter(Mandatory = $true)]
    [ValidateSet('WarSails', 'NoDlc', 'FeatureOff', 'ChainOff')]
    [string]$Mode,

    [int]$MinDays = -1,

    [string]$ResultPath = ''
)

Set-StrictMode -Version 2
$ErrorActionPreference = 'Stop'

if ($MinDays -lt 0) {
    if ($Mode -eq 'WarSails') {
        $MinDays = 10
    }
    else {
        $MinDays = 3
    }
}

if (-not (Test-Path -LiteralPath $LogPath)) {
    Write-Host "Log file not found: $LogPath"
    exit 1
}

$rawLines = @(Get-Content -LiteralPath $LogPath)

# ---------------------------------------------------------------------------
# Parse the log into in-game days. Several "=== ... ===" sections can share
# one day (gateways / war posture / chain of command); group by the
# "Year X, Season D" text, in first-seen order.
# ---------------------------------------------------------------------------
$headerPattern = '^=== (Year \d+, \S+ \d+) - (.+) ===$'

$dayOrder = New-Object System.Collections.ArrayList
$dayLines = @{}
$currentDay = $null

foreach ($line in $rawLines) {
    if ($line -match $headerPattern) {
        $dayKey = $Matches[1]
        if (-not $dayLines.ContainsKey($dayKey)) {
            [void]$dayOrder.Add($dayKey)
            $dayLines[$dayKey] = New-Object System.Collections.ArrayList
        }
        $currentDay = $dayKey
        continue
    }
    if ($null -ne $currentDay) {
        [void]$dayLines[$currentDay].Add($line)
    }
}

$dayCount = $dayOrder.Count

$results = New-Object System.Collections.ArrayList
function Add-Result {
    param([string]$Ac, [string]$Verdict, [string]$Evidence)
    [void]$script:results.Add([PSCustomObject]@{ AC = $Ac; Verdict = $Verdict; Evidence = $Evidence })
}

# ---------------------------------------------------------------------------
# AC1 (WarSails) / AC2 (NoDlc) - the one-time model chain line.
# FeatureOff keeps War Sails and the chain on (only the marshal doctrine is off),
# so it must name the same Naval chain as WarSails. ChainOff skips it: the line says
# nothing about the criteria the baseline exists to test.
# ---------------------------------------------------------------------------
$chainPattern = '^\[Command\] Command chain: TargetScore->(.+), Garrison->(.+), Army->(.+)$'
$chainLine = $null
foreach ($line in $rawLines) {
    if ($line -match $chainPattern) {
        $chainLine = $line
        break
    }
}

if ($Mode -eq 'WarSails') {
    $acLabel = 'AC1'
    $expectedPrefix = 'Naval'
}
elseif ($Mode -eq 'NoDlc') {
    $acLabel = 'AC2'
    $expectedPrefix = 'Default'
}
elseif ($Mode -eq 'ChainOff') {
    $acLabel = 'AC1/AC2'
    $expectedPrefix = $null
}
else {
    $acLabel = 'AC1'
    $expectedPrefix = 'Naval'
}

if ($Mode -eq 'ChainOff') {
    Add-Result $acLabel 'N/A for mode' 'Chain-naming check (AC1 War Sails / AC2 NoDlc) does not apply to -Mode ChainOff.'
}
elseif ($null -eq $chainLine) {
    Add-Result $acLabel 'FAIL' 'No "[Command] Command chain:" line found in the log at all.'
}
else {
    $null = $chainLine -match $chainPattern
    $targetScore = $Matches[1]
    $garrison = $Matches[2]
    $army = $Matches[3]
    $chainOk = ($targetScore -like "$expectedPrefix*") -and ($garrison -like "$expectedPrefix*") -and ($army -like "$expectedPrefix*")
    if ($chainOk) {
        Add-Result $acLabel 'PASS' $chainLine
    }
    else {
        Add-Result $acLabel 'FAIL' $chainLine
    }
}

# ---------------------------------------------------------------------------
# AC10 - FeatureOff = marshal doctrine off only (EnableMarshalDoctrine = false);
# chain of command and naval orders stay on. The applied config is proven by the
# init line alone: the last "Settings in effect." / "CoherentWarAI active." [Init]
# line (the startup line may still report defaults) must say
# 'marshal doctrine: False' and 'chain of command: True'.
# Deliberately NOT asserted: absence of MARSHAL lines or Offense delegations. With
# the doctrine off the mod still lists lords who already lead a host as MARSHAL
# (MarshalPlanner.IsAppointed(leadsHost: true) holds at marshalCount 0) and they
# become Offense holders, so such lines are legitimate. A "no non-host MARSHAL"
# check would need the log to say who led a host; it does not.
#
# ChainOff = the pre-chain-of-command baseline (AC25a, doctrine only): marshal doctrine
# on, chain of command and naval orders off. The same last init line must say
# 'marshal doctrine: True' and 'chain of command: False' (and must not say
# 'naval orders: True' when it prints that setting - only "Settings in effect."
# does), and no DAILY [Command] delegation/order lines may appear (the one-time
# chain line is allowed - it is deliberately ungated).
# ---------------------------------------------------------------------------
$initLine = $null
foreach ($line in $rawLines) {
    if ($line -match '^\[Init\] (CoherentWarAI active|Settings in effect)\.') {
        $initLine = $line
    }
}
$noInitLineText = '(no [Init] "CoherentWarAI active." or "Settings in effect." line found)'

if ($Mode -eq 'ChainOff') {
    $initOk = ($null -ne $initLine) -and ($initLine -match 'marshal doctrine: True\b') -and ($initLine -match 'chain of command: False\b') -and ($initLine -notmatch 'naval orders: True\b')

    $dailyCommandLines = New-Object System.Collections.ArrayList
    foreach ($day in $dayOrder) {
        foreach ($line in $dayLines[$day]) {
            if ($line -match '^\[Command\] .+ (delegates|orders) ') {
                [void]$dailyCommandLines.Add("$day`: $line")
            }
        }
    }

    if (-not $initOk) {
        $shown = if ($null -eq $initLine) { $noInitLineText } else { $initLine }
        Add-Result 'AC10' 'FAIL' "Init line does not say 'marshal doctrine: True', 'chain of command: False' (and 'naval orders: False' where printed): $shown"
    }
    elseif ($dailyCommandLines.Count -gt 0) {
        Add-Result 'AC10' 'FAIL' "Found $($dailyCommandLines.Count) daily [Command] line(s) despite the chain of command being off, e.g.: $($dailyCommandLines[0])"
    }
    else {
        Add-Result 'AC10' 'PASS' "Init line confirms doctrine on, chain of command and naval orders off; 0 daily [Command] lines across $dayCount day(s): $initLine"
    }
}
elseif ($Mode -ne 'FeatureOff') {
    Add-Result 'AC10' 'N/A for mode' 'Feature-off config check only applies to -Mode FeatureOff and -Mode ChainOff.'
}
else {
    $initOk = ($null -ne $initLine) -and ($initLine -match 'marshal doctrine: False\b') -and ($initLine -match 'chain of command: True\b')

    if ($initOk) {
        Add-Result 'AC10' 'PASS' "Init line confirms 'marshal doctrine: False' and 'chain of command: True': $initLine"
    }
    else {
        $shown = if ($null -eq $initLine) { $noInitLineText } else { $initLine }
        Add-Result 'AC10' 'FAIL' "Init line does not say 'marshal doctrine: False' and 'chain of command: True': $shown"
    }
}

# ---------------------------------------------------------------------------
# AC9 (Slice 1) - within the first 3 days: a Naval delegation line and a
# patrol order line.
# ---------------------------------------------------------------------------
$windowDays9 = @($dayOrder | Select-Object -First 3)
$firstDelegatesNaval = $null
$firstOrdersPatrol = $null
foreach ($day in $windowDays9) {
    foreach ($line in $dayLines[$day]) {
        if ($null -eq $firstDelegatesNaval -and $line -match '^\[Command\] .+ delegates Naval to .+$') {
            $firstDelegatesNaval = $line
        }
        if ($null -eq $firstOrdersPatrol -and $line -match '^\[Command\] .+ orders .+ to patrol \(') {
            $firstOrdersPatrol = $line
        }
    }
}

if ($Mode -eq 'NoDlc') {
    Add-Result 'AC9' 'N/A for mode' 'Naval delegation/patrol needs War Sails; -Mode NoDlc has none.'
}
elseif ($Mode -eq 'ChainOff') {
    Add-Result 'AC9' 'N/A for mode' 'Naval delegation/patrol needs the chain of command and naval orders; -Mode ChainOff has them off (AC10).'
}
elseif ($null -ne $firstDelegatesNaval -and $null -ne $firstOrdersPatrol) {
    Add-Result 'AC9' 'PASS' "First delegation: $firstDelegatesNaval | First order: $firstOrdersPatrol"
}
elseif ($dayOrder.Count -lt 3) {
    Add-Result 'AC9' 'INSUFFICIENT DATA' "Only $($dayOrder.Count) day(s) observed (need 3) and no match yet."
}
else {
    Add-Result 'AC9' 'FAIL' 'No Naval delegation line and/or patrol order line found within the first 3 days.'
}

# ---------------------------------------------------------------------------
# AC20 (Slice 2a) - within 5 days: at least one Defense delegation AND one
# Offense delegation line. Prints per-day counts for both domains.
# ---------------------------------------------------------------------------
$windowDays20 = @($dayOrder | Select-Object -First 5)
$defenseFound = $false
$offenseFound = $false
$firstDefense = $null
$firstOffense = $null
$perDayCounts20 = New-Object System.Collections.ArrayList

foreach ($day in $windowDays20) {
    $defCount = 0
    $offCount = 0
    foreach ($line in $dayLines[$day]) {
        if ($line -match '^\[Command\] .+ delegates Defense to .+$') {
            $defCount++
            if (-not $defenseFound) {
                $defenseFound = $true
                $firstDefense = $line
            }
        }
        if ($line -match '^\[Command\] .+ delegates Offense to .+$') {
            $offCount++
            if (-not $offenseFound) {
                $offenseFound = $true
                $firstOffense = $line
            }
        }
    }
    [void]$perDayCounts20.Add("[${day}: Defense=$defCount, Offense=$offCount]")
}
$countsSummary20 = $perDayCounts20 -join ' '

# FeatureOff: Offense holders are the appointed marshals, which without the doctrine are
# only lords already leading a host (possibly none), so only the Defense delegation is
# required there; an Offense delegation is allowed but not demanded.
$offenseNeeded = ($Mode -ne 'FeatureOff')
if ($Mode -eq 'ChainOff') {
    Add-Result 'AC20' 'N/A for mode' 'Defense/Offense delegation needs the chain of command; -Mode ChainOff has no delegations by design (AC10).'
}
elseif ($defenseFound -and ($offenseFound -or -not $offenseNeeded)) {
    $offenseShown = if ($offenseNeeded) { $firstOffense } else { '(not required for FeatureOff)' }
    Add-Result 'AC20' 'PASS' "Defense: $firstDefense | Offense: $offenseShown | Per-day counts: $countsSummary20"
}
elseif ($dayOrder.Count -lt 5) {
    Add-Result 'AC20' 'INSUFFICIENT DATA' "Only $($dayOrder.Count) day(s) observed (need 5). Per-day counts so far: $countsSummary20"
}
else {
    Add-Result 'AC20' 'FAIL' "No Defense+Offense delegation within the first 5 days. Per-day counts: $countsSummary20"
}

# ---------------------------------------------------------------------------
# AC21 (Slice 2a regression) - Naval lines must still have the Slice 1 shape,
# including the "near <port>" suffix CommandBehavior.cs actually emits today.
# Whole-log check (not day-windowed): this is a shape/regression check, not a
# "how many days" check.
# ---------------------------------------------------------------------------
$navalDelegateLine = $null
$navalOrderLine = $null
foreach ($line in $rawLines) {
    if ($null -eq $navalDelegateLine -and $line -match '^\[Command\] .+ delegates Naval to .+$') {
        $navalDelegateLine = $line
    }
    if ($null -eq $navalOrderLine -and $line -match '^\[Command\] .+ orders .+ to patrol \(-?\d+, -?\d+\) near .+$') {
        $navalOrderLine = $line
    }
}

if ($Mode -eq 'NoDlc') {
    Add-Result 'AC21' 'N/A for mode' 'Naval line shape needs War Sails; -Mode NoDlc has none.'
}
elseif ($Mode -eq 'ChainOff') {
    Add-Result 'AC21' 'N/A for mode' 'Naval line shape needs the chain of command and naval orders; -Mode ChainOff has them off (AC10).'
}
elseif ($null -ne $navalDelegateLine -and $null -ne $navalOrderLine) {
    Add-Result 'AC21' 'PASS' "$navalDelegateLine | $navalOrderLine"
}
elseif ($dayOrder.Count -eq 0) {
    Add-Result 'AC21' 'INSUFFICIENT DATA' 'No day sections found in the log.'
}
else {
    $parts = New-Object System.Collections.ArrayList
    if ($null -ne $navalDelegateLine) {
        [void]$parts.Add("delegation line OK: $navalDelegateLine")
    }
    else {
        [void]$parts.Add('no "delegates Naval to" line found')
    }
    if ($null -ne $navalOrderLine) {
        [void]$parts.Add("patrol line OK: $navalOrderLine")
    }
    else {
        [void]$parts.Add('no line matching "orders <lord> to patrol (x, y) near <port>" found')
    }
    Add-Result 'AC21' 'FAIL' ($parts -join '; ')
}

# ---------------------------------------------------------------------------
# AC13 (Slice 1) / AC23 (Slice 2a) - the contention counter reports 0 AND is
# provably alive. Contract (COMMAND-SLICE-2A.md AC23, tightened 2026-09-16):
#   * WarAiStats.FlushContention always writes one line, zero or not.
#   * It runs at the START of WarPostureBehavior's daily tick, so each line
#     reports the PREVIOUS in-game day and may land in the next day's bucket;
#     the last observed day is therefore usually not flushed yet.
#   * The very first line in a file comes from the forced launch tick in
#     OnSessionLaunched and legitimately reads "tracked 0 parties".
# Checks, order-independent on purpose:
#   FAIL  any line not starting with "0 parties"
#   FAIL  any "tracked 0" line other than the first line in the file
#   FAIL  an interior day bucket (not first, not last) with no line at all
#   FAIL  >= 2 days but no line at all, or no line with tracked N > 0
#   INSUFFICIENT DATA  fewer than -MinDays days
# ---------------------------------------------------------------------------
$allContention = @(Get-Content -LiteralPath $LogPath | Where-Object { $_ -match '^\[Contention\] ' })
$nonZeroContention = New-Object System.Collections.ArrayList
$trackedZeroOutsideLaunch = New-Object System.Collections.ArrayList
$sawTrackedPositive = $false

for ($ci = 0; $ci -lt $allContention.Count; $ci++) {
    $cl = $allContention[$ci]
    if ($cl -notmatch '^\[Contention\] 0 parties') {
        [void]$nonZeroContention.Add($cl)
        continue
    }
    if ($cl -match 'tracked (\d+) part') {
        $trackedN = [int]$matches[1]
        if ($trackedN -gt 0) {
            $sawTrackedPositive = $true
        }
        elseif ($ci -gt 0) {
            [void]$trackedZeroOutsideLaunch.Add($cl)
        }
    }
}

$interiorMissing = New-Object System.Collections.ArrayList
$perDayCounts = New-Object System.Collections.ArrayList
for ($di = 0; $di -lt $dayOrder.Count; $di++) {
    $day = $dayOrder[$di]
    $dayContention = @($dayLines[$day] | Where-Object { $_ -match '^\[Contention\] ' }).Count
    [void]$perDayCounts.Add(("{0}={1}" -f $day, $dayContention))
    $isInterior = ($di -gt 0) -and ($di -lt ($dayOrder.Count - 1))
    if ($isInterior -and $dayContention -eq 0) {
        [void]$interiorMissing.Add($day)
    }
}
$perDayText = $perDayCounts -join ', '

if ($nonZeroContention.Count -gt 0) {
    Add-Result 'AC13/AC23' 'FAIL' "Non-zero contention reported: $($nonZeroContention -join ' | ')"
}
elseif ($Mode -eq 'NoDlc' -or $Mode -eq 'ChainOff') {
    Add-Result 'AC13/AC23' 'N/A for mode' "Liveness checks need the naval dispatch (day-1 writer); -Mode $Mode has none. Hard check still ran: $($allContention.Count) [Contention] line(s), none non-zero."
}
elseif ($trackedZeroOutsideLaunch.Count -gt 0) {
    Add-Result 'AC13/AC23' 'FAIL' "'tracked 0 parties' outside the launch tick - the counter recorded no write on a normal day: $($trackedZeroOutsideLaunch -join ' | ')"
}
elseif ($interiorMissing.Count -gt 0) {
    Add-Result 'AC13/AC23' 'FAIL' "No [Contention] line on interior day(s) $($interiorMissing -join ', ') - the daily flush stopped running. Per day: $perDayText"
}
elseif ($dayOrder.Count -ge 2 -and $allContention.Count -eq 0) {
    Add-Result 'AC13/AC23' 'FAIL' "No [Contention] line at all over $($dayOrder.Count) day(s) - the flush never ran (or this log comes from a build before the 2026-09-16 zero-line change)."
}
elseif ($dayOrder.Count -ge 2 -and -not $sawTrackedPositive) {
    Add-Result 'AC13/AC23' 'FAIL' "Every [Contention] line reads 'tracked 0' - the counter never saw a write; posture/command writes are not being recorded."
}
elseif ($dayOrder.Count -lt $MinDays) {
    Add-Result 'AC13/AC23' 'INSUFFICIENT DATA' "$($allContention.Count) [Contention] line(s) over $($dayOrder.Count) day(s), all '0 parties'; -MinDays $MinDays requires more observation (a 1-day run cannot judge the flush - day 1 is only flushed at the start of day 2). Per day: $perDayText"
}
else {
    Add-Result 'AC13/AC23' 'PASS' "$($allContention.Count) [Contention] line(s) over $($dayOrder.Count) day(s), all '0 parties', counter live (tracked > 0 seen). Per day: $perDayText"
}

# ---------------------------------------------------------------------------
# AC26 - defenders before attackers: a lord released to attack (MARSHAL or
# "attacks:" in that day's posture digest for a kingdom) must not also be the
# addressee of a same-day patrol order for that kingdom. The one legitimate
# exception (fewer non-attacking naval lords than lanes) is not observable
# from the log, so violations are reported as WARN, never FAIL.
#
# Holder -> kingdom is carried forward across days: AppointHolder only logs
# "<kingdom> delegates <Domain> to <hero>" the day the holder actually
# CHANGES (CommandBehavior.cs), so a day with an unchanged incumbent has no
# fresh delegation line to read the kingdom from.
# ---------------------------------------------------------------------------
$kingdomHeaderPattern = '^\[Posture\] (.+): \d+ parties, \d+ war\(s\), \d+ % of realm threatened -> \d+ may attack, \d+ defend$'
# Hero names may carry a trailing " [id=<StringId>]" (builds that log the hero id); the
# patterns drop it so the names compare equal whether or not every line type has it.
$postureNamePattern = '^\[Posture\]\s+(MARSHAL|attacks): (.+?)(?: \[id=[^\]]+\])? \(strength'
$delegatesAnyPattern = '^\[Command\] (.+) delegates (Naval|Defense|Offense) to (.+?)(?: \[id=[^\]]+\])?$'
$ordersAnyPattern = '^\[Command\] (.+?)(?: \[id=[^\]]+\])? orders (.+?)(?: \[id=[^\]]+\])? to patrol \('

$holderKingdom = @{}
$violations = New-Object System.Collections.ArrayList

foreach ($day in $dayOrder) {
    $lines = $dayLines[$day]
    $attackerSets = @{}
    $currentKingdom = $null

    # Pass 1: build this day's per-kingdom attacker set and refresh holder->kingdom.
    foreach ($line in $lines) {
        if ($line -match $kingdomHeaderPattern) {
            $currentKingdom = $Matches[1]
            if (-not $attackerSets.ContainsKey($currentKingdom)) {
                $attackerSets[$currentKingdom] = New-Object 'System.Collections.Generic.HashSet[string]'
            }
            continue
        }
        if ($null -ne $currentKingdom -and $line -match $postureNamePattern) {
            [void]$attackerSets[$currentKingdom].Add($Matches[2])
            continue
        }
        if ($line -match $delegatesAnyPattern) {
            $holderKingdom[$Matches[3]] = $Matches[1]
            continue
        }
    }

    # Pass 2: check every patrol addressee against that kingdom's attacker set.
    foreach ($line in $lines) {
        if ($line -match $ordersAnyPattern) {
            $holderName = $Matches[1]
            $lordName = $Matches[2]
            if ($holderKingdom.ContainsKey($holderName)) {
                $kingdomName = $holderKingdom[$holderName]
                if ($attackerSets.ContainsKey($kingdomName) -and $attackerSets[$kingdomName].Contains($lordName)) {
                    [void]$violations.Add("day '$day', kingdom $kingdomName`: $lordName (patrol addressee, also attacker/marshal)")
                }
            }
        }
    }
}

if ($dayOrder.Count -eq 0) {
    Add-Result 'AC26' 'INSUFFICIENT DATA' 'No day sections found in the log.'
}
elseif ($violations.Count -eq 0) {
    Add-Result 'AC26' 'PASS' "No attacker/marshal was also a same-day patrol addressee, across $($dayOrder.Count) day(s)."
}
else {
    $warnEvidence = "$($violations.Count) potential violation(s) - can be the documented fallback (fewer non-attacking naval lords than lanes), which is not observable from the log; verify naval-candidate counts manually if this matters. " + ($violations -join ' | ')
    Add-Result 'AC26' 'WARN - verify fallback' $warnEvidence
}

# ---------------------------------------------------------------------------
# AC25a - army composition cannot be read from the log at all; it is judged
# across a whole WarSails/FeatureOff/ChainOff job by CW-1 (tools\Compare-Ac25a.ps1).
# ---------------------------------------------------------------------------
Add-Result 'AC25a' 'MANUAL' 'Army composition cannot be evaluated from a single mod log. It is judged by CW-1 (coherentwarai.ac25a-digest): tools\Compare-Ac25a.ps1 over a job of WarSails, FeatureOff and ChainOff runs on several seeds checks that no non-ruler lord who holds only Defense or Naval (never Offense, never MARSHAL/attacks) leads an army, and compares total army-days WarSails vs ChainOff (COMMAND-SLICE-2A.md AC25a).'

# ---------------------------------------------------------------------------
# Print the table and finish.
# ---------------------------------------------------------------------------
Write-Host ''
Write-Host "=== CoherentWarAI Command Log Check ==="
Write-Host "Log:      $LogPath"
Write-Host "Mode:     $Mode"
Write-Host "MinDays:  $MinDays"
Write-Host "Days:     $dayCount parsed$(if ($dayCount -gt 0) { ' (' + ($dayOrder -join '; ') + ')' })"
Write-Host ''
Write-Host ('{0,-10} | {1,-20} | {2}' -f 'AC', 'Verdict', 'Evidence')
Write-Host (('-' * 10) + '-+-' + ('-' * 20) + '-+-' + ('-' * 40))
foreach ($r in $results) {
    Write-Host ('{0,-10} | {1,-20} | {2}' -f $r.AC, $r.Verdict, $r.Evidence)
}

if ($ResultPath) {
    $resultDoc = [ordered]@{
        schema = 1
        mode   = $Mode
        days   = $dayCount
        rows   = @($results | ForEach-Object { [ordered]@{ ac = $_.AC; verdict = $_.Verdict; evidence = $_.Evidence } })
    }
    $resultFile = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($ResultPath)
    [System.IO.File]::WriteAllText($resultFile, (ConvertTo-Json -InputObject $resultDoc -Depth 4), (New-Object System.Text.UTF8Encoding $false))
}

$failRows = @($results | Where-Object { $_.Verdict -eq 'FAIL' })
Write-Host ''
if ($failRows.Count -gt 0) {
    Write-Host "SUMMARY: $($failRows.Count) check(s) FAILED - $((($failRows | ForEach-Object { $_.AC }) -join ', '))."
    exit 1
}
else {
    Write-Host 'SUMMARY: no FAIL verdicts (PASS / N/A for mode / MANUAL / INSUFFICIENT DATA / WARN only).'
    exit 0
}
