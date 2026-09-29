using System;
using System.Collections;
using System.Text.RegularExpressions;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Rec.Adapters.Playback;
using Hidano.FacialControl.Rec.Domain.Models;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Rec.Tests.EditMode
{
    [TestFixture]
    [SmallTest]
    public class RecAnalogInjectorTests : SizedTestFixture
    {
        // ログ文言の完全一致は避け、文言改善で変わらない sourceId のみ照合する。
        private static readonly Regex RegisteredLogPattern =
            new Regex("analog.*'input:gaze'", RegexOptions.CultureInvariant);

        private static readonly Regex OccupiedLogPattern =
            new Regex("analog.*'input:analog'", RegexOptions.CultureInvariant);

        private static readonly Regex RestoreMismatchLogPattern =
            new Regex("analog.*'input:analog'", RegexOptions.CultureInvariant);

        [Test]
        public void BeginInjection_WithRegisteredOriginal_ReplacesSourceAndSeedsBaselineAxes()
        {
            var registry = new InputSourceRegistry();
            IInputSource original = new StubInputSource("live:analog");
            registry.Register(AdapterSlug.Parse("input"), "analog", original);
            var injector = new RecAnalogInjector(registry);

            injector.BeginInjection(CreateBaseline("input:analog", 0.25f, -0.5f));

            Assert.That(registry.TryResolve("input:analog", out IInputSource resolved), Is.True);
            Assert.That(resolved, Is.TypeOf<RecPlaybackAnalogSource>());
            var playbackSource = (RecPlaybackAnalogSource)resolved;
            Assert.That(playbackSource.ReplacedSource, Is.SameAs(original));
            Assert.That(playbackSource.TryReadVector2(out float x, out float y), Is.True);
            Assert.That(x, Is.EqualTo(0.25f));
            Assert.That(y, Is.EqualTo(-0.5f));
        }

        [Test]
        public void BeginInjection_WithRegisteredAnalogOutsideBaseline_ReplacesSourceAndSeedsZeroAxes()
        {
            var registry = new InputSourceRegistry();
            var original = new StubAnalogInputSource("input:gaze", 2, 0.9f, -0.4f);
            registry.Register(AdapterSlug.Parse("input"), "gaze", original);
            var injector = new RecAnalogInjector(registry);

            injector.BeginInjection(RecBaselineState.Empty);

            Assert.That(registry.TryResolve("input:gaze", out IInputSource resolved), Is.True);
            Assert.That(resolved, Is.TypeOf<RecPlaybackAnalogSource>());
            var playbackSource = (RecPlaybackAnalogSource)resolved;
            Assert.That(playbackSource.ReplacedSource, Is.SameAs(original));
            Assert.That(playbackSource.TryReadVector2(out float x, out float y), Is.True);
            Assert.That(x, Is.Zero);
            Assert.That(y, Is.Zero);
        }

        [Test]
        public void BeginInjection_WhenOriginalIsMissing_RegistersSourceAndLogsInfo()
        {
            LogAssert.Expect(LogType.Log, RegisteredLogPattern);
            var registry = new InputSourceRegistry();
            var injector = new RecAnalogInjector(registry);

            injector.BeginInjection(CreateBaseline("input:gaze", -1f, 0.75f));

            Assert.That(registry.TryResolve("input:gaze", out IInputSource resolved), Is.True);
            Assert.That(resolved, Is.TypeOf<RecPlaybackAnalogSource>());
            var playbackSource = (RecPlaybackAnalogSource)resolved;
            Assert.That(playbackSource.ReplacedSource, Is.Null);
            Assert.That(playbackSource.TryReadVector2(out float x, out float y), Is.True);
            Assert.That(x, Is.EqualTo(-1f));
            Assert.That(y, Is.EqualTo(0.75f));
        }

        [Test]
        public void BeginInjection_WithBaselineAndRegisteredSource_PrefersBaselineSeed()
        {
            var registry = new InputSourceRegistry();
            var original = new StubAnalogInputSource("input:gaze", 2, 0.9f, -0.4f);
            registry.Register(AdapterSlug.Parse("input"), "gaze", original);
            var injector = new RecAnalogInjector(registry);

            injector.BeginInjection(CreateBaseline("input:gaze", -1f, 0.75f));

            Assert.That(registry.TryResolve("input:gaze", out IInputSource resolved), Is.True);
            Assert.That(resolved, Is.TypeOf<RecPlaybackAnalogSource>());
            var playbackSource = (RecPlaybackAnalogSource)resolved;
            Assert.That(playbackSource.TryReadVector2(out float x, out float y), Is.True);
            Assert.That(x, Is.EqualTo(-1f));
            Assert.That(y, Is.EqualTo(0.75f));
        }

        [Test]
        public void BeginInjection_WhenCurrentEntryIsInjected_LogsWarningAndSkipsReplacement()
        {
            LogAssert.Expect(LogType.Warning, OccupiedLogPattern);
            var registry = new InputSourceRegistry();
            var occupied = new StubInjectedInputSource("occupied", new StubInputSource("original"));
            registry.Register(AdapterSlug.Parse("input"), "analog", occupied);
            var injector = new RecAnalogInjector(registry);

            injector.BeginInjection(CreateBaseline("input:analog", 0.5f));

            Assert.That(registry.TryResolve("input:analog", out IInputSource resolved), Is.True);
            Assert.That(resolved, Is.SameAs(occupied));
        }

        [Test]
        public void InjectAnalogSample_AfterBeginInjection_UpdatesPlaybackSourceAxes()
        {
            var registry = new InputSourceRegistry();
            registry.Register(AdapterSlug.Parse("input"), "analog", new StubInputSource("live:analog"));
            var injector = new RecAnalogInjector(registry);
            injector.BeginInjection(CreateBaseline("input:analog", 0f, 0f));

            injector.InjectAnalogSample("input:analog", new float[] { 0.8f, -0.2f });

            Assert.That(registry.TryResolve("input:analog", out IInputSource resolved), Is.True);
            var playbackSource = (RecPlaybackAnalogSource)resolved;
            Assert.That(playbackSource.TryReadVector2(out float x, out float y), Is.True);
            Assert.That(x, Is.EqualTo(0.8f));
            Assert.That(y, Is.EqualTo(-0.2f));
        }

        [Test]
        public void EndInjection_WithOriginal_RestoresOriginalSource()
        {
            var registry = new InputSourceRegistry();
            IInputSource original = new StubInputSource("live:analog");
            registry.Register(AdapterSlug.Parse("input"), "analog", original);
            var injector = new RecAnalogInjector(registry);
            injector.BeginInjection(CreateBaseline("input:analog", 0.5f));

            injector.EndInjection();

            Assert.That(registry.TryResolve("input:analog", out IInputSource resolved), Is.True);
            Assert.That(resolved, Is.SameAs(original));
        }

        [Test]
        public void EndInjection_WithoutOriginal_UnregistersInjectedSource()
        {
            LogAssert.Expect(LogType.Log, RegisteredLogPattern);
            var registry = new InputSourceRegistry();
            var injector = new RecAnalogInjector(registry);
            injector.BeginInjection(CreateBaseline("input:gaze", 0.1f, 0.2f));

            injector.EndInjection();

            Assert.That(registry.TryResolve("input:gaze", out _), Is.False);
        }

        [Test]
        public void EndInjection_WhenCurrentEntryIsNoLongerOwned_LogsWarningAndPreservesCurrentSource()
        {
            LogAssert.Expect(LogType.Warning, RestoreMismatchLogPattern);
            var registry = new InputSourceRegistry();
            registry.Register(AdapterSlug.Parse("input"), "analog", new StubInputSource("live:analog"));
            var injector = new RecAnalogInjector(registry);
            injector.BeginInjection(CreateBaseline("input:analog", 0.5f));
            Assert.That(registry.TryResolve("input:analog", out IInputSource playbackSource), Is.True);
            var competingSource = new StubInjectedInputSource("other", playbackSource);
            registry.Replace(AdapterSlug.Parse("input"), "analog", competingSource);

            injector.EndInjection();

            Assert.That(registry.TryResolve("input:analog", out IInputSource resolved), Is.True);
            Assert.That(resolved, Is.SameAs(competingSource));
        }

        private static RecBaselineState CreateBaseline(string sourceId, params float[] axes)
        {
            return new RecBaselineState(
                triggerEntries: null,
                analogEntries: new[]
                {
                    new RecBaselineState.AnalogEntry(sourceId, axes),
                });
        }

        private class StubInputSource : IInputSource
        {
            public StubInputSource(string id)
            {
                Id = id;
            }

            public string Id { get; }

            public InputSourceType Type => InputSourceType.ValueProvider;

            public int BlendShapeCount => 0;

            public BitArray ContributeMask { get; } = new BitArray(0);

            public void Tick(float deltaTime)
            {
            }

            public bool TryWriteValues(Span<float> output)
            {
                return false;
            }
        }

        private sealed class StubAnalogInputSource : StubInputSource, IAnalogInputSource
        {
            private readonly float[] _axes;

            public StubAnalogInputSource(string id, params float[] axes)
                : base(id)
            {
                _axes = axes ?? Array.Empty<float>();
            }

            public bool IsValid => true;

            public int AxisCount => _axes.Length;

            public bool TryReadScalar(out float value)
            {
                if (_axes.Length == 0)
                {
                    value = default;
                    return false;
                }

                value = _axes[0];
                return true;
            }

            public bool TryReadVector2(out float x, out float y)
            {
                if (_axes.Length < 2)
                {
                    x = default;
                    y = default;
                    return false;
                }

                x = _axes[0];
                y = _axes[1];
                return true;
            }

            public bool TryReadAxes(Span<float> output)
            {
                if (output.Length < _axes.Length)
                {
                    return false;
                }

                _axes.AsSpan().CopyTo(output);
                return true;
            }
        }

        private sealed class StubInjectedInputSource : StubInputSource, IInjectedInputSource
        {
            public StubInjectedInputSource(string id, IInputSource replacedSource)
                : base(id)
            {
                ReplacedSource = replacedSource;
            }

            public IInputSource ReplacedSource { get; }
        }
    }
}
