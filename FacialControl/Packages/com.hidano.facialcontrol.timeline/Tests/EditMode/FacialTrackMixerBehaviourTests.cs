using System;
using System.Collections.Generic;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Timeline.Adapters;
using Hidano.FacialControl.Timeline.Adapters.Assets;
using Hidano.FacialControl.Timeline.Adapters.InputSources;
using Hidano.FacialControl.Timeline.Clips;
using Hidano.FacialControl.Timeline.Domain.Services;
using Hidano.FacialControl.Timeline.Tracks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Timeline.Tests.EditMode
{
    [MediumTest]
    public sealed class FacialTrackMixerBehaviourTests : SizedTestFixture
    {
        [Test]
        public void ExpressionMixer_CollectsParentAndChildLaneEvents_AndAdvancesLinearly()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            var directorObject = new GameObject("FacialTrackMixerBehaviourTests_Director");
            var receiverObject = new GameObject("FacialTrackMixerBehaviourTests_Receiver");
            var director = directorObject.AddComponent<PlayableDirector>();
            var receiver = receiverObject.AddComponent<FacialTimelineReceiver>();
            FacialTimelineBakeAsset bake = null;

            try
            {
                FacialProfile profile = CreateProfile();
                TimelineExpressionStateSink expressionSink = CreateExpressionSink(profile);
                TimelineAsset configuredTimeline = CreateTimeline(timeline);
                receiver.Configure(
                    profile,
                    new FakeInputSourceRegistry(),
                    new[] { ("Expressions", expressionSink) },
                    Array.Empty<(string sub, TimelineBakedValueSink sink)>(),
                    Array.Empty<(string sub, TimelineAnalogInputSource sink)>(),
                    Array.Empty<(string sub, TimelineGazeInputSource sink, string takeoverSourceId)>());

                bake = ScriptableObject.CreateInstance<FacialTimelineBakeAsset>();
                bake.SampleRate = 60f;
                bake.SourceHashHex = FacialTimelineHashCalculator.ComputeHashHex(configuredTimeline, profile, bake.SampleRate);
                receiver.BakeAsset = bake;

                director.playableAsset = configuredTimeline;
                director.timeUpdateMode = DirectorUpdateMode.Manual;
                director.extrapolationMode = DirectorWrapMode.None;
                director.SetGenericBinding(configuredTimeline.GetOutputTrack(0), receiver);
                director.Play();
                director.playableGraph.Evaluate(0f);

                director.playableGraph.Evaluate(0.75f);
                CollectionAssert.AreEqual(new[] { "smile", "angry" }, expressionSink.ActiveExpressionIds);

                director.playableGraph.Evaluate(0.5f);
                CollectionAssert.AreEqual(new[] { "angry" }, expressionSink.ActiveExpressionIds);
            }
            finally
            {
                DestroyGraph(director);
                UnityEngine.Object.DestroyImmediate(directorObject);
                UnityEngine.Object.DestroyImmediate(receiverObject);
                UnityEngine.Object.DestroyImmediate(timeline);
                if (bake != null)
                {
                    UnityEngine.Object.DestroyImmediate(bake);
                }
            }
        }

        [Test]
        public void ExpressionMixer_WhenScrubbedBackward_ReconstructsTargetStackByJump()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            var directorObject = new GameObject("FacialTrackMixerBehaviourTests_ScrubDirector");
            var receiverObject = new GameObject("FacialTrackMixerBehaviourTests_ScrubReceiver");
            var director = directorObject.AddComponent<PlayableDirector>();
            var receiver = receiverObject.AddComponent<FacialTimelineReceiver>();
            FacialTimelineBakeAsset bake = null;

            try
            {
                FacialProfile profile = CreateProfile();
                TimelineExpressionStateSink expressionSink = CreateExpressionSink(profile);
                TimelineAsset configuredTimeline = CreateTimeline(timeline);
                receiver.Configure(
                    profile,
                    new FakeInputSourceRegistry(),
                    new[] { ("Expressions", expressionSink) },
                    Array.Empty<(string sub, TimelineBakedValueSink sink)>(),
                    Array.Empty<(string sub, TimelineAnalogInputSource sink)>(),
                    Array.Empty<(string sub, TimelineGazeInputSource sink, string takeoverSourceId)>());

                bake = ScriptableObject.CreateInstance<FacialTimelineBakeAsset>();
                bake.SampleRate = 60f;
                bake.SourceHashHex = FacialTimelineHashCalculator.ComputeHashHex(configuredTimeline, profile, bake.SampleRate);
                receiver.BakeAsset = bake;

                director.playableAsset = configuredTimeline;
                director.timeUpdateMode = DirectorUpdateMode.Manual;
                director.extrapolationMode = DirectorWrapMode.None;
                director.SetGenericBinding(configuredTimeline.GetOutputTrack(0), receiver);
                director.Play();
                director.playableGraph.Evaluate(0f);

                director.playableGraph.Evaluate(1.25f);
                CollectionAssert.AreEqual(new[] { "angry" }, expressionSink.ActiveExpressionIds);

                director.time = 0.25d;
                director.Evaluate();
                CollectionAssert.AreEqual(new[] { "smile" }, expressionSink.ActiveExpressionIds);
            }
            finally
            {
                DestroyGraph(director);
                UnityEngine.Object.DestroyImmediate(directorObject);
                UnityEngine.Object.DestroyImmediate(receiverObject);
                UnityEngine.Object.DestroyImmediate(timeline);
                if (bake != null)
                {
                    UnityEngine.Object.DestroyImmediate(bake);
                }
            }
        }

        [Test]
        public void ValueMixer_SamplesAnalogCurvesIntoSink()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            var directorObject = new GameObject("FacialTrackMixerBehaviourTests_ValueDirector");
            var receiverObject = new GameObject("FacialTrackMixerBehaviourTests_ValueReceiver");
            var director = directorObject.AddComponent<PlayableDirector>();
            var receiver = receiverObject.AddComponent<FacialTimelineReceiver>();
            var analogSink = new TimelineAnalogInputSource(InputSourceId.Parse("timeline:analog-main"), axisCount: 3);

            try
            {
                receiver.Configure(
                    CreateProfile(),
                    new FakeInputSourceRegistry(),
                    Array.Empty<(string layer, TimelineExpressionStateSink sink)>(),
                    Array.Empty<(string sub, TimelineBakedValueSink sink)>(),
                    new[] { ("analog-main", analogSink) },
                    Array.Empty<(string sub, TimelineGazeInputSource sink, string takeoverSourceId)>());

                TimelineAsset configuredTimeline = CreateAnalogTimeline(timeline);
                director.playableAsset = configuredTimeline;
                director.SetGenericBinding(configuredTimeline.GetOutputTrack(0), receiver);
                director.RebuildGraph();

                director.time = 0.25d;
                director.Evaluate();

                Span<float> axes = stackalloc float[3];
                Assert.That(analogSink.IsValid, Is.True);
                Assert.That(analogSink.TryReadAxes(axes), Is.True);
                Assert.That(axes.ToArray(), Is.EqualTo(new[] { 0.25f, 0.5f, -0.25f }).Within(0.0001f));
            }
            finally
            {
                DestroyGraph(director);
                UnityEngine.Object.DestroyImmediate(directorObject);
                UnityEngine.Object.DestroyImmediate(receiverObject);
                UnityEngine.Object.DestroyImmediate(timeline);
            }
        }

        [Test]
        public void ValueMixer_SamplesGazeCurvesAndInvalidatesOutsideClip()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            var directorObject = new GameObject("FacialTrackMixerBehaviourTests_GazeDirector");
            var receiverObject = new GameObject("FacialTrackMixerBehaviourTests_GazeReceiver");
            var director = directorObject.AddComponent<PlayableDirector>();
            var receiver = receiverObject.AddComponent<FacialTimelineReceiver>();
            var gazeSink = new TimelineGazeInputSource(InputSourceId.Parse("timeline:gaze-0"));

            try
            {
                receiver.Configure(
                    CreateProfile(),
                    new FakeInputSourceRegistry(),
                    Array.Empty<(string layer, TimelineExpressionStateSink sink)>(),
                    Array.Empty<(string sub, TimelineBakedValueSink sink)>(),
                    Array.Empty<(string sub, TimelineAnalogInputSource sink)>(),
                    new[] { ("gaze-main", gazeSink, string.Empty) });

                TimelineAsset configuredTimeline = CreateGazeTimeline(timeline);
                director.playableAsset = configuredTimeline;
                director.SetGenericBinding(configuredTimeline.GetOutputTrack(0), receiver);
                director.RebuildGraph();

                director.time = 0.25d;
                director.Evaluate();

                Assert.That(gazeSink.IsValid, Is.True);
                Assert.That(gazeSink.TryReadVector2(out float x, out float y), Is.True);
                Assert.That(x, Is.EqualTo(-0.5f).Within(0.0001f));
                Assert.That(y, Is.EqualTo(0.5f).Within(0.0001f));

                director.time = 0.75d;
                director.Evaluate();

                Assert.That(gazeSink.IsValid, Is.False);
                Assert.That(gazeSink.TryReadVector2(out _, out _), Is.False);
            }
            finally
            {
                DestroyGraph(director);
                UnityEngine.Object.DestroyImmediate(directorObject);
                UnityEngine.Object.DestroyImmediate(receiverObject);
                UnityEngine.Object.DestroyImmediate(timeline);
            }
        }

        private static TimelineExpressionStateSink CreateExpressionSink(FacialProfile profile)
        {
            return new TimelineExpressionStateSink(
                InputSourceId.Parse("timeline:Expressions"),
                maxStackDepth: 8,
                exclusionMode: ExclusionMode.LastWins,
                blendShapeNames: new[] { "Smile", "Angry" },
                profile);
        }

        private static TimelineAsset CreateTimeline(TimelineAsset timeline)
        {
            var parentTrack = timeline.CreateTrack<FacialExpressionTrack>(null, "Expressions");
            TimelineClip parentClip = parentTrack.CreateClip<FacialExpressionClip>();
            parentClip.start = 0.0d;
            parentClip.duration = 1.0d;
            ((FacialExpressionClip)parentClip.asset).ExpressionId = "smile";

            var childTrack = timeline.CreateTrack<FacialExpressionTrack>(parentTrack, "Expressions Layer");
            TimelineClip childClip = childTrack.CreateClip<FacialExpressionClip>();
            childClip.start = 0.5d;
            childClip.duration = 1.0d;
            ((FacialExpressionClip)childClip.asset).ExpressionId = "angry";

            return timeline;
        }

        private static TimelineAsset CreateAnalogTimeline(TimelineAsset timeline)
        {
            var track = timeline.CreateTrack<FacialValueTrack>(null, "Analog");
            track.ChannelSubId = "analog-main";
            track.ChannelKind = FacialValueChannelKind.Analog;

            TimelineClip clip = track.CreateClip<FacialValueClip>();
            clip.start = 0.0d;
            clip.duration = 0.5d;
            ((FacialValueClip)clip.asset).Axes = new[]
            {
                AnimationCurve.Linear(0f, 0f, 0.5f, 0.5f),
                AnimationCurve.Linear(0f, 1f, 0.5f, 0f),
                AnimationCurve.Linear(0f, -0.5f, 0.5f, 0f),
            };

            return timeline;
        }

        private static TimelineAsset CreateGazeTimeline(TimelineAsset timeline)
        {
            var track = timeline.CreateTrack<FacialValueTrack>(null, "Gaze");
            track.ChannelSubId = "gaze-main";
            track.ChannelKind = FacialValueChannelKind.Gaze;

            TimelineClip clip = track.CreateClip<FacialValueClip>();
            clip.start = 0.0d;
            clip.duration = 0.5d;
            ((FacialValueClip)clip.asset).Axes = new[]
            {
                AnimationCurve.Linear(0f, -1f, 0.5f, 0f),
                AnimationCurve.Linear(0f, 1f, 0.5f, 0f),
            };

            TimelineClip secondClip = track.CreateClip<FacialValueClip>();
            secondClip.start = 1.0d;
            secondClip.duration = 0.25d;
            ((FacialValueClip)secondClip.asset).Axes = new[]
            {
                AnimationCurve.Linear(0f, 0f, 0.25f, 0.5f),
                AnimationCurve.Linear(0f, 0f, 0.25f, -0.5f),
            };

            return timeline;
        }

        private static FacialProfile CreateProfile()
        {
            return new FacialProfile(
                schemaVersion: "1.0.0",
                layers: new[]
                {
                    new LayerDefinition("Expressions", 0, ExclusionMode.LastWins),
                },
                expressions: new[]
                {
                    CreateExpression("smile", "Smile"),
                    CreateExpression("angry", "Angry"),
                });
        }

        private static Expression CreateExpression(string id, string name)
        {
            return new Expression(
                id: id,
                name: name,
                layer: "Expressions",
                transitionDuration: 0.1f,
                transitionCurve: TransitionCurve.Linear,
                blendShapeValues: new[]
                {
                    new BlendShapeMapping(name, 1.0f),
                });
        }

        private static void DestroyGraph(PlayableDirector director)
        {
            if (director != null && director.playableGraph.IsValid())
            {
                director.playableGraph.Destroy();
            }
        }

        private sealed class FakeInputSourceRegistry : IInputSourceRegistry
        {
            private readonly Dictionary<string, IInputSource> _entries =
                new Dictionary<string, IInputSource>(StringComparer.Ordinal);
            private readonly List<string> _registeredIds = new List<string>();

            public IReadOnlyList<string> RegisteredIds => _registeredIds;

            public void Register(AdapterSlug slug, IInputSource source)
            {
                RegisterInternal(slug.Value, source);
            }

            public void Replace(AdapterSlug slug, IInputSource source)
            {
                RegisterInternal(slug.Value, source);
            }

            public void Register(AdapterSlug slug, string sub, IInputSource source)
            {
                RegisterInternal(Compose(slug, sub), source);
            }

            public void Replace(AdapterSlug slug, string sub, IInputSource source)
            {
                RegisterInternal(Compose(slug, sub), source);
            }

            public void Unregister(AdapterSlug slug)
            {
                UnregisterInternal(slug.Value);
            }

            public void Unregister(AdapterSlug slug, string sub)
            {
                UnregisterInternal(Compose(slug, sub));
            }

            public bool TryResolve(string layerInputSourceId, out IInputSource source)
            {
                return _entries.TryGetValue(layerInputSourceId, out source);
            }

            public void Subscribe(string id, Action<IInputSource> handler)
            {
            }

            private void RegisterInternal(string id, IInputSource source)
            {
                _entries[id] = source;
                if (!_registeredIds.Contains(id))
                {
                    _registeredIds.Add(id);
                }
            }

            private void UnregisterInternal(string id)
            {
                _entries.Remove(id);
                _registeredIds.Remove(id);
            }

            private static string Compose(AdapterSlug slug, string sub)
            {
                return string.IsNullOrEmpty(sub) ? slug.Value : slug.Value + ":" + sub;
            }
        }
    }
}
