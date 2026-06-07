using FDG.Data;
using FDG.Players;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Stages;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.TempVisuals;
using FDG.Tests.RulesHarness;
using NUnit.Framework;

namespace FDG.Tests
{
    // Vertical-slice integration test for #042: proves Deadly's wound multiplier flows through the
    // REAL AssignWoundsStage. The stage fires the Shooting_OnPreApplyWound "when", the RuleEvaluator
    // queues MultiplyWounds, and the WoundModifierSink folds the net multiplier into the wound count
    // the stage hands to the player — none of it interpreted by the stage. The defender has 5 wounds
    // so the multiplied count stays sub-lethal and lands in the "ask the player how to assign" branch,
    // where a capturing requester records the requested wound count (the cleanest observable).
    [TestFixture]
    public class WoundRuleIntegrationTests
    {
        private GameDataStore _store = null!;
        private CapturingWoundRequester _requester = null!;
        private WoundTestContext _ctx = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _requester = new CapturingWoundRequester();
            _ctx = new WoundTestContext(_store, _requester);
        }

        [Test]
        public async Task NoRules_WoundCountUnmultiplied()
        {
            DataBinding<UnitData> attacker = MakeUnit(modelCount: 1);
            DataBinding<UnitData> defender = MakeUnit(modelCount: 5);

            await RunStage(attacker, defender, failedSaves: 1);

            Assert.That(_requester.Captured!.TotalWoundsToAssign, Is.EqualTo(1f),
                "one failed save → one wound to assign, with no rule to multiply it.");
        }

        [Test]
        public async Task DeadlyAttacker_MultipliesWoundCountByArgument()
        {
            DataBinding<UnitData> attacker = MakeUnit(modelCount: 1);
            AttachDeadly(attacker, x: 3);
            DataBinding<UnitData> defender = MakeUnit(modelCount: 5);

            await RunStage(attacker, defender, failedSaves: 1);

            Assert.That(_requester.Captured!.TotalWoundsToAssign, Is.EqualTo(3f),
                "Deadly(3) multiplies the one failed save into three wounds to assign.");
        }

        private async Task RunStage(DataBinding<UnitData> attacker, DataBinding<UnitData> defender, int failedSaves)
        {
            var layer = new NoOpLayer<ICombatMetadata>();
            var stage = new AssignWoundsStage<ICombatMetadata>(_ctx, layer);
            stage.NextStage.Bind("done");

            var weapon = new Weapon("Test", rangeInches: 48f, attacks: 1, armorPenetration: 0,
                specialRules: new HashSet<ISpecialRule_Weapon>());
            var metadata = new CombatMetadata(_ctx, attacker, defender, weapon, weaponCount: 1);

            // One FailedSaveInfo per wound (SaveCount == its dice TotalRolls == 1).
            var failedList = new List<FailedSaveInfo>();
            for (int i = 0; i < failedSaves; i++)
            {
                failedList.Add(new FailedSaveInfo(TestDice.Faces(1), new PendingSaveRolls(TestDice.Faces(1), 4)));
            }
            metadata.AddResult(new RollToSaveResults(new List<SuccessfulSaveInfo>(), failedList));

            await stage.Enter(metadata);
        }

        private static void AttachDeadly(DataBinding<UnitData> unit, int x)
        {
            unit.GetValue().AttachRuleDefinition(
                new ResolvedRule("Deadly", CoreRuleCatalog.Deadly, new RuleArgument[] { new RuleArgument.Int(x) }));
        }

        private DataBinding<UnitData> MakeUnit(int modelCount)
        {
            var modelBindings = new List<DataBinding<ModelData>>(modelCount);
            for (int i = 0; i < modelCount; i++)
            {
                var model = new ModelData(
                    baseRadiusInches: 0.75f,
                    weapons: new List<Weapon>(),
                    specialRules: new List<SpecialRule>(),
                    initialPosition: new Position(0, 0),
                    gameDataStore: _store);
                modelBindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            var unit = new UnitData(new PlayerID(System.Guid.NewGuid()), "TestUnit",
                quality: 4, defense: 4,
                specialRules: new List<SpecialRule>(),
                modelBindings: modelBindings);
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }
    }

    // Captures the AssignWoundsRequest the stage emits and auto-resolves it so the stage completes.
    // (TestGameContext's NullPlayerRequester never completes, so the "ask the player" branch needs
    // a real reply to be testable.)
    internal sealed class CapturingWoundRequester : IPlayerRequestByID
    {
        public AssignWoundsRequest? Captured { get; private set; }

        public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
            where TRequest : IStageTaskRequest<TReply>
        {
            if (request is AssignWoundsRequest woundRequest)
            {
                Captured = woundRequest;
                var result = new AssignWoundsResults(woundRequest.UnitReceivingWounds, woundRequest.TotalWoundsToAssign);
                result.AutoFill();
                return Task.FromResult((TReply)(object)result);
            }
            throw new System.InvalidOperationException("Unexpected request type: " + request.GetType());
        }
    }

    // Minimal IGameContext with a real RuleEvaluator and an injectable requester.
    internal sealed class WoundTestContext : IGameContext
    {
        public ITextOutput TextOutput { get; } = new EmptyTextOutput();
        public IDiceRoller DiceRoller { get; } = new FixedDiceRoller(4);
        public RuleEvaluator RuleEvaluator { get; } = new RuleEvaluator(new FixedDiceRoller(4));
        public IPlayerRequestByID PlayerRequester { get; }
        public TableState TableState { get; }
        public IReadWriteableGameDataStore GameDataStore { get; }
        public ITempVisualDrawer TempVisualDrawer { get; } = new NullTempVisualDrawer();
        public GameSettings Settings { get; } = GameSettings.GetDefault();
        public List<ITeam>? FirstDeploymentRollOrder => null;
        IGameContext IGameContextAccessor.GameContext => this;

        public WoundTestContext(GameDataStore store, IPlayerRequestByID requester)
        {
            GameDataStore = store;
            TableState = new TableState(store);
            PlayerRequester = requester;
        }

        public void SetFirstDeploymentRollOrder(List<ITeam> order) { }
        public void NotifyGameEnded(string result) { }
    }
}
