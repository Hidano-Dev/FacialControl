#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Hidano.FacialControl.Adapters.Playable;
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

            // 対応表の判定は REC Export と同じ処理（そのログ）を使い、書き出さなかった理由を重ねて出す。
            LogAssert.Expect(LogType.Error, new Regex("collisions or invalid ids"));
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

        [Test]
        public void Window_RecordingChangedWithIdentityRemap_RefusesToExport()
        {
            TimelineE2ECharacter character = Prepare(new RecFixtureWriter.Recording { IncludeValueProvider = true });
            var window = ScriptableObject.CreateInstance<RecAnimationClipExportWindow>();
            try
            {
                window.SetInputs(_fixture.RecordingPath, character.Controller, null, _outputFolder);
                File.Copy(WriteOtherRecording(), _fixture.RecordingPath, true);

                Assert.That(window.Export(), Is.False, "対応表が恒等でも、読み込んだ内容と違う REC は書き出さない");
                Assert.That(window.StatusText, Does.Contain("Reload Sources"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void Window_ChangeCharacter_ReplacesAutoFilledProfileWithoutReloadingRecording()
        {
            TimelineE2ECharacter character = Prepare(new RecFixtureWriter.Recording());
            TimelineE2ECharacter other = _fixture.Spawn(TimelineE2EPlacement.SameObject);
            var otherProfile = ScriptableObject.CreateInstance<Hidano.FacialControl.Adapters.ScriptableObject.Serializable.FacialCharacterProfileSO>();
            otherProfile.name = "OtherProfile";
            other.Controller.CharacterSO = otherProfile;
            var window = ScriptableObject.CreateInstance<RecAnimationClipExportWindow>();
            try
            {
                window.SetInputs(_fixture.RecordingPath, character.Controller, null, _outputFolder);
                int loads = window.RecordingLoadCount;
                Assert.That(window.ProfileValue, Is.SameAs(_fixture.ProfileAsset), "前提: キャラの Character SO が入る");

                window.SetController(other.Controller);

                Assert.That(window.ProfileValue, Is.SameAs(otherProfile), "自動で入れた Profile は新しいキャラの SO に差し替える");
                Assert.That(window.RecordingLoadCount, Is.EqualTo(loads), "キャラの変更では REC を読み直さない");
                Assert.That(window.RemapPanel.NoticeText, Is.Empty);

                window.SetProfile(_fixture.ProfileAsset);
                Assert.That(window.RemapPanel.NoticeText, Does.Contain("OtherProfile"), "キャラと Profile が食い違えば警告する");

                window.SetController(character.Controller);
                Assert.That(window.ProfileValue, Is.SameAs(_fixture.ProfileAsset), "手で選んだ Profile はそのまま");
                Assert.That(window.RemapPanel.NoticeText, Is.Empty, "食い違いが解消したら警告を消す");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
                other.Controller.CharacterSO = null;
                UnityEngine.Object.DestroyImmediate(otherProfile);
            }
        }

        [Test]
        public void Window_ExceptionWhileSaving_RollsBackAndShowsError()
        {
            TimelineE2ECharacter character = Prepare(new RecFixtureWriter.Recording());
            var window = ScriptableObject.CreateInstance<RecAnimationClipExportWindow>();
            try
            {
                window.SetInputs(_fixture.RecordingPath, character.Controller, null, _outputFolder, createTimeline: true);
                Assert.That(window.Export(), Is.True, "前提: 書き出せる");
                Assert.That(window.LastResult, Is.Not.Null);
                foreach (string guid in AssetDatabase.FindAssets(string.Empty, new[] { _outputFolder }))
                {
                    AssetDatabase.DeleteAsset(AssetDatabase.GUIDToAssetPath(guid));
                }

                RecAnimationClipExporter.BeforeCreateTimelineForTests = _ => throw new InvalidOperationException("save failed for test");
                LogAssert.Expect(LogType.Exception, new Regex("save failed for test"));
                bool exported = window.Export();

                Assert.That(exported, Is.False, "例外は捕まえて失敗として返す");
                Assert.That(window.LastResult, Is.Null, "結果を残さない");
                Assert.That(window.StatusText, Does.Contain("save failed for test"));
                Assert.That(AssetDatabase.FindAssets("t:AnimationClip", new[] { _outputFolder }), Is.Empty, "途中まで保存した .anim を消す");
                Assert.That(AssetDatabase.FindAssets("t:TimelineAsset", new[] { _outputFolder }), Is.Empty);
            }
            finally
            {
                RecAnimationClipExporter.BeforeCreateTimelineForTests = null;
                UnityEngine.Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void TryExport_ExceptionWhileSaving_DeletesSavedClipsAndDestroysUnsaved()
        {
            TimelineE2ECharacter character = Prepare(new RecFixtureWriter.Recording { IncludeValueProvider = true });
            int clipCount = Resources.FindObjectsOfTypeAll<AnimationClip>().Length;
            RecAnimationClipExporter.BeforeCreateTimelineForTests = _ => throw new InvalidOperationException("save failed for test");
            try
            {
                Assert.Throws<InvalidOperationException>(() => RecAnimationClipExporter.TryExport(
                    _fixture.RecordingPath, character.Controller, null, null, _outputFolder, true, out RecAnimationClipExportResult _));
            }
            finally
            {
                RecAnimationClipExporter.BeforeCreateTimelineForTests = null;
            }

            Assert.That(AssetDatabase.FindAssets("t:AnimationClip", new[] { _outputFolder }), Is.Empty, "保存した .anim を消す");
            Assert.That(Resources.FindObjectsOfTypeAll<AnimationClip>().Length, Is.EqualTo(clipCount), "Clip をメモリに残さない");
        }

        [Test]
        public void DestroyScratch_TimelineWithChildTracksAndClips_DestroysEverything()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            var parent = timeline.CreateTrack<GroupTrack>(null, "Group");
            var child = timeline.CreateTrack<AnimationTrack>(parent, "Child");
            TimelineClip clip = child.CreateDefaultClip();
            UnityEngine.Object clipAsset = clip.asset;
            var bake = ScriptableObject.CreateInstance<FacialTimelineBakeAsset>();

            RecAnimationClipExporter.DestroyScratch(timeline, bake);

            Assert.That(timeline == null, Is.True);
            Assert.That(parent == null, Is.True, "親トラック");
            Assert.That(child == null, Is.True, "子トラック");
            Assert.That(clipAsset == null, Is.True, "Clip アセット");
            Assert.That(bake == null, Is.True);
        }

        [Test]
        public void RemapPanel_CustomSourceId_ShowsCustomChoiceInDropdown()
        {
            TimelineE2ECharacter character = Prepare(new RecFixtureWriter.Recording { IncludeValueProvider = true });
            var window = ScriptableObject.CreateInstance<RecAnimationClipExportWindow>();
            try
            {
                window.SetInputs(_fixture.RecordingPath, character.Controller, null, _outputFolder);
                window.RemapPanel.SetRemapChoice(false, RecFixtureWriter.ValueProviderSourceId, "osc-custom");
                window.Reload();

                var dropdowns = UnityEngine.UIElements.UQueryExtensions.Query<UnityEngine.UIElements.DropdownField>(window.RemapPanel).ToList();
                UnityEngine.UIElements.DropdownField custom = dropdowns.Find(d => d.value == RecSourceRemapPanel.CustomChoice);
                Assert.That(custom, Is.Not.Null, "手入力の行は「(手入力)」と表示する");
                Assert.That(custom.choices, Does.Contain(RecTimelineExportWindow.KeepChoice), "「そのまま」を選び直せる");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void TryExport_PrefabAsset_CurvesMatchSamePrefabPlacedInScene()
        {
            Prepare(new RecFixtureWriter.Recording
            {
                IncludeValueProvider = true,
                IncludeLayerWeights = true,
            });
            GameObject prefab = CreateCharacterPrefab();
            var sceneInstance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            try
            {
                bool sceneSuccess = RecAnimationClipExporter.TryExport(
                    _fixture.RecordingPath, sceneInstance.GetComponent<FacialController>(), null, null, _outputFolder, false,
                    out RecAnimationClipExportResult expected);
                Assert.That(sceneSuccess, Is.True, "前提: シーンに置いた prefab から書き出せる: " + expected.Error);

                bool success = RecAnimationClipExporter.TryExport(
                    _fixture.RecordingPath, prefab.GetComponent<FacialController>(), null, null, _outputFolder, false,
                    out RecAnimationClipExportResult actual);

                Assert.That(success, Is.True, actual.Error);
                AssertSameCurves(expected, actual);
                Assert.That(HasKind(actual, FacialAnimationClipKind.Gaze), Is.True, "目ボーンの Clip も作る（prefab の初期姿勢から）");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(sceneInstance);
            }
        }

        [Test]
        public void TryExport_PrefabAsset_LeavesPrefabAndScenesUntouched()
        {
            Prepare(new RecFixtureWriter.Recording { IncludeValueProvider = true });
            GameObject prefab = CreateCharacterPrefab();
            string prefabPath = AssetDatabase.GetAssetPath(prefab);
            byte[] prefabBytes = File.ReadAllBytes(prefabPath);
            int previewSceneCount = UnityEditor.SceneManagement.EditorSceneManager.previewSceneCount;
            int sceneCount = UnityEngine.SceneManagement.SceneManager.sceneCount;
            int rootCount = UnityEngine.SceneManagement.SceneManager.GetActiveScene().rootCount;
            int controllerCount = Resources.FindObjectsOfTypeAll<FacialController>().Length;
            FacialController loaded = null;
            RecAnimationClipExporter.PrefabInstanceLoadedForTests = instance => loaded = instance;
            try
            {
                bool success = RecAnimationClipExporter.TryExport(
                    _fixture.RecordingPath, prefab.GetComponent<FacialController>(), null, null, _outputFolder, false,
                    out RecAnimationClipExportResult result);

                Assert.That(success, Is.True, result.Error);
            }
            finally
            {
                RecAnimationClipExporter.PrefabInstanceLoadedForTests = null;
            }

            Assert.That(ReferenceEquals(loaded, null), Is.False, "prefab を一時的に読み込んで評価する");
            Assert.That(loaded.IsInitialized, Is.False, "一時インスタンスの FacialController は初期化しない（OnEnable を走らせない）");
            Assert.That(loaded == null, Is.True, "読み込んだ一時インスタンスは破棄する");
            Assert.That(UnityEditor.SceneManagement.EditorSceneManager.previewSceneCount, Is.EqualTo(previewSceneCount), "隔離シーンを残さない");
            Assert.That(UnityEngine.SceneManagement.SceneManager.sceneCount, Is.EqualTo(sceneCount), "シーンを開かない");
            Assert.That(UnityEngine.SceneManagement.SceneManager.GetActiveScene().rootCount, Is.EqualTo(rootCount), "開いているシーンに置かない");
            Assert.That(Resources.FindObjectsOfTypeAll<FacialController>().Length, Is.EqualTo(controllerCount), "一時インスタンスを残さない");
            Assert.That(EditorUtility.IsDirty(prefab), Is.False, "prefab アセットを dirty にしない");
            Assert.That(File.ReadAllBytes(prefabPath), Is.EqualTo(prefabBytes), "prefab アセットを書き換えない");
        }

        [Test]
        public void Window_PrefabAsset_AutoFillsProfileAndExports()
        {
            Prepare(new RecFixtureWriter.Recording());
            GameObject prefab = CreateCharacterPrefab();
            var window = ScriptableObject.CreateInstance<RecAnimationClipExportWindow>();
            try
            {
                window.SetInputs(_fixture.RecordingPath, prefab.GetComponent<FacialController>(), null, _outputFolder);

                Assert.That(window.ProfileValue, Is.SameAs(_fixture.ProfileAsset), "prefab の Character SO を Profile に入れる");
                Assert.That(window.Export(), Is.True, window.StatusText);
                Assert.That(window.LastResult.Clips.Count, Is.GreaterThan(0));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
            }
        }

        /// <summary>
        /// fixture のキャラと同じ構成（Animator・BlendShape 付きメッシュ・目ボーン・FacialController）の prefab を fixture のフォルダに保存して返す。
        /// メッシュも prefab から参照できるようアセットとして保存する（fixture の後片付けでフォルダごと消える）。
        /// </summary>
        private GameObject CreateCharacterPrefab()
        {
            string folder = Path.GetDirectoryName(AssetDatabase.GetAssetPath(_fixture.Timeline)).Replace('\\', '/');
            Mesh mesh = TimelineE2ECharacter.CreateMesh("PrefabCharacter_Mesh");
            AssetDatabase.CreateAsset(mesh, folder + "/PrefabCharacter_Mesh.asset");

            var root = new GameObject("PrefabCharacter");
            try
            {
                root.SetActive(false);
                root.AddComponent<Animator>();
                var face = new GameObject("Face");
                face.transform.SetParent(root.transform, false);
                face.AddComponent<SkinnedMeshRenderer>().sharedMesh = mesh;
                AddChild(root.transform, TimelineE2EFixture.LeftEyeName, new Vector3(-0.03f, 0f, 0f));
                AddChild(root.transform, TimelineE2EFixture.RightEyeName, new Vector3(0.03f, 0f, 0f));
                root.AddComponent<FacialController>().CharacterSO = _fixture.ProfileAsset;
                root.SetActive(true);
                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, folder + "/PrefabCharacter.prefab");
                Assert.That(prefab, Is.Not.Null, "前提: prefab を保存できる");
                return prefab;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void AddChild(Transform parent, string name, Vector3 localPosition)
        {
            var child = new GameObject(name).transform;
            child.SetParent(parent, false);
            child.localPosition = localPosition;
        }

        private static bool HasKind(RecAnimationClipExportResult result, FacialAnimationClipKind kind)
        {
            for (int i = 0; i < result.Clips.Count; i++)
            {
                if (result.Clips[i].Part.Kind == kind)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>2 つの書き出し結果が、同じ並び・同じプロパティで、Timeline のフレームごとに同じ値になることを確かめる。</summary>
        private void AssertSameCurves(RecAnimationClipExportResult expected, RecAnimationClipExportResult actual)
        {
            Assert.That(actual.Clips.Count, Is.EqualTo(expected.Clips.Count), "同じ数の Clip");
            double frameRate = _fixture.Timeline.editorSettings.frameRate;
            double duration = _fixture.Timeline.duration;
            int frameCount = (int)Math.Ceiling(duration * frameRate);
            for (int c = 0; c < expected.Clips.Count; c++)
            {
                FacialAnimationClipPart e = expected.Clips[c].Part;
                FacialAnimationClipPart a = actual.Clips[c].Part;
                Assert.That(a.ClipName, Is.EqualTo(e.ClipName), "同じ並び・名前");
                Assert.That(a.Kind, Is.EqualTo(e.Kind));
                EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(e.Clip);
                Assert.That(AnimationUtility.GetCurveBindings(a.Clip), Is.EquivalentTo(bindings), $"{e.ClipName}: 同じプロパティ");
                float tolerance = e.Kind == FacialAnimationClipKind.Gaze ? RotationComponentTolerance : BlendShapeTolerance;
                for (int b = 0; b < bindings.Length; b++)
                {
                    AnimationCurve expectedCurve = AnimationUtility.GetEditorCurve(e.Clip, bindings[b]);
                    AnimationCurve actualCurve = AnimationUtility.GetEditorCurve(a.Clip, bindings[b]);
                    for (int f = 0; f <= frameCount; f++)
                    {
                        float time = (float)Math.Min(f / frameRate, duration);
                        Assert.That(
                            actualCurve.Evaluate(time),
                            Is.EqualTo(expectedCurve.Evaluate(time)).Within(tolerance),
                            $"{e.ClipName} {bindings[b].propertyName} t={time:F4}");
                    }
                }
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
