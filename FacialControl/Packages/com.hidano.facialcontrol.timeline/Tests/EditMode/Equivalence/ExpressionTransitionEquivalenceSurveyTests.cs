using System.Collections.Generic;
using System.Text;
using Hidano.FacialControl.Testing;
using Hidano.FacialControl.Timeline.Tests.Shared;
using NUnit.Framework;
using UnityEngine;

namespace Hidano.FacialControl.Timeline.Tests.EditMode
{
    /// <summary>
    /// 往復検証の Small 層の現状調査。全シナリオ × 全フレームの刻みを回し、差分レポートと一覧を
    /// <c>test-results/rec-timeline-equivalence/</c> に書く。
    /// </summary>
    /// <remarks>
    /// 合否は必須テスト（<see cref="ExpressionTransitionEquivalenceTests"/>）が決める。このテストは現状を記録するためのもので、
    /// 不一致があっても失敗しない（検証の仕組み自体が例外で止まった場合だけ失敗する）。
    /// </remarks>
    [LargeTest]
    public sealed class ExpressionTransitionEquivalenceSurveyTests : SizedTestFixture
    {
        [Test]
        public void Survey_AllScenarios_WritesReports()
        {
            string directory = EquivalenceReportWriter.ResolveDirectory(Application.dataPath);
            var summary = new StringBuilder();
            summary.Append("# REC ⇔ Timeline 往復検証 現状調査（Small 層: 遷移計算のみ）\n\n");
            summary.Append("| scenario | step | tags | 結果 | 最初の不一致 |\n|---|---|---|---|---|\n");

            int mismatches = 0;
            IReadOnlyList<RecEquivalenceScenario> scenarios = RecEquivalenceScenarios.All();
            for (int i = 0; i < scenarios.Count; i++)
            {
                if (!scenarios[i].HasTag(RecEquivalenceTags.ExpressionTransition))
                {
                    continue;
                }

                foreach (RecEquivalenceFrameStepMode mode in ExpressionTransitionEquivalenceTests.StepModes())
                {
                    EquivalenceReport report = ExpressionTransitionEquivalence.Run(scenarios[i], mode);
                    EquivalenceReportWriter.Write(directory, report);
                    if (!report.Result.IsMatch)
                    {
                        mismatches++;
                    }

                    summary.Append("| ").Append(scenarios[i].Name)
                        .Append(" | ").Append(mode)
                        .Append(" | ").Append(string.Join(", ", scenarios[i].Tags))
                        .Append(" | ").Append(report.Result.IsMatch ? "一致" : "不一致")
                        .Append(" | ").Append(DescribeFirstMismatch(report.Result))
                        .Append(" |\n");
                }
            }

            string path = EquivalenceReportWriter.WriteText(directory, "survey-small.md", summary.ToString());
            TestContext.WriteLine(summary.ToString());
            TestContext.WriteLine("survey: " + path + " (不一致 " + mismatches + " 件)");
        }

        internal static string DescribeFirstMismatch(EquivalenceResult result)
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
                System.Globalization.CultureInfo.InvariantCulture,
                "t={0:0.0000} {1} REC={2} Timeline={3} diff={4:0.####}",
                first.TimeSeconds,
                first.ChannelName,
                first.Expected,
                first.Actual,
                first.Difference);
        }
    }
}
