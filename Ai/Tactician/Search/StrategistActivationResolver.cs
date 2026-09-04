using FDG.Data;
using FDG.Simulation;
using FDG.StageResolution;
using FDG.StageResolution.Requests;

namespace FDG.Ai.Tactician.Search
{
    /// <summary>
    /// B5 (#191 campaign step 9): the point where the search actually starts driving a game.
    /// <para>
    /// It does NOT answer the request itself. It roots a tree on the live position, takes the
    /// search's best root edge, and <see cref="TacticianPlanner.Prescribe"/>s it - then hands the
    /// request to the ordinary <see cref="Resolvers.TacticianActivationResolver"/>, which consumes
    /// the prescription exactly as a simulated line does (5b's finding: the activation choice IS
    /// the seam, and prescribing THROUGH the policy is what keeps the rest of the activation -
    /// movement geometry, targets, wounds - coherent instead of falling back to solo behavior).
    /// One <c>Prescribe</c> covers the whole activation: the unit half is consumed here, the action
    /// and macro halves survive <c>BeginActivation</c> and are consumed at Choose Action.
    /// </para>
    /// <para>
    /// <b>G3 is absolute here.</b> A search is a whole resumed engine and can fault; a real game
    /// cannot. Every failure path - no store to serialize, a faulted root probe, an exception from
    /// anywhere in the tree - falls through to the A policy, logged and counted, and the game
    /// continues. That is why the search result is never required to exist.
    /// </para>
    /// </summary>
    public sealed class StrategistActivationResolver
        : IStageResolver<ChooseUnitToActivateRequest, DataBinding<UnitData>>
    {
        private readonly ITableState _tableState;
        private readonly TacticianPlanner _planner;
        private readonly Resolvers.TacticianActivationResolver _policy;
        private readonly IPositionEvaluator _evaluator;
        private readonly UctOptions _options;
        private readonly Action<string>? _log;

        /// <summary>Activations where a search was attempted.</summary>
        public int Searches { get; private set; }

        /// <summary>Activations that fell back to the A policy (G3).</summary>
        public int Fallbacks { get; private set; }

        /// <summary>Total wall time spent searching, for the per-game cost line.</summary>
        public long SearchMs { get; private set; }

        /// <summary>
        /// The prescription the last successful search handed the planner, or null if that search
        /// fell back. Read by tests to pin that the SEARCH's choice is what the game activated;
        /// note <see cref="TacticianPlanner.LastPrescriptionHonored"/> cannot answer that here,
        /// because it also waits on the action half, which Choose Action consumes later.
        /// </summary>
        public SimulationService.Prescription? LastPrescription { get; private set; }

        public StrategistActivationResolver(ITableState tableState, TacticianPlanner planner,
            Resolvers.TacticianActivationResolver policy, IPositionEvaluator evaluator,
            UctOptions options, Action<string>? log = null)
        {
            _tableState = tableState;
            _planner = planner;
            _policy = policy;
            _evaluator = evaluator;
            _options = options;
            _log = log;
        }

        public async Task<DataBinding<UnitData>> Resolve(ChooseUnitToActivateRequest request)
        {
            // A prescription that was never consumed (an activation the engine cut short) must not
            // survive into this one - the search below sets the only prescription that applies here.
            _planner.ClearPrescription();
            LastPrescription = null;

            if (request.ValidOptions.Count > 0)
            {
                await TrySearchAndPrescribe();
            }

            return await _policy.Resolve(request);
        }

        /// <summary>
        /// Roots a tree on the live position and prescribes the winning edge. Returns quietly on
        /// every failure - the caller then gets plain A (G3). Note the search runs even when only
        /// ONE unit can activate: level 2 (which action that unit takes) is the half that still has
        /// a choice in it, and the activation resolver consumes a single-option prescription fine.
        /// </summary>
        private async Task TrySearchAndPrescribe()
        {
            // Save needs the concrete store for its type map; a read model over anything else means
            // no search here (there is no such implementation today - this is the honest degrade).
            if (_tableState.DataStore is not GameDataStore store)
            {
                Count(fellBack: true, "no serializable store on this table state");
                return;
            }

            try
            {
                // The engine's rolling save point (DeterminePlayerTurnStage.Enter) has just written
                // the flow state, so serializing here captures exactly this activation boundary.
                string snapshot = SimulationService.Snapshot(store);
                var clock = System.Diagnostics.Stopwatch.StartNew();
                SearchResult result = await UctSearch.RunAsync(snapshot, _options, _evaluator);
                clock.Stop();
                SearchMs += clock.ElapsedMilliseconds;

                if (result.Choice is not { } choice)
                {
                    Count(fellBack: true, $"search returned no choice ({result.Note})");
                    return;
                }

                // Bindings and macro geometry come from the SEARCH's store, not this one. Matching
                // the unit by DataReference and rebinding the macro is what stops a foreign store
                // leaking into the live game (B2 hit exactly this, and it was silent).
                DataBinding<UnitData>? unit = choice.Prescription.Unit.HasValue
                    ? store.GetDataBinding<UnitData>(choice.Prescription.Unit.Value)
                    : null;
                _planner.Prescribe(unit, choice.Prescription.Action,
                    choice.Prescription.Macro == null
                        ? null
                        : SimulationService.Rebind(choice.Prescription.Macro, store));
                LastPrescription = choice.Prescription;

                Count(fellBack: false,
                    $"{choice.Label} ({choice.Visits} visits, {result.Iterations} iterations, " +
                    $"{result.Nodes} nodes, depth {result.MaxDepth}, {result.ElapsedMs}ms)");
            }
            catch (Exception exception)
            {
                // G3's whole point: a search fault is never allowed to reach the game.
                _planner.ClearPrescription();
                Count(fellBack: true, $"search faulted: {exception.GetType().Name}: {exception.Message}");
            }
        }

        private void Count(bool fellBack, string note)
        {
            Searches++;
            if (fellBack) Fallbacks++;
            _log?.Invoke(fellBack ? $"search fallback to A - {note}" : $"search {note}");
        }
    }
}
