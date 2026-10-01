using System;
using System.Collections.Generic;
using Hidano.FacialControl.Adapters.Bone;
using Hidano.FacialControl.Adapters.Playable;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Timeline.Adapters;
using Hidano.FacialControl.Timeline.Adapters.Assets;
using Hidano.FacialControl.Timeline.EditorPreview;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using GazeChannel = Hidano.FacialControl.Adapters.ScriptableObject.GazeChannel;

namespace Hidano.FacialControl.Timeline.Editor
{
    [InitializeOnLoad]
    internal static class FacialTimelineEditorPreview
    {
        private const string MissingBakeMessage =
            "[FacialTimelineEditorPreview] BakeAsset is missing. Scrub preview is disabled.";

        private static readonly HashSet<int> MissingBakeWarnings = new HashSet<int>();

        // path 未指定の目に使う Humanoid の目ボーンと rest 回転・軸、および path 解決用の resolver
        // (FacialController の instanceID ごと)。
        // rest 回転はプレビューで書き換える前の姿勢から取る必要があるため、GatherProperties (プレビュー開始時、
        // Timeline が対象プロパティを記録する時点) で取り直し、スクラブ中は使い回す。
        private static readonly Dictionary<int, CachedGazeEyeFallback> GazeEyeFallbacks =
            new Dictionary<int, CachedGazeEyeFallback>();

        private static readonly List<int> StaleGazeEyeFallbackKeys = new List<int>();

        private static readonly List<FacialTimelinePreviewEyeTarget> GazeTargetBuffer =
            new List<FacialTimelinePreviewEyeTarget>();

        static FacialTimelineEditorPreview()
        {
            FacialTimelineEditorPreviewBridge.ApplyPreview = ApplyPreview;
            FacialTimelineEditorPreviewBridge.GatherProperties = GatherProperties;
        }

        internal static void ApplyPreview(FacialTimelineReceiver receiver, TimelineAsset timeline, double timeSeconds)
        {
            if (receiver == null)
            {
                return;
            }

            FacialTimelineBakeAsset bakeAsset = receiver.BakeAsset;
            if (bakeAsset == null)
            {
                WarnMissingBake(receiver);
                return;
            }

            MissingBakeWarnings.Remove(receiver.GetInstanceID());

            FacialController controller = ResolveController(receiver);
            if (controller == null)
            {
                return;
            }

            ApplyBlendShapes(controller, bakeAsset, timeSeconds);
            ApplyGaze(controller, bakeAsset, timeSeconds);
        }

        internal static void GatherProperties(PlayableDirector director, TrackAsset track, IPropertyCollector collector)
        {
            if (director == null || track == null || collector == null)
            {
                return;
            }

            FacialTimelineReceiver receiver = ResolveBoundReceiver(director, track);
            if (receiver == null)
            {
                return;
            }

            FacialController controller = ResolveController(receiver);
            if (controller == null)
            {
                return;
            }

            RegisterBlendShapeProperties(controller, collector);
            RegisterGazeProperties(controller, collector);
        }

        private static void ApplyBlendShapes(FacialController controller, FacialTimelineBakeAsset bakeAsset, double timeSeconds)
        {
            SkinnedMeshRenderer[] renderers = controller.SkinnedMeshRenderers;
            if (renderers == null || renderers.Length == 0)
            {
                return;
            }

            var sampledWeights = new Dictionary<string, float>(StringComparer.Ordinal);
            ExpressionSourceBake[] expressionBakes = bakeAsset.ExpressionBakes ?? Array.Empty<ExpressionSourceBake>();
            for (int i = 0; i < expressionBakes.Length; i++)
            {
                BlendShapeCurve[] curves = expressionBakes[i]?.Curves ?? Array.Empty<BlendShapeCurve>();
                for (int j = 0; j < curves.Length; j++)
                {
                    BlendShapeCurve curve = curves[j];
                    if (curve == null || string.IsNullOrEmpty(curve.BlendShapeName) || curve.Curve == null)
                    {
                        continue;
                    }

                    float value = curve.Curve.Evaluate((float)timeSeconds);
                    if (sampledWeights.TryGetValue(curve.BlendShapeName, out float existing))
                    {
                        sampledWeights[curve.BlendShapeName] = existing + value;
                    }
                    else
                    {
                        sampledWeights[curve.BlendShapeName] = value;
                    }
                }
            }

            for (int i = 0; i < renderers.Length; i++)
            {
                SkinnedMeshRenderer renderer = renderers[i];
                Mesh mesh = renderer != null ? renderer.sharedMesh : null;
                if (renderer == null || mesh == null)
                {
                    continue;
                }

                for (int shapeIndex = 0; shapeIndex < mesh.blendShapeCount; shapeIndex++)
                {
                    string blendShapeName = mesh.GetBlendShapeName(shapeIndex);
                    sampledWeights.TryGetValue(blendShapeName, out float weight);
                    renderer.SetBlendShapeWeight(shapeIndex, Mathf.Clamp01(weight) * 100f);
                }
            }
        }

        private static void ApplyGaze(FacialController controller, FacialTimelineBakeAsset bakeAsset, double timeSeconds)
        {
            IReadOnlyList<GazeChannel> gazeConfigs = controller.CharacterSO != null
                ? controller.CharacterSO.GazeChannels
                : Array.Empty<GazeChannel>();
            if (gazeConfigs == null || gazeConfigs.Count == 0)
            {
                return;
            }

            ValueChannelBake[] gazeChannels = CollectGazeChannels(bakeAsset);
            if (gazeChannels.Length == 0)
            {
                return;
            }

            // ランタイムは入力源の無い channel を駆動しない。プレビューではベイク値の無い channel がそれに当たる。
            CachedGazeEyeFallback cached = GetGazeEyeFallback(controller);
            GazeTargetBuffer.Clear();
            FacialTimelinePreviewGazeTargets.Resolve(
                cached.Resolver,
                gazeConfigs,
                index => index < gazeChannels.Length && HasAnyAxis(gazeChannels[index]),
                cached.Fallback,
                GazeTargetBuffer);

            for (int i = 0; i < GazeTargetBuffer.Count; i++)
            {
                FacialTimelinePreviewEyeTarget target = GazeTargetBuffer[i];
                ValueChannelBake bake = gazeChannels[target.ChannelIndex];
                float x = EvaluateAxis(bake.Axes, 0, timeSeconds);
                float y = EvaluateAxis(bake.Axes, 1, timeSeconds);
                target.Bone.localRotation = FacialTimelinePreviewGazeTargets.ComputeLocalRotation(
                    target,
                    gazeConfigs[target.ChannelIndex],
                    x,
                    y);
            }

            GazeTargetBuffer.Clear();
        }

        private static bool HasAnyAxis(ValueChannelBake bake)
        {
            AnimationCurve[] axes = bake.Axes;
            if (axes == null)
            {
                return false;
            }

            for (int i = 0; i < axes.Length; i++)
            {
                if (axes[i] != null)
                {
                    return true;
                }
            }

            return false;
        }

        private static ValueChannelBake[] CollectGazeChannels(FacialTimelineBakeAsset bakeAsset)
        {
            ValueChannelBake[] channels = bakeAsset.ValueBakes ?? Array.Empty<ValueChannelBake>();
            if (channels.Length == 0)
            {
                return Array.Empty<ValueChannelBake>();
            }

            var gazeChannels = new List<ValueChannelBake>(channels.Length);
            for (int i = 0; i < channels.Length; i++)
            {
                if (channels[i] != null && channels[i].IsGaze)
                {
                    gazeChannels.Add(channels[i]);
                }
            }

            return gazeChannels.Count == 0 ? Array.Empty<ValueChannelBake>() : gazeChannels.ToArray();
        }

        private static float EvaluateAxis(AnimationCurve[] axes, int axisIndex, double timeSeconds)
        {
            if (axes == null || axisIndex >= axes.Length || axes[axisIndex] == null)
            {
                return 0f;
            }

            return Mathf.Clamp(axes[axisIndex].Evaluate((float)timeSeconds), -1f, 1f);
        }

        private static void RegisterBlendShapeProperties(FacialController controller, IPropertyCollector collector)
        {
            SkinnedMeshRenderer[] renderers = controller.SkinnedMeshRenderers;
            if (renderers == null)
            {
                return;
            }

            for (int i = 0; i < renderers.Length; i++)
            {
                SkinnedMeshRenderer renderer = renderers[i];
                Mesh mesh = renderer != null ? renderer.sharedMesh : null;
                if (renderer == null || mesh == null)
                {
                    continue;
                }

                for (int shapeIndex = 0; shapeIndex < mesh.blendShapeCount; shapeIndex++)
                {
                    collector.AddFromName<SkinnedMeshRenderer>(renderer.gameObject, "blendShape." + mesh.GetBlendShapeName(shapeIndex));
                }
            }
        }

        private static void RegisterGazeProperties(FacialController controller, IPropertyCollector collector)
        {
            IReadOnlyList<GazeChannel> gazeConfigs = controller.CharacterSO != null
                ? controller.CharacterSO.GazeChannels
                : Array.Empty<GazeChannel>();
            if (gazeConfigs == null || gazeConfigs.Count == 0)
            {
                return;
            }

            // プレビュー開始時の姿勢から fallback の rest 回転・軸を取り直す。
            PruneDestroyedGazeEyeFallbacks();
            CachedGazeEyeFallback cached = CreateCachedGazeEyeFallback(controller);
            GazeEyeFallbacks[controller.GetInstanceID()] = cached;

            // 復元対象の登録はベイクの有無に依らず、駆動し得るボーンをすべて登録する。
            GazeTargetBuffer.Clear();
            FacialTimelinePreviewGazeTargets.Resolve(
                cached.Resolver,
                gazeConfigs,
                null,
                cached.Fallback,
                GazeTargetBuffer);

            for (int i = 0; i < GazeTargetBuffer.Count; i++)
            {
                RegisterRotationProperties(GazeTargetBuffer[i].Bone, collector);
            }

            GazeTargetBuffer.Clear();
        }

        private static CachedGazeEyeFallback GetGazeEyeFallback(FacialController controller)
        {
            int key = controller.GetInstanceID();
            if (GazeEyeFallbacks.TryGetValue(key, out CachedGazeEyeFallback cached))
            {
                return cached;
            }

            // GatherProperties を経ずに呼ばれた場合 (スクリプトからの Evaluate 等) は現在の姿勢から作る。
            // 目ボーンが無い (非 Humanoid) 場合も結果をキャッシュし、毎フレーム解決し直さない。
            cached = CreateCachedGazeEyeFallback(controller);
            GazeEyeFallbacks[key] = cached;
            return cached;
        }

        private static void PruneDestroyedGazeEyeFallbacks()
        {
            StaleGazeEyeFallbackKeys.Clear();
            foreach (KeyValuePair<int, CachedGazeEyeFallback> entry in GazeEyeFallbacks)
            {
                if (entry.Value.Controller == null)
                {
                    StaleGazeEyeFallbackKeys.Add(entry.Key);
                }
            }

            for (int i = 0; i < StaleGazeEyeFallbackKeys.Count; i++)
            {
                GazeEyeFallbacks.Remove(StaleGazeEyeFallbackKeys[i]);
            }

            StaleGazeEyeFallbackKeys.Clear();
        }

        private static CachedGazeEyeFallback CreateCachedGazeEyeFallback(FacialController controller)
        {
            return new CachedGazeEyeFallback(
                controller,
                new BoneTransformResolver(controller.transform),
                GazeEyeBoneFallback.FromAnimator(controller.GetComponent<Animator>()));
        }

        private static void RegisterRotationProperties(Transform target, IPropertyCollector collector)
        {
            if (target == null)
            {
                return;
            }

            collector.AddFromName<Transform>(target.gameObject, "m_LocalRotation.x");
            collector.AddFromName<Transform>(target.gameObject, "m_LocalRotation.y");
            collector.AddFromName<Transform>(target.gameObject, "m_LocalRotation.z");
            collector.AddFromName<Transform>(target.gameObject, "m_LocalRotation.w");
        }

        private static FacialTimelineReceiver ResolveBoundReceiver(PlayableDirector director, TrackAsset track)
        {
            for (TrackAsset current = track; current != null; current = current.parent as TrackAsset)
            {
                switch (director.GetGenericBinding(current))
                {
                    case FacialTimelineReceiver receiver:
                        return receiver;
                    case GameObject gameObject:
                        return gameObject.GetComponent<FacialTimelineReceiver>();
                    case Component component:
                        return component.GetComponent<FacialTimelineReceiver>();
                }
            }

            return null;
        }

        private static FacialController ResolveController(FacialTimelineReceiver receiver)
        {
            return receiver.GetComponent<FacialController>()
                   ?? receiver.GetComponentInParent<FacialController>()
                   ?? receiver.GetComponentInChildren<FacialController>();
        }

        private readonly struct CachedGazeEyeFallback
        {
            public CachedGazeEyeFallback(
                FacialController controller,
                BoneTransformResolver resolver,
                GazeEyeBoneFallback fallback)
            {
                Controller = controller;
                Resolver = resolver;
                Fallback = fallback;
            }

            public FacialController Controller { get; }
            public BoneTransformResolver Resolver { get; }
            public GazeEyeBoneFallback Fallback { get; }
        }

        private static void WarnMissingBake(FacialTimelineReceiver receiver)
        {
            if (receiver == null || !MissingBakeWarnings.Add(receiver.GetInstanceID()))
            {
                return;
            }

            Debug.LogWarning(MissingBakeMessage, receiver);
        }
    }
}
