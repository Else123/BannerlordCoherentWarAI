# Design

This mod hooks the campaign AI through Bannerlord's standard extension points:
game-model overrides registered on the `CampaignGameStarter`, plus (for the
coordination layer) a `CampaignBehaviorBase`. It avoids Harmony on the AI hot
path; the coordinator publishes state that the target-score override reads.

## Slices

### Slice A - Target selection (`TargetScoreCalculatingModel`)
We subclass the vanilla target-score model and multiply the base score by two
toggleable factors (each has a no-op parameter shape for when the feature is
disabled; the shipped defaults are tuned to change behavior):

- **Overkill damping** - once an attacker is already comfortably stronger than a
  target's defenders, piling on *more* relative strength stops increasing the
  score. Value and front-coherence decide the target instead of "who is weakest".
- **Front coherence** - fiefs adjacent to our own territory (our front) are
  favored over distant soft targets, generalizing the front bias vanilla already
  applies to sieges so it also covers raids and other actions.

Both are implemented as pure functions (`Logic/TargetWeights`) with no engine
dependency, so they are unit-tested directly. The model override only wires
live game state (defender strength, neighbor ownership) into them.

### Slice C - Garrisons (`SettlementGarrisonModel`)
Scales garrison target size and auto-recruitment by a threat factor derived from
whether a settlement is on a hostile border and the local threat, so border fiefs
hold and refill larger garrisons while safe interior fiefs free troops for the field.

Local threat is measured by the mod itself (`GarrisonPlanner.ActiveThreat`), not
read from vanilla's `NearbyLandThreatIntensity`/`NearbyNavalThreatIntensity`.
Every ~3 h vanilla adds three times the threat value of each hostile party nearby
(bandits included, at half weight) plus a flat +0.6 for every alarmed own caravan
or villager party, and decays the total by only 0.85 - it has no fixed scale - and
it skips every party in an army. In a 31-day run it kept the gateway
multiplier at its cap (Qasira x2.72 on 30 of 31 days) while a marching enemy army
did not register at all.

The own measure: hostile lord parties and armies (an army once, through its
leader, with the whole army's strength) within vanilla's threat check radius
(`MobilePartyAIModel.GetSettlementNearbyThreatAndAllyCheckRadius`; land around
the gate, sea around the port for coastal fiefs), each weighted linearly from 1 at
the search centre to 0 at the radius, divided by the defenders - garrison plus
militia plus `AllyWeight` (0.25) of friendly lords nearby, never less than
`MinDefenceStrength` (50, about 40 mid-tier troops). Bandits, caravans and
villagers do not count; neither do enemies resting inside another settlement,
disbanding or leaderless enemy parties, or lords of allied kingdoms (neutral, as in
vanilla). The lord asking how many troops to take or leave, and his army, never
count as defenders of that garrison. 1.0 means the enemy at the gate matches the
defence; `GarrisonThreatCap` (4) is reached only at a fourfold overmatch (against
an emptied fief: about 200 strength at the gate, a full lord party) or under siege.

### Slice B - Strategic coordinator (`CampaignBehaviorBase`)
A per-kingdom strategic layer that derives fronts and per-objective "claims" from
live game state, then publishes a bias map the Slice A model reads. It caps how
much force piles onto a single objective (anti-dogpile), concentrates on a
primary enemy, and pre-positions defenders on threatened border fiefs via party
objectives. State is derived, never persisted - the mod is add/remove safe.

## Non-goals
- **Diplomacy decisions** (declare war / make peace / alliances) are out of
  scope; this mod coexists with the Diplomacy mod instead of overriding
  `DiplomacyModel`.
- **Battle/tactical AI** is untouched; this is campaign-map strategy only.

## Compatibility notes
- Models are registered so the last-registered override wins; extend rather than
  clobber other model overrides where they exist.
- Naval navigation and port targeting are left entirely to vanilla.
