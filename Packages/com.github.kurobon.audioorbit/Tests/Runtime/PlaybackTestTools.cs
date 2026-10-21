using System;
using System.Threading.Tasks;
using UnityEngine;

namespace AudioOrbit.Tests
{
    public readonly struct PlaybackTestStart
    {
        public bool Started { get; }
        public PlayRejectionReason RejectionReason { get; }
        public PlaybackHandle Handle { get; }
        public Exception Error { get; }
        internal PlaybackTestStart(bool started, PlayRejectionReason reason, Exception error)
        { Started = started; RejectionReason = reason; Error = error; Handle = null; }
        internal PlaybackTestStart(PlaybackHandle handle)
        {
            Started = handle.Started; RejectionReason = handle.RejectionReason; Handle = handle;
            Error = handle.IsCompleted ? handle.WaitForCompletionAsync().GetAwaiter().GetResult().Error : null;
        }
    }
    public static class PlaybackTestTools
    {
        public static PlaybackHandle StartForTest(this PlaybackHandle handle, PlayOptions options = default)
        {
            handle.Play(options);
            return handle;
        }

        internal static PlaybackRequest StartRunForTest(this PlaybackHandle handle, PlayOptions options = default)
        {
            handle.Play(options);
            return handle.Request;
        }

        internal static async Task<PlaybackTestStart> ObserveStartAsync(this PlaybackRequest request)
        {
            while (!request.Started && !request.IsCompleted) await Task.Delay(10);
            var result = request.IsCompleted ? await request.WaitForCompletionAsync() : default;
            return new PlaybackTestStart(request.Started, request.RejectionReason, result.Error);
        }

        public static async Task<PlaybackTestStart> ObserveStartAsync(this PlaybackHandle handle)
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (!handle.Started && !handle.IsCompleted)
            {
                if (DateTime.UtcNow > deadline) throw new TimeoutException("Playback did not start or finish.");
                await Task.Delay(1);
            }
            return new PlaybackTestStart(handle);
        }
    }
}
