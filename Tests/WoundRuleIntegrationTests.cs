using FDG.Data;
using FDG.Players;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Stages;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.Presentation;
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

        // Deadly's no-carry-over confinement: against single-wound models the multiplier is WASTED — each
        // failed save is a clump of X that lands on one model, but a 1-wound model absorbs only 1, the rest
        // lost (Deadly's whole point is anti-Tough). One failed save → one dead model → one wound to assign.
        [Test]
        public async Task DeadlyAttacker_VsSingleWoundModels_MultiplierWasted()
        {
            DataBinding<UnitData> attacker = MakeUnit(modelCount: 1);
            AttachDeadly(attacker, x: 3);
            DataBinding<UnitData> defender = MakeUnit(modelCount: 5);

            await RunStage(attacker, defender, failedSaves: 1);

            Assert.That(_requester.Captured!.TotalWoundsToAssign, Is.EqualTo(1f),
                "Deadly(3) against 1-wound models is wasted: the clump kills one model, the extra 2 don't carry over.");
        }

        // Against a Tough model the clump deals the full multiplier to that one model: Deadly(3) + one
        // failed save → 3 wounds on a single Tough(5) model (which survives, so it routes to the player).
        [Test]
        public async Task DeadlyAttacker_VsToughModels_ClumpDealsMultiplierToOneModel()
        {
            DataBinding<UnitData> attacker = MakeUnit(modelCount: 1);
            AttachDeadly(attacker, x: 3);
            DataBinding<UnitData> defender = MakeUnit(modelCount: 2, woundsPerModel: 5);

            await RunStage(attacker, defender, failedSaves: 1);

            Assert.That(_requester.Captured!.TotalWoundsToAssign, Is.EqualTo(3f),
                "Deadly(3) lands a full 3-wound clump on one Tough(5) model (vs only 1 on a single-wound model).");
        }

        // Overkill within a clump is NOT carried to the next model: Deadly(3) + two failed saves against
        // Tough(5) models spends both clumps on the first model (ceil(5/3) = 2 clumps to kill), dealing 5 —
        // the 6th wound (overkill) is lost rather than spilling onto the second model. (Naive total×X = 6.)
        [Test]
        public async Task DeadlyAttacker_OverkillNotCarriedToNextModel()
        {
            DataBinding<UnitData> attacker = MakeUnit(modelCount: 1);
            AttachDeadly(attacker, x: 3);
            DataBinding<UnitData> defender = MakeUnit(modelCount: 2, woundsPerModel: 5);

            await RunStage(attacker, defender, failedSaves: 2);

            Assert.That(_requester.Captured!.TotalWoundsToAssign, Is.EqualTo(5f),
                "two clumps kill the first Tough(5) model (5 wounds); the overkill doesn't carry to the second.");
        }

        // #100 Shred: each unmodified 1 to block adds a wound. The harness rolls every failed save as an
        // unmodified 1, so two failed saves → +2 Shred wounds on top of the 2 they already dealt = 4.
        // The 5-model defender survives, routing to the player branch where the count is captured.
        [Test]
        public async Task ShredAttacker_AddsAWoundPerUnmodifiedBlockOf1()
        {
            DataBinding<UnitData> attacker = MakeUnit(modelCount: 1);
            AttachShred(attacker);
            DataBinding<UnitData> defender = MakeUnit(modelCount: 5);

            await RunStage(attacker, defender, failedSaves: 2);

            Assert.That(_requester.Captured!.TotalWoundsToAssign, Is.EqualTo(4f),
                "2 failed saves (each an unmodified 1 to block) → +2 Shred wounds → 4 total.");
        }

        // Control: no Shred → the two failed saves stay two wounds.
        [Test]
        public async Task NoShred_WoundCountUnchanged()
        {
            DataBinding<UnitData> attacker = MakeUnit(modelCount: 1);
            DataBinding<UnitData> defender = MakeUnit(modelCount: 5);

            await RunStage(attacker, defender, failedSaves: 2);

            Assert.That(_requester.Captured!.TotalWoundsToAssign, Is.EqualTo(2f));
        }

        // #093: "Shred when shooting" adds its wounds on a shooting attack (the Not(IsMelee) gate holds) —
        // 2 unmodified-1 blocks → +2 → 4, like Shred.
        [Test]
        public async Task ShredWhenShootingAttacker_Shooting_AddsWounds()
        {
            DataBinding<UnitData> attacker = MakeUnit(modelCount: 1);
            attacker.GetValue().AttachRuleDefinition(
                new ResolvedRule("Shred when shooting", CoreRuleCatalog.ShredWhenShooting));
            DataBinding<UnitData> defender = MakeUnit(modelCount: 5);

            await RunStage(attacker, defender, failedSaves: 2); // shooting

            Assert.That(_requester.Captured!.TotalWoundsToAssign, Is.EqualTo(4f),
                "shooting: the gate holds, Shred adds +2 → 4 total.");
        }

        // In melee the gate fails, so no extra wounds are injected — the two failed saves stay two wounds.
        [Test]
        public async Task ShredWhenShootingAttacker_Melee_NoExtraWounds()
        {
            DataBinding<UnitData> attacker = MakeUnit(modelCount: 1);
            attacker.GetValue().AttachRuleDefinition(
                new ResolvedRule("Shred when shooting", CoreRuleCatalog.ShredWhenShooting));
            DataBinding<UnitData> defender = MakeUnit(modelCount: 5);

            await RunStage(attacker, defender, failedSaves: 2, isMelee: true);

            Assert.That(_requester.Captured!.TotalWoundsToAssign, Is.EqualTo(2f),
                "melee: the gate fails, no Shred wounds are added.");
        }

        // Regression for the single-living-model auto-resolve branch: a lone multi-wound model (Tough) taking
        // a sub-lethal hit must be assigned the wounds actually DEALT, not its full remaining health. The
        // branch used to assign defenderRemainingWounds, instantly killing any Tough monster that took even
        // one wound (a user shot a Tough(12) Carnivo-Rex for 3 wounds and "applying 12 wounds" killed it).
        [Test]
        public async Task SingleToughModel_SubLethalHit_AssignsOnlyWoundsDealt()
        {
            DataBinding<UnitData> attacker = MakeUnit(modelCount: 1);
            DataBinding<UnitData> defender = MakeUnit(modelCount: 1, woundsPerModel: 12); // Tough(12), lone model

            CombatMetadata metadata = await RunStage(attacker, defender, failedSaves: 3);

            Assert.That(metadata.QueryForResult(out AssignWoundsResults result), Is.True);
            Assert.That(result.TotalWoundsToAssign, Is.EqualTo(3f),
                "3 failed saves → 3 wounds assigned, not the model's full 12 remaining.");
            Assert.That(result.PendingWounds, Has.Count.EqualTo(1));
            Assert.That(result.PendingWounds[0].Wounds, Is.EqualTo(3f));
            Assert.That(result.PendingWounds[0].Wounds,
                Is.LessThan(((ModelData)defender.GetValue().Models[0]).TotalWounds),
                "the lone Tough model survives a sub-lethal hit.");
        }

        private async Task<CombatMetadata> RunStage(DataBinding<UnitData> attacker, DataBinding<UnitData> defender,
            int failedSaves, bool isMelee = false)
        {
            var layer = new NoOpLayer<ICombatMetadata>();
            var stage = new AssignWoundsStage<ICombatMetadata>(_ctx, layer);
            stage.NextStage.Bind("done");

            var weapon = new Weapon("Test", rangeInches: 48f, attacks: 1, armorPenetration: 0);
            var metadata = new CombatMetadata(_ctx, attacker, defender, weapon, weaponCount: 1,
                attackerMoved: false, isMelee: isMelee);

            // One FailedSaveInfo per wound (SaveCount == its dice TotalRolls == 1).
            var failedList = new List<FailedSaveInfo>();
            for (int i = 0; i < failedSaves; i++)
            {
                failedList.Add(new FailedSaveInfo(TestDice.Faces(1), new PendingSaveRolls(TestDice.Faces(1), 4)));
            }
            metadata.AddResult(new RollToSaveResults(new List<SuccessfulSaveInfo>(), failedList));

            await stage.Enter(metadata);
            return metadata;
        }

        private static void AttachDeadly(DataBinding<UnitData> unit, int x)
        {
            unit.GetValue().AttachRuleDefinition(
                new ResolvedRule("Deadly", CoreRuleCatalog.Deadly, new RuleArgument[] { new RuleArgument.Int(x) }));
        }

        private static void AttachShred(DataBinding<UnitData> unit)
        {
            unit.GetValue().AttachRuleDefinition(new ResolvedRule("Shred", CoreRuleCatalog.Shred));
        }

        private DataBinding<UnitData> MakeUnit(int modelCount, int woundsPerModel = 1)
        {
            var modelBindings = new List<DataBinding<ModelData>>(modelCount);
            for (int i = 0; i < modelCount; i++)
            {
                var model = new ModelData(
                    baseRadiusInches: 0.75f,
                    weapons: new List<Weapon>(),
                    initialPosition: new Position(0, 0),
                    gameDataStore: _store);
                if (woundsPerModel != 1) model.SetMaxWounds(woundsPerModel); // Tough(woundsPerModel)
                modelBindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            var unit = new UnitData(new PlayerID(System.Guid.NewGuid()), "TestUnit",
                quality: 4, defense: 4,
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
        public IDiceRoller DiceRoller { get; }
        public RuleEvaluator RuleEvaluator { get; }
        public IPlayerRequestByID PlayerRequester { get; }
        public TableState TableState { get; }
        public IReadWriteableGameDataStore GameDataStore { get; }
        public IPresenter Presenter { get; }
        public GameSettings Settings { get; } = GameSettings.GetDefault();
        public List<ITeam>? FirstDeploymentRollOrder => null;
        IGameContext IGameContextAccessor.GameContext => this;

        // diceRoller defaults to a fixed 4 (Deadly tests don't roll); the wound-ignore tests inject a
        // ProbabilisticDiceRoller so Regeneration's per-wound roll is deterministic and fractional. Pass a
        // presenter (e.g. RecordingPresenter) to assert which beats a stage emits; defaults to a no-op sink.
        public WoundTestContext(GameDataStore store, IPlayerRequestByID requester, IDiceRoller? diceRoller = null,
            IPresenter? presenter = null)
        {
            GameDataStore = store;
            TableState = new TableState(store);
            PlayerRequester = requester;
            DiceRoller = diceRoller ?? new FixedDiceRoller(4);
            RuleEvaluator = new RuleEvaluator(DiceRoller);
            Presenter = presenter ?? new LocalPresenter(null, new InstantPresentationClock());
        }

        public void SetFirstDeploymentRollOrder(List<ITeam> order) { }
        public void NotifyGameEnded(string result) { }
    }
}
