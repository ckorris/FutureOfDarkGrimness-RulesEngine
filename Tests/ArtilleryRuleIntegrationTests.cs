using System.Collections.Generic;
using System.Linq;
using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // #425 - Artillery, Core Rules v3.6.0: "When this model uses Hold actions and shoots at enemies over 12" away,
    // it gets +1 to hit rolls. If most models in this unit have this rule, as long as they haven't moved during
    // this round, enemy units shooting at them from over 12" away get -1 to hit rolls."
    //
    // Through the REAL DetermineHitRollStage (the HitRollRuleIntegrationTests harness, where the 3.5 version -
    // +1 / -2 beyond 9", ungated - used to live). Units are Quality 4, so the base roll needed is 4. Each gate is
    // pinned from both sides: Hold vs moved, over vs within 12" (10.5" would have qualified under the old 9"),
    // unmoved vs MovedThisRound, most models vs a minority.
    [TestFixture]
    public class ArtilleryRuleIntegrationTests
    {
        private static readonly Position AttackerPos = new Position(0, 5);
        private static readonly Position FarPos = new Position(20, 5);      // ~18.5" base-to-base: over 12"
        private static readonly Position MiddlePos = new Position(12, 5);   // ~10.5": over 9", not over 12"

        private GameDataStore _store = null!;
        private TestGameContext _ctx = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _ctx = new TestGameContext(_store, new FixedDiceRoller(4), ruleResolver: CoreRuleCatalog.CreateResolver());
        }

        [Test]
        public void Artillery_CarriesTheCore360Wording()
        {
            Assert.That(CoreRuleCatalog.Artillery.Description, Is.EqualTo(
                "When this model uses Hold actions and shoots at enemies over 12\" away, it gets +1 to hit rolls. " +
                "If most models in this unit have this rule, as long as they haven't moved during this round, enemy " +
                "units shooting at them from over 12\" away get -1 to hit rolls."));
        }

        // --- Offensive: +1 only on a Hold, only over 12" ---------------------------------------------------

        [Test]
        public async Task Attacker_OnAHold_Over12_GetsPlusOne()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos, artilleryModels: 1);
            DataBinding<UnitData> defender = MakeUnit(FarPos);

            Assert.That((await RunStage(attacker, defender, attackerMoved: false)).HitRollNeeded, Is.EqualTo(3),
                "base 4, lowered by the +1 for a Hold over 12\".");
        }

        [Test]
        public async Task Attacker_AfterMoving_Over12_GetsNothing()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos, artilleryModels: 1);
            DataBinding<UnitData> defender = MakeUnit(FarPos);

            Assert.That((await RunStage(attacker, defender, attackerMoved: true)).HitRollNeeded, Is.EqualTo(4),
                "'uses Hold actions' - a unit that moved this activation gets no +1.");
        }

        [Test]
        public async Task Attacker_OnAHold_Within12_GetsNothing()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos, artilleryModels: 1);
            DataBinding<UnitData> defender = MakeUnit(MiddlePos);

            Assert.That((await RunStage(attacker, defender, attackerMoved: false)).HitRollNeeded, Is.EqualTo(4),
                "10.5\" is over 9\" (the 3.5 threshold) but not over 12\".");
        }

        // --- Defensive: -1 only unmoved, most models, over 12" ---------------------------------------------

        [Test]
        public async Task Defender_Unmoved_Over12_GivesTheShooterMinusOne()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            DataBinding<UnitData> defender = MakeUnit(FarPos, artilleryModels: 1);

            Assert.That((await RunStage(attacker, defender)).HitRollNeeded, Is.EqualTo(5),
                "base 4, raised by 1 (3.6: -1, not the old -2).");
        }

        [Test]
        public async Task Defender_MovedThisRound_GivesNothing()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            DataBinding<UnitData> defender = MakeUnit(FarPos, artilleryModels: 1);
            defender.GetValue().Tokens.AddToken(TokenDefinitionCatalog.Create(TokenType.MovedThisRound));

            Assert.That((await RunStage(attacker, defender)).HitRollNeeded, Is.EqualTo(4),
                "'as long as they haven't moved during this round' - the MovedThisRound stamp switches it off.");
        }

        [Test]
        public async Task Defender_Within12_GivesNothing()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            DataBinding<UnitData> defender = MakeUnit(MiddlePos, artilleryModels: 1);

            Assert.That((await RunStage(attacker, defender)).HitRollNeeded, Is.EqualTo(4), "not over 12\".");
        }

        [Test]
        public async Task Defender_MostModelsGate()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);

            DataBinding<UnitData> minority = MakeUnit(FarPos, models: 3, artilleryModels: 1);
            Assert.That((await RunStage(attacker, minority)).HitRollNeeded, Is.EqualTo(4),
                "1 of 3 models with the rule is not most of the unit.");

            DataBinding<UnitData> majority = MakeUnit(FarPos, models: 3, artilleryModels: 2);
            Assert.That((await RunStage(attacker, majority)).HitRollNeeded, Is.EqualTo(5),
                "2 of 3 is.");
        }

        [Test]
        public async Task BothSeats_Stack()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos, artilleryModels: 1);
            DataBinding<UnitData> defender = MakeUnit(FarPos, artilleryModels: 1);

            Assert.That((await RunStage(attacker, defender, attackerMoved: false)).HitRollNeeded, Is.EqualTo(4),
                "a Holding Artillery shooter (+1) at an unmoved Artillery target (-1): 4 - 1 + 1.");
        }

        // The stamp the defensive facet reads is the one MovementStage writes: a real move of more than 0" sets
        // it (D4: a 0" Advance does not - MovementBackOutTests pins both halves), and the rule's chip shows it.
        [Test]
        public void TheDefensiveFacet_ReadsTheMovedThisRoundToken()
        {
            DataBinding<UnitData> gun = MakeUnit(FarPos, artilleryModels: 1);
            Assert.That(TokenReadership.IsReadByAnyRule(gun.GetValue(), TokenType.MovedThisRound), Is.True,
                "so the 'Moved' chip is shown on Artillery units, where it matters.");
        }

        // --- helpers ---------------------------------------------------------------------------------------

        private async Task<DetermineHitRollResults> RunStage(DataBinding<UnitData> attacker,
            DataBinding<UnitData> defender, bool attackerMoved = false)
        {
            var stage = new DetermineHitRollStage<ICombatMetadata>(_ctx, new NoOpLayer<ICombatMetadata>());
            stage.NextStage.Bind("done");
            // The shooter's own cannon: the Actor seat dispatches the rules of the models carrying the weapon.
            Weapon weapon = attacker.GetValue().Models[0].Weapons.Single();
            var metadata = new CombatMetadata(_ctx, attacker, defender, weapon, weaponCount: 1, attackerMoved);
            await stage.Enter(metadata);
            Assert.That(metadata.QueryForResult(out DetermineHitRollResults result), Is.True);
            return result;
        }

        // Artillery is attached per MODEL so a test can state how many carry it (MostModelsRuleGateIntegrationTests'
        // convention); a 1-model unit with 1 carrier is the ordinary all-models case.
        private DataBinding<UnitData> MakeUnit(Position position, int models = 1, int artilleryModels = 0)
        {
            var bindings = new List<DataBinding<ModelData>>();
            for (int i = 0; i < models; i++)
            {
                var model = new ModelData(0.75f,
                    new List<Weapon> { new Weapon("Cannon", rangeInches: 48f, attacks: 1, armorPenetration: 0) },
                    new Position(position.x, position.z + i * 1.6f), _store);
                if (i < artilleryModels)
                    model.AttachRuleDefinition(new ResolvedRule("Artillery", CoreRuleCatalog.Artillery));
                bindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }
            var unit = new UnitData(new PlayerID(System.Guid.NewGuid()), "TestUnit", quality: 4, defense: 4,
                modelBindings: bindings);
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }
    }
}
