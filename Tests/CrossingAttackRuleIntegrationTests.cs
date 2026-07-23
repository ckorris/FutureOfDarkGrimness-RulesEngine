using FDG.Data;
using FDG.Players;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.Stages;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.Utilities;
using NUnit.Framework;

namespace FDG.Tests
{
    // Vertical-slice integration test for #197 P10 Crossing Attack: the auto-wound mid-move attack. Proves
    // the ability is offered at the move-through hook, that its (X) argument is threaded into the effect
    // (the first arg-driven activated ability), and that accepting rolls a pool whose 6+ successes become
    // DIRECT wounds - fed through the REAL AssignWounds stage, skipping the save, but still regenerable.
    // Also pins the offer isolation from Strafing (same hook, different op).
    [TestFixture]
    public class CrossingAttackRuleIntegrationTests
    {
        private static readonly TokenType UsedMarker = new("AbilityUsed:Crossing Attack");

        private GameDataStore _store = null!;
        private PlayerID _mover;
        private PlayerID _foe;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _mover = new PlayerID(System.Guid.NewGuid());
            _foe = new PlayerID(System.Guid.NewGuid());
        }

        [Test]
        public void Dispatch_OffersCrossing_AndQueuesAutoWoundsAndCostMarker()
        {
            var ctx = new WoundTestContext(_store, new NullPlayerRequester());
            DataBinding<UnitData> mover = MakeMover("Skimmers", crossingX: 1, new Position(0f, 0f));
            DataBinding<UnitData> enemy = MakeEnemy("Grunts", new Position(5f, 0f));

            IReadOnlyList<AbilityOffer> offers = ctx.RuleEvaluator.GatherOffers(
                new MoveThroughEnemyContext(mover.GetValue()));

            Assert.That(offers.Count, Is.EqualTo(1), "Crossing Attack is offered at the move-through hook");

            IReadOnlyList<RuleOperation> ops = ctx.RuleEvaluator.ResolveAbility(offers[0],
                new[] { (IUnit)enemy.GetValue() });

            Assert.That(ops.OfType<RuleOperation.InvokeDealAutoWounds>()
                .Any(op => op.Target == enemy.GetValue() && op.DiceCount == 1 && op.SuccessThreshold == 6),
                Is.True, "accepting queues a 1-die auto-wound pool at 6+ against the crossed enemy");
            Assert.That(ops.OfType<RuleOperation.GrantTokenToUnit>().Any(op => op.TokenToGrant.Type == UsedMarker),
                Is.True, "the once-per-activation cost marker is queued");
        }

        // The (X) is read from the bearing rule, not hardcoded: Crossing Attack(2) queues 2 dice. This is
        // the first activated ability whose effect reads ValueSource.Arg, so it also pins the arg threading
        // through ResolveAbility.
        [Test]
        public void Dispatch_ThreadsTheRuleArgument_IntoTheDiceCount()
        {
            var ctx = new WoundTestContext(_store, new NullPlayerRequester());
            DataBinding<UnitData> mover = MakeMover("Skimmers", crossingX: 2, new Position(0f, 0f));
            DataBinding<UnitData> enemy = MakeEnemy("Grunts", new Position(5f, 0f));

            IReadOnlyList<AbilityOffer> offers = ctx.RuleEvaluator.GatherOffers(
                new MoveThroughEnemyContext(mover.GetValue()));
            IReadOnlyList<RuleOperation> ops = ctx.RuleEvaluator.ResolveAbility(offers[0],
                new[] { (IUnit)enemy.GetValue() });

            Assert.That(ops.OfType<RuleOperation.InvokeDealAutoWounds>().Single().DiceCount, Is.EqualTo(2),
                "Crossing Attack(2) threads its argument to a 2-die pool");
        }

        // End-to-end through the stage: every die a 6, so Crossing Attack(1) deals 1 wound. The wound takes
        // NO save - a defense-4 model saving on a rolled 6 would block it if a save were rolled, so the
        // wound landing proves the save stage is skipped.
        [Test]
        public async Task Stage_Accept_DealsUnsaveableWound()
        {
            var requester = new StrafeRequester(accept: true);
            var ctx = new WoundTestContext(_store, requester, new AllOnFaceDiceRoller(6));
            DataBinding<UnitData> mover = MakeMover("Skimmers", crossingX: 1, new Position(0f, 0f));
            MakeEnemy("Grunts", new Position(5f, 0f), new Position(5f, 1f), new Position(5f, 2f),
                new Position(5f, 3f), new Position(5f, 4f));

            await RunCrossing(ctx, mover, new Position(10f, 0f));

            Assert.That(requester.WoundRequest, Is.Not.Null, "accepting resolves the crossing into a wound assignment");
            Assert.That(requester.WoundRequest!.TotalWoundsToAssign, Is.EqualTo(1f),
                "Crossing Attack(1) rolls one 6 = 1 wound; a rolled-6 armor save would block it if one ran, " +
                "so the wound landing proves the save is skipped");
        }

        // The unsaveable wound is still regenerable (owner ruling): every die a 6, so the lone wound is
        // ignored on the 5+ regen roll and the enemy ends untouched - proving the wound reached the
        // wound-ignore sink, unlike a wound that bypassed it.
        [Test]
        public async Task Stage_Accept_WoundRemainsSubjectToRegeneration()
        {
            var requester = new StrafeRequester(accept: true);
            var ctx = new WoundTestContext(_store, requester, new AllOnFaceDiceRoller(6));
            DataBinding<UnitData> mover = MakeMover("Skimmers", crossingX: 1, new Position(0f, 0f));
            DataBinding<UnitData> enemy = MakeEnemy("Grunts", new Position(5f, 0f), new Position(5f, 1f),
                new Position(5f, 2f), new Position(5f, 3f), new Position(5f, 4f));
            enemy.GetValue().AttachRuleDefinition(new ResolvedRule("Regeneration", CoreRuleCatalog.Regeneration));

            await RunCrossing(ctx, mover, new Position(10f, 0f));

            Assert.That(enemy.RemainingWounds(), Is.EqualTo(5f),
                "the crossing wound reaches the regen sink and is ignored on 5+, so the enemy survives untouched");
        }

        [Test]
        public async Task Stage_Decline_NoWoundsNoCost()
        {
            var requester = new StrafeRequester(accept: false);
            var ctx = new WoundTestContext(_store, requester, new AllOnFaceDiceRoller(6));
            DataBinding<UnitData> mover = MakeMover("Skimmers", crossingX: 1, new Position(0f, 0f));
            MakeEnemy("Grunts", new Position(5f, 0f));

            await RunCrossing(ctx, mover, new Position(10f, 0f));

            Assert.That(requester.WoundRequest, Is.Null, "declining resolves no attack");
            Assert.That(mover.GetValue().Tokens.HasToken(UsedMarker), Is.False, "declining spends nothing");
        }

        // Isolation: a unit carrying BOTH Strafing (DealHits) and Crossing Attack (DealAutoWounds) at the
        // same hook - each stage claims only its own op type, so neither double-offers or double-charges.
        [Test]
        public void Dispatch_StrafingAndCrossing_SplitByEffectType()
        {
            var ctx = new WoundTestContext(_store, new NullPlayerRequester());
            DataBinding<UnitData> mover = MakeMover("Skimmers", crossingX: 1, new Position(0f, 0f));
            mover.GetValue().AttachRuleDefinition(new ResolvedRule("Strafing", CoreRuleCatalog.Strafing));

            IReadOnlyList<AbilityOffer> offers = ctx.RuleEvaluator.GatherOffers(
                new MoveThroughEnemyContext(mover.GetValue()));

            Assert.That(offers.Count(o => o.Ability.Effect is Effect.DealHits), Is.EqualTo(1),
                "StrafingStage's filter claims exactly the Strafing (DealHits) ability");
            Assert.That(offers.Count(o => o.Ability.Effect is Effect.DealAutoWounds), Is.EqualTo(1),
                "CrossingAttackStage's filter claims exactly the Crossing Attack (DealAutoWounds) ability");
        }

        private static async Task RunCrossing(WoundTestContext ctx, DataBinding<UnitData> mover, Position destination)
        {
            var moveContext = new MovementActionContext(ctx, mover);
            moveContext.SubmitValidPathTemplate(new List<ModelMoveEntry>
            {
                new ModelMoveEntry(mover.GetValue().ModelBindings[0], new List<Position> { destination })
            });

            var stage = new CrossingAttackStage(ctx, new NoOpLayer<IMovementActionContext>());
            stage.OnCrossingResolved.Bind("done");
            await stage.Enter(moveContext);
        }

        private sealed class AllOnFaceDiceRoller : IDiceRoller
        {
            private readonly int _face;
            public AllOnFaceDiceRoller(int face) => _face = face;

            public IDiceResults Roll(int sideCount, float rollCount)
            {
                float[] perSide = new float[sideCount];
                perSide[_face - 1] = rollCount;
                return new DiceResults(perSide);
            }
        }

        private DataBinding<UnitData> MakeMover(string name, int crossingX, Position pos)
        {
            var model = new ModelData(0.5f, new List<Weapon>(), pos, _store);
            var modelBindings = new List<DataBinding<ModelData>> { _store.GetDataBinding<ModelData>(_store.Create(model)) };
            var unit = new UnitData(_mover, name, quality: 4, defense: 4, modelBindings: modelBindings);
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            binding.GetValue().AttachRuleDefinition(new ResolvedRule("Crossing Attack", CoreRuleCatalog.CrossingAttack,
                new RuleArgument[] { new RuleArgument.Int(crossingX) }));
            _store.Create(new ArmyData(_mover, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }

        private DataBinding<UnitData> MakeEnemy(string name, params Position[] positions)
        {
            var modelBindings = new List<DataBinding<ModelData>>();
            foreach (Position pos in positions)
            {
                var model = new ModelData(0.5f, new List<Weapon>(), pos, _store);
                modelBindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }
            var unit = new UnitData(_foe, name, quality: 4, defense: 4, modelBindings: modelBindings);
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            _store.Create(new ArmyData(_foe, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }
    }
}
