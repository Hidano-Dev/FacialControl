using System;
using System.Collections.Generic;
using Hidano.FacialControl.Timeline.Adapters.Scanning;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Hidano.FacialControl.Timeline.Adapters.Session
{
    /// <summary>
    /// Director の解決経路（D7）。
    /// </summary>
    public enum DirectorResolveStatus
    {
        /// <summary>上書き指定の Director を採用した。</summary>
        Override = 0,

        /// <summary>Receiver と同じ GameObject の Director。</summary>
        SameObject = 1,

        /// <summary>親階層の Director。</summary>
        Parent = 2,

        /// <summary>シーン走査で Receiver を binding している Director が 1 つだけ見つかった。</summary>
        SceneUnique = 3,

        /// <summary>シーン走査の候補が 2 つ以上（上書き指定が必要）。</summary>
        Ambiguous = 4,

        /// <summary>候補なし。</summary>
        NotFound = 5,
    }

    /// <summary>
    /// <see cref="TimelineTrackBindingResolver.EnsureBindings"/> の結果。
    /// </summary>
    public readonly struct TrackBindingReport
    {
        private static readonly TrackAsset[] EmptyTracks = Array.Empty<TrackAsset>();

        private readonly IReadOnlyList<TrackAsset> _boundToOther;

        public TrackBindingReport(int assigned, int alreadyBound, IReadOnlyList<TrackAsset> boundToOther)
        {
            Assigned = assigned;
            AlreadyBound = alreadyBound;
            _boundToOther = boundToOther;
        }

        /// <summary>本呼び出しで設定した数。</summary>
        public int Assigned { get; }

        /// <summary>既に自分（Receiver またはその GameObject）を指していた数。</summary>
        public int AlreadyBound { get; }

        /// <summary>他オブジェクトが設定済みで触らなかったトラック。</summary>
        public IReadOnlyList<TrackAsset> BoundToOther => _boundToOther ?? EmptyTracks;
    }

    /// <summary>
    /// Director の解決規則（D7）と、Facial トラック（root + 子）の generic binding 自動設定（Req 1.6）。
    /// </summary>
    /// <remarks>
    /// <para>解決順: 上書き → 同 GameObject → 親階層 → シーン走査（Facial トラックを持つ TimelineAsset をバインドし、
    /// いずれかの Facial トラックの binding が Receiver またはその GameObject を指す Director）。シーン走査で 2 つ以上なら Ambiguous。</para>
    /// <para>シーン走査を伴うため、呼び出しはセッション開始時と Inspector 評価時に限る（毎フレーム呼ばない）。</para>
    /// </remarks>
    public static class TimelineTrackBindingResolver
    {
        /// <param name="receiver">解決対象の Receiver。</param>
        /// <param name="overrideDirector">任意上書き（Receiver の上書きフィールド）。非 null なら無条件に採用する。</param>
        /// <param name="status">解決経路。</param>
        /// <returns>解決した Director。Ambiguous / NotFound では null。</returns>
        public static PlayableDirector ResolveDirector(
            FacialTimelineReceiver receiver,
            PlayableDirector overrideDirector,
            out DirectorResolveStatus status)
        {
            if (overrideDirector != null)
            {
                status = DirectorResolveStatus.Override;
                return overrideDirector;
            }

            if (receiver == null)
            {
                status = DirectorResolveStatus.NotFound;
                return null;
            }

            if (receiver.TryGetComponent(out PlayableDirector sameObject))
            {
                status = DirectorResolveStatus.SameObject;
                return sameObject;
            }

            Transform parent = receiver.transform.parent;
            if (parent != null)
            {
                PlayableDirector ancestor = parent.GetComponentInParent<PlayableDirector>(true);
                if (ancestor != null)
                {
                    status = DirectorResolveStatus.Parent;
                    return ancestor;
                }
            }

            PlayableDirector found = null;
            int candidateCount = 0;
            PlayableDirector[] directors = UnityEngine.Object.FindObjectsByType<PlayableDirector>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            for (int i = 0; i < directors.Length; i++)
            {
                if (!BindsReceiver(directors[i], receiver))
                {
                    continue;
                }

                candidateCount++;
                found = directors[i];
            }

            if (candidateCount == 1)
            {
                status = DirectorResolveStatus.SceneUnique;
                return found;
            }

            status = candidateCount > 1 ? DirectorResolveStatus.Ambiguous : DirectorResolveStatus.NotFound;
            return null;
        }

        /// <summary>
        /// binding が未設定の Facial トラック（root + 子）にだけ Receiver を設定する。既に Receiver（またはその GameObject）を
        /// 指すものは既存扱い、他オブジェクトを指すものは触らずに <see cref="TrackBindingReport.BoundToOther"/> へ記録する。
        /// </summary>
        /// <remarks><see cref="TrackBindingReport.Assigned"/> &gt; 0 かつ graph が有効なら、呼び出し側が <c>RebuildGraph()</c> を行う。</remarks>
        public static TrackBindingReport EnsureBindings(
            PlayableDirector director,
            TimelineAsset timeline,
            FacialTimelineReceiver receiver,
            ITrackBindingWriter writer)
        {
            if (director == null)
            {
                throw new ArgumentNullException(nameof(director));
            }

            if (receiver == null)
            {
                throw new ArgumentNullException(nameof(receiver));
            }

            if (writer == null)
            {
                throw new ArgumentNullException(nameof(writer));
            }

            List<TrackAsset> tracks = CollectBindableTracks(timeline);
            int assigned = 0;
            int alreadyBound = 0;
            List<TrackAsset> boundToOther = null;
            for (int i = 0; i < tracks.Count; i++)
            {
                TrackAsset track = tracks[i];
                UnityEngine.Object binding = director.GetGenericBinding(track);
                if (binding == null)
                {
                    writer.SetGenericBinding(director, track, receiver);
                    assigned++;
                }
                else if (PointsTo(binding, receiver))
                {
                    alreadyBound++;
                }
                else
                {
                    boundToOther ??= new List<TrackAsset>();
                    boundToOther.Add(track);
                }
            }

            return new TrackBindingReport(assigned, alreadyBound, boundToOther);
        }

        public static bool HasFacialTracks(TimelineAsset timeline)
        {
            return TimelineAssetScanner.Scan(timeline).HasFacialTracks;
        }

        private static bool BindsReceiver(PlayableDirector director, FacialTimelineReceiver receiver)
        {
            if (director == null || !(director.playableAsset is TimelineAsset timeline))
            {
                return false;
            }

            List<TrackAsset> tracks = CollectBindableTracks(timeline);
            for (int i = 0; i < tracks.Count; i++)
            {
                if (PointsTo(director.GetGenericBinding(tracks[i]), receiver))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Receiver を binding する全トラック（Facial トラック + レイヤー weight トラック）。</summary>
        private static List<TrackAsset> CollectBindableTracks(TimelineAsset timeline)
        {
            var tracks = new List<TrackAsset>(TimelineAssetScanner.Scan(timeline).TrackAssets);
            tracks.AddRange(TimelineAssetScanner.CollectLayerWeightTracks(timeline));
            return tracks;
        }

        private static bool PointsTo(UnityEngine.Object binding, FacialTimelineReceiver receiver)
        {
            if (binding == null)
            {
                return false;
            }

            return binding == receiver || binding == receiver.gameObject;
        }
    }
}
