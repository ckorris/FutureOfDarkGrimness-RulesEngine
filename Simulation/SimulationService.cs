using FDG.Ai;
using FDG.Ai.Tactician;
using FDG.Data;
using FDG.GameModel;
using FDG.Players;
using FDG.SaveLoad;
using FDG.StageResolution;

namespace FDG.Simulation
{
    /// <summary>
    /// Phase B's simulation primitive (#191 B1, campaign step 5): resume a snapshot in-process, play
    /// a LINE of activations under prescribed decisions, and hand back the snapshot at the end of
    /// the line. This is B0Spike's <c>BuildResumedServer</c> productionized, plus 5c's pause/step
    /// hook and bus bypass.
    ///
    /// <para><b>Why a line and not a clone per node.</b> B0 measured a 2k node expansion at 223ms:
    /// 165ms of policy thinking, 54ms of load+save, 3ms of assembly. Step 5b removed the policy
    /// thinking for a prescribed activation (the planner consumes the decision instead of scoring
    /// it). What is left is dominated by cloning the game once per activation - which a search does
    /// not need, because the activations along one path of the tree are consecutive. <see
    /// cref="Run"/> plays them in ONE game instance, pausing at each boundary
    /// (<see cref="IActivationBoundaryHook"/>) for the next prescription, and serializes only at the
    /// end. The caller snapshots only where its tree actually branches.</para>
    ///
    /// <para><b>Depth is a parameter, never 1.</b> The line length is <c>prescriptions.Count</c>, so
    /// multi-ply is the same call as single-ply. If the per-activation cost disappoints, B ships
    /// shallow by passing shorter lines - no rewrite.</para>
    ///
    /// <para><b>Stopping.</b> The end of a line throws <see cref="SimulationStopSignal"/> from the
    /// hook, which unwinds the state machine into FDGServer's own catch and completes the game.
    /// That is B0's throw-stop, measured 30/30 with zero heap growth over 400 simulations at 4k;
    /// ABANDON is not used.</para>
    ///
    /// <para><b>Dice.</b> A simulation defaults to <see cref="ERandomnessType.Probabilistic"/> -
    /// expected-value combat with real, seeded draws for the decisive rolls (morale, objective
    /// count, dangerous terrain). The threshold-shift invariant lives in the dice roller itself;
    /// this class only chooses the mode and the seed, and never adjusts a roll after the fact.</para>
    /// </summary>
    public sealed class SimulationService
    {
        /// <summary>
        /// One activation's decision, as the search's tree edge carries it. Prescribing goes THROUGH
        /// the policy (step 5b): the unit is matched by <see cref="DataReference"/>, so a caller may
        /// name a unit from any store of the same game.
        /// <para>
        /// A null <see cref="Unit"/> or <see cref="Action"/> leaves that level unprescribed and the
        /// policy scores its own choice, so a list of nulls is simply natural play for N activations
        /// (see <see cref="RunNatural"/>). <see cref="Macro"/> is required alongside a plan-bearing
        /// action - see <see cref="TacticianPlanner.Prescribe"/>.
        /// </para>
        /// </summary>
        public sealed record Prescription(DataReference? Unit, string? Action = null,
            MacroAction? Macro = null);

        /// <summary>Per-simulation knobs. The seed is the simulation's own, not the parent game's.</summary>
        public sealed record SimulationOptions
        {
            /// <summary>The policy every slot plays under inside the simulation.</summary>
            public EAiProfile Profile { get; init; } = EAiProfile.Tactician;

            /// <summary>This simulation's RNG seed - independent per simulation, so runs are reproducible (G5).</summary>
            public int Seed { get; init; }

            public ERandomnessType Randomness { get; init; } = ERandomnessType.Probabilistic;

            /// <summary>Wall-clock guard so a wedged simulation can never hang a search.</summary>
            public int TimeoutSeconds { get; init; } = 60;

            /// <summary>
            /// 5c's bus bypass: answer decisions straight from the target slot's registry instead of
            /// serializing them onto the message bus (<see cref="DirectPlayerRequester"/>). On by
            /// default - it is the whole point of the seam. Settable so the equivalence pin can run
            /// the SAME line both ways and assert they agree, and so a debugger can put a simulation
            /// back on the wire path without touching the engine.
            /// </summary>
            public bool BypassBus { get; init; } = true;
        }

        /// <summary>
        /// The snapshot at the end of the line, or why there isn't one. <see cref="EndedEarly"/> is
        /// set when the game reached its natural end before the line did - a legitimate outcome
        /// (the search has found a terminal node), not a fault.
        /// </summary>
        public sealed record SimulationResult(string? Snapshot, int ActivationsRun,
            GameResult? EndedEarly, string Note)
        {
            public bool ReachedEndOfLine => Snapshot != null;
        }

        private readonly SimulationOptions _options;

        public SimulationService(SimulationOptions? options = null) =>
            _options = options ?? new SimulationOptions();

        /// <summary>Serializes a live game's store - the snapshot every other call here takes.</summary>
        public static string Snapshot(GameDataStore store) => GameSaveSerializer.Save(store);

        /// <summary>One activation under one prescription: the node-expansion primitive.</summary>
        public Task<SimulationResult> Advance(string snapshot, Prescription? prescription) =>
            Run(snapshot, new[] { prescription });

        /// <summary>N consecutive activations with no prescriptions - the policy plays itself.</summary>
        public Task<SimulationResult> RunNatural(string snapshot, int activations) =>
            Run(snapshot, new Prescription?[activations]);

        /// <summary>
        /// Plays <c>prescriptions.Count</c> consecutive activations in ONE resumed game instance and
        /// returns the snapshot at the boundary after the last one. This is 5c's line.
        /// </summary>
        public async Task<SimulationResult> Run(string snapshot, IReadOnlyList<Prescription?> prescriptions)
        {
            if (prescriptions.Count == 0)
            {
                return new SimulationResult(snapshot, 0, null, "empty line - nothing to simulate");
            }

            GameDataStore store = GameSaveSerializer.Load(snapshot);

            GameProgressData? progress = GameProgressUtilities.TryGetProgress(store)
                ?? throw new InvalidOperationException(
                    "Tried to simulate from a snapshot that carries no GameProgressData.");

            // The simulation owns its randomness: its own seed and (by default) probabilistic dice,
            // written back onto the progress record so the resumed server reads them as its settings.
            GameSettings settings = progress.Settings;
            settings.DiceSeed = _options.Seed;
            settings.RandomnessType = _options.Randomness;
            progress.Settings = settings;
            GameProgressUtilities.WriteProgress(store, progress);

            List<PlayerSlotInfo> savedInfos = store.GetAllValues<PlayerSlotInfo>()
                .OrderBy(info => info.SlotID).ToList();
            if (savedInfos.Count == 0)
            {
                throw new InvalidOperationException("Tried to simulate from a snapshot with no player slots.");
            }

            // The slots are rebuilt on the SAVED PlayerIDs, so prescriptions and objective ownership
            // still name the same players. Re-creating a slot writes a fresh PlayerSlotInfo, so the
            // loaded ones are destroyed first (they are per-session crew, not world state).
            foreach (DataReference oldInfo in store.GetAllDataReferences<PlayerSlotInfo>().ToList())
            {
                store.Destroy(oldInfo);
            }

            var bus = new SimulationMessageBus();
            var slots = new PlayerSlot[savedInfos.Count];
            var registriesByPlayer = new Dictionary<PlayerID, IStageResolverRegistry>();
            var plannersByPlayer = new Dictionary<PlayerID, TacticianPlanner?>();

            for (int i = 0; i < savedInfos.Count; i++)
            {
                PlayerID playerID = savedInfos[i].PlayerID;
                slots[i] = new PlayerSlot(i, savedInfos[i].TeamNumber, playerID, new ArmyListFile(), store);

                var localGame = new FDGGame_AsLocal(store, bus);
                IStageResolverRegistry registry = AiProfileFactory.BuildRegistry(
                    _options.Profile, localGame.TableState, playerID, out TacticianPlanner? planner,
                    _options.Seed, slots[i].SlotID, decisionLog: null,
                    seeThroughFriendlyUnits: settings.SeeThroughFriendlyUnits);

                registriesByPlayer[playerID] = registry;
                plannersByPlayer[playerID] = planner;
                slots[i].AssignPlayerController(new SimulationPlayerController(
                    $"sim slot {i}", playerID, localGame, registry));
            }

            var captured = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var ended = new TaskCompletionSource<GameResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var line = new LineDriver(prescriptions, plannersByPlayer, store, captured);

            var server = new FDGServer(store, bus, slots, presentationClock: null, lobbySettings: null,
                simulation: new SimulationHostOptions
                {
                    BoundaryHook = line,
                    PlayerRequester = _options.BypassBus
                        ? new DirectPlayerRequester(registriesByPlayer)
                        : null,
                });
            server.OnGameCompleted += result => ended.TrySetResult(result);

            Task finished = await Task.WhenAny(captured.Task, ended.Task,
                Task.Delay(TimeSpan.FromSeconds(_options.TimeoutSeconds)));

            if (finished == captured.Task)
            {
                return new SimulationResult(await captured.Task, line.ActivationsRun, null,
                    $"line of {prescriptions.Count} complete");
            }

            if (finished == ended.Task)
            {
                GameResult result = await ended.Task;
                return new SimulationResult(null, line.ActivationsRun, result,
                    $"game ended after {line.ActivationsRun} activation(s): {result.Outcome}");
            }

            return new SimulationResult(null, line.ActivationsRun, null,
                $"timed out after {_options.TimeoutSeconds}s");
        }

        /// <summary>
        /// Walks the line: applies each boundary's prescription to the ACTING player's policy, and
        /// at the boundary after the last one captures the snapshot and stops the game.
        /// </summary>
        private sealed class LineDriver : IActivationBoundaryHook
        {
            private readonly IReadOnlyList<Prescription?> _prescriptions;
            private readonly IReadOnlyDictionary<PlayerID, TacticianPlanner?> _planners;
            private readonly GameDataStore _store;
            private readonly TaskCompletionSource<string> _captured;
            private int _boundariesSeen;

            public int ActivationsRun => Math.Min(_boundariesSeen, _prescriptions.Count);

            public LineDriver(IReadOnlyList<Prescription?> prescriptions,
                IReadOnlyDictionary<PlayerID, TacticianPlanner?> planners, GameDataStore store,
                TaskCompletionSource<string> captured)
            {
                _prescriptions = prescriptions;
                _planners = planners;
                _store = store;
                _captured = captured;
            }

            public Task AtActivationBoundary(PlayerID actingPlayer)
            {
                int index = _boundariesSeen++;

                // One past the end of the line: this boundary IS the result state. Serialize here,
                // where the engine's own rolling save point has just written the flow state, then
                // throw-stop so nothing further mutates the store.
                if (index >= _prescriptions.Count)
                {
                    _captured.TrySetResult(GameSaveSerializer.Save(_store));
                    throw new SimulationStopSignal();
                }

                Prescription? prescription = _prescriptions[index];
                if (prescription == null)
                {
                    return Task.CompletedTask; // Natural activation - the policy scores its own.
                }

                if (_planners.TryGetValue(actingPlayer, out TacticianPlanner? planner) && planner != null)
                {
                    // Matched by DataReference: the caller's binding comes from a different store
                    // instance of the same game (5b's seam handles the lookup on the engine side).
                    DataBinding<UnitData>? unit = prescription.Unit.HasValue
                        ? _store.GetDataBinding<UnitData>(prescription.Unit.Value)
                        : null;
                    planner.Prescribe(unit, prescription.Action, prescription.Macro);
                }

                return Task.CompletedTask;
            }
        }

        /// <summary>
        /// The minimal AI controller a simulated slot needs: ready immediately, no log sink, no
        /// presentation. (FdgLab's LabPlayerController with the logging removed.)
        /// </summary>
        private sealed class SimulationPlayerController : IPlayerController
        {
            public string Name { get; }
            public PlayerID ID { get; }
            public bool IsReady => true;
            public Presentation.IPresentationSink? PresentationSink => null;

#pragma warning disable CS0067 // A simulated player never changes readiness or chats.
            public event Action<bool>? OnReadyStateChanged;
            public event Action<PlayerID, EChatMessageType, string>? OnMessageSentByPlayer;
#pragma warning restore CS0067

            public SimulationPlayerController(string name, PlayerID id, FDGGame_AsLocal localGame,
                IStageResolverRegistry registry)
            {
                Name = name;
                ID = id;
                localGame.AddLocalPlayerID(id);
                localGame.AssignInterfaces(null, null, registry, null, null);
            }

            public Task WaitUntilReadyAsync() => Task.CompletedTask;
            public void SendLogMessage(string logMessage, TextColor color) { }
            public void SendPlayerMessage(string sendingPlayerName, EChatMessageType messageType, string message) { }
        }
    }
}
