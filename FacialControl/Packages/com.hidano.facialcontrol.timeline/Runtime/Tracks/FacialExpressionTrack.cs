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
    [TrackClipType(typeof(FacialExpressionClip))]
    [TrackBindingType(typeof(FacialTimelineReceiver))]
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
            playable.GetBehaviour().ConfigureExpression(name, TimelineStateEventCollector.Collect(this));
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
