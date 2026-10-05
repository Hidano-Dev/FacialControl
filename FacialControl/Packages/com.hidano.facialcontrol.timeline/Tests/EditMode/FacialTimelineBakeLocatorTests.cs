using System.Collections.Generic;
using Hidano.FacialControl.Testing;
using Hidano.FacialControl.Timeline.Adapters.Assets;
using Hidano.FacialControl.Timeline.Tracks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Timeline;

namespace Hidano.FacialControl.Timeline.Tests.EditMode
{
    [SmallTest]
    public sealed class FacialTimelineBakeLocatorTests : SizedTestFixture
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
        public void Locate_NullTimeline_ReturnsMissing()
        {
            BakeLocateResult result = FacialTimelineBakeLocator.Locate(null, null);

            Assert.That(result.Status, Is.EqualTo(BakeLocateStatus.Missing));
            Assert.That(result.Bake, Is.Null);
            Assert.That(result.HolderCount, Is.EqualTo(0));
        }

        [Test]
        public void Locate_AllFacialTracksShareOneReference_ReturnsFound()
        {
            FacialTimelineBakeAsset bake = CreateBake();
            TimelineAsset timeline = CreateTimeline(bake, bake, bake, bake);

            BakeLocateResult result = FacialTimelineBakeLocator.Locate(timeline, null);

            Assert.That(result.Status, Is.EqualTo(BakeLocateStatus.Found));
            Assert.That(result.Bake, Is.SameAs(bake));
            Assert.That(result.TrackBake, Is.SameAs(bake));
            Assert.That(result.HolderCount, Is.EqualTo(4));
            Assert.That(result.NullHolderCount, Is.EqualTo(0));
            Assert.That(result.DistinctReferenceCount, Is.EqualTo(1));
            Assert.That(result.OverrideDiffers, Is.False);
        }

        [Test]
        public void Locate_TimelineWithoutFacialTracks_ReturnsMissing()
        {
            TimelineAsset timeline = Track(ScriptableObject.CreateInstance<TimelineAsset>());
            timeline.CreateTrack<AnimationTrack>(null, "anim");

            BakeLocateResult result = FacialTimelineBakeLocator.Locate(timeline, null);

            Assert.That(result.Status, Is.EqualTo(BakeLocateStatus.Missing));
            Assert.That(result.Bake, Is.Null);
            Assert.That(result.HolderCount, Is.EqualTo(0));
        }

        [Test]
        public void Locate_AllReferencesNull_ReturnsLegacyExport()
        {
            TimelineAsset timeline = CreateTimeline(null, null, null, null);

            BakeLocateResult result = FacialTimelineBakeLocator.Locate(timeline, null);

            Assert.That(result.Status, Is.EqualTo(BakeLocateStatus.LegacyExport));
            Assert.That(result.Bake, Is.Null);
            Assert.That(result.TrackBake, Is.Null);
            Assert.That(result.HolderCount, Is.EqualTo(4));
            Assert.That(result.NullHolderCount, Is.EqualTo(4));
            Assert.That(result.DistinctReferenceCount, Is.EqualTo(0));
        }

        [Test]
        public void Locate_MixedReferences_ReturnsConflictWithoutAdoptingAny()
        {
            FacialTimelineBakeAsset bakeA = CreateBake();
            FacialTimelineBakeAsset bakeB = CreateBake();
            TimelineAsset timeline = CreateTimeline(bakeA, bakeA, bakeB, bakeA);

            BakeLocateResult result = FacialTimelineBakeLocator.Locate(timeline, null);

            Assert.That(result.Status, Is.EqualTo(BakeLocateStatus.Conflict));
            Assert.That(result.Bake, Is.Null);
            Assert.That(result.TrackBake, Is.Null);
            Assert.That(result.NullHolderCount, Is.EqualTo(0));
            Assert.That(result.DistinctReferenceCount, Is.EqualTo(2));
        }

        [Test]
        public void Locate_PartiallyMissingReferences_ReturnsConflict()
        {
            FacialTimelineBakeAsset bake = CreateBake();
            TimelineAsset timeline = CreateTimeline(bake, null, bake, bake);

            BakeLocateResult result = FacialTimelineBakeLocator.Locate(timeline, null);

            Assert.That(result.Status, Is.EqualTo(BakeLocateStatus.Conflict));
            Assert.That(result.Bake, Is.Null);
            Assert.That(result.NullHolderCount, Is.EqualTo(1));
            Assert.That(result.DistinctReferenceCount, Is.EqualTo(1));
        }

        [Test]
        public void Locate_OverrideMatchingTrackReferences_ReturnsOverrideUsedWithoutDiffers()
        {
            FacialTimelineBakeAsset bake = CreateBake();
            TimelineAsset timeline = CreateTimeline(bake, bake, bake, bake);

            BakeLocateResult result = FacialTimelineBakeLocator.Locate(timeline, bake);

            Assert.That(result.Status, Is.EqualTo(BakeLocateStatus.OverrideUsed));
            Assert.That(result.Bake, Is.SameAs(bake));
            Assert.That(result.TrackBake, Is.SameAs(bake));
            Assert.That(result.OverrideDiffers, Is.False);
        }

        [Test]
        public void Locate_OverrideDifferentFromTrackReferences_ReturnsOverrideUsedWithDiffers()
        {
            FacialTimelineBakeAsset trackBake = CreateBake();
            FacialTimelineBakeAsset overrideBake = CreateBake();
            TimelineAsset timeline = CreateTimeline(trackBake, trackBake, trackBake, trackBake);

            BakeLocateResult result = FacialTimelineBakeLocator.Locate(timeline, overrideBake);

            Assert.That(result.Status, Is.EqualTo(BakeLocateStatus.OverrideUsed));
            Assert.That(result.Bake, Is.SameAs(overrideBake));
            Assert.That(result.TrackBake, Is.SameAs(trackBake));
            Assert.That(result.OverrideDiffers, Is.True);
        }

        [Test]
        public void Locate_OverrideWithPartiallyMissingTrackReferences_ReturnsOverrideUsedWithDiffers()
        {
            FacialTimelineBakeAsset bake = CreateBake();
            TimelineAsset timeline = CreateTimeline(bake, bake, null, bake);

            BakeLocateResult result = FacialTimelineBakeLocator.Locate(timeline, bake);

            Assert.That(result.Status, Is.EqualTo(BakeLocateStatus.OverrideUsed));
            Assert.That(result.Bake, Is.SameAs(bake));
            Assert.That(result.TrackBake, Is.Null);
            Assert.That(result.NullHolderCount, Is.EqualTo(1));
            Assert.That(result.OverrideDiffers, Is.True);
        }

        [Test]
        public void Locate_OverrideWithLegacyExport_ReturnsOverrideUsedWithDiffers()
        {
            FacialTimelineBakeAsset overrideBake = CreateBake();
            TimelineAsset timeline = CreateTimeline(null, null, null, null);

            BakeLocateResult result = FacialTimelineBakeLocator.Locate(timeline, overrideBake);

            Assert.That(result.Status, Is.EqualTo(BakeLocateStatus.OverrideUsed));
            Assert.That(result.Bake, Is.SameAs(overrideBake));
            Assert.That(result.OverrideDiffers, Is.True);
        }

        [Test]
        public void Locate_TrackOrderSwapped_ReturnsSameResult()
        {
            FacialTimelineBakeAsset bakeA = CreateBake();
            FacialTimelineBakeAsset bakeB = CreateBake();
            TimelineAsset forward = CreateTimeline(bakeA, bakeA, bakeB, null);
            TimelineAsset reversed = CreateTimelineReversed(bakeA, bakeA, bakeB, null);

            BakeLocateResult first = FacialTimelineBakeLocator.Locate(forward, null);
            BakeLocateResult second = FacialTimelineBakeLocator.Locate(reversed, null);

            Assert.That(second.Status, Is.EqualTo(first.Status));
            Assert.That(second.Status, Is.EqualTo(BakeLocateStatus.Conflict));
            Assert.That(second.Bake, Is.SameAs(first.Bake));
            Assert.That(second.TrackBake, Is.SameAs(first.TrackBake));
            Assert.That(second.HolderCount, Is.EqualTo(first.HolderCount));
            Assert.That(second.NullHolderCount, Is.EqualTo(first.NullHolderCount));
            Assert.That(second.DistinctReferenceCount, Is.EqualTo(first.DistinctReferenceCount));

            FacialTimelineBakeAsset shared = CreateBake();
            BakeLocateResult foundForward = FacialTimelineBakeLocator.Locate(CreateTimeline(shared, shared, shared, shared), null);
            BakeLocateResult foundReversed = FacialTimelineBakeLocator.Locate(CreateTimelineReversed(shared, shared, shared, shared), null);
            Assert.That(foundReversed.Status, Is.EqualTo(foundForward.Status));
            Assert.That(foundReversed.Bake, Is.SameAs(foundForward.Bake));
        }

        [Test]
        public void Locate_DoesNotModifyTrackReferences()
        {
            FacialTimelineBakeAsset bakeA = CreateBake();
            FacialTimelineBakeAsset overrideBake = CreateBake();
            TimelineAsset timeline = CreateTimeline(bakeA, null, bakeA, bakeA);

            FacialTimelineBakeLocator.Locate(timeline, overrideBake);

            var holders = new List<FacialTimelineBakeAsset>();
            foreach (TrackAsset track in timeline.GetOutputTracks())
            {
                holders.Add(((IFacialTimelineBakeHolder)track).Bake);
                foreach (TrackAsset child in track.GetChildTracks())
                {
                    holders.Add(((IFacialTimelineBakeHolder)child).Bake);
                }
            }

            CollectionAssert.AreEqual(new[] { bakeA, null, bakeA, bakeA }, holders);
        }

        // root Expression / その子 / root Expression / root Value の 4 トラック（root + 子）を作る
        private TimelineAsset CreateTimeline(
            FacialTimelineBakeAsset emotion,
            FacialTimelineBakeAsset emotionLane,
            FacialTimelineBakeAsset eye,
            FacialTimelineBakeAsset value)
        {
            TimelineAsset timeline = Track(ScriptableObject.CreateInstance<TimelineAsset>());
            var emotionTrack = timeline.CreateTrack<FacialExpressionTrack>(null, "emotion");
            timeline.CreateTrack<FacialExpressionTrack>(emotionTrack, "emotion Lane 1").Bake = emotionLane;
            emotionTrack.Bake = emotion;
            timeline.CreateTrack<FacialExpressionTrack>(null, "eye").Bake = eye;
            timeline.CreateTrack<FacialValueTrack>(null, "osc:lt").Bake = value;
            return timeline;
        }

        // 同じ割り当てを root の並び順を逆にして作る
        private TimelineAsset CreateTimelineReversed(
            FacialTimelineBakeAsset emotion,
            FacialTimelineBakeAsset emotionLane,
            FacialTimelineBakeAsset eye,
            FacialTimelineBakeAsset value)
        {
            TimelineAsset timeline = Track(ScriptableObject.CreateInstance<TimelineAsset>());
            timeline.CreateTrack<FacialValueTrack>(null, "osc:lt").Bake = value;
            timeline.CreateTrack<FacialExpressionTrack>(null, "eye").Bake = eye;
            var emotionTrack = timeline.CreateTrack<FacialExpressionTrack>(null, "emotion");
            timeline.CreateTrack<FacialExpressionTrack>(emotionTrack, "emotion Lane 1").Bake = emotionLane;
            emotionTrack.Bake = emotion;
            return timeline;
        }

        private FacialTimelineBakeAsset CreateBake()
        {
            return Track(ScriptableObject.CreateInstance<FacialTimelineBakeAsset>());
        }

        private T Track<T>(T obj) where T : Object
        {
            _created.Add(obj);
            return obj;
        }
    }
}
