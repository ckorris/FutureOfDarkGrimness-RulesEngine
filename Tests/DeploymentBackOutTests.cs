using System;
using System.Collections.Generic;
using System.Linq;
using FDG.Data;
using FDG.Players;
using FDG.Stages;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using NUnit.Framework;

namespace FDG.Tests
{
    // #308: picking a unit to deploy used to be irreversible - DeployUnitStage's placement was mandatory,
    // so a mis-click cost you the choice for that deployment turn. The placement is now cancellable, and
    // backing out returns the unit to the pool AT ITS ORIGINAL SLOT and re-offers the unit list to the
    // SAME player (their turn is not over).
    //
    // Only DEPLOYMENT gets this. Scout, Ambush arrival and transport spillout run the same stage shape but
    // are consequences of an already-committed decision, with nowhere to return to.
    [TestFixture]
    public class DeploymentBackOutTests
    {
        private GameDataStore _store = null!;
        private TeamData _team = null!;
        private PlayerID _player;
        private Dictionary<ITeam, DataBinding<RectangularZone>> _zones = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _player = new PlayerID(Guid.NewGuid());
            _team = new TeamData(0, new List<PlayerID> { _player });

            var zone = new RectangularZone(0f, GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES,
                0f, GameWideConstants.DEPLOYMENT_DISTANCE_INCHES);
            _zones = new Dictionary<ITeam, DataBinding<RectangularZone>>
            {
                [_team] = _store.GetDataBinding<RectangularZone>(_store.Create(zone)),
            };
        }

        [Test]
        public async Task DeployUnit_OffersACancellablePlacement()
        {
            DataBinding<UnitData> squad = MakeUnit("Grunts", modelCount: 2);
            MakeArmy(squad);
            var requester = new PlacementRequester(cancel: true);

            await RunDeployUnit(requester, squad, poolIndex: 0, MakePool(squad));

            Assert.That(requester.Captured!.AllowCancel, Is.True,
                "deployment placement must offer Back - nothing is committed until the models land.");
            Assert.That(requester.Captured!.CancelHint, Is.Not.Empty,
                "the stage words what backing out does; the resolver no longer hard-codes it.");
        }

        [Test]
        public async Task CancelledPlacement_ReturnsTheUnitToThePool_AtItsOriginalIndex()
        {
            DataBinding<UnitData> first  = MakeUnit("Scouts", modelCount: 1);
            DataBinding<UnitData> chosen = MakeUnit("Grunts", modelCount: 2);
            DataBinding<UnitData> last   = MakeUnit("Tank", modelCount: 1);
            MakeArmy(first, chosen, last);

            // The pool as ChooseUnitToDeployStage leaves it: the chosen unit pulled out of the middle.
            List<DataBinding<UnitData>> pool = MakePool(first, last);

            (DeploymentTurnContext context, bool finished, bool backedOut) =
                await RunDeployUnit(new PlacementRequester(cancel: true), chosen, poolIndex: 1, pool);

            Assert.That(backedOut, Is.True, "a cancelled placement re-offers the unit list.");
            Assert.That(finished, Is.False, "it must not fall through to the next player's deployment.");
            Assert.That(context.UndeployedUnits[_player], Is.EqualTo(new[] { first, chosen, last }),
                "the unit goes back where the player found it, not to the end of the menu.");
            Assert.That(context.CurrentDeployingUnit, Is.Null,
                "ChooseUnitToDeployStage throws if a unit is still chosen when it is re-entered.");
            Assert.That(chosen.GetValue().GetIsOnBattlefield(), Is.False,
                "backing out places nothing.");
        }

        [Test]
        public async Task CommittedPlacement_DeploysAndDoesNotReturnToThePool()
        {
            DataBinding<UnitData> squad = MakeUnit("Grunts", modelCount: 2);
            MakeArmy(squad);
            List<DataBinding<UnitData>> pool = MakePool();

            (DeploymentTurnContext context, bool finished, bool backedOut) =
                await RunDeployUnit(new PlacementRequester(cancel: false), squad, poolIndex: 0, pool);

            Assert.That(finished, Is.True);
            Assert.That(backedOut, Is.False);
            Assert.That(context.UndeployedUnits[_player], Is.Empty, "a deployed unit does not go back.");
            Assert.That(context.CurrentDeployingUnit, Is.Null);
            Assert.That(squad.GetValue().GetIsOnBattlefield(), Is.True, "the models were placed.");
        }

        // Runs DeployUnitStage once with the given unit set as current (as ChooseUnitToDeployStage would
        // have left it, including the remembered pool index), returning which outgoing binding fired.
        private async Task<(DeploymentTurnContext Context, bool Finished, bool BackedOut)> RunDeployUnit(
            IPlayerRequestByID requester, DataBinding<UnitData> currentUnit, int poolIndex,
            List<DataBinding<UnitData>> pool)
        {
            var ctx = new TriggeredMoveTestContext(_store, requester);
            var deployment = new DeploymentTurnContext(ctx, new List<ITeam> { _team }, _zones)
            {
                CurrentDeployingUnit = currentUnit,
                CurrentDeployingUnitPoolIndex = poolIndex,
            };
            deployment.UndeployedUnits[_player] = pool;

            var stage = new DeployUnitStage(ctx, new NoOpLayer<IDeploymentTurnContext>());
            bool finished = false, backedOut = false;
            stage.OnFinish.Bind("finish"); stage.OnFinish.OnWillActivate += _ => finished = true;
            stage.BackToChooseUnit.Bind("back"); stage.BackToChooseUnit.OnWillActivate += _ => backedOut = true;

            await stage.Enter(deployment);
            return (deployment, finished, backedOut);
        }

        private List<DataBinding<UnitData>> MakePool(params DataBinding<UnitData>[] units) => units.ToList();

        private DataBinding<UnitData> MakeUnit(string name, int modelCount)
        {
            var modelBindings = new List<DataBinding<ModelData>>();
            for (int i = 0; i < modelCount; i++)
            {
                var model = new ModelData(0.5f, new List<Weapon>(), new Position(0f, 0f), _store);
                modelBindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            var unit = new UnitData(_player, name, quality: 4, defense: 4, modelBindings: modelBindings);
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }

        private void MakeArmy(params DataBinding<UnitData>[] units) =>
            _store.Create(new ArmyData(_player, units.ToList()));

        // Answers the placement either by cancelling (Back) or by standing every model on a legal spot
        // inside the deployment zone, and records the request so the cancellability can be asserted.
        private sealed class PlacementRequester : IPlayerRequestByID
        {
            private readonly bool _cancel;

            public PlaceObjectsRequest<ModelData>? Captured { get; private set; }

            public PlacementRequester(bool cancel) => _cancel = cancel;

            public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
                where TRequest : IStageTaskRequest<TReply>
            {
                if (request is PlaceObjectsRequest<ModelData> placement)
                {
                    Captured = placement;
                    CancellableResult<List<PlacedObjectEntry<ModelData>>> reply = _cancel
                        ? new Cancelled<List<PlacedObjectEntry<ModelData>>>()
                        : new Selected<List<PlacedObjectEntry<ModelData>>>(Spread(placement));
                    return Task.FromResult((TReply)(object)reply);
                }

                // Post-deployment ability offers (none in these fixtures) default to "no".
                if (request is YesNoRequest) return Task.FromResult((TReply)(object)false);

                throw new InvalidOperationException("Unexpected request type: " + request.GetType());
            }

            private static List<PlacedObjectEntry<ModelData>> Spread(PlaceObjectsRequest<ModelData> request)
            {
                var entries = new List<PlacedObjectEntry<ModelData>>();
                for (int i = 0; i < request.ModelsToPlace.Count; i++)
                {
                    entries.Add(new PlacedObjectEntry<ModelData>(request.ModelsToPlace[i],
                        new Position(10f + i * 2f, 5f)));
                }
                return entries;
            }
        }
    }
}
