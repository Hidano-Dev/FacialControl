using System;
using System.Collections.Generic;
using System.IO;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Testing;
using Hidano.FacialControl.Timeline.Adapters;
using Hidano.FacialControl.Timeline.Editor;
using Hidano.FacialControl.Timeline.Tests.Shared;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.UIElements;

namespace Hidano.FacialControl.Timeline.Tests.EditMode
{
    /// <summary>
    /// REC Export ウィンドウの生成・破棄 smoke と、配線指定欄が無いこと・検出結果の読み取り専用リスト・Export 後の残り手順を固定する（見た目は検証しない）。
    /// </summary>
    [MediumTest]
    public sealed class RecTimelineExportWindowTests : SizedTestFixture
    {
        private RecTimelineExportWindow _window;
        private string _folderPath;

        [TearDown]
        public void TearDown()
        {
            if (_window != null)
            {
                UnityEngine.Object.DestroyImmediate(_window);
                _window = null;
            }

            if (!string.IsNullOrEmpty(_folderPath) && AssetDatabase.IsValidFolder(_folderPath))
            {
                AssetDatabase.DeleteAsset(_folderPath);
            }

            _folderPath = null;
        }

        [Test]
        public void Build_HasNoDirectorReceiverFieldsAndNoOverrideSelectors()
        {
            _window = ScriptableObject.CreateInstance<RecTimelineExportWindow>();

            _window.EnsureBuilt();

            VisualElement root = _window.rootVisualElement;
            List<ObjectField> objectFields = root.Query<ObjectField>().ToList();
            foreach (ObjectField field in objectFields)
            {
                Assert.That(field.objectType, Is.Not.EqualTo(typeof(PlayableDirector)), "Director の指定欄は無い");
                Assert.That(field.objectType, Is.Not.EqualTo(typeof(FacialTimelineReceiver)), "Receiver の指定欄は無い");
            }

            Assert.That(root.Query<EnumField>().ToList(), Is.Empty, "Source Overrides（Auto / Analog / Gaze）の選択欄は無い");
            Assert.That(root.Q(RecTimelineExportWindow.DetectionListName), Is.Not.Null);
        }

        [Test]
        public void RefreshDetections_ListsAnalogAndGazeSourcesButNotTriggerOnlySource()
        {
            FacialCharacterProfileSO profile = CreateProfileAsset();
            string recordingPath = WriteRecording();
            _window = ScriptableObject.CreateInstance<RecTimelineExportWindow>();
            _window.EnsureBuilt();

            _window.SetInputs(recordingPath, profile);
            _window.RefreshDetections();

            IReadOnlyList<string> listed = _window.ListedSourceIds;
            Assert.That(listed, Is.EquivalentTo(new[] { RecFixtureWriter.AnalogSourceId, RecFixtureWriter.GazeSourceId }));
            Assert.That(listed, Does.Not.Contain(RecFixtureWriter.TriggerSourceId), "トリガー専用 source は表示しない");
        }

        [Test]
        public void ExportTo_ShowsRemainingStepsInStatus()
        {
            FacialCharacterProfileSO profile = CreateProfileAsset();
            string recordingPath = WriteRecording();
            _window = ScriptableObject.CreateInstance<RecTimelineExportWindow>();
            _window.EnsureBuilt();
            _window.SetInputs(recordingPath, profile);

            bool success = _window.ExportTo(_folderPath + "/Exported.playable");

            Assert.That(success, Is.True);
            Assert.That(_window.StatusText, Does.Contain("PlayableDirector"));
            Assert.That(_window.StatusText, Does.Contain("FacialTimelineReceiver"));
        }

        private FacialCharacterProfileSO CreateProfileAsset()
        {
            string folderName = "RecTimelineExportWindowTests_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", folderName);
            _folderPath = "Assets/" + folderName;

            var profile = ScriptableObject.CreateInstance<FacialCharacterProfileSO>();
            profile.SchemaVersion = "1.0.0";
            profile.Layers.Add(new LayerDefinitionSerializable
            {
                name = "emotion",
                priority = 0,
                exclusionMode = ExclusionMode.LastWins,
            });
            profile.Expressions.Add(new ExpressionSerializable
            {
                id = RecFixtureWriter.TriggerExpressionId,
                name = "Smile",
                layer = "emotion",
                transitionDuration = 0.1f,
                blendShapeValues = new List<BlendShapeMappingSerializable>
                {
                    new BlendShapeMappingSerializable { name = "Smile", value = 1f },
                },
            });
            AssetDatabase.CreateAsset(profile, _folderPath + "/Profile.asset");
            return profile;
        }

        private string WriteRecording()
        {
            string path = _folderPath + "/recording.fcrec";
            RecFixtureWriter.Write(path, new RecFixtureWriter.Recording());
            AssetDatabase.Refresh();
            return Path.GetFullPath(path);
        }
    }
}
