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

- **Defense first:** holding your own territory is the default job. Vanilla never
  assigns AI lords a party objective at all, so nobody guards anything until a
  settlement is already under attack. This mod assigns objectives explicitly:
  most lords defend, and only a limited number - scaled down as more of the realm
  comes under threat, and picked by strength and the lord's Valor - are released
  to attack.
- **Target selection:** de-greeds the target score so overwhelming a soft target
  no longer beats attacking a coherent, reachable objective on your own front.
- **No more dithering at the gates:** vanilla re-decides every target from scratch
  each tick against a single hard threshold, so a lord in front of a castle flips
  between attacking and aborting whenever the defenders change - which is also
  exploitable by stepping in and out of a settlement. Committing to a target and
  sticking with one now use different thresholds, and a fresh commitment is not
  reconsidered over momentary noise (though an outright collapse still ends it).
- **Garrisons that reflect the map:** vanilla sizes garrisons from economics
  alone, so the fief the enemy marches through is defended no better than one
  deep inside the realm, and one-troop-a-day recruitment cannot refill it between
  raids. Garrison size and recruitment now scale with how exposed a settlement
  is - and **chokepoints**, the fiefs that both face foreign ground and shield
  friendly ground behind them, are held hardest. Quiet interior holdings shrink
  instead, giving those troops back to the field army.
- **Strategic coordination (planned):** a faction-level layer that stops lords
  dogpiling one fief and concentrates force on a primary enemy.

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
