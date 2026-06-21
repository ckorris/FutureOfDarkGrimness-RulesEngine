using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // Vertical-slice integration test for #042: proves Thrust flows through the REAL hit and save
    // stages when (and only when) the attacker is charging in melee. Both facets ride existing
    // sinks gated on melee + charging: +1 to hit folds at DetermineHitRollStage (lowering the
    // threshold), and AP(+1) — modelled as a -1 save modifier — folds at RollToHitStage and is
    // carried to the save stage via RollToHitResults.SaveModifier (same machinery as Rending).
    // Melee is only ever entered via Charge, so the charger's swing is charging and a strike-back
    // is not — the gate distinguishes them with no separate flag.
    [TestFixture]
    public class ThrustRuleIntegrationTests
    {
        private static readonly Position AttackerPos = new Position(0, 5);
        private static readonly Position DefenderPos = new Position(3, 5);

        private GameDataStore _store = null!;
        private TestGameContext _ctx = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _ctx = new TestGameContext(_store, new FixedDiceRoller(4));
        }

        // --- +1 to hit (DetermineHitRollStage) ---

        [Test]
        public async Task ThrustCharging_LowersHitThreshold()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            AttachThrust(attacker);

            DetermineHitRollResults result = await RunHitStage(attacker, MakeUnit(DefenderPos),
                isMelee: true, isCharging: true);

            Assert.That(result.HitRollNeeded, Is.EqualTo(3),
                "base 4, lowered by 1 because Thrust gives +1 to hit when charging in melee.");
        }

        [Test]
        public async Task ThrustStrikeBack_NoHitChange()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            AttachThrust(attacker);

            DetermineHitRollResults result = await RunHitStage(attacker, MakeUnit(DefenderPos),
                isMelee: true, isCharging: false);

            Assert.That(result.HitRollNeeded, Is.EqualTo(4),
                "a strike-back is melee but not charging, so Thrust's charging gate fails.");
        }

        [Test]
        public async Task ThrustShooting_NoHitChange()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            AttachThrust(attacker);

            DetermineHitRollResults result = await RunHitStage(attacker, MakeUnit(DefenderPos),
                isMelee: false, isCharging: false);

            Assert.That(result.HitRollNeeded, Is.EqualTo(4),
                "Thrust's melee gate fails in shooting.");
        }

        // --- AP(+1) as a save modifier (RollToHitStage → RollToHitResults.SaveModifier) ---

        [Test]
        public async Task ThrustCharging_AppliesSaveModifier()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            AttachThrust(attacker);

            RollToHitResults result = await RunRollToHit(attacker, MakeUnit(DefenderPos),
                isMelee: true, isCharging: true);

            Assert.That(result.SaveModifier, Is.EqualTo(-1),
                "Thrust's AP(+1) is carried as a -1 save modifier when charging in melee.");
        }

        [Test]
        public async Task ThrustStrikeBack_NoSaveModifier()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            AttachThrust(attacker);

            RollToHitResults result = await RunRollToHit(attacker, MakeUnit(DefenderPos),
                isMelee: true, isCharging: false);

            Assert.That(result.SaveModifier, Is.EqualTo(0),
                "a strike-back is not charging, so Thrust's AP modifier does not apply.");
        }

        private async Task<DetermineHitRollResults> RunHitStage(
            DataBinding<UnitData> attacker, DataBinding<UnitData> defender, bool isMelee, bool isCharging)
        {
            var layer = new NoOpLayer<ICombatMetadata>();
            var stage = new DetermineHitRollStage<ICombatMetadata>(_ctx, layer);
            stage.NextStage.Bind("done");

            var metadata = MakeMetadata(attacker, defender, isMelee, isCharging);
            await stage.Enter(metadata);

            Assert.That(metadata.QueryForResult(out DetermineHitRollResults result), Is.True,
                "Stage must store a DetermineHitRollResults in metadata.");
            return result;
        }

        private async Task<RollToHitResults> RunRollToHit(
            DataBinding<UnitData> attacker, DataBinding<UnitData> defender, bool isMelee, bool isCharging)
        {
            var layer = new NoOpLayer<ICombatMetadata>();
            var stage = new RollToHitStage<ICombatMetadata>(_ctx, layer);
            stage.NextStage.Bind("done");

            var metadata = MakeMetadata(attacker, defender, isMelee, isCharging);
            metadata.AddResult(new DetermineHitRollResults(4, attackCount: 1));

            await stage.Enter(metadata);

            Assert.That(metadata.QueryForResult(out RollToHitResults result), Is.True,
                "Stage must store a RollToHitResults in metadata.");
            return result;
        }

        private CombatMetadata MakeMetadata(DataBinding<UnitData> attacker, DataBinding<UnitData> defender,
            bool isMelee, bool isCharging)
        {
            var weapon = new Weapon("Test", rangeInches: 48f, attacks: 1, armorPenetration: 0);
            return new CombatMetadata(_ctx, attacker, defender, weapon, weaponCount: 1,
                attackerMoved: false, isMelee: isMelee, isCharging: isCharging);
        }

        private static void AttachThrust(DataBinding<UnitData> unit) =>
            unit.GetValue().AttachRuleDefinition(new ResolvedRule("Thrust", CoreRuleCatalog.Thrust));

        private DataBinding<UnitData> MakeUnit(Position position)
        {
            var model = new ModelData(
                baseRadiusInches: 0.75f,
                weapons: new List<Weapon>(),
                initialPosition: position,
                gameDataStore: _store);
            DataBinding<ModelData> modelBinding = _store.GetDataBinding<ModelData>(_store.Create(model));

            var unit = new UnitData(new PlayerID(System.Guid.NewGuid()), "TestUnit",
                quality: 4, defense: 4,
                modelBindings: new List<DataBinding<ModelData>> { modelBinding });
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }
    }
}
