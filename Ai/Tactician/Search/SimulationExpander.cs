using FDG.Simulation;

namespace FDG.Ai.Tactician.Search
{
    /// <summary>
    /// Opens an edge by running one 5c line (#191 B2 sec 4.2): the edge's prescription, then
    /// <see cref="SearchOptions.Continuation"/> natural activations, then a stop at the next boundary
    /// where the leaf is evaluated LIVE before the line's one Save (sec 5.2). The honored flag of the
    /// prescribed activation decides whether the child exists at all (sec 4.3).
    /// </summary>
    public sealed class SimulationExpander : INodeExpander
    {
        private readonly SearchOptions _options;
        private readonly IPositionEvaluator _evaluator;
        private readonly SideMap _sides;

        public SimulationExpander(SearchOptions options, IPositionEvaluator evaluator, SideMap sides)
        {
            _options = options;
            _evaluator = evaluator;
            _sides = sides;
        }

        public async Task<ExpansionOutcome> Expand(SearchNode parent, SearchEdge edge, int seed)
        {
            if (parent.Snapshot == null)
                throw new InvalidOperationException("SimulationExpander: cannot expand a terminal node.");

            var service = new SimulationService(new SimulationService.SimulationOptions
            {
                Profile = _options.InSimProfile,
                Seed = seed,
                Randomness = _options.Randomness,
                TimeoutSeconds = _options.TimeoutSeconds,
            });
            var driver = new EdgeLine(edge.Prescription, _options.Continuation, _evaluator, _sides);
            SimulationService.SimulationResult result = await service.Run(parent.Snapshot, driver);

            bool honored = result.Honored.Count > 0 && result.Honored[0];
            if (result.ReachedEndOfLine)
            {
                return new ExpansionOutcome(result.Snapshot, result.ActingPlayerAtEnd, null, driver.Leaf,
                    honored, honored ? result.Note : "prescription fell through at play: " + result.Note);
            }
            if (result.EndedEarly is { } ended)
            {
                bool valued = ended.Outcome is EGameOutcome.Win or EGameOutcome.Tie;
                return new ExpansionOutcome(null, null, valued ? ended : null, null, honored && valued,
                    valued ? result.Note : "line faulted: " + ended.Message);
            }
            return new ExpansionOutcome(null, null, null, null, false, result.Note);
        }

        private sealed class EdgeLine : SimulationService.ILineDriver
        {
            private readonly SimulationService.Prescription _edge;
            private readonly int _continuation;
            private readonly IPositionEvaluator _evaluator;
            private readonly SideMap _sides;

            public SideValues? Leaf { get; private set; }

            public EdgeLine(SimulationService.Prescription edge, int continuation,
                IPositionEvaluator evaluator, SideMap sides)
            {
                _edge = edge;
                _continuation = continuation;
                _evaluator = evaluator;
                _sides = sides;
            }

            public SimulationService.LineStep AtBoundary(SimulationService.LineBoundary boundary)
            {
                if (boundary.Index == 0) return SimulationService.LineStep.Prescribe(_edge);
                if (boundary.Index <= _continuation) return SimulationService.LineStep.Natural;
                Leaf = _evaluator.Evaluate(boundary.State, _sides);
                return SimulationService.LineStep.Stop;
            }
        }
    }
}
