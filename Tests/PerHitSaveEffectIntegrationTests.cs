using FDG.Data;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // Vertical-slice integration test for #016: a save modifier carried on a specific hit group
    // (SuccessfulHitInfo.SaveModifier) flows into that group's save threshold in the REAL
    // DetermineSaveRollsNeededStage, independent of and stacking with the unit-wide
    // RollToHitResults.SaveModifier. This is the "way for effects on the hits to affect the save
    // rolls" seam that Rending/Crack now ride via PerHitApSplitter (see RendingRuleIntegrationTests);
    // here the mechanism is exercised by tagging the hit group directly, decoupled from any rule.
    // FixedDiceRoller(6) makes the one attack a natural 6 → one hit group. Sign convention mirrors the
    // unit-wide carry: a negative modifier raises the threshold (harder to save).
    [TestFixture]
    public class PerHitSaveEffectIntegrationTests
    {
        private static readonly Position AttackerPos = new Position(0, 5);
        private static readonly Position DefenderPos = new Position(20, 5);

        private GameDataStore _store = null!;
        private TestGameContext _ctx = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _ctx = new TestGameContext(_store, new FixedDiceRoller(6)); // every die shows 6
        }

        [Test]
        public async Task PerHitSaveModifier_RaisesSaveThreshold()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            DataBinding<UnitData> defender = MakeUnit(DefenderPos); // defense 4

            RollToHitResults hits = WithPerHitSaveModifier(await RunHitStage(attacker, defender), -2);

            DetermineSaveRollNeededResults saves = await RunSaveNeededStage(attacker, defender, hits);
            foreach (PendingSaveRolls pending in saves.PendingSaveRollsList)
            {
                Assert.That(pending.SaveNeeded, Is.EqualTo(6),
                    "defense 4, raised by 2 because the hit group carries a -2 save modifier.");
            }
        }

        [Test]
        public async Task NoPerHitModifier_SaveThresholdUnchanged()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            DataBinding<UnitData> defender = MakeUnit(DefenderPos);

            // The hit group's SaveModifier defaults to 0, so the threshold is just the defender's defense.
            RollToHitResults hits = await RunHitStage(attacker, defender);

            DetermineSaveRollNeededResults saves = await RunSaveNeededStage(attacker, defender, hits);
            foreach (PendingSaveRolls pending in saves.PendingSaveRollsList)
            {
                Assert.That(pending.SaveNeeded, Is.EqualTo(4), "no per-hit effect → just the defender's defense.");
            }
        }

        [Test]
        public async Task PerHitModifier_StacksWithUnitWideSaveModifier()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            DataBinding<UnitData> defender = MakeUnit(DefenderPos);

            RollToHitResults hits = WithPerHitSaveModifier(await RunHitStage(attacker, defender), -2);
            hits.SaveModifier = -1; // unit-wide carry (e.g. the blunt Rending path), stacks on the per-hit -2

            DetermineSaveRollNeededResults saves = await RunSaveNeededStage(attacker, defender, hits);
            foreach (PendingSaveRolls pending in saves.PendingSaveRollsList)
            {
                Assert.That(pending.SaveNeeded, Is.EqualTo(7),
                    "defense 4 + 1 (unit-wide save modifier) + 2 (per-hit save modifier) = 7.");
            }
        }

        // Re-tag every hit group with the given per-hit save modifier, preserving the dice, the failed
        // hits, and any unit-wide modifier already on the result.
        private static RollToHitResults WithPerHitSaveModifier(RollToHitResults hits, int saveModifier)
        {
            var tagged = new List<SuccessfulHitInfo>();
            foreach (SuccessfulHitInfo hit in hits.SuccessfulHitList)
            {
                tagged.Add(new SuccessfulHitInfo(hit.Rolls, saveModifier));
            }
            return new RollToHitResults(tagged, hits.FailedHitList) { SaveModifier = hits.SaveModifier };
        }

        private async Task<RollToHitResults> RunHitStage(DataBinding<UnitData> attacker, DataBinding<UnitData> defender)
        {
            var stage = new RollToHitStage<ICombatMetadata>(_ctx, new NoOpLayer<ICombatMetadata>());
            stage.NextStage.Bind("done");

            var metadata = NewMetadata(attacker, defender);
            metadata.AddResult(new DetermineHitRollResults(4, attackCount: 1)); // a 6 clears 4+ → one hit group
            await stage.Enter(metadata);

            Assert.That(metadata.QueryForResult(out RollToHitResults result), Is.True);
            return result;
        }

        private async Task<DetermineSaveRollNeededResults> RunSaveNeededStage(
            DataBinding<UnitData> attacker, DataBinding<UnitData> defender, RollToHitResults hits)
        {
            var stage = new DetermineSaveRollsNeededStage<ICombatMetadata>(_ctx, new NoOpLayer<ICombatMetadata>());
            stage.NextStage.Bind("done");

            var metadata = NewMetadata(attacker, defender);
            metadata.AddResult(hits);
            metadata.AddResult(new CoverCheckResults(0)); // no cover
            await stage.Enter(metadata);

            Assert.That(metadata.QueryForResult(out DetermineSaveRollNeededResults result), Is.True);
            return result;
        }

        private CombatMetadata NewMetadata(DataBinding<UnitData> attacker, DataBinding<UnitData> defender)
        {
            var weapon = new Weapon("Test", rangeInches: 48f, attacks: 1, armorPenetration: 0);
            return new CombatMetadata(_ctx, attacker, defender, weapon, weaponCount: 1);
        }

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
