<#
.SYNOPSIS
    CW-1: judges COMMAND-SLICE-2A.md AC25a (army composition) across a test rig job of
    WarSails, FeatureOff and ChainOff runs.

.DESCRIPTION
    Reads every run folder r<N> of a job folder:
      run-result.json    seed (integer), mode, status, daysRequested, daysReached, and the
                         scenarios[] entry of coherentwarai.ac25a-digest
      harness.log        the ac25a v1 digest written by the coherentwarai.ac25a-digest scenario:
                           ac25a v1 date day=<d> calendar=Year <y>, <Season> <n>
                           ac25a v1 day=<d> kingdom=<id>|<name> armies=<n> leaders=<heroId>|<name>|<ruler01>;...
                           ac25a v1 created day=<d> kingdom=<id>|<name> leader=<heroId>|<name>|<ruler01> calendar=Year <y>, <Season> <n>
                         A date line is written for the start day and for every digested day; it maps
                         the harness day to the calendar date the mod log uses in its headers. The
                         trailing calendar= field of a created line is the calendar date at the event
                         itself; older digests lack it. The same log holds the rig's own
                           <seconds> DailyTick <n> done (hours=...)
                         line, written from inside the daily-tick dispatch; the date line of day n
                         follows it. One writer writes all these lines in event order (see "governing
                         posture" below).
      CoherentWarAI.log  "=== Year <y>, <Season> <n> - <section> ===" headers, and under them
                         "[Command] <kingdom> delegates <Domain> to <hero>" and, in each
                         "war posture" section, one "[Posture] <kingdom>: <n> parties, ..." block per
                         kingdom with its "[Posture]   MARSHAL|attacks: <hero>" lines. Only the
                         "<kingdom>: <n> parties, " head of the block line is matched: the threat share
                         after it is culture-formatted ({3:P0}). A WARN says so when the log has war
                         posture sections but not one kingdom block could be parsed.

    Verdicts per check: Passed / Failed / Inconclusive / RigError.

    coherentwarai.ac25a-invariant
        Doctrine D1 (chain of command on): a non-ruler lord may raise an army only while he
        holds Offense. The army model asks CommandBehavior.HoldsDomain(Offense) at the moment
        of the army: the lord is in the kingdom's published Offense holder set or is its
        current Offense delegate. WarPostureBehavior replaces that holder set once per posture
        tick (daily tick, plus once at session launch) with the MARSHAL lines of the kingdom's
        posture block. A kingdom without a block in a tick (no candidates) holds no Offense
        after it in builds that write "[Command] Offense holders cleared each posture tick"
        (the clear marker, T-0118); older builds kept its previous set, and their logs are
        still judged that way. "attacks" slots never count, neither do Defense or Naval.
        Session launch: the first "war posture" section of the mod log. Builds that do not write
        "[Command] Offense holders from session launch: <n> kingdom(s)" between it and the next
        war posture section (the launch marker) wipe the launch holder set right after it is
        published (CommandBehavior.OnSessionLaunched runs after WarPostureBehavior's), so until a
        kingdom's first daily block only its Offense delegate counts. Without the marker a MARSHAL
        line of the session-launch block therefore neither clears nor convicts (ambiguous, reason
        "session-launch holders reset by CommandBehavior (build without launch marker)").
        Episodes: every army led by a non-ruler in a WarSails run - one per created event (one
        per hero and governing posture tick), plus one for the first daily snapshot of a leader
        whose army was not seen being created (it predates the digest, or its event was skipped).
        Governing posture: vanilla creates AI armies only in the hourly party AI
        (AiPartyThinkBehavior.PartyHourlyAiTick), which the campaign runs after the whole daily
        tick dispatch (where the posture is published), never inside it. So an army is
        governed by the posture of the most recent daily tick before it. In harness.log that is
        the last "DailyTick <n> done" line before the created line; its calendar date D is the
        date line of day n (which may follow the created line: ctx.Day, and so the created
        line's day=, only advances when the scenario runner ticks, after the daily tick). The
        governing block G is the kingdom's last daily block dated D, or, when the kingdom has
        none on D (no candidates in that tick), its last block before D (the set the mod kept;
        the session-launch block only when no daily block came before, subject to the launch
        marker rule). A created line before the first DailyTick line is governed by the
        session-launch posture: the kingdom's block in the session-launch section, which must
        be dated on the start date (date line of the start day). A snapshot episode is governed
        by the daily tick of its day. The date reported for an episode is D (or the start date).
        Cross-checks: a created line whose own calendar= field is earlier than D is impossible
        (the army came after that tick); a WARN says so, the episode is judged by tick order.
        The daily tick fires in the morning, so calendar= may be D or the next day (an army
        raised between midnight and the next tick). A calendar= later than the date of the
        next daily tick means a DailyTick line was lost: the run is unusable.
        Fallback when harness.log has no DailyTick line at all (WARN "tick order unavailable"):
        the episode is dated C, the created line's own calendar= field, without it the date
        line of its harness day, and two posture blocks of the kingdom can have governed it:
          G  the kingdom's last block dated C, or, when it has none on C, its last block before C;
          P  the block in force before G: the kingdom's previous block on C when C has two, else
             its last block before C, else G itself (the start date).
        The episode is
          cleared    when the leader is MARSHAL in G (by tick order) or in both G and P (fallback);
          ambiguous  (reported as WARN; any ambiguous episode makes the verdict Inconclusive) when
                       - the creation was not observed (snapshot episode): a lord already
                         leading a host ranks first for the attack slots but is listed as
                         MARSHAL only while the marshal quota has room, so the posture log can
                         neither clear nor convict him;
                       - fallback only: he is MARSHAL in exactly one of G and P (the log does not
                         order the army against the posture tick of C);
                       - the MARSHAL line that would clear him is in the session-launch block and
                         the mod log has no launch marker;
                       - he is not MARSHAL, but the kingdom's Offense delegate (the last
                         "delegates Offense" line dated on or before D, in the fallback on or
                         before C or the date of P) names him: the mod reclaims a delegation
                         without a log line, so a delegation can neither clear nor convict him;
                       - his kingdom has no posture block on or before D (C), in a build
                         without the clear marker;
          a violation otherwise: never held Offense, held it only after the governing posture,
                     lost it before it (for example replaced as delegate), or "unlinked" (no
                     mod-log entry at all; counted separately because a name mismatch between
                     digest and mod log looks the same). With the clear marker a kingdom without
                     a block on D (C) holds no Offense, so its army is judged like that of a
                     lord who is not MARSHAL - also when the kingdom has no block at all.
        Leaders are linked to the mod log by hero StringId when its MARSHAL/attacks and
        "delegates" lines carry " [id=<StringId>]" after the hero name (then shared names do
        not matter); a mod log without ids (older builds) is linked by (kingdom name, hero
        name), and a name shared by several hero ids within one kingdom and run cannot be
        linked and is reported as WARN instead of judged. A mod log that mixes lines with and
        without ids is linked by id only: its heroes without an id are judged ambiguous (not a
        violation) and the run gets a warning.
        Rulers (ruler01=1) and player-clan leaders (optional 4th digest field playerClan01=1)
        are exempt - the army model lets rulers raise the royal host by design and never
        constrains the player's clan. Both still count as ordinary (non-ruler) army-days in
        the armydays check; only this invariant exempts them. The detail also reports the
        possible violators: non-ruler lords
        who held Defense or Naval but never Offense in a run (rulers known from the digest
        excluded). Zero possible violators means no lord was in a position to break the
        doctrine the way AC25a describes.
        Verdict: Failed on any violation; else RigError when a WarSails run (or a run of
        unknown mode) has a rig error; else Inconclusive when a WarSails run (or a run of
        unknown mode) is unusable or missing, there is no WarSails run, any episode is ambiguous
        (the detail gives the count and the first three), or no episode was cleared; else
        Passed (at least one cleared, none ambiguous, no violation).
        A WarSails run is unusable for the invariant when
          - its mod log has no parsed "delegates" line (chain of command not active or not logged);
          - its digest lacks a date line for a digested day or for a day whose posture governs an
            episode;
          - its tick order is broken (the rig's Log swallows write errors): the DailyTick numbers
            are not 1, 2, 3, ... in order, or the last DailyTick line before the date line of a
            day n is not "DailyTick n done" (the start date line of day 0: none before it), or a
            created calendar= is later than the date of the next daily tick (see above);
          - its mod log lacks a complete war posture section for a date whose posture governs a
            non-ruler episode (by tick order: a daily section for D, the session-launch section
            for the start date) or for the date of the digest's last daily tick (without tick
            order: of its last date line). WarAiLog stops writing for good after one failed
            flush, and a size-triggered flush can cut a section; a section counts as complete
            when another section header follows it. So a kingdom without a block on D falls
            back to its earlier set (or, with the clear marker, to no holders) only when the
            posture tick of D was fully logged.

    coherentwarai.ac25a-armydays
        Modes: WarSails (everything on), FeatureOff (only the marshal doctrine off) and ChainOff
        (marshal doctrine on, chain of command and naval orders off - the pre-chain-of-command
        baseline (AC25a, doctrine only); D1 demands zero net change against it).
        Army-days per run = sum over the daily snapshots of armies led by non-rulers (ruler
        armies are not constrained by the doctrine in either mode). D = seed-stratified mean
        difference WarSails - ChainOff (mean over seeds of the per-seed arm means).
        Two-sided exact permutation test: WarSails/ChainOff labels are re-assigned within
        each seed only (runs of one seed are exchangeable under "no effect", runs of
        different seeds are not), every assignment is enumerated, p = share with |D*| >= |D|.
        Being exact, the test is deterministic; no random seed exists.
        Reported alongside: noise s (pooled standard deviation of runs within one seed and
        mode) and a minimum detectable effect MDE = (t(0.975, df) + t(0.80, df)) * SE(D)
        (alpha 0.05 two-sided, power 0.8, df = pooled within-cell degrees of freedom; above
        df 30 the normal approximation 1.960 + 0.842), in army-days and as % of the ChainOff
        mean (mdePercent). The MDE never changes the verdict; a Passed verdict with an MDE
        above 50 % of the ChainOff mean adds a WARN line (only large effects are detectable)
        and "WARN weak test" to its detail.
        Verdict, in this order: RigError when any run has a rig error; Inconclusive when the
        job has no ChainOff run, planned or present ("no ChainOff baseline runs": a job from before
        that mode); Inconclusive when a WarSails, ChainOff or unknown-mode run is unusable, such
        a planned run is missing or a seed of the WarSails/ChainOff runs lacks one of the two; Inconclusive when
        the smallest p the realised design can produce (2 / assignments when every seed has as
        many WarSails as ChainOff runs, else 1 / assignments) is not below 0.05 ("design
        cannot reach alpha"); Failed when p < 0.05; Inconclusive when the data cannot support
        "no difference": no non-ruler army-days in any run, a ChainOff mean of 0, or an MDE
        that is not computable (noise s is 0 or undefined); else Passed.
        Doctrine effect (info, never a verdict): the same statistic for WarSails - FeatureOff,
        appended to the detail ("; doctrine effect vs FO: D=..., p=...") and as the object
        "doctrine" in the JSON. It is n/a (with the reason) when there is no FeatureOff run, a
        WarSails or FeatureOff run is unusable or missing, or a seed lacks one of the two arms;
        an unusable or missing FeatureOff run affects only this info, not the verdict.

    A run is usable when run-result.json is readable with an integer seed and status
    Completed, its coherentwarai.ac25a-digest scenario entry is Passed, the mode is WarSails,
    FeatureOff or ChainOff, the digest has at most one line per (day, kingdom) and covers exactly
    daysRequested consecutive days starting the day after its start date line (the scenario
    may start later than day 0, so the digest's own coverage is compared, not daysReached).
    A run has a rig error (missing or broken artifact) when run-result.json is missing, not
    parseable or lacks seed/mode/status/daysRequested or an integer seed, or - for a
    Completed run - harness.log is missing or empty, or a WarSails run's CoherentWarAI.log
    is missing or empty. A non-Completed run is unusable (its status says why), not a rig error.

    Job plan: the planned runs (runId, mode) come from -JobManifestPath, else from
    <JobPath>\job.json. job-result.json is not used as plan (it is written from the folder
    being checked). A planned run missing from the folder makes the affected verdicts
    Inconclusive. Without a plan, missing-run detection is unavailable: a WARN line and a note
    in both details say so. A given or found plan that is missing or not readable is a rig
    error for both verdicts.

    Per-kingdom rows (mean army-days of WarSails, ChainOff and FeatureOff, the WarSails - ChainOff
    difference and its s, ruler share of all army-days per arm) are info only, keyed by kingdom
    id, and use usable runs only.

    Output: one console line per verdict plus info/FAIL/WARN lines and a closing summary
    line, and the same values as UTF-8 JSON (no BOM) in -ResultPath. Exit code (same
    precedence as the rig, 1 > 3 > 2 > 0):
      1 a verdict is RigError, or the job folder is unusable (no JSON is written then);
      3 a verdict is Inconclusive; 2 a verdict is Failed; 0 all Passed.
    Exit 1 or 3 can hide a Failed verdict; the summary line names every verdict.

.PARAMETER JobPath
    Job result folder holding r1, r2, ... (e.g. out\results\<jobId>).

.PARAMETER JobManifestPath
    The job's plan, job.json (in the rig: out\jobs\<jobId>\job.json). Defaults to
    <JobPath>\job.json when that exists.

.PARAMETER ResultPath
    Where to write the JSON. Defaults to <JobPath>\ac25a-compare.json.

.EXAMPLE
    powershell -NoProfile -File tools\Compare-Ac25a.ps1 -JobPath C:\rig\out\results\20261104-101500-abcd -JobManifestPath C:\rig\out\jobs\20261104-101500-abcd\job.json
#>

param(
    [Parameter(Mandatory = $true)]
    [string]$JobPath,

    [string]$JobManifestPath = '',

    [string]$ResultPath = ''
)

Set-StrictMode -Version 2
$ErrorActionPreference = 'Stop'

$Alpha = 0.05
# Run modes of a CW-1 job: WarSails (all on), FeatureOff (marshal doctrine off only) and ChainOff
# (doctrine on, chain of command and naval orders off - the pre-chain-of-command baseline
# (AC25a, doctrine only) the armydays verdict compares against).
$KnownModes = @('WarSails', 'FeatureOff', 'ChainOff')
# A Passed armydays verdict with an MDE above this share of the ChainOff mean gets a WARN.
$MdeWarnPercent = 50
# Student t quantiles t(0.975, df) and t(0.80, df) for df 1..30 (two-sided alpha 0.05, power 0.8).
$TQuantile975 = @(12.706, 4.303, 3.182, 2.776, 2.571, 2.447, 2.365, 2.306, 2.262, 2.228, 2.201, 2.179, 2.160, 2.145, 2.131,
    2.120, 2.110, 2.101, 2.093, 2.086, 2.080, 2.074, 2.069, 2.064, 2.060, 2.056, 2.052, 2.048, 2.045, 2.042)
$TQuantile80 = @(1.376, 1.061, 0.978, 0.941, 0.920, 0.906, 0.896, 0.889, 0.883, 0.879, 0.876, 0.873, 0.870, 0.868, 0.866,
    0.865, 0.863, 0.862, 0.861, 0.860, 0.859, 0.858, 0.858, 0.857, 0.856, 0.856, 0.855, 0.855, 0.854, 0.854)
$NormalMdeFactor = 1.960 + 0.842
$CheckInvariant = 'coherentwarai.ac25a-invariant'
$CheckArmyDays = 'coherentwarai.ac25a-armydays'
$DigestScenario = 'coherentwarai.ac25a-digest'
$Seasons = @('Spring', 'Summer', 'Autumn', 'Winter')

$datePattern = '\bac25a v1 date day=(\d+) calendar=(.+)$'
$dayPattern = '\bac25a v1 day=(\d+) kingdom=([^|]*)\|(.+?) armies=(\d+) leaders=(.*)$'
# The trailing calendar= field is optional: digests written before it existed lack it.
$createdPattern = '\bac25a v1 created day=(\d+) kingdom=([^|]*)\|(.+?) leader=(.*?)(?: calendar=(.+))?$'
# The rig harness's own line, written from inside the daily-tick dispatch: "<seconds> DailyTick <n> done (hours=...)".
$dailyTickPattern = '^(?:\S+ )?DailyTick (\d+) done\b'
# The 4th field (player-clan flag) is optional: digests written before it existed have three.
$leaderPattern = '^([^|]*)\|(.*?)\|([01])(?:\|([01]))?$'
$calendarPattern = '^Year (\d+), (\w+) (\d+)$'
$sectionHeaderPattern = '^=== (Year \d+, \w+ \d+) - (.*) ===$'
$postureSection = 'war posture'
# The hero name may be followed by " [id=<StringId>]" (builds that log the hero id); the id links
# the line to the digest hero, the name is the fallback for older logs.
$delegatesPattern = '^\[Command\] (.+) delegates (Naval|Defense|Offense) to (.+?)(?: \[id=([^\]]+)\])?$'
# Only the head of the block line: the threat share after it is culture-formatted ({3:P0}).
$kingdomHeaderPattern = '^\[Posture\] (.+): \d+ parties, '
$postureNamePattern = '^\[Posture\]\s+(MARSHAL|attacks): (.+?)(?: \[id=([^\]]+)\])? \(strength'
$digestDonePattern = '\bdigest done: .*?, (\d+) skipped \('
# Written by builds whose CommandBehavior keeps the session-launch Offense holder set.
$launchMarkerPattern = '^\[Command\] Offense holders from session launch: \d+ kingdom\(s\)$'
# Written by builds whose WarPostureBehavior clears every Offense holder set at the start of each
# posture tick (T-0118): there a kingdom without a block on a date holds no Offense that day.
$clearMarkerPattern = '^\[Command\] Offense holders cleared each posture tick$'

$warnings = New-Object System.Collections.ArrayList

# Orderable key of a calendar date "Year <y>, <Season> <n>" (as WarAiLog.GameDate writes it), or -1.
function ConvertTo-DateKey([string]$Text) {
    if ($Text -notmatch $calendarPattern) { return -1 }
    $season = [Array]::IndexOf($Seasons, $Matches[2])
    if ($season -lt 0) { return -1 }
    [int]$Matches[1] * 1000 + $season * 100 + [int]$Matches[3]
}

function ConvertTo-Leader([string]$Text, [int]$Day, [string]$KingdomId, [string]$KingdomName, [bool]$Created) {
    if ($Text -notmatch $leaderPattern) {
        return $null
    }
    [PSCustomObject]@{
        HeroId = $Matches[1]; Name = $Matches[2]; Ruler = ($Matches[3] -eq '1'); PlayerClan = ($Matches[4] -eq '1')
        KingdomId = $KingdomId; Kingdom = $KingdomName; Day = $Day; Created = $Created
        Calendar = -1  # date key of a created line's own calendar= field, -1 when absent
        Tick = -1      # number of the last "DailyTick <n> done" line before this line, -1 when none
    }
}

# Parses the digest lines of one harness.log.
function Read-Digest([string]$Path, [string]$RunName) {
    $days = New-Object 'System.Collections.Generic.HashSet[int]'
    $dayKingdoms = New-Object 'System.Collections.Generic.HashSet[string]'
    $duplicates = New-Object System.Collections.ArrayList
    $dates = @{}
    $leaders = New-Object System.Collections.ArrayList
    $perKingdom = @{}
    $armyDays = 0
    $rulerArmyDays = 0
    $tick = -1
    $tickCount = 0
    # Tick order breaks (the rig's Log swallows write errors, so a lost line leaves no other trace):
    # the session's daily ticks count 1, 2, ... from session launch, and the date line of day n
    # follows "DailyTick n done" (the start date line of day 0 follows none).
    $tickErrors = New-Object System.Collections.ArrayList
    foreach ($line in [IO.File]::ReadAllLines($Path)) {
        if ($line -match $dailyTickPattern) {
            $next = [int]$Matches[1]
            $expected = [Math]::Max($tick, 0) + 1
            if ($next -ne $expected) { [void]$tickErrors.Add("DailyTick $next where DailyTick $expected was due") }
            $tick = $next
            $tickCount++
        }
        elseif ($line -match $datePattern) {
            $day = [int]$Matches[1]
            if ($day -ne [Math]::Max($tick, 0)) {
                $after = if ($tick -lt 0) { 'before any DailyTick line' } else { "after DailyTick $tick" }
                [void]$tickErrors.Add("date line of day $day $after")
            }
            $key = ConvertTo-DateKey $Matches[2]
            if ($key -lt 0) { [void]$script:warnings.Add("$RunName`: unreadable calendar '$($Matches[2])'") }
            else { $dates[$day] = $key }
        }
        elseif ($line -match $dayPattern) {
            $day = [int]$Matches[1]
            [void]$days.Add($day)
            $kingdomId = $Matches[2]
            $kingdomName = $Matches[3]
            $declared = [int]$Matches[4]
            $entries = @($Matches[5] -split ';' | Where-Object { $_ -ne '' })
            if (-not $dayKingdoms.Add("$day|$kingdomId")) {
                # A repeated (day, kingdom) line would count that kingdom's armies twice.
                [void]$duplicates.Add("day $day $kingdomId")
                continue
            }
            if (-not $perKingdom.ContainsKey($kingdomId)) {
                $perKingdom[$kingdomId] = [PSCustomObject]@{ Name = $kingdomName; ArmyDays = 0; RulerArmyDays = 0 }
            }
            if ($entries.Count -ne $declared) {
                [void]$script:warnings.Add("$RunName day $day $kingdomName`: armies=$declared but $($entries.Count) leader entries")
            }
            foreach ($entry in $entries) {
                $leader = ConvertTo-Leader $entry $day $kingdomId $kingdomName $false
                if ($null -eq $leader) {
                    [void]$script:warnings.Add("$RunName`: unreadable leader entry '$entry'")
                    continue
                }
                [void]$leaders.Add($leader)
                if ($leader.Ruler) {
                    $rulerArmyDays++
                    $perKingdom[$kingdomId].RulerArmyDays++
                }
                else {
                    $armyDays++
                    $perKingdom[$kingdomId].ArmyDays++
                }
            }
        }
        elseif ($line -match $digestDonePattern) {
            if ([int]$Matches[1] -gt 0) {
                [void]$script:warnings.Add("$RunName`: digest skipped $($Matches[1]) created event(s) (no kingdom or leader); those armies are seen only in the daily snapshots, as 'creation not observed'")
            }
        }
        elseif ($line -match $createdPattern) {
            $entry = $Matches[4]
            $calendarText = $Matches[5]
            $leader = ConvertTo-Leader $entry ([int]$Matches[1]) $Matches[2] $Matches[3] $true
            if ($null -eq $leader) {
                [void]$script:warnings.Add("$RunName`: unreadable created entry '$entry'")
                continue
            }
            if ($calendarText) {
                $leader.Calendar = ConvertTo-DateKey $calendarText
                if ($leader.Calendar -lt 0) { [void]$script:warnings.Add("$RunName`: unreadable created calendar '$calendarText'; the day's date line is used") }
            }
            $leader.Tick = $tick
            [void]$leaders.Add($leader)
        }
    }
    [PSCustomObject]@{
        Days = $days; Dates = $dates; Leaders = $leaders; PerKingdom = $perKingdom; ArmyDays = $armyDays
        RulerArmyDays = $rulerArmyDays; Duplicates = @($duplicates); TickCount = $tickCount; LastTick = $tick
        TickErrors = @($tickErrors)
    }
}

# Parses one mod log into the Offense timeline per kingdom:
#   Held                "<kingdom>|<hero>" -> roles ever held (Naval/Defense/Offense, Attack for an attacks slot)
#   PostureDates        sorted date keys of the "war posture" sections
#   PostureSections     the "war posture" sections in log order (Date; Launch: the first one, the
#                       session-launch posture; Complete: another section header follows it, so the
#                       section was not cut by a failed or partial flush)
#   LaunchMarker        the "Offense holders from session launch" line follows the session-launch
#                       posture section, before the next one: the build keeps the launch holder set
#   ClearMarker         the "Offense holders cleared each posture tick" line occurs: the build
#                       clears every holder set per posture tick, so no block on a date = no holders
#   Blocks              kingdom -> its posture blocks in log order (Date, Launch, Marshals: the MARSHAL
#                       names); each block is one posture tick's Offense holder set of that kingdom
#   OffenseDelegations  kingdom -> "delegates Offense" lines in log order (Date, Name)
#   OffenseDates        "<kingdom>|<hero>" -> dates of his MARSHAL and Offense delegation lines
#   HasIds              the log carries " [id=<StringId>]" on its hero lines: heroes are then
#                       identified by id (the <hero> above, and the Marshals / Name entries), else by name
function Read-ModLog([string]$Path, [string]$RunName) {
    $held = @{}
    $postureDates = New-Object 'System.Collections.Generic.SortedSet[int]'
    $postureSections = New-Object System.Collections.ArrayList
    $launchMarker = $false
    $clearMarker = $false
    $blocks = @{}
    $blockCount = 0
    $offenseDelegations = @{}
    $offenseDates = @{}
    $delegations = 0
    $hasIds = $false
    $plainHeroLines = 0
    $inPosture = $false
    $currentSection = $null
    $currentKingdom = $null
    $currentBlock = $null
    $currentDate = -1
    foreach ($line in [IO.File]::ReadAllLines($Path)) {
        if ($line -match $sectionHeaderPattern) {
            if ($null -ne $currentSection) { $currentSection.Complete = $true }
            $currentDate = ConvertTo-DateKey $Matches[1]
            $inPosture = $Matches[2] -eq $postureSection -and $currentDate -ge 0
            $currentSection = $null
            if ($inPosture) {
                [void]$postureDates.Add($currentDate)
                $currentSection = [PSCustomObject]@{ Date = $currentDate; Launch = ($postureSections.Count -eq 0); Complete = $false }
                [void]$postureSections.Add($currentSection)
            }
            $currentKingdom = $null
            $currentBlock = $null
            continue
        }
        if ($line -match $launchMarkerPattern -and $postureSections.Count -eq 1) { $launchMarker = $true }
        if ($line -match $clearMarkerPattern) { $clearMarker = $true }
        $key = $null
        $role = $null
        $isOffense = $false
        if ($inPosture -and $line -match $kingdomHeaderPattern) {
            $currentKingdom = $Matches[1]
            $currentBlock = [PSCustomObject]@{ Date = $currentDate; Launch = $currentSection.Launch; Marshals = New-Object 'System.Collections.Generic.HashSet[string]' }
            if (-not $blocks.ContainsKey($currentKingdom)) { $blocks[$currentKingdom] = New-Object System.Collections.ArrayList }
            [void]$blocks[$currentKingdom].Add($currentBlock)
            $blockCount++
            continue
        }
        if ($line -match $delegatesPattern) {
            $delegations++
            $hero = $Matches[3]
            if ($Matches[4]) { $hero = $Matches[4]; $hasIds = $true } else { $plainHeroLines++ }
            $key = $Matches[1] + '|' + $hero
            $role = $Matches[2]
            if ($role -eq 'Offense') {
                if ($currentDate -lt 0) {
                    [void]$script:warnings.Add("$RunName`: Offense delegation before any dated section ignored for timing: $line")
                }
                else {
                    $isOffense = $true
                    if (-not $offenseDelegations.ContainsKey($Matches[1])) { $offenseDelegations[$Matches[1]] = New-Object System.Collections.ArrayList }
                    [void]$offenseDelegations[$Matches[1]].Add([PSCustomObject]@{ Date = $currentDate; Name = $hero })
                }
            }
        }
        elseif ($null -ne $currentKingdom -and $line -match $postureNamePattern) {
            $hero = $Matches[2]
            if ($Matches[3]) { $hero = $Matches[3]; $hasIds = $true } else { $plainHeroLines++ }
            $key = $currentKingdom + '|' + $hero
            if ($Matches[1] -eq 'MARSHAL') {
                $role = 'Offense'
                $isOffense = $true
                [void]$currentBlock.Marshals.Add($hero)
            }
            else { $role = 'Attack' }
        }
        if ($null -ne $key) {
            if (-not $held.ContainsKey($key)) { $held[$key] = New-Object 'System.Collections.Generic.HashSet[string]' }
            [void]$held[$key].Add($role)
            if ($isOffense) {
                if (-not $offenseDates.ContainsKey($key)) { $offenseDates[$key] = New-Object 'System.Collections.Generic.SortedSet[int]' }
                [void]$offenseDates[$key].Add($currentDate)
            }
        }
    }
    if ($postureDates.Count -gt 0 -and $blockCount -eq 0) {
        [void]$script:warnings.Add("$RunName`: CoherentWarAI.log has $($postureDates.Count) war posture section(s) but no parsable '[Posture] <kingdom>: <n> parties, ' block line; no MARSHAL line can clear anyone")
    }
    # Hero lines with and without an id in one log (a hero logged as " [id=]" counts as without):
    # the id mode links by id only, so heroes whose lines have none cannot be linked.
    $mixedIds = $hasIds -and $plainHeroLines -gt 0
    if ($mixedIds) {
        [void]$script:warnings.Add("$RunName`: CoherentWarAI.log mixes hero lines with and without [id=...] ($plainHeroLines without); heroes are linked by id only, so heroes without an id on their lines cannot be linked and are judged ambiguous")
    }
    [PSCustomObject]@{
        Held = $held; PostureDates = @($postureDates); PostureSections = @($postureSections); LaunchMarker = $launchMarker; ClearMarker = $clearMarker; Blocks = $blocks
        OffenseDelegations = $offenseDelegations; OffenseDates = $offenseDates; Delegations = $delegations; HasIds = $hasIds; MixedIds = $mixedIds
    }
}

function Format-DateKey([int]$Key) {
    if ($Key -lt 0) { return 'never' }
    'Year {0}, {1} {2}' -f [Math]::Floor($Key / 1000), $Seasons[[Math]::Floor(($Key % 1000) / 100)], ($Key % 100)
}

# The kingdom's last "delegates Offense" line dated on or before $Date ($null when none). An
# unlogged reclaim may have ended that delegation since.
function Get-OffenseDelegation($ModLog, [string]$Kingdom, [int]$Date) {
    $last = $null
    if ($ModLog.OffenseDelegations.ContainsKey($Kingdom)) {
        foreach ($d in $ModLog.OffenseDelegations[$Kingdom]) { if ($d.Date -le $Date) { $last = $d } }
    }
    $last
}

function New-Judgement([string]$Outcome, [string]$Why) {
    [PSCustomObject]@{ Outcome = $Outcome; Why = $Why }
}

# A posture block without MARSHAL lines: the holder set of a kingdom that had no block in a posture
# tick of a build with the clear marker.
function New-EmptyBlock([int]$Date, [bool]$Launch) {
    [PSCustomObject]@{ Date = $Date; Launch = $Launch; Marshals = New-Object 'System.Collections.Generic.HashSet[string]' }
}

# Build with the clear marker, no tick order: the holder set the posture tick before $Date left -
# the kingdom's last earlier block if it is dated on the posture date right before $Date, else
# none (no block in that tick, or no earlier posture at all).
function Get-ClearedPreviousSet($ModLog, $Before, [int]$Date) {
    $earlierDates = @($ModLog.PostureDates | Where-Object { $_ -lt $Date })
    if ($earlierDates.Count -eq 0) { return New-EmptyBlock $Date $false }
    $previousDate = $earlierDates[-1]
    if ($Before.Count -gt 0 -and $Before[-1].Date -eq $previousDate) { return $Before[-1] }
    New-EmptyBlock $previousDate $false
}

# Judges one army episode against the mod log; see the help for the rules. $Rule says which
# posture block(s) can have governed it:
#   Tick      the army came after the daily tick dated $Date: the kingdom's last daily block
#             dated $Date, else its last block before (G = P);
#   Launch    the army came before the first daily tick, $Date is the start date: the kingdom's
#             session-launch block (G = P);
#   Calendar  no tick order (fallback): G and P of calendar date $Date, as described in the help.
# A session-launch block clears nobody unless the mod log has the launch marker (see the help).
# With the clear marker a missing block is an empty holder set: no block on $Date makes G empty,
# and without tick order P is the block of the posture date right before $Date, empty when the
# kingdom has none there (Get-ClearedPreviousSet). Without it a missing block keeps the last set.
# Read-Run has made sure that the posture section of $Date exists and is complete.
function Get-EpisodeJudgement($ModLog, [string]$Kingdom, [string]$Name, [int]$Date, [bool]$Observed, [string]$Rule) {
    $key = $Kingdom + '|' + $Name
    $dateText = Format-DateKey $Date
    $all = @(if ($ModLog.Blocks.ContainsKey($Kingdom)) { $ModLog.Blocks[$Kingdom] })
    if ($Rule -eq 'Launch') {
        $onC = @($all | Where-Object { $_.Launch })
        $before = @()
    }
    elseif ($Rule -eq 'Tick') {
        $onC = @($all | Where-Object { $_.Date -eq $Date -and -not $_.Launch })
        # The session-launch block is the kingdom's first block, so it is the kept set only when
        # no daily block came before $Date.
        $before = @($all | Where-Object { $_.Date -lt $Date -or $_.Launch })
    }
    else {
        $onC = @($all | Where-Object { $_.Date -eq $Date })
        $before = @($all | Where-Object { $_.Date -lt $Date })
    }
    if ($onC.Count -eq 0 -and $ModLog.ClearMarker) {
        # No block on $Date, build with the clear marker: the posture tick of $Date left the
        # kingdom without holders.
        $g = New-EmptyBlock $Date ($Rule -eq 'Launch')
        $p = if ($Rule -eq 'Calendar') { Get-ClearedPreviousSet $ModLog $before $Date } else { $g }
    }
    elseif ($onC.Count -eq 0 -and $before.Count -eq 0) {
        return New-Judgement 'Ambiguous' "$Kingdom has no posture block on or before $dateText"
    }
    elseif ($onC.Count -eq 0) {
        # No block on $Date (no candidates in that tick), build without the clear marker: the
        # mod kept the last earlier set.
        $g = $before[-1]
        $p = $g
    }
    elseif ($Rule -ne 'Calendar') {
        $g = $onC[-1]
        $p = $g
    }
    else {
        # G: the set in force at the end of the posture tick of C; P: the set in force before it.
        $g = $onC[-1]
        $p = if ($onC.Count -gt 1) { $onC[-2] }
            elseif ($ModLog.ClearMarker) { Get-ClearedPreviousSet $ModLog $before $Date }
            elseif ($before.Count -gt 0) { $before[-1] }
            else { $g }
    }
    $gText = if ($g.Launch) { "the session-launch posture of $(Format-DateKey $g.Date)" } else { Format-DateKey $g.Date }
    $pText = if ($p -eq $g) { $gText } elseif ($p.Date -eq $g.Date) { "the earlier posture of $gText" } else { Format-DateKey $p.Date }
    $inG = $g.Marshals.Contains($Name)
    $inP = $p.Marshals.Contains($Name)

    if (-not $Observed) {
        if ($inG -or $inP) { return New-Judgement 'Ambiguous' "MARSHAL on $(if ($inG) { $gText } else { $pText }), but the creation was not observed and the line may be his own host" }
        return New-Judgement 'Ambiguous' 'creation not observed; a lord already leading a host ranks first for the attack slots but is listed as MARSHAL only while the marshal quota has room'
    }
    # Builds without the launch marker wipe the session-launch holder set right after it is
    # published, so a MARSHAL line of the launch posture neither clears nor convicts.
    if (-not $ModLog.LaunchMarker -and (($inG -and $g.Launch) -or ($inP -and $p.Launch))) {
        $launchBlock = if ($inG -and $g.Launch) { $gText } else { $pText }
        return New-Judgement 'Ambiguous' "MARSHAL on $launchBlock, but session-launch holders reset by CommandBehavior (build without launch marker)"
    }
    if ($inG -and $inP) { return New-Judgement 'Cleared' '' }
    if ($inG) { return New-Judgement 'Ambiguous' "MARSHAL on $gText but not on $pText; whether the army came before or after the posture tick of $dateText is not logged" }
    if ($inP) { return New-Judgement 'Ambiguous' "MARSHAL on $pText but not on $gText; whether the army came before or after the posture tick of $dateText is not logged" }
    # By tick order only the delegation in force on the governing date counts; without it also the one of P.
    $delegationDates = if ($Rule -eq 'Calendar') { @($Date, $p.Date) } else { @($Date) }
    foreach ($delegation in @($delegationDates | ForEach-Object { Get-OffenseDelegation $ModLog $Kingdom $_ })) {
        if ($null -ne $delegation -and $delegation.Name -eq $Name) {
            return New-Judgement 'Ambiguous' "held Offense only as delegate (delegation of $(Format-DateKey $delegation.Date)); a reclaim of the delegation is not logged"
        }
    }

    $offense = @(if ($ModLog.OffenseDates.ContainsKey($key)) { $ModLog.OffenseDates[$key] })
    $earlier = @($offense | Where-Object { $_ -lt $Date })
    $notHeld = if ($p -eq $g) { "not MARSHAL on $gText" } else { "MARSHAL neither on $pText nor on $gText" }
    if ($ModLog.MixedIds -and -not $ModLog.Held.ContainsKey($key)) {
        return New-Judgement 'Ambiguous' 'unlinked: no entry in the mod log, which mixes hero lines with and without [id=...]'
    }
    $why = if (-not $ModLog.Held.ContainsKey($key)) { 'unlinked: no entry in the mod log' }
    elseif ($offense.Count -eq 0) { "never held Offense (held $((@($ModLog.Held[$key]) | Sort-Object) -join '+'))" }
    elseif ($earlier.Count -eq 0) { "Offense only from $(Format-DateKey $offense[0]), after the army" }
    else { "Offense lost before the army (last Offense line $(Format-DateKey $earlier[-1]), $notHeld)" }
    New-Judgement 'Violation' $why
}

function Get-Mean([double[]]$Values) {
    if ($Values.Count -eq 0) { return [double]::NaN }
    ($Values | Measure-Object -Sum).Sum / $Values.Count
}

# Pooled standard deviation of the runs within each (seed, mode) cell, with its degrees of freedom.
function Get-PooledSd($Cells) {
    $ss = 0.0
    $df = 0
    foreach ($cell in $Cells) {
        $values = [double[]]@($cell)
        if ($values.Count -lt 2) { continue }
        $mean = Get-Mean $values
        foreach ($v in $values) { $ss += ($v - $mean) * ($v - $mean) }
        $df += $values.Count - 1
    }
    $s = if ($df -eq 0) { [double]::NaN } else { [Math]::Sqrt($ss / $df) }
    [PSCustomObject]@{ S = $s; Df = $df }
}

# t(0.975, df) + t(0.80, df); the normal approximation above the table.
function Get-MdeFactor([int]$Df) {
    if ($Df -lt 1) { return [double]::NaN }
    if ($Df -le $TQuantile975.Count) { return $TQuantile975[$Df - 1] + $TQuantile80[$Df - 1] }
    $NormalMdeFactor
}

function Get-Binomial([int]$N, [int]$K) {
    $r = 1.0
    for ($i = 1; $i -le $K; $i++) { $r = $r * ($N - $K + $i) / $i }
    $r
}

# Every per-stratum mean difference reachable by re-labelling the runs of one stratum,
# keeping the number of WarSails runs fixed.
function Get-StratumDiffs([double[]]$Values, [int]$WsCount) {
    $diffs = New-Object 'System.Collections.Generic.List[double]'
    $n = $Values.Count
    for ($mask = 0; $mask -lt (1 -shl $n); $mask++) {
        $ws = 0.0; $fo = 0.0; $k = 0
        for ($i = 0; $i -lt $n; $i++) {
            if ($mask -band (1 -shl $i)) { $ws += $Values[$i]; $k++ } else { $fo += $Values[$i] }
        }
        if ($k -eq $WsCount) {
            $diffs.Add($ws / $WsCount - $fo / ($n - $WsCount))
        }
    }
    , $diffs
}

# Exact two-sided stratified permutation test. Strata: objects with Ws and Base value arrays.
function Invoke-StratifiedPermutationTest($Strata) {
    $observed = 0.0
    $sums = New-Object 'System.Collections.Generic.List[double]'
    $sums.Add(0.0)
    foreach ($stratum in $Strata) {
        $ws = [double[]]@($stratum.Ws)
        $base = [double[]]@($stratum.Base)
        $observed += (Get-Mean $ws) - (Get-Mean $base)
        $diffs = Get-StratumDiffs ([double[]]($ws + $base)) $ws.Count
        $next = New-Object 'System.Collections.Generic.List[double]'
        foreach ($s in $sums) { foreach ($d in $diffs) { $next.Add($s + $d) } }
        $sums = $next
    }
    $k = @($Strata).Count
    $dObs = $observed / $k
    $tolerance = 1e-9 * [Math]::Max(1.0, [Math]::Abs($dObs))
    $extreme = 0
    foreach ($s in $sums) {
        if ([Math]::Abs($s / $k) -ge [Math]::Abs($dObs) - $tolerance) { $extreme++ }
    }
    [PSCustomObject]@{ D = $dObs; P = $extreme / $sums.Count; Permutations = $sums.Count; Extreme = $extreme }
}

# Seed-stratified comparison of one metric, WarSails vs the runs of $BaseMode: D, p, smallest
# attainable p, noise s and MDE. Runs of other modes are ignored; every seed with a run of
# either arm must have both (the callers check that first).
function Measure-Arms($Runs, [string]$BaseMode, [scriptblock]$Metric) {
    $armRuns = @($Runs | Where-Object { $_.Mode -eq 'WarSails' -or $_.Mode -eq $BaseMode })
    $strata = New-Object System.Collections.ArrayList
    $cells = New-Object System.Collections.ArrayList
    $varSum = 0.0
    $assignments = 1.0
    $balanced = $true
    $seeds = @($armRuns | ForEach-Object { $_.Seed } | Sort-Object -Unique)
    foreach ($seed in $seeds) {
        $ws = [double[]]@($armRuns | Where-Object { $_.Seed -eq $seed -and $_.Mode -eq 'WarSails' } | ForEach-Object $Metric)
        $base = [double[]]@($armRuns | Where-Object { $_.Seed -eq $seed -and $_.Mode -eq $BaseMode } | ForEach-Object $Metric)
        [void]$strata.Add([PSCustomObject]@{ Ws = $ws; Base = $base })
        [void]$cells.Add($ws)
        [void]$cells.Add($base)
        $varSum += 1.0 / $ws.Count + 1.0 / $base.Count
        $assignments *= Get-Binomial ($ws.Count + $base.Count) $ws.Count
        if ($ws.Count -ne $base.Count) { $balanced = $false }
    }
    # Mirroring every label maps an assignment to one with -D only when each seed is balanced.
    $minP = $(if ($balanced) { 2.0 } else { 1.0 }) / $assignments
    $pooled = Get-PooledSd $cells
    $test = Invoke-StratifiedPermutationTest $strata
    $wsAll = [double[]]@($armRuns | Where-Object { $_.Mode -eq 'WarSails' } | ForEach-Object $Metric)
    $baseAll = [double[]]@($armRuns | Where-Object { $_.Mode -eq $BaseMode } | ForEach-Object $Metric)
    $baseMean = Get-Mean $baseAll
    $mde = (Get-MdeFactor $pooled.Df) * $pooled.S * [Math]::Sqrt($varSum) / $strata.Count
    [PSCustomObject]@{
        D            = $test.D
        P            = $test.P
        MinP         = $minP
        Permutations = $test.Permutations
        S            = $pooled.S
        Df           = $pooled.Df
        Mde          = $mde
        MdePercent   = if ($baseMean -eq 0) { [double]::NaN } else { 100.0 * $mde / $baseMean }
        WsMean       = Get-Mean $wsAll
        BaseMean     = $baseMean
        Total        = ($wsAll + $baseAll | Measure-Object -Sum).Sum
    }
}

# Seeds (of all runs) that lack a WarSails or a $BaseMode run.
function Get-UnbalancedSeeds($Runs, [string]$BaseMode) {
    @($Runs | ForEach-Object { $_.Seed } | Sort-Object -Unique | Where-Object {
            $seed = $_
            @($Runs | Where-Object { $_.Seed -eq $seed -and $_.Mode -eq 'WarSails' }).Count -eq 0 -or
            @($Runs | Where-Object { $_.Seed -eq $seed -and $_.Mode -eq $BaseMode }).Count -eq 0
        })
}

function Format-Number([double]$Value, [string]$Format = '0.0') {
    if ([double]::IsNaN($Value) -or [double]::IsInfinity($Value)) { return 'n/a' }
    $Value.ToString($Format, [Globalization.CultureInfo]::InvariantCulture)
}

function Format-Percent([double]$Part, [double]$Whole) {
    if ($Whole -eq 0 -or [double]::IsNaN($Whole) -or [double]::IsNaN($Part)) { return 'n/a' }
    (Format-Number (100.0 * $Part / $Whole) '0') + ' %'
}

function ConvertTo-JsonNumber([double]$Value) {
    if ([double]::IsNaN($Value) -or [double]::IsInfinity($Value)) { return $null }
    [Math]::Round($Value, 4)
}

function Test-JsonProperty($Object, [string]$Name) {
    $null -ne $Object -and $null -ne $Object.PSObject.Properties[$Name] -and $null -ne $Object.$Name
}

# The job plan: run ids (and modes, where given) from job.json runs[] (runId, mode).
# Available is false when there is no plan file; Error names a plan file that is missing or broken.
function Read-JobPlan([string]$ManifestPath, [string]$JobFolder) {
    $plan = [PSCustomObject]@{ Runs = @{}; Available = $false; Error = ''; File = '' }
    if ($ManifestPath) {
        $file = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($ManifestPath)
        if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { $plan.Error = "job manifest $file missing"; return $plan }
    }
    else {
        $file = Join-Path $JobFolder 'job.json'
        if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { return $plan }
    }
    $plan.File = $file
    try {
        $doc = [IO.File]::ReadAllText($file) | ConvertFrom-Json
        if (-not (Test-JsonProperty $doc 'runs')) { throw 'no runs' }
        foreach ($entry in @($doc.runs)) {
            if (-not (Test-JsonProperty $entry 'runId')) { throw 'a run has no runId' }
            $plan.Runs[[string]$entry.runId] = if (Test-JsonProperty $entry 'mode') { [string]$entry.mode } else { $null }
        }
        $plan.Available = $true
    }
    catch {
        $plan.Error = "job manifest $file not readable ($($_.Exception.Message))"
    }
    $plan
}

function Set-RigError($Run, [string]$Reason) {
    $Run.RigError = $true
    $Run.Reason = $Reason
}

# Status of the run's coherentwarai.ac25a-digest entry in run-result.json scenarios[], or '' when absent.
function Get-DigestScenarioStatus($Result) {
    if (-not (Test-JsonProperty $Result 'scenarios')) { return '' }
    foreach ($scenario in @($Result.scenarios)) {
        if ((Test-JsonProperty $scenario 'id') -and [string]$scenario.id -eq $DigestScenario) {
            if (Test-JsonProperty $scenario 'status') { return [string]$scenario.status }
            return 'without status'
        }
    }
    ''
}

# By tick order, the created lines whose own calendar= date is later than the date of the NEXT
# daily tick (impossible: that tick would have come first). The daily tick fires in the morning,
# not at midnight, so an army raised between midnight and the next tick legitimately carries the
# next calendar date while still being governed by the previous tick. Without a date line for the
# next tick day there is no bound and the line is not checked.
function Get-LateCreatedLines($Digest, [int]$StartDay) {
    if ($Digest.TickCount -eq 0) { return }
    foreach ($leader in $Digest.Leaders) {
        if (-not $leader.Created -or $leader.Calendar -lt 0) { continue }
        $tickDay = if ($leader.Tick -lt 0) { $StartDay } else { $leader.Tick }
        if (-not $Digest.Dates.ContainsKey($tickDay + 1)) { continue }
        $nextDate = [int]$Digest.Dates[$tickDay + 1]
        if ($leader.Calendar -gt $nextDate) {
            "$($leader.Name) [$($leader.HeroId)] calendar $(Format-DateKey $leader.Calendar) after the next tick of $(Format-DateKey $nextDate)"
        }
    }
}

# The posture sections the run's non-ruler episodes are judged against (see the help), and the
# date of the digest's last tick, that the mod log does not hold complete: a missing section (the
# mod log stops writing after one failed flush) or one no later section header follows (cut by a
# partial flush). By tick order a daily tick needs a daily section and an army before the first
# tick the session-launch section; without tick order any section of the date serves.
function Get-PostureGaps($Digest, $ModLog, [int]$StartDay) {
    $tickOrder = $Digest.TickCount -gt 0
    $needLaunch = $false
    $dates = New-Object 'System.Collections.Generic.SortedSet[int]'
    foreach ($leader in $Digest.Leaders) {
        if ($leader.Ruler -or $leader.PlayerClan) { continue }
        if (-not $leader.Created) { [void]$dates.Add([int]$Digest.Dates[$leader.Day]) }
        elseif (-not $tickOrder) { [void]$dates.Add($(if ($leader.Calendar -ge 0) { $leader.Calendar } else { [int]$Digest.Dates[$leader.Day] })) }
        elseif ($leader.Tick -lt 0) { $needLaunch = $true }
        else { [void]$dates.Add([int]$Digest.Dates[$leader.Tick]) }
    }
    if ($tickOrder -and $Digest.Dates.ContainsKey($Digest.LastTick)) { [void]$dates.Add([int]$Digest.Dates[$Digest.LastTick]) }
    else { [void]$dates.Add([int]($Digest.Dates.Values | Measure-Object -Maximum).Maximum) }

    $sections = @($ModLog.PostureSections)
    if ($needLaunch) {
        $startDate = [int]$Digest.Dates[$StartDay]
        $launch = @($sections | Where-Object { $_.Launch -and $_.Date -eq $startDate })
        if ($launch.Count -eq 0) { "the session launch on $(Format-DateKey $startDate) (missing)" }
        elseif (-not $launch[0].Complete) { "the session launch on $(Format-DateKey $startDate) (cut off)" }
    }
    foreach ($date in $dates) {
        $onDate = @($sections | Where-Object { $_.Date -eq $date -and -not ($tickOrder -and $_.Launch) })
        if ($onDate.Count -eq 0) { "$(Format-DateKey $date) (missing)" }
        elseif (@($onDate | Where-Object { -not $_.Complete }).Count -gt 0) { "$(Format-DateKey $date) (cut off)" }
    }
}

# Reads one run folder into $Run: Valid, or a Reason (with RigError for a missing or broken artifact).
function Read-Run($Run, [string]$Folder) {
    $resultFile = Join-Path $Folder 'run-result.json'
    $harnessFile = Join-Path $Folder 'harness.log'
    $modLogFile = Join-Path $Folder 'CoherentWarAI.log'
    if (-not (Test-Path -LiteralPath $resultFile)) { Set-RigError $Run 'run-result.json missing'; return }
    try { $result = [IO.File]::ReadAllText($resultFile) | ConvertFrom-Json }
    catch { Set-RigError $Run 'run-result.json not readable'; return }
    foreach ($field in 'seed', 'mode', 'status', 'daysRequested') {
        if (-not (Test-JsonProperty $result $field)) { Set-RigError $Run "run-result.json has no $field"; return }
    }
    if (-not ($result.seed -is [int] -or $result.seed -is [long])) { Set-RigError $Run "seed '$($result.seed)' is not an integer"; return }
    $Run.Seed = [long]$result.seed
    $Run.Mode = [string]$result.mode
    $Run.Status = [string]$result.status
    $Run.DaysRequested = [int]$result.daysRequested
    if (Test-JsonProperty $result 'daysReached') { $Run.DaysReached = [int]$result.daysReached }
    $scenarioStatus = Get-DigestScenarioStatus $result

    $hasHarness = Test-Path -LiteralPath $harnessFile -PathType Leaf
    $harnessEmpty = $hasHarness -and [IO.File]::ReadAllText($harnessFile).Trim().Length -eq 0
    $digest = $null
    if ($hasHarness -and -not $harnessEmpty) {
        $digest = Read-Digest $harnessFile $Run.Name
        $Run.ArmyDays = $digest.ArmyDays
        $Run.RulerArmyDays = $digest.RulerArmyDays
        $Run.PerKingdom = $digest.PerKingdom
        $Run.Leaders = @($digest.Leaders)
        $Run.Dates = $digest.Dates
        $Run.DigestDays = $digest.Days.Count
        $Run.TickCount = $digest.TickCount
    }
    $hasModLog = Test-Path -LiteralPath $modLogFile -PathType Leaf
    $modLogEmpty = $hasModLog -and [IO.File]::ReadAllText($modLogFile).Trim().Length -eq 0
    if ($Run.Mode -eq 'WarSails' -and $hasModLog -and -not $modLogEmpty) { $Run.Roles = Read-ModLog $modLogFile $Run.Name }

    # A run the rig itself reports as not completed is unusable whatever artifacts it left.
    if ($Run.Status -ne 'Completed') { $Run.Reason = "status $($Run.Status)"; return }
    if (-not $hasHarness) { Set-RigError $Run 'harness.log missing'; return }
    if ($harnessEmpty) { Set-RigError $Run 'harness.log empty'; return }
    if ($Run.Mode -eq 'WarSails' -and -not $hasModLog) { Set-RigError $Run 'CoherentWarAI.log missing'; return }
    if ($Run.Mode -eq 'WarSails' -and $modLogEmpty) { Set-RigError $Run 'CoherentWarAI.log empty'; return }

    # The digest's own coverage: start date line day s, then exactly days s+1 .. s+daysRequested.
    $start = -1
    if ($digest.Dates.Count -gt 0) { $start = [int]($digest.Dates.Keys | Measure-Object -Minimum).Minimum }
    $Run.StartDay = $start
    $expectedDays = @(); if ($start -ge 0) { $expectedDays = @(($start + 1)..($start + [Math]::Max(1, $Run.DaysRequested))) }
    $coverage = $start -ge 0 -and $Run.DaysRequested -gt 0 -and $digest.Days.Count -eq $Run.DaysRequested -and
        @($expectedDays | Where-Object { -not $digest.Days.Contains($_) }).Count -eq 0
    # Every digested day and every day a leader was seen needs a calendar date to be judged. By
    # tick order a created line is dated by the date line of the daily tick it follows (before
    # the first tick: the start date line); without tick lines by its own calendar= field, else
    # by its day's date line.
    if ($digest.TickCount -gt 0) {
        $leaderDays = @($digest.Leaders | ForEach-Object { if (-not $_.Created) { $_.Day } elseif ($_.Tick -ge 0) { $_.Tick } })
    }
    else {
        $leaderDays = @($digest.Leaders | Where-Object { $_.Calendar -lt 0 } | ForEach-Object { $_.Day })
    }
    $undated = @(@($expectedDays) + $leaderDays | Sort-Object -Unique |
        Where-Object { -not $digest.Dates.ContainsKey([int]$_) })

    if ($Run.Mode -notin $KnownModes) { $Run.Reason = "unexpected mode $($Run.Mode)" }
    elseif ($scenarioStatus -eq '') { $Run.Reason = "run-result.json has no $DigestScenario scenario entry" }
    elseif ($scenarioStatus -ne 'Passed') { $Run.Reason = "scenario $DigestScenario $scenarioStatus" }
    elseif ($digest.Duplicates.Count -gt 0) { $Run.Reason = "digest repeats (day, kingdom) line(s): $($digest.Duplicates -join ', ')" }
    elseif ($start -lt 0) { $Run.Reason = 'digest has no date line' }
    elseif (-not $coverage) { $Run.Reason = "digest covers $($digest.Days.Count) days, expected days $($start + 1)..$($start + $Run.DaysRequested)" }
    elseif ($Run.Mode -eq 'WarSails' -and $undated.Count -gt 0) { $Run.Reason = "digest has no date line for day(s) $($undated -join ', ')" }
    elseif ($Run.Mode -eq 'WarSails' -and $Run.Roles.Delegations -eq 0) { $Run.Reason = 'CoherentWarAI.log has no delegates line (chain of command not logged)' }
    elseif ($Run.Mode -eq 'WarSails' -and $digest.TickCount -gt 0 -and $digest.TickErrors.Count -gt 0) {
        $Run.Reason = "harness.log tick order broken ($($digest.TickErrors -join '; ')); armies cannot be dated by their daily tick"
    }
    elseif ($Run.Mode -eq 'WarSails' -and ($late = @(Get-LateCreatedLines $digest $start)).Count -gt 0) {
        $Run.Reason = "created line(s) dated after the posture tick they follow ($($late -join '; ')): a DailyTick line is missing"
    }
    elseif ($Run.Mode -eq 'WarSails' -and ($gaps = @(Get-PostureGaps $digest $Run.Roles $start)).Count -gt 0) {
        $more = if ($gaps.Count -gt 3) { ", ... ($($gaps.Count - 3) more)" } else { '' }
        $Run.Reason = "CoherentWarAI.log lacks a complete war posture section for $((@($gaps | Select-Object -First 3)) -join ', ')$more (log cut off or posture tick not logged)"
    }
    else { $Run.Valid = $true }
}

# ---------------------------------------------------------------------------
# Read the runs.
# ---------------------------------------------------------------------------
if (-not (Test-Path -LiteralPath $JobPath -PathType Container)) {
    Write-Host "Job folder not found: $JobPath"
    exit 1
}
$jobFull = (Resolve-Path -LiteralPath $JobPath).Path
$runDirs = @(Get-ChildItem -LiteralPath $jobFull -Directory | Where-Object { $_.Name -match '^r(\d+)$' } |
    Sort-Object { [int]($_.Name.Substring(1)) })
if ($runDirs.Count -eq 0) {
    Write-Host "No run folders r<N> in $jobFull"
    exit 1
}

$runs = New-Object System.Collections.ArrayList
foreach ($dir in $runDirs) {
    $run = [PSCustomObject]@{
        Name = $dir.Name; Seed = $null; Mode = $null; Status = $null; DaysRequested = 0; DaysReached = 0; StartDay = -1
        DigestDays = 0; TickCount = 0; ArmyDays = 0; RulerArmyDays = 0; PerKingdom = @{}; Leaders = @(); Dates = @{}; Roles = $null
        Valid = $false; RigError = $false; Reason = ''
    }
    [void]$runs.Add($run)
    try { Read-Run $run $dir.FullName }
    catch { $run.Valid = $false; Set-RigError $run "not readable: $($_.Exception.Message)" }
}

$invalid = @($runs | Where-Object { -not $_.Valid })
$rigErrorRuns = @($runs | Where-Object { $_.RigError })
foreach ($run in $invalid) {
    $kind = if ($run.RigError) { 'rig error' } else { 'not usable' }
    [void]$warnings.Add("$($run.Name) $kind`: $($run.Reason)")
}

$plan = Read-JobPlan $JobManifestPath $jobFull
$planNote = ''
if ($plan.Error) { [void]$warnings.Add("$($plan.Error); planned runs not checked") }
elseif (-not $plan.Available) {
    $planNote = ' (no job plan: missing runs not checked)'
    [void]$warnings.Add('no job plan (pass -JobManifestPath or put job.json in the job folder); missing runs not checked')
}
$expectedRuns = $plan.Runs
$missingRuns = @($expectedRuns.Keys | Where-Object { $name = $_; @($runs | Where-Object { $_.Name -eq $name }).Count -eq 0 } | Sort-Object)
foreach ($name in $missingRuns) { [void]$warnings.Add("$name planned by the job but missing from the folder") }

# ---------------------------------------------------------------------------
# Verdict 1: the invariant.
# ---------------------------------------------------------------------------
$wsRuns = @($runs | Where-Object { $_.Mode -eq 'WarSails' })
$wsValid = @($wsRuns | Where-Object { $_.Valid })
$violations = New-Object System.Collections.ArrayList
$ambiguousEpisodes = New-Object System.Collections.ArrayList
$ambiguousLabels = New-Object System.Collections.ArrayList
$rulersExempt = New-Object 'System.Collections.Generic.HashSet[string]'
$judgedLeaders = New-Object 'System.Collections.Generic.HashSet[string]'
$judgedEpisodes = 0
$clearedEpisodes = 0
$unobservedEpisodes = 0
$unlinked = New-Object 'System.Collections.Generic.HashSet[string]'
$possibleViolators = 0
foreach ($run in $wsValid) {
    $modLog = $run.Roles
    $tickOrder = $run.TickCount -gt 0
    if (-not $tickOrder) {
        [void]$warnings.Add("$($run.Name): harness.log has no 'DailyTick <n> done' line, tick order unavailable; armies are judged by the calendar date of their created line against the two posture blocks that can have governed it")
    }
    # Hero ids per (kingdom, name) in this run: more than one id means the name cannot be linked.
    # A mod log with ids links by id (exact, shared names do not matter); one without links by name.
    $idsByName = @{}
    $rulerNames = New-Object 'System.Collections.Generic.HashSet[string]'
    $sharedNameWarned = New-Object 'System.Collections.Generic.HashSet[string]'
    foreach ($leader in $run.Leaders) {
        $key = $leader.Kingdom + '|' + $leader.Name
        if (-not $idsByName.ContainsKey($key)) { $idsByName[$key] = New-Object 'System.Collections.Generic.HashSet[string]' }
        [void]$idsByName[$key].Add($leader.HeroId)
        if ($leader.Ruler -or $leader.PlayerClan) { [void]$rulerNames.Add($key); [void]$rulerNames.Add($leader.Kingdom + '|' + $leader.HeroId) }
    }
    foreach ($key in $modLog.Held.Keys) {
        $held = $modLog.Held[$key]
        if (($held.Contains('Defense') -or $held.Contains('Naval')) -and -not $held.Contains('Offense') -and -not $rulerNames.Contains($key)) { $possibleViolators++ }
    }

    # Episodes per hero: every created event, plus the first snapshot not preceded by one.
    $byHero = @{}
    foreach ($leader in $run.Leaders) {
        if ($leader.Ruler -or $leader.PlayerClan) { [void]$rulersExempt.Add($run.Name + '|' + $leader.HeroId); continue }
        $heroKey = $leader.KingdomId + '|' + $leader.HeroId
        if (-not $byHero.ContainsKey($heroKey)) { $byHero[$heroKey] = New-Object System.Collections.ArrayList }
        [void]$byHero[$heroKey].Add($leader)
    }
    foreach ($heroKey in @($byHero.Keys | Sort-Object)) {
        $appearances = @($byHero[$heroKey])
        $first = $appearances[0]
        $nameKey = $first.Kingdom + '|' + $first.Name
        if ($idsByName[$nameKey].Count -gt 1 -and -not $modLog.HasIds) {
            $key = $nameKey
            if ($sharedNameWarned.Add($key)) { [void]$warnings.Add("$($run.Name) $($first.Kingdom): name '$($first.Name)' is shared by hero ids $((@($idsByName[$key]) | Sort-Object) -join ', '); not linked to the mod log") }
            continue
        }
        $linkName = if ($modLog.HasIds) { $first.HeroId } else { $first.Name }
        $key = $first.Kingdom + '|' + $linkName
        # One episode per governing posture tick: by tick order the daily tick the created line
        # follows (before the first one: the session launch), else the created calendar date
        # (the line's own calendar= field, else its day's date line).
        $episodes = New-Object System.Collections.ArrayList
        $episodeKeys = New-Object 'System.Collections.Generic.HashSet[string]'
        $createdEvents = @($appearances | Where-Object { $_.Created } | Sort-Object Day)
        foreach ($created in $createdEvents) {
            if ($tickOrder -and $created.Tick -lt 0) {
                $rule = 'Launch'; $createdDate = [int]$run.Dates[$run.StartDay]; $episodeKey = 'launch'
            }
            elseif ($tickOrder) {
                $rule = 'Tick'; $createdDate = [int]$run.Dates[$created.Tick]; $episodeKey = "tick $($created.Tick)"
            }
            else {
                $rule = 'Calendar'; $createdDate = if ($created.Calendar -ge 0) { $created.Calendar } else { [int]$run.Dates[[int]$created.Day] }; $episodeKey = "date $createdDate"
            }
            if ($tickOrder -and $created.Calendar -ge 0 -and $created.Calendar -lt $createdDate) {
                [void]$warnings.Add("$($run.Name) $($first.Kingdom): created line of $($first.Name) [$($first.HeroId)] on day $($created.Day) has calendar $(Format-DateKey $created.Calendar), earlier than the posture tick it follows ($(Format-DateKey $createdDate)); judged by tick order")
            }
            if ($episodeKeys.Add($episodeKey)) { [void]$episodes.Add([PSCustomObject]@{ Day = $created.Day; Date = $createdDate; Observed = $true; Rule = $rule }) }
        }
        $createdDays = @($createdEvents | ForEach-Object { $_.Day })
        $snapshotDays = @($appearances | Where-Object { -not $_.Created } | ForEach-Object { $_.Day } | Sort-Object -Unique)
        if ($snapshotDays.Count -gt 0 -and @($createdDays | Where-Object { $_ -le $snapshotDays[0] }).Count -eq 0) {
            # A snapshot is written right after the daily tick of its day.
            $snapshotRule = if ($tickOrder) { 'Tick' } else { 'Calendar' }
            [void]$episodes.Add([PSCustomObject]@{ Day = $snapshotDays[0]; Date = [int]$run.Dates[[int]$snapshotDays[0]]; Observed = $false; Rule = $snapshotRule })
        }
        [void]$judgedLeaders.Add($run.Name + '|' + $heroKey)
        if (-not $modLog.Held.ContainsKey($key)) { [void]$unlinked.Add($run.Name + '|' + $heroKey) }
        foreach ($episode in $episodes) {
            $judgedEpisodes++
            if (-not $episode.Observed) { $unobservedEpisodes++ }
            $date = $episode.Date
            $judgement = Get-EpisodeJudgement $modLog $first.Kingdom $linkName $date $episode.Observed $episode.Rule
            if ($judgement.Outcome -eq 'Cleared') { $clearedEpisodes++; continue }
            $what = if ($episode.Observed) { 'raised an army' } else { 'led an army (creation not observed)' }
            $line = "$($run.Name) (seed $($run.Seed)) $($first.Kingdom): $($first.Name) [$($first.HeroId)] $what on day $($episode.Day) ($(Format-DateKey $date)); "
            if ($judgement.Outcome -eq 'Ambiguous') {
                [void]$ambiguousEpisodes.Add($line + 'ambiguous: ' + $judgement.Why)
                [void]$ambiguousLabels.Add("$($run.Name) $($first.Name) [$($first.HeroId)] day $($episode.Day)")
            }
            else { [void]$violations.Add($line + $judgement.Why) }
        }
    }
}
foreach ($row in $ambiguousEpisodes) { [void]$warnings.Add($row) }

$invariantDetail = "$($violations.Count) violations, $($ambiguousEpisodes.Count) ambiguous, $clearedEpisodes cleared ($($wsValid.Count) WarSails runs, $($judgedLeaders.Count) non-ruler leaders in $judgedEpisodes army episodes, $unobservedEpisodes with creation not observed, $($unlinked.Count) unlinked, $possibleViolators possible violators, $($rulersExempt.Count) rulers exempt)"
# Planned WarSails runs, or runs planned without a mode: the invariant reads WarSails runs only.
$missingWs = @($missingRuns | Where-Object { $expectedRuns[$_] -ne 'FeatureOff' -and $expectedRuns[$_] -ne 'ChainOff' })
$wsRigErrors = @($rigErrorRuns | Where-Object { $_.Mode -eq 'WarSails' -or $null -eq $_.Mode })
if ($violations.Count -gt 0) { $invariantVerdict = 'Failed' }
elseif ($plan.Error) { $invariantVerdict = 'RigError'; $invariantDetail += " - $($plan.Error)" }
elseif ($wsRigErrors.Count -gt 0) { $invariantVerdict = 'RigError'; $invariantDetail += " - rig error in $((@($wsRigErrors | ForEach-Object { $_.Name + ': ' + $_.Reason })) -join '; ')" }
elseif ($wsValid.Count -lt $wsRuns.Count -or @($runs | Where-Object { $null -eq $_.Mode }).Count -gt 0) { $invariantVerdict = 'Inconclusive'; $invariantDetail += ' - a WarSails run (or a run of unknown mode) is not usable' }
elseif ($missingWs.Count -gt 0) { $invariantVerdict = 'Inconclusive'; $invariantDetail += " - planned run(s) $($missingWs -join ', ') missing" }
elseif ($wsValid.Count -eq 0) { $invariantVerdict = 'Inconclusive'; $invariantDetail += ' - no WarSails run' }
elseif ($ambiguousEpisodes.Count -gt 0) {
    # An ambiguous episode may hide a violation, so it blocks a Passed.
    $shown = @($ambiguousLabels | Select-Object -First 3)
    $more = if ($ambiguousLabels.Count -gt $shown.Count) { ", ... ($($ambiguousLabels.Count - $shown.Count) more)" } else { '' }
    $invariantVerdict = 'Inconclusive'; $invariantDetail += " - $($ambiguousEpisodes.Count) ambiguous army episode(s): $($shown -join ', ')$more"
}
elseif ($clearedEpisodes -eq 0) { $invariantVerdict = 'Inconclusive'; $invariantDetail += ' - no army episode decidable' }
else { $invariantVerdict = 'Passed' }
$invariantDetail += $planNote

# ---------------------------------------------------------------------------
# Verdict 2: army-days WarSails vs ChainOff, seed-stratified exact permutation test.
# FeatureOff runs feed only the doctrine info below: an unusable or missing FeatureOff run does
# not block this verdict (a rig error in any run still does - the job's artifacts are broken).
# ---------------------------------------------------------------------------
$armyStats = $null
$armyInvalid = @($invalid | Where-Object { $_.Mode -ne 'FeatureOff' })
$armyMissing = @($missingRuns | Where-Object { $expectedRuns[$_] -ne 'FeatureOff' })
$hasChainOff = @($runs | Where-Object { $_.Mode -eq 'ChainOff' }).Count -gt 0 -or @($expectedRuns.Values | Where-Object { $_ -eq 'ChainOff' }).Count -gt 0
$unbalancedSeeds = @(Get-UnbalancedSeeds @($runs | Where-Object { $_.Mode -eq 'WarSails' -or $_.Mode -eq 'ChainOff' }) 'ChainOff')
if ($plan.Error) {
    $armyVerdict = 'RigError'
    $armyDetail = $plan.Error
}
elseif ($rigErrorRuns.Count -gt 0) {
    $armyVerdict = 'RigError'
    $armyDetail = "rig error in $($rigErrorRuns.Count) of $($runs.Count) runs ($((@($rigErrorRuns | ForEach-Object { $_.Name + ': ' + $_.Reason })) -join '; '))"
}
elseif (-not $hasChainOff) {
    $armyVerdict = 'Inconclusive'
    $armyDetail = 'no ChainOff baseline runs (army-days are judged WarSails vs ChainOff)'
}
elseif ($armyInvalid.Count -gt 0) {
    $armyVerdict = 'Inconclusive'
    $armyDetail = "$($armyInvalid.Count) of $($runs.Count) runs not usable ($((@($armyInvalid | ForEach-Object { $_.Name + ': ' + $_.Reason })) -join '; '))"
}
elseif ($armyMissing.Count -gt 0) {
    $armyVerdict = 'Inconclusive'
    $armyDetail = "planned run(s) $($armyMissing -join ', ') missing"
}
elseif ($unbalancedSeeds.Count -gt 0) {
    $armyVerdict = 'Inconclusive'
    $armyDetail = "seed(s) $($unbalancedSeeds -join ', ') lack a WarSails or a ChainOff run"
}
else {
    $armyStats = Measure-Arms $runs 'ChainOff' { $_.ArmyDays }
    $mdeLabel = if ($armyStats.Df -gt $TQuantile975.Count) { 'normal approx' } else { "t, df $($armyStats.Df)" }
    $armyDetail = ('D={0} army-days ({1} of CO {2}), noise s={3}, p={4} ({5} perms, min p {6}), MDE ~{7} army-days ({8} of CO, {9})' -f
        (Format-Number $armyStats.D), (Format-Percent $armyStats.D $armyStats.BaseMean), (Format-Number $armyStats.BaseMean),
        (Format-Number $armyStats.S), (Format-Number $armyStats.P '0.000'), $armyStats.Permutations,
        (Format-Number $armyStats.MinP '0.000'), (Format-Number $armyStats.Mde), (Format-Percent $armyStats.Mde $armyStats.BaseMean), $mdeLabel)
    if ($armyStats.MinP -ge $Alpha) { $armyVerdict = 'Inconclusive'; $armyDetail += " - design cannot reach alpha $Alpha" }
    elseif ($armyStats.P -lt $Alpha) { $armyVerdict = 'Failed' }
    elseif ($armyStats.Total -eq 0) { $armyVerdict = 'Inconclusive'; $armyDetail += ' - no non-ruler army-days in any run' }
    elseif ($armyStats.BaseMean -eq 0) { $armyVerdict = 'Inconclusive'; $armyDetail += ' - ChainOff mean is 0, no reference level' }
    elseif ([double]::IsNaN($armyStats.Mde) -or $armyStats.S -eq 0) { $armyVerdict = 'Inconclusive'; $armyDetail += ' - MDE not computable (noise s is 0 or undefined)' }
    else {
        $armyVerdict = 'Passed'
        if ($armyStats.MdePercent -gt $MdeWarnPercent) {
            [void]$warnings.Add(('armydays: MDE ~{0} army-days is {1} of CO (above {2} %); only large effects are detectable, more seeds give a stronger test' -f
                (Format-Number $armyStats.Mde), (Format-Percent $armyStats.Mde $armyStats.BaseMean), $MdeWarnPercent))
            # The rig shows only the detail of a job check, so the warning is repeated there.
            $armyDetail += " - WARN weak test: MDE above $MdeWarnPercent % of CO"
        }
    }
}

# ---------------------------------------------------------------------------
# Info, no verdict: the doctrine effect, army-days WarSails vs FeatureOff (only the marshal
# doctrine off), by the same statistic. Computed only from usable runs of both arms on every seed.
# ---------------------------------------------------------------------------
$doctrineStats = $null
$doctrineRuns = @($runs | Where-Object { $_.Mode -eq 'WarSails' -or $_.Mode -eq 'FeatureOff' })
$doctrineInvalid = @($doctrineRuns | Where-Object { -not $_.Valid })
$doctrineMissing = @($missingRuns | Where-Object { $expectedRuns[$_] -ne 'ChainOff' })
$doctrineUnbalanced = @(Get-UnbalancedSeeds $doctrineRuns 'FeatureOff')
$doctrineReason = $null
if ($plan.Error) { $doctrineReason = $plan.Error }
elseif (@($doctrineRuns | Where-Object { $_.Mode -eq 'FeatureOff' }).Count -eq 0) { $doctrineReason = 'no FeatureOff runs' }
elseif (@($runs | Where-Object { $null -eq $_.Mode }).Count -gt 0) { $doctrineReason = 'a run of unknown mode' }
elseif ($doctrineInvalid.Count -gt 0) { $doctrineReason = "run(s) not usable: $((@($doctrineInvalid | ForEach-Object { $_.Name + ': ' + $_.Reason })) -join '; ')" }
elseif ($doctrineMissing.Count -gt 0) { $doctrineReason = "planned run(s) $($doctrineMissing -join ', ') missing" }
elseif ($doctrineUnbalanced.Count -gt 0) { $doctrineReason = "seed(s) $($doctrineUnbalanced -join ', ') lack a WarSails or a FeatureOff run" }
else { $doctrineStats = Measure-Arms $doctrineRuns 'FeatureOff' { $_.ArmyDays } }
if ($null -ne $doctrineStats) {
    $armyDetail += ('; doctrine effect vs FO: D={0} army-days ({1} of FO {2}), p={3} ({4} perms)' -f
        (Format-Number $doctrineStats.D), (Format-Percent $doctrineStats.D $doctrineStats.BaseMean), (Format-Number $doctrineStats.BaseMean),
        (Format-Number $doctrineStats.P '0.000'), $doctrineStats.Permutations)
}
else { $armyDetail += "; doctrine effect vs FO: n/a ($doctrineReason)" }
$armyDetail += $planNote

# ---------------------------------------------------------------------------
# Info: per kingdom, keyed by kingdom id. Usable runs only: an unusable FeatureOff run (which
# does not block the verdict) can hold a partial digest.
# ---------------------------------------------------------------------------
$kingdomRows = New-Object System.Collections.ArrayList
if ($null -ne $armyStats) {
    $validRuns = @($runs | Where-Object { $_.Valid })
    $kingdomIds = @($validRuns | ForEach-Object { $_.PerKingdom.Keys } | Sort-Object -Unique)
    foreach ($kingdomId in $kingdomIds) {
        $k = $kingdomId
        $name = @($validRuns | Where-Object { $_.PerKingdom.ContainsKey($k) } | ForEach-Object { $_.PerKingdom[$k].Name })[0]
        $kingdomArmyDays = { if ($_.PerKingdom.ContainsKey($k)) { $_.PerKingdom[$k].ArmyDays } else { 0 } }
        $stats = Measure-Arms $validRuns 'ChainOff' $kingdomArmyDays
        $foMean = Get-Mean ([double[]]@($validRuns | Where-Object { $_.Mode -eq 'FeatureOff' } | ForEach-Object $kingdomArmyDays))
        $share = @{}
        foreach ($mode in 'WarSails', 'ChainOff', 'FeatureOff') {
            $own = 0; $ruler = 0
            foreach ($run in @($validRuns | Where-Object { $_.Mode -eq $mode -and $_.PerKingdom.ContainsKey($k) })) {
                $own += $run.PerKingdom[$k].ArmyDays; $ruler += $run.PerKingdom[$k].RulerArmyDays
            }
            $share[$mode] = if ($own + $ruler -gt 0) { $ruler / [double]($own + $ruler) } else { [double]::NaN }
        }
        [void]$kingdomRows.Add([PSCustomObject]@{
                KingdomId = $k; Kingdom = $name; WsMean = $stats.WsMean; CoMean = $stats.BaseMean; FoMean = $foMean; Diff = $stats.D; S = $stats.S
                RulerShareWs = $share['WarSails']; RulerShareCo = $share['ChainOff']; RulerShareFo = $share['FeatureOff']
            })
    }
}

# ---------------------------------------------------------------------------
# Console and JSON.
# ---------------------------------------------------------------------------
$verdicts = @($invariantVerdict, $armyVerdict)
$exitCode = if ($verdicts -contains 'RigError') { 1 } elseif ($verdicts -contains 'Inconclusive') { 3 } elseif ($verdicts -contains 'Failed') { 2 } else { 0 }

Write-Host ('{0,-34} {1} {2}' -f $CheckInvariant, $invariantVerdict, $invariantDetail)
Write-Host ('{0,-34} {1} {2}' -f $CheckArmyDays, $armyVerdict, $armyDetail)
if ($kingdomRows.Count -gt 0) {
    Write-Host ('  info  {0,-20} {1,8} {2,8} {3,8} {4,7} {5,6}  {6}' -f 'kingdom', 'WS mean', 'CO mean', 'FO mean', 'WS-CO', 's', 'ruler share WS/CO/FO')
    foreach ($row in $kingdomRows) {
        Write-Host ('  info  {0,-20} {1,8} {2,8} {3,8} {4,7} {5,6}  {6}/{7}/{8}' -f $row.Kingdom, (Format-Number $row.WsMean), (Format-Number $row.CoMean),
            (Format-Number $row.FoMean), (Format-Number $row.Diff), (Format-Number $row.S), (Format-Percent $row.RulerShareWs 1), (Format-Percent $row.RulerShareCo 1), (Format-Percent $row.RulerShareFo 1))
    }
}
foreach ($row in $violations) { Write-Host "  FAIL  $row" }
foreach ($w in $warnings) { Write-Host "  WARN  $w" }
Write-Host ("result: invariant {0}, armydays {1} -> exit {2}" -f $invariantVerdict, $armyVerdict, $exitCode)

$armyJson = [ordered]@{ check = $CheckArmyDays; verdict = $armyVerdict; detail = $armyDetail }
if ($null -ne $armyStats) {
    $armyJson.d = ConvertTo-JsonNumber $armyStats.D
    $armyJson.wsMean = ConvertTo-JsonNumber $armyStats.WsMean
    $armyJson.coMean = ConvertTo-JsonNumber $armyStats.BaseMean
    $armyJson.s = ConvertTo-JsonNumber $armyStats.S
    $armyJson.df = $armyStats.Df
    $armyJson.p = ConvertTo-JsonNumber $armyStats.P
    $armyJson.minP = ConvertTo-JsonNumber $armyStats.MinP
    $armyJson.permutations = $armyStats.Permutations
    $armyJson.mde = ConvertTo-JsonNumber $armyStats.Mde
    $armyJson.mdePercent = ConvertTo-JsonNumber $armyStats.MdePercent
    $armyJson.alpha = $Alpha
}
# Info only, never a verdict: WarSails vs FeatureOff (the doctrine effect).
$doctrineJson = [ordered]@{ available = ($null -ne $doctrineStats); reason = $doctrineReason }
if ($null -ne $doctrineStats) {
    $doctrineJson.d = ConvertTo-JsonNumber $doctrineStats.D
    $doctrineJson.wsMean = ConvertTo-JsonNumber $doctrineStats.WsMean
    $doctrineJson.foMean = ConvertTo-JsonNumber $doctrineStats.BaseMean
    $doctrineJson.s = ConvertTo-JsonNumber $doctrineStats.S
    $doctrineJson.df = $doctrineStats.Df
    $doctrineJson.p = ConvertTo-JsonNumber $doctrineStats.P
    $doctrineJson.minP = ConvertTo-JsonNumber $doctrineStats.MinP
    $doctrineJson.permutations = $doctrineStats.Permutations
    $doctrineJson.mde = ConvertTo-JsonNumber $doctrineStats.Mde
    $doctrineJson.mdePercent = ConvertTo-JsonNumber $doctrineStats.MdePercent
}
$armyJson.doctrine = $doctrineJson
$doc = [ordered]@{
    schema      = 1
    job         = $jobFull
    jobManifest = if ($plan.File) { $plan.File } else { $null }
    checks      = @(
        [ordered]@{
            check = $CheckInvariant; verdict = $invariantVerdict; detail = $invariantDetail; violations = @($violations)
            ambiguous = $ambiguousEpisodes.Count; ambiguousEpisodes = @($ambiguousEpisodes); cleared = $clearedEpisodes
            leaders = $judgedLeaders.Count; episodes = $judgedEpisodes; unobservedCreations = $unobservedEpisodes
            unlinked = $unlinked.Count; possibleViolators = $possibleViolators; rulersExempt = $rulersExempt.Count
        },
        $armyJson
    )
    runs        = @($runs | ForEach-Object {
            [ordered]@{
                run = $_.Name; seed = $_.Seed; mode = $_.Mode; status = $_.Status; daysRequested = $_.DaysRequested
                daysReached = $_.DaysReached; startDay = $_.StartDay; digestDays = $_.DigestDays; armyDays = $_.ArmyDays
                rulerArmyDays = $_.RulerArmyDays; valid = $_.Valid; rigError = $_.RigError; reason = $_.Reason
            }
        })
    missingRuns = @($missingRuns)
    kingdoms    = @($kingdomRows | ForEach-Object {
            [ordered]@{
                kingdomId = $_.KingdomId; kingdom = $_.Kingdom; wsMean = ConvertTo-JsonNumber $_.WsMean; coMean = ConvertTo-JsonNumber $_.CoMean
                foMean = ConvertTo-JsonNumber $_.FoMean; diff = ConvertTo-JsonNumber $_.Diff; s = ConvertTo-JsonNumber $_.S
                rulerShareWs = ConvertTo-JsonNumber $_.RulerShareWs; rulerShareCo = ConvertTo-JsonNumber $_.RulerShareCo; rulerShareFo = ConvertTo-JsonNumber $_.RulerShareFo
            }
        })
    warnings    = @($warnings)
}
if (-not $ResultPath) { $ResultPath = Join-Path $jobFull 'ac25a-compare.json' }
$resultFile = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($ResultPath)
[IO.File]::WriteAllText($resultFile, (ConvertTo-Json -InputObject $doc -Depth 6), (New-Object System.Text.UTF8Encoding $false))

exit $exitCode
