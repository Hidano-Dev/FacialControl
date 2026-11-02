using System.Collections.Generic;
using Hidano.FacialControl.Adapters.Playable;
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
    /// <summary>
    /// 対象の解決（HID-408）: Facial トラックの対象は祖先の Facial Character トラックの binding だけで決まり、
    /// 静的診断の Director はその binding を持つ Director をシーンから探す。トラックの binding は書かない（自動 Bind しない）。
    /// </summary>
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

        // ---- FacialCharacterTrackBinding ----

        [Test]
        public void ResolveController_ChildAndLaneOfBoundCharacterTrack_ReturnsBoundController()
        {
            FacialController controller = CreateController("Character");
            TimelineAsset timeline = CreateTimeline(out FacialCharacterTrack character, out FacialExpressionTrack emotion, out FacialValueTrack value);
            FacialExpressionTrack lane = timeline.CreateTrack<FacialExpressionTrack>(emotion, "emotion Lane 1");
            PlayableDirector director = CreateDirector("Director", timeline);
            director.SetGenericBinding(character, controller);

            Assert.That(FacialCharacterTrackBinding.ResolveController(director, emotion), Is.SameAs(controller));
            Assert.That(FacialCharacterTrackBinding.ResolveController(director, value), Is.SameAs(controller));
            Assert.That(FacialCharacterTrackBinding.ResolveController(director, lane), Is.SameAs(controller), "Lane も親トラックの対象を使う");
        }

        [Test]
        public void ResolveController_CharacterTrackBoundToControllerGameObject_ReturnsController()
        {
            FacialController controller = CreateController("Character");
            TimelineAsset timeline = CreateTimeline(out FacialCharacterTrack character, out FacialExpressionTrack emotion, out _);
            PlayableDirector director = CreateDirector("Director", timeline);
            director.SetGenericBinding(character, controller.gameObject);

            Assert.That(FacialCharacterTrackBinding.ResolveController(director, emotion), Is.SameAs(controller));
        }

        [Test]
        public void ResolveController_UnboundCharacterTrack_ReturnsNullAndDoesNotWriteBinding()
        {
            CreateController("Character");
            TimelineAsset timeline = CreateTimeline(out FacialCharacterTrack character, out FacialExpressionTrack emotion, out _);
            PlayableDirector director = CreateDirector("Director", timeline);

            Assert.That(FacialCharacterTrackBinding.ResolveController(director, emotion), Is.Null, "シーンから推測しない");
            Assert.That(director.GetGenericBinding(character), Is.Null, "自動 Bind しない");
        }

        [Test]
        public void ResolveController_TrackOutsideCharacterTrack_ReturnsNullEvenIfBound()
        {
            FacialController controller = CreateController("Character");
            TimelineAsset timeline = Track(ScriptableObject.CreateInstance<TimelineAsset>());
            FacialExpressionTrack legacy = timeline.CreateTrack<FacialExpressionTrack>(null, "emotion");
            PlayableDirector director = CreateDirector("Director", timeline);
            director.SetGenericBinding(legacy, controller);

            Assert.That(FacialCharacterTrackBinding.ResolveController(director, legacy), Is.Null, "旧構造のトラックは再生しない");
            CollectionAssert.AreEqual(new TrackAsset[] { legacy }, FacialCharacterTrackBinding.CollectTracksOutsideCharacter(timeline));
        }

        [Test]
        public void CollectTracksOutsideCharacter_AllUnderCharacter_ReturnsEmpty()
        {
            TimelineAsset timeline = CreateTimeline(out _, out _, out _);

            Assert.That(FacialCharacterTrackBinding.CollectTracksOutsideCharacter(timeline), Is.Empty);
            Assert.That(FacialCharacterTrackBinding.CollectCharacterTracks(timeline), Has.Count.EqualTo(1));
        }

        // ---- ResolveDirector ----

        [Test]
        public void ResolveDirector_OverrideGiven_ReturnsOverride()
        {
            FacialTimelineReceiver receiver = CreateReceiver("Character");
            PlayableDirector overrideDirector = CreateDirector("OverrideDirector", null);

            PlayableDirector resolved = TimelineTrackBindingResolver.ResolveDirector(
                receiver, overrideDirector, out DirectorResolveStatus status);

            Assert.That(status, Is.EqualTo(DirectorResolveStatus.Override));
            Assert.That(resolved, Is.SameAs(overrideDirector));
        }

        [Test]
        public void ResolveDirector_SceneDirectorBindingController_ReturnsSceneUnique()
        {
            FacialTimelineReceiver receiver = CreateReceiver("Character");
            FacialTimelineReceiver otherReceiver = CreateReceiver("Other");

            TimelineAsset timeline = CreateTimeline(out FacialCharacterTrack character, out _, out _);
            PlayableDirector target = CreateDirector("Director", timeline);
            target.SetGenericBinding(character, receiver.GetComponent<FacialController>());

            TimelineAsset otherTimeline = CreateTimeline(out FacialCharacterTrack otherCharacter, out _, out _);
            CreateDirector("OtherDirector", otherTimeline).SetGenericBinding(otherCharacter, otherReceiver.GetComponent<FacialController>());

            PlayableDirector resolved = TimelineTrackBindingResolver.ResolveDirector(receiver, null, out DirectorResolveStatus status);

            Assert.That(status, Is.EqualTo(DirectorResolveStatus.SceneUnique));
            Assert.That(resolved, Is.SameAs(target));
        }

        [Test]
        public void ResolveDirector_DirectorOnSameObjectWithoutBinding_ReturnsNotFound()
        {
            FacialTimelineReceiver receiver = CreateReceiver("Character");
            PlayableDirector sameObject = receiver.gameObject.AddComponent<PlayableDirector>();
            sameObject.playableAsset = CreateTimeline(out _, out _, out _);

            PlayableDirector resolved = TimelineTrackBindingResolver.ResolveDirector(receiver, null, out DirectorResolveStatus status);

            Assert.That(status, Is.EqualTo(DirectorResolveStatus.NotFound), "同じ GameObject の Director でも Bind されていなければ対象にしない");
            Assert.That(resolved, Is.Null);
        }

        [Test]
        public void ResolveDirector_TwoSceneDirectorsBindingController_ReturnsAmbiguous()
        {
            FacialTimelineReceiver receiver = CreateReceiver("Character");
            FacialController controller = receiver.GetComponent<FacialController>();
            TimelineAsset first = CreateTimeline(out FacialCharacterTrack firstCharacter, out _, out _);
            CreateDirector("DirectorA", first).SetGenericBinding(firstCharacter, controller);
            TimelineAsset second = CreateTimeline(out FacialCharacterTrack secondCharacter, out _, out _);
            CreateDirector("DirectorB", second).SetGenericBinding(secondCharacter, controller);

            PlayableDirector resolved = TimelineTrackBindingResolver.ResolveDirector(receiver, null, out DirectorResolveStatus status);

            Assert.That(status, Is.EqualTo(DirectorResolveStatus.Ambiguous));
            Assert.That(resolved, Is.Null);
        }

        [Test]
        public void ResolveDirector_NoDirectorBindsController_ReturnsNotFound()
        {
            FacialTimelineReceiver receiver = CreateReceiver("Character");
            CreateDirector("Unbound", CreateTimeline(out _, out _, out _));

            PlayableDirector resolved = TimelineTrackBindingResolver.ResolveDirector(receiver, null, out DirectorResolveStatus status);

            Assert.That(status, Is.EqualTo(DirectorResolveStatus.NotFound));
            Assert.That(resolved, Is.Null);
        }

        // ---- ヘルパー ----

        private FacialController CreateController(string name)
        {
            GameObject go = CreateGameObject(name);
            go.AddComponent<Animator>();
            return go.AddComponent<FacialController>();
        }

        private FacialTimelineReceiver CreateReceiver(string name)
        {
            return CreateController(name).gameObject.AddComponent<FacialTimelineReceiver>();
        }

        private GameObject CreateGameObject(string name)
        {
            return Track(new GameObject(name));
        }

        private PlayableDirector CreateDirector(string name, TimelineAsset timeline)
        {
            PlayableDirector director = CreateGameObject(name).AddComponent<PlayableDirector>();
            director.playableAsset = timeline;
            return director;
        }

        private TimelineAsset CreateTimeline(out FacialCharacterTrack character, out FacialExpressionTrack emotion, out FacialValueTrack value)
        {
            TimelineAsset timeline = Track(ScriptableObject.CreateInstance<TimelineAsset>());
            character = timeline.CreateTrack<FacialCharacterTrack>(null, "Facial Character");
            emotion = character.CreateChildTrack<FacialExpressionTrack>("emotion");
            value = character.CreateChildTrack<FacialValueTrack>("osc:lt");
            return timeline;
        }

        private T Track<T>(T obj) where T : Object
        {
            _created.Add(obj);
            return obj;
        }
    }
}
