#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Hidano.FacialControl.Adapters.Bone;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Editor.AutoExport;
using Hidano.FacialControl.Testing;
using Hidano.FacialControl.Timeline.Editor;
using Hidano.FacialControl.Timeline.Editor.AnimationTrackConversion;
using Hidano.FacialControl.Timeline.Tests.Shared;
using Hidano.FacialControl.Timeline.Tracks;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.Timeline;

namespace Hidano.FacialControl.Timeline.Tests.PlayMode
{
    /// <summary>
    /// 独自 Track → AnimationTrack 変換（<see cref="FacialAnimationTrackConverter"/>）の契約（HID-190）。
    /// 変換した AnimationClip を再生した結果が、変換元を Edit プレビューと同じ合成（= Play と同じ値。
    /// <see cref="TimelinePreviewCompositorTests"/> で固定）で評価した結果と許容誤差内で一致することを、フレームごとに確かめる。
    /// </summary>
    [TestFixture]
    [MediumTest]
    public sealed class FacialAnimationTrackConverterTests : SizedTestFixture
    {
        /// <summary>BlendShape の許容誤差（間引き閾値 + float の丸め）。</summary>
        private const float BlendShapeTolerance = FacialAnimationTrackConverter.BlendShapeTolerance + 0.01f;

        /// <summary>目ボーン回転の許容誤差（度。間引き閾値 + float の丸め）。</summary>
        private const float GazeToleranceDegrees = FacialAnimationTrackConverter.GazeRotationToleranceDegrees + 0.05f;

        private TimelineE2EFixture _fixture;
        private TimelinePreviewCompositor _compositor;
        private readonly List<FacialTimelinePreviewEyeTarget> _gazeBuffer = new List<FacialTimelinePreviewEyeTarget>();

        [TearDown]
        public void TearDown()
        {
            FacialTimelineEditorPreview.ClearCache();
            _compositor?.Dispose();
            _compositor = null;
            _fixture?.Dispose();
            _fixture = null;
        }

        [Test]
        public void Convert_RecExportedTimeline_ClipPlaybackMatchesSourceTracksEveryFrame()
        {
            TimelineE2ECharacter character = Prepare(new RecFixtureWriter.Recording
            {
                IncludeValueProvider = true,
                IncludeLayerWeights = true,
            });
            List<(double time, float[] weights, Quaternion left, Quaternion right)> expected = EvaluateSource(character);

            List<AnimationTrackConversionResult> results = Convert(character);

            Assert.That(results.Count, Is.EqualTo(1), "対象 1 つにつき AnimationTrack は 1 本");
            AnimationClip clip = results[0].Clip;
            GameObject animatorObject = character.Root;
            float maxSmile = 0f;
            for (int i = 0; i < expected.Count; i++)
            {
                (double time, float[] weights, Quaternion left, Quaternion right) = expected[i];
                clip.SampleAnimation(animatorObject, (float)time);
                string[] names = TimelineE2EFixture.MeshBlendShapes;
                for (int s = 0; s < names.Length; s++)
                {
                    Assert.That(
                        character.GetBlendShapeWeight(names[s]),
                        Is.EqualTo(weights[s]).Within(BlendShapeTolerance),
                        $"t={time:F4} {names[s]}");
                }

                Assert.That(Quaternion.Angle(character.LeftEye.localRotation, left), Is.LessThanOrEqualTo(GazeToleranceDegrees), $"t={time:F4} LeftEye");
                Assert.That(Quaternion.Angle(character.RightEye.localRotation, right), Is.LessThanOrEqualTo(GazeToleranceDegrees), $"t={time:F4} RightEye");
                maxSmile = Math.Max(maxSmile, weights[0]);
            }

            Assert.That(maxSmile, Is.GreaterThan(50f), "前提: 比較した時刻に smile が出ている");
        }

        [Test]
        public void Convert_RecExportedTimeline_ThinsKeysOfConstantAndConstantSlopeSections()
        {
            TimelineE2ECharacter character = Prepare(new RecFixtureWriter.Recording());
            int frameCount = (int)Math.Ceiling(_fixture.Timeline.duration * _fixture.Timeline.editorSettings.frameRate) + 1;

            AnimationClip clip = Convert(character)[0].Clip;

            EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(clip);
            Assert.That(bindings.Length, Is.GreaterThan(0));
            for (int i = 0; i < bindings.Length; i++)
            {
                AnimationCurve curve = AnimationUtility.GetEditorCurve(clip, bindings[i]);
                Assert.That(curve.length, Is.LessThan(frameCount / 2), $"{bindings[i].path}/{bindings[i].propertyName} はフルキーにしない");
                if (IsConstant(curve))
                {
                    Assert.That(curve.length, Is.LessThanOrEqualTo(2), $"{bindings[i].propertyName}: 値が一定なら両端だけ");
                }
            }
        }

        [Test]
        public void Convert_SourceTracks_CreatesBoundAnimationTrackMutesSourcesAndSavesClipBesideTimeline()
        {
            TimelineE2ECharacter character = Prepare(new RecFixtureWriter.Recording { IncludeLayerWeights = true });

            AnimationTrackConversionResult result = Convert(character)[0];

            Assert.That(character.Director.GetGenericBinding(result.Track), Is.SameAs(character.Root.GetComponent<Animator>()));
            Assert.That(result.Track.GetClips(), Has.Exactly(1).Matches<TimelineClip>(c => c.start == 0d));
            Assert.That(result.MutedTracks.Count, Is.GreaterThan(0));
            foreach (TrackAsset track in _fixture.Timeline.GetOutputTracks())
            {
                if (FacialAnimationTrackConverter.IsFacialTrack(track))
                {
                    Assert.That(track.muted, Is.True, $"{track.name} はミュートして残す");
                }
            }

            string folder = System.IO.Path.GetDirectoryName(AssetDatabase.GetAssetPath(_fixture.Timeline)).Replace('\\', '/');
            Assert.That(result.ClipPath, Is.EqualTo(folder + "/" + _fixture.Timeline.name + "_" + character.Root.name + ".anim"));
            Assert.That(AssetDatabase.LoadAssetAtPath<AnimationClip>(result.ClipPath), Is.Not.Null);
        }

        [Test]
        public void Convert_ThenUndo_RemovesAnimationTrackAndUnmutesSources()
        {
            TimelineE2ECharacter character = Prepare(new RecFixtureWriter.Recording());
            int trackCount = CountOutputTracks(_fixture.Timeline);

            Convert(character);
            Undo.PerformUndo();

            Assert.That(CountOutputTracks(_fixture.Timeline), Is.EqualTo(trackCount), "AnimationTrack が消える");
            foreach (TrackAsset track in _fixture.Timeline.GetOutputTracks())
            {
                Assert.That(track, Is.Not.InstanceOf<AnimationTrack>());
                Assert.That(track.muted, Is.False, $"{track.name} のミュートが戻る");
            }
        }

        [Test]
        public void Convert_AlreadyConverted_LogsErrorAndAddsNoTrack()
        {
            TimelineE2ECharacter character = Prepare(new RecFixtureWriter.Recording());
            Convert(character);
            int trackCount = CountOutputTracks(_fixture.Timeline);

            LogAssert.Expect(LogType.Error, new Regex("ミュート中の独自 Track"));
            List<AnimationTrackConversionResult> second =
                FacialAnimationTrackConverter.Convert(character.Director, _fixture.Timeline.GetOutputTracks());

            Assert.That(second, Is.Empty, "ミュートした変換元を再び焼き込まない");
            Assert.That(CountOutputTracks(_fixture.Timeline), Is.EqualTo(trackCount));
        }

        [Test]
        public void Convert_MutedChildLaneTrack_LogsErrorAndAddsNoTrack()
        {
            TimelineE2ECharacter character = Prepare(new RecFixtureWriter.Recording());
            FacialExpressionTrack parent = null;
            foreach (TrackAsset track in _fixture.Timeline.GetRootTracks())
            {
                parent ??= track as FacialExpressionTrack;
            }

            Assert.That(parent, Is.Not.Null, "前提: 表情トラックがある");
            var lane = _fixture.Timeline.CreateTrack<FacialExpressionTrack>(parent, parent.name + " Lane 1");
            lane.muted = true;
            int trackCount = CountOutputTracks(_fixture.Timeline);

            LogAssert.Expect(LogType.Error, new Regex("ミュート中の独自 Track"));
            List<AnimationTrackConversionResult> results =
                FacialAnimationTrackConverter.Convert(character.Director, _fixture.Timeline.GetOutputTracks());

            Assert.That(results, Is.Empty, "子トラック（Lane）のミュートも検出する");
            Assert.That(CountOutputTracks(_fixture.Timeline), Is.EqualTo(trackCount));
        }

        private TimelineE2ECharacter Prepare(RecFixtureWriter.Recording recording)
        {
            _fixture = TimelineE2EFixture.Create(recording);
            _fixture.MarkProfileChanged();
            FacialCharacterProfileAutoExporter.ExportIfEnabled(_fixture.ProfileAsset);
            TimelineProfileSource.InvalidateCache(_fixture.ProfileAsset);
            _fixture.Rebake();
            return _fixture.Spawn(TimelineE2EPlacement.SameObject);
        }

        private List<AnimationTrackConversionResult> Convert(TimelineE2ECharacter character)
        {
            Undo.ClearAll();
            List<AnimationTrackConversionResult> results =
                FacialAnimationTrackConverter.Convert(character.Director, _fixture.Timeline.GetOutputTracks());
            Assert.That(results.Count, Is.GreaterThan(0), "前提: 変換できる");
            return results;
        }

        /// <summary>変換元を Edit プレビューと同じ合成で Timeline のフレームごとに評価する。</summary>
        private List<(double time, float[] weights, Quaternion left, Quaternion right)> EvaluateSource(TimelineE2ECharacter character)
        {
            FacialProfile profile = TimelineProfileSource.Resolve(_fixture.ProfileAsset);
            _compositor = new TimelinePreviewCompositor(character.Controller, _fixture.ProfileAsset, profile, null, _fixture.Timeline);
            Assert.That(_compositor.CanRender, Is.True, "前提: Bake を解決できる");
            FacialTimelineEditorPreview.GetGazeEyeResolver(character.Controller, out BoneTransformResolver resolver, out GazeEyeBoneFallback fallback);

            var expected = new List<(double, float[], Quaternion, Quaternion)>();
            double frameRate = _fixture.Timeline.editorSettings.frameRate;
            double duration = _fixture.Timeline.duration;
            int frameCount = (int)Math.Ceiling(duration * frameRate);
            for (int f = 0; f <= frameCount; f++)
            {
                double time = Math.Min(f / frameRate, duration);
                _compositor.Evaluate(time);
                _compositor.EvaluateGaze(time, _fixture.ProfileAsset.GazeChannels, resolver, fallback, _gazeBuffer);
                string[] names = TimelineE2EFixture.MeshBlendShapes;
                var weights = new float[names.Length];
                for (int s = 0; s < names.Length; s++)
                {
                    weights[s] = character.GetBlendShapeWeight(names[s]);
                }

                expected.Add((time, weights, character.LeftEye.localRotation, character.RightEye.localRotation));
            }

            return expected;
        }

        private static bool IsConstant(AnimationCurve curve)
        {
            Keyframe[] keys = curve.keys;
            for (int i = 1; i < keys.Length; i++)
            {
                if (Math.Abs(keys[i].value - keys[0].value) > 1e-4f)
                {
                    return false;
                }
            }

            return true;
        }

        private static int CountOutputTracks(TimelineAsset timeline)
        {
            int count = 0;
            foreach (TrackAsset unused in timeline.GetOutputTracks())
            {
                count++;
            }

            return count;
        }
    }
}
#endif
