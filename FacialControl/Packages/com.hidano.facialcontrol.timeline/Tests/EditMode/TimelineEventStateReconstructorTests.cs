using System.Collections.Generic;
using Hidano.FacialControl.Timeline.Domain.Models;
using Hidano.FacialControl.Timeline.Domain.Services;
using NUnit.Framework;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Timeline.Tests.EditMode
{
    [SmallTest]
    public sealed class TimelineEventStateReconstructorTests : SizedTestFixture
    {
        [Test]
        public void AdvanceLinear_ReplaysEventsInRecordedOrder_AndMatchesExpectedStack()
        {
            var reconstructor = new TimelineEventStateReconstructor();
            reconstructor.SetEvents(new[]
            {
                Event(0.25d, TimelineStateEvent.KindOn, "smile"),
                Event(1.0d, TimelineStateEvent.KindOn, "angry"),
                Event(1.0d, TimelineStateEvent.KindOff, "smile"),
                Event(1.0d, TimelineStateEvent.KindOn, "smile"),
            });

            var sink = new FakeTimelineTriggerSink();

            reconstructor.AdvanceLinear(0.0d, 1.0d, sink);

            CollectionAssert.AreEqual(
                new[]
                {
                    "On:smile",
                    "On:angry",
                    "Off:smile",
                    "On:smile",
                },
                sink.Calls);
            CollectionAssert.AreEqual(new[] { "angry", "smile" }, sink.ActiveExpressionIds);
        }

        [Test]
        public void JumpTo_ReordersExistingStack_ByReplayingTargetStackFromOldestToNewest()
        {
            var reconstructor = new TimelineEventStateReconstructor();
            reconstructor.SetEvents(new[]
            {
                Event(0.10d, TimelineStateEvent.KindOn, "smile"),
                Event(0.20d, TimelineStateEvent.KindOn, "angry"),
                Event(0.30d, TimelineStateEvent.KindOff, "smile"),
                Event(0.40d, TimelineStateEvent.KindOn, "smile"),
            });

            var sink = new FakeTimelineTriggerSink();
            sink.TriggerOn("smile");
            sink.TriggerOn("obsolete");
            sink.ClearCalls();

            reconstructor.JumpTo(0.40d, sink);

            CollectionAssert.AreEqual(
                new[]
                {
                    "Off:obsolete",
                    "On:angry",
                    "On:smile",
                },
                sink.Calls);
            CollectionAssert.AreEqual(new[] { "angry", "smile" }, sink.ActiveExpressionIds);
        }

        [Test]
        public void JumpTo_WithSameTimestampEvents_PreservesRecordedOrderWhenRestoringLifoStack()
        {
            var reconstructor = new TimelineEventStateReconstructor();
            reconstructor.SetEvents(new[]
            {
                Event(1.0d, TimelineStateEvent.KindOn, "smile"),
                Event(1.0d, TimelineStateEvent.KindOn, "angry"),
                Event(1.0d, TimelineStateEvent.KindOff, "smile"),
                Event(1.0d, TimelineStateEvent.KindOn, "smile"),
            });

            var sink = new FakeTimelineTriggerSink();

            reconstructor.JumpTo(1.0d, sink);

            CollectionAssert.AreEqual(
                new[]
                {
                    "On:angry",
                    "On:smile",
                },
                sink.Calls);
            CollectionAssert.AreEqual(new[] { "angry", "smile" }, sink.ActiveExpressionIds);
        }

        private static TimelineStateEvent Event(double timeSeconds, byte kind, string expressionId)
        {
            return new TimelineStateEvent(timeSeconds, kind, expressionId, "Expressions");
        }

        private sealed class FakeTimelineTriggerSink : ITimelineTriggerSink
        {
            private readonly List<string> _activeExpressionIds = new List<string>();

            public List<string> Calls { get; } = new List<string>();

            public IReadOnlyList<string> ActiveExpressionIds => _activeExpressionIds;

            public void TriggerOn(string expressionId)
            {
                _activeExpressionIds.Remove(expressionId);
                _activeExpressionIds.Add(expressionId);
                Calls.Add($"On:{expressionId}");
            }

            public void TriggerOff(string expressionId)
            {
                if (_activeExpressionIds.Remove(expressionId))
                {
                    Calls.Add($"Off:{expressionId}");
                }
            }

            public void ClearCalls()
            {
                Calls.Clear();
            }
        }
    }
}
