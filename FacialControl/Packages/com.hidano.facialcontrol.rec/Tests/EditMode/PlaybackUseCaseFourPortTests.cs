using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Rec.Application.UseCases;
using Hidano.FacialControl.Rec.Domain.Interfaces;
using Hidano.FacialControl.Rec.Domain.Models;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

using Hidano.FacialControl.Testing;

namespace Hidano.FacialControl.Rec.Tests.EditMode
{
    [TestFixture]
    [SmallTest]
    public sealed class PlaybackUseCaseFourPortTests : SizedTestFixture
    {
        [Test]
        public void StartPlayback_FivePorts_UsesWeightFirstAndWeightLastReleaseOrder()
        {
            var order = new List<string>();
            var weight = new FakeWeightPort(order, "weight", true);
            var trigger = new FakePort(order, "trigger", true);
            var expression = new FakePort(order, "expression", true);
            var analog = new FakePort(order, "analog", true);
            var valueProvider = new FakePort(order, "valueProvider", true);
            var useCase = new PlaybackUseCase(weight, trigger, expression, analog, valueProvider);
            useCase.Load(CreateTimeline(), CreateProfile());

            Assert.That(useCase.StartPlayback(), Is.True);
            useCase.StopPlayback();

            Assert.That(order, Is.EqualTo(new[]
            {
                "weight.begin", "trigger.begin", "expression.begin", "analog.begin", "valueProvider.begin",
                "trigger.end", "expression.end", "analog.end", "valueProvider.end", "weight.end",
            }));
        }

        [Test]
        public void StartPlayback_FivePorts_WeightPreflightFailsWithoutBeginningAnyPort()
        {
            var order = new List<string>();
            var weight = new FakeWeightPort(order, "weight", false, "weight unavailable");
            var useCase = new PlaybackUseCase(weight, new FakePort(order, "trigger", true),
                new FakePort(order, "expression", true), new FakePort(order, "analog", true),
                new FakePort(order, "valueProvider", true));
            useCase.Load(CreateTimeline(), CreateProfile());

            LogAssert.Expect(LogType.Error, new Regex("weight unavailable"));
            Assert.That(useCase.StartPlayback(), Is.False);
            Assert.That(order, Is.Empty);
        }

        [Test]
        public void VisitWeightSamples_FivePorts_InjectsBothWeightKinds()
        {
            var order = new List<string>();
            var weight = new FakeWeightPort(order, "weight", true);
            var useCase = new PlaybackUseCase(weight, new FakePort(order, "trigger", true),
                new FakePort(order, "expression", true), new FakePort(order, "analog", true),
                new FakePort(order, "valueProvider", true));

            useCase.VisitLayerWeightSample("emotion", 0.25f);
            useCase.VisitInputSourceWeightSample("emotion", "input:osc", 0.75f);

            Assert.That(order, Is.EqualTo(new[] { "weight.layer:emotion:0.25", "weight.source:emotion:input:osc:0.75" }));
        }
        [Test]
        public void StartPlayback_AllPortsSucceed_UsesTriggerExpressionAnalogValueProviderOrder()
        {
            var order = new List<string>();
            var trigger = new FakePort(order, "trigger", true);
            var expression = new FakePort(order, "expression", true);
            var analog = new FakePort(order, "analog", true);
            var valueProvider = new FakePort(order, "valueProvider", true);
            var useCase = CreateUseCase(trigger, expression, analog, valueProvider);
            useCase.Load(CreateTimeline(), CreateProfile());

            Assert.That(useCase.StartPlayback(), Is.True);
            Assert.That(order, Is.EqualTo(new[]
            {
                "trigger.begin", "expression.begin", "analog.begin", "valueProvider.begin",
            }));
            Assert.That(useCase.State, Is.EqualTo(RecPlaybackState.Playing));

            useCase.StopPlayback();
            Assert.That(order, Is.EqualTo(new[]
            {
                "trigger.begin", "expression.begin", "analog.begin", "valueProvider.begin",
                "trigger.end", "expression.end", "analog.end", "valueProvider.end",
            }));
        }

        [Test]
        public void StartPlayback_PreflightFails_DoesNotBeginAnyPortAndReportsAllReasons()
        {
            var order = new List<string>();
            var trigger = new FakePort(order, "trigger", true);
            var expression = new FakePort(order, "expression", false, "expression unavailable");
            var analog = new FakePort(order, "analog", true);
            var valueProvider = new FakePort(order, "valueProvider", false, "value provider unavailable");
            var useCase = CreateUseCase(trigger, expression, analog, valueProvider);
            useCase.Load(CreateTimeline(), CreateProfile());

            LogAssert.Expect(LogType.Error, new Regex("expression unavailable.*value provider unavailable"));
            Assert.That(useCase.StartPlayback(), Is.False);
            Assert.That(order, Is.Empty);
            Assert.That(useCase.State, Is.EqualTo(RecPlaybackState.Idle));
        }

        [Test]
        public void StartPlayback_ThirdPortFails_RollsBackEstablishedPortsInReverseOrder()
        {
            var order = new List<string>();
            var trigger = new FakePort(order, "trigger", true);
            var expression = new FakePort(order, "expression", true);
            var analog = new FakePort(order, "analog", true, beginResult: false);
            var valueProvider = new FakePort(order, "valueProvider", true);
            var useCase = CreateUseCase(trigger, expression, analog, valueProvider);
            useCase.Load(CreateTimeline(), CreateProfile());

            LogAssert.Expect(LogType.Error, new Regex("analog"));
            Assert.That(useCase.StartPlayback(), Is.False);
            Assert.That(order, Is.EqualTo(new[]
            {
                "trigger.begin", "expression.begin", "analog.begin",
                "expression.end", "trigger.end",
            }));
            Assert.That(valueProvider.BeginCount, Is.EqualTo(0));
            Assert.That(useCase.State, Is.EqualTo(RecPlaybackState.Idle));
        }

        [Test]
        public void StartPlayback_WithOffset_FoldsExpressionsWithLoadedProfileLayerRules()
        {
            var order = new List<string>();
            var expression = new FakePort(order, "expression", true);
            var useCase = CreateUseCase(new FakePort(order, "trigger", true), expression,
                new FakePort(order, "analog", true), new FakePort(order, "valueProvider", true));
            var profile = new FacialProfile(
                "1.0.0",
                new[]
                {
                    new LayerDefinition("emotion", 0, ExclusionMode.LastWins),
                    new LayerDefinition("accent", 1, ExclusionMode.Blend),
                },
                new[]
                {
                    new Expression("smile", "Smile", "emotion"),
                    new Expression("angry", "Angry", "emotion"),
                    new Expression("blink", "Blink", "accent"),
                });
            var timeline = new RecTimeline(
                RecBaselineState.Empty,
                new[]
                {
                    RecEvent.CreateExpressionActivate(0.1d, 0, 0),
                    RecEvent.CreateExpressionActivate(0.2d, 0, 2),
                    RecEvent.CreateExpressionActivate(0.3d, 0, 1),
                },
                new[] { "@expression" },
                new[] { "smile", "angry", "blink" },
                1d,
                new IReadOnlyList<float>[] { Array.Empty<float>(), Array.Empty<float>(), Array.Empty<float>() });
            useCase.Load(timeline, profile);

            Assert.That(useCase.StartPlayback(0.5d), Is.True);

            // profile 無しで畳むと最後の 1 件 (angry) しか残らない。Blend レイヤーの blink は共存し、LastWins の smile は angry に押し出される。
            Assert.That(expression.Baseline.ExpressionEntries, Is.EqualTo(new[] { "blink", "angry" }));
        }

        [Test]
        public void StartPlayback_WithMissingExpressionIds_KeepsValueProviderBaselineAndFiltersExpressionBaseline()
        {
            var order = new List<string>();
            var expression = new FakePort(order, "expression", true);
            var valueProvider = new FakePort(order, "valueProvider", true);
            var useCase = CreateUseCase(new FakePort(order, "trigger", true), expression,
                new FakePort(order, "analog", true), valueProvider);
            var baseline = new RecBaselineState(
                null,
                null,
                new[] { new RecBaselineState.ValueProviderEntry("input:osc", true, new byte[] { 0x01 }, new[] { 0.5f }) },
                new[] { "smile", "missing" });
            var timeline = new RecTimeline(baseline, Array.Empty<RecEvent>(), Array.Empty<string>(),
                new[] { "smile", "missing" }, 0.1d, Array.Empty<IReadOnlyList<float>>());
            useCase.Load(timeline, CreateProfile());

            LogAssert.Expect(LogType.Warning, new Regex("'missing'"));
            Assert.That(useCase.StartPlayback(), Is.True);

            Assert.That(valueProvider.Baseline.TryGetValueProviderEntry("input:osc", out RecBaselineState.ValueProviderEntry entry), Is.True);
            Assert.That(entry.Values, Is.EqualTo(new[] { 0.5f }));
            Assert.That(expression.Baseline.ExpressionEntries, Is.EqualTo(new[] { "smile" }));
        }

        private static PlaybackUseCase CreateUseCase(
            FakePort trigger,
            FakePort expression,
            FakePort analog,
            FakePort valueProvider)
        {
            return new PlaybackUseCase(trigger, expression, analog, valueProvider);
        }

        private static RecTimeline CreateTimeline()
        {
            return new RecTimeline(
                RecBaselineState.Empty,
                new[] { RecEvent.CreateTriggerOn(0.1d, 0, 0) },
                new[] { "input:trigger" },
                new[] { "smile" },
                0.1d,
                new IReadOnlyList<float>[] { Array.Empty<float>() });
        }

        private static FacialProfile CreateProfile()
        {
            return new FacialProfile(
                "1.0.0",
                new[] { new LayerDefinition("emotion", 0, ExclusionMode.Blend) },
                new[] { new Expression("smile", "Smile", "emotion") });
        }

        private sealed class FakePort : ITriggerInjectionPort, IExpressionInjectionPort,
            IAnalogInjectionPort, IValueProviderInjectionPort
        {
            private readonly List<string> _order;
            private readonly string _name;
            private readonly bool _canBegin;
            private readonly string _reason;
            private readonly bool _beginResult;

            public FakePort(List<string> order, string name, bool canBegin, string reason = "", bool beginResult = true)
            {
                _order = order;
                _name = name;
                _canBegin = canBegin;
                _reason = reason;
                _beginResult = beginResult;
            }

            public int BeginCount { get; private set; }

            public RecBaselineState Baseline { get; private set; }

            public bool CanBeginInjection(out string reason)
            {
                reason = _reason;
                return _canBegin;
            }

            public bool TryBeginInjection(RecBaselineState baseline)
            {
                BeginCount++;
                Baseline = baseline;
                _order.Add(_name + ".begin");
                return _beginResult;
            }

            public void EndInjection() => _order.Add(_name + ".end");
            public void InjectTriggerOn(string sourceId, string expressionId) { }
            public void InjectTriggerOff(string sourceId, string expressionId) { }
            public void InjectActivate(string expressionId) { }
            public void InjectDeactivate(string expressionId) { }
            public void InjectAnalogSample(string sourceId, ReadOnlySpan<float> axes) { }
            public void InjectValueProviderState(string sourceId, bool isValid, ReadOnlySpan<byte> maskBytes, ReadOnlySpan<float> values) { }
        }

        private sealed class FakeWeightPort : IWeightInjectionPort
        {
            private readonly List<string> _order;
            private readonly string _name;
            private readonly bool _canBegin;
            private readonly string _reason;

            public FakeWeightPort(List<string> order, string name, bool canBegin, string reason = "")
            {
                _order = order;
                _name = name;
                _canBegin = canBegin;
                _reason = reason;
            }

            public bool CanBeginInjection(out string reason) { reason = _reason; return _canBegin; }
            public bool TryBeginInjection(RecBaselineState baseline) { _order.Add(_name + ".begin"); return true; }
            public void EndInjection() { _order.Add(_name + ".end"); }
            public void InjectLayerWeight(string layerName, float weight) => _order.Add($"weight.layer:{layerName}:{weight}");
            public void InjectInputSourceWeight(string layerName, string slotId, float weight) => _order.Add($"weight.source:{layerName}:{slotId}:{weight}");
        }
    }
}
