using System;
using System.Collections.Generic;

namespace Hidano.FacialControl.Timeline.Editor.AnimationTrackConversion
{
    /// <summary>
    /// 変換で評価する時刻列。Timeline のフレーム（0 〜 duration）に、値が跳び得る時刻 t ごとの (t - <see cref="StepProbeSeconds"/>, t) を加える。
    /// </summary>
    internal sealed class SampleTimeline
    {
        /// <summary>段差を調べる時刻で、直前の値を評価する時間幅（秒）。</summary>
        public const double StepProbeSeconds = 5e-4d;

        private const double MergeTolerance = 1e-6d;

        private readonly List<double> _times;

        // _probeIndices[k] は「直前の値」を評価したサンプル（値が跳び得る時刻の直前）。跳ぶかどうかはチャネルごとに判定する。
        private readonly List<int> _probeIndices;

        private SampleTimeline(List<double> times, List<int> probeIndices)
        {
            _times = times;
            _probeIndices = probeIndices;
        }

        public IReadOnlyList<double> Times => _times;

        public static SampleTimeline Create(double duration, double frameRate, IReadOnlyList<double> stepCandidates)
        {
            duration = Math.Max(0d, duration);
            var all = new List<double>();
            int frameCount = (int)Math.Ceiling((duration * frameRate) - MergeTolerance);
            for (int f = 0; f <= frameCount; f++)
            {
                all.Add(Math.Min(f / frameRate, duration));
            }

            var candidates = new List<double>();
            var probes = new List<double>();
            if (stepCandidates != null)
            {
                for (int i = 0; i < stepCandidates.Count; i++)
                {
                    double t = stepCandidates[i];
                    if (t - StepProbeSeconds <= 0d || t > duration + MergeTolerance)
                    {
                        continue;
                    }

                    t = Math.Min(t, duration);
                    candidates.Add(t);
                    probes.Add(t - StepProbeSeconds);
                }
            }

            // 「直前の値」のすぐ次のサンプルが段差候補の時刻になるよう、(t - probe, t) に入るフレームは評価しない。
            candidates.Sort();
            all.RemoveAll(frame => IsInsideProbeWindow(candidates, frame));
            all.AddRange(candidates);
            all.AddRange(probes);
            all.Sort();
            var times = new List<double>(all.Count);
            for (int i = 0; i < all.Count; i++)
            {
                if (times.Count == 0 || all[i] - times[times.Count - 1] > MergeTolerance)
                {
                    times.Add(all[i]);
                }
            }

            probes.Sort();
            var probeIndices = new List<int>(probes.Count);
            int cursor = 0;
            for (int i = 0; i < probes.Count; i++)
            {
                while (cursor < times.Count && times[cursor] < probes[i] - MergeTolerance)
                {
                    cursor++;
                }

                if (cursor + 1 < times.Count && (probeIndices.Count == 0 || probeIndices[probeIndices.Count - 1] != cursor))
                {
                    probeIndices.Add(cursor);
                }
            }

            return new SampleTimeline(times, probeIndices);
        }

        private static bool IsInsideProbeWindow(List<double> sortedCandidates, double frame)
        {
            int index = sortedCandidates.BinarySearch(frame);
            if (index < 0)
            {
                index = ~index;
            }

            // frame 以上で最小の候補 t について、t - probe < frame < t なら窓の中。
            while (index < sortedCandidates.Count && sortedCandidates[index] - frame <= MergeTolerance)
            {
                index++;
            }

            return index < sortedCandidates.Count
                && sortedCandidates[index] - StepProbeSeconds < frame - MergeTolerance;
        }

        /// <summary>
        /// 「直前の値」のサンプル p と次のサンプル p + 1 の間で、保持のずれが <paramref name="tolerance"/> を超えるものを段差とする
        /// （戻り値の [p + 1] が true）。
        /// </summary>
        public bool[] DetectSteps(IKeyframeChannelMetric metric, float tolerance)
        {
            var steps = new bool[_times.Count];
            for (int i = 0; i < _probeIndices.Count; i++)
            {
                int p = _probeIndices[i];
                if (metric.DeviationFromHold(p, p + 1) > tolerance)
                {
                    steps[p + 1] = true;
                }
            }

            return steps;
        }
    }
}
