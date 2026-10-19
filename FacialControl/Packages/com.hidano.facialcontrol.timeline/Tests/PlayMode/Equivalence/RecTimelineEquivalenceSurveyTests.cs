#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Hidano.FacialControl.Testing;
using Hidano.FacialControl.Timeline.Tests.Shared;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Hidano.FacialControl.Timeline.Tests.PlayMode
{
    /// <summary>
    /// REC → Timeline 往復検証の全シナリオの現状調査（Large）。全シナリオ × 全フレームの刻みを回し、差分レポートと一覧
    /// （<c>survey-medium.md</c>）を <c>test-results/rec-timeline-equivalence/</c> に書く。
    /// </summary>
    /// <remarks>
    /// 合否は必須テスト（<see cref="RecTimelineEquivalenceTests"/>）が決める。このテストは現状を記録するためのもので、
    /// 不一致があっても失敗しない（検証の仕組み自体が例外で止まった場合だけ失敗する）。CI の Large ジョブ（手動・夜間）で回る。
    /// </remarks>
    [TestFixture]
    [LargeTest]
    public sealed class RecTimelineEquivalenceSurveyTests : SizedTestFixture
    {
        private readonly List<RecTimelineEquivalenceFixture> _fixtures = new List<RecTimelineEquivalenceFixture>();
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
            for (int i = 0; i < _fixtures.Count; i++)
            {
                _fixtures[i]?.Dispose();
            }

            _fixtures.Clear();
        }

        [UnityTest]
        public IEnumerator Survey_AllScenarios_WritesReports()
        {
            string directory = EquivalenceReportWriter.ResolveDirectory(UnityEngine.Application.dataPath);
            var summary = new StringBuilder();
            summary.Append("# REC ⇔ Timeline 往復検証 現状調査（Medium: REC 再生 ⇔ Timeline 再生）\n\n");
            summary.Append("| scenario | step | tags | 必須 | 結果 | 最初の不一致 | 差の大きいチャンネル |\n|---|---|---|---|---|---|---|\n");

            int mismatches = 0;
            IReadOnlyList<RecEquivalenceScenario> scenarios = RecEquivalenceScenarios.All();
            for (int i = 0; i < scenarios.Count; i++)
            {
                foreach (RecEquivalenceFrameStepMode mode in RecTimelineEquivalenceTests.StepModes())
                {
                    var run = new RecTimelineEquivalenceRun();
                    yield return run.Execute(scenarios[i], mode, f => _fixtures.Add(f));
                    EquivalenceReport report = run.Report;
                    EquivalenceReportWriter.Write(directory, report);
                    if (!report.Result.IsMatch)
                    {
                        mismatches++;
                    }

                    summary.Append("| ").Append(scenarios[i].Name)
                        .Append(" | ").Append(mode)
                        .Append(" | ").Append(string.Join(", ", scenarios[i].Tags))
                        .Append(" | ").Append(RecEquivalenceScenarios.EnabledInMedium.Contains(scenarios[i].Name) ? "○" : "-")
                        .Append(" | ").Append(report.Result.IsMatch ? "一致" : "不一致")
                        .Append(" | ").Append(DescribeFirstMismatch(report.Result))
                        .Append(" | ").Append(DescribeWorst(report.Result))
                        .Append(" |\n");

                    // シナリオごとに生成物を消す（Assets と StreamingAssets を溜めない）。
                    for (int f = 0; f < _fixtures.Count; f++)
                    {
                        _fixtures[f].Dispose();
                    }

                    _fixtures.Clear();
                }
            }

            string path = EquivalenceReportWriter.WriteText(directory, "survey-medium.md", summary.ToString());
            TestContext.WriteLine(summary.ToString());
            TestContext.WriteLine("survey: " + path + " (不一致 " + mismatches + " 件)");
        }

        private static string DescribeFirstMismatch(EquivalenceResult result)
        {
            if (result.StructureError != null)
            {
                return "比較できない: " + result.StructureError;
            }

            if (!result.FirstMismatch.HasValue)
            {
                return "-";
            }

            EquivalenceMismatch first = result.FirstMismatch.Value;
            return string.Format(
                CultureInfo.InvariantCulture,
                "t={0:0.0000} {1} REC={2} Timeline={3} diff={4:0.####}",
                first.TimeSeconds,
                first.ChannelName,
                first.Expected,
                first.Actual,
                first.Difference);
        }

        private static string DescribeWorst(EquivalenceResult result)
        {
            IReadOnlyList<EquivalenceChannelSummary> worst = result.WorstChannels(3);
            var parts = new string[worst.Count];
            for (int i = 0; i < worst.Count; i++)
            {
                parts[i] = string.Format(CultureInfo.InvariantCulture, "{0} {1:0.###}", worst[i].Name, worst[i].MaxDifference);
            }

            return parts.Length == 0 ? "-" : string.Join(", ", parts);
        }
    }
}
#endif
