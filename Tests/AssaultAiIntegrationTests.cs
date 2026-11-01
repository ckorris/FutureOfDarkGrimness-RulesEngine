using System;
using System.Collections.Generic;
using FDG.Ai;
using FDG.Ai.Tactician;
using FDG.Data;
using FDG.Players;
using FDG.Rules.Dispatch;
using NUnit.Framework;

namespace FDG.Tests
{
    // #424 Assault for the AI (the half #442 deferred here): every AI read of "what can this unit do" goes through
    // the unit's two weapon pools or the classification seam, so an Assault pistol is priced as a shot AND as a
    // swing without any AI change - these pins prove it, and name the archetype tie-break (D6) so #442's pricing
    // work has a fixed point to start from.
    [TestFixture]
    public class AssaultAiIntegrationTests
    {
        private GameDataStore _store = null!;
        private RuleEvaluator _evaluator = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _evaluator = new RuleEvaluator(new ProbabilisticDiceRoller());
        }

        // --- AiUnitClassifier (the solo bot's move policy keys on this) -----------------------------------------

        // An all-pistol unit is no longer a pure shooter the bot kites with and never charges: both pools are
        // populated. The archetype itself is Shooting - the melee score and the ranged score are the SAME weapons,
        // so the "melee outscores ranged" test for Hybrid is a tie, and a tie keeps the shooter's advance-and-fire
        // policy (D6; a 12" pistol unit that advances still gets its shot, and the charge gate is open to it).
        [Test]
        public void AnAssaultOnlyUnit_HasBothPools_AndClassifiesAsShooting()
        {
            DataBinding<UnitData> pistoleers = MakeUnit(3, AssaultDefinitions.HeavyPistol());

            Assert.That(pistoleers.GetValue().GetRangedWeapons(), Has.Count.EqualTo(3));
            Assert.That(pistoleers.GetValue().GetMeleeWeapons(), Has.Count.EqualTo(3));
            Assert.That(AiUnitClassifier.Classify(pistoleers.GetValue()), Is.EqualTo(AiUnitArchetype.Shooting));
            Assert.That(AiUnitClassifier.Classify(MakeUnit(3, new Weapon("Pistol", 12f, 1, 1)).GetValue()),
                Is.EqualTo(AiUnitArchetype.Shooting), "control: the plain pistol unit reads the same");
        }

        [Test]
        public void AnAssaultPistolPlusASword_ClassifiesAsHybrid_TheSwordAloneAsMelee()
        {
            DataBinding<UnitData> brawlers = MakeUnit(3, AssaultDefinitions.HeavyPistol(), new Weapon("Sword", 0f, 2, 0));
            DataBinding<UnitData> swordsmen = MakeUnit(3, new Weapon("Sword", 0f, 2, 0));

            Assert.That(AiUnitClassifier.Classify(brawlers.GetValue()), Is.EqualTo(AiUnitArchetype.Hybrid),
                "pistol swings + sword swings outscore pistol shots alone");
            Assert.That(AiUnitClassifier.Classify(swordsmen.GetValue()), Is.EqualTo(AiUnitArchetype.Melee));
        }

        // --- CombatMath (the Tactician's expected-wounds mirror) ----------------------------------------------

        // 3 Heavy Pistols (A1, AP1) at Quality 4 into Defense 4: 3 x 0.5 hits x 4/6 failed 5+ saves = 1.0 wounds,
        // priced identically as a volley at 10" and as a melee swing - the same weapon, the same stat line.
        [Test]
        public void EstimateShooting_AndEstimateMelee_BothPriceTheAssaultPistol()
        {
            DataBinding<UnitData> pistoleers = MakeUnit(3, AssaultDefinitions.HeavyPistol(), atX: 0f);
            DataBinding<UnitData> grunts = MakeUnit(5, null, atX: 10f);

            AttackEstimate shot = CombatMath.EstimateShooting(_evaluator, pistoleers, grunts,
                new AttackContext(DistanceInches: 10f));
            MeleeEstimate melee = CombatMath.EstimateMelee(_evaluator, pistoleers, grunts);

            Assert.That(shot.ExpectedWounds, Is.EqualTo(1f).Within(0.0001f), "the shot at 10\"");
            Assert.That(melee.AttackerAttack.ExpectedWounds, Is.EqualTo(1f).Within(0.0001f), "the swing");
        }

        [Test]
        public void EstimateMelee_OfAPlainPistolUnit_StillPricesNoSwing()
        {
            DataBinding<UnitData> riflemen = MakeUnit(3, new Weapon("Pistol", 12f, 1, 1), atX: 0f);
            DataBinding<UnitData> grunts = MakeUnit(5, null, atX: 10f);

            MeleeEstimate melee = CombatMath.EstimateMelee(_evaluator, riflemen, grunts);

            Assert.That(melee.AttackerAttack.ExpectedWounds, Is.EqualTo(0f).Within(0.0001f),
                "control: without the grant a pistol has nothing to swing");
        }

        // --- TacticalAnalysis (unit value, threat) -----------------------------------------------------------

        // The UnitValue output loop prices a unit's ranged and melee halves separately; both must see the pistol
        // (RangedOutputWounds / MeleeOutputWounds used to test the range directly - the one AI site that did).
        [Test]
        public void OutputWounds_CountThePistol_OnBothSides()
        {
            DataBinding<UnitData> pistoleers = MakeUnit(3, AssaultDefinitions.HeavyPistol());
            DataBinding<UnitData> riflemen = MakeUnit(3, new Weapon("Pistol", 12f, 1, 1));

            float ranged = TacticalAnalysis.RangedOutputWounds(pistoleers.GetValue());
            float melee = TacticalAnalysis.MeleeOutputWounds(pistoleers.GetValue());

            Assert.That(ranged, Is.EqualTo(1f).Within(0.0001f), "3 x 0.5 x 4/6 against the Q4/D4 reference");
            Assert.That(melee, Is.EqualTo(ranged).Within(0.0001f), "the same pistols, priced as swings too");
            Assert.That(TacticalAnalysis.MeleeOutputWounds(riflemen.GetValue()), Is.EqualTo(0f).Within(0.0001f),
                "control: a plain pistol prices no melee output");
        }

        // The planner's EnemyFacts.HasMelee and MacroActionGenerator's hasMelee both read these two predicates;
        // an Assault-only enemy is now something to keep out of charge reach of.
        [Test]
        public void TheMeleeCapabilityPredicates_ReadAnAssaultOnlyUnitAsMeleeCapable()
        {
            DataBinding<UnitData> pistoleers = MakeUnit(3, AssaultDefinitions.HeavyPistol());

            Assert.That(pistoleers.GetValue().GetMeleeWeapons().Count > 0, Is.True, "EnemyFacts.HasMelee's read");
            Assert.That(ChargeContactRules.CanFightInMelee(pistoleers.GetValue()), Is.True, "MacroActionGenerator's read");
            Assert.That(TacticalAnalysis.MayCharge(pistoleers.GetValue(), _evaluator), Is.True);
        }

        // --- helpers ----------------------------------------------------------------------------------------

        private DataBinding<UnitData> MakeUnit(int modelCount, Weapon? weapon, Weapon? second = null,
            float atX = 20f)
        {
            var modelBindings = new List<DataBinding<ModelData>>(modelCount);
            for (int i = 0; i < modelCount; i++)
            {
                var weapons = new List<Weapon>();
                if (weapon != null) weapons.Add(weapon);
                if (second != null) weapons.Add(second);
                var model = new ModelData(baseRadiusInches: 0.5f, weapons: weapons,
                    initialPosition: new Position(atX, i * 1.2f), gameDataStore: _store);
                modelBindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }
            var unit = new UnitData(new PlayerID(Guid.NewGuid()), "TestUnit", quality: 4, defense: 4,
                modelBindings: modelBindings);
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }
    }
}
