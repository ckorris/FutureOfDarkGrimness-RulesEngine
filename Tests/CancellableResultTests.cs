using FDG.Data;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using Newtonsoft.Json;
using NUnit.Framework;
using System;
using System.Threading.Tasks;

namespace FDG.Tests
{
    [TestFixture]
    public class CancellableResultTests
    {
        // ── Type behavior ──────────────────────────────────────────────────────

        [Test]
        public void Selected_ExposesValue()
        {
            CancellableResult<int> result = new Selected<int>(42);
            Assert.That(result, Is.InstanceOf<Selected<int>>());
            Assert.That(((Selected<int>)result).Value, Is.EqualTo(42));
        }

        [Test]
        public void Cancelled_HasNoValue()
        {
            CancellableResult<int> result = new Cancelled<int>();
            Assert.That(result, Is.InstanceOf<Cancelled<int>>());
        }

        [Test]
        public void Selected_PatternMatch_ExtractsValue()
        {
            CancellableResult<string> result = new Selected<string>("hello");

            string? extracted = result switch
            {
                Selected<string> s => s.Value,
                Cancelled<string> => null,
                _ => null
            };

            Assert.That(extracted, Is.EqualTo("hello"));
        }

        [Test]
        public void Cancelled_PatternMatch_TakesCancelBranch()
        {
            CancellableResult<string> result = new Cancelled<string>();

            bool cancelHit = result switch
            {
                Selected<string> => false,
                Cancelled<string> => true,
                _ => false
            };

            Assert.That(cancelHit, Is.True);
        }

        [Test]
        public void Selected_RecordEquality_HoldsForEqualValues()
        {
            var a = new Selected<int>(5);
            var b = new Selected<int>(5);
            Assert.That(a, Is.EqualTo(b));
        }

        [Test]
        public void Selected_RecordEquality_FailsForDifferentValues()
        {
            var a = new Selected<int>(5);
            var b = new Selected<int>(6);
            Assert.That(a, Is.Not.EqualTo(b));
        }

        [Test]
        public void Cancelled_RecordEquality_AllInstancesEqual()
        {
            var a = new Cancelled<int>();
            var b = new Cancelled<int>();
            Assert.That(a, Is.EqualTo(b));
        }

        // ── JSON round-trip with TypeNameHandling.Auto ─────────────────────────
        // Reply payloads will be deserialized as the abstract CancellableResult<T>;
        // the runtime concrete type must round-trip.

        private static JsonSerializerSettings AutoTypeSettings() => new()
        {
            TypeNameHandling = TypeNameHandling.Auto
        };

        // Reply paths must serialize with the declared (TReply) type so
        // TypeNameHandling.Auto records the concrete subtype.

        [Test]
        public void Selected_RoundTrip_PreservesConcreteTypeAndValue()
        {
            CancellableResult<int> original = new Selected<int>(7);
            string json = JsonConvert.SerializeObject(original, typeof(CancellableResult<int>), AutoTypeSettings());
            CancellableResult<int>? round = JsonConvert.DeserializeObject<CancellableResult<int>>(json, AutoTypeSettings());

            Assert.That(round, Is.InstanceOf<Selected<int>>());
            Assert.That(((Selected<int>)round!).Value, Is.EqualTo(7));
        }

        [Test]
        public void Cancelled_RoundTrip_PreservesConcreteType()
        {
            CancellableResult<int> original = new Cancelled<int>();
            string json = JsonConvert.SerializeObject(original, typeof(CancellableResult<int>), AutoTypeSettings());
            CancellableResult<int>? round = JsonConvert.DeserializeObject<CancellableResult<int>>(json, AutoTypeSettings());

            Assert.That(round, Is.InstanceOf<Cancelled<int>>());
        }

        // ── JSON round-trip via the GameDataStore's settings ───────────────────
        // Reply deserialization in RequestMessageSender uses the data store's
        // JsonSerializerSettings (TypeNameHandling.Auto + DataBinding converters).
        // Make sure CancellableResult<T> still round-trips through those settings
        // for a reference-type T.

        [Test]
        public void Selected_RoundTrip_ViaGameDataStoreSettings_String()
        {
            GameDataStore store = GameDataStore.GameDataStoreBuilder.GetDefault();
            JsonSerializerSettings settings = store.GetJsonSettings();

            CancellableResult<string> original = new Selected<string>("ok");
            string json = JsonConvert.SerializeObject(original, typeof(CancellableResult<string>), settings);
            CancellableResult<string>? round = JsonConvert.DeserializeObject<CancellableResult<string>>(json, settings);

            Assert.That(round, Is.InstanceOf<Selected<string>>());
            Assert.That(((Selected<string>)round!).Value, Is.EqualTo("ok"));
        }

        [Test]
        public void Cancelled_RoundTrip_ViaGameDataStoreSettings_String()
        {
            GameDataStore store = GameDataStore.GameDataStoreBuilder.GetDefault();
            JsonSerializerSettings settings = store.GetJsonSettings();

            CancellableResult<string> original = new Cancelled<string>();
            string json = JsonConvert.SerializeObject(original, typeof(CancellableResult<string>), settings);
            CancellableResult<string>? round = JsonConvert.DeserializeObject<CancellableResult<string>>(json, settings);

            Assert.That(round, Is.InstanceOf<Cancelled<string>>());
        }

        // ── End-to-end registry path ───────────────────────────────────────────
        // StageResolverRegistry.ResolveRequestAsJson must serialize the reply with
        // the declared TReply type so polymorphic CancellableResult<T> survives
        // the round trip back into a typed value.

        [Test]
        public async Task Registry_CancellableReply_Cancelled_RoundTripsThroughResolveRequestAsJson()
        {
            GameDataStore store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var registry = new StageResolverRegistry();
            registry.RegisterResolver<TestCancellableRequest, CancellableResult<int>>(
                new TestCancellableResolver(new Cancelled<int>()));

            var request = new TestCancellableRequest(new PlayerID(Guid.NewGuid()), new TaskID(Guid.NewGuid()));
            string requestJson = JsonConvert.SerializeObject(request, store.GetJsonSettings());

            string replyJson = await registry.ResolveRequestAsJson(
                typeof(TestCancellableRequest).FullName!, requestJson, store);

            CancellableResult<int>? reply = JsonConvert.DeserializeObject<CancellableResult<int>>(
                replyJson, store.GetJsonSettings());

            Assert.That(reply, Is.InstanceOf<Cancelled<int>>());
        }

        [Test]
        public async Task Registry_CancellableReply_Selected_RoundTripsThroughResolveRequestAsJson()
        {
            GameDataStore store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var registry = new StageResolverRegistry();
            registry.RegisterResolver<TestCancellableRequest, CancellableResult<int>>(
                new TestCancellableResolver(new Selected<int>(99)));

            var request = new TestCancellableRequest(new PlayerID(Guid.NewGuid()), new TaskID(Guid.NewGuid()));
            string requestJson = JsonConvert.SerializeObject(request, store.GetJsonSettings());

            string replyJson = await registry.ResolveRequestAsJson(
                typeof(TestCancellableRequest).FullName!, requestJson, store);

            CancellableResult<int>? reply = JsonConvert.DeserializeObject<CancellableResult<int>>(
                replyJson, store.GetJsonSettings());

            Assert.That(reply, Is.InstanceOf<Selected<int>>());
            Assert.That(((Selected<int>)reply!).Value, Is.EqualTo(99));
        }

        // ── Test fixtures ──────────────────────────────────────────────────────

        private class TestCancellableRequest : IStageTaskRequest<CancellableResult<int>>
        {
            public PlayerID TargetPlayerID { get; }
            public TaskID TaskID { get; }
            public string TaskName => "TestCancellable";

            [JsonConstructor]
            public TestCancellableRequest(PlayerID targetPlayerID, TaskID taskID)
            {
                TargetPlayerID = targetPlayerID;
                TaskID = taskID;
            }

            public Task<CancellableResult<int>> Resolve(CancellableResult<int> resolution)
                => Task.FromResult(resolution);
        }

        private class TestCancellableResolver : IStageResolver<TestCancellableRequest, CancellableResult<int>>
        {
            private readonly CancellableResult<int> _result;
            public TestCancellableResolver(CancellableResult<int> result) => _result = result;
            public Task<CancellableResult<int>> Resolve(TestCancellableRequest context)
                => Task.FromResult(_result);
        }
    }
}
