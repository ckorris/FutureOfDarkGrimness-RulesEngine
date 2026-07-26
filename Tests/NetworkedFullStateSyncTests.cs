using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using FDG.Data;
using FDG.MessageBus;
using FDG.Network;
using FDG.Network.Connection;
using FDG.Network.Synchronization;
using FDG.Players;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// Networked equivalent of the headless smoke, at the layer that actually broke: the full-state
    /// catch-up sync a client runs on join. Stands up the real MessageBusHost_Networked /
    /// MessageBusClient_Networked + GameDataUpdateSender / GameDataUpdateReceiver over an in-process
    /// loopback "network" (no TCP, fully synchronous), populates a host store like army-load does, and
    /// asserts the client store ends up a faithful, rule-carrying copy.
    ///
    /// Regression for the networked-deploy crash: ModelData carries a DataBinding&lt;Float2&gt; facing but
    /// Float2 is registered last, so the snapshot lists every model before any facing. The old retry-less
    /// receiver threw IsNotAssigned on that forward reference and left the client store empty, so the first
    /// deploy SelectionRequest&lt;UnitData&gt; failed to resolve and faulted the host state machine.
    /// </summary>
    [TestFixture]
    public class NetworkedFullStateSyncTests
    {
        private static readonly PlayerID Player = new PlayerID(Guid.NewGuid());

        [Test]
        public void FullStateSync_PopulatesClientStore_WithUnitsModelsAndRehydratedRules()
        {
            var hostStore = GameDataStore.GameDataStoreBuilder.GetDefault();
            var clientStore = GameDataStore.GameDataStoreBuilder.GetDefault();

            var (hostBus, clientBus) = ConnectLoopback(hostStore, clientStore);

            // Host owns the canonical data (only the host runs army-load / FDGServer).
            var sender = new GameDataUpdateSender(hostStore, hostBus);

            // Build an army the way army-load does: a model (whose ctor auto-creates its Float2 facing) and
            // a unit that references it and carries an attached special rule.
            var model = new ModelData(baseRadiusInches: 0.5f, weapons: new List<Weapon>(),
                initialPosition: new Position(5, 5), gameDataStore: hostStore);
            DataReference modelRef = hostStore.Create(model);

            var unit = new UnitData(Player, "Warriors", quality: 4, defense: 4,
                modelBindings: new List<DataBinding<ModelData>> { hostStore.GetDataBinding<ModelData>(modelRef) });
            unit.AttachRuleDefinition(new ResolvedRule("Stealth", CoreRuleCatalog.Stealth));
            DataReference unitRef = hostStore.Create(unit);

            // Client joins and asks for the whole world (this is what FDGGame_AsClient's ctor does).
            var receiver = new GameDataUpdateReceiver(clientStore, clientBus);
            receiver.RequestAllCurrentData();

            // The reference the deploy stage will hand the client resolves to a real unit now.
            Assert.That(clientStore.IsValid(unitRef, out _), Is.True,
                "UnitData must exist on the client after full-state sync (this is what deploy resolves).");
            Assert.That(clientStore.IsValid(modelRef, out _), Is.True,
                "ModelData must exist on the client - its forward Float2 facing reference resolves via retry.");

            UnitData clientUnit = clientStore.GetValue<UnitData>(unitRef);
            Assert.That(clientUnit.Name, Is.EqualTo("Warriors"));
            Assert.That(clientUnit.Models.Count(), Is.EqualTo(1),
                "The unit's model binding must resolve against the synced ModelData.");
            Assert.That(clientUnit.RuleDefinitions.Select(r => r.Definition.Name), Does.Contain("Stealth"),
                "[JsonIgnore] special rules must be rehydrated on the client, not silently dropped.");
        }

        /// <summary>
        /// #190 regression: mid-game token changes must reach a joined client. Tokens live in an in-place
        /// TokenContainer field on UnitData/ModelData, so before TokenChangeBroadcaster nothing re-Set the
        /// owning entry on a token change and the client's tokens stayed frozen at the join snapshot - the
        /// "Fatigued never clears" field report. Covers add, count-change (unit) and add + removal (model),
        /// removal being the case that report actually hit.
        /// </summary>
        [Test]
        public void TokenChanges_AfterJoin_ReplicateToClient()
        {
            var hostStore = GameDataStore.GameDataStoreBuilder.GetDefault();
            var clientStore = GameDataStore.GameDataStoreBuilder.GetDefault();

            var (hostBus, clientBus) = ConnectLoopback(hostStore, clientStore);

            var sender = new GameDataUpdateSender(hostStore, hostBus);
            // Constructed before the unit/model exist, exactly as FDGServer's new-game path does, so it hooks
            // them on creation.
            var tokenBroadcaster = new TokenChangeBroadcaster(hostStore);

            var model = new ModelData(baseRadiusInches: 0.5f, weapons: new List<Weapon>(),
                initialPosition: new Position(5, 5), gameDataStore: hostStore);
            DataReference modelRef = hostStore.Create(model);

            var unit = new UnitData(Player, "Warriors", quality: 4, defense: 4,
                modelBindings: new List<DataBinding<ModelData>> { hostStore.GetDataBinding<ModelData>(modelRef) });
            DataReference unitRef = hostStore.Create(unit);

            var receiver = new GameDataUpdateReceiver(clientStore, clientBus);
            receiver.RequestAllCurrentData();

            // Baseline: the join snapshot carried no tokens.
            Assert.That(clientStore.GetValue<UnitData>(unitRef).Tokens.HasToken(TokenType.Shaken), Is.False,
                "Precondition: the unit starts with no Shaken token on the client.");

            // Add on the host -> client sees it (OnTokenAdded).
            hostStore.GetValue<UnitData>(unitRef).Tokens
                .AddToken(new Token(TokenType.Shaken, 1, new TokenClearTrigger.RoundEnd()));
            Assert.That(clientStore.GetValue<UnitData>(unitRef).Tokens.HasToken(TokenType.Shaken), Is.True,
                "A token added on the host after join must replicate to the client.");
            Assert.That(clientStore.GetValue<UnitData>(unitRef).Tokens.GetTokenCount(TokenType.Shaken), Is.EqualTo(1));

            // Stack another -> client sees the new count (OnTokenCountChanged).
            hostStore.GetValue<UnitData>(unitRef).Tokens
                .AddToken(new Token(TokenType.Shaken, 1, new TokenClearTrigger.RoundEnd()));
            Assert.That(clientStore.GetValue<UnitData>(unitRef).Tokens.GetTokenCount(TokenType.Shaken), Is.EqualTo(2),
                "A token count change on the host must replicate to the client.");

            // Model-level token add -> client sees it (covers the ModelData hook path).
            hostStore.GetValue<ModelData>(modelRef).Tokens
                .AddToken(new Token(TokenType.Fatigued, 1, new TokenClearTrigger.RoundEnd()));
            Assert.That(clientStore.GetValue<ModelData>(modelRef).Tokens.HasToken(TokenType.Fatigued), Is.True,
                "A model-level token added on the host must replicate to the client.");

            // Removal on the host -> client clears it (OnTokenRemoved) - the "never clears" case.
            hostStore.GetValue<ModelData>(modelRef).Tokens.RemoveTokens(TokenType.Fatigued);
            Assert.That(clientStore.GetValue<ModelData>(modelRef).Tokens.HasToken(TokenType.Fatigued), Is.False,
                "A token removed on the host must clear on the client (the stale-Fatigued field report).");
        }

        // Wires two networked message buses to each other with a synchronous in-process loopback that
        // delivers each serialized frame straight to the other side's OnMessageReceived - no TCP, no
        // threads, so the whole request/response completes inline.
        private static (MessageBusHost_Networked host, MessageBusClient_Networked client) ConnectLoopback(
            IReadableGameDataStore hostStore, IReadableGameDataStore clientStore)
        {
            var host = new LoopbackHost();
            var client = new LoopbackClient();
            host.Client = client;
            client.Host = host;
            return (new MessageBusHost_Networked(host, hostStore),
                    new MessageBusClient_Networked(client, clientStore));
        }

        private static ArraySegment<byte> Copy(ArraySegment<byte> data) =>
            new ArraySegment<byte>(data.ToArray());

        private sealed class LoopbackHost : INetworkHost
        {
            public static readonly ConnectionID ClientId = new ConnectionID(Guid.NewGuid());

            public LoopbackClient? Client;

            public event Action<ConnectionID>? OnNewClientConnected;
            public event Action<ConnectionID>? OnClientDisconnected;
            public event Action<ArraySegment<byte>, ConnectionID>? OnMessageReceived;

            public Task StartAsync() => Task.CompletedTask;

            public Task SendCommandToAllAsync(ArraySegment<byte> data, bool isPooled)
            {
                Client!.Deliver(Copy(data));
                return Task.CompletedTask;
            }

            public Task SendCommandToSingleClientAsync(ConnectionID clientId, ArraySegment<byte> data, bool isPooled)
            {
                Client!.Deliver(Copy(data));
                return Task.CompletedTask;
            }

            public void DisconnectClient(ConnectionID clientId) { }

            public void MarkClientAuthenticated(ConnectionID clientId) { }

            public void Stop() { }

            internal void ReceiveFromClient(ArraySegment<byte> data) =>
                OnMessageReceived?.Invoke(data, ClientId);
        }

        private sealed class LoopbackClient : INetworkClient
        {
            public LoopbackHost? Host;

            public event Action<ArraySegment<byte>>? OnMessageReceived;
            public event Action? OnDisconnected;

            public Task<bool> ConnectAsync(IPAddress serverIP, int port = NetworkProtocol.DefaultPort) => Task.FromResult(true);

            public Task SendCommandToHost(ArraySegment<byte> command, bool isPooled)
            {
                Host!.ReceiveFromClient(Copy(command));
                return Task.CompletedTask;
            }

            public void Disconnect() => OnDisconnected?.Invoke();

            internal void Deliver(ArraySegment<byte> data) => OnMessageReceived?.Invoke(data);
        }
    }
}
