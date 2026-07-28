using FDG.Data;
using FDG.Players;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.Stages;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using NUnit.Framework;

namespace FDG.Tests
{
    // #197 P22: the relational Ambush-arrival constraints - Repel Ambushers (enemy keep-out discs) and
    // Ambush Beacon (friendly waiver discs) - plus the shared PlacementDistanceRules authority every
    // placement resolver judges them through. Owner sign-offs pinned here (2026-07-28): the waiver is
    // judged PER MODEL, and it overrides BOTH restriction kinds (the flat over-9" rule and Repel's 12").
    //
    // Like CapabilityRuleQueriesTests, the conferring definitions here are LOCAL, not the shipped ones -
    // the capability seam means any rule can confer these; the shipped supplement data is pinned app-side
    // (AmbushConstraintShippedDataTests).
    [TestFixture]
    public class AmbushArrivalConstraintTests
    {
        private GameDataStore _store = null!;
        private TableState _tableState = null!;
        private RuleEvaluator _evaluator = null!;
        private PlayerID _us, _ally, _enemy;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _tableState = new TableState(_store);
            _evaluator = new RuleEvaluator(new FixedDiceRoller(4));
            _us = new PlayerID(System.Guid.NewGuid());
            _ally = new PlayerID(System.Guid.NewGuid());
            _enemy = new PlayerID(System.Guid.NewGuid());
        }

        private static SpecialRuleDefinition Repel(float distance = 12f, Condition? when = null) =>
            new("Test Repel", new[]
            {
                new HookEntry(EHookID.Lifecycle_OnCapabilityQuery, when ?? new Condition.Always(),
                    new Effect.RepelAmbushers(distance), ELifetime.UntilEndOfGame),
            }, System.Array.Empty<ActivatedAbility>());

        private static SpecialRuleDefinition Beacon(float range = 6f) =>
            new("Test Beacon", new[]
            {
                new HookEntry(EHookID.Lifecycle_OnCapabilityQuery, new Condition.Always(),
                    new Effect.AmbushBeacon(range), ELifetime.UntilEndOfGame),
            }, System.Array.Empty<ActivatedAbility>());

        // --- AmbushArrivalRules: who projects which discs -------------------------------------------

        [Test]
        public void EnemyRepelUnit_ProjectsAKeepOutDiscPerLivingModel()
        {
            DataBinding<UnitData> arriver = MakeUnit(_us, ("Arriver", null));
            MakeUnit(_enemy, ("Repeller", Repel()), new Position(30f, 30f), new Position(31f, 30f));

            IReadOnlyList<PlacementDisc> discs = AmbushArrivalRules.KeepOutDiscs(
                arriver.GetValue(), _tableState, _evaluator);

            Assert.That(discs.Count, Is.EqualTo(2), "one disc per living model of the repelling unit");
            Assert.That(discs.All(d => d.RadiusInches == 12f), Is.True);
            Assert.That(discs.Select(d => d.Center.x), Is.EquivalentTo(new[] { 30f, 31f }));
        }

        [Test]
        public void DeadRepelModels_ProjectNothing()
        {
            DataBinding<UnitData> arriver = MakeUnit(_us, ("Arriver", null));
            DataBinding<UnitData> repeller = MakeUnit(_enemy, ("Repeller", Repel()),
                new Position(30f, 30f), new Position(31f, 30f));
            ModelData casualty = repeller.GetValue().ModelBindings[1].GetValue();
            casualty.DealWounds(casualty.TotalWounds);

            IReadOnlyList<PlacementDisc> discs = AmbushArrivalRules.KeepOutDiscs(
                arriver.GetValue(), _tableState, _evaluator);

            Assert.That(discs.Count, Is.EqualTo(1), "a dead model has no table presence to keep away from");
            Assert.That(discs[0].Center.x, Is.EqualTo(30f).Within(0.001f));
        }

        [Test]
        public void AlliedRepelUnit_DoesNotRepelItsOwnSide()
        {
            _store.Create(new TeamData(0, new List<PlayerID> { _us, _ally }));
            DataBinding<UnitData> arriver = MakeUnit(_us, ("Arriver", null));
            MakeUnit(_ally, ("Allied Repeller", Repel()), new Position(30f, 30f));

            IReadOnlyList<PlacementDisc> discs = AmbushArrivalRules.KeepOutDiscs(
                arriver.GetValue(), _tableState, _evaluator);

            Assert.That(discs, Is.Empty,
                "\"enemy units using Ambush\" is judged by SIDE - a teammate's repel never pushes its own side away.");
        }

        [Test]
        public void RepelUnitStillInReserve_ProjectsNothing()
        {
            DataBinding<UnitData> arriver = MakeUnit(_us, ("Arriver", null));
            DataBinding<UnitData> repeller = MakeUnit(_enemy, ("Reserved Repeller", Repel()),
                new Position(30f, 30f));
            ReserveRules.PlaceInReserve(repeller.GetValue());

            IReadOnlyList<PlacementDisc> discs = AmbushArrivalRules.KeepOutDiscs(
                arriver.GetValue(), _tableState, _evaluator);

            Assert.That(discs, Is.Empty, "an off-table unit has no position to measure from");
        }

        [Test]
        public void RepelGatedByALiveCondition_AnswersLive()
        {
            DataBinding<UnitData> arriver = MakeUnit(_us, ("Arriver", null));
            DataBinding<UnitData> repeller = MakeUnit(_enemy,
                ("Gated Repeller", Repel(when: new Condition.TokenPresent(TokenType.SpellTokens))),
                new Position(30f, 30f));

            Assert.That(AmbushArrivalRules.KeepOutDiscs(arriver.GetValue(), _tableState, _evaluator),
                Is.Empty, "the capability answer respects the entry's Condition");

            repeller.GetValue().Tokens.AddToken(new Token(TokenType.SpellTokens, 1,
                new TokenClearTrigger.ManualOnly()));

            Assert.That(AmbushArrivalRules.KeepOutDiscs(arriver.GetValue(), _tableState, _evaluator),
                Has.Count.EqualTo(1), "re-asked once the condition holds, the disc appears");
        }

        [Test]
        public void FriendlyBeacon_ProjectsWaiverDiscs_AndEnemyBeaconDoesNot()
        {
            _store.Create(new TeamData(0, new List<PlayerID> { _us, _ally }));
            DataBinding<UnitData> arriver = MakeUnit(_us, ("Arriver", null));
            MakeUnit(_ally, ("Allied Beacon", Beacon()), new Position(10f, 10f));
            MakeUnit(_enemy, ("Enemy Beacon", Beacon()), new Position(40f, 40f));

            IReadOnlyList<PlacementDisc> waivers = AmbushArrivalRules.WaiverDiscs(
                arriver.GetValue(), _tableState, _evaluator);

            Assert.That(waivers.Count, Is.EqualTo(1), "only the own side's beacon lights the way in");
            Assert.That(waivers[0].Center.x, Is.EqualTo(10f).Within(0.001f));
            Assert.That(waivers[0].RadiusInches, Is.EqualTo(6f).Within(0.001f));
        }

        // --- PlacementDistanceRules: the combination authority --------------------------------------

        [Test]
        public void InsideAKeepOutDisc_Violates_AndTheBoundaryIsExclusive()
        {
            PlaceObjectsRequest<ModelData> request = Request(minEnemyDist: 9f,
                keepOut: new[] { new PlacementDisc(new Position(30f, 30f), 12f) });

            Assert.That(PlacementDistanceRules.ViolatesEnemyDistance(request,
                new Position(35f, 30f), NoEnemies), Is.True, "5\" from a 12\" repel source is out");
            Assert.That(PlacementDistanceRules.ViolatesEnemyDistance(request,
                new Position(42f, 30f), NoEnemies), Is.False,
                "exactly 12.0\" is legal - \"over 12\" away\" mirrors the over-9\" rule's exclusive boundary");
        }

        [Test]
        public void TheFlatMinimumStillApplies_WithNoDiscsInvolved()
        {
            PlaceObjectsRequest<ModelData> request = Request(minEnemyDist: 9f);
            var enemies = new List<Position> { new Position(20f, 20f) };

            Assert.That(PlacementDistanceRules.ViolatesEnemyDistance(request,
                new Position(25f, 20f), enemies), Is.True);
            Assert.That(PlacementDistanceRules.ViolatesEnemyDistance(request,
                new Position(29.5f, 20f), enemies), Is.False);
        }

        [Test]
        public void InsideAWaiver_TheFlatMinimumIsIgnored()
        {
            PlaceObjectsRequest<ModelData> request = Request(minEnemyDist: 9f,
                waivers: new[] { new PlacementDisc(new Position(20f, 25f), 6f) });
            var enemies = new List<Position> { new Position(20f, 20f) };

            Assert.That(PlacementDistanceRules.ViolatesEnemyDistance(request,
                new Position(20f, 22f), enemies), Is.False,
                "2\" from an enemy but within 6\" of the beacon - the waiver overrides the over-9\" rule");
        }

        [Test]
        public void InsideAWaiver_AKeepOutDiscIsIgnoredToo()
        {
            PlaceObjectsRequest<ModelData> request = Request(minEnemyDist: 9f,
                keepOut: new[] { new PlacementDisc(new Position(20f, 20f), 12f) },
                waivers: new[] { new PlacementDisc(new Position(20f, 25f), 6f) });

            Assert.That(PlacementDistanceRules.ViolatesEnemyDistance(request,
                new Position(20f, 24f), NoEnemies), Is.False,
                "owner sign-off 2026-07-28: the beacon waives BOTH restriction kinds, Repel's 12\" included");
        }

        [Test]
        public void TheWaiverIsPerModel_APointOutsideItStillAnswersToEveryRestriction()
        {
            PlaceObjectsRequest<ModelData> request = Request(minEnemyDist: 9f,
                keepOut: new[] { new PlacementDisc(new Position(20f, 20f), 12f) },
                waivers: new[] { new PlacementDisc(new Position(20f, 25f), 6f) });

            Assert.That(PlacementDistanceRules.ViolatesEnemyDistance(request,
                new Position(20f, 31.5f), NoEnemies), Is.True,
                "11.5\" from the repel source and past the beacon's 6\" - each model is judged where IT stands");
            Assert.That(PlacementDistanceRules.IsWaived(request, new Position(20f, 31f)), Is.True,
                "exactly 6.0\" from the beacon is still within it (inclusive), unlike the exclusive keep-outs");
        }

        // --- Stage wiring: the arrival request carries the discs ------------------------------------

        [Test]
        public async Task ArrivalRequest_CarriesKeepOutAndWaiverDiscs()
        {
            _store.Create(new TeamData(0, new List<PlayerID> { _us, _ally }));
            DataBinding<UnitData> ambush = MakeUnit(_us, ("Shifters", null));
            ambush.GetValue().AttachRuleDefinition(new ResolvedRule("Ambush", CoreRuleCatalog.Ambush));
            ReserveRules.PlaceInReserve(ambush.GetValue());
            MakeUnit(_enemy, ("Repeller", Repel()), new Position(30f, 30f));
            MakeUnit(_ally, ("Beacon", Beacon()), new Position(10f, 10f));

            var requester = new AmbushArrivalRequester(accept: true, destX: 20f, destZ: 20f);
            var ctx = new TriggeredMoveTestContext(_store, requester);
            var stage = new StartOfRoundExtraActionStage(ctx, new NoOpLayer<IMainPhaseContext>());
            stage.OnFinished.Bind("done");
            await stage.Enter(new TestMainPhaseContext(ctx, roundCount: 2));

            Assert.That(requester.PlaceRequest, Is.Not.Null);
            Assert.That(requester.PlaceRequest!.EnemyKeepOutDiscs, Has.Count.EqualTo(1),
                "the arrival request snapshots the enemy repel disc");
            Assert.That(requester.PlaceRequest.EnemyKeepOutDiscs[0].RadiusInches, Is.EqualTo(12f).Within(0.001f));
            Assert.That(requester.PlaceRequest.EnemyDistanceWaiverDiscs, Has.Count.EqualTo(1),
                "and the allied beacon's waiver disc");
            Assert.That(requester.PlaceRequest.EnemyDistanceWaiverDiscs[0].RadiusInches, Is.EqualTo(6f).Within(0.001f));
        }

        private static readonly IReadOnlyList<Position> NoEnemies = System.Array.Empty<Position>();

        private static PlaceObjectsRequest<ModelData> Request(float minEnemyDist,
            IReadOnlyList<PlacementDisc>? keepOut = null, IReadOnlyList<PlacementDisc>? waivers = null)
        {
            return new PlaceObjectsRequest<ModelData>(new PlayerID(System.Guid.NewGuid()), "Ambush Deploy",
                new RectangularZone(0f, 72f, 0f, 48f), new List<DataBinding<ModelData>>(),
                minDistanceFromEnemiesInches: minEnemyDist,
                enemyKeepOutDiscs: keepOut, enemyDistanceWaiverDiscs: waivers);
        }

        private DataBinding<UnitData> MakeUnit(PlayerID owner,
            (string name, SpecialRuleDefinition? rule) spec, params Position[] modelPositions)
        {
            Position[] positions = modelPositions.Length > 0
                ? modelPositions
                : new[] { new Position(1f, 1f) };

            var modelBindings = new List<DataBinding<ModelData>>();
            foreach (Position pos in positions)
            {
                var model = new ModelData(0.5f, new List<Weapon>(), pos, _store);
                modelBindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            var unit = new UnitData(owner, spec.name, quality: 4, defense: 4, modelBindings: modelBindings);
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            if (spec.rule != null)
            {
                binding.GetValue().AttachRuleDefinition(new ResolvedRule(spec.rule.Name, spec.rule));
            }

            _store.Create(new ArmyData(owner, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }
    }
}
