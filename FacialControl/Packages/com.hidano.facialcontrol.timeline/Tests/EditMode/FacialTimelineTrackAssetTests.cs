using Hidano.FacialControl.Timeline.Clips;
using Hidano.FacialControl.Timeline.Playables;
using Hidano.FacialControl.Timeline.Tracks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Timeline.Tests.EditMode
{
    [SmallTest]
    public sealed class FacialTimelineTrackAssetTests : SizedTestFixture
    {
        [Test]
        public void ExpressionTrack_CreatesExpressionClip_WithClipCapsNone()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();

            try
            {
                var track = timeline.CreateTrack<FacialExpressionTrack>(null, "Expressions");
                TimelineClip clip = track.CreateClip<FacialExpressionClip>();
                var asset = clip.asset as FacialExpressionClip;

                Assert.That(asset, Is.Not.Null);
                Assert.That(asset.clipCaps, Is.EqualTo(ClipCaps.None));
                Assert.That(asset.ExpressionId, Is.EqualTo(string.Empty));

                var graph = PlayableGraph.Create("ExpressionClipPlayable");
                try
                {
                    ScriptPlayable<FacialExpressionClipBehaviour> playable =
                        (ScriptPlayable<FacialExpressionClipBehaviour>)asset.CreatePlayable(graph, null);
                    Assert.That(playable.GetBehaviour().ExpressionId, Is.EqualTo(string.Empty));
                }
                finally
                {
                    graph.Destroy();
                }
            }
            finally
            {
                Object.DestroyImmediate(timeline);
            }
        }

        [Test]
        public void ExpressionTrack_SupportsLayeredChildTracks()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();

            try
            {
                var parentTrack = timeline.CreateTrack<FacialExpressionTrack>(null, "Expressions");
                var childTrack = timeline.CreateTrack<FacialExpressionTrack>(parentTrack, "Expressions Layer");
                TimelineClip childClip = childTrack.CreateClip<FacialExpressionClip>();

                Assert.That(parentTrack, Is.Not.Null);
                Assert.That(childTrack.parent, Is.SameAs(parentTrack));
                Assert.That(childClip.asset, Is.TypeOf<FacialExpressionClip>());
            }
            finally
            {
                Object.DestroyImmediate(timeline);
            }
        }

        [Test]
        public void ValueTrack_CreatesValueClip_WithTrackMetadataAndPlayablePayload()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();

            try
            {
                var track = timeline.CreateTrack<FacialValueTrack>(null, "Gaze");
                track.ChannelSubId = "gaze-main";
                track.ChannelKind = FacialValueChannelKind.Gaze;

                TimelineClip clip = track.CreateClip<FacialValueClip>();
                var asset = clip.asset as FacialValueClip;
                asset.Axes = new[]
                {
                    AnimationCurve.Linear(0f, -1f, 1f, 1f),
                    AnimationCurve.Linear(0f, 1f, 1f, -1f),
                };

                Assert.That(asset, Is.Not.Null);
                Assert.That(asset.clipCaps, Is.EqualTo(ClipCaps.None));
                Assert.That(track.ChannelSubId, Is.EqualTo("gaze-main"));
                Assert.That(track.ChannelKind, Is.EqualTo(FacialValueChannelKind.Gaze));
                Assert.That(asset.Axes, Has.Length.EqualTo(2));

                var graph = PlayableGraph.Create("ValueClipPlayable");
                try
                {
                    ScriptPlayable<FacialValueClipBehaviour> playable =
                        (ScriptPlayable<FacialValueClipBehaviour>)asset.CreatePlayable(graph, null);
                    Assert.That(playable.GetBehaviour().Axes, Has.Length.EqualTo(2));
                }
                finally
                {
                    graph.Destroy();
                }
            }
            finally
            {
                Object.DestroyImmediate(timeline);
            }
        }
    }
}
