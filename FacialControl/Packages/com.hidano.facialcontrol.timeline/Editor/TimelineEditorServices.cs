using System;
using System.Collections.Generic;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Editor.AutoExport;
using Hidano.FacialControl.Timeline.Adapters.Assets;
using Hidano.FacialControl.Timeline.Clips;
using Hidano.FacialControl.Timeline.Domain.Diagnostics;
using UnityEditor;
using UnityEditor.Timeline;
using UnityEngine.Timeline;

namespace Hidano.FacialControl.Timeline.Editor
{
    /// <summary>
    /// timeline Editor の Unity イベント購読の唯一の所有者（D8）。<see cref="TimelineEditChangeWatcher"/> を生成・保持し、
    /// <see cref="TimelineBakeDirtyWatcher"/> の Play 遷移処理を配送する。
    /// </summary>
    /// <remarks>
    /// <para>固定購読（6）: ObjectChangeEvents.changesPublished / Undo.undoRedoPerformed / EditorApplication.playModeStateChanged /
    /// AssemblyReloadEvents.beforeAssemblyReload / EditorApplication.quitting / FacialCharacterProfileAutoExporter.Exported。
    /// EditorApplication.update は Watcher に pending がある間だけ参照カウントで購読する。</para>
    /// <para>TrackEditor / ClipEditor / Inspector は購読を持たず <see cref="ChangeWatcher"/> の MarkDirty を呼ぶだけ。</para>
    /// </remarks>
    [InitializeOnLoad]
    public static class TimelineEditorServices
    {
        private const int FixedSubscriptionCount = 6;

        private static bool _initialized;
        private static int _tickRefCount;
        private static bool _updateSubscribed;

        static TimelineEditorServices()
        {
            EnsureInitialized();
        }

        /// <summary>変更検知の合流先。<see cref="EnsureInitialized"/> 後は非 null（Shutdown 後は null）。</summary>
        public static TimelineEditChangeWatcher ChangeWatcher { get; private set; }

        public static bool IsInitialized => _initialized;

        /// <summary>テスト用: 現在登録している Unity イベント購読の数（update を含む）。</summary>
        public static int ActiveSubscriptionCount => (_initialized ? FixedSubscriptionCount : 0) + (_updateSubscribed ? 1 : 0);

        /// <summary>Edit の警告を 1 エポック 1 回に絞るゲート（EnteredEditMode でエポックをリセットする）。</summary>
        public static TimelineOnceWarningGate EditWarningGate { get; } = new TimelineOnceWarningGate();

        /// <summary>テスト用: 次の <see cref="EnsureInitialized"/> で Watcher に渡す再ベイク口（null なら既定）。</summary>
        internal static IRebakeExecutor RebakeExecutorOverride { get; set; }

        /// <summary>テスト用: ExitingEditMode の直列処理（null なら <see cref="TimelineBakeDirtyWatcher.ProcessOpenSceneTimelinesNow"/>）。</summary>
        internal static Action ExitingEditModeProcessor { get; set; }

        /// <summary>冪等。初期化済みなら何もしない。Watcher 生成 + 固定 6 購読。</summary>
        public static void EnsureInitialized()
        {
            if (_initialized)
            {
                return;
            }

            _initialized = true;
            IRebakeExecutor executor = RebakeExecutorOverride ?? TrackingRebakeExecutor.Instance;
            ChangeWatcher = new TimelineEditChangeWatcher(executor, null, RequestTick, ReleaseTick);

            ObjectChangeEvents.changesPublished += OnChangesPublished;
            Undo.undoRedoPerformed += OnUndoRedoPerformed;
            EditorApplication.playModeStateChanged += DispatchPlayModeStateChanged;
            AssemblyReloadEvents.beforeAssemblyReload += Shutdown;
            EditorApplication.quitting += Shutdown;
            FacialCharacterProfileAutoExporter.Exported += OnProfileExported;
        }

        /// <summary>全購読解除（update 含む）+ Watcher 破棄（pending 破棄）。二重呼び出しは no-op。</summary>
        public static void Shutdown()
        {
            if (!_initialized)
            {
                return;
            }

            _initialized = false;
            ObjectChangeEvents.changesPublished -= OnChangesPublished;
            Undo.undoRedoPerformed -= OnUndoRedoPerformed;
            EditorApplication.playModeStateChanged -= DispatchPlayModeStateChanged;
            AssemblyReloadEvents.beforeAssemblyReload -= Shutdown;
            EditorApplication.quitting -= Shutdown;
            FacialCharacterProfileAutoExporter.Exported -= OnProfileExported;

            TimelineEditChangeWatcher watcher = ChangeWatcher;
            ChangeWatcher = null;
            watcher?.Dispose();

            _tickRefCount = 0;
            UnsubscribeUpdate();
        }

        /// <summary>Watcher: 最初の pending で update を購読する（参照カウント）。</summary>
        internal static void RequestTick()
        {
            _tickRefCount++;
            if (_tickRefCount == 1 && _initialized && !_updateSubscribed)
            {
                EditorApplication.update += OnUpdate;
                _updateSubscribed = true;
            }
        }

        /// <summary>Watcher: pending が 0 になったら update を解除する。</summary>
        internal static void ReleaseTick()
        {
            if (_tickRefCount > 0)
            {
                _tickRefCount--;
            }

            if (_tickRefCount == 0)
            {
                UnsubscribeUpdate();
            }
        }

        /// <summary>playModeStateChanged の配送（テストからも呼べる）。</summary>
        internal static void DispatchPlayModeStateChanged(PlayModeStateChange change)
        {
            switch (change)
            {
                case PlayModeStateChange.ExitingEditMode:
                    ChangeWatcher?.FlushNow();
                    (ExitingEditModeProcessor ?? TimelineBakeDirtyWatcher.ProcessOpenSceneTimelinesNow)();
                    break;
                case PlayModeStateChange.EnteredEditMode:
                    TimelineBakeDirtyWatcher.TryRepairPendingSessionIssuesNow();
                    EditWarningGate.ResetEpoch();
                    break;
                case PlayModeStateChange.ExitingPlayMode:
                    // Play 中の MarkDirty は捨てる（Edit 復帰の無言修復が照合する）。
                    ChangeWatcher?.DiscardPending();
                    break;
            }
        }

        private static void UnsubscribeUpdate()
        {
            if (_updateSubscribed)
            {
                EditorApplication.update -= OnUpdate;
                _updateSubscribed = false;
            }
        }

        private static void OnUpdate()
        {
            ChangeWatcher?.Tick();
        }

        private static void OnUndoRedoPerformed()
        {
            TimelineEditChangeWatcher watcher = ChangeWatcher;
            if (watcher == null)
            {
                return;
            }

            TimelineAsset inspected = TimelineEditor.inspectedAsset;
            if (inspected != null)
            {
                watcher.MarkDirty(inspected, TimelineDirtyReason.UndoRedo);
            }

            // Timeline ウィンドウが閉じていても対象を取りこぼさないよう、追跡中の全 Timeline を MarkDirty する（ハッシュ一致なら no-op）。
            var tracked = new List<TimelineAsset>(watcher.TrackedTimelines);
            for (int i = 0; i < tracked.Count; i++)
            {
                if (tracked[i] != null && !ReferenceEquals(tracked[i], inspected))
                {
                    watcher.MarkDirty(tracked[i], TimelineDirtyReason.UndoRedo);
                }
            }
        }

        private static void OnProfileExported(FacialCharacterProfileSO profileAsset)
        {
            if (profileAsset == null)
            {
                return;
            }

            TimelineProfileSource.InvalidateCache(profileAsset);
            ChangeWatcher?.MarkProfileChanged(profileAsset);
        }

        private static void OnChangesPublished(ref ObjectChangeEventStream stream)
        {
            TimelineEditChangeWatcher watcher = ChangeWatcher;
            if (watcher == null)
            {
                return;
            }

            for (int i = 0; i < stream.length; i++)
            {
                switch (stream.GetEventType(i))
                {
                    case ObjectChangeKind.ChangeAssetObjectProperties:
                        stream.GetChangeAssetObjectPropertiesEvent(i, out ChangeAssetObjectPropertiesEventArgs changed);
                        DispatchAssetChange(watcher, changed.instanceId, changed.guid);
                        break;
                    case ObjectChangeKind.CreateAssetObject:
                        stream.GetCreateAssetObjectEvent(i, out CreateAssetObjectEventArgs created);
                        DispatchAssetChange(watcher, created.instanceId, created.guid);
                        break;
                    case ObjectChangeKind.DestroyAssetObject:
                        stream.GetDestroyAssetObjectEvent(i, out DestroyAssetObjectEventArgs destroyed);
                        DispatchDestroyedAsset(watcher, destroyed.guid);
                        break;
                }
            }
        }

        private static void DispatchAssetChange(TimelineEditChangeWatcher watcher, int instanceId, GUID guid)
        {
            UnityEngine.Object obj = EditorUtility.InstanceIDToObject(instanceId);
            switch (obj)
            {
                case FacialCharacterProfileSO profileAsset:
                    TimelineProfileSource.InvalidateCache(profileAsset);
                    watcher.MarkProfileChanged(profileAsset);
                    return;
                case TimelineAsset timeline:
                    watcher.MarkDirty(timeline, TimelineDirtyReason.ObjectChange);
                    return;
                case TrackAsset track when track is IFacialTimelineBakeHolder:
                    watcher.MarkDirty(track.timelineAsset, TimelineDirtyReason.ObjectChange);
                    return;
                case FacialExpressionClip _:
                case FacialValueClip _:
                    MarkMainTimeline(watcher, guid);
                    return;
            }
        }

        private static void DispatchDestroyedAsset(TimelineEditChangeWatcher watcher, GUID guid)
        {
            MarkMainTimeline(watcher, guid);
        }

        private static void MarkMainTimeline(TimelineEditChangeWatcher watcher, GUID guid)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path) || AssetDatabase.GetMainAssetTypeAtPath(path) != typeof(TimelineAsset))
            {
                return;
            }

            TimelineAsset timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(path);
            if (timeline != null)
            {
                watcher.MarkDirty(timeline, TimelineDirtyReason.ObjectChange);
            }
        }

        /// <summary>
        /// 既定の再ベイク口。Timeline から Profile SO を解決して Watcher の追跡（SO → Timeline の逆引き）に登録し、
        /// <see cref="TimelineBakeDirtyWatcher.RebakeNow(TimelineAsset, FacialCharacterProfileSO, out FacialTimelineBakeAsset, out string)"/> を呼ぶ。
        /// </summary>
        private sealed class TrackingRebakeExecutor : IRebakeExecutor
        {
            public static readonly TrackingRebakeExecutor Instance = new TrackingRebakeExecutor();

            public RebakeOutcome Rebake(TimelineAsset timeline, out FacialTimelineBakeAsset bake, out string failureReason)
            {
                bake = null;
                if (!TimelineProfileSource.TryResolveProfileAssetForTimeline(timeline, out FacialCharacterProfileSO profileAsset))
                {
                    failureReason = $"Profile asset could not be resolved for timeline '{(timeline != null ? timeline.name : "null")}'.";
                    return RebakeOutcome.Failed;
                }

                ChangeWatcher?.TrackProfile(timeline, profileAsset);
                return TimelineBakeDirtyWatcher.RebakeNow(timeline, profileAsset, out bake, out failureReason);
            }
        }
    }
}
