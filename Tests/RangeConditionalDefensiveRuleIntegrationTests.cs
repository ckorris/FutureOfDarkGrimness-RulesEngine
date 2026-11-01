using System.Linq;
using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Stages;
using FDG.Tests.RulesHarness;
using NUnit.Framework;

namespace FDG.Tests
{
    // #432 - the DEFENSIVE half of the OPR 3.6 range-conditional family ("... when shot/charged from over 9"/6"
    // away"), through the REAL stages, the rule on the defender (Subject seat) and gated on most models:
    // Changebound's -1 to hit (DetermineHitRollStage), Guarded's +1 to Defense (RollToHitStage ->
    // DetermineSaveRollsNeededStage), and Knightborn's wound ignore (AssignWoundsStage, histogram mode): spell wounds on
    // 2+, other wounds on 6+ only when shot/charged from over range. Shooting at 10" vs 8", charges from 7" vs 5".
    [TestFixture]
    public class RangeConditionalDefensiveRuleIntegrationTests
    {
        private const float BaseRadius = 0.5f;

        private GameDataStore _store = null!;
        private WoundTestContext _ctx = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _ctx = new WoundTestContext(_store, new CapturingWoundRequester(), new ProbabilisticDiceRoller());
        }

        // --- Changebound (and Machine-Fog / Screened / Empyrean Spirit): -1 to hit ------------------------------

        [TestCase(10f, false, 0f, 5)]
        [TestCase(8f, false, 0f, 4)]
        [TestCase(0.5f, true, 7f, 5)]
        [TestCase(0.5f, true, 5f, 4)]
        public async Task Changebound_EnemiesGetMinusOneToHit_OnlyFromOverRange(float distance, bool charging,
            float origin, int expectedHitNeeded)
        {
            DataBinding<UnitData> defender = MakeUnit("Daemons", z: 10f, models: 3,
                RangeConditional36Definitions.MinusHit());

            Assert.That(await HitNeeded(defender, distance, charging, origin), Is.EqualTo(expectedHitNeeded),
                "Quality 4, shifted only by a shot from over 9\" or a charge from over 6\"");
        }

        [Test]
        public async Task Changebound_OnAMinority_DoesNothing()
        {
            // 1 of 3 models: "if most models in this unit have this rule" fails.
            DataBinding<UnitData> defender = MakeUnit("Daemons", z: 10f, models: 3, rule: null);
            ModelData carrier = defender.GetValue().ModelBindings[0].GetValue();
            carrier.AttachRuleDefinition(new ResolvedRule("Changebound", RangeConditional36Definitions.MinusHit()));

            Assert.That(await HitNeeded(defender, 10f, charging: false, origin: 0f), Is.EqualTo(4));
        }

        // --- Guarded (and Sturdy): +1 to Defense rolls ------------------------------------------------------------

        [TestCase(10f, false, 0f, 3)]
        [TestCase(8f, false, 0f, 4)]
        [TestCase(0.5f, true, 7f, 3)]
        [TestCase(0.5f, true, 5f, 4)]
        public async Task Guarded_DefenderGetsPlusOneDefense_OnlyWhenShotOrChargedFromOverRange(float distance,
            bool charging, float origin, int expectedSaveNeeded)
        {
            DataBinding<UnitData> defender = MakeUnit("Guards", z: 10f, models: 1,
                RangeConditional36Definitions.PlusDefense());

            Assert.That(await SaveNeeded(defender, distance, charging, origin), Is.EqualTo(expectedSaveNeeded),
                "Defense 4, improved to 3+ only when shot from over 9\" / charged from over 6\"");
        }

        [Test]
        public async Task Guarded_OnTheCharger_DoesNotGuardItAgainstTheStrikeBack()
        {
            // The charger was not "charged": the strike-back's context carries no charge distance, so the Guarded
            // charger defends at its plain 4+ however far it charged from.
            DataBinding<UnitData> charger = MakeUnit("Guards", z: 10f, models: 1,
                RangeConditional36Definitions.PlusDefense());

            Assert.That(await SaveNeeded(charger, 0.5f, charging: false, origin: 0f, isMelee: true), Is.EqualTo(4));
        }

        // --- Knightborn (and Warding / Angelic Blessing): the wound ignore ----------------------------------------

        [Test]
        public async Task Knightborn_SpellWound_IsIgnoredOnTwoPlus()
        {
            float wounds = await WoundsFromOneFailedSave(Knights(), distance: 4f, isSpell: true);

            Assert.That(wounds, Is.EqualTo(1f / 6f).Within(0.0001f), "5 in 6 spell wounds ignored, at any range");
        }

        [Test]
        public async Task Knightborn_ShootingWoundFrom10Inches_IsIgnoredOnSixPlus()
        {
            float wounds = await WoundsFromOneFailedSave(Knights(), distance: 10f);

            Assert.That(wounds, Is.EqualTo(5f / 6f).Within(0.0001f), "shot from over 9\": 1 in 6 ignored");
        }

        [Test]
        public async Task Knightborn_ShootingWoundFrom8Inches_IsNotIgnored()
        {
            float wounds = await WoundsFromOneFailedSave(Knights(), distance: 8f);

            Assert.That(wounds, Is.EqualTo(1f).Within(0.0001f), "inside 9\" the 6+ ignore does not apply");
        }

        [TestCase(7f, 5f / 6f)]
        [TestCase(5f, 1f)]
        public async Task Knightborn_ChargeWound_IsIgnoredOnSixPlus_OnlyFromOverSixInches(float origin, float expected)
        {
            float wounds = await WoundsFromOneFailedSave(Knights(), distance: 0.5f, melee: true, origin: origin);

            Assert.That(wounds, Is.EqualTo(expected).Within(0.0001f));
        }

        // --- helpers --------------------------------------------------------------------------------------------

        private DataBinding<UnitData> Knights() => MakeUnit("Knights", z: 10f, models: 1,
            RangeConditional36Definitions.Ward());

        private async Task<int> HitNeeded(DataBinding<UnitData> defender, float distance, bool charging, float origin)
        {
            DataBinding<UnitData> attacker = MakeUnit("Attacker", z: 0f, models: 1, rule: null);
            Place(defender, distance);

            var stage = new DetermineHitRollStage<ICombatMetadata>(_ctx, new NoOpLayer<ICombatMetadata>());
            stage.NextStage.Bind("done");
            var metadata = new CombatMetadata(_ctx, attacker, defender,
                new Weapon("Gun", charging ? 0f : 24f, 1, 0), weaponCount: 1, attackerMoved: false,
                isMelee: charging, isCharging: charging, chargeOriginDistanceInches: origin);
            await stage.Enter(metadata);

            Assert.That(metadata.QueryForResult(out DetermineHitRollResults result), Is.True);
            return result.HitRollNeeded;
        }

        private async Task<int> SaveNeeded(DataBinding<UnitData> defender, float distance, bool charging, float origin,
            bool isMelee = false)
        {
            DataBinding<UnitData> attacker = MakeUnit("Attacker", z: 0f, models: 1, rule: null);
            Place(defender, distance);
            bool melee = isMelee || charging;
            var weapon = new Weapon("Gun", melee ? 0f : 24f, 1, 0);

            var hitStage = new RollToHitStage<ICombatMetadata>(_ctx, new NoOpLayer<ICombatMetadata>());
            hitStage.NextStage.Bind("done");
            var hitMetadata = new CombatMetadata(_ctx, attacker, defender, weapon, weaponCount: 1,
                attackerMoved: false, isMelee: melee, isCharging: charging, chargeOriginDistanceInches: origin);
            hitMetadata.AddResult(new DetermineHitRollResults(4, 1));
            await hitStage.Enter(hitMetadata);
            Assert.That(hitMetadata.QueryForResult(out RollToHitResults hits), Is.True);

            var saveStage = new DetermineSaveRollsNeededStage<ICombatMetadata>(_ctx, new NoOpLayer<ICombatMetadata>());
            saveStage.NextStage.Bind("done");
            var metadata = new CombatMetadata(_ctx, attacker, defender, weapon, weaponCount: 1);
            metadata.AddResult(hits);
            metadata.AddResult(new CoverCheckResults(0));
            await saveStage.Enter(metadata);

            Assert.That(metadata.QueryForResult(out DetermineSaveRollNeededResults result), Is.True);
            return result.PendingSaveRollsList.Max(p => p.SaveNeeded);
        }

        private async Task<float> WoundsFromOneFailedSave(DataBinding<UnitData> defender, float distance,
            bool melee = false, float origin = 0f, bool isSpell = false)
        {
            DataBinding<UnitData> attacker = MakeUnit("Attacker", z: 0f, models: 1, rule: null);
            Place(defender, distance);

            var stage = new AssignWoundsStage<ICombatMetadata>(_ctx, new NoOpLayer<ICombatMetadata>());
            stage.NextStage.Bind("done");
            var metadata = new CombatMetadata(_ctx, attacker, defender, new Weapon("Gun", 24f, 1, 0), weaponCount: 1,
                attackerMoved: false, isMelee: melee, isCharging: melee, isSpell: isSpell,
                chargeOriginDistanceInches: origin);
            var failed = new List<FailedSaveInfo>
            {
                new FailedSaveInfo(TestDice.Faces(2), new PendingSaveRolls(TestDice.Faces(2), 4)),
            };
            metadata.AddResult(new RollToSaveResults(new List<SuccessfulSaveInfo>(), failed));
            await stage.Enter(metadata);

            Assert.That(metadata.QueryForResult(out AssignWoundsResults result), Is.True);
            return result.TotalWoundsToAssign;
        }

        private DataBinding<UnitData> MakeUnit(string name, float z, int models, SpecialRuleDefinition? rule)
        {
            var bindings = new List<DataBinding<ModelData>>();
            for (int i = 0; i < models; i++)
            {
                var model = new ModelData(BaseRadius, new List<Weapon>(), new Position(i * 1.5f, z), _store);
                bindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            var unit = new UnitData(new PlayerID(System.Guid.NewGuid()), name, quality: 4, defense: 4,
                modelBindings: bindings);
            if (rule != null) unit.AttachRuleDefinition(new ResolvedRule(rule.Name, rule));
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }

        private static void Place(DataBinding<UnitData> unit, float edgeDistance)
        {
            List<DataBinding<ModelData>> models = unit.GetValue().ModelBindings;
            for (int i = 0; i < models.Count; i++)
            {
                models[i].GetValue().SetPosition(new Position(i * 1.5f, edgeDistance + 2f * BaseRadius));
            }
        }
    }
}
