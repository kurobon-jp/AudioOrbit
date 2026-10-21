![AudioOrbit](docs/images/audioorbit-banner.png)

# AudioOrbit

[日本語](README.ja.md) | English

An audio library for Unity 2022.3 and later. Organize sound effects (SE), voice lines (VOICE), and music (BGM) into libraries, with a shared API for playback, preloading, fades, and completion.

## Installation

In Unity's Package Manager, select **Add package from git URL** and enter:

```text
https://github.com/kurobon-jp/AudioOrbit.git?path=Packages/com.github.kurobon.audioorbit/
```

AudioOrbit supports **Unity 2022.3 and later**. Only LoopRegionClip and generator playback require **Unity 6.3 or later**.

## Data model

| Type | Purpose |
|---|---|
| AudioEngineSettings | AudioSource pool, global playback limits, and behavior when an owner or followed target is destroyed |
| AudioLibrary | Asset containing Audio playback definitions and defaults for new entries |
| AudioDefinition | Key, display name, Category, Volume, Loop, selection mode, limits, and Clips |
| ClipDefinition | Candidate audio asset, Volume, Pitch, random ranges, and Weight |
| AudioCategory | Category volume, playback limits, AudioMixerGroup, and related settings |
| SpatialProfile | Spatial playback settings, including distance attenuation |
| PlaybackHandle | Handle for playback, stopping, and state notifications; reusable for repeated playback |

In this document, an Audio playback definition (AudioDefinition) stored in an AudioLibrary is called an “Audio entry.”

## EngineSettings

AudioEngineSettings is a ScriptableObject that configures engine-wide playback limits, the AudioSource pool, and behavior when a target or owner is destroyed.

Create it through `Assets > Create > AudioOrbit > Engine Settings` and pass it to `new AudioEngine(settings)`.

<img width="50%" src="docs/images/engine_settings.png">

| Setting | Description | Default |
|---|---|---|
| Initial Pool Size | Number of AudioSources prepared when the engine is created | 16 |
| Max Pool Size | Maximum number of AudioSources the engine creates; the pool expands as needed | 64 |
| Max Playbacks | Maximum number of simultaneous playbacks across the engine | 64 |
| Limit Behavior | Behavior at the playback limit: Reject New, Stop Oldest, or Stop Lowest Priority | Reject New |
| Target Lost Behavior | Behavior when the followed target Transform is destroyed: Stop, Fade Out, or Keep Last Position | Fade Out |
| Owner Lost Behavior | Behavior when the owner is destroyed: Stop, Fade Out, or Continue | Fade Out |
| Loss Fade Out Seconds | Fade duration in seconds when target or owner loss uses Fade Out | 0.1 |

Initial Pool Size must not exceed Max Pool Size. Target and owner loss settings can be overridden per playback through `PlayOptions`. Configure per-entry Volume, Loop, and Category in AudioLibrary.

## AudioLibrary

AudioLibrary is a ScriptableObject asset that stores playback definitions for sound effects, voice lines, and music. Each AudioDefinition contains a Key, volume, loop settings, category, and candidate Clips. Add multiple Clips to a definition to select them in sequence or by rules such as weighted random selection.

Group libraries by purpose or loading needs. Register them directly with an engine or load them through Resources or Addressables, then play their entries using keys or references.

Create one through `Assets > Create > AudioOrbit > Audio Library` and edit its contents in AudioLibraryWindow.

<img width="50%" src="docs/images/library.png">

AudioDefinition and ClipDefinition are serialized inside the library. You do not need a ScriptableObject per audio asset. Ordinary Clips use AudioClip. Unity 6.3 and later also support IAudioGenerator assets such as LoopRegionClip.

## First playback

1. Create settings using `Assets > Create > AudioOrbit > Engine Settings`.
2. Create a library using `Assets > Create > AudioOrbit > Audio Library`.
3. Open `Window > AudioOrbit > Library Manager`, select the library, and drop an AudioClip onto the list.
4. Set the Audio entry's Key to `attack` and assign the assets to this component.

```csharp
using AudioOrbit;
using UnityEngine;

public sealed class AudioExample : MonoBehaviour
{
    [SerializeField] private AudioEngineSettings settings;
    [SerializeField] private AudioLibrary library;
    private AudioEngine engine;
    private PlaybackHandle attack;

    private void Awake()
    {
        engine = new AudioEngine(settings);
        engine.RegisterLibrary(library);
        attack = engine.CreatePlayback("attack");
        attack.StateChanged += (_, state) => Debug.Log(state);
    }

    public void PlayAttack()
    {
        attack.Play();
    }

    private void OnDestroy()
    {
        attack?.Dispose();
        engine?.Dispose();
    }
}
```

The scene needs an AudioListener. Without options, playback is 2D and uses the Audio entry's Volume, Loop, and Clip selection settings. Call engine APIs on the Unity main thread.

## Controlling a PlaybackHandle

Keep the handle returned by `CreatePlayback()` and use it to control playback from input or events.

The following component excerpt uses an initialized `engine`. Call `PlayAttack()` and `StopAttack()` from separate inputs or events.

```csharp
private PlaybackHandle playback;

private void Awake()
{
    playback = engine.CreatePlayback("attack");
    playback.StateChanged += (handle, state) => Debug.Log(state);
}

// Call from a play button or attack event.
public void PlayAttack() => playback.Play();

// Call from a stop button or another event that stops playback.
public void StopAttack() => playback.Stop(fadeSeconds: 0.2f);
```

`Play()` and `Stop()` return immediately while the engine handles loading and fades. Calling `Play()` again on the same handle immediately stops the current run and starts a new one, including during loading or a fade stop. Use multiple handles for overlapping playback. Disposing a handle when its owner is destroyed also stops playback.

### Monitoring state

A new handle starts in `Idle`. Subscribe with `playback.StateChanged += OnStateChanged;`. Callback arguments are `(PlaybackHandle handle, PlaybackState state)`.

- Notifications are queued to the Unity main thread only when state changes, and delivered in order.
- The completion result is finalized before the Ended notification.
- Subscriber exceptions are logged without preventing other notifications.
- Past transitions are not replayed. Read `playback.State` to initialize UI immediately after subscribing. Synchronous playback may reach Playing before you subscribe.
- Notifications contain the state at the time of the transition. The current `State` may have advanced before the notification arrives.

### Engine-wide stop operations

The engine provides StopAll, StopCategory, StopOwner, and StopLibrary, each with an Async counterpart. Fades use unscaled time.

## AudioLibraryLoader

`IAudioLibraryLoader` defines how libraries are loaded. Passing a loader to `engine.PreloadAsync(loader, token)` registers and caches its libraries in the engine. You can then pass an entry key to `CreatePlayback("voice_attack")`. The loader does not hold the key of an Audio entry to play.

### Loading from Resources

```csharp
var loader = ResourcesAudioLibraryLoader.FromPath("Audio/VoiceLibrary");
await engine.PreloadAsync(loader, token);
var playback = engine.CreatePlayback("voice_attack");
playback.Play();
```

This loads the entire library at a Resources-relative path, such as `Assets/Game/Resources/Audio/VoiceLibrary.asset`.

### Addressables: address or label

```csharp
using AudioOrbit.Addressables;

var loader = AddressablesAudioLibraryLoader.FromKey("audio/voice/character01");
await engine.PreloadAsync(loader, token);
var playback = engine.CreatePlayback("voice_attack");
playback.Play();
```

`FromKey` accepts an address or label. A label can load multiple libraries together.

### Loader ownership and releasing caches

- The caller disposes loaders passed directly to `PreloadAsync(loader, token)`.
- Loaders share their cache and concurrent loads. Disposing a loader releases its own cache; existing leases remain valid until their last use ends.
- Playback completion does not release the engine cache. Use `engine.ReleaseLibrary(loader)` or dispose the engine.
- `CreatePlayback(string key)` uses a key in registered libraries.

## LibraryReferences

`IAudioLibraryReference` identifies the Library to load. The Resources implementation stores a path; the Addressables implementation stores an AssetReference.

```csharp
var libraryReference = new ResourcesLibraryReference { LibraryPath = "Audio/VoiceLibrary" };
await engine.PreloadAsync(libraryReference, token);
// When the Library cache is no longer needed
engine.ReleaseLibrary(libraryReference);
```

## AudioReference

`IAudioReference` identifies what to play. Resources and Addressables references combine library loading information with an Audio entry key. Create a handle with `engine.CreatePlayback(reference)` and call `Play()` to load an unloaded library automatically. ResourcesAudioReference and AddressablesAudioReference implement `IAudioReference` and hold a Library reference plus an Audio entry key. The engine manages loaders created by Library references.

Engine Audio Reference uses a registered key, while Direct Audio Reference points directly to a Clip; neither loads a library automatically.

`IAudioReference` is the common playback reference type. `IAudioLibraryReference` is independent: it identifies a Library and creates its loader, without an Audio entry key. `PreloadAsync(libraryReference)` accepts a Library reference and retains the entire library.

### References and caching

- Preloading is optional. An unloaded library loads before playback starts. Use `await engine.PreloadAsync(reference.LibraryReference, token)` during preparation only to avoid loading delays when audio is triggered.
- `PreloadAsync(reference.LibraryReference)` caches the entire library independently of the playback key.
- Neither automatic loading nor preloading releases the cache when playback ends. Call `engine.ReleaseLibrary(reference.LibraryReference)` or dispose the engine. Active playback retains its resources until it ends, even after explicit cache release.
- Concurrent requests share loading. The engine cache and active playback retain library resources.
- `CreatePlayback("attack")` uses a registered library key. `CreatePlayback(reference)` also supports automatic library loading at playback time.

### Resources references

`ResourcesAudioReference` holds a Library of type `ResourcesLibraryReference` plus an Audio entry key. The Library reference stores the Resources-relative path. Its Inspector provides a Library field, Audio selector, and button to open Library Manager.

```csharp
private ResourcesAudioReference reference = new ResourcesAudioReference
{
    Library = new ResourcesLibraryReference { LibraryPath = "Audio/VoiceLibrary" },
    Key = "voice_attack"
};

// In the first example's Awake, replace RegisterLibrary and CreatePlayback("attack") with:
attack = engine.CreatePlayback(reference);
```

For example, place the library at `Assets/Game/Resources/Audio/VoiceLibrary.asset`. Use the first example's `PlayAttack()` and destruction cleanup.

Declare `[SerializeReference] private IAudioLibraryReference library;` to configure only a Library, choosing ResourcesLibraryReference or AddressablesLibraryReference without an Audio selector. Use `IAudioReference` for playback references. Arrays are supported.

The Resources backend releases references when leases end. Native memory reclamation follows Unity's unused-asset unloading. It does not call `Resources.UnloadAsset`, which could affect other users. Keep Resources paths unique.

### Playing through Addressables

Register the library itself as Addressable. Clips load as library dependencies. For Resources, use Core's `ResourcesAudioReference`.

```csharp
using AudioOrbit.Addressables;

[SerializeField] private AddressablesAudioReference attackReference;

// In the first example's Awake, replace RegisterLibrary and CreatePlayback("attack") with:
attack = engine.CreatePlayback(attackReference);
```

Use the first example's `PlayAttack()` and destruction cleanup. Unloaded libraries load automatically when playback starts. Preload during preparation to avoid a loading delay when audio is triggered.

```csharp
await engine.PreloadAsync(attackReference.Library, token);
```

In the Inspector, select an Addressable library and an Audio entry. Open navigates to that entry in Library Manager. The Library reference stores an AssetReference; the playback reference stores a Library reference and Key. Include the library GUID in the Addressables catalog.

### Direct Clip playback

Play an AudioClip or ClipDefinition directly without creating a library.

```csharp
var playback = engine.CreatePlayback(new DirectAudioReference(audioClip));
playback.Play();
```

You can create multiple handles from the same `DirectAudioReference` and play them simultaneously. Each playback keeps its own Volume, Pitch, and other settings while sharing the AudioClip.

- Direct references use the uncategorized category and global playback limits.
- Set SpatialProfile through `PlayOptions`; otherwise standard spatial settings apply.
- Weight is unused for a single Clip.
- Set ClipDefinition.Generator to a LoopRegionClip on Unity 6.3 or later.
- The caller manages asset loading and ownership.

Select Direct Audio Reference in the AudioPlayer Inspector to configure it there.

### Custom backends

Implement `IAudioLibraryReference` and `IAudioLibraryLoader` for a custom library backend. Playback references implement `IAudioReference`, returning the entry key through `Key` and the Library location through `LibraryReference`. Direct Clip and registered-key references return null for LibraryReference. The engine owns loaders created by Library references; the caller owns loaders passed directly to `PreloadAsync(loader)`.

## Playback and stopping

```csharp
var options = PlayOptions.TwoD();
options.FadeInSeconds = 0.5f;
var playback = engine.CreatePlayback("bgm_battle");
playback.Play(options);

playback.Pause();
playback.Resume();
playback.Stop(fadeSeconds: 1f);
```

### Waiting for playback and stopping

`PlayAsync` starts playback and waits for completion. Canceling its token also stops the playback or pending playback request started by that call. `WaitForCompletionAsync` waits on an existing run; its token cancels only the wait.

```csharp
var result = await playback.PlayAsync(token);
// With playback options
var nextResult = await playback.PlayAsync(options, token);
```

- Cancellation completes with `OperationCanceledException`.
- An already-canceled token starts no playback and leaves any existing run unchanged.
- Canceling an old token after reusing the handle does not stop the new run.
- Shared library loading and the engine cache are retained.

Use `WaitForCompletionAsync()` to advance dialogue after a voice line ends, or `StopAsync()` to continue after a fade stop finishes.

```csharp
// Inside an async method: wait until playback ends.
using var playback = engine.CreatePlayback("attack");
playback.Play();
var result = await playback.WaitForCompletionAsync(token);
```

```csharp
// Fade an active handle out and wait until it stops.
await playback.StopAsync(fadeSeconds: 1f, cancellationToken: token);
```

`using var` disposes the handle and stops playback when the method exits. To keep playback active between events, store the handle in a field as in the first example.

An existing wait remains tied to the run active when it was called, even if the handle is later reused.

#### PlaybackHandle APIs

| API | Behavior |
|---|---|
| `Play(...)` | Starts playback and returns void; the same handle can stop a pending load |
| `PlayAsync(options, token)` | Starts playback and waits for completion; its token also stops that run |
| `WaitForCompletionAsync(token)` | Waits through loading until playback ends or fails |
| `Stop(fadeSeconds)` | Requests a stop and returns void |
| `StopAsync(fadeSeconds, token)` | Requests a stop and waits for completion |

#### Engine APIs

| API | Behavior |
|---|---|
| `PreloadAsync(...)` | Waits for loading into the engine cache |
| `ReleaseLibrary(...)` | Releases the library cache; active playback retains its resources until it ends |

The handle itself is not awaitable. `Play` does not accept a `CancellationToken`. Use `Stop` or the token passed to `PlayAsync` to cancel playback. Tokens passed to `WaitForCompletionAsync` or `StopAsync` cancel only the wait; playback and previously requested stop fades continue.

### Fades and positioning

FadeInSeconds controls the volume ramp at playback start. Stop's `fadeSeconds` controls the ramp down before stopping. A crossfade overlaps the new track's fade-in with the previous track's fade-out.

Use `PlayOptions.At(position)` for a fixed position and `PlayOptions.Attached(transform)` to follow a Transform.

## AudioLibraryWindow

AudioLibraryWindow manages playback definitions in a library. Open it through `Window > AudioOrbit > Library Manager`.

1. Select a library, or create one with New Library.
2. Drop AudioClips onto the list or use Import Folder to import a Project folder. Use + Audio to add an empty definition.
3. Select a definition to edit its Key, playback settings, and candidate Clips. Search or sort columns to find entries, and use Duplicate or Remove to manage them.
4. Audition with Preview Audio or Preview Clip, then save changes with Save.

<img width="50%" src="docs/images/library_window.png">

- **Audio**: Set Key, Display Name, Category, Volume, Loop, and other settings.
- **Clips**: Assign candidate audio assets and configure Volume, Pitch, random ranges, and Weight.
- **Selection Mode**: Choose Sequence (default), Weighted Random, Avoid Immediate Repeat, or Shuffle.
- **New Audio Defaults**: Defaults for newly added Audio entries; see below.
- **Import Folder**: Add AudioClips in the Project. External files are not copied, and Clips already referenced by the library are skipped.

Preview Audio applies the selection rules; Preview Clip auditions a specific asset. Waveforms display each channel separately and support seeking by clicking or dragging. Edits support Undo, including batch changes. Changes are stored in the library and persisted with Save.

Keys are case-sensitive strings. Empty and duplicate keys, including duplicates in other libraries, are validated during editing and rejected during registration. An incomplete Audio entry, such as one without a Clip, fails only when that entry is played; it does not prevent other entries from playing.

| Selection Mode | Behavior |
|---|---|
| Sequence (default) | Plays in list order, wrapping back to the first Clip |
| Weighted Random | Selects according to Weight |
| Avoid Immediate Repeat | Uses weighted selection while avoiding the previous Clip |
| Shuffle | Plays candidates in a shuffled order |

Clips with zero Weight are excluded. Preview Clip auditions the specified Clip independently of the selection rules.

### New Audio Defaults

New Audio Defaults includes Category, Spatial Profile, Volume, Loop, Selection Mode, playback limits, and cooldowns.

- Values are copied when adding Audio entries, dropping Clips, or importing a folder. Existing entries are unaffected.
- Limit settings are copied separately for each Audio entry.
- Duplicate keeps the source entry's settings.

Add an entry from code as follows:

```csharp
var audio = library.CreateAudio(); // Copies defaults; does not insert the entry.
audio.Key = "attack";
audio.DisplayName = "Attack";
audio.Clips.Add(new ClipDefinition { Clip = audioClip });
library.Entries.Add(audio);
```

## Intro, loop, and outro

**Requires Unity 6.3 or later.** Earlier versions exclude LoopRegionClip and Generator APIs from compilation, and the Inspector accepts only AudioClip assets.

<img width="50%" src="docs/images/loop_region_clip.png">

### Creating and configuring an asset

Create an asset using `Assets > Create > AudioOrbit > Loop Region Clip`, then set its AudioClip, Loop Start Frame, and Loop End Frame Exclusive. Assign that asset to the Audio entry's Clip field.

The region is `[start, end)` in sample frames. With Loop enabled, playback starts with the intro and repeats the region.

### Reference Sample Rate

LoopRegionClip's Reference Sample Rate stores the rate at which Loop Start Frame and Loop End Frame Exclusive were authored. Runtime scales both boundaries to `Clip.frequency` and validates the resulting range.

- Assigning a Clip to a newly created asset initializes the reference rate. Subsequent import changes never overwrite it.
- Existing assets with an unset value display a warning. Confirm and enter the original authoring rate.
- Use Current Clip Sample Rate only when the saved positions use the current Clip rate.
- Set `ReferenceSampleRate` explicitly when creating a region from code.

### Playback example

The following component excerpt uses an initialized `engine`. Call `PlayBgm()` when the BGM starts and `PlayOutro()` from the event that triggers the outro. Dispose the handle as shown in the initialization example.

```csharp
private PlaybackHandle bgm;

private void Awake()
{
    bgm = engine.CreatePlayback("bgm_battle");
}

// Call from the event that starts the BGM.
public void PlayBgm()
{
    var options = PlayOptions.TwoD();
    options.Loop = true;
    bgm.Play(options);
}

// Call from a separate event to continue through the region end into the outro.
public void PlayOutro() => bgm.Loop = false;
```

Setting `PlaybackHandle.Loop` to `false` during playback ends looping and continues through the region end into the outro.

### Constraints and memory

DSP processing handles boundaries using one AudioSource. Concurrent playbacks of the same asset have independent playback state and share PCM data. Changes do not retroactively affect buffers already queued for output.

In the source AudioClip's Import Settings, **set Load Type to Decompress On Load**. LoopRegionClip does not support Compressed In Memory or Streaming. The clip must be mono or stereo, and Pitch must be positive (normally `1`). Ordinary AudioClip playback supports Compressed In Memory and Streaming.

Region playback retains an additional decoded float PCM copy of the entire clip. Allow approximately `duration in seconds × sample rate × channel count × 4 bytes` of extra memory: about 66 MiB for a three-minute, 48 kHz stereo clip. The initial copy also allocates a temporary array of approximately the same size. Simultaneous playbacks of the same AudioClip share the PCM copy.

## External AudioSources

```csharp
var source = engine.RegisterExternalSource(audioSource);
var playback = engine.CreatePlayback(source);
playback.Play();
```

This plays the Clip assigned to the AudioSource. AudioOrbit controls the source while registered, so disable `playOnAwake`. `ExternalSourceOptions.Key` is optional. Without a Key, limits are tracked per registration. An explicit Key shares limits and cooldowns with other playback using that Key.

## OrbitContext

OrbitContext is a ScriptableObject that manages settings and lifetime for AudioEngines shared by multiple components. Instead of creating and storing an Engine yourself, use `OrbitContext.Get()` for the default Engine or `OrbitContext.Get("BGM")` for a specified Key. The same Key returns the same Engine regardless of the caller.

![OrbitContext](docs/images/context.png)

Configure each Engine definition as follows:

| Setting | Description |
|---|---|
| Key | String identifying an Engine; defaults to `Default`. This is separate from an Audio entry's Key |
| Settings | AudioEngineSettings for that Engine |
| Libraries | AudioLibraries registered directly when the Engine is created. Optional when using only Resources or Addressables references, for example |

`Get` creates an Engine on first access and registers its Libraries. `TryGet(key, out engine)` retrieves only an existing Engine and never creates one. The Context owns shared Engines, so components using them must not dispose them. Each component manages the PlaybackHandles it creates.

OrbitContext is optional. To own an Engine yourself, create it with `new AudioEngine(settings)` and dispose it as shown in the first playback example. AudioPlayer uses an Engine configured in OrbitContext.


**Loading and registration**

- The Context loads through Preloaded Assets. At runtime, `OnEnable` registers its instance and rejects replacement by another Context.
- Runtime access does not search for a Context.
- In the Editor, only the single Context configured in Preloaded Assets registers its instance. Only Edit Mode preview falls back to Preloaded Assets when no instance is registered.

**Engine management**

- Only configured keys create Engines, lazily.
- Each Key has independent Settings and directly registered Libraries.
- Engines have independent pools, playback limits, batch stop operations, and library loading state.
- Empty and duplicate keys show Inspector errors. Get throws for unknown keys.
- The Player Engine Key selector lists configured keys.

**Disposal**

- All Engines are disposed when the application exits or the Context is released.

**Editor playback**

- `OrbitContext.Get()` also supports Edit Mode playback. A dedicated Preview Scene and Editor clock handle natural completion, fades, and cooldowns.
- Preview resources are released when entering or leaving Play Mode, before assembly reload, and on Editor exit.
- AudioPlayer automatically starts only in Play Mode. Invoke Play explicitly for Edit Mode preview.

## Automatic playback with AudioPlayer

1. Create a Context ScriptableObject through `Create > AudioOrbit > Orbit Context` and click Assign in its Inspector to register it in Preloaded Assets. Register only one Context.
2. Add Engines with Key, Settings, and optional Libraries.
3. Add AudioPlayer and select an Engine. The default is `Default`. Identical keys share an engine; different keys select independent engines. Keys are case-sensitive.
4. Choose a Reference Type and configure Reference according to the table below.
5. With Play On Awake enabled, playback starts whenever the component becomes enabled in Play Mode.

![AudioPlayer](docs/images/audioplayer.png)

| Reference Type | Configuration and behavior |
|---|---|
| Engine Audio Reference | Enter the Key of an Audio entry registered with the selected Engine as text |
| Direct Audio Reference | Assign an AudioClip directly |
| Resources Audio Reference | Select a library inside a Resources folder and an Audio entry. Unloaded libraries load automatically when playback starts |
| Addressables Audio Reference | Select an Addressable library and an Audio entry. Unloaded libraries load automatically when playback starts |

For Engine Audio Reference, register the library through the OrbitContext Engine settings or code before playback. The Inspector accepts arbitrary keys; lookup happens when playback starts.

Disabling a player stops only its own playback and pending playback request. Calling `Play()` again stops its previous run and starts a new one.

```csharp
var engine = OrbitContext.Get();
var bgm = OrbitContext.Get("BGM"); // Define BGM in the Context first.
await engine.PreloadAsync(reference.LibraryReference, token);
var playback = engine.CreatePlayback(reference);
playback.Play();
await playback.StopAsync(fadeSeconds: 0.5f, cancellationToken: token);

// Control an existing engine without creating one.
if (OrbitContext.TryGet("BGM", out var existing)) existing.StopAll();
```

Player Mode supports TwoD, AtPosition, and Attached. Each player is its playback Owner. Playback exposes the current handle. Fade In Seconds controls the start fade; Stop and StopAsync accept the stop fade duration.
