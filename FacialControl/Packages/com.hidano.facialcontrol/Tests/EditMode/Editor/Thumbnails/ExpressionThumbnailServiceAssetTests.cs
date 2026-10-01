using System;
using System.IO;
using Hidano.FacialControl.Editor.Sampling;
using Hidano.FacialControl.Editor.Thumbnails;
using Hidano.FacialControl.Testing;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Hidano.FacialControl.Tests.EditMode.Editor.Thumbnails
{
    /// <summary>
    /// アセット化した参照モデル（prefab）と AnimationClip を使う <see cref="ExpressionThumbnailService"/> のテスト。
    /// <list type="bullet">
    /// <item>ディスクキャッシュ: 一度描画したサムネイルは、次に Inspector を開いたとき（新しいサービス）に描画せず読み込む</item>
    /// <item>インポート通知: clip を中身を変えずに保存しただけでは描画し直さない / 参照モデルが変わったら描画し直す</item>
    /// </list>
    /// </summary>
    [TestFixture]
    [MediumTest]
    public class ExpressionThumbnailServiceAssetTests : SizedTestFixture
    {
        private const string TempFolderParent = "Assets";
        private const string TempFolderName = "__Temp_ExpressionThumbnailServiceAssetTests";
        private static readonly string TempFolderPath = TempFolderParent + "/" + TempFolderName;

        private string _cacheDirectory;
        private string _modelPath;
        private string _clipPath;
        private GameObject _model;
        private AnimationClip _clip;
        private FakeExpressionThumbnailRenderer _renderer;
        private ExpressionThumbnailService _service;

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(TempFolderPath))
            {
                AssetDatabase.CreateFolder(TempFolderParent, TempFolderName);
            }

            var suffix = Guid.NewGuid().ToString("N");
            _modelPath = TempFolderPath + "/ThumbnailModel_" + suffix + ".prefab";
            _clipPath = TempFolderPath + "/ThumbnailClip_" + suffix + ".anim";

            var source = new GameObject("ThumbnailModel");
            try
            {
                _model = PrefabUtility.SaveAsPrefabAsset(source, _modelPath);
            }
            finally
            {
                Object.DestroyImmediate(source);
            }

            var clip = new AnimationClip();
            AnimationUtility.SetEditorCurve(
                clip,
                EditorCurveBinding.FloatCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.smile"),
                AnimationCurve.Constant(0f, 0f, 50f));
            AssetDatabase.CreateAsset(clip, _clipPath);
            _clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(_clipPath);

            _cacheDirectory = Path.Combine(Path.GetTempPath(), "FacialControlThumbnailServiceAssetTests_" + suffix);
            _renderer = new FakeExpressionThumbnailRenderer();
            _service = CreateService(_renderer);
        }

        [TearDown]
        public void TearDown()
        {
            _service?.Dispose();
            _service = null;

            if (!string.IsNullOrEmpty(_modelPath)) AssetDatabase.DeleteAsset(_modelPath);
            if (!string.IsNullOrEmpty(_clipPath)) AssetDatabase.DeleteAsset(_clipPath);
            if (AssetDatabase.IsValidFolder(TempFolderPath))
            {
                var remaining = AssetDatabase.FindAssets(string.Empty, new[] { TempFolderPath });
                if (remaining == null || remaining.Length == 0)
                {
                    AssetDatabase.DeleteAsset(TempFolderPath);
                }
            }

            if (!string.IsNullOrEmpty(_cacheDirectory) && Directory.Exists(_cacheDirectory))
            {
                Directory.Delete(_cacheDirectory, true);
            }
        }

        [Test]
        public void Pump_PersistentAssets_NextServiceLoadsFromDiskWithoutRendering()
        {
            var view = new ExpressionThumbnailView();
            _service.Bind(view, _model, _clip);
            _service.Pump();
            Assert.That(_renderer.RenderCount, Is.EqualTo(1));
            Assert.That(File.Exists(Path.Combine(_cacheDirectory, view.CacheKey + ".png")), Is.True,
                "描画結果がディスクキャッシュに保存されていません。");

            // Inspector を閉じて開き直した状況（別のサービス）。
            _service.Dispose();
            var secondRenderer = new FakeExpressionThumbnailRenderer();
            _service = CreateService(secondRenderer);
            var reopened = new ExpressionThumbnailView();
            _service.Bind(reopened, _model, _clip);
            Assert.That(reopened.StatusText, Is.EqualTo(ExpressionThumbnailView.PendingMessage));

            _service.Pump();

            Assert.That(secondRenderer.RenderCount, Is.Zero, "ディスクキャッシュがあるのに描画し直しました。");
            Assert.That(reopened.Texture, Is.Not.Null);
            Assert.That(reopened.CacheKey, Is.EqualTo(view.CacheKey));
        }

        [Test]
        public void LoadFullSize_DiskCached_ReturnsCaptureResolutionOwnedTexture()
        {
            var view = new ExpressionThumbnailView();
            _service.Bind(view, _model, _clip);
            _service.Pump();

            var texture = _service.LoadFullSize(view.CacheKey, out bool owned);
            try
            {
                Assert.That(owned, Is.True);
                Assert.That(texture.width, Is.EqualTo(ExpressionThumbnailService.CaptureResolution));
            }
            finally
            {
                if (owned) Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void NotifyAssetsImported_ClipSavedWithoutContentChange_KeepsThumbnailWithoutRendering()
        {
            var view = new ExpressionThumbnailView();
            _service.Bind(view, _model, _clip);
            _service.Pump();
            var key = view.CacheKey;

            EditorUtility.SetDirty(_clip);
            AssetDatabase.SaveAssets();
            _service.NotifyAssetsImported(new[] { _clipPath });
            _service.Pump();

            Assert.That(view.CacheKey, Is.EqualTo(key));
            Assert.That(view.Texture, Is.Not.Null);
            Assert.That(_renderer.RenderCount, Is.EqualTo(1));
        }

        [Test]
        public void NotifyAssetsImported_ReferenceModelChanged_RendersAgain()
        {
            var view = new ExpressionThumbnailView();
            _service.Bind(view, _model, _clip);
            _service.Pump();
            var key = view.CacheKey;

            // prefab を編集して保存（再インポートで依存ハッシュが変わる）。
            var contents = PrefabUtility.LoadPrefabContents(_modelPath);
            try
            {
                new GameObject("AddedChild").transform.SetParent(contents.transform);
                PrefabUtility.SaveAsPrefabAsset(contents, _modelPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
            _service.NotifyAssetsImported(new[] { _modelPath });

            Assert.That(view.StatusText, Is.EqualTo(ExpressionThumbnailView.PendingMessage));
            _service.Pump();

            Assert.That(_renderer.RenderCount, Is.EqualTo(2));
            Assert.That(view.CacheKey, Is.Not.EqualTo(key));
        }

        [Test]
        public void NotifyAssetsImported_UnrelatedAsset_DoesNothing()
        {
            var view = new ExpressionThumbnailView();
            _service.Bind(view, _model, _clip);
            _service.Pump();

            _service.NotifyAssetsImported(new[] { "Assets/Unrelated.mat" });

            Assert.That(view.Texture, Is.Not.Null);
            Assert.That(_service.PendingCount, Is.Zero);
        }

        private ExpressionThumbnailService CreateService(IExpressionThumbnailRenderer renderer)
        {
            return new ExpressionThumbnailService(
                renderer: renderer,
                diskCache: new ExpressionThumbnailDiskCache(_cacheDirectory),
                sampler: new AnimationClipExpressionSampler(),
                autoPump: false);
        }
    }
}
