# Bannerlord Coherent War AI

A gameplay mod for **Mount & Blade II: Bannerlord** that makes the campaign AI
wage war more coherently. Focus: AI/logic, not graphics.

> **Status: work in progress.** Targets game **v1.4.7** (with the War Sails /
> Naval DLC). Built as a standalone module on vanilla anchors - not a fork.

## What it does

Vanilla campaign AI tends to pile every lord onto whichever fief is momentarily
weakest, fights incoherent multi-front wars, and leaves realms undefended until
they are already under attack. This mod addresses that through clean, additive
overrides of the vanilla decision models:

- **Target selection (Slice A):** de-greeds the target score so overwhelming a
  soft target no longer beats attacking a coherent, reachable objective on your
  own front.
- **Garrison defense (Slice C):** makes garrison strength and recruitment
  threat- and frontline-aware, so border fiefs are actually defensible.
- **Strategic coordination (Slice B):** a faction-level layer that stops lords
  dogpiling one fief, concentrates force on a primary enemy, and pre-positions
  defenders before a settlement is hit.

Each slice can be toggled off, and every weight has a no-op parameter shape, so a
disabled or partially built feature is a no-op rather than a regression (the
*shipped* defaults are, of course, tuned to actually change behavior). Diplomacy
(declare-war /
make-peace decisions) is intentionally out of scope - this mod coexists with the
**Diplomacy** mod rather than duplicating it.

## Compatibility

- Coexists with the **Diplomacy** mod (no `DiplomacyModel` override).
- Naval / War Sails targeting is left to vanilla and preserved.
- Add/remove safe: the mod stores no data in your save.

## Building

The project references the game assemblies via MSBuild properties. Copy
`src/CoherentWarAI/paths.props.user.template` to
`src/CoherentWarAI/paths.props.user` and set your Bannerlord install path (the
template auto-detects common Steam locations). Then build with MSBuild
(Visual Studio 2022+/2026). The post-build step deploys the module into your
Bannerlord `Modules/` directory.

Engine-free logic lives under `Logic/` and is unit-tested in
`tests/CoherentWarAI.Tests` (`dotnet test`, no game install required).

## License

[MIT](LICENSE).
