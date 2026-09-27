using UnityEngine;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Adapters.Playable;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;

namespace Hidano.FacialControl.Samples
{
    /// <summary>
    /// レイヤー上の Trigger 系 ExpressionTrigger 入力源を 1 系統駆動し、ウェイトと
    /// トリガーで BlendShape 加重和がどう見えるかを目視検証するための PlayMode 専用 HUD。
    /// アナログ操作 (Vector2 入力 → 両目ボーン / BlendShape) の現在値も同 HUD で観測する。
    /// </summary>
    /// <remarks>
    /// 詳しい使い方は本サンプル同梱の README.md 参照。
    /// 本 HUD は FacialController の既存公開 API のみを利用し、書込はしないオブザーバ。
    /// アナログ表情の InputAction やボーンパス / BlendShape 名は SO の Inspector で編集する。
    /// 駆動対象は <c>profile.inputSources[].id</c> 形式の slug
    /// (<c>&lt;slug&gt;</c> または <c>&lt;slug&gt;:&lt;sub&gt;</c>) で指定し、
    /// レイヤー内の slot index は実行時に
    /// <see cref="FacialController.GetInputSourceWeightsSnapshot"/> から解決する。
    /// </remarks>
    [DefaultExecutionOrder(-100)]
    [AddComponentMenu("FacialControl/Samples/Multi Source Blend Demo HUD")]
    public class MultiSourceBlendDemoHUD : MonoBehaviour
    {
        [Tooltip("対象の FacialController")]
        [SerializeField]
        private FacialController _facialController;

        [Tooltip("HUD を表示するレイヤー index。既定: 0 (emotion)。")]
        [SerializeField]
        private int _layerIndex = 0;

        [Tooltip("駆動対象の入力源 slug。profile.inputSources[].id 形式 (<slug> または <slug>:<sub>)。既定: input")]
        [SerializeField]
        private string _inputSourceSlug = "input-system";

        [Tooltip("HUD で観測する LeftEye Transform (BonePose ターゲット)。アナログ機能未使用時は未割当で OK。")]
        [SerializeField]
        private Transform _leftEye;

        [Tooltip("HUD で観測する RightEye Transform (BonePose ターゲット)。アナログ機能未使用時は未割当で OK。")]
        [SerializeField]
        private Transform _rightEye;

        [Tooltip("観測対象の SkinnedMeshRenderer。未割当時は FacialController から探索する。")]
        [SerializeField]
        private SkinnedMeshRenderer _meshRenderer;

        [Tooltip("HUD に表示する mouth-open BlendShape 名 (デフォルト: jawOpen)。")]
        [SerializeField]
        private string _mouthOpenBlendShapeName = "jawOpen";

        [Tooltip("HUD で表示するアナログ入力 (Vector2) のアクション名 (表示専用)。")]
        [SerializeField]
        private string[] _displayedSourceIds = { "Look" };

        private static readonly string[] s_expressionIds = { "smile", "anger", "surprise", "lipsync_a" };

        private float _inputWeight = 1.0f;
        private Vector2 _scroll;
        private bool _initialWeightsApplied;

        private void Awake()
        {
            // Game View が非 focus でも Aggregator が tick し続けるように。
            UnityEngine.Application.runInBackground = true;
        }

        private void Update()
        {
            // FacialController は OnEnable で SO + StreamingAssets JSON から非同期に初期化される
            // 場合があるため、初期化検出後 1 度だけウェイトを適用する。
            if (_initialWeightsApplied)
            {
                return;
            }
            if (_facialController == null || !_facialController.IsInitialized)
            {
                return;
            }

            ApplyInitialWeights();
            _initialWeightsApplied = true;
        }

        private void ApplyInitialWeights()
        {
            if (TryResolveSourceIndex(out int sourceIdx))
            {
                _facialController.SetInputSourceWeight(_layerIndex, sourceIdx, _inputWeight);
            }
        }

        private void OnGUI()
        {
            if (_facialController == null)
            {
                GUILayout.BeginArea(new Rect(10, 10, 400, 80));
                GUILayout.Label("MultiSourceBlendDemoHUD: FacialController 未設定");
                GUILayout.EndArea();
                return;
            }

            if (!_facialController.IsInitialized)
            {
                GUILayout.BeginArea(new Rect(10, 10, 400, 80));
                GUILayout.Label("MultiSourceBlendDemoHUD: FacialController 初期化前");
                GUILayout.EndArea();
                return;
            }

            var inputSource = GetSource(_inputSourceSlug);
            bool hasSourceIdx = TryResolveSourceIndex(out int sourceIdx);

            GUILayout.BeginArea(new Rect(10, 10, 480, Screen.height - 20), GUI.skin.box);
            _scroll = GUILayout.BeginScrollView(_scroll);

            GUILayout.Label("<b>Multi Source Blend Demo</b>", RichStyle());
            GUILayout.Label($"layer {_layerIndex} の slug='{_inputSourceSlug}' 入力源を駆動");

            if (hasSourceIdx)
            {
                DrawWeightSlider($"Input weight (slug={_inputSourceSlug}, slot={sourceIdx})",
                    ref _inputWeight, sourceIdx);
            }
            else
            {
                GUILayout.Label(
                    $"  slug '{_inputSourceSlug}' は layer {_layerIndex} の inputSources に未宣言。");
            }

            GUILayout.Space(8);
            GUILayout.Label($"<b>Trigger 入力源 (slug={_inputSourceSlug})</b>", RichStyle());
            DrawSourceTriggerRow(inputSource, _inputSourceSlug);

            GUILayout.Space(8);
            if (GUILayout.Button("Trigger ソースの全 Expression を Off"))
            {
                ReleaseAll(inputSource);
            }

            GUILayout.Space(10);
            GUILayout.Label("<b>Active Expression</b>", RichStyle());
            GUILayout.Label(inputSource == null
                ? $"  {_inputSourceSlug}: (source not registered)"
                : $"  {_inputSourceSlug}: [{string.Join(", ", inputSource.ActiveExpressionIds)}]");

            GUILayout.Space(10);
            GUILayout.Label("<b>Weight Snapshot</b>", RichStyle());
            var snapshot = _facialController.GetInputSourceWeightsSnapshot();
            for (int i = 0; i < snapshot.Count; i++)
            {
                var entry = snapshot[i];
                GUILayout.Label(
                    $"  layer={entry.LayerIdx} source={entry.SourceId} weight={entry.Weight:0.00} saturated={entry.Saturated}");
            }

            GUILayout.Space(10);
            DrawBoneSection();
            GUILayout.Space(8);
            DrawBlendShapeSection();
            GUILayout.Space(8);
            DrawSourceIdSection();

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawBoneSection()
        {
            GUILayout.Label("<b>BonePose 出力 (Eye Euler)</b>", RichStyle());
            DrawBone("  LeftEye", _leftEye);
            DrawBone("  RightEye", _rightEye);
        }

        private static void DrawBone(string label, Transform t)
        {
            if (t == null)
            {
                GUILayout.Label($"{label}: (未割当)");
                return;
            }
            var euler = t.localRotation.eulerAngles;
            GUILayout.Label(
                $"{label}: ({euler.x:0.0}, {euler.y:0.0}, {euler.z:0.0})");
        }

        private void DrawBlendShapeSection()
        {
            GUILayout.Label("<b>BlendShape 出力 (mouth-open)</b>", RichStyle());

            var renderer = ResolveRenderer();
            if (renderer == null)
            {
                GUILayout.Label("  (SkinnedMeshRenderer 未解決)");
                return;
            }

            var mesh = renderer.sharedMesh;
            if (mesh == null)
            {
                GUILayout.Label("  (sharedMesh 未割当)");
                return;
            }

            int idx = mesh.GetBlendShapeIndex(_mouthOpenBlendShapeName);
            if (idx < 0)
            {
                GUILayout.Label($"  '{_mouthOpenBlendShapeName}' BlendShape が見つかりません");
                return;
            }

            float weight = renderer.GetBlendShapeWeight(idx);
            GUILayout.Label($"  {_mouthOpenBlendShapeName}: {weight:0.00}");
        }

        private void DrawSourceIdSection()
        {
            GUILayout.Label("<b>定義済みアナログ source ID (参考)</b>", RichStyle());
            if (_displayedSourceIds == null || _displayedSourceIds.Length == 0)
            {
                GUILayout.Label("  (未設定)");
                return;
            }
            for (int i = 0; i < _displayedSourceIds.Length; i++)
            {
                GUILayout.Label($"  - {_displayedSourceIds[i]}");
            }
        }

        private SkinnedMeshRenderer ResolveRenderer()
        {
            if (_meshRenderer != null) return _meshRenderer;
            if (_facialController == null) return null;
            return _facialController.GetComponentInChildren<SkinnedMeshRenderer>();
        }

        private void DrawWeightSlider(string label, ref float weight, int sourceIndex)
        {
            GUILayout.Label($"{label}: {weight:0.00}");
            float next = GUILayout.HorizontalSlider(weight, 0f, 1f);
            if (!Mathf.Approximately(next, weight))
            {
                weight = next;
                _facialController.SetInputSourceWeight(_layerIndex, sourceIndex, weight);
            }
        }

        private void DrawSourceTriggerRow(ExpressionTriggerInputSourceBase source, string slug)
        {
            if (source == null)
            {
                GUILayout.Label($"  '{slug}' は profile.inputSources に未宣言。");
                return;
            }

            for (int i = 0; i < s_expressionIds.Length; i++)
            {
                string exprId = s_expressionIds[i];
                bool active = Contains(source.ActiveExpressionIds, exprId);
                GUILayout.BeginHorizontal();
                GUILayout.Label(exprId, GUILayout.Width(80));
                if (GUILayout.Button("On", GUILayout.Width(60)))
                {
                    source.TriggerOn(exprId);
                }
                if (GUILayout.Button("Off", GUILayout.Width(60)))
                {
                    source.TriggerOff(exprId);
                }
                GUILayout.Label(active ? "(active)" : "");
                GUILayout.EndHorizontal();
            }
        }

        private static bool Contains(System.Collections.Generic.IReadOnlyList<string> list, string id)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] == id) return true;
            }
            return false;
        }

        private void ReleaseAll(ExpressionTriggerInputSourceBase source)
        {
            if (source == null) return;
            for (int i = 0; i < s_expressionIds.Length; i++)
            {
                source.TriggerOff(s_expressionIds[i]);
            }
        }

        private ExpressionTriggerInputSourceBase GetSource(string slug)
        {
            if (string.IsNullOrEmpty(slug))
            {
                return null;
            }
            _facialController.TryGetExpressionTriggerSourceById(slug, out var source);
            return source;
        }

        /// <summary>
        /// <see cref="_inputSourceSlug"/> を <see cref="_layerIndex"/> 内の slot index に解決する。
        /// snapshot は (layer, slot) 順に走査されるため、同 layer 内の出現順を slot index として採用する。
        /// 未初期化 / slug 未宣言 / slug 空 のいずれかに該当する場合は false を返す。
        /// </summary>
        private bool TryResolveSourceIndex(out int sourceIdx)
        {
            sourceIdx = -1;
            if (_facialController == null || !_facialController.IsInitialized)
            {
                return false;
            }
            if (string.IsNullOrEmpty(_inputSourceSlug))
            {
                return false;
            }

            var snapshot = _facialController.GetInputSourceWeightsSnapshot();
            int currentLayer = -1;
            int slotInLayer = -1;
            for (int i = 0; i < snapshot.Count; i++)
            {
                LayerSourceWeightEntry entry = snapshot[i];
                if (entry.LayerIdx != currentLayer)
                {
                    currentLayer = entry.LayerIdx;
                    slotInLayer = 0;
                }
                else
                {
                    slotInLayer++;
                }

                if (currentLayer == _layerIndex
                    && entry.SourceId.Value == _inputSourceSlug)
                {
                    sourceIdx = slotInLayer;
                    return true;
                }
            }
            return false;
        }

        private static GUIStyle s_richStyle;
        private static GUIStyle RichStyle()
        {
            if (s_richStyle == null)
            {
                s_richStyle = new GUIStyle(GUI.skin.label) { richText = true };
            }
            return s_richStyle;
        }
    }
}
