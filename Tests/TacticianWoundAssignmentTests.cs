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

        private DataBinding<ModelData> MakeModel(Weapon weapon, int wounds = 1)
        {
            var model = new ModelData(0.5f, new List<Weapon> { weapon }, new Position(10f, 10f), _store);
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
