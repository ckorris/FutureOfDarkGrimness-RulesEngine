using System.Collections.Generic;
using System.Linq;
using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.SaveLoad;
using NUnit.Framework;

namespace FDG.Tests
{
    // #006 slice B: a joined hero is assigned wounds last. AssignWoundsResults orders the hero last (so
    // AutoFill spares it until the rank and file are full) and TryAddWounds rejects the hero while any
    // non-hero model still has room (so the player-choice path can't pick it early).
    [TestFixture]
    public class HeroWoundAssignmentTests
    {
        private GameDataStore _store = null!;

        [SetUp]
        public void SetUp() => _store = GameDataStore.GameDataStoreBuilder.GetDefault();

        [Test]
        public void TryAddWounds_RejectsHero_WhileRankAndFileHaveRoom()
        {
            var (host, hero, grunts) = MakeMergedUnit(gruntCount: 2);
            var results = new AssignWoundsResults(host, totalWoundsToAssign: 1);

            Assert.That(results.TryAddWounds(hero), Is.False, "the hero can't take a wound while grunts have room.");
            Assert.That(results.TryAddWounds(grunts[0]), Is.True, "a rank-and-file model takes it instead.");
        }

        [Test]
        public void AutoFill_SparesHero_WhenWoundsFewerThanModels()
        {
            var (host, hero, _) = MakeMergedUnit(gruntCount: 2); // 2 grunts + hero = 3 models
            var results = new AssignWoundsResults(host, totalWoundsToAssign: 2);

            results.AutoFill();

            Assert.That(WoundsOn(results, hero), Is.EqualTo(0f), "two wounds fall on the two grunts; the hero is spared.");
        }

        [Test]
        public void AutoFill_WoundsHeroLast_WhenEveryModelMustFall()
        {
            var (host, hero, grunts) = MakeMergedUnit(gruntCount: 2);
            var results = new AssignWoundsResults(host, totalWoundsToAssign: 3); // exactly enough for all 3

            results.AutoFill();

            Assert.That(grunts.All(g => WoundsOn(results, g) == 1f), Is.True, "both grunts are filled first.");
            Assert.That(WoundsOn(results, hero), Is.EqualTo(1f), "the hero takes the last wound once the grunts are full.");
        }

        [Test]
        public void TryAddWounds_AllowsHero_OnceGruntsAreFull()
        {
            // Host must be multi-model to be a valid join target, so use two grunts and fill both first.
            var (host, hero, grunts) = MakeMergedUnit(gruntCount: 2);
            var results = new AssignWoundsResults(host, totalWoundsToAssign: 3);

            Assert.That(results.TryAddWounds(grunts[0]), Is.True);
            Assert.That(results.TryAddWounds(grunts[1]), Is.True);
            Assert.That(results.TryAddWounds(hero), Is.True, "with every grunt full, the hero is now a legal target.");
        }

        // CanAssignWoundTo is the non-mutating predicate the wound dialog uses to gray a model; it must
        // agree with TryAddWounds so the hero is shown un-assignable until the rest of the unit is dead.

        [Test]
        public void CanAssignWoundTo_RejectsHero_WhileRankAndFileHaveRoom()
        {
            var (host, hero, grunts) = MakeMergedUnit(gruntCount: 2);
            var results = new AssignWoundsResults(host, totalWoundsToAssign: 1);

            Assert.That(results.CanAssignWoundTo(EntryFor(results, hero)), Is.False,
                "the hero is not a valid target while grunts have room — the dialog grays it.");
            Assert.That(results.CanAssignWoundTo(EntryFor(results, grunts[0])), Is.True,
                "a rank-and-file model with room is a valid target.");
        }

        [Test]
        public void CanAssignWoundTo_AllowsHero_OnceGruntsAreFull()
        {
            var (host, hero, grunts) = MakeMergedUnit(gruntCount: 2);
            var results = new AssignWoundsResults(host, totalWoundsToAssign: 3);

            results.TryAddWounds(grunts[0]);
            results.TryAddWounds(grunts[1]);

            Assert.That(results.CanAssignWoundTo(EntryFor(results, hero)), Is.True,
                "with every grunt full, the hero becomes a valid target.");
        }

        [Test]
        public void CanAssignWoundTo_RejectsModelAlreadyFull()
        {
            var (host, _, grunts) = MakeMergedUnit(gruntCount: 2);
            var results = new AssignWoundsResults(host, totalWoundsToAssign: 3);

            results.TryAddWounds(grunts[0]); // 1-wound model, now full in this assignment

            Assert.That(results.CanAssignWoundTo(EntryFor(results, grunts[0])), Is.False,
                "a model already full in this assignment is no longer a valid target.");
        }

        // --- helpers ---

        private static float WoundsOn(AssignWoundsResults results, DataBinding<ModelData> model) =>
            results.PendingWounds.First(entry => entry.Model.GetValue().ID == model.GetValue().ID).Wounds;

        private static PendingWounds EntryFor(AssignWoundsResults results, DataBinding<ModelData> model) =>
            results.PendingWounds.First(entry => entry.Model.GetValue().ID == model.GetValue().ID);

        private (DataBinding<UnitData> Host, DataBinding<ModelData> Hero, List<DataBinding<ModelData>> Grunts)
            MakeMergedUnit(int gruntCount)
        {
            UnitData host = MakeUnit(gruntCount, quality: 4, defense: 4);
            UnitData hero = MakeUnit(1, quality: 2, defense: 2);
            hero.AttachRuleDefinition(new ResolvedRule("Hero", CoreRuleCatalog.Hero));

            HeroJoinResolver.Apply(new List<(UnitFileEntry, UnitData)>
            {
                (new UnitFileEntry { Id = "host" }, host),
                (new UnitFileEntry { JoinsUnitId = "host" }, hero),
            });

            DataBinding<UnitData> hostBinding = _store.GetDataBinding<UnitData>(_store.Create(host));
            ModelID heroId = host.HeroAttachment!.HeroModelId;
            DataBinding<ModelData> heroBinding = host.ModelBindings.First(b => b.GetValue().ID == heroId);
            List<DataBinding<ModelData>> grunts = host.ModelBindings.Where(b => b.GetValue().ID != heroId).ToList();
            return (hostBinding, heroBinding, grunts);
        }

        private UnitData MakeUnit(int modelCount, int quality, int defense)
        {
            var modelBindings = new List<DataBinding<ModelData>>(modelCount);
            for (int i = 0; i < modelCount; i++)
            {
                var model = new ModelData(0.75f, new List<Weapon>(), new Position(0, 0), _store);
                modelBindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            return new UnitData(new PlayerID(System.Guid.NewGuid()), "TestUnit",
                quality: quality, defense: defense, modelBindings: modelBindings);
        }
    }
}
