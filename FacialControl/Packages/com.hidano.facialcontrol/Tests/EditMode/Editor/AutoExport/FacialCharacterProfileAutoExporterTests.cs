using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Editor.AutoExport;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.EditMode.Editor.AutoExport
{
    [TestFixture]
    [MediumTest]
    public sealed class FacialCharacterProfileAutoExporterTests : SizedTestFixture
    {
        private const string TempFolderName = "Temp_AutoExporterSaveTests";
        private const string TempFolderPath = "Assets/" + TempFolderName;
        private const string ProfileAssetName = "AutoExporterSaveTestProfile";

        private readonly List<FacialCharacterProfileSO> _exportedEvents = new List<FacialCharacterProfileSO>();
        private Action<FacialCharacterProfileSO> _throwingHandler;

        [SetUp]
        public void SetUp()
        {
            _exportedEvents.Clear();
            _throwingHandler = null;
            FacialCharacterProfileAutoExporter.Exported += OnExported;
        }

        [TearDown]
        public void TearDown()
        {
            FacialCharacterProfileAutoExporter.Exported -= OnExported;
            if (_throwingHandler != null)
            {
                FacialCharacterProfileAutoExporter.Exported -= _throwingHandler;
                _throwingHandler = null;
            }

            if (AssetDatabase.IsValidFolder(TempFolderPath))
            {
                AssetDatabase.DeleteAsset(TempFolderPath);
            }

            string exportDir = Path.Combine(
                UnityEngine.Application.streamingAssetsPath,
                FacialCharacterProfileSO.StreamingAssetsRootFolder,
                ProfileAssetName);
            if (Directory.Exists(exportDir))
            {
                Directory.Delete(exportDir, recursive: true);
            }

            string metaPath = exportDir + ".meta";
            if (File.Exists(metaPath))
            {
                File.Delete(metaPath);
            }
        }

        private void OnExported(FacialCharacterProfileSO so)
        {
            _exportedEvents.Add(so);
        }

        private static string ProfileJsonPath =>
            FacialCharacterProfileSO.GetStreamingAssetsProfilePath(ProfileAssetName);

        /// <summary>
        /// テスト用 SO を AssetDatabase に保存して返す（CharacterAssetName = ProfileAssetName）。
        /// </summary>
        private static FacialCharacterProfileSO CreateSavedProfileSO()
        {
            var so = ScriptableObject.CreateInstance<FacialCharacterProfileSO>();
            so.Layers.Add(new LayerDefinitionSerializable { name = "emotion", priority = 0 });
            so.Expressions.Add(new ExpressionSerializable
            {
                id = "smile",
                name = "Smile",
                layer = "emotion",
                transitionDuration = 0.25f,
            });

            if (!AssetDatabase.IsValidFolder(TempFolderPath))
            {
                AssetDatabase.CreateFolder("Assets", TempFolderName);
            }
            string assetPath = TempFolderPath + "/" + ProfileAssetName + ".asset";
            AssetDatabase.CreateAsset(so, assetPath);
            AssetDatabase.SaveAssets();
            return so;
        }

        /// <summary>
        /// 2 回目の書き出しの有無を LastWriteTimeUtc で検出できるよう、既存ファイルの時刻を過去へずらす。
        /// </summary>
        private static DateTime BackdateProfileJson()
        {
            var past = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(ProfileJsonPath, past);
            return File.GetLastWriteTimeUtc(ProfileJsonPath);
        }

        [Test]
        public void ExportAll_DirtyProfileSO_SavesUnsavedEditsToAssetFile()
        {
            // 回帰テスト（dirty 未保存の overlay 編集が .asset へ確定されない不具合）。
            // Inspector の自動保存 delayCall は Editor 破棄（別オブジェクト選択等）で失われ、
            // suppress 編集がメモリ上の SO にだけ存在し .asset ディスクに書かれないまま残り得る。
            // Play 突入 / ビルド時の ExportAll は profile.json だけでなく、未保存の .asset も
            // ディスクへ確定する必要がある（メモリとディスクの不整合を持ち越さないため）。
            var so = ScriptableObject.CreateInstance<FacialCharacterProfileSO>();
            so.Layers.Add(new LayerDefinitionSerializable { name = "emotion", priority = 0 });
            so.Expressions.Add(new ExpressionSerializable
            {
                id = "smile",
                name = "Smile",
                layer = "emotion",
                overlays = new List<OverlaySlotBindingSerializable>
                {
                    new OverlaySlotBindingSerializable { slot = "blink" },
                },
            });

            if (!AssetDatabase.IsValidFolder(TempFolderPath))
            {
                AssetDatabase.CreateFolder("Assets", TempFolderName);
            }
            string assetPath = TempFolderPath + "/" + ProfileAssetName + ".asset";
            AssetDatabase.CreateAsset(so, assetPath);
            AssetDatabase.SaveAssets();

            // Inspector の suppress 編集相当を SerializedProperty 経由で加え、保存はしない。
            var serialized = new SerializedObject(so);
            var suppressProp = serialized
                .FindProperty("_expressions")
                .GetArrayElementAtIndex(0)
                .FindPropertyRelative("overlays")
                .GetArrayElementAtIndex(0)
                .FindPropertyRelative("suppress");
            suppressProp.boolValue = true;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(so);
            Assert.That(EditorUtility.IsDirty(so), Is.True,
                "前提: suppress 編集直後の SO は dirty（未保存）である必要があります。");

            FacialCharacterProfileAutoExporter.ExportAll("test");

            Assert.That(EditorUtility.IsDirty(so), Is.False,
                "ExportAll 後も SO が dirty のままです。未保存編集を .asset へ確定する必要があります。");
            StringAssert.Contains(
                "suppress: 1",
                File.ReadAllText(assetPath),
                ".asset ディスク上に suppress 編集が書き出されていません。");
        }

        [Test]
        public void ExportIfEnabled_FirstCall_ReturnsTrueFiresExportedOnceAndCreatesFile()
        {
            var so = CreateSavedProfileSO();
            Assert.That(File.Exists(ProfileJsonPath), Is.False, "前提: profile.json は未生成。");

            bool result = FacialCharacterProfileAutoExporter.ExportIfEnabled(so);

            Assert.That(result, Is.True);
            Assert.That(_exportedEvents.Count, Is.EqualTo(1));
            Assert.That(_exportedEvents[0], Is.SameAs(so));
            Assert.That(File.Exists(ProfileJsonPath), Is.True);
        }

        [Test]
        public void ExportIfEnabled_SameContentSecondCall_ReturnsFalseWithoutEventAndKeepsLastWriteTime()
        {
            var so = CreateSavedProfileSO();
            Assert.That(FacialCharacterProfileAutoExporter.ExportIfEnabled(so), Is.True);
            DateTime before = BackdateProfileJson();
            _exportedEvents.Clear();

            bool result = FacialCharacterProfileAutoExporter.ExportIfEnabled(so);

            Assert.That(result, Is.False);
            Assert.That(_exportedEvents.Count, Is.EqualTo(0));
            Assert.That(File.GetLastWriteTimeUtc(ProfileJsonPath), Is.EqualTo(before));
        }

        [Test]
        public void ExportIfEnabled_ContentChanged_ReturnsTrueAndFiresExported()
        {
            var so = CreateSavedProfileSO();
            Assert.That(FacialCharacterProfileAutoExporter.ExportIfEnabled(so), Is.True);
            DateTime before = BackdateProfileJson();
            _exportedEvents.Clear();

            so.Expressions[0].transitionDuration = 0.75f;
            EditorUtility.SetDirty(so);

            bool result = FacialCharacterProfileAutoExporter.ExportIfEnabled(so);

            Assert.That(result, Is.True);
            Assert.That(_exportedEvents.Count, Is.EqualTo(1));
            Assert.That(File.GetLastWriteTimeUtc(ProfileJsonPath), Is.Not.EqualTo(before));
            StringAssert.Contains("0.75", File.ReadAllText(ProfileJsonPath));
        }

        [Test]
        public void ExportIfEnabled_EmptyCharacterAssetName_ReturnsFalseWithoutFileOrEvent()
        {
            // 未保存の CreateInstance は name が空 = CharacterAssetName 空。
            var so = ScriptableObject.CreateInstance<FacialCharacterProfileSO>();
            try
            {
                Assert.That(so.CharacterAssetName, Is.Empty, "前提: CharacterAssetName が空。");

                bool result = FacialCharacterProfileAutoExporter.ExportIfEnabled(so);

                Assert.That(result, Is.False);
                Assert.That(_exportedEvents.Count, Is.EqualTo(0));
                Assert.That(File.Exists(ProfileJsonPath), Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(so);
            }
        }

        [Test]
        public void ExportIfEnabled_Null_ReturnsFalse()
        {
            Assert.That(FacialCharacterProfileAutoExporter.ExportIfEnabled(null), Is.False);
            Assert.That(_exportedEvents.Count, Is.EqualTo(0));
        }

        [Test]
        public void ExportIfEnabled_SubscriberThrows_LogsExceptionAndStillReturnsTrue()
        {
            var so = CreateSavedProfileSO();
            _throwingHandler = _ => throw new InvalidOperationException("subscriber failure");
            FacialCharacterProfileAutoExporter.Exported += _throwingHandler;
            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException"));

            bool result = FacialCharacterProfileAutoExporter.ExportIfEnabled(so);

            Assert.That(result, Is.True);
            Assert.That(File.Exists(ProfileJsonPath), Is.True);
        }

        [Test]
        public void ExportAll_ReturnValue_EqualsExportIfEnabledTrueCount()
        {
            var so = CreateSavedProfileSO();

            int first = FacialCharacterProfileAutoExporter.ExportAll("test");

            Assert.That(first, Is.EqualTo(_exportedEvents.Count));
            Assert.That(_exportedEvents, Has.Member(so));

            _exportedEvents.Clear();
            int second = FacialCharacterProfileAutoExporter.ExportAll("test");

            Assert.That(second, Is.EqualTo(_exportedEvents.Count));
            Assert.That(_exportedEvents, Has.No.Member(so),
                "同一内容の 2 回目の全件書き出しで対象 SO が再度書き出されています。");
        }

        [Test]
        public void ExportAll_ThenExportIfEnabled_SecondIsNoOp()
        {
            var so = CreateSavedProfileSO();
            FacialCharacterProfileAutoExporter.ExportAll("test");
            Assert.That(File.Exists(ProfileJsonPath), Is.True);
            DateTime before = BackdateProfileJson();
            _exportedEvents.Clear();

            bool result = FacialCharacterProfileAutoExporter.ExportIfEnabled(so);

            Assert.That(result, Is.False);
            Assert.That(_exportedEvents.Count, Is.EqualTo(0));
            Assert.That(File.GetLastWriteTimeUtc(ProfileJsonPath), Is.EqualTo(before));
        }

        [Test]
        public void ExportIfEnabled_ThenExportAll_SecondIsNoOpForSameSO()
        {
            var so = CreateSavedProfileSO();
            Assert.That(FacialCharacterProfileAutoExporter.ExportIfEnabled(so), Is.True);
            DateTime before = BackdateProfileJson();
            _exportedEvents.Clear();

            FacialCharacterProfileAutoExporter.ExportAll("test");

            Assert.That(_exportedEvents, Has.No.Member(so));
            Assert.That(File.GetLastWriteTimeUtc(ProfileJsonPath), Is.EqualTo(before));
        }
    }
}
