using System.Runtime.ExceptionServices;
using FDG.Ai;
using FDG.Ai.Tactician.Search;
using FDG.Data;
using FDG.SaveLoad;
using FDG.Simulation;
using NUnit.Framework;

namespace FDG.Tests
{
    // #191 R9 - the GUI freeze at the Strategist's first activation. A search is thousands of
    // resumed engines, and two engine paths were built on thrown-and-caught exceptions: the save
    // loader resolved forward references by catching (~200 per 2k snapshot), and the end of a
    // simulated line was a throw that re-unwound the state machine's nested-await transition
    // chain at every frame (~70-190 re-throws per line). Some 400 first-chance exceptions per
    // simulation, invisible in Release, and each one a stop-the-process debugger event under
    // Visual Studio - which is what pinned Chris's laptop. These pin that both paths are
    // exception-free now, that a cancelled or timed-out line stops its game at the next boundary
    // instead of running on, and that the default worker count leaves the machine a core.
    [TestFixture]
    public class SimulationStopTests
    {
        private static string Snapshot() => TacticianActionSpaceTests.Fixture.Snapshot(2, objectives: 1);

        /// <summary>
        /// Counts first-chance exceptions of one type raised while <paramref name="action"/> runs,
        /// on any thread when <paramref name="anyThread"/> (a simulated game runs on the pool),
        /// else only on the calling thread (the loader is synchronous, and parallel fixtures may
        /// throw the same type legitimately elsewhere).
        /// </summary>
        private static async Task<int> CountFirstChance(string typeName, bool anyThread, Func<Task> action)
        {
            int count = 0;
            int thread = Environment.CurrentManagedThreadId;
            void Handler(object? sender, FirstChanceExceptionEventArgs e)
            {
                if (e.Exception.GetType().Name != typeName) return;
                if (anyThread || Environment.CurrentManagedThreadId == thread) Interlocked.Increment(ref count);
            }
            AppDomain.CurrentDomain.FirstChanceException += Handler;
            try { await action(); }
            finally { AppDomain.CurrentDomain.FirstChanceException -= Handler; }
            return count;
        }

        [Test]
        public async Task Load_ResolvesForwardReferences_WithoutThrowing()
        {
            string snapshot = Snapshot();
            GameDataStore? store = null;

            int thrown = await CountFirstChance("InvalidDataReferenceException", anyThread: false, () =>
            {
                store = GameSaveSerializer.Load(snapshot);
                return Task.CompletedTask;
            });

            Assert.That(thrown, Is.EqualTo(0),
                "the loader orders entries by checking their references, never by catching");
            Assert.That(store!.GetAllValues<UnitData>().Count(), Is.EqualTo(4), "and still loads the whole store");
            Assert.That(SimulationService.Snapshot(store), Is.EqualTo(snapshot),
                "a checked replay must produce the same store the catching one did");
        }

        [Test]
        public void ScanReferences_FindsEveryEmbeddedReference()
        {
            // The three-field shape Newtonsoft writes for a DataReference / DataBinding field.
            const string json = "{\"PositionBinding\":{\"TypeID\":{\"ID\":4},\"Index\":7,\"Generation\":2}," +
                                "\"Owner\":{\"TypeID\":{\"ID\":11},\"Index\":0,\"Generation\":1},\"X\":1.0}";

            DataReference[] references = StoreReplay.ScanReferences(json);

            Assert.That(references, Has.Length.EqualTo(2));
            Assert.That(references[0].TypeID.ID, Is.EqualTo(4));
            Assert.That(references[0].Index, Is.EqualTo(7));
            Assert.That(references[0].Generation, Is.EqualTo(2));
            Assert.That(references[1].TypeID.ID, Is.EqualTo(11));
            Assert.That(StoreReplay.ScanReferences("1.0"), Is.Empty, "a leaf value has no references");
        }

        [Test]
        [CancelAfter(120_000)]
        public async Task LineStop_IsCooperative_NoExceptionUnwindsTheStateMachine()
        {
            string start = Snapshot();
            SimulationService.SimulationResult? result = null;

            int thrown = await CountFirstChance("SimulationStopSignal", anyThread: true, async () =>
            {
                result = await new SimulationService(new SimulationService.SimulationOptions { Seed = 11 })
                    .RunNatural(start, activations: 1);
            });

            Assert.That(result!.ReachedEndOfLine, Is.True, result.Note);
            Assert.That(result.ActivationsRun, Is.EqualTo(1));
            Assert.That(thrown, Is.EqualTo(0), "the end of a line returns through the machine, it does not throw");
        }

        [Test]
        [CancelAfter(120_000)]
        public async Task CancelledLine_StopsAtItsNextBoundaryWithoutPlaying()
        {
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            var service = new SimulationService(new SimulationService.SimulationOptions
            {
                Seed = 11,
                Cancellation = cancelled.Token,
            });

            SimulationService.SimulationResult result = await service.RunNatural(Snapshot(), activations: 3);

            Assert.That(result.ReachedEndOfLine, Is.False);
            Assert.That(result.EndedEarly, Is.Null, "a cancellation is neither a fault nor a natural end");
            Assert.That(result.Note, Does.Contain("cancelled"));
            Assert.That(result.ActivationsRun, Is.EqualTo(0), "the game stopped at its first boundary");
        }

        [Test]
        [CancelAfter(120_000)]
        public async Task DeadlineSearch_LeavesAnInterruptedEdgeOpen_RatherThanClosed()
        {
            // A tree whose deadline has already passed: opening an edge runs a line that stops at its
            // first boundary. That is the search ending, not the edge failing, so the edge must stay
            // untried (a closed edge is never credited again).
            using var deadline = new CancellationTokenSource();
            var options = new SearchOptions { InSimProfile = EAiProfile.Tactician, Cancellation = deadline.Token };
            SearchTree tree = await SearchTree.FromSnapshotAsync(Snapshot(), options,
                new TacticianActionSpace(options), new HandWeightedEvaluator());
            UnitBranch unit = tree.UnitsOf(tree.Root)[0];
            SearchEdge edge = tree.EdgesOf(tree.Root, unit)[0];

            deadline.Cancel();
            SearchNode? child = await tree.OpenAsync(tree.Root, unit, edge);

            Assert.That(child, Is.Null, "nothing opens past the deadline");
            Assert.That(edge.Closed, Is.False, "and the edge is not blamed for it");
            Assert.That(edge.Child, Is.Null);
        }

        [Test]
        [CancelAfter(300_000)]
        public async Task TimeBudgetedSearch_StillReturnsAChoice_WithTheDeadlineArmed()
        {
            // The deadline is armed only under a time budget; a generous one on a tiny board must
            // still search normally and return a playable choice.
            var options = new UctOptions
            {
                RootSeed = 3,
                Workers = 2,
                BaseBudgetMs = 1500,
                MaxBudgetMs = 1500,
                Tree = new SearchOptions { InSimProfile = EAiProfile.Tactician, TimeoutSeconds = 120 },
            };

            SearchResult result = await UctSearch.RunAsync(Snapshot(), options, new HandWeightedEvaluator());

            Assert.That(result.Choice, Is.Not.Null, result.Note);
            Assert.That(result.Iterations, Is.GreaterThan(0));
        }

        [Test]
        public void DefaultSearchWorkers_LeaveTheMachineACore_AndNeverExceedThePlansFour()
        {
            int workers = AiProfileFactory.DefaultSearchWorkers;

            Assert.That(workers, Is.InRange(1, 4));
            Assert.That(workers, Is.EqualTo(Math.Clamp(Environment.ProcessorCount - 1, 1, 4)));
            Assert.That(AiProfileFactory.DefaultSearchBudget.Workers, Is.EqualTo(workers));
        }
    }
}
