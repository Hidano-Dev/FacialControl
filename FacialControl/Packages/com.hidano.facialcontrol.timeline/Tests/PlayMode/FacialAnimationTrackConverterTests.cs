#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Hidano.FacialControl.Adapters.Bone;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Editor.AutoExport;
using Hidano.FacialControl.Testing;
using Hidano.FacialControl.Timeline.Adapters;
using Hidano.FacialControl.Timeline.Editor;
using Hidano.FacialControl.Timeline.Editor.AnimationTrackConversion;
using Hidano.FacialControl.Timeline.Tests.Shared;
using Hidano.FacialControl.Timeline.Tracks;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
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
        private const float BlendShapeTolerance = FacialAnimationCurveFactory.BlendShapeTolerance + 0.01f;

        /// <summary>目ボーン回転の許容誤差（度。間引き閾値 + float の丸め）。</summary>
        private const float GazeToleranceDegrees = FacialAnimationCurveFactory.GazeRotationToleranceDegrees + 0.05f;

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

            Assert.That(results.Count, Is.EqualTo(1), "対象 1 つにつき結果は 1 つ");
            IReadOnlyList<AnimationTrackConversionClip> clips = results[0].Clips;
            Assert.That(clips.Count, Is.GreaterThan(1), "レイヤー別と目線の Clip に分かれる");
            GameObject animatorObject = character.Root;
            float maxSmile = 0f;
            for (int i = 0; i < expected.Count; i++)
            {
                (double time, float[] weights, Quaternion left, Quaternion right) = expected[i];
                // Clip 同士は別のプロパティだけを持つため、全 AnimationTrack を並べた再生と同じく順にサンプルすればよい。
                for (int c = 0; c < clips.Count; c++)
                {
                    clips[c].Part.Clip.SampleAnimation(animatorObject, (float)time);
                }

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
        public void Convert_RecExportedTimeline_DirectorPlaybackOfAllAnimationTracksMatchesSourceEveryFrame()
        {
            // 変換で作った複数の AnimationTrack を、同じ Animator にバインドしたまま PlayableDirector で再生する。
            // Timeline はトラックごとに Animation の layer を重ねるため、後ろのトラックが前のトラックのプロパティを
            // 既定値へ戻さない（Clip 同士でプロパティが重ならない）ことを、合成結果との一致で確かめる。
            TimelineE2ECharacter character = Prepare(new RecFixtureWriter.Recording
            {
                IncludeValueProvider = true,
                IncludeLayerWeights = true,
            });
            List<(double time, float[] weights, Quaternion left, Quaternion right)> expected = EvaluateSource(character);
            List<AnimationTrackConversionResult> results = Convert(character);
            Assert.That(results[0].Clips.Count, Is.GreaterThan(2), "前提: レイヤー別・目線の複数トラックを再生する");

            // FacialController / Receiver の LateUpdate が renderer を上書きしないよう止め、AnimationTrack の再生結果だけを読む。
            character.Controller.enabled = false;
            character.Receiver.enabled = false;
            PlayableDirector director = character.Director;
            director.RebuildGraph();
            director.Play();
            float maxSmile = 0f;
            string[] names = TimelineE2EFixture.MeshBlendShapes;
            for (int i = 0; i < expected.Count; i++)
            {
                (double time, float[] weights, Quaternion left, Quaternion right) = expected[i];
                director.time = time;
                director.Evaluate();
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

            director.Stop();
            Assert.That(maxSmile, Is.GreaterThan(50f), "前提: 比較した時刻に smile が出ている");
        }

        [Test]
        public void Convert_GenericModelWithoutJointNames_DrivesEyesByRecordedPaths()
        {
            // 書き出し側は Generic (Avatar なし) で、目線メニューのジョイント名も未指定。Avatar からも名前からも目線ボーンを
            // 引けないが、REC に記録された目線ボーンのパスで引いて目線を再現する (HID-192)。
            TimelineE2ECharacter character = Prepare(new RecFixtureWriter.Recording(), ClearGazeJointNames);

            AnimationClip clip = FindClip(Convert(character)[0], FacialAnimationClipKind.Gaze);

            Assert.That(clip, Is.Not.Null, "目線の Clip がある");
            EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(clip);
            Assert.That(HasRotationCurve(bindings, RecFixtureWriter.DefaultLeftEyeBonePath), Is.True, "左目の回転カーブがある");
            Assert.That(HasRotationCurve(bindings, RecFixtureWriter.DefaultRightEyeBonePath), Is.True, "右目の回転カーブがある");
            clip.SampleAnimation(character.Root, (float)(_fixture.Recording.FirstSampleSeconds + 0.5d));
            Assert.That(Quaternion.Angle(character.LeftEye.localRotation, Quaternion.identity), Is.GreaterThan(1f), "目線が左目に出る");
        }

        [Test]
        public void Convert_RecWithoutGazeEyeBonePaths_HasNoEyeRotationCurves()
        {
            // 目線パスの無い REC は Export が Gaze トラックを作らないため、変換結果にも目線のキーは無い (HID-192)。
            LogAssert.Expect(LogType.Warning, new Regex(@"\[RecToTimelineExporter\].*no gaze eye bone paths"));
            TimelineE2ECharacter character = Prepare(
                new RecFixtureWriter.Recording { LeftEyeBonePath = null, RightEyeBonePath = null },
                ClearGazeJointNames);

            AnimationTrackConversionResult result = Convert(character)[0];

            Assert.That(FindClip(result, FacialAnimationClipKind.Gaze), Is.Null, "目線の Clip は作らない");
            List<EditorCurveBinding> bindings = AllBindings(result);
            Assert.That(HasRotationCurve(bindings, RecFixtureWriter.DefaultLeftEyeBonePath), Is.False);
            Assert.That(HasRotationCurve(bindings, RecFixtureWriter.DefaultRightEyeBonePath), Is.False);
            Assert.That(bindings.Count, Is.GreaterThan(0), "目線以外 (BlendShape) は従来どおり変換する");
        }

        private static void ClearGazeJointNames(Hidano.FacialControl.Adapters.ScriptableObject.Serializable.FacialCharacterProfileSO profileAsset)
        {
            Hidano.FacialControl.Adapters.ScriptableObject.GazeChannel gaze = profileAsset.GazeChannels[0];
            gaze.leftEyeBonePath = string.Empty;
            gaze.rightEyeBonePath = string.Empty;
        }

        private static bool HasRotationCurve(IReadOnlyList<EditorCurveBinding> bindings, string path)
        {
            for (int i = 0; i < bindings.Count; i++)
            {
                if (bindings[i].path == path && bindings[i].type == typeof(Transform) && bindings[i].propertyName == "m_LocalRotation.w")
                {
                    return true;
                }
            }

            return false;
        }

        [Test]
        public void Convert_RecExportedTimeline_ThinsKeysOfConstantAndConstantSlopeSections()
        {
            TimelineE2ECharacter character = Prepare(new RecFixtureWriter.Recording());
            int frameCount = (int)Math.Ceiling(_fixture.Timeline.duration * _fixture.Timeline.editorSettings.frameRate) + 1;

            AnimationTrackConversionResult result = Convert(character)[0];

            Assert.That(AllBindings(result).Count, Is.GreaterThan(0));
            for (int c = 0; c < result.Clips.Count; c++)
            {
                AnimationClip clip = result.Clips[c].Part.Clip;
                foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
                {
                    AnimationCurve curve = AnimationUtility.GetEditorCurve(clip, binding);
                    Assert.That(curve.length, Is.LessThan(frameCount / 2), $"{binding.path}/{binding.propertyName} はフルキーにしない");
                    if (IsConstant(curve))
                    {
                        Assert.That(curve.length, Is.LessThanOrEqualTo(2), $"{binding.propertyName}: 値が一定なら両端だけ");
                    }
                }
            }
        }

        [Test]
        public void Convert_RecExportedTimeline_EachPropertyInExactlyOneClip()
        {
            TimelineE2ECharacter character = Prepare(new RecFixtureWriter.Recording
            {
                IncludeValueProvider = true,
                IncludeLayerWeights = true,
            });

            AnimationTrackConversionResult result = Convert(character)[0];

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (EditorCurveBinding binding in AllBindings(result))
            {
                Assert.That(seen.Add(binding.path + "/" + binding.propertyName), Is.True, $"{binding.propertyName} は 1 つの Clip にだけ入る");
            }

            foreach (string shape in TimelineE2EFixture.MeshBlendShapes)
            {
                Assert.That(seen.Contains(RendererPath(character) + "/blendShape." + shape), Is.True, $"{shape} はどれかの Clip に入る");
            }
        }

        [Test]
        public void Convert_ShapesWrittenByLayers_GoToHighestPriorityWritingLayerClip()
        {
            // emotion(priority 0) は Smile、値提供型を受ける face(priority 2) は Blink / JawOpen を書く。
            TimelineE2ECharacter character = Prepare(new RecFixtureWriter.Recording { IncludeValueProvider = true });

            AnimationTrackConversionResult result = Convert(character)[0];

            Assert.That(ShapesOf(result, TimelineE2EFixture.EmotionLayer), Does.Contain(TimelineE2EFixture.SmileBlendShape));
            Assert.That(ShapesOf(result, TimelineE2EFixture.FaceLayer), Does.Contain(TimelineE2EFixture.BlinkBlendShape));
            Assert.That(ShapesOf(result, TimelineE2EFixture.FaceLayer), Does.Contain(TimelineE2EFixture.JawOpenBlendShape));
            AnimationClip gaze = FindClip(result, FacialAnimationClipKind.Gaze);
            Assert.That(gaze, Is.Not.Null);
            foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(gaze))
            {
                Assert.That(binding.type, Is.EqualTo(typeof(Transform)), "目線の Clip は目ボーン回転だけを持つ");
            }
        }

        [Test]
        public void Convert_SourceTracks_CreatesBoundAnimationTrackMutesSourcesAndSavesClipBesideTimeline()
        {
            TimelineE2ECharacter character = Prepare(new RecFixtureWriter.Recording { IncludeLayerWeights = true });

            AnimationTrackConversionResult result = Convert(character)[0];

            string folder = System.IO.Path.GetDirectoryName(AssetDatabase.GetAssetPath(_fixture.Timeline)).Replace('\\', '/');
            for (int c = 0; c < result.Clips.Count; c++)
            {
                AnimationTrackConversionClip converted = result.Clips[c];
                Assert.That(character.Director.GetGenericBinding(converted.Track), Is.SameAs(character.Root.GetComponent<Animator>()));
                Assert.That(converted.Track.GetClips(), Has.Exactly(1).Matches<TimelineClip>(clip => clip.start == 0d));
                Assert.That(converted.Track.name, Is.EqualTo(character.Root.name + " (" + converted.Part.ClipName + ")"));
                Assert.That(converted.ClipPath, Is.EqualTo(folder + "/" + _fixture.Timeline.name + "_" + character.Root.name + "_" + converted.Part.ClipName + ".anim"));
                Assert.That(AssetDatabase.LoadAssetAtPath<AnimationClip>(converted.ClipPath), Is.Not.Null);
            }

            Assert.That(result.MutedTracks.Count, Is.GreaterThan(0));
            foreach (TrackAsset track in _fixture.Timeline.GetOutputTracks())
            {
                if (FacialAnimationTrackConverter.IsFacialTrack(track))
                {
                    Assert.That(track.muted, Is.True, $"{track.name} はミュートして残す");
                }
            }
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
            foreach (TrackAsset track in _fixture.Timeline.GetOutputTracks())
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

        [Test]
        public void Convert_TracksUnderTwoCharacterTracksBoundToDifferentControllers_LogsErrorAndAddsNoTrack()
        {
            TimelineE2ECharacter character = Prepare(new RecFixtureWriter.Recording());
            TimelineE2ECharacter other = _fixture.Spawn(TimelineE2EPlacement.SameObject);
            var secondCharacter = _fixture.Timeline.CreateTrack<FacialCharacterTrack>(null, "Second Character");
            var secondTrack = secondCharacter.CreateChildTrack<FacialExpressionTrack>(TimelineE2EFixture.EmotionLayer);
            secondTrack.Bake = _fixture.Bake;
            character.Director.SetGenericBinding(secondCharacter, other.Controller);

            int trackCount = CountOutputTracks(_fixture.Timeline);

            LogAssert.Expect(LogType.Error, new Regex("複数の対象"));
            List<AnimationTrackConversionResult> results =
                FacialAnimationTrackConverter.Convert(character.Director, _fixture.Timeline.GetOutputTracks());

            Assert.That(results, Is.Empty, "対象ごとに分けられない合成を焼き込まない");
            Assert.That(CountOutputTracks(_fixture.Timeline), Is.EqualTo(trackCount));
        }

        [Test]
        public void Convert_TimelineWithTrackOutsideCharacterTrack_LogsErrorAndAddsNoTrack()
        {
            TimelineE2ECharacter character = Prepare(new RecFixtureWriter.Recording());
            TrackAsset bound = null;
            foreach (TrackAsset track in _fixture.Timeline.GetOutputTracks())
            {
                if (FacialAnimationTrackConverter.IsFacialTrack(track))
                {
                    bound ??= track;
                }
            }

            // 親トラックの外にある旧構造のトラックは対象が決まらず、Play でも再生されない（HID-408）。
            _fixture.Timeline.CreateTrack<FacialExpressionTrack>(null, "legacy").Bake = _fixture.Bake;
            int trackCount = CountOutputTracks(_fixture.Timeline);

            LogAssert.Expect(LogType.Error, new Regex("対象を解決できない"));
            List<AnimationTrackConversionResult> results =
                FacialAnimationTrackConverter.Convert(character.Director, new[] { bound });

            Assert.That(results, Is.Empty, "Play で適用されないトラックを焼き込まない");
            Assert.That(CountOutputTracks(_fixture.Timeline), Is.EqualTo(trackCount));
        }


        [Test]
        public void Convert_SplitPerBlendShape_OneTrackPerBlendShapeAndPlaybackMatchesSource()
        {
            TimelineE2ECharacter character = Prepare(new RecFixtureWriter.Recording
            {
                IncludeValueProvider = true,
                IncludeLayerWeights = true,
            });
            List<(double time, float[] weights, Quaternion left, Quaternion right)> expected = EvaluateSource(character);

            Undo.ClearAll();
            List<AnimationTrackConversionResult> results = FacialAnimationTrackConverter.Convert(
                character.Director,
                _fixture.Timeline.GetOutputTracks(),
                new FacialAnimationClipExportOptions(FacialAnimationClipSplitMode.BlendShape, 0d));

            Assert.That(results.Count, Is.EqualTo(1));
            IReadOnlyList<AnimationTrackConversionClip> clips = results[0].Clips;
            var shapeClips = 0;
            for (int c = 0; c < clips.Count; c++)
            {
                Assert.That(clips[c].Part.Kind, Is.EqualTo(FacialAnimationClipKind.BlendShape).Or.EqualTo(FacialAnimationClipKind.Gaze));
                Assert.That(character.Director.GetGenericBinding(clips[c].Track), Is.Not.Null, "各トラックを Animator に Bind する");
                shapeClips += clips[c].Part.Kind == FacialAnimationClipKind.BlendShape ? 1 : 0;
            }

            Assert.That(shapeClips, Is.EqualTo(TimelineE2EFixture.MeshBlendShapes.Length), "BlendShape 1 つにつき 1 トラック");
            AssertClipPlaybackMatches(character, clips, expected);
        }

        [Test]
        public void Dialog_Convert_UsesChosenOptions()
        {
            TimelineE2ECharacter character = Prepare(new RecFixtureWriter.Recording { IncludeValueProvider = true });
            Undo.ClearAll();
            var dialog = ScriptableObject.CreateInstance<ConvertToAnimationTrackDialog>();
            try
            {
                dialog.Initialize(character.Director, _fixture.Timeline.GetOutputTracks());
                dialog.OptionsFields.SetOptions(new FacialAnimationClipExportOptions(FacialAnimationClipSplitMode.BlendShape, 30d));

                List<AnimationTrackConversionResult> results = dialog.Convert();

                Assert.That(results.Count, Is.EqualTo(1), "ダイアログで選んだ内容で変換する");
                Assert.That(results[0].Clips[0].Part.Kind, Is.EqualTo(FacialAnimationClipKind.BlendShape));
                Assert.That(results[0].Clips[0].Part.Clip.frameRate, Is.EqualTo(30f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(dialog);
            }
        }

        /// <summary>全 Clip を順にサンプルした結果が、変換元の合成と各時刻で一致することを確かめる。</summary>
        private void AssertClipPlaybackMatches(
            TimelineE2ECharacter character,
            IReadOnlyList<AnimationTrackConversionClip> clips,
            List<(double time, float[] weights, Quaternion left, Quaternion right)> expected)
        {
            string[] names = TimelineE2EFixture.MeshBlendShapes;
            float maxSmile = 0f;
            for (int i = 0; i < expected.Count; i++)
            {
                (double time, float[] weights, Quaternion left, Quaternion right) = expected[i];
                for (int c = 0; c < clips.Count; c++)
                {
                    clips[c].Part.Clip.SampleAnimation(character.Root, (float)time);
                }

                for (int s = 0; s < names.Length; s++)
                {
                    Assert.That(character.GetBlendShapeWeight(names[s]), Is.EqualTo(weights[s]).Within(BlendShapeTolerance), $"t={time:F4} {names[s]}");
                }

                Assert.That(Quaternion.Angle(character.LeftEye.localRotation, left), Is.LessThanOrEqualTo(GazeToleranceDegrees), $"t={time:F4} LeftEye");
                Assert.That(Quaternion.Angle(character.RightEye.localRotation, right), Is.LessThanOrEqualTo(GazeToleranceDegrees), $"t={time:F4} RightEye");
                maxSmile = Math.Max(maxSmile, weights[0]);
            }

            Assert.That(maxSmile, Is.GreaterThan(50f), "前提: 比較した時刻に smile が出ている");
        }

        private TimelineE2ECharacter Prepare(
            RecFixtureWriter.Recording recording,
            Action<Hidano.FacialControl.Adapters.ScriptableObject.Serializable.FacialCharacterProfileSO> configureProfile = null)
        {
            _fixture = TimelineE2EFixture.Create(recording, configureProfile);
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
            GazeEyeBoneResolver resolver = FacialTimelineEditorPreview.GetGazeEyeResolver(character.Controller);

            var expected = new List<(double, float[], Quaternion, Quaternion)>();
            double frameRate = _fixture.Timeline.editorSettings.frameRate;
            double duration = _fixture.Timeline.duration;
            int frameCount = (int)Math.Ceiling(duration * frameRate);
            for (int f = 0; f <= frameCount; f++)
            {
                double time = Math.Min(f / frameRate, duration);
                _compositor.Evaluate(time);
                _compositor.EvaluateGaze(time, _fixture.ProfileAsset.GazeChannels, resolver, _gazeBuffer);
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

        private static AnimationClip FindClip(AnimationTrackConversionResult result, FacialAnimationClipKind kind)
        {
            for (int i = 0; i < result.Clips.Count; i++)
            {
                if (result.Clips[i].Part.Kind == kind)
                {
                    return result.Clips[i].Part.Clip;
                }
            }

            return null;
        }

        private static List<EditorCurveBinding> AllBindings(AnimationTrackConversionResult result)
        {
            var bindings = new List<EditorCurveBinding>();
            for (int i = 0; i < result.Clips.Count; i++)
            {
                bindings.AddRange(AnimationUtility.GetCurveBindings(result.Clips[i].Part.Clip));
            }

            return bindings;
        }

        /// <summary>レイヤー <paramref name="layerName"/> の Clip が持つ BlendShape 名（Clip が無ければ空）。</summary>
        private static List<string> ShapesOf(AnimationTrackConversionResult result, string layerName)
        {
            var shapes = new List<string>();
            for (int i = 0; i < result.Clips.Count; i++)
            {
                FacialAnimationClipPart clip = result.Clips[i].Part;
                if (clip.Kind != FacialAnimationClipKind.Layer || clip.Name != layerName)
                {
                    continue;
                }

                foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip.Clip))
                {
                    if (binding.propertyName.StartsWith("blendShape.", StringComparison.Ordinal))
                    {
                        shapes.Add(binding.propertyName.Substring("blendShape.".Length));
                    }
                }
            }

            return shapes;
        }

        private static string RendererPath(TimelineE2ECharacter character)
        {
            return AnimationUtility.CalculateTransformPath(character.Renderer.transform, character.Root.transform);
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
