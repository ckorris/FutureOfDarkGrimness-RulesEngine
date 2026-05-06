using FDG.Data;
using FDG.Players;
using FDG.Stages;
using FDG.StageResolution;
using FDG.TempVisuals;
using System.Drawing;
using System.Numerics;

namespace FDG.Tests
{
    // Shared fake/stub types used by DangerousTerrainWoundTests and CoverMajorityTests.

    // Returns a single fixed die value for every Roll call.
    internal class FixedDiceRoller : IDiceRoller
    {
        private readonly int _value;
        public FixedDiceRoller(int value) => _value = value;
        public IDiceResults Roll(int sideCount, float rollCount) => new FixedDiceResults(_value);
    }

    internal class FixedDiceResults : IDiceResults
    {
        private readonly int _value;
        public FixedDiceResults(int value) => _value = value;

        public float this[int index] => _value;
        public int SideMin => 1;
        public int SideMax => 6;
        public float TotalRolls => 1f;
        public float At(int rollNumber)         => rollNumber == _value ? 1f : 0f;
        public float AtOrAbove(int rollNumber)  => _value >= rollNumber ? 1f : 0f;
        public float Above(int rollNumber)      => _value >  rollNumber ? 1f : 0f;
        public float BelowOrAt(int rollNumber)  => _value <= rollNumber ? 1f : 0f;
        public float Below(int rollNumber)      => _value <  rollNumber ? 1f : 0f;
        public float Range(int lo, int hi)      => _value >= lo && _value <= hi ? 1f : 0f;
        public IDiceResults SubsetAt(int n)        => new FixedDiceResults(At(n) > 0 ? _value : 0);
        public IDiceResults SubsetAtOrAbove(int n) => new FixedDiceResults(AtOrAbove(n) > 0 ? _value : 0);
        public IDiceResults SubsetAbove(int n)     => new FixedDiceResults(Above(n) > 0 ? _value : 0);
        public IDiceResults SubsetBelowOrAt(int n) => new FixedDiceResults(BelowOrAt(n) > 0 ? _value : 0);
        public IDiceResults SubsetBelow(int n)     => new FixedDiceResults(Below(n) > 0 ? _value : 0);
        public IDiceResults SubsetRange(int lo, int hi) => new FixedDiceResults(Range(lo, hi) > 0 ? _value : 0);
    }

    internal class NullTempVisualDrawer : ITempVisualDrawer
    {
        public void AddVisual(ITempVisual visual) { }
        public void UpdateVisualTransform(Guid id, Position p, Quaternion r, Vector3 s) { }
        public void UpdateVisualColor(Guid id, Color c) { }
        public void RemoveVisual(Guid id) { }
        public void ClearAllVisuals() { }
    }

    internal class NullPlayerRequester : IPlayerRequestByID
    {
        public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
            where TRequest : IStageTaskRequest<TReply>
            => new TaskCompletionSource<TReply>().Task;
    }

    // Minimal IGameContext backed by real GameDataStore and TableState.
    internal class TestGameContext : IGameContext
    {
        public ITextOutput TextOutput { get; } = new EmptyTextOutput();
        public IDiceRoller DiceRoller { get; }
        public IPlayerRequestByID PlayerRequester { get; } = new NullPlayerRequester();
        public TableState TableState { get; }
        public IReadWriteableGameDataStore GameDataStore { get; }
        public ITempVisualDrawer TempVisualDrawer { get; } = new NullTempVisualDrawer();
        public List<ITeam>? FirstDeploymentRollOrder => null;
        IGameContext IGameContextAccessor.GameContext => this;

        public TestGameContext(GameDataStore store, IDiceRoller diceRoller)
        {
            GameDataStore = store;
            TableState = new TableState(store);
            DiceRoller = diceRoller;
        }

        public void SetFirstDeploymentRollOrder(List<ITeam> order) { }
        public void NotifyGameEnded(string result) { }
    }

    // Parent layer that accepts all transitions without doing anything.
    internal class NoOpLayer<TContext> : IStateMachineLayer<TContext>
    {
        public Task ExecuteTransition(string eventName, StageBase<TContext> leaving, TContext context)
            => Task.CompletedTask;
        public void NotifyChildEntered(IStage stage) { }
        public void NotifyChildExited(IStage stage) { }
    }
}
