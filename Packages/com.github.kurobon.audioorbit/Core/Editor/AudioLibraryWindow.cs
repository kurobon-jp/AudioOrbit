using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace AudioOrbit.Editor
{
    public sealed class AudioLibraryWindow : EditorWindow
    {
        [SerializeField] private AudioLibrary library;
        private SerializedObject serializedLibrary;
        private MultiColumnListView table;
        private ScrollView details;
        private VisualElement preview;
        private ObjectField libraryField;
        private ToolbarButton importFolder;
        private ToolbarSearchField search;
        private readonly List<int> rows = new List<int>();
        private int selected = -1;
        private bool rebuildingRows;
        private bool navigationSelection;
        private int navigationVersion;
        private AudioClip previewClip;
        private AudioWaveform waveform;
        private Label clipInfo;
        private AudioPreview audition;
        private int previewClipIndex;
        private string previewOrigin;
        private Label previewStatus;
        private Button previewAudioButton;
        private ToolbarButton pauseButton, resumeButton, stopButton;

        [MenuItem("Window/AudioOrbit/Library Manager")]
        public static void Open() => ShowLibrary(Selection.activeObject as AudioLibrary);

        public static void ShowLibrary(AudioLibrary asset) => OpenLibrary(asset);

        private static AudioLibraryWindow OpenLibrary(AudioLibrary asset)
        {
            var window = GetWindow<AudioLibraryWindow>("AudioOrbit Library Manager");
            window.navigationVersion++;
            window.navigationSelection = false;
            window.minSize = new Vector2(850, 480);
            if (asset != null && window.library != asset) window.selected = -1;
            if (asset != null) window.library = asset;
            if (window.table == null) window.Rebuild();
            else
            {
                window.libraryField.SetValueWithoutNotify(window.library);
                window.Refresh();
            }

            window.Show();
            return window;
        }

        public void CreateGUI() => Rebuild();

        public static void ShowAudio(AudioLibrary asset, int soundIndex)
        {
            if (asset == null) throw new ArgumentNullException(nameof(asset));
            if (soundIndex < 0 || soundIndex >= asset.Entries.Count)
                throw new ArgumentOutOfRangeException(nameof(soundIndex));
            var window = OpenLibrary(asset);
            window.NavigateToAudio(soundIndex);
        }

        private void NavigateToAudio(int soundIndex)
        {
            var asset = library;
            var version = ++navigationVersion;

            void SelectAudio()
            {
                if (this == null || version != navigationVersion || library != asset || asset == null ||
                    soundIndex >= asset.Entries.Count) return;
                search.SetValueWithoutNotify(string.Empty);
                selected = soundIndex;
                navigationSelection = true;
                RefreshRows();
                ShowDetails();
                table.ScrollToItem(rows.IndexOf(soundIndex));
            }

            SelectAudio();
            rootVisualElement.schedule.Execute(SelectAudio);
            EditorApplication.delayCall += SelectAudio;
        }

        private void OnEnable() => Undo.undoRedoPerformed += Refresh;

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= Refresh;
            ResetPreview();
            waveform?.Dispose();
            rootVisualElement.Unbind();
            serializedLibrary?.Dispose();
        }

        private void Rebuild()
        {
            var styles =
                AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/com.audioorbit.core/Editor/AudioLibraryStyles.uss");
            if (styles != null && !rootVisualElement.styleSheets.Contains(styles))
                rootVisualElement.styleSheets.Add(styles);
            ResetPreview();
            waveform?.Dispose();
            rootVisualElement.Unbind();
            rootVisualElement.Clear();
            var toolbar = new Toolbar();
            libraryField = new ObjectField("Library")
                { objectType = typeof(AudioLibrary), allowSceneObjects = false, value = library };
            libraryField.style.minWidth = 280;
            libraryField.RegisterValueChangedCallback(e =>
            {
                if (library == e.newValue) return;
                navigationVersion++;
                navigationSelection = false;
                library = e.newValue as AudioLibrary;
                selected = -1;
                StopPreview();
                Refresh();
            });
            toolbar.Add(libraryField);
            toolbar.Add(new ToolbarMenu { text = "Libraries" });
            var menu = (ToolbarMenu)toolbar[1];
            foreach (var guid in AssetDatabase.FindAssets("t:AudioLibrary"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                menu.menu.AppendAction(path,
                    _ => libraryField.value = AssetDatabase.LoadAssetAtPath<AudioLibrary>(path));
            }

            toolbar.Add(new ToolbarButton(CreateLibrary) { text = "New Library" });
            toolbar.Add(new ToolbarButton(() => AssetDatabase.SaveAssets()) { text = "Save" });
            rootVisualElement.Add(toolbar);
            var actions = new Toolbar();
            search = new ToolbarSearchField();
            search.style.flexGrow = 1;
            search.RegisterValueChangedCallback(_ =>
            {
                navigationVersion++;
                navigationSelection = false;
                RefreshRows();
            });
            actions.Add(search);
            actions.Add(new ToolbarButton(() => AddClips(Array.Empty<AudioClip>())) { text = "+ Audio" });
            importFolder = new ToolbarButton(() =>
            {
                var target = library;
                AudioFolderImportWindow.ShowFor(target, () =>
                {
                    if (this == null || library != target) return;
                    selected = library.Entries.Count - 1;
                    Refresh();
                });
            }) { text = "Import Folder", tooltip = "Add AudioClips from a Project folder to this Library." };
            importFolder.SetEnabled(library != null);
            actions.Add(importFolder);
            actions.Add(new ToolbarButton(Duplicate) { text = "Duplicate" });
            actions.Add(new ToolbarButton(Remove) { text = "Remove" });
            rootVisualElement.Add(actions);
            var split = new TwoPaneSplitView(1, 360, TwoPaneSplitViewOrientation.Horizontal);
            split.style.flexGrow = 1;
            table = new MultiColumnListView
            {
                itemsSource = rows, fixedItemHeight = 24, selectionType = SelectionType.Single,
                showAlternatingRowBackgrounds = AlternatingRowBackground.All, viewDataKey = "audioorbit-library-table"
            };
#if UNITY_6000_3_OR_NEWER
            table.sortingMode = ColumnSortingMode.Custom;
#else
#pragma warning disable CS0618
            table.sortingEnabled = true;
#pragma warning restore CS0618
#endif
            AddColumn("Name", 200, s => s.DisplayName);
            AddColumn("Key", 200, s => s.Key);
            AddColumn("Category", 110, s => s.Category != null ? s.Category.name : "—");
            AddColumn("Clips", 55, s => (s.Clips?.Count ?? 0).ToString());
            AddColumn("Volume", 65, s => s.Volume.ToString("0.##"));
            AddColumn("Loop", 55, s => s.Loop ? "Yes" : "—");
            AddColumn("Selection", 110, s => s.SelectionMode.ToString());
            table.columnSortingChanged += RefreshRows;
            table.RegisterCallback<PointerDownEvent>(_ =>
            {
                navigationVersion++;
                navigationSelection = false;
            }, TrickleDown.TrickleDown);
            table.RegisterCallback<KeyDownEvent>(_ =>
            {
                navigationVersion++;
                navigationSelection = false;
            }, TrickleDown.TrickleDown);
            table.selectionChanged += values =>
            {
                if (rebuildingRows) return;
                var next = values.OfType<int>().DefaultIfEmpty(-1).First();
                if (navigationSelection && rows.Contains(selected))
                {
                    table.SetSelectionWithoutNotify(new[] { rows.IndexOf(selected) });
                    return;
                }

                selected = next;
                ShowDetails();
            };
            table.RegisterCallback<DragUpdatedEvent>(e =>
            {
                if (library == null || DraggedClips().Length == 0) return;
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                e.StopImmediatePropagation();
            }, TrickleDown.TrickleDown);
            table.RegisterCallback<DragPerformEvent>(e =>
            {
                if (library == null) return;
                var clips = DraggedClips();
                if (clips.Length == 0) return;
                e.StopImmediatePropagation();
                DragAndDrop.AcceptDrag();
                AddClips(clips);
            }, TrickleDown.TrickleDown);
            split.Add(table);
            var right = new VisualElement();
            details = new ScrollView { name = "audioorbit-library-details" };
            details.contentContainer.AddToClassList("audioorbit-details");
            details.RegisterCallback<ClipPreviewEvent>(e =>
            {
                PlayPreview(e.Index);
                e.StopPropagation();
            });
            details.RegisterCallback<SerializedPropertyChangeEvent>(OnPropertyChanged);
            details.RegisterCallback<ClipChangedEvent>(_ => UpdatePreviewAudioButton());
            details.style.flexGrow = 1;
            right.Add(details);
            preview = new VisualElement();
            preview.AddToClassList("audioorbit-preview");
            preview.style.paddingLeft = preview.style.paddingRight = 8;
            preview.style.paddingBottom = 8;
            var previewToolbar = new Toolbar();
            var previewTitle = new Label("Preview");
            previewTitle.AddToClassList("audioorbit-section-title");
            previewToolbar.Add(previewTitle);
            pauseButton = new ToolbarButton(() => audition?.Pause()) { text = "Ⅱ Pause" };
            resumeButton = new ToolbarButton(() => audition?.Resume()) { text = "▶ Resume" };
            stopButton = new ToolbarButton(StopPreview) { text = "■ Stop" };
            previewToolbar.Add(pauseButton);
            previewToolbar.Add(resumeButton);
            previewToolbar.Add(stopButton);
            preview.Add(previewToolbar);
            previewStatus = new Label
            {
                style =
                {
                    whiteSpace = WhiteSpace.Normal, unityFontStyleAndWeight = FontStyle.Bold,
                    unityTextAlign = TextAnchor.MiddleLeft
                }
            };
            preview.Add(previewStatus);
            clipInfo = new Label();
            preview.Add(clipInfo);
            waveform = new AudioWaveform();
            waveform.style.height = 150;
            waveform.SeekRequested += fraction =>
            {
                audition?.Seek(fraction);
                UpdatePlayhead();
            };
            preview.Add(waveform);
            right.Add(preview);
            split.Add(right);
            rootVisualElement.Add(split);
            rootVisualElement.Add(
                new Label("Drop AudioClips onto the list to add them. Changes are stored in the Library.")
                    { style = { paddingLeft = 8, paddingBottom = 4 } });
            waveform.schedule.Execute(UpdateWaveform).Every(200);
            waveform.schedule.Execute(UpdatePlayhead).Every(33);
            Refresh();
        }

        private void AddColumn(string title, float width, Func<AudioDefinition, string> text)
        {
            table.columns.Add(new Column
            {
                name = title, title = title, width = width, sortable = true, stretchable = title == "Name",
                makeCell = () => new Label { style = { paddingLeft = 8 } },
                bindCell = (element, index) => ((Label)element).text =
                    library != null && index < rows.Count ? text(library.Entries[rows[index]]) : ""
            });
        }

        private static AudioClip[] DraggedClips()
        {
            var clips = DragAndDrop.objectReferences.OfType<AudioClip>().Distinct().ToArray();
            if (clips.Length > 0) return clips;
            return DragAndDrop.paths.Where(p => (p.StartsWith("Assets/", StringComparison.Ordinal) ||
                                                 p.StartsWith("Packages/", StringComparison.Ordinal)) &&
                                                !AssetDatabase.IsValidFolder(p))
                .SelectMany(AssetDatabase.LoadAllAssetsAtPath).OfType<AudioClip>().Distinct().ToArray();
        }

        private void Refresh()
        {
            importFolder?.SetEnabled(library != null);
            details?.Unbind();
            serializedLibrary?.Dispose();
            serializedLibrary = library != null ? new SerializedObject(library) : null;
            RefreshRows();
            ShowDetails();
        }

        private void RefreshRows()
        {
            if (table == null) return;
            rows.Clear();
            if (library != null)
                for (var i = 0; i < library.Entries.Count; i++)
                    if (library.Entries[i] != null && (string.IsNullOrEmpty(search.value) ||
                                                       (library.Entries[i].DisplayName ?? "").IndexOf(search.value,
                                                           StringComparison.OrdinalIgnoreCase) >= 0))
                        rows.Add(i);
            var sorting = table.sortedColumns.ToArray();
            if (library != null && sorting.Length > 0)
                rows.Sort((left, right) =>
                {
                    foreach (var sort in sorting)
                    {
                        var comparison = CompareColumn(library.Entries[left], library.Entries[right],
                            sort.column?.name ?? sort.columnName);
                        if (comparison != 0)
                            return sort.direction == SortDirection.Descending ? -comparison : comparison;
                    }

                    return left.CompareTo(right);
                });
            rebuildingRows = true;
            try
            {
                table.Rebuild();
                table.SetSelectionWithoutNotify(rows.IndexOf(selected) < 0
                    ? Array.Empty<int>()
                    : new[] { rows.IndexOf(selected) });
            }
            finally
            {
                rebuildingRows = false;
            }
        }

        private static int CompareColumn(AudioDefinition left, AudioDefinition right, string column) => column switch
        {
            "Key" => StringComparer.Ordinal.Compare(left.Key, right.Key),
            "Name" => StringComparer.OrdinalIgnoreCase.Compare(left.DisplayName ?? "", right.DisplayName ?? ""),
            "Category" => StringComparer.OrdinalIgnoreCase.Compare(left.Category != null ? left.Category.name : "",
                right.Category != null ? right.Category.name : ""),
            "Clips" => (left.Clips?.Count ?? 0).CompareTo(right.Clips?.Count ?? 0),
            "Volume" => left.Volume.CompareTo(right.Volume),
            "Loop" => left.Loop.CompareTo(right.Loop),
            "Selection" => StringComparer.OrdinalIgnoreCase.Compare(left.SelectionMode.ToString(),
                right.SelectionMode.ToString()),
            _ => 0
        };

        private void ShowDetails()
        {
            if (details == null) return;
            details.Unbind();
            details.Clear();
            ResetPreview();
            previewClip = null;
            previewClipIndex = 0;
            previewOrigin = null;
            if (library != null && serializedLibrary != null)
            {
                serializedLibrary.Update();
            }

            if (library == null || selected < 0 || selected >= library.Entries.Count)
            {
                details.Add(new HelpBox("Select a Library and an Audio entry.", HelpBoxMessageType.Info));
                UpdateWaveform();
                return;
            }

            serializedLibrary.Update();
            var property = serializedLibrary.FindProperty("definitions").GetArrayElementAtIndex(selected);
            var soundSection = new VisualElement();
            soundSection.AddToClassList("audioorbit-section");
            details.Add(soundSection);
            var soundPreview = new VisualElement();
            soundPreview.AddToClassList("audioorbit-section-header");
            var soundTitle = new Label("Audio");
            soundTitle.AddToClassList("audioorbit-section-title");
            soundPreview.Add(soundTitle);
            previewAudioButton = new Button(() => PlayPreview(null))
            {
                text = "▶ Preview Audio", tooltip = "Preview the Audio using its Selection Mode and playback settings."
            };
            UpdatePreviewAudioButton();
            previewAudioButton.TrackSerializedObjectValue(property.serializedObject, _ => UpdatePreviewAudioButton());
            soundPreview.Add(previewAudioButton);
            soundSection.Add(soundPreview);
            foreach (var field in new[] { "Key", "DisplayName", "Category", "Volume", "Loop" })
                soundSection.Add(new PropertyField(property.FindPropertyRelative(field)));
            var clipsSection = new VisualElement();
            clipsSection.AddToClassList("audioorbit-section");
            details.Add(clipsSection);
            var mode = property.FindPropertyRelative("SelectionMode");
            var selectionField = new PropertyField(mode, "Selection Mode")
                { tooltip = SelectionDescription(library.Entries[selected].SelectionMode) };
            selectionField.TrackPropertyValue(mode,
                p => selectionField.tooltip = SelectionDescription((ClipSelectionMode)p.enumValueIndex));
            clipsSection.Add(selectionField);
            var clipsField = new PropertyField(property.FindPropertyRelative("Clips"), "Clips");
            clipsField.name = "sound-clips";
            clipsField.tooltip = "Drop audio assets here to append Clips to this Audio.";
            clipsField.RegisterCallback<DragUpdatedEvent>(e =>
            {
                if (IsClipResourceField(e.target as VisualElement)) return;
                if (DraggedResources().Length == 0) return;
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                e.StopImmediatePropagation();
            }, TrickleDown.TrickleDown);
            clipsField.RegisterCallback<DragPerformEvent>(e =>
            {
                if (IsClipResourceField(e.target as VisualElement)) return;
                var resources = DraggedResources();
                if (!HasSelection() || resources.Length == 0) return;
                e.StopImmediatePropagation();
                DragAndDrop.AcceptDrag();
                Undo.RecordObject(library, "Add Audio Clips");
                var sound = library.Entries[selected];
                sound.Clips ??= new List<ClipDefinition>();
                foreach (var resource in resources)
                    sound.Clips.Add(new ClipDefinition { Resource = resource });
                Commit();
            }, TrickleDown.TrickleDown);
            clipsSection.Add(clipsField);
            clipsField.schedule.Execute(() =>
            {
                var foldout = clipsField.Q<Foldout>();
                if (foldout != null) foldout.value = true;
            });
            var advanced = new Foldout { text = "Advanced", value = false, viewDataKey = "sound-advanced" };
            advanced.AddToClassList("audioorbit-section");
            advanced.Add(new PropertyField(property.FindPropertyRelative("SpatialProfile")));
            advanced.Add(new PropertyField(property.FindPropertyRelative("Limits")));
            details.Add(advanced);
            details.Bind(serializedLibrary);
            previewClip = library.Entries[selected].Clips?.FirstOrDefault()?.Clip;
            UpdateWaveform();
        }

        private static bool IsClipResourceField(VisualElement element)
        {
            for (; element != null; element = element.parent)
                if (element.ClassListContains("audioorbit-clip-resource-field"))
                    return true;
            return false;
        }

        private static UnityEngine.Object[] DraggedResources() => DragAndDrop.objectReferences
#if UNITY_6000_3_OR_NEWER
            .Where(asset => asset != null && asset is UnityEngine.Audio.IAudioGenerator)
#else
            .Where(asset => asset is AudioClip)
#endif
            .Distinct().ToArray();

        private void UpdatePreviewAudioButton()
        {
            if (previewAudioButton == null) return;
            if (!HasSelection())
            {
                previewAudioButton.SetEnabled(false);
                return;
            }

            var clips = library.Entries[selected].Clips;
            previewAudioButton.SetEnabled(clips != null && clips.Count > 0 &&
                                          clips.All(c => c != null && c.HasSupportedResource
#if UNITY_6000_3_OR_NEWER
                                                                   && (!(c.Resource is LoopRegionClip region) ||
                                                                       region.Clip != null)
#endif
                                          ));
        }

        private void OnPropertyChanged(SerializedPropertyChangeEvent e)
        {
            if (library == null) return;
            EditorUtility.SetDirty(library);
            UpdatePreviewAudioButton();
            RefreshRows();
            if (e.changedProperty != null &&
                ((e.changedProperty.propertyPath.EndsWith(".Clip") ||
                  e.changedProperty.propertyPath.EndsWith(".resource")) ||
                 e.changedProperty.propertyPath.EndsWith(".Clips.Array.size")))
                details.schedule.Execute(ShowDetails);
        }

        private void AddClips(AudioClip[] clips)
        {
            if (library == null) return;
            Undo.RecordObject(library, "Add Audio");
            if (clips.Length == 0)
            {
                var sound = library.CreateAudio();
                sound.Key = UniqueAudioKey("new_audio");
                library.Entries.Add(sound);
            }

            foreach (var clip in clips)
            {
                var sound = library.CreateAudio();
                sound.Key = UniqueAudioKey(clip.name);
                sound.DisplayName = clip.name;
                sound.Clips.Add(new ClipDefinition { Clip = clip });
                library.Entries.Add(sound);
            }

            selected = library.Entries.Count - 1;
            Commit();
        }

        private void Duplicate()
        {
            if (!HasSelection()) return;
            Undo.RecordObject(library, "Duplicate Audio");
            var copy = JsonUtility.FromJson<AudioDefinition>(JsonUtility.ToJson(library.Entries[selected]));
            copy.Key = AudioKeyAuthoring.Unique(library, copy.Key);
            library.Entries.Insert(selected + 1, copy);
            selected++;
            Commit();
        }

        private string UniqueAudioKey(string name)
        {
            var prefix = library.name.ToLowerInvariant() + "_" + name;
            return AudioKeyAuthoring.Unique(library, prefix);
        }

        private void Remove()
        {
            if (!HasSelection()) return;
            Undo.RecordObject(library, "Remove Audio");
            library.Entries.RemoveAt(selected);
            selected = Mathf.Min(selected, library.Entries.Count - 1);
            Commit();
        }

        private bool HasSelection() => library != null && selected >= 0 && selected < library.Entries.Count;

        private void Commit()
        {
            EditorUtility.SetDirty(library);
            Refresh();
        }

        private void CreateLibrary()
        {
            var path = EditorUtility.SaveFilePanelInProject("Create Audio Library", "AudioLibrary", "asset",
                "Choose where to save the Library.");
            if (string.IsNullOrEmpty(path)) return;
            var asset = CreateInstance<AudioLibrary>();
            AssetDatabase.CreateAsset(asset, path);
            libraryField.value = asset;
        }

        private static string SelectionDescription(ClipSelectionMode mode) => mode switch
        {
            ClipSelectionMode.Sequence => "Sequence: Play the next Clip on each click, wrapping to the first.",
            ClipSelectionMode.Shuffle => "Shuffle: Play every eligible Clip once in random order before repeating.",
            ClipSelectionMode.AvoidImmediateRepeat =>
                "Avoid Immediate Repeat: Select by Weight without repeating the previous Clip.",
            _ => "Weighted Random: Select a Clip by Weight on each click."
        };

        private void PlayPreview(int? clipDefinition)
        {
            if (!HasSelection()) return;
            if (audition == null)
            {
                audition = new AudioPreview();
                audition.Changed += OnPreviewChanged;
            }

            previewOrigin = clipDefinition.HasValue ? "Clip Preview" : "Audio";
            audition.Play(library.Entries[selected], clipDefinition);
        }

        private void OnPreviewChanged()
        {
            if (HasSelection() && audition != null && audition.ClipIndex >= 0)
            {
                previewClipIndex = audition.ClipIndex;
                var clips = library.Entries[selected].Clips;
                previewClip = previewClipIndex < clips.Count ? clips[previewClipIndex]?.Clip : null;
            }

            UpdateWaveform();
        }

        private void StopPreview() => audition?.Stop();

        private void ResetPreview()
        {
            if (audition == null) return;
            audition.Changed -= OnPreviewChanged;
            audition.Dispose();
            audition = null;
        }

        private void UpdateWaveform()
        {
            if (waveform == null) return;
            if (HasSelection()) audition?.ApplyChanges(library.Entries[selected]);
            waveform.SetClip(previewClip);
            UpdatePlayhead();
            clipInfo.text = previewClip != null
                ? $"{previewClip.name}   {previewClip.length:0.00}s   {previewClip.frequency} Hz   {previewClip.channels} ch"
                : "No AudioClip waveform available";
            var state = audition?.State ?? PlaybackState.Ended;
            var active = state == PlaybackState.Playing || state == PlaybackState.Paused;
            pauseButton.SetEnabled(state == PlaybackState.Playing);
            resumeButton.SetEnabled(state == PlaybackState.Paused);
            stopButton.SetEnabled(active || state == PlaybackState.Loading);
            var status = state == PlaybackState.Loading ? "Loading" :
                state == PlaybackState.Paused ? "Paused" :
                active ? "Playing" :
                previewOrigin != null ? "Finished" : "Ready";
            var count = HasSelection() ? library.Entries[selected].Clips?.Count ?? 0 : 0;
            var name = previewClip != null ? previewClip.name : "No waveform";
            previewStatus.text = audition?.Error != null ? $"Preview failed: {audition.Error}"
                : state == PlaybackState.Loading ? $"Loading preview ({previewOrigin})"
                : count > 0 ? $"{status}: Clip {previewClipIndex + 1} / {count} — {name}" +
                              (previewOrigin != null ? $" ({previewOrigin})" : " (Not previewed)")
                : "Select a Audio and a Clip";
            foreach (var row in details.Query<VisualElement>(className: "audioorbit-clip").ToList())
            {
                var highlighted = state != PlaybackState.Loading && count > 0 && row.userData is int index &&
                                  index == previewClipIndex;
                row.style.backgroundColor = highlighted ? new Color(0.2f, 0.5f, 0.8f, 0.15f) : Color.clear;
                row.style.borderLeftWidth = highlighted ? 3 : 0;
                row.style.borderLeftColor = new Color(0.3f, 0.65f, 1f);
            }
        }

        private void UpdatePlayhead()
        {
            waveform?.SetPlaybackPosition(audition?.PositionSeconds ?? 0,
                audition?.Duration > 0 ? audition.Duration : previewClip != null ? previewClip.length : 0,
                audition?.CanSeek ?? false);
        }
    }
}