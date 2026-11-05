# In-game test scenarios

`CoherentWarAI.Scenarios` is a plugin module for the Bannerlord test rig (the sibling
`BannerlordTestRig` repo). It holds `IScenario` implementations that the rig's harness
(`TestRig.Harness`) discovers and runs inside a real campaign.

It exists for the test rig only. It is not part of the shipped mod, not in the normal mod build
and not in any solution; never install it for normal play.

## Build

The rig runner builds it, not you. It builds `src/CoherentWarAI/CoherentWarAI.csproj`, then the
harness, then `CoherentWarAI.Scenarios/CoherentWarAI.Scenarios.csproj`, all with the same
`/p:BannerlordModulesPath=<stage folder>`. This project references the staged `CoherentWarAI.dll`
and `TestRig.Harness.dll` from that folder and copies its own DLL, PDB and `SubModule.xml` to
`<stage folder>\CoherentWarAI.Scenarios\`.

## Scenarios

| Id | Class | Checks |
|----|-------|--------|
| `coherentwarai.health` | `HealthScenario` | all 9 campaign behaviors are registered; the 3 GameModels (target score, garrison, army management) are the active ones. Runs with time stopped. As a health scenario it runs before and after a soak; the second run also checks that the mod log has no `[Error]` line since the first. That is a tripwire only: the mod has no catch sites today and `WarAiLog` is gated by the MCM `EnableLogging` setting; uncaught exceptions are covered by the rig's exception classification (`exceptions.own`). |
| `coherentwarai.ac25a-digest` | `Ac25aDigestScenario` | Observer for AC25a, vanilla data only. Subscribes to `CampaignEvents.ArmyCreated`, then advances `days` (`IScenarioContext.DaysRequested`) days one at a time and writes a digest to the harness log. Checks `setup.days-requested` (`days` > 0), `digest.days-aligned` (every one-day step lands exactly one day after the previous: days start+1 .. start+`days`), `digest.kingdoms` (at least one kingdom line) and `digest.created-errors` (no exception in the `ArmyCreated` listener; it is caught and counted, never thrown into the campaign). The `digest done` line also counts created events skipped for lack of a kingdom or leader hero. Judged offline by `tools/Compare-Ac25a.ps1`. Needs a run with `days` >= 1; the per-scenario time limit (300 s) caps it at roughly 80 days. |

### AC25a digest format (v1)

One harness-log line per living kingdom and day, plus one per army created, plus one date line for the start day and for every digested day. The date line maps the harness day to the calendar date in `WarAiLog.GameDate()` format, which heads every mod-log section. The start day is the harness day the scenario started on (not necessarily 0). Names are the plain `Name.ToString()` of the kingdom and hero, as in the mod log, because the comparer joins on (kingdom, name). `ruler01` is `1` for the kingdom leader.

A created line ends with its own `calendar=` date, taken when the event fires. The harness day (`day=`) only advances when the scenario runner ticks, so an army created after the daily tick but before that tick still carries the previous day. The field was appended to v1; the comparer dates an army by tick order (see below) and uses the field as a cross-check, or, when the harness log has no tick lines, as the army's date (else the date line of `day=` for older digests).

```
ac25a v1 date day=<d> calendar=Year <y>, <Season> <n>
ac25a v1 day=<d> kingdom=<id>|<name> armies=<n> leaders=<heroId>|<name>|<ruler01>|<playerClan01>;...
ac25a v1 created day=<d> kingdom=<id>|<name> leader=<heroId>|<name>|<ruler01>|<playerClan01> calendar=Year <y>, <Season> <n>
```

### How the comparer judges an army (AC25a invariant)

The full rules are in the help of `tools/Compare-Ac25a.ps1` (`Get-Help tools\Compare-Ac25a.ps1 -Full`). In short: the army model asks `CommandBehavior.HoldsDomain(Offense)` when the army is raised, and `WarPostureBehavior` replaces each kingdom's Offense holder set with the MARSHAL lines of its posture block once per posture tick (session launch, then every daily tick). A kingdom without a block in a tick holds no Offense after it in builds that write the clear marker `[Command] Offense holders cleared each posture tick` (T-0118); for older builds the comparer keeps its previous set, as those builds did. Vanilla raises AI armies only in the hourly party AI, which runs after the whole daily-tick dispatch, so an army is governed by the posture of the most recent daily tick before it. The comparer reads that from the order of `harness.log`: the last rig line `DailyTick <n> done` before the created line names the tick, and the date line of day n gives its calendar date D (it may come after the created line, whose `day=` still lags). The governing block is the kingdom's last daily block of D, or its last earlier block when D has none; before the first `DailyTick` line it is the session-launch block (the kingdom's block in the first war posture section of the mod log). The session-launch block clears nobody unless the mod log has the launch marker `[Command] Offense holders from session launch: <n> kingdom(s)` after that section: older builds wipe the launch holder set right after publishing it, so a MARSHAL line there is ambiguous ("session-launch holders reset by CommandBehavior (build without launch marker)"). The army is cleared when the leader is MARSHAL in that block, ambiguous (WARN) when he holds Offense only as delegate on D or his army was not seen being created, and a violation when none of that applies. The invariant is Passed only with at least one cleared episode and none ambiguous; any ambiguous episode makes it Inconclusive, any violation Failed. A created `calendar=` earlier than D is impossible and only gives a WARN. The run is unusable (Inconclusive) when its tick order is broken (DailyTick numbers not 1, 2, 3, ..., a date line of day n not after `DailyTick n done`, or a created `calendar=` later than the date of the next daily tick; the tick fires in the morning, so `calendar=` may legitimately be D or the next day), or when `CoherentWarAI.log` lacks a complete war posture section (one that another section header follows) for a governing date or for the date of the last daily tick: the mod log stops writing after one failed flush. Without any `DailyTick` line (WARN "tick order unavailable") the comparer falls back to the calendar date C of the created line and both blocks that can have governed it: G, the kingdom's last block of C (or its last earlier block), and P, the block before G; cleared needs MARSHAL in both, MARSHAL in only one is ambiguous. Rulers are exempt.
