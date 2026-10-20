using System;
using System.Collections.Generic;

namespace Hidano.FacialControl.Timeline.Tests.Shared
{
    /// <summary>
    /// 往復検証のシナリオ一覧。どれもシード付きで決定的に生成し、時刻は 1/60・1/120 秒の格子から外す
    /// （<see cref="RecEquivalenceScenarioBuilder.OffGrid"/>）。
    /// </summary>
    /// <remarks>
    /// 必須のテスト（Small / Medium）が回すのは <see cref="EnabledInSmall"/> / <see cref="EnabledInMedium"/> に載せた名前だけ。
    /// 載っていないシナリオは全シナリオの現状調査（Large）で回し、差分レポートに出す。
    /// 既知の差を直す Issue は、自分の差に当たるシナリオをここに足し、必須のテストで一致させる。
    /// </remarks>
    public static class RecEquivalenceScenarios
    {
        /// <summary>Small 層（遷移計算のみ）の必須テストで回すシナリオ。</summary>
        public static readonly HashSet<string> EnabledInSmall = new HashSet<string>(StringComparer.Ordinal)
        {
        };

        /// <summary>Medium（REC 再生 ⇔ Timeline 再生の全経路）の必須テストで回すシナリオ。</summary>
        public static readonly HashSet<string> EnabledInMedium = new HashSet<string>(StringComparer.Ordinal)
        {
            "analog-ramp",
            "gaze-dense",
            "gaze-sparse",
        };

        private static readonly Func<RecEquivalenceScenario>[] Factories =
        {
            ExpressionSwitchBasic,
            ExpressionInterrupt,
            ExpressionHoldFallback,
            ExpressionZeroDuration,
            GazeDense,
            GazeSparse,
            AnalogRamp,
            ValueProviderRecords,
            LayerWeightOverExpression,
            BaselineTrigger,
            () => RandomMix(1),
            () => RandomMix(2),
            () => RandomMix(3),
        };

        /// <summary>全シナリオを生成する（呼ぶたびに新しいインスタンス）。</summary>
        public static IReadOnlyList<RecEquivalenceScenario> All()
        {
            var scenarios = new List<RecEquivalenceScenario>(Factories.Length);
            for (int i = 0; i < Factories.Length; i++)
            {
                scenarios.Add(Factories[i]());
            }

            return scenarios;
        }

        /// <summary>全シナリオの名前（テストケース名用）。</summary>
        public static IReadOnlyList<string> AllNames()
        {
            IReadOnlyList<RecEquivalenceScenario> scenarios = All();
            var names = new string[scenarios.Count];
            for (int i = 0; i < names.Length; i++)
            {
                names[i] = scenarios[i].Name;
            }

            return names;
        }

        public static RecEquivalenceScenario Get(string name)
        {
            IReadOnlyList<RecEquivalenceScenario> scenarios = All();
            for (int i = 0; i < scenarios.Count; i++)
            {
                if (string.Equals(scenarios[i].Name, name, StringComparison.Ordinal))
                {
                    return scenarios[i];
                }
            }

            throw new ArgumentException("unknown scenario: " + name, nameof(name));
        }

        private static double T(double seconds)
        {
            return RecEquivalenceScenarioBuilder.OffGrid(seconds);
        }

        /// <summary>切り替え（off と on が同時）、短い隙間の後の on、遷移時間 0 の表情。</summary>
        private static RecEquivalenceScenario ExpressionSwitchBasic()
        {
            return new RecEquivalenceScenarioBuilder("expr-switch-basic", 0, 3.2d)
                .TriggerOn(T(0.20), RecEquivalenceSpec.SmileExpressionId)
                .TriggerOff(T(1.00), RecEquivalenceSpec.SmileExpressionId)
                .TriggerOn(T(1.00), RecEquivalenceSpec.AngryExpressionId)
                .TriggerOff(T(1.60), RecEquivalenceSpec.AngryExpressionId)
                .TriggerOn(T(1.65), RecEquivalenceSpec.SurpriseExpressionId)
                .TriggerOff(T(2.30), RecEquivalenceSpec.SurpriseExpressionId)
                .TriggerOn(T(2.50), RecEquivalenceSpec.CalmExpressionId)
                .TriggerOff(T(2.90), RecEquivalenceSpec.CalmExpressionId)
                .Build();
        }

        /// <summary>遷移の途中で次の表情が来る（数フレームおきの押下・解放、スタックの戻り）。</summary>
        private static RecEquivalenceScenario ExpressionInterrupt()
        {
            return new RecEquivalenceScenarioBuilder("expr-interrupt", 0, 2.0d)
                .TriggerOn(T(0.10), RecEquivalenceSpec.SmileExpressionId)
                .TriggerOn(T(0.22), RecEquivalenceSpec.AngryExpressionId)
                .TriggerOff(T(0.27), RecEquivalenceSpec.AngryExpressionId)
                .TriggerOn(T(0.33), RecEquivalenceSpec.SurpriseExpressionId)
                .TriggerOff(T(0.33), RecEquivalenceSpec.SmileExpressionId)
                .TriggerOn(T(0.38), RecEquivalenceSpec.AngryExpressionId)
                .TriggerOff(T(0.41), RecEquivalenceSpec.SurpriseExpressionId)
                .TriggerOn(T(0.47), RecEquivalenceSpec.CalmExpressionId)
                .TriggerOff(T(0.52), RecEquivalenceSpec.CalmExpressionId)
                .TriggerOff(T(0.70), RecEquivalenceSpec.AngryExpressionId)
                .TriggerOn(T(0.72), RecEquivalenceSpec.SmileExpressionId)
                .TriggerOff(T(1.30), RecEquivalenceSpec.SmileExpressionId)
                .Build();
        }

        /// <summary>押したまま別の表情を押して離す（下の表情への戻り）。</summary>
        private static RecEquivalenceScenario ExpressionHoldFallback()
        {
            return new RecEquivalenceScenarioBuilder("expr-hold-fallback", 0, 2.6d)
                .TriggerOn(T(0.20), RecEquivalenceSpec.SmileExpressionId)
                .TriggerOn(T(0.80), RecEquivalenceSpec.AngryExpressionId)
                .TriggerOff(T(1.30), RecEquivalenceSpec.AngryExpressionId)
                .TriggerOn(T(1.50), RecEquivalenceSpec.SurpriseExpressionId)
                .TriggerOff(T(1.70), RecEquivalenceSpec.SurpriseExpressionId)
                .TriggerOff(T(2.00), RecEquivalenceSpec.SmileExpressionId)
                .Build();
        }

        /// <summary>遷移時間 0 の表情の素早い on/off と、遷移のある表情の組み合わせ。</summary>
        private static RecEquivalenceScenario ExpressionZeroDuration()
        {
            var builder = new RecEquivalenceScenarioBuilder("expr-zero-duration", 0, 2.0d);
            double t = 0.1d;
            for (int i = 0; i < 6; i++)
            {
                builder.TriggerOn(T(t), RecEquivalenceSpec.CalmExpressionId);
                builder.TriggerOff(T(t + 0.05d), RecEquivalenceSpec.CalmExpressionId);
                t += 0.09d;
            }

            return builder
                .TriggerOn(T(0.80), RecEquivalenceSpec.SmileExpressionId)
                .TriggerOn(T(1.00), RecEquivalenceSpec.CalmExpressionId)
                .TriggerOff(T(1.20), RecEquivalenceSpec.CalmExpressionId)
                .TriggerOff(T(1.50), RecEquivalenceSpec.SmileExpressionId)
                .Build();
        }

        /// <summary>目線の速い往復（約 11 ms おきのサンプル）と端への振り切り。</summary>
        private static RecEquivalenceScenario GazeDense()
        {
            var builder = new RecEquivalenceScenarioBuilder("gaze-dense", 0, 2.0d);
            for (int i = 0; ; i++)
            {
                double t = T(0.05d + i * 0.011d);
                if (t > 1.9d)
                {
                    break;
                }

                float x = (float)Math.Sin(t * 9d);
                float y = (float)Math.Cos(t * 5d) * 0.8f;
                if (i % 23 == 0)
                {
                    x = i % 2 == 0 ? 1f : -1f;
                    y = i % 3 == 0 ? 1f : -1f;
                }

                builder.Gaze(t, x, y);
            }

            return builder.Build();
        }

        /// <summary>目線のまばらなサンプル（0.1 s おき。サンプルの間のフレームの扱いを見る）。</summary>
        private static RecEquivalenceScenario GazeSparse()
        {
            var random = new Random(41);
            var builder = new RecEquivalenceScenarioBuilder("gaze-sparse", 41, 2.0d);
            for (int i = 0; i < 18; i++)
            {
                builder.Gaze(T(0.05d + i * 0.1d), (float)(random.NextDouble() * 2d - 1d), (float)(random.NextDouble() * 2d - 1d));
            }

            return builder.Build();
        }

        /// <summary>Analog の上下（20 ms おき）と、まばらな跳び。</summary>
        private static RecEquivalenceScenario AnalogRamp()
        {
            var builder = new RecEquivalenceScenarioBuilder("analog-ramp", 0, 2.2d);
            for (int i = 0; i <= 50; i++)
            {
                double phase = i / 50d;
                builder.Analog(T(0.05d + i * 0.02d), (float)(phase <= 0.5d ? phase * 2d : (1d - phase) * 2d));
            }

            return builder
                .Analog(T(1.30), 0.9f)
                .Analog(T(1.55), 0.1f)
                .Analog(T(1.80), 1f)
                .Analog(T(2.00), 0f)
                .Build();
        }

        /// <summary>値提供型: 基準・値だけの更新・mask の変更・無効化・再有効化。</summary>
        private static RecEquivalenceScenario ValueProviderRecords()
        {
            // メッシュの index 5 / 6 / 7（Blink / JawOpen / EyeWide）。values は mask の立った順。
            const byte blinkJaw = 0b0110_0000;
            const byte jawEye = 0b1100_0000;
            return new RecEquivalenceScenarioBuilder("value-provider", 0, 2.0d)
                .ValueProviderBaseline(true, new[] { blinkJaw }, new[] { 0.2f, 0.4f })
                .ValueProvider(T(0.31), true, null, new[] { 0.5f, 0.1f })
                .ValueProvider(T(0.61), true, new[] { jawEye }, new[] { 0.7f, 0.9f })
                .ValueProvider(T(0.91), false, null, null)
                .ValueProvider(T(1.21), true, null, new[] { 0.3f, 0.6f })
                .ValueProvider(T(1.51), true, new[] { blinkJaw }, new[] { 1f, 0f })
                .ValueProvider(T(1.81), true, null, new[] { 0.25f, 0.75f })
                .Build();
        }

        /// <summary>表情を出したまま emotion レイヤーの weight を上下させる（発話ゲートと同じ形）。</summary>
        private static RecEquivalenceScenario LayerWeightOverExpression()
        {
            return new RecEquivalenceScenarioBuilder("layer-weight", 0, 2.0d)
                .TriggerOn(T(0.10), RecEquivalenceSpec.SmileExpressionId)
                .LayerWeight(T(0.55), 0.3f)
                .LayerWeight(T(0.80), 0f)
                .LayerWeight(T(1.05), 1f)
                .LayerWeight(T(1.30), 0.6f)
                .TriggerOff(T(1.60), RecEquivalenceSpec.SmileExpressionId)
                .Build();
        }

        /// <summary>録画開始時に押されていた表情（基準 kind 5）。</summary>
        private static RecEquivalenceScenario BaselineTrigger()
        {
            return new RecEquivalenceScenarioBuilder("baseline-trigger", 0, 2.0d)
                .BaselineTrigger(RecEquivalenceSpec.SmileExpressionId)
                .TriggerOff(T(0.60), RecEquivalenceSpec.SmileExpressionId)
                .TriggerOn(T(0.90), RecEquivalenceSpec.AngryExpressionId)
                .TriggerOff(T(1.40), RecEquivalenceSpec.AngryExpressionId)
                .Build();
        }

        /// <summary>表情・目線・Analog・レイヤー weight を乱数で混ぜた 3 秒の記録。</summary>
        private static RecEquivalenceScenario RandomMix(int seed)
        {
            const double duration = 3d;
            var random = new Random(seed);
            var builder = new RecEquivalenceScenarioBuilder("random-mix-" + seed, seed, duration);
            var held = new List<string>();
            string[] expressions = RecEquivalenceSpec.TriggerExpressionIds;

            double t = 0.05d;
            while (true)
            {
                t += 0.01d + random.NextDouble() * 0.2d;
                if (t > duration - 0.4d)
                {
                    break;
                }

                double time = T(t);
                int action = random.Next(10);
                if (action < 5)
                {
                    bool release = held.Count > 0 && (held.Count >= 3 || random.Next(2) == 0);
                    if (release)
                    {
                        string expressionId = held[random.Next(held.Count)];
                        held.Remove(expressionId);
                        builder.TriggerOff(time, expressionId);
                    }
                    else
                    {
                        string expressionId = expressions[random.Next(expressions.Length)];
                        if (!held.Contains(expressionId))
                        {
                            held.Add(expressionId);
                            builder.TriggerOn(time, expressionId);
                        }
                    }
                }
                else if (action < 7)
                {
                    builder.Gaze(time, (float)(random.NextDouble() * 2d - 1d), (float)(random.NextDouble() * 2d - 1d));
                }
                else if (action < 9)
                {
                    builder.Analog(time, (float)random.NextDouble());
                }
                else
                {
                    builder.LayerWeight(time, (float)Math.Round(random.NextDouble(), 2));
                }
            }

            double releaseTime = duration - 0.35d;
            for (int i = 0; i < held.Count; i++)
            {
                builder.TriggerOff(T(releaseTime + i * 0.02d), held[i]);
            }

            return builder.Build();
        }
    }
}
