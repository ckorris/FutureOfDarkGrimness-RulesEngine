using FDG.Data;
using FDG.Players;
using FDG.Stages;
using FDG.StageResolution;
using FDG.TempVisuals;
using FDG.Rules.Dispatch;
using NUnit.Framework;

namespace FDG.Tests
{
    [TestFixture]
    public class GameProgressTests
    {
        // ──────────────────────────────────────────────────────────────────────
        // Serialization round-trip (the foundation save/load builds on)
        // ──────────────────────────────────────────────────────────────────────

        [Test]
        public void RoundTrip_ScalarsListsDictAndSettings_SurviveCrossStore()
        {
            var from = GameDataStore.GameDataStoreBuilder.GetDefault();

            var settings = new GameSettings
            {
                ArmyPoints = 1500,
                TerrainPieceCount = 12,
                RandomnessType = ERandomnessType.Probabilistic,
                TurnStyle = ETurnStyle.BoltAction,
                AutoPlaceObjectivesDebug = true,
                TerrainPlacementMode = ETerrainPlacementMode.LoadFromFile,
                TerrainLayoutPath = "layouts/ruins.json",
            };

            var progress = new GameProgressData(
                stage: EResumeStage.MainPhase,
                roundCount: 3,
                teamActivateOrder: new List<int> { 2, 1 },
                currentRoundTeamFinishOrder: new List<int> { 2 },
                currentTeamIndex: 1,
                currentPlayerIndexPerTeam: new Dictionary<int, int> { { 1, 0 }, { 2, 1 } },
                unactivatedUnits: new List<DataBinding<UnitData>>(),
                settings: settings);

            DataReference progressRef = from.Create(progress);
            string json = from.GetValueAsJson<GameProgressData>(progressRef);

            // Rehydrate into a fresh store (same type map) — mirrors the network replication path.
            var to = GameDataStore.GameDataStoreBuilder.GetDefault();
            to.CreateFromReferenceAndJson(progressRef, json);
            GameProgressData result = to.GetValue<GameProgressData>(progressRef);

            Assert.That(result.Stage, Is.EqualTo(EResumeStage.MainPhase));
            Assert.That(result.RoundCount, Is.EqualTo(3));
            Assert.That(result.TeamActivateOrder, Is.EqualTo(new List<int> { 2, 1 }).AsCollection);
            Assert.That(result.CurrentRoundTeamFinishOrder, Is.EqualTo(new List<int> { 2 }).AsCollection);
            Assert.That(result.CurrentTeamIndex, Is.EqualTo(1));
            Assert.That(result.CurrentPlayerIndexPerTeam.Count, Is.EqualTo(2));
            Assert.That(result.CurrentPlayerIndexPerTeam[1], Is.EqualTo(0));
            Assert.That(result.CurrentPlayerIndexPerTeam[2], Is.EqualTo(1));
            Assert.That(result.UnactivatedUnits, Is.Empty);

            Assert.That(result.Settings.ArmyPoints, Is.EqualTo(1500));
            Assert.That(result.Settings.TerrainPieceCount, Is.EqualTo(12));
            Assert.That(result.Settings.RandomnessType, Is.EqualTo(ERandomnessType.Probabilistic));
            Assert.That(result.Settings.TurnStyle, Is.EqualTo(ETurnStyle.BoltAction));
            Assert.That(result.Settings.AutoPlaceObjectivesDebug, Is.True);
            Assert.That(result.Settings.TerrainPlacementMode, Is.EqualTo(ETerrainPlacementMode.LoadFromFile));
            Assert.That(result.Settings.TerrainLayoutPath, Is.EqualTo("layouts/ruins.json"));
        }

        [Test]
        public void RoundTrip_UnactivatedUnitBindings_PreserveReferenceAndValue()
        {
            var from = GameDataStore.GameDataStoreBuilder.GetDefault();

            var player = new PlayerID(Guid.NewGuid());
            DataBinding<ModelData> model = MakeModel(from, new Position(1, 2));
            DataBinding<UnitData> unit = MakeUnit(from, player, "Grunt", new[] { model });

            var progress = new GameProgressData(
                stage: EResumeStage.MainPhase,
                roundCount: 1,
                teamActivateOrder: new List<int> { 0 },
                currentRoundTeamFinishOrder: new List<int>(),
                currentTeamIndex: 0,
                currentPlayerIndexPerTeam: new Dictionary<int, int> { { 0, 0 } },
                unactivatedUnits: new List<DataBinding<UnitData>> { unit },
                settings: GameSettings.GetDefault());
            DataReference progressRef = from.Create(progress);

            // Serialize the whole dependency chain and replay it into a fresh store, in order.
            string woundsJson = from.GetValueAsJson<float>(model.GetValue().RemainingWoundsBinding.Reference);
            string posJson = from.GetValueAsJson<Position>(model.GetValue().PositionBinding.Reference);
            string modelJson = from.GetValueAsJson<ModelData>(model.Reference);
            string unitJson = from.GetValueAsJson<UnitData>(unit.Reference);
            string progressJson = from.GetValueAsJson<GameProgressData>(progressRef);

            var to = GameDataStore.GameDataStoreBuilder.GetDefault();
            to.CreateFromReferenceAndJson(model.GetValue().RemainingWoundsBinding.Reference, woundsJson);
            to.CreateFromReferenceAndJson(model.GetValue().PositionBinding.Reference, posJson);
            to.CreateFromReferenceAndJson(model.Reference, modelJson);
            to.CreateFromReferenceAndJson(unit.Reference, unitJson);
            to.CreateFromReferenceAndJson(progressRef, progressJson);

            GameProgressData result = to.GetValue<GameProgressData>(progressRef);

            Assert.That(result.UnactivatedUnits.Count, Is.EqualTo(1));
            Assert.That(result.UnactivatedUnits[0].Reference, Is.EqualTo(unit.Reference));
            Assert.That(result.UnactivatedUnits[0].GetValue().Name, Is.EqualTo("Grunt"));
        }

        // ──────────────────────────────────────────────────────────────────────
        // Capture from live contexts
        // ──────────────────────────────────────────────────────────────────────

        [Test]
        public void CaptureFromContexts_ReflectsRoundCursorAndUnactivatedSet()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();

            var playerA = new PlayerID(Guid.NewGuid());
            var playerB = new PlayerID(Guid.NewGuid());
            var teamA = new TeamData(0, new List<PlayerID> { playerA });
            var teamB = new TeamData(1, new List<PlayerID> { playerB });
            store.Create(teamA);
            store.Create(teamB);

            // Team A: two units; Team B: one unit. All placed off-origin so GetIsOnBattlefield is true.
            DataBinding<UnitData> a1 = MakeUnit(store, playerA, "A1", new[] { MakeModel(store, new Position(1, 1)) });
            DataBinding<UnitData> a2 = MakeUnit(store, playerA, "A2", new[] { MakeModel(store, new Position(2, 1)) });
            store.Create(new ArmyData(playerA, new List<DataBinding<UnitData>> { a1, a2 }));

            DataBinding<UnitData> b1 = MakeUnit(store, playerB, "B1", new[] { MakeModel(store, new Position(1, 5)) });
            store.Create(new ArmyData(playerB, new List<DataBinding<UnitData>> { b1 }));

            var ctx = new CaptureTestCtx(store);
            var teamOrder = new List<ITeam> { teamA, teamB };
            var round = new SingleRoundContext(ctx, teamOrder, roundCount: 1);

            GameProgressData before = GameProgressUtilities.Capture(
                round, ctx.Settings, EResumeStage.MainPhase);

            Assert.That(before.RoundCount, Is.EqualTo(1));
            Assert.That(before.Stage, Is.EqualTo(EResumeStage.MainPhase));
            Assert.That(before.TeamActivateOrder, Is.EqualTo(new List<int> { 0, 1 }).AsCollection);
            Assert.That(before.CurrentTeamIndex, Is.EqualTo(0));
            Assert.That(before.CurrentPlayerIndexPerTeam.Keys, Is.EquivalentTo(new[] { 0, 1 }));
            Assert.That(before.CurrentRoundTeamFinishOrder, Is.Empty);
            Assert.That(before.UnactivatedUnits.Count, Is.EqualTo(3), "All three units start unactivated.");

            round.MarkUnitAsActivated(a1);

            GameProgressData after = GameProgressUtilities.Capture(
                round, ctx.Settings, EResumeStage.MainPhase);

            Assert.That(after.UnactivatedUnits.Count, Is.EqualTo(2), "One unit has now activated.");
            Assert.That(after.CurrentRoundTeamFinishOrder, Is.Empty, "Team A still has A2 to activate.");
        }

        [Test]
        public async Task Trigger_DeterminePlayerTurnStage_WritesRollingSnapshotToStore()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var player = new PlayerID(Guid.NewGuid());
            var team = new TeamData(0, new List<PlayerID> { player });
            store.Create(team);
            DataBinding<UnitData> unit = MakeUnit(store, player, "A1",
                new[] { MakeModel(store, new Position(1, 1)) });
            store.Create(new ArmyData(player, new List<DataBinding<UnitData>> { unit }));

            var ctx = new CaptureTestCtx(store);
            var round = new SingleRoundContext(ctx, new List<ITeam> { team }, roundCount: 3);

            var stage = new DeterminePlayerTurnStage(ctx, new NoOpLayer<ISingleRoundContext>());
            stage.OnDeterminedPlayerTurn.Bind("determined");
            stage.OnNoPlayersLeft.Bind("none");

            Assert.That(GameProgressUtilities.TryGetProgress(store), Is.Null, "No snapshot before the stage runs.");

            await stage.Enter(round);

            GameProgressData? progress = GameProgressUtilities.TryGetProgress(store);
            Assert.That(progress, Is.Not.Null, "Entering the stage should write a rolling snapshot.");
            Assert.That(progress!.RoundCount, Is.EqualTo(3));
            Assert.That(progress.UnactivatedUnits.Count, Is.EqualTo(1));
        }

        [Test]
        public void WriteProgress_UpdatesInPlaceRatherThanDuplicating()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();

            GameProgressUtilities.WriteProgress(store, MakeBareProgress(roundCount: 1));
            GameProgressUtilities.WriteProgress(store, MakeBareProgress(roundCount: 2));

            Assert.That(store.GetAllDataReferences<GameProgressData>().Count(), Is.EqualTo(1));
            Assert.That(GameProgressUtilities.TryGetProgress(store)!.RoundCount, Is.EqualTo(2));
        }

        // ──────────────────────────────────────────────────────────────────────
        // Helpers
        // ──────────────────────────────────────────────────────────────────────

        private static GameProgressData MakeBareProgress(int roundCount) => new GameProgressData(
            stage: EResumeStage.MainPhase,
            roundCount: roundCount,
            teamActivateOrder: new List<int> { 0 },
            currentRoundTeamFinishOrder: new List<int>(),
            currentTeamIndex: 0,
            currentPlayerIndexPerTeam: new Dictionary<int, int> { { 0, 0 } },
            unactivatedUnits: new List<DataBinding<UnitData>>(),
            settings: GameSettings.GetDefault());

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

        /// <summary>Minimal context: only the store + table state are exercised by the round/turn contexts.</summary>
        private class CaptureTestCtx : IGameContext
        {
            public ITextOutput TextOutput { get; } = new EmptyTextOutput();
            public IDiceRoller DiceRoller => null!;
            public RuleEvaluator RuleEvaluator => null!;
            public IPlayerRequestByID PlayerRequester => null!;
            public TableState TableState { get; }
            public IReadWriteableGameDataStore GameDataStore { get; }
            public ITempVisualDrawer TempVisualDrawer => null!;
            public GameSettings Settings { get; } = GameSettings.GetDefault();
            public List<ITeam>? FirstDeploymentRollOrder => null;
            IGameContext IGameContextAccessor.GameContext => this;

            public CaptureTestCtx(GameDataStore store)
            {
                GameDataStore = store;
                TableState = new TableState(store);
            }

            public void SetFirstDeploymentRollOrder(List<ITeam> order) { }
            public void NotifyGameEnded(string result) { }
        }
    }
}
