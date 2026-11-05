# Run:  powershell -NoProfile -Command "Invoke-Pester <repo>\tests\tools"
# Pester 3.4 / Windows PowerShell 5.1. Builds synthetic job folders (r<N>\run-result.json, harness.log
# with the ac25a v1 digest, CoherentWarAI.log) under TestDrive and runs tools\Compare-Ac25a.ps1 as a
# child process.
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$script:comparer = (Resolve-Path (Join-Path $here '..\..\tools\Compare-Ac25a.ps1')).Path
$script:realisticFixture = (Resolve-Path (Join-Path $here 'fixtures\ac25a-realistic')).Path
$script:kingdom = 'empire|Northern Empire'
$script:prefix = 'scenario coherentwarai.ac25a-digest: ac25a v1'

# Calendar date of harness day $Day, as WarAiLog.GameDate writes it (day 0 = Summer 1).
function Get-Calendar([int]$Day) { "Year 1084, Summer $($Day + 1)" }

$script:launchMarker = '[Command] Offense holders from session launch: 1 kingdom(s)'
$script:clearMarker = '[Command] Offense holders cleared each posture tick'
$script:blockHeader ='[Posture] Northern Empire: 17 parties, 13 war(s), 0 % of realm threatened -> 6 may attack, 11 defend'

# A war posture section for harness day $Day: the Northern Empire block (block line $Header) with
# Lord 0..Lord 39 as MARSHAL except the names in $Except, plus the MARSHAL names in $Extra.
function Get-PostureSection([int]$Day, [string[]]$Except = @(), [string[]]$Extra = @(), [string]$Header = $script:blockHeader) {
    $lines = New-Object System.Collections.ArrayList
    [void]$lines.Add("=== $(Get-Calendar $Day) - war posture ===")
    [void]$lines.Add($Header)
    for ($i = 0; $i -lt 40; $i++) {
        if ($Except -notcontains "Lord $i") { [void]$lines.Add("[Posture]   MARSHAL: Lord $i (strength 229, valor 0)") }
    }
    foreach ($name in $Extra) { [void]$lines.Add("[Posture]   MARSHAL: $name (strength 229, valor 0)") }
    [string[]]$lines
}

# A posture section for harness day $Day that has a block for another kingdom only: Northern Empire
# had no candidates that tick. A build without the clear marker kept its previous Offense holder
# set; a build with it (T-0118) left it without holders.
function Get-ForeignPostureSection([int]$Day) {
    @("=== $(Get-Calendar $Day) - war posture ===", '[Posture] Khuzait: 18 parties, 11 war(s), 7 % of realm threatened -> 6 may attack, 12 defend',
        '[Posture]   MARSHAL: Monchug (strength 313, valor 0)')
}

# The realistic fixture job (tests\tools\fixtures\ac25a-realistic) copied to TestDrive, so the
# comparer's JSON is not written into the repo. -StripCalendar removes the created lines' calendar=
# field (a digest written before that field existed); -StripDailyTicks removes the rig's
# "DailyTick <n> done" lines (a harness log without tick order).
function Copy-RealisticJob([switch]$StripCalendar, [switch]$StripDailyTicks) {
    $job = Join-Path $TestDrive ([guid]::NewGuid().ToString('N'))
    Copy-Item -Path $script:realisticFixture -Destination $job -Recurse
    $harness = Join-Path $job 'r1\harness.log'
    $lines = @([IO.File]::ReadAllLines($harness))
    if ($StripCalendar) { $lines = @($lines | ForEach-Object { if ($_ -match ' created ') { $_ -replace ' calendar=.*$', '' } else { $_ } }) }
    if ($StripDailyTicks) { $lines = @($lines | Where-Object { $_ -notmatch ' DailyTick \d+ done ' }) }
    [IO.File]::WriteAllLines($harness, [string[]]$lines)
    $job
}

# Writes one run folder. ArmyDays non-ruler armies are spread round-robin over the Days snapshots
# (days StartDay+1 ..), each led by a distinct lord_<i> "Lord <i>" whose army is created on his first
# snapshot day; the ruler Lucon leads one army on the first day. The mod log appoints Lord 0..Lord 39
# MARSHAL on the start date (the session-launch posture: without a later block it governs every army,
# because the mod keeps a kingdom's holder set until its next block) except the names in NoOffense,
# writes the launch marker of a build that keeps that set (NoLaunchMarker: an older build that wipes
# it), and has one Naval delegation; ModLog appends lines (each tick date without a war posture
# section in ModLog gets one without a Northern Empire block). Leaders adds digest entries (without a created
# event) to the first day; Digest adds digest lines (prefix added): a created line right after the
# date line of its day, any other line at the end. harness.log orders the lines as the rig does:
# per day d "DailyTick d done", the AfterTick[d] digest lines (prefix added; e.g. a created line
# whose ctx.Day still lags), the date line of d, the created lines of d, the snapshot of d.
# NoDailyTicks leaves out the DailyTick lines (a harness log without tick order). ForeignLaunch
# writes the session-launch section without a Northern Empire block.
function New-Run {
    param([string]$Job, [int]$Index, [long]$Seed, [string]$Mode, [int]$ArmyDays, [string]$Status = 'Completed',
        [int]$Days = 3, [int]$StartDay = 0, [int]$DigestDays = -1, [string[]]$Leaders = @(), [string[]]$NoOffense = @(),
        [string[]]$ModLog = @(), [string[]]$Digest = @(), [string]$ResultJson = '', [string]$ScenarioStatus = 'Passed',
        [hashtable]$AfterTick = @{}, [switch]$NoDailyTicks, [switch]$NoModLog, [switch]$NoLaunchMarker, [switch]$ForeignLaunch)
    $dir = Join-Path $Job "r$Index"
    New-Item -ItemType Directory -Path $dir | Out-Null
    $reached = $StartDay + 1
    if ($Status -eq 'Completed') { $reached = $StartDay + $Days }
    if (-not $ResultJson) {
        $result = [ordered]@{ schema = 1; status = $Status; seed = $Seed; mode = $Mode; daysRequested = $Days; daysReached = $reached }
        if ($ScenarioStatus) { $result.scenarios = @([ordered]@{ id = 'coherentwarai.ac25a-digest'; status = $ScenarioStatus; seconds = 9 }) }
        $ResultJson = ConvertTo-Json -InputObject $result -Depth 4
    }
    [IO.File]::WriteAllText((Join-Path $dir 'run-result.json'), $ResultJson)

    $first = $StartDay + 1
    $perDay = @{}
    for ($d = $first; $d -lt $first + $Days; $d++) { $perDay[$d] = New-Object System.Collections.ArrayList }
    [void]$perDay[$first].Add('ruler_1|Lucon|1')
    for ($i = 0; $i -lt $ArmyDays; $i++) { [void]$perDay[($i % $Days) + $first].Add("lord_$i|Lord $i|0") }
    foreach ($extra in $Leaders) { [void]$perDay[$first].Add($extra) }
    $digested = if ($DigestDays -ge 0) { $DigestDays } else { $reached - $StartDay }
    # Digest created lines go after the date line of their day (day StartDay: before the first
    # daily tick of the digest), the other Digest lines at the end.
    $createdOn = @{}
    $tail = New-Object System.Collections.ArrayList
    foreach ($line in $Digest) {
        if ($line -match '^created day=(\d+) ' -and [int]$Matches[1] -ge $StartDay -and [int]$Matches[1] -lt $first + $Days) {
            $d = [int]$Matches[1]
            if (-not $createdOn.ContainsKey($d)) { $createdOn[$d] = New-Object System.Collections.ArrayList }
            [void]$createdOn[$d].Add($line)
        }
        else { [void]$tail.Add($line) }
    }
    $lines = New-Object System.Collections.ArrayList
    [void]$lines.Add("0.0 Start: seed=$Seed days=$Days mode=$Mode")
    # The rig's daily tick lines, in the real format; the harness counts days from session launch.
    $tickLine = { param([int]$Day) "40.$Day DailyTick $Day done (hours=$(2185857 + 24 * $Day).15)" }
    if (-not $NoDailyTicks) { for ($d = 1; $d -le $StartDay; $d++) { [void]$lines.Add((& $tickLine $d)) } }
    [void]$lines.Add("40.0 $($script:prefix) date day=$StartDay calendar=$(Get-Calendar $StartDay)")
    if ($createdOn.ContainsKey($StartDay)) { foreach ($line in $createdOn[$StartDay]) { [void]$lines.Add("40.0 $($script:prefix) $line") } }
    for ($d = $first; $d -lt $first + $Days; $d++) {
        if (-not $NoDailyTicks) { [void]$lines.Add((& $tickLine $d)) }
        if ($AfterTick.ContainsKey($d)) { foreach ($line in $AfterTick[$d]) { [void]$lines.Add("40.$d $($script:prefix) $line") } }
        $isDigested = $d -lt $first + $digested
        if ($isDigested) { [void]$lines.Add("40.$d $($script:prefix) date day=$d calendar=$(Get-Calendar $d)") }
        for ($j = $d - $first; $j -lt $ArmyDays; $j += $Days) {
            [void]$lines.Add("40.$d $($script:prefix) created day=$d kingdom=$($script:kingdom) leader=lord_$j|Lord $j|0")
        }
        if ($createdOn.ContainsKey($d)) { foreach ($line in $createdOn[$d]) { [void]$lines.Add("40.$d $($script:prefix) $line") } }
        if ($isDigested) { [void]$lines.Add("40.$d $($script:prefix) day=$d kingdom=$($script:kingdom) armies=$($perDay[$d].Count) leaders=$($perDay[$d] -join ';')") }
    }
    foreach ($line in $tail) { [void]$lines.Add("50.0 $($script:prefix) $line") }
    [IO.File]::WriteAllLines((Join-Path $dir 'harness.log'), [string[]]$lines)

    if ($NoModLog) { return }
    # The mod writes a war posture section on every daily tick: a tick date without one in ModLog
    # gets a section without a Northern Empire block (it keeps its set), in date order. A closing
    # chain-of-command header completes the last section.
    $posted = New-Object 'System.Collections.Generic.HashSet[string]'
    foreach ($line in $ModLog) { if ($line -match '^=== (.+) - war posture ===$') { [void]$posted.Add($Matches[1]) } }
    $fillers = New-Object System.Collections.Queue
    for ($d = $first; $d -lt $first + $Days; $d++) { if (-not $posted.Contains((Get-Calendar $d))) { $fillers.Enqueue($d) } }
    $mod = New-Object System.Collections.ArrayList
    $launchSection = if ($ForeignLaunch) { Get-ForeignPostureSection $StartDay } else { Get-PostureSection $StartDay $NoOffense }
    foreach ($line in $launchSection) { [void]$mod.Add($line) }
    if (-not $NoLaunchMarker) { [void]$mod.Add($script:launchMarker) }
    [void]$mod.Add("=== $(Get-Calendar $StartDay) - chain of command ===")
    [void]$mod.Add('[Command] Northern Empire delegates Naval to Lucon')
    # As in the real log, a chain-of-command section follows each posture section.
    $addFiller = { $day = $fillers.Dequeue(); foreach ($f in (Get-ForeignPostureSection $day)) { [void]$mod.Add($f) }; [void]$mod.Add("=== $(Get-Calendar $day) - chain of command ===") }
    foreach ($line in $ModLog) {
        if ($line -match '^=== Year 1084, Summer (\d+) - ') {
            $lineDay = [int]$Matches[1] - 1
            while ($fillers.Count -gt 0 -and $fillers.Peek() -le $lineDay) { . $addFiller }
        }
        [void]$mod.Add($line)
    }
    while ($fillers.Count -gt 0) { . $addFiller }
    [void]$mod.Add("=== $(Get-Calendar ($first + $Days - 1)) - chain of command ===")
    [IO.File]::WriteAllLines((Join-Path $dir 'CoherentWarAI.log'), [string[]]$mod)
}

# Cuts run $Index's CoherentWarAI.log right before the header of the war posture section of harness
# day $Day (-KeepHeader: right after it), as a failed or partial flush leaves it.
function Limit-ModLog([string]$Job, [int]$Index, [int]$Day, [switch]$KeepHeader) {
    $file = Join-Path $Job "r$Index\CoherentWarAI.log"
    $lines = @([IO.File]::ReadAllLines($file))
    $at = [Array]::IndexOf($lines, "=== $(Get-Calendar $Day) - war posture ===")
    if ($at -lt 0) { throw "no posture section for day $Day" }
    if ($KeepHeader) { $at++ }
    [IO.File]::WriteAllLines($file, [string[]]@($lines[0..($at - 1)]))
}

# Drops the war posture section of harness day $Day from run $Index's CoherentWarAI.log (a posture
# tick not logged), keeping every later section.
function Remove-PostureSectionFromLog([string]$Job, [int]$Index, [int]$Day) {
    $file = Join-Path $Job "r$Index\CoherentWarAI.log"
    $header = "=== $(Get-Calendar $Day) - war posture ==="
    $kept = New-Object System.Collections.ArrayList
    $skip = $false
    foreach ($line in [IO.File]::ReadAllLines($file)) {
        if ($line -match '^=== ') { $skip = $line -eq $header }
        if (-not $skip) { [void]$kept.Add($line) }
    }
    [IO.File]::WriteAllLines($file, [string[]]$kept)
}

# Removes the lines matching $Pattern from run $Index's harness.log (e.g. a lost DailyTick line).
function Remove-HarnessLine([string]$Job, [int]$Index, [string]$Pattern) {
    $file = Join-Path $Job "r$Index\harness.log"
    [IO.File]::WriteAllLines($file, [string[]]@([IO.File]::ReadAllLines($file) | Where-Object { $_ -notmatch $Pattern }))
}

# Rewrites run $Index's CoherentWarAI.log the way a build that appends the hero StringId does: the
# MARSHAL lines and the "delegates <Domain> to" lines of "Lord <n>" get " [id=lord_<n>]" (and the
# foreign kingdom's Monchug " [id=lord_monchug]", the ruler's Lucon " [id=ruler_1]"), so every hero line of the rewritten log has an id.
function Add-HeroIds([string]$Job, [int]$Index) {
    $file = Join-Path $Job "r$Index\CoherentWarAI.log"
    $lines = @([IO.File]::ReadAllLines($file) | ForEach-Object {
            $_ -replace '^(\[Posture\]\s+(?:MARSHAL|attacks): Lord (\d+)) \(', '$1 [id=lord_$2] (' -replace '^(\[Command\] .+ delegates \w+ to Lord (\d+))$', '$1 [id=lord_$2]' -replace '^(\[Posture\]\s+MARSHAL: Monchug) \(', '$1 [id=lord_monchug] (' -replace '^(\[Command\] .+ delegates \w+ to Lucon)$', '$1 [id=ruler_1]'
        })
    [IO.File]::WriteAllLines($file, [string[]]$lines)
}

# Per-seed run layouts. The armydays arms: WS, CO, CO, WS (the default of New-Job). The CW1 job
# since the ChainOff baseline: WS, FO, CO, CO, FO, WS. A job from before it: WS, FO, FO, WS.
$script:wsCoLayout = @('WarSails', 'ChainOff', 'ChainOff', 'WarSails')
$script:cw1Layout = @('WarSails', 'FeatureOff', 'ChainOff', 'ChainOff', 'FeatureOff', 'WarSails')
$script:oldLayout = @('WarSails', 'FeatureOff', 'FeatureOff', 'WarSails')

# One run per $Modes entry per seed. $Values[seed-1] holds the army-days in $Modes order (default
# @(WS1, AO1, AO2, WS2)); 3 seeds by default.
function New-Job([object[]]$Values, [double]$WsFactor = 1.0, [hashtable]$Overrides = @{}, [string[]]$Modes = $script:wsCoLayout) {
    $job = Join-Path $TestDrive ([guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $job | Out-Null
    $modes = $Modes
    $index = 1
    for ($s = 0; $s -lt $Values.Count; $s++) {
        for ($j = 0; $j -lt $modes.Count; $j++) {
            $v = [double]$Values[$s][$j]
            if ($modes[$j] -eq 'WarSails') { $v = $v * $WsFactor }
            $runArgs = @{ Job = $job; Index = $index; Seed = $s + 1; Mode = $modes[$j]; ArmyDays = [int][Math]::Round($v) }
            if ($Overrides.ContainsKey($index)) { foreach ($k in $Overrides[$index].Keys) { $runArgs[$k] = $Overrides[$index][$k] } }
            New-Run @runArgs
            $index++
        }
    }
    $job
}

# Writes a job.json plan with runs r1..r$Count (modes as in New-Job) to $Path.
function New-Manifest([string]$Path, [int]$Count, [string[]]$Modes = $script:wsCoLayout) {
    $modes = $Modes
    $planned = @(1..$Count | ForEach-Object { [ordered]@{ runId = "r$_"; seed = [int][Math]::Ceiling($_ / $modes.Count); mode = $modes[($_ - 1) % $modes.Count] } })
    New-Item -ItemType Directory -Path (Split-Path -Parent $Path) -Force | Out-Null
    [IO.File]::WriteAllText($Path, (ConvertTo-Json -InputObject ([ordered]@{ schema = 1; runs = $planned }) -Depth 4))
}

function Invoke-Comparer([string]$Job, [string]$Culture = '', [string]$Manifest = '') {
    $manifestArg = if ($Manifest) { " -JobManifestPath '$Manifest'" } else { '' }
    if ($Culture) {
        $command = "[Threading.Thread]::CurrentThread.CurrentCulture = '$Culture'; & '$($script:comparer)' -JobPath '$Job'$manifestArg; exit `$LASTEXITCODE"
        $out = & powershell -NoProfile -Command $command
    }
    elseif ($Manifest) {
        $out = & powershell -NoProfile -File $script:comparer -JobPath $Job -JobManifestPath $Manifest
    }
    else {
        $out = & powershell -NoProfile -File $script:comparer -JobPath $Job
    }
    $exit = $LASTEXITCODE
    $doc = [IO.File]::ReadAllText((Join-Path $Job 'ac25a-compare.json')) | ConvertFrom-Json
    [PSCustomObject]@{
        Exit      = $exit
        Output    = (@($out) -join "`n")
        Doc       = $doc
        Invariant = @($doc.checks | Where-Object { $_.check -eq 'coherentwarai.ac25a-invariant' })[0]
        ArmyDays  = @($doc.checks | Where-Object { $_.check -eq 'coherentwarai.ac25a-armydays' })[0]
    }
}

# Realistic noise: every seed has its own level, runs scatter around it, both arms alike.
$script:noisy = @(@(20, 18, 22, 21), @(30, 33, 27, 31), @(12, 14, 11, 13))

Describe 'Compare-Ac25a' {
    It 'passes when both arms are equally distributed' {
        $r = Invoke-Comparer (New-Job $script:noisy)
        $r.Exit | Should Be 0
        $r.Invariant.verdict | Should Be 'Passed'
        $r.ArmyDays.verdict | Should Be 'Passed'
        $r.ArmyDays.permutations | Should Be 216
        $r.ArmyDays.minP | Should Be ([Math]::Round(2 / 216, 4))
        ($r.ArmyDays.p -ge 0.05) | Should Be $true
        @($r.Doc.runs).Count | Should Be 12
        @($r.Doc.runs | Where-Object { $_.valid }).Count | Should Be 12
        @($r.Doc.kingdoms).Count | Should Be 1
        $r.Doc.kingdoms[0].kingdomId | Should Be 'empire'
        $r.Doc.kingdoms[0].kingdom | Should Be 'Northern Empire'
        $r.Invariant.unlinked | Should Be 0
        # Every WarSails lord's army is created after a posture date that lists him as MARSHAL.
        $r.Invariant.episodes | Should Be (20 + 21 + 30 + 31 + 12 + 13)
        $r.Invariant.cleared | Should Be $r.Invariant.episodes
        $r.Invariant.ambiguous | Should Be 0
        $r.Output | Should Match 'result: invariant Passed, armydays Passed -> exit 0'
    }

    It 'fails army-days when WarSails army-days are cut to 30 %' {
        $r = Invoke-Comparer (New-Job $script:noisy 0.3)
        $r.Exit | Should Be 2
        $r.ArmyDays.verdict | Should Be 'Failed'
        $r.ArmyDays.p | Should Be ([Math]::Round(2 / 216, 4))
        ($r.ArmyDays.d -lt 0) | Should Be $true
        $r.Invariant.verdict | Should Be 'Passed'
    }

    It 'counts army-days without ruler armies' {
        $r = Invoke-Comparer (New-Job $script:noisy)
        $r.Doc.runs[0].armyDays | Should Be 20
        $r.Doc.runs[0].rulerArmyDays | Should Be 1
        $r.ArmyDays.coMean | Should Be ([Math]::Round((18 + 22 + 33 + 27 + 14 + 11) / 6.0, 4))
    }

    It 'fails the invariant for an injected Defense-only army leader' {
        $r = Invoke-Comparer (New-Job $script:noisy 1.0 @{ 4 = @{ NoOffense = @('Lord 2'); ModLog = @('[Command] Northern Empire delegates Defense to Lord 2') } })
        $r.Exit | Should Be 2
        $r.Invariant.verdict | Should Be 'Failed'
        @($r.Invariant.violations).Count | Should Be 1
        $r.Invariant.violations[0] | Should Match 'r4 .*Lord 2 \[lord_2\].*never held Offense \(held Defense\)'
        $r.Invariant.possibleViolators | Should Be 1
        $r.ArmyDays.verdict | Should Be 'Passed'
    }

    It 'shows a Failed invariant in the console even when the exit code is 3' {
        $r = Invoke-Comparer (New-Job $script:noisy 1.0 @{ 4 = @{ NoOffense = @('Lord 2') }; 6 = @{ Status = 'Hung' } })
        $r.Exit | Should Be 3
        $r.Invariant.verdict | Should Be 'Failed'
        $r.Output | Should Match 'result: invariant Failed, armydays Inconclusive -> exit 3'
        $r.Output | Should Match 'FAIL  r4 '
    }

    It 'treats an Offense delegate who is not MARSHAL on the posture date before his army as ambiguous, which blocks a Passed' {
        $r = Invoke-Comparer (New-Job $script:noisy 1.0 @{ 1 = @{ NoOffense = @('Lord 1'); ModLog = @('[Command] Northern Empire delegates Defense to Lord 0', '[Command] Northern Empire delegates Offense to Lord 1', '[Command] Northern Empire delegates Naval to Lord 1') } })
        $r.Invariant.verdict | Should Be 'Inconclusive'
        $r.Invariant.detail | Should Match ' - 1 ambiguous army episode\(s\): r1 Lord 1 \[lord_1\] day 2'
        ($r.Invariant.cleared -gt 0) | Should Be $true
        $r.Invariant.possibleViolators | Should Be 0
        $r.Invariant.ambiguous | Should Be 1
        $r.Invariant.ambiguousEpisodes[0] | Should Match 'r1 .*Lord 1 \[lord_1\] raised an army on day 2 .*ambiguous: held Offense only as delegate \(delegation of Year 1084, Summer 1\)'
        $r.Output | Should Match 'WARN  r1 .*Lord 1 \[lord_1\].*ambiguous'
    }

    It 'no longer clears an Offense delegate once his delegation was replaced' {
        $mod = @('[Command] Northern Empire delegates Offense to Lord 1') + @(Get-PostureSection 1 @('Lord 1')) +
            @('=== Year 1084, Summer 2 - chain of command ===', '[Command] Northern Empire delegates Offense to Lord 5')
        $r = Invoke-Comparer (New-Job $script:noisy 1.0 @{ 1 = @{ NoOffense = @('Lord 1'); ModLog = $mod } })
        $r.Invariant.verdict | Should Be 'Failed'
        @($r.Invariant.violations).Count | Should Be 1
        $r.Invariant.violations[0] | Should Match 'r1 .*Lord 1 \[lord_1\] raised an army on day 2 \(Year 1084, Summer 3\); Offense lost before the army \(last Offense line Year 1084, Summer 1, not MARSHAL on Year 1084, Summer 2\)'
    }

    It 'fails a lord who lost his MARSHAL post before he raised his army' {
        # The daily tick of Summer 2 drops Lord 0: both his armies come after it (Summer 2 and, with
        # no block on Summer 3, the kept Summer 2 set of a build without the clear marker) and are
        # violations.
        $r = Invoke-Comparer (New-Job $script:noisy 1.0 @{ 1 = @{ ModLog = @(Get-PostureSection 1 @('Lord 0')); Digest = @("created day=2 kingdom=$($script:kingdom) leader=lord_0|Lord 0|0") } })
        $r.Invariant.verdict | Should Be 'Failed'
        @($r.Invariant.violations).Count | Should Be 2
        $r.Invariant.violations[0] | Should Match 'r1 .*Lord 0 \[lord_0\] raised an army on day 1 \(Year 1084, Summer 2\); Offense lost before the army \(last Offense line Year 1084, Summer 1, not MARSHAL on Year 1084, Summer 2\)'
        $r.Invariant.violations[1] | Should Match 'r1 .*Lord 0 \[lord_0\] raised an army on day 2 \(Year 1084, Summer 3\); Offense lost before the army \(last Offense line Year 1084, Summer 1, not MARSHAL on Year 1084, Summer 2\)'
        $r.Invariant.ambiguous | Should Be 0
    }

    It 'fails a lord listed by the daily tick before his army but dropped by the one his army follows' {
        # Listed on Summer 2, dropped on Summer 3, army after the Summer 3 tick: governed by Summer 3.
        $mod = @(Get-PostureSection 1) + @(Get-PostureSection 2 @('Lord 1'))
        $r = Invoke-Comparer (New-Job $script:noisy 1.0 @{ 1 = @{ ModLog = $mod } })
        $r.Invariant.verdict | Should Be 'Failed'
        @($r.Invariant.violations).Count | Should Be 1
        $r.Invariant.violations[0] | Should Match 'r1 .*Lord 1 \[lord_1\] raised an army on day 2 \(Year 1084, Summer 3\); Offense lost before the army \(last Offense line Year 1084, Summer 2, not MARSHAL on Year 1084, Summer 3\)'
        $r.Invariant.ambiguous | Should Be 0
    }

    It 'clears a lord listed by the daily tick his army follows but dropped by the next one' {
        # Lord 1 is appointed by the Summer 3 tick and dropped by the Summer 4 tick; his army comes
        # between the two. Judged by its calendar date alone (Summer 3 against Summer 2) it would
        # be ambiguous.
        $mod = @(Get-PostureSection 1 @('Lord 1')) + @(Get-PostureSection 2) + @(Get-PostureSection 3 @('Lord 1'))
        $r = Invoke-Comparer (New-Job $script:noisy 1.0 @{ 1 = @{ NoOffense = @('Lord 1'); ModLog = $mod; Days = 4 } })
        $r.Doc.runs[0].valid | Should Be $true
        @($r.Invariant.violations).Count | Should Be 0
        $r.Invariant.ambiguous | Should Be 0
        $r.Invariant.cleared | Should Be $r.Invariant.episodes
    }

    It 'fails a lord who is MARSHAL neither on the day before his army nor on its own day' {
        $mod = @(Get-PostureSection 1 @('Lord 1')) + @(Get-PostureSection 2 @('Lord 1'))
        $r = Invoke-Comparer (New-Job $script:noisy 1.0 @{ 1 = @{ ModLog = $mod } })
        $r.Invariant.verdict | Should Be 'Failed'
        @($r.Invariant.violations).Count | Should Be 1
        $r.Invariant.violations[0] | Should Match 'Lord 1 \[lord_1\] raised an army on day 2 \(Year 1084, Summer 3\); Offense lost before the army \(last Offense line Year 1084, Summer 1, not MARSHAL on Year 1084, Summer 3\)'
    }

    It 'clears a lord who is MARSHAL both on the day before his army and on its own day' {
        $mod = @(Get-PostureSection 1) + @(Get-PostureSection 2)
        $r = Invoke-Comparer (New-Job $script:noisy 1.0 @{ 1 = @{ ModLog = $mod } })
        $r.Invariant.verdict | Should Be 'Passed'
        $r.Invariant.ambiguous | Should Be 0
        $r.Invariant.cleared | Should Be $r.Invariant.episodes
    }

    It 'uses the last earlier block of a kingdom that has no block on the day before the army (build without the clear marker)' {
        # Summer 2 has a posture section, but no Northern Empire block: an older build kept the
        # Summer 1 set, which lists Lord 1 like Summer 3 does.
        $mod = @(Get-ForeignPostureSection 1) + @(Get-PostureSection 2)
        $r = Invoke-Comparer (New-Job $script:noisy 1.0 @{ 1 = @{ ModLog = $mod } })
        $r.Invariant.verdict | Should Be 'Passed'
        $r.Invariant.ambiguous | Should Be 0
        $r.Invariant.cleared | Should Be $r.Invariant.episodes
    }

    It 'treats a kingdom without a block on the day before the army as holding no Offense (build with the clear marker)' {
        # Same log as above plus the clear marker (T-0118); the launch marker is written by default.
        # The Summer 2 tick and the filler Summer 4 tick have no Northern Empire block and leave the
        # kingdom without holders: the 7 armies of day 1 and the 6 of day 3 are violations. The
        # armies after the Summer 3 tick, whose block lists everyone, stay cleared.
        $mod = @(Get-ForeignPostureSection 1) + @(Get-PostureSection 2) + @($script:clearMarker)
        $r = Invoke-Comparer (New-Job $script:noisy 1.0 @{ 1 = @{ ModLog = $mod } })
        $r.Invariant.verdict | Should Be 'Failed'
        $violations = @($r.Invariant.violations)
        $violations.Count | Should Be 13
        @($violations | Where-Object { $_ -match 'raised an army on day 1 \(Year 1084, Summer 2\); .*not MARSHAL on Year 1084, Summer 2\)$' }).Count | Should Be 7
        @($violations | Where-Object { $_ -match 'raised an army on day 3 \(Year 1084, Summer 4\); .*not MARSHAL on Year 1084, Summer 4\)$' }).Count | Should Be 6
        $r.Invariant.ambiguous | Should Be 0
        $r.Invariant.cleared | Should Be ($r.Invariant.episodes - 13)
    }

    It 'reports a lord MARSHAL only on the posture date right before his army as ambiguous without tick order (clear marker)' {
        # G = Summer 3 (block without Lord X), P = Summer 2, the posture date right before it (X).
        $mod = @(Get-PostureSection 1 @() @('Lord X')) + @(Get-PostureSection 2) + @($script:clearMarker)
        $r = Invoke-Comparer (New-Job $script:noisy 1.0 @{ 1 = @{ NoDailyTicks = $true; ModLog = $mod; Digest = @("created day=2 kingdom=$($script:kingdom) leader=lord_x|Lord X|0 calendar=Year 1084, Summer 3") } })
        @($r.Invariant.violations | Where-Object { $_ -match 'Lord X' }).Count | Should Be 0
        $x = @($r.Invariant.ambiguousEpisodes | Where-Object { $_ -match 'Lord X' })
        $x.Count | Should Be 1
        $x[0] | Should Match 'Lord X \[lord_x\] raised an army on day 2 \(Year 1084, Summer 3\); ambiguous: MARSHAL on Year 1084, Summer 2 but not on Year 1084, Summer 3'
    }

    It 'convicts a lord whose last MARSHAL block lies further back than the posture date before his army without tick order (clear marker)' {
        # G = Summer 4 (block without Lord X); Summer 3 has no Northern Empire block, so P is empty
        # and the Summer 2 block that lists X no longer counts.
        $mod = @(Get-PostureSection 1 @() @('Lord X')) + @(Get-ForeignPostureSection 2) + @(Get-PostureSection 3) + @($script:clearMarker)
        $r = Invoke-Comparer (New-Job $script:noisy 1.0 @{ 1 = @{ NoDailyTicks = $true; ModLog = $mod; Digest = @("created day=3 kingdom=$($script:kingdom) leader=lord_x|Lord X|0 calendar=Year 1084, Summer 4") } })
        @($r.Invariant.ambiguousEpisodes | Where-Object { $_ -match 'Lord X' }).Count | Should Be 0
        $x = @($r.Invariant.violations | Where-Object { $_ -match 'Lord X' })
        $x.Count | Should Be 1
        $x[0] | Should Match 'Lord X \[lord_x\] raised an army on day 3 \(Year 1084, Summer 4\); Offense lost before the army \(last Offense line Year 1084, Summer 2, MARSHAL neither on Year 1084, Summer 3 nor on Year 1084, Summer 4\)'
    }

    It 'takes P as empty when the posture date before the army has no block without tick order (clear marker)' {
        # Lord X is MARSHAL on Summer 2 and Summer 4 (G); Summer 3 has no Northern Empire block, so
        # P is empty: ambiguous. Without the marker P would be Summer 2 and the army cleared.
        $mod = @(Get-PostureSection 1 @() @('Lord X')) + @(Get-ForeignPostureSection 2) + @(Get-PostureSection 3 @() @('Lord X'))
        $digest = @("created day=3 kingdom=$($script:kingdom) leader=lord_x|Lord X|0 calendar=Year 1084, Summer 4")
        $r = Invoke-Comparer (New-Job $script:noisy 1.0 @{ 1 = @{ NoDailyTicks = $true; ModLog = ($mod + @($script:clearMarker)); Digest = $digest } })
        $x = @($r.Invariant.ambiguousEpisodes | Where-Object { $_ -match 'Lord X' })
        $x.Count | Should Be 1
        $x[0] | Should Match 'Lord X \[lord_x\] raised an army on day 3 \(Year 1084, Summer 4\); ambiguous: MARSHAL on Year 1084, Summer 4 but not on Year 1084, Summer 3'
        $old = Invoke-Comparer (New-Job $script:noisy 1.0 @{ 1 = @{ NoDailyTicks = $true; ModLog = $mod; Digest = $digest } })
        @($old.Invariant.ambiguousEpisodes | Where-Object { $_ -match 'Lord X' }).Count | Should Be 0
        @($old.Invariant.violations | Where-Object { $_ -match 'Lord X' }).Count | Should Be 0
    }

    It 'convicts an army on the start date of a kingdom without a session-launch block (clear marker)' {
        # Without the marker the same log is ambiguous ("no posture block on or before").
        $digest = @("created day=0 kingdom=$($script:kingdom) leader=lord_0|Lord 0|0")
        $r = Invoke-Comparer (New-Job $script:noisy 1.0 @{ 1 = @{ ForeignLaunch = $true; ModLog = @($script:clearMarker); Digest = $digest } })
        $x = @($r.Invariant.violations | Where-Object { $_ -match 'raised an army on day 0 ' })
        $x.Count | Should Be 1
        $x[0] | Should Match 'Lord 0 \[lord_0\] raised an army on day 0 '
        $old = Invoke-Comparer (New-Job $script:noisy 1.0 @{ 1 = @{ ForeignLaunch = $true; Digest = $digest } })
        @($old.Invariant.ambiguousEpisodes | Where-Object { $_ -match 'raised an army on day 0 .*no posture block on or before' }).Count | Should Be 1
    }

    It 'judges an army created after a daily tick but before its date line by that tick' {
        # The Manteos case (job b0f0 r1): the created line follows "DailyTick 2 done" but precedes
        # the date line of day 2, and says day=1 without calendar. Lord X is MARSHAL from the
        # Summer 3 posture (tick 2) on: by tick order cleared, by its day (Summer 2) a violation.
        $mod = @(Get-PostureSection 2 @() @('Lord X'))
        $r = Invoke-Comparer (New-Job $script:noisy 1.0 @{ 1 = @{ ModLog = $mod; AfterTick = @{ 2 = @("created day=1 kingdom=$($script:kingdom) leader=lord_x|Lord X|0") } } })
        $r.Invariant.verdict | Should Be 'Passed'
        @($r.Invariant.violations).Count | Should Be 0
        $r.Invariant.ambiguous | Should Be 0
        $r.Invariant.cleared | Should Be $r.Invariant.episodes
        @($r.Doc.warnings | Where-Object { $_ -match 'tick order unavailable' }).Count | Should Be 0
    }

    It 'does not clear an army created before the first daily tick by the session-launch posture of a build without launch marker' {
        # The probe jobC case: such builds wipe the launch holder set right after it is published,
        # so Lord 0's MARSHAL line at launch proves nothing for his army before DailyTick 1.
        # Summer 2 lists him for his day-1 army, which is cleared.
        $r = Invoke-Comparer (New-Job $script:noisy 1.0 @{ 1 = @{ NoLaunchMarker = $true; ModLog = @(Get-PostureSection 1); Digest = @("created day=0 kingdom=$($script:kingdom) leader=lord_0|Lord 0|0") } })
        $r.Invariant.verdict | Should Be 'Inconclusive'
        @($r.Invariant.violations).Count | Should Be 0
        $r.Invariant.ambiguous | Should Be 1
        $r.Invariant.ambiguousEpisodes[0] | Should Match 'r1 .*Lord 0 \[lord_0\] raised an army on day 0 \(Year 1084, Summer 1\); ambiguous: MARSHAL on the session-launch posture of Year 1084, Summer 1, but session-launch holders reset by CommandBehavior \(build without launch marker\)'
        $r.Invariant.cleared | Should Be ($r.Invariant.episodes - 1)
    }

    It 'ignores a launch marker that does not follow the session-launch posture section' {
        # The marker only counts between the session-launch section and the first daily one.
        $mod = @(Get-PostureSection 1) + @('[Command] Offense holders from session launch: 1 kingdom(s)')
        $r = Invoke-Comparer (New-Job $script:noisy 1.0 @{ 1 = @{ NoLaunchMarker = $true; ModLog = $mod; Digest = @("created day=0 kingdom=$($script:kingdom) leader=lord_0|Lord 0|0") } })
        $r.Invariant.verdict | Should Be 'Inconclusive'
        $r.Invariant.ambiguousEpisodes[0] | Should Match 'build without launch marker'
    }

    It 'does not let a later date fall back to the session-launch set of a build without launch marker' {
        # No Northern Empire block in any daily tick: the mod "kept" the launch set, which such a
        # build had wiped. Every army after a daily tick is then ambiguous, not cleared.
        $r = Invoke-Comparer (New-Job $script:noisy 1.0 @{ 1 = @{ NoLaunchMarker = $true } })
        $r.Invariant.verdict | Should Be 'Inconclusive'
        @($r.Invariant.violations).Count | Should Be 0
        @($r.Invariant.ambiguousEpisodes | Where-Object { $_ -match '^r1 .*ambiguous: MARSHAL on the session-launch posture of Year 1084, Summer 1, but session-launch holders reset by CommandBehavior' }).Count | Should Be 20
        $r.Invariant.cleared | Should Be ($r.Invariant.episodes - 20)
    }

    It 'judges by the daily blocks of a build without launch marker as usual' {
        # Summer 2 lists everyone: the session-launch set no longer matters for the armies after it.
        $r = Invoke-Comparer (New-Job $script:noisy 1.0 @{ 1 = @{ NoLaunchMarker = $true; ModLog = @(Get-PostureSection 1) } })
        $r.Invariant.verdict | Should Be 'Passed'
        $r.Invariant.ambiguous | Should Be 0
        $r.Invariant.cleared | Should Be $r.Invariant.episodes
    }

    It 'fails an army created before the first daily tick when the session-launch posture does not list him' {
        # Without launch marker a lord missing from the launch block is judged as usual: he held
        # Offense neither by MARSHAL line nor by delegation when he raised his army.
        $r = Invoke-Comparer (New-Job $script:noisy 1.0 @{ 1 = @{ NoLaunchMarker = $true; NoOffense = @('Lord 0'); ModLog = @(Get-PostureSection 1); Digest = @("created day=0 kingdom=$($script:kingdom) leader=lord_0|Lord 0|0") } })
        $r.Invariant.verdict | Should Be 'Failed'
        @($r.Invariant.violations).Count | Should Be 1
        $r.Invariant.violations[0] | Should Match 'Lord 0 \[lord_0\] raised an army on day 0 \(Year 1084, Summer 1\); Offense only from Year 1084, Summer 2, after the army'
    }

    It 'warns when a created line has a calendar earlier than the daily tick it follows' {
        $mod = @(Get-PostureSection 2 @() @('Lord X'))
        $r = Invoke-Comparer (New-Job $script:noisy 1.0 @{ 1 = @{ ModLog = $mod; AfterTick = @{ 2 = @("created day=1 kingdom=$($script:kingdom) leader=lord_x|Lord X|0 calendar=Year 1084, Summer 2") } } })
        $r.Invariant.verdict | Should Be 'Passed'
        $r.Invariant.cleared | Should Be $r.Invariant.episodes
        @($r.Doc.warnings | Where-Object { $_ -match 'r1 Northern Empire: created line of Lord X \[lord_x\] on day 1 has calendar Year 1084, Summer 2, earlier than the posture tick it follows \(Year 1084, Summer 3\); judged by tick order' }).Count | Should Be 1
    }

    It 'falls back to the calendar date and warns when harness.log has no DailyTick line' {
        # Without tick order: Lord X (MARSHAL from Summer 3) with a created line of day 1 and
        # calendar Summer 3 is judged by G (Summer 3) and P (Summer 1) and is ambiguous.
        $mod = @(Get-PostureSection 2 @() @('Lord X'))
        $r = Invoke-Comparer (New-Job $script:noisy 1.0 @{ 1 = @{ NoDailyTicks = $true; ModLog = $mod; Digest = @("created day=1 kingdom=$($script:kingdom) leader=lord_x|Lord X|0 calendar=Year 1084, Summer 3") } })
        @($r.Invariant.violations).Count | Should Be 0
        $r.Invariant.ambiguous | Should Be 1
        $r.Invariant.ambiguousEpisodes[0] | Should Match 'Lord X \[lord_x\] raised an army on day 1 \(Year 1084, Summer 3\); ambiguous: MARSHAL on Year 1084, Summer 3 but not on Year 1084, Summer 1'
        @($r.Doc.warnings | Where-Object { $_ -match "^r1: harness.log has no 'DailyTick <n> done' line, tick order unavailable" }).Count | Should Be 1
        @($r.Doc.warnings | Where-Object { $_ -match 'tick order unavailable' }).Count | Should Be 1
    }

    It 'falls back to the date line for a created line without calendar and without tick order' {
        $mod = @(Get-PostureSection 2 @() @('Lord X'))
        $r = Invoke-Comparer (New-Job $script:noisy 1.0 @{ 1 = @{ NoDailyTicks = $true; ModLog = $mod; Digest = @("created day=1 kingdom=$($script:kingdom) leader=lord_x|Lord X|0") } })
        @($r.Invariant.violations).Count | Should Be 1
        $r.Invariant.violations[0] | Should Match 'Lord X \[lord_x\] raised an army on day 1 \(Year 1084, Summer 2\); Offense only from Year 1084, Summer 3, after the army'
    }

    It 'passes when a ruler who holds Defense leads an army' {
        $r = Invoke-Comparer (New-Job $script:noisy 1.0 @{ 1 = @{ ModLog = @('[Command] Northern Empire delegates Defense to Lucon') } })
        $r.Exit | Should Be 0
        $r.Invariant.verdict | Should Be 'Passed'
        $r.Invariant.detail | Should Match '6 rulers exempt'
        $r.Invariant.possibleViolators | Should Be 0
    }

    It 'exempts a player-clan leader (4th digest field 1) who holds Defense like a ruler' {
        $r = Invoke-Comparer (New-Job $script:noisy 1.0 @{ 1 = @{ Leaders = @('lord_pc|Player Lord|0|1'); ModLog = @('[Command] Northern Empire delegates Defense to Player Lord') } })
        $r.Exit | Should Be 0
        $r.Invariant.verdict | Should Be 'Passed'
        $r.Invariant.possibleViolators | Should Be 0
        $r.Invariant.rulersExempt | Should Be 7
    }

    It 'still judges a leader with 4th digest field 0 and an old 3-field entry as non-exempt' {
        foreach ($entry in 'lord_pc|Player Lord|0|0', 'lord_pc|Player Lord|0') {
            $r = Invoke-Comparer (New-Job $script:noisy 1.0 @{ 1 = @{ Leaders = @($entry); ModLog = @('[Command] Northern Empire delegates Defense to Player Lord') } })
            $r.Invariant.possibleViolators | Should Be 1
            $r.Invariant.rulersExempt | Should Be 6
        }
    }

    It 'clears a lord appointed MARSHAL by the daily tick his army follows' {
        # Not MARSHAL at launch, appointed by the Summer 2 tick; his army comes after that tick.
        $r = Invoke-Comparer (New-Job $script:noisy 1.0 @{ 1 = @{ NoOffense = @('Lord 0'); ModLog = @(Get-PostureSection 1) } })
        $r.Invariant.verdict | Should Be 'Passed'
        @($r.Invariant.violations).Count | Should Be 0
        $r.Invariant.ambiguous | Should Be 0
        $r.Invariant.cleared | Should Be $r.Invariant.episodes
    }

    It 'reports a lord appointed MARSHAL on his creation day as ambiguous without tick order' {
        $r = Invoke-Comparer (New-Job $script:noisy 1.0 @{ 1 = @{ NoDailyTicks = $true; NoOffense = @('Lord 0'); ModLog = @(Get-PostureSection 1) } })
        $r.Invariant.verdict | Should Be 'Inconclusive'
        @($r.Invariant.violations).Count | Should Be 0
        $r.Invariant.ambiguous | Should Be 1
        $r.Invariant.ambiguousEpisodes[0] | Should Match 'r1 .*Lord 0 \[lord_0\] raised an army on day 1 \(Year 1084, Summer 2\); ambiguous: MARSHAL on Year 1084, Summer 2 but not on Year 1084, Summer 1; whether the army came before or after the posture tick of Year 1084, Summer 2 is not logged'
    }

    It 'clears an army created on the start date when the session-launch posture lists him and the build keeps it' {
        # The default run writes the launch marker: the launch holder set is in force until DailyTick 1.
        $r = Invoke-Comparer (New-Job $script:noisy 1.0 @{ 1 = @{ Digest = @("created day=0 kingdom=$($script:kingdom) leader=lord_0|Lord 0|0") } })
        $r.Invariant.verdict | Should Be 'Passed'
        $r.Invariant.ambiguous | Should Be 0
        # Lord 0 has two episodes in r1 (start date and day 1), both cleared.
        $r.Invariant.episodes | Should Be (20 + 21 + 30 + 31 + 12 + 13 + 1)
        $r.Invariant.cleared | Should Be $r.Invariant.episodes
    }

    It 'fails an army created on the start date when the session-launch posture does not list him, with or without launch marker' {
        foreach ($noMarker in $false, $true) {
            $r = Invoke-Comparer (New-Job $script:noisy 1.0 @{ 1 = @{ NoLaunchMarker = $noMarker; Digest = @("created day=0 kingdom=$($script:kingdom) leader=lord_40|Lord 40|0"); ModLog = @('[Command] Northern Empire delegates Defense to Lord 40') + @(Get-PostureSection 1) } })
            $r.Invariant.verdict | Should Be 'Failed'
            $r.Invariant.violations[0] | Should Match 'Lord 40 \[lord_40\] raised an army on day 0 \(Year 1084, Summer 1\); never held Offense \(held Defense\)'
        }
    }

    It 'judges a start date with two posture ticks against both without tick order' {
        # Session launch and the daily tick on the same date: Lord 0 is dropped by the second.
        $mod = @(Get-PostureSection 0 @('Lord 0'))
        $r = Invoke-Comparer (New-Job $script:noisy 1.0 @{ 1 = @{ NoDailyTicks = $true; ModLog = $mod; Digest = @("created day=0 kingdom=$($script:kingdom) leader=lord_0|Lord 0|0") } })
        $r.Invariant.ambiguousEpisodes[0] | Should Match 'Lord 0 \[lord_0\] raised an army on day 0 \(Year 1084, Summer 1\); ambiguous: MARSHAL on the earlier posture of Year 1084, Summer 1 but not on Year 1084, Summer 1'
    }

    It 'is Inconclusive when every army episode is ambiguous' {
        # Both WarSails runs: Lord 0 holds Offense only as delegate when he raises his army.
        $delegateOnly = @{ NoOffense = @('Lord 0'); ModLog = @('[Command] Northern Empire delegates Offense to Lord 0') }
        $r = Invoke-Comparer (New-Job @(, @(1, 1, 1, 1)) 1.0 @{ 1 = $delegateOnly; 4 = $delegateOnly })
        $r.Invariant.verdict | Should Be 'Inconclusive'
        $r.Invariant.ambiguous | Should Be 2
        $r.Invariant.cleared | Should Be 0
        $r.Invariant.detail | Should Match ' - 2 ambiguous army episode\(s\): r1 Lord 0 \[lord_0\] day 1, r4 Lord 0 \[lord_0\] day 1'
    }

    It 'treats a MARSHAL line as ambiguous for an army whose creation was not observed' {
        $r = Invoke-Comparer (New-Job $script:noisy 1.0 @{ 1 = @{ Leaders = @('lord_39|Lord 39|0') } })
        # An unobserved creation is ambiguous like any other episode, so it blocks a Passed.
        $r.Invariant.verdict | Should Be 'Inconclusive'
        $r.Invariant.detail | Should Match ' - 1 ambiguous army episode\(s\): r1 Lord 39 \[lord_39\] day 1'
        $r.Invariant.ambiguous | Should Be 1
        $r.Invariant.ambiguousEpisodes[0] | Should Match 'Lord 39 \[lord_39\] led an army \(creation not observed\).*ambiguous: MARSHAL on the session-launch posture of Year 1084, Summer 1, but the creation was not observed'
    }

    It 'does not let an attacks slot exonerate a leader' {
        # A second posture section of the same date replaces the first (the later tick's marshals).
        $attacks = @(Get-PostureSection 0 @('Lord 2')) + @('[Posture]   attacks: Lord 2 (strength 100, valor 0)')
        $r = Invoke-Comparer (New-Job $script:noisy 1.0 @{ 1 = @{ NoOffense = @('Lord 2'); ModLog = $attacks } })
        @($r.Invariant.violations).Count | Should Be 1
        $r.Invariant.verdict | Should Be 'Failed'
        $r.Invariant.violations[0] | Should Match 'Lord 2 \[lord_2\] raised an army.*never held Offense \(held Attack\)'
    }

    It 'fails an unlinked non-ruler leader and counts him' {
        $r = Invoke-Comparer (New-Job $script:noisy 1.0 @{ 1 = @{ Leaders = @('lord_u|Stranger|0'); Digest = @("created day=1 kingdom=$($script:kingdom) leader=lord_u|Stranger|0") } })
        $r.Invariant.verdict | Should Be 'Failed'
        $r.Invariant.unlinked | Should Be 1
        $r.Invariant.violations[0] | Should Match 'Stranger \[lord_u\] raised an army.*unlinked: no entry in the mod log'
    }

    It 'never counts an army whose creation was not observed as a violation' {
        # Not in the mod log at all, and no created line: still only ambiguous (a lord leading a
        # host is listed as MARSHAL only while he ranks among the attackers).
        $r = Invoke-Comparer (New-Job $script:noisy 1.0 @{ 1 = @{ Leaders = @('lord_u|Stranger|0') } })
        $r.Invariant.verdict | Should Be 'Inconclusive'
        @($r.Invariant.violations).Count | Should Be 0
        $r.Invariant.unlinked | Should Be 1
        $r.Invariant.unobservedCreations | Should Be 1
        $r.Invariant.ambiguousEpisodes[0] | Should Match 'Stranger \[lord_u\] led an army \(creation not observed\).*ambiguous: creation not observed'
    }

    It 'parses kingdom blocks whose threat share is formatted by another culture' {
        # {3:P0} under e.g. tr-TR writes "%27"; the block must still replace the holder set.
        $header = '[Posture] Northern Empire: 17 parties, 13 war(s), %27 of realm threatened -> 4 may attack, 13 defend'
        $r = Invoke-Comparer (New-Job $script:noisy 1.0 @{ 1 = @{ ModLog = @(Get-PostureSection 1 @('Lord 1') @() $header) } })
        $r.Invariant.verdict | Should Be 'Failed'
        $r.Invariant.violations[0] | Should Match 'Lord 1 \[lord_1\] raised an army on day 2 \(Year 1084, Summer 3\); Offense lost before the army'
        @($r.Doc.warnings | Where-Object { $_ -match 'no parsable' }).Count | Should Be 0
    }

    It 'warns when the mod log has war posture sections but no parsable kingdom block' {
        $job = New-Job $script:noisy
        $broken = @(Get-PostureSection 0 @() @() '[Posture] Northern Empire - 17 parties') + @('=== Year 1084, Summer 1 - chain of command ===', '[Command] Northern Empire delegates Naval to Lucon')
        [IO.File]::WriteAllLines((Join-Path $job 'r4\CoherentWarAI.log'), [string[]]$broken)
        $r = Invoke-Comparer $job
        $r.Output | Should Match "WARN  r4: CoherentWarAI.log has 1 war posture section\(s\) but no parsable '\[Posture\] <kingdom>: <n> parties, ' block line"
    }

    It 'gives RigError when a WarSails mod log is missing' {
        $r = Invoke-Comparer (New-Job $script:noisy 1.0 @{ 1 = @{ NoModLog = $true } })
        $r.Exit | Should Be 1
        $r.Invariant.verdict | Should Be 'RigError'
        $r.ArmyDays.verdict | Should Be 'RigError'
        $r.Doc.runs[0].reason | Should Be 'CoherentWarAI.log missing'
        $r.Doc.runs[0].rigError | Should Be $true
    }

    It 'gives RigError when a run-result.json is missing' {
        $job = New-Job $script:noisy
        Remove-Item (Join-Path $job 'r5\run-result.json')
        $r = Invoke-Comparer $job
        $r.Exit | Should Be 1
        $r.Doc.runs[4].reason | Should Be 'run-result.json missing'
        $r.ArmyDays.verdict | Should Be 'RigError'
        # Mode unknown: the run may have been a WarSails run.
        $r.Invariant.verdict | Should Be 'RigError'
    }

    It 'gives RigError for a missing or empty harness.log of a ChainOff run' {
        $job = New-Job $script:noisy
        Remove-Item (Join-Path $job 'r2\harness.log')
        [IO.File]::WriteAllText((Join-Path $job 'r3\harness.log'), '')
        $r = Invoke-Comparer $job
        $r.Exit | Should Be 1
        $r.Doc.runs[1].reason | Should Be 'harness.log missing'
        $r.Doc.runs[2].reason | Should Be 'harness.log empty'
        $r.ArmyDays.verdict | Should Be 'RigError'
        $r.Invariant.verdict | Should Be 'Passed'
        $r.Output | Should Match 'result: invariant Passed, armydays RigError -> exit 1'
    }

    It 'gives RigError when a completed WarSails run has an empty mod log' {
        $job = New-Job $script:noisy
        [IO.File]::WriteAllText((Join-Path $job 'r4\CoherentWarAI.log'), " `r`n")
        $r = Invoke-Comparer $job
        $r.Exit | Should Be 1
        $r.Doc.runs[3].reason | Should Be 'CoherentWarAI.log empty'
        $r.Doc.runs[3].rigError | Should Be $true
        $r.Invariant.verdict | Should Be 'RigError'
        $r.ArmyDays.verdict | Should Be 'RigError'
    }

    It 'makes the invariant Inconclusive when a WarSails mod log has no delegates line' {
        $job = New-Job $script:noisy
        [IO.File]::WriteAllLines((Join-Path $job 'r4\CoherentWarAI.log'), [string[]]@(Get-PostureSection 0))
        $r = Invoke-Comparer $job
        $r.Invariant.verdict | Should Be 'Inconclusive'
        $r.Doc.runs[3].reason | Should Match 'no delegates line'
        $r.Doc.runs[3].rigError | Should Be $false
    }

    It 'judges the realistic fixture (real 57a7 mod-log lines, 46ce digest format)' {
        # Battania: Brighan MARSHAL from Summer 6, army after the Summer 7 tick -> cleared. Aserai:
        # Tais MARSHAL on Summer 7 only, his created line (day 1, ctx.Day lagging) follows
        # "DailyTick 2 done" (Summer 7) -> cleared; Ruma only ever "attacks", army after the Summer 8
        # tick -> violation; Nuqar (MARSHAL on Summer 8) leads an army not seen being created ->
        # ambiguous. Caladog is ruler.
        $r = Invoke-Comparer (Copy-RealisticJob)
        $r.Invariant.verdict | Should Be 'Failed'
        $r.Invariant.cleared | Should Be 2
        $r.Invariant.ambiguous | Should Be 1
        @($r.Invariant.violations).Count | Should Be 1
        $r.Invariant.violations[0] | Should Match 'r1 \(seed 1\) Aserai: Ruma \[lord_3_4\] raised an army on day 3 \(Year 1084, Summer 8\); never held Offense \(held Attack\)'
        $r.Invariant.ambiguousEpisodes[0] | Should Match 'Aserai: Nuqar \[lord_3_12\] led an army \(creation not observed\) on day 3 \(Year 1084, Summer 8\); ambiguous: MARSHAL on Year 1084, Summer 8, but the creation was not observed'
        $r.Invariant.rulersExempt | Should Be 1
        $r.Invariant.possibleViolators | Should Be 0
        @($r.Doc.warnings | Where-Object { $_ -match 'no parsable|tick order unavailable|earlier than the posture tick' }).Count | Should Be 0
    }

    It 'judges the realistic fixture by tick order when the created lines lack calendar' {
        # The older digest format: Tais's day-1 line would fall on Summer 6 by its day, but it
        # follows the Summer 7 tick.
        $r = Invoke-Comparer (Copy-RealisticJob -StripCalendar)
        @($r.Invariant.violations).Count | Should Be 1
        $r.Invariant.cleared | Should Be 2
        @($r.Invariant.violations | Where-Object { $_ -match 'Tais' }).Count | Should Be 0
    }

    It 'judges the realistic fixture by calendar date when harness.log has no DailyTick line' {
        $r = Invoke-Comparer (Copy-RealisticJob -StripDailyTicks)
        $r.Invariant.cleared | Should Be 1
        $r.Invariant.ambiguous | Should Be 2
        $r.Invariant.ambiguousEpisodes[1] | Should Match 'Aserai: Tais \[lord_3_9\] raised an army on day 1 \(Year 1084, Summer 7\); ambiguous: MARSHAL on Year 1084, Summer 7 but not on Year 1084, Summer 6'
        $r.Output | Should Match "WARN  r1: harness.log has no 'DailyTick <n> done' line, tick order unavailable"
    }

    It 'makes a run unusable whose mod log stops before a governing posture section' {
        # The probe jobB case: Lord 1 is dropped by the Summer 3 tick and raises his army after it,
        # a violation. A failed flush ends the log before that section; falling back to the kept
        # Summer 2 set would clear him.
        $mod = @(Get-PostureSection 1) + @('=== Year 1084, Summer 2 - chain of command ===') + @(Get-PostureSection 2 @('Lord 1'))
        $job = New-Job $script:noisy 1.0 @{ 1 = @{ ModLog = $mod } }
        (Invoke-Comparer $job).Invariant.verdict | Should Be 'Failed'
        Limit-ModLog $job 1 2
        $r = Invoke-Comparer $job
        $r.Invariant.verdict | Should Be 'Inconclusive'
        @($r.Invariant.violations).Count | Should Be 0
        $r.Doc.runs[0].valid | Should Be $false
        $r.Doc.runs[0].reason | Should Be 'CoherentWarAI.log lacks a complete war posture section for Year 1084, Summer 3 (missing), Year 1084, Summer 4 (missing) (log cut off or posture tick not logged)'
        $r.Invariant.detail | Should Match 'a WarSails run \(or a run of unknown mode\) is not usable'
    }

    It 'makes a run unusable whose governing posture section was cut off' {
        # A partial flush ends the log right after the Summer 3 header: the section has no
        # Northern Empire block, but that does not mean the kingdom had no candidates.
        $mod = @(Get-PostureSection 1) + @(Get-PostureSection 2 @('Lord 1'))
        $job = New-Job $script:noisy 1.0 @{ 1 = @{ ModLog = $mod } }
        Limit-ModLog $job 1 2 -KeepHeader
        $r = Invoke-Comparer $job
        $r.Invariant.verdict | Should Be 'Inconclusive'
        $r.Doc.runs[0].reason | Should Match 'section for Year 1084, Summer 3 \(cut off\), Year 1084, Summer 4 \(missing\) '
    }

    It 'makes a run unusable when the posture section of a governing date is missing' {
        # Summer 3 is not in the log at all while Summer 4 is: a no-block fallback for Summer 3
        # would rest on a tick that was never logged.
        $job = New-Job $script:noisy
        Remove-PostureSectionFromLog $job 1 2
        $r = Invoke-Comparer $job
        $r.Invariant.verdict | Should Be 'Inconclusive'
        $r.Doc.runs[0].reason | Should Be 'CoherentWarAI.log lacks a complete war posture section for Year 1084, Summer 3 (missing) (log cut off or posture tick not logged)'
    }

    It 'makes a run unusable whose mod log does not reach the last daily tick' {
        # No army after the Summer 4 tick, but the log must still cover it.
        $job = New-Job $script:noisy 1.0 @{ 1 = @{ ArmyDays = 2 } }
        Limit-ModLog $job 1 3
        $r = Invoke-Comparer $job
        $r.Doc.runs[0].reason | Should Be 'CoherentWarAI.log lacks a complete war posture section for Year 1084, Summer 4 (missing) (log cut off or posture tick not logged)'
        $r.Invariant.verdict | Should Be 'Inconclusive'
    }

    It 'makes a run unusable whose harness.log lost a DailyTick line' {
        # Lord 1 is MARSHAL on Summer 2 only and raises his army after the Summer 3 tick. Without
        # "DailyTick 2 done" his army would be dated by tick 1 (Summer 2) and cleared.
        $mod = @(Get-PostureSection 1) + @(Get-PostureSection 2 @('Lord 1'))
        $job = New-Job $script:noisy 1.0 @{ 1 = @{ ModLog = $mod } }
        Remove-HarnessLine $job 1 ' DailyTick 2 done '
        $r = Invoke-Comparer $job
        $r.Invariant.verdict | Should Be 'Inconclusive'
        @($r.Invariant.violations).Count | Should Be 0
        $r.Doc.runs[0].reason | Should Be 'harness.log tick order broken (date line of day 2 after DailyTick 1; DailyTick 3 where DailyTick 2 was due); armies cannot be dated by their daily tick'
    }

    It 'makes a run unusable whose first DailyTick line is lost' {
        # Without "DailyTick 1 done" every army after it would count as before the first tick.
        $job = New-Job $script:noisy
        Remove-HarnessLine $job 1 ' DailyTick 1 done '
        $r = Invoke-Comparer $job
        $r.Doc.runs[0].reason | Should Match '^harness.log tick order broken \(date line of day 1 before any DailyTick line; DailyTick 2 where DailyTick 1 was due\)'
    }

    It 'makes a run unusable when a created calendar is later than the next tick' {
        # The created line follows "DailyTick 1 done" (Summer 2) but says Summer 4: the Summer 3
        # tick line was lost. By the Summer 2 posture Lord X would be cleared.
        $mod = @(Get-PostureSection 1 @() @('Lord X'))
        $r = Invoke-Comparer (New-Job $script:noisy 1.0 @{ 1 = @{ ModLog = $mod; AfterTick = @{ 1 = @("created day=1 kingdom=$($script:kingdom) leader=lord_x|Lord X|0 calendar=Year 1084, Summer 4") } } })
        $r.Invariant.verdict | Should Be 'Inconclusive'
        @($r.Invariant.violations).Count | Should Be 0
        $r.Doc.runs[0].reason | Should Be 'created line(s) dated after the posture tick they follow (Lord X [lord_x] calendar Year 1084, Summer 4 after the next tick of Year 1084, Summer 3): a DailyTick line is missing'
    }

    It 'accepts a created calendar of the next day before the next tick' {
        # The daily tick fires in the morning: an army raised after midnight but before the
        # Summer 3 tick says Summer 3 and is still governed by the Summer 2 posture (live job
        # 20261103-210923-95c9 r1, Caladog and Muinser).
        $mod = @(Get-PostureSection 1 @() @('Lord X'))
        $r = Invoke-Comparer (New-Job $script:noisy 1.0 @{ 1 = @{ ModLog = $mod; AfterTick = @{ 1 = @("created day=1 kingdom=$($script:kingdom) leader=lord_x|Lord X|0 calendar=Year 1084, Summer 3") } } })
        $r.Doc.runs[0].valid | Should Be $true
        @($r.Invariant.violations).Count | Should Be 0
        $r.Invariant.ambiguous | Should Be 0
        $r.Invariant.cleared | Should Be $r.Invariant.episodes
    }

    It 'is Inconclusive when one ChainOff run hung' {
        $r = Invoke-Comparer (New-Job $script:noisy 1.0 @{ 6 = @{ Status = 'Hung' } })
        $r.Exit | Should Be 3
        $r.ArmyDays.verdict | Should Be 'Inconclusive'
        $r.ArmyDays.detail | Should Match 'r6: status Hung'
        $r.Invariant.verdict | Should Be 'Passed'
    }

    It 'makes the invariant Inconclusive when a WarSails run hung' {
        $r = Invoke-Comparer (New-Job $script:noisy 1.0 @{ 1 = @{ Status = 'Hung' } })
        $r.Exit | Should Be 3
        $r.Invariant.verdict | Should Be 'Inconclusive'
    }

    It 'marks a run unusable whose digest scenario did not pass or is not listed' {
        $r = Invoke-Comparer (New-Job $script:noisy 1.0 @{ 1 = @{ ScenarioStatus = 'Failed' }; 2 = @{ ScenarioStatus = '' } })
        $r.Exit | Should Be 3
        $r.Doc.runs[0].reason | Should Be 'scenario coherentwarai.ac25a-digest Failed'
        $r.Doc.runs[1].reason | Should Be 'run-result.json has no coherentwarai.ac25a-digest scenario entry'
        $r.Doc.runs[0].rigError | Should Be $false
        $r.Invariant.verdict | Should Be 'Inconclusive'
        $r.ArmyDays.verdict | Should Be 'Inconclusive'
    }

    It 'marks a run unusable whose digest repeats a (day, kingdom) line' {
        $dup = "day=2 kingdom=$($script:kingdom) armies=1 leaders=lord_1|Lord 1|0"
        $r = Invoke-Comparer (New-Job $script:noisy 1.0 @{ 2 = @{ Digest = @($dup) } })
        $r.Exit | Should Be 3
        $r.Doc.runs[1].valid | Should Be $false
        $r.Doc.runs[1].reason | Should Match 'digest repeats \(day, kingdom\) line\(s\): day 2 empire'
        $r.ArmyDays.verdict | Should Be 'Inconclusive'
    }

    It 'is Inconclusive when a seed has only one arm' {
        $r = Invoke-Comparer (New-Job $script:noisy 1.0 @{ 10 = @{ Mode = 'WarSails' }; 11 = @{ Mode = 'WarSails' } })
        $r.Exit | Should Be 3
        $r.ArmyDays.verdict | Should Be 'Inconclusive'
        $r.ArmyDays.detail | Should Match 'seed\(s\) 3 lack'
    }

    It 'rejects a run whose digest does not cover the requested days' {
        $r = Invoke-Comparer (New-Job $script:noisy 1.0 @{ 2 = @{ DigestDays = 2 } })
        $r.ArmyDays.verdict | Should Be 'Inconclusive'
        $r.Doc.runs[1].valid | Should Be $false
        $r.Doc.runs[1].reason | Should Match 'digest covers 2 days, expected days 1\.\.3'
    }

    It 'accepts a digest that starts after day 0' {
        $r = Invoke-Comparer (New-Job $script:noisy 1.0 @{ 1 = @{ StartDay = 5 }; 2 = @{ StartDay = 5 } })
        $r.Exit | Should Be 0
        $r.Doc.runs[0].startDay | Should Be 5
        $r.Doc.runs[0].daysReached | Should Be 8
    }

    It 'gives the exact p of a small known case, but no Passed on zero noise' {
        # Only seed 1 differs (WS 20/20, CO 10/10); seeds 2 and 3 are constant. Of the 6 labellings
        # of seed 1, two reach |diff| = 10 and four give 0; seeds 2/3 always give 0 -> p = 72/216 = 1/3.
        $r = Invoke-Comparer (New-Job @(@(20, 10, 10, 20), @(10, 10, 10, 10), @(10, 10, 10, 10)))
        $r.ArmyDays.p | Should Be ([Math]::Round(1 / 3, 4))
        $r.ArmyDays.d | Should Be ([Math]::Round(10 / 3, 4))
        $r.ArmyDays.s | Should Be 0
        $r.ArmyDays.verdict | Should Be 'Inconclusive'
        $r.ArmyDays.detail | Should Match 'MDE not computable'
    }

    It 'reports the smallest attainable p for a perfectly separated case' {
        $r = Invoke-Comparer (New-Job @(@(20, 10, 10, 20), @(21, 11, 11, 21), @(22, 12, 12, 22)))
        $r.ArmyDays.p | Should Be ([Math]::Round(2 / 216, 4))
        $r.ArmyDays.verdict | Should Be 'Failed'
    }

    It 'is Inconclusive, not Passed, without any army-days' {
        $r = Invoke-Comparer (New-Job @(@(0, 0, 0, 0), @(0, 0, 0, 0), @(0, 0, 0, 0)))
        $r.Exit | Should Be 3
        $r.ArmyDays.verdict | Should Be 'Inconclusive'
        $r.ArmyDays.detail | Should Match 'no non-ruler army-days in any run'
        $r.ArmyDays.mdePercent | Should Be $null
        $r.Invariant.verdict | Should Be 'Inconclusive'
    }

    It 'is Inconclusive, not Passed, when the ChainOff mean is 0' {
        $r = Invoke-Comparer (New-Job @(@(1, 0, 0, 0), @(0, 0, 0, 0), @(0, 0, 0, 0)))
        $r.ArmyDays.p | Should Be 1
        $r.ArmyDays.verdict | Should Be 'Inconclusive'
        $r.ArmyDays.detail | Should Match 'ChainOff mean is 0'
    }

    It 'is Inconclusive when the design cannot reach alpha' {
        # One seed, 2 WS + 2 CO: 6 labellings, the smallest p is 2/6 even for total separation.
        $r = Invoke-Comparer (New-Job @(, @(40, 10, 10, 40)))
        $r.Exit | Should Be 3
        $r.ArmyDays.verdict | Should Be 'Inconclusive'
        $r.ArmyDays.minP | Should Be ([Math]::Round(2 / 6, 4))
        $r.ArmyDays.detail | Should Match 'design cannot reach alpha'
    }

    It 'links by hero StringId when the mod log carries it, with the same verdicts as by name' {
        $job = New-Job $script:noisy 1.0 @{ 4 = @{ NoOffense = @('Lord 2'); ModLog = @('[Command] Northern Empire delegates Defense to Lord 2') } }
        foreach ($i in 1..12) { Add-HeroIds $job $i }
        $r = Invoke-Comparer $job
        $r.Invariant.verdict | Should Be 'Failed'
        @($r.Invariant.violations).Count | Should Be 1
        $r.Invariant.violations[0] | Should Match 'r4 .*Lord 2 \[lord_2\].*never held Offense \(held Defense\)'
        $r.Invariant.possibleViolators | Should Be 1
        $r.Invariant.unlinked | Should Be 0
        $r.Invariant.cleared | Should Be ($r.Invariant.episodes - 1)
    }

    It 'links two heroes sharing a name by StringId instead of warning about the name' {
        # lord_x is the MARSHAL "Lord 2" (own id); lord_2 is not MARSHAL at all.
        $job = New-Job $script:noisy 1.0 @{ 1 = @{ Leaders = @('lord_x|Lord 2|0'); NoOffense = @('Lord 2'); ModLog = @(Get-PostureSection 1 @('Lord 2') @('Lord 2 [id=lord_x]')) } }
        Add-HeroIds $job 1
        $r = Invoke-Comparer $job
        @($r.Doc.warnings | Where-Object { $_ -match 'is shared by hero ids' }).Count | Should Be 0
        $r.Invariant.verdict | Should Be 'Failed'
        @($r.Invariant.violations | Where-Object { $_ -match 'Lord 2 \[lord_2\] raised an army' }).Count | Should BeGreaterThan 0
        @($r.Invariant.ambiguousEpisodes | Where-Object { $_ -match 'Lord 2 \[lord_x\] led an army \(creation not observed\).*ambiguous: MARSHAL on' }).Count | Should Be 1
    }

    It 'judges heroes without an id ambiguous, not a violation, when the mod log mixes lines with and without [id=...]' {
        $job = New-Job $script:noisy
        foreach ($i in 1..12) { Add-HeroIds $job $i }
        # Lord 3 of r1 loses the id on his MARSHAL line (as a hero logged without a StringId would).
        $file = Join-Path $job 'r1\CoherentWarAI.log'
        [IO.File]::WriteAllLines($file, [string[]]@([IO.File]::ReadAllLines($file) | ForEach-Object { $_ -replace '^(\[Posture\]\s+MARSHAL: Lord 3) \[id=lord_3\] \(', '$1 (' }))
        $r = Invoke-Comparer $job
        @($r.Invariant.violations).Count | Should Be 0
        $r.Invariant.verdict | Should Be 'Inconclusive'
        $r.Invariant.unlinked | Should BeGreaterThan 0
        @($r.Invariant.ambiguousEpisodes | Where-Object { $_ -match 'Lord 3 \[lord_3\].*ambiguous: unlinked.*mixes hero lines' }).Count | Should BeGreaterThan 0
        @($r.Doc.warnings | Where-Object { $_ -match '^r1: CoherentWarAI\.log mixes hero lines with and without \[id=' }).Count | Should Be 1
    }

    It 'links a hero whose name contains brackets or a pipe by his id' {
        $leaders = @('lord_b|Bob [Jr]|0', 'lord_p|Pipe|Man|0|0')
        $job = New-Job $script:noisy 1.0 @{ 1 = @{ Leaders = $leaders; ModLog = @(Get-PostureSection 1 @() @('Bob [Jr] [id=lord_b]', 'Pipe|Man [id=lord_p]')) } }
        Add-HeroIds $job 1
        $r = Invoke-Comparer $job
        $r.Invariant.unlinked | Should Be 0
        @($r.Doc.warnings | Where-Object { $_ -match 'unreadable leader entry' }).Count | Should Be 0
        @($r.Invariant.violations | Where-Object { $_ -match 'Bob|Pipe' }).Count | Should Be 0
        @($r.Invariant.ambiguousEpisodes | Where-Object { $_ -match 'Bob \[Jr\] \[lord_b\]' }).Count | Should Be 1
        @($r.Invariant.ambiguousEpisodes | Where-Object { $_ -match 'Pipe\|Man \[lord_p\]' }).Count | Should Be 1
    }

    It 'warns when the digest skipped created events and keeps the verdict' {
        $base = Invoke-Comparer (New-Job $script:noisy)
        $r = Invoke-Comparer (New-Job $script:noisy 1.0 @{ 1 = @{ Digest = @('digest done: 3 of 3 days aligned, 6 kingdom lines, 4 created events, 2 skipped (no kingdom or leader), 0 errors') } })
        @($r.Doc.warnings | Where-Object { $_ -match '^r1: digest skipped 2 created event\(s\)' }).Count | Should Be 1
        $r.Invariant.verdict | Should Be $base.Invariant.verdict
        $r.Exit | Should Be $base.Exit
        @($base.Doc.warnings | Where-Object { $_ -match 'digest skipped' }).Count | Should Be 0
    }

    It 'does not warn about skipped created events when the digest skipped none' {
        $r = Invoke-Comparer (Copy-RealisticJob)
        @($r.Doc.warnings | Where-Object { $_ -match 'digest skipped' }).Count | Should Be 0
    }

    It 'warns about an ambiguous leader name instead of judging it' {
        $r = Invoke-Comparer (New-Job $script:noisy 1.0 @{ 1 = @{ Leaders = @('lord_x|Lord 2|0'); NoOffense = @('Lord 2') } })
        @($r.Doc.warnings | Where-Object { $_ -match "name 'Lord 2' is shared by hero ids lord_2, lord_x" }).Count | Should Be 1
        $r.Invariant.verdict | Should Be 'Passed'
    }

    It 'computes noise s as the pooled within-cell standard deviation and the MDE with t(df)' {
        $r = Invoke-Comparer (New-Job $script:noisy)
        # cells (WS,CO) per seed: (20,21)(18,22)(30,31)(33,27)(12,13)(14,11); ss = 0.5+8+0.5+18+0.5+4.5 = 32, df 6
        $mde = (2.447 + 0.906) * [Math]::Sqrt(32 / 6) * [Math]::Sqrt(3) / 3
        $r.ArmyDays.s | Should Be ([Math]::Round([Math]::Sqrt(32 / 6), 4))
        $r.ArmyDays.df | Should Be 6
        $r.ArmyDays.mde | Should Be ([Math]::Round($mde, 4))
        $r.ArmyDays.mdePercent | Should Be ([Math]::Round(100 * $mde / ((18 + 22 + 33 + 27 + 14 + 11) / 6.0), 4))
        $r.ArmyDays.detail | Should Match 'MDE ~4\.5 army-days \(21 % of CO, t, df 6\)'
        @($r.Doc.warnings | Where-Object { $_ -match '^armydays: MDE' }).Count | Should Be 0
        $r.ArmyDays.detail | Should Not Match 'weak test'
    }

    It 'keeps Passed but warns when the MDE is above 50 % of the ChainOff mean' {
        # runs per seed are WS, CO, CO, WS: both arms average 10, every cell spreads 4..16
        $r = Invoke-Comparer (New-Job @(@(4, 16, 4, 16), @(6, 14, 6, 14), @(5, 15, 5, 15)))
        $r.Exit | Should Be 0
        $r.ArmyDays.verdict | Should Be 'Passed'
        ($r.ArmyDays.mdePercent -gt 50) | Should Be $true
        @($r.Doc.warnings | Where-Object { $_ -match '^armydays: MDE ~\S+ army-days is \d+ % of CO \(above 50 %\); only large effects are detectable' }).Count | Should Be 1
        $r.Output | Should Match 'WARN  armydays: MDE'
        $r.ArmyDays.detail | Should Match ' - WARN weak test: MDE above 50 % of CO'
    }

    It 'gives the same numbers under a de-DE culture' {
        $r = Invoke-Comparer (New-Job $script:noisy) 'de-DE'
        $r.Exit | Should Be 0
        $r.ArmyDays.s | Should Be ([Math]::Round([Math]::Sqrt(32 / 6), 4))
        $r.Output | Should Match 'noise s=2\.3, p=\d\.\d{3} \(216 perms, min p 0\.009\)'
    }

    It 'gives RigError for a broken run-result.json instead of aborting' {
        $r = Invoke-Comparer (New-Job $script:noisy 1.0 @{ 2 = @{ ResultJson = '{ not json' }; 3 = @{ ResultJson = '{ "status": "Completed", "seed": "1", "mode": "FeatureOff", "daysRequested": 3 }' }; 5 = @{ ResultJson = '{ "status": "Completed" }' } })
        $r.Exit | Should Be 1
        $r.Doc.runs[1].reason | Should Be 'run-result.json not readable'
        $r.Doc.runs[2].reason | Should Be "seed '1' is not an integer"
        $r.Doc.runs[4].reason | Should Be 'run-result.json has no seed'
        $r.ArmyDays.verdict | Should Be 'RigError'
        $r.Invariant.verdict | Should Be 'RigError'
    }

    It 'reports runs planned in the job manifest (out\jobs\<id>\job.json) but missing from the folder' {
        $job = New-Job $script:noisy
        $manifest = Join-Path $TestDrive ("jobs\" + (Split-Path -Leaf $job) + '\job.json')
        New-Manifest $manifest 13
        $r = Invoke-Comparer $job '' $manifest
        $r.Exit | Should Be 3
        @($r.Doc.missingRuns).Count | Should Be 1
        $r.Doc.missingRuns[0] | Should Be 'r13'
        $r.Doc.jobManifest | Should Be $manifest
        $r.ArmyDays.detail | Should Match 'planned run\(s\) r13 missing'
        $r.Invariant.verdict | Should Be 'Inconclusive'
    }

    It 'passes with a complete job manifest and does not warn about a missing plan' {
        $job = New-Job $script:noisy
        $manifest = Join-Path $TestDrive ("jobs\" + (Split-Path -Leaf $job) + '\job.json')
        New-Manifest $manifest 12
        $r = Invoke-Comparer $job '' $manifest
        $r.Exit | Should Be 0
        $r.ArmyDays.detail | Should Not Match 'no job plan'
        @($r.Doc.warnings | Where-Object { $_ -match 'no job plan' }).Count | Should Be 0
    }

    It 'falls back to job.json in the job folder' {
        $job = New-Job $script:noisy
        New-Manifest (Join-Path $job 'job.json') 13
        $r = Invoke-Comparer $job
        $r.Doc.missingRuns[0] | Should Be 'r13'
        $r.ArmyDays.verdict | Should Be 'Inconclusive'
    }

    It 'says so when no job plan is available and does not use job-result.json as plan' {
        $job = New-Job $script:noisy
        $planned = @(1..13 | ForEach-Object { [ordered]@{ runId = "r$_"; mode = 'WarSails' } })
        [IO.File]::WriteAllText((Join-Path $job 'job-result.json'), (ConvertTo-Json -InputObject ([ordered]@{ runs = $planned }) -Depth 4))
        $r = Invoke-Comparer $job
        $r.Exit | Should Be 0
        @($r.Doc.missingRuns).Count | Should Be 0
        $r.ArmyDays.detail | Should Match 'no job plan: missing runs not checked'
        $r.Invariant.detail | Should Match 'no job plan: missing runs not checked'
        $r.Output | Should Match 'WARN  no job plan'
    }

    It 'gives RigError when the given job manifest is missing' {
        $job = New-Job $script:noisy
        $r = Invoke-Comparer $job '' (Join-Path $TestDrive 'jobs\nope\job.json')
        $r.Exit | Should Be 1
        $r.Invariant.verdict | Should Be 'RigError'
        $r.ArmyDays.verdict | Should Be 'RigError'
        $r.ArmyDays.detail | Should Match 'job manifest .*missing'
    }

    # CW1 layout per seed WS, FO, CO, CO, FO, WS: WS and CO as in $script:noisy, FO about three times higher.
    $script:cw1Values = @(@(20, 60, 18, 22, 66, 21), @(30, 95, 33, 27, 90, 31), @(12, 40, 14, 11, 36, 13))
    $script:cw1ChainOffRuns = @{ 3 = @{ NoModLog = $true }; 4 = @{ NoModLog = $true }; 9 = @{ NoModLog = $true }; 10 = @{ NoModLog = $true }; 15 = @{ NoModLog = $true }; 16 = @{ NoModLog = $true } }

    It 'judges army-days WarSails vs ChainOff in an 18-run CW1 job and reports the doctrine effect vs FeatureOff as info' {
        $r = Invoke-Comparer (New-Job $script:cw1Values 1.0 $script:cw1ChainOffRuns $script:cw1Layout)
        $r.Exit | Should Be 0
        @($r.Doc.runs).Count | Should Be 18
        @($r.Doc.runs | Where-Object { $_.valid }).Count | Should Be 18
        $r.ArmyDays.verdict | Should Be 'Passed'
        $r.ArmyDays.permutations | Should Be 216
        $r.ArmyDays.coMean | Should Be ([Math]::Round((18 + 22 + 33 + 27 + 14 + 11) / 6.0, 4))
        $r.ArmyDays.s | Should Be ([Math]::Round([Math]::Sqrt(32 / 6), 4))
        $r.ArmyDays.detail | Should Match '^D=\S+ army-days \(\S+ % of CO 20\.8\)'
        # The doctrine effect is info only: perfectly separated, yet the verdict stays Passed.
        $r.ArmyDays.detail | Should Match '; doctrine effect vs FO: D=-?\d+\.\d army-days \(\S+ % of FO \d+\.\d\), p=0\.009 \(216 perms\)'
        $r.ArmyDays.doctrine.available | Should Be $true
        $r.ArmyDays.doctrine.p | Should Be ([Math]::Round(2 / 216, 4))
        $r.ArmyDays.doctrine.foMean | Should Be ([Math]::Round((60 + 66 + 95 + 90 + 40 + 36) / 6.0, 4))
        ($r.ArmyDays.doctrine.d -lt 0) | Should Be $true
        $r.Doc.kingdoms[0].coMean | Should Be ([Math]::Round((18 + 22 + 33 + 27 + 14 + 11) / 6.0, 4))
        $r.Doc.kingdoms[0].foMean | Should Be ([Math]::Round((60 + 66 + 95 + 90 + 40 + 36) / 6.0, 4))
        # ChainOff runs (no mod log needed) do not leak into the WarSails-only invariant.
        $r.Invariant.verdict | Should Be 'Passed'
        $r.Invariant.episodes | Should Be (20 + 21 + 30 + 31 + 12 + 13)
    }

    It 'is Inconclusive for a job without ChainOff runs but still reports the doctrine effect' {
        $r = Invoke-Comparer (New-Job $script:noisy 1.0 @{} $script:oldLayout)
        $r.Exit | Should Be 3
        $r.ArmyDays.verdict | Should Be 'Inconclusive'
        $r.ArmyDays.detail | Should Match '^no ChainOff baseline runs'
        $r.ArmyDays.detail | Should Match '; doctrine effect vs FO: D=\S+ army-days .*p=\d\.\d{3} \(216 perms\)'
        $r.ArmyDays.doctrine.permutations | Should Be 216
        $r.Invariant.verdict | Should Be 'Passed'
        $r.Output | Should Match 'result: invariant Passed, armydays Inconclusive -> exit 3'
    }

    It 'keeps the ChainOff verdict when a FeatureOff run is unusable or missing, and drops only the doctrine info' {
        # The hung r2 left a partial digest, including a kingdom no usable run has.
        $job = New-Job $script:cw1Values 1.0 @{ 2 = @{ Status = 'Hung'; Digest = @('day=1 kingdom=vlandia|Vlandia armies=1 leaders=lord_v|Lord V|0') } } $script:cw1Layout
        Remove-Item -Recurse (Join-Path $job 'r5')
        New-Manifest (Join-Path $job 'job.json') 18 $script:cw1Layout
        $r = Invoke-Comparer $job
        $r.Exit | Should Be 0
        $r.ArmyDays.verdict | Should Be 'Passed'
        $r.ArmyDays.detail | Should Match '; doctrine effect vs FO: n/a \(.*r2: status Hung'
        $r.ArmyDays.doctrine.available | Should Be $false
        $r.Invariant.verdict | Should Be 'Passed'
        # Per-kingdom rows use usable runs only: no row for Vlandia, FO mean and ruler share without r2.
        @($r.Doc.kingdoms).Count | Should Be 1
        $r.Doc.kingdoms[0].kingdomId | Should Be 'empire'
        $r.Doc.kingdoms[0].foMean | Should Be ([Math]::Round((95 + 90 + 40 + 36) / 4.0, 4))
        $r.Doc.kingdoms[0].rulerShareFo | Should Be ([Math]::Round(4 / (95 + 90 + 40 + 36 + 4.0), 4))
        $r.Doc.kingdoms[0].rulerShareCo | Should Be ([Math]::Round(6 / (18 + 22 + 33 + 27 + 14 + 11 + 6.0), 4))
        $r.Output | Should Match 'ruler share WS/CO/FO'
    }

    It 'keeps the ChainOff verdict when only a planned FeatureOff run is missing' {
        $job = New-Job $script:cw1Values 1.0 @{} $script:cw1Layout
        Remove-Item -Recurse (Join-Path $job 'r5')
        New-Manifest (Join-Path $job 'job.json') 18 $script:cw1Layout
        $r = Invoke-Comparer $job
        $r.Exit | Should Be 0
        $r.ArmyDays.verdict | Should Be 'Passed'
        $r.ArmyDays.detail | Should Match '; doctrine effect vs FO: n/a \(planned run\(s\) r5 missing\)'
        $r.ArmyDays.doctrine.available | Should Be $false
    }

    It 'keeps the ChainOff verdict when a seed has only FeatureOff runs' {
        $job = New-Job $script:cw1Values 1.0 @{} $script:cw1Layout
        New-Run -Job $job -Index 19 -Seed 4 -Mode 'FeatureOff' -ArmyDays 50
        $r = Invoke-Comparer $job
        $r.Exit | Should Be 0
        $r.ArmyDays.verdict | Should Be 'Passed'
        $r.ArmyDays.permutations | Should Be 216
        $r.ArmyDays.detail | Should Match '; doctrine effect vs FO: n/a \(seed\(s\) 4 lack a WarSails or a FeatureOff run\)'
    }

    It 'makes army-days, not the invariant, Inconclusive when a planned ChainOff run is missing' {
        $job = New-Job $script:cw1Values 1.0 @{} $script:cw1Layout
        Remove-Item -Recurse (Join-Path $job 'r3')
        New-Manifest (Join-Path $job 'job.json') 18 $script:cw1Layout
        $r = Invoke-Comparer $job
        $r.Exit | Should Be 3
        $r.ArmyDays.verdict | Should Be 'Inconclusive'
        $r.ArmyDays.detail | Should Match '^planned run\(s\) r3 missing'
        $r.Invariant.verdict | Should Be 'Passed'
    }

    It 'still rejects an unexpected mode' {
        $r = Invoke-Comparer (New-Job $script:noisy 1.0 @{ 2 = @{ Mode = 'NoDlc' } })
        $r.Doc.runs[1].reason | Should Be 'unexpected mode NoDlc'
        $r.ArmyDays.verdict | Should Be 'Inconclusive'
    }

    It 'exits 1 for a missing job folder' {
        $null = & powershell -NoProfile -File $script:comparer -JobPath (Join-Path $TestDrive 'nope')
        $LASTEXITCODE | Should Be 1
    }
}
