using System.Collections.Generic;
using System.Linq;
using FDG.Data;
using FDG.Presentation;
using FDG.Presentation.Beats;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // #204: RollToSaveStage groups its save-roll beats by save THRESHOLD, so hits that save with the same
    // stats (base + Blast's multiplier hits + Furious's on-6 extras) show as ONE roll, while a genuinely
    // different threshold (Rending's per-hit AP) shows as its own beat - mirroring the tabletop. Each beat is
    // captioned with the weapon and why there are extra hits. These tests drive the stage with hand-built
    // hit groups carrying HitGroupSource provenance and assert the beat count + caption text.
    [TestFixture]
    public class SaveBeatGroupingTests
    {
        private GameDataStore _store = null!;
        private RecordingPresenter _presenter = null!;
        private TestGameContext _ctx = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _presenter = new RecordingPresenter();
            // Always-2 roller so save rolls resolve deterministically; the tests assert beat count/text, not saves.
            _ctx = new TestGameContext(_store, new FixedDiceRoller(2), presenter: _presenter);
        }

        private static IDiceResults Hits(float count) => new DiceResults(new float[] { 0, 0, 0, count, 0, 0 });

        [Test]
        public async Task BaseAndBlast_SameThreshold_OneBeat_WithBlastArithmetic()
        {
            // 2 base hits + 4 Blast overflow, all saving at 4+ -> one roll of 6.
            await RunSaveStage(
                new PendingSaveRolls(Hits(2), 4, HitGroupSource.Base),
                new PendingSaveRolls(Hits(4), 4, new HitGroupSource(EHitSourceKind.BlastMultiplier, "Blast", 3)));

            List<string> labels = SaveBeatLabels();
            Assert.That(labels.Count, Is.EqualTo(1), "same-threshold hits collapse into one save roll.");
            Assert.That(labels[0], Is.EqualTo("Test: 2 hits x3 (Blast) = 6"));
        }

        [Test]
        public async Task BaseAndFurious_SameThreshold_OneBeat_WithAdditiveArithmetic()
        {
            await RunSaveStage(
                new PendingSaveRolls(Hits(3), 3, HitGroupSource.Base),
                new PendingSaveRolls(Hits(1), 3, new HitGroupSource(EHitSourceKind.ExtraHits, "Furious", 1)));

            List<string> labels = SaveBeatLabels();
            Assert.That(labels.Count, Is.EqualTo(1));
            Assert.That(labels[0], Is.EqualTo("Test: 3 hits +1 (Furious) = 4"));
        }

        [Test]
        public async Task BaseFuriousAndBlast_OneBeat_WithNestedArithmetic()
        {
            // 2 base +1 Furious = 3, then x3 Blast overflow of 6 -> 9 total, all at 4+.
            await RunSaveStage(
                new PendingSaveRolls(Hits(2), 4, HitGroupSource.Base),
                new PendingSaveRolls(Hits(1), 4, new HitGroupSource(EHitSourceKind.ExtraHits, "Furious", 1)),
                new PendingSaveRolls(Hits(6), 4, new HitGroupSource(EHitSourceKind.BlastMultiplier, "Blast", 3)));

            List<string> labels = SaveBeatLabels();
            Assert.That(labels.Count, Is.EqualTo(1));
            Assert.That(labels[0], Is.EqualTo("Test: (2 +1 Furious) x3 (Blast) = 9"));
        }

        [Test]
        public async Task Rending_DifferentThreshold_TwoBeats_BaseThenRending()
        {
            // 3 base hits save at 4+; 2 Rending hits carry AP+1 and save at 3+ -> two separate rolls.
            await RunSaveStage(
                new PendingSaveRolls(Hits(3), 4, HitGroupSource.Base),
                new PendingSaveRolls(Hits(2), 3, new HitGroupSource(EHitSourceKind.PerHitAp, "Rending", 1)));

            List<string> labels = SaveBeatLabels();
            Assert.That(labels.Count, Is.EqualTo(2), "different save thresholds roll separately.");
            Assert.That(labels[0], Is.EqualTo("Test: 3 hits"));
            Assert.That(labels[1], Is.EqualTo("Test: 2 hits, Rending AP+1"));
        }

        [Test]
        public async Task PlainVolley_NoModifiers_OneBeat_JustHitCount()
        {
            await RunSaveStage(new PendingSaveRolls(Hits(4), 4, HitGroupSource.Base));

            List<string> labels = SaveBeatLabels();
            Assert.That(labels.Count, Is.EqualTo(1));
            Assert.That(labels[0], Is.EqualTo("Test: 4 hits"));
        }

        private List<string> SaveBeatLabels() =>
            _presenter.Beats.OfType<DiceRolledBeat>().Where(b => b.Label.StartsWith("Test:"))
                .Select(b => b.Label).ToList();

        private async Task RunSaveStage(params PendingSaveRolls[] groups)
        {
            var stage = new RollToSaveStage<ICombatMetadata>(_ctx, new NoOpLayer<ICombatMetadata>());
            stage.NextStage.Bind("done");

            CombatMetadata metadata = NewMetadata();
            metadata.AddResult(new DetermineSaveRollNeededResults(groups.ToList()));
            await stage.Enter(metadata);
        }

        private CombatMetadata NewMetadata()
        {
            DataBinding<UnitData> attacker = MakeUnit(new Position(0, 5));
            DataBinding<UnitData> defender = MakeUnit(new Position(20, 5));
            var weapon = new Weapon("Test", rangeInches: 48f, attacks: 1, armorPenetration: 0);
            return new CombatMetadata(_ctx, attacker, defender, weapon, weaponCount: 1);
        }

        private DataBinding<UnitData> MakeUnit(Position position)
        {
            var model = new ModelData(0.75f, new List<Weapon>(), position, _store);
            DataBinding<ModelData> modelBinding = _store.GetDataBinding<ModelData>(_store.Create(model));

            var unit = new UnitData(new PlayerID(System.Guid.NewGuid()), "TestUnit",
                quality: 4, defense: 4,
                modelBindings: new List<DataBinding<ModelData>> { modelBinding });
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }
    }
}
