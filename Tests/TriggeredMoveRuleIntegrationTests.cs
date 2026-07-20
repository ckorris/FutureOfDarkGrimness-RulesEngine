using FDG.Data;
using FDG.Players;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.Stages;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.Presentation;
using NUnit.Framework;

namespace FDG.Tests
{
    // Vertical-slice integration test for #042 Phase 7h: proves movement is now an INVOCABLE
    // subsystem and that an InvokeTriggeredMove operation flows through the real MovementExecutor.
    //  - TryMove_* drive the movement primitive directly (commit on success, no mutation on a
    //    rejected over-budget move).
    //  - Vanguard_ThroughSeam drives the rule end-to-end: ResolveAbility -> OperationExecutor ->
    //    GameOperationServices -> MovementExecutor, faking only the player's destination choice via
    //    a canned path requester (mirrors how WoundRuleIntegrationTests fakes wound assignment).
    [TestFixture]
    public class TriggeredMoveRuleIntegrationTests
    {
        private GameDataStore _store = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
        }

        [Test]
        public void TryMove_WithinBudget_CommitsNewPositions()
        {
            var ctx = new TriggeredMoveTestContext(_store, new NullPlayerRequester());
            DataBinding<UnitData> unit = MakeUnit(new Position(0f, 0f), new Position(0f, 1f));

            // Translate the whole unit +3" along x — within the 9" budget, coherency preserved.
            List<ModelMoveEntry> paths = TranslatePaths(unit, dx: 3f, dz: 0f);

            bool moved = MovementExecutor.TryMove(ctx, unit, paths, maxInches: 9f, out var errors, out _);

            Assert.That(moved, Is.True, "a 3\" move is within the 9\" budget");
            Assert.That(errors, Is.Empty);
            AssertModelAt(unit, 0, 3f, 0f);
            AssertModelAt(unit, 1, 3f, 1f);
        }

        [Test]
        public void TryMove_OverBudget_RejectsAndLeavesPositionsUnchanged()
        {
            var ctx = new TriggeredMoveTestContext(_store, new NullPlayerRequester());
            DataBinding<UnitData> unit = MakeUnit(new Position(0f, 0f), new Position(0f, 1f));

            List<ModelMoveEntry> paths = TranslatePaths(unit, dx: 20f, dz: 0f);

            bool moved = MovementExecutor.TryMove(ctx, unit, paths, maxInches: 9f, out var errors, out _);

            Assert.That(moved, Is.False, "a 20\" move exceeds the 9\" budget");
            Assert.That(errors, Is.Not.Empty);
            AssertModelAt(unit, 0, 0f, 0f);
            AssertModelAt(unit, 1, 0f, 1f);
        }

        [Test]
        public async Task Vanguard_ThroughSeam_RepositionsTheUnit()
        {
            var requester = new CannedMovePathRequester(dx: 5f, dz: 0f);
            var ctx = new TriggeredMoveTestContext(_store, requester);

            DataBinding<UnitData> unit = MakeUnit(new Position(0f, 0f), new Position(0f, 1f));

            // Vanguard: after deploying, may move up to 9" once per game (test 17's ability shape).
            var ability = new ActivatedAbility(EHookID.Deployment_OnUnitDeployed, new Cost.OncePerGame(),
                new TargetSelector(0f, 1, 1, ETargetAffinity.Self, false),
                new Effect.TriggeredMove(MaxInches: 9f, IsOptional: true), new Condition.Always());

            IReadOnlyList<RuleOperation> ops = ctx.RuleEvaluator.ResolveAbility(
                new AbilityOffer(unit.GetValue(), "Vanguard", ability), new[] { unit.GetValue() });

            await OperationExecutor.Execute(ops, new GameOperationServices(ctx));

            Assert.That(requester.Captured, Is.Not.Null, "the seam issued a movement-path request");
            Assert.That(requester.Captured!.MaxDistanceInches, Is.EqualTo(9f).Within(0.001f),
                "the 9\" triggered-move budget reaches the resolver");
            AssertModelAt(unit, 0, 5f, 0f);
            AssertModelAt(unit, 1, 5f, 1f);
        }

        // #208: an OPTIONAL triggered move whose resolver hands back a path the authoritative validator
        // rejects (here a cohesion-breaking hold - the models start >1" apart, as a unit spread by
        // post-melee intermingling does, and the resolver can only hold them there) is DECLINED, not
        // faulted. The unit stays exactly where it was and no exception escapes.
        [Test]
        public async Task TriggeredMove_OptionalInvalidPath_DeclinesInsteadOfFaulting()
        {
            var ctx = new TriggeredMoveTestContext(_store, new CannedHoldPathRequester());
            DataBinding<UnitData> unit = MakeUnit(new Position(0f, 0f), new Position(5f, 0f));

            var move = new RuleOperation.InvokeTriggeredMove(unit.GetValue(), MaxInches: 3f, IsOptional: true);

            Assert.DoesNotThrowAsync(() =>
                OperationExecutor.Execute(new RuleOperation[] { move }, new GameOperationServices(ctx)));
            AssertModelAt(unit, 0, 0f, 0f);
            AssertModelAt(unit, 1, 5f, 0f);
        }

        // The forced-move twin: a NON-optional triggered move (a spell pushing an enemy) that comes back
        // invalid is a genuine engine bug, not a legal "no thanks" - so it still faults loudly. Guards
        // that the #208 decline is scoped to optional moves and did not swallow forced-move errors.
        [Test]
        public void TriggeredMove_ForcedInvalidPath_StillFaults()
        {
            var ctx = new TriggeredMoveTestContext(_store, new CannedHoldPathRequester());
            DataBinding<UnitData> unit = MakeUnit(new Position(0f, 0f), new Position(5f, 0f));

            var move = new RuleOperation.InvokeTriggeredMove(unit.GetValue(), MaxInches: 3f, IsOptional: false);

            Assert.ThrowsAsync<RequestResponseInvalidException>(() =>
                OperationExecutor.Execute(new RuleOperation[] { move }, new GameOperationServices(ctx)));
        }

        // #208: the clean decline channel - an optional move is cancellable (allowCancel = isOptional), so
        // a resolver with no legal destination (the AI's stuck case) replies Cancelled. That is a decline,
        // not a fault: the unit stays put and no exception escapes.
        [Test]
        public async Task TriggeredMove_OptionalCancelled_DeclinesInsteadOfFaulting()
        {
            var requester = new CannedCancelMovePathRequester();
            var ctx = new TriggeredMoveTestContext(_store, requester);
            DataBinding<UnitData> unit = MakeUnit(new Position(0f, 0f), new Position(0f, 1f));

            var move = new RuleOperation.InvokeTriggeredMove(unit.GetValue(), MaxInches: 3f, IsOptional: true);

            Assert.DoesNotThrowAsync(() =>
                OperationExecutor.Execute(new RuleOperation[] { move }, new GameOperationServices(ctx)));
            Assert.That(requester.Captured!.AllowCancel, Is.True,
                "an optional triggered move offers the resolver a decline (allowCancel = isOptional)");
            AssertModelAt(unit, 0, 0f, 0f);
            AssertModelAt(unit, 1, 0f, 1f);
        }

        // A forced move is NOT cancellable (allowCancel is false), so a Cancelled reply is an engine
        // contract violation and still faults - the pre-#208 behavior, preserved for forced moves.
        [Test]
        public void TriggeredMove_ForcedCancelled_StillFaults()
        {
            var requester = new CannedCancelMovePathRequester();
            var ctx = new TriggeredMoveTestContext(_store, requester);
            DataBinding<UnitData> unit = MakeUnit(new Position(0f, 0f), new Position(0f, 1f));

            var move = new RuleOperation.InvokeTriggeredMove(unit.GetValue(), MaxInches: 3f, IsOptional: false);

            Assert.ThrowsAsync<RequestResponseInvalidException>(() =>
                OperationExecutor.Execute(new RuleOperation[] { move }, new GameOperationServices(ctx)));
        }

        // Harassing is a PASSIVE rule (a HookEntry, not an activated ability) that fires at the
        // post-shoot hook PostShootStage now drives. Proves the same TriggeredMove seam reached from a
        // static rule at Shooting_OnPostShoot: EvaluateAll -> OperationExecutor -> movement subsystem.
        [Test]
        public async Task Harassing_AtPostShootHook_RepositionsTheUnit()
        {
            var requester = new CannedMovePathRequester(dx: 2f, dz: 0f);
            var ctx = new TriggeredMoveTestContext(_store, requester);

            DataBinding<UnitData> unit = MakeUnit(new Position(0f, 0f), new Position(0f, 1f));
            unit.GetValue().AttachRuleDefinition(new ResolvedRule("Harassing", CoreRuleCatalog.Harassing));

            IReadOnlyList<RuleOperation> ops = ctx.RuleEvaluator.EvaluateAll(
                new PostShootActionContext(unit.GetValue()), RuleParticipant.Actor(unit.GetValue()));

            await OperationExecutor.Execute(ops, new GameOperationServices(ctx));

            Assert.That(requester.Captured, Is.Not.Null, "the post-shoot hook issued a movement-path request");
            Assert.That(requester.Captured!.MaxDistanceInches, Is.EqualTo(3f).Within(0.001f),
                "Harassing's 3\" post-shoot budget reaches the resolver");
            AssertModelAt(unit, 0, 2f, 0f);
            AssertModelAt(unit, 1, 2f, 1f);
        }

        // The melee twin: Harassing also fires at the post-melee hook PostMeleeStage drives, giving the
        // charged unit an optional 3" disengage. Same TriggeredMove seam, reached from Melee_OnPostMelee.
        [Test]
        public async Task Harassing_AtPostMeleeHook_RepositionsTheUnit()
        {
            var requester = new CannedMovePathRequester(dx: 0f, dz: 2f);
            var ctx = new TriggeredMoveTestContext(_store, requester);

            DataBinding<UnitData> unit = MakeUnit(new Position(0f, 0f), new Position(1f, 0f));
            unit.GetValue().AttachRuleDefinition(new ResolvedRule("Harassing", CoreRuleCatalog.Harassing));

            IReadOnlyList<RuleOperation> ops = ctx.RuleEvaluator.EvaluateAll(
                new PostMeleeActionContext(unit.GetValue()), RuleParticipant.Actor(unit.GetValue()));

            await OperationExecutor.Execute(ops, new GameOperationServices(ctx));

            Assert.That(requester.Captured, Is.Not.Null, "the post-melee hook issued a movement-path request");
            Assert.That(requester.Captured!.MaxDistanceInches, Is.EqualTo(3f).Within(0.001f),
                "Harassing's 3\" post-melee budget reaches the resolver");
            AssertModelAt(unit, 0, 0f, 2f);
            AssertModelAt(unit, 1, 1f, 2f);
        }

        // The post-combat-move family clones Harassing's shape but differs in WHICH hook each carries.
        // These guard the catalog wiring so a copy-paste hook mistake (e.g. a "Shooter" rule firing in
        // melee) is caught: each rule must yield a move at its hook and nothing at the other.
        [Test]
        public void HitAndRunShooter_FiresOnShootHookOnly()
        {
            AssertFiresAt(CoreRuleCatalog.HitAndRunShooter, "Hit & Run Shooter", shoot: true, melee: false);
        }

        [Test]
        public void HitAndRunFighter_FiresOnMeleeHookOnly()
        {
            AssertFiresAt(CoreRuleCatalog.HitAndRunFighter, "Hit & Run Fighter", shoot: false, melee: true);
        }

        [Test]
        public void HitAndRun_FiresOnBothHooks()
        {
            AssertFiresAt(CoreRuleCatalog.HitAndRun, "Hit & Run", shoot: true, melee: true);
        }

        [Test]
        public void Guerrilla_FiresOnBothHooks()
        {
            AssertFiresAt(CoreRuleCatalog.Guerrilla, "Guerrilla", shoot: true, melee: true);
        }

        // Attaches the rule to a unit and checks it produces exactly one triggered move at each hook it's
        // expected at, and none at the hooks it isn't — purely at the evaluator level (no move enacted).
        private void AssertFiresAt(SpecialRuleDefinition rule, string name, bool shoot, bool melee)
        {
            var ctx = new TriggeredMoveTestContext(_store, new NullPlayerRequester());
            DataBinding<UnitData> unit = MakeUnit(new Position(0f, 0f));
            unit.GetValue().AttachRuleDefinition(new ResolvedRule(name, rule));
            IUnit u = unit.GetValue();

            IReadOnlyList<RuleOperation> shootOps = ctx.RuleEvaluator.EvaluateAll(
                new PostShootActionContext(u), RuleParticipant.Actor(u));
            IReadOnlyList<RuleOperation> meleeOps = ctx.RuleEvaluator.EvaluateAll(
                new PostMeleeActionContext(u), RuleParticipant.Actor(u));

            Assert.That(shootOps.Count, Is.EqualTo(shoot ? 1 : 0), $"{name} post-shoot op count");
            Assert.That(meleeOps.Count, Is.EqualTo(melee ? 1 : 0), $"{name} post-melee op count");
            if (shoot) Assert.That(shootOps[0], Is.InstanceOf<RuleOperation.InvokeTriggeredMove>());
            if (melee) Assert.That(meleeOps[0], Is.InstanceOf<RuleOperation.InvokeTriggeredMove>());
        }

        // A unit without a post-shoot rule produces no operation at the hook — PostShootStage is a
        // no-op for it (no spurious move request).
        [Test]
        public void NoPostShootRule_AtPostShootHook_ProducesNoOperation()
        {
            var requester = new CannedMovePathRequester(dx: 2f, dz: 0f);
            var ctx = new TriggeredMoveTestContext(_store, requester);

            DataBinding<UnitData> unit = MakeUnit(new Position(0f, 0f));

            IReadOnlyList<RuleOperation> ops = ctx.RuleEvaluator.EvaluateAll(
                new PostShootActionContext(unit.GetValue()), RuleParticipant.Actor(unit.GetValue()));

            Assert.That(ops, Is.Empty, "a unit without a post-shoot rule yields no triggered move");
            Assert.That(requester.Captured, Is.Null, "no movement-path request is issued");
        }

        // The once-per-round gate: a unit's post-combat move is spent for the round when it actually
        // repositions, and the budget is SHARED across the shooting and melee triggers (Hit & Run is
        // "once per round after shooting OR melee"). Drives PostCombatMoveGate directly.
        [Test]
        public async Task PostCombatMove_OncePerRound_SharedAcrossShootAndMelee()
        {
            var requester = new CannedMovePathRequester(dx: 2f, dz: 0f);
            var ctx = new TriggeredMoveTestContext(_store, requester);
            DataBinding<UnitData> unit = MakeUnit(new Position(0f, 0f));
            unit.GetValue().AttachRuleDefinition(new ResolvedRule("Hit & Run", CoreRuleCatalog.HitAndRun));
            IUnit u = unit.GetValue();

            // First trigger (after shooting): moves and spends the round's budget.
            await PostCombatMoveGate.OfferIfAvailable(ctx, u, ctx.RuleEvaluator.EvaluateAll(
                new PostShootActionContext(u), RuleParticipant.Actor(u)));
            AssertModelAt(unit, 0, 2f, 0f);
            Assert.That(u.Tokens.HasToken(TokenType.PostCombatMoveUsed), Is.True,
                "moving after shooting spends the once-per-round post-combat move");

            // Second trigger same round (after melee): gated — the unit does NOT move again.
            await PostCombatMoveGate.OfferIfAvailable(ctx, u, ctx.RuleEvaluator.EvaluateAll(
                new PostMeleeActionContext(u), RuleParticipant.Actor(u)));
            AssertModelAt(unit, 0, 2f, 0f);
        }

        // Declining the optional move (a zero-distance submission) must NOT burn the round's budget —
        // the unit can still move after a later combat that round.
        [Test]
        public async Task PostCombatMove_DeclinedMove_KeepsBudget()
        {
            var requester = new CannedMovePathRequester(dx: 0f, dz: 0f); // zero move = decline
            var ctx = new TriggeredMoveTestContext(_store, requester);
            DataBinding<UnitData> unit = MakeUnit(new Position(0f, 0f));
            unit.GetValue().AttachRuleDefinition(new ResolvedRule("Hit & Run", CoreRuleCatalog.HitAndRun));
            IUnit u = unit.GetValue();

            await PostCombatMoveGate.OfferIfAvailable(ctx, u, ctx.RuleEvaluator.EvaluateAll(
                new PostShootActionContext(u), RuleParticipant.Actor(u)));

            Assert.That(u.Tokens.HasToken(TokenType.PostCombatMoveUsed), Is.False,
                "declining the optional move (zero distance) keeps the round's budget");
        }

        // Boost: with the base rule present, the post-combat move upgrades from 3" to 6". The gate
        // coalesces the base 3" op and the boost 6" op into a SINGLE 6" move — a 4" submission is legal
        // (would exceed a 3" budget) and the unit moves exactly once.
        [Test]
        public async Task HarassingBoost_UpgradesMoveTo6_WhenUnitHasHarassing()
        {
            var requester = new CannedMovePathRequester(dx: 4f, dz: 0f);
            var ctx = new TriggeredMoveTestContext(_store, requester);
            DataBinding<UnitData> unit = MakeUnit(new Position(0f, 0f));
            unit.GetValue().AttachRuleDefinition(new ResolvedRule("Harassing", CoreRuleCatalog.Harassing));
            unit.GetValue().AttachRuleDefinition(new ResolvedRule("Harassing Boost", CoreRuleCatalog.HarassingBoost));
            IUnit u = unit.GetValue();

            IReadOnlyList<RuleOperation> ops = ctx.RuleEvaluator.EvaluateAll(
                new PostShootActionContext(u), RuleParticipant.Actor(u));
            await PostCombatMoveGate.OfferIfAvailable(ctx, u, ops);

            Assert.That(requester.Captured!.MaxDistanceInches, Is.EqualTo(6f).Within(0.001f),
                "Harassing Boost coalesces with Harassing into a single 6\" move");
            AssertModelAt(unit, 0, 4f, 0f); // moved ONCE (not 3"+6")
        }

        // Boost is inert without the base rule: UnitHasRule(Harassing) fails, so no 6" move — and with no
        // Harassing there's no 3" move either, so nothing is produced.
        [Test]
        public void HarassingBoost_NoMove_WithoutHarassing()
        {
            var ctx = new TriggeredMoveTestContext(_store, new NullPlayerRequester());
            DataBinding<UnitData> unit = MakeUnit(new Position(0f, 0f));
            unit.GetValue().AttachRuleDefinition(new ResolvedRule("Harassing Boost", CoreRuleCatalog.HarassingBoost));
            IUnit u = unit.GetValue();

            IReadOnlyList<RuleOperation> ops = ctx.RuleEvaluator.EvaluateAll(
                new PostShootActionContext(u), RuleParticipant.Actor(u));

            Assert.That(ops, Is.Empty,
                "Harassing Boost's UnitHasRule(Harassing) gate fails when the unit lacks Harassing");
        }

        // Aura: "this model and its unit get X" grants the named family rule unit-wide at creation, and
        // the granted rule then fires at its hook via the read-back (needs a resolver to resolve the name).
        [Test]
        public void HitAndRunShooterAura_GrantsRuleUnitWide_FiresAtPostShootOnly()
        {
            var evaluator = new RuleEvaluator(new FixedDiceRoller(4), ruleResolver: CoreRuleCatalog.CreateResolver());
            DataBinding<UnitData> unit = MakeUnit(new Position(0f, 0f));
            unit.GetValue().AttachRuleDefinition(
                new ResolvedRule("Hit & Run Shooter Aura", CoreRuleCatalog.HitAndRunShooterAura));
            IUnit u = unit.GetValue();

            // The aura grants its rule to the unit at creation.
            UnitCreationRules.Apply(u, evaluator);
            Assert.That(u.Tokens.HasToken(TokenType.RuleGrant), Is.True, "aura granted a rule to the unit");

            // The granted Hit & Run Shooter projects via read-back and fires at post-shoot, not melee.
            IReadOnlyList<RuleOperation> shootOps = evaluator.EvaluateAll(
                new PostShootActionContext(u), RuleParticipant.Actor(u));
            IReadOnlyList<RuleOperation> meleeOps = evaluator.EvaluateAll(
                new PostMeleeActionContext(u), RuleParticipant.Actor(u));

            Assert.That(shootOps.Count, Is.EqualTo(1), "granted Hit & Run Shooter fires at post-shoot");
            Assert.That(shootOps[0], Is.InstanceOf<RuleOperation.InvokeTriggeredMove>());
            Assert.That(meleeOps, Is.Empty, "Hit & Run Shooter is shooting-only — nothing at post-melee");
        }

        // Catalog integrity: every Effect.Aura in the catalog must grant a rule the resolver actually
        // knows — guards against a typo or case mismatch in a grant name (the resolver is case-sensitive,
        // e.g. "Bane when Shooting Aura" must grant the registered "Bane when shooting"). Covers all auras
        // at once, including any added later.
        [Test]
        public void EveryCatalogAura_GrantsAResolvableRule()
        {
            RuleResolver resolver = CoreRuleCatalog.CreateResolver();
            foreach (SpecialRuleDefinition rule in CoreRuleCatalog.All)
            {
                foreach (HookEntry entry in rule.Passive)
                {
                    if (entry.Effect is Effect.Aura aura)
                    {
                        Assert.That(resolver.TryResolve(aura.RuleName, out _), Is.True,
                            $"'{rule.Name}' grants '{aura.RuleName}', which is not a registered rule");
                    }
                }
            }
        }

        private static List<ModelMoveEntry> TranslatePaths(DataBinding<UnitData> unit, float dx, float dz)
        {
            var entries = new List<ModelMoveEntry>();
            foreach (DataBinding<ModelData> model in unit.GetValue().ModelBindings)
            {
                Position start = model.GetValue().PositionBinding.GetValue();
                entries.Add(new ModelMoveEntry(model,
                    new List<Position> { new Position(start.x + dx, start.z + dz) }));
            }
            return entries;
        }

        private static void AssertModelAt(DataBinding<UnitData> unit, int index, float x, float z)
        {
            Position pos = unit.GetValue().ModelBindings[index].GetValue().PositionBinding.GetValue();
            Assert.That(pos.x, Is.EqualTo(x).Within(0.001f), $"model {index} x");
            Assert.That(pos.z, Is.EqualTo(z).Within(0.001f), $"model {index} z");
        }

        private DataBinding<UnitData> MakeUnit(params Position[] modelPositions)
        {
            var playerID = new PlayerID(System.Guid.NewGuid());

            var modelBindings = new List<DataBinding<ModelData>>(modelPositions.Length);
            foreach (Position pos in modelPositions)
            {
                var model = new ModelData(
                    baseRadiusInches: 0.5f,
                    weapons: new List<Weapon>(),
                    initialPosition: pos,
                    gameDataStore: _store);
                modelBindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            var unit = new UnitData(playerID, "Vanguards",
                quality: 4, defense: 4,
                modelBindings: modelBindings);
            DataBinding<UnitData> unitBinding = _store.GetDataBinding<UnitData>(_store.Create(unit));

            // Wrap the unit in an army, as production always does — that's how GameOperationServices
            // resolves an IUnit back to its live data binding.
            var army = new ArmyData(playerID, new List<DataBinding<UnitData>> { unitBinding });
            _store.Create(army);

            return unitBinding;
        }
    }

    // Returns a canned move that translates every model in the requested unit by a fixed delta,
    // standing in for the player choosing a reposition destination.
    internal sealed class CannedMovePathRequester : IPlayerRequestByID
    {
        private readonly float _dx;
        private readonly float _dz;
        public DefineMovementPathRequest? Captured { get; private set; }

        public CannedMovePathRequester(float dx, float dz)
        {
            _dx = dx;
            _dz = dz;
        }

        public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
            where TRequest : IStageTaskRequest<TReply>
        {
            if (request is DefineMovementPathRequest moveRequest)
            {
                Captured = moveRequest;
                var entries = new List<ModelMoveEntry>();
                foreach (DataBinding<ModelData> model in moveRequest.UnitDataBinding.GetValue().ModelBindings)
                {
                    Position start = model.GetValue().PositionBinding.GetValue();
                    entries.Add(new ModelMoveEntry(model,
                        new List<Position> { new Position(start.x + _dx, start.z + _dz) }));
                }
                return Task.FromResult((TReply)(object)new Selected<List<ModelMoveEntry>>(entries));
            }
            throw new System.InvalidOperationException("Unexpected request type: " + request.GetType());
        }
    }

    // Holds every model at its exact current position (a zero-length path per model). When the models
    // start >1" apart this is a cohesion-BREAKING submission - the #208 case a stuck resolver produces.
    internal sealed class CannedHoldPathRequester : IPlayerRequestByID
    {
        public DefineMovementPathRequest? Captured { get; private set; }

        public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
            where TRequest : IStageTaskRequest<TReply>
        {
            if (request is DefineMovementPathRequest moveRequest)
            {
                Captured = moveRequest;
                var entries = moveRequest.UnitDataBinding.GetValue().ModelBindings
                    .Select(model => new ModelMoveEntry(model,
                        new List<Position> { model.GetValue().PositionBinding.GetValue() }))
                    .ToList();
                return Task.FromResult((TReply)(object)new Selected<List<ModelMoveEntry>>(entries));
            }
            throw new System.InvalidOperationException("Unexpected request type: " + request.GetType());
        }
    }

    // Replies Cancelled to a movement request - the AI's decline when no legal path exists (#208).
    internal sealed class CannedCancelMovePathRequester : IPlayerRequestByID
    {
        public DefineMovementPathRequest? Captured { get; private set; }

        public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
            where TRequest : IStageTaskRequest<TReply>
        {
            if (request is DefineMovementPathRequest moveRequest)
            {
                Captured = moveRequest;
                return Task.FromResult((TReply)(object)new Cancelled<List<ModelMoveEntry>>());
            }
            throw new System.InvalidOperationException("Unexpected request type: " + request.GetType());
        }
    }

    // Minimal IGameContext with a real RuleEvaluator and an injectable requester (mirrors WoundTestContext).
    internal sealed class TriggeredMoveTestContext : IGameContext
    {
        public ITextOutput TextOutput { get; } = new EmptyTextOutput();
        public IDiceRoller DiceRoller { get; }
        // #193: tests get a fixed-seed stream so any Rng-driven stage behaves reproducibly.
        public Random Rng { get; } = new Random(20260709);
        public RuleEvaluator RuleEvaluator { get; }
        public IPlayerRequestByID PlayerRequester { get; }
        public TableState TableState { get; }
        public IReadWriteableGameDataStore GameDataStore { get; }
        public IPresenter Presenter { get; }
        public GameSettings Settings { get; } = GameSettings.GetDefault();
        public List<ITeam>? FirstDeploymentRollOrder => null;
        IGameContext IGameContextAccessor.GameContext => this;

        // ruleResolver is optional and defaults to null, matching the bare-evaluator default: granted-rule
        // read-back and dispatch-time WithRules resolution (#164) both no-op without one. Tests that need
        // either pass a resolver with the relevant definitions registered.
        public TriggeredMoveTestContext(GameDataStore store, IPlayerRequestByID requester,
            IDiceRoller? diceRoller = null, IPresentationSink? presentationSink = null,
            IRuleResolver? ruleResolver = null)
        {
            GameDataStore = store;
            TableState = new TableState(store);
            PlayerRequester = requester;
            DiceRoller = diceRoller ?? new FixedDiceRoller(4);
            RuleEvaluator = new RuleEvaluator(DiceRoller, ruleResolver: ruleResolver);
            Presenter = new LocalPresenter(presentationSink, new InstantPresentationClock());
        }

        public void SetFirstDeploymentRollOrder(List<ITeam> order) { }
        public void NotifyGameCompleted(GameResult result) { }
    }
}
