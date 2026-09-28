using System.Collections.Generic;
using System.IO;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Editor.AutoExport;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

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

        [TearDown]
        public void TearDown()
        {
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
    }
}
