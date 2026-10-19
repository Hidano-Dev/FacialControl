using System.Collections.Generic;
using Hidano.FacialControl.Testing;
using Hidano.FacialControl.Timeline.Tests.Shared;
using NUnit.Framework;

namespace Hidano.FacialControl.Timeline.Tests.EditMode
{
    /// <summary>
    /// 往復検証の Small 層の必須テスト。<see cref="RecEquivalenceScenarios.EnabledInSmall"/> に載せたシナリオを、
    /// 3 通りのフレームの刻みで全フレーム比べる。
    /// </summary>
    [SmallTest]
    public sealed class ExpressionTransitionEquivalenceTests : SizedTestFixture
    {
        private const string NoneEnabled = "(none-enabled)";

        public static IEnumerable<TestCaseData> EnabledCases()
        {
            bool any = false;
            IReadOnlyList<string> names = RecEquivalenceScenarios.AllNames();
            for (int i = 0; i < names.Count; i++)
            {
                if (!RecEquivalenceScenarios.EnabledInSmall.Contains(names[i]))
                {
                    continue;
                }

                any = true;
                foreach (RecEquivalenceFrameStepMode mode in StepModes())
                {
                    yield return new TestCaseData(names[i], mode).SetName($"Small_{names[i]}_{mode}");
                }
            }

            if (!any)
            {
                // 有効なシナリオが 0 件でもテストケースを 1 件出す（空の TestCaseSource をランナーに失敗扱いされないため）。
                yield return new TestCaseData(NoneEnabled, RecEquivalenceFrameStepMode.Fixed60).SetName("Small_NoScenarioEnabled");
            }
        }

        [TestCaseSource(nameof(EnabledCases))]
        public void ExpressionTransitions_MatchRecPlaybackEveryFrame(string scenarioName, RecEquivalenceFrameStepMode mode)
        {
            if (scenarioName == NoneEnabled)
            {
                Assert.Pass("Small 層の必須シナリオはまだありません（RecEquivalenceScenarios.EnabledInSmall）。");
            }

            EquivalenceReport report = ExpressionTransitionEquivalence.Run(RecEquivalenceScenarios.Get(scenarioName), mode);
            Assert.That(report.Result.IsMatch, Is.True, report.ToSummary() + "\n" + report.ToMarkdown());
        }

        internal static IEnumerable<RecEquivalenceFrameStepMode> StepModes()
        {
            yield return RecEquivalenceFrameStepMode.Fixed60;
            yield return RecEquivalenceFrameStepMode.Fixed120;
            yield return RecEquivalenceFrameStepMode.Jitter;
        }
    }
}
