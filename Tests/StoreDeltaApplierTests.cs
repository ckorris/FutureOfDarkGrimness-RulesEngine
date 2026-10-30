using FDG.Data;
using FDG.Network.Synchronization;
using FDG.Replay;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.SaveLoad;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// #454: the one delta-apply implementation shared by the networked client's incremental sync and replay
    /// playback. A mirror store is fed the source store's own JSON events (exactly what the sender sends and
    /// the recorder records) and must end up a faithful copy of the source - compared as whole re-saved
    /// stores, since a ModelData's JSON carries a Position derived from its binding (#450 note).
    /// </summary>
    [TestFixture]
    public class StoreDeltaApplierTests
    {
        private static readonly PlayerID Player = new PlayerID(Guid.NewGuid());

        // Feeds every delta the source raises into the mirror, as GameDataUpdateSender -> Receiver does.
        private static void Mirror(GameDataStore source, GameDataStore mirror,
            EDeltaGenerationPolicy policy = EDeltaGenerationPolicy.Sequential)
        {
            source.OnDataAddedAsJson += (reference, json) => StoreDeltaApplier.ApplyAdd(mirror, reference, json, policy);
            source.OnDataUpdatedAsJson += (reference, json) => StoreDeltaApplier.ApplyUpdate(mirror, reference, json);
            source.OnDataRemoved += reference => StoreDeltaApplier.ApplyRemove(mirror, reference);
        }

        private static (DataReference Unit, DataReference Model) CreateUnitWithRules(GameDataStore store)
        {
            var model = new ModelData(baseRadiusInches: 0.5f, weapons: new List<Weapon>(),
                initialPosition: new Position(5, 5), gameDataStore: store);
            model.AttachRuleDefinition(new ResolvedRule("Stealth", CoreRuleCatalog.Stealth));
            DataReference modelRef = store.Create(model);

            var unit = new UnitData(Player, "Warriors", quality: 4, defense: 4,
                modelBindings: new List<DataBinding<ModelData>> { store.GetDataBinding<ModelData>(modelRef) });
            unit.AttachRuleDefinition(new ResolvedRule("Stealth", CoreRuleCatalog.Stealth));
            DataReference unitRef = store.Create(unit);

            return (unitRef, modelRef);
        }

        [Test]
        public void AddsUpdatesAndRemoves_ProduceAFaithfulCopyOfTheSource()
        {
            var source = GameDataStore.GameDataStoreBuilder.GetDefault();
            var mirror = GameDataStore.GameDataStoreBuilder.GetDefault();
            Mirror(source, mirror);
            new TokenChangeBroadcaster(source);

            (DataReference unitRef, DataReference modelRef) = CreateUnitWithRules(source);
            DataReference scratch = source.Create("scratch");

            ModelData model = source.GetValue<ModelData>(modelRef);
            model.SetPosition(new Position(12, 7));
            source.GetValue<UnitData>(unitRef).Tokens.AddToken(new Token(TokenType.Shaken, 1, new TokenClearTrigger.RoundEnd()));
            source.SetValue(scratch, "rewritten");
            source.Destroy(scratch);

            Assert.That(mirror.IsValid(scratch, out _), Is.False, "the remove reached the mirror");
            Assert.That(GameSaveSerializer.Save(mirror), Is.EqualTo(GameSaveSerializer.Save(source)));
        }

        [Test]
        public void AnUpdatedUnitAndModel_KeepTheirRules_OnTheMirror()
        {
            var source = GameDataStore.GameDataStoreBuilder.GetDefault();
            var mirror = GameDataStore.GameDataStoreBuilder.GetDefault();
            Mirror(source, mirror);

            (DataReference unitRef, DataReference modelRef) = CreateUnitWithRules(source);

            // Each update reaches the mirror as a whole new deserialized instance.
            source.SetValue(unitRef, source.GetValue<UnitData>(unitRef));
            source.SetValue(modelRef, source.GetValue<ModelData>(modelRef));

            Assert.That(mirror.GetValue<UnitData>(unitRef).RuleDefinitions.Select(r => r.Definition.Name),
                Does.Contain("Stealth"), "a unit's rules survive its update on the mirror");
            Assert.That(mirror.GetValue<ModelData>(modelRef).RuleDefinitions.Select(r => r.Definition.Name),
                Does.Contain("Stealth"), "a model's rules survive its update on the mirror");
        }

        // A slot destroyed before a snapshot and refilled after it: the rebuilt store knows nothing of the
        // slot's generation, so the refill looks "from the future" to the live stream's check.
        [Test]
        public void AnAddIntoASlotRecycledBeforeTheSnapshot_NeedsTheAdoptPolicy()
        {
            var source = GameDataStore.GameDataStoreBuilder.GetDefault();
            DataReference first = source.Create("first");
            source.Destroy(first);
            string snapshot = GameSaveSerializer.Save(source);

            DataReference? refill = null;
            string? refillJson = null;
            source.OnDataAddedAsJson += (reference, json) => (refill, refillJson) = (reference, json);
            DataReference second = source.Create("second");
            Assert.That(refill, Is.EqualTo(second));
            Assert.That((second.Index, second.Generation), Is.EqualTo((first.Index, first.Generation + 1)),
                "precondition: the add refills the destroyed slot at the next generation");

            GameDataStore sequential = GameSaveSerializer.Load(snapshot);
            Assert.That(() => StoreDeltaApplier.ApplyAdd(sequential, second, refillJson!, EDeltaGenerationPolicy.Sequential),
                Throws.Exception, "the live stream's check rejects it");

            GameDataStore adopted = GameSaveSerializer.Load(snapshot);
            StoreDeltaApplier.ApplyAdd(adopted, second, refillJson!, EDeltaGenerationPolicy.Adopt);
            Assert.That(adopted.GetValue<string>(second), Is.EqualTo("second"));
            Assert.That(GameSaveSerializer.Save(adopted), Is.EqualTo(GameSaveSerializer.Save(source)));
        }

        [Test]
        public void ReplayEvents_ApplyByKind_AndOtherEventsAreLeftAlone()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            DataReference reference = new DataReference
            {
                TypeID = new TypeID(store.GetTypeMap().IndexOf(typeof(string))),
                Index = 0,
                Generation = 1,
            };

            Assert.That(StoreDeltaApplier.Apply(store, new StoreAddEvent(reference, "\"a\"")), Is.True);
            Assert.That(StoreDeltaApplier.Apply(store, new StoreUpdateEvent(reference, "\"b\"")), Is.True);
            Assert.That(store.GetValue<string>(reference), Is.EqualTo("b"));
            Assert.That(StoreDeltaApplier.Apply(store, new LogEvent("not a delta")), Is.False);
            Assert.That(StoreDeltaApplier.Apply(store, new StoreRemoveEvent(reference)), Is.True);
            Assert.That(store.IsValid(reference, out _), Is.False);
            Assert.That(StoreDeltaApplier.ApplyRemove(store, reference), Is.False, "removing a gone entry is not an error");
        }
    }
}
