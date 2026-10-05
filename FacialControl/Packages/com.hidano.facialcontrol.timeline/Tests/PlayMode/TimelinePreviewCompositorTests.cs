#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Hidano.FacialControl.Adapters.Bone;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Editor.AutoExport;
using Hidano.FacialControl.Testing;
using Hidano.FacialControl.Timeline.Domain.Diagnostics;
using Hidano.FacialControl.Timeline.Editor;
using Hidano.FacialControl.Timeline.Tests.Shared;
using Hidano.FacialControl.Timeline.Tracks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.Timeline;

namespace Hidano.FacialControl.Timeline.Tests.PlayMode
{
    /// <summary>
    /// Edit プレビューの合成（<see cref="TimelinePreviewCompositor"/>）が Director 再生（Play）と同じ値を
    /// renderer と目ボーンへ出すことを、D9 の比較時刻集合と許容誤差で固定する（Req 7.1 / 7.6 / 11.5）。
    /// </summary>
    /// <remarks>
    /// 比較は「Timeline 以外の live 入力が無く、レイヤー weight が既定」の条件で行う。Compositor は Analog チャネル →
    /// analog 消費者（Fake binding）の経路を再現しないため、記録の Analog 値は 0 にする。
    /// </remarks>
    [TestFixture]
    [MediumTest]
    public sealed class TimelinePreviewCompositorTests : SizedTestFixture
    {
        /// <summary>renderer の BlendShape weight（0..100 スケール。正規化 1e-4 相当）の許容誤差。</summary>
        private const float RendererTolerance = 0.01f;

        /// <summary>目ボーン localRotation の各成分の許容誤差。</summary>
        private const float GazeTolerance = 1e-3f;

        private const double FrameSeconds = 1d / 60d;

        private static readonly string[] ComparedBlendShapes =
        {
            TimelineE2EFixture.SmileBlendShape,
            TimelineE2EFixture.SquintBlendShape,
            TimelineE2EFixture.BlinkBlendShape,
        };

        private TimelineE2EFixture _fixture;
        private TimelinePreviewCompositor _compositor;
        private readonly List<FacialTimelinePreviewEyeTarget> _gazeBuffer = new List<FacialTimelinePreviewEyeTarget>();

        [TearDown]
        public void TearDown()
        {
            _compositor?.Dispose();
            _compositor = null;
            _fixture?.Dispose();
            _fixture = null;
        }

        [UnityTest]
        public IEnumerator Evaluate_SameSnapshot_MatchesDirectorPlaybackAtComparisonTimes()
        {
            _fixture = TimelineE2EFixture.Create(new RecFixtureWriter.Recording { AnalogValue = 0f });
            PrepareSameSnapshot(_fixture);
            TimelineE2ECharacter character = _fixture.Spawn(TimelineE2EPlacement.SameObject);

            FacialProfile profile = TimelineProfileSource.Resolve(_fixture.ProfileAsset);
            _compositor = new TimelinePreviewCompositor(
                character.Controller,
                _fixture.ProfileAsset,
                profile,
                null,
                _fixture.Timeline);
            Assert.That(_compositor.CanRender, Is.True, "前提: Export 直後の Timeline は Bake を Found で解決できる");
            Assert.That(_compositor.ProfileCheck, Is.EqualTo(TimelineDiagnosticCode.Ok), "前提: 同一スナップショットで焼いた Bake");

            var resolver = new BoneTransformResolver(character.Controller.transform);
            GazeEyeBoneFallback fallback = GazeEyeBoneFallback.FromAnimator(character.Controller.GetComponent<Animator>());
            bool sawSmile = false;
            bool sawGaze = false;

            foreach (double time in CollectComparisonTimes(_fixture.Timeline))
            {
                yield return character.EvaluateAt(time);
                float[] played = ReadWeights(character);
                Quaternion playedLeft = character.LeftEye.localRotation;
                Quaternion playedRight = character.RightEye.localRotation;

                _compositor.Evaluate(time);
                _compositor.EvaluateGaze(time, _fixture.ProfileAsset.GazeChannels, resolver, fallback, _gazeBuffer);
                float[] composed = ReadWeights(character);

                for (int i = 0; i < ComparedBlendShapes.Length; i++)
                {
                    Assert.That(
                        composed[i],
                        Is.EqualTo(played[i]).Within(RendererTolerance),
                        $"t={time:F4} {ComparedBlendShapes[i]}: Edit={composed[i]} Play={played[i]}");
                }

                AssertRotation(playedLeft, character.LeftEye.localRotation, $"t={time:F4} LeftEye");
                AssertRotation(playedRight, character.RightEye.localRotation, $"t={time:F4} RightEye");

                sawSmile |= played[0] > 50f;
                sawGaze |= Quaternion.Angle(playedLeft, Quaternion.identity) > 1f;
            }

            Assert.That(sawSmile, Is.True, "前提: 比較時刻に smile が出ている時刻を含む");
            Assert.That(sawGaze, Is.True, "前提: 比較時刻に Gaze が出ている時刻を含む");
        }

        /// <summary>
        /// 同一スナップショット: SO 保存 → profile.json 書き出し（AutoExporter の冪等入口）→ Profile 読込キャッシュ無効化 → 再ベイク。
        /// </summary>
        private static void PrepareSameSnapshot(TimelineE2EFixture fixture)
        {
            fixture.MarkProfileChanged();
            FacialCharacterProfileAutoExporter.ExportIfEnabled(fixture.ProfileAsset);
            TimelineProfileSource.InvalidateCache(fixture.ProfileAsset);
            fixture.Rebake();
        }

        /// <summary>
        /// D9 の比較時刻集合: 0 / 各 Clip の start・end ±1/60 s / 中点 / duration（[0, duration] に収め昇順・重複除去）。
        /// </summary>
        private static List<double> CollectComparisonTimes(TimelineAsset timeline)
        {
            double duration = timeline.duration;
            var times = new SortedSet<double> { 0d, duration };
            foreach (TrackAsset track in timeline.GetOutputTracks())
            {
                if (!(track is FacialExpressionTrack) && !(track is FacialValueTrack))
                {
                    continue;
                }

                foreach (TimelineClip clip in track.GetClips())
                {
                    AddClamped(times, clip.start - FrameSeconds, duration);
                    AddClamped(times, clip.start + FrameSeconds, duration);
                    AddClamped(times, clip.end - FrameSeconds, duration);
                    AddClamped(times, clip.end + FrameSeconds, duration);
                    AddClamped(times, (clip.start + clip.end) * 0.5d, duration);
                }
            }

            return new List<double>(times);
        }

        private static void AddClamped(SortedSet<double> times, double time, double duration)
        {
            times.Add(Math.Max(0d, Math.Min(duration, time)));
        }

        private static float[] ReadWeights(TimelineE2ECharacter character)
        {
            var weights = new float[ComparedBlendShapes.Length];
            for (int i = 0; i < ComparedBlendShapes.Length; i++)
            {
                weights[i] = character.GetBlendShapeWeight(ComparedBlendShapes[i]);
            }

            return weights;
        }

        private static void AssertRotation(Quaternion expected, Quaternion actual, string label)
        {
            // q と -q は同じ回転なので符号を揃えて成分を比べる。
            float sign = Quaternion.Dot(expected, actual) < 0f ? -1f : 1f;
            var message = new StringBuilder(label).Append(": Edit=").Append(actual.ToString("F5"))
                .Append(" Play=").Append(expected.ToString("F5")).ToString();
            Assert.That(actual.x * sign, Is.EqualTo(expected.x).Within(GazeTolerance), message);
            Assert.That(actual.y * sign, Is.EqualTo(expected.y).Within(GazeTolerance), message);
            Assert.That(actual.z * sign, Is.EqualTo(expected.z).Within(GazeTolerance), message);
            Assert.That(actual.w * sign, Is.EqualTo(expected.w).Within(GazeTolerance), message);
        }
    }
}
#endif
