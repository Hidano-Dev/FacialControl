using System;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Testing;
using Hidano.FacialControl.Timeline.Domain.Models;
using Hidano.FacialControl.Timeline.Domain.Services;
using Hidano.FacialControl.Timeline.Tracks;
using NUnit.Framework;

namespace Hidano.FacialControl.Timeline.Tests.EditMode
{
    [SmallTest]
    public sealed class TimelineChannelDeriverTests : SizedTestFixture
    {
        [Test]
        public void Derive_RootExpressionTracksMatchingProfileLayers_ReturnsLayersWithProfileIndex()
        {
            var tracks = new[]
            {
                ExpressionTrack(0, "eye"),
                ExpressionTrack(1, "emotion"),
            };

            TimelineDerivation derivation = TimelineChannelDeriver.Derive(tracks, CreateProfile("emotion", "lipsync", "eye"));

            Assert.That(derivation.Layers.Count, Is.EqualTo(2));
            Assert.That(derivation.Layers[0].LayerName, Is.EqualTo("eye"));
            Assert.That(derivation.Layers[0].LayerIndex, Is.EqualTo(2));
            Assert.That(derivation.Layers[0].TrackIndex, Is.EqualTo(0));
            Assert.That(derivation.Layers[0].IsMatched, Is.True);
            Assert.That(derivation.Layers[1].LayerName, Is.EqualTo("emotion"));
            Assert.That(derivation.Layers[1].LayerIndex, Is.EqualTo(0));
            Assert.That(derivation.Layers[1].TrackIndex, Is.EqualTo(1));
            Assert.That(derivation.UnmatchedTrackNames, Is.Empty);
            Assert.That(derivation.HasFacialTracks, Is.True);
        }

        [Test]
        public void Derive_ChildExpressionTracks_AreExcludedFromLayersAndUnmatched()
        {
            var tracks = new[]
            {
                ExpressionTrack(0, "emotion"),
                ExpressionTrack(1, "emotion Lane 1", isChild: true, parentIndex: 0),
                ExpressionTrack(2, "unknown Lane 1", isChild: true, parentIndex: 0),
            };

            TimelineDerivation derivation = TimelineChannelDeriver.Derive(tracks, CreateProfile("emotion"));

            Assert.That(derivation.Layers.Count, Is.EqualTo(1));
            Assert.That(derivation.Layers[0].TrackIndex, Is.EqualTo(0));
            Assert.That(derivation.UnmatchedTrackNames, Is.Empty);
        }

        [Test]
        public void Derive_RootExpressionTrackNotInProfile_RecordsUnmatchedTrackName()
        {
            var tracks = new[]
            {
                ExpressionTrack(0, "emotion"),
                ExpressionTrack(1, "Emotion"),
                ExpressionTrack(2, "mouth"),
            };

            TimelineDerivation derivation = TimelineChannelDeriver.Derive(tracks, CreateProfile("emotion"));

            Assert.That(derivation.Layers.Count, Is.EqualTo(1));
            Assert.That(derivation.Layers[0].LayerName, Is.EqualTo("emotion"));
            CollectionAssert.AreEqual(new[] { "Emotion", "mouth" }, derivation.UnmatchedTrackNames);
        }

        [Test]
        public void Derive_LayersAreOrderedByTrackIndex()
        {
            var tracks = new[]
            {
                ExpressionTrack(3, "eye"),
                ExpressionTrack(1, "emotion"),
            };

            TimelineDerivation derivation = TimelineChannelDeriver.Derive(tracks, CreateProfile("emotion", "eye"));

            Assert.That(derivation.Layers[0].TrackIndex, Is.EqualTo(1));
            Assert.That(derivation.Layers[1].TrackIndex, Is.EqualTo(3));
        }

        [Test]
        public void Derive_RootValueTracks_ReturnsChannelsWithRecSourceIdAsIs()
        {
            var tracks = new[]
            {
                ValueTrack(0, "osc:lt", FacialValueChannelKind.Analog, 1),
                ValueTrack(1, "osc:gaze.left", FacialValueChannelKind.Gaze, 2),
            };

            TimelineDerivation derivation = TimelineChannelDeriver.Derive(tracks, CreateProfile("emotion"));

            Assert.That(derivation.Channels.Count, Is.EqualTo(2));
            Assert.That(derivation.Channels[0].ChannelSubId, Is.EqualTo("osc:lt"));
            Assert.That(derivation.Channels[0].Kind, Is.EqualTo(FacialValueChannelKind.Analog));
            Assert.That(derivation.Channels[0].TrackIndex, Is.EqualTo(0));
            Assert.That(derivation.Channels[1].ChannelSubId, Is.EqualTo("osc:gaze.left"));
            Assert.That(derivation.Channels[1].Kind, Is.EqualTo(FacialValueChannelKind.Gaze));
            Assert.That(derivation.Channels[1].AxisCount, Is.EqualTo(2));
            Assert.That(derivation.InvalidChannelSubIds, Is.Empty);
            Assert.That(derivation.Layers, Is.Empty);
            Assert.That(derivation.UnmatchedTrackNames, Is.Empty);
        }

        [Test]
        public void Derive_ChildValueTrack_IsNotAChannel()
        {
            var tracks = new[]
            {
                ExpressionTrack(0, "emotion"),
                ValueTrack(1, "osc:lt", FacialValueChannelKind.Analog, 1, isChild: true, parentIndex: 0),
            };

            TimelineDerivation derivation = TimelineChannelDeriver.Derive(tracks, CreateProfile("emotion"));

            Assert.That(derivation.Channels, Is.Empty);
        }

        [Test]
        public void Derive_DuplicateChannelSubId_FirstWinsAndLaterIsInvalid()
        {
            var tracks = new[]
            {
                ValueTrack(0, "osc:lt", FacialValueChannelKind.Analog, 1),
                ValueTrack(1, "osc:lt", FacialValueChannelKind.Analog, 3),
            };

            TimelineDerivation derivation = TimelineChannelDeriver.Derive(tracks, CreateProfile("emotion"));

            Assert.That(derivation.Channels.Count, Is.EqualTo(1));
            Assert.That(derivation.Channels[0].TrackIndex, Is.EqualTo(0));
            Assert.That(derivation.Channels[0].AxisCount, Is.EqualTo(1));
            CollectionAssert.AreEqual(new[] { "osc:lt" }, derivation.InvalidChannelSubIds);
        }

        [Test]
        public void Derive_ValueTrackAxisCount_UsesDescriptorMaximum()
        {
            var tracks = new[]
            {
                ValueTrack(0, "osc:stick", FacialValueChannelKind.Analog, 3),
            };

            TimelineDerivation derivation = TimelineChannelDeriver.Derive(tracks, CreateProfile("emotion"));

            Assert.That(derivation.Channels[0].AxisCount, Is.EqualTo(3));
        }

        [Test]
        public void Derive_ValueTrackWithZeroAxes_IsInvalid()
        {
            var tracks = new[]
            {
                ValueTrack(0, "osc:empty", FacialValueChannelKind.Analog, 0),
            };

            TimelineDerivation derivation = TimelineChannelDeriver.Derive(tracks, CreateProfile("emotion"));

            Assert.That(derivation.Channels, Is.Empty);
            CollectionAssert.AreEqual(new[] { "osc:empty" }, derivation.InvalidChannelSubIds);
        }

        [Test]
        public void Derive_ValueTrackWithEmptyChannelSubId_IsInvalid()
        {
            var tracks = new[]
            {
                ValueTrack(0, string.Empty, FacialValueChannelKind.Analog, 1),
            };

            TimelineDerivation derivation = TimelineChannelDeriver.Derive(tracks, CreateProfile("emotion"));

            Assert.That(derivation.Channels, Is.Empty);
            CollectionAssert.AreEqual(new[] { string.Empty }, derivation.InvalidChannelSubIds);
        }

        [Test]
        public void Derive_EmptyTracks_HasNoFacialTracks()
        {
            TimelineDerivation derivation = TimelineChannelDeriver.Derive(
                Array.Empty<TimelineTrackDescriptor>(),
                CreateProfile("emotion"));

            Assert.That(derivation.HasFacialTracks, Is.False);
            Assert.That(derivation.Layers, Is.Empty);
            Assert.That(derivation.Channels, Is.Empty);
            Assert.That(derivation.UnmatchedTrackNames, Is.Empty);
            Assert.That(derivation.InvalidChannelSubIds, Is.Empty);
        }

        [Test]
        public void Derive_DefaultProfile_AllRootExpressionTracksAreUnmatched()
        {
            var tracks = new[]
            {
                ExpressionTrack(0, "emotion"),
                ExpressionTrack(1, "eye"),
                ValueTrack(2, "osc:lt", FacialValueChannelKind.Analog, 1),
            };

            TimelineDerivation derivation = TimelineChannelDeriver.Derive(tracks, default);

            Assert.That(derivation.Layers, Is.Empty);
            CollectionAssert.AreEqual(new[] { "emotion", "eye" }, derivation.UnmatchedTrackNames);
            Assert.That(derivation.HasFacialTracks, Is.True);
        }

        [Test]
        public void Derive_NullTracks_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => TimelineChannelDeriver.Derive(null, CreateProfile("emotion")));
        }

        [Test]
        public void Derive_SameInput_ReturnsEquivalentResult()
        {
            var tracks = new[]
            {
                ExpressionTrack(0, "emotion"),
                ExpressionTrack(1, "mouth"),
                ValueTrack(2, "osc:lt", FacialValueChannelKind.Analog, 1),
            };
            FacialProfile profile = CreateProfile("emotion");

            TimelineDerivation first = TimelineChannelDeriver.Derive(tracks, profile);
            TimelineDerivation second = TimelineChannelDeriver.Derive(tracks, profile);

            CollectionAssert.AreEqual(first.Layers, second.Layers);
            CollectionAssert.AreEqual(first.UnmatchedTrackNames, second.UnmatchedTrackNames);
            CollectionAssert.AreEqual(first.Channels, second.Channels);
        }

        [Test]
        public void TrackDescriptor_ExposesConstructorValues()
        {
            var descriptor = new TimelineTrackDescriptor(
                trackIndex: 4,
                kind: TimelineTrackKind.Value,
                name: "Gaze",
                channelSubId: "osc:gaze",
                channelKind: FacialValueChannelKind.Gaze,
                maxAxisCount: 2,
                hasBakeReference: true,
                bakeInstanceId: 1234,
                isChild: true,
                parentIndex: 1);

            Assert.That(descriptor.TrackIndex, Is.EqualTo(4));
            Assert.That(descriptor.Kind, Is.EqualTo(TimelineTrackKind.Value));
            Assert.That(descriptor.Name, Is.EqualTo("Gaze"));
            Assert.That(descriptor.ChannelSubId, Is.EqualTo("osc:gaze"));
            Assert.That(descriptor.ChannelKind, Is.EqualTo(FacialValueChannelKind.Gaze));
            Assert.That(descriptor.MaxAxisCount, Is.EqualTo(2));
            Assert.That(descriptor.HasBakeReference, Is.True);
            Assert.That(descriptor.BakeInstanceId, Is.EqualTo(1234));
            Assert.That(descriptor.IsChild, Is.True);
            Assert.That(descriptor.ParentIndex, Is.EqualTo(1));
        }

        private static TimelineTrackDescriptor ExpressionTrack(
            int trackIndex,
            string name,
            bool isChild = false,
            int parentIndex = -1)
        {
            return new TimelineTrackDescriptor(
                trackIndex,
                TimelineTrackKind.Expression,
                name,
                channelSubId: string.Empty,
                channelKind: FacialValueChannelKind.Analog,
                maxAxisCount: 0,
                hasBakeReference: false,
                bakeInstanceId: 0,
                isChild: isChild,
                parentIndex: parentIndex);
        }

        private static TimelineTrackDescriptor ValueTrack(
            int trackIndex,
            string channelSubId,
            FacialValueChannelKind kind,
            int maxAxisCount,
            bool isChild = false,
            int parentIndex = -1)
        {
            return new TimelineTrackDescriptor(
                trackIndex,
                TimelineTrackKind.Value,
                "Value " + trackIndex,
                channelSubId,
                kind,
                maxAxisCount,
                hasBakeReference: false,
                bakeInstanceId: 0,
                isChild: isChild,
                parentIndex: parentIndex);
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
