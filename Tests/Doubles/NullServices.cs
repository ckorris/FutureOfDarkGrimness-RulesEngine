using FDG.Players;
using FDG.StageResolution;
using FDG.TempVisuals;
using System.Drawing;
using System.Numerics;

namespace FDG.Tests
{
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
}
