using System;
using System.Collections.Generic;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Adapters.ScriptableObject;
using Hidano.FacialControl.Adapters.Playable;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Timeline.Adapters;
using Hidano.FacialControl.Timeline.Adapters.Assets;
using Hidano.FacialControl.Timeline.Adapters.InputSources;
using Hidano.FacialControl.Timeline.Clips;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.Timeline;

namespace Hidano.FacialControl.Timeline.Tests.EditMode
{
    public sealed class FacialTimelineReceiverTests
    {
        [Test]
        public void BeginPlaybackSession_WhenBakeAssetMissing_WarnsAndMarksStatus()
        {
            var receiver = CreateReceiver();
            var registry = new FakeInputSourceRegistry();
            var timeline = CreateTimeline();
            var profile = CreateProfile();

            try
            {
                receiver.Configure(
                    profile,
                    registry,
                    Array.Empty<(string layer, TimelineExpressionStateSink sink)>(),
                    Array.Empty<(string sub, TimelineBakedValueSink sink)>(),
                    Array.Empty<(string sub, TimelineAnalogInputSource sink)>(),
                    Array.Empty<(string sub, TimelineGazeInputSource sink, string takeoverSourceId)>());

                LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(@"\[FacialTimelineReceiver\].*BakeAsset"));

                receiver.BeginPlaybackSession(profile, timeline);

                Assert.That(receiver.LastBakeInspectionStatus, Is.EqualTo(BakeInspectionStatus.MissingBakeAsset));
            }
            finally
            {
                DestroyReceiver(receiver);
                UnityEngine.Object.DestroyImmediate(timeline);
            }
        }

        [Test]
        public void BeginPlaybackSession_WhenBakeHashMismatches_WarnsAndMarksStatus()
        {
            var receiver = CreateReceiver();
            var registry = new FakeInputSourceRegistry();
            var timeline = CreateTimeline();
            var profile = CreateProfile();
            var bake = ScriptableObject.CreateInstance<FacialTimelineBakeAsset>();

            try
            {
                bake.SourceHashHex = "deadbeef";
                bake.SampleRate = 60f;
                receiver.BakeAsset = bake;
                receiver.Configure(
                    profile,
                    registry,
                    Array.Empty<(string layer, TimelineExpressionStateSink sink)>(),
                    Array.Empty<(string sub, TimelineBakedValueSink sink)>(),
                    Array.Empty<(string sub, TimelineAnalogInputSource sink)>(),
                    Array.Empty<(string sub, TimelineGazeInputSource sink, string takeoverSourceId)>());

                LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(@"\[FacialTimelineReceiver\].*hash mismatch"));

                receiver.BeginPlaybackSession(profile, timeline);

                Assert.That(receiver.LastBakeInspectionStatus, Is.EqualTo(BakeInspectionStatus.HashMismatch));
            }
            finally
            {
                DestroyReceiver(receiver);
                UnityEngine.Object.DestroyImmediate(timeline);
                UnityEngine.Object.DestroyImmediate(bake);
            }
        }

        [Test]
        public void BeginPlaybackSession_WhenBakeHashMismatches_RaisesInspectionIssue()
        {
            var receiver = CreateReceiver();
            var registry = new FakeInputSourceRegistry();
            var timeline = CreateTimeline();
            var profile = CreateProfile();
            var profileAsset = ScriptableObject.CreateInstance<FacialCharacterProfileSO>();
            var controller = receiver.gameObject.AddComponent<FacialController>();
            var bake = ScriptableObject.CreateInstance<FacialTimelineBakeAsset>();
            BakeInspectionIssue? captured = null;

            try
            {
                controller.CharacterSO = profileAsset;
                bake.SourceHashHex = "deadbeef";
                bake.SampleRate = 60f;
                receiver.BakeAsset = bake;
                receiver.Configure(
                    profile,
                    registry,
                    Array.Empty<(string layer, TimelineExpressionStateSink sink)>(),
                    Array.Empty<(string sub, TimelineBakedValueSink sink)>(),
                    Array.Empty<(string sub, TimelineAnalogInputSource sink)>(),
                    Array.Empty<(string sub, TimelineGazeInputSource sink, string takeoverSourceId)>());

                FacialTimelineReceiver.BakeIssueDetected += Capture;
                LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(@"\[FacialTimelineReceiver\].*hash mismatch"));

                receiver.BeginPlaybackSession(profile, timeline);

                Assert.That(captured.HasValue, Is.True);
                Assert.That(captured.Value.Timeline, Is.SameAs(timeline));
                Assert.That(captured.Value.ProfileSource, Is.SameAs(profileAsset));
                Assert.That(captured.Value.Status, Is.EqualTo(BakeInspectionStatus.HashMismatch));
            }
            finally
            {
                FacialTimelineReceiver.BakeIssueDetected -= Capture;
                DestroyReceiver(receiver);
                UnityEngine.Object.DestroyImmediate(timeline);
                UnityEngine.Object.DestroyImmediate(bake);
                UnityEngine.Object.DestroyImmediate(profileAsset);
            }

            void Capture(BakeInspectionIssue issue)
            {
                captured = issue;
            }
        }

        [Test]
        public void BeginPlaybackSession_WithResolvableGazeTakeover_ReplacesAndReleaseRestores()
        {
            var receiver = CreateReceiver();
            var registry = new FakeInputSourceRegistry();
            var liveSource = new FakeInputSource("live:gaze");
            var timelineGaze = new TimelineGazeInputSource(InputSourceId.Parse("timeline:gaze-0"));

            registry.Register(AdapterSlug.Parse("live"), "gaze", liveSource);
            receiver.Configure(
                CreateProfile(),
                registry,
                Array.Empty<(string layer, TimelineExpressionStateSink sink)>(),
                Array.Empty<(string sub, TimelineBakedValueSink sink)>(),
                Array.Empty<(string sub, TimelineAnalogInputSource sink)>(),
                new[] { ("gaze-main", timelineGaze, "live:gaze") });

            try
            {
                receiver.BeginPlaybackSession(CreateProfile(), timeline: null);

                Assert.That(registry.TryResolve("live:gaze", out var replaced), Is.True);
                Assert.That(replaced, Is.SameAs(timelineGaze));
                Assert.That(timelineGaze.ReplacedSource, Is.SameAs(liveSource));

                receiver.ReleaseAll();

                Assert.That(registry.TryResolve("live:gaze", out var restored), Is.True);
                Assert.That(restored, Is.SameAs(liveSource));
                Assert.That(timelineGaze.ReplacedSource, Is.Null);
            }
            finally
            {
                DestroyReceiver(receiver);
            }
        }

        [Test]
        public void BeginPlaybackSession_WithRealRegistry_RebindsGazeConsumerAndReleaseRestoresOriginalSource()
        {
            var receiver = CreateReceiver();
            var registry = new InputSourceRegistry();
            var liveSource = new TimelineAnalogInputSource(InputSourceId.Parse("live:gaze"), axisCount: 2);
            var timelineGaze = new TimelineGazeInputSource(InputSourceId.Parse("timeline:gaze-0"));
            var config = new GazeChannel
            {
                id = "gaze",
                useDistinctLeftRight = true,
                sourceIdLeft = "live:gaze",
                sourceIdRight = "live:gaze",
            };

            registry.Register(AdapterSlug.Parse("live"), "gaze", liveSource);
            receiver.Configure(
                CreateProfile(),
                registry,
                Array.Empty<(string layer, TimelineExpressionStateSink sink)>(),
                Array.Empty<(string sub, TimelineBakedValueSink sink)>(),
                Array.Empty<(string sub, TimelineAnalogInputSource sink)>(),
                new[] { ("gaze-main", timelineGaze, "live:gaze") });

            try
            {
                Assert.That(
                    GazeChannelResolver.TryResolve(config, registry, out ResolvedGazeInputSources beforeResolved),
                    Is.True);
                Assert.That(beforeResolved.LeftSource, Is.SameAs(liveSource));

                receiver.BeginPlaybackSession(CreateProfile(), timeline: null);

                Assert.That(
                    GazeChannelResolver.TryResolve(config, registry, out ResolvedGazeInputSources duringResolved),
                    Is.True);
                Assert.That(duringResolved.LeftSource, Is.SameAs(timelineGaze));
                Assert.That(duringResolved.RightSource, Is.SameAs(timelineGaze));
                Assert.That(timelineGaze.ReplacedSource, Is.SameAs(liveSource));

                receiver.ReleaseAll();

                Assert.That(
                    GazeChannelResolver.TryResolve(config, registry, out ResolvedGazeInputSources afterResolved),
                    Is.True);
                Assert.That(afterResolved.LeftSource, Is.SameAs(liveSource));
                Assert.That(afterResolved.RightSource, Is.SameAs(liveSource));
                Assert.That(timelineGaze.ReplacedSource, Is.Null);
            }
            finally
            {
                DestroyReceiver(receiver);
            }
        }

        [Test]
        public void BeginPlaybackSession_WhenTakeoverSourceAlreadyInjected_WarnsAndSkipsReplacement()
        {
            var receiver = CreateReceiver();
            var registry = new FakeInputSourceRegistry();
            var occupiedSource = new TimelineGazeInputSource(InputSourceId.Parse("timeline:gaze-occupied"));
            occupiedSource.AttachReplacement(new FakeInputSource("live:original"));

            registry.Register(AdapterSlug.Parse("live"), "gaze", occupiedSource);
            receiver.Configure(
                CreateProfile(),
                registry,
                Array.Empty<(string layer, TimelineExpressionStateSink sink)>(),
                Array.Empty<(string sub, TimelineBakedValueSink sink)>(),
                Array.Empty<(string sub, TimelineAnalogInputSource sink)>(),
                new[] { ("gaze-main", new TimelineGazeInputSource(InputSourceId.Parse("timeline:gaze-0")), "live:gaze") });

            try
            {
                LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(@"\[FacialTimelineReceiver\].*'live:gaze'"));

                receiver.BeginPlaybackSession(CreateProfile(), timeline: null);

                Assert.That(registry.TryResolve("live:gaze", out var current), Is.True);
                Assert.That(current, Is.SameAs(occupiedSource));
            }
            finally
            {
                DestroyReceiver(receiver);
            }
        }

        [Test]
        public void ReleaseAll_WhenTakeoverOwnershipChanged_WarnsAndKeepsCurrentOccupant()
        {
            var receiver = CreateReceiver();
            var registry = new FakeInputSourceRegistry();
            var liveSource = new FakeInputSource("live:gaze");
            var timelineGaze = new TimelineGazeInputSource(InputSourceId.Parse("timeline:gaze-0"));
            var otherOwner = new FakeInputSource("other:gaze");

            registry.Register(AdapterSlug.Parse("live"), "gaze", liveSource);
            receiver.Configure(
                CreateProfile(),
                registry,
                Array.Empty<(string layer, TimelineExpressionStateSink sink)>(),
                Array.Empty<(string sub, TimelineBakedValueSink sink)>(),
                Array.Empty<(string sub, TimelineAnalogInputSource sink)>(),
                new[] { ("gaze-main", timelineGaze, "live:gaze") });

            try
            {
                receiver.BeginPlaybackSession(CreateProfile(), timeline: null);
                registry.Replace(AdapterSlug.Parse("live"), "gaze", otherOwner);

                LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(@"\[FacialTimelineReceiver\].*'live:gaze'"));

                receiver.ReleaseAll();

                Assert.That(registry.TryResolve("live:gaze", out var current), Is.True);
                Assert.That(current, Is.SameAs(otherOwner));
                Assert.That(timelineGaze.ReplacedSource, Is.Null);
            }
            finally
            {
                DestroyReceiver(receiver);
            }
        }

        [Test]
        public void ReleaseAll_WhenTakeoverSourceMissing_WarnsAndClearsInjectedOwnership()
        {
            var receiver = CreateReceiver();
            var registry = new FakeInputSourceRegistry();
            var liveSource = new FakeInputSource("live:gaze");
            var timelineGaze = new TimelineGazeInputSource(InputSourceId.Parse("timeline:gaze-0"));

            registry.Register(AdapterSlug.Parse("live"), "gaze", liveSource);
            receiver.Configure(
                CreateProfile(),
                registry,
                Array.Empty<(string layer, TimelineExpressionStateSink sink)>(),
                Array.Empty<(string sub, TimelineBakedValueSink sink)>(),
                Array.Empty<(string sub, TimelineAnalogInputSource sink)>(),
                new[] { ("gaze-main", timelineGaze, "live:gaze") });

            try
            {
                receiver.BeginPlaybackSession(CreateProfile(), timeline: null);
                registry.Unregister(AdapterSlug.Parse("live"), "gaze");

                LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(@"\[FacialTimelineReceiver\].*'live:gaze'"));

                receiver.ReleaseAll();

                Assert.That(registry.TryResolve("live:gaze", out _), Is.False);
                Assert.That(timelineGaze.ReplacedSource, Is.Null);
            }
            finally
            {
                DestroyReceiver(receiver);
            }
        }

        [Test]
        public void ReleaseAll_ClearsStateAndInvalidatesValueAndAnalogSinks()
        {
            var receiver = CreateReceiver();
            var registry = new FakeInputSourceRegistry();
            var expressionSink = new TimelineExpressionStateSink(
                InputSourceId.Parse("timeline:emotion"),
                maxStackDepth: 4,
                exclusionMode: ExclusionMode.LastWins,
                CreateProfile());
            var valueSink = new TimelineBakedValueSink(
                InputSourceId.Parse("timeline:value"),
                new[] { "Smile", "Blink" },
                new[] { "Smile" });
            var analogSink = new TimelineAnalogInputSource(InputSourceId.Parse("timeline:analog"), axisCount: 2);
            var gazeSink = new TimelineGazeInputSource(InputSourceId.Parse("timeline:gaze-0"));

            receiver.Configure(
                CreateProfile(),
                registry,
                new[] { ("emotion", expressionSink) },
                new[] { ("emotion-value", valueSink) },
                new[] { ("analog-main", analogSink) },
                new[] { ("gaze-main", gazeSink, string.Empty) });

            try
            {
                expressionSink.TriggerOn("smile");
                valueSink.SetValues(new[] { 0.5f });
                analogSink.SetAxes(new[] { 0.2f, -0.3f });
                gazeSink.Publish(0.25f, -0.5f);

                receiver.ReleaseAll();

                Assert.That(expressionSink.ActiveExpressionIds, Is.Empty);
                Assert.That(valueSink.IsValid, Is.False);
                Assert.That(analogSink.IsValid, Is.False);
                Assert.That(gazeSink.IsValid, Is.False);
            }
            finally
            {
                DestroyReceiver(receiver);
            }
        }

        [Test]
        public void Configure_BuildsLookupMaps()
        {
            var receiver = CreateReceiver();
            var registry = new FakeInputSourceRegistry();
            var expressionSink = new TimelineExpressionStateSink(
                InputSourceId.Parse("timeline:emotion"),
                maxStackDepth: 4,
                exclusionMode: ExclusionMode.LastWins,
                CreateProfile());
            var valueSink = new TimelineBakedValueSink(
                InputSourceId.Parse("timeline:value"),
                new[] { "Smile" },
                new[] { "Smile" });
            var analogSink = new TimelineAnalogInputSource(InputSourceId.Parse("timeline:analog"), axisCount: 1);
            var gazeSink = new TimelineGazeInputSource(InputSourceId.Parse("timeline:gaze-0"));

            try
            {
                receiver.Configure(
                    CreateProfile(),
                    registry,
                    new[] { ("emotion", expressionSink) },
                    new[] { ("value-main", valueSink) },
                    new[] { ("analog-main", analogSink) },
                    new[] { ("gaze-main", gazeSink, string.Empty) });

                Assert.That(receiver.TryGetExpressionSink("emotion", out var resolvedExpression), Is.True);
                Assert.That(receiver.TryGetExpressionValueSink("value-main", out var resolvedValue), Is.True);
                Assert.That(receiver.TryGetAnalogSink("analog-main", out var resolvedAnalog), Is.True);
                Assert.That(receiver.TryGetGazeSink("gaze-main", out var resolvedGaze), Is.True);
                Assert.That(resolvedExpression, Is.SameAs(expressionSink));
                Assert.That(resolvedValue, Is.SameAs(valueSink));
                Assert.That(resolvedAnalog, Is.SameAs(analogSink));
                Assert.That(resolvedGaze, Is.SameAs(gazeSink));
            }
            finally
            {
                DestroyReceiver(receiver);
            }
        }

        private static FacialTimelineReceiver CreateReceiver()
        {
            return new GameObject("ReceiverTest").AddComponent<FacialTimelineReceiver>();
        }

        private static void DestroyReceiver(FacialTimelineReceiver receiver)
        {
            if (receiver != null)
            {
                UnityEngine.Object.DestroyImmediate(receiver.gameObject);
            }
        }

        private static TimelineAsset CreateTimeline()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            var expressionTrack = timeline.CreateTrack<Tracks.FacialExpressionTrack>(null, "Expressions");
            TimelineClip expressionClip = expressionTrack.CreateClip<FacialExpressionClip>();
            expressionClip.start = 0.0d;
            expressionClip.duration = 1.0d;
            ((FacialExpressionClip)expressionClip.asset).ExpressionId = "smile";
            return timeline;
        }

        private static FacialProfile CreateProfile()
        {
            return new FacialProfile(
                schemaVersion: "1.0.0",
                layers: new[]
                {
                    new LayerDefinition("emotion", 0, ExclusionMode.LastWins),
                },
                expressions: new[]
                {
                    new Expression(
                        id: "smile",
                        name: "Smile",
                        layer: "emotion",
                        transitionDuration: 0.1f,
                        transitionCurve: TransitionCurve.Linear,
                        blendShapeValues: new[]
                        {
                            new BlendShapeMapping("Smile", 1.0f),
                        }),
                });
        }

        private sealed class FakeInputSource : IInputSource
        {
            public FakeInputSource(string id)
            {
                Id = id;
                ContributeMask = new System.Collections.BitArray(0);
            }

            public string Id { get; }

            public InputSourceType Type => InputSourceType.ValueProvider;

            public int BlendShapeCount => 0;

            public System.Collections.BitArray ContributeMask { get; }

            public void Tick(float deltaTime)
            {
            }

            public bool TryWriteValues(Span<float> output)
            {
                return false;
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
                ReplaceInternal(slug.Value, source);
            }

            public void Register(AdapterSlug slug, string sub, IInputSource source)
            {
                RegisterInternal(Compose(slug, sub), source);
            }

            public void Replace(AdapterSlug slug, string sub, IInputSource source)
            {
                ReplaceInternal(Compose(slug, sub), source);
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

            private void ReplaceInternal(string id, IInputSource source)
            {
                RegisterInternal(id, source);
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
