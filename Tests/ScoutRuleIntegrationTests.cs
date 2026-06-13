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
    // Vertical-slice integration test for #042 Phase 7h (deploy primitive): proves Scout is set aside
    // during normal deployment and placed afterward through the real machinery.
    //  - Reserve: DeploymentTurnContext fires Deployment_OnPreDeploymentSelect per unit and routes a
    //    Scout unit into DeferredUnits instead of the normal UndeployedUnits pool.
    //  - Placement: PlaceDeferredUnitsStage places the Scout in a forward-expanded zone via the real
    //    PlaceObjectsRequest flow, faking only the player's chosen positions (canned requester).
    [TestFixture]
    public class ScoutRuleIntegrationTests
    {
        private GameDataStore _store = null!;
        private TeamData _team = null!;
        private PlayerID _player;
        private Dictionary<ITeam, DataBinding<RectangularZone>> _zones = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _player = new PlayerID(System.Guid.NewGuid());
            _team = new TeamData(0, new List<PlayerID> { _player });

            // Bottom-edge deployment zone (Z 0..DEPLOYMENT_DISTANCE), as DeployAllUnitsStage builds.
            var zone = new RectangularZone(0f, GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES,
                0f, GameWideConstants.DEPLOYMENT_DISTANCE_INCHES);
            _zones = new Dictionary<ITeam, DataBinding<RectangularZone>>
            {
                [_team] = _store.GetDataBinding<RectangularZone>(_store.Create(zone)),
            };
        }

        [Test]
        public void ScoutUnits_AreReserved_NotInNormalPool()
        {
            // Two scouts + one normal unit — exercises partitioning more than one deferred unit.
            DataBinding<UnitData> scoutA = MakeUnit("Scouts A", scout: true);
            DataBinding<UnitData> normal = MakeUnit("Grunts", scout: false);
            DataBinding<UnitData> scoutB = MakeUnit("Scouts B", scout: true);
            MakeArmy(scoutA, normal, scoutB);

            var ctx = new TriggeredMoveTestContext(_store, new NullPlayerRequester());
            var deployment = new DeploymentTurnContext(ctx, new List<ITeam> { _team }, _zones);

            Assert.That(deployment.DeferredUnits.Select(d => d.Unit), Is.EquivalentTo(new[] { scoutA, scoutB }),
                "both Scouts are pulled out of the normal pool into the reserve.");
            Assert.That(deployment.UndeployedUnits[_player], Is.EqualTo(new[] { normal }),
                "only the non-Scout unit deploys normally.");
        }

        [Test]
        public async Task ScoutUnits_AllPlacedForward_AfterNormalDeployment()
        {
            // Two scouts: the placement pass must loop and place every reserved unit.
            DataBinding<UnitData> scoutA = MakeUnit("Scouts A", scout: true);
            DataBinding<UnitData> scoutB = MakeUnit("Scouts B", scout: true);
            MakeArmy(scoutA, scoutB);

            var requester = new CannedPlaceObjectsRequester();
            var ctx = new TriggeredMoveTestContext(_store, requester);
            var deployment = new DeploymentTurnContext(ctx, new List<ITeam> { _team }, _zones);

            var stage = new PlaceDeferredUnitsStage(ctx, new NoOpLayer<IDeploymentTurnContext>());
            stage.OnFinish.Bind("done");
            await stage.Enter(deployment);

            float center = GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES / 2f;
            float expectedTop = System.Math.Min(GameWideConstants.DEPLOYMENT_DISTANCE_INCHES + 12f, center);

            Assert.That(requester.PlacementCount, Is.EqualTo(2), "every reserved Scout is placed");
            Assert.That(requester.LastZone!.Top, Is.EqualTo(expectedTop).Within(0.001f),
                "Scout's zone is extended 12\" forward (toward table center), clamped at center.");

            foreach (DataBinding<UnitData> scout in new[] { scoutA, scoutB })
            {
                foreach (DataBinding<ModelData> model in scout.GetValue().ModelBindings)
                {
                    Position pos = model.GetValue().PositionBinding.GetValue();
                    Assert.That(pos.z, Is.GreaterThan(GameWideConstants.DEPLOYMENT_DISTANCE_INCHES - 0.001f),
                        $"{scout.GetValue().Name} models end forward of the original deployment zone.");
                }
            }
        }

        private DataBinding<UnitData> MakeUnit(string name, bool scout)
        {
            var modelBindings = new List<DataBinding<ModelData>>();
            for (int i = 0; i < 3; i++)
            {
                var model = new ModelData(
                    baseRadiusInches: 0.5f,
                    weapons: new List<Weapon>(),
                    initialPosition: new Position(0f, 0f),
                    gameDataStore: _store);
                modelBindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            var unit = new UnitData(_player, name, quality: 4, defense: 4,
                modelBindings: modelBindings);
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));

            if (scout)
            {
                binding.GetValue().AttachRuleDefinition(new ResolvedRule("Scout", CoreRuleCatalog.Scout));
            }

            return binding;
        }

        private void MakeArmy(params DataBinding<UnitData>[] units)
        {
            _store.Create(new ArmyData(_player, units.ToList()));
        }
    }

    // Places every requested model at the center of the request's (forward-expanded) zone,
    // standing in for the player choosing reserve-placement positions.
    internal sealed class CannedPlaceObjectsRequester : IPlayerRequestByID
    {
        public int PlacementCount { get; private set; }
        public RectangularZone? LastZone { get; private set; }

        public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
            where TRequest : IStageTaskRequest<TReply>
        {
            if (request is PlaceObjectsRequest<ModelData> placeRequest)
            {
                PlacementCount++;
                RectangularZone zone = placeRequest.DeploymentZone.GetValue();
                LastZone = zone;
                var dest = new Position((zone.Left + zone.Right) / 2f, (zone.Bottom + zone.Top) / 2f);

                var entries = placeRequest.ModelsToPlace
                    .Select(m => new PlacedObjectEntry<ModelData>(m, dest))
                    .ToList();
                return Task.FromResult((TReply)(object)entries);
            }
            throw new System.InvalidOperationException("Unexpected request type: " + request.GetType());
        }
    }
}
