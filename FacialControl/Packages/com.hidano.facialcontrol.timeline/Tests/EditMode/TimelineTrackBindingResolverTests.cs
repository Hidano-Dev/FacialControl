using System.Collections.Generic;
using Hidano.FacialControl.Testing;
using Hidano.FacialControl.Timeline.Adapters;
using Hidano.FacialControl.Timeline.Adapters.Session;
using Hidano.FacialControl.Timeline.Tracks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Hidano.FacialControl.Timeline.Tests.EditMode
{
    [MediumTest]
    public sealed class TimelineTrackBindingResolverTests : SizedTestFixture
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

        // ---- ResolveDirector ----

        [Test]
        public void ResolveDirector_OverrideGiven_ReturnsOverrideBeforeOtherRules()
        {
            FacialTimelineReceiver receiver = CreateReceiver("Character");
            receiver.gameObject.AddComponent<PlayableDirector>();
            PlayableDirector overrideDirector = CreateDirector("OverrideDirector", null);

            PlayableDirector resolved = TimelineTrackBindingResolver.ResolveDirector(
                receiver, overrideDirector, out DirectorResolveStatus status);

            Assert.That(status, Is.EqualTo(DirectorResolveStatus.Override));
            Assert.That(resolved, Is.SameAs(overrideDirector));
        }

        [Test]
        public void ResolveDirector_DirectorOnSameObject_ReturnsSameObjectBeforeParent()
        {
            GameObject parent = CreateGameObject("Parent");
            parent.AddComponent<PlayableDirector>();
            FacialTimelineReceiver receiver = CreateReceiver("Character");
            receiver.transform.SetParent(parent.transform, false);
            PlayableDirector sameObject = receiver.gameObject.AddComponent<PlayableDirector>();

            PlayableDirector resolved = TimelineTrackBindingResolver.ResolveDirector(
                receiver, null, out DirectorResolveStatus status);

            Assert.That(status, Is.EqualTo(DirectorResolveStatus.SameObject));
            Assert.That(resolved, Is.SameAs(sameObject));
        }

        [Test]
        public void ResolveDirector_DirectorOnAncestor_ReturnsParent()
        {
            GameObject root = CreateGameObject("Root");
            PlayableDirector ancestorDirector = root.AddComponent<PlayableDirector>();
            GameObject middle = CreateGameObject("Middle");
            middle.transform.SetParent(root.transform, false);
            FacialTimelineReceiver receiver = CreateReceiver("Character");
            receiver.transform.SetParent(middle.transform, false);

            PlayableDirector resolved = TimelineTrackBindingResolver.ResolveDirector(
                receiver, null, out DirectorResolveStatus status);

            Assert.That(status, Is.EqualTo(DirectorResolveStatus.Parent));
            Assert.That(resolved, Is.SameAs(ancestorDirector));
        }

        [Test]
        public void ResolveDirector_SceneDirectorBindingFacialTrackToReceiver_ReturnsSceneUnique()
        {
            FacialTimelineReceiver receiver = CreateReceiver("Character");
            FacialTimelineReceiver otherReceiver = CreateReceiver("Other");

            TimelineAsset timeline = CreateTimeline(out FacialExpressionTrack emotion, out _, out _);
            PlayableDirector target = CreateDirector("Director", timeline);
            target.SetGenericBinding(emotion, receiver);

            TimelineAsset otherTimeline = CreateTimeline(out FacialExpressionTrack otherEmotion, out _, out _);
            PlayableDirector otherDirector = CreateDirector("OtherDirector", otherTimeline);
            otherDirector.SetGenericBinding(otherEmotion, otherReceiver);

            TimelineAsset nonFacial = Track(ScriptableObject.CreateInstance<TimelineAsset>());
            nonFacial.CreateTrack<AnimationTrack>(null, "anim");
            CreateDirector("NonFacialDirector", nonFacial);

            PlayableDirector resolved = TimelineTrackBindingResolver.ResolveDirector(
                receiver, null, out DirectorResolveStatus status);

            Assert.That(status, Is.EqualTo(DirectorResolveStatus.SceneUnique));
            Assert.That(resolved, Is.SameAs(target));
        }

        [Test]
        public void ResolveDirector_SceneDirectorBindingReceiverGameObjectOnChildOrValueTrack_IsCandidate()
        {
            FacialTimelineReceiver receiver = CreateReceiver("Character");
            TimelineAsset timeline = CreateTimeline(out _, out _, out FacialValueTrack value);
            PlayableDirector target = CreateDirector("Director", timeline);
            target.SetGenericBinding(value, receiver.gameObject);

            PlayableDirector resolved = TimelineTrackBindingResolver.ResolveDirector(
                receiver, null, out DirectorResolveStatus status);

            Assert.That(status, Is.EqualTo(DirectorResolveStatus.SceneUnique));
            Assert.That(resolved, Is.SameAs(target));
        }

        [Test]
        public void ResolveDirector_TwoSceneDirectorsBindingReceiver_ReturnsAmbiguous()
        {
            FacialTimelineReceiver receiver = CreateReceiver("Character");
            TimelineAsset first = CreateTimeline(out FacialExpressionTrack firstEmotion, out _, out _);
            CreateDirector("DirectorA", first).SetGenericBinding(firstEmotion, receiver);
            TimelineAsset second = CreateTimeline(out FacialExpressionTrack secondEmotion, out _, out _);
            CreateDirector("DirectorB", second).SetGenericBinding(secondEmotion, receiver);

            PlayableDirector resolved = TimelineTrackBindingResolver.ResolveDirector(
                receiver, null, out DirectorResolveStatus status);

            Assert.That(status, Is.EqualTo(DirectorResolveStatus.Ambiguous));
            Assert.That(resolved, Is.Null);
        }

        [Test]
        public void ResolveDirector_NoCandidate_ReturnsNotFound()
        {
            FacialTimelineReceiver receiver = CreateReceiver("Character");
            TimelineAsset timeline = CreateTimeline(out _, out _, out _);
            CreateDirector("UnboundDirector", timeline);

            PlayableDirector resolved = TimelineTrackBindingResolver.ResolveDirector(
                receiver, null, out DirectorResolveStatus status);

            Assert.That(status, Is.EqualTo(DirectorResolveStatus.NotFound));
            Assert.That(resolved, Is.Null);
        }

        // ---- EnsureBindings ----

        [Test]
        public void EnsureBindings_UnboundFacialTracks_AreAssignedToReceiverIncludingChildren()
        {
            FacialTimelineReceiver receiver = CreateReceiver("Character");
            TimelineAsset timeline = CreateTimeline(out FacialExpressionTrack emotion, out FacialExpressionTrack lane, out FacialValueTrack value);
            AnimationTrack anim = timeline.CreateTrack<AnimationTrack>(null, "anim");
            PlayableDirector director = CreateDirector("Director", timeline);
            var writer = new RecordingWriter();

            TrackBindingReport report = TimelineTrackBindingResolver.EnsureBindings(director, timeline, receiver, writer);

            Assert.That(report.Assigned, Is.EqualTo(3));
            Assert.That(report.AlreadyBound, Is.EqualTo(0));
            Assert.That(report.BoundToOther, Is.Empty);
            Assert.That(director.GetGenericBinding(emotion), Is.SameAs(receiver));
            Assert.That(director.GetGenericBinding(lane), Is.SameAs(receiver));
            Assert.That(director.GetGenericBinding(value), Is.SameAs(receiver));
            Assert.That(director.GetGenericBinding(anim), Is.Null);
            CollectionAssert.AreEquivalent(new TrackAsset[] { emotion, lane, value }, writer.Tracks);
        }

        [Test]
        public void EnsureBindings_SecondCall_ReportsAlreadyBoundAndDoesNotWrite()
        {
            FacialTimelineReceiver receiver = CreateReceiver("Character");
            TimelineAsset timeline = CreateTimeline(out FacialExpressionTrack emotion, out _, out _);
            PlayableDirector director = CreateDirector("Director", timeline);
            TimelineTrackBindingResolver.EnsureBindings(director, timeline, receiver, RuntimeTrackBindingWriter.Instance);
            director.SetGenericBinding(emotion, receiver.gameObject);
            var writer = new RecordingWriter();

            TrackBindingReport report = TimelineTrackBindingResolver.EnsureBindings(director, timeline, receiver, writer);

            Assert.That(report.Assigned, Is.EqualTo(0));
            Assert.That(report.AlreadyBound, Is.EqualTo(3));
            Assert.That(writer.Tracks, Is.Empty);
            Assert.That(director.GetGenericBinding(emotion), Is.SameAs(receiver.gameObject));
        }

        [Test]
        public void EnsureBindings_TrackBoundToOtherObject_IsLeftUntouchedAndReported()
        {
            FacialTimelineReceiver receiver = CreateReceiver("Character");
            FacialTimelineReceiver otherReceiver = CreateReceiver("Other");
            TimelineAsset timeline = CreateTimeline(out FacialExpressionTrack emotion, out FacialExpressionTrack lane, out FacialValueTrack value);
            PlayableDirector director = CreateDirector("Director", timeline);
            director.SetGenericBinding(emotion, otherReceiver);

            TrackBindingReport report = TimelineTrackBindingResolver.EnsureBindings(
                director, timeline, receiver, RuntimeTrackBindingWriter.Instance);

            Assert.That(report.Assigned, Is.EqualTo(2));
            Assert.That(report.BoundToOther, Is.EqualTo(new TrackAsset[] { emotion }));
            Assert.That(director.GetGenericBinding(emotion), Is.SameAs(otherReceiver));
            Assert.That(director.GetGenericBinding(lane), Is.SameAs(receiver));
            Assert.That(director.GetGenericBinding(value), Is.SameAs(receiver));
        }

        [Test]
        public void EnsureBindings_TimelineWithoutFacialTracks_DoesNothing()
        {
            FacialTimelineReceiver receiver = CreateReceiver("Character");
            TimelineAsset timeline = Track(ScriptableObject.CreateInstance<TimelineAsset>());
            AnimationTrack anim = timeline.CreateTrack<AnimationTrack>(null, "anim");
            PlayableDirector director = CreateDirector("Director", timeline);
            var writer = new RecordingWriter();

            TrackBindingReport report = TimelineTrackBindingResolver.EnsureBindings(director, timeline, receiver, writer);

            Assert.That(TimelineTrackBindingResolver.HasFacialTracks(timeline), Is.False);
            Assert.That(report.Assigned, Is.EqualTo(0));
            Assert.That(report.AlreadyBound, Is.EqualTo(0));
            Assert.That(report.BoundToOther, Is.Empty);
            Assert.That(writer.Tracks, Is.Empty);
            Assert.That(director.GetGenericBinding(anim), Is.Null);
        }

        [Test]
        public void HasFacialTracks_TimelineWithFacialTrack_ReturnsTrue()
        {
            TimelineAsset timeline = CreateTimeline(out _, out _, out _);

            Assert.That(TimelineTrackBindingResolver.HasFacialTracks(timeline), Is.True);
            Assert.That(TimelineTrackBindingResolver.HasFacialTracks(null), Is.False);
        }

        // ---- helpers ----

        private TimelineAsset CreateTimeline(
            out FacialExpressionTrack emotion,
            out FacialExpressionTrack lane,
            out FacialValueTrack value)
        {
            TimelineAsset timeline = Track(ScriptableObject.CreateInstance<TimelineAsset>());
            emotion = timeline.CreateTrack<FacialExpressionTrack>(null, "emotion");
            lane = timeline.CreateTrack<FacialExpressionTrack>(emotion, "emotion Lane 1");
            value = timeline.CreateTrack<FacialValueTrack>(null, "osc:lt");
            return timeline;
        }

        private PlayableDirector CreateDirector(string name, TimelineAsset timeline)
        {
            GameObject go = CreateGameObject(name);
            var director = go.AddComponent<PlayableDirector>();
            director.playOnAwake = false;
            director.playableAsset = timeline;
            return director;
        }

        private FacialTimelineReceiver CreateReceiver(string name)
        {
            return CreateGameObject(name).AddComponent<FacialTimelineReceiver>();
        }

        private GameObject CreateGameObject(string name)
        {
            return Track(new GameObject(name));
        }

        private T Track<T>(T obj) where T : Object
        {
            _created.Add(obj);
            return obj;
        }

        private sealed class RecordingWriter : ITrackBindingWriter
        {
            public List<TrackAsset> Tracks { get; } = new List<TrackAsset>();

            public void SetGenericBinding(PlayableDirector director, TrackAsset track, Object value)
            {
                Tracks.Add(track);
                director.SetGenericBinding(track, value);
            }
        }
    }
}
