using System;
using System.Collections.Generic;
using Hidano.FacialControl.Adapters.Playable;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Editor.AutoExport;
using Hidano.FacialControl.Timeline.Adapters;
using Hidano.FacialControl.Timeline.Adapters.Assets;
using Hidano.FacialControl.Timeline.Adapters.Session;
using Hidano.FacialControl.Timeline.Domain.Diagnostics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Hidano.FacialControl.Timeline.Editor
{
    /// <summary>
    /// Timeline の再ベイク実行・Bake 参照の修復・Play 移行前の Profile 直列同期・Edit 復帰時の無言修復を行う静的サービス（D6 / D8）。
    /// </summary>
    /// <remarks>
    /// <para>Receiver の <see cref="FacialTimelineReceiver.BakeAsset"/> は任意の上書き欄なので自動では書かない。ユーザーが既に
    /// 同じ Timeline の Bake を明示設定している場合だけ、再ベイク後の Bake へ追従更新する（Undo 可能）。</para>
    /// <para>Play 中（Play 遷移中を含む）の保存フックは再ベイクも Receiver 更新もせず、対象を記録して Edit 復帰時に処理する。</para>
    /// </remarks>
    [InitializeOnLoad]
    public static class TimelineBakeDirtyWatcher
    {
        private const string LogPrefix = "[TimelineBakeDirtyWatcher] ";

        private static readonly Dictionary<string, PendingRepairRequest> PendingRepairRequests =
            new Dictionary<string, PendingRepairRequest>(StringComparer.Ordinal);
        private static readonly HashSet<string> PendingSavedPaths = new HashSet<string>(StringComparer.Ordinal);
        private static bool _suppressSaveHook;

        /// <summary>Play 遷移中の判定（既定 <see cref="EditorApplication.isPlayingOrWillChangePlaymode"/>。テストから差し替え可）。</summary>
        internal static Func<bool> IsPlayModeTransition = () => EditorApplication.isPlayingOrWillChangePlaymode;

        static TimelineBakeDirtyWatcher()
        {
            FacialTimelineReceiver.BakeIssueDetected -= OnBakeIssueDetected;
            FacialTimelineReceiver.BakeIssueDetected += OnBakeIssueDetected;

            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        // ================================================================
        // 保存フック（AssetModificationProcessor から）
        // ================================================================

        public static string[] OnWillSaveAssets(string[] paths)
        {
            if (_suppressSaveHook || paths == null || paths.Length == 0)
            {
                return paths;
            }

            string[] interestingPaths = FilterInterestingPaths(paths);
            if (interestingPaths.Length == 0)
            {
                return paths;
            }

            if (IsPlaying())
            {
                RecordPendingSavedPaths(interestingPaths);
                return paths;
            }

            string[] capturedPaths = interestingPaths;
            EditorApplication.delayCall += () => ProcessTrackedAssetPathsNow(capturedPaths);
            return paths;
        }

        public static void ProcessTrackedAssetPathsNow(string[] paths)
        {
            if (paths == null || paths.Length == 0)
            {
                return;
            }

            // 保存フックの delayCall が Play 中に届いた場合も、再ベイクと Receiver 更新はせず Edit 復帰まで遅らせる。
            if (IsPlaying())
            {
                RecordPendingSavedPaths(paths);
                return;
            }

            var requests = new Dictionary<string, RebakeRequest>(StringComparer.Ordinal);
            CollectRequestsForPaths(paths, requests);
            ExecuteRequests(requests.Values, null);
        }

        // ================================================================
        // 再ベイク口
        // ================================================================

        /// <summary>
        /// Timeline だけを指定して再ベイクする。Profile SO は <see cref="TimelineProfileSource.TryResolveProfileAssetForTimeline"/> で解決する。
        /// </summary>
        public static RebakeOutcome RebakeNow(TimelineAsset timeline, out FacialTimelineBakeAsset bake, out string failureReason)
        {
            bake = null;
            if (timeline == null)
            {
                failureReason = "Timeline asset is missing.";
                return RebakeOutcome.Failed;
            }

            if (!TimelineProfileSource.TryResolveProfileAssetForTimeline(timeline, out FacialCharacterProfileSO profileAsset))
            {
                failureReason = $"Profile asset could not be resolved for timeline '{timeline.name}'.";
                return RebakeOutcome.Failed;
            }

            return RebakeNow(timeline, profileAsset, out bake, out failureReason);
        }

        /// <summary>
        /// 指定 Profile SO で Timeline を再ベイクする。Profile 内容ハッシュ → Source ハッシュが一致すれば焼き直さず、
        /// Bake 参照の修復（全 Facial トラックへ同一参照）だけ行う。
        /// </summary>
        /// <returns>
        /// 焼き直したら <see cref="RebakeOutcome.Rebaked"/>、参照だけ変えたら <see cref="RebakeOutcome.ReferencesRepaired"/>、
        /// 何もしなければ <see cref="RebakeOutcome.NoChange"/>、失敗なら <see cref="RebakeOutcome.Failed"/>（前回の Bake を保持）。
        /// </returns>
        public static RebakeOutcome RebakeNow(
            TimelineAsset timeline,
            FacialCharacterProfileSO profileAsset,
            out FacialTimelineBakeAsset bake,
            out string failureReason)
        {
            bake = null;
            failureReason = string.Empty;

            if (timeline == null)
            {
                failureReason = "Timeline asset is missing.";
                return RebakeOutcome.Failed;
            }

            if (profileAsset == null)
            {
                failureReason = "Profile asset is missing.";
                return RebakeOutcome.Failed;
            }

            string timelinePath = AssetDatabase.GetAssetPath(timeline);
            if (string.IsNullOrEmpty(timelinePath))
            {
                failureReason = $"Timeline asset path could not be resolved for '{timeline.name}'.";
                return RebakeOutcome.Failed;
            }

            FacialTimelineBakeAsset targetBake = ResolveTargetBake(timeline, timelinePath);
            bool created = false;

            try
            {
                _suppressSaveHook = true;

                if (targetBake != null && TimelineBakeService.IsStale(timeline, profileAsset, targetBake) == BakeStaleReason.None)
                {
                    bool referencesChanged = BakeReferenceWriter.Apply(timeline, targetBake);
                    bool receiversChanged = UpdateExplicitReceiverOverrides(timelinePath, targetBake);
                    if (referencesChanged)
                    {
                        AssetDatabase.SaveAssetIfDirty(targetBake);
                        AssetDatabase.SaveAssetIfDirty(timeline);
                    }

                    bake = targetBake;
                    return referencesChanged || receiversChanged ? RebakeOutcome.ReferencesRepaired : RebakeOutcome.NoChange;
                }

                if (targetBake == null)
                {
                    targetBake = ScriptableObject.CreateInstance<FacialTimelineBakeAsset>();
                    targetBake.name = "FacialTimelineBake";
                    AssetDatabase.AddObjectToAsset(targetBake, timeline);
                    created = true;
                }

                // UpdateBakeAsset は焼いた後に BakeReferenceWriter.Apply を必ず通す（全 Facial トラックへ同一参照）。
                TimelineBakeService.UpdateBakeAsset(timeline, profileAsset, targetBake);
                EditorUtility.SetDirty(targetBake);
                EditorUtility.SetDirty(timeline);
                UpdateExplicitReceiverOverrides(timelinePath, targetBake);

                AssetDatabase.SaveAssetIfDirty(targetBake);
                AssetDatabase.SaveAssetIfDirty(timeline);
                bake = targetBake;
                return RebakeOutcome.Rebaked;
            }
            catch (Exception ex)
            {
                failureReason = ex.Message;
                if (created && targetBake != null)
                {
                    UnityEngine.Object.DestroyImmediate(targetBake, true);
                }

                bake = null;
                return RebakeOutcome.Failed;
            }
            finally
            {
                _suppressSaveHook = false;
            }
        }

        // ================================================================
        // Play 遷移
        // ================================================================

        /// <summary>
        /// Edit 復帰時の無言修復。Play 中に記録した保存対象、Receiver の診断（BakeStale / ProfileMismatch / 参照不整合）、
        /// シーン上の Director の Timeline を照合し、必要なものだけ再ベイクする。修復した場合は Console に Info を 1 行出す
        /// （失敗があれば Warning 1 行）。ダイアログは出さない。
        /// </summary>
        public static RepairRunResult TryRepairPendingSessionIssuesNow()
        {
            var requests = new Dictionary<string, RebakeRequest>(StringComparer.Ordinal);
            var failures = new List<string>();

            // (1) Play 中に記録した保存対象。
            if (PendingSavedPaths.Count > 0)
            {
                string[] saved = new string[PendingSavedPaths.Count];
                PendingSavedPaths.CopyTo(saved);
                PendingSavedPaths.Clear();
                CollectRequestsForPaths(saved, requests);
            }

            // (2) Play 中に Receiver が通知した旧イベント（Editor 購読の一元化で撤去予定）。
            if (PendingRepairRequests.Count > 0)
            {
                PendingRepairRequest[] pending = new PendingRepairRequest[PendingRepairRequests.Count];
                PendingRepairRequests.Values.CopyTo(pending, 0);
                PendingRepairRequests.Clear();
                for (int i = 0; i < pending.Length; i++)
                {
                    PendingRepairRequest request = pending[i];
                    if (requests.ContainsKey(request.TimelinePath))
                    {
                        continue;
                    }

                    if (!TryLoadTimeline(request.TimelinePath, out TimelineAsset timeline))
                    {
                        failures.Add($"Timeline not found: {request.TimelinePath}");
                        continue;
                    }

                    if (!TryResolveProfileAsset(request.TimelinePath, request.ProfileAssetGuid, out FacialCharacterProfileSO profileAsset))
                    {
                        failures.Add($"Profile not found for timeline: {request.TimelinePath}");
                        continue;
                    }

                    requests[request.TimelinePath] = new RebakeRequest(timeline, profileAsset);
                }
            }

            // (3) Receiver の診断状態。
            CollectReceiverDiagnosticRequests(requests);

            // (4) シーン上の Director の Timeline（鮮度・参照不整合）。
            CollectOpenSceneRequests(requests);

            int attempted = failures.Count;
            int succeeded = 0;
            foreach (RebakeRequest request in requests.Values)
            {
                RebakeOutcome outcome = RebakeNow(request.Timeline, request.ProfileAsset, out _, out string failureReason);
                switch (outcome)
                {
                    case RebakeOutcome.Rebaked:
                    case RebakeOutcome.ReferencesRepaired:
                        attempted++;
                        succeeded++;
                        break;
                    case RebakeOutcome.Failed:
                        attempted++;
                        failures.Add($"{AssetDatabase.GetAssetPath(request.Timeline)}: {failureReason}");
                        break;
                }
            }

            if (attempted == 0)
            {
                return RepairRunResult.Empty;
            }

            string message = failures.Count == 0
                ? $"Play モード後に Timeline の Bake を {succeeded} 件自動修復しました。"
                : BuildFailureMessage(attempted, succeeded, failures);

            if (failures.Count == 0)
            {
                Debug.Log(LogPrefix + message);
            }
            else
            {
                Debug.LogWarning(LogPrefix + message);
            }

            return new RepairRunResult(attempted, succeeded, failures.Count, message);
        }

        /// <summary>
        /// Play 移行前（ExitingEditMode）の直列同期。シーン上の Director から解決した (Timeline, Profile SO) について、
        /// SO ごとに 1 回 AutoExporter の冪等入口で profile.json を最新化 → Profile 読込キャッシュ無効化 → 再解決 →
        /// 鮮度と Bake 参照を照合し、必要なら再ベイクする。Timeline 側は profile.json を書かない（書くのは AutoExporter）。
        /// </summary>
        public static void ProcessOpenSceneTimelinesNow()
        {
            List<RebakeRequest> pairs = CollectOpenScenePairs();
            var exportedProfiles = new HashSet<FacialCharacterProfileSO>();

            for (int i = 0; i < pairs.Count; i++)
            {
                FacialCharacterProfileSO profileAsset = pairs[i].ProfileAsset;
                if (!exportedProfiles.Add(profileAsset))
                {
                    continue;
                }

                try
                {
                    _suppressSaveHook = true;
                    FacialCharacterProfileAutoExporter.ExportIfEnabled(profileAsset);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"{LogPrefix}Profile '{profileAsset.name}' の profile.json 同期に失敗しました: {ex.Message}");
                }
                finally
                {
                    _suppressSaveHook = false;
                }

                TimelineProfileSource.InvalidateCache(profileAsset);
            }

            var processed = new HashSet<TimelineAsset>();
            for (int i = 0; i < pairs.Count; i++)
            {
                RebakeRequest pair = pairs[i];
                if (!processed.Add(pair.Timeline))
                {
                    continue;
                }

                // RebakeNow 内で Resolve（= LoadProfile の現在値）によるハッシュ照合と Bake 参照の修復を行う。
                RebakeOutcome outcome = RebakeNow(pair.Timeline, pair.ProfileAsset, out _, out string failureReason);
                if (outcome == RebakeOutcome.Failed)
                {
                    Debug.LogWarning($"{LogPrefix}Timeline '{pair.Timeline.name}' の Play 前再ベイクに失敗しました: {failureReason}");
                }
            }
        }

        /// <summary>記録済みの修復対象を破棄する（Play 中の変更を捨てる / テスト）。</summary>
        internal static void ClearPendingRepairs()
        {
            PendingRepairRequests.Clear();
            PendingSavedPaths.Clear();
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingEditMode)
            {
                ProcessOpenSceneTimelinesNow();
                return;
            }

            if (change == PlayModeStateChange.EnteredEditMode)
            {
                TryRepairPendingSessionIssuesNow();
            }
        }

        private static void OnBakeIssueDetected(BakeInspectionIssue issue)
        {
            if (issue.Status != BakeInspectionStatus.HashMismatch || issue.Timeline == null || issue.ProfileSource == null)
            {
                return;
            }

            string timelinePath = AssetDatabase.GetAssetPath(issue.Timeline);
            string profilePath = AssetDatabase.GetAssetPath(issue.ProfileSource);
            if (string.IsNullOrEmpty(timelinePath) || string.IsNullOrEmpty(profilePath))
            {
                return;
            }

            string profileAssetGuid = AssetDatabase.AssetPathToGUID(profilePath);
            if (string.IsNullOrEmpty(profileAssetGuid))
            {
                return;
            }

            PendingRepairRequests[timelinePath] = new PendingRepairRequest(timelinePath, profileAssetGuid);
        }

        // ================================================================
        // 収集
        // ================================================================

        private static bool IsPlaying()
        {
            Func<bool> probe = IsPlayModeTransition;
            return probe != null && probe();
        }

        private static void RecordPendingSavedPaths(string[] paths)
        {
            for (int i = 0; i < paths.Length; i++)
            {
                if (!string.IsNullOrEmpty(paths[i]))
                {
                    PendingSavedPaths.Add(paths[i]);
                }
            }
        }

        private static void CollectRequestsForPaths(string[] paths, IDictionary<string, RebakeRequest> requests)
        {
            for (int i = 0; i < paths.Length; i++)
            {
                string path = paths[i];
                if (string.IsNullOrEmpty(path))
                {
                    continue;
                }

                Type assetType = AssetDatabase.GetMainAssetTypeAtPath(path);
                if (assetType == typeof(TimelineAsset))
                {
                    RegisterTimelineRequest(path, requests);
                    continue;
                }

                if (assetType != null && typeof(FacialCharacterProfileSO).IsAssignableFrom(assetType))
                {
                    RegisterProfileRequests(path, requests);
                }
            }
        }

        private static void CollectReceiverDiagnosticRequests(IDictionary<string, RebakeRequest> requests)
        {
            FacialTimelineReceiver[] receivers = UnityEngine.Object.FindObjectsByType<FacialTimelineReceiver>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

            for (int i = 0; i < receivers.Length; i++)
            {
                FacialTimelineReceiver receiver = receivers[i];
                FacialTimelineDiagnostics diagnostics = receiver.Diagnostics;
                if (!diagnostics.Contains(TimelineDiagnosticCode.BakeStale)
                    && !diagnostics.Contains(TimelineDiagnosticCode.ProfileMismatch)
                    && !diagnostics.Contains(TimelineDiagnosticCode.BakeReferenceConflict)
                    && !diagnostics.Contains(TimelineDiagnosticCode.BakeLegacyExport))
                {
                    continue;
                }

                TimelineAsset timeline = receiver.ActiveTimeline;
                if (timeline == null)
                {
                    PlayableDirector director = TimelineTrackBindingResolver.ResolveDirector(receiver, receiver.DirectorOverride, out _);
                    timeline = director != null ? director.playableAsset as TimelineAsset : null;
                }

                string timelinePath = timeline != null ? AssetDatabase.GetAssetPath(timeline) : null;
                if (string.IsNullOrEmpty(timelinePath) || requests.ContainsKey(timelinePath))
                {
                    continue;
                }

                FacialController controller = receiver.GetComponent<FacialController>();
                FacialCharacterProfileSO profileAsset = controller != null ? controller.CharacterSO : null;
                if (profileAsset == null && !TimelineProfileSource.TryResolveProfileAssetForTimeline(timeline, out profileAsset))
                {
                    continue;
                }

                requests[timelinePath] = new RebakeRequest(timeline, profileAsset);
            }
        }

        private static void CollectOpenSceneRequests(IDictionary<string, RebakeRequest> requests)
        {
            List<RebakeRequest> pairs = CollectOpenScenePairs();
            for (int i = 0; i < pairs.Count; i++)
            {
                string timelinePath = AssetDatabase.GetAssetPath(pairs[i].Timeline);
                if (!requests.ContainsKey(timelinePath))
                {
                    requests[timelinePath] = pairs[i];
                }
            }
        }

        private static List<RebakeRequest> CollectOpenScenePairs()
        {
            var pairs = new List<RebakeRequest>();
            PlayableDirector[] directors = UnityEngine.Object.FindObjectsByType<PlayableDirector>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

            for (int i = 0; i < directors.Length; i++)
            {
                PlayableDirector director = directors[i];
                if (!(director.playableAsset is TimelineAsset timeline))
                {
                    continue;
                }

                if (string.IsNullOrEmpty(AssetDatabase.GetAssetPath(timeline)))
                {
                    continue;
                }

                if (!TryResolveDirectorProfile(director, timeline, out FacialCharacterProfileSO profileAsset))
                {
                    continue;
                }

                pairs.Add(new RebakeRequest(timeline, profileAsset));
            }

            return pairs;
        }

        private static string[] FilterInterestingPaths(string[] paths)
        {
            var interesting = new List<string>(paths.Length);
            for (int i = 0; i < paths.Length; i++)
            {
                string path = paths[i];
                if (string.IsNullOrEmpty(path))
                {
                    continue;
                }

                Type assetType = AssetDatabase.GetMainAssetTypeAtPath(path);
                if (assetType == typeof(TimelineAsset)
                    || (assetType != null && typeof(FacialCharacterProfileSO).IsAssignableFrom(assetType)))
                {
                    interesting.Add(path);
                }
            }

            return interesting.Count == 0 ? Array.Empty<string>() : interesting.ToArray();
        }

        private static void RegisterTimelineRequest(string timelinePath, IDictionary<string, RebakeRequest> requests)
        {
            if (requests.ContainsKey(timelinePath) || !TryLoadTimeline(timelinePath, out TimelineAsset timeline))
            {
                return;
            }

            if (!TimelineProfileSource.TryResolveProfileAssetForTimeline(timeline, out FacialCharacterProfileSO profileAsset))
            {
                return;
            }

            requests[timelinePath] = new RebakeRequest(timeline, profileAsset);
        }

        private static void RegisterProfileRequests(string profilePath, IDictionary<string, RebakeRequest> requests)
        {
            string profileAssetGuid = AssetDatabase.AssetPathToGUID(profilePath);
            if (string.IsNullOrEmpty(profileAssetGuid))
            {
                return;
            }

            FacialCharacterProfileSO profileAsset = AssetDatabase.LoadAssetAtPath<FacialCharacterProfileSO>(profilePath);
            if (profileAsset == null)
            {
                return;
            }

            TimelineProfileSource.InvalidateCache(profileAsset);
            string[] timelineGuids = AssetDatabase.FindAssets("t:TimelineAsset");
            for (int i = 0; i < timelineGuids.Length; i++)
            {
                string timelinePath = AssetDatabase.GUIDToAssetPath(timelineGuids[i]);
                if (requests.ContainsKey(timelinePath))
                {
                    continue;
                }

                FacialTimelineBakeAsset bake = FindBakeAsset(timelinePath);
                if (bake == null || !string.Equals(bake.ProfileAssetGuid, profileAssetGuid, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!TryLoadTimeline(timelinePath, out TimelineAsset timeline))
                {
                    continue;
                }

                requests[timelinePath] = new RebakeRequest(timeline, profileAsset);
            }
        }

        private static void ExecuteRequests(IEnumerable<RebakeRequest> requests, List<string> failures)
        {
            foreach (RebakeRequest request in requests)
            {
                RebakeOutcome outcome = RebakeNow(request.Timeline, request.ProfileAsset, out _, out string failureReason);
                if (outcome == RebakeOutcome.Failed)
                {
                    failures?.Add(failureReason);
                }
            }
        }

        // ================================================================
        // Receiver 上書き欄・Bake 解決
        // ================================================================

        /// <summary>
        /// Receiver の上書き欄が「同じ Timeline の別の Bake」を明示指定している場合だけ、<paramref name="bake"/> に追従させる
        /// （Undo.RecordObject + SetDirty）。未設定（null）や他 Timeline の Bake は触らない。
        /// </summary>
        private static bool UpdateExplicitReceiverOverrides(string timelinePath, FacialTimelineBakeAsset bake)
        {
            FacialTimelineReceiver[] receivers = UnityEngine.Object.FindObjectsByType<FacialTimelineReceiver>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

            bool changed = false;
            for (int i = 0; i < receivers.Length; i++)
            {
                FacialTimelineReceiver receiver = receivers[i];
                FacialTimelineBakeAsset current = receiver.BakeAsset;
                if (current == null || ReferenceEquals(current, bake))
                {
                    continue;
                }

                if (!string.Equals(AssetDatabase.GetAssetPath(current), timelinePath, StringComparison.Ordinal))
                {
                    continue;
                }

                Undo.RecordObject(receiver, "Update Timeline Bake Override");
                receiver.BakeAsset = bake;
                EditorUtility.SetDirty(receiver);
                changed = true;
            }

            return changed;
        }

        private static FacialTimelineBakeAsset ResolveTargetBake(TimelineAsset timeline, string timelinePath)
        {
            FacialTimelineBakeAsset trackBake = FacialTimelineBakeLocator.Locate(timeline, null).TrackBake;
            if (trackBake != null && string.Equals(AssetDatabase.GetAssetPath(trackBake), timelinePath, StringComparison.Ordinal))
            {
                return trackBake;
            }

            return FindBakeAsset(timelinePath);
        }

        private static bool TryResolveProfileAsset(
            string timelinePath,
            string profileAssetGuid,
            out FacialCharacterProfileSO profileAsset)
        {
            profileAsset = null;
            if (string.IsNullOrEmpty(profileAssetGuid))
            {
                return false;
            }

            string profilePath = AssetDatabase.GUIDToAssetPath(profileAssetGuid);
            if (string.IsNullOrEmpty(profilePath))
            {
                Debug.LogWarning($"{LogPrefix}Profile GUID '{profileAssetGuid}' could not be resolved for '{timelinePath}'.");
                return false;
            }

            profileAsset = AssetDatabase.LoadAssetAtPath<FacialCharacterProfileSO>(profilePath);
            if (profileAsset == null)
            {
                Debug.LogWarning($"{LogPrefix}Profile asset '{profilePath}' could not be loaded for '{timelinePath}'.");
                return false;
            }

            return true;
        }

        private static bool TryResolveDirectorProfile(
            PlayableDirector director,
            TimelineAsset timeline,
            out FacialCharacterProfileSO profileAsset)
        {
            profileAsset = null;
            foreach (TrackAsset track in timeline.GetOutputTracks())
            {
                if (!TryResolveReceiver(director.GetGenericBinding(track), out FacialTimelineReceiver receiver))
                {
                    continue;
                }

                FacialController controller = receiver.GetComponent<FacialController>();
                if (controller == null || controller.CharacterSO == null)
                {
                    continue;
                }

                profileAsset = controller.CharacterSO;
                return true;
            }

            return false;
        }

        private static bool TryResolveReceiver(object binding, out FacialTimelineReceiver receiver)
        {
            switch (binding)
            {
                case FacialTimelineReceiver directReceiver:
                    receiver = directReceiver;
                    return true;
                case GameObject gameObject:
                    receiver = gameObject.GetComponent<FacialTimelineReceiver>();
                    return receiver != null;
                case Component component:
                    receiver = component.GetComponent<FacialTimelineReceiver>();
                    return receiver != null;
                default:
                    receiver = null;
                    return false;
            }
        }

        private static bool TryLoadTimeline(string timelinePath, out TimelineAsset timeline)
        {
            timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(timelinePath);
            return timeline != null;
        }

        private static FacialTimelineBakeAsset FindBakeAsset(string timelinePath)
        {
            if (string.IsNullOrEmpty(timelinePath))
            {
                return null;
            }

            UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(timelinePath);
            for (int i = 0; i < assets.Length; i++)
            {
                if (assets[i] is FacialTimelineBakeAsset bakeAsset)
                {
                    return bakeAsset;
                }
            }

            return null;
        }

        private static string BuildFailureMessage(int attempted, int succeeded, IReadOnlyList<string> failures)
        {
            string summary = $"Play モード後の Timeline Bake 修復: 対象 {attempted} 件 / 成功 {succeeded} 件 / 失敗 {failures.Count} 件。";
            return failures.Count == 0 ? summary : summary + " " + string.Join(" / ", failures);
        }

        private readonly struct RebakeRequest
        {
            public RebakeRequest(TimelineAsset timeline, FacialCharacterProfileSO profileAsset)
            {
                Timeline = timeline;
                ProfileAsset = profileAsset;
            }

            public TimelineAsset Timeline { get; }

            public FacialCharacterProfileSO ProfileAsset { get; }
        }

        private readonly struct PendingRepairRequest
        {
            public PendingRepairRequest(string timelinePath, string profileAssetGuid)
            {
                TimelinePath = timelinePath;
                ProfileAssetGuid = profileAssetGuid;
            }

            public string TimelinePath { get; }

            public string ProfileAssetGuid { get; }
        }
    }

    /// <summary><see cref="TimelineBakeDirtyWatcher.TryRepairPendingSessionIssuesNow"/> の結果。</summary>
    public readonly struct RepairRunResult
    {
        public static RepairRunResult Empty => new RepairRunResult(0, 0, 0, string.Empty);

        public RepairRunResult(int attempted, int succeeded, int failed, string message)
        {
            Attempted = attempted;
            Succeeded = succeeded;
            Failed = failed;
            Message = message ?? string.Empty;
        }

        public int Attempted { get; }

        public int Succeeded { get; }

        public int Failed { get; }

        public string Message { get; }
    }

    /// <summary><see cref="TimelineBakeDirtyWatcher.RebakeNow(TimelineAsset, out FacialTimelineBakeAsset, out string)"/> を呼ぶ既定の再ベイク口。</summary>
    public sealed class TimelineBakeDirtyWatcherRebakeExecutor : IRebakeExecutor
    {
        public static readonly TimelineBakeDirtyWatcherRebakeExecutor Instance = new TimelineBakeDirtyWatcherRebakeExecutor();

        public RebakeOutcome Rebake(TimelineAsset timeline, out FacialTimelineBakeAsset bake, out string failureReason)
        {
            return TimelineBakeDirtyWatcher.RebakeNow(timeline, out bake, out failureReason);
        }
    }

    public sealed class TimelineBakeDirtyWatcherAssetHook : AssetModificationProcessor
    {
        public static string[] OnWillSaveAssets(string[] paths)
        {
            return TimelineBakeDirtyWatcher.OnWillSaveAssets(paths);
        }
    }
}
