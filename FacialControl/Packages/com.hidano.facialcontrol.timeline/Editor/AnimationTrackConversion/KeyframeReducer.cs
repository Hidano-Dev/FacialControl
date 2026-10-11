using System;
using System.Collections.Generic;

namespace Hidano.FacialControl.Timeline.Editor.AnimationTrackConversion
{
    /// <summary>
    /// 間引き後に残すキー 1 つ分（元サンプルの index と、次のキーまで値を保持する段差か）。
    /// </summary>
    internal readonly struct ReducedKey
    {
        public ReducedKey(int sampleIndex, bool holdUntilNext)
        {
            SampleIndex = sampleIndex;
            HoldUntilNext = holdUntilNext;
        }

        /// <summary>元サンプルの index。</summary>
        public int SampleIndex { get; }

        /// <summary>true なら次のキーまで値を保持し、次のキーで段差になる（右接線 Constant）。false なら次のキーへ直線（Linear）。</summary>
        public bool HoldUntilNext { get; }
    }

    /// <summary>
    /// 間引きの誤差の測り方（1 チャネル分。BlendShape は 1 成分、回転は 4 成分の quaternion）。
    /// </summary>
    internal interface IKeyframeChannelMetric
    {
        /// <summary>サンプル <paramref name="a"/> と <paramref name="b"/> を直線で結んだとき、サンプル <paramref name="i"/> の時刻でのずれ。</summary>
        float DeviationFromLine(int a, int b, int i);

        /// <summary>サンプル <paramref name="a"/> の値を保持したとき、サンプル <paramref name="i"/> とのずれ。</summary>
        float DeviationFromHold(int a, int i);
    }

    /// <summary>
    /// 毎フレームのサンプル列から、直線で結んだときのずれが閾値以下の中間キーを消す（HID-190）。
    /// </summary>
    /// <remarks>
    /// <para>段差（<c>stepBefore[i]</c> が true のサンプル i の直前で値が跳ぶ）でサンプル列を区間に分け、区間ごとに
    /// Douglas–Peucker で間引く。区間の両端は必ず残すので、段差の前後の値はそのまま保たれる。</para>
    /// <para>段差の直前の区間は、最後に残ったキーから区間末尾まで値が一定（保持のずれが閾値以下）なら区間末尾のキーを消し、
    /// 残ったキーから次の区間の先頭まで値を保持する（右接線 Constant）。一定でなければ区間末尾のキーで保持する。</para>
    /// </remarks>
    internal static class KeyframeReducer
    {
        public static List<ReducedKey> Reduce(
            IReadOnlyList<bool> stepBefore,
            int sampleCount,
            IKeyframeChannelMetric metric,
            float tolerance)
        {
            if (metric == null)
            {
                throw new ArgumentNullException(nameof(metric));
            }

            var keys = new List<ReducedKey>();
            if (sampleCount <= 0)
            {
                return keys;
            }

            var kept = new List<int>();
            var stack = new Stack<(int start, int end)>();
            int segmentStart = 0;
            while (segmentStart < sampleCount)
            {
                int segmentEnd = segmentStart;
                while (segmentEnd + 1 < sampleCount && !IsStep(stepBefore, segmentEnd + 1))
                {
                    segmentEnd++;
                }

                kept.Clear();
                ReduceSegment(segmentStart, segmentEnd, metric, tolerance, kept, stack);
                bool endsWithStep = segmentEnd + 1 < sampleCount;
                if (endsWithStep && kept.Count >= 2 && IsHold(kept[kept.Count - 2], segmentEnd, metric, tolerance))
                {
                    kept.RemoveAt(kept.Count - 1);
                }

                for (int i = 0; i < kept.Count; i++)
                {
                    bool last = i == kept.Count - 1;
                    keys.Add(new ReducedKey(kept[i], last && endsWithStep));
                }

                segmentStart = segmentEnd + 1;
            }

            return keys;
        }

        private static bool IsStep(IReadOnlyList<bool> stepBefore, int index)
        {
            return stepBefore != null && index < stepBefore.Count && stepBefore[index];
        }

        private static bool IsHold(int from, int to, IKeyframeChannelMetric metric, float tolerance)
        {
            for (int i = from + 1; i <= to; i++)
            {
                if (metric.DeviationFromHold(from, i) > tolerance)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>区間 [start, end] を Douglas–Peucker で間引き、残すサンプル index を昇順で <paramref name="kept"/> に入れる。</summary>
        private static void ReduceSegment(
            int start,
            int end,
            IKeyframeChannelMetric metric,
            float tolerance,
            List<int> kept,
            Stack<(int start, int end)> stack)
        {
            kept.Add(start);
            if (end == start)
            {
                return;
            }

            // 右側の区間を先に積み、左側から処理することで kept を昇順に保つ。
            stack.Clear();
            stack.Push((start, end));
            while (stack.Count > 0)
            {
                (int a, int b) = stack.Pop();
                int worst = -1;
                float worstDeviation = tolerance;
                for (int i = a + 1; i < b; i++)
                {
                    float deviation = metric.DeviationFromLine(a, b, i);
                    if (deviation > worstDeviation)
                    {
                        worstDeviation = deviation;
                        worst = i;
                    }
                }

                if (worst < 0)
                {
                    kept.Add(b);
                    continue;
                }

                stack.Push((worst, b));
                stack.Push((a, worst));
            }
        }
    }

    /// <summary>1 成分の値（BlendShape weight 等）の誤差。ずれは値の差の絶対値。</summary>
    internal sealed class ScalarChannelMetric : IKeyframeChannelMetric
    {
        private readonly IReadOnlyList<double> _times;
        private readonly IReadOnlyList<float> _values;

        public ScalarChannelMetric(IReadOnlyList<double> times, IReadOnlyList<float> values)
        {
            _times = times ?? throw new ArgumentNullException(nameof(times));
            _values = values ?? throw new ArgumentNullException(nameof(values));
        }

        public float DeviationFromLine(int a, int b, int i)
        {
            double span = _times[b] - _times[a];
            double u = span > 0d ? (_times[i] - _times[a]) / span : 0d;
            double interpolated = _values[a] + ((_values[b] - _values[a]) * u);
            return (float)Math.Abs(_values[i] - interpolated);
        }

        public float DeviationFromHold(int a, int i)
        {
            return Math.Abs(_values[i] - _values[a]);
        }
    }

    /// <summary>
    /// 回転（quaternion の x, y, z, w を 4 成分ずつ並べた列）の誤差。ずれは角度（度）。
    /// 直線は成分ごとの線形補間を正規化したもの（AnimationClip が <c>m_LocalRotation.*</c> の 4 カーブを評価するのと同じ）。
    /// </summary>
    /// <remarks>
    /// 呼出側は隣り合うサンプルの内積が負にならないよう符号を揃えておく（<see cref="AlignHemispheres"/>）。
    /// </remarks>
    internal sealed class QuaternionChannelMetric : IKeyframeChannelMetric
    {
        private const double RadiansToDegrees = 180d / Math.PI;

        private readonly IReadOnlyList<double> _times;
        private readonly IReadOnlyList<float> _components;

        public QuaternionChannelMetric(IReadOnlyList<double> times, IReadOnlyList<float> components)
        {
            _times = times ?? throw new ArgumentNullException(nameof(times));
            _components = components ?? throw new ArgumentNullException(nameof(components));
        }

        /// <summary>隣り合うサンプルの内積が負なら後ろのサンプルの符号を反転し、補間が遠回りしないようにする。</summary>
        public static void AlignHemispheres(IList<float> components)
        {
            for (int i = 4; i + 3 < components.Count; i += 4)
            {
                double dot = (components[i] * components[i - 4])
                             + (components[i + 1] * components[i - 3])
                             + (components[i + 2] * components[i - 2])
                             + (components[i + 3] * components[i - 1]);
                if (dot < 0d)
                {
                    for (int c = 0; c < 4; c++)
                    {
                        components[i + c] = -components[i + c];
                    }
                }
            }
        }

        public float DeviationFromLine(int a, int b, int i)
        {
            double span = _times[b] - _times[a];
            double u = span > 0d ? (_times[i] - _times[a]) / span : 0d;
            double x = Lerp(a, b, 0, u);
            double y = Lerp(a, b, 1, u);
            double z = Lerp(a, b, 2, u);
            double w = Lerp(a, b, 3, u);
            return AngleDegrees(i, x, y, z, w);
        }

        public float DeviationFromHold(int a, int i)
        {
            int o = a * 4;
            return AngleDegrees(i, _components[o], _components[o + 1], _components[o + 2], _components[o + 3]);
        }

        private double Lerp(int a, int b, int component, double u)
        {
            float from = _components[(a * 4) + component];
            float to = _components[(b * 4) + component];
            return from + ((to - from) * u);
        }

        private float AngleDegrees(int i, double x, double y, double z, double w)
        {
            double length = Math.Sqrt((x * x) + (y * y) + (z * z) + (w * w));
            if (length <= 0d)
            {
                return 180f;
            }

            int o = i * 4;
            double sx = _components[o];
            double sy = _components[o + 1];
            double sz = _components[o + 2];
            double sw = _components[o + 3];
            double sampleLength = Math.Sqrt((sx * sx) + (sy * sy) + (sz * sz) + (sw * sw));
            if (sampleLength <= 0d)
            {
                return 180f;
            }

            double dot = Math.Abs(((x * sx) + (y * sy) + (z * sz) + (w * sw)) / (length * sampleLength));
            dot = Math.Min(1d, dot);
            return (float)(2d * Math.Acos(dot) * RadiansToDegrees);
        }
    }
}
