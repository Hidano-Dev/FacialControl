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

            public bool CanBeginInjection(out string reason)
            {
                reason = _reason;
                return _canBegin;
            }

            public bool TryBeginInjection(RecBaselineState baseline)
            {
                BeginCount++;
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
    }
}
