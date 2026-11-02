#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Hidano.FacialControl.Editor.AutoExport;
using Hidano.FacialControl.Testing;
using Hidano.FacialControl.Timeline.Adapters.Assets;
using Hidano.FacialControl.Timeline.Domain.Models;
using Hidano.FacialControl.Timeline.Editor;
using Hidano.FacialControl.Timeline.Editor.AnimationClipExport;
using Hidano.FacialControl.Timeline.Editor.AnimationTrackConversion;
using Hidano.FacialControl.Timeline.Tests.Shared;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.Timeline;

namespace Hidano.FacialControl.Timeline.Tests.PlayMode
{
    /// <summary>
    /// REC から直接 AnimationClip を書き出す経路（<see cref="RecAnimationClipExporter"/>。HID-409）の契約。
    /// 従来の「REC Export → Timeline → AnimationTrack 変換」と同じカーブになること、シーンと一時オブジェクトを残さないことを確かめる。
    /// </summary>
    [TestFixture]
    [MediumTest]
    public sealed class RecAnimationClipExporterTests : SizedTestFixture
    {
        private const float BlendShapeTolerance = 1e-3f;
        private const float RotationComponentTolerance = 1e-4f;

        private TimelineE2EFixture _fixture;
        private string _outputFolder;

        [TearDown]
        public void TearDown()
        {
            FacialTimelineEditorPreview.ClearCache();
            _fixture?.Dispose();
            _fixture = null;
        }

        [Test]
        public void TryExport_RecWithAllChannels_CurvesMatchTimelineConversion()
        {
            TimelineE2ECharacter character = Prepare(new RecFixtureWriter.Recording
            {
                IncludeValueProvider = true,
                IncludeLayerWeights = true,
            });
            Undo.ClearAll();
            List<AnimationTrackConversionResult> converted =
                FacialAnimationTrackConverter.Convert(character.Director, _fixture.Timeline.GetOutputTracks());
            Assert.That(converted.Count, Is.EqualTo(1), "前提: 従来経路で変換できる");

            bool success = RecAnimationClipExporter.TryExport(
                _fixture.RecordingPath, character.Controller, null, null, _outputFolder, false, out RecAnimationClipExportResult result);

            Assert.That(success, Is.True, result.Error);
            IReadOnlyList<AnimationTrackConversionClip> expectedClips = converted[0].Clips;
            Assert.That(result.Clips.Count, Is.EqualTo(expectedClips.Count), "同じ数の Clip（レイヤー別 + Base + Gaze）");
            double frameRate = _fixture.Timeline.editorSettings.frameRate;
            double duration = _fixture.Timeline.duration;
            for (int c = 0; c < expectedClips.Count; c++)
            {
                FacialAnimationClipPart expected = expectedClips[c].Part;
                FacialAnimationClipPart actual = result.Clips[c].Part;
                Assert.That(actual.ClipName, Is.EqualTo(expected.ClipName), "同じ並び・名前");
                Assert.That(actual.Kind, Is.EqualTo(expected.Kind));
                EditorCurveBinding[] expectedBindings = AnimationUtility.GetCurveBindings(expected.Clip);
                EditorCurveBinding[] actualBindings = AnimationUtility.GetCurveBindings(actual.Clip);
                Assert.That(actualBindings, Is.EquivalentTo(expectedBindings), $"{expected.ClipName}: 同じプロパティ");
                float tolerance = expected.Kind == FacialAnimationClipKind.Gaze ? RotationComponentTolerance : BlendShapeTolerance;
                for (int b = 0; b < expectedBindings.Length; b++)
                {
                    AnimationCurve expectedCurve = AnimationUtility.GetEditorCurve(expected.Clip, expectedBindings[b]);
                    AnimationCurve actualCurve = AnimationUtility.GetEditorCurve(actual.Clip, expectedBindings[b]);
                    int frameCount = (int)Math.Ceiling(duration * frameRate);
                    for (int f = 0; f <= frameCount; f++)
                    {
                        float time = (float)Math.Min(f / frameRate, duration);
                        Assert.That(
                            actualCurve.Evaluate(time),
                            Is.EqualTo(expectedCurve.Evaluate(time)).Within(tolerance),
                            $"{expected.ClipName} {expectedBindings[b].propertyName} t={time:F4}");
                    }
                }
            }

            Assert.That(result.Clips[0].AssetPath, Does.StartWith(_outputFolder + "/recording_"), "REC 名 + 対象名で保存する");
            Assert.That(AssetDatabase.LoadAssetAtPath<AnimationClip>(result.Clips[0].AssetPath), Is.Not.Null, "アセットとして保存する");
        }

        [Test]
        public void TryExport_LeavesSceneAndTemporaryObjectsUntouched()
        {
            TimelineE2ECharacter character = Prepare(new RecFixtureWriter.Recording { IncludeValueProvider = true });
            int componentCount = character.Root.GetComponents<Component>().Length;
            float[] weightsBefore = ReadWeights(character);
            int timelineCount = Resources.FindObjectsOfTypeAll<TimelineAsset>().Length;
            int trackCount = Resources.FindObjectsOfTypeAll<TrackAsset>().Length;
            int bakeCount = Resources.FindObjectsOfTypeAll<FacialTimelineBakeAsset>().Length;

            bool success = RecAnimationClipExporter.TryExport(
                _fixture.RecordingPath, character.Controller, null, null, _outputFolder, false, out RecAnimationClipExportResult result);

            Assert.That(success, Is.True, result.Error);
            Assert.That(character.Root.GetComponents<Component>().Length, Is.EqualTo(componentCount), "コンポーネント（Receiver 等）を追加しない");
            Assert.That(ReadWeights(character), Is.EqualTo(weightsBefore), "renderer を書き換えない");
            Assert.That(Resources.FindObjectsOfTypeAll<TimelineAsset>().Length, Is.EqualTo(timelineCount), "一時 TimelineAsset を残さない");
            Assert.That(Resources.FindObjectsOfTypeAll<TrackAsset>().Length, Is.EqualTo(trackCount), "一時トラックを残さない");
            Assert.That(Resources.FindObjectsOfTypeAll<FacialTimelineBakeAsset>().Length, Is.EqualTo(bakeCount), "一時 Bake を残さない");
            Assert.That(result.TimelineAssetPath, Is.Null, "既定では TimelineAsset を作らない");
        }

        [Test]
        public void TryExport_CreateTimeline_CreatesTimelineWithOneAnimationTrackPerClip()
        {
            TimelineE2ECharacter character = Prepare(new RecFixtureWriter.Recording { IncludeValueProvider = true });

            bool success = RecAnimationClipExporter.TryExport(
                _fixture.RecordingPath, character.Controller, null, null, _outputFolder, true, out RecAnimationClipExportResult result);

            Assert.That(success, Is.True, result.Error);
            var timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(result.TimelineAssetPath);
            Assert.That(timeline, Is.Not.Null, "TimelineAsset を保存する");
            var clips = new List<AnimationClip>();
            foreach (TrackAsset track in timeline.GetOutputTracks())
            {
                Assert.That(track, Is.InstanceOf<AnimationTrack>());
                foreach (TimelineClip clip in track.GetClips())
                {
                    clips.Add(((AnimationPlayableAsset)clip.asset).clip);
                }
            }

            var expected = new List<AnimationClip>();
            for (int i = 0; i < result.Clips.Count; i++)
            {
                expected.Add(result.Clips[i].Part.Clip);
            }

            Assert.That(clips, Is.EqualTo(expected), "書き出した Clip を 1 本ずつ並べる");
        }

        [Test]
        public void TryExport_RemapExcludingValueProvider_DropsItsBlendShapes()
        {
            TimelineE2ECharacter character = Prepare(new RecFixtureWriter.Recording { IncludeValueProvider = true });
            var remap = new RecSourceRemap(
                new[] { new KeyValuePair<string, string>(RecFixtureWriter.ValueProviderSourceId, null) },
                Array.Empty<KeyValuePair<string, string>>());

            bool success = RecAnimationClipExporter.TryExport(
                _fixture.RecordingPath, character.Controller, null, remap, _outputFolder, false, out RecAnimationClipExportResult result);

            Assert.That(success, Is.True, result.Error);
            for (int i = 0; i < result.Clips.Count; i++)
            {
                Assert.That(result.Clips[i].Part.Name, Is.Not.EqualTo(TimelineE2EFixture.FaceLayer), "除外した値提供型の担当レイヤーの Clip は作らない");
            }
        }

        [Test]
        public void TryExport_RemapCollision_ReturnsFalseAndWritesNothing()
        {
            TimelineE2ECharacter character = Prepare(new RecFixtureWriter.Recording { IncludeValueProvider = true });
            var remap = new RecSourceRemap(
                new[] { new KeyValuePair<string, string>(RecFixtureWriter.AnalogSourceId, RecFixtureWriter.ValueProviderSourceId) },
                Array.Empty<KeyValuePair<string, string>>());

            LogAssert.Expect(LogType.Error, new Regex("対応先が重複"));
            bool success = RecAnimationClipExporter.TryExport(
                _fixture.RecordingPath, character.Controller, null, remap, _outputFolder, false, out RecAnimationClipExportResult result);

            Assert.That(success, Is.False);
            Assert.That(result.Clips, Is.Empty);
            Assert.That(AssetDatabase.FindAssets("t:AnimationClip", new[] { _outputFolder }), Is.Empty, "何も保存しない");
        }

        [Test]
        public void TryExport_FolderOutsideAssets_ReturnsFalse()
        {
            TimelineE2ECharacter character = Prepare(new RecFixtureWriter.Recording());

            LogAssert.Expect(LogType.Error, new Regex("出力フォルダ"));
            bool success = RecAnimationClipExporter.TryExport(
                _fixture.RecordingPath, character.Controller, null, null, "Packages/foo", false, out RecAnimationClipExportResult _);

            Assert.That(success, Is.False);
        }

        [Test]
        public void Window_ExportWithInvalidCustomSourceId_RefusesToExport()
        {
            TimelineE2ECharacter character = Prepare(new RecFixtureWriter.Recording { IncludeValueProvider = true });
            var window = ScriptableObject.CreateInstance<RecAnimationClipExportWindow>();
            try
            {
                window.SetInputs(_fixture.RecordingPath, character.Controller, null, _outputFolder);
                Assert.That(window.RemapPanel.Report, Is.Not.Null, "前提: REC と Profile から対応表を作る");

                window.RemapPanel.SetRemapChoice(false, RecFixtureWriter.ValueProviderSourceId, "not a valid id");

                Assert.That(window.RemapPanel.HasErrors, Is.True, "解釈できない入力源 id はエラー");
                Assert.That(window.RemapPanel.ProblemText, Is.Not.Empty, "誤りを一覧に出す");
                Assert.That(window.Export(), Is.False, "誤りがあれば書き出さない");
                Assert.That(AssetDatabase.FindAssets("t:AnimationClip", new[] { _outputFolder }), Is.Empty);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void Window_ExportWithDefaults_WritesClipsUsingCharacterProfile()
        {
            TimelineE2ECharacter character = Prepare(new RecFixtureWriter.Recording());
            var window = ScriptableObject.CreateInstance<RecAnimationClipExportWindow>();
            try
            {
                window.SetInputs(_fixture.RecordingPath, character.Controller, null, _outputFolder);

                Assert.That(window.Export(), Is.True, window.StatusText);
                Assert.That(window.LastResult.Clips.Count, Is.GreaterThan(0));
                Assert.That(window.StatusText, Does.Contain(window.LastResult.Clips[0].AssetPath));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void RemapPanel_RebuildWithDifferentRecording_ResetsChoices()
        {
            TimelineE2ECharacter character = Prepare(new RecFixtureWriter.Recording { IncludeValueProvider = true });
            var window = ScriptableObject.CreateInstance<RecAnimationClipExportWindow>();
            try
            {
                window.SetInputs(_fixture.RecordingPath, character.Controller, null, _outputFolder);
                window.RemapPanel.SetRemapChoice(false, RecFixtureWriter.ValueProviderSourceId, null);
                Assert.That(window.RemapPanel.BuildSourceRemap().IsIdentity, Is.False, "前提: 除外を選んだ");

                window.Reload();
                Assert.That(window.RemapPanel.BuildSourceRemap().IsIdentity, Is.False, "同じ REC の読み直しでは選択を残す");

                string other = WriteOtherRecording();
                window.SetInputs(other, character.Controller, null, _outputFolder);

                Assert.That(window.RemapPanel.BuildSourceRemap().IsIdentity, Is.True, "別の REC には前の選択を持ち込まない");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void Window_RecordingChangedAfterRemap_RefusesToExport()
        {
            TimelineE2ECharacter character = Prepare(new RecFixtureWriter.Recording { IncludeValueProvider = true });
            var window = ScriptableObject.CreateInstance<RecAnimationClipExportWindow>();
            try
            {
                window.SetInputs(_fixture.RecordingPath, character.Controller, null, _outputFolder);
                window.RemapPanel.SetRemapChoice(false, RecFixtureWriter.ValueProviderSourceId, null);

                // 対応表を作った後に REC の中身が変わった（読み込み直していない）。
                File.Copy(WriteOtherRecording(), _fixture.RecordingPath, true);

                Assert.That(window.Export(), Is.False, "前の REC 用の選択を当てない");
                Assert.That(window.StatusText, Does.Contain("Reload Sources"));
                Assert.That(AssetDatabase.FindAssets("t:AnimationClip", new[] { _outputFolder }), Is.Empty);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
            }
        }

        /// <summary>fixture の REC とは中身が違う REC（値提供型の入力源は同じ）を書き、絶対パスを返す。</summary>
        private string WriteOtherRecording()
        {
            string path = Path.Combine(Path.GetDirectoryName(_fixture.RecordingPath), "other.fcrec");
            RecFixtureWriter.Write(path, new RecFixtureWriter.Recording
            {
                IncludeValueProvider = true,
                IncludeLayerWeights = true,
                BlendShapeNames = TimelineE2EFixture.MeshBlendShapes,
            });
            return path;
        }

        private TimelineE2ECharacter Prepare(RecFixtureWriter.Recording recording)
        {
            _fixture = TimelineE2EFixture.Create(recording);
            _fixture.MarkProfileChanged();
            FacialCharacterProfileAutoExporter.ExportIfEnabled(_fixture.ProfileAsset);
            TimelineProfileSource.InvalidateCache(_fixture.ProfileAsset);
            _fixture.Rebake();
            string folder = Path.GetDirectoryName(AssetDatabase.GetAssetPath(_fixture.Timeline)).Replace('\\', '/');
            AssetDatabase.CreateFolder(folder, "Direct");
            _outputFolder = folder + "/Direct";
            return _fixture.Spawn(TimelineE2EPlacement.SameObject);
        }

        private static float[] ReadWeights(TimelineE2ECharacter character)
        {
            string[] names = TimelineE2EFixture.MeshBlendShapes;
            var weights = new float[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                weights[i] = character.GetBlendShapeWeight(names[i]);
            }

            return weights;
        }
    }
}
#endif
