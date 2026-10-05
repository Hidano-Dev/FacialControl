using System;
using System.Collections.Generic;
using Hidano.FacialControl.Adapters.Bone;
using Hidano.FacialControl.Adapters.Playable;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Timeline.Adapters;
using Hidano.FacialControl.Timeline.Adapters.Assets;
using Hidano.FacialControl.Timeline.Domain.Diagnostics;
using Hidano.FacialControl.Timeline.EditorPreview;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using GazeChannel = Hidano.FacialControl.Adapters.ScriptableObject.GazeChannel;

namespace Hidano.FacialControl.Timeline.Editor
{
    /// <summary>
    /// Timeline の Edit プレビュー（スクラブ）。Mixer から bridge 経由で呼ばれ、Play と同じ合成規則
    /// （<see cref="TimelinePreviewCompositor"/>）で renderer と目ボーンを描く。再生セッションは開始しない（Req 7.2）。
    /// </summary>
    /// <remarks>
    /// <para>Bake は <see cref="FacialTimelineBakeLocator"/> で解決し、Compositor は FacialController ごとにキャッシュする
    /// （入力が変われば作り直し、再ベイク完了 <see cref="TimelineEditChangeWatcher.BakeUpdated"/> で破棄）。</para>
    /// <para>構成の欠落（FacialController / Profile SO / Bake が無い）は例外にせず、警告ゲートで 1 エポック 1 回だけ警告して
    /// プレビューを継続する（Req 7.3）。Bake 参照が Conflict / LegacyExport のときは描かずに再ベイクを予約し、
    /// Profile 内容ハッシュ不一致（ProfileMismatch）は描画を続けつつ Receiver の診断に Warning を書いて再ベイクを予約する。</para>
    /// </remarks>
    [InitializeOnLoad]
    internal static class FacialTimelineEditorPreview
    {
        private const string LogPrefix = "[FacialTimelineEditorPreview] ";
        private const string ControllerMissingMessage =
            "FacialController が見つからないためプレビューできません。FacialTimelineReceiver と同じ GameObject に FacialController を置いてください。";
        private const string ProfileAssetMissingMessage =
            "FacialController に Character SO（Profile SO）が設定されていないためプレビューできません。";
        private const string TimelineMissingMessage =
            "PlayableDirector に TimelineAsset がセットされていないためプレビューできません。";
        private const string BakeMissingMessage =
            "Facial トラックに Bake がありません。Timeline を保存するか REC Export し直してください。プレビューは Bake ができるまで描画しません。";
        private const string ProfileMismatchDetail =
            "Bake が現在の Profile（profile.json / SO）と異なるスナップショットから作られています。自動再ベイク中です（それまでは Bake の値でプレビューします）。";
        private const string ProfileMatchedDetail = "Bake は現在の Profile と同じスナップショットから作られています。";

        // path 未指定の目に使う Humanoid の目ボーンと rest 回転・軸、および path 解決用の resolver
        // (FacialController の instanceID ごと)。
        // rest 回転はプレビューで書き換える前の姿勢から取る必要があるため、GatherProperties (プレビュー開始時、
        // Timeline が対象プロパティを記録する時点) で取り直し、スクラブ中は使い回す。
        private static readonly Dictionary<int, CachedGazeEyeFallback> GazeEyeFallbacks =
            new Dictionary<int, CachedGazeEyeFallback>();

        private static readonly List<int> StaleGazeEyeFallbackKeys = new List<int>();

        private static readonly List<FacialTimelinePreviewEyeTarget> GazeTargetBuffer =
            new List<FacialTimelinePreviewEyeTarget>();

        // FacialController の instanceID → Compositor。
        private static readonly Dictionary<int, CompositorEntry> Compositors = new Dictionary<int, CompositorEntry>();

        private static readonly List<int> StaleCompositorKeys = new List<int>();

        private static TimelineEditChangeWatcher _subscribedWatcher;

        static FacialTimelineEditorPreview()
        {
            FacialTimelineEditorPreviewBridge.ApplyPreview = ApplyPreview;
            FacialTimelineEditorPreviewBridge.GatherProperties = GatherProperties;
        }

        /// <summary>キャッシュ中の Compositor を全て破棄する（テスト / ドメインリロード相当）。</summary>
        internal static void ClearCache()
        {
            foreach (KeyValuePair<int, CompositorEntry> pair in Compositors)
            {
                pair.Value.Compositor.Dispose();
            }

            Compositors.Clear();
        }

        internal static void ApplyPreview(FacialTimelineReceiver receiver, TimelineAsset timeline, double timeSeconds)
        {
            if (receiver == null)
            {
                return;
            }

            EnsureWatcherSubscription();

            FacialController controller = ResolveController(receiver);
            if (controller == null)
            {
                WarnOnce(receiver, TimelineDiagnosticCode.ControllerMissing, receiver.name, ControllerMissingMessage);
                return;
            }

            FacialCharacterProfileSO profileAsset = controller.CharacterSO;
            if (profileAsset == null)
            {
                WarnOnce(receiver, TimelineDiagnosticCode.ControllerNotInitialized, controller.name, ProfileAssetMissingMessage);
                return;
            }

            if (timeline == null)
            {
                WarnOnce(receiver, TimelineDiagnosticCode.TimelineNotBound, receiver.name, TimelineMissingMessage);
                return;
            }

            FacialProfile profile = TimelineProfileSource.Resolve(profileAsset);
            CompositorEntry entry = AcquireCompositor(receiver, controller, profileAsset, profile, timeline);
            TimelinePreviewCompositor compositor = entry.Compositor;

            if (!compositor.CanRender)
            {
                BakeLocateStatus status = compositor.BakeLocate.Status;
                if (status == BakeLocateStatus.Conflict || status == BakeLocateStatus.LegacyExport)
                {
                    // Bake が一意に決まらないので描かない（前フレームの値も上書きしない）。再ベイクで全トラックの参照を揃える。
                    MarkDirtyOnce(entry, timeline, TimelineDirtyReason.BakeReferenceInconsistent);
                }
                else
                {
                    WarnOnce(receiver, TimelineDiagnosticCode.BakeMissing, timeline.name, BakeMissingMessage);
                }

                return;
            }

            if (compositor.ProfileCheck == TimelineDiagnosticCode.ProfileMismatch)
            {
                MarkDirtyOnce(entry, timeline, TimelineDirtyReason.ProfileMismatch);
            }

            compositor.Evaluate(timeSeconds);

            IReadOnlyList<GazeChannel> gazeConfigs = profileAsset.GazeChannels;
            if (gazeConfigs != null && gazeConfigs.Count > 0)
            {
                CachedGazeEyeFallback cached = GetGazeEyeFallback(controller);
                compositor.EvaluateGaze(timeSeconds, gazeConfigs, cached.Resolver, cached.Fallback, GazeTargetBuffer);
            }
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

        private static CompositorEntry AcquireCompositor(
            FacialTimelineReceiver receiver,
            FacialController controller,
            FacialCharacterProfileSO profileAsset,
            FacialProfile profile,
            TimelineAsset timeline)
        {
            int key = controller.GetInstanceID();
            if (Compositors.TryGetValue(key, out CompositorEntry cached))
            {
                if (cached.Compositor.Matches(controller, profileAsset, profile, receiver.BakeAsset, timeline))
                {
                    return cached;
                }

                cached.Compositor.Dispose();
                Compositors.Remove(key);
            }

            PruneDestroyedCompositors();

            var compositor = new TimelinePreviewCompositor(controller, profileAsset, profile, receiver.BakeAsset, timeline);
            var entry = new CompositorEntry(compositor, controller, timeline);
            Compositors[key] = entry;
            WriteProfileDiagnostic(receiver, profileAsset, compositor);
            return entry;
        }

        /// <summary>
        /// Compositor を作り直したときだけ Receiver の診断（Profile 領域）を書き換える（毎フレーム Revision を進めない）。
        /// </summary>
        private static void WriteProfileDiagnostic(
            FacialTimelineReceiver receiver,
            FacialCharacterProfileSO profileAsset,
            TimelinePreviewCompositor compositor)
        {
            if (compositor.BakeLocate.Bake == null)
            {
                return;
            }

            TimelineDiagnosticItem item = compositor.ProfileCheck == TimelineDiagnosticCode.ProfileMismatch
                ? new TimelineDiagnosticItem(
                    TimelineDiagnosticArea.Profile,
                    TimelineDiagnosticCode.ProfileMismatch,
                    TimelineDiagnosticSeverity.Warning,
                    profileAsset.name,
                    ProfileMismatchDetail)
                : new TimelineDiagnosticItem(
                    TimelineDiagnosticArea.Profile,
                    TimelineDiagnosticCode.ProfileMatched,
                    TimelineDiagnosticSeverity.Ok,
                    profileAsset.name,
                    ProfileMatchedDetail);
            receiver.Diagnostics.ReplaceArea(TimelineDiagnosticArea.Profile, new[] { item });
        }

        private static void MarkDirtyOnce(CompositorEntry entry, TimelineAsset timeline, TimelineDirtyReason reason)
        {
            if (entry.MarkedDirty)
            {
                return;
            }

            TimelineEditChangeWatcher watcher = TimelineEditorServices.ChangeWatcher;
            if (watcher == null)
            {
                return;
            }

            // 予約は Compositor 1 つにつき 1 回（スクラブ中に毎フレーム呼んでデバウンスを延ばし続けない）。
            // Play 遷移中などで Ignored になった場合は次の評価で再試行する。
            MarkDirtyResult result = watcher.MarkDirty(timeline, reason);
            entry.MarkedDirty = result != MarkDirtyResult.Ignored;
        }

        private static void EnsureWatcherSubscription()
        {
            TimelineEditChangeWatcher watcher = TimelineEditorServices.ChangeWatcher;
            if (ReferenceEquals(watcher, _subscribedWatcher))
            {
                return;
            }

            if (_subscribedWatcher != null)
            {
                _subscribedWatcher.BakeUpdated -= OnBakeUpdated;
            }

            _subscribedWatcher = watcher;
            if (watcher != null)
            {
                watcher.BakeUpdated += OnBakeUpdated;
            }
        }

        private static void OnBakeUpdated(TimelineAsset timeline, FacialTimelineBakeAsset bake, TimelineDirtyReason reason)
        {
            StaleCompositorKeys.Clear();
            foreach (KeyValuePair<int, CompositorEntry> pair in Compositors)
            {
                if (timeline == null || ReferenceEquals(pair.Value.Timeline, timeline))
                {
                    StaleCompositorKeys.Add(pair.Key);
                }
            }

            RemoveCompositors(StaleCompositorKeys);
        }

        private static void PruneDestroyedCompositors()
        {
            StaleCompositorKeys.Clear();
            foreach (KeyValuePair<int, CompositorEntry> pair in Compositors)
            {
                if (pair.Value.Controller == null || pair.Value.Timeline == null)
                {
                    StaleCompositorKeys.Add(pair.Key);
                }
            }

            RemoveCompositors(StaleCompositorKeys);
        }

        private static void RemoveCompositors(List<int> keys)
        {
            for (int i = 0; i < keys.Count; i++)
            {
                if (Compositors.TryGetValue(keys[i], out CompositorEntry entry))
                {
                    entry.Compositor.Dispose();
                    Compositors.Remove(keys[i]);
                }
            }

            keys.Clear();
        }

        private static void WarnOnce(FacialTimelineReceiver receiver, TimelineDiagnosticCode code, string subject, string message)
        {
            if (!TimelineEditorServices.EditWarningGate.TryPass(receiver.GetInstanceID(), code, subject ?? string.Empty))
            {
                return;
            }

            Debug.LogWarning(LogPrefix + code + ": " + message, receiver);
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

        private sealed class CompositorEntry
        {
            public CompositorEntry(TimelinePreviewCompositor compositor, FacialController controller, TimelineAsset timeline)
            {
                Compositor = compositor;
                Controller = controller;
                Timeline = timeline;
            }

            public TimelinePreviewCompositor Compositor { get; }

            public FacialController Controller { get; }

            public TimelineAsset Timeline { get; }

            /// <summary>この Compositor の不整合（Conflict / ProfileMismatch）で再ベイクを予約済みか。</summary>
            public bool MarkedDirty { get; set; }
        }
    }
}
