using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using FDG;
using FDG.Data;
using FDG.Network;
using FDG.Network.Messages;
using FDG.Presentation;
using FDG.Presentation.Beats;
using FDG.Presentation.Messages;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.SaveLoad;
using FDG.StageResolution;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using NUnit.Framework;

namespace FDG.Tests
{
    // #186 — wire deserialization hardening. A network message's $type strings are attacker
    // controlled; the wire binder must only resolve them to registered stable IDs, engine types,
    // or benign collections thereof. Framework types — where every known Newtonsoft
    // deserialization gadget lives — must be refused, turning a crafted payload into a
    // JsonSerializationException (and thus, via the read loop's catch, a disconnect).
    [TestFixture]
    public class AllowlistSerializationBinderTests
    {
        // ---- The allowlist rule itself -------------------------------------------------------

        [Test]
        public void IsAllowed_EngineType_IsTrue()
        {
            Assert.That(AllowlistSerializationBinder.IsAllowed(typeof(PresentationBeat)), Is.True);
            Assert.That(AllowlistSerializationBinder.IsAllowed(typeof(TokenPayload.RuleGrant)), Is.True);
        }

        [Test]
        public void IsAllowed_CollectionsOfEngineTypes_AreTrue()
        {
            Assert.That(AllowlistSerializationBinder.IsAllowed(typeof(List<IZone>)), Is.True);
            Assert.That(AllowlistSerializationBinder.IsAllowed(typeof(Position[])), Is.True);
            Assert.That(AllowlistSerializationBinder.IsAllowed(typeof(Dictionary<string, UnitData>)), Is.True);
            Assert.That(AllowlistSerializationBinder.IsAllowed(typeof(List<int>)), Is.True);
        }

        [Test]
        public void IsAllowed_FrameworkTypes_AreFalse()
        {
            // Stand-ins for the gadget class: framework types with side-effectful members.
            Assert.That(AllowlistSerializationBinder.IsAllowed(typeof(FileInfo)), Is.False);
            Assert.That(AllowlistSerializationBinder.IsAllowed(typeof(System.Diagnostics.ProcessStartInfo)), Is.False);
            Assert.That(AllowlistSerializationBinder.IsAllowed(typeof(StringBuilder)), Is.False);
            // A benign collection of a refused type is still refused.
            Assert.That(AllowlistSerializationBinder.IsAllowed(typeof(List<FileInfo>)), Is.False);
            // Non-collection framework generics don't ride the collection exemption.
            Assert.That(AllowlistSerializationBinder.IsAllowed(typeof(Lazy<int>)), Is.False);
        }

        // ---- BindToType behavior -------------------------------------------------------------

        [Test]
        public void BindToType_RegisteredStableId_Resolves()
        {
            var binder = new AllowlistSerializationBinder();
            Assert.That(binder.BindToType(null, "clearTrigger.roundEnd"),
                Is.EqualTo(typeof(TokenClearTrigger.RoundEnd)));
        }

        [Test]
        public void BindToType_UnknownAssemblylessName_Throws()
        {
            // Our serializer only writes assembly-less names for registered IDs; an unknown one is forged.
            var binder = new AllowlistSerializationBinder();
            Assert.Throws<JsonSerializationException>(() => binder.BindToType(null, "no.such.id"));
        }

        [Test]
        public void BindToType_EngineTypeByAssemblyQualifiedName_Resolves()
        {
            var binder = new AllowlistSerializationBinder();
            var writer = new DefaultSerializationBinder();
            writer.BindToName(typeof(PresentationBeat), out string? assembly, out string? name);

            Assert.That(binder.BindToType(assembly, name!), Is.EqualTo(typeof(PresentationBeat)));
        }

        [Test]
        public void BindToType_FrameworkTypeByAssemblyQualifiedName_Throws()
        {
            var binder = new AllowlistSerializationBinder();
            // Emit the exact resolvable name the default binder would write, so this pins the
            // allowlist rejection rather than a name-resolution failure.
            var writer = new DefaultSerializationBinder();
            writer.BindToName(typeof(FileInfo), out string? assembly, out string? name);

            Assert.Throws<JsonSerializationException>(() => binder.BindToType(assembly, name!));
        }

        // ---- End to end through MessageSerializer --------------------------------------------

        private static MessageSerializer BuildSerializer()
        {
            var serializer = new MessageSerializer(GameDataStore.GameDataStoreBuilder.GetDefault());
            serializer.RegisterMessageType<PresentBeatMessage>();
            return serializer;
        }

        /// <summary>Builds a wire frame ([type-length][type-string][json]) by hand, as an attacker would.</summary>
        private static ArraySegment<byte> CraftFrame(string typeString, string json)
        {
            byte[] typeBytes = Encoding.UTF8.GetBytes(typeString);
            byte[] jsonBytes = Encoding.UTF8.GetBytes(json);
            byte[] frame = new byte[sizeof(int) + typeBytes.Length + jsonBytes.Length];
            BitConverter.GetBytes(typeBytes.Length).CopyTo(frame, 0);
            typeBytes.CopyTo(frame, sizeof(int));
            jsonBytes.CopyTo(frame, sizeof(int) + typeBytes.Length);
            return new ArraySegment<byte>(frame);
        }

        [Test]
        public void MessageSerializer_LegitimatePolymorphicMessage_StillRoundTrips()
        {
            MessageSerializer serializer = BuildSerializer();
            var message = new PresentBeatMessage(new BannerBeat("Wire binder test", new TextColor(255, 255, 255, 255)));

            ArraySegment<byte> bytes = serializer.SerializeMessage(message);
            object? roundTripped = serializer.DeserializeMessage(bytes);

            Assert.That(roundTripped, Is.InstanceOf<PresentBeatMessage>());
            Assert.That(((PresentBeatMessage)roundTripped!).Beat, Is.InstanceOf<BannerBeat>());
        }

        [Test]
        public void MessageSerializer_HostileInnerType_ThrowsInsteadOfResolving()
        {
            MessageSerializer serializer = BuildSerializer();

            var writer = new DefaultSerializationBinder();
            writer.BindToName(typeof(FileInfo), out string? assembly, out string? name);
            string hostileJson = $"{{\"Beat\":{{\"$type\":\"{name}, {assembly}\",\"fileName\":\"/tmp/x\"}}}}";
            ArraySegment<byte> frame = CraftFrame(typeof(PresentBeatMessage).ToString(), hostileJson);

            // The exception matters: it bubbles into the read loop's catch and drops the connection.
            Assert.Throws<JsonSerializationException>(() => serializer.DeserializeMessage(frame));
        }

        [Test]
        public void MessageSerializer_ForgedStableId_ThrowsInsteadOfResolving()
        {
            MessageSerializer serializer = BuildSerializer();

            string hostileJson = "{\"Beat\":{\"$type\":\"totally.fake.id\"}}";
            ArraySegment<byte> frame = CraftFrame(typeof(PresentBeatMessage).ToString(), hostileJson);

            Assert.Throws<JsonSerializationException>(() => serializer.DeserializeMessage(frame));
        }

        // ---- Request-BODY path (StageResolverRegistry.ResolveRequestAsJson) ------------------
        // The envelope hardening isn't enough: the inner request/reply JSON is deserialized
        // separately. This is the site a malicious host attacks on a connecting client (#264).

        [Test]
        public async Task ResolveRequestAsJson_LegitimateRequestBody_StillResolves()
        {
            GameDataStore store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var registry = new StageResolverRegistry();
            registry.RegisterResolver<WireProbeRequest, string>(new WireProbeResolver());

            var request = new WireProbeRequest(new PlayerID(Guid.NewGuid()), new TaskID(Guid.NewGuid()));
            string requestJson = JsonConvert.SerializeObject(request, store.GetJsonSettings());

            string replyJson = await registry.ResolveRequestAsJson(
                typeof(WireProbeRequest).FullName!, requestJson, store);

            Assert.That(replyJson, Does.Contain("resolved"));
        }

        [Test]
        public void ResolveRequestAsJson_HostileTypeInRequestBody_Throws()
        {
            GameDataStore store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var registry = new StageResolverRegistry();
            registry.RegisterResolver<WireProbeRequest, string>(new WireProbeResolver());

            // A crafted request body naming a framework gadget where a nested polymorphic value
            // would sit. The store's permissive settings would resolve it; the wire settings the
            // resolver now uses must refuse it (bubbling out of ResolveRequestAsJson's task).
            var writer = new DefaultSerializationBinder();
            writer.BindToName(typeof(FileInfo), out string? assembly, out string? name);
            string hostileRequestJson =
                $"{{\"TargetPlayerID\":{{\"ID\":\"{Guid.Empty}\"}},\"TaskID\":{{\"ID\":\"{Guid.Empty}\"}}," +
                $"\"Payload\":{{\"$type\":\"{name}, {assembly}\",\"fileName\":\"/tmp/x\"}}}}";

            Assert.ThrowsAsync<JsonSerializationException>(async () => await registry.ResolveRequestAsJson(
                typeof(WireProbeRequest).FullName!, hostileRequestJson, store));
        }

        private sealed class WireProbeRequest : IStageTaskRequest<string>
        {
            public PlayerID TargetPlayerID { get; }
            public TaskID TaskID { get; }
            // A polymorphic slot: exactly where an attacker plants a gadget $type. Object-typed so
            // TypeNameHandling.Auto records the concrete type — the thing the binder must gate.
            public object? Payload { get; }
            public string TaskName => "WireProbe";

            [JsonConstructor]
            public WireProbeRequest(PlayerID targetPlayerID, TaskID taskID, object? payload = null)
            {
                TargetPlayerID = targetPlayerID;
                TaskID = taskID;
                Payload = payload;
            }

            public Task<string> Resolve(string resolution) => Task.FromResult(resolution);
        }

        private sealed class WireProbeResolver : IStageResolver<WireProbeRequest, string>
        {
            public Task<string> Resolve(WireProbeRequest context) => Task.FromResult("resolved");
        }

        // ---- Store settings (save/load + network full-state sync share this binder, #265) -----

        [Test]
        public void StoreSettings_UseTheAllowlistBinder()
        {
            GameDataStore store = GameDataStore.GameDataStoreBuilder.GetDefault();
            Assert.That(store.GetJsonSettings().SerializationBinder, Is.InstanceOf<AllowlistSerializationBinder>());
        }

        [Test]
        public void StoreSettings_LegitimatePolymorphicPayload_RoundTrips()
        {
            JsonSerializerSettings settings = GameDataStore.GameDataStoreBuilder.GetDefault().GetJsonSettings();

            // A registered polymorphic zone ($type "zone.circular") round-trips through the store
            // settings exactly as a store entry would. Pass the declared type (IZone) so Auto records
            // the concrete subtype, as the store does for a polymorphic field.
            string json = JsonConvert.SerializeObject(new CircularZone(20, 24, 5), typeof(IZone), settings);
            var back = JsonConvert.DeserializeObject<IZone>(json, settings);

            Assert.That(back, Is.InstanceOf<CircularZone>());
        }

        [Test]
        public void StoreSettings_HostilePolymorphicPayload_Throws()
        {
            JsonSerializerSettings settings = GameDataStore.GameDataStoreBuilder.GetDefault().GetJsonSettings();

            var writer = new DefaultSerializationBinder();
            writer.BindToName(typeof(FileInfo), out string? assembly, out string? name);
            string hostileJson = $"{{\"$type\":\"{name}, {assembly}\",\"fileName\":\"/tmp/x\"}}";

            Assert.Throws<JsonSerializationException>(
                () => JsonConvert.DeserializeObject<object>(hostileJson, settings));
        }

        // ---- Terrain layout files (#265) -----------------------------------------------------

        [Test]
        public void TerrainLayout_LegitimateFile_LoadsIncludingLegacyAssemblyQualifiedType()
        {
            // Serialize with a bare TypeNameHandling.Auto (no binder) so the zone $type is written
            // assembly-qualified, exactly like a terrain file authored before #265. The hardened
            // loader must still accept it (RectangularZone is an engine type).
            var legacySettings = new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.Auto };
            var layout = new TerrainLayoutFile
            {
                Name = "Legit",
                Pieces = new List<TerrainPieceEntry>
                {
                    new TerrainPieceEntry
                    {
                        TerrainType = ETerrainType.Cover,
                        Shape = new RectangularZone(10, 20, 10, 20),
                        HeightInches = 1f,
                    },
                },
            };
            string path = Path.GetTempFileName();
            try
            {
                File.WriteAllText(path, JsonConvert.SerializeObject(layout, legacySettings));

                TerrainLayoutFile? loaded = TerrainLayoutLoader.TryLoadFromFile(path, out string? error);

                Assert.That(error, Is.Null);
                Assert.That(loaded, Is.Not.Null);
                Assert.That(loaded!.Pieces[0].Shape, Is.InstanceOf<RectangularZone>());
            }
            finally { File.Delete(path); }
        }

        [Test]
        public void TerrainLayout_HostileZoneType_FailsToLoad()
        {
            var writer = new DefaultSerializationBinder();
            writer.BindToName(typeof(FileInfo), out string? assembly, out string? name);
            // A terrain file whose polymorphic Shape names a framework gadget instead of a zone.
            string hostile =
                $"{{\"Name\":\"x\",\"Pieces\":[{{\"TerrainType\":0," +
                $"\"Shape\":{{\"$type\":\"{name}, {assembly}\",\"fileName\":\"/tmp/x\"}},\"HeightInches\":0.0}}]}}";
            string path = Path.GetTempFileName();
            try
            {
                File.WriteAllText(path, hostile);

                // TryLoadFromFile catches the JsonSerializationException and reports it as a load error
                // rather than instantiating the gadget.
                TerrainLayoutFile? loaded = TerrainLayoutLoader.TryLoadFromFile(path, out string? error);

                Assert.That(loaded, Is.Null);
                Assert.That(error, Is.Not.Null);
            }
            finally { File.Delete(path); }
        }
    }
}
