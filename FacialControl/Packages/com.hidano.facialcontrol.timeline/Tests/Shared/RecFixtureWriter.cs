using System;
using System.Collections.Generic;
using System.IO;
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
        }

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
            const ushort expression = 0;

            double lastSample = recording.DurationSeconds - 0.05d;
            var events = new List<(RecEvent Event, IReadOnlyList<float> Axes)>
            {
                (RecEvent.CreateAnalogSample(recording.FirstSampleSeconds, analogSource, 1), new[] { recording.AnalogValue }),
                (RecEvent.CreateAnalogSample(recording.FirstSampleSeconds, gazeSource, 2), new[] { recording.GazeX, recording.GazeY }),
                (RecEvent.CreateTriggerOn(recording.TriggerOnSeconds, triggerSource, expression), Array.Empty<float>()),
                (RecEvent.CreateTriggerOff(recording.TriggerOffSeconds, triggerSource, expression), Array.Empty<float>()),
                (RecEvent.CreateAnalogSample(lastSample, analogSource, 1), new[] { recording.AnalogValue }),
                (RecEvent.CreateAnalogSample(lastSample, gazeSource, 2), new[] { recording.GazeX, recording.GazeY }),
            };

            // 記録は時刻の非減少順でなければならない（同時刻は追加順を保つ安定ソート）。
            var ordered = new List<(RecEvent Event, IReadOnlyList<float> Axes)>(events.Count);
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
            for (int i = 0; i < ordered.Count; i++)
            {
                recEvents[i] = ordered[i].Event;
                axes[i] = ordered[i].Axes;
            }

            return new RecTimeline(
                RecBaselineState.Empty,
                recEvents,
                new[] { TriggerSourceId, AnalogSourceId, GazeSourceId },
                new[] { recording.ExpressionId },
                recording.DurationSeconds,
                axes);
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
