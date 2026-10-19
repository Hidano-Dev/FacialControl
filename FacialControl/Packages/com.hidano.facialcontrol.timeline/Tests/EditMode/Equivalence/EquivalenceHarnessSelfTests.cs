using System;
using System.Collections.Generic;
using Hidano.FacialControl.Rec.Domain.Models;
using Hidano.FacialControl.Testing;
using Hidano.FacialControl.Timeline.Tests.Shared;
using NUnit.Framework;

namespace Hidano.FacialControl.Timeline.Tests.EditMode
{
    /// <summary>
    /// 往復検証の仕組み自体のテスト（比較・シナリオ生成・レポート）。わざと壊した入力で、不一致を正しく指すことを確かめる。
    /// </summary>
    [SmallTest]
    public sealed class EquivalenceHarnessSelfTests : SizedTestFixture
    {
        [Test]
        public void Compare_IdenticalTraces_Match()
        {
            EquivalenceFrameTrace trace = CreateRampTrace(60);

            EquivalenceResult result = EquivalenceComparer.Compare(trace, CreateRampTrace(60));

            Assert.That(result.IsMatch, Is.True);
            Assert.That(result.ComparedFrameCount, Is.EqualTo(60));
        }

        [Test]
        public void Compare_ValuesShiftedByOneFrame_ReportsFirstChangingFrameAndChannel()
        {
            EquivalenceFrameTrace expected = CreateStepTrace(60, stepFrame: 20);
            EquivalenceFrameTrace shifted = expected.ShiftValues(1);

            EquivalenceResult result = EquivalenceComparer.Compare(expected, shifted);

            Assert.That(result.IsMatch, Is.False);
            Assert.That(result.FirstMismatch.HasValue, Is.True);
            Assert.That(result.FirstMismatch.Value.Frame, Is.EqualTo(19), "1 フレーム先の値を読むので、段差の 1 フレーム前で食い違う");
            Assert.That(result.FirstMismatch.Value.ChannelName, Is.EqualTo("Smile"));
            Assert.That(result.MismatchFrameCount, Is.EqualTo(1));

            var report = new EquivalenceReport("Small", CreateScenario(), RecEquivalenceFrameStepMode.Fixed60, result, Array.Empty<string>());
            string summary = report.ToSummary();
            StringAssert.Contains("frame 19", summary);
            StringAssert.Contains("channel=Smile", summary);
            StringAssert.Contains("\"frame\": 19", report.ToJson());
        }

        [Test]
        public void Compare_DifferenceWithinTolerance_Matches()
        {
            EquivalenceFrameTrace expected = CreateRampTrace(10);
            var actual = new EquivalenceFrameTrace(expected.Channels);
            for (int frame = 0; frame < expected.FrameCount; frame++)
            {
                actual.AddFrame(expected.GetTime(frame), new[] { expected.GetValue(frame, 0) + 0.009f, 0f, 0f, 0f, 1f });
            }

            Assert.That(EquivalenceComparer.Compare(expected, actual).IsMatch, Is.True);
        }

        [Test]
        public void Compare_BoneRotationBeyondTolerance_ReportsAngleDifference()
        {
            var channels = new[] { new EquivalenceChannel("LeftEye", EquivalenceChannelKind.BoneRotation) };
            var expected = new EquivalenceFrameTrace(channels);
            var actual = new EquivalenceFrameTrace(channels);
            expected.AddFrame(0d, new[] { 0f, 0f, 0f, 1f });
            float half = (float)(0.5d * Math.PI / 180d * 0.5d);
            actual.AddFrame(0d, new[] { (float)Math.Sin(half), 0f, 0f, (float)Math.Cos(half) });

            EquivalenceResult result = EquivalenceComparer.Compare(expected, actual);

            Assert.That(result.IsMatch, Is.False);
            Assert.That(result.FirstMismatch.Value.Difference, Is.EqualTo(0.5d).Within(1e-3d));
        }

        [Test]
        public void Compare_DifferentFrameCounts_ReportsStructureError()
        {
            EquivalenceResult result = EquivalenceComparer.Compare(CreateRampTrace(10), CreateRampTrace(9));

            Assert.That(result.IsMatch, Is.False);
            Assert.That(result.StructureError, Does.Contain("フレーム数"));
        }

        [Test]
        public void Scenarios_SameName_AreDeterministic()
        {
            IReadOnlyList<RecEquivalenceScenario> first = RecEquivalenceScenarios.All();
            IReadOnlyList<RecEquivalenceScenario> second = RecEquivalenceScenarios.All();

            Assert.That(second.Count, Is.EqualTo(first.Count));
            for (int i = 0; i < first.Count; i++)
            {
                Assert.That(second[i].Name, Is.EqualTo(first[i].Name));
                Assert.That(second[i].Timeline.Events.Count, Is.EqualTo(first[i].Timeline.Events.Count), first[i].Name);
                for (int e = 0; e < first[i].Timeline.Events.Count; e++)
                {
                    Assert.That(second[i].Timeline.Events[e].TimestampSeconds, Is.EqualTo(first[i].Timeline.Events[e].TimestampSeconds), first[i].Name);
                    Assert.That(second[i].Timeline.Events[e].Kind, Is.EqualTo(first[i].Timeline.Events[e].Kind), first[i].Name);
                }
            }
        }

        [Test]
        public void Scenarios_EventTimes_StayOffTheFrameGrids()
        {
            IReadOnlyList<RecEquivalenceScenario> scenarios = RecEquivalenceScenarios.All();
            for (int i = 0; i < scenarios.Count; i++)
            {
                IReadOnlyList<RecEvent> events = scenarios[i].Timeline.Events;
                for (int e = 0; e < events.Count; e++)
                {
                    double time = events[e].TimestampSeconds;
                    Assert.That(DistanceToGrid(time, 1d / 60d), Is.GreaterThan(5e-5d), $"{scenarios[i].Name} t={time}");
                    Assert.That(DistanceToGrid(time, 1d / 120d), Is.GreaterThan(5e-5d), $"{scenarios[i].Name} t={time}");
                    Assert.That(time, Is.LessThanOrEqualTo(scenarios[i].DurationSeconds), scenarios[i].Name);
                }
            }
        }

        [Test]
        public void Scenarios_EnabledNames_ExistInCatalog()
        {
            IReadOnlyList<string> names = RecEquivalenceScenarios.AllNames();
            foreach (string name in RecEquivalenceScenarios.EnabledInSmall)
            {
                Assert.That(names, Does.Contain(name), "EnabledInSmall");
            }

            foreach (string name in RecEquivalenceScenarios.EnabledInMedium)
            {
                Assert.That(names, Does.Contain(name), "EnabledInMedium");
            }
        }

        private static double DistanceToGrid(double time, double grid)
        {
            double remainder = time % grid;
            return Math.Min(remainder, grid - remainder);
        }

        private static RecEquivalenceScenario CreateScenario()
        {
            return new RecEquivalenceScenarioBuilder("self-test", 0, 1d)
                .TriggerOn(RecEquivalenceScenarioBuilder.OffGrid(0.3d), RecEquivalenceSpec.SmileExpressionId)
                .Build();
        }

        private static IReadOnlyList<EquivalenceChannel> Channels()
        {
            return new[]
            {
                new EquivalenceChannel("Smile", EquivalenceChannelKind.BlendShape),
                new EquivalenceChannel("LeftEye", EquivalenceChannelKind.BoneRotation),
            };
        }

        private static EquivalenceFrameTrace CreateRampTrace(int frames)
        {
            var trace = new EquivalenceFrameTrace(Channels());
            for (int frame = 0; frame < frames; frame++)
            {
                trace.AddFrame(frame / 60d, new[] { frame * 1.5f, 0f, 0f, 0f, 1f });
            }

            return trace;
        }

        private static EquivalenceFrameTrace CreateStepTrace(int frames, int stepFrame)
        {
            var trace = new EquivalenceFrameTrace(Channels());
            for (int frame = 0; frame < frames; frame++)
            {
                trace.AddFrame(frame / 60d, new[] { frame >= stepFrame ? 100f : 0f, 0f, 0f, 0f, 1f });
            }

            return trace;
        }
    }
}
