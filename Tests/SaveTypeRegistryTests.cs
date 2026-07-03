using FDG.Data;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.SaveLoad;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// Pins the save-type registry (#070) so it stays complete: every type the store can persist — a
    /// registered store type, or a concrete of a polymorphic family Newtonsoft records as a <c>$type</c>
    /// (base shapes, token payloads, token clear-triggers, terrain zones) — must have a stable save ID.
    /// Adding a new one of any of those without registering an ID fails these tests, catching the rename
    /// fragility before a real save silently breaks.
    /// </summary>
    [TestFixture]
    public class SaveTypeRegistryTests
    {
        [Test]
        public void EveryRegisteredStoreType_HasStableId()
        {
            foreach (Type t in GameDataStore.GameDataStoreBuilder.GetDefault().GetTypeMap())
            {
                Assert.That(SaveTypeRegistry.TryGetId(t, out _), Is.True,
                    $"Store type {t.FullName} has no stable save ID — add one to SaveTypeRegistry.");
            }
        }

        [TestCase(typeof(IBaseShape))]
        [TestCase(typeof(TokenPayload))]
        [TestCase(typeof(TokenClearTrigger))]
        [TestCase(typeof(IZone))]
        public void EveryPersistedPolymorphicConcrete_HasStableId(Type family)
        {
            foreach (Type t in PersistedConcretes(family))
            {
                Assert.That(SaveTypeRegistry.TryGetId(t, out _), Is.True,
                    $"{family.Name} implementation {t.FullName} has no stable save ID — add one to " +
                    "SaveTypeRegistry so renaming it can't break saves.");
            }
        }

        [Test]
        public void StableIds_RoundTripBidirectionally()
        {
            IEnumerable<Type> all = GameDataStore.GameDataStoreBuilder.GetDefault().GetTypeMap()
                .Concat(PersistedConcretes(typeof(IBaseShape)))
                .Concat(PersistedConcretes(typeof(TokenPayload)))
                .Concat(PersistedConcretes(typeof(TokenClearTrigger)))
                .Concat(PersistedConcretes(typeof(IZone)));

            foreach (Type t in all)
            {
                if (!SaveTypeRegistry.TryGetId(t, out string id)) continue;   // absence is caught by the pins above
                Assert.That(SaveTypeRegistry.TryGetType(id, out Type back), Is.True,
                    $"ID '{id}' for {t.FullName} did not resolve back to a type.");
                Assert.That(back, Is.EqualTo(t), $"ID '{id}' resolved to {back.FullName}, not {t.FullName}.");
            }
        }

        // Production concrete implementations of a persisted family, excluding test doubles (which live in
        // an FDG.*Test* namespace and share this assembly until the suite is split out, #068).
        private static IEnumerable<Type> PersistedConcretes(Type family)
        {
            return typeof(GameDataStore).Assembly.GetTypes()
                .Where(t => !t.IsAbstract && !t.IsInterface && family.IsAssignableFrom(t)
                    && t.Namespace != null && t.Namespace.StartsWith("FDG")
                    && !t.Namespace.Contains("Test"));
        }
    }
}
