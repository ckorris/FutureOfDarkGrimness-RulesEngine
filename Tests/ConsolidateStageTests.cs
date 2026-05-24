using FDG.Data;
using FDG.Players;
using FDG.Stages;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.TempVisuals;
using NUnit.Framework;

namespace FDG.Tests
{
    [TestFixture]
    public class ConsolidateStageTests
    {
        [Test]
        public async Task BothAlive_FiresDisengageRequestWithOneInchCap()
        {
            var (ctx, combat, requester, _, _) = BuildBattlefield(
                attackerPositions: new[] { new Position(0, 0) },
                defenderPositions: new[] { new Position(2, 0) });
            requester.Reply = req => StayInPlace(req);

            await RunConsolidate(ctx, combat);

            Assert.That(requester.Captured, Is.Not.Null);
            Assert.That(requester.Captured!.Reason, Is.EqualTo(EConsolidationReason.Disengage));
            Assert.That(requester.Captured.MaxDistanceInches, Is.EqualTo(ConsolidateStage.DISENGAGE_MAX_DISTANCE_INCHES));
        }

        [Test]
        public async Task DefenderWiped_FiresWipeoutRequestWithThreeInchCap()
        {
            var (ctx, combat, requester, _, defender) = BuildBattlefield(
                attackerPositions: new[] { new Position(0, 0) },
                defenderPositions: new[] { new Position(2, 0) });
            KillAll(defender);
            requester.Reply = req => StayInPlace(req);

            await RunConsolidate(ctx, combat);

            Assert.That(requester.Captured, Is.Not.Null);
            Assert.That(requester.Captured!.Reason, Is.EqualTo(EConsolidationReason.Wipeout));
            Assert.That(requester.Captured.MaxDistanceInches, Is.EqualTo(ConsolidateStage.WIPEOUT_MAX_DISTANCE_INCHES));
        }

        [Test]
        public async Task AttackerWiped_NoRequestFiredAndStageExits()
        {
            var (ctx, combat, requester, attacker, _) = BuildBattlefield(
                attackerPositions: new[] { new Position(0, 0) },
                defenderPositions: new[] { new Position(2, 0) });
            KillAll(attacker);

            bool consolidated = await RunConsolidate(ctx, combat);

            Assert.That(requester.Captured, Is.Null);
            Assert.That(consolidated, Is.True);
        }

        [Test]
        public async Task PlayerCommitsDelta_AllAliveModelsMoveByDelta()
        {
            var (ctx, combat, requester, attacker, _) = BuildBattlefield(
                attackerPositions: new[] { new Position(0, 0), new Position(0, 1) },
                defenderPositions: new[] { new Position(5, 0) });
            // Disengage cap is 1"; pick 0.5" in -z direction.
            requester.Reply = req => req.UnitDataBinding.GetValue().ModelBindings
                .Where(mb => mb.GetValue().GetIsAlive())
                .Select(mb =>
                {
                    var m = mb.GetValue();
                    return new ModelMoveEntry(mb,
                        new List<Position> { new Position(m.Position.x, m.Position.z - 0.5f) });
                }).ToList();

            await RunConsolidate(ctx, combat);

            var positions = attacker.GetValue().ModelBindings.Select(mb => mb.GetValue().Position).ToList();
            Assert.That(positions[0].z, Is.EqualTo(-0.5f).Within(0.0001f));
            Assert.That(positions[1].z, Is.EqualTo(0.5f).Within(0.0001f));
        }

        [Test]
        public async Task PlayerDeclines_NoModelMoves()
        {
            var (ctx, combat, requester, attacker, _) = BuildBattlefield(
                attackerPositions: new[] { new Position(3, 4) },
                defenderPositions: new[] { new Position(5, 4) });
            requester.Reply = req => StayInPlace(req);

            await RunConsolidate(ctx, combat);

            var pos = attacker.GetValue().ModelBindings[0].GetValue().Position;
            Assert.That(pos.x, Is.EqualTo(3f));
            Assert.That(pos.z, Is.EqualTo(4f));
        }

        [Test]
        public void OverCapResponse_Throws()
        {
            var (ctx, combat, requester, _, _) = BuildBattlefield(
                attackerPositions: new[] { new Position(0, 0) },
                defenderPositions: new[] { new Position(2, 0) });
            // Disengage cap is 1"; request 5".
            requester.Reply = req => req.UnitDataBinding.GetValue().ModelBindings
                .Select(mb => new ModelMoveEntry(mb,
                    new List<Position> { new Position(5, 0) })).ToList();

            Assert.ThrowsAsync<RequestResponseInvalidException>(async () => await RunConsolidate(ctx, combat));
        }

        // ──────────────────────────────────────────────────────────────────────
        // Helpers
        // ──────────────────────────────────────────────────────────────────────

        private static async Task<bool> RunConsolidate(TestCtx ctx, CombatActionContext combat)
        {
            var stage = new ConsolidateStage(ctx, new NoOpLayer<ICombatActionContext>());
            stage.OnConsolidated.Bind("test-consolidated");
            bool fired = false;
            stage.OnConsolidated.OnWillActivate += _ => fired = true;
            await stage.Enter(combat);
            return fired;
        }

        private static List<ModelMoveEntry> StayInPlace(ConsolidationMoveRequest req) =>
            req.UnitDataBinding.GetValue().ModelBindings
                .Where(mb => mb.GetValue().GetIsAlive())
                .Select(mb => new ModelMoveEntry(mb, new List<Position>()))
                .ToList();

        private static void KillAll(DataBinding<UnitData> unit)
        {
            foreach (var mb in unit.GetValue().ModelBindings)
            {
                var m = mb.GetValue();
                m.DealWounds(m.TotalWounds);
            }
        }

        private static (TestCtx ctx, CombatActionContext combat, CapturingRequester requester,
            DataBinding<UnitData> attacker, DataBinding<UnitData> defender) BuildBattlefield(
                Position[] attackerPositions, Position[] defenderPositions)
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var requester = new CapturingRequester();
            var ctx = new TestCtx(store, requester);
            var attackerPlayer = new PlayerID(Guid.NewGuid());
            var defenderPlayer = new PlayerID(Guid.NewGuid());

            store.Create(new TeamData(0, new List<PlayerID> { attackerPlayer }));
            store.Create(new TeamData(1, new List<PlayerID> { defenderPlayer }));

            var attackerModels = attackerPositions.Select(p => MakeModel(store, p)).ToList();
            var attackerUnit = MakeUnit(store, attackerPlayer, "Attacker", attackerModels);
            store.Create(new ArmyData(attackerPlayer, new List<DataBinding<UnitData>> { attackerUnit }));

            var defenderModels = defenderPositions.Select(p => MakeModel(store, p)).ToList();
            var defenderUnit = MakeUnit(store, defenderPlayer, "Defender", defenderModels);
            store.Create(new ArmyData(defenderPlayer, new List<DataBinding<UnitData>> { defenderUnit }));

            var combat = new CombatActionContext(ctx, attackerUnit, isMelee: true);
            combat.SetDefender(defenderUnit);
            return (ctx, combat, requester, attackerUnit, defenderUnit);
        }

        private static DataBinding<ModelData> MakeModel(GameDataStore store, Position p)
        {
            var model = new ModelData(
                baseRadiusInches: 0.5f,
                weapons: new List<Weapon>(),
                specialRules: new List<SpecialRule>(),
                initialPosition: p,
                gameDataStore: store);
            return store.GetDataBinding<ModelData>(store.Create(model));
        }

        private static DataBinding<UnitData> MakeUnit(GameDataStore store, PlayerID playerID, string name,
            IEnumerable<DataBinding<ModelData>> models)
        {
            var unit = new UnitData(playerID, name, quality: 4, defense: 4,
                specialRules: new List<SpecialRule>(),
                modelBindings: models.ToList());
            return store.GetDataBinding<UnitData>(store.Create(unit));
        }

        internal class TestCtx : IGameContext
        {
            public ITextOutput TextOutput { get; } = new EmptyTextOutput();
            public IDiceRoller DiceRoller { get; } = new FixedDiceRoller(4);
            public IPlayerRequestByID PlayerRequester { get; }
            public TableState TableState { get; }
            public IReadWriteableGameDataStore GameDataStore { get; }
            public ITempVisualDrawer TempVisualDrawer { get; } = new NullTempVisualDrawer();
            public GameSettings Settings { get; } = GameSettings.GetDefault();
            public List<ITeam>? FirstDeploymentRollOrder => null;
            IGameContext IGameContextAccessor.GameContext => this;

            public TestCtx(GameDataStore store, IPlayerRequestByID requester)
            {
                GameDataStore = store;
                TableState = new TableState(store);
                PlayerRequester = requester;
            }

            public void SetFirstDeploymentRollOrder(List<ITeam> order) { }
            public void NotifyGameEnded(string result) { }
        }

        internal class CapturingRequester : IPlayerRequestByID
        {
            public ConsolidationMoveRequest? Captured { get; private set; }
            public Func<ConsolidationMoveRequest, List<ModelMoveEntry>> Reply { get; set; }
                = _ => new List<ModelMoveEntry>();

            public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
                where TRequest : IStageTaskRequest<TReply>
            {
                if (request is ConsolidationMoveRequest cr)
                {
                    Captured = cr;
                    object reply = Reply(cr)!;
                    return Task.FromResult((TReply)reply);
                }
                throw new InvalidOperationException("Unexpected request type: " + request.GetType());
            }
        }
    }
}
