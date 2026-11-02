using System.Collections.Generic;
using Hidano.FacialControl.Adapters.Playable;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Hidano.FacialControl.Timeline.Tracks
{
    /// <summary>
    /// Facial トラックの対象（FacialController）を、祖先の <see cref="FacialCharacterTrack"/> の binding から引く（HID-408）。
    /// </summary>
    /// <remarks>
    /// 子トラックは Bind を持たず、親トラックの binding だけが対象を決める。自動 Bind はしない。
    /// <see cref="ResolveController"/> は確保しない（Mixer が毎フレーム呼ぶ）。走査系（Collect*）は確保するので毎フレーム呼ばない。
    /// </remarks>
    public static class FacialCharacterTrackBinding
    {
        /// <summary>トラック自身か、その祖先にある最も近い親トラック。無ければ null（旧構造のトラック）。</summary>
        public static FacialCharacterTrack FindCharacterTrack(TrackAsset track)
        {
            for (TrackAsset current = track; current != null; current = current.parent as TrackAsset)
            {
                if (current is FacialCharacterTrack character)
                {
                    return character;
                }
            }

            return null;
        }

        /// <summary>
        /// トラックの対象を返す。親トラックが無い・Bind されていない・Bind 先に FacialController が無いときは null。
        /// </summary>
        public static FacialController ResolveController(PlayableDirector director, TrackAsset track)
        {
            if (director == null)
            {
                return null;
            }

            FacialCharacterTrack character = FindCharacterTrack(track);
            return character != null ? ToController(director.GetGenericBinding(character)) : null;
        }

        /// <summary>binding の値（FacialController / その GameObject / 同じ GameObject の Component）を FacialController に写す。</summary>
        public static FacialController ToController(Object binding)
        {
            switch (binding)
            {
                case FacialController controller:
                    return controller;
                case GameObject gameObject:
                    return gameObject.TryGetComponent(out FacialController fromObject) ? fromObject : null;
                case Component component:
                    return component.TryGetComponent(out FacialController fromComponent) ? fromComponent : null;
                default:
                    return null;
            }
        }

        /// <summary>Timeline の全階層から親トラックを集める（出現順）。</summary>
        /// <param name="includeMuted">false ならミュート中（親階層のミュートを含む）の親トラックを除く（再生されないため）。</param>
        public static List<FacialCharacterTrack> CollectCharacterTracks(TimelineAsset timeline, bool includeMuted = true)
        {
            var results = new List<FacialCharacterTrack>();
            if (timeline == null)
            {
                return results;
            }

            foreach (TrackAsset root in timeline.GetRootTracks())
            {
                CollectCharacterTracks(root, includeMuted, results);
            }

            return results;
        }

        /// <summary>
        /// 親トラックの外にある Facial トラック（表情 / 値 / レイヤー weight。親トラック導入前に書き出した旧構造）を集める。
        /// 旧構造のトラックは再生されない（REC から出し直す）。ミュート中（親階層のミュートを含む）のトラックは再生されないので数えない。
        /// </summary>
        public static List<TrackAsset> CollectTracksOutsideCharacter(TimelineAsset timeline)
        {
            var results = new List<TrackAsset>();
            if (timeline == null)
            {
                return results;
            }

            foreach (TrackAsset root in timeline.GetRootTracks())
            {
                CollectOutside(root, results);
            }

            return results;
        }

        /// <summary>Timeline 内で <paramref name="controller"/>（またはその GameObject）を Bind している親トラックがあるか。</summary>
        public static bool BindsController(PlayableDirector director, FacialController controller)
        {
            if (director == null || controller == null || !(director.playableAsset is TimelineAsset timeline))
            {
                return false;
            }

            // ミュート中（親階層のミュートを含む）の親トラックは再生されないので、この controller の Director として数えない。
            List<FacialCharacterTrack> characters = CollectCharacterTracks(timeline, includeMuted: false);
            for (int i = 0; i < characters.Count; i++)
            {
                if (ToController(director.GetGenericBinding(characters[i])) == controller)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// トラック自身か祖先のどれかがミュートされているか。Timeline の <c>TrackAsset.mutedInHierarchy</c> は Group の親しか見ないため、
        /// ミュートした Facial Character トラックの子も「ミュート中」と判定できるよう祖先をすべて辿る。確保しない。
        /// </summary>
        public static bool IsMutedInHierarchy(TrackAsset track)
        {
            for (TrackAsset current = track; current != null; current = current.parent as TrackAsset)
            {
                if (current.muted)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 全トラック（Group・子・Lane を含む）のミュート状態の指紋。走査はミュート中のトラックを除くので、導出結果・プレビューの
        /// キャッシュはこの値もキーにする（ミュートの切り替えで作り直す）。確保しない。
        /// </summary>
        public static int ComputeMuteFingerprint(TimelineAsset timeline)
        {
            if (timeline == null)
            {
                return 0;
            }

            unchecked
            {
                return AppendMuteFingerprint(17, timeline.GetRootTracks());
            }
        }

        private static int AppendMuteFingerprint(int hash, IEnumerable<TrackAsset> tracks)
        {
            unchecked
            {
                // Timeline はトラック一覧を List で返すので、添字で辿って列挙子の確保を避ける（再生の再開時・プレビューの毎フレームに通るため）。
                if (tracks is IList<TrackAsset> list)
                {
                    for (int i = 0; i < list.Count; i++)
                    {
                        hash = AppendMuteFingerprint(hash, list[i]);
                    }

                    return hash;
                }

                if (tracks != null)
                {
                    foreach (TrackAsset track in tracks)
                    {
                        hash = AppendMuteFingerprint(hash, track);
                    }
                }

                return hash;
            }
        }

        private static int AppendMuteFingerprint(int hash, TrackAsset track)
        {
            if (track == null)
            {
                return hash;
            }

            unchecked
            {
                hash = (hash * 31) + track.GetInstanceID();
                hash = (hash * 31) + (track.muted ? 1 : 0);
                return AppendMuteFingerprint(hash, track.GetChildTracks());
            }
        }

        /// <summary>表情 / 値 / レイヤー weight トラック（親トラックの子になるトラック）か。</summary>
        public static bool IsFacialChildTrack(TrackAsset track)
        {
            return track != null && IsFacialChildTrack(track.GetType());
        }

        /// <summary>表情 / 値 / レイヤー weight トラックの型か。</summary>
        public static bool IsFacialChildTrack(System.Type trackType)
        {
            return trackType == typeof(FacialExpressionTrack)
                || trackType == typeof(FacialValueTrack)
                || trackType == typeof(FacialLayerWeightTrack);
        }

        private static void CollectCharacterTracks(TrackAsset track, bool includeMuted, List<FacialCharacterTrack> results)
        {
            if (!includeMuted && IsMutedInHierarchy(track))
            {
                return;
            }

            if (track is FacialCharacterTrack character)
            {
                results.Add(character);
            }

            foreach (TrackAsset child in track.GetChildTracks())
            {
                CollectCharacterTracks(child, includeMuted, results);
            }
        }

        private static void CollectOutside(TrackAsset track, List<TrackAsset> results)
        {
            if (track is FacialCharacterTrack || IsMutedInHierarchy(track))
            {
                return;
            }

            if (IsFacialChildTrack(track))
            {
                // 旧構造の表情トラックの Lane（子）は親と一緒に扱うので、最上位の Facial トラックだけ記録する。
                results.Add(track);
                return;
            }

            foreach (TrackAsset child in track.GetChildTracks())
            {
                CollectOutside(child, results);
            }
        }
    }
}
