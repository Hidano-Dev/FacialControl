using System;
using System.Collections.Generic;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Editor.Common;
using UnityEditor;
using UnityEngine;

namespace Hidano.FacialControl.Editor.Thumbnails
{
    /// <summary>
    /// <see cref="PreviewRenderUtility"/> で参照モデルの顔を描画する <see cref="IExpressionThumbnailRenderer"/> 実装。
    /// <para>
    /// 描画のたびに参照モデルを複製し直す。前の Expression で動かした BlendShape・ボーン・マテリアルを
    /// 戻す処理を持たずに済み、どの Expression も同じ初期姿勢から適用される。
    /// </para>
    /// <para>
    /// Expression の適用は 2 段階で行う:
    /// 1. <see cref="AnimationClip.SampleAnimation"/> で時刻 0 を適用する（ボーン・マテリアル差し替え等、clip が持つ全プロパティ）。
    /// 2. snapshot の BlendShape を名前で全 SkinnedMeshRenderer に書く。ランタイムの出力
    ///    （<c>SkinnedMeshRendererBlendShapeWriter</c>）は RendererPath を見ず BlendShape 名だけで解決するため、
    ///    clip の binding path がモデル階層と一致しない場合もランタイムと同じ見た目にする。
    /// </para>
    /// <para>
    /// 描画はレンダーパイプラインに依存しない（SRP では RenderRequest、Built-in では
    /// <see cref="PreviewRenderUtility.Render"/>。<see cref="PreviewRenderCapture"/> 参照）。
    /// </para>
    /// </summary>
    public sealed class ExpressionThumbnailRenderer : IExpressionThumbnailRenderer
    {
        private const float KeyLightIntensity = 1.2f;
        private const float FillLightIntensity = 0.6f;

        /// <summary>キーライトの向き（カメラ基準）。カメラの左上後方から当てる。</summary>
        private static readonly Quaternion KeyLightRotationFromCamera = Quaternion.Euler(30f, -30f, 0f);

        /// <summary>フィルライトの向き（カメラ基準）。キーライトの反対側の下から弱く当てる。</summary>
        private static readonly Quaternion FillLightRotationFromCamera = Quaternion.Euler(-15f, 40f, 0f);

        private static readonly Color BackgroundColor = new Color(0.22f, 0.22f, 0.22f, 1f);
        private static readonly Color AmbientColor = new Color(0.35f, 0.35f, 0.35f, 1f);

        private PreviewRenderUtility _previewRenderUtility;
        private GameObject _instance;
        private bool _disposed;

        public Texture2D Render(GameObject referenceModel, AnimationClip clip, in ExpressionSnapshot snapshot, int resolution)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ExpressionThumbnailRenderer));
            if (referenceModel == null)
                throw new ArgumentNullException(nameof(referenceModel));
            if (resolution <= 0)
                throw new ArgumentOutOfRangeException(nameof(resolution), resolution, "Resolution must be greater than zero.");

            EnsurePreviewRenderUtility();

            try
            {
                DestroyInstance();
                _instance = InstantiatePreviewInstance(referenceModel);
                _previewRenderUtility.AddSingleGO(_instance);

                // 構図は Expression 適用前の姿勢で決める（ボーンを動かす Expression で構図がぶれないように）。
                var pose = ComputeCameraPose(_instance);

                ApplyExpression(_instance, clip, snapshot);

                var camera = _previewRenderUtility.camera;
                camera.transform.SetPositionAndRotation(pose.Position, pose.Rotation);
                camera.fieldOfView = pose.FieldOfView;
                camera.nearClipPlane = pose.NearClip;
                camera.farClipPlane = pose.FarClip;

                var lights = _previewRenderUtility.lights;
                if (lights != null && lights.Length > 0 && lights[0] != null)
                {
                    lights[0].intensity = KeyLightIntensity;
                    lights[0].transform.rotation = pose.Rotation * KeyLightRotationFromCamera;
                }
                if (lights != null && lights.Length > 1 && lights[1] != null)
                {
                    lights[1].intensity = FillLightIntensity;
                    lights[1].transform.rotation = pose.Rotation * FillLightRotationFromCamera;
                }

                return PreviewRenderCapture.Capture(_previewRenderUtility, resolution, resolution);
            }
            finally
            {
                // 描画ごとに作り直すので、保持し続ける必要はない。
                DestroyInstance();
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            DestroyInstance();
            if (_previewRenderUtility != null)
            {
                _previewRenderUtility.Cleanup();
                _previewRenderUtility = null;
            }
        }

        private void EnsurePreviewRenderUtility()
        {
            if (_previewRenderUtility != null) return;

            _previewRenderUtility = new PreviewRenderUtility();
            var camera = _previewRenderUtility.camera;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = BackgroundColor;
            _previewRenderUtility.ambientColor = AmbientColor;
        }

        private void DestroyInstance()
        {
            if (_instance != null)
            {
                UnityEngine.Object.DestroyImmediate(_instance);
                _instance = null;
            }
        }

        private static GameObject InstantiatePreviewInstance(GameObject referenceModel)
        {
            var instance = UnityEngine.Object.Instantiate(referenceModel);
            instance.hideFlags = HideFlags.HideAndDontSave;
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            // 同一エディタフレーム内で BlendShape を書き換えてから描画するため、スキニングの再計算を強制する
            // （Expression Creator の PreviewRenderWrapper と同じ理由）。
            var skinnedRenderers = instance.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            for (int i = 0; i < skinnedRenderers.Length; i++)
            {
                skinnedRenderers[i].forceMatrixRecalculationPerRender = true;
            }

            return instance;
        }

        private static ExpressionThumbnailCameraPose ComputeCameraPose(GameObject instance)
        {
            var bounds = CalculateBounds(instance);
            var faceJoint = FaceTrackTargetResolver.Resolve(instance);
            var root = instance.transform;
            return ExpressionThumbnailFraming.Compute(
                bounds,
                faceJoint != null,
                faceJoint != null ? faceJoint.position : Vector3.zero,
                root.forward,
                root.up);
        }

        private static Bounds CalculateBounds(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
                return new Bounds(go.transform.position, Vector3.one);

            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }
            return bounds;
        }

        /// <summary>
        /// Expression をプレビュー用インスタンスへ適用する。
        /// </summary>
        public static void ApplyExpression(GameObject instance, AnimationClip clip, in ExpressionSnapshot snapshot)
        {
            if (instance == null) return;

            if (clip != null)
            {
                clip.SampleAnimation(instance, 0f);
            }

            var blendShapes = snapshot.BlendShapes.Span;
            if (blendShapes.Length == 0) return;

            // BlendShape 名 → 正規化値。同名が複数 RendererPath にある場合は絶対値の大きい方を採用する
            // （ランタイムが 1 つの出力 index に畳み込むのと同じ扱い）。
            var valuesByName = new Dictionary<string, float>(blendShapes.Length, StringComparer.Ordinal);
            for (int i = 0; i < blendShapes.Length; i++)
            {
                var name = blendShapes[i].Name;
                if (string.IsNullOrEmpty(name)) continue;

                var value = blendShapes[i].Value;
                if (!valuesByName.TryGetValue(name, out var existing) || Mathf.Abs(value) > Mathf.Abs(existing))
                {
                    valuesByName[name] = value;
                }
            }

            var renderers = instance.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            for (int r = 0; r < renderers.Length; r++)
            {
                var mesh = renderers[r].sharedMesh;
                if (mesh == null) continue;

                int count = mesh.blendShapeCount;
                for (int b = 0; b < count; b++)
                {
                    if (valuesByName.TryGetValue(mesh.GetBlendShapeName(b), out var value))
                    {
                        renderers[r].SetBlendShapeWeight(b, value * 100f);
                    }
                }
            }
        }
    }
}
