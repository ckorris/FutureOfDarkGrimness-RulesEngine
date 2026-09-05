using System;
using FDG.Ai.Tactician.Resolvers;
using FDG.Data;
using FDG.StageResolution.Requests;
using NUnit.Framework;

namespace FDG.Tests
{
    // #191 A4-4 — output-preserving wound assignment: casualties come from the cheapest weapons
    // first, and a partial pool is soaked by a multi-wound model instead of killing a gunner.
    // The engine's ordering rules (pre-assign to wounded, hero last, finish-before-starting) are
    // enforced by AssignWoundsResults itself and pinned by its own tests; these pin the CHOICE.
    [TestFixture]
    public class TacticianWoundAssignmentTests
    {
        private GameDataStore _store = null!;
        private PlayerID _player;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _player = new PlayerID(Guid.NewGuid());
        }

        [Test]
        public async Task Casualties_ComeFromTheCheapestWeaponsFirst()
        {
            // 3 riflemen (A1) + 2 heavy gunners (A3 AP1). Two kills' worth of wounds: the dead
            // must both be riflemen. Solo AutoFill would kill in list order - gunners first here.
            var models = new List<DataBinding<ModelData>>
            {
                MakeModel(Heavy()), MakeModel(Heavy()),
                MakeModel(Rifle()), MakeModel(Rifle()), MakeModel(Rifle()),
            };
            DataBinding<UnitData> unit = MakeUnit(models);

            var resolver = new TacticianAssignWoundsResolver();
            AssignWoundsResults results = await resolver.Resolve(
                new AssignWoundsRequest(_player, "Assign Wounds", unit, totalWoundsToAssign: 2f));

            Assert.That(results.IsFinishedAssigning, Is.True);
            foreach (PendingWounds entry in results.PendingWounds)
            {
                bool isHeavy = entry.Model.GetValue().Weapons[0].Attacks == 3;
                if (isHeavy)
                    Assert.That(entry.Wounds, Is.EqualTo(0f).Within(0.001f),
                        "heavy gunners must be spared while riflemen can absorb the pool");
            }
        }

        [Test]
        public async Task PartialPool_IsSoakedByTheToughModel_NotByKillingAGunner()
        {
            // 2 heavy gunners (1W) + one 3-wound brute with a cheap fist. One wound incoming:
            // chip the brute (it survives, nothing dies) rather than delete a gunner.
            var brute = MakeModel(Fist(), wounds: 3);
            var models = new List<DataBinding<ModelData>>
            {
                MakeModel(Heavy()), MakeModel(Heavy()), brute,
            };
            DataBinding<UnitData> unit = MakeUnit(models);

            var resolver = new TacticianAssignWoundsResolver();
            AssignWoundsResults results = await resolver.Resolve(
                new AssignWoundsRequest(_player, "Assign Wounds", unit, totalWoundsToAssign: 1f));

            Assert.That(results.IsFinishedAssigning, Is.True);
            PendingWounds bruteEntry = results.PendingWounds.First(e => e.Model == brute);
            Assert.That(bruteEntry.Wounds, Is.EqualTo(1f).Within(0.001f),
                "the multi-wound model soaks the partial volley; every gunner stays alive");
        }

        // --- step 10 P0 (2026-09-05): objective-aware allocation ------------------------------------
        // Chris's GUI game: a unit partially on a marker assigned its wounds to the models ON the
        // marker and stopped holding it. These pin the marker stake; the output rule above is
        // unchanged when the resolver has no table state or the unit stands on no marker.

        [Test]
        public async Task PartiallyOnAMarker_TheOffMarkerModelsDieFirst()
        {
            // 5 identical riflemen: 2 within 3" of the marker, 3 well off it. Three wounds: all three
            // casualties must be the off-marker models. The plain output rule kills in list order
            // here (equal costs, strict-less picks the first), and the on-marker pair is listed first.
            var tableState = new TableState(_store);
            _store.Create(new ObjectiveData(new Position(30f, 30f), _store));
            var onMarkerA = MakeModel(Rifle(), x: 30f, z: 31f);
            var onMarkerB = MakeModel(Rifle(), x: 31f, z: 30f);
            var models = new List<DataBinding<ModelData>>
            {
                onMarkerA, onMarkerB,
                MakeModel(Rifle(), x: 40f, z: 30f), MakeModel(Rifle(), x: 41f, z: 30f), MakeModel(Rifle(), x: 42f, z: 30f),
            };
            DataBinding<UnitData> unit = MakeUnit(models);

            var resolver = new TacticianAssignWoundsResolver(tableState);
            AssignWoundsResults results = await resolver.Resolve(
                new AssignWoundsRequest(_player, "Assign Wounds", unit, totalWoundsToAssign: 3f));

            Assert.That(results.IsFinishedAssigning, Is.True);
            Assert.That(Pending(results, onMarkerA), Is.EqualTo(0f).Within(0.001f), "a model on the marker must be spared");
            Assert.That(Pending(results, onMarkerB), Is.EqualTo(0f).Within(0.001f), "a model on the marker must be spared");
        }

        [Test]
        public async Task TheLastModelOnTheMarker_OutlivesAHeavyGunnerOffIt()
        {
            // One rifleman holds the marker alone; two heavy gunners stand off it. One wound: the
            // output rule alone kills the rifleman (1.0 vs 3.45); the marker stake makes the last
            // body on the marker worth ~10 in round 1, so a gunner dies instead.
            var tableState = new TableState(_store);
            _store.Create(new ObjectiveData(new Position(30f, 30f), _store));
            var holder = MakeModel(Rifle(), x: 30f, z: 31f);
            var models = new List<DataBinding<ModelData>>
            {
                MakeModel(Heavy(), x: 40f, z: 30f), MakeModel(Heavy(), x: 41f, z: 30f), holder,
            };
            DataBinding<UnitData> unit = MakeUnit(models);

            var resolver = new TacticianAssignWoundsResolver(tableState);
            AssignWoundsResults results = await resolver.Resolve(
                new AssignWoundsRequest(_player, "Assign Wounds", unit, totalWoundsToAssign: 1f));

            Assert.That(Pending(results, holder), Is.EqualTo(0f).Within(0.001f),
                "the last model on the marker dies after everything else, gun or no gun");
        }

        [Test]
        public async Task WithAnAllyAlreadyOnTheMarker_OutputDecidesAgain()
        {
            // Same unit, but another allied unit also stands on the marker: this unit's presence is
            // redundant (a fifth of the stake, ~2 + 1 < the gunner's 3.45), so the cheap rifleman dies.
            var tableState = new TableState(_store);
            _store.Create(new ObjectiveData(new Position(30f, 30f), _store));
            MakeUnit(new List<DataBinding<ModelData>> { MakeModel(Rifle(), x: 29f, z: 30f), MakeModel(Rifle(), x: 29f, z: 31f) });
            var holder = MakeModel(Rifle(), x: 30f, z: 31f);
            var models = new List<DataBinding<ModelData>>
            {
                MakeModel(Heavy(), x: 40f, z: 30f), MakeModel(Heavy(), x: 41f, z: 30f), holder,
            };
            DataBinding<UnitData> unit = MakeUnit(models);

            var resolver = new TacticianAssignWoundsResolver(tableState);
            AssignWoundsResults results = await resolver.Resolve(
                new AssignWoundsRequest(_player, "Assign Wounds", unit, totalWoundsToAssign: 1f));

            Assert.That(Pending(results, holder), Is.EqualTo(1f).Within(0.001f),
                "with an ally holding the marker anyway, the heavy gunners are the ones to keep");
        }

        private static float Pending(AssignWoundsResults results, DataBinding<ModelData> model) =>
            results.PendingWounds.First(e => e.Model == model).Wounds;

        [Test]
        public async Task LethalPool_StillAssignsEverything()
        {
            // Overkill volley: every model dies; the resolver must place the full pool without
            // faulting (the AutoFill guarantee solo relies on).
            var models = new List<DataBinding<ModelData>>
            {
                MakeModel(Rifle()), MakeModel(Heavy()), MakeModel(Fist(), wounds: 2),
            };
            DataBinding<UnitData> unit = MakeUnit(models);

            var resolver = new TacticianAssignWoundsResolver();
            AssignWoundsResults results = await resolver.Resolve(
                new AssignWoundsRequest(_player, "Assign Wounds", unit, totalWoundsToAssign: 4f));

            Assert.That(results.IsFinishedAssigning, Is.True);
            Assert.That(results.TotalAssignedWounds, Is.EqualTo(4f).Within(0.001f));
        }

        // --- fixtures ---

        private static Weapon Rifle() => new Weapon("Rifle", 24f, 1, 0);
        private static Weapon Heavy() => new Weapon("Heavy Rifle", 30f, 3, 1);
        private static Weapon Fist() => new Weapon("Fist", 0f, 1, 0);

        private DataBinding<ModelData> MakeModel(Weapon weapon, int wounds = 1, float x = 10f, float z = 10f)
        {
            var model = new ModelData(0.5f, new List<Weapon> { weapon }, new Position(x, z), _store);
            if (wounds > 1) model.SetMaxWounds(wounds);
            return _store.GetDataBinding<ModelData>(_store.Create(model));
        }

        private DataBinding<UnitData> MakeUnit(List<DataBinding<ModelData>> models)
        {
            var unit = new UnitData(_player, "Mixed", quality: 4, defense: 4, modelBindings: models);
            var binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            _store.Create(new ArmyData(_player, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }
    }
}
