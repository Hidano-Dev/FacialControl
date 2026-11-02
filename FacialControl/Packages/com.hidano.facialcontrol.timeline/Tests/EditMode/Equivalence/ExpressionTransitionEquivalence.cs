using System;
using System.Collections.Generic;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Rec.Domain.Models;
using Hidano.FacialControl.Timeline.Adapters.Session;
using Hidano.FacialControl.Timeline.Clips;
using Hidano.FacialControl.Timeline.Domain.Models;
using Hidano.FacialControl.Timeline.Domain.Services;
using Hidano.FacialControl.Timeline.Editor;
using Hidano.FacialControl.Timeline.Playables;
using Hidano.FacialControl.Timeline.Tests.Shared;
using Hidano.FacialControl.Timeline.Tracks;
using UnityEngine.Timeline;

namespace Hidano.FacialControl.Timeline.Tests.EditMode
{
    /// <summary>
    /// 往復検証の Small 層。シーンを使わず、表情の遷移計算だけを全フレームで比べる。
    /// </summary>
    /// <remarks>
    /// <para>期待: REC 再生と同じ順序（フレームの先頭で経過時間を進め、記録時刻が経過時間以下の trigger を発火してから、
    /// trigger 入力源を同じ dt で進める）で、ランタイムの trigger 入力源（<see cref="FakeTriggerInputSource"/>）を回した値。</para>
    /// <para>実際: 同じ記録を REC Export した emotion トラックを、Timeline の Play と同じ手順（<see cref="TimelineExpressionTransitionPlayer"/>。
    /// 最初のフレームは JumpTo、以降は前回の時刻から Advance）で同じフレーム時刻に回した値。</para>
    /// <para>Medium（実ランタイムでの REC 再生 ⇔ Timeline 再生）の近似。両者が食い違うときは Medium が正しい。</para>
    /// </remarks>
    internal static class ExpressionTransitionEquivalence
    {
        public const string Tier = "Small";

        public static EquivalenceReport Run(RecEquivalenceScenario scenario, RecEquivalenceFrameStepMode stepMode)
        {
            FacialProfile profile = RecEquivalenceSpec.CreateDomainProfile();
            string[] blendShapeNames = CollectBlendShapeNames();
            string[] emotionBlendShapes = CollectEmotionBlendShapes();

            var channels = new EquivalenceChannel[emotionBlendShapes.Length];
            for (int i = 0; i < channels.Length; i++)
            {
                channels[i] = new EquivalenceChannel(emotionBlendShapes[i], EquivalenceChannelKind.BlendShape);
            }

            EquivalenceFrameTrace expected = SimulateRecPlayback(scenario, profile, blendShapeNames, emotionBlendShapes, channels, stepMode);
            var context = new List<string>();
            EquivalenceFrameTrace actual = EvaluateTimelinePlayback(scenario, profile, blendShapeNames, emotionBlendShapes, channels, expected, context);
            EquivalenceResult result = EquivalenceComparer.Compare(expected, actual);
            return new EquivalenceReport(Tier, scenario, stepMode, result, context);
        }

        private static EquivalenceFrameTrace SimulateRecPlayback(
            RecEquivalenceScenario scenario,
            FacialProfile profile,
            string[] blendShapeNames,
            string[] emotionBlendShapes,
            EquivalenceChannel[] channels,
            RecEquivalenceFrameStepMode stepMode)
        {
            var source = new FakeTriggerInputSource(
                InputSourceId.Parse(RecEquivalenceSpec.TriggerSourceId),
                blendShapeNames,
                profile);
            RecTimeline timeline = scenario.Timeline;
            // REC 再生の開始（RecTriggerInjector.TryBeginInjection）と同じく、基準のスタックを遷移なしで即座に反映する。
            if (timeline.Baseline.TryGetTriggerStack(RecEquivalenceSpec.TriggerSourceId, out IReadOnlyList<string> baselineStack))
            {
                source.ResetToExpressionStack(baselineStack);
            }

            var trace = new EquivalenceFrameTrace(channels);
            var step = new RecEquivalenceFrameStep(stepMode);
            var values = new float[blendShapeNames.Length];
            var frame = new float[emotionBlendShapes.Length];
            int eventIndex = 0;
            double elapsed = 0d;
            bool first = true;
            while (true)
            {
                float delta = step.NextDelta();
                if (!first)
                {
                    elapsed += delta;
                }

                first = false;
                while (eventIndex < timeline.Events.Count && timeline.Events[eventIndex].TimestampSeconds <= elapsed)
                {
                    Dispatch(source, timeline, timeline.Events[eventIndex]);
                    eventIndex++;
                }

                source.Tick(delta);
                Array.Clear(values, 0, values.Length);
                source.TryWriteValues(values);
                for (int i = 0; i < emotionBlendShapes.Length; i++)
                {
                    frame[i] = values[Array.IndexOf(blendShapeNames, emotionBlendShapes[i])] * 100f;
                }

                trace.AddFrame(elapsed, frame);
                if (elapsed >= scenario.DurationSeconds)
                {
                    return trace;
                }
            }
        }

        private static void Dispatch(FakeTriggerInputSource source, RecTimeline timeline, RecEvent recEvent)
        {
            if (recEvent.Kind == RecEventKind.TriggerOn)
            {
                source.TriggerOn(timeline.ExpressionIds[recEvent.ExpressionIdIndex]);
            }
            else if (recEvent.Kind == RecEventKind.TriggerOff)
            {
                source.TriggerOff(timeline.ExpressionIds[recEvent.ExpressionIdIndex]);
            }
        }

        /// <summary>
        /// Timeline 再生（<c>FacialTrackMixerBehaviour</c> の Play 経路）と同じ手順で、Export した emotion トラックの表情の値を
        /// REC 再生と同じフレーム時刻で作る（最初のフレームは JumpTo、以降は前回の時刻から Advance）。
        /// </summary>
        private static EquivalenceFrameTrace EvaluateTimelinePlayback(
            RecEquivalenceScenario scenario,
            FacialProfile profile,
            string[] blendShapeNames,
            string[] emotionBlendShapes,
            EquivalenceChannel[] channels,
            EquivalenceFrameTrace expected,
            List<string> context)
        {
            TimelineAsset timeline = null;
            try
            {
                timeline = RecToTimelineExporter.CreateTimelineAsset(new RecEventSequenceAdapter(scenario.Timeline), profile);
                DescribeTimeline(timeline, context);

                FacialExpressionTrack track = FindExpressionTrack(timeline, RecEquivalenceSpec.EmotionLayer);
                TimelineStateEvent[] events = track != null ? TimelineStateEventCollector.Collect(track) : Array.Empty<TimelineStateEvent>();
                LayerDefinition? layer = profile.FindLayerByName(RecEquivalenceSpec.EmotionLayer);
                var player = new TimelineExpressionTransitionPlayer(
                    events,
                    blendShapeNames,
                    profile,
                    TimelineLayerConnector.DefaultMaxStackDepth,
                    layer.HasValue ? layer.Value.ExclusionMode : ExclusionMode.LastWins);

                var trace = new EquivalenceFrameTrace(channels);
                var values = new float[blendShapeNames.Length];
                var frame = new float[emotionBlendShapes.Length];
                // Medium の Timeline 再生は Director が Hold で終端に留まる（RecTimelineEquivalenceFixture）。最後のフレームが
                // 記録の長さを越えても、イベントは Timeline の終端までしか拾わない。
                double holdEndTime = timeline.duration;
                double previousTime = 0d;
                double previousEvaluated = 0d;
                for (int f = 0; f < expected.FrameCount; f++)
                {
                    double time = expected.GetTime(f);
                    double evaluatedTime = FacialTrackMixerBehaviour.ResolveEvaluationTime(time, holdEndTime);
                    if (f == 0)
                    {
                        player.JumpTo(evaluatedTime);
                    }
                    else
                    {
                        player.Advance(previousEvaluated, evaluatedTime, (float)(time - previousTime));
                    }

                    previousTime = time;
                    previousEvaluated = evaluatedTime;

                    Array.Clear(values, 0, values.Length);
                    player.TryWriteValues(values);
                    for (int i = 0; i < emotionBlendShapes.Length; i++)
                    {
                        frame[i] = values[Array.IndexOf(blendShapeNames, emotionBlendShapes[i])] * 100f;
                    }

                    trace.AddFrame(time, frame);
                }

                return trace;
            }
            finally
            {
                if (timeline != null)
                {
                    UnityEngine.Object.DestroyImmediate(timeline);
                }
            }
        }

        private static FacialExpressionTrack FindExpressionTrack(TimelineAsset timeline, string layerName)
        {
            foreach (TrackAsset track in timeline.GetOutputTracks())
            {
                if (track is FacialExpressionTrack expressionTrack && string.Equals(track.name, layerName, StringComparison.Ordinal))
                {
                    return expressionTrack;
                }
            }

            return null;
        }

        internal static void DescribeTimeline(TimelineAsset timeline, List<string> context)
        {
            foreach (TrackAsset track in timeline.GetOutputTracks())
            {
                foreach (TimelineClip clip in track.GetClips())
                {
                    string label = clip.asset is FacialExpressionClip expressionClip
                        ? expressionClip.ExpressionId
                        : clip.asset != null ? clip.asset.GetType().Name : "(null)";
                    context.Add($"{track.name}: [{clip.start:0.00000}, {clip.end:0.00000}] {label}");
                }
            }
        }

        /// <summary>Bake と同じ並び（Profile の Expression 順、重複なし）の BlendShape 名。</summary>
        private static string[] CollectBlendShapeNames()
        {
            var names = new List<string>();
            for (int i = 0; i < RecEquivalenceSpec.Expressions.Count; i++)
            {
                string name = RecEquivalenceSpec.Expressions[i].BlendShape;
                if (!names.Contains(name))
                {
                    names.Add(name);
                }
            }

            return names.ToArray();
        }

        private static string[] CollectEmotionBlendShapes()
        {
            var names = new List<string>();
            for (int i = 0; i < RecEquivalenceSpec.Expressions.Count; i++)
            {
                RecEquivalenceSpec.ExpressionSpec spec = RecEquivalenceSpec.Expressions[i];
                if (spec.Layer == RecEquivalenceSpec.EmotionLayer && !names.Contains(spec.BlendShape))
                {
                    names.Add(spec.BlendShape);
                }
            }

            return names.ToArray();
        }
    }
}
