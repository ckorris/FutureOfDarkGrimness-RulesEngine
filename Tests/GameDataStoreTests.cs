using FDG.Data;
using NUnit.Framework;

namespace FDG.Tests
{
    // #061: ComponentStore used to allocate fixed-size arrays and throw once full, so two normal
    // ~40-model armies crashed at army creation (ModelData registered at 64). Stores now grow by
    // doubling; capacity is an initial hint, not a ceiling. DataReference is a {TypeID, Index,
    // Generation} value identity, so a grow must leave every existing reference valid. These tests
    // pin that growth contract plus the generation-reuse and foreign-reference (save/network replay)
    // paths the audit flagged as untested.
    [TestFixture]
    public class GameDataStoreTests
    {
        private static readonly TypeID Type = new TypeID(1);

        [Test]
        public void Create_BeyondInitialCapacity_GrowsInsteadOfThrowing()
        {
            ComponentStore<int> store = new ComponentStore<int>(4, Type);

            for (int i = 0; i < 100; i++)
            {
                Assert.DoesNotThrow(() => store.Create(i));
            }

            Assert.That(store.Capacity, Is.GreaterThanOrEqualTo(100));
        }

        [Test]
        public void Create_AfterGrow_PreExistingReferencesStillResolve()
        {
            ComponentStore<int> store = new ComponentStore<int>(2, Type);

            DataReference first = store.Create(111);
            DataReference second = store.Create(222);

            // Force several grows.
            for (int i = 0; i < 50; i++)
            {
                store.Create(i);
            }

            Assert.That(store.IsValid(first, out _), Is.True);
            Assert.That(store.IsValid(second, out _), Is.True);
            Assert.That(store.GetValue(first), Is.EqualTo(111));
            Assert.That(store.GetValue(second), Is.EqualTo(222));
        }

        [Test]
        public void DefaultStore_HoldsTwoFortyModelArmiesWorthOfEntries()
        {
            // The crash the item is named for: ModelData defaults to 64, two 40-model armies = 80.
            // Exercised here through GameDataStore.Create<int> (int defaults to 64) — same store path.
            GameDataStore store = GameDataStore.GameDataStoreBuilder.GetDefault();

            for (int i = 0; i < 80; i++)
            {
                Assert.DoesNotThrow(() => store.Create<int>(i));
            }
        }

        [Test]
        public void Destroy_ThenCreate_ReusesSlotWithNewGenerationAndInvalidatesOldReference()
        {
            ComponentStore<int> store = new ComponentStore<int>(4, Type);

            DataReference original = store.Create(7);
            Assert.That(store.Destroy(original), Is.True);

            DataReference reused = store.Create(9);

            // Slot index is reused, but the generation advances so the stale reference is rejected.
            Assert.That(reused.Index, Is.EqualTo(original.Index));
            Assert.That(reused.Generation, Is.GreaterThan(original.Generation));
            Assert.That(store.IsValid(original, out EInvalidReason reason), Is.False);
            Assert.That(reason, Is.EqualTo(EInvalidReason.OutdatedGeneration));
            Assert.That(store.IsValid(reused, out _), Is.True);
        }

        [Test]
        public void CreateFromReference_IndexBeyondCapacity_GrowsToFit()
        {
            // The save-load / network replay path: a foreign reference whose source store had grown
            // past ours must be accepted by growing, not rejected.
            ComponentStore<int> store = new ComponentStore<int>(4, Type);

            DataReference foreign = new DataReference { TypeID = Type, Index = 500, Generation = 1 };

            Assert.DoesNotThrow(() => store.CreateFromReference(foreign, 42));
            Assert.That(store.Capacity, Is.GreaterThan(500));
            Assert.That(store.IsValid(foreign, out _), Is.True);
            Assert.That(store.GetValue(foreign), Is.EqualTo(42));
        }

        [Test]
        public void CreateFromReference_NegativeIndex_StillThrows()
        {
            ComponentStore<int> store = new ComponentStore<int>(4, Type);

            DataReference bad = new DataReference { TypeID = Type, Index = -1, Generation = 1 };

            Assert.Catch<System.Exception>(() => store.CreateFromReference(bad, 1));
        }
    }
}
