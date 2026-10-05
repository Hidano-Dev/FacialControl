using System;
using System.Collections;
using System.Collections.Generic;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Testing;
using Hidano.FacialControl.Timeline.Adapters.InputSources;
using Hidano.FacialControl.Timeline.Adapters.Session;
using Hidano.FacialControl.Timeline.Domain.Diagnostics;
using Hidano.FacialControl.Timeline.Domain.Models;
using Hidano.FacialControl.Timeline.Tracks;
using NUnit.Framework;

namespace Hidano.FacialControl.Timeline.Tests.EditMode
{
    /// <summary>
    /// <see cref="TimelineChannelTakeover"/> の Replace 乗っ取りと復元を Fake registry で検証する。
    /// </summary>
    [TestFixture]
    [SmallTest]
    public sealed class TimelineChannelTakeoverTests : SizedTestFixture
    {
        private const string AnalogId = "osc:lt";
        private const string GazeId = "osc:gaze";

        private FakeRegistry _registry;
        private TimelineChannelTakeover _takeover;
        private FacialTimelineDiagnostics _diagnostics;

        [SetUp]
        public void SetUp()
        {
            _registry = new FakeRegistry();
            _takeover = new TimelineChannelTakeover(_registry, AdapterSlug.Parse("timeline"));
            _diagnostics = new FacialTimelineDiagnostics();
        }

        // ================================================================
        // Attach
        // ================================================================

        [Test]
        public void Attach_RegisteredAnalogSource_ReplacesWithTimelineSinkAndRecordsAttached()
        {
            var original = new FakeScalarSource("original", 0.2f);
            _registry.Register(AdapterSlug.Parse("osc"), "lt", original);

            _takeover.Attach(new[] { Analog(AnalogId, 1) }, _diagnostics);

            Assert.That(_takeover.TryGetAnalogSink(AnalogId, out TimelineAnalogInputSource sink), Is.True);
            Assert.That(_registry.Resolve(AnalogId), Is.SameAs(sink), "registry の ChannelSubId エントリが Timeline sink に置き換わる");
            Assert.That(sink.ReplacedSource, Is.SameAs(original), "原本を退避している");
            Assert.That(sink.AxisCount, Is.EqualTo(1));
            Assert.That(_diagnostics.Contains(TimelineDiagnosticCode.AnalogTakeoverAttached, AnalogId), Is.True);
            Assert.That(SeverityOf(TimelineDiagnosticCode.AnalogTakeoverAttached), Is.EqualTo(TimelineDiagnosticSeverity.Info));
            Assert.That(_takeover.Entries, Has.Count.EqualTo(1));
            Assert.That(_takeover.Entries[0].ChannelSubId, Is.EqualTo(AnalogId));
            Assert.That(_takeover.Entries[0].Kind, Is.EqualTo(FacialValueChannelKind.Analog));
            Assert.That(_takeover.Entries[0].IsAttached, Is.True);
            Assert.That(_takeover.Entries[0].Status, Is.EqualTo(TimelineDiagnosticCode.AnalogTakeoverAttached));
        }

        [Test]
        public void Attach_RegisteredGazeSource_ReplacesWithGazeSinkAndRecordsAttached()
        {
            var original = new FakeScalarSource("original", 0f);
            _registry.Register(AdapterSlug.Parse("osc"), "gaze", original);

            _takeover.Attach(new[] { Gaze(GazeId, 2) }, _diagnostics);

            Assert.That(_takeover.TryGetGazeSink(GazeId, out TimelineGazeInputSource sink), Is.True);
            Assert.That(_registry.Resolve(GazeId), Is.SameAs(sink));
            Assert.That(sink.ReplacedSource, Is.SameAs(original));
            Assert.That(_diagnostics.Contains(TimelineDiagnosticCode.GazeTakeoverAttached, GazeId), Is.True);
            Assert.That(_takeover.TryGetAnalogSink(GazeId, out _), Is.False, "Gaze チャネルは Analog として引けない");
        }

        [Test]
        public void Attach_GazeChannelWithOneAxis_StillUsesTwoAxisSink()
        {
            _registry.Register(AdapterSlug.Parse("osc"), "gaze", new FakeScalarSource("original", 0f));

            _takeover.Attach(new[] { Gaze(GazeId, 1) }, _diagnostics);

            Assert.That(_takeover.TryGetGazeSink(GazeId, out TimelineGazeInputSource sink), Is.True);
            Assert.That(sink.AxisCount, Is.EqualTo(2), "Gaze は 2 軸前提");
        }

        [Test]
        public void Attach_ChannelSubIdIsUsedAsIs()
        {
            const string explicitLeft = "osc:gaze.left";
            var original = new FakeScalarSource("left", 0f);
            _registry.Register(AdapterSlug.Parse("osc"), "gaze.left", original);
            int registeredBefore = _registry.RegisteredIds.Count;

            _takeover.Attach(new[] { Gaze(explicitLeft, 2) }, _diagnostics);

            Assert.That(_registry.ReplacedKeys, Is.EqualTo(new[] { explicitLeft }),
                "ChannelSubId を最初の ':' で slug / sub に分けてそのまま Replace する");
            Assert.That(_registry.RegisteredIds.Count, Is.EqualTo(registeredBefore), "独自 id で別途登録しない");
            Assert.That(_takeover.TryGetGazeSink(explicitLeft, out _), Is.True);
        }

        [Test]
        public void Attach_SourceNotRegistered_RecordsNotFoundWarningAndRegistersNothing()
        {
            _takeover.Attach(new[] { Analog(AnalogId, 1), Gaze(GazeId, 2) }, _diagnostics);

            Assert.That(_diagnostics.Contains(TimelineDiagnosticCode.AnalogSourceNotFound, AnalogId), Is.True);
            Assert.That(_diagnostics.Contains(TimelineDiagnosticCode.GazeSourceNotFound, GazeId), Is.True);
            Assert.That(SeverityOf(TimelineDiagnosticCode.AnalogSourceNotFound), Is.EqualTo(TimelineDiagnosticSeverity.Warning));
            Assert.That(SeverityOf(TimelineDiagnosticCode.GazeSourceNotFound), Is.EqualTo(TimelineDiagnosticSeverity.Warning));
            Assert.That(_registry.RegisteredIds, Is.Empty, "未登録の id を新規登録しない");
            Assert.That(_takeover.TryGetAnalogSink(AnalogId, out _), Is.False);
            Assert.That(_takeover.Entries[0].IsAttached, Is.False);
            Assert.That(_takeover.Entries[0].Status, Is.EqualTo(TimelineDiagnosticCode.AnalogSourceNotFound));
        }

        [Test]
        public void Attach_ExistingEntryIsInjected_SkipsWithOccupiedWarning()
        {
            var occupant = new FakeInjectedSource("rec");
            _registry.Register(AdapterSlug.Parse("osc"), "lt", occupant);
            _registry.Register(AdapterSlug.Parse("osc"), "gaze", new FakeInjectedSource("rec-gaze"));

            _takeover.Attach(new[] { Analog(AnalogId, 1), Gaze(GazeId, 2) }, _diagnostics);

            Assert.That(_registry.Resolve(AnalogId), Is.SameAs(occupant), "他者占有は触らない");
            Assert.That(_diagnostics.Contains(TimelineDiagnosticCode.AnalogOccupied, AnalogId), Is.True);
            Assert.That(_diagnostics.Contains(TimelineDiagnosticCode.GazeOccupied, GazeId), Is.True);
            Assert.That(SeverityOf(TimelineDiagnosticCode.AnalogOccupied), Is.EqualTo(TimelineDiagnosticSeverity.Warning));
            Assert.That(_takeover.TryGetAnalogSink(AnalogId, out _), Is.False);
        }

        [Test]
        public void Attach_AnalogAxisCountZero_RecordsAxisCountInvalid()
        {
            _registry.Register(AdapterSlug.Parse("osc"), "lt", new FakeScalarSource("original", 0.2f));

            _takeover.Attach(new[] { Analog(AnalogId, 0) }, _diagnostics);

            Assert.That(_diagnostics.Contains(TimelineDiagnosticCode.AnalogAxisCountInvalid, AnalogId), Is.True);
            Assert.That(_registry.ReplacedKeys, Is.Empty);
        }

        [Test]
        public void Attach_PartialFailure_OtherChannelsContinue()
        {
            _registry.Register(AdapterSlug.Parse("osc"), "gaze", new FakeScalarSource("gaze", 0f));

            _takeover.Attach(new[] { Analog(AnalogId, 1), Gaze(GazeId, 2) }, _diagnostics);

            Assert.That(_diagnostics.Contains(TimelineDiagnosticCode.AnalogSourceNotFound, AnalogId), Is.True);
            Assert.That(_diagnostics.Contains(TimelineDiagnosticCode.GazeTakeoverAttached, GazeId), Is.True);
        }

        // ================================================================
        // Release
        // ================================================================

        [Test]
        public void Release_RestoresOriginalByReplace()
        {
            var original = new FakeScalarSource("original", 0.2f);
            _registry.Register(AdapterSlug.Parse("osc"), "lt", original);
            _takeover.Attach(new[] { Analog(AnalogId, 1) }, _diagnostics);
            _takeover.TryGetAnalogSink(AnalogId, out TimelineAnalogInputSource sink);

            _takeover.Release();

            Assert.That(_registry.Resolve(AnalogId), Is.SameAs(original));
            Assert.That(sink.ReplacedSource, Is.Null);
            Assert.That(sink.IsValid, Is.False, "解放した sink は無効化される");
            Assert.That(_takeover.Entries, Is.Empty);
            Assert.That(_takeover.TryGetAnalogSink(AnalogId, out _), Is.False);
            Assert.DoesNotThrow(() => _takeover.Release(), "二重解放は no-op");
        }

        [Test]
        public void Release_EntryReplacedByOther_LeavesOtherOccupantUntouched()
        {
            var original = new FakeScalarSource("original", 0.2f);
            _registry.Register(AdapterSlug.Parse("osc"), "lt", original);
            _takeover.Attach(new[] { Analog(AnalogId, 1) }, _diagnostics);
            var laterOccupant = new FakeInjectedSource("rec");
            _registry.Replace(AdapterSlug.Parse("osc"), "lt", laterOccupant);

            _takeover.Release();

            Assert.That(_registry.Resolve(AnalogId), Is.SameAs(laterOccupant), "参照同一でなければ復元しない");
        }

        [Test]
        public void Attach_Twice_ReleasesPreviousTakeoverFirst()
        {
            var original = new FakeScalarSource("original", 0.2f);
            _registry.Register(AdapterSlug.Parse("osc"), "lt", original);
            _takeover.Attach(new[] { Analog(AnalogId, 1) }, _diagnostics);

            _takeover.Attach(new[] { Analog(AnalogId, 1) }, _diagnostics);

            Assert.That(_takeover.TryGetAnalogSink(AnalogId, out TimelineAnalogInputSource sink), Is.True);
            Assert.That(sink.ReplacedSource, Is.SameAs(original), "自分の sink を原本として退避しない");
            Assert.That(_diagnostics.Contains(TimelineDiagnosticCode.AnalogOccupied), Is.False);

            _takeover.Release();
            Assert.That(_registry.Resolve(AnalogId), Is.SameAs(original));
        }

        // ================================================================
        // 消費者への到達（registry 購読型の Analog Expression 消費者）
        // ================================================================

        [Test]
        public void Attach_ConsumerAttachedToRegistry_WritesClipValueAndReleaseRestoresOriginal()
        {
            var slug = AdapterSlug.Parse("osc");
            var original = new FakeScalarSource("original", 0.2f);
            _registry.Register(slug, "lt", original);
            AnalogExpressionInputSource consumer = BuildConsumer(original);
            consumer.AttachRegistry(_registry, slug);
            AssertConsumerOutput(consumer, 0.2f);

            _takeover.Attach(new[] { Analog(AnalogId, 1) }, _diagnostics);
            Assert.That(_takeover.TryGetAnalogSink(AnalogId, out TimelineAnalogInputSource sink), Is.True);
            sink.SetAxes(new[] { 0.8f });

            AssertConsumerOutput(consumer, 0.8f);

            _takeover.Release();

            AssertConsumerOutput(consumer, 0.2f);
        }

        // ================================================================
        // ヘルパー
        // ================================================================

        private static TimelineChannelDescriptor Analog(string id, int axisCount)
        {
            return new TimelineChannelDescriptor(id, FacialValueChannelKind.Analog, axisCount, trackIndex: 0);
        }

        private static TimelineChannelDescriptor Gaze(string id, int axisCount)
        {
            return new TimelineChannelDescriptor(id, FacialValueChannelKind.Gaze, axisCount, trackIndex: 1);
        }

        private TimelineDiagnosticSeverity SeverityOf(TimelineDiagnosticCode code)
        {
            foreach (TimelineDiagnosticItem item in _diagnostics.Items)
            {
                if (item.Code == code)
                {
                    return item.Severity;
                }
            }

            Assert.Fail($"診断 {code} が記録されていない");
            return default;
        }

        private static readonly string[] ConsumerBlendShapes = { "squint", "other" };

        private static AnalogExpressionInputSource BuildConsumer(IAnalogInputSource source)
        {
            var layers = new[] { new LayerDefinition("emotion", 0, ExclusionMode.LastWins) };
            var squint = new Expression(
                id: "squint",
                name: "Squint",
                layer: "emotion",
                blendShapeValues: new[] { new BlendShapeMapping("squint", 1f) });
            var profile = new FacialProfile("1.0", layers, new[] { squint });
            var sources = new Dictionary<string, IAnalogInputSource>(StringComparer.Ordinal) { { "lt", source } };
            return new AnalogExpressionInputSource(
                InputSourceId.Parse(AnalogExpressionInputSource.ReservedId),
                ConsumerBlendShapes.Length,
                ConsumerBlendShapes,
                profile,
                sources,
                new[] { new AnalogExpressionBinding("lt", 0, "squint", 1f) });
        }

        private static void AssertConsumerOutput(AnalogExpressionInputSource consumer, float expected)
        {
            Span<float> output = stackalloc float[ConsumerBlendShapes.Length];
            output.Clear();
            Assert.That(consumer.TryWriteValues(output), Is.True);
            Assert.That(output[0], Is.EqualTo(expected).Within(1e-6f));
            Assert.That(output[1], Is.EqualTo(0f).Within(1e-6f));
        }

        private sealed class FakeScalarSource : IInputSource, IAnalogInputSource
        {
            public FakeScalarSource(string id, float value)
            {
                Id = id;
                Value = value;
            }

            public string Id { get; }
            public float Value { get; }
            public bool IsValid => true;
            public int AxisCount => 1;
            public InputSourceType Type => InputSourceType.ValueProvider;
            public int BlendShapeCount => 0;
            public BitArray ContributeMask { get; } = new BitArray(0);
            public void Tick(float deltaTime) { }
            public bool TryWriteValues(Span<float> output) => false;

            public bool TryReadScalar(out float value)
            {
                value = Value;
                return true;
            }

            public bool TryReadVector2(out float x, out float y)
            {
                x = Value;
                y = 0f;
                return false;
            }

            public bool TryReadAxes(Span<float> output)
            {
                if (output.Length >= 1) output[0] = Value;
                return output.Length >= 1;
            }
        }

        private sealed class FakeInjectedSource : IInputSource, IInjectedInputSource
        {
            public FakeInjectedSource(string id)
            {
                Id = id;
            }

            public string Id { get; }
            public IInputSource ReplacedSource => null;
            public InputSourceType Type => InputSourceType.ValueProvider;
            public int BlendShapeCount => 0;
            public BitArray ContributeMask { get; } = new BitArray(0);
            public void Tick(float deltaTime) { }
            public bool TryWriteValues(Span<float> output) => false;
        }

        /// <summary>実 registry と同じ通知契約（Register / Replace は新 source、Unregister は null）を持つ最小 registry。</summary>
        private sealed class FakeRegistry : IInputSourceRegistry
        {
            private readonly Dictionary<string, IInputSource> _entries =
                new Dictionary<string, IInputSource>(StringComparer.Ordinal);
            private readonly Dictionary<string, List<Action<IInputSource>>> _handlers =
                new Dictionary<string, List<Action<IInputSource>>>(StringComparer.Ordinal);
            private readonly List<string> _ids = new List<string>();

            /// <summary>Timeline 側の sink（Fake 以外）で Replace されたキー。</summary>
            public List<string> ReplacedKeys { get; } = new List<string>();

            public IReadOnlyList<string> RegisteredIds => _ids;

            public IInputSource Resolve(string id)
            {
                return _entries.TryGetValue(id, out IInputSource source) ? source : null;
            }

            public void Register(AdapterSlug slug, IInputSource source) => Set(slug.Value, source, replace: false);

            public void Replace(AdapterSlug slug, IInputSource source) => Set(slug.Value, source, replace: true);

            public void Register(AdapterSlug slug, string sub, IInputSource source) => Set(slug.Value + ":" + sub, source, replace: false);

            public void Replace(AdapterSlug slug, string sub, IInputSource source) => Set(slug.Value + ":" + sub, source, replace: true);

            public void Unregister(AdapterSlug slug) => Remove(slug.Value);

            public void Unregister(AdapterSlug slug, string sub) => Remove(slug.Value + ":" + sub);

            public bool TryResolve(string layerInputSourceId, out IInputSource source)
            {
                if (string.IsNullOrEmpty(layerInputSourceId))
                {
                    source = null;
                    return false;
                }

                return _entries.TryGetValue(layerInputSourceId, out source);
            }

            public void Subscribe(string id, Action<IInputSource> handler)
            {
                if (string.IsNullOrEmpty(id) || handler == null)
                {
                    return;
                }

                if (!_handlers.TryGetValue(id, out List<Action<IInputSource>> list))
                {
                    list = new List<Action<IInputSource>>();
                    _handlers[id] = list;
                }

                list.Add(handler);
            }

            private void Set(string key, IInputSource source, bool replace)
            {
                if (source == null) throw new ArgumentNullException(nameof(source));
                if (replace && !(source is FakeInjectedSource) && !(source is FakeScalarSource))
                {
                    ReplacedKeys.Add(key);
                }

                if (!_entries.ContainsKey(key)) _ids.Add(key);
                _entries[key] = source;
                Notify(key, source);
            }

            private void Remove(string key)
            {
                if (!_entries.Remove(key)) return;
                _ids.Remove(key);
                Notify(key, null);
            }

            private void Notify(string key, IInputSource source)
            {
                if (!_handlers.TryGetValue(key, out List<Action<IInputSource>> list)) return;
                for (int i = 0; i < list.Count; i++)
                {
                    list[i](source);
                }
            }
        }
    }
}
