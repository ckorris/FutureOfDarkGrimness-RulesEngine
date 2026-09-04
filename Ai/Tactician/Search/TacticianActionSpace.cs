using FDG.Ai.Tactician.Resolvers;
using FDG.Data;
using FDG.Rules.Dispatch;
using FDG.SaveLoad;
using FDG.Simulation;
using FDG.Stages;

namespace FDG.Ai.Tactician.Search
{
    /// <summary>
    /// The real action space (#191 B2 sec 3): level 1 is the acting player's unactivated, living
    /// units ordered by the A policy's own activation ranking (<see
    /// cref="TacticianActivationResolver.ActivationScores"/>); level 2 is
    /// <see cref="MacroActionGenerator.Enumerate"/> for one unit, each candidate mapped to its action
    /// with the planner's own <see cref="TacticianPlanner.ActionNameFor"/> and ordered by the
    /// planner's <see cref="TacticianPlanner.Score"/>. Both priors are computed on a SCRATCH planner
    /// over the node's loaded store, never on a live game's planner.
    /// <para>
    /// Why this makes B a superset of A: the first unit opened is A's unit and the first edge opened
    /// under it is A's macro-action, so a tree allowed one expansion plays exactly A's move (the
    /// design doc's test 4). Every further edge costs a line, not a policy think.
    /// </para>
    /// </summary>
    public sealed class TacticianActionSpace : IActionSpace
    {
        private readonly SearchOptions _options;

        public TacticianActionSpace(SearchOptions options) => _options = options;

        /// <summary>
        /// The actions the stage COULD offer this unit, for mapping candidates to actions. What the
        /// stage actually offers is only known at play; a mismatch is caught by the honored flag
        /// (sec 4.3), not predicted here.
        /// </summary>
        private static List<string> OfferableActions(UnitData unit, RuleEvaluator evaluator)
        {
            var offered = new List<string>
            {
                ChooseActionStage.MOVEMENT_CHOICE_NAME,
                ChooseActionStage.CHARGE_CHOICE_NAME,
                ChooseActionStage.PASS_CHOICE_NAME,
            };
            if (unit.GetRangedWeapons().Count > 0) offered.Add(ChooseActionStage.SHOOT_CHOICE_NAME);
            if (CapabilityRuleQueries.CanCast(unit, evaluator)) offered.Add(ChooseActionStage.CAST_CHOICE_NAME);
            return offered;
        }

        public IReadOnlyList<UnitBranch> EnumerateUnits(SearchNode node)
        {
            Scratch scratch = Load(node);

            // The stage's own offer: the acting player's armies in binding order, filtered to the
            // unactivated pool and the living (ChooseUnitToActivateStage) - so ties resolve to the
            // same "first option" the resolver would pick.
            var pool = new HashSet<DataReference>(scratch.Progress.UnactivatedUnits.Select(u => u.Reference));
            var units = new List<DataBinding<UnitData>>();
            foreach (ArmyData army in scratch.Store.GetAllValues<ArmyData>())
            {
                if (!army.IsOwnedBy(node.ActingPlayer)) continue;
                foreach (DataBinding<UnitData> unit in army.UnitBindings)
                {
                    if (unit.GetValue().GetIsDead() || !pool.Contains(unit.Reference)) continue;
                    units.Add(unit);
                }
            }
            if (units.Count == 0) return Array.Empty<UnitBranch>();

            IReadOnlyList<TacticianActivationResolver.ActivationScore> scores = scratch.Activation.ActivationScores(units);
            float[] priors = Softmax(scores.Select(s => s.Score).ToArray());

            // Stable descending sort: equal scores keep offer order (the resolver's strict-greater rule).
            var order = Enumerable.Range(0, units.Count)
                .OrderByDescending(i => scores[i].Score)
                .ThenBy(i => i)
                .ToList();

            var branches = new List<UnitBranch>(units.Count);
            for (int rank = 0; rank < order.Count; rank++)
            {
                int i = order[rank];
                branches.Add(new UnitBranch(rank, units[i].Reference, units[i].GetValue().Name, priors[i]));
            }
            return branches;
        }

        public IReadOnlyList<SearchEdge> EnumerateEdges(SearchNode node, UnitBranch unit)
        {
            Scratch scratch = Load(node);
            DataBinding<UnitData> binding = scratch.Store.GetDataBinding<UnitData>(unit.Unit);
            UnitData self = binding.GetValue();

            // Score needs the planner's per-activation state - exactly what a natural activation
            // establishes first.
            scratch.Planner.BeginActivation(binding);
            List<MacroAction> candidates = MacroActionGenerator.Enumerate(scratch.Evaluator, scratch.Table,
                binding, _options.CandidateBudget, scratch.SeeThroughFriendlyUnits);
            List<string> offered = OfferableActions(self, scratch.Evaluator);

            // Plan-bearing edges: candidate x its action, scored by the planner. A Hold that maps to
            // Shoot also gets a Pass twin (same macro, same score, ranked after it): if the stage does
            // not offer Shoot the Shoot edge closes and the Pass edge is the next in prior order -
            // which is what the natural policy picks in that situation too.
            var plan = new List<(MacroAction Macro, string Action, float Score, int Order)>();
            for (int i = 0; i < candidates.Count; i++)
            {
                MacroAction candidate = candidates[i];
                string? action = TacticianPlanner.ActionNameFor(candidate, offered);
                if (action == null) continue;
                float score = scratch.Planner.Score(candidate);
                plan.Add((candidate, action, score, plan.Count));
                if (candidate.Intent == EMacroIntent.Hold && action == ChooseActionStage.SHOOT_CHOICE_NAME)
                    plan.Add((candidate, ChooseActionStage.PASS_CHOICE_NAME, score, plan.Count));
            }

            float[] priors = Softmax(plan.Select(p => p.Score).ToArray());
            var ordered = plan.Select((p, i) => (Entry: p, Prior: priors[i]))
                .OrderByDescending(x => x.Entry.Score)
                .ThenBy(x => x.Entry.Order)
                .ToList();

            var edges = new List<SearchEdge>();
            foreach ((var entry, float prior) in ordered)
            {
                edges.Add(new SearchEdge(edges.Count,
                    new SimulationService.Prescription(unit.Unit, entry.Action, entry.Macro), prior,
                    $"{self.Name}: {entry.Action} / {entry.Macro.Rationale}"));
            }

            // Non-plan edges (5b handles them without a macro): Cast for a caster, Disembark for
            // cargo. The planner prices these by a different path, so they take the mean prior and
            // search sorts out their worth.
            float meanPrior = edges.Count == 0 ? 1f : priors.Average();
            if (offered.Contains(ChooseActionStage.CAST_CHOICE_NAME))
            {
                edges.Add(new SearchEdge(edges.Count,
                    new SimulationService.Prescription(unit.Unit, ChooseActionStage.CAST_CHOICE_NAME), meanPrior,
                    $"{self.Name}: Cast"));
            }
            if (TransportUtilities.IsEmbarked(self))
            {
                edges.Add(new SearchEdge(edges.Count,
                    new SimulationService.Prescription(unit.Unit, CoreRuleCatalog.DisembarkRuleName), meanPrior,
                    $"{self.Name}: Disembark"));
            }
            return edges;
        }

        private static float[] Softmax(float[] scores)
        {
            if (scores.Length == 0) return Array.Empty<float>();
            float max = scores.Max();
            var exp = scores.Select(s => MathF.Exp(s - max)).ToArray();
            float sum = exp.Sum();
            return exp.Select(e => e / sum).ToArray();
        }

        private sealed class Scratch
        {
            public GameDataStore Store = null!;
            public TableState Table = null!;
            public GameProgressData Progress = null!;
            public RuleEvaluator Evaluator = null!;
            public TacticianPlanner Planner = null!;
            public TacticianActivationResolver Activation = null!;
            public bool SeeThroughFriendlyUnits;
        }

        private static Scratch Load(SearchNode node)
        {
            if (node.Scratch is Scratch cached) return cached;
            if (node.Snapshot == null)
                throw new InvalidOperationException("TacticianActionSpace: a terminal node has no action space.");

            GameDataStore store = GameSaveSerializer.Load(node.Snapshot);
            GameProgressData progress = GameProgressUtilities.TryGetProgress(store)
                ?? throw new InvalidOperationException("TacticianActionSpace: the snapshot carries no GameProgressData.");
            var table = new TableState(store);
            // The same bare evaluator the Tactician's own registry builds (TacticianResolverRegistryFactory).
            var evaluator = new RuleEvaluator(new ProbabilisticDiceRoller());
            bool seeThrough = progress.Settings.SeeThroughFriendlyUnits;
            var planner = new TacticianPlanner(table, evaluator, decisionLog: null, seeThrough);
            var scratch = new Scratch
            {
                Store = store,
                Table = table,
                Progress = progress,
                Evaluator = evaluator,
                Planner = planner,
                Activation = new TacticianActivationResolver(table, evaluator, planner, null, seeThrough),
                SeeThroughFriendlyUnits = seeThrough,
            };
            node.Scratch = scratch;
            return scratch;
        }
    }
}
