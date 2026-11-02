using Hidano.FacialControl.Timeline.Playables;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Hidano.FacialControl.Timeline.Clips
{
    [System.Serializable]
    public sealed class FacialExpressionClip : PlayableAsset, ITimelineClipAsset
    {
        [SerializeField] private string expressionId = string.Empty;

        public string ExpressionId
        {
            get => expressionId;
            set => expressionId = value ?? string.Empty;
        }

        [Tooltip("Timeline の先頭（0 秒）から始まる Clip を、遷移なしで最初から有効にする。REC Export が、録画開始時に既に押されていた表情に付ける。先頭以外から始まる Clip では無視する。")]
        [SerializeField] private bool startsActive;

        /// <summary>
        /// Timeline の先頭（0 秒）から始まる Clip を、遷移なしで最初から有効にするか（REC 再生が録画開始時のスタックを
        /// 即座に反映するのに合わせる。HID-298）。先頭以外から始まる Clip では無視する。
        /// </summary>
        public bool StartsActive
        {
            get => startsActive;
            set => startsActive = value;
        }

        public ClipCaps clipCaps => ClipCaps.None;

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            ScriptPlayable<FacialExpressionClipBehaviour> playable =
                ScriptPlayable<FacialExpressionClipBehaviour>.Create(graph);
            FacialExpressionClipBehaviour behaviour = playable.GetBehaviour();
            behaviour.ExpressionId = expressionId;
            return playable;
        }
    }
}
