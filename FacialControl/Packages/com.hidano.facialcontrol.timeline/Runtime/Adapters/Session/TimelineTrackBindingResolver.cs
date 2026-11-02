using Hidano.FacialControl.Adapters.Playable;
using Hidano.FacialControl.Timeline.Adapters.Scanning;
using Hidano.FacialControl.Timeline.Tracks;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Hidano.FacialControl.Timeline.Adapters.Session
{
    /// <summary>
    /// Director の解決経路。
    /// </summary>
    public enum DirectorResolveStatus
    {
        /// <summary>上書き指定の Director を採用した。</summary>
        Override = 0,

        /// <summary>再生中の Director（Mixer から渡された）。</summary>
        SameObject = 1,

        /// <summary>（廃止。HID-408 で親階層の推測をやめた）</summary>
        Parent = 2,

        /// <summary>シーン走査で、この controller を Bind した Facial Character トラックを持つ Director が 1 つだけ見つかった。</summary>
        SceneUnique = 3,

        /// <summary>シーン走査の候補が 2 つ以上（上書き指定が必要）。</summary>
        Ambiguous = 4,

        /// <summary>候補なし。</summary>
        NotFound = 5,
    }

    /// <summary>
    /// 静的診断に使う Director の解決（HID-408）。Facial Character トラックに Receiver の FacialController（またはその GameObject）を
    /// Bind している Director を探す。トラックの binding は書かない（自動 Bind はしない）。
    /// </summary>
    /// <remarks>シーン走査を伴うため、呼び出しは診断評価時に限る（毎フレーム呼ばない）。</remarks>
    public static class TimelineTrackBindingResolver
    {
        /// <param name="receiver">解決対象の Receiver。</param>
        /// <param name="overrideDirector">任意上書き。非 null なら無条件に採用する。</param>
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

            FacialController controller = receiver != null ? receiver.Controller : null;
            if (controller == null)
            {
                status = DirectorResolveStatus.NotFound;
                return null;
            }

            PlayableDirector found = null;
            int candidateCount = 0;
            PlayableDirector[] directors = Object.FindObjectsByType<PlayableDirector>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            for (int i = 0; i < directors.Length; i++)
            {
                if (!FacialCharacterTrackBinding.BindsController(directors[i], controller))
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

        public static bool HasFacialTracks(TimelineAsset timeline)
        {
            return TimelineAssetScanner.Scan(timeline).HasFacialTracks;
        }
    }
}
