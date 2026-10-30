using FDG.Ai.Resolvers;
using FDG.Data;
using FDG.Players;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Stages;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using NUnit.Framework;

namespace FDG.Tests
{
    // #426 vertical slice for Scout's Core Rules v3.6.0 placement: "After all other units are deployed, may be
    // deployed anywhere fully within 9" of their deployment zone, over 9" away from non-Scout enemy units."
    // (3.5: 12" of the zone, no enemy clause.) Drives the REAL PlaceDeferredUnitsStage with two players, and
    // judges the request it issues with the shared validators every resolver uses (PlacementUtilities'
    // base-in-zone test, PlacementDistanceRules for the keep-out discs) - so "accepted"/"rejected" here is
    // literally what the CLI, GUI and AI resolvers would say.
    [TestFixture]
    public class ScoutPlacementRuleIntegrationTests
    {
        private const float BaseRadius = 0.5f;

        private GameDataStore _store = null!;
        private PlayerID _us, _them;
        private TeamData _ourTeam = null!, _theirTeam = null!;
        private Dictionary<ITeam, DataBinding<RectangularZone>> _zones = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _us = new PlayerID(Guid.NewGuid());
            _them = new PlayerID(Guid.NewGuid());
            _ourTeam = new TeamData(0, new List<PlayerID> { _us });
            _theirTeam = new TeamData(1, new List<PlayerID> { _them });

            float w = GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES;
            float h = GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES;
            float d = GameWideConstants.DEPLOYMENT_DISTANCE_INCHES;
            _zones = new Dictionary<ITeam, DataBinding<RectangularZone>>
            {
                [_ourTeam] = _store.GetDataBinding<RectangularZone>(_store.Create(new RectangularZone(0f, w, 0f, d))),
                [_theirTeam] = _store.GetDataBinding<RectangularZone>(_store.Create(new RectangularZone(0f, w, h - d, h))),
            };
        }

        [Test]
        public void Catalog_Scout_CarriesThe36Wording_AndBothNineInchFigures()
        {
            Assert.That(CoreRuleCatalog.Scout.Description, Is.EqualTo(
                "May be set aside before deployment. After all other units are deployed, may be deployed " +
                "anywhere fully within 9\" of their deployment zone, over 9\" away from non-Scout enemy units. " +
                "Players alternate in placing Scout units, starting with the player that activates next."));

            var defer = (Effect.DeferDeployment)CoreRuleCatalog.Scout.Passive.Single().Effect;
            Assert.That(defer.Timing, Is.EqualTo(EDeferTiming.AfterNormalDeployment));
            Assert.That(defer.PlacementRangeInches, Is.EqualTo(9f), "fully within 9\" of the zone (3.5: 12\").");
            Assert.That(defer.EnemyClearanceInches, Is.EqualTo(9f), "over 9\" away from non-Scout enemy units.");
        }

        [Test]
        public async Task ZoneHalf_BaseEdgeAtNineInches_Accepted_AtNinePointFive_Rejected()
        {
            DataBinding<UnitData> scout = MakeUnit(_us, "Pathfinders", scout: true);
            MakeArmy(_us, scout);

            var requester = new ScoutCapturingRequester();
            await RunScoutPass(requester, _ourTeam, _theirTeam);

            PlaceObjectsRequest<ModelData> request = requester.RequestFor(_us);
            float zoneEdge = GameWideConstants.DEPLOYMENT_DISTANCE_INCHES;
            IBaseShape shape = scout.GetValue().ModelBindings[0].GetValue().BaseShape;
            var facing = new Float2(0f, 1f);

            // The base's far edge exactly 9" past the zone edge: fully within 9" of the zone.
            Assert.That(PlacementUtilities.IsBaseWithinZone(new Position(36f, zoneEdge + 9f - BaseRadius), shape,
                facing, request.DeploymentZone), Is.True, "a base reaching exactly 9\" from the zone is legal.");
            // Half an inch further: part of the base is now more than 9" from the zone.
            Assert.That(PlacementUtilities.IsBaseWithinZone(new Position(36f, zoneEdge + 9.5f - BaseRadius), shape,
                facing, request.DeploymentZone), Is.False, "9.5\" from the zone is outside the Scout band.");
        }

        [Test]
        public async Task EnemyHalf_NonScoutEnemyAtEightInches_Rejected_EnemyScoutAtEightInches_Accepted()
        {
            DataBinding<UnitData> ourScout = MakeUnit(_us, "Pathfinders", scout: true);
            MakeArmy(_us, ourScout);

            // Their normally-deployed unit already stands on the table; their Scout is placed in the same pass,
            // BEFORE ours (they activate first, so their Scout goes first).
            DataBinding<UnitData> theirGrunts = MakeUnit(_them, "Grunts", scout: false);
            PlaceAt(theirGrunts, new Position(36f, 26f));
            DataBinding<UnitData> theirScout = MakeUnit(_them, "Rangers", scout: true);
            MakeArmy(_them, theirGrunts, theirScout);

            var requester = new ScoutCapturingRequester();
            requester.PlaceUnitsOf(_them, new Position(50f, 26f));
            await RunScoutPass(requester, _theirTeam, _ourTeam);

            Assert.That(theirScout.GetValue().GetIsOnBattlefield(), Is.True, "their Scout landed first.");
            PlaceObjectsRequest<ModelData> request = requester.RequestFor(_us);
            var noFlatEnemies = new List<Position>(); // the clause rides on discs, not the flat minimum

            Assert.That(request.MinDistanceFromEnemiesInches, Is.EqualTo(0f),
                "the flat minimum would push off the enemy Scout too; the clause is carried as discs.");
            Assert.That(PlacementDistanceRules.ViolatesEnemyDistance(request, new Position(36f, 18f), noFlatEnemies),
                Is.True, "8\" from a non-Scout enemy unit is too close.");
            Assert.That(PlacementDistanceRules.ViolatesEnemyDistance(request, new Position(36f, 17f), noFlatEnemies),
                Is.False, "exactly 9\" is legal - 'over 9\"' is exclusive at the boundary, as for Ambush.");
            Assert.That(PlacementDistanceRules.ViolatesEnemyDistance(request, new Position(50f, 18f), noFlatEnemies),
                Is.False, "8\" from an enemy SCOUT is fine - an enemy Scout does not push back.");
            Assert.That(request.EnemyKeepOutDiscs, Has.Count.EqualTo(theirGrunts.GetValue().ModelBindings.Count),
                "one 9\" disc per living model of the non-Scout enemy unit, none for the Scout.");
            Assert.That(request.EnemyKeepOutDiscs.Select(disc => disc.RadiusInches), Is.All.EqualTo(9f));
            Assert.That(request.EnemyKeepOutNote, Does.Contain("non-Scout").And.Contain("9\""),
                "the resolvers' rejection reason is worded from the same number.");
        }

        [Test]
        public async Task OwnNonScoutUnits_DoNotPushBack()
        {
            DataBinding<UnitData> ourGrunts = MakeUnit(_us, "Grunts", scout: false);
            PlaceAt(ourGrunts, new Position(36f, 12f));
            DataBinding<UnitData> ourScout = MakeUnit(_us, "Pathfinders", scout: true);
            MakeArmy(_us, ourGrunts, ourScout);

            var requester = new ScoutCapturingRequester();
            await RunScoutPass(requester, _ourTeam, _theirTeam);

            Assert.That(requester.RequestFor(_us).EnemyKeepOutDiscs, Is.Empty,
                "only ENEMY units project the Scout keep-out.");
        }

        [Test]
        public async Task AiPlacement_FindsTheLegalStrip_BehindAnEnemyLine()
        {
            // A wall of their (non-Scout) models across the table 12" up leaves only a thin strip of our Scout
            // band legal (z under ~3.5"). The solo bot aims at the middle of the band; it must search its way
            // into the strip, never land inside a keep-out disc.
            var line = new List<DataBinding<UnitData>>();
            for (float x = 3f; x < GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES; x += 6f)
            {
                DataBinding<UnitData> picket = MakeUnit(_them, $"Picket {x}", scout: false, modelCount: 1);
                PlaceAt(picket, new Position(x, 12f));
                line.Add(picket);
            }
            MakeArmy(_them, line.ToArray());
            DataBinding<UnitData> ourScout = MakeUnit(_us, "Pathfinders", scout: true);
            MakeArmy(_us, ourScout);

            var ai = new AiPlaceObjectsResolver<ModelData>(new TableState(_store));
            var requester = new ScoutCapturingRequester(ai);
            await RunScoutPass(requester, _ourTeam, _theirTeam);

            PlaceObjectsRequest<ModelData> request = requester.RequestFor(_us);
            foreach (DataBinding<ModelData> model in ourScout.GetValue().ModelBindings)
            {
                ModelData m = model.GetValue();
                Assert.That(PlacementUtilities.IsBaseWithinZone(m.Position, m.BaseShape, m.Facing,
                    request.DeploymentZone), Is.True, "inside the Scout band.");
                Assert.That(PlacementDistanceRules.ViolatesEnemyDistance(request, m.Position, new List<Position>()),
                    Is.False, $"model at ({m.Position.x:F1}, {m.Position.z:F1}) keeps over 9\" from the line.");
            }
        }

        // --- helpers ---

        // The Scout pass with `first` activating next (the deployment roll order's head).
        private async Task RunScoutPass(ScoutCapturingRequester requester, ITeam first, ITeam second)
        {
            var ctx = new TriggeredMoveTestContext(_store, requester);
            var deployment = new DeploymentTurnContext(ctx, new List<ITeam> { first, second }, _zones);
            var stage = new PlaceDeferredUnitsStage(ctx, new NoOpLayer<IDeploymentTurnContext>());
            stage.OnFinish.Bind("done");
            await stage.Enter(deployment);
        }

        private void PlaceAt(DataBinding<UnitData> unit, Position at)
        {
            int i = 0;
            foreach (DataBinding<ModelData> model in unit.GetValue().ModelBindings)
            {
                model.GetValue().SetPosition(new Position(at.x + i * 1.1f, at.z));
                i++;
            }
        }

        private DataBinding<UnitData> MakeUnit(PlayerID owner, string name, bool scout, int modelCount = 3)
        {
            var modelBindings = new List<DataBinding<ModelData>>();
            for (int i = 0; i < modelCount; i++)
            {
                var model = new ModelData(BaseRadius, new List<Weapon>(), new Position(0f, 0f), _store);
                modelBindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            var unit = new UnitData(owner, name, quality: 4, defense: 4, modelBindings: modelBindings);
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            if (scout) binding.GetValue().AttachRuleDefinition(new ResolvedRule("Scout", CoreRuleCatalog.Scout));
            return binding;
        }

        private void MakeArmy(PlayerID owner, params DataBinding<UnitData>[] units) =>
            _store.Create(new ArmyData(owner, units.ToList()));
    }

    // Captures every Scout placement request by player and answers it: through a real resolver when one is
    // given, else by lining the unit up at a per-player spot (or the zone centre).
    internal sealed class ScoutCapturingRequester : IPlayerRequestByID
    {
        private readonly IStageResolver<PlaceObjectsRequest<ModelData>,
            CancellableResult<List<PlacedObjectEntry<ModelData>>>>? _resolver;
        private readonly Dictionary<PlayerID, Position> _spots = new();

        public List<PlaceObjectsRequest<ModelData>> Requests { get; } = new();

        public ScoutCapturingRequester(IStageResolver<PlaceObjectsRequest<ModelData>,
            CancellableResult<List<PlacedObjectEntry<ModelData>>>>? resolver = null)
        {
            _resolver = resolver;
        }

        public void PlaceUnitsOf(PlayerID player, Position at) => _spots[player] = at;

        public PlaceObjectsRequest<ModelData> RequestFor(PlayerID player) =>
            Requests.Single(r => r.TargetPlayerID == player);

        public async Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
            where TRequest : IStageTaskRequest<TReply>
        {
            if (request is not PlaceObjectsRequest<ModelData> place)
                throw new InvalidOperationException("Unexpected request type: " + request.GetType());

            Requests.Add(place);
            if (_resolver != null) return (TReply)(object)await _resolver.Resolve(place);

            Position at = _spots.TryGetValue(place.TargetPlayerID, out Position spot)
                ? spot
                : new Position(place.DeploymentZone.Bounds.CenterX, place.DeploymentZone.Bounds.CenterZ);
            var entries = place.ModelsToPlace
                .Select((m, i) => new PlacedObjectEntry<ModelData>(m, new Position(at.x + i * 1.1f, at.z)))
                .ToList();
            return (TReply)(object)new Selected<List<PlacedObjectEntry<ModelData>>>(entries);
        }
    }
}
