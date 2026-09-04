using FDG.Ai;
using FDG.Ai.Tactician;
using FDG.Data;
using FDG.GameModel;
using FDG.Players;
using FDG.SaveLoad;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.Utilities;

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

            /// <summary>
            /// #191 B2 (docs/tactician-b2-design.md sec 4.3): per activation of the line, whether its
            /// prescription was CONSUMED by the policy. A natural (unprescribed) activation is true.
            /// False means 5b's G3 fall-through happened - the policy scored its own choice - so the
            /// activation was NOT the one the edge named, and search must close that edge rather than
            /// credit it. Has one entry per activation actually run.
            /// </summary>
            public IReadOnlyList<bool> Honored { get; init; } = Array.Empty<bool>();

            /// <summary>The player about to activate at the line's first boundary (the snapshot's own).</summary>
            public PlayerID? ActingPlayerAtStart { get; init; }

            /// <summary>
            /// The player about to activate at the boundary the line stopped at - the child node's
            /// acting player, read from the engine's own determination (P19 overrides and reactivations
            /// included) rather than inferred from the parent. Null when the game ended first.
            /// </summary>
            public PlayerID? ActingPlayerAtEnd { get; init; }
        }

        /// <summary>
        /// What a <see cref="ILineDriver"/> sees at one activation boundary (#191 B2 sec 4.4).
        /// <see cref="State"/> is the LIVE table state of the simulated game: a driver that is about to
        /// stop evaluates the position here, before the line's one Save - no serialization is spent
        /// on evaluation. It must only be READ (the encoder and evaluators are pure), and it must not
        /// be retained past the call.
        /// </summary>
        public sealed record LineBoundary(int Index, PlayerID ActingPlayer, ITableState State,
            bool? PreviousHonored)
        {
            /// <summary>
            /// The decision the previous boundary's policy took, as a prescription that reproduces it
            /// (unit, first action, macro) - null at index 0 or for a non-planning policy. What a
            /// recorded natural line replays as a fully prescribed one (the B2 cost measurement), and
            /// what a behavior-cloning exporter would read.
            /// </summary>
            public Prescription? PreviousDecision { get; init; }
        }

        /// <summary>A driver's answer at a boundary: prescribe, play naturally, or stop here.</summary>
        public readonly record struct LineStep(bool IsStop, Prescription? Prescription)
        {
            /// <summary>Let the policy score its own activation.</summary>
            public static LineStep Natural => new(false, null);

            /// <summary>Stop at this boundary: the snapshot captured here is the line's result.</summary>
            public static LineStep Stop => new(true, null);

            public static LineStep Prescribe(Prescription prescription) => new(false, prescription);
        }

        /// <summary>
        /// Supplies a line's decisions lazily, one boundary at a time (#191 B2 sec 4.4; 5c's recorded
        /// note 1): a tree hands out the next prescription as the line walks rather than as a fixed
        /// list, and evaluates the leaf in place at the boundary it stops at. The list-based
        /// <see cref="SimulationService.Run(string, IReadOnlyList{Prescription})"/> is this with a list.
        /// </summary>
        public interface ILineDriver
        {
            LineStep AtBoundary(LineBoundary boundary);
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
        /// returns the snapshot at the boundary after the last one. This is 5c's line, as a fixed list;
        /// it is the callback form below with a <see cref="ListLineDriver"/> (pinned byte-identical).
        /// </summary>
        public Task<SimulationResult> Run(string snapshot, IReadOnlyList<Prescription?> prescriptions)
        {
            if (prescriptions.Count == 0)
            {
                return Task.FromResult(new SimulationResult(snapshot, 0, null, "empty line - nothing to simulate"));
            }
            return Run(snapshot, new ListLineDriver(prescriptions));
        }

        /// <summary>
        /// Resumes the snapshot and stops at its very first boundary without playing anything: the
        /// engine's own answer to "who is about to activate here", for building a search root
        /// (#191 B2 sec 2). The returned snapshot is the engine's re-saved state at that boundary.
        /// </summary>
        public Task<SimulationResult> Probe(string snapshot) => Run(snapshot, new ProbeDriver());

        /// <summary>
        /// The callback line (#191 B2 sec 4.4): the driver is asked at every boundary, sees the live
        /// state, and says stop when the line is done. The snapshot is captured at the stop boundary,
        /// after the driver has looked.
        /// </summary>
        public async Task<SimulationResult> Run(string snapshot, ILineDriver driver)
        {
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
                // B5 (#191 step 9): search NEVER runs inside a simulation. A Strategist in-sim
                // policy would root a new tree at every boundary of every line of the tree above
                // it - unbounded recursion, not a deeper search - so it degrades to the A policy
                // it is built on. That is also the honest model of the opponent: the search
                // assumes the other side plays A, and says so.
                EAiProfile inSimProfile = _options.Profile == EAiProfile.Strategist
                    ? EAiProfile.Tactician
                    : _options.Profile;
                IStageResolverRegistry registry = AiProfileFactory.BuildRegistry(
                    inSimProfile, localGame.TableState, playerID, out TacticianPlanner? planner,
                    _options.Seed, slots[i].SlotID, decisionLog: null,
                    seeThroughFriendlyUnits: settings.SeeThroughFriendlyUnits);

                registriesByPlayer[playerID] = registry;
                plannersByPlayer[playerID] = planner;
                slots[i].AssignPlayerController(new SimulationPlayerController(
                    $"sim slot {i}", playerID, localGame, registry));
            }

            var captured = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var ended = new TaskCompletionSource<GameResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var line = new LineHook(driver, plannersByPlayer, store, new TableState(store), captured);

            var server = new FDGServer(store, bus, slots, presentationClock: null, lobbySettings: null,
                simulation: new SimulationHostOptions
                {
                    BoundaryHook = line,
                    PlayerRequester = _options.BypassBus
                        ? new DirectPlayerRequester(registriesByPlayer)
                        : null,
                });
            server.OnGameCompleted += result => ended.TrySetResult(result);

            // The watchdog timer is CANCELLED the moment the line settles (#191 B5 crash chase). A
            // simulation lasts ~100ms; an uncancelled 60s Task.Delay outlives it 600x, and through the
            // WhenAny wiring it keeps the captured snapshot string - hundreds of KB, on the large
            // object heap - rooted for the full minute. A search runs thousands of lines a minute, so
            // that was gigabytes of dead-but-rooted strings churning through the GC at any moment.
            using var watchdog = new CancellationTokenSource();
            Task finished = await Task.WhenAny(captured.Task, ended.Task,
                Task.Delay(TimeSpan.FromSeconds(_options.TimeoutSeconds), watchdog.Token));
            watchdog.Cancel();

            if (finished == captured.Task)
            {
                return new SimulationResult(await captured.Task, line.ActivationsRun, null,
                    $"line of {line.ActivationsRun} complete")
                {
                    Honored = line.Honored,
                    ActingPlayerAtStart = line.ActingPlayerAtStart,
                    ActingPlayerAtEnd = line.ActingPlayerAtEnd,
                };
            }

            if (finished == ended.Task)
            {
                GameResult result = await ended.Task;
                line.SettleAfterGameEnd();
                return new SimulationResult(null, line.ActivationsRun, result,
                    $"game ended after {line.ActivationsRun} activation(s): {result.Outcome}")
                {
                    Honored = line.Honored,
                    ActingPlayerAtStart = line.ActingPlayerAtStart,
                };
            }

            return new SimulationResult(null, line.ActivationsRun, null,
                $"timed out after {_options.TimeoutSeconds}s")
            {
                Honored = line.Honored,
                ActingPlayerAtStart = line.ActingPlayerAtStart,
            };
        }

        /// <summary>
        /// A prescribed <see cref="MacroAction"/> was enumerated on ANOTHER store of the same game (the
        /// search's scratch load of the parent snapshot, #191 B2 sec 3.2), and its move entries bind
        /// models of that store. The movement resolver hands those entries to the engine, which
        /// applies the move THROUGH the bindings - onto the wrong store, silently, while this game's
        /// models never move (found by B2's determinism pin: two runs of one edge diverged because
        /// the first had moved the scratch models). So every prescribed macro is rebound onto this
        /// simulation's store first: model bindings by <see cref="DataReference"/> (stable across
        /// every store of the game), positions copied, targets re-resolved by unit ID / marker position
        /// (they are only read for logging downstream, but a macro must never leak a foreign store).
        /// </summary>
        internal static MacroAction Rebind(MacroAction macro, GameDataStore store)
        {
            var move = new List<ModelMoveEntry>(macro.Move.Count);
            foreach (ModelMoveEntry entry in macro.Move)
            {
                move.Add(new ModelMoveEntry(store.GetDataBinding<ModelData>(entry.Model.Reference),
                    new List<Position>(entry.Positions),
                    entry.Facings == null ? null : new List<Float2>(entry.Facings)));
            }

            IUnit? enemy = macro.TargetEnemy == null ? null : FindUnit(store, macro.TargetEnemy.ID);
            IUnit? ally = macro.TargetAlly == null ? null : FindUnit(store, macro.TargetAlly.ID);
            IObjective? objective = macro.TargetObjective == null ? null
                : store.GetAllValues<ObjectiveData>().FirstOrDefault(o =>
                    o.Position.x == macro.TargetObjective.Position.x && o.Position.z == macro.TargetObjective.Position.z);

            return macro with
            {
                Move = move,
                TargetEnemy = enemy,
                TargetAlly = ally,
                TargetObjective = objective,
            };
        }

        private static IUnit? FindUnit(GameDataStore store, UnitID id)
        {
            foreach (UnitData unit in store.GetAllValues<UnitData>())
                if (unit.ID.Equals(id)) return unit;
            return null;
        }

        /// <summary>The fixed-list line: prescription i at boundary i, stop at boundary Count.</summary>
        private sealed class ListLineDriver : ILineDriver
        {
            private readonly IReadOnlyList<Prescription?> _prescriptions;
            public ListLineDriver(IReadOnlyList<Prescription?> prescriptions) => _prescriptions = prescriptions;

            public LineStep AtBoundary(LineBoundary boundary)
            {
                if (boundary.Index >= _prescriptions.Count) return LineStep.Stop;
                Prescription? prescription = _prescriptions[boundary.Index];
                return prescription == null ? LineStep.Natural : LineStep.Prescribe(prescription);
            }
        }

        private sealed class ProbeDriver : ILineDriver
        {
            public LineStep AtBoundary(LineBoundary boundary) => LineStep.Stop;
        }

        /// <summary>
        /// Walks the line: asks the driver at each boundary, applies a prescription to the ACTING
        /// player's policy, reports at the next boundary whether the previous one was honored, and
        /// at the stop boundary captures the snapshot and throw-stops the game.
        /// </summary>
        private sealed class LineHook : IActivationBoundaryHook
        {
            private readonly ILineDriver _driver;
            private readonly IReadOnlyDictionary<PlayerID, TacticianPlanner?> _planners;
            private readonly GameDataStore _store;
            private readonly ITableState _tableState;
            private readonly TaskCompletionSource<string> _captured;
            private readonly List<bool> _honored = new();
            private int _boundariesSeen;
            private PlayerID? _previousActing;
            private bool _previousPrescribed;

            /// <summary>Activations STARTED by the line (the old list semantics): boundaries seen, less the stop.</summary>
            public int ActivationsRun => _stopped ? _boundariesSeen - 1 : _boundariesSeen;
            public IReadOnlyList<bool> Honored => _honored;
            private bool _stopped;
            public PlayerID? ActingPlayerAtStart { get; private set; }
            public PlayerID? ActingPlayerAtEnd { get; private set; }

            public LineHook(ILineDriver driver, IReadOnlyDictionary<PlayerID, TacticianPlanner?> planners,
                GameDataStore store, ITableState tableState, TaskCompletionSource<string> captured)
            {
                _driver = driver;
                _planners = planners;
                _store = store;
                _tableState = tableState;
                _captured = captured;
            }

            public Task AtActivationBoundary(PlayerID actingPlayer)
            {
                int index = _boundariesSeen++;
                if (index == 0) ActingPlayerAtStart = actingPlayer;

                // The previous activation is over: settle its honored flag from its policy. A natural
                // activation is honored by definition; a prescribed one asks the planner (5b's
                // fall-through leaves LastPrescriptionHonored false). Non-planning profiles have no
                // planner and can consume no prescription, so a prescription to them is unhonored.
                bool? previousHonored = null;
                if (index > 0)
                {
                    previousHonored = !_previousPrescribed
                        || (_previousActing.HasValue
                            && _planners.TryGetValue(_previousActing.Value, out TacticianPlanner? previous)
                            && previous?.LastPrescriptionHonored == true);
                    _honored.Add(previousHonored.Value);
                    if (_previousActing.HasValue
                        && _planners.TryGetValue(_previousActing.Value, out TacticianPlanner? clear))
                    {
                        clear?.ClearPrescription();
                    }
                }

                Prescription? previousDecision = null;
                if (index > 0 && _previousActing.HasValue
                    && _planners.TryGetValue(_previousActing.Value, out TacticianPlanner? decided)
                    && decided?.ActiveUnit != null && decided.LastAction != null)
                {
                    previousDecision = new Prescription(decided.ActiveUnit.Reference, decided.LastAction, decided.LastMacro);
                }

                LineStep step = _driver.AtBoundary(new LineBoundary(index, actingPlayer, _tableState, previousHonored)
                {
                    PreviousDecision = previousDecision,
                });

                if (step.IsStop)
                {
                    _stopped = true;
                    // This boundary IS the result state. Serialize here, where the engine's own rolling
                    // save point has just written the flow state, then throw-stop so nothing further
                    // mutates the store.
                    ActingPlayerAtEnd = actingPlayer;
                    _captured.TrySetResult(GameSaveSerializer.Save(_store));
                    throw new SimulationStopSignal();
                }

                _previousActing = actingPlayer;
                _previousPrescribed = step.Prescription != null;
                if (step.Prescription is { } prescription
                    && _planners.TryGetValue(actingPlayer, out TacticianPlanner? planner) && planner != null)
                {
                    // Matched by DataReference: the caller's binding comes from a different store
                    // instance of the same game (5b's seam handles the lookup on the engine side).
                    DataBinding<UnitData>? unit = prescription.Unit.HasValue
                        ? _store.GetDataBinding<UnitData>(prescription.Unit.Value)
                        : null;
                    planner.Prescribe(unit, prescription.Action,
                        prescription.Macro == null ? null : Rebind(prescription.Macro, _store));
                }

                return Task.CompletedTask;
            }

            /// <summary>
            /// The game ended inside the last started activation, so no later boundary settled its
            /// flag: settle it now from the policy, so Honored always has one entry per activation.
            /// </summary>
            public void SettleAfterGameEnd()
            {
                if (_stopped || _honored.Count >= _boundariesSeen || _boundariesSeen == 0) return;
                bool honored = !_previousPrescribed
                    || (_previousActing.HasValue
                        && _planners.TryGetValue(_previousActing.Value, out TacticianPlanner? previous)
                        && previous?.LastPrescriptionHonored == true);
                _honored.Add(honored);
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
