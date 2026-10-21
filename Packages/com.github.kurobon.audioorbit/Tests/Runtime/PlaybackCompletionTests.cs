using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AudioOrbit.Tests
{
    public sealed class PlaybackCompletionTests
    {
        private AudioEngine engine;
        private AudioEngineSettings settings;
        private AudioClip clip;
        [SetUp] public void Setup()
        {
            settings = ScriptableObject.CreateInstance<AudioEngineSettings>();
            settings.InitialPoolSize = 0;
            clip = AudioClip.Create("completion test", 44100, 1, 44100, false);
            engine = new AudioEngine(settings);
        }
        [TearDown] public void Cleanup()
        {
            engine.Dispose();
            UnityEngine.Object.DestroyImmediate(clip);
            UnityEngine.Object.DestroyImmediate(settings);
        }
        private PlaybackHandle Play() => engine.CreatePlayback(new DirectAudioReference(clip)).StartForTest(new PlayOptions { Loop = true });

        [Test] public async Task MultipleWaitersAndLateWaitersReceiveTheSameResult()
        {
            var handle = Play();
            var first = handle.WaitForCompletionAsync();
            var second = handle.WaitForCompletionAsync();
            Assert.False(first.IsCompleted || second.IsCompleted);
            handle.Stop();
            foreach (var result in await Task.WhenAll(first, second, handle.WaitForCompletionAsync()))
            {
                Assert.True(result.Started);
                Assert.AreEqual(PlaybackEndReason.Stopped, result.EndReason);
            }
        }
        [Test] public async Task CancelingOneWaiterDoesNotCancelAnotherOrPlayback()
        {
            var handle = Play();
            using var cancellation = new CancellationTokenSource();
            var first = handle.WaitForCompletionAsync(cancellation.Token);
            var second = handle.WaitForCompletionAsync();
            cancellation.Cancel();
            try { await first; Assert.Fail("The canceled waiter completed successfully."); }
            catch (OperationCanceledException) { }
            Assert.False(handle.IsCompleted || second.IsCompleted);
            handle.Stop();
            Assert.AreEqual(PlaybackEndReason.Stopped, (await second).EndReason);
        }
        [Test] public async Task CompletionBeforeFirstWaitPreservesResultEvenWithCanceledToken()
        {
            var handle = Play(); handle.Stop();
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            var result = await handle.WaitForCompletionAsync(cancellation.Token);
            Assert.True(result.Started);
            Assert.AreEqual(PlaybackEndReason.Stopped, result.EndReason);
        }
        [Test] public async Task FailureBeforeFirstWaitPreservesErrorAndRejection()
        {
            LogAssert.Expect(LogType.Exception, new System.Text.RegularExpressions.Regex("KeyNotFoundException: Unknown Audio key: completion_missing"));
            var handle = engine.CreatePlayback("completion_missing").StartForTest();
            Assert.True(handle.IsCompleted);
            var result = await handle.WaitForCompletionAsync();
            Assert.False(result.Started);
            Assert.AreEqual(PlayRejectionReason.Failed, result.RejectionReason);
            Assert.NotNull(result.Error);
        }
        [Test] public async Task BackgroundWaiterReceivesMainThreadCompletion()
        {
            var handle = Play();
            var ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var waiter = Task.Run(async () =>
            {
                var completion = handle.WaitForCompletionAsync();
                ready.SetResult(true);
                return await completion;
            });
            await ready.Task;
            handle.Stop();
            Assert.AreEqual(PlaybackEndReason.Stopped, (await waiter).EndReason);
        }
    }
}
