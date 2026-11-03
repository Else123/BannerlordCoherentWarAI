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
| `coherentwarai.health` | `HealthScenario` | all 9 campaign behaviors are registered; the 3 GameModels (target score, garrison, army management) are the active ones. Runs with time stopped. As a health scenario it runs before and after a soak; the second run also checks that the mod log has no error markers since the first. |
