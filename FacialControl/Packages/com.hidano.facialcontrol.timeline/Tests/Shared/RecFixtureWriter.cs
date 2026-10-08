using System;
using System.Collections.Generic;
using System.IO;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Rec.Domain.Models;
using Hidano.FacialControl.Rec.Domain.Services;

namespace Hidano.FacialControl.Timeline.Tests.Shared
{
    /// <summary>
    /// REC Export の入力になる <c>.fcrec</c> を組み立てて書き出すテスト用ヘルパー（EditMode / PlayMode 共用）。
    /// </summary>
    /// <remarks>
    /// 既定の記録は trigger（<see cref="TriggerExpressionId"/>）/ analog（<see cref="AnalogSourceId"/> 1 軸）/
    /// gaze（<see cref="GazeSourceId"/> 2 軸）の 3 種。analog / gaze は記録区間を通して一定値にする
    /// （Export の Value クリップは最初のサンプル時刻から記録終端までを覆う）。
    /// </remarks>
    public static class RecFixtureWriter
    {
        public const string TriggerSourceId = "osc:trigger";
        public const string AnalogSourceId = "osc:lt";
        public const string GazeSourceId = "osc:gaze";
        public const string TriggerExpressionId = "smile";

        private const long StartedAtUnixMilliseconds = 123L;

        /// <summary>trigger / analog / gaze の 3 種を含む記録の内容。</summary>
        public sealed class Recording
        {
            public string ExpressionId { get; set; } = TriggerExpressionId;

            public double TriggerOnSeconds { get; set; } = 0.2d;

            public double TriggerOffSeconds { get; set; } = 0.8d;

            /// <summary>Analog / Gaze の最初のサンプル時刻（Value クリップの開始時刻になる）。</summary>
            public double FirstSampleSeconds { get; set; } = 0.1d;

            public float AnalogValue { get; set; } = 0.5f;

            public float GazeX { get; set; } = 0.5f;

            public float GazeY { get; set; } = 0.25f;

            public double DurationSeconds { get; set; } = 2.0d;

            /// <summary>値提供型（<see cref="ValueProviderSourceId"/>）の基準と時刻付きレコードを含めるか。</summary>
            public bool IncludeValueProvider { get; set; }

            /// <summary>値提供型の mask バイト数（記録時の FacialController の BlendShape 数 / 8 の切り上げ）。</summary>
            public int ValueProviderMaskByteCount { get; set; } = 1;

            /// <summary>
            /// 録画時のホストの BlendShape 名（REC が基準と一緒に記録する。index = 値提供型の BlendShape index）。
            /// null なら記録しない（BlendShape 名を記録しない旧 REC と同じ）。
            /// </summary>
            public string[] BlendShapeNames { get; set; }

            /// <summary>
            /// レイヤー weight（<see cref="LayerWeightLayer"/> の基準と <see cref="LayerWeightSamples"/>）を含めるか。
            /// 発話ゲートが lipsync レイヤーの weight を上下させるのと同じ形の記録。
            /// </summary>
            public bool IncludeLayerWeights { get; set; }
        }

        /// <summary>レイヤー weight を記録するレイヤー（trigger の Expression が乗るレイヤー）。</summary>
        public const string LayerWeightLayer = "emotion";

        /// <summary>レイヤー weight の基準（t=0）。</summary>
        public const float LayerWeightBaseline = 1f;

        /// <summary>
        /// レイヤー weight の時刻付きレコード（trigger の on 区間 0.2〜0.8 秒の途中で 0 にして戻す）。
        /// 時刻は 1/60 秒の格子に乗らないようにずらす。
        /// </summary>
        public static IReadOnlyList<(double TimeSeconds, float Weight)> LayerWeightSamples { get; } = new[]
        {
            (0.41d, 0f),
            (0.61d, 1f),
        };

        /// <summary>値提供型の source id（iFacialMocap の BlendShape 受信と同じく slug だけ）。</summary>
        public const string ValueProviderSourceId = "ifm";

        /// <summary>値提供型の 1 レコード（記録と同じ差分形式。空の mask / 値は「従来のまま」）。</summary>
        public readonly struct ValueProviderRecord
        {
            public ValueProviderRecord(double timeSeconds, bool isValid, byte[] maskBytes, float[] values)
            {
                TimeSeconds = timeSeconds;
                IsValid = isValid;
                MaskBytes = maskBytes ?? Array.Empty<byte>();
                Values = values ?? Array.Empty<float>();
            }

            public double TimeSeconds { get; }

            public bool IsValid { get; }

            public byte[] MaskBytes { get; }

            public float[] Values { get; }
        }

        /// <summary>
        /// 値提供型の基準（t=0 として扱う）。BlendShape index 2 / 3 が寄与（e2e のメッシュでは Blink / JawOpen）。
        /// </summary>
        public static ValueProviderRecord ValueProviderBaseline =>
            new ValueProviderRecord(0d, true, new byte[] { 0b0000_1100 }, new[] { 0.2f, 0.4f });

        /// <summary>
        /// 値提供型の時刻付きレコード。値だけの更新 / mask の変更 / 無効化 / 再有効化 / 形の合わないレコード（REC 再生でも拒否）を含む。
        /// 時刻は 1/60 秒の格子に乗らないようにずらす（フレーム評価がレコードの境界を跨ぐ順序に依存しないため）。
        /// </summary>
        public static IReadOnlyList<ValueProviderRecord> ValueProviderSamples { get; } = new[]
        {
            new ValueProviderRecord(0.31d, true, null, new[] { 0.5f, 0.1f }),
            new ValueProviderRecord(0.61d, true, new byte[] { 0b0001_1000 }, new[] { 0.7f, 0.9f }),
            new ValueProviderRecord(0.91d, false, null, null),
            new ValueProviderRecord(1.21d, true, null, new[] { 0.3f, 0.6f }),
            new ValueProviderRecord(1.51d, true, null, new[] { 1f, 1f, 1f }),
            new ValueProviderRecord(1.81d, true, null, new[] { 1f, 0f }),
        };

        /// <summary>記録を <see cref="RecTimeline"/> に組み立てる。</summary>
        public static RecTimeline CreateTimeline(Recording recording)
        {
            if (recording == null)
            {
                throw new ArgumentNullException(nameof(recording));
            }

            const ushort triggerSource = 0;
            const ushort analogSource = 1;
            const ushort gazeSource = 2;
            const ushort valueProviderSource = 3;
            const ushort expression = 0;

            double lastSample = recording.DurationSeconds - 0.05d;
            var events = new List<(RecEvent Event, IReadOnlyList<float> Axes, IReadOnlyList<byte> Mask)>
            {
                (RecEvent.CreateAnalogSample(recording.FirstSampleSeconds, analogSource, 1), new[] { recording.AnalogValue }, null),
                (RecEvent.CreateAnalogSample(recording.FirstSampleSeconds, gazeSource, 2), new[] { recording.GazeX, recording.GazeY }, null),
                (RecEvent.CreateTriggerOn(recording.TriggerOnSeconds, triggerSource, expression), Array.Empty<float>(), null),
                (RecEvent.CreateTriggerOff(recording.TriggerOffSeconds, triggerSource, expression), Array.Empty<float>(), null),
                (RecEvent.CreateAnalogSample(lastSample, analogSource, 1), new[] { recording.AnalogValue }, null),
                (RecEvent.CreateAnalogSample(lastSample, gazeSource, 2), new[] { recording.GazeX, recording.GazeY }, null),
            };

            var sourceIds = new List<string> { TriggerSourceId, AnalogSourceId, GazeSourceId };
            RecBaselineState baseline = RecBaselineState.Empty;
            if (recording.IncludeValueProvider)
            {
                sourceIds.Add(ValueProviderSourceId);
                ValueProviderRecord baselineRecord = ExpandMask(ValueProviderBaseline, recording.ValueProviderMaskByteCount);
                baseline = new RecBaselineState(
                    null,
                    null,
                    new[]
                    {
                        new RecBaselineState.ValueProviderEntry(
                            ValueProviderSourceId, baselineRecord.IsValid, baselineRecord.MaskBytes, baselineRecord.Values),
                    },
                    null);
                if (recording.BlendShapeNames != null)
                {
                    baseline = baseline.WithBlendShapeNames(recording.BlendShapeNames);
                }

                for (int i = 0; i < ValueProviderSamples.Count; i++)
                {
                    ValueProviderRecord record = ExpandMask(ValueProviderSamples[i], recording.ValueProviderMaskByteCount);
                    RecValueProviderFlags flags = record.IsValid ? RecValueProviderFlags.IsValid : RecValueProviderFlags.None;
                    if (record.MaskBytes.Length > 0)
                    {
                        flags |= RecValueProviderFlags.HasMask;
                    }

                    if (record.Values.Length > 0)
                    {
                        flags |= RecValueProviderFlags.HasValues;
                    }

                    events.Add((
                        RecEvent.CreateValueProviderSample(
                            record.TimeSeconds,
                            valueProviderSource,
                            flags,
                            checked((ushort)record.Values.Length),
                            checked((ushort)record.MaskBytes.Length)),
                        record.Values,
                        record.MaskBytes));
                }
            }

            string[] layerIds = null;
            if (recording.IncludeLayerWeights)
            {
                layerIds = new[] { LayerWeightLayer };
                baseline = new RecBaselineState(
                    null,
                    null,
                    baseline.ValueProviderEntries,
                    null,
                    new[] { new LayerWeightEntry(LayerWeightLayer, LayerWeightBaseline) },
                    null,
                    baseline.BlendShapeNames);
                for (int i = 0; i < LayerWeightSamples.Count; i++)
                {
                    events.Add((
                        RecEvent.CreateLayerWeightSample(LayerWeightSamples[i].TimeSeconds, 0),
                        new[] { LayerWeightSamples[i].Weight },
                        null));
                }
            }

            // 記録は時刻の非減少順でなければならない（同時刻は追加順を保つ安定ソート）。
            var ordered = new List<(RecEvent Event, IReadOnlyList<float> Axes, IReadOnlyList<byte> Mask)>(events.Count);
            for (int i = 0; i < events.Count; i++)
            {
                int insertAt = ordered.Count;
                while (insertAt > 0 && ordered[insertAt - 1].Event.TimestampSeconds > events[i].Event.TimestampSeconds)
                {
                    insertAt--;
                }

                ordered.Insert(insertAt, events[i]);
            }

            var recEvents = new RecEvent[ordered.Count];
            var axes = new IReadOnlyList<float>[ordered.Count];
            var masks = new IReadOnlyList<byte>[ordered.Count];
            for (int i = 0; i < ordered.Count; i++)
            {
                recEvents[i] = ordered[i].Event;
                axes[i] = ordered[i].Axes;
                masks[i] = ordered[i].Mask ?? Array.Empty<byte>();
            }

            return new RecTimeline(
                baseline,
                recEvents,
                sourceIds,
                new[] { recording.ExpressionId },
                layerIds,
                recording.DurationSeconds,
                axes,
                masks);
        }

        /// <summary>mask を記録時の BlendShape 数に合わせたバイト数へ広げる（上位バイトは 0）。</summary>
        public static ValueProviderRecord ExpandMask(ValueProviderRecord record, int maskByteCount)
        {
            if (record.MaskBytes.Length == 0 || record.MaskBytes.Length >= maskByteCount)
            {
                return record;
            }

            var mask = new byte[maskByteCount];
            Array.Copy(record.MaskBytes, mask, record.MaskBytes.Length);
            return new ValueProviderRecord(record.TimeSeconds, record.IsValid, mask, record.Values);
        }

        /// <summary>記録を <c>.fcrec</c> として書き出す。親ディレクトリは呼び出し側が用意する。</summary>
        public static void Write(string path, Recording recording)
        {
            Write(path, CreateTimeline(recording));
        }

        public static void Write(string path, RecTimeline timeline)
        {
            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentException("path must be non-empty.", nameof(path));
            }

            File.WriteAllBytes(path, RecBinaryFormat.Serialize(timeline, StartedAtUnixMilliseconds));
        }
    }
}
