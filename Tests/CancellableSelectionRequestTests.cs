using FDG.Data;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using Newtonsoft.Json;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace FDG.Tests
{
    [TestFixture]
    public class CancellableSelectionRequestTests
    {
        // ── Type contract ──────────────────────────────────────────────────────

        [Test]
        public void CancellableSelectionRequest_DeclaresCancellableReply()
        {
            Type[] interfaces = typeof(CancellableSelectionRequest<UnitData>).GetInterfaces();
            bool hasCancellableReply = false;
            foreach (Type i in interfaces)
            {
                if (!i.IsGenericType) continue;
                if (i.GetGenericTypeDefinition() != typeof(IStageTaskRequest<>)) continue;
                Type reply = i.GetGenericArguments()[0];
                if (reply == typeof(CancellableResult<DataBinding<UnitData>>))
                    hasCancellableReply = true;
            }
            Assert.That(hasCancellableReply, Is.True,
                "CancellableSelectionRequest<T> should declare IStageTaskRequest<CancellableResult<DataBinding<T>>>.");
        }

        // ── Serialization ──────────────────────────────────────────────────────

        [Test]
        public void CancellableSelectionRequest_RoundTrips_ViaGameDataStoreSettings()
        {
            GameDataStore store = GameDataStore.GameDataStoreBuilder.GetDefault();
            JsonSerializerSettings settings = store.GetJsonSettings();

            // Create a unit so we have a real DataBinding<UnitData> reference.
            UnitData unit = new UnitData(new PlayerID(Guid.NewGuid()), "Bob", 4, 4,
                new List<DataBinding<ModelData>>());
            DataReference unitRef = store.Create(unit);
            DataBinding<UnitData> unitBinding = store.GetDataBinding<UnitData>(unitRef);

            var validOpt = new CancellableSelectionRequest<UnitData>.ValidOption(unitBinding, "Bob");
            var request = new CancellableSelectionRequest<UnitData>(
                new PlayerID(Guid.NewGuid()),
                "Pick a unit",
                new List<CancellableSelectionRequest<UnitData>.ValidOption> { validOpt },
                new List<CancellableSelectionRequest<UnitData>.InvalidOption>());

            string json = JsonConvert.SerializeObject(request, settings);
            CancellableSelectionRequest<UnitData>? round =
                JsonConvert.DeserializeObject<CancellableSelectionRequest<UnitData>>(json, settings);

            Assert.That(round, Is.Not.Null);
            Assert.That(round!.Instructions, Is.EqualTo("Pick a unit"));
            Assert.That(round.ValidOptions.Count, Is.EqualTo(1));
            Assert.That(round.ValidOptions[0].Name, Is.EqualTo("Bob"));
        }

        // ── End-to-end: registry path produces correctly-typed Cancelled / Selected replies ──

        [Test]
        public async Task Registry_CancellableSelectionRequest_Cancelled_RoundTrips()
        {
            GameDataStore store = GameDataStore.GameDataStoreBuilder.GetDefault();
            JsonSerializerSettings settings = store.GetJsonSettings();

            UnitData unit = new UnitData(new PlayerID(Guid.NewGuid()), "Bob", 4, 4,
                new List<DataBinding<ModelData>>());
            DataReference unitRef = store.Create(unit);
            DataBinding<UnitData> unitBinding = store.GetDataBinding<UnitData>(unitRef);

            var registry = new StageResolverRegistry();
            registry.RegisterResolver<CancellableSelectionRequest<UnitData>, CancellableResult<DataBinding<UnitData>>>(
                new TestResolver(new Cancelled<DataBinding<UnitData>>()));

            var request = new CancellableSelectionRequest<UnitData>(
                new PlayerID(Guid.NewGuid()),
                "pick",
                new List<CancellableSelectionRequest<UnitData>.ValidOption>
                {
                    new(unitBinding, "Bob")
                },
                new List<CancellableSelectionRequest<UnitData>.InvalidOption>());

            string requestJson = JsonConvert.SerializeObject(request, settings);
            string replyJson = await registry.ResolveRequestAsJson(
                typeof(CancellableSelectionRequest<UnitData>).FullName!, requestJson, store);

            CancellableResult<DataBinding<UnitData>>? reply =
                JsonConvert.DeserializeObject<CancellableResult<DataBinding<UnitData>>>(replyJson, settings);

            Assert.That(reply, Is.InstanceOf<Cancelled<DataBinding<UnitData>>>());
        }

        [Test]
        public async Task Registry_CancellableSelectionRequest_Selected_RoundTripsWithBinding()
        {
            GameDataStore store = GameDataStore.GameDataStoreBuilder.GetDefault();
            JsonSerializerSettings settings = store.GetJsonSettings();

            UnitData unit = new UnitData(new PlayerID(Guid.NewGuid()), "Alice", 4, 4,
                new List<DataBinding<ModelData>>());
            DataReference unitRef = store.Create(unit);
            DataBinding<UnitData> unitBinding = store.GetDataBinding<UnitData>(unitRef);

            var registry = new StageResolverRegistry();
            registry.RegisterResolver<CancellableSelectionRequest<UnitData>, CancellableResult<DataBinding<UnitData>>>(
                new TestResolver(new Selected<DataBinding<UnitData>>(unitBinding)));

            var request = new CancellableSelectionRequest<UnitData>(
                new PlayerID(Guid.NewGuid()),
                "pick",
                new List<CancellableSelectionRequest<UnitData>.ValidOption>
                {
                    new(unitBinding, "Alice")
                },
                new List<CancellableSelectionRequest<UnitData>.InvalidOption>());

            string requestJson = JsonConvert.SerializeObject(request, settings);
            string replyJson = await registry.ResolveRequestAsJson(
                typeof(CancellableSelectionRequest<UnitData>).FullName!, requestJson, store);

            CancellableResult<DataBinding<UnitData>>? reply =
                JsonConvert.DeserializeObject<CancellableResult<DataBinding<UnitData>>>(replyJson, settings);

            Assert.That(reply, Is.InstanceOf<Selected<DataBinding<UnitData>>>());
            DataBinding<UnitData> resolvedBinding = ((Selected<DataBinding<UnitData>>)reply!).Value;
            Assert.That(resolvedBinding.GetValue().Name, Is.EqualTo("Alice"));
        }

        private class TestResolver : IStageResolver<CancellableSelectionRequest<UnitData>, CancellableResult<DataBinding<UnitData>>>
        {
            private readonly CancellableResult<DataBinding<UnitData>> _result;
            public TestResolver(CancellableResult<DataBinding<UnitData>> result) => _result = result;
            public Task<CancellableResult<DataBinding<UnitData>>> Resolve(CancellableSelectionRequest<UnitData> context)
                => Task.FromResult(_result);
        }
    }
}
