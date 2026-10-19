using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Hidano.FacialControl.Timeline.Tests.Shared
{
    /// <summary>
    /// 往復検証の 1 ケース分の差分レポート（Markdown / JSON / テスト失敗メッセージ用の要約）。
    /// </summary>
    public sealed class EquivalenceReport
    {
        /// <summary>不一致の前後として載せる REC イベントの範囲（秒）。</summary>
        public const double ContextWindowSeconds = 0.15d;

        private const int WorstChannelCount = 5;

        public EquivalenceReport(
            string tier,
            RecEquivalenceScenario scenario,
            RecEquivalenceFrameStepMode stepMode,
            EquivalenceResult result,
            IReadOnlyList<string> timelineContext)
        {
            Tier = tier ?? string.Empty;
            Scenario = scenario ?? throw new ArgumentNullException(nameof(scenario));
            StepMode = stepMode;
            Result = result ?? throw new ArgumentNullException(nameof(result));
            TimelineContext = timelineContext ?? Array.Empty<string>();
        }

        /// <summary>Small（遷移計算のみ）/ Medium（全経路）。</summary>
        public string Tier { get; }

        public RecEquivalenceScenario Scenario { get; }

        public RecEquivalenceFrameStepMode StepMode { get; }

        public EquivalenceResult Result { get; }

        /// <summary>Timeline 側のトラック・Clip などの説明（不一致の前後を読む手がかり）。</summary>
        public IReadOnlyList<string> TimelineContext { get; }

        /// <summary>ファイル名に使う識別子。</summary>
        public string CaseId => Tier + "_" + Scenario.Name + "_" + StepMode;

        /// <summary>テスト失敗メッセージに載せる要約（CI のジョブログに出る先頭部分）。</summary>
        public string ToSummary()
        {
            var sb = new StringBuilder();
            sb.Append("[REC⇔Timeline ").Append(Tier).Append("] scenario=").Append(Scenario.Name)
                .Append(" seed=").Append(Scenario.Seed)
                .Append(" step=").Append(StepMode)
                .Append(" tags=").Append(string.Join(",", Scenario.Tags))
                .Append('\n');
            if (Result.IsMatch)
            {
                sb.Append("一致（").Append(Result.ComparedFrameCount).Append(" フレーム）\n");
                return sb.ToString();
            }

            if (Result.StructureError != null)
            {
                sb.Append("比較できない: ").Append(Result.StructureError).Append('\n');
                return sb.ToString();
            }

            EquivalenceMismatch first = Result.FirstMismatch.Value;
            sb.Append("最初の不一致: frame ").Append(first.Frame)
                .Append(" t=").Append(Format(first.TimeSeconds, "0.00000"))
                .Append(" channel=").Append(first.ChannelName)
                .Append(" expected(REC)=").Append(first.Expected)
                .Append(" actual(Timeline)=").Append(first.Actual)
                .Append(" diff=").Append(Format(first.Difference, "0.#####"))
                .Append(" (許容 ").Append(Format(first.Tolerance, "0.#####")).Append(")\n");
            sb.Append("不一致フレーム数: ").Append(Result.MismatchFrameCount).Append(" / ").Append(Result.ComparedFrameCount).Append('\n');
            sb.Append("差の大きいチャンネル: ");
            IReadOnlyList<EquivalenceChannelSummary> worst = Result.WorstChannels(WorstChannelCount);
            for (int i = 0; i < worst.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(", ");
                }

                sb.Append(worst[i].Name).Append(' ').Append(Format(worst[i].MaxDifference, "0.####"))
                    .Append("@t=").Append(Format(worst[i].MaxDifferenceTimeSeconds, "0.000"));
            }

            sb.Append('\n');
            return sb.ToString();
        }

        public string ToMarkdown()
        {
            var sb = new StringBuilder();
            sb.Append("# REC ⇔ Timeline 往復検証: ").Append(Scenario.Name).Append(" / ").Append(StepMode).Append(" (").Append(Tier).Append(")\n\n");
            sb.Append("- 結果: ").Append(Result.IsMatch ? "一致" : "不一致").Append('\n');
            sb.Append("- シード: ").Append(Scenario.Seed).Append('\n');
            sb.Append("- タグ: ").Append(string.Join(", ", Scenario.Tags)).Append('\n');
            sb.Append("- 記録の長さ: ").Append(Format(Scenario.DurationSeconds, "0.###")).Append(" s\n");
            sb.Append("- 比較したフレーム数: ").Append(Result.ComparedFrameCount).Append('\n');
            if (Result.StructureError != null)
            {
                sb.Append("- 比較できない: ").Append(Result.StructureError).Append('\n');
            }

            if (Result.FirstMismatch.HasValue)
            {
                EquivalenceMismatch first = Result.FirstMismatch.Value;
                sb.Append("\n## 最初の不一致\n\n");
                sb.Append("| frame | t (s) | channel | 期待 (REC 再生) | 実際 (Timeline) | 差 | 許容 |\n");
                sb.Append("|---|---|---|---|---|---|---|\n");
                sb.Append("| ").Append(first.Frame)
                    .Append(" | ").Append(Format(first.TimeSeconds, "0.00000"))
                    .Append(" | ").Append(first.ChannelName)
                    .Append(" | ").Append(first.Expected)
                    .Append(" | ").Append(first.Actual)
                    .Append(" | ").Append(Format(first.Difference, "0.#####"))
                    .Append(" | ").Append(Format(first.Tolerance, "0.#####"))
                    .Append(" |\n");
                sb.Append("\n不一致フレーム数: ").Append(Result.MismatchFrameCount).Append(" / ").Append(Result.ComparedFrameCount).Append('\n');
            }

            sb.Append("\n## チャンネルごとの最大差\n\n");
            sb.Append("| channel | 最大差 | 最大差の時刻 (s) | 不一致フレーム数 |\n");
            sb.Append("|---|---|---|---|\n");
            for (int i = 0; i < Result.Channels.Count; i++)
            {
                EquivalenceChannelSummary channel = Result.Channels[i];
                sb.Append("| ").Append(channel.Name)
                    .Append(" | ").Append(Format(channel.MaxDifference, "0.#####"))
                    .Append(" | ").Append(Format(channel.MaxDifferenceTimeSeconds, "0.000"))
                    .Append(" | ").Append(channel.MismatchFrameCount)
                    .Append(" |\n");
            }

            IReadOnlyList<RecEquivalenceEventInfo> events = EventsAroundFirstMismatch();
            sb.Append("\n## REC イベント").Append(Result.FirstMismatch.HasValue ? "（最初の不一致の前後）" : "").Append("\n\n");
            for (int i = 0; i < events.Count; i++)
            {
                sb.Append("- ").Append(events[i]).Append('\n');
            }

            if (TimelineContext.Count > 0)
            {
                sb.Append("\n## Timeline\n\n");
                for (int i = 0; i < TimelineContext.Count; i++)
                {
                    sb.Append("- ").Append(TimelineContext[i]).Append('\n');
                }
            }

            return sb.ToString();
        }

        public string ToJson()
        {
            var sb = new StringBuilder();
            sb.Append("{\n");
            AppendJson(sb, "tier", Tier, true);
            AppendJson(sb, "scenario", Scenario.Name, true);
            sb.Append("  \"seed\": ").Append(Scenario.Seed).Append(",\n");
            AppendJson(sb, "step", StepMode.ToString(), true);
            sb.Append("  \"tags\": [");
            for (int i = 0; i < Scenario.Tags.Count; i++)
            {
                sb.Append(i > 0 ? ", " : string.Empty).Append(Quote(Scenario.Tags[i]));
            }

            sb.Append("],\n");
            sb.Append("  \"match\": ").Append(Result.IsMatch ? "true" : "false").Append(",\n");
            sb.Append("  \"comparedFrames\": ").Append(Result.ComparedFrameCount).Append(",\n");
            sb.Append("  \"mismatchFrames\": ").Append(Result.MismatchFrameCount).Append(",\n");
            sb.Append("  \"structureError\": ").Append(Result.StructureError == null ? "null" : Quote(Result.StructureError)).Append(",\n");
            sb.Append("  \"firstMismatch\": ");
            if (Result.FirstMismatch.HasValue)
            {
                EquivalenceMismatch first = Result.FirstMismatch.Value;
                sb.Append("{ \"frame\": ").Append(first.Frame)
                    .Append(", \"time\": ").Append(Format(first.TimeSeconds, "0.000000"))
                    .Append(", \"channel\": ").Append(Quote(first.ChannelName))
                    .Append(", \"expected\": ").Append(Quote(first.Expected))
                    .Append(", \"actual\": ").Append(Quote(first.Actual))
                    .Append(", \"difference\": ").Append(Format(first.Difference, "0.########"))
                    .Append(", \"tolerance\": ").Append(Format(first.Tolerance, "0.########"))
                    .Append(" },\n");
            }
            else
            {
                sb.Append("null,\n");
            }

            sb.Append("  \"channels\": [\n");
            for (int i = 0; i < Result.Channels.Count; i++)
            {
                EquivalenceChannelSummary channel = Result.Channels[i];
                sb.Append("    { \"name\": ").Append(Quote(channel.Name))
                    .Append(", \"kind\": ").Append(Quote(channel.Kind.ToString()))
                    .Append(", \"maxDifference\": ").Append(Format(channel.MaxDifference, "0.########"))
                    .Append(", \"maxDifferenceTime\": ").Append(Format(channel.MaxDifferenceTimeSeconds, "0.000000"))
                    .Append(", \"mismatchFrames\": ").Append(channel.MismatchFrameCount)
                    .Append(i + 1 < Result.Channels.Count ? " },\n" : " }\n");
            }

            sb.Append("  ],\n");
            IReadOnlyList<RecEquivalenceEventInfo> events = EventsAroundFirstMismatch();
            sb.Append("  \"recEvents\": [");
            for (int i = 0; i < events.Count; i++)
            {
                sb.Append(i > 0 ? ", " : string.Empty).Append(Quote(events[i].ToString()));
            }

            sb.Append("],\n");
            sb.Append("  \"timeline\": [");
            for (int i = 0; i < TimelineContext.Count; i++)
            {
                sb.Append(i > 0 ? ", " : string.Empty).Append(Quote(TimelineContext[i]));
            }

            sb.Append("]\n}\n");
            return sb.ToString();
        }

        /// <summary>最初の不一致の前後 <see cref="ContextWindowSeconds"/> 秒の REC イベント（一致なら全件）。</summary>
        public IReadOnlyList<RecEquivalenceEventInfo> EventsAroundFirstMismatch()
        {
            if (!Result.FirstMismatch.HasValue)
            {
                return Scenario.Events;
            }

            double time = Result.FirstMismatch.Value.TimeSeconds;
            var events = new List<RecEquivalenceEventInfo>();
            for (int i = 0; i < Scenario.Events.Count; i++)
            {
                if (Math.Abs(Scenario.Events[i].TimeSeconds - time) <= ContextWindowSeconds)
                {
                    events.Add(Scenario.Events[i]);
                }
            }

            return events;
        }

        private static void AppendJson(StringBuilder sb, string key, string value, bool comma)
        {
            sb.Append("  ").Append(Quote(key)).Append(": ").Append(Quote(value)).Append(comma ? ",\n" : "\n");
        }

        private static string Quote(string value)
        {
            if (value == null)
            {
                return "null";
            }

            var sb = new StringBuilder(value.Length + 2);
            sb.Append('"');
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                switch (c)
                {
                    case '"':
                        sb.Append("\\\"");
                        break;
                    case '\\':
                        sb.Append("\\\\");
                        break;
                    case '\n':
                        sb.Append("\\n");
                        break;
                    case '\r':
                        sb.Append("\\r");
                        break;
                    case '\t':
                        sb.Append("\\t");
                        break;
                    default:
                        if (c < 0x20)
                        {
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            sb.Append(c);
                        }

                        break;
                }
            }

            sb.Append('"');
            return sb.ToString();
        }

        private static string Format(double value, string format)
        {
            if (double.IsPositiveInfinity(value))
            {
                return "1e999";
            }

            return value.ToString(format, CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// 差分レポートをファイルに書く。既定の出力先はリポジトリ直下の <c>test-results/rec-timeline-equivalence/</c>
    /// （CI はこのディレクトリを成果物として上げる）。環境変数 <see cref="DirectoryVariable"/> で上書きできる。
    /// </summary>
    public static class EquivalenceReportWriter
    {
        public const string DirectoryVariable = "FACIALCONTROL_EQUIVALENCE_REPORT_DIR";
        public const string DirectoryName = "rec-timeline-equivalence";

        /// <summary>出力先ディレクトリ。<paramref name="unityDataPath"/> は <c>Application.dataPath</c>（プロジェクトの Assets）。</summary>
        public static string ResolveDirectory(string unityDataPath)
        {
            string overridden = Environment.GetEnvironmentVariable(DirectoryVariable);
            if (!string.IsNullOrWhiteSpace(overridden))
            {
                return overridden;
            }

            // <repo>/FacialControl/Assets → <repo>/test-results/rec-timeline-equivalence
            return Path.GetFullPath(Path.Combine(unityDataPath, "..", "..", "test-results", DirectoryName));
        }

        /// <summary>Markdown と JSON を書き、Markdown のパスを返す。</summary>
        public static string Write(string directory, EquivalenceReport report)
        {
            Directory.CreateDirectory(directory);
            string baseName = Sanitize(report.CaseId);
            string markdownPath = Path.Combine(directory, baseName + ".md");
            File.WriteAllText(markdownPath, report.ToMarkdown(), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(directory, baseName + ".json"), report.ToJson(), new UTF8Encoding(false));
            return markdownPath;
        }

        /// <summary>任意のテキスト（全シナリオの現状調査の一覧など）を書き、パスを返す。</summary>
        public static string WriteText(string directory, string fileName, string text)
        {
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, Sanitize(fileName));
            File.WriteAllText(path, text, new UTF8Encoding(false));
            return path;
        }

        private static string Sanitize(string name)
        {
            var sb = new StringBuilder(name.Length);
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                sb.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.' ? c : '_');
            }

            return sb.ToString();
        }
    }
}
