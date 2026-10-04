using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Hidano.FacialControl.Domain.Interfaces;
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
    public sealed class RecExpressionInjectorTests : SizedTestFixture
    {
        [Test]
        public void CanBeginInjection_GateUnresolved_ReturnsFalseWithReason()
        {
            var injector = new RecExpressionInjector(() => null);

            Assert.That(injector.CanBeginInjection(out string reason), Is.False);
            Assert.That(reason, Does.Contain("ExpressionActivationGate"));
        }

        [Test]
        public void TryBeginInjection_GateUnresolved_ReturnsFalseWithoutSuspending()
        {
            bool resolved = false;
            var gate = new FakeGate();
            var injector = new RecExpressionInjector(() => resolved ? gate : null);

            Assert.That(injector.TryBeginInjection(RecBaselineState.Empty), Is.False);
            Assert.That(gate.SuspendCalls, Is.Zero);
        }

        [Test]
        public void TryBeginInjection_GateResolved_SuspendsThenResetsAndReturnsTrue()
        {
            var gate = new FakeGate();
            var injector = new RecExpressionInjector(() => gate);
            var baseline = new RecBaselineState(null, null, null, new[] { "smile", "angry" });

            Assert.That(injector.TryBeginInjection(baseline), Is.True);
            Assert.That(gate.Suspended, Is.True);
            Assert.That(gate.ResetIds, Is.EqualTo(new[] { "smile", "angry" }));

            injector.EndInjection();

            Assert.That(gate.Suspended, Is.False);
            Assert.That(gate.ResumeCalls, Is.EqualTo(1));
        }

        [Test]
        public void InjectUnknownExpression_WarnsOnlyOnce()
        {
            var gate = new FakeGate { KnownId = "smile" };
            var injector = new RecExpressionInjector(() => gate);
            Assert.That(injector.TryBeginInjection(RecBaselineState.Empty), Is.True);

            LogAssert.Expect(LogType.Warning, new Regex("expressionId 'missing'"));
            injector.InjectActivate("missing");
            injector.InjectDeactivate("missing");
        }

        private sealed class FakeGate : IExpressionActivationGate
        {
            public bool Suspended { get; private set; }
            public int SuspendCalls { get; private set; }
            public int ResumeCalls { get; private set; }
            public string KnownId { get; set; }
            public List<string> ResetIds { get; } = new List<string>();
            public bool IsActivationSuspended => Suspended;
            public int ResetGeneration { get; private set; }

            public bool SuspendActivation()
            {
                SuspendCalls++;
                if (Suspended) return false;
                Suspended = true;
                return true;
            }

            public bool ResumeActivation()
            {
                ResumeCalls++;
                if (!Suspended) return false;
                Suspended = false;
                return true;
            }

            public bool InjectActivate(string expressionId) => string.Equals(expressionId, KnownId, StringComparison.Ordinal);

            public bool InjectDeactivate(string expressionId) => string.Equals(expressionId, KnownId, StringComparison.Ordinal);

            public void ResetActiveExpressions(IReadOnlyList<string> expressionIds)
            {
                ResetIds.Clear();
                for (int i = 0; i < expressionIds.Count; i++) ResetIds.Add(expressionIds[i]);
                ResetGeneration++;
            }

            public void CollectActiveExpressionIds(List<string> buffer) { buffer.Clear(); }
        }
    }
}
