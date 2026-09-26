using System;
using System.Collections.Generic;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.RoutingEditor.Graph;
using Hidano.FacialControl.Editor.Windows.Routing.Logic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Hidano.FacialControl.RoutingEditor
{
    /// <summary>
    /// ルーティング設定を編集する GraphView ベースの EditorWindow。
    /// </summary>
    public sealed class RoutingEditorWindow : EditorWindow
    {
        private const string WindowTitle = "Routing Editor";
        private const string InvalidProfileWarning =
            "[RoutingEditorWindow] FacialCharacterProfileSO is null or invalid. Window will not open.";
        private const string MissingProfileWarning =
            "[RoutingEditorWindow] Observed FacialCharacterProfileSO became invalid. Closing window.";

        private readonly IRoutingGraphModelBuilder _graphModelBuilder = new RoutingGraphModelBuilder();
        private readonly IWiringSerializedMapper _wiringSerializedMapper = new WiringSerializedMapper();

        private RoutingGraphView _graphView;
        private FacialCharacterProfileSO _profile;
        private SerializedObject _serializedObject;
        private string _profileKey = string.Empty;
        private int _lastObservedStateHash;
        private bool _isClosingForMissingProfile;

        public static RoutingEditorWindow Open(ScriptableObject profile)
        {
            if (!TryResolveProfile(profile, out FacialCharacterProfileSO facialProfile))
            {
                Debug.LogWarning(InvalidProfileWarning);
                return null;
            }

            string profileKey = BuildProfileKey(facialProfile);
            RoutingEditorWindow existingWindow = FindOpenWindow(profileKey);
            if (existingWindow != null)
            {
                existingWindow.BindProfile(facialProfile);
                existingWindow.Focus();
                return existingWindow;
            }

            RoutingEditorWindow window = UnityEngine.Application.isBatchMode
                ? ScriptableObject.CreateInstance<RoutingEditorWindow>()
                : CreateWindow<RoutingEditorWindow>();
            window.titleContent = new GUIContent(WindowTitle);
            window.minSize = new Vector2(640f, 360f);
            window.BindProfile(facialProfile);
            if (!UnityEngine.Application.isBatchMode)
            {
                window.Show();
            }
            return window;
        }

        private void OnEnable()
        {
            Undo.undoRedoPerformed += HandleUndoRedoPerformed;
            EditorApplication.update += HandleEditorUpdate;
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= HandleUndoRedoPerformed;
            EditorApplication.update -= HandleEditorUpdate;
            _wiringSerializedMapper.EndContinuousWeight();
        }

        public void CreateGUI()
        {
            RebuildWindowContent();
            if (_profile != null)
            {
                RebuildGraph();
            }
        }

        private void BindProfile(FacialCharacterProfileSO profile)
        {
            if (profile == null)
            {
                Debug.LogWarning(InvalidProfileWarning);
                return;
            }

            _profile = profile;
            _profileKey = BuildProfileKey(profile);
            _serializedObject = new SerializedObject(profile);
            _isClosingForMissingProfile = false;

            titleContent = new GUIContent(WindowTitle);
            minSize = new Vector2(640f, 360f);

            RebuildWindowContent();
            RebuildGraph();
        }

        private void RebuildWindowContent()
        {
            VisualElement root = rootVisualElement;
            root.Clear();

            if (_profile == null)
            {
                return;
            }

            _graphView = new RoutingGraphView();
            _graphView.style.flexGrow = 1f;
            root.Add(_graphView);

        }

        private void HandleUndoRedoPerformed()
        {
            RebuildGraphIfChanged(force: true);
        }

        private void HandleEditorUpdate()
        {
            if (_profile != null)
            {
                RebuildGraphIfChanged();
                return;
            }

            if (_isClosingForMissingProfile || !HasObservedProfile())
            {
                return;
            }

            _isClosingForMissingProfile = true;
            Debug.LogWarning(MissingProfileWarning);
            Close();
        }

        private void RebuildGraphIfChanged(bool force = false)
        {
            if (!EnsureProfileIsValid())
            {
                return;
            }

            int currentStateHash = CalculateObservedStateHash(_profile);
            if (!force && currentStateHash == _lastObservedStateHash)
            {
                return;
            }

            RebuildGraph();
        }

        private bool EnsureProfileIsValid()
        {
            if (_profile != null)
            {
                return true;
            }

            if (_isClosingForMissingProfile || !HasObservedProfile())
            {
                return false;
            }

            _isClosingForMissingProfile = true;
            Debug.LogWarning(MissingProfileWarning);
            Close();
            return false;
        }

        private bool HasObservedProfile()
        {
            return !string.IsNullOrEmpty(_profileKey) || _serializedObject != null;
        }

        private void RebuildGraph()
        {
            if (!EnsureProfileIsValid())
            {
                return;
            }

            if (_graphView == null)
            {
                RebuildWindowContent();
            }

            if (_serializedObject == null)
            {
                _serializedObject = new SerializedObject(_profile);
            }

            _serializedObject.Update();

            // 同一 priority の重複は表示順を正として distinct な値へ補正し SO へ書き戻す。
            // 補正後の値でモデルを構築するため、ノード/エッジは常に整合した priority で描画される。
            NormalizeLayerPrioritiesIfNeeded();

            RoutingGraphModel model = _graphModelBuilder.Build(_profile);
            _graphView.SetAdapterNodes(model.AdapterNodes);
            _graphView.SetLayerNodes(model.LayerNodes, _serializedObject, _wiringSerializedMapper);
            _graphView.SetOutputNode(model.OutputNode, _serializedObject, _wiringSerializedMapper);
            _graphView.SetCompositionEdges();
            _graphView.SetWiringEdges(model.Edges, _serializedObject, _wiringSerializedMapper);
            _graphView.SetInvalidInputs(model.InvalidEdges);

            // All ポート展開などで残ったドラッグ由来の一時エッジ（ゴースト）を掃除する。
            // これがないと一括配線→Undo 後に All-Input 間の線が描画されたまま残る。
            _graphView.PruneUntrackedEdges();

            _lastObservedStateHash = CalculateObservedStateHash(_profile);
        }

        /// <summary>
        /// レイヤーの priority に重複があれば、表示順（priority 昇順）を保ったまま distinct な値へ補正し、
        /// SO へ書き戻す。補正は変更があった場合のみ行い、単一 Undo グループにまとめる。
        /// </summary>
        private void NormalizeLayerPrioritiesIfNeeded()
        {
            IReadOnlyList<LayerDefinitionSerializable> layers = _profile.Layers;
            if (layers == null || layers.Count == 0)
            {
                return;
            }

            var priorities = new int[layers.Count];
            for (int i = 0; i < layers.Count; i++)
            {
                priorities[i] = layers[i]?.priority ?? 0;
            }

            if (!LayerPriorityNormalizer.RequiresCorrection(priorities))
            {
                return;
            }

            int[] corrected = LayerPriorityNormalizer.Normalize(priorities);

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Normalize Routing Priorities");
            int undoGroup = Undo.GetCurrentGroup();
            try
            {
                for (int i = 0; i < layers.Count; i++)
                {
                    if (corrected[i] == priorities[i])
                    {
                        continue;
                    }

                    LayerDefinitionSerializable layer = layers[i];
                    _wiringSerializedMapper.SetLayerProperties(
                        _serializedObject,
                        i,
                        layer?.name ?? string.Empty,
                        corrected[i],
                        layer?.exclusionMode ?? ExclusionMode.LastWins,
                        layer?.layerOverrideMask);
                }
            }
            finally
            {
                Undo.CollapseUndoOperations(undoGroup);
            }

            _serializedObject.Update();
        }

        private static bool TryResolveProfile(
            ScriptableObject profile,
            out FacialCharacterProfileSO facialProfile)
        {
            facialProfile = profile as FacialCharacterProfileSO;
            return facialProfile != null;
        }

        private static RoutingEditorWindow FindOpenWindow(string profileKey)
        {
            RoutingEditorWindow[] openWindows = Resources.FindObjectsOfTypeAll<RoutingEditorWindow>();
            for (int i = 0; i < openWindows.Length; i++)
            {
                RoutingEditorWindow window = openWindows[i];
                if (window == null)
                {
                    continue;
                }

                if (string.Equals(window._profileKey, profileKey, StringComparison.Ordinal))
                {
                    return window;
                }
            }

            return null;
        }

        private static string BuildProfileKey(FacialCharacterProfileSO profile)
        {
            if (profile == null)
            {
                return string.Empty;
            }

            if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(profile, out string guid, out long localId))
            {
                return guid + ":" + localId;
            }

            return profile.GetInstanceID().ToString();
        }

        private static int CalculateObservedStateHash(FacialCharacterProfileSO profile)
        {
            if (profile == null)
            {
                return 0;
            }

            string json = EditorJsonUtility.ToJson(profile);
            return StringComparer.Ordinal.GetHashCode(json ?? string.Empty);
        }
    }
}
