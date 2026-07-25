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
whether a settlement is on a hostile border and the local threat/ally intensity,
so border fiefs hold and refill larger garrisons while safe interior fiefs free
troops for the field.

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
