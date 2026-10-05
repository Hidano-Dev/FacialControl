using System.Collections.Generic;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Testing;
using Hidano.FacialControl.Timeline.Adapters.Assets;
using Hidano.FacialControl.Timeline.Adapters.Scanning;
using Hidano.FacialControl.Timeline.Clips;
using Hidano.FacialControl.Timeline.Domain.Models;
using Hidano.FacialControl.Timeline.Domain.Services;
using Hidano.FacialControl.Timeline.Tracks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Timeline;

namespace Hidano.FacialControl.Timeline.Tests.EditMode
{
    [SmallTest]
    public sealed class TimelineAssetScannerTests : SizedTestFixture
    {
        private readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            for (int i = _created.Count - 1; i >= 0; i--)
            {
                if (_created[i] != null)
                {
                    Object.DestroyImmediate(_created[i]);
                }
            }

            _created.Clear();
        }

        [Test]
        public void Scan_Null_ReturnsEmptyResult()
        {
            TimelineScanResult result = TimelineAssetScanner.Scan(null);

            Assert.That(result.Tracks, Is.Empty);
            Assert.That(result.TrackAssets, Is.Empty);
            Assert.That(result.HasFacialTracks, Is.False);
        }

        [Test]
        public void Empty_HasNoTracks()
        {
            Assert.That(TimelineAssetScanner.Empty.Tracks, Is.Empty);
            Assert.That(TimelineAssetScanner.Empty.TrackAssets, Is.Empty);
            Assert.That(TimelineAssetScanner.Empty.HasFacialTracks, Is.False);
        }

        [Test]
        public void Scan_RootsAndChildren_ListsEachRootFollowedByItsChildrenWithParentIndex()
        {
            TimelineAsset timeline = CreateTimeline();
            var emotion = timeline.CreateTrack<FacialExpressionTrack>(null, "emotion");
            var emotionLane = timeline.CreateTrack<FacialExpressionTrack>(emotion, "emotion Lane 1");
            var eye = timeline.CreateTrack<FacialExpressionTrack>(null, "eye");
            var eyeLane1 = timeline.CreateTrack<FacialExpressionTrack>(eye, "eye Lane 1");
            var eyeLane2 = timeline.CreateTrack<FacialExpressionTrack>(eye, "eye Lane 2");
            var value = timeline.CreateTrack<FacialValueTrack>(null, "osc:lt");

            TimelineScanResult result = TimelineAssetScanner.Scan(timeline);

            Assert.That(result.Tracks.Count, Is.EqualTo(6));
            Assert.That(result.TrackAssets.Count, Is.EqualTo(6));
            AssertTrack(result, 0, "emotion", TimelineTrackKind.Expression, isChild: false, parentIndex: -1);
            AssertTrack(result, 1, "emotion Lane 1", TimelineTrackKind.Expression, isChild: true, parentIndex: 0);
            AssertTrack(result, 2, "eye", TimelineTrackKind.Expression, isChild: false, parentIndex: -1);
            AssertTrack(result, 3, "eye Lane 1", TimelineTrackKind.Expression, isChild: true, parentIndex: 2);
            AssertTrack(result, 4, "eye Lane 2", TimelineTrackKind.Expression, isChild: true, parentIndex: 2);
            AssertTrack(result, 5, "osc:lt", TimelineTrackKind.Value, isChild: false, parentIndex: -1);

            Assert.That(result.TrackAssets[0], Is.SameAs(emotion));
            Assert.That(result.TrackAssets[1], Is.SameAs(emotionLane));
            Assert.That(result.TrackAssets[2], Is.SameAs(eye));
            Assert.That(result.TrackAssets[3], Is.SameAs(eyeLane1));
            Assert.That(result.TrackAssets[4], Is.SameAs(eyeLane2));
            Assert.That(result.TrackAssets[5], Is.SameAs(value));
            Assert.That(result.HasFacialTracks, Is.True);
        }

        [Test]
        public void Scan_NonFacialTracks_AreExcluded()
        {
            TimelineAsset timeline = CreateTimeline();
            timeline.CreateTrack<AnimationTrack>(null, "anim");
            var emotion = timeline.CreateTrack<FacialExpressionTrack>(null, "emotion");
            timeline.CreateTrack<ActivationTrack>(null, "activation");
            var value = timeline.CreateTrack<FacialValueTrack>(null, "osc:gaze");

            TimelineScanResult result = TimelineAssetScanner.Scan(timeline);

            Assert.That(result.Tracks.Count, Is.EqualTo(2));
            Assert.That(result.TrackAssets[0], Is.SameAs(emotion));
            Assert.That(result.TrackAssets[1], Is.SameAs(value));
            Assert.That(result.Tracks[0].TrackIndex, Is.EqualTo(0));
            Assert.That(result.Tracks[1].TrackIndex, Is.EqualTo(1));
        }

        [Test]
        public void Scan_TimelineWithoutFacialTracks_ReturnsEmpty()
        {
            TimelineAsset timeline = CreateTimeline();
            timeline.CreateTrack<AnimationTrack>(null, "anim");

            TimelineScanResult result = TimelineAssetScanner.Scan(timeline);

            Assert.That(result.Tracks, Is.Empty);
            Assert.That(result.HasFacialTracks, Is.False);
        }

        [Test]
        public void Scan_ValueTrack_CopiesChannelMetadataAndMaxAxisCountAcrossClips()
        {
            TimelineAsset timeline = CreateTimeline();
            var gaze = timeline.CreateTrack<FacialValueTrack>(null, "osc:gaze");
            gaze.ChannelSubId = "osc:gaze";
            gaze.ChannelKind = FacialValueChannelKind.Gaze;
            AddValueClip(gaze, 0d, 1);
            AddValueClip(gaze, 1d, 3);
            AddValueClip(gaze, 2d, 2);
            var empty = timeline.CreateTrack<FacialValueTrack>(null, "osc:none");
            empty.ChannelSubId = "osc:none";

            TimelineScanResult result = TimelineAssetScanner.Scan(timeline);

            TimelineTrackDescriptor gazeDescriptor = result.Tracks[0];
            Assert.That(gazeDescriptor.Kind, Is.EqualTo(TimelineTrackKind.Value));
            Assert.That(gazeDescriptor.ChannelSubId, Is.EqualTo("osc:gaze"));
            Assert.That(gazeDescriptor.ChannelKind, Is.EqualTo(FacialValueChannelKind.Gaze));
            Assert.That(gazeDescriptor.MaxAxisCount, Is.EqualTo(3));
            Assert.That(result.Tracks[1].MaxAxisCount, Is.EqualTo(0));
        }

        [Test]
        public void Scan_ExpressionTrack_HasEmptyChannelMetadata()
        {
            TimelineAsset timeline = CreateTimeline();
            timeline.CreateTrack<FacialExpressionTrack>(null, "emotion");

            TimelineTrackDescriptor descriptor = TimelineAssetScanner.Scan(timeline).Tracks[0];

            Assert.That(descriptor.ChannelSubId, Is.EqualTo(string.Empty));
            Assert.That(descriptor.MaxAxisCount, Is.EqualTo(0));
        }

        [Test]
        public void Scan_BakeHolder_ReportsReferencePresenceAndIdentityKey()
        {
            TimelineAsset timeline = CreateTimeline();
            var bakeA = Track(ScriptableObject.CreateInstance<FacialTimelineBakeAsset>());
            var bakeB = Track(ScriptableObject.CreateInstance<FacialTimelineBakeAsset>());
            var emotion = timeline.CreateTrack<FacialExpressionTrack>(null, "emotion");
            var lane = timeline.CreateTrack<FacialExpressionTrack>(emotion, "emotion Lane 1");
            var value = timeline.CreateTrack<FacialValueTrack>(null, "osc:lt");
            var other = timeline.CreateTrack<FacialValueTrack>(null, "osc:rt");
            emotion.Bake = bakeA;
            lane.Bake = bakeA;
            other.Bake = bakeB;

            TimelineScanResult result = TimelineAssetScanner.Scan(timeline);

            Assert.That(result.Tracks[0].HasBakeReference, Is.True);
            Assert.That(result.Tracks[0].BakeInstanceId, Is.EqualTo(bakeA.GetInstanceID()));
            Assert.That(result.Tracks[1].HasBakeReference, Is.True);
            Assert.That(result.Tracks[1].BakeInstanceId, Is.EqualTo(result.Tracks[0].BakeInstanceId));
            Assert.That(result.Tracks[2].HasBakeReference, Is.False);
            Assert.That(result.Tracks[2].BakeInstanceId, Is.EqualTo(0));
            Assert.That(result.Tracks[3].HasBakeReference, Is.True);
            Assert.That(result.Tracks[3].BakeInstanceId, Is.Not.EqualTo(result.Tracks[0].BakeInstanceId));
            Assert.That(value.Bake, Is.Null);
        }

        [Test]
        public void Scan_DoesNotModifyTimeline()
        {
            TimelineAsset timeline = CreateTimeline();
            var emotion = timeline.CreateTrack<FacialExpressionTrack>(null, "emotion");
            timeline.CreateTrack<FacialValueTrack>(null, "osc:lt");

            TimelineAssetScanner.Scan(timeline);
            TimelineScanResult second = TimelineAssetScanner.Scan(timeline);

            Assert.That(second.Tracks.Count, Is.EqualTo(2));
            Assert.That(emotion.Bake, Is.Null);
            Assert.That(emotion.name, Is.EqualTo("emotion"));
        }

        [Test]
        public void Scan_ThenDerive_YieldsMatchedLayersAndValidChannels()
        {
            TimelineAsset timeline = CreateTimeline();
            var emotion = timeline.CreateTrack<FacialExpressionTrack>(null, "emotion");
            timeline.CreateTrack<FacialExpressionTrack>(emotion, "emotion Lane 1");
            timeline.CreateTrack<FacialExpressionTrack>(null, "unknown");
            var lt = timeline.CreateTrack<FacialValueTrack>(null, "osc:lt");
            lt.ChannelSubId = "osc:lt";
            AddValueClip(lt, 0d, 1);
            var gaze = timeline.CreateTrack<FacialValueTrack>(null, "osc:gaze");
            gaze.ChannelSubId = "osc:gaze";
            gaze.ChannelKind = FacialValueChannelKind.Gaze;
            AddValueClip(gaze, 0d, 2);

            TimelineScanResult result = TimelineAssetScanner.Scan(timeline);
            TimelineDerivation derivation = TimelineChannelDeriver.Derive(result.Tracks, CreateProfile("emotion", "eye"));

            Assert.That(derivation.Layers.Count, Is.EqualTo(1));
            Assert.That(derivation.Layers[0].LayerName, Is.EqualTo("emotion"));
            Assert.That(derivation.Layers[0].LayerIndex, Is.EqualTo(0));
            Assert.That(result.TrackAssets[derivation.Layers[0].TrackIndex], Is.SameAs(emotion));
            CollectionAssert.AreEqual(new[] { "unknown" }, derivation.UnmatchedTrackNames);
            Assert.That(derivation.Channels.Count, Is.EqualTo(2));
            Assert.That(derivation.Channels[0].ChannelSubId, Is.EqualTo("osc:lt"));
            Assert.That(derivation.Channels[0].AxisCount, Is.EqualTo(1));
            Assert.That(result.TrackAssets[derivation.Channels[0].TrackIndex], Is.SameAs(lt));
            Assert.That(derivation.Channels[1].ChannelSubId, Is.EqualTo("osc:gaze"));
            Assert.That(derivation.Channels[1].Kind, Is.EqualTo(FacialValueChannelKind.Gaze));
            Assert.That(derivation.Channels[1].AxisCount, Is.EqualTo(2));
            Assert.That(derivation.InvalidChannelSubIds, Is.Empty);
        }

        private TimelineAsset CreateTimeline()
        {
            return Track(ScriptableObject.CreateInstance<TimelineAsset>());
        }

        private T Track<T>(T obj) where T : Object
        {
            _created.Add(obj);
            return obj;
        }

        private static void AddValueClip(FacialValueTrack track, double start, int axisCount)
        {
            TimelineClip clip = track.CreateClip<FacialValueClip>();
            clip.start = start;
            clip.duration = 1d;
            var axes = new AnimationCurve[axisCount];
            for (int i = 0; i < axisCount; i++)
            {
                axes[i] = AnimationCurve.Constant(0f, 1f, 0.5f);
            }

            ((FacialValueClip)clip.asset).Axes = axes;
        }

        private static void AssertTrack(
            TimelineScanResult result,
            int index,
            string name,
            TimelineTrackKind kind,
            bool isChild,
            int parentIndex)
        {
            TimelineTrackDescriptor descriptor = result.Tracks[index];
            Assert.That(descriptor.TrackIndex, Is.EqualTo(index), name);
            Assert.That(descriptor.Name, Is.EqualTo(name));
            Assert.That(descriptor.Kind, Is.EqualTo(kind), name);
            Assert.That(descriptor.IsChild, Is.EqualTo(isChild), name);
            Assert.That(descriptor.ParentIndex, Is.EqualTo(parentIndex), name);
        }

        private static FacialProfile CreateProfile(params string[] layerNames)
        {
            var layers = new LayerDefinition[layerNames.Length];
            for (int i = 0; i < layerNames.Length; i++)
            {
                layers[i] = new LayerDefinition(layerNames[i], i, ExclusionMode.LastWins);
            }

            return new FacialProfile("1.0.0", layers: layers);
        }
    }
}
