using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // #432 - the MORALE third of the OPR 3.6 range-conditional family: Hive Bond / Hold the Line / Valiant, "If most
    // models in this unit have this rule, they get +1 to Morale Test rolls when shot/charged from over 9"/6" away."
    // The test's trigger now rides the pre-morale context: through the REAL ResolveRangedMoraleStage (the distance the
    // shooter first targeted the unit from, captured by CombatActionContext.RegisterAttackedDefender) and the REAL
    // RollForMoraleStage (the charge distance the losing unit RECEIVED, from the charger's activation-start snapshot).
    //
    // Quality 4 throughout and every die shows 3: the test fails at 4+ and passes only with Hive Bond's +1 (3+).
    [TestFixture]
    public class RangeConditionalMoraleRuleIntegrationTests
    {
        private const float BaseRadius = 0.5f;

        private GameDataStore _store = null!;
        private TestGameContext _ctx = null!;
        private PlayerID _p1;
        private PlayerID _p2;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _ctx = new TestGameContext(_store, new FixedDiceRoller(3));
            _p1 = new PlayerID(System.Guid.NewGuid());
            _p2 = new PlayerID(System.Guid.NewGuid());
        }

        // --- shot from over 9" (the wound-driven test) ----------------------------------------------------------

        [Test]
        public async Task HiveBond_ShotFrom10Inches_GetsPlusOne_AndPasses()
        {
            (CombatActionContext shoot, DataBinding<UnitData> swarm) = ShootAt(distance: 10f, hiveBond: true);

            await RunRangedMorale(shoot);

            Assert.That(IsShaken(swarm), Is.False, "shot from over 9\": 3+ to pass, and the die shows 3");
            Assert.That(shoot.AttackedDefenders[0].ShotFromInches, Is.EqualTo(10f).Within(0.01f),
                "the distance is captured when the shooter first targets the unit");
        }

        [Test]
        public async Task HiveBond_ShotFrom8Inches_GetsNothing_AndFails()
        {
            (CombatActionContext shoot, DataBinding<UnitData> swarm) = ShootAt(distance: 8f, hiveBond: true);

            await RunRangedMorale(shoot);

            Assert.That(IsShaken(swarm), Is.True, "inside 9\" the test is the plain 4+, and a 3 fails");
        }

        [Test]
        public async Task WithoutHiveBond_ShotFrom10Inches_Fails()
        {
            (CombatActionContext shoot, DataBinding<UnitData> swarm) = ShootAt(distance: 10f, hiveBond: false);

            await RunRangedMorale(shoot);

            Assert.That(IsShaken(swarm), Is.True, "control: the same die without the rule");
        }

        // --- charged from over 6" (the melee loser's test) ------------------------------------------------------

        [TestCase(7f, true)]
        [TestCase(5f, false)]
        public async Task HiveBond_ChargedFromOverSixInches_GetsPlusOne(float gap, bool expectedPass)
        {
            (CombatActionContext melee, DataBinding<UnitData> charger, DataBinding<UnitData> swarm) =
                Charge(gap, hiveBondOnDefender: true);

            bool passed = await RunMeleeMorale(melee, loser: swarm);

            Assert.That(passed, Is.EqualTo(expectedPass),
                $"the charged unit lost; the charge came from {gap}\"");
            Assert.That(melee.ChargedFromInchesFor(swarm.GetValue()), Is.EqualTo(gap).Within(0.01f));
            Assert.That(melee.ChargedFromInchesFor(charger.GetValue()), Is.Null, "the charger was not charged");
        }

        [Test]
        public async Task HiveBond_OnALosingCharger_GetsNothing()
        {
            // The charger came from 10" away, but it was not charged - its own morale rule has nothing to read.
            (CombatActionContext melee, DataBinding<UnitData> charger, _) = Charge(10f, hiveBondOnDefender: false,
                hiveBondOnCharger: true);

            Assert.That(await RunMeleeMorale(melee, loser: charger), Is.False);
        }

        // --- helpers --------------------------------------------------------------------------------------------

        private (CombatActionContext, DataBinding<UnitData>) ShootAt(float distance, bool hiveBond)
        {
            DataBinding<UnitData> shooter = MakeUnit(_p1, "Shooters", models: 1, z: 0f, hiveBond: false);
            DataBinding<UnitData> swarm = MakeUnit(_p2, "Swarm", models: 4, z: distance + 2f * BaseRadius, hiveBond);

            var shoot = new CombatActionContext(_ctx, shooter, isMelee: false);
            shoot.RegisterAttackedDefender(swarm);
            Kill(swarm, 2);                              // the volley leaves it at half strength - a test is due
            return (shoot, swarm);
        }

        private (CombatActionContext, DataBinding<UnitData>, DataBinding<UnitData>) Charge(float gap,
            bool hiveBondOnDefender, bool hiveBondOnCharger = false)
        {
            DataBinding<UnitData> charger = MakeUnit(_p1, "Chargers", models: 2, z: 0f, hiveBondOnCharger);
            DataBinding<UnitData> swarm = MakeUnit(_p2, "Swarm", models: 3, z: gap + 2f * BaseRadius,
                hiveBondOnDefender);
            _store.Create(new ArmyData(_p1, new List<DataBinding<UnitData>> { charger }));
            _store.Create(new ArmyData(_p2, new List<DataBinding<UnitData>> { swarm }));
            _store.Create(new TeamData(0, new List<PlayerID> { _p1 }));
            _store.Create(new TeamData(1, new List<PlayerID> { _p2 }));

            var unitContext = new UnitActionContext(_ctx, charger);
            unitContext.Reset(charger);                  // the pre-charge gap is snapshotted here

            var melee = new CombatActionContext(_ctx, charger, isMelee: true, attackerMoved: true, isCharging: true,
                activationContext: unitContext);
            melee.SetDefender(swarm);
            return (melee, charger, swarm);
        }

        private async Task RunRangedMorale(CombatActionContext shoot)
        {
            var stage = new ResolveRangedMoraleStage(_ctx, new NoOpLayer<ICombatActionContext>());
            stage.ToFinished.Bind("ToFinished");
            await stage.Enter(shoot);
        }

        private async Task<bool> RunMeleeMorale(CombatActionContext melee, DataBinding<UnitData> loser)
        {
            melee.AddResult(new DetermineMoraleSaveNeededResult(loser.GetValue().Quality, loser));
            var stage = new RollForMoraleStage(_ctx, new NoOpLayer<ICombatActionContext>());
            bool passed = false;
            stage.OnMoralePassed.Bind(RollForMoraleStage.ROLL_FOR_MORALE_PASSED_TRANSITION);
            stage.OnMoraleFailed.Bind(RollForMoraleStage.ROLL_FOR_MORALE_FAILED_TRANSITION);
            stage.OnMoralePassed.OnWillActivate += _ => passed = true;
            await stage.Enter(melee);
            return passed;
        }

        private DataBinding<UnitData> MakeUnit(PlayerID player, string name, int models, float z, bool hiveBond)
        {
            var bindings = new List<DataBinding<ModelData>>();
            for (int i = 0; i < models; i++)
            {
                var model = new ModelData(BaseRadius, new List<Weapon> { new Weapon("Claws", 0f, 1, 0) },
                    new Position(i * 1.5f, z), _store);
                bindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            var unit = new UnitData(player, name, quality: 4, defense: 4, modelBindings: bindings);
            if (hiveBond)
            {
                SpecialRuleDefinition rule = RangeConditional36Definitions.Morale();
                unit.AttachRuleDefinition(new ResolvedRule(rule.Name, rule));
            }
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }

        private static void Kill(DataBinding<UnitData> unit, int count)
        {
            for (int i = 0; i < count; i++)
            {
                ModelData model = unit.GetValue().ModelBindings[i].GetValue();
                model.DealWounds(model.TotalWounds);
            }
        }

        private static bool IsShaken(DataBinding<UnitData> unit) => unit.GetValue().Tokens.HasToken(TokenType.Shaken);
    }
}
