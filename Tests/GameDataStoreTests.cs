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

        // ──────────────────────────────────────────────────────────────────────
        // #270 — the two foreign-create paths, and how they differ
        // ──────────────────────────────────────────────────────────────────────

        // The LIVE stream is sequential, so a generation further ahead than the next one means a create
        // was missed. That guard is the reason the live path exists separately; it must stay.
        [Test]
        public void CreateFromReference_GenerationTooFarAhead_StillRejected()
        {
            ComponentStore<int> store = new ComponentStore<int>(4, Type);

            DataReference skipped = new DataReference { TypeID = Type, Index = 0, Generation = 3 };

            // The assignment exception is private to ComponentStore, so assert on the reason it reports.
            var ex = Assert.Catch<System.Exception>(() => store.CreateFromReference(skipped, 1));
            Assert.That(ex!.Message, Does.Contain(EInvalidReason.FutureGeneration.ToString()));
        }

        // A SNAPSHOT carries whatever generation each slot had reached, and this store starts at 0.
        // Rejecting that made every save of a resumed game unloadable.
        [Test]
        public void CreateFromReplay_AdoptsAGenerationFromARecycledSlot()
        {
            ComponentStore<int> store = new ComponentStore<int>(4, Type);

            DataReference recycled = new DataReference { TypeID = Type, Index = 0, Generation = 3 };

            Assert.DoesNotThrow(() => store.CreateFromReplay(recycled, 42));
            Assert.That(store.IsValid(recycled, out _), Is.True, "the entry's own generation is adopted");
            Assert.That(store.GetValue(recycled), Is.EqualTo(42));

            // Adopted, not flattened: a stale reference to the same slot is still stale afterwards.
            DataReference stale = new DataReference { TypeID = Type, Index = 0, Generation = 1 };
            Assert.That(store.IsValid(stale, out EInvalidReason reason), Is.False);
            Assert.That(reason, Is.EqualTo(EInvalidReason.OutdatedGeneration));
        }

        // Everything that still means something on the replay path.
        [Test]
        public void CreateFromReplay_KeepsTheInvariantsThatMatter()
        {
            ComponentStore<int> store = new ComponentStore<int>(4, Type);

            DataReference negative = new DataReference { TypeID = Type, Index = -1, Generation = 1 };
            Assert.Catch<System.Exception>(() => store.CreateFromReplay(negative, 1), "negative index");

            DataReference wrongType = new DataReference { TypeID = new TypeID(2), Index = 0, Generation = 1 };
            Assert.Catch<System.Exception>(() => store.CreateFromReplay(wrongType, 1), "another type's reference");

            // Generation 0 is an unset/null reference - Create pre-increments, so live entries start at 1.
            DataReference unset = new DataReference { TypeID = Type, Index = 1, Generation = 0 };
            Assert.Catch<System.Exception>(() => store.CreateFromReplay(unset, 1), "generation 0");

            DataReference good = new DataReference { TypeID = Type, Index = 2, Generation = 5 };
            store.CreateFromReplay(good, 1);
            Assert.Catch<System.Exception>(() => store.CreateFromReplay(good, 2),
                "the same slot twice in one replay");
        }

        [Test]
        public void CreateFromReplay_IndexBeyondCapacity_GrowsToFit()
        {
            ComponentStore<int> store = new ComponentStore<int>(4, Type);

            DataReference foreign = new DataReference { TypeID = Type, Index = 500, Generation = 4 };

            Assert.DoesNotThrow(() => store.CreateFromReplay(foreign, 42));
            Assert.That(store.Capacity, Is.GreaterThan(500));
            Assert.That(store.GetValue(foreign), Is.EqualTo(42));
        }

        // ──────────────────────────────────────────────────────────────────────
        // #63 — IsValid, direct: every EInvalidReason branch, checked via IsValid itself rather than
        // an exception message. The tests above already pin OutdatedGeneration and (indirectly, via
        // exception text) FutureGeneration/IncorrectType; these fill the remaining reason codes and
        // check the two already covered through the actual IsValid() call sites will use.
        // ──────────────────────────────────────────────────────────────────────

        [Test]
        public void IsValid_FreshlyCreatedReference_IsValid()
        {
            ComponentStore<int> store = new ComponentStore<int>(4, Type);

            DataReference reference = store.Create(1);

            Assert.That(store.IsValid(reference, out EInvalidReason reason), Is.True);
            Assert.That(reason, Is.EqualTo(EInvalidReason.Valid));
        }

        [Test]
        public void IsValid_NeverCreatedSlot_IsNotAssigned()
        {
            ComponentStore<int> store = new ComponentStore<int>(4, Type);

            // A reference into a slot nothing has ever occupied (capacity 4, nothing created yet).
            DataReference untouched = new DataReference { TypeID = Type, Index = 2, Generation = 1 };

            Assert.That(store.IsValid(untouched, out EInvalidReason reason), Is.False);
            Assert.That(reason, Is.EqualTo(EInvalidReason.IsNotAssigned));
        }

        [Test]
        public void IsValid_DestroyedSlot_BeforeReuse_IsNotAssigned()
        {
            // Distinct from the reuse case below: right after Destroy, the generation hasn't moved
            // yet, so a reference to the just-freed slot reads as "nothing there" (IsNotAssigned), not
            // as stale (OutdatedGeneration) — that reason only appears once the slot is reused.
            ComponentStore<int> store = new ComponentStore<int>(4, Type);

            DataReference original = store.Create(7);
            Assert.That(store.Destroy(original), Is.True);

            Assert.That(store.IsValid(original, out EInvalidReason reason), Is.False);
            Assert.That(reason, Is.EqualTo(EInvalidReason.IsNotAssigned));
        }

        [Test]
        public void IsValid_WrongTypeID_IncorrectType()
        {
            ComponentStore<int> store = new ComponentStore<int>(4, Type);
            DataReference reference = store.Create(1);

            DataReference wrongType = new DataReference
            {
                TypeID = new TypeID(Type.ID + 1), Index = reference.Index, Generation = reference.Generation,
            };

            Assert.That(store.IsValid(wrongType, out EInvalidReason reason), Is.False);
            Assert.That(reason, Is.EqualTo(EInvalidReason.IncorrectType));
        }

        [Test]
        public void IsValid_IndexAtOrBeyondCapacity_IndexExceedsCapacity()
        {
            ComponentStore<int> store = new ComponentStore<int>(4, Type);

            DataReference atCapacity = new DataReference { TypeID = Type, Index = 4, Generation = 1 };
            DataReference wellBeyond = new DataReference { TypeID = Type, Index = 999, Generation = 1 };
            DataReference negative = new DataReference { TypeID = Type, Index = -1, Generation = 1 };

            Assert.That(store.IsValid(atCapacity, out EInvalidReason r1), Is.False);
            Assert.That(r1, Is.EqualTo(EInvalidReason.IndexExceedsCapacity));

            Assert.That(store.IsValid(wellBeyond, out EInvalidReason r2), Is.False);
            Assert.That(r2, Is.EqualTo(EInvalidReason.IndexExceedsCapacity));

            Assert.That(store.IsValid(negative, out EInvalidReason r3), Is.False);
            Assert.That(r3, Is.EqualTo(EInvalidReason.IndexExceedsCapacity));
        }

        [Test]
        public void IsValid_GenerationAheadOfCurrent_FutureGeneration()
        {
            ComponentStore<int> store = new ComponentStore<int>(4, Type);
            DataReference reference = store.Create(1); // generation 1 at this slot.

            DataReference aheadOfCurrent = new DataReference
            {
                TypeID = Type, Index = reference.Index, Generation = reference.Generation + 5,
            };

            Assert.That(store.IsValid(aheadOfCurrent, out EInvalidReason reason), Is.False);
            Assert.That(reason, Is.EqualTo(EInvalidReason.FutureGeneration));
        }

        // ──────────────────────────────────────────────────────────────────────
        // #63 — CreateFromReference rejection paths not already covered above: the live-stream path's
        // sibling checks (IncorrectType, IndexAlreadyAssigned) and the OutdatedGeneration half of its
        // generation gate (only the FutureGeneration half was pinned before).
        // ──────────────────────────────────────────────────────────────────────

        [Test]
        public void CreateFromReference_WrongTypeID_Rejected()
        {
            ComponentStore<int> store = new ComponentStore<int>(4, Type);

            DataReference wrongType = new DataReference { TypeID = new TypeID(Type.ID + 1), Index = 0, Generation = 1 };

            var ex = Assert.Catch<System.Exception>(() => store.CreateFromReference(wrongType, 1));
            Assert.That(ex!.Message, Does.Contain(EInvalidReason.IncorrectType.ToString()));
        }

        [Test]
        public void CreateFromReference_SlotAlreadyOccupied_Rejected()
        {
            ComponentStore<int> store = new ComponentStore<int>(4, Type);
            DataReference occupied = store.Create(1);

            DataReference sameSlot = new DataReference
            {
                TypeID = Type, Index = occupied.Index, Generation = occupied.Generation + 1,
            };

            var ex = Assert.Catch<System.Exception>(() => store.CreateFromReference(sameSlot, 2));
            Assert.That(ex!.Message, Does.Contain(EInvalidReason.IndexAlreadyAssigned.ToString()));
        }

        [Test]
        public void CreateFromReference_GenerationAtOrBehindCurrent_RejectedAsOutdated()
        {
            // The other half of the live-stream generation gate (#270): further ahead than the next
            // one is FutureGeneration (already pinned above); at-or-behind is a stale/duplicate
            // message and must be rejected as OutdatedGeneration, not silently accepted.
            ComponentStore<int> store = new ComponentStore<int>(4, Type);

            DataReference stale = new DataReference { TypeID = Type, Index = 0, Generation = 1 };
            store.CreateFromReference(stale, 1); // now at generation 1.

            DataReference sameGenerationAgain = new DataReference { TypeID = Type, Index = 1, Generation = 0 };
            // Generation 0 at an untouched slot (current generation 0) is "at or behind" (>=), not ahead.
            var ex = Assert.Catch<System.Exception>(() => store.CreateFromReference(sameGenerationAgain, 2));
            Assert.That(ex!.Message, Does.Contain(EInvalidReason.OutdatedGeneration.ToString()));
        }
    }
}
