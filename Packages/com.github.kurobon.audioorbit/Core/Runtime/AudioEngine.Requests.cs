using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace AudioOrbit
{
    public sealed partial class AudioEngine
    {
        private readonly HashSet<PlaybackRequest> pending = new();
        internal void RemovePending(PlaybackRequest handle) => pending.Remove(handle);
        private PlaybackRequest CreateRequest(string id, PlayOptions options)
        {
            Check(); ValidateOptions(options);
            var handle = new PlaybackRequest(this) { Key = id, Options = options };
            pending.Add(handle);
            return handle;
        }
        private void RunRequest(PlaybackRequest handle, ValueTask<PlayStartResult> operation)
        {
            if (operation.IsCompleted) handle.AcceptStart(operation.GetAwaiter().GetResult());
            else _ = AwaitRequest(handle, operation);
        }
        private async Task AwaitRequest(PlaybackRequest handle, ValueTask<PlayStartResult> operation)
        {
            try { handle.AcceptStart(await operation); }
            catch (Exception error) { FailRequest(handle, error); }
        }
        private void FailRequest(PlaybackRequest handle, Exception error)
        {
            if (error is OperationCanceledException)
            { handle.FinishBeforeStart(PlayRejectionReason.None, disposed ? PlaybackEndReason.EngineDisposed : PlaybackEndReason.Canceled); }
            else handle.Fail(error);
        }
        internal void PlayExternalRequest(PlaybackRequest handle, ExternalSourceHandle source)
        {
            if (handle.IsCompleted) return;
            Check();
            handle.Engine = this;
            handle.Key = source.Options.Key;
            handle.Options = new PlayOptions { Owner = source.Options.Owner };
            pending.Add(handle);
            try { handle.AcceptStart(StartExternal(source, handle)); }
            catch (Exception error) { FailRequest(handle, error); }
        }
        private void StopPending(Predicate<PlaybackRequest> match, PlaybackEndReason reason)
        {
            List<PlaybackRequest> captured = null;
            foreach (var handle in pending)
                if (match(handle)) (captured ??= new List<PlaybackRequest>()).Add(handle);
            if (captured == null) return;
            foreach (var handle in captured) handle.FinishBeforeStart(PlayRejectionReason.None, reason);
        }
    }
}
