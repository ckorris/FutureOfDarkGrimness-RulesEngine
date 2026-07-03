using System.Linq;
using FDG.Data;
using FDG.Rules.Dispatch;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // Vertical-slice integration test for #032/#042: proves Rending's PER-HIT AP promotion flows from
    // the REAL RollToHitStage to the REAL DetermineSaveRollsNeededStage. On an unmodified 6 to hit,
    // Rending fires Shooting_OnHitRollComplete -> PerHitSaveModifier(6, -4); RollToHitStage peels the
    // natural-6 hits into their own SuccessfulHitInfo carrying that modifier while the rest stay at base
    // AP, and the save stage raises only that group's threshold by 4. FixedDiceRoller(6) makes the lone
    // hit a natural 6 (one AP group); ProbabilisticRoll_* mixes faces to prove non-6 hits are untouched.
    [TestFixture]
    public class RendingRuleIntegrationTests
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
        public async Task RendingAttacker_OnNatural6_RaisesSaveThresholdByFour()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            DataBinding<UnitData> defender = MakeUnit(DefenderPos); // defense 4
            AttachRending(attacker);

            RollToHitResults hits = await RunHitStage(attacker, defender);
            Assert.That(hits.SaveModifier, Is.EqualTo(0),
                "Rending's AP is per-hit now — it no longer carries a whole-attack save modifier.");
            Assert.That(hits.SuccessfulHitList.Count, Is.EqualTo(1), "the lone hit was a natural 6 → one AP group, no base remainder.");
            Assert.That(hits.SuccessfulHitList[0].SaveModifier, Is.EqualTo(-4),
                "the natural-6 hit group carries Rending's -4 per-hit save modifier.");

            DetermineSaveRollNeededResults saves = await RunSaveNeededStage(attacker, defender, hits);
            foreach (PendingSaveRolls pending in saves.PendingSaveRollsList)
            {
                Assert.That(pending.SaveNeeded, Is.EqualTo(8),
                    "base defense 4 + 4 from Rending's per-hit AP = save threshold 8 (pre-clamp).");
            }
        }

        // The heart of the fix: with a mix of hit faces, ONLY the natural 6s get AP. The probabilistic
        // roller makes Roll(6) yield exactly one of each face, so hitting on 4+ lands 3 hits — one 6
        // (AP4 → save 8) and two 4/5s (base AP → save 4). Proves both the per-hit split and that it holds
        // under the probabilistic (histogram) roller, where a face's count is fractional/aggregate.
        [Test]
        public async Task RendingAttacker_ProbabilisticRoll_OnlyNatural6sSaveAtRaisedThreshold()
        {
            _ctx = new TestGameContext(_store, new ProbabilisticDiceRoller());

            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            DataBinding<UnitData> defender = MakeUnit(DefenderPos); // defense 4, quality 4 → hit on 4+
            AttachRending(attacker);

            RollToHitResults hits = await RunHitStage(attacker, defender, attackCount: 6);
            Assert.That(hits.SaveModifier, Is.EqualTo(0), "no whole-attack modifier — Rending's AP is carried per-hit.");

            DetermineSaveRollNeededResults saves = await RunSaveNeededStage(attacker, defender, hits);

            PendingSaveRolls apGroup = saves.PendingSaveRollsList.Single(p => p.SaveNeeded == 8);
            PendingSaveRolls baseGroup = saves.PendingSaveRollsList.Single(p => p.SaveNeeded == 4);
            Assert.That(apGroup.HitCount, Is.EqualTo(1f).Within(0.0001f),
                "one of the six dice was a natural 6 → one AP hit at save threshold 8.");
            Assert.That(baseGroup.HitCount, Is.EqualTo(2f).Within(0.0001f),
                "the 4 and 5 hits (two of them) save at base defense 4 — untouched by Rending.");
        }

        [Test]
        public async Task NoRending_SaveThresholdUnchanged()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            DataBinding<UnitData> defender = MakeUnit(DefenderPos);

            RollToHitResults hits = await RunHitStage(attacker, defender);
            Assert.That(hits.SaveModifier, Is.EqualTo(0), "no Rending → no carried save modifier.");

            DetermineSaveRollNeededResults saves = await RunSaveNeededStage(attacker, defender, hits);
            foreach (PendingSaveRolls pending in saves.PendingSaveRollsList)
            {
                Assert.That(pending.SaveNeeded, Is.EqualTo(4), "just the defender's defense, no AP.");
            }
        }

        // Crack is Rending's AP-on-6 facet at half strength (AP(+2) = -2 to save) and without the
        // Regeneration-ignore: a natural 6 to hit raises the save threshold by 2 (4 → 6).
        [Test]
        public async Task CrackAttacker_OnNatural6_RaisesSaveThresholdByTwo()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            DataBinding<UnitData> defender = MakeUnit(DefenderPos);
            attacker.GetValue().AttachRuleDefinition(new ResolvedRule("Crack", CoreRuleCatalog.Crack));

            RollToHitResults hits = await RunHitStage(attacker, defender);
            Assert.That(hits.SaveModifier, Is.EqualTo(0), "Crack's AP is per-hit — no whole-attack save modifier.");
            Assert.That(hits.SuccessfulHitList[0].SaveModifier, Is.EqualTo(-2),
                "the natural-6 hit group carries Crack's -2 per-hit save modifier (AP+2).");

            DetermineSaveRollNeededResults saves = await RunSaveNeededStage(attacker, defender, hits);
            foreach (PendingSaveRolls pending in saves.PendingSaveRollsList)
            {
                Assert.That(pending.SaveNeeded, Is.EqualTo(6),
                    "base defense 4 + 2 from Crack's per-hit AP = save threshold 6.");
            }
        }

        private async Task<RollToHitResults> RunHitStage(DataBinding<UnitData> attacker, DataBinding<UnitData> defender,
            int attackCount = 1)
        {
            var stage = new RollToHitStage<ICombatMetadata>(_ctx, new NoOpLayer<ICombatMetadata>());
            stage.NextStage.Bind("done");

            var metadata = NewMetadata(attacker, defender);
            metadata.AddResult(new DetermineHitRollResults(4, attackCount)); // a 6 clears 4+
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

        private static void AttachRending(DataBinding<UnitData> unit) =>
            unit.GetValue().AttachRuleDefinition(new ResolvedRule("Rending", CoreRuleCatalog.Rending));

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
