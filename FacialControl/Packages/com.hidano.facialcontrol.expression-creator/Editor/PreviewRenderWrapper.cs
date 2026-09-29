using System;
using Hidano.FacialControl.Editor.Common;
using SceneViewStyleCameraController;
using SceneViewStyleCameraController.Handlers;
using UnityEditor;
using UnityEngine;

namespace Hidano.FacialControl.ExpressionCreator
{
    public class PreviewRenderWrapper : IDisposable
    {
        public const float DefaultFov = 30f;

        /// <summary>
        /// トラッキング対象（顔ジョイント）が指定されたときの FoV。
        /// カメラ位置は全身 bounds ベースのまま動かさず、FoV を下げることで顔のアップを実現する。
        /// </summary>
        public const float FaceTrackFov = 12f;

        public const float DefaultNearClip = 0.01f;
        public const float DefaultFarClip = 100f;
        public const float DefaultLightIntensity = 1.2f;
        public static readonly Quaternion DefaultLightRotation = Quaternion.Euler(30f, -30f, 0f);
        public static readonly Color DefaultBackgroundColor = new Color(0.15f, 0.15f, 0.15f, 1f);

        private const float OrbitSensitivity = 0.5f;
        private const float PanSensitivity = 0.002f;
        private const float DollyScrollSensitivity = 0.1f;
        private const float DollyDragSensitivity = 0.02f;
        private const float MinPivotDistance = 0.1f;

        /// <summary>
        /// プレビュー RenderTexture の 1 辺の上限（px）。これを超える要求は描画をスキップする。
        /// </summary>
        private const float MaxPreviewTextureSize = 4096f;

        private PreviewRenderUtility _previewRenderUtility;
        private GameObject _previewInstance;
        private bool _disposed;

        private CameraState _state;
        private CameraState _initialState;

        private int _dragButton = -1;
        private bool _dragAlt;

        /// <summary>異常 rect の警告を毎フレーム出さないための 1 回きりフラグ。</summary>
        private bool _invalidRectWarned;

        public bool IsInitialized => _previewRenderUtility != null && _previewInstance != null;

        public GameObject PreviewInstance => _previewInstance;

        /// <summary>
        /// 現在のプレビューカメラ FoV。未初期化時は <see cref="DefaultFov"/> を返す。
        /// </summary>
        public float CameraFieldOfView
            => _previewRenderUtility != null ? _previewRenderUtility.camera.fieldOfView : DefaultFov;

        public void Setup(GameObject sourceObject)
        {
            Setup(sourceObject, null);
        }

        /// <summary>
        /// プレビューをセットアップする。
        /// <paramref name="trackTargetPath"/> にソースルートからの相対 Transform パスを渡すと、
        /// その位置をカメラの注視点にし FoV を <see cref="FaceTrackFov"/> へ下げる（顔アップ用途）。
        /// null / 解決不能パスの場合は従来どおり bounds / Humanoid Head ベースの注視点と
        /// <see cref="DefaultFov"/> を用いる。
        /// </summary>
        public void Setup(GameObject sourceObject, string trackTargetPath)
        {
            Cleanup();

            if (sourceObject == null)
                return;

            _previewRenderUtility = new PreviewRenderUtility();

            _previewRenderUtility.camera.fieldOfView = DefaultFov;
            _previewRenderUtility.camera.nearClipPlane = DefaultNearClip;
            _previewRenderUtility.camera.farClipPlane = DefaultFarClip;
            _previewRenderUtility.camera.clearFlags = CameraClearFlags.SolidColor;
            _previewRenderUtility.camera.backgroundColor = DefaultBackgroundColor;

            _previewRenderUtility.lights[0].intensity = DefaultLightIntensity;
            _previewRenderUtility.lights[0].transform.rotation = DefaultLightRotation;

            _previewInstance = UnityEngine.Object.Instantiate(sourceObject);
            _previewInstance.hideFlags = HideFlags.HideAndDontSave;
            _previewInstance.transform.position = Vector3.zero;
            _previewInstance.transform.rotation = Quaternion.identity;

            _previewRenderUtility.AddSingleGO(_previewInstance);

            // 同一エディタフレーム内で SetBlendShapeWeight → キャプチャを複数回行う一括書き出しでは、
            // スキニング再計算がフレームあたり 1 回に間引かれ BlendShape 変更が描画に反映されない。
            // プレビュー専用インスタンスなので毎レンダー再計算のコスト影響は無視できる。
            var skinnedRenderers = _previewInstance.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            for (int i = 0; i < skinnedRenderers.Length; i++)
            {
                skinnedRenderers[i].forceMatrixRecalculationPerRender = true;
            }

            var bounds = CalculateBounds(_previewInstance);
            var trackTarget = ResolveTrackTarget(_previewInstance.transform, trackTargetPath);
            Vector3 pivotPoint;
            if (trackTarget != null)
            {
                pivotPoint = trackTarget.position;
                _previewRenderUtility.camera.fieldOfView = FaceTrackFov;
            }
            else
            {
                pivotPoint = CalculatePivotPoint(_previewInstance, bounds);
            }

            var pivotDistance = bounds.extents.magnitude * 2f;
            var rotation = Quaternion.Euler(0f, 180f, 0f);
            var position = pivotPoint - rotation * Vector3.forward * pivotDistance;

            _initialState = new CameraState(position, rotation, pivotPoint, pivotDistance);
            _state = _initialState;
        }

        public void Cleanup()
        {
            if (_previewInstance != null)
            {
                UnityEngine.Object.DestroyImmediate(_previewInstance);
                _previewInstance = null;
            }

            if (_previewRenderUtility != null)
            {
                _previewRenderUtility.Cleanup();
                _previewRenderUtility = null;
            }
        }

        public void Render(Rect rect)
        {
            if (_previewRenderUtility == null || _previewInstance == null)
                return;

            // BeginPreview は RenderTexture を確保するため、実際に画面へ描く Repaint 以外では実行しない。
            // Layout / Used などレイアウトが確定していないイベントでは rect が正しい値にならず、
            // 無駄な RenderTexture 生成（＋サイズ不正）の原因になる。
            if (Event.current == null || Event.current.type != EventType.Repaint)
                return;

            if (!IsRenderableRect(rect))
                return;

            _previewRenderUtility.camera.transform.position = _state.position;
            _previewRenderUtility.camera.transform.rotation = _state.rotation;

            _previewRenderUtility.BeginPreview(rect, GUIStyle.none);
            _previewRenderUtility.Render(true, true);
            var texture = _previewRenderUtility.EndPreview();

            GUI.DrawTexture(rect, texture, ScaleMode.StretchToFill, false);
        }

        /// <summary>
        /// <see cref="PreviewRenderUtility.BeginPreview"/> に渡せる rect かを判定する。
        /// <para>
        /// PreviewRenderUtility は rect のサイズ × <see cref="EditorGUIUtility.pixelsPerPoint"/> で
        /// RenderTexture を確保し、上限クランプを行わない。そのためレイアウト未確定などで
        /// 異常な rect が渡ると巨大な RenderTexture の生成に失敗し
        /// "RenderTexture.Create failed" が Console に出る。
        /// </para>
        /// 弾いた場合は原因調査のため実際の rect と要求サイズを 1 度だけ警告出力する。
        /// </summary>
        private bool IsRenderableRect(Rect rect)
        {
            float scale = Mathf.Max(EditorGUIUtility.pixelsPerPoint, 1f);
            float width = rect.width * scale;
            float height = rect.height * scale;

            bool renderable = width >= 1f && height >= 1f
                && width <= MaxPreviewTextureSize && height <= MaxPreviewTextureSize;
            if (renderable)
                return true;

            if (!_invalidRectWarned)
            {
                _invalidRectWarned = true;
                Debug.LogWarning(
                    "[PreviewRenderWrapper] 異常なプレビュー rect を検出したため描画をスキップしました。"
                        + $" rect={rect}, pixelsPerPoint={EditorGUIUtility.pixelsPerPoint},"
                        + $" 要求 RenderTexture サイズ={(int)width}x{(int)height}。");
            }

            return false;
        }

        public Texture2D CapturePreviewTexture(int width, int height)
        {
            if (_previewRenderUtility == null || _previewInstance == null)
                return null;

            if (width <= 0)
                throw new ArgumentOutOfRangeException(nameof(width), width, "Width must be greater than zero.");
            if (height <= 0)
                throw new ArgumentOutOfRangeException(nameof(height), height, "Height must be greater than zero.");

            _previewRenderUtility.camera.transform.position = _state.position;
            _previewRenderUtility.camera.transform.rotation = _state.rotation;

            return PreviewRenderCapture.Capture(_previewRenderUtility, width, height);
        }

        public bool HandleInput(Rect rect)
        {
            var evt = Event.current;
            var scrollDelta = evt.type == EventType.ScrollWheel ? evt.delta : Vector2.zero;
            var frame = new PreviewInputFrame(
                evt.type,
                evt.button,
                evt.mousePosition,
                evt.delta,
                scrollDelta,
                evt.alt);

            var changed = HandleInput(rect, frame);
            if (changed)
                evt.Use();
            return changed;
        }

        public bool HandleInput(Rect rect, PreviewInputFrame frame)
        {
            if (frame.EventType == EventType.MouseDown)
            {
                if (rect.Contains(frame.MousePosition))
                {
                    _dragButton = frame.Button;
                    _dragAlt = frame.Alt;
                }
                return false;
            }

            if (frame.EventType == EventType.MouseUp)
            {
                if (_dragButton == frame.Button)
                    _dragButton = -1;
                return false;
            }

            bool isCapturedDrag = frame.EventType == EventType.MouseDrag && _dragButton >= 0;
            int button = isCapturedDrag ? _dragButton : frame.Button;
            bool alt = isCapturedDrag ? _dragAlt : frame.Alt;

            if (!isCapturedDrag && !rect.Contains(frame.MousePosition))
                return false;

            var previous = _state;

            var verticalFlippedDelta = new Vector2(frame.Delta.x, -frame.Delta.y);

            switch (frame.EventType)
            {
                case EventType.MouseDrag when button == 0 && alt:
                    _state = OrbitHandler.Apply(_state, verticalFlippedDelta, OrbitSensitivity, MinPivotDistance);
                    break;
                case EventType.MouseDrag when button == 2:
                    _state = PanHandler.Apply(_state, verticalFlippedDelta, PanSensitivity, MinPivotDistance);
                    break;
                case EventType.ScrollWheel:
                    // Unity の ScrollWheel delta.y は「奥にホイール (≒ ズームイン期待)」で負、
                    // 「手前にホイール (≒ ズームアウト期待)」で正になる。DollyHandler は
                    // 正の dollyAmount で「カメラを前方に進める = ズームイン」になるため、
                    // 直感どおりの方向にするには符号を反転して渡す必要がある。
                    _state = DollyHandler.ApplyScroll(_state, -frame.ScrollDelta.y, DollyScrollSensitivity, MinPivotDistance);
                    break;
                case EventType.MouseDrag when button == 1 && alt:
                    _state = DollyHandler.ApplyDrag(_state, -frame.Delta.y, DollyDragSensitivity, MinPivotDistance);
                    break;
                default:
                    return false;
            }

            return _state.position != previous.position
                || _state.rotation != previous.rotation
                || _state.pivotPoint != previous.pivotPoint
                || _state.pivotDistance != previous.pivotDistance;
        }

        public void ResetCamera()
        {
            _state = _initialState;
        }

        /// <summary>
        /// トラッキング対象パスをプレビューインスタンス内の Transform に解決する。
        /// 空文字はルート自身、null / 不一致は null を返す。
        /// </summary>
        private static Transform ResolveTrackTarget(Transform instanceRoot, string trackTargetPath)
        {
            if (trackTargetPath == null)
                return null;

            if (trackTargetPath.Length == 0)
                return instanceRoot;

            return instanceRoot.Find(trackTargetPath);
        }

        public static Vector3 CalculatePivotPoint(GameObject go, Bounds fallbackBounds)
        {
            var animator = go.GetComponentInChildren<Animator>();
            if (animator != null && animator.isHuman)
            {
                var headBone = animator.GetBoneTransform(HumanBodyBones.Head);
                if (headBone != null)
                {
                    var headHeight = headBone.position.y;
                    return new Vector3(fallbackBounds.center.x, headHeight, fallbackBounds.center.z);
                }
            }

            return fallbackBounds.center;
        }

        public static Bounds CalculateBounds(GameObject go)
        {
            return PreviewModelBounds.Calculate(go);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                Cleanup();
                _disposed = true;
            }
        }
    }
}
