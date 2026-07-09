# Future of Dark Grimness — the engine

Future of Dark Grimness (FDG) is a rules engine for a fast-playing tabletop mass-battle wargame — the kind where two players take turns activating units, roll dice to shoot and fight, and win by holding objectives rather than by tabling each other. This repository is **just the engine**: it knows the rules, runs the whole game from map setup through to a declared winner, and asks the players for decisions when it needs them. It has no window, no buttons, and no opinions about how any of it should look.

That last part is the whole point. The engine talks to the outside world through a small set of interfaces, so the application sitting on top of it is **interchangeable**. The Raylib + ImGui client in the sibling project is one such application; you could write a web front-end, a different renderer, a play-by-mail bot, or a pure test harness against the same engine without touching a line of rules code. The engine drives, the front-end paints.

*Note: This is a work-in-progress README for a work-in-progress engine. It describes what's actually wired up today, and is honest about what isn't (see [What's in progress](#whats-in-progress)). If a rule has a stage but the stage is a stub, I say so.*

---

## What it does

The engine models a complete game as a state machine — `MapSetup → Deployment → MainPhaseRound (×4) → VictoryCalculation` — and resolves each phase against the rules. Here's the feature set, roughly in the order a game plays out.

### Mission setup
Rolls for the objective count (D3+2, so 3–5 objectives), then has the players alternate placing real objectives and terrain on the table. Terrain can be auto-generated from a layout, loaded from a file, or placed by the players. This used to be stubbed; it isn't anymore — the objectives and terrain that come out of setup are the same ones the rest of the game reasons about.

### Deployment
Players alternate dropping units into their deployment zones, with unit cohesion enforced (models stay within 1″ of a neighbor and 9″ of the whole unit). Reserve units — the ones with Scout/Ambush-style rules — are held back and walk on from round 2, placed more than 9″ from the enemy.

### Activations, movement, and the turn structure
Units activate one at a time, alternating between players, across four rounds. On its activation a unit picks an action — Hold, Advance (6″), Rush (12″), Charge (12″) — and the engine validates the move:

- Paths are blocked by **impassable** terrain.
- **Dangerous** terrain rolls a d6 per model whose path crosses it and deals a wound on a 1.
- **Difficult** terrain caps movement.
- Cohesion is re-checked so a unit can't tear itself apart by moving.

### Shooting
A shooting unit picks weapons and targets (up to two enemy units per action), and the engine runs the full resolution: **range → line of sight → cover → to-hit (quality) → saves (defense, modified by AP) → wounds**. Line of sight is real — it traces against terrain *and* the physical bases of other models as circular blockers, ignoring the firing unit's own models and the target's.

### Melee
Charge into contact and the engine runs the melee sequence: pile-in, strike order, swinging, strike-backs, working out who won, and consolidating afterward. The combat math (hits, wounds, saves) shares the same pipeline as shooting.

### Wounds, saves, and the nastier weapon rules
Wound assignment handles multi-wound (Tough) models, and the rules system layers on the modifiers you'd expect — Deadly, Rending, Regeneration, Bane, and friends — at the right points in the hit/wound/save chain.

### Morale
A failed morale test makes a unit **Shaken** (it idles its next activation and recovers); a melee loser at half strength or worse **Routs** (removed from play). Shaken units automatically fail further tests, which is how a Shaken unit that loses again gets wiped out. Wound-driven morale fires from shooting and dangerous terrain too, not just melee.

### Objectives and victory
At the end of each round the engine reconciles objectives: an objective with living models from exactly one player within 3″ is seized by that player, an objective contested by multiple players goes neutral, and otherwise ownership holds. After four rounds it tallies controlled objectives and declares a winner (a unique top scorer wins; everything else is a tie).

*Note: A player can win with every model dead, as long as they hold the objectives. Unit counts are never a win condition — don't let any front-end you build assume otherwise.*

### Special rules as data
This is the big one. Special rules aren't hardcoded C# — they're **data**, authored as `Condition × Effect` records over a set of named hooks, with a per-unit/per-model token system as the state primitive. Core rules live in a catalog, but an army can also ship *its own* rules embedded right in its `.fdgarmy` file, registered into the engine at load with no rebuild. See [`docs/rule-json-schema.md`](docs/rule-json-schema.md) and the worked example in [`armies/example-with-rules.fdgarmy`](armies/example-with-rules.fdgarmy). A couple dozen rules are live today across the hit, movement, wound, save, and deployment hooks.

### Casting
Casters get a per-round pool of spell tokens (capped at 6), spend them to attempt spells on a 4+, and the spell either deals damage (through the same synthetic-hit pipeline as a weapon) or grants a rule/buff (through the same token system as everything else). Spell *content* is authored as data, same as special rules.

### A computer opponent
There's a full set of AI resolvers, so any seat can be played by the engine itself — it answers the same decision requests a human front-end would, so you get a working solo/headless game for free.

### Networking and save/load
The engine ships a TCP host/client (port 6389) and synchronizes game state across the wire, so the exact same rules and resolver flow run in multiplayer with no extra work on the front-end. Games can also be saved and resumed.

---

## How a front-end plugs in

The gist: **read the world off `ITableState`, answer the engine's questions with resolvers, and (optionally) animate the play-by-play from the presentation stream.** Three seams, and you're done.

### 1. Get an `IFDGGame` and hand it your interfaces

`FDGServer` is the host-side driver that owns the state machine. Each player side gets an `IFDGGame` (`FDGGame_AsLocal` for same-machine, `FDGGame_AsClient` over the network), and you wire your front-end into it in one call:

```csharp
game.AssignInterfaces(
    logMessageUI:           myLog,        // ILogMessageUI        — game log lines
    playerMessageUI:        myChat,       // IPlayerMessageUI     — player / chat text
    stageResolverRegistry:  myRegistry,   // IStageResolverRegistry — how you answer decisions
    presentationSink:       myRenderer,   // IPresentationSink     — the animation/beat stream
    outstandingTaskDisplay: null);        // IOutstandingListDisplay — optional "waiting on…" UI
```

Everything else hangs off that.

### 2. Read the live world — no polling

`game.TableState` is an observable view of everything on the table: `Players`, `Teams`, `Armies`, `Units`, `Models`, `Terrain`, and `Objectives`. Each collection raises `OnObjectCreated` / `OnObjectRemoved`, and each model raises `OnPositionChanged` / `OnWoundsDealt`. You subscribe once and react — you never poll the engine and you never call back into the rules to ask "what's happening."

```csharp
foreach (var model in game.TableState.Models.Objects)
    Draw(model.Position, model.BaseRadiusInches);   // circles at true scale

game.TableState.Models.OnObjectCreated += m =>
    m.OnPositionChanged += () => /* move the sprite */;
```

*Note: A model exists in `TableState.Models` from the moment it's created, but its `Position` sits at (0,0,0) until the engine calls `SetPosition`. Anything that scans for "what's on the table" has to filter those out — the real client only draws a model after its first position change.*

### 3. Answer the engine's questions with resolvers

When the engine needs a decision — pick a target, place a model, assign wounds, choose a deployment zone — it sends an `IStageTaskRequest<TResult>` through the message bus. You implement `IStageResolver<TRequest, TResult>` for each request type and register it:

```csharp
public interface IStageResolver<TRequest, TReply>
    where TRequest : IStageTaskRequest<TReply>
{
    Task<TReply> Resolve(TRequest request);
}

var registry = new StageResolverRegistry()
    .RegisterResolver(new MyYesNoResolver())
    .RegisterResolver(new MyTargetResolver())
    // …one per request type
```

There are ~14 request types — yes/no, generic selection, string selection, choose deployment zone, choose ranged attack, assign wounds, define a movement path, place objects/objectives/terrain, choose a melee defender, and so on. Your `Resolve` can do anything that eventually returns the answer: block on stdin, await a button click, run an AI heuristic. The engine doesn't care *how* you decide, only that you eventually do.

*Note: This is why the front-end is interchangeable. The CLI client answers requests from stdin; the GUI client answers them from mouse clicks on a canvas; the AI answers them with heuristics — all the same interface, all the same engine. Multiplayer is the same story: a `NetworkedRequestMessageReceiver` pulls requests off the bus, routes them to your local resolvers, and ships the replies back to the host, so your resolvers don't even know they're networked.*

### 4. Show the play-by-play (optional)

For everything the engine *does* (as opposed to *asks*), there's a separate, one-way stream of **presentation beats** — `AttackBeat`, `ModelWoundedBeat`, `ModelDiedBeat`, `UnitMovedBeat`, `BannerBeat`, `RollOffBeat`, `DiceRolledBeat`, and so on. Implement `IPresentationSink.OnBeat(...)` and you get a clean, semantic feed of "what just happened" to animate, narrate, or log however you like. A renderer enqueues beats onto an animation timeline; a CLI just prints their text; a headless run no-ops them entirely.

The engine separates *computing* state from *presenting* it, so it injects a presentation clock to pace the feed: instant for headless/tests (stay deterministic), real-time for a GUI (so the battle unfolds at a watchable tempo).

*Note: `OnBeat` is called on the engine thread and the engine does not wait for it. Return promptly and marshal to your render thread yourself — same lock-and-handoff discipline the resolvers use. Don't do slow work inside it.*

### What you supply vs. what the engine ships
The engine ships the networked message bus, the request/resolver plumbing, the AI resolver set, save/load, and the rules themselves. **You** supply the front-end implementations of the interfaces above, and — for single-process play — a local message bus that implements `IMessageBusHost`/`IMessageBusClient` without a socket. (The networked host/client are in the box; the in-process one lives on the application side.)

---

## What's in progress

The engine is real and plays a full game end-to-end, but it is **not** a finished rulebook, and you shouldn't assume a rule is enforced just because a stage with its name exists. The honest state of things:

- **Special-rule coverage isn't complete.** The data-driven framework is solid and a couple dozen rules are live, but making *every* army-book rule and spell authorable as data is ongoing — a chunk of rules work today, a chunk need a small seamed extension, and a chunk still need new engine primitives. This is the largest active workstream.
- **Spell content** is being filled out behind the casting subsystem (the mechanics work; the actual per-faction spell lists are authored separately and not committed, since they're copyrighted).
- **Per-model rules** are getting a proper reckoning. The engine historically assumed special rules apply unit-wide; heroes and joined casters break that assumption, and that's being generalized rather than special-cased.
- **Base shapes beyond circles.** Model bases can be sized and shaped (circles and rectangles), but several geometry paths — swept-path-vs-terrain, pile-in, move-through-enemy, LoS blocking, objective seizure — still approximate everything as its bounding circle. Rectangles likely need facing/rotation before that's exact.
- **A few validation gaps remain.** Move-through-enemy-unit validation is a TODO (paths can currently pass through enemy bases), and the in-melee range checks let any model in an engaged unit fight rather than only those actually in base contact.
- **Morale and fatigue** have their core outcomes (Shaken/Rout) but the modifier surface — fatigue effects, Fear/Fearless, roll modifiers — is partial.
- **Movement/range modifier rules** (Strider, the various range-extension rules) are declared but not yet threaded through the validation path.

A lot of the above lands feature-by-feature, each with its own integration test, so the test suite (800+ tests, NUnit) is the most reliable source of truth for "what actually works right now." When in doubt, grep the tests.

---

## Build & test

It's a standard .NET 8 class library — nothing exotic to install.

```bash
# Build
dotnet build FutureOfDarkGrimness.csproj

# Run the tests (this is the real spec — 800+ of them)
dotnet test FutureOfDarkGrimness.csproj
```

The engine doesn't run on its own — it needs a front-end to give it players and a window (or a terminal). The sibling Raylib client is the reference application; clone that and run it if you want to actually watch a game.

---

## Layout, briefly

| Folder | What's in it |
|---|---|
| `EngineInterface/` | `IFDGGame` — the front-end's single point of contact |
| `GameModel/` | `FDGServer` (the driver) and the local/client game implementations |
| `StateMachine/` | The phase/turn/stage graph — setup, deployment, movement, shooting, melee, victory |
| `StageResolution/` | The request/resolver infrastructure and every request type |
| `TableState/` | The observable game world — units, models, terrain, objectives, zones |
| `Rules/` | The data-driven special-rule framework (conditions, effects, hooks, catalog) |
| `Ai/` | The computer-opponent resolvers |
| `Network/` | TCP host/client, lobby, state sync, networked request bridging |
| `Presentation/` | The one-way beat stream and its clocks |
| `Tests/` | The NUnit suite — the most honest documentation in the repo |
| `docs/` | Authoring docs (e.g. the special-rule JSON schema) |
</content>
