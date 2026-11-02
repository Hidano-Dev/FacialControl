using Hidano.FacialControl.Timeline.Adapters;
using Hidano.FacialControl.Timeline.Adapters.Assets;
using Hidano.FacialControl.Timeline.Clips;
using Hidano.FacialControl.Timeline.Domain.Services;
using Hidano.FacialControl.Timeline.EditorPreview;
using Hidano.FacialControl.Timeline.Playables;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Hidano.FacialControl.Timeline.Tracks
{
    /// <summary>
    /// 表情トラック（トラック名 = Profile のレイヤー名）。<see cref="FacialCharacterTrack"/> の子に置き、対象は親の binding を使う
    /// （本トラックは Bind を持たない。HID-408）。
    /// </summary>
    [TrackClipType(typeof(FacialExpressionClip))]
    [TrackColor(0.78f, 0.36f, 0.28f)]
    public sealed class FacialExpressionTrack : TrackAsset, ILayerable, IPropertyPreview, IFacialTimelineBakeHolder
    {
        // 内部キャッシュ（Bake サブアセット）への参照。Exporter / 再ベイクが全 Facial トラックへ同じ参照を書く（D5）。
        [SerializeField, HideInInspector] private FacialTimelineBakeAsset bake;

        public FacialTimelineBakeAsset Bake
        {
            get => bake;
            set => bake = value;
        }

        public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount)
        {
            ScriptPlayable<FacialTrackMixerBehaviour> playable =
                ScriptPlayable<FacialTrackMixerBehaviour>.Create(graph, inputCount);
            FacialTrackMixerBehaviour behaviour = playable.GetBehaviour();
            behaviour.SetOwnerTrack(this);
            behaviour.ConfigureExpression(name, TimelineStateEventCollector.Collect(this));
            return playable;
        }

        Playable ILayerable.CreateLayerMixer(PlayableGraph graph, GameObject go, int inputCount)
        {
            return Playable.Null;
        }

        public override void GatherProperties(PlayableDirector director, IPropertyCollector driver)
        {
            FacialTimelineEditorPreviewBridge.GatherProperties?.Invoke(director, this, driver);
        }
    }
}
