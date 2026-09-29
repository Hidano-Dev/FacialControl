using System;
using System.Linq;
using System.Text.RegularExpressions;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Editor.Inspector.AdapterBindings;
using Hidano.FacialControl.Tests.EditMode.Adapters.ScriptableObjectTests.AdapterBindings;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.EditMode.Editor.Inspector.AdapterBindings
{
    [Serializable]
    [FacialAdapterBinding(displayName: "ZZZ_AssetGuardTest_AAA")]
    public sealed class MockAssetGuardAlphaBinding : AdapterBindingBase { }

    [Serializable]
    [FacialAdapterBinding(displayName: "ZZZ_AssetGuardTest_BBB")]
    public sealed class MockAssetGuardBetaBinding : AdapterBindingBase { }

    /// <summary>
    /// task 5.5 の観測可能完了条件: <see cref="FacialCharacterProfileAssetGuard"/> が
    /// 重複 slug を持つ <see cref="Adapters.ScriptableObject.Serializable.FacialCharacterProfileSO"/>
    /// の save をブロックし、修復後は save が通ることを assert する。
    /// </summary>
    [TestFixture]
    [MediumTest]
    public class FacialCharacterProfileAssetGuardTests : SizedTestFixture
    {
        private const string TempFolderParent = "Assets";
        private const string TempFolderName = "__Temp_FacialCharacterProfileAssetGuardTests";
        private static readonly string TempFolderPath = TempFolderParent + "/" + TempFolderName;

        private string _assetPath;
        private TestFacialCharacterProfileSO _so;

        [SetUp]
        public void SetUp()
        {
            FacialCharacterProfileAssetGuard.SuppressDialogForTesting = true;

            if (!AssetDatabase.IsValidFolder(TempFolderPath))
            {
                AssetDatabase.CreateFolder(TempFolderParent, TempFolderName);
            }

            _assetPath = TempFolderPath + "/FacialCharacterProfileAssetGuardTests_"
                + Guid.NewGuid().ToString("N") + ".asset";
            _so = ScriptableObject.CreateInstance<TestFacialCharacterProfileSO>();
            AssetDatabase.CreateAsset(_so, _assetPath);
            AssetDatabase.SaveAssets();
        }

        [TearDown]
        public void TearDown()
        {
            FacialCharacterProfileAssetGuard.SuppressDialogForTesting = false;
            _so = null;

            if (!string.IsNullOrEmpty(_assetPath))
            {
                AssetDatabase.DeleteAsset(_assetPath);
                _assetPath = null;
            }
            if (AssetDatabase.IsValidFolder(TempFolderPath))
            {
                var remaining = AssetDatabase.FindAssets(string.Empty, new[] { TempFolderPath });
                if (remaining == null || remaining.Length == 0)
                {
                    AssetDatabase.DeleteAsset(TempFolderPath);
                }
            }
        }

        // ---------------------------------------------------------------
        // 重複 slug → save ブロック（path が return 配列から除外される）+ LogError
        // ---------------------------------------------------------------

        [Test]
        public void OnWillSaveAssets_DuplicateSlug_ExcludesPathAndLogsError()
        {
            const string duplicateSlug = "duplicate-slug";

            _so.WritableAdapterBindings.Add(new MockAssetGuardAlphaBinding { Slug = duplicateSlug });
            _so.WritableAdapterBindings.Add(new MockAssetGuardBetaBinding { Slug = duplicateSlug });
            EditorUtility.SetDirty(_so);

            LogAssert.Expect(
                LogType.Error,
                new Regex(Regex.Escape($"[FacialControl] Save blocked: duplicate slug '{duplicateSlug}' in")
                    + ".*" + Regex.Escape(_assetPath)));

            var input = new[] { _assetPath };
            var result = FacialCharacterProfileAssetGuard.OnWillSaveAssets(input);

            Assert.IsNotNull(result, "OnWillSaveAssets は non-null を返すべき。");
            Assert.IsFalse(result.Contains(_assetPath),
                "重複 slug を持つ SO の path は return 配列から除外されるべき 。");
        }

        [Test]
        public void OnWillSaveAssets_DuplicateSlug_FocusesBlockedAssetInSelection()
        {
            const string duplicateSlug = "focus-target";

            _so.WritableAdapterBindings.Add(new MockAssetGuardAlphaBinding { Slug = duplicateSlug });
            _so.WritableAdapterBindings.Add(new MockAssetGuardBetaBinding { Slug = duplicateSlug });
            EditorUtility.SetDirty(_so);

            LogAssert.Expect(LogType.Error, new Regex("Save blocked: duplicate slug"));

            // 既存 selection を意図的に他に向けておく。
            Selection.activeObject = null;

            FacialCharacterProfileAssetGuard.OnWillSaveAssets(new[] { _assetPath });

            Assert.AreSame(_so, Selection.activeObject,
                "重複 slug 検出時は当該 SO に Selection.activeObject が向けられるべき。");
        }

        // ---------------------------------------------------------------
        // 重複なし → save が通る（path が return 配列に保持される）
        // ---------------------------------------------------------------

        [Test]
        public void OnWillSaveAssets_UniqueSlugs_KeepsPathInReturnArray()
        {
            _so.WritableAdapterBindings.Add(new MockAssetGuardAlphaBinding { Slug = "alpha" });
            _so.WritableAdapterBindings.Add(new MockAssetGuardBetaBinding { Slug = "beta" });
            EditorUtility.SetDirty(_so);

            var input = new[] { _assetPath };
            var result = FacialCharacterProfileAssetGuard.OnWillSaveAssets(input);

            Assert.IsTrue(result.Contains(_assetPath),
                "slug 重複が無い場合、path は return 配列に保持されるべき。");
        }

        // ---------------------------------------------------------------
        // 修復後の save 通過（Red → 修復 → Green）
        // ---------------------------------------------------------------

        [Test]
        public void OnWillSaveAssets_AfterResolvingDuplicate_PathIsKeptInReturnArray()
        {
            const string duplicateSlug = "to-be-fixed";
            var first = new MockAssetGuardAlphaBinding { Slug = duplicateSlug };
            var second = new MockAssetGuardBetaBinding { Slug = duplicateSlug };
            _so.WritableAdapterBindings.Add(first);
            _so.WritableAdapterBindings.Add(second);
            EditorUtility.SetDirty(_so);

            LogAssert.Expect(LogType.Error, new Regex("Save blocked: duplicate slug"));

            var blocked = FacialCharacterProfileAssetGuard.OnWillSaveAssets(new[] { _assetPath });
            Assert.IsFalse(blocked.Contains(_assetPath),
                "修復前は path が除外されるべき (前提条件)。");

            // 重複解消。
            second.Slug = "now-unique";
            EditorUtility.SetDirty(_so);

            var allowed = FacialCharacterProfileAssetGuard.OnWillSaveAssets(new[] { _assetPath });
            Assert.IsTrue(allowed.Contains(_assetPath),
                "重複を解消した後は path が return 配列に保持されるべき (観測可能完了条件)。");
        }

        // ---------------------------------------------------------------
        // 非 FacialCharacterProfileSO は即 skip（早期 return パスのカバレッジ）
        // ---------------------------------------------------------------

        [Test]
        public void OnWillSaveAssets_NonFacialCharacterProfilePath_IsPassedThroughUnchanged()
        {
            // テスト一時 folder に textasset を作って path を渡す。
            var textPath = TempFolderPath + "/non-profile-" + Guid.NewGuid().ToString("N") + ".asset";
            var dummy = ScriptableObject.CreateInstance<UnrelatedScriptableObject>();
            try
            {
                AssetDatabase.CreateAsset(dummy, textPath);
                AssetDatabase.SaveAssets();

                var input = new[] { textPath };
                var result = FacialCharacterProfileAssetGuard.OnWillSaveAssets(input);

                Assert.IsTrue(result.Contains(textPath),
                    "FacialCharacterProfileSO 以外の path は無条件で return 配列に保持されるべき。");
            }
            finally
            {
                AssetDatabase.DeleteAsset(textPath);
            }
        }

        [Test]
        public void OnWillSaveAssets_NullOrEmptyInput_ReturnsInputUnchanged()
        {
            Assert.IsNull(FacialCharacterProfileAssetGuard.OnWillSaveAssets(null));

            var empty = Array.Empty<string>();
            var result = FacialCharacterProfileAssetGuard.OnWillSaveAssets(empty);
            Assert.AreSame(empty, result,
                "空配列入力時は同じ参照を返してアロケーションを避けるべき。");
        }

        public sealed class UnrelatedScriptableObject : ScriptableObject { }
    }
}
