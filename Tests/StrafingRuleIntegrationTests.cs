using FDG.Data;
using FDG.Players;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.Stages;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using NUnit.Framework;

namespace FDG.Tests
{
    // Vertical-slice integration test for #042 Phase 7h (mid-move attack primitive): proves Strafing offers
    // a mid-move attack when a unit's path passes through an enemy and resolves it through the REAL
    // save->wound stages, inside the movement flow.
    //  - Geometry: GetEnemyUnitsMovedThrough flags a crossed enemy and ignores one off the path.
    //  - Dispatch: the catalog rule queues InvokeDealHits + a once-per-activation cost marker, and stops
    //    offering once the marker is present.
    //  - Stage: accepting the offer feeds 3 synthetic hits through DetermineSaveRolls/RollToSave/AssignWounds
    //    against the crossed enemy (observed via the wound request the stage emits).
    [TestFixture]
    public class StrafingRuleIntegrationTests
    {
        private static readonly TokenType UsedMarker = new("AbilityUsed:Strafing");

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
        public void Geometry_DetectsCrossedEnemy_IgnoresEnemyOffPath()
        {
            var ctx = new WoundTestContext(_store, new NullPlayerRequester());
            DataBinding<UnitData> mover = MakeUnit(_mover, "Bikers", withStrafing: true, new Position(0f, 0f));
            DataBinding<UnitData> onPath = MakeUnit(_foe, "Grunts", withStrafing: false, new Position(5f, 0f));
            DataBinding<UnitData> offPath = MakeUnit(_foe, "Bystanders", withStrafing: false, new Position(5f, 30f));

            // Move straight along z=0 from the origin through (5,0) where 'onPath' stands.
            var paths = new List<ModelMoveEntry>
            {
                new ModelMoveEntry(mover.GetValue().ModelBindings[0], new List<Position> { new Position(10f, 0f) })
            };

            List<DataBinding<UnitData>> crossed = MovementUtilities.GetEnemyUnitsMovedThrough(paths, mover, ctx);

            Assert.That(crossed, Does.Contain(onPath), "the enemy on the move line is crossed");
            Assert.That(crossed, Does.Not.Contain(offPath), "an enemy well off the line is not crossed");
        }

        [Test]
        public void Dispatch_OffersStrafing_AndQueuesDealHitsAndCostMarker()
        {
            var ctx = new WoundTestContext(_store, new NullPlayerRequester());
            DataBinding<UnitData> mover = MakeUnit(_mover, "Bikers", withStrafing: true, new Position(0f, 0f));
            DataBinding<UnitData> enemy = MakeUnit(_foe, "Grunts", withStrafing: false, new Position(5f, 0f));

            IReadOnlyList<AbilityOffer> offers = ctx.RuleEvaluator.GatherOffers(
                new MoveThroughEnemyContext(mover.GetValue()));

            Assert.That(offers.Count, Is.EqualTo(1), "Strafing is offered at the move-through hook");

            IReadOnlyList<RuleOperation> ops = ctx.RuleEvaluator.ResolveAbility(offers[0],
                new[] { (IUnit)enemy.GetValue() });

            Assert.That(ops.OfType<RuleOperation.InvokeDealHits>()
                .Any(op => op.Target == enemy.GetValue() && op.Count == 3), Is.True,
                "accepting queues 3 deal-hits against the crossed enemy");
            Assert.That(ops.OfType<RuleOperation.GrantTokenToUnit>().Any(op => op.TokenToGrant.Type == UsedMarker), Is.True,
                "the once-per-activation cost marker is queued");
        }

        [Test]
        public void Dispatch_OncePerActivation_NotOfferedAfterMarkerPresent()
        {
            var ctx = new WoundTestContext(_store, new NullPlayerRequester());
            DataBinding<UnitData> mover = MakeUnit(_mover, "Bikers", withStrafing: true, new Position(0f, 0f));

            mover.GetValue().Tokens.AddToken(new Token(UsedMarker, 1, new TokenClearTrigger.ActivationEnd()));

            IReadOnlyList<AbilityOffer> offers = ctx.RuleEvaluator.GatherOffers(
                new MoveThroughEnemyContext(mover.GetValue()));

            Assert.That(offers, Is.Empty, "with the used-marker present the once-per-activation gate is closed");
        }

        [Test]
        public async Task Stage_Accept_FeedsThreeHitsThroughSaveAndWound()
        {
            var requester = new StrafeRequester(accept: true);
            // Fixed roll of 1 → every save fails, so all three hits become wounds to assign.
            var ctx = new WoundTestContext(_store, requester, new AllOnFaceDiceRoller(1));

            DataBinding<UnitData> mover = MakeUnit(_mover, "Bikers", withStrafing: true, new Position(0f, 0f));
            DataBinding<UnitData> enemy = MakeUnit(_foe, "Grunts", withStrafing: false,
                new Position(5f, 0f), new Position(5f, 1f), new Position(5f, 2f), new Position(5f, 3f), new Position(5f, 4f));

            await RunStrafe(ctx, mover, new Position(10f, 0f));

            Assert.That(requester.WoundRequest, Is.Not.Null, "accepting resolves the strafe into a wound assignment");
            Assert.That(requester.WoundRequest!.TotalWoundsToAssign, Is.EqualTo(3f),
                "3 strafe hits, all saves failed → 3 wounds reach assignment");
        }

        [Test]
        public async Task Stage_Decline_NoWounds()
        {
            var requester = new StrafeRequester(accept: false);
            var ctx = new WoundTestContext(_store, requester, new AllOnFaceDiceRoller(1));

            DataBinding<UnitData> mover = MakeUnit(_mover, "Bikers", withStrafing: true, new Position(0f, 0f));
            DataBinding<UnitData> enemy = MakeUnit(_foe, "Grunts", withStrafing: false, new Position(5f, 0f));

            await RunStrafe(ctx, mover, new Position(10f, 0f));

            Assert.That(requester.WoundRequest, Is.Null, "declining resolves no attack");
            Assert.That(mover.GetValue().Tokens.HasToken(UsedMarker), Is.False, "declining spends nothing");
        }

        private static async Task RunStrafe(WoundTestContext ctx, DataBinding<UnitData> mover, Position destination)
        {
            var moveContext = new MovementActionContext(ctx, mover);
            moveContext.SubmitValidPathTemplate(new List<ModelMoveEntry>
            {
                new ModelMoveEntry(mover.GetValue().ModelBindings[0], new List<Position> { destination })
            });

            var stage = new StrafingStage(ctx, new NoOpLayer<IMovementActionContext>());
            stage.OnStrafeResolved.Bind("done");
            await stage.Enter(moveContext);
        }

        // FixedDiceRoller models a single die (TotalRolls=1); this scales — N requested rolls all land on
        // one face, so N strafe-save rolls all fail on face 1 (mirrors ImpactRuleIntegrationTests' helper).
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

        private DataBinding<UnitData> MakeUnit(PlayerID player, string name, bool withStrafing, params Position[] positions)
        {
            var modelBindings = new List<DataBinding<ModelData>>();
            foreach (Position pos in positions)
            {
                var model = new ModelData(0.5f, new List<Weapon>(), pos, _store);
                modelBindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            var unit = new UnitData(player, name, quality: 4, defense: 4,
                modelBindings: modelBindings);
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));

            if (withStrafing)
            {
                binding.GetValue().AttachRuleDefinition(new ResolvedRule("Strafing", CoreRuleCatalog.Strafing));
            }

            _store.Create(new ArmyData(player, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }
    }

    // Accepts/declines the strafe YesNo, and captures + auto-resolves the AssignWoundsRequest so the stage
    // completes (mirrors CapturingWoundRequester).
    internal sealed class StrafeRequester : IPlayerRequestByID
    {
        private readonly bool _accept;
        public AssignWoundsRequest? WoundRequest { get; private set; }

        public StrafeRequester(bool accept) => _accept = accept;

        public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
            where TRequest : IStageTaskRequest<TReply>
        {
            if (request is YesNoRequest)
            {
                return Task.FromResult((TReply)(object)_accept);
            }
            if (request is AssignWoundsRequest woundRequest)
            {
                WoundRequest = woundRequest;
                var result = new AssignWoundsResults(woundRequest.UnitReceivingWounds, woundRequest.TotalWoundsToAssign);
                result.AutoFill();
                return Task.FromResult((TReply)(object)result);
            }
            throw new System.InvalidOperationException("Unexpected request type: " + request.GetType());
        }
    }
}
