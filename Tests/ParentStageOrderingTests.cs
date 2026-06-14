using FDG.Data;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // #064 — characterization tests pinning ParentStage's enter/exit/reconcile ordering BEFORE #083
    // refactors the async-void transition chain. They drive transitions through the real
    // StageBinding → SignalEvent → ExecuteTransition path with synchronous-completing child stages,
    // so the observable order is deterministic and these guards survive the planned refactor.
    //
    // A single shared trace list records, in order: child enter/exit, the parent layer's
    // NotifyChildEntered/Exited bubble-ups, the reconcile hook, and sibling-out ExecuteTransition.
    [TestFixture]
    public class ParentStageOrderingTests
    {
        private GameDataStore _store = null!;
        private TestGameContext _gameCtx = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _gameCtx = new TestGameContext(_store, new FixedDiceRoller(4));
        }

        [Test]
        public async Task Enter_EntersStartingChild_NotifyingBeforeEntering()
        {
            var (parent, _) = BuildParent();

            await parent.Enter(new SimpleContext(_gameCtx));

            Assert.That(parent.CurrentChild, Is.EqualTo(parent.ChildA));
            Assert.That(parent.Log, Is.EqualTo(
                new[] { "notifyEntered:ChildAStage", "enter:ChildAStage" }));
        }

        [Test]
        public async Task ChildToChildTransition_ExitsLeavingBeforeEnteringNew()
        {
            var (parent, _) = BuildParent();
            await parent.Enter(new SimpleContext(_gameCtx));
            parent.Log.Clear();

            parent.ChildA.Go.Bind(parent.ChildB.Name);
            await parent.ChildA.Go.Activate(new SimpleContext(_gameCtx));

            Assert.That(parent.CurrentChild, Is.EqualTo(parent.ChildB));
            Assert.That(parent.Log, Is.EqualTo(
                new[]
                {
                    "exit:ChildAStage",
                    "notifyExited:ChildAStage",
                    "notifyEntered:ChildBStage",
                    "enter:ChildBStage",
                }));
        }

        [Test]
        public async Task SiblingTransition_ReconcilesThenBubblesToParentLayer()
        {
            var (parent, top) = BuildParent();
            await parent.Enter(new SimpleContext(_gameCtx));
            parent.Log.Clear();

            parent.ChildA.Go.Bind("to-sibling");
            await parent.ChildA.Go.Activate(new SimpleContext(_gameCtx));

            // Reconcile runs after the leaving child exits and before the parent's sibling binding
            // fires up to its own parent layer.
            Assert.That(parent.Log, Is.EqualTo(
                new[]
                {
                    "exit:ChildAStage",
                    "notifyExited:ChildAStage",
                    "reconcile",
                    "execTransition:parent-done",
                }));
            Assert.That(top.Log, Is.SameAs(parent.Log));
        }

        // #083 — the headline guarantee of the await-through refactor: a fault thrown inside a child
        // stage's Enter, reached via a transition, propagates out of the awaited Activate chain.
        // Under the old async-void Transition delegate this exception was unobservable (it escaped to
        // the synchronization context rather than faulting the awaited Task), so this test would have
        // failed to surface anything to await.
        [Test]
        public async Task ChildToChildTransition_WhenEnteredChildThrows_FaultPropagatesThroughActivate()
        {
            var (parent, _) = BuildParent();
            await parent.Enter(new SimpleContext(_gameCtx));
            parent.ChildB.ThrowOnEnter = true;

            parent.ChildA.Go.Bind(parent.ChildB.Name);

            InvalidOperationException ex = Assert.ThrowsAsync<InvalidOperationException>(
                async () => await parent.ChildA.Go.Activate(new SimpleContext(_gameCtx)));
            Assert.That(ex!.Message, Is.EqualTo("boom from ChildBStage"));
        }

        [Test]
        public void ExecuteTransition_UnknownEvent_ThrowsKeyNotFound()
        {
            var (parent, _) = BuildParent();

            Assert.ThrowsAsync<KeyNotFoundException>(async () =>
                await parent.ExecuteTransition("no-such-event", parent.ChildA, new SimpleContext(_gameCtx)));
        }

        [Test]
        public async Task Exit_NotifiesParentAndClearsCurrentChild()
        {
            var (parent, _) = BuildParent();
            await parent.Enter(new SimpleContext(_gameCtx));
            parent.Log.Clear();

            parent.Exit();

            Assert.That(parent.CurrentChild, Is.Null);
            Assert.That(parent.Log, Is.EqualTo(new[] { "notifyExited:ChildAStage" }));
        }

        // The save/load resume hook: when GetResumeEntry returns a child, Enter restores into that
        // child instead of the normal starting child. #052 depends on this path.
        [Test]
        public async Task Enter_WithResumeEntry_EntersResumeChildInsteadOfStart()
        {
            var (parent, _) = BuildParent();
            parent.ResumeToChildB = true;

            await parent.Enter(new SimpleContext(_gameCtx));

            Assert.That(parent.CurrentChild, Is.EqualTo(parent.ChildB));
            Assert.That(parent.Log, Is.EqualTo(
                new[] { "notifyEntered:ChildBStage", "enter:ChildBStage" }));
        }

        private (TestParentStage Parent, RecordingLayer<SimpleContext> Top) BuildParent()
        {
            var top = new RecordingLayer<SimpleContext>();
            var parent = new TestParentStage(_gameCtx, top);
            top.Log = parent.Log; // unify the trace so child + layer events share one ordered list
            return (parent, top);
        }
    }

    // Minimal child context — ParentStage<TSelf, TChild> only needs IGameContextAccessor.
    internal sealed class SimpleContext : IGameContextAccessor
    {
        public IGameContext GameContext { get; }
        public SimpleContext(IGameContext gameContext) => GameContext = gameContext;
    }

    // Records the layer-level notifications and sibling-out transitions a real grandparent would see.
    internal sealed class RecordingLayer<T> : IStateMachineLayer<T>
    {
        public List<string> Log = new();

        public Task ExecuteTransition(string eventName, StageBase<T> leavingChild, T context)
        {
            Log.Add($"execTransition:{eventName}");
            return Task.CompletedTask;
        }

        public void NotifyChildEntered(IStage stage) => Log.Add($"notifyEntered:{stage.Name}");
        public void NotifyChildExited(IStage stage) => Log.Add($"notifyExited:{stage.Name}");
    }

    internal abstract class RecordingChildStage : StageBase<SimpleContext>
    {
        private readonly List<string> _log;
        public StageBinding Go; // bound by tests to drive a transition out of this child
        public bool ThrowOnEnter = false; // #083 — fault-propagation guard

        protected RecordingChildStage(IGameContext gameContext,
            IStateMachineLayer<SimpleContext> parent, List<string> log) : base(gameContext, parent)
        {
            _log = log;
            Go = new StageBinding(this);
        }

        public override Task Enter(SimpleContext context)
        {
            _log.Add($"enter:{Name}");
            if (ThrowOnEnter)
            {
                throw new InvalidOperationException($"boom from {Name}");
            }
            return Task.CompletedTask;
        }

        public override void Exit() => _log.Add($"exit:{Name}");
    }

    internal sealed class ChildAStage : RecordingChildStage
    {
        public ChildAStage(IGameContext g, IStateMachineLayer<SimpleContext> p, List<string> log)
            : base(g, p, log) { }
    }

    internal sealed class ChildBStage : RecordingChildStage
    {
        public ChildBStage(IGameContext g, IStateMachineLayer<SimpleContext> p, List<string> log)
            : base(g, p, log) { }
    }

    internal sealed class TestParentStage : ParentStage<SimpleContext, SimpleContext>
    {
        // Field initializer runs before the base constructor (which calls PopulateTransitions),
        // so Log is non-null by the time the children are created against it.
        public readonly List<string> Log = new();

        public ChildAStage ChildA = null!;
        public ChildBStage ChildB = null!;
        public StageBinding OnReachedSibling = null!;

        public bool ResumeToChildB = false;

        public TestParentStage(IGameContext gameContext, IStateMachineLayer<SimpleContext> parent)
            : base(gameContext, parent) { }

        protected override Dictionary<string, Transition> PopulateTransitions(
            out StageBase<SimpleContext> startingChild)
        {
            ChildA = new ChildAStage(GameContext, this, Log);
            ChildB = new ChildBStage(GameContext, this, Log);
            OnReachedSibling = new StageBinding(this);
            OnReachedSibling.Bind("parent-done");

            startingChild = ChildA;
            return new TransitionSetBuilder(this)
                .AddChild(ChildA)
                .AddChild(ChildB)
                .AddSibling("to-sibling", OnReachedSibling)
                .Build();
        }

        protected override SimpleContext GetNewChildContext(SimpleContext contextSelf)
            => new SimpleContext(GameContext);

        protected override (StageBase<SimpleContext> Child, SimpleContext Context)? GetResumeEntry(
            SimpleContext contextSelf)
            => ResumeToChildB ? (ChildB, new SimpleContext(GameContext)) : null;

        protected override void ReconcileChildContextBeforeLeaving(
            SimpleContext selfContext, SimpleContext childContext) => Log.Add("reconcile");
    }
}
