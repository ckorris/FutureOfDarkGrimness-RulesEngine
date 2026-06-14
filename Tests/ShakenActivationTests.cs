using FDG.Data;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // #008 — Shaken activation behavior. A unit that begins its activation Shaken stays idle the whole
    // activation and recovers (the token clears). A unit that becomes Shaken *mid*-activation keeps the
    // token for next time. Shaken units can neither seize nor contest objectives.
    [TestFixture]
    public class ShakenActivationTests
    {
        private GameDataStore _store = null!;
        private TestGameContext _ctx = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _ctx = new TestGameContext(_store, new FixedDiceRoller(4));
        }

        // --- Activation: force-idle + recovery ---

        [Test]
        public async Task ShakenAtActivationStart_StaysIdleAndRecovers()
        {
            var unit = MakeUnit(new Position(0, 0));
            MakeShaken(unit);
            var unitCtx = NewActivation(unit); // fresh: !HasMoved, !HasAttacked

            bool reconciled = await RunChooseAction(unitCtx);

            Assert.That(reconciled, Is.True, "a Shaken unit should skip straight to end-of-activation.");
            Assert.That(unit.GetValue().Tokens.HasToken(TokenType.Shaken), Is.False,
                "it recovers — the Shaken token clears at the end of its idle activation.");
        }

        // The recover-or-keep decision reads the activation-start snapshot, NOT the action flags. This
        // is the robustness guarantee: a unit that becomes Shaken after its activation began keeps the
        // token even with no move/attack recorded — the snapshot is frozen at Reset and not re-read.
        // (Tested directly on the context to avoid driving ChooseActionStage into a player request.)
        [Test]
        public void StartedActivationShaken_IsSnapshotAtReset_AndIsFlagIndependent()
        {
            var unit = MakeUnit(new Position(0, 0));

            var notShakenCtx = NewActivation(unit);
            Assert.That(notShakenCtx.StartedActivationShaken, Is.False,
                "a unit not Shaken when its activation began snapshots false.");

            MakeShaken(unit); // becomes Shaken mid-activation, no move/attack recorded
            Assert.That(notShakenCtx.StartedActivationShaken, Is.False,
                "the snapshot is frozen at activation start — applying Shaken afterward does not flip it, so the unit keeps the token for next time.");

            var shakenCtx = NewActivation(unit); // a fresh activation that begins Shaken
            Assert.That(shakenCtx.StartedActivationShaken, Is.True,
                "a unit Shaken when its activation begins snapshots true and will recover.");
        }

        // A unit that became Shaken before its activation but has already acted (e.g. it was Shaken as a
        // defender, then on its activation it... here simulated with the attacked flag) still recovers
        // via the snapshot — and a unit Shaken only mid-activation does not. Drives the real stage on the
        // no-actions path (HasAttacked gates every action off, so no player request is made).
        [Test]
        public async Task ShakenMidActivation_DrivenThroughStage_KeepsToken()
        {
            var unit = MakeUnit(new Position(0, 0));
            var unitCtx = NewActivation(unit);    // snapshot: not Shaken
            unitCtx.RegisterAttackedFinished();   // it acted...
            MakeShaken(unit);                     // ...then became Shaken

            await RunChooseAction(unitCtx);

            Assert.That(unit.GetValue().Tokens.HasToken(TokenType.Shaken), Is.True,
                "Shaken applied after activation start keeps the token — it idles on its *next* activation.");
        }

        // --- Objectives: a Shaken unit can neither seize nor contest ---

        [Test]
        public async Task ShakenUnit_AloneOnObjective_DoesNotSeize()
        {
            var objective = CreateObjective(new Position(5, 5));
            var unit = CreateUnit(new PlayerID(Guid.NewGuid()), new Position(5, 5));
            MakeShaken(unit);

            await RunReconcileOnce();

            Assert.That(objective.OwnerID, Is.Null,
                "a Shaken unit cannot seize the objective it stands on.");
        }

        [Test]
        public async Task ShakenUnit_DoesNotContest_EnemySeizes()
        {
            var objective = CreateObjective(new Position(5, 5));
            var enemy = new PlayerID(Guid.NewGuid());
            var shaken = CreateUnit(new PlayerID(Guid.NewGuid()), new Position(5.0f, 5));
            MakeShaken(shaken);
            CreateUnit(enemy, new Position(5.5f, 5));

            await RunReconcileOnce();

            Assert.That(objective.OwnerID, Is.EqualTo(enemy),
                "the Shaken unit can't contest, so the enemy seizes uncontested.");
        }

        // --- Helpers ---

        private async Task<bool> RunChooseAction(UnitActionContext unitCtx)
        {
            var stage = new ChooseActionStage(_ctx, new NoOpLayer<IUnitActionContext>());
            bool reconciled = false;
            stage.ToReconcileEndOfActivation.Bind("ToReconcileEndOfActivation");
            stage.ToReconcileEndOfActivation.OnWillActivate += _ => reconciled = true;
            await stage.Enter(unitCtx);
            return reconciled;
        }

        private UnitActionContext NewActivation(DataBinding<UnitData> unit)
        {
            var unitCtx = new UnitActionContext(_ctx, unit);
            unitCtx.Reset(unit);
            return unitCtx;
        }

        private static void MakeShaken(DataBinding<UnitData> unit) => MakeShaken(unit.GetValue());

        private static void MakeShaken(UnitData unit) =>
            unit.Tokens.AddToken(new Token(TokenType.Shaken, 1, new TokenClearTrigger.ManualOnly()));

        private DataBinding<UnitData> MakeUnit(Position position)
        {
            var model = new ModelData(
                baseRadiusInches: 0.5f,
                weapons: new List<Weapon>(),
                initialPosition: position,
                gameDataStore: _store);
            var modelBinding = _store.GetDataBinding<ModelData>(_store.Create(model));

            var unit = new UnitData(new PlayerID(Guid.NewGuid()), "Test Unit", quality: 4, defense: 4,
                modelBindings: new List<DataBinding<ModelData>> { modelBinding });
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }

        private ObjectiveData CreateObjective(Position position)
        {
            var obj = new ObjectiveData(position, _store);
            _store.Create(obj);
            return obj;
        }

        private UnitData CreateUnit(PlayerID playerID, Position modelPosition)
        {
            var model = new ModelData(
                baseRadiusInches: 0.75f,
                weapons: new List<Weapon>(),
                initialPosition: modelPosition,
                gameDataStore: _store);
            var modelBinding = _store.GetDataBinding<ModelData>(_store.Create(model));

            var unit = new UnitData(playerID, "TestUnit", quality: 4, defense: 4,
                modelBindings: new List<DataBinding<ModelData>> { modelBinding });
            _store.Create(unit);
            return unit;
        }

        private Task RunReconcileOnce()
        {
            var stage = new ReconcileObjectivesStage(_ctx, new NoOpLayer<IMainPhaseContext>());
            stage.ToReconcileEndOfTurn.Bind(ReconcileObjectivesStage.RECONCILE_OBJECTIVES_TO_RECONCILE_NEW_TURN);
            stage.ToVictoryCalculation.Bind(ReconcileObjectivesStage.RECONCILE_OBJECTIVES_TO_VICTORY_CALCULATION_TRANSITION);
            return stage.Enter(new StubMainPhaseContext(_ctx));
        }
    }
}
