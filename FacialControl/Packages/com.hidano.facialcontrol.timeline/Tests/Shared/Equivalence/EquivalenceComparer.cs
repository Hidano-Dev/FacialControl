using System;
using System.Collections.Generic;

namespace Hidano.FacialControl.Timeline.Tests.Shared
{
    /// <summary>
    /// 期待（REC 再生）と実際（Timeline 再生）の毎フレームの出力を、全フレーム・全チャンネルで比べる。
    /// </summary>
    public static class EquivalenceComparer
    {
        /// <summary>BlendShape の許容誤差（0..100 スケール）。</summary>
        public const double DefaultBlendShapeTolerance = 0.01d;

        /// <summary>ボーン回転の許容誤差（度）。</summary>
        public const double DefaultRotationToleranceDegrees = 0.01d;

        /// <summary>同じフレームとみなす時刻の差（秒）。</summary>
        public const double TimeTolerance = 1e-6d;

        public static EquivalenceResult Compare(
            EquivalenceFrameTrace expected,
            EquivalenceFrameTrace actual,
            double blendShapeTolerance = DefaultBlendShapeTolerance,
            double rotationToleranceDegrees = DefaultRotationToleranceDegrees)
        {
            if (expected == null)
            {
                throw new ArgumentNullException(nameof(expected));
            }

            if (actual == null)
            {
                throw new ArgumentNullException(nameof(actual));
            }

            string structureError = CheckStructure(expected, actual);
            int channelCount = expected.Channels.Count;
            var maxDiffs = new double[channelCount];
            var maxDiffFrames = new int[channelCount];
            var mismatchFrameCounts = new int[channelCount];
            EquivalenceMismatch? first = null;
            int mismatchFrames = 0;
            int frameCount = Math.Min(expected.FrameCount, actual.FrameCount);

            if (structureError == null)
            {
                for (int frame = 0; frame < frameCount; frame++)
                {
                    if (Math.Abs(expected.GetTime(frame) - actual.GetTime(frame)) > TimeTolerance)
                    {
                        structureError = $"frame {frame} の時刻が食い違う（expected={expected.GetTime(frame):0.000000} actual={actual.GetTime(frame):0.000000}）";
                        break;
                    }

                    bool frameMismatched = false;
                    for (int channel = 0; channel < channelCount; channel++)
                    {
                        double diff = Difference(expected, actual, frame, channel);
                        double tolerance = expected.Channels[channel].Kind == EquivalenceChannelKind.BoneRotation
                            ? rotationToleranceDegrees
                            : blendShapeTolerance;
                        if (diff > maxDiffs[channel])
                        {
                            maxDiffs[channel] = diff;
                            maxDiffFrames[channel] = frame;
                        }

                        if (diff > tolerance)
                        {
                            mismatchFrameCounts[channel]++;
                            frameMismatched = true;
                            if (!first.HasValue)
                            {
                                first = new EquivalenceMismatch(
                                    frame,
                                    expected.GetTime(frame),
                                    channel,
                                    expected.Channels[channel].Name,
                                    expected.Describe(frame, channel),
                                    actual.Describe(frame, channel),
                                    diff,
                                    tolerance);
                            }
                        }
                    }

                    if (frameMismatched)
                    {
                        mismatchFrames++;
                    }
                }
            }

            var channels = new EquivalenceChannelSummary[channelCount];
            for (int channel = 0; channel < channelCount; channel++)
            {
                channels[channel] = new EquivalenceChannelSummary(
                    expected.Channels[channel].Name,
                    expected.Channels[channel].Kind,
                    maxDiffs[channel],
                    channelCount > 0 && frameCount > 0 ? expected.GetTime(maxDiffFrames[channel]) : 0d,
                    mismatchFrameCounts[channel]);
            }

            return new EquivalenceResult(structureError, first, mismatchFrames, frameCount, channels);
        }

        private static string CheckStructure(EquivalenceFrameTrace expected, EquivalenceFrameTrace actual)
        {
            if (expected.Channels.Count != actual.Channels.Count)
            {
                return $"チャンネル数が食い違う（expected={expected.Channels.Count} actual={actual.Channels.Count}）";
            }

            for (int i = 0; i < expected.Channels.Count; i++)
            {
                if (!string.Equals(expected.Channels[i].Name, actual.Channels[i].Name, StringComparison.Ordinal)
                    || expected.Channels[i].Kind != actual.Channels[i].Kind)
                {
                    return $"チャンネル {i} が食い違う（expected={expected.Channels[i].Name} actual={actual.Channels[i].Name}）";
                }
            }

            if (expected.FrameCount == 0)
            {
                return "期待側のフレームが 0 件";
            }

            if (expected.FrameCount != actual.FrameCount)
            {
                return $"フレーム数が食い違う（expected={expected.FrameCount} actual={actual.FrameCount}）";
            }

            return null;
        }

        private static double Difference(EquivalenceFrameTrace expected, EquivalenceFrameTrace actual, int frame, int channel)
        {
            if (expected.Channels[channel].Kind == EquivalenceChannelKind.BoneRotation)
            {
                expected.GetRotation(frame, channel, out float ax, out float ay, out float az, out float aw);
                actual.GetRotation(frame, channel, out float bx, out float by, out float bz, out float bw);
                return EquivalenceMath.AngleDegrees(ax, ay, az, aw, bx, by, bz, bw);
            }

            double a = expected.GetValue(frame, channel);
            double b = actual.GetValue(frame, channel);
            // NaN / 無限大は両側で同じでも不正な出力なので、許容差との比較で必ず不一致になる無限大にする。
            if (double.IsNaN(a) || double.IsInfinity(a) || double.IsNaN(b) || double.IsInfinity(b))
            {
                return double.PositiveInfinity;
            }

            return Math.Abs(a - b);
        }
    }

    public readonly struct EquivalenceMismatch
    {
        public EquivalenceMismatch(
            int frame,
            double timeSeconds,
            int channelIndex,
            string channelName,
            string expected,
            string actual,
            double difference,
            double tolerance)
        {
            Frame = frame;
            TimeSeconds = timeSeconds;
            ChannelIndex = channelIndex;
            ChannelName = channelName;
            Expected = expected;
            Actual = actual;
            Difference = difference;
            Tolerance = tolerance;
        }

        public int Frame { get; }

        public double TimeSeconds { get; }

        public int ChannelIndex { get; }

        public string ChannelName { get; }

        public string Expected { get; }

        public string Actual { get; }

        public double Difference { get; }

        public double Tolerance { get; }
    }

    public readonly struct EquivalenceChannelSummary
    {
        public EquivalenceChannelSummary(
            string name,
            EquivalenceChannelKind kind,
            double maxDifference,
            double maxDifferenceTimeSeconds,
            int mismatchFrameCount)
        {
            Name = name;
            Kind = kind;
            MaxDifference = maxDifference;
            MaxDifferenceTimeSeconds = maxDifferenceTimeSeconds;
            MismatchFrameCount = mismatchFrameCount;
        }

        public string Name { get; }

        public EquivalenceChannelKind Kind { get; }

        public double MaxDifference { get; }

        public double MaxDifferenceTimeSeconds { get; }

        public int MismatchFrameCount { get; }
    }

    public sealed class EquivalenceResult
    {
        public EquivalenceResult(
            string structureError,
            EquivalenceMismatch? firstMismatch,
            int mismatchFrameCount,
            int comparedFrameCount,
            IReadOnlyList<EquivalenceChannelSummary> channels)
        {
            StructureError = structureError;
            FirstMismatch = firstMismatch;
            MismatchFrameCount = mismatchFrameCount;
            ComparedFrameCount = comparedFrameCount;
            Channels = channels;
        }

        /// <summary>フレーム数・時刻・チャンネルが揃わない（比較できない）ときの理由。揃っていれば null。</summary>
        public string StructureError { get; }

        public EquivalenceMismatch? FirstMismatch { get; }

        public int MismatchFrameCount { get; }

        public int ComparedFrameCount { get; }

        public IReadOnlyList<EquivalenceChannelSummary> Channels { get; }

        public bool IsMatch => StructureError == null && !FirstMismatch.HasValue;

        /// <summary>差の大きい順のチャンネル（差が 0 のものは除く）。</summary>
        public IReadOnlyList<EquivalenceChannelSummary> WorstChannels(int count)
        {
            var sorted = new List<EquivalenceChannelSummary>();
            for (int i = 0; i < Channels.Count; i++)
            {
                if (Channels[i].MaxDifference > 0d)
                {
                    sorted.Add(Channels[i]);
                }
            }

            sorted.Sort((a, b) => b.MaxDifference.CompareTo(a.MaxDifference));
            if (sorted.Count > count)
            {
                sorted.RemoveRange(count, sorted.Count - count);
            }

            return sorted;
        }
    }
}
