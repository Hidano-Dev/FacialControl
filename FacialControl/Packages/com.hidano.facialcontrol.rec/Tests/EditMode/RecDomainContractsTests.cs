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
        public void RecEvent_CreateAnalogSample_RejectsZeroAxisCount() => Assert.Throws<ArgumentOutOfRangeException>(() => RecEvent.CreateAnalogSample(0.5d, 1, 0));

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
            var baseline = new RecBaselineState(new[] { new RecBaselineState.TriggerEntry("input:trigger", expressions) }, new[] { new RecBaselineState.AnalogEntry("input:gaze", axes) });
            expressions[0] = "changed"; axes[0] = 1f;
            Assert.That(baseline.TryGetTriggerStack("input:trigger", out IReadOnlyList<string> storedExpressions), Is.True);
            Assert.That(storedExpressions.ToArray(), Is.EqualTo(new[] { "smile", "angry" }));
            Assert.That(baseline.TryGetAnalogAxes("input:gaze", out IReadOnlyList<float> storedAxes), Is.True);
            Assert.That(storedAxes.ToArray(), Is.EqualTo(new[] { 0.25f, -0.5f }));
        }

        [Test]
        public void RecTimeline_RejectsNonTimedEvents()
        {
            Assert.Throws<ArgumentException>(() => new RecTimeline(RecBaselineState.Empty, new[] { RecEvent.CreateBaselineTrigger(0, 0) }, new[] { "input:trigger" }, new[] { "smile" }, 0d));
        }

        [Test]
        public void RecTimeline_RejectsOutOfOrderTimestamps()
        {
            RecEvent[] events = { RecEvent.CreateTriggerOn(0.5d, 0, 0), RecEvent.CreateTriggerOff(0.25d, 0, 0) };
            Assert.Throws<ArgumentException>(() => new RecTimeline(RecBaselineState.Empty, events, new[] { "input:trigger" }, new[] { "smile" }, 1d));
        }

        [Test]
        public void RecTimeline_RejectsUnknownSourceIndex()
        {
            Assert.Throws<ArgumentException>(() => new RecTimeline(RecBaselineState.Empty, new[] { RecEvent.CreateAnalogSample(0.25d, 1, 2) }, new[] { "input:gaze" }, Array.Empty<string>(), 1d));
        }

        [Test]
        public void RecTimeline_StoresCopiedImmutableState()
        {
            var sourceIds = new[] { "input:trigger", "input:gaze" }; var expressionIds = new[] { "smile" };
            RecEvent[] events = { RecEvent.CreateTriggerOn(0.25d, 0, 0), RecEvent.CreateAnalogSample(0.5d, 1, 2) };
            var timeline = new RecTimeline(RecBaselineState.Empty, events, sourceIds, expressionIds, 0.5d, new IReadOnlyList<float>[] { Array.Empty<float>(), new float[] { 0.25f, -0.5f } });
            sourceIds[0] = "changed"; expressionIds[0] = "changed"; events[0] = RecEvent.CreateTriggerOff(0.25d, 0, 0);
            Assert.That(timeline.SourceIds.ToArray(), Is.EqualTo(new[] { "input:trigger", "input:gaze" }));
            Assert.That(timeline.ExpressionIds.ToArray(), Is.EqualTo(new[] { "smile" }));
            Assert.That(timeline.Events[0].Kind, Is.EqualTo(RecEventKind.TriggerOn));
        }

        [Test]
        public void RecLoadResult_DeduplicatesMissingExpressionIds()
        {
            var timeline = new RecTimeline(RecBaselineState.Empty, Array.Empty<RecEvent>(), Array.Empty<string>(), new[] { "smile" }, 0d);
            var result = new RecLoadResult(timeline, new[] { "smile", "blink", "smile" });
            Assert.That(result.HasMissingExpressionIds, Is.True);
            Assert.That(result.MissingExpressionIds.ToArray(), Is.EqualTo(new[] { "smile", "blink" }));
        }

        [Test]
        public void RecValidation_FindMissingExpressionIds_ReturnsDistinctMissingIdsFromTimelineAndBaseline()
        {
            var profile = new FacialProfile("1.0.0", layers: new[] { new LayerDefinition("emotion", 0, ExclusionMode.Blend) }, expressions: new[] { new Expression("smile", "Smile", "emotion"), new Expression("blink", "Blink", "emotion") });
            var baseline = new RecBaselineState(new[] { new RecBaselineState.TriggerEntry("input:trigger", new[] { "smile", "missing-baseline", "missing-timeline" }) }, null, null, new[] { "missing-expression-baseline" });
            var timeline = new RecTimeline(baseline, new[] { RecEvent.CreateTriggerOn(0.1d, 0, 0), RecEvent.CreateTriggerOff(0.2d, 0, 2), RecEvent.CreateTriggerOn(0.3d, 0, 2) }, new[] { "input:trigger" }, new[] { "smile", "blink", "missing-timeline" }, 0.3d);
            Assert.That(RecValidation.FindMissingExpressionIds(timeline, profile), Is.EqualTo(new[] { "missing-timeline", "missing-baseline", "missing-expression-baseline" }));
        }

        [Test]
        public void RecValidation_FindMissingExpressionIds_WithMatchingProfile_ReturnsEmpty()
        {
            var profile = new FacialProfile("1.0.0", layers: new[] { new LayerDefinition("emotion", 0, ExclusionMode.Blend) }, expressions: new[] { new Expression("smile", "Smile", "emotion") });
            var timeline = new RecTimeline(RecBaselineState.Empty, new[] { RecEvent.CreateTriggerOn(0.1d, 0, 0) }, new[] { "input:trigger" }, new[] { "smile" }, 0.1d);
            Assert.That(RecValidation.FindMissingExpressionIds(timeline, profile), Is.Empty);
        }

        [Test]
        public void RecValidation_FindMissingExpressionIds_WithNullTimeline_DoesNotThrow()
        {
            Assert.That(RecValidation.FindMissingExpressionIds(null, new FacialProfile("1.0.0")), Is.Empty);
        }

        [Test]
        public void Interfaces_ExposeExpectedContracts()
        {
            Assert.That(typeof(IRecClock).GetProperty(nameof(IRecClock.ElapsedSeconds)), Is.Not.Null);
            Assert.That(typeof(IRecClock).GetMethod(nameof(IRecClock.Reset)), Is.Not.Null);
            Assert.That(typeof(IRecEventSink).GetMethod(nameof(IRecEventSink.Open)), Is.Not.Null);
            Assert.That(typeof(IRecEventSink).GetMethod(nameof(IRecEventSink.AppendEvent)), Is.Not.Null);
            Assert.That(typeof(IRecEventSink).GetMethod(nameof(IRecEventSink.Complete)), Is.Not.Null);
            Assert.That(typeof(IInjectionPort).GetMethod(nameof(IInjectionPort.CanBeginInjection)), Is.Not.Null);
            Assert.That(typeof(IInjectionPort).GetMethod(nameof(IInjectionPort.TryBeginInjection)), Is.Not.Null);
            Assert.That(typeof(ITriggerInjectionPort).GetMethod(nameof(ITriggerInjectionPort.InjectTriggerOn)), Is.Not.Null);
            Assert.That(typeof(ITriggerInjectionPort).GetMethod(nameof(ITriggerInjectionPort.InjectTriggerOff)), Is.Not.Null);
            Assert.That(typeof(IInjectionPort).GetMethod(nameof(IInjectionPort.EndInjection)), Is.Not.Null);
            Assert.That(typeof(IInjectionPort).GetMethod(nameof(IInjectionPort.TryBeginInjection)), Is.Not.Null);
            Assert.That(typeof(IAnalogInjectionPort).GetMethod(nameof(IAnalogInjectionPort.InjectAnalogSample)), Is.Not.Null);
        }

        [Test]
        public void NewFactories_PreserveKindIndexesFlagsAndCounts()
        {
            RecEvent sample = RecEvent.CreateValueProviderSample(1.25d, 3, RecValueProviderFlags.IsValid | RecValueProviderFlags.HasMask | RecValueProviderFlags.HasValues, 12, 7);
            RecEvent baseline = RecEvent.CreateBaselineValueProvider(4, false, 9, 5);
            Assert.That(sample.Kind, Is.EqualTo(RecEventKind.ValueProviderSample)); Assert.That(sample.SourceIdIndex, Is.EqualTo(3));
            Assert.That(sample.ValueCount, Is.EqualTo(12)); Assert.That(sample.MaskByteCount, Is.EqualTo(7));
            Assert.That(sample.Flags, Is.EqualTo(RecValueProviderFlags.IsValid | RecValueProviderFlags.HasMask | RecValueProviderFlags.HasValues));
            Assert.That(baseline.Kind, Is.EqualTo(RecEventKind.BaselineValueProvider)); Assert.That(baseline.SourceIdIndex, Is.EqualTo(4));
            Assert.That(baseline.Flags, Is.EqualTo(RecValueProviderFlags.HasMask | RecValueProviderFlags.HasValues));
            Assert.That(RecEvent.CreateExpressionActivate(2d, 6, 8).Kind, Is.EqualTo(RecEventKind.ExpressionActivate));
            Assert.That(RecEvent.CreateExpressionDeactivate(3d, 7, 9).Kind, Is.EqualTo(RecEventKind.ExpressionDeactivate));
            Assert.That(RecEvent.CreateBaselineExpression(10, 11).Kind, Is.EqualTo(RecEventKind.BaselineExpression));
        }

        [Test]
        public void PayloadFloatCount_FollowsKindAndHasValues()
        {
            Assert.That(RecEvent.CreateValueProviderSample(0d, 0, RecValueProviderFlags.HasMask, 0, 2).PayloadFloatCount, Is.EqualTo(0));
            Assert.That(RecEvent.CreateValueProviderSample(0d, 0, RecValueProviderFlags.HasValues, 13, 0).PayloadFloatCount, Is.EqualTo(13));
            Assert.That(RecEvent.CreateBaselineValueProvider(0, true, 4, 1).PayloadFloatCount, Is.EqualTo(4));
            Assert.That(RecEvent.CreateExpressionActivate(0d, 0, 0).PayloadFloatCount, Is.EqualTo(0));
        }

        [Test]
        public void IsTimedEvent_IncludesAllTimedKinds()
        {
            Assert.That(RecEvent.CreateTriggerOn(0d, 0, 0).IsTimedEvent, Is.True); Assert.That(RecEvent.CreateTriggerOff(0d, 0, 0).IsTimedEvent, Is.True);
            Assert.That(RecEvent.CreateAnalogSample(0d, 0, 1).IsTimedEvent, Is.True); Assert.That(RecEvent.CreateValueProviderSample(0d, 0, RecValueProviderFlags.None, 0, 0).IsTimedEvent, Is.True);
            Assert.That(RecEvent.CreateExpressionActivate(0d, 0, 0).IsTimedEvent, Is.True); Assert.That(RecEvent.CreateExpressionDeactivate(0d, 0, 0).IsTimedEvent, Is.True);
            Assert.That(RecEvent.CreateBaselineExpression(0, 0).IsTimedEvent, Is.False);
        }

        [Test]
        public void Flags_DefineFullInputBaselineHeaderBit()
        {
            Assert.That((ushort)RecHeaderFlags.FullInputBaseline, Is.EqualTo(0x0001));
            Assert.That((byte)RecValueProviderFlags.IsValid, Is.EqualTo(0x01)); Assert.That((byte)RecValueProviderFlags.HasMask, Is.EqualTo(0x02)); Assert.That((byte)RecValueProviderFlags.HasValues, Is.EqualTo(0x04));
        }

        [Test]
        public void WeightFactories_UseKindsIndexesPayloadAndTiming()
        {
            RecEvent layer = RecEvent.CreateLayerWeightSample(1.25d, 4);
            RecEvent source = RecEvent.CreateInputSourceWeightSample(2.5d, 4, 7);
            RecEvent baselineLayer = RecEvent.CreateBaselineLayerWeight(4);
            RecEvent baselineSource = RecEvent.CreateBaselineInputSourceWeight(4, 7);

            Assert.That(layer.Kind, Is.EqualTo(RecEventKind.LayerWeightSample));
            Assert.That(layer.LayerIdIndex, Is.EqualTo(4));
            Assert.That(layer.PayloadFloatCount, Is.EqualTo(1));
            Assert.That(layer.IsTimedEvent, Is.True);
            Assert.That(source.Kind, Is.EqualTo(RecEventKind.InputSourceWeightSample));
            Assert.That(source.LayerIdIndex, Is.EqualTo(4));
            Assert.That(source.SourceIdIndex, Is.EqualTo(7));
            Assert.That(baselineLayer.Kind, Is.EqualTo(RecEventKind.BaselineLayerWeight));
            Assert.That(baselineSource.Kind, Is.EqualTo(RecEventKind.BaselineInputSourceWeight));
            Assert.That(baselineLayer.IsTimedEvent, Is.False);
            Assert.That(baselineSource.PayloadFloatCount, Is.EqualTo(1));
        }

        [Test]
        public void LayerIdDefinition_IsSupported()
        {
            RecEvent evt = RecEvent.CreateIdDefine(2, RecEvent.IdDefinitionKind.Layer);
            Assert.That(evt.DefinedIdKind, Is.EqualTo(RecEvent.IdDefinitionKind.Layer));
        }

        [Test]
        public void Flags_DefineWeightBaselineHeaderBit()
        {
            Assert.That((ushort)RecHeaderFlags.WeightBaseline, Is.EqualTo(0x0002));
        }
    }
}
