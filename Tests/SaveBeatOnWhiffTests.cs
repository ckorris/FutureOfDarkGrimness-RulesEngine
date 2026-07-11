using System.Collections.Generic;
using System.Linq;
using FDG.Data;
using FDG.Presentation;
using FDG.Presentation.Beats;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // RollToHitStage emits one hit group per volley even on a complete miss, so a whiffed volley reaches
    // RollToSaveStage as a group with zero save dice. The stage must NOT narrate a "Roll to Save" beat
    // for such a group (it would play a hollow "0 saved, 0 wounds" animation). A group that actually
    // landed hits still narrates its save roll. FixedDiceResults can't model an empty group (its
    // TotalRolls is hard-coded to 1), so the groups are built from real DiceResults.
    [TestFixture]
    public class SaveBeatOnWhiffTests
    {
        private GameDataStore _store = null!;
        private RecordingPresenter _presenter = null!;
        private TestGameContext _ctx = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _presenter = new RecordingPresenter();
            _ctx = new TestGameContext(_store, new FixedDiceRoller(6), presenter: _presenter);
        }

        [Test]
        public async Task NoHits_DoesNotPlayRollToSaveBeat()
        {
            var emptyHits = new DiceResults(new float[6]); // TotalRolls 0 — a whiffed volley
            await RunSaveStage(new PendingSaveRolls(emptyHits, saveNeeded: 4));

            Assert.That(SaveBeatCount(), Is.EqualTo(0),
                "a hit group with no hits must not play a roll-to-save animation.");
        }

        [Test]
        public async Task HasHits_PlaysRollToSaveBeat()
        {
            var oneHit = new DiceResults(new float[] { 0, 0, 0, 1, 0, 0 }); // one hit on face 4
            await RunSaveStage(new PendingSaveRolls(oneHit, saveNeeded: 4));

            Assert.That(SaveBeatCount(), Is.EqualTo(1),
                "a hit group with hits still narrates its save roll.");
        }

        private async Task RunSaveStage(PendingSaveRolls group)
        {
            var stage = new RollToSaveStage<ICombatMetadata>(_ctx, new NoOpLayer<ICombatMetadata>());
            stage.NextStage.Bind("done");

            CombatMetadata metadata = NewMetadata();
            metadata.AddResult(new DetermineSaveRollNeededResults(new List<PendingSaveRolls> { group }));
            await stage.Enter(metadata);
        }

        // #204: save-roll beats are now captioned "{weapon}: {breakdown}" (grouped by threshold) rather
        // than a fixed "Roll to Save" label; the test weapon is named "Test".
        private int SaveBeatCount() =>
            _presenter.Beats.OfType<DiceRolledBeat>().Count(b => b.Label.StartsWith("Test:"));

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

    // Records every beat presented so tests can assert which animations played.
    internal sealed class RecordingPresenter : IPresenter
    {
        public List<PresentationBeat> Beats { get; } = new();

        public System.Threading.Tasks.Task Present(PresentationBeat beat,
            System.Threading.CancellationToken ct = default)
        {
            Beats.Add(beat);
            return System.Threading.Tasks.Task.CompletedTask;
        }
    }
}
