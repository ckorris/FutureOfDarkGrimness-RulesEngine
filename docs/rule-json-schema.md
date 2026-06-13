# Authoring special rules in a `.fdgarmy` file (#059)

An army list (`.fdgarmy`, JSON) can carry its own special-rule **definitions** inline, so an
army template ships the rules it needs as data — no engine rebuild. This is how the ~20+ army
templates each introduce their own rules independently of the C# core.

The canonical, tested example lives at [`armies/example-with-rules.fdgarmy`](../armies/example-with-rules.fdgarmy);
`ExampleArmyFileTests` is its contract. Start by copying it.

## File shape

```jsonc
{
  "name": "Example Raiders",
  "faction": "Example",
  "pointsLimit": 500,
  "units": [ /* unit entries that reference rules by name */ ],
  "ruleDefinitions": [ /* the embedded rule definitions, see below */ ]
}
```

- **`units[].specialRules`** and **`units[].weapons[].specialRules`** *reference* rules by name.
  They do not define behavior; they name a rule the engine must resolve.
- **`ruleDefinitions`** *defines* behavior. Each entry is a full rule the engine registers into its
  resolver at load.

A unit reference and a definition connect by **name**. In the example, the unit names `"Frenzied"`
and `ruleDefinitions` defines `"Frenzied"`.

## Registration & override

At army load the engine registers core rules first, then each loaded army's `ruleDefinitions`
**override by name** — so an embedded `"Stealth"` definition *retunes* the core Stealth for that
game. A name not in the core catalog (like `"Frenzied"`) is simply additive. Last-loaded wins on a
same-name collision across two armies in play.

## Rule references (`specialRules` entries)

Each entry is tagged by `kind`:

| `kind` | Fields | Meaning |
|---|---|---|
| `core` | `name` | Reference a rule by name (no argument). |
| `coreNumeric` | `name`, `numericValue` | Reference a parameterized rule (`Impact(2)` → `numericValue: 2`). The number becomes argument 0. |
| `alias` | `name`, `aliasedRule` | An army-flavored display name for another reference (recursive). |

## Rule definitions (`ruleDefinitions` entries)

```jsonc
{
  "name": "Frenzied",
  "passive": [
    {
      "hookID": "Shooting_OnHitRollComplete",
      "condition": { "kind": "unmodifiedRollEquals", "dieValue": 6 },
      "effect":    { "kind": "addExtraHit", "onRollValue": 6, "count": 1 },
      "lifetime": "ThisAttack",
      "seat": "Actor"
    }
  ],
  "activated": [],
  "scope": "Unit"
}
```

- **`name`** — the canonical key units reference.
- **`passive`** — hook attachments that fire automatically. Each is `(hookID, condition, effect,
  lifetime, seat)`. `seat` defaults to `Actor` (the acting side); defensive rules (Stealth-like)
  use `Subject`.
- **`activated`** — player-triggered abilities (cost-gated). Often empty.
- **`scope`** — `Unit` (default) or `Weapon` (#027). Load refuses to attach a rule at the wrong scope.

### Polymorphic `kind` tags

`condition` and `effect` are closed sum types: each carries a `"kind"` discriminator plus that
case's fields. Examples of conditions: `always`, `unmodifiedRollEquals`, `distanceGreaterThan`,
`statGreaterOrEqualTo`, `and`/`or`/`not`, `isMelee`, `isCharging`. Examples of effects:
`rollModifier`, `addExtraHit`, `addExtraWound`, `movementBonus`, `heal`, `grantToken`, `reactivate`.

The **authoritative, always-current** tag lists are the `[JsonDerivedType]` attributes on the
source types — there is no separate registry to drift out of sync:

- Conditions — `Rules/Definitions/Condition.cs`
- Effects — `Rules/Definitions/Effect.cs`
- Value sources (`literal` / `arg`) — `Rules/Definitions/ValueSource.cs`
- Costs, reroll conditions, token triggers, dice expressions — same `Rules/Definitions/` folder

### Argument-driven effects

A few effects (`multiplyWounds`, `multiplyHits`, `setMaxWounds`, `chargeImpactHits`,
`extraMeleeWoundCount`, `grantToken`) take a `ValueSource` instead of a plain int:

- `{ "kind": "literal", "value": 3 }` — a fixed value, or
- `{ "kind": "arg", "index": 0 }` — read argument 0 (supplied by a `coreNumeric` reference).

So `Deadly(3)` = a definition whose effect uses `arg` index 0, referenced via
`{ "kind": "coreNumeric", "name": "Deadly", "numericValue": 3 }`.

## Validation at load (hard-fail)

Every embedded definition is validated when the army loads (workstream 3). If a condition or effect
requires a capability its hook's context can't provide — e.g. a `distanceGreaterThan` on a
lifecycle hook that has no distance — the **whole army load is rejected** with a
`RuleValidationException` listing every violation. This is deliberate: a capability/hook mismatch is
an authoring bug, and failing loudly at load beats misbehaving silently mid-game. Fix the file and
reload.

## Format conventions

- camelCase property names, `WriteIndented`, string enums — all via `RuleJson.Options`.
- Don't hand-maintain JSON from scratch; copy the example and adjust. To regenerate the example after
  a format change, serialize an `ArmyListFile` through `RuleJson.Options` and overwrite the file,
  then keep this doc in step.
