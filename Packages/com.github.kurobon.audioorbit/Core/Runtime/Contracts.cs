using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace AudioOrbit
{
    public enum PlayRejectionReason
    {
        None,
        PlaybackLimit,
        Cooldown,
        TargetLost,
        LibraryUnregistered,
        Failed
    }

    public enum PlaybackEndReason
    {
        Completed,
        Stopped,
        Stolen,
        TargetLost,
        OwnerLost,
        EngineDisposed,
        Failed,
        Canceled
    }

    public enum PlaybackState
    {
        Idle,
        Loading,
        Playing,
        Paused,
        FadingOut,
        Ended
    }

    public enum PlaybackMode
    {
        TwoD,
        AtPosition,
        Attached
    }

    public enum TargetLostBehavior
    {
        Stop,
        FadeOut,
        KeepLastPosition
    }

    public enum OwnerLostBehavior
    {
        Stop,
        FadeOut,
        Continue
    }

    public enum PlaybackLimitBehavior
    {
        RejectNew,
        StopOldest,
        StopLowestPriority
    }

    public enum ClipSelectionMode
    {
        Sequence,
        WeightedRandom,
        AvoidImmediateRepeat,
        Shuffle
    }

    public enum ExternalRepeatBehavior
    {
        RejectNew,
        Restart
    }

    /// <summary>Only an explicit value overrides the definition, including zero and false.</summary>
    [Serializable]
    public struct OverrideValue<T>
    {
        [SerializeField] private bool isSpecified;
        [SerializeField] private T value;
        public bool IsSpecified => isSpecified;
        public T Value => value;

        public OverrideValue(T value)
        {
            this.value = value;
            isSpecified = true;
        }

        public static implicit operator OverrideValue<T>(T value) => new(value);
    }

    internal readonly struct PlayStartResult
    {
        public bool Started { get; }
        public PlayRejectionReason RejectionReason { get; }
        public Exception Error { get; }

        internal PlayStartResult(bool started, PlayRejectionReason reason, Exception error = null)
        {
            Started = started;
            RejectionReason = reason;
            Error = error;
        }
    }

    public readonly struct PlaybackResult
    {
        public bool Started { get; }
        public PlayRejectionReason RejectionReason { get; }
        public PlaybackEndReason? EndReason { get; }
        public Exception Error { get; }

        internal PlaybackResult(bool started, PlayRejectionReason rejection, PlaybackEndReason? end, Exception error = null)
        {
            Started = started;
            RejectionReason = rejection;
            EndReason = end;
            Error = error;
        }
    }

    /// <summary>A directly referenced Clip or an entry in a loadable AudioLibrary.</summary>
    public interface IAudioReference
    {
        string Key { get; }
        /// <summary>The Library to load, or null for a registered key or directly referenced Clip.</summary>
        IAudioLibraryReference LibraryReference { get; }
    }

    /// <summary>Backend-provided Library location and loading information, independent of any Audio key.</summary>
    public interface IAudioLibraryReference
    {
        string LibraryIdentity { get; }
        /// <summary>Creates a loader owned and disposed by the engine.</summary>
        IAudioLibraryLoader CreateLibraryLoader();
    }

    /// <summary>Acquires one or more libraries. Called on the Unity main thread.</summary>
    public interface IAudioLibraryLoader : IDisposable
    {
        Task<IAudioLibraryLease> LoadAsync(CancellationToken cancellationToken = default);
    }

    public interface IAudioLibraryLease : IDisposable
    {
        System.Collections.Generic.IReadOnlyList<AudioLibrary> Libraries { get; }
    }

    public sealed class LibraryHandle
    {
        internal AudioEngine Engine;
        internal readonly System.Collections.Generic.List<string> Keys = new();
        internal bool Registered = true;

        internal LibraryHandle(AudioEngine engine)
        {
            Engine = engine;
        }
    }

    public sealed class ExternalSourceHandle : IDisposable
    {
        internal readonly object LimitKey;
        internal AudioEngine Engine;
        internal AudioSource Source;
        internal ExternalSourceOptions Options;
        internal SourceSnapshot Snapshot;

        internal ExternalSourceHandle(AudioEngine engine, AudioSource source, ExternalSourceOptions options)
        {
            Engine = engine;
            Source = source;
            Options = options;
            LimitKey = string.IsNullOrEmpty(options.Key) ? new object() : options.Key;
            Snapshot = new SourceSnapshot(source);
        }

        public void Dispose()
        {
            Engine?.UnregisterExternal(this);
        }
    }
}
