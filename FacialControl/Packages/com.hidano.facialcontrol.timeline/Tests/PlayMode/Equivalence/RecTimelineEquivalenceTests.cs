#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using Hidano.FacialControl.Testing;
using Hidano.FacialControl.Timeline.Tests.Shared;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Hidano.FacialControl.Timeline.Tests.PlayMode
{
    /// <summary>
    /// REC → Timeline 往復検証の必須テスト（Medium）。同じ .fcrec を REC 再生で流した毎フレームの出力（正解）と、
    /// REC Export した TimelineAsset を再生した毎フレームの出力を、全 BlendShape と左右の目ボーンで比べる。
    /// </summary>
    /// <remarks>
    /// <para>回すのは <see cref="RecEquivalenceScenarios.EnabledInMedium"/> に載せたシナリオだけ。全シナリオの現状は
    /// <see cref="RecTimelineEquivalenceSurveyTests"/>（Large）で調べる。</para>
    /// <para>不一致のときは差分レポートを <c>test-results/rec-timeline-equivalence/</c> に書き、失敗メッセージの先頭に要約を出す。</para>
    /// </remarks>
    [TestFixture]
    [MediumTest]
    public sealed class RecTimelineEquivalenceTests : SizedTestFixture
    {
        private const string NoneEnabled = "(none-enabled)";

        /// <summary>仕組み自体の検査に使うシナリオ（表情・目線・Analog・レイヤー weight を含む）。</summary>
        private const string HarnessScenario = "random-mix-1";

        private RecTimelineEquivalenceFixture _fixture;
        private float _previousCaptureDeltaTime;

        [SetUp]
        public void SetUp()
        {
            _previousCaptureDeltaTime = Time.captureDeltaTime;
        }

        [TearDown]
        public void TearDown()
        {
            Time.captureDeltaTime = _previousCaptureDeltaTime;
            _fixture?.Dispose();
            _fixture = null;
        }

        public static IEnumerable<TestCaseData> EnabledCases()
        {
            bool any = false;
            IReadOnlyList<string> names = RecEquivalenceScenarios.AllNames();
            for (int i = 0; i < names.Count; i++)
            {
                if (!RecEquivalenceScenarios.EnabledInMedium.Contains(names[i]))
                {
                    continue;
                }

                any = true;
                foreach (RecEquivalenceFrameStepMode mode in StepModes())
                {
                    yield return new TestCaseData(names[i], mode).SetName($"Medium_{names[i]}_{mode}");
                }
            }

            if (!any)
            {
                // 有効なシナリオが 0 件でもテストケースを 1 件出す（空の TestCaseSource をランナーに失敗扱いされないため）。
                yield return new TestCaseData(NoneEnabled, RecEquivalenceFrameStepMode.Fixed60).SetName("Medium_NoScenarioEnabled");
            }
        }

        [UnityTest]
        [TestCaseSource(nameof(EnabledCases))]
        public IEnumerator Timeline_MatchesRecPlaybackEveryFrame(string scenarioName, RecEquivalenceFrameStepMode mode)
        {
            if (scenarioName == NoneEnabled)
            {
                // Medium の必須シナリオはまだない（RecEquivalenceScenarios.EnabledInMedium）。
                yield break;
            }

            var result = new RecTimelineEquivalenceRun();
            yield return result.Execute(RecEquivalenceScenarios.Get(scenarioName), mode, f => _fixture = f);

            EquivalenceReport report = result.Report;
            if (!report.Result.IsMatch)
            {
                string path = EquivalenceReportWriter.Write(EquivalenceReportWriter.ResolveDirectory(Application.dataPath), report);
                Assert.Fail(report.ToSummary() + "差分レポート: " + path + "\n\n" + report.ToMarkdown());
            }
        }

        /// <summary>同じシナリオの REC 再生を 2 回記録すると、全フレームが一致する（正解側が決定的であること）。</summary>
        [UnityTest]
        public IEnumerator Harness_RecPlaybackTwice_IsDeterministic()
        {
            _fixture = RecTimelineEquivalenceFixture.Create(RecEquivalenceScenarios.Get(HarnessScenario));
            var first = new EquivalenceFrameTrace(RecTimelineEquivalenceFixture.Channels());
            var second = new EquivalenceFrameTrace(RecTimelineEquivalenceFixture.Channels());

            yield return _fixture.CaptureRecPlayback(RecEquivalenceFrameStepMode.Fixed60, first);
            yield return _fixture.CaptureRecPlayback(RecEquivalenceFrameStepMode.Fixed60, second);

            EquivalenceResult result = EquivalenceComparer.Compare(first, second);
            Assert.That(result.IsMatch, Is.True, Summarize("REC 再生が決定的でない", result));
            Assert.That(first.FrameCount, Is.GreaterThan(100), "前提: 記録の長さ分のフレームを記録している");
        }

        /// <summary>
        /// 同じ Timeline を 2 回記録すると一致し、1 フレーム先の時刻を評価した記録とは不一致になる
        /// （わざと壊した入力を比較が正しく不一致と判定すること）。
        /// </summary>
        [UnityTest]
        public IEnumerator Harness_TimelineShiftedByOneFrame_IsReportedAsMismatch()
        {
            _fixture = RecTimelineEquivalenceFixture.Create(RecEquivalenceScenarios.Get(HarnessScenario));
            var times = new List<double>();
            for (int frame = 0; frame / 60d <= _fixture.Scenario.DurationSeconds; frame++)
            {
                times.Add(frame / 60d);
            }

            var first = new EquivalenceFrameTrace(RecTimelineEquivalenceFixture.Channels());
            var second = new EquivalenceFrameTrace(RecTimelineEquivalenceFixture.Channels());
            var shifted = new EquivalenceFrameTrace(RecTimelineEquivalenceFixture.Channels());
            yield return _fixture.CaptureTimeline(times, first);
            yield return _fixture.CaptureTimeline(times, second);
            yield return _fixture.CaptureTimeline(times, shifted, frameShift: 1);

            EquivalenceResult same = EquivalenceComparer.Compare(first, second);
            Assert.That(same.IsMatch, Is.True, Summarize("Timeline 再生が決定的でない", same));

            EquivalenceResult broken = EquivalenceComparer.Compare(first, shifted);
            Assert.That(broken.StructureError, Is.Null);
            Assert.That(broken.IsMatch, Is.False, "1 フレームずらした Timeline を不一致と判定できない");
            Assert.That(broken.FirstMismatch.HasValue, Is.True);

            var report = new EquivalenceReport("Medium", _fixture.Scenario, RecEquivalenceFrameStepMode.Fixed60, broken, _fixture.DescribeTimeline());
            StringAssert.Contains("最初の不一致: frame " + broken.FirstMismatch.Value.Frame, report.ToSummary());
            StringAssert.Contains("\"match\": false", report.ToJson());
        }

        internal static IEnumerable<RecEquivalenceFrameStepMode> StepModes()
        {
            yield return RecEquivalenceFrameStepMode.Fixed60;
            yield return RecEquivalenceFrameStepMode.Fixed120;
            yield return RecEquivalenceFrameStepMode.Jitter;
        }

        private static string Summarize(string title, EquivalenceResult result)
        {
            if (result.StructureError != null)
            {
                return title + ": " + result.StructureError;
            }

            if (!result.FirstMismatch.HasValue)
            {
                return title;
            }

            EquivalenceMismatch first = result.FirstMismatch.Value;
            return $"{title}: frame {first.Frame} t={first.TimeSeconds:0.00000} {first.ChannelName} {first.Expected} / {first.Actual}";
        }
    }

    /// <summary>1 シナリオ × 1 フレームの刻みの往復検証を実行する（必須テストと現状調査で共用）。</summary>
    internal sealed class RecTimelineEquivalenceRun
    {
        public const string Tier = "Medium";

        public EquivalenceReport Report { get; private set; }

        /// <param name="onFixtureCreated">作った fixture を呼び出し側の後片付けに渡す。</param>
        public IEnumerator Execute(
            RecEquivalenceScenario scenario,
            RecEquivalenceFrameStepMode mode,
            System.Action<RecTimelineEquivalenceFixture> onFixtureCreated)
        {
            RecTimelineEquivalenceFixture fixture = RecTimelineEquivalenceFixture.Create(scenario);
            onFixtureCreated?.Invoke(fixture);

            var expected = new EquivalenceFrameTrace(RecTimelineEquivalenceFixture.Channels());
            yield return fixture.CaptureRecPlayback(mode, expected);

            var times = new double[expected.FrameCount];
            for (int i = 0; i < times.Length; i++)
            {
                times[i] = expected.GetTime(i);
            }

            var actual = new EquivalenceFrameTrace(RecTimelineEquivalenceFixture.Channels());
            yield return fixture.CaptureTimeline(times, actual);

            EquivalenceResult result = EquivalenceComparer.Compare(expected, actual);
            Report = new EquivalenceReport(Tier, scenario, mode, result, fixture.DescribeTimeline());
        }
    }
}
#endif
