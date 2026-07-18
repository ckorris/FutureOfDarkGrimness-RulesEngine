using System.Collections.Generic;
using System.Linq;
using FDG.Data;
using FDG.Presentation;
using FDG.Presentation.Beats;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // #232 casualty cascade: ApplyWoundsStage flags every casualty beat of a volley EXCEPT the last
    // as Overlap, so the presenter paces only the short stagger between them and a multi-kill plays
    // as rapid-fire overlapping deaths with the final one running out in full. The last entry that
    // will emit a beat is the last PendingWounds entry with Wounds > 0 - trailing untouched models
    // must not count.
    [TestFixture]
    public class CasualtyCascadeTests
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
        public async Task MultiKill_OverlapsAllButTheLastCasualty()
        {
            CombatMetadata metadata = NewMetadata(defenderModels: 3);

            // Kill models 0 and 1; model 2 is untouched (a trailing Wounds == 0 entry), so the LAST
            // casualty is entry 1 - and the unit survives, keeping destruction hooks out of scope.
            var assign = new AssignWoundsResults(metadata.DefendingUnit, totalWoundsToAssign: 2);
            assign.PendingWounds[0].Wounds = 1;
            assign.PendingWounds[1].Wounds = 1;
            metadata.AddResult(assign);

            await RunApplyWoundsStage(metadata);

            List<ModelDiedBeat> deaths = _presenter.Beats.OfType<ModelDiedBeat>().ToList();
            Assert.That(deaths, Has.Count.EqualTo(2));
            Assert.That(deaths[0].Overlap, Is.True,
                "every casualty before the last overlaps the next (paces only the stagger)");
            Assert.That(deaths[1].Overlap, Is.False,
                "the LAST casualty must play out in full - a trailing untouched model must not steal it");
        }

        [Test]
        public async Task SingleKill_PlaysInFull()
        {
            CombatMetadata metadata = NewMetadata(defenderModels: 2);

            var assign = new AssignWoundsResults(metadata.DefendingUnit, totalWoundsToAssign: 1);
            assign.PendingWounds[0].Wounds = 1;
            metadata.AddResult(assign);

            await RunApplyWoundsStage(metadata);

            List<ModelDiedBeat> deaths = _presenter.Beats.OfType<ModelDiedBeat>().ToList();
            Assert.That(deaths, Has.Count.EqualTo(1));
            Assert.That(deaths[0].Overlap, Is.False, "a lone casualty has nothing to overlap with");
        }

        private async Task RunApplyWoundsStage(CombatMetadata metadata)
        {
            var stage = new ApplyWoundsStage<ICombatMetadata>(_ctx, new NoOpLayer<ICombatMetadata>());
            stage.NextStage.Bind("done");
            await stage.Enter(metadata);
        }

        private CombatMetadata NewMetadata(int defenderModels)
        {
            DataBinding<UnitData> attacker = MakeUnit(1, new Position(0, 5));
            DataBinding<UnitData> defender = MakeUnit(defenderModels, new Position(20, 5));
            var weapon = new Weapon("Test", rangeInches: 48f, attacks: 1, armorPenetration: 0);
            return new CombatMetadata(_ctx, attacker, defender, weapon, weaponCount: 1);
        }

        private DataBinding<UnitData> MakeUnit(int modelCount, Position position)
        {
            var modelBindings = new List<DataBinding<ModelData>>();
            for (int i = 0; i < modelCount; i++)
            {
                var model = new ModelData(0.75f, new List<Weapon>(),
                    new Position(position.x + i * 2f, position.z), _store);
                modelBindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            var unit = new UnitData(new PlayerID(System.Guid.NewGuid()), "TestUnit",
                quality: 4, defense: 4, modelBindings: modelBindings);
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }
    }
}
