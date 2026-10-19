using System;
using System.Collections.Generic;

namespace Hidano.FacialControl.Timeline.Tests.Shared
{
    public enum EquivalenceChannelKind
    {
        /// <summary>BlendShape の weight（0..100）。値 1 個。</summary>
        BlendShape = 0,

        /// <summary>ボーンの localRotation。値 4 個（x, y, z, w）。角度差（度）で比べる。</summary>
        BoneRotation = 1,
    }

    public readonly struct EquivalenceChannel
    {
        public EquivalenceChannel(string name, EquivalenceChannelKind kind)
        {
            Name = name;
            Kind = kind;
        }

        public string Name { get; }

        public EquivalenceChannelKind Kind { get; }

        public int ValueCount => Kind == EquivalenceChannelKind.BoneRotation ? 4 : 1;
    }

    /// <summary>毎フレームの出力の記録（時刻とチャンネルごとの値）。</summary>
    public sealed class EquivalenceFrameTrace
    {
        private readonly EquivalenceChannel[] _channels;
        private readonly int[] _offsets;
        private readonly int _stride;
        private readonly List<double> _times = new List<double>();
        private readonly List<float[]> _frames = new List<float[]>();

        public EquivalenceFrameTrace(IReadOnlyList<EquivalenceChannel> channels)
        {
            if (channels == null)
            {
                throw new ArgumentNullException(nameof(channels));
            }

            _channels = new EquivalenceChannel[channels.Count];
            _offsets = new int[channels.Count];
            int offset = 0;
            for (int i = 0; i < channels.Count; i++)
            {
                _channels[i] = channels[i];
                _offsets[i] = offset;
                offset += channels[i].ValueCount;
            }

            _stride = offset;
        }

        public IReadOnlyList<EquivalenceChannel> Channels => _channels;

        public int FrameCount => _frames.Count;

        /// <summary>1 フレームのチャンネルごとの値の総数（BoneRotation は 4 個）。</summary>
        public int Stride => _stride;

        /// <summary>1 フレーム分を追加する。<paramref name="values"/> はチャンネル順に並べた値（コピーして保持する）。</summary>
        public void AddFrame(double timeSeconds, IReadOnlyList<float> values)
        {
            if (values == null || values.Count != _stride)
            {
                throw new ArgumentException($"values must have {_stride} elements.", nameof(values));
            }

            var copy = new float[_stride];
            for (int i = 0; i < _stride; i++)
            {
                copy[i] = values[i];
            }

            _times.Add(timeSeconds);
            _frames.Add(copy);
        }

        public double GetTime(int frame)
        {
            return _times[frame];
        }

        /// <summary>チャンネルの値（BlendShape は weight、BoneRotation は x 成分。全成分は <see cref="GetRotation"/>）。</summary>
        public float GetValue(int frame, int channel)
        {
            return _frames[frame][_offsets[channel]];
        }

        public void GetRotation(int frame, int channel, out float x, out float y, out float z, out float w)
        {
            float[] values = _frames[frame];
            int offset = _offsets[channel];
            x = values[offset];
            y = values[offset + 1];
            z = values[offset + 2];
            w = values[offset + 3];
        }

        /// <summary>フレームの時刻だけを <paramref name="frameShift"/> ずらした複製（わざと壊した入力の検証用）。</summary>
        public EquivalenceFrameTrace ShiftValues(int frameShift)
        {
            var shifted = new EquivalenceFrameTrace(_channels);
            for (int frame = 0; frame < FrameCount; frame++)
            {
                int source = Math.Max(0, Math.Min(FrameCount - 1, frame + frameShift));
                shifted.AddFrame(_times[frame], _frames[source]);
            }

            return shifted;
        }

        /// <summary>チャンネルの値を人が読める形で返す。</summary>
        public string Describe(int frame, int channel)
        {
            if (_channels[channel].Kind == EquivalenceChannelKind.BoneRotation)
            {
                GetRotation(frame, channel, out float x, out float y, out float z, out float w);
                return EquivalenceMath.DescribeRotation(x, y, z, w);
            }

            return GetValue(frame, channel).ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    public static class EquivalenceMath
    {
        /// <summary>2 つの回転の角度差（度）。<c>Quaternion.Angle</c> と同じ式。</summary>
        /// <remarks>どちらかに NaN / 無限大が含まれる場合は無限大を返す（許容差との比較で必ず不一致になるように）。</remarks>
        public static double AngleDegrees(
            float ax, float ay, float az, float aw,
            float bx, float by, float bz, float bw)
        {
            if (!IsFinite(ax) || !IsFinite(ay) || !IsFinite(az) || !IsFinite(aw)
                || !IsFinite(bx) || !IsFinite(by) || !IsFinite(bz) || !IsFinite(bw))
            {
                return double.PositiveInfinity;
            }

            double lengthA = Math.Sqrt((double)ax * ax + (double)ay * ay + (double)az * az + (double)aw * aw);
            double lengthB = Math.Sqrt((double)bx * bx + (double)by * by + (double)bz * bz + (double)bw * bw);
            if (lengthA <= 0d || lengthB <= 0d)
            {
                return lengthA == lengthB ? 0d : 180d;
            }

            double dot = ((double)ax * bx + (double)ay * by + (double)az * bz + (double)aw * bw) / (lengthA * lengthB);
            dot = Math.Min(1d, Math.Abs(dot));
            return 2d * Math.Acos(dot) * (180d / Math.PI);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        /// <summary>回転をオイラー角（度、Unity と同じ ZXY 順）で表した文字列。</summary>
        public static string DescribeRotation(float x, float y, float z, float w)
        {
            // pitch (x), yaw (y), roll (z)
            double sinX = 2d * ((double)w * x - (double)y * z);
            double pitch = Math.Abs(sinX) >= 1d ? Math.Sign(sinX) * 90d : Math.Asin(sinX) * (180d / Math.PI);
            double yaw = Math.Atan2(2d * ((double)w * y + (double)z * x), 1d - 2d * ((double)x * x + (double)y * y)) * (180d / Math.PI);
            double roll = Math.Atan2(2d * ((double)w * z + (double)x * y), 1d - 2d * ((double)z * z + (double)x * x)) * (180d / Math.PI);
            return string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "euler({0:0.###}, {1:0.###}, {2:0.###})",
                pitch,
                yaw,
                roll);
        }
    }
}
