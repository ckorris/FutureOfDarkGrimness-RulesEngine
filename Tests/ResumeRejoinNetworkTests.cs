using System.Net;
using System.Net.Sockets;
using FDG.Data;
using FDG.Network.Connection;
using FDG.Network.Connection.Lobby;
using FDG.Players;
using FDG.SaveLoad;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// #187 — the rejoin half: a player who dropped out of a networked game reconnects to the host's
    /// resumed save and gets their OWN slot back. Until now `OnResumeClientGreeting` was correct-by-design
    /// but never exercised, which is exactly the code a recovery save depends on.
    ///
    /// <para>Unlike the other lobby tests (which drive the view models over in-process loopback doubles),
    /// these run the REAL transport: an <see cref="FDGHost"/> listening on 127.0.0.1 and real
    /// <see cref="FDGClient"/>s connecting to it, so the greeting handshake, framing, and per-connection
    /// PlayerID targeting are all in the path. That makes them the first tests over actual sockets in this
    /// suite (#065) - hence the generous timeouts and the teardown discipline below.</para>
    ///
    /// <para>What they do NOT cover: launching the resumed game and playing on. That needs both clients to
    /// stand up resolver registries and report post-launch ready, and is the two-machine hand-verify
    /// recorded in the work item.</para>
    /// </summary>
    [TestFixture]
    public class ResumeRejoinNetworkTests
    {
        private static readonly TimeSpan JoinTimeout = TimeSpan.FromSeconds(20);

        private readonly List<IDisposable> _disposables = new();
        private readonly List<FDGHost> _hosts = new();
        private readonly List<FDGClient> _clients = new();

        [TearDown]
        public void TearDown()
        {
            // Sockets outlive a failed assertion, and a listener left running would make the next test's
            // port pick flaky. Tear down in join order: view models first (they deregister bus handlers),
            // then connections.
            foreach (IDisposable disposable in _disposables) TryQuietly(disposable.Dispose);
            foreach (FDGClient client in _clients) TryQuietly(client.Disconnect);
            foreach (FDGHost host in _hosts) TryQuietly(host.Stop);

            _disposables.Clear();
            _clients.Clear();
            _hosts.Clear();
        }

        // The 1v1 case: one saved AI slot, one returning player. They must come back as THEMSELVES - the
        // save's PlayerID - or the resumed game hands them someone else's army.
        [Test]
        [CancelAfter(120_000)]
        public async Task RejoiningClient_AdoptsTheSavedSlotsPlayerID()
        {
            (LobbyViewModel_Host hostVm, IReadOnlyList<PlayerSlotInfo> savedSlots) = StandUpResumeHost(playerCount: 2);
            PlayerID droppedPlayerSlot = savedSlots[1].PlayerID; // slot 0 is the host itself

            LobbyViewModel_Client clientVm = await JoinAsync("Mrs. Client", hostVm);

            Assert.That(clientVm.CheckCanModifyPlayerIDInfo(droppedPlayerSlot), Is.True,
                "the rejoining client must adopt the SAVED PlayerID, not a freshly minted one.");

            LobbyPlayerInfoSummary slot = await WaitForSlot(hostVm, droppedPlayerSlot);
            Assert.That(slot.PlayerType, Is.EqualTo(EPlayerType.Network),
                "the host must hand the saved slot over to the connected client (it was standing in as AI).");
            Assert.That(slot.PlayerName, Is.EqualTo("Mrs. Client"), "the slot takes the returning player's name.");
            Assert.That(hostVm.PlayerInfos.Count, Is.EqualTo(savedSlots.Count),
                "a rejoin fills a saved slot - it must not add a roster row.");
        }

        // The case the auto-fill was flagged as unverified for: more than one remote player coming back.
        // Each must land on a DIFFERENT saved slot, and neither may be handed the other's PlayerID (the
        // failure the single-connection ID assignment of QF5 exists to prevent).
        [Test]
        [CancelAfter(120_000)]
        public async Task TwoRejoiningClients_TakeDistinctSavedSlots()
        {
            (LobbyViewModel_Host hostVm, IReadOnlyList<PlayerSlotInfo> savedSlots) = StandUpResumeHost(playerCount: 3);
            PlayerID slotB = savedSlots[1].PlayerID;
            PlayerID slotC = savedSlots[2].PlayerID;

            LobbyViewModel_Client first = await JoinAsync("First Back", hostVm);
            LobbyViewModel_Client second = await JoinAsync("Second Back", hostVm);

            Assert.That(first.CheckCanModifyPlayerIDInfo(slotB), Is.True,
                "the first client back takes the first open saved slot.");
            Assert.That(second.CheckCanModifyPlayerIDInfo(slotC), Is.True,
                "the second client back takes the next one.");
            Assert.That(second.CheckCanModifyPlayerIDInfo(slotB), Is.False,
                "the second client must NOT also believe it owns the first client's slot.");

            LobbyPlayerInfoSummary rowB = await WaitForSlot(hostVm, slotB);
            LobbyPlayerInfoSummary rowC = await WaitForSlot(hostVm, slotC);
            Assert.That(rowB.PlayerType, Is.EqualTo(EPlayerType.Network));
            Assert.That(rowC.PlayerType, Is.EqualTo(EPlayerType.Network));
            Assert.That(rowB.PlayerName, Is.EqualTo("First Back"));
            Assert.That(rowC.PlayerName, Is.EqualTo("Second Back"));
            Assert.That(hostVm.PlayerInfos.Count, Is.EqualTo(savedSlots.Count));
        }

        // The host still holds slot 0 itself, and no rejoin may take it away.
        [Test]
        [CancelAfter(120_000)]
        public async Task RejoiningClient_DoesNotTakeTheHostsOwnSlot()
        {
            (LobbyViewModel_Host hostVm, IReadOnlyList<PlayerSlotInfo> savedSlots) = StandUpResumeHost(playerCount: 2);
            PlayerID hostSlot = savedSlots[0].PlayerID;

            LobbyViewModel_Client clientVm = await JoinAsync("Mrs. Client", hostVm);

            Assert.That(clientVm.CheckCanModifyPlayerIDInfo(hostSlot), Is.False,
                "the returning client must not be handed the host's own saved slot.");
            Assert.That(hostVm.PlayerInfos.Single(p => p.PlayerID == hostSlot).PlayerType,
                Is.EqualTo(EPlayerType.Local), "the host keeps playing its own slot.");
        }

        // Builds a resume-mode host lobby around a real loaded save, listening on a free loopback port.
        private (LobbyViewModel_Host Host, IReadOnlyList<PlayerSlotInfo> SavedSlots) StandUpResumeHost(int playerCount)
        {
            // Round-tripped through the serializer so this is a genuinely loaded save, not a live store.
            GameDataStore compiled = ScenarioCompiler.Compile(MakeScenario(playerCount), MakeArmies(playerCount));
            GameDataStore loaded = GameSaveSerializer.Load(GameSaveSerializer.Save(compiled));

            List<PlayerSlotInfo> savedSlots = loaded.GetAllValues<PlayerSlotInfo>()
                .OrderBy(slot => slot.SlotID).ToList();
            Assert.That(savedSlots.Count, Is.EqualTo(playerCount), "the save must carry every player's slot.");

            int port = FreeLoopbackPort();
            var host = new FDGHost(port: port);
            _hosts.Add(host);

            // StartAsync IS the accept loop - awaiting it would block until the host stops.
            Task listening = host.StartAsync();
            Assert.That(listening.IsFaulted, Is.False,
                $"host failed to bind: {listening.Exception?.GetBaseException().Message}");

            var hostVm = new LobbyViewModel_Host("Mr. Host", "Recovered Game", "", host, loaded);
            _disposables.Add(hostVm);
            _portForHost[hostVm] = port;
            return (hostVm, savedSlots);
        }

        // Connects a real client and waits out the greeting handshake, as ClientModal does.
        private async Task<LobbyViewModel_Client> JoinAsync(string playerName, LobbyViewModel_Host hostVm)
        {
            var client = new FDGClient();
            _clients.Add(client);

            bool connected = await client.ConnectAsync(IPAddress.Loopback, PortOf(hostVm));
            Assert.That(connected, Is.True, $"{playerName} could not connect to the host.");

            // The greeting goes out from the constructor; the assignment coming back completes the join.
            var clientVm = new LobbyViewModel_Client(playerName, client, "");
            _disposables.Add(clientVm);

            Task<string?> joinTask = clientVm.JoinResultTask;
            Task winner = await Task.WhenAny(joinTask, Task.Delay(JoinTimeout));
            Assert.That(winner, Is.SameAs(joinTask), $"{playerName}'s join handshake did not complete.");
            Assert.That(await joinTask, Is.Null, $"{playerName} was rejected by the host.");

            return clientVm;
        }

        // The host applies the slot handover before it answers the greeting, so this is settled by the time
        // a join completes - but the roster arrives on the read-loop thread, so poll rather than assume.
        private static async Task<LobbyPlayerInfoSummary> WaitForSlot(LobbyViewModel_Host hostVm, PlayerID playerID)
        {
            for (int attempt = 0; attempt < 100; attempt++)
            {
                LobbyPlayerInfoSummary? row = hostVm.PlayerInfos.FirstOrDefault(p => p.PlayerID == playerID);
                if (row != null && row.PlayerType == EPlayerType.Network) return row;
                await Task.Delay(50);
            }

            Assert.Fail($"the host never handed slot {playerID} to a connected client.");
            throw new InvalidOperationException("unreachable");
        }

        private readonly Dictionary<LobbyViewModel_Host, int> _portForHost = new();

        private int PortOf(LobbyViewModel_Host hostVm) => _portForHost[hostVm];

        // Ask the OS for a free port, then hand it to FDGHost. There is a small window between releasing
        // it here and FDGHost binding it; on a loopback-only test machine that is not worth a retry loop,
        // and a collision surfaces as an explicit bind failure rather than a silent pass.
        private int FreeLoopbackPort()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            int port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
        }

        private static void TryQuietly(Action action)
        {
            try { action(); } catch { /* teardown must not mask the real failure */ }
        }

        private static ArmyListFile[] MakeArmies(int playerCount) =>
            Enumerable.Range(0, playerCount).Select(i => new ArmyListFile
            {
                Name = $"Army {i}",
                Units = new()
                {
                    new UnitFileEntry
                    {
                        Name = "Troops", ModelCount = 3, Quality = 4, Defense = 4,
                        Weapons = new() { new WeaponFileEntry { Name = "Rifle", RangeInches = 24, Attacks = 1 } },
                    },
                },
            }).ToArray();

        private static ScenarioFile MakeScenario(int playerCount) => new ScenarioFile
        {
            Name = "Rejoin after a drop",
            Round = 2,
            ActivePlayer = 0,
            Settings = new ScenarioSettings { Randomness = "Realistic", DiceSeed = 5150 },
            Players = Enumerable.Range(0, playerCount).Select(i => new ScenarioPlayer
            {
                Army = $"army{i}.fdgarmy",
                Units = new()
                {
                    new ScenarioUnit
                    {
                        Unit = "Troops",
                        Models = new()
                        {
                            new[] { 20f + i * 10, 20f },
                            new[] { 21f + i * 10, 20f },
                            new[] { 22f + i * 10, 20f },
                        },
                    },
                },
            }).ToList(),
        };
    }
}
