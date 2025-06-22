using FDG.StageResolution;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace FDG.Players
{
    internal interface IPlayerController : IPlayerInfo
    {
        public bool IsReady { get; }

        public event Action<bool> OnReadyStateChanged;

        public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
            where TRequest : IStageTaskRequest<TReply>;

        public Task WaitUntilReadyAsync();
    }

    internal static class IPlayerControllerExtensions
    {
        public static Task WaitUntilReadyAsyncStatic(this IPlayerController controller)
        {
            if (controller.IsReady)
            {
                return Task.CompletedTask;
            }

            TaskCompletionSource<bool> source
                = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            void Handler(bool ready)
            {
                if (ready == false)
                {
                    return;
                }

                controller.OnReadyStateChanged -= Handler;
                source.SetResult(true);
            }

            controller.OnReadyStateChanged += Handler;
            return source.Task;
        }
    }
}
