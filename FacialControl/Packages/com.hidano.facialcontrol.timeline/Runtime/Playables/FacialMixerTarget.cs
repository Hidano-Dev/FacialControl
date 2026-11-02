using System.Collections.Generic;
using Hidano.FacialControl.Adapters.Playable;
using Hidano.FacialControl.Timeline.Adapters;
using Hidano.FacialControl.Timeline.Tracks;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Hidano.FacialControl.Timeline.Playables
{
    /// <summary>
    /// 子トラックの Mixer が再生対象（Receiver）を引く口（HID-408）。対象は祖先の <see cref="FacialCharacterTrack"/> に Bind された
    /// FacialController で、Receiver はその GameObject に内部実装として自動で置く（<see cref="FacialTimelineReceiver.GetOrCreate"/>）。
    /// </summary>
    /// <remarks>
    /// <para>親トラックが無い（旧構造）・親が Bind されていないトラックは再生しない。その旨はトラックごとに 1 回だけ Console に出す。</para>
    /// <para>対象が変わらない間は Receiver をキャッシュする（毎フレームの解決は確保しない）。</para>
    /// </remarks>
    internal sealed class FacialMixerTarget
    {
        private const string LogPrefix = "[FacialTimeline] ";

        // 出力側を辿る深さの上限（Mixer → 親トラック → Timeline の 2 段 + 通知 Playable 等の余裕）。
        private const int MaxOutputDepth = 8;

        // 旧構造 / 未 Bind を知らせたトラック（instanceID）。ドメインリロードまで 1 トラック 1 回。
        private static readonly HashSet<int> WarnedLegacyTracks = new HashSet<int>();
        private static readonly HashSet<int> WarnedUnboundTracks = new HashSet<int>();

        /// <summary>
        /// 警告済みの記録を消す。PlayMode 突入時（Domain Reload を無効にした設定でも）に呼ばれ、Play ごとに 1 回ずつ知らせ直す。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void ResetWarnings()
        {
            WarnedLegacyTracks.Clear();
            WarnedUnboundTracks.Clear();
        }

        /// <summary>テスト用: 旧構造 / 未 Bind の警告を記録したトラック数。</summary>
        internal static int WarnedTrackCount => WarnedLegacyTracks.Count + WarnedUnboundTracks.Count;

        private TrackAsset _track;
        private FacialController _controller;
        private FacialTimelineReceiver _receiver;

        /// <summary>
        /// Mixer が評価に使うトラックの時刻（= Timeline の時刻）。Director のグラフでは root の Timeline Playable の時刻、
        /// それ以外（単体のグラフ）は Mixer 自身の時刻。
        /// </summary>
        /// <remarks>
        /// 子トラックの Mixer は親トラック（<see cref="FacialCharacterTrack"/>）の Playable の入力に繋がるため、自身の時刻が
        /// Timeline の時刻どおりに進まない。Timeline 直下に Mixer があった旧構造では両者は一致していた。確保しない。
        /// </remarks>
        public static double TrackTime(Playable playable)
        {
            // まず自分の出力側を辿り、自分が属する TimelinePlayable を探す（1 つのグラフに Timeline が複数あっても取り違えない）。
            Playable current = playable;
            for (int depth = 0; depth < MaxOutputDepth && current.IsValid(); depth++)
            {
                if (current.GetPlayableType() == typeof(TimelinePlayable))
                {
                    return current.GetTime();
                }

                if (current.GetOutputCount() == 0)
                {
                    break;
                }

                current = current.GetOutput(0);
            }

            // 出力側で見つからない（Timeline の外のグラフ）ときは root を探す。
            PlayableGraph graph = playable.GetGraph();
            if (graph.IsValid())
            {
                int rootCount = graph.GetRootPlayableCount();
                for (int i = 0; i < rootCount; i++)
                {
                    Playable root = graph.GetRootPlayable(i);
                    if (root.IsValid() && root.GetPlayableType() == typeof(TimelinePlayable))
                    {
                        return root.GetTime();
                    }
                }
            }

            return playable.GetTime();
        }

        public void SetTrack(TrackAsset track)
        {
            _track = track;
            _controller = null;
            _receiver = null;
        }

        /// <summary>再生対象の Receiver。解決できなければ null（理由はトラックごとに 1 回ログに出す）。</summary>
        public FacialTimelineReceiver Resolve(PlayableDirector director)
        {
            // Timeline はミュートした Facial Character トラックの子も（Group の子ではないため）コンパイルして再生する。
            // 親をミュートしたら子も鳴らさない（チャネル導出・構造診断と同じ規則）。確保しない。
            if (FacialCharacterTrackBinding.IsMutedInHierarchy(_track))
            {
                _controller = null;
                _receiver = null;
                return null;
            }

            FacialController controller = FacialCharacterTrackBinding.ResolveController(director, _track);
            if (controller == null)
            {
                _controller = null;
                _receiver = null;
                WarnUnresolvedOnce(director);
                return null;
            }

            if (!ReferenceEquals(controller, _controller) || _receiver == null)
            {
                _controller = controller;
                _receiver = FacialTimelineReceiver.GetOrCreate(controller);
            }

            return _receiver;
        }

        private void WarnUnresolvedOnce(PlayableDirector director)
        {
            if (_track == null || director == null)
            {
                return;
            }

            FacialCharacterTrack character = FacialCharacterTrackBinding.FindCharacterTrack(_track);
            if (character == null)
            {
                if (WarnedLegacyTracks.Add(_track.GetInstanceID()))
                {
                    Debug.LogWarning(
                        LogPrefix + $"トラック '{_track.name}' は Facial Character トラックの外にある旧構造のため再生しません。"
                        + "REC から TimelineAsset を書き出し直してください。",
                        director);
                }

                return;
            }

            if (WarnedUnboundTracks.Add(character.GetInstanceID()))
            {
                Debug.LogWarning(
                    LogPrefix + $"Facial Character トラック '{character.name}' に FacialController が Bind されていないため再生しません。"
                    + "PlayableDirector のトラックの binding に FacialController をセットしてください。",
                    director);
            }
        }
    }
}
