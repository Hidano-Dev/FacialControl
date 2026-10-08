using System;
using Hidano.FacialControl.Timeline.Playables;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Hidano.FacialControl.Timeline.Clips
{
    [Serializable]
    public sealed class FacialValueClip : PlayableAsset, ITimelineClipAsset
    {
        [SerializeField] private AnimationCurve[] axes = Array.Empty<AnimationCurve>();

        // 以下は値提供型（FacialValueChannelKind.ValueProvider）のトラックだけが使う。Analog / Gaze では空のまま。
        [SerializeField] private string[] blendShapeNames = Array.Empty<string>();
        [SerializeField] private int[] blendShapeIndices = Array.Empty<int>();
        [SerializeField] private AnimationCurve[] contributes = Array.Empty<AnimationCurve>();
        [SerializeField] private AnimationCurve validity = new AnimationCurve();

        /// <summary>
        /// 軸ごとのカーブ。Analog / Gaze は軸値、値提供型は BlendShape ごとの値（0..1）。
        /// </summary>
        public AnimationCurve[] Axes
        {
            get => axes;
            set => axes = value ?? Array.Empty<AnimationCurve>();
        }

        /// <summary>
        /// 値提供型のみ。軸ごとの BlendShape 名（解決できなかった軸は空文字。再生時は名前を優先して対応付ける）。
        /// </summary>
        public string[] BlendShapeNames
        {
            get => blendShapeNames;
            set => blendShapeNames = value ?? Array.Empty<string>();
        }

        /// <summary>
        /// 値提供型のみ。軸ごとの記録時の BlendShape index（FacialController の並び。名前が空の軸の対応付けに使う）。
        /// </summary>
        public int[] BlendShapeIndices
        {
            get => blendShapeIndices;
            set => blendShapeIndices = value ?? Array.Empty<int>();
        }

        /// <summary>
        /// 値提供型のみ。軸ごとの寄与 mask（0 / 1 の階段カーブ。無い / キー無しの軸は常に寄与）。
        /// </summary>
        public AnimationCurve[] Contributes
        {
            get => contributes;
            set => contributes = value ?? Array.Empty<AnimationCurve>();
        }

        /// <summary>
        /// 値提供型のみ。入力源の有効状態（0 / 1 の階段カーブ。キー無しなら常に有効）。
        /// </summary>
        public AnimationCurve Validity
        {
            get => validity;
            set => validity = value ?? new AnimationCurve();
        }

        public ClipCaps clipCaps => ClipCaps.None;

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            ScriptPlayable<FacialValueClipBehaviour> playable =
                ScriptPlayable<FacialValueClipBehaviour>.Create(graph);
            FacialValueClipBehaviour behaviour = playable.GetBehaviour();
            behaviour.Axes = axes ?? Array.Empty<AnimationCurve>();
            return playable;
        }
    }
}
