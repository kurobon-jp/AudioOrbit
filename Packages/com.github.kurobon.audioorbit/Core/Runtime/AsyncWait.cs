using System;
using System.Threading;
using System.Threading.Tasks;

namespace AudioOrbit
{
    internal static class AsyncWait
    {
        internal static Task<T> WithCancellation<T>(Task<T> task, CancellationToken token)
        {
            if (task.IsCompleted || !token.CanBeCanceled) return task;
            if (token.IsCancellationRequested) return Task.FromCanceled<T>(token);
            return AwaitWithCancellation(task, token);
        }

        private static async Task<T> AwaitWithCancellation<T>(Task<T> task, CancellationToken token)
        {
            var canceled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            await using (token.Register(() => canceled.TrySetResult(true)))
            {
                if (await Task.WhenAny(task, canceled.Task) != task)
                    throw new OperationCanceledException(token);
                return await task;
            }
        }

        internal static Task WithCancellation(Task task, CancellationToken token)
        {
            if (task.IsCompleted || !token.CanBeCanceled) return task;
            if (token.IsCancellationRequested) return Task.FromCanceled(token);
            return AwaitWithCancellation(task, token);
        }

        private static async Task AwaitWithCancellation(Task task, CancellationToken token)
        {
            var canceled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (token.Register(() => canceled.TrySetResult(true)))
            {
                if (await Task.WhenAny(task, canceled.Task) != task) throw new OperationCanceledException(token);
                await task;
            }
        }
    }
}
