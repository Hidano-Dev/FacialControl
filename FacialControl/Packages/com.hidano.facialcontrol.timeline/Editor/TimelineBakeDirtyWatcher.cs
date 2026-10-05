using System;
using System.Collections.Generic;
using Hidano.FacialControl.Adapters.Playable;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Timeline.Adapters;
using Hidano.FacialControl.Timeline.Adapters.Assets;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Hidano.FacialControl.Timeline.Editor
{
    [InitializeOnLoad]
    public static class TimelineBakeDirtyWatcher
    {
        private const string DialogTitle = "Timeline Bake Repair";
        private static readonly Dictionary<string, PendingRepairRequest> PendingRepairRequests =
            new Dictionary<string, PendingRepairRequest>(StringComparer.Ordinal);
        private static bool _suppressSaveHook;

        public static Action<string, string, string> DisplayDialog =
            (title, message, ok) => EditorUtility.DisplayDialog(title, message, ok);

        static TimelineBakeDirtyWatcher()
        {
            FacialTimelineReceiver.BakeIssueDetected -= OnBakeIssueDetected;
            FacialTimelineReceiver.BakeIssueDetected += OnBakeIssueDetected;

            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

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

            var requests = new Dictionary<string, RebakeRequest>(StringComparer.Ordinal);
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

                if (typeof(FacialCharacterProfileSO).IsAssignableFrom(assetType))
                {
                    RegisterProfileRequests(path, requests);
                }
            }

            ExecuteRequests(requests.Values);
        }

        internal static RepairRunResult TryRepairPendingSessionIssuesNow()
        {
            if (PendingRepairRequests.Count == 0)
            {
                return RepairRunResult.Empty;
            }

            PendingRepairRequest[] pending = new PendingRepairRequest[PendingRepairRequests.Count];
            PendingRepairRequests.Values.CopyTo(pending, 0);
            PendingRepairRequests.Clear();

            int attempted = 0;
            int succeeded = 0;
            var failures = new List<string>();

            for (int i = 0; i < pending.Length; i++)
            {
                PendingRepairRequest request = pending[i];
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

                attempted++;
                if (TryAutoRebake(timeline, profileAsset, out string failureReason))
                {
                    succeeded++;
                }
                else
                {
                    failures.Add($"{request.TimelinePath}: {failureReason}");
                }
            }

            if (attempted == 0 && failures.Count == 0)
            {
                return RepairRunResult.Empty;
            }

            string message = succeeded == attempted && failures.Count == 0
                ? $"Auto-rebaked {succeeded} timeline bake asset(s) after Play Mode."
                : BuildFailureMessage(attempted, succeeded, failures);

            return new RepairRunResult(attempted, succeeded, failures.Count, message);
        }

        public static RepairRunResult HandleEnteredEditModeNow()
        {
            RepairRunResult result = TryRepairPendingSessionIssuesNow();
            if (result.HasDialog)
            {
                DisplayDialog(DialogTitle, result.Message, "OK");
            }

            return result;
        }

        internal static void ProcessOpenSceneTimelinesNow()
        {
            var requests = new Dictionary<string, RebakeRequest>(StringComparer.Ordinal);
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

                string timelinePath = AssetDatabase.GetAssetPath(timeline);
                if (string.IsNullOrEmpty(timelinePath))
                {
                    continue;
                }

                if (!TryResolveDirectorProfile(director, timeline, out FacialCharacterProfileSO profileAsset))
                {
                    continue;
                }

                FacialTimelineBakeAsset bake = FindBakeAsset(timelinePath);
                if (bake != null && TimelineBakeService.IsStale(timeline, profileAsset, bake) == BakeStaleReason.None)
                {
                    continue;
                }

                requests[timelinePath] = new RebakeRequest(timeline, profileAsset);
            }

            ExecuteRequests(requests.Values);
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingEditMode)
            {
                ProcessOpenSceneTimelinesNow();
                return;
            }

            if (change != PlayModeStateChange.EnteredEditMode)
            {
                return;
            }

            HandleEnteredEditModeNow();
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
                if (assetType == typeof(TimelineAsset) || typeof(FacialCharacterProfileSO).IsAssignableFrom(assetType))
                {
                    interesting.Add(path);
                }
            }

            return interesting.Count == 0 ? Array.Empty<string>() : interesting.ToArray();
        }

        private static void RegisterTimelineRequest(string timelinePath, IDictionary<string, RebakeRequest> requests)
        {
            if (!TryLoadTimeline(timelinePath, out TimelineAsset timeline))
            {
                return;
            }

            if (!TryResolveProfileAssetForTimeline(timelinePath, out FacialCharacterProfileSO profileAsset))
            {
                return;
            }

            FacialTimelineBakeAsset bake = FindBakeAsset(timelinePath);
            if (bake != null && TimelineBakeService.IsStale(timeline, profileAsset, bake) == BakeStaleReason.None)
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

            string[] timelineGuids = AssetDatabase.FindAssets("t:TimelineAsset");
            for (int i = 0; i < timelineGuids.Length; i++)
            {
                string timelinePath = AssetDatabase.GUIDToAssetPath(timelineGuids[i]);
                FacialTimelineBakeAsset bake = FindBakeAsset(timelinePath);
                if (bake == null || !string.Equals(bake.ProfileAssetGuid, profileAssetGuid, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!TryLoadTimeline(timelinePath, out TimelineAsset timeline))
                {
                    continue;
                }

                if (TimelineBakeService.IsStale(timeline, profileAsset, bake) == BakeStaleReason.None)
                {
                    continue;
                }

                requests[timelinePath] = new RebakeRequest(timeline, profileAsset);
            }
        }

        private static void ExecuteRequests(IEnumerable<RebakeRequest> requests)
        {
            foreach (RebakeRequest request in requests)
            {
                TryAutoRebake(request.Timeline, request.ProfileAsset, out _);
            }
        }

        private static bool TryAutoRebake(
            TimelineAsset timeline,
            FacialCharacterProfileSO profileAsset,
            out string failureReason)
        {
            failureReason = string.Empty;

            if (timeline == null)
            {
                failureReason = "Timeline asset is missing.";
                return false;
            }

            if (profileAsset == null)
            {
                failureReason = "Profile asset is missing.";
                return false;
            }

            string timelinePath = AssetDatabase.GetAssetPath(timeline);
            if (string.IsNullOrEmpty(timelinePath))
            {
                failureReason = $"Timeline asset path could not be resolved for '{timeline.name}'.";
                return false;
            }

            FacialTimelineBakeAsset targetBake = FindBakeAsset(timelinePath);
            bool created = false;

            try
            {
                if (targetBake == null)
                {
                    targetBake = ScriptableObject.CreateInstance<FacialTimelineBakeAsset>();
                    targetBake.name = "FacialTimelineBake";
                    AssetDatabase.AddObjectToAsset(targetBake, timeline);
                    created = true;
                }

                TimelineBakeService.UpdateBakeAsset(timeline, profileAsset, targetBake);
                EditorUtility.SetDirty(targetBake);
                EditorUtility.SetDirty(timeline);
                UpdateLoadedReceiverReferences(timeline, targetBake);

                _suppressSaveHook = true;
                AssetDatabase.SaveAssetIfDirty(targetBake);
                AssetDatabase.SaveAssetIfDirty(timeline);
                return true;
            }
            catch (Exception ex)
            {
                failureReason = ex.Message;
                if (created && targetBake != null)
                {
                    UnityEngine.Object.DestroyImmediate(targetBake, true);
                }

                return false;
            }
            finally
            {
                _suppressSaveHook = false;
            }
        }

        private static void UpdateLoadedReceiverReferences(TimelineAsset timeline, FacialTimelineBakeAsset bakeAsset)
        {
            PlayableDirector[] directors = UnityEngine.Object.FindObjectsByType<PlayableDirector>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

            for (int i = 0; i < directors.Length; i++)
            {
                PlayableDirector director = directors[i];
                if (!ReferenceEquals(director.playableAsset, timeline))
                {
                    continue;
                }

                foreach (TrackAsset track in timeline.GetOutputTracks())
                {
                    if (!TryResolveReceiver(director.GetGenericBinding(track), out FacialTimelineReceiver receiver))
                    {
                        continue;
                    }

                    receiver.BakeAsset = bakeAsset;
                }
            }
        }

        private static bool TryResolveProfileAssetForTimeline(string timelinePath, out FacialCharacterProfileSO profileAsset)
        {
            profileAsset = null;
            FacialTimelineBakeAsset bake = FindBakeAsset(timelinePath);
            if (bake != null && TryResolveProfileAsset(timelinePath, bake.ProfileAssetGuid, out profileAsset))
            {
                return true;
            }

            if (!TryLoadTimeline(timelinePath, out TimelineAsset timeline))
            {
                return false;
            }

            PlayableDirector[] directors = UnityEngine.Object.FindObjectsByType<PlayableDirector>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

            for (int i = 0; i < directors.Length; i++)
            {
                PlayableDirector director = directors[i];
                if (!ReferenceEquals(director.playableAsset, timeline))
                {
                    continue;
                }

                if (TryResolveDirectorProfile(director, timeline, out profileAsset))
                {
                    return true;
                }
            }

            return false;
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
                Debug.LogWarning($"[TimelineBakeDirtyWatcher] Profile GUID '{profileAssetGuid}' could not be resolved for '{timelinePath}'.");
                return false;
            }

            profileAsset = AssetDatabase.LoadAssetAtPath<FacialCharacterProfileSO>(profilePath);
            if (profileAsset == null)
            {
                Debug.LogWarning($"[TimelineBakeDirtyWatcher] Profile asset '{profilePath}' could not be loaded for '{timelinePath}'.");
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
            string summary = $"Attempted {attempted} repair(s). Success: {succeeded}. Failure: {failures.Count}.";
            if (failures.Count == 0)
            {
                return summary;
            }

            return summary + "\n\n" + string.Join("\n", failures);
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

        public bool HasDialog => Attempted > 0 || Failed > 0;
    }

    public sealed class TimelineBakeDirtyWatcherAssetHook : AssetModificationProcessor
    {
        public static string[] OnWillSaveAssets(string[] paths)
        {
            return TimelineBakeDirtyWatcher.OnWillSaveAssets(paths);
        }
    }
}
