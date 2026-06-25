using FDG.Data;
using FDG.Network;
using FDG.Network.Messages;
using FDG.Players;
using Newtonsoft.Json;
using NUnit.Framework;

namespace FDG.Tests
{
    // #075 — version / type-map handshake on client join + enums-as-strings on the wire.
    [TestFixture]
    public class NetworkProtocolTests
    {
        [Test]
        public void TryValidateJoin_MatchingVersionAndHash_Accepts()
        {
            bool ok = NetworkProtocol.TryValidateJoin(
                NetworkProtocol.Version, NetworkProtocol.LocalTypeMapHash, out string reason);

            Assert.That(ok, Is.True);
            Assert.That(reason, Is.Empty);
        }

        [Test]
        public void TryValidateJoin_VersionMismatch_RejectsWithReason()
        {
            bool ok = NetworkProtocol.TryValidateJoin(
                NetworkProtocol.Version + 1, NetworkProtocol.LocalTypeMapHash, out string reason);

            Assert.That(ok, Is.False);
            Assert.That(reason, Does.Contain("version").IgnoreCase);
        }

        [Test]
        public void TryValidateJoin_TypeMapHashMismatch_RejectsWithReason()
        {
            bool ok = NetworkProtocol.TryValidateJoin(
                NetworkProtocol.Version, "not-a-real-hash", out string reason);

            Assert.That(ok, Is.False);
            Assert.That(reason, Is.Not.Empty);
        }

        // A pre-handshake client sends no version/hash; the greeting defaults them to 0/null, which must
        // be rejected rather than silently accepted.
        [Test]
        public void TryValidateJoin_LegacyGreetingDefaults_Rejected()
        {
            var legacyGreeting = new NewLobbyClientGreeting("Old Client");

            bool ok = NetworkProtocol.TryValidateJoin(
                legacyGreeting.ProtocolVersion, legacyGreeting.TypeMapHash, out _);

            Assert.That(ok, Is.False);
        }

        [Test]
        public void TypeMapFingerprint_TwoDefaultStores_Match()
        {
            string a = GameDataStore.GameDataStoreBuilder.GetDefault().GetTypeMapFingerprint();
            string b = GameDataStore.GameDataStoreBuilder.GetDefault().GetTypeMapFingerprint();

            Assert.That(a, Is.Not.Empty);
            Assert.That(a, Is.EqualTo(b), "the same registration order must hash identically.");
        }

        [Test]
        public void TypeMapFingerprint_DifferentTypeMap_Differs()
        {
            string two = new GameDataStore.GameDataStoreBuilder()
                .RegisterType<int>(64).RegisterType<float>(64).Build().GetTypeMapFingerprint();
            string three = new GameDataStore.GameDataStoreBuilder()
                .RegisterType<int>(64).RegisterType<float>(64).RegisterType<Position>(64).Build().GetTypeMapFingerprint();

            Assert.That(two, Is.Not.EqualTo(three), "adding a registered type changes the fingerprint.");
        }

        // The handshake fields are primitives so the greeting survives the wire even across a format
        // mismatch; confirm they round-trip through the store's JSON settings (the wire path).
        [Test]
        public void Greeting_VersionAndHash_RoundTripJson()
        {
            var settings = GameDataStore.GameDataStoreBuilder.GetDefault().GetJsonSettings();
            var greeting = new NewLobbyClientGreeting("Player", 7, "ABC123");

            string json = JsonConvert.SerializeObject(greeting, settings);
            var back = JsonConvert.DeserializeObject<NewLobbyClientGreeting>(json, settings);

            Assert.That(back, Is.Not.Null);
            Assert.That(back!.ProtocolVersion, Is.EqualTo(7));
            Assert.That(back.TypeMapHash, Is.EqualTo("ABC123"));
            Assert.That(back.PlayerName, Is.EqualTo("Player"));
        }

        [Test]
        public void JoinRejectedMessage_RoundTripJson()
        {
            var settings = GameDataStore.GameDataStoreBuilder.GetDefault().GetJsonSettings();
            var message = new LobbyJoinRejectedMessage("Incompatible build.");

            string json = JsonConvert.SerializeObject(message, settings);
            var back = JsonConvert.DeserializeObject<LobbyJoinRejectedMessage>(json, settings);

            Assert.That(back, Is.Not.Null);
            Assert.That(back!.Reason, Is.EqualTo("Incompatible build."));
        }

        // Enums must serialize as their member name on the wire so reordering members can't silently
        // change the format. The converter also reads integer tokens, so older int-encoded payloads load.
        [Test]
        public void Enum_SerializesAsName_AndReadsBothForms()
        {
            var settings = GameDataStore.GameDataStoreBuilder.GetDefault().GetJsonSettings();

            string json = JsonConvert.SerializeObject(EChatMessageType.Team, settings);
            Assert.That(json, Does.Contain("Team"), "the enum serializes as its name, not an ordinal.");
            Assert.That(json, Does.Not.Contain("\"0\"").And.Not.EqualTo("0"));

            Assert.That(JsonConvert.DeserializeObject<EChatMessageType>(json, settings),
                Is.EqualTo(EChatMessageType.Team));
            Assert.That(JsonConvert.DeserializeObject<EChatMessageType>(
                ((int)EChatMessageType.Team).ToString(), settings),
                Is.EqualTo(EChatMessageType.Team), "an integer token still loads (tolerant of old payloads).");
        }
    }
}
