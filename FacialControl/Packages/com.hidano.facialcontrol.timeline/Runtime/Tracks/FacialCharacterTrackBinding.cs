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
        public static List<FacialCharacterTrack> CollectCharacterTracks(TimelineAsset timeline)
        {
            var results = new List<FacialCharacterTrack>();
            if (timeline == null)
            {
                return results;
            }

            foreach (TrackAsset root in timeline.GetRootTracks())
            {
                CollectCharacterTracks(root, results);
            }

            return results;
        }

        /// <summary>
        /// 親トラックの外にある Facial トラック（表情 / 値 / レイヤー weight。親トラック導入前に書き出した旧構造）を集める。
        /// 旧構造のトラックは再生されない（REC から出し直す）。
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

            List<FacialCharacterTrack> characters = CollectCharacterTracks(timeline);
            for (int i = 0; i < characters.Count; i++)
            {
                if (ToController(director.GetGenericBinding(characters[i])) == controller)
                {
                    return true;
                }
            }

            return false;
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

        private static void CollectCharacterTracks(TrackAsset track, List<FacialCharacterTrack> results)
        {
            if (track is FacialCharacterTrack character)
            {
                results.Add(character);
            }

            foreach (TrackAsset child in track.GetChildTracks())
            {
                CollectCharacterTracks(child, results);
            }
        }

        private static void CollectOutside(TrackAsset track, List<TrackAsset> results)
        {
            if (track is FacialCharacterTrack)
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
