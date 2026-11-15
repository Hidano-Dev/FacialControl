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
            var expectedParts = new List<FacialAnimationClipPart>();
            foreach (AnimationTrackConversionClip clip in converted[0].Clips)
            {
                expectedParts.Add(clip.Part);
            }

            AssertSameCurves(expectedParts, PartsOf(result));
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
                Assert.That(window.RemapPanel.IsExpanded, Is.False, "誤りが無ければ対応表は畳んでおく");

                window.RemapPanel.SetRemapChoice(false, RecFixtureWriter.ValueProviderSourceId, "not a valid id");

                Assert.That(window.RemapPanel.HasErrors, Is.True, "解釈できない入力源 id はエラー");
                Assert.That(window.RemapPanel.ProblemText, Is.Not.Empty, "誤りを一覧に出す");
                Assert.That(window.RemapPanel.IsExpanded, Is.True, "書き出しを止める誤りがあれば対応表を開く");
                Assert.That(window.RemapPanel.HeaderText, Does.Contain("誤り"), "畳んでいても見出しで分かる");
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
        public void Window_RecordingChangedAfterRemap_ReloadsAndStopsBecauseChoicesWereReset()
        {
            TimelineE2ECharacter character = Prepare(new RecFixtureWriter.Recording { IncludeValueProvider = true });
            var window = ScriptableObject.CreateInstance<RecAnimationClipExportWindow>();
            try
            {
                window.SetInputs(_fixture.RecordingPath, character.Controller, null, _outputFolder);
                window.RemapPanel.SetRemapChoice(false, RecFixtureWriter.ValueProviderSourceId, null);
                int loads = window.RecordingLoadCount;

                // 対応表を作った後に REC の中身が変わった（読み込み直していない）。
                File.Copy(WriteOtherRecording(), _fixture.RecordingPath, true);

                Assert.That(window.Export(), Is.False, "前の REC 用の選択を当てず、選び直してもらう");
                Assert.That(window.RecordingLoadCount, Is.EqualTo(loads + 1), "変わった REC を読み直す");
                Assert.That(window.RemapPanel.BuildSourceRemap().IsIdentity, Is.True, "前の REC 用の選択は持ち込まない");
                Assert.That(window.StatusText, Does.Contain("読み直しました"));
                Assert.That(window.RemapPanel.IsExpanded, Is.True, "確かめてもらう対応表を開く");
                Assert.That(AssetDatabase.FindAssets("t:AnimationClip", new[] { _outputFolder }), Is.Empty);

                Assert.That(window.Export(), Is.True, "読み直した後はそのまま書き出せる: " + window.StatusText);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void Window_RecordingChangedWithIdentityRemap_ReloadsAndExports()
        {
            TimelineE2ECharacter character = Prepare(new RecFixtureWriter.Recording { IncludeValueProvider = true });
            var window = ScriptableObject.CreateInstance<RecAnimationClipExportWindow>();
            try
            {
                window.SetInputs(_fixture.RecordingPath, character.Controller, null, _outputFolder);
                int loads = window.RecordingLoadCount;
                File.Copy(WriteOtherRecording(), _fixture.RecordingPath, true);

                Assert.That(window.Export(), Is.True, "対応表を選んでいなければ、読み直して書き出す: " + window.StatusText);
                Assert.That(window.RecordingLoadCount, Is.EqualTo(loads + 1), "変わった REC を読み直す");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void Window_RecordingUnchanged_ExportsWithoutReloading()
        {
            TimelineE2ECharacter character = Prepare(new RecFixtureWriter.Recording { IncludeValueProvider = true });
            var window = ScriptableObject.CreateInstance<RecAnimationClipExportWindow>();
            try
            {
                window.SetInputs(_fixture.RecordingPath, character.Controller, null, _outputFolder);
                int loads = window.RecordingLoadCount;

                Assert.That(window.Export(), Is.True, window.StatusText);
                Assert.That(window.RecordingLoadCount, Is.EqualTo(loads), "変わっていない REC は読み直さない");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void RemapPanel_UnresolvedSource_StaysCollapsedAndShowsInHeader()
        {
            TimelineE2ECharacter character = Prepare(new RecFixtureWriter.Recording { IncludeValueProvider = true });
            var window = ScriptableObject.CreateInstance<RecAnimationClipExportWindow>();
            try
            {
                window.SetInputs(_fixture.RecordingPath, character.Controller, null, _outputFolder);
                Assert.That(window.RemapPanel.HeaderText, Does.Not.Contain("未対応"), "前提: すべて対応している");

                // 形式は正しいが Profile に宣言の無い id へ付け替える（書き出せるが、その分は再生されない）。
                window.RemapPanel.SetRemapChoice(false, RecFixtureWriter.ValueProviderSourceId, "osc-custom");

                Assert.That(window.RemapPanel.HasErrors, Is.False, "前提: 書き出しは止めない");
                Assert.That(window.RemapPanel.IsExpanded, Is.False, "未対応だけなら開かない");
                Assert.That(window.RemapPanel.HeaderText, Does.Contain("未対応"), "畳んでいても見出しで分かる");
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
            TimelineE2ECharacter character = Prepare(new RecFixtureWriter.Recording
            {
                IncludeValueProvider = true,
                IncludeLayerWeights = true,
            });
            GameObject prefab = SaveAsPrefab(character.Root, "PrefabCharacter");

            ExportFromSceneInstanceAndPrefab(prefab, null, out RecAnimationClipExportResult expected, out RecAnimationClipExportResult actual);

            AssertSameCurves(PartsOf(expected), PartsOf(actual));
            Assert.That(HasKind(actual, FacialAnimationClipKind.Gaze), Is.True, "目ボーンの Clip も作る（prefab の初期姿勢から）");
        }

        // 録画時の目ボーンのパスが実在する場合（VRM 等で録った REC）と、見つからず Humanoid Avatar の LeftEye / RightEye へ
        // 落ちる場合（別キャラで録った REC）。どちらも Profile の目ボーン名は空にして、Avatar の rest 姿勢・目ボーンを使わせる。
        [TestCase("Hips/Spine/Neck/Head/LeftEye", "Hips/Spine/Neck/Head/RightEye")]
        [TestCase("Missing/LeftEye", "Missing/RightEye")]
        public void TryExport_HumanoidPrefabAsset_GazeMatchesSamePrefabPlacedInScene(string recordedLeftEye, string recordedRightEye)
        {
            Prepare(
                new RecFixtureWriter.Recording { LeftEyeBonePath = recordedLeftEye, RightEyeBonePath = recordedRightEye },
                profile =>
                {
                    profile.GazeChannels[0].leftEyeBonePath = string.Empty;
                    profile.GazeChannels[0].rightEyeBonePath = string.Empty;
                });
            GameObject root = BuildHumanoidCharacter(out Avatar avatar);
            GameObject prefab;
            try
            {
                AssetDatabase.CreateAsset(avatar, FixtureFolder() + "/HumanoidCharacter_Avatar.asset");
                prefab = SaveAsPrefab(root, "HumanoidCharacter");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }

            ExportFromSceneInstanceAndPrefab(prefab, null, out RecAnimationClipExportResult expected, out RecAnimationClipExportResult actual);

            Assert.That(HasKind(expected, FacialAnimationClipKind.Gaze), Is.True, "前提: シーンに置いた Humanoid は Avatar の目ボーンで Gaze を作る");
            AssertSameCurves(PartsOf(expected), PartsOf(actual));
        }

        [Test]
        public void TryExport_PrefabSavedWithInactiveRoot_EvaluatesLikeActivatedSceneInstance()
        {
            TimelineE2ECharacter character = Prepare(new RecFixtureWriter.Recording { IncludeValueProvider = true });
            character.Root.SetActive(false);
            GameObject prefab = SaveAsPrefab(character.Root, "InactiveCharacter");
            character.Root.SetActive(true);

            // シーンに置いたインスタンスはルートを有効にして使う（prefab 側は非アクティブのまま保存されている）。
            bool loadedInitialized = true;
            RecAnimationClipExporter.PrefabInstanceLoadedForTests = instance => loadedInitialized = instance.IsInitialized;
            RecAnimationClipExportResult expected;
            RecAnimationClipExportResult actual;
            try
            {
                ExportFromSceneInstanceAndPrefab(prefab, instance => instance.SetActive(true), out expected, out actual);
            }
            finally
            {
                RecAnimationClipExporter.PrefabInstanceLoadedForTests = null;
            }

            Assert.That(loadedInitialized, Is.False, "読み込んだ写しのルートを有効にしても FacialController は初期化しない");
            Assert.That(expected.Clips.Count, Is.GreaterThan(0), "前提: 有効にしたシーンのインスタンスから書き出せる");
            AssertSameCurves(PartsOf(expected), PartsOf(actual));
            Assert.That(prefab.activeSelf, Is.False, "prefab アセットのアクティブ状態は変えない");
        }

        [Test]
        public void TryExport_PrefabAsset_LeavesPrefabAndScenesUntouched()
        {
            TimelineE2ECharacter character = Prepare(new RecFixtureWriter.Recording { IncludeValueProvider = true });
            GameObject prefab = SaveAsPrefab(character.Root, "PrefabCharacter");
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
            TimelineE2ECharacter character = Prepare(new RecFixtureWriter.Recording());
            GameObject prefab = SaveAsPrefab(character.Root, "PrefabCharacter");
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

        [Test]
        public void TryExport_SplitPerBlendShape_OneClipPerBlendShapeWithSameCurvesAsLayerSplit()
        {
            TimelineE2ECharacter character = Prepare(new RecFixtureWriter.Recording
            {
                IncludeValueProvider = true,
                IncludeLayerWeights = true,
            });
            bool layerSuccess = RecAnimationClipExporter.TryExport(
                _fixture.RecordingPath, character.Controller, null, null, _outputFolder, false, out RecAnimationClipExportResult layered);
            Assert.That(layerSuccess, Is.True, "前提: レイヤー単位で書き出せる: " + layered.Error);

            bool success = RecAnimationClipExporter.TryExport(
                _fixture.RecordingPath, character.Controller, null, null, _outputFolder, false, out RecAnimationClipExportResult result,
                new FacialAnimationClipExportOptions(FacialAnimationClipSplitMode.BlendShape, 0d));

            Assert.That(success, Is.True, result.Error);
            var expectedBindings = new List<EditorCurveBinding>();
            for (int i = 0; i < layered.Clips.Count; i++)
            {
                expectedBindings.AddRange(AnimationUtility.GetCurveBindings(layered.Clips[i].Part.Clip));
            }

            var actualBindings = new List<EditorCurveBinding>();
            var shapeNames = new HashSet<string>();
            for (int i = 0; i < result.Clips.Count; i++)
            {
                FacialAnimationClipPart part = result.Clips[i].Part;
                Assert.That(part.Kind, Is.Not.EqualTo(FacialAnimationClipKind.Layer), "レイヤー別には分けない");
                EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(part.Clip);
                actualBindings.AddRange(bindings);
                if (part.Kind == FacialAnimationClipKind.Base)
                {
                    // どのレイヤーも書かなかった BlendShape は、レイヤー単位と同じく Base 1 本にまとめる（BlendShape ごとにトラックを作らない）。
                    AnimationClip layeredBase = FindKind(layered, FacialAnimationClipKind.Base);
                    Assert.That(layeredBase, Is.Not.Null, "前提: レイヤー単位にも Base がある");
                    Assert.That(bindings, Is.EquivalentTo(AnimationUtility.GetCurveBindings(layeredBase)), "Base の中身はレイヤー単位と同じ");
                    AssertSameValues(layeredBase, part.Clip, bindings, BlendShapeTolerance);
                    continue;
                }

                if (part.Kind != FacialAnimationClipKind.BlendShape)
                {
                    continue;
                }

                for (int b = 0; b < bindings.Length; b++)
                {
                    Assert.That(bindings[b].propertyName, Is.EqualTo("blendShape." + part.Name), $"{part.ClipName}: BlendShape 1 つ分だけを持つ");
                }

                Assert.That(shapeNames.Add(part.Name), Is.True, $"{part.Name}: BlendShape 1 つにつき Clip は 1 本");
                AssertSameValues(FindCurveOwner(layered, bindings[0]), part.Clip, bindings, BlendShapeTolerance);
            }

            Assert.That(actualBindings, Is.EquivalentTo(expectedBindings), "全体として同じプロパティを 1 回ずつ持つ（Clip 同士で重ならない）");
            Assert.That(shapeNames.Count, Is.GreaterThan(1), "前提: 複数の BlendShape に分かれる");
            Assert.That(HasKind(result, FacialAnimationClipKind.Gaze), Is.True, "目ボーンは 1 本の Gaze Clip のまま");
        }

        [Test]
        public void TryExport_FrameRateOverride_WritesClipsAtThatRateWithSameValues()
        {
            TimelineE2ECharacter character = Prepare(new RecFixtureWriter.Recording
            {
                IncludeValueProvider = true,
                IncludeLayerWeights = true,
            });
            bool baseSuccess = RecAnimationClipExporter.TryExport(
                _fixture.RecordingPath, character.Controller, null, null, _outputFolder, false, out RecAnimationClipExportResult full);
            Assert.That(baseSuccess, Is.True, "前提: 既定のフレームレートで書き出せる: " + full.Error);

            bool success = RecAnimationClipExporter.TryExport(
                _fixture.RecordingPath, character.Controller, null, null, _outputFolder, false, out RecAnimationClipExportResult result,
                new FacialAnimationClipExportOptions(FacialAnimationClipSplitMode.Layer, 15d));

            Assert.That(success, Is.True, result.Error);
            Assert.That(result.Clips.Count, Is.EqualTo(full.Clips.Count), "分割は変わらない");
            for (int i = 0; i < result.Clips.Count; i++)
            {
                Assert.That(result.Clips[i].Part.Clip.frameRate, Is.EqualTo(15f), "Clip のフレームレートを指定値にする");
            }

            // キー数は比べない（fixture のカーブは折れ線と段差で、既定のフレームレートでも間引きで最小限まで減っている）。
            // 指定したフレームの時刻では、既定のフレームレートで書き出した値と（間引きの許容誤差 2 回分以内で）一致する。
            for (int i = 0; i < result.Clips.Count; i++)
            {
                if (result.Clips[i].Part.Kind == FacialAnimationClipKind.Gaze)
                {
                    continue;
                }

                AnimationClip clip = result.Clips[i].Part.Clip;
                AssertSameValues(full.Clips[i].Part.Clip, clip, AnimationUtility.GetCurveBindings(clip),
                    (FacialAnimationCurveFactory.BlendShapeTolerance * 2f) + 0.01f, 15d);
            }
        }

        [Test]
        public void Window_ExportWithOptions_UsesSplitModeAndFrameRate()
        {
            TimelineE2ECharacter character = Prepare(new RecFixtureWriter.Recording { IncludeValueProvider = true });
            var window = ScriptableObject.CreateInstance<RecAnimationClipExportWindow>();
            try
            {
                window.SetInputs(_fixture.RecordingPath, character.Controller, null, _outputFolder, createTimeline: true,
                    options: new FacialAnimationClipExportOptions(FacialAnimationClipSplitMode.BlendShape, 30d));

                Assert.That(window.Export(), Is.True, window.StatusText);
                Assert.That(HasKind(window.LastResult, FacialAnimationClipKind.BlendShape), Is.True, "BlendShape 単位で書き出す");
                Assert.That(window.LastResult.Clips[0].Part.Clip.frameRate, Is.EqualTo(30f));
                var timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(window.LastResult.TimelineAssetPath);
                Assert.That(timeline.editorSettings.frameRate, Is.EqualTo(30d), "作る TimelineAsset も Clip と同じフレームレートにする");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void OptionsFields_SetOptionsWithNonPresetFrameRate_ReturnsSameValue()
        {
            var fields = new FacialAnimationClipExportOptionsFields();

            // 表示用の書式（小数 3 桁）で丸めた値を読み直さない。
            fields.SetOptions(new FacialAnimationClipExportOptions(FacialAnimationClipSplitMode.BlendShape, 29.97002997d));
            FacialAnimationClipExportOptions options = fields.Options;

            Assert.That(options.SplitMode, Is.EqualTo(FacialAnimationClipSplitMode.BlendShape));
            Assert.That(options.FrameRate, Is.EqualTo(29.97002997d));

            fields.SetOptions(new FacialAnimationClipExportOptions(FacialAnimationClipSplitMode.Layer, 29.97d));
            Assert.That(fields.Options.FrameRate, Is.EqualTo(29.97d), "表示が近い別の値も区別する");

            fields.SetOptions(new FacialAnimationClipExportOptions(FacialAnimationClipSplitMode.Layer, 0d));
            Assert.That(fields.Options.HasFrameRate, Is.False, "指定なしは Timeline に従う");
        }

        private static AnimationClip FindKind(RecAnimationClipExportResult result, FacialAnimationClipKind kind)
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

        /// <summary><paramref name="binding"/> を持つ Clip を <paramref name="result"/> から探す。</summary>
        private static AnimationClip FindCurveOwner(RecAnimationClipExportResult result, EditorCurveBinding binding)
        {
            for (int i = 0; i < result.Clips.Count; i++)
            {
                AnimationClip clip = result.Clips[i].Part.Clip;
                if (Array.IndexOf(AnimationUtility.GetCurveBindings(clip), binding) >= 0)
                {
                    return clip;
                }
            }

            Assert.Fail($"{binding.propertyName} を持つ Clip がない");
            return null;
        }

        /// <summary>
        /// <paramref name="bindings"/> の各カーブが、<paramref name="frameRate"/>（0 なら fixture の Timeline）のフレームごとに同じ値になることを確かめる。
        /// </summary>
        private void AssertSameValues(AnimationClip expected, AnimationClip actual, EditorCurveBinding[] bindings, float tolerance, double frameRate = 0d)
        {
            frameRate = frameRate > 0d ? frameRate : _fixture.Timeline.editorSettings.frameRate;
            double duration = _fixture.Timeline.duration;
            int frameCount = (int)Math.Ceiling(duration * frameRate);
            for (int b = 0; b < bindings.Length; b++)
            {
                AnimationCurve expectedCurve = AnimationUtility.GetEditorCurve(expected, bindings[b]);
                AnimationCurve actualCurve = AnimationUtility.GetEditorCurve(actual, bindings[b]);
                Assert.That(expectedCurve, Is.Not.Null, $"{bindings[b].propertyName}: 比較元にカーブがある");
                for (int f = 0; f <= frameCount; f++)
                {
                    float time = (float)Math.Min(f / frameRate, duration);
                    Assert.That(
                        actualCurve.Evaluate(time),
                        Is.EqualTo(expectedCurve.Evaluate(time)).Within(tolerance),
                        $"{bindings[b].propertyName} t={time:F4}");
                }
            }
        }

        private string FixtureFolder()
        {
            return Path.GetDirectoryName(AssetDatabase.GetAssetPath(_fixture.Timeline)).Replace('\\', '/');
        }

        /// <summary>
        /// キャラ（fixture が配置したもの等）を fixture のフォルダに prefab として保存して返す。
        /// メモリ上だけのメッシュは prefab から参照できないので、写しをアセットとして保存して付け替える（fixture の後片付けでフォルダごと消える）。
        /// </summary>
        private GameObject SaveAsPrefab(GameObject root, string name)
        {
            string folder = FixtureFolder();
            foreach (SkinnedMeshRenderer renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (renderer.sharedMesh != null && !EditorUtility.IsPersistent(renderer.sharedMesh))
                {
                    Mesh copy = UnityEngine.Object.Instantiate(renderer.sharedMesh);
                    AssetDatabase.CreateAsset(copy, AssetDatabase.GenerateUniqueAssetPath(folder + "/" + name + "_Mesh.asset"));
                    renderer.sharedMesh = copy;
                }
            }

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, folder + "/" + name + ".prefab");
            Assert.That(prefab, Is.Not.Null, "前提: prefab を保存できる");
            return prefab;
        }

        /// <summary>
        /// 同じ prefab をシーンに置いたインスタンスと prefab アセットの両方から書き出す。
        /// <paramref name="prepareSceneInstance"/> はシーンのインスタンスを書き出し前に整える（ルートを有効にする等）。
        /// </summary>
        private void ExportFromSceneInstanceAndPrefab(
            GameObject prefab,
            Action<GameObject> prepareSceneInstance,
            out RecAnimationClipExportResult expected,
            out RecAnimationClipExportResult actual)
        {
            var sceneInstance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            try
            {
                prepareSceneInstance?.Invoke(sceneInstance);
                bool sceneSuccess = RecAnimationClipExporter.TryExport(
                    _fixture.RecordingPath, sceneInstance.GetComponent<FacialController>(), null, null, _outputFolder, false, out expected);
                Assert.That(sceneSuccess, Is.True, "前提: シーンに置いた prefab から書き出せる: " + expected.Error);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(sceneInstance);
            }

            bool success = RecAnimationClipExporter.TryExport(
                _fixture.RecordingPath, prefab.GetComponent<FacialController>(), null, null, _outputFolder, false, out actual);
            Assert.That(success, Is.True, actual.Error);
        }

        /// <summary>
        /// 目ボーンを Humanoid Avatar にだけマップしたキャラ（Hips〜Head の下に LeftEye / RightEye、顔メッシュ、FacialController）を作る。
        /// Avatar はアセットとして保存されていないので、呼び出し側で保存する。
        /// </summary>
        private GameObject BuildHumanoidCharacter(out Avatar avatar)
        {
            var root = new GameObject("HumanoidCharacter");
            root.SetActive(false);
            Transform hips = MakeBone("Hips", root.transform, new Vector3(0f, 1.0f, 0f));
            Transform spine = MakeBone("Spine", hips, new Vector3(0f, 0.2f, 0f));
            Transform neck = MakeBone("Neck", spine, new Vector3(0f, 0.4f, 0f));
            Transform head = MakeBone("Head", neck, new Vector3(0f, 0.2f, 0f));
            MakeBone("LeftEye", head, new Vector3(-0.04f, 0.1f, 0.08f));
            MakeBone("RightEye", head, new Vector3(0.04f, 0.1f, 0.08f));
            Transform lUpperArm = MakeBone("LeftUpperArm", spine, new Vector3(-0.2f, 0.4f, 0f));
            Transform lLowerArm = MakeBone("LeftLowerArm", lUpperArm, new Vector3(-0.25f, 0f, 0f));
            MakeBone("LeftHand", lLowerArm, new Vector3(-0.25f, 0f, 0f));
            Transform rUpperArm = MakeBone("RightUpperArm", spine, new Vector3(0.2f, 0.4f, 0f));
            Transform rLowerArm = MakeBone("RightLowerArm", rUpperArm, new Vector3(0.25f, 0f, 0f));
            MakeBone("RightHand", rLowerArm, new Vector3(0.25f, 0f, 0f));
            Transform lUpperLeg = MakeBone("LeftUpperLeg", hips, new Vector3(-0.1f, -0.05f, 0f));
            Transform lLowerLeg = MakeBone("LeftLowerLeg", lUpperLeg, new Vector3(0f, -0.4f, 0f));
            MakeBone("LeftFoot", lLowerLeg, new Vector3(0f, -0.4f, 0f));
            Transform rUpperLeg = MakeBone("RightUpperLeg", hips, new Vector3(0.1f, -0.05f, 0f));
            Transform rLowerLeg = MakeBone("RightLowerLeg", rUpperLeg, new Vector3(0f, -0.4f, 0f));
            MakeBone("RightFoot", rLowerLeg, new Vector3(0f, -0.4f, 0f));

            HumanBodyBones[] mapped =
            {
                HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Neck, HumanBodyBones.Head,
                HumanBodyBones.LeftEye, HumanBodyBones.RightEye,
                HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand,
                HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand,
                HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot,
                HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot,
            };
            var humanBones = new HumanBone[mapped.Length];
            for (int i = 0; i < mapped.Length; i++)
            {
                string boneName = HumanTrait.BoneName[(int)mapped[i]];
                humanBones[i] = new HumanBone
                {
                    humanName = boneName,
                    boneName = mapped[i].ToString(),
                    limit = new HumanLimit { useDefaultValues = true },
                };
            }

            var skeleton = new List<SkeletonBone>();
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                skeleton.Add(new SkeletonBone
                {
                    name = t.name,
                    position = t.localPosition,
                    rotation = t.localRotation,
                    scale = t.localScale,
                });
            }

            avatar = AvatarBuilder.BuildHumanAvatar(root, new HumanDescription
            {
                human = humanBones,
                skeleton = skeleton.ToArray(),
                upperArmTwist = 0.5f,
                lowerArmTwist = 0.5f,
                upperLegTwist = 0.5f,
                lowerLegTwist = 0.5f,
                armStretch = 0.05f,
                legStretch = 0.05f,
                hasTranslationDoF = false,
            });
            Assert.That(avatar.isValid && avatar.isHuman, Is.True, "前提: Humanoid Avatar を作れる");
            avatar.name = "HumanoidCharacter_Avatar";
            root.AddComponent<Animator>().avatar = avatar;

            var face = new GameObject("Face");
            face.transform.SetParent(root.transform, false);
            Mesh mesh = TimelineE2ECharacter.CreateMesh("HumanoidCharacter_Mesh");
            AssetDatabase.CreateAsset(mesh, FixtureFolder() + "/HumanoidCharacter_Mesh.asset");
            face.AddComponent<SkinnedMeshRenderer>().sharedMesh = mesh;
            root.AddComponent<FacialController>().CharacterSO = _fixture.ProfileAsset;
            root.SetActive(true);
            return root;
        }

        private static Transform MakeBone(string name, Transform parent, Vector3 localPosition)
        {
            var bone = new GameObject(name).transform;
            bone.SetParent(parent, false);
            bone.localPosition = localPosition;
            bone.localRotation = Quaternion.identity;
            return bone;
        }

        private static List<FacialAnimationClipPart> PartsOf(RecAnimationClipExportResult result)
        {
            var parts = new List<FacialAnimationClipPart>(result.Clips.Count);
            for (int i = 0; i < result.Clips.Count; i++)
            {
                parts.Add(result.Clips[i].Part);
            }

            return parts;
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

        /// <summary>2 組の Clip が、同じ並び・同じプロパティで、Timeline のフレームごとに同じ値になることを確かめる。</summary>
        private void AssertSameCurves(IReadOnlyList<FacialAnimationClipPart> expected, IReadOnlyList<FacialAnimationClipPart> actual)
        {
            Assert.That(actual.Count, Is.EqualTo(expected.Count), "同じ数の Clip（レイヤー別 + Base + Gaze）");
            double frameRate = _fixture.Timeline.editorSettings.frameRate;
            double duration = _fixture.Timeline.duration;
            int frameCount = (int)Math.Ceiling(duration * frameRate);
            for (int c = 0; c < expected.Count; c++)
            {
                FacialAnimationClipPart e = expected[c];
                FacialAnimationClipPart a = actual[c];
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

        /// <param name="configureProfile">Bake の前に fixture の Profile を変える（目ボーン名を空にする等）。null なら変えない。</param>
        private TimelineE2ECharacter Prepare(
            RecFixtureWriter.Recording recording,
            Action<Hidano.FacialControl.Adapters.ScriptableObject.Serializable.FacialCharacterProfileSO> configureProfile = null)
        {
            _fixture = TimelineE2EFixture.Create(recording);
            configureProfile?.Invoke(_fixture.ProfileAsset);
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
