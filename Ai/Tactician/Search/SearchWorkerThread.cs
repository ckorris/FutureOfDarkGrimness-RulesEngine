using System.Collections.Concurrent;

namespace FDG.Ai.Tactician.Search
{
    /// <summary>
    /// Runs one root worker's entire descent on ONE dedicated OS thread (#191 perf, 2026-09-09).
    /// <para>
    /// <b>Why this exists.</b> A worker is a sequential loop of iterations, but it is written in
    /// <c>async</c> code and every edge expansion ends in a <c>TaskCompletionSource</c> created with
    /// <see cref="TaskCreationOptions.RunContinuationsAsynchronously"/> (see
    /// <c>SimulationService.Run</c>). Under the default context that posts each continuation to the
    /// GLOBAL thread-pool queue, so the logical worker resumes on whichever pool thread happens to
    /// take it: one measured 4-worker search was carried by TEN distinct <c>.NET TP Worker</c>
    /// threads. Every hop is a cold stack and a cold allocation context, and four workers scaled only
    /// 2.3x on a 32-CPU box because of it.
    /// </para>
    /// <para>
    /// Installing a single-threaded <see cref="SynchronizationContext"/> makes every await inside the
    /// worker come back to the thread it left, which is what the measurement wanted: capping the
    /// process's pool at the worker count (the crude version of the same thing) took board A from
    /// 13.3 to 9.9 ms per iteration, 26% of a 33% effect that had been attributed to L3 locality.
    /// Doing it per worker rather than process-wide leaves the app's networking - and the lab's
    /// concurrent games - on the pool where they belong.
    /// </para>
    /// <para>
    /// <b>This changes no search semantics.</b> The worker still runs its iterations in order on its
    /// own tree, and the merge is still a deterministic reduction in worker order (G5); only the
    /// thread the work sits on changes.
    /// </para>
    /// </summary>
    internal static class SearchWorkerThread
    {
        /// <summary>
        /// Starts <paramref name="work"/> on a fresh background thread whose await continuations come
        /// back to that same thread, and completes when the work does. A thread costs tens of
        /// microseconds against a budget measured in seconds, so each search makes its own rather
        /// than keeping a pool of them alive between activations.
        /// </summary>
        public static Task<T> RunAsync<T>(string name, Func<Task<T>> work)
        {
            var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(() =>
            {
                SynchronizationContext? previous = SynchronizationContext.Current;
                var pump = new Pump();
                SynchronizationContext.SetSynchronizationContext(pump);
                try
                {
                    Task<T> root = work();
                    // The worker settles the caller's task and closes the queue in one continuation:
                    // the pump thread is inside Run() by then and cannot signal itself, and nothing
                    // here ever blocks on root. ExecuteSynchronously so the common case - root
                    // finishing ON the pump thread - costs no hop at all.
                    root.ContinueWith(finished =>
                    {
                        pump.Close();
                        if (finished.IsCanceled) completion.TrySetCanceled();
                        else if (finished.Exception is { } faulted) completion.TrySetException(faulted.InnerExceptions);
                        else completion.TrySetResult(finished.Result);
                    }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

                    try { pump.Run(); }
                    finally { pump.Close(); }
                }
                catch (Exception exception)
                {
                    // work() threw before returning a task, or a queued callback escaped with one.
                    completion.TrySetException(exception);
                }
                finally
                {
                    SynchronizationContext.SetSynchronizationContext(previous);
                }
            })
            {
                IsBackground = true,
                Name = name,
            };
            thread.Start();
            return completion.Task;
        }

        /// <summary>
        /// A message loop for one thread: <see cref="Post"/> queues, <see cref="Run"/> drains until
        /// the owner closes it. Late arrivals (a watchdog <c>Task.Delay</c> whose line already
        /// settled) fall back to the thread pool rather than being dropped - a dropped continuation
        /// is an async method that never finishes.
        /// </summary>
        private sealed class Pump : SynchronizationContext
        {
            private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = new();

            public override void Post(SendOrPostCallback d, object? state)
            {
                if (!_queue.IsAddingCompleted)
                {
                    try
                    {
                        _queue.Add((d, state));
                        return;
                    }
                    catch (InvalidOperationException)
                    {
                        // Closed between the check and the Add; fall through to the pool.
                    }
                }
                ThreadPool.UnsafeQueueUserWorkItem(
                    static work => work.Callback(work.State), (Callback: d, State: state), preferLocal: false);
            }

            /// <summary>
            /// Nothing in the search path calls this, but a Send that deadlocked would be a very
            /// obscure failure, so it is answered honestly: inline on the pump's own thread, and a
            /// posted-and-awaited round trip from anywhere else.
            /// </summary>
            public override void Send(SendOrPostCallback d, object? state)
            {
                if (Current == this)
                {
                    d(state);
                    return;
                }
                using var done = new ManualResetEventSlim(false);
                Exception? failure = null;
                Post(_ =>
                {
                    try { d(state); }
                    catch (Exception exception) { failure = exception; }
                    finally { done.Set(); }
                }, null);
                done.Wait();
                if (failure != null) throw failure;
            }

            /// <summary>The context belongs to its thread, so a copy is the same loop.</summary>
            public override SynchronizationContext CreateCopy() => this;

            public void Close() => _queue.CompleteAdding();

            public void Run()
            {
                foreach ((SendOrPostCallback callback, object? state) in _queue.GetConsumingEnumerable())
                {
                    callback(state);
                }
            }
        }
    }
}
