using System;
using System.Collections.Generic;
using System.Linq;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Rec.Domain.Interfaces;
using Hidano.FacialControl.Rec.Domain.Models;
using Hidano.FacialControl.Rec.Domain.Services;
using NUnit.Framework;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Rec.Tests.EditMode
{
    [TestFixture]
    [SmallTest]
    public class RecDomainContractsTests : SizedTestFixture
    {
        [Test]
        public void RecEvent_CreateAnalogSample_RejectsZeroAxisCount()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => RecEvent.CreateAnalogSample(0.5d, 1, 0));
        }

        [Test]
        public void RecEvent_CreateIdDefine_StoresSourceDefinition()
        {
            RecEvent evt = RecEvent.CreateIdDefine(3, RecEvent.IdDefinitionKind.Source);

            Assert.That(evt.Kind, Is.EqualTo(RecEventKind.IdDefine));
            Assert.That(evt.IdIndex, Is.EqualTo(3));
            Assert.That(evt.DefinedIdKind, Is.EqualTo(RecEvent.IdDefinitionKind.Source));
            Assert.That(evt.IsTimedEvent, Is.False);
        }

        [Test]
        public void RecBaselineState_CopiesInputCollections()
        {
            string[] expressions = { "smile", "angry" };
            float[] axes = { 0.25f, -0.5f };
            var baseline = new RecBaselineState(
                new[] { new RecBaselineState.TriggerEntry("input:trigger", expressions) },
                new[] { new RecBaselineState.AnalogEntry("input:gaze", axes) });

            expressions[0] = "changed";
            axes[0] = 1f;

            Assert.That(baseline.TryGetTriggerStack("input:trigger", out IReadOnlyList<string> storedExpressions), Is.True);
            Assert.That(storedExpressions.ToArray(), Is.EqualTo(new[] { "smile", "angry" }));
            Assert.That(baseline.TryGetAnalogAxes("input:gaze", out IReadOnlyList<float> storedAxes), Is.True);
            Assert.That(storedAxes.ToArray(), Is.EqualTo(new[] { 0.25f, -0.5f }));
        }

        [Test]
        public void RecTimeline_RejectsNonTimedEvents()
        {
            var baseline = RecBaselineState.Empty;
            RecEvent[] events =
            {
                RecEvent.CreateBaselineTrigger(0, 0),
            };

            Assert.Throws<ArgumentException>(() =>
                new RecTimeline(baseline, events, new[] { "input:trigger" }, new[] { "smile" }, 0d));
        }

        [Test]
        public void RecTimeline_RejectsOutOfOrderTimestamps()
        {
            RecEvent[] events =
            {
                RecEvent.CreateTriggerOn(0.5d, 0, 0),
                RecEvent.CreateTriggerOff(0.25d, 0, 0),
            };

            Assert.Throws<ArgumentException>(() =>
                new RecTimeline(RecBaselineState.Empty, events, new[] { "input:trigger" }, new[] { "smile" }, 1d));
        }

        [Test]
        public void RecTimeline_RejectsUnknownSourceIndex()
        {
            RecEvent[] events =
            {
                RecEvent.CreateAnalogSample(0.25d, 1, 2),
            };

            Assert.Throws<ArgumentException>(() =>
                new RecTimeline(RecBaselineState.Empty, events, new[] { "input:gaze" }, Array.Empty<string>(), 1d));
        }

        [Test]
        public void RecTimeline_StoresCopiedImmutableState()
        {
            var sourceIds = new[] { "input:trigger", "input:gaze" };
            var expressionIds = new[] { "smile" };
            RecEvent[] events =
            {
                RecEvent.CreateTriggerOn(0.25d, 0, 0),
                RecEvent.CreateAnalogSample(0.5d, 1, 2),
            };

            var timeline = new RecTimeline(
                RecBaselineState.Empty,
                events,
                sourceIds,
                expressionIds,
                0.5d,
                new IReadOnlyList<float>[]
                {
                    Array.Empty<float>(),
                    new float[] { 0.25f, -0.5f },
                });
            sourceIds[0] = "changed";
            expressionIds[0] = "changed";
            events[0] = RecEvent.CreateTriggerOff(0.25d, 0, 0);

            Assert.That(timeline.SourceIds.ToArray(), Is.EqualTo(new[] { "input:trigger", "input:gaze" }));
            Assert.That(timeline.ExpressionIds.ToArray(), Is.EqualTo(new[] { "smile" }));
            Assert.That(timeline.Events[0].Kind, Is.EqualTo(RecEventKind.TriggerOn));
        }

        [Test]
        public void RecLoadResult_DeduplicatesMissingExpressionIds()
        {
            var timeline = new RecTimeline(
                RecBaselineState.Empty,
                Array.Empty<RecEvent>(),
                Array.Empty<string>(),
                new[] { "smile" },
                0d);

            var result = new RecLoadResult(timeline, new[] { "smile", "blink", "smile" });

            Assert.That(result.HasMissingExpressionIds, Is.True);
            Assert.That(result.MissingExpressionIds.ToArray(), Is.EqualTo(new[] { "smile", "blink" }));
        }

        [Test]
        public void RecValidation_FindMissingExpressionIds_ReturnsDistinctMissingIdsFromTimelineAndBaseline()
        {
            var profile = new FacialProfile(
                "1.0.0",
                layers: new[] { new LayerDefinition("emotion", 0, ExclusionMode.Blend) },
                expressions: new[]
                {
                    new Expression("smile", "Smile", "emotion"),
                    new Expression("blink", "Blink", "emotion"),
                });

            var baseline = new RecBaselineState(
                new[]
                {
                    new RecBaselineState.TriggerEntry("input:trigger", new[] { "smile", "missing-baseline", "missing-timeline" }),
                },
                null);

            var timeline = new RecTimeline(
                baseline,
                new[]
                {
                    RecEvent.CreateTriggerOn(0.1d, 0, 0),
                    RecEvent.CreateTriggerOff(0.2d, 0, 2),
                    RecEvent.CreateTriggerOn(0.3d, 0, 2),
                },
                new[] { "input:trigger" },
                new[] { "smile", "blink", "missing-timeline" },
                0.3d);

            IReadOnlyList<string> missing = RecValidation.FindMissingExpressionIds(timeline, profile);

            Assert.That(missing, Is.EqualTo(new[] { "missing-timeline", "missing-baseline" }));
        }

        [Test]
        public void RecValidation_FindMissingExpressionIds_WithMatchingProfile_ReturnsEmpty()
        {
            var profile = new FacialProfile(
                "1.0.0",
                layers: new[] { new LayerDefinition("emotion", 0, ExclusionMode.Blend) },
                expressions: new[]
                {
                    new Expression("smile", "Smile", "emotion"),
                });

            var timeline = new RecTimeline(
                RecBaselineState.Empty,
                new[]
                {
                    RecEvent.CreateTriggerOn(0.1d, 0, 0),
                },
                new[] { "input:trigger" },
                new[] { "smile" },
                0.1d);

            IReadOnlyList<string> missing = RecValidation.FindMissingExpressionIds(timeline, profile);

            Assert.That(missing, Is.Empty);
        }

        [Test]
        public void RecValidation_FindMissingExpressionIds_WithNullTimeline_DoesNotThrow()
        {
            var profile = new FacialProfile("1.0.0");

            IReadOnlyList<string> missing = RecValidation.FindMissingExpressionIds(null, profile);

            Assert.That(missing, Is.Empty);
        }

        [Test]
        public void Interfaces_ExposeExpectedContracts()
        {
            Assert.That(typeof(IRecClock).GetProperty(nameof(IRecClock.ElapsedSeconds)), Is.Not.Null);
            Assert.That(typeof(IRecClock).GetMethod(nameof(IRecClock.Reset)), Is.Not.Null);
            Assert.That(typeof(IRecEventSink).GetMethod(nameof(IRecEventSink.Open)), Is.Not.Null);
            Assert.That(typeof(IRecEventSink).GetMethod(nameof(IRecEventSink.AppendEvent)), Is.Not.Null);
            Assert.That(typeof(IRecEventSink).GetMethod(nameof(IRecEventSink.Complete)), Is.Not.Null);
            Assert.That(typeof(ITriggerInjectionPort).GetMethod(nameof(ITriggerInjectionPort.BeginInjection)), Is.Not.Null);
            Assert.That(typeof(ITriggerInjectionPort).GetMethod(nameof(ITriggerInjectionPort.InjectTriggerOn)), Is.Not.Null);
            Assert.That(typeof(ITriggerInjectionPort).GetMethod(nameof(ITriggerInjectionPort.InjectTriggerOff)), Is.Not.Null);
            Assert.That(typeof(ITriggerInjectionPort).GetMethod(nameof(ITriggerInjectionPort.EndInjection)), Is.Not.Null);
            Assert.That(typeof(IAnalogInjectionPort).GetMethod(nameof(IAnalogInjectionPort.BeginInjection)), Is.Not.Null);
            Assert.That(typeof(IAnalogInjectionPort).GetMethod(nameof(IAnalogInjectionPort.InjectAnalogSample)), Is.Not.Null);
            Assert.That(typeof(IAnalogInjectionPort).GetMethod(nameof(IAnalogInjectionPort.EndInjection)), Is.Not.Null);
        }
    }
}
