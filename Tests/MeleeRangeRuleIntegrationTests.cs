using FDG.Ai.Tactician;
using FDG.Data;
using FDG.Rules.Dispatch;
using FDG.Stages;
using FDG.Utilities;
using NUnit.Framework;

namespace FDG.Tests
{
    // #417 - Core Rules v3.6.0 (Melee): "Charging models must move into base contact with targets, or as
    // close as possible, and then defenders must do the same by moving up to 3". Models within 3" of enemies
    // must strike with all melee weapons, which works like shooting. [...] If either unit is destroyed, the
    // other may move by up to 3", else chargers must move back and separate by 1" (if possible)." The strike
    // range was 2" in 3.5.1; pile-in, consolidation and the 1" separation were already these numbers.
    //
    // One constant (MELEE_RANGE_INCHES_HORIZONTAL) feeds every gate, so each gate is driven here through its
    // real stage at a 2.5" gap (in under 3.6, out under 3.5) and a 3.5" gap (out under both): a stray literal
    // 2 left in any one of them fails here. The vertical reach stays this engine's 4" convention (#022, plan
    // Q4) - its cases are in VerticalMeleeRangeTests. The 1" standoff is unchanged, so the band where a unit
    // MAY charge but is not FORCED to (#206/#334/#390) is now 1"-3".
    //
    // Bases are 0.5" circles here: base-to-base gap = centre distance - 1.0". Units anchor at x=10, because a
    // model centred at the origin is the engine's never-placed marker (#263) and is in melee range of nothing.
    [TestFixture]
    public class MeleeRangeRuleIntegrationTests
    {
        private const float Radius = 0.5f;
        private const float InsideNewRange = 2.5f;   // within 3" (3.6), beyond 2" (3.5.1)
        private const float BeyondNewRange = 3.5f;   // beyond 3"

        private GameDataStore _store = null!;
        private PlayerID _us;
        private PlayerID _them;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _us = new PlayerID(Guid.NewGuid());
            _them = new PlayerID(Guid.NewGuid());
        }

        // ---- The numbers, against the 3.6 text ---------------------------------------------------------

        [Test]
        public void Constants_MatchTheV360MeleeText()
        {
            Assert.That(GameWideConstants.MELEE_RANGE_INCHES_HORIZONTAL, Is.EqualTo(3f),
                "\"Models within 3\" of enemies must strike\".");
            Assert.That(GameWideConstants.MELEE_RANGE_INCHES_VERTICAL, Is.EqualTo(4f),
                "the vertical reach is our #022 convention, kept at 4\" (plan Q4).");
            Assert.That(PileInUtilities.MAX_PILE_IN_DISTANCE_INCHES, Is.EqualTo(3f),
                "\"defenders must do the same by moving up to 3\"\".");
            Assert.That(ConsolidateStage.WIPEOUT_MAX_DISTANCE_INCHES, Is.EqualTo(3f),
                "\"If either unit is destroyed, the other may move by up to 3\"\".");
            Assert.That(ConsolidateStage.DISENGAGE_MAX_DISTANCE_INCHES, Is.EqualTo(1f),
                "\"else chargers must move back and separate by 1\"\".");
            Assert.That(GameWideConstants.ENEMY_STANDOFF_DISTANCE_INCHES, Is.EqualTo(1f),
                "\"Units may only be within 1\" of other units when charging\" - the forced-charge band.");
        }

        // ---- The single definition ----------------------------------------------------------------------

        [TestCase(0.0f, true, TestName = "StrikeRange_BaseContact_InRange")]
        [TestCase(2.5f, true, TestName = "StrikeRange_TwoAndAHalfInches_InRange")]
        [TestCase(3.0f, true, TestName = "StrikeRange_ExactlyThreeInches_InRange")]
        [TestCase(3.05f, false, TestName = "StrikeRange_JustOverThreeInches_OutOfRange")]
        [TestCase(3.5f, false, TestName = "StrikeRange_ThreeAndAHalfInches_OutOfRange")]
        public void AreModelsInMeleeRange_ThreeInchHorizontalBoundary(float gap, bool expected)
        {
            IModel a = StoredModel(new Position(10f, 0f));
            IModel b = StoredModel(new Position(10f + 2f * Radius + gap, 0f));

            Assert.That(MeleeRangeUtilities.AreModelsInMeleeRange(a, b), Is.EqualTo(expected),
                $"a {gap}\" base-to-base gap against the 3\" strike range");
        }

        // ---- Charge availability (Choose Action) -------------------------------------------------------

        [Test]
        public async Task ChooseAction_EnemyAtTwoAndAHalfInches_OffersCharge_AndStillPass()
        {
            var requester = new RecordingActionRequester(ChooseActionStage.PASS_CHOICE_NAME);
            var ctx = new TestGameContext(_store, new FixedDiceRoller(4), playerRequester: requester);
            DataBinding<UnitData> us = MakeUnit(_us, new Position(10f, 0f));
            MakeUnit(_them, new Position(10f + 2f * Radius + InsideNewRange, 0f));

            UnitActionContext unitCtx = await DriveChooseAction(ctx, us);

            Assert.That(requester.OfferedOptions, Does.Contain(ChooseActionStage.CHARGE_CHOICE_NAME),
                "an enemy 2.5\" away is inside the 3\" strike range, so Charge is offered.");
            Assert.That(requester.OfferedOptions, Does.Contain(ChooseActionStage.PASS_CHOICE_NAME),
                "2.5\" is outside the 1\" standoff: the 1\"-3\" band may charge but is not forced (#206).");
            Assert.That(ChooseActionStage.ShootWouldForfeitObligatedCharge(ctx, unitCtx, canCharge: true),
                Is.False, "and nothing in that band withholds a shot either (#390).");
        }

        [Test]
        public async Task ChooseAction_EnemyAtThreeAndAHalfInches_GreysCharge_NamingTheThreeInchRange()
        {
            var requester = new RecordingActionRequester(ChooseActionStage.PASS_CHOICE_NAME);
            var ctx = new TestGameContext(_store, new FixedDiceRoller(4), playerRequester: requester);
            DataBinding<UnitData> us = MakeUnit(_us, new Position(10f, 0f));
            MakeUnit(_them, new Position(10f + 2f * Radius + BeyondNewRange, 0f));

            await DriveChooseAction(ctx, us);

            Assert.That(requester.OfferedOptions, Does.Not.Contain(ChooseActionStage.CHARGE_CHOICE_NAME));
            var greyed = requester.OfferedInvalidOptions
                .Single(option => option.Option == ChooseActionStage.CHARGE_CHOICE_NAME);
            Assert.That(greyed.Reason, Is.EqualTo("No enemies within the 3\" melee range."),
                "the greyed reason names the range the player has to close to.");
        }

        // ---- Defender eligibility (Choose Melee Defender) ----------------------------------------------

        [Test]
        public async Task ChooseMeleeDefender_EnemyAtTwoAndAHalfInches_IsAValidDefender()
        {
            var requester = new MeleeDefenderRequester();
            var ctx = new WoundTestContext(_store, requester);
            DataBinding<UnitData> us = MakeUnit(_us, new Position(10f, 0f));
            MakeUnit(_them, new Position(10f + 2f * Radius + InsideNewRange, 0f));

            var (chosen, backedOut) = await RunChooseMeleeDefender(ctx, us);

            Assert.That(chosen, Is.True, "a defender 2.5\" away is inside the 3\" strike range.");
            Assert.That(backedOut, Is.False);
        }

        [Test]
        public async Task ChooseMeleeDefender_EnemyAtThreeAndAHalfInches_IsNotOffered()
        {
            var requester = new MeleeDefenderRequester();
            var ctx = new WoundTestContext(_store, requester);
            DataBinding<UnitData> us = MakeUnit(_us, new Position(10f, 0f));
            MakeUnit(_them, new Position(10f + 2f * Radius + BeyondNewRange, 0f));

            var (chosen, backedOut) = await RunChooseMeleeDefender(ctx, us);

            Assert.That(chosen, Is.False, "3.5\" is outside the strike range: no defender to pick.");
            Assert.That(backedOut, Is.True);
        }

        // ---- Who strikes, who strikes back --------------------------------------------------------------

        [Test]
        public async Task Strike_AttackerModelAtTwoAndAHalfInches_Swings_OneAtThreeAndAHalf_DoesNot()
        {
            var ctx = new WoundTestContext(_store, new CapturingWoundRequester());
            DataBinding<UnitData> defender = MakeUnit(_them, new Position(10f, 0f));
            // Two attacking models at different gaps from the one defender: along X at 2.5", along Z at 3.5".
            DataBinding<UnitData> attacker = MakeUnit(_us,
                new Position(10f + 2f * Radius + InsideNewRange, 0f),
                new Position(10f, 2f * Radius + BeyondNewRange));

            CombatActionContext combat = await RunAttackers(ctx, attacker, defender);

            Assert.That(combat.InRangeAttackingModels, Has.Count.EqualTo(1),
                "only the model within 3\" strikes.");
            Assert.That(combat.InRangeAttackingModels[0].GetValue().Position.x,
                Is.EqualTo(10f + 2f * Radius + InsideNewRange).Within(0.001f));
            Assert.That(combat.AvailableWeapons.Values.Sum(), Is.EqualTo(1),
                "the 3.5\" model's weapon stays out of the swing pool.");
        }

        [Test]
        public async Task StrikeBack_DefenderModelAtTwoAndAHalfInches_IsEligible_OneAtThreeAndAHalf_IsNot()
        {
            var ctx = new WoundTestContext(_store, new CapturingWoundRequester());
            DataBinding<UnitData> attacker = MakeUnit(_us, new Position(10f, 0f));
            DataBinding<UnitData> defender = MakeUnit(_them,
                new Position(10f + 2f * Radius + InsideNewRange, 0f),
                new Position(10f, 2f * Radius + BeyondNewRange));

            CombatActionContext combat = await RunAttackers(ctx, attacker, defender);
            var defendersStage = new DetermineInRangeDefendersStage(ctx, new NoOpLayer<ICombatActionContext>());
            defendersStage.ToChooseMeleeWeapons.Bind("done");
            await defendersStage.Enter(combat);

            Assert.That(combat.InRangeDefendingModels, Has.Count.EqualTo(1),
                "only the defending model within 3\" may strike back.");
        }

        // ---- The AI is told -----------------------------------------------------------------------------

        [Test]
        public void Ai_MeleeThreatReach_IsTheChargeDistancePlusThreeInches()
        {
            DataBinding<UnitData> charger = MakeUnit(_us, new Position(10f, 0f));
            DataBinding<UnitData> target = MakeUnit(_them, new Position(40f, 0f));
            var evaluator = new RuleEvaluator(new FixedDiceRoller(4));

            float reach = TacticalAnalysis.MeleeThreatReach(charger.GetValue(), target.GetValue(), evaluator);

            Assert.That(reach, Is.EqualTo(GameWideConstants.CHARGE_DISTANCE_INCHES + 3f).Within(0.001f),
                "a charge only has to END within the strike range, so the threat reaches 12\" + 3\".");
        }

        // ---- Helpers ------------------------------------------------------------------------------------

        private async Task<UnitActionContext> DriveChooseAction(IGameContext ctx, DataBinding<UnitData> unit)
        {
            var unitCtx = new UnitActionContext(ctx, unit);
            unitCtx.Reset(unit);

            var stage = new ChooseActionStage(ctx, new NoOpLayer<IUnitActionContext>());
            stage.ToReconcileEndOfActivation.Bind("test-pass");
            await stage.Enter(unitCtx);
            return unitCtx;
        }

        private static async Task<(bool chosen, bool backedOut)> RunChooseMeleeDefender(IGameContext ctx,
            DataBinding<UnitData> attacker)
        {
            var combat = new CombatActionContext(ctx, attacker, isMelee: true, isCharging: true);
            var stage = new ChooseMeleeDefenderStage(ctx, new NoOpLayer<ICombatActionContext>());

            bool chosen = false, backedOut = false;
            stage.OnDefenderChosen.Bind("done");
            stage.BackToChooseAction.Bind("done");
            stage.OnDefenderChosen.OnWillActivate += _ => chosen = true;
            stage.BackToChooseAction.OnWillActivate += _ => backedOut = true;

            await stage.Enter(combat);
            return (chosen, backedOut);
        }

        private static async Task<CombatActionContext> RunAttackers(IGameContext ctx,
            DataBinding<UnitData> attacker, DataBinding<UnitData> defender)
        {
            var combat = new CombatActionContext(ctx, attacker, isMelee: true, isCharging: true);
            combat.SetDefender(defender);

            var stage = new DetermineInRangeAttackersStage(ctx, new NoOpLayer<ICombatActionContext>());
            stage.ToDetermineDefenders.Bind("done");
            await stage.Enter(combat);
            return combat;
        }

        private ModelData MakeModel(Position at) =>
            new ModelData(Radius, new List<Weapon> { Blade() }, at, _store);

        private IModel StoredModel(Position at)
        {
            ModelData model = MakeModel(at);
            _store.Create(model);
            return model;
        }

        // Each model carries one melee weapon, so the swing pool size equals the in-range model count. Every
        // unit gets its own army so the stages' enemy scans find it.
        private DataBinding<UnitData> MakeUnit(PlayerID owner, params Position[] positions)
        {
            var models = new List<DataBinding<ModelData>>(positions.Length);
            foreach (Position at in positions)
                models.Add(_store.GetDataBinding<ModelData>(_store.Create(MakeModel(at))));

            var unit = new UnitData(owner, $"U{Guid.NewGuid().ToString()[..4]}", quality: 4, defense: 4,
                modelBindings: models);
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            _store.Create(new ArmyData(owner, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }

        private static Weapon Blade() => new Weapon("Blade", rangeInches: 0f, attacks: 1, armorPenetration: 0);
    }
}
