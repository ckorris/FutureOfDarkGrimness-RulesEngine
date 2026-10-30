using System.Collections.Generic;
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
    // #435 - the AoF supplement sweep's re-authored definitions that sit on existing primitives, through the REAL stages
    // where the effect lands (the GdfSweep36RuleIntegrationTests rig): Great Sergeant's AP(+2) at RollToHitStage ->
    // DetermineSaveRollsNeededStage. Units are Quality 4 / Defense 4 and the weapon AP(0) unless a case says otherwise,
    // so a save threshold of 4 is "no modifier". (Banner / Musician / Great Banner / Great Musician drive the morale and
    // movement sites in AofCommandGroup36RuleIntegrationTests.)
    [TestFixture]
    public class AofSweep36RuleIntegrationTests
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

        // --- Great Sergeant -----------------------------------------------------------------------------------------

        [Test]
        public async Task GreatSergeant_TheCarryingWeapon_GetsApPlusTwo_InMeleeAndShooting()
        {
            Weapon champion = WeaponWith(AofSweep36Definitions.GreatSergeant());

            Assert.That(await SaveNeeded(champion, MakeDefender(), 10f), Is.EqualTo(6), "shooting: Defense 4+ at AP(2)");
            Assert.That(await SaveNeeded(champion, MakeDefender(), 0.5f, isMelee: true), Is.EqualTo(6), "melee");
            Assert.That(await SaveNeeded(PlainWeapon(), MakeDefender(), 10f), Is.EqualTo(4),
                "control: a weapon without the champion's copy");
        }

        [Test]
        public async Task GreatSergeant_NoLongerAddsHits_OnFivesAndSixes()
        {
            // 3.5 Great Sergeant dealt an extra hit on each unmodified 5-6; 3.6 is AP only.
            _ctx = new WoundTestContext(_store, new CapturingWoundRequester(), new FixedDiceRoller(6));

            RollToHitResults hits = await Hits(WeaponWith(AofSweep36Definitions.GreatSergeant()), MakeDefender(), 10f);

            Assert.That(hits.SuccessfulHitList.Sum(g => g.HitCount), Is.EqualTo(1f));
        }

        // --- helpers ------------------------------------------------------------------------------------------------

        private async Task<int> SaveNeeded(Weapon weapon, DataBinding<UnitData> defender, float distance,
            DataBinding<UnitData>? attacker = null, bool isMelee = false, bool charging = false, bool moved = false)
        {
            attacker ??= MakeUnit("Attacker", 0f, 1, rule: null);
            RollToHitResults hits = await Hits(weapon, defender, distance, attacker, isMelee, charging, moved);

            var stage = new DetermineSaveRollsNeededStage<ICombatMetadata>(_ctx, new NoOpLayer<ICombatMetadata>());
            stage.NextStage.Bind("done");
            var metadata = new CombatMetadata(_ctx, attacker, defender, weapon, weaponCount: 1);
            metadata.AddResult(hits);
            metadata.AddResult(new CoverCheckResults(0));
            await stage.Enter(metadata);

            Assert.That(metadata.QueryForResult(out DetermineSaveRollNeededResults result), Is.True);
            return result.PendingSaveRollsList.Max(p => p.SaveNeeded);
        }

        private async Task<RollToHitResults> Hits(Weapon weapon, DataBinding<UnitData> defender, float distance,
            DataBinding<UnitData>? attacker = null, bool isMelee = false, bool charging = false, bool moved = false)
        {
            attacker ??= MakeUnit("Attacker", 0f, 1, rule: null);
            Place(defender, distance);

            var stage = new RollToHitStage<ICombatMetadata>(_ctx, new NoOpLayer<ICombatMetadata>());
            stage.NextStage.Bind("done");
            var metadata = new CombatMetadata(_ctx, attacker, defender, weapon, weaponCount: 1,
                attackerMoved: moved, isMelee: isMelee, isCharging: charging);
            metadata.AddResult(new DetermineHitRollResults(4, 1));
            await stage.Enter(metadata);

            Assert.That(metadata.QueryForResult(out RollToHitResults result), Is.True);
            return result;
        }

        private static Weapon PlainWeapon() => new Weapon("Plain gun", rangeInches: 24f, attacks: 1, armorPenetration: 0);

        private static Weapon WeaponWith(SpecialRuleDefinition rule)
        {
            var weapon = new Weapon("Test weapon", rangeInches: 24f, attacks: 1, armorPenetration: 0);
            weapon.AttachRuleDefinition(new ResolvedRule(rule.Name, rule));
            return weapon;
        }

        private DataBinding<UnitData> MakeDefender(int tough = 1) => MakeUnit("Target", 10f, 1, rule: null, tough: tough);

        private DataBinding<UnitData> MakeUnit(string name, float z, int models, SpecialRuleDefinition? rule, int tough = 1)
        {
            var bindings = new List<DataBinding<ModelData>>();
            for (int i = 0; i < models; i++)
            {
                var model = new ModelData(BaseRadius, new List<Weapon>(), new Position(i * 1.5f, z), _store);
                if (tough > 1) model.SetMaxWounds(tough);
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
