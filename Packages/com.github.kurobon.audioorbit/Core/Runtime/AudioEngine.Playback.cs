using System;
using System.Collections.Generic;
using UnityEngine;

namespace AudioOrbit
{
    public sealed partial class AudioEngine
    {
        private enum LimitScope { Engine, Category, Audio, Owner }

        private readonly Stack<HashSet<Playback>> victimSets = new();

        private void AddVictim(ref HashSet<Playback> victims, Playback playback)
        {
            if (victims == null)
                victims = victimSets.Count > 0 ? victimSets.Pop() : new HashSet<Playback>();
            victims.Add(playback);
        }

        private PlayStartResult Start(Definition d, int clipIndex, ClipDefinition clipDefinition, AudioClip clip, SessionReference lease, PlayOptions o,
            ExternalSourceHandle ext, PlaybackRequest request = null)
        {
            AudioSource source = null;
            HashSet<Playback> victims = null;
#if UNITY_6000_3_OR_NEWER
            LoopRegionPlayback generator = null;
#endif
            try
            {
                Check();
                if (o.Mode == PlaybackMode.Attached && o.Target == null ||
                    !ReferenceEquals(o.Owner, null) && o.Owner == null)
                {
                    lease?.Dispose();
                    return Reject(PlayRejectionReason.TargetLost);
                }

                var category = categories[d.Category];
                Playback previous = null;
                if (ext != null)
                {
                    foreach (var existingPlayback in playbacks)
                        if (existingPlayback.External == ext)
                        {
                            previous = existingPlayback;
                            break;
                        }

                    if (previous != null)
                    {
                        if (ext.Options.RepeatBehavior == ExternalRepeatBehavior.RejectNew)
                            return Reject(PlayRejectionReason.PlaybackLimit);
                        AddVictim(ref victims, previous);
                    }
                }

                var now = Time.unscaledTimeAsDouble;
#if UNITY_EDITOR
                now = editorTime?.Invoke() ?? now;
#endif
                var owner = o.Owner != null ? o.Owner.GetInstanceID() : 0;
                if (d.Limits.CooldownSeconds > 0 && cooldown.TryGetValue(d.LimitKey, out var last) &&
                    now - last < d.Limits.CooldownSeconds ||
                    d.Limits.OwnerCooldownSeconds > 0 && ownerCooldown.TryGetValue((d.LimitKey, owner), out last) &&
                    now - last < d.Limits.OwnerCooldownSeconds)
                {
                    lease?.Dispose();
                    return Reject(PlayRejectionReason.Cooldown);
                }

                if (!Plan(settings.MaxPlaybacks, settings.LimitBehavior, LimitScope.Engine, d, o.Owner, ref victims) ||
                    !Plan(category.Max, category.Behavior, LimitScope.Category, d, o.Owner, ref victims) ||
                    !Plan(d.Limits.MaxPlaybacks, d.Limits.Behavior, LimitScope.Audio, d, o.Owner, ref victims) ||
                    !Plan(d.Limits.MaxPlaybacksPerOwner, d.Limits.Behavior, LimitScope.Owner, d, o.Owner, ref victims))
                {
                    lease?.Dispose();
                    return Reject(PlayRejectionReason.PlaybackLimit);
                }

                if (ext == null && pool.Count == 0 && poolCount >= settings.MaxPoolSize)
                {
                    var reusable = false;
                    if (victims != null)
                        foreach (var victim in victims)
                            if (victim.External == null)
                                reusable = true;
                    if (!reusable)
                    {
                        lease?.Dispose();
                        return Reject(PlayRejectionReason.PlaybackLimit);
                    }
                }

                ValidateProfile(o.SpatialProfile);
                var profile = o.SpatialProfile != null
                    ? new SpatialSettings(o.SpatialProfile)
                    : d.Profile;
                var pitch = o.Pitch.IsSpecified
                    ? o.Pitch.Value
                    : clipDefinition.Pitch + UnityEngine.Random.Range(-clipDefinition.PitchRandomRange, clipDefinition.PitchRandomRange);
                ValidatePitch(pitch);
#if UNITY_6000_3_OR_NEWER
                var region = clipDefinition.Resource as LoopRegionClip;
                if (region != null && pitch < 0)
                    throw new ArgumentException("Region playback requires positive pitch.");
#endif
                var loop = request != null ? request.ResolveLoop(o.Loop.IsSpecified ? o.Loop.Value : d.Loop)
                    : o.Loop.IsSpecified ? o.Loop.Value : d.Loop;
#if UNITY_6000_3_OR_NEWER
                if (ext == null && region != null)
                    region.GetRuntimeLoopFrames(out _, out _);
#endif
                if (victims != null)
                    foreach (var victim in victims)
                        End(victim, victim == previous ? PlaybackEndReason.Stopped : PlaybackEndReason.Stolen);
                source = ext != null ? ext.Source : pool.Count > 0 ? pool.Pop() : CreateSource();
                if (source == null) throw new InvalidOperationException("AudioSource was destroyed.");
                source.clip = ext != null ? ext.Snapshot.Clip : clip;
                source.loop = loop;
#if UNITY_6000_3_OR_NEWER
                if (ext == null && clipDefinition.Resource is UnityEngine.Audio.IAudioGenerator audioGenerator &&
                    !(clipDefinition.Resource is AudioClip))
                {
                    source.clip = null;
                    source.generator = audioGenerator;
                    if (region != null) source.loop = false;
                }
#endif
                source.pitch = pitch;
                source.outputAudioMixerGroup = category.Mixer;
                source.ignoreListenerPause = category.Ignore;
                var priority = o.Priority.IsSpecified ? o.Priority.Value : category.Priority;
                source.priority = 255 - Mathf.Clamp(priority, 0, 255);
                if (ext == null)
                {
                    ApplyProfile(source, profile);
                    source.spatialBlend = o.Mode == PlaybackMode.TwoD ? 0 :
                        o.SpatialProfile != null ? profile.Blend : 1;
                    source.transform.position = o.Mode == PlaybackMode.Attached
                        ? o.Target.TransformPoint(o.LocalOffset)
                        : o.Position;
                }
                else if (profile != null) ApplyProfile(source, profile);

                if (o.SpatialBlend.IsSpecified) source.spatialBlend = o.SpatialBlend.Value;
                var playback = GetReusablePlayback(request) ?? new Playback();
                playback.Key = d.Key;
                playback.LimitKey = d.LimitKey;
                playback.Library = d.Library;
                playback.Source = source;
                playback.Lease = lease;
                playback.External = ext;
                playback.ClipIndex = clipIndex;
                playback.Category = d.Category;
                playback.Owner = o.Owner;
                playback.HasOwner = !ReferenceEquals(o.Owner, null);
                playback.Target = o.Target;
                playback.Following = o.Mode == PlaybackMode.Attached;
                playback.Offset = o.LocalOffset;
                playback.BaseVolume = d.Volume * UnityEngine.Random.Range(
                        Mathf.Clamp01(clipDefinition.Volume - clipDefinition.VolumeRandomRange),
                        Mathf.Clamp01(clipDefinition.Volume + clipDefinition.VolumeRandomRange));
                playback.Volume = o.Volume.IsSpecified ? o.Volume.Value : 1;
                playback.Priority = priority;
                playback.Started = now;
                playback.StartFrame = Time.frameCount;
                playback.FadeIn = o.FadeInSeconds.IsSpecified ? o.FadeInSeconds.Value : 0;
                playback.TargetLost = o.TargetLostBehavior.IsSpecified
                        ? o.TargetLostBehavior.Value
                        : settings.TargetLostBehavior;
                playback.OwnerLost =
                        o.OwnerLostBehavior.IsSpecified ? o.OwnerLostBehavior.Value : settings.OwnerLostBehavior;
                playback.LossFadeOutSeconds = o.LossFadeOutSeconds.IsSpecified
                        ? o.LossFadeOutSeconds.Value
                        : settings.LossFadeOutSeconds;
#if UNITY_6000_3_OR_NEWER
                playback.Generator = generator;
#endif
                playback.Paused = playback.Stopping = playback.Ended = false;
                playback.FadeStart = 1;
                playback.FadeElapsed = playback.FadeDuration = 0;
                playback.StopReason = default;
                playback.Fade = playback.FadeIn > 0 ? 0 : 1;
                playback.Handle = request ?? new PlaybackRequest(this);
                ApplyVolume(playback);
                source.Play();
#if UNITY_6000_3_OR_NEWER
                if (ext == null && region != null)
                {
                    generator = LoopRegionPlayback.Find(source.generatorInstance)
                        ?? throw new InvalidOperationException("Loop region instance was not created.");
                    generator.SetLoop(loop);
                    playback.Generator = generator;
                }
#endif
                playbacks.Add(playback);
                playback.Handle.Attach(playback);
                if (d.Library != null || ext != null)
                {
                    cooldown[d.LimitKey] = now;
                    ownerCooldown[(d.LimitKey, owner)] = now;
                }
                return new PlayStartResult(true, PlayRejectionReason.None);
            }
            catch
            {
                if (source != null && ext == null)
                {
                    ResetSource(source);
                    pool.Push(source);
                }

                lease?.Dispose();
#if UNITY_6000_3_OR_NEWER
                generator?.Dispose();
#endif
                throw;
            }
            finally
            {
                if (victims != null)
                {
                    // Keep each plan private until End callbacks have returned, including reentrant Play calls.
                    victims.Clear();
                    victimSets.Push(victims);
                }
            }
        }

        private static bool MatchesLimit(Playback playback, LimitScope scope, Definition definition, UnityEngine.Object owner)
        {
            switch (scope)
            {
                case LimitScope.Category: return playback.Category == definition.Category;
                case LimitScope.Audio: return Equals(playback.LimitKey, definition.LimitKey);
                case LimitScope.Owner: return Equals(playback.LimitKey, definition.LimitKey) && playback.Owner == owner;
                default: return true;
            }
        }

        private bool Plan(int max, PlaybackLimitBehavior behavior, LimitScope scope, Definition definition,
            UnityEngine.Object owner, ref HashSet<Playback> victims)
        {
            if (max <= 0) return true;
            var count = 0;
            foreach (var v in playbacks)
                if ((victims == null || !victims.Contains(v)) && MatchesLimit(v, scope, definition, owner))
                    count++;
            while (count >= max)
            {
                if (behavior == PlaybackLimitBehavior.RejectNew) return false;
                Playback candidate = null;
                foreach (var v in playbacks)
                {
                    if (victims != null && victims.Contains(v) || !MatchesLimit(v, scope, definition, owner)) continue;
                    if (candidate == null || (behavior == PlaybackLimitBehavior.StopLowestPriority &&
                                              v.Priority < candidate.Priority) ||
                        ((behavior != PlaybackLimitBehavior.StopLowestPriority || v.Priority == candidate.Priority) &&
                         v.Started < candidate.Started)) candidate = v;
                }

                if (candidate == null) return false;
                AddVictim(ref victims, candidate);
                count--;
            }

            return true;
        }

        private AudioSource CreateSource()
        {
            var go = new GameObject("Playback");
            go.transform.SetParent(root.transform, false);
            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            poolCount++;
            ResetSource(source);
            return source;
        }

        private static void ResetSource(AudioSource s)
        {
            s.Stop();
#if UNITY_6000_3_OR_NEWER
            s.generator = null;
#endif
            s.clip = null;
            s.loop = false;
            s.volume = 1;
            s.pitch = 1;
            s.spatialBlend = 0;
            s.minDistance = 1;
            s.maxDistance = 500;
            s.spread = 0;
            s.dopplerLevel = 1;
            s.rolloffMode = AudioRolloffMode.Logarithmic;
            s.outputAudioMixerGroup = null;
            s.ignoreListenerPause = false;
            s.priority = 128;
            s.transform.localPosition = Vector3.zero;
            s.transform.localRotation = Quaternion.identity;
            s.transform.localScale = Vector3.one;
        }

        private static void ValidateProfile(SpatialProfile p)
        {
            if (p == null) return;
            ValidateNonNegative(p.MinDistance, "MinDistance");
            ValidateNonNegative(p.MaxDistance, "MaxDistance");
            ValidateNonNegative(p.DopplerLevel, "DopplerLevel");
            ValidateNonNegative(p.Spread, "Spread");
            if (p.Spread > 360) throw new ArgumentException("Spread exceeds 360 degrees.");
            if (p.MaxDistance < p.MinDistance) throw new ArgumentException("MaxDistance is below MinDistance.");
            if (float.IsNaN(p.SpatialBlend) || p.SpatialBlend < 0 || p.SpatialBlend > 1)
                throw new ArgumentException("Invalid SpatialBlend.");
            if (p.RolloffMode == AudioRolloffMode.Custom && p.RolloffCurve == null)
                throw new ArgumentException("Missing rolloff curve.");
        }

        private static void ApplyProfile(AudioSource s, SpatialSettings p)
        {
            if (p == null) return;
            s.spatialBlend = p.Blend;
            s.minDistance = p.Min;
            s.maxDistance = p.Max;
            s.spread = p.Spread;
            s.dopplerLevel = p.Doppler;
            if (p.Rolloff == AudioRolloffMode.Custom) s.SetCustomCurve(AudioSourceCurveType.CustomRolloff, p.Curve);
            s.rolloffMode = p.Rolloff;
        }

        internal void ApplyVolume(Playback v)
        {
            var c = categories[v.Category];
            if (v.Source != null)
                v.Source.volume = master * (c.Muted ? 0 : c.Volume) * v.BaseVolume * v.Volume * v.Fade;
        }
    }
}
