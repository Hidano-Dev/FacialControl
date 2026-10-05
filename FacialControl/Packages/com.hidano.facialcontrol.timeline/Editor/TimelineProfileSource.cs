using System;
using System.Collections.Generic;
using System.IO;
using Hidano.FacialControl.Adapters.Playable;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Timeline.Adapters;
using Hidano.FacialControl.Timeline.Adapters.Assets;
using UnityEditor;
using UnityEditor.Timeline;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Hidano.FacialControl.Timeline.Editor
{
    /// <summary>
    /// Editor 系（Bake / 鮮度判定 / Validator / Exporter / Edit プレビュー / Edit 診断）が使う Profile の統一入口（D6）。
    /// 中身は Runtime の <see cref="FacialCharacterProfileSO.LoadProfile"/>（StreamingAssets の profile.json 優先、
    /// 無ければ SO）をそのまま呼ぶだけで、優先順位やパス規則を再実装しない。
    /// </summary>
    /// <remarks>
    /// <para>キャッシュキーは SO の instanceID + profile.json の有無 / 最終更新時刻 + SO のダーティ状態。
    /// profile.json はポーリングせず、<see cref="Resolve"/> を呼んだ時点でキーを比較して変化を検出する。</para>
    /// <para>ダーティな SO と未保存（非永続）の SO は、編集を検知する手段がキーに無いためキャッシュせず毎回読み直す。</para>
    /// <para><see cref="Resolve"/> は読むだけで profile.json を書かない。Play 中の Receiver / Connector は本クラスを使わず
    /// <c>FacialController.CurrentProfile</c> を使う（両者の一致は Profile 内容ハッシュで検証する）。</para>
    /// </remarks>
    public static class TimelineProfileSource
    {
        private const string ProfileJsonSuffix = "/" + FacialCharacterProfileSO.ProfileJsonFileName;

        private static readonly Dictionary<int, CacheEntry> Cache = new Dictionary<int, CacheEntry>();

        /// <summary>Profile SO から Runtime と同じ読込経路の Profile を返す（キャッシュ付き）。</summary>
        public static FacialProfile Resolve(FacialCharacterProfileSO profileAsset)
        {
            if (profileAsset == null)
            {
                throw new ArgumentNullException(nameof(profileAsset));
            }

            if (!EditorUtility.IsPersistent(profileAsset))
            {
                return profileAsset.LoadProfile();
            }

            int instanceId = profileAsset.GetInstanceID();
            CacheKey key = CaptureKey(profileAsset);
            if (!key.IsDirty
                && Cache.TryGetValue(instanceId, out CacheEntry cached)
                && cached.Key.Equals(key))
            {
                return cached.Profile;
            }

            FacialProfile profile = profileAsset.LoadProfile();
            if (key.IsDirty)
            {
                Cache.Remove(instanceId);
            }
            else
            {
                Cache[instanceId] = new CacheEntry(key, profile);
            }

            return profile;
        }

        /// <summary>
        /// Timeline から Profile SO を解決し、<see cref="Resolve"/> の Profile を返す。解決順は
        /// (1) Bake に記録された Profile GUID → (2) シーン上で Timeline を再生する Director にバインドされた Receiver の
        /// FacialController.CharacterSO → (3) Timeline ウィンドウが開いている Director。
        /// </summary>
        public static bool TryResolveForTimeline(
            TimelineAsset timeline,
            out FacialCharacterProfileSO profileAsset,
            out FacialProfile profile)
        {
            profile = default;
            if (!TryResolveProfileAssetForTimeline(timeline, out profileAsset))
            {
                return false;
            }

            profile = Resolve(profileAsset);
            return true;
        }

        /// <summary><see cref="TryResolveForTimeline"/> の Profile SO 解決部分だけを行う。</summary>
        public static bool TryResolveProfileAssetForTimeline(TimelineAsset timeline, out FacialCharacterProfileSO profileAsset)
        {
            profileAsset = null;
            if (timeline == null)
            {
                return false;
            }

            if (TryResolveFromBake(timeline, out profileAsset))
            {
                return true;
            }

            PlayableDirector[] directors = UnityEngine.Object.FindObjectsByType<PlayableDirector>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            for (int i = 0; i < directors.Length; i++)
            {
                if (TryResolveFromDirector(directors[i], timeline, out profileAsset))
                {
                    return true;
                }
            }

            return TryResolveFromDirector(TimelineEditor.inspectedDirector, timeline, out profileAsset);
        }

        /// <summary>指定 SO のキャッシュを破棄する。</summary>
        public static void InvalidateCache(FacialCharacterProfileSO profileAsset)
        {
            if (profileAsset == null)
            {
                return;
            }

            Cache.Remove(profileAsset.GetInstanceID());
        }

        /// <summary>全キャッシュを破棄する。</summary>
        public static void InvalidateAll()
        {
            Cache.Clear();
        }

        /// <summary>
        /// 保存直前の通知（<see cref="TimelineProfileSourceAssetHook"/> が呼ぶ）。Profile SO または profile.json の保存で
        /// キャッシュを無効化する。保存対象は変更しない。
        /// </summary>
        public static string[] OnWillSaveAssets(string[] paths)
        {
            if (paths == null || paths.Length == 0 || Cache.Count == 0)
            {
                return paths;
            }

            for (int i = 0; i < paths.Length; i++)
            {
                string path = paths[i];
                if (string.IsNullOrEmpty(path))
                {
                    continue;
                }

                if (path.Replace('\\', '/').EndsWith(ProfileJsonSuffix, StringComparison.OrdinalIgnoreCase))
                {
                    InvalidateAll();
                    return paths;
                }

                Type assetType = AssetDatabase.GetMainAssetTypeAtPath(path);
                if (assetType != null && typeof(FacialCharacterProfileSO).IsAssignableFrom(assetType))
                {
                    InvalidateCache(AssetDatabase.LoadAssetAtPath<FacialCharacterProfileSO>(path));
                }
            }

            return paths;
        }

        private static CacheKey CaptureKey(FacialCharacterProfileSO profileAsset)
        {
            bool isDirty = EditorUtility.IsDirty(profileAsset);
            string jsonPath = FacialCharacterProfileSO.GetStreamingAssetsProfilePath(profileAsset.CharacterAssetName);
            if (string.IsNullOrEmpty(jsonPath) || !File.Exists(jsonPath))
            {
                return new CacheKey(jsonExists: false, lastWriteTicks: 0L, isDirty);
            }

            return new CacheKey(jsonExists: true, File.GetLastWriteTimeUtc(jsonPath).Ticks, isDirty);
        }

        private static bool TryResolveFromBake(TimelineAsset timeline, out FacialCharacterProfileSO profileAsset)
        {
            profileAsset = null;
            FacialTimelineBakeAsset trackBake = FacialTimelineBakeLocator.Locate(timeline, null).TrackBake;
            if (TryLoadProfileAsset(trackBake, out profileAsset))
            {
                return true;
            }

            string timelinePath = AssetDatabase.GetAssetPath(timeline);
            if (string.IsNullOrEmpty(timelinePath))
            {
                return false;
            }

            UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(timelinePath);
            for (int i = 0; i < assets.Length; i++)
            {
                if (assets[i] is FacialTimelineBakeAsset subAssetBake
                    && !ReferenceEquals(subAssetBake, trackBake)
                    && TryLoadProfileAsset(subAssetBake, out profileAsset))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryLoadProfileAsset(FacialTimelineBakeAsset bake, out FacialCharacterProfileSO profileAsset)
        {
            profileAsset = null;
            if (bake == null || string.IsNullOrEmpty(bake.ProfileAssetGuid))
            {
                return false;
            }

            string profilePath = AssetDatabase.GUIDToAssetPath(bake.ProfileAssetGuid);
            if (string.IsNullOrEmpty(profilePath))
            {
                return false;
            }

            profileAsset = AssetDatabase.LoadAssetAtPath<FacialCharacterProfileSO>(profilePath);
            return profileAsset != null;
        }

        private static bool TryResolveFromDirector(
            PlayableDirector director,
            TimelineAsset timeline,
            out FacialCharacterProfileSO profileAsset)
        {
            profileAsset = null;
            if (director == null || !ReferenceEquals(director.playableAsset, timeline))
            {
                return false;
            }

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

        private readonly struct CacheKey : IEquatable<CacheKey>
        {
            public CacheKey(bool jsonExists, long lastWriteTicks, bool isDirty)
            {
                JsonExists = jsonExists;
                LastWriteTicks = lastWriteTicks;
                IsDirty = isDirty;
            }

            public bool JsonExists { get; }

            public long LastWriteTicks { get; }

            public bool IsDirty { get; }

            public bool Equals(CacheKey other)
            {
                return JsonExists == other.JsonExists
                    && LastWriteTicks == other.LastWriteTicks
                    && IsDirty == other.IsDirty;
            }

            public override bool Equals(object obj)
            {
                return obj is CacheKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(JsonExists, LastWriteTicks, IsDirty);
            }
        }

        private readonly struct CacheEntry
        {
            public CacheEntry(CacheKey key, FacialProfile profile)
            {
                Key = key;
                Profile = profile;
            }

            public CacheKey Key { get; }

            public FacialProfile Profile { get; }
        }
    }

    /// <summary>Profile SO / profile.json の保存で <see cref="TimelineProfileSource"/> のキャッシュを無効化する。</summary>
    public sealed class TimelineProfileSourceAssetHook : AssetModificationProcessor
    {
        public static string[] OnWillSaveAssets(string[] paths)
        {
            return TimelineProfileSource.OnWillSaveAssets(paths);
        }
    }
}
