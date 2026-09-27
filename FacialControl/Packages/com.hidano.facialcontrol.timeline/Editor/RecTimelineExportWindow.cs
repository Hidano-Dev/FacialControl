using System;
using System.Collections.Generic;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Rec.Adapters.FileSystem;
using Hidano.FacialControl.Rec.Domain.Services;
using Hidano.FacialControl.Timeline.Adapters;
using Hidano.FacialControl.Timeline.Tracks;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using UnityEngine.UIElements;

namespace Hidano.FacialControl.Timeline.Editor
{
    public sealed class RecTimelineExportWindow : EditorWindow
    {
        private readonly Dictionary<string, SourceKindOverrideMode> _sourceOverrideModes =
            new Dictionary<string, SourceKindOverrideMode>(StringComparer.Ordinal);

        private TextField _recordingPathField;
        private ObjectField _profileField;
        private ObjectField _timelineField;
        private ObjectField _directorField;
        private ObjectField _receiverField;
        private ScrollView _sourceOverrideList;
        private HelpBox _statusBox;

        [MenuItem("Tools/FacialControl/Timeline/REC Export")]
        public static void Open()
        {
            RecTimelineExportWindow window = GetWindow<RecTimelineExportWindow>();
            window.titleContent = new GUIContent("REC Export");
            window.minSize = new Vector2(520f, 420f);
        }

        private void CreateGUI()
        {
            VisualElement root = rootVisualElement;
            root.style.paddingLeft = 10f;
            root.style.paddingRight = 10f;
            root.style.paddingTop = 10f;
            root.style.paddingBottom = 10f;

            _recordingPathField = new TextField("REC File");
            root.Add(_recordingPathField);

            var recordingButtons = new VisualElement();
            recordingButtons.style.flexDirection = FlexDirection.Row;
            recordingButtons.style.marginBottom = 6f;
            root.Add(recordingButtons);

            var browseRecordingButton = new Button(BrowseRecording)
            {
                text = "Browse REC"
            };
            recordingButtons.Add(browseRecordingButton);

            var reloadSourcesButton = new Button(RefreshSourceOverridesFromRecording)
            {
                text = "Reload Sources"
            };
            reloadSourcesButton.style.marginLeft = 6f;
            recordingButtons.Add(reloadSourcesButton);

            _profileField = new ObjectField("Profile")
            {
                objectType = typeof(FacialCharacterProfileSO),
                allowSceneObjects = false,
            };
            root.Add(_profileField);

            _timelineField = new ObjectField("Output Timeline")
            {
                objectType = typeof(TimelineAsset),
                allowSceneObjects = false,
            };
            root.Add(_timelineField);

            _directorField = new ObjectField("Playable Director")
            {
                objectType = typeof(PlayableDirector),
                allowSceneObjects = true,
            };
            root.Add(_directorField);

            _receiverField = new ObjectField("Timeline Receiver")
            {
                objectType = typeof(FacialTimelineReceiver),
                allowSceneObjects = true,
            };
            root.Add(_receiverField);

            var sourceHeader = new Label("Source Overrides");
            sourceHeader.style.marginTop = 8f;
            sourceHeader.style.unityFontStyleAndWeight = FontStyle.Bold;
            root.Add(sourceHeader);

            _sourceOverrideList = new ScrollView(ScrollViewMode.Vertical);
            _sourceOverrideList.style.flexGrow = 1f;
            _sourceOverrideList.style.minHeight = 180f;
            _sourceOverrideList.style.marginTop = 4f;
            root.Add(_sourceOverrideList);

            _statusBox = new HelpBox("Select a REC file and reload sources.", HelpBoxMessageType.Info);
            _statusBox.style.marginTop = 8f;
            root.Add(_statusBox);

            var exportButton = new Button(Export)
            {
                text = "Export Timeline"
            };
            exportButton.style.marginTop = 8f;
            root.Add(exportButton);
        }

        private void BrowseRecording()
        {
            string initialDirectory = string.Empty;
            string currentPath = _recordingPathField.value;
            if (!string.IsNullOrWhiteSpace(currentPath))
            {
                initialDirectory = System.IO.Path.GetDirectoryName(currentPath);
            }

            string selected = EditorUtility.OpenFilePanel("Select REC File", initialDirectory ?? string.Empty, "fcrec");
            if (string.IsNullOrWhiteSpace(selected))
            {
                return;
            }

            _recordingPathField.value = selected;
            RefreshSourceOverridesFromRecording();
        }

        private void RefreshSourceOverridesFromRecording()
        {
            _sourceOverrideModes.Clear();
            _sourceOverrideList.Clear();

            if (!RecFileReader.TryRead(_recordingPathField.value, out RecBinaryFormat.ReadResult readResult))
            {
                SetStatus("REC load failed. Check Console for details.", HelpBoxMessageType.Error);
                return;
            }

            IReadOnlyList<string> sourceIds = readResult.Timeline.SourceIds;
            for (int i = 0; i < sourceIds.Count; i++)
            {
                string sourceId = sourceIds[i];
                _sourceOverrideModes[sourceId] = SourceKindOverrideMode.Auto;
                _sourceOverrideList.Add(CreateSourceRow(sourceId));
            }

            SetStatus($"Loaded {sourceIds.Count} source id(s) from REC.", HelpBoxMessageType.Info);
        }

        private VisualElement CreateSourceRow(string sourceId)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 4f;

            var label = new Label(sourceId);
            label.style.flexGrow = 1f;
            row.Add(label);

            var enumField = new EnumField(SourceKindOverrideMode.Auto);
            enumField.RegisterValueChangedCallback(evt =>
            {
                _sourceOverrideModes[sourceId] = (SourceKindOverrideMode)evt.newValue;
            });
            row.Add(enumField);
            return row;
        }

        private void Export()
        {
            var profile = _profileField.value as FacialCharacterProfileSO;
            if (profile == null)
            {
                SetStatus("Profile is required.", HelpBoxMessageType.Error);
                return;
            }

            string outputPath = ResolveOutputPath();
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                SetStatus("Export was cancelled before selecting an output TimelineAsset.", HelpBoxMessageType.Warning);
                return;
            }

            var existingTimeline = _timelineField.value as TimelineAsset;
            var director = _directorField.value as PlayableDirector;
            var receiver = _receiverField.value as FacialTimelineReceiver;
            IReadOnlyDictionary<string, FacialValueChannelKind> sourceOverrides = BuildSourceOverrides();

            bool success = RecToTimelineExporter.TryExportTimelineAsset(
                _recordingPathField.value,
                profile,
                outputPath,
                out RecToTimelineExporter.ExportResult result,
                existingTimeline,
                director,
                receiver,
                sourceOverrides);

            if (!success)
            {
                if (result != null && result.Cancelled)
                {
                    SetStatus("Export cancelled.", HelpBoxMessageType.Warning);
                }
                else
                {
                    SetStatus("Export failed. Check Console for details.", HelpBoxMessageType.Error);
                }

                return;
            }

            _timelineField.value = result.Timeline;
            SetStatus($"Exported TimelineAsset to '{result.OutputAssetPath}'.", HelpBoxMessageType.Info);
        }

        private IReadOnlyDictionary<string, FacialValueChannelKind> BuildSourceOverrides()
        {
            var overrides = new Dictionary<string, FacialValueChannelKind>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, SourceKindOverrideMode> pair in _sourceOverrideModes)
            {
                switch (pair.Value)
                {
                    case SourceKindOverrideMode.Analog:
                        overrides[pair.Key] = FacialValueChannelKind.Analog;
                        break;
                    case SourceKindOverrideMode.Gaze:
                        overrides[pair.Key] = FacialValueChannelKind.Gaze;
                        break;
                }
            }

            return overrides;
        }

        private string ResolveOutputPath()
        {
            if (_timelineField.value is TimelineAsset timelineAsset)
            {
                return AssetDatabase.GetAssetPath(timelineAsset);
            }

            return EditorUtility.SaveFilePanelInProject(
                "Export TimelineAsset",
                "RecordedTimeline",
                "playable",
                "Choose where to save the exported TimelineAsset.");
        }

        private void SetStatus(string message, HelpBoxMessageType messageType)
        {
            _statusBox.messageType = messageType;
            _statusBox.text = message;
        }

        private enum SourceKindOverrideMode
        {
            Auto = 0,
            Analog = 1,
            Gaze = 2,
        }
    }
}
