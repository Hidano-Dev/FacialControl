using System.Reflection;
using Hidano.FacialControl.Timeline.Adapters;
using Hidano.FacialControl.Timeline.Adapters.Assets;
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

        [Test]
        public void ExpressionTrack_BakeReference_DefaultsToNull()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();

            try
            {
                var track = timeline.CreateTrack<FacialExpressionTrack>(null, "Expressions");

                Assert.That(track, Is.InstanceOf<IFacialTimelineBakeHolder>());
                Assert.That(((IFacialTimelineBakeHolder)track).Bake, Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(timeline);
            }
        }

        [Test]
        public void ValueTrack_BakeReference_DefaultsToNull()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();

            try
            {
                var track = timeline.CreateTrack<FacialValueTrack>(null, "osc:lt");

                Assert.That(track, Is.InstanceOf<IFacialTimelineBakeHolder>());
                Assert.That(((IFacialTimelineBakeHolder)track).Bake, Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(timeline);
            }
        }

        [Test]
        public void BakeHolder_SerializationRoundTrip_PreservesSameReferenceOnBothTrackKinds()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            var bake = ScriptableObject.CreateInstance<FacialTimelineBakeAsset>();
            FacialExpressionTrack restoredExpression = null;
            FacialValueTrack restoredValue = null;

            try
            {
                var expressionTrack = timeline.CreateTrack<FacialExpressionTrack>(null, "Expressions");
                var valueTrack = timeline.CreateTrack<FacialValueTrack>(null, "osc:lt");
                ((IFacialTimelineBakeHolder)expressionTrack).Bake = bake;
                ((IFacialTimelineBakeHolder)valueTrack).Bake = bake;

                // Instantiate は Unity のシリアライズ経由で複製する（シリアライズされないフィールドは引き継がれない）。
                restoredExpression = Object.Instantiate(expressionTrack);
                restoredValue = Object.Instantiate(valueTrack);

                Assert.That(((IFacialTimelineBakeHolder)restoredExpression).Bake, Is.SameAs(bake));
                Assert.That(((IFacialTimelineBakeHolder)restoredValue).Bake, Is.SameAs(bake));
            }
            finally
            {
                if (restoredExpression != null)
                {
                    Object.DestroyImmediate(restoredExpression);
                }

                if (restoredValue != null)
                {
                    Object.DestroyImmediate(restoredValue);
                }

                Object.DestroyImmediate(bake);
                Object.DestroyImmediate(timeline);
            }
        }

        [Test]
        public void BakeHolder_SetNull_ClearsReference()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            var bake = ScriptableObject.CreateInstance<FacialTimelineBakeAsset>();

            try
            {
                var track = timeline.CreateTrack<FacialValueTrack>(null, "osc:lt");
                var holder = (IFacialTimelineBakeHolder)track;
                holder.Bake = bake;
                holder.Bake = null;

                Assert.That(holder.Bake, Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(bake);
                Object.DestroyImmediate(timeline);
            }
        }

        [TestCase(typeof(FacialExpressionTrack))]
        [TestCase(typeof(FacialValueTrack))]
        public void BakeHolder_BackingField_IsSerializedButHiddenInInspector(System.Type trackType)
        {
            FieldInfo field = trackType.GetField("bake", BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null);
            Assert.That(field.FieldType, Is.EqualTo(typeof(FacialTimelineBakeAsset)));
            Assert.That(field.GetCustomAttribute<SerializeField>(), Is.Not.Null);
            Assert.That(field.GetCustomAttribute<HideInInspector>(), Is.Not.Null);
        }

        [Test]
        public void ValueTrack_DeclaresReceiverAsTrackBindingType()
        {
            var attribute = typeof(FacialValueTrack).GetCustomAttribute<TrackBindingTypeAttribute>();

            Assert.That(attribute, Is.Not.Null);
            Assert.That(attribute.type, Is.EqualTo(typeof(FacialTimelineReceiver)));
        }
    }
}
