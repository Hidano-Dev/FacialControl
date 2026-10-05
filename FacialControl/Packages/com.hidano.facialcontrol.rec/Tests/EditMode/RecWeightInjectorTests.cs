using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Rec.Adapters.Playback;
using Hidano.FacialControl.Rec.Domain.Models;
using Hidano.FacialControl.Testing;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Hidano.FacialControl.Rec.Tests.EditMode
{
    [TestFixture]
    [SmallTest]
    public sealed class RecWeightInjectorTests : SizedTestFixture
    {
        [Test]
        public void CanBeginInjection_UnresolvedGate_ReturnsFalseWithReason()
        {
            var injector = new RecWeightInjector(() => null);

            Assert.That(injector.CanBeginInjection(out string reason), Is.False);
            Assert.That(reason, Does.Contain("WeightInjectionGate is null"));
        }

        [Test]
        public void CanBeginInjection_DuplicateLayerNames_ReturnsFalseWithoutSideEffects()
        {
            var gate = new FakeGate { LayerNamesAreUnique = false };
            var injector = new RecWeightInjector(() => gate);

            Assert.That(injector.CanBeginInjection(out string reason), Is.False);
            Assert.That(reason, Does.Contain("duplicate layer names"));
            Assert.That(gate.Calls, Is.Empty);
        }

        [Test]
        public void TryBeginInjection_ResolvesAndEstablishesInSuspendResetBaselineOrder()
        {
            var gate = new FakeGate();
            var injector = new RecWeightInjector(() => gate);
            var baseline = new RecBaselineState(
                null, null, null, null,
                new[] { new LayerWeightEntry("emotion", 0.4f) },
                new[] { new InputSourceWeightEntry("emotion", "mouth", 0.7f) });

            Assert.That(injector.TryBeginInjection(baseline), Is.True);
            Assert.That(gate.Calls, Is.EqualTo(new[] { "suspend", "reset", "baseline-layer:emotion:0.4", "baseline-slot:emotion:mouth:0.7" }));
        }

        [Test]
        public void InjectUnknownTarget_WarnsOnceAndContinues()
        {
            var gate = new FakeGate();
            var injector = new RecWeightInjector(() => gate);
            Assert.That(injector.TryBeginInjection(RecBaselineState.Empty), Is.True);

            LogAssert.Expect(LogType.Warning, new Regex("layer 'missing'"));
            injector.InjectLayerWeight("missing", 0.2f);
            injector.InjectLayerWeight("missing", 0.3f);
            Assert.That(gate.Calls.FindAll(call => call.StartsWith("inject-layer:missing", StringComparison.Ordinal)), Has.Count.EqualTo(2));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void InjectUnknownSlot_WarnsOncePerLayerAndSlot()
        {
            var gate = new FakeGate();
            var injector = new RecWeightInjector(() => gate);
            Assert.That(injector.TryBeginInjection(RecBaselineState.Empty), Is.True);

            LogAssert.Expect(LogType.Warning, new Regex("slot 'osc' in layer 'missing'"));
            LogAssert.Expect(LogType.Warning, new Regex("slot 'pad' in layer 'missing'"));
            injector.InjectInputSourceWeight("missing", "osc", 0.2f);
            injector.InjectInputSourceWeight("missing", "osc", 0.3f);
            injector.InjectInputSourceWeight("missing", "pad", 0.3f);
            injector.InjectInputSourceWeight("missing", "pad", 0.4f);

            Assert.That(gate.Calls.FindAll(call => call.StartsWith("inject-slot:missing", StringComparison.Ordinal)), Has.Count.EqualTo(4));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void TryBeginInjection_UnresolvedGate_ReturnsFalseWithoutSideEffects()
        {
            var gate = new FakeGate();
            bool resolved = false;
            var injector = new RecWeightInjector(() => resolved ? gate : null);

            Assert.That(injector.TryBeginInjection(RecBaselineState.Empty), Is.False);
            injector.InjectLayerWeight("emotion", 0.5f);
            injector.EndInjection();

            Assert.That(gate.Calls, Is.Empty);
        }

        [Test]
        public void TryBeginInjection_SuspendFails_ReturnsFalseWithoutResetOrBaseline()
        {
            var gate = new FakeGate { SuspendResult = false };
            var injector = new RecWeightInjector(() => gate);
            var baseline = new RecBaselineState(
                null, null, null, null,
                new[] { new LayerWeightEntry("emotion", 0.4f) },
                null);

            Assert.That(injector.TryBeginInjection(baseline), Is.False);
            injector.InjectLayerWeight("emotion", 0.5f);
            injector.EndInjection();

            Assert.That(gate.Calls, Is.EqualTo(new[] { "suspend" }));
        }

        [Test]
        public void TryBeginInjection_UnknownBaselineTarget_WarnsOnceAndContinues()
        {
            var gate = new FakeGate();
            var injector = new RecWeightInjector(() => gate);
            var baseline = new RecBaselineState(
                null, null, null, null,
                new[] { new LayerWeightEntry("emotion", 0.4f), new LayerWeightEntry("missing", 0.1f) },
                new[] { new InputSourceWeightEntry("missing", "osc", 0.2f) });

            LogAssert.Expect(LogType.Warning, new Regex("weight baseline for 'missing'"));
            LogAssert.Expect(LogType.Warning, new Regex("weight baseline for 'missing/osc'"));
            Assert.That(injector.TryBeginInjection(baseline), Is.True);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void EndInjection_IsIdempotentAndOnlyResumes()
        {
            var gate = new FakeGate();
            var injector = new RecWeightInjector(() => gate);
            Assert.That(injector.TryBeginInjection(RecBaselineState.Empty), Is.True);

            injector.EndInjection();
            injector.EndInjection();

            Assert.That(gate.Calls, Does.Contain("resume"));
            Assert.That(gate.Calls.FindAll(call => call == "resume"), Has.Count.EqualTo(1));
        }

        private sealed class FakeGate : IWeightInjectionGate
        {
            public bool IsLiveWeightSuspended { get; private set; }
            public bool LayerNamesAreUnique { get; set; } = true;
            public List<string> Calls { get; } = new List<string>();
            public bool SuspendResult { get; set; } = true;

            public bool SuspendLiveWeights() { Calls.Add("suspend"); IsLiveWeightSuspended = SuspendResult; return SuspendResult; }
            public bool ResumeLiveWeights() { Calls.Add("resume"); IsLiveWeightSuspended = false; return true; }
            public void ResetWeightsToDeclared() => Calls.Add("reset");
            public bool TrySetBaselineLayerWeight(string layerName, float weight) { Calls.Add($"baseline-layer:{layerName}:{weight}"); return layerName == "emotion"; }
            public bool TrySetBaselineInputSourceWeight(string layerName, string slotId, float weight) { Calls.Add($"baseline-slot:{layerName}:{slotId}:{weight}"); return layerName == "emotion"; }
            public bool TryInjectLayerWeight(string layerName, float weight) { Calls.Add($"inject-layer:{layerName}:{weight}"); return layerName == "emotion"; }
            public bool TryInjectInputSourceWeight(string layerName, string slotId, float weight) { Calls.Add($"inject-slot:{layerName}:{slotId}:{weight}"); return layerName == "emotion"; }
            public void CollectLayerWeights(List<LayerWeightEntry> buffer) { }
            public void CollectInputSourceWeights(List<InputSourceWeightEntry> buffer) { }
        }
    }
}
