using System.Collections.Generic;
using Hidano.FacialControl.Editor.Sampling;
using Hidano.FacialControl.Editor.Thumbnails;
using Hidano.FacialControl.Testing;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Hidano.FacialControl.Tests.EditMode.Editor.Thumbnails
{
    /// <summary>
    /// <see cref="ExpressionThumbnailService"/> の解決順序と後始末。
    /// GPU 描画は外部境界として Fake に差し替え、次を守る:
    /// <list type="bullet">
    /// <item>参照モデル・clip が無いときはプレースホルダを出し、描画しない</item>
    /// <item>生成は遅延（Bind 時点では描画せず、Pump 1 回につき描画 1 件）</item>
    /// <item>同じ内容はメモリキャッシュから再利用し、描画し直さない（Inspector 再構築時）</item>
    /// <item>clip の中身が変わったら作り直す / 再生成ボタンで作り直す</item>
    /// <item>Dispose でテクスチャと描画器を破棄する</item>
    /// </list>
    /// アセット化していない（メモリ上の）モデル・clip を使うため、ディスクキャッシュは使われない。
    /// </summary>
    [TestFixture]
    [MediumTest]
    public class ExpressionThumbnailServiceTests : SizedTestFixture
    {
        private readonly List<Object> _tracked = new List<Object>();
        private FakeExpressionThumbnailRenderer _renderer;
        private ExpressionThumbnailService _service;

        [SetUp]
        public void SetUp()
        {
            _renderer = new FakeExpressionThumbnailRenderer();
            _service = new ExpressionThumbnailService(
                renderer: _renderer,
                diskCache: null,
                sampler: new AnimationClipExpressionSampler(),
                autoPump: false);
        }

        [TearDown]
        public void TearDown()
        {
            _service?.Dispose();
            _service = null;

            for (int i = 0; i < _tracked.Count; i++)
            {
                if (_tracked[i] != null)
                    Object.DestroyImmediate(_tracked[i]);
            }
            _tracked.Clear();
        }

        [Test]
        public void Bind_NoReferenceModel_ShowsPlaceholderAndNeverRenders()
        {
            var view = new ExpressionThumbnailView();

            _service.Bind(view, null, CreateClip("smile", 0.5f));
            _service.Pump();

            Assert.That(view.StatusText, Is.EqualTo(ExpressionThumbnailView.NoReferenceModelMessage));
            Assert.That(_renderer.RenderCount, Is.Zero);
            Assert.That(_service.PendingCount, Is.Zero);
        }

        [Test]
        public void Bind_NoClip_ShowsPlaceholderAndNeverRenders()
        {
            var view = new ExpressionThumbnailView();

            _service.Bind(view, CreateModel(), null);
            _service.Pump();

            Assert.That(view.StatusText, Is.EqualTo(ExpressionThumbnailView.NoClipMessage));
            Assert.That(_renderer.RenderCount, Is.Zero);
        }

        [Test]
        public void Bind_ModelAndClip_ShowsPendingUntilPump()
        {
            var view = new ExpressionThumbnailView();

            _service.Bind(view, CreateModel(), CreateClip("smile", 0.5f));

            Assert.That(view.StatusText, Is.EqualTo(ExpressionThumbnailView.PendingMessage));
            Assert.That(_renderer.RenderCount, Is.Zero, "Bind の時点で同期描画してはいけません。");

            _service.Pump();

            Assert.That(_renderer.RenderCount, Is.EqualTo(1));
            Assert.That(view.Texture, Is.Not.Null);
            Assert.That(view.StatusText, Is.Null);
            Assert.That(view.Texture.width, Is.LessThanOrEqualTo(ExpressionThumbnailService.DisplayResolution));
            Assert.That(_renderer.LastResolution, Is.EqualTo(ExpressionThumbnailService.CaptureResolution));
        }

        [Test]
        public void Pump_ManyPending_RendersOnePerCall()
        {
            var model = CreateModel();
            var views = new[] { new ExpressionThumbnailView(), new ExpressionThumbnailView(), new ExpressionThumbnailView() };
            _service.Bind(views[0], model, CreateClip("a", 0.1f));
            _service.Bind(views[1], model, CreateClip("b", 0.2f));
            _service.Bind(views[2], model, CreateClip("c", 0.3f));

            _service.Pump();
            Assert.That(_renderer.RenderCount, Is.EqualTo(1));
            Assert.That(_service.PendingCount, Is.EqualTo(2));

            _service.Pump();
            _service.Pump();
            Assert.That(_renderer.RenderCount, Is.EqualTo(3));
            Assert.That(_service.PendingCount, Is.Zero);
        }

        [Test]
        public void Bind_TwoViewsSameClip_RendersOnceAndShowsBoth()
        {
            var model = CreateModel();
            var clip = CreateClip("smile", 0.5f);
            var first = new ExpressionThumbnailView();
            var second = new ExpressionThumbnailView();

            _service.Bind(first, model, clip);
            _service.Bind(second, model, clip);
            _service.Pump();
            _service.Pump();

            Assert.That(_renderer.RenderCount, Is.EqualTo(1));
            Assert.That(first.Texture, Is.Not.Null);
            Assert.That(second.Texture, Is.SameAs(first.Texture));
        }

        [Test]
        public void Bind_AfterClearBindings_ReusesTextureWithoutRendering()
        {
            // Inspector の Expression 一覧再構築（ClearBindings → 新しい view で Bind）を模す。
            var model = CreateModel();
            var clip = CreateClip("smile", 0.5f);
            var view = new ExpressionThumbnailView();
            _service.Bind(view, model, clip);
            _service.Pump();

            _service.ClearBindings();
            var rebuilt = new ExpressionThumbnailView();
            _service.Bind(rebuilt, model, clip);

            Assert.That(rebuilt.Texture, Is.SameAs(view.Texture), "再構築直後に即表示される必要があります。");
            Assert.That(_renderer.RenderCount, Is.EqualTo(1));
        }

        [Test]
        public void CheckForClipChanges_ClipEdited_RegeneratesThumbnail()
        {
            var model = CreateModel();
            var clip = CreateClip("smile", 0.5f);
            var view = new ExpressionThumbnailView();
            _service.Bind(view, model, clip);
            _service.Pump();
            var before = view.CacheKey;

            SetBlendShapeCurve(clip, "smile", 0.9f);
            EditorUtility.SetDirty(clip);

            // 変化を見た直後は作り直さない（編集中の連続変更で毎回描画しない）。
            _service.CheckForClipChanges();
            Assert.That(view.CacheKey, Is.EqualTo(before));

            // 次の確認でも変化が落ち着いていれば作り直す。
            _service.CheckForClipChanges();
            Assert.That(view.StatusText, Is.EqualTo(ExpressionThumbnailView.PendingMessage));
            _service.Pump();
            Assert.That(_renderer.RenderCount, Is.EqualTo(2));
            Assert.That(view.CacheKey, Is.Not.EqualTo(before));
        }

        [Test]
        public void CheckForClipChanges_StillEditing_WaitsUntilChangesSettle()
        {
            var clip = CreateClip("smile", 0.5f);
            var view = new ExpressionThumbnailView();
            _service.Bind(view, CreateModel(), clip);
            _service.Pump();

            // 確認のたびに値が変わり続けている間（スライダー操作中など）は作り直さない。
            for (int i = 0; i < 3; i++)
            {
                SetBlendShapeCurve(clip, "smile", 0.6f + i * 0.1f);
                EditorUtility.SetDirty(clip);
                _service.CheckForClipChanges();
                _service.Pump();
            }

            Assert.That(_renderer.RenderCount, Is.EqualTo(1));
            Assert.That(view.Texture, Is.Not.Null);
        }

        [Test]
        public void CheckForClipChanges_Unchanged_DoesNotRender()
        {
            var view = new ExpressionThumbnailView();
            _service.Bind(view, CreateModel(), CreateClip("smile", 0.5f));
            _service.Pump();

            _service.CheckForClipChanges();
            _service.Pump();

            Assert.That(_renderer.RenderCount, Is.EqualTo(1));
            Assert.That(view.Texture, Is.Not.Null);
        }

        [Test]
        public void CheckForClipChanges_ClipDestroyed_ShowsNoClipPlaceholder()
        {
            var clip = CreateClip("smile", 0.5f);
            var view = new ExpressionThumbnailView();
            _service.Bind(view, CreateModel(), clip);
            _service.Pump();

            Object.DestroyImmediate(clip);
            _service.CheckForClipChanges();

            Assert.That(view.StatusText, Is.EqualTo(ExpressionThumbnailView.NoClipMessage));
        }

        [Test]
        public void RebindReferenceModel_Null_ShowsReferenceModelPlaceholder()
        {
            var view = new ExpressionThumbnailView();
            _service.Bind(view, CreateModel(), CreateClip("smile", 0.5f));
            _service.Pump();

            _service.RebindReferenceModel(null);

            Assert.That(view.StatusText, Is.EqualTo(ExpressionThumbnailView.NoReferenceModelMessage));
        }

        [Test]
        public void RebindReferenceModel_OtherModel_RendersAgain()
        {
            var view = new ExpressionThumbnailView();
            _service.Bind(view, CreateModel(), CreateClip("smile", 0.5f));
            _service.Pump();
            var before = view.CacheKey;

            _service.RebindReferenceModel(CreateModel());
            _service.Pump();

            Assert.That(_renderer.RenderCount, Is.EqualTo(2));
            Assert.That(view.CacheKey, Is.Not.EqualTo(before));
        }

        [Test]
        public void RegenerateAll_Cached_RendersAgainAndReleasesOldTexture()
        {
            var view = new ExpressionThumbnailView();
            _service.Bind(view, CreateModel(), CreateClip("smile", 0.5f));
            _service.Pump();
            var oldTexture = view.Texture;

            _service.RegenerateAll();
            Assert.That(view.StatusText, Is.EqualTo(ExpressionThumbnailView.PendingMessage));
            _service.Pump();

            Assert.That(_renderer.RenderCount, Is.EqualTo(2));
            Assert.That(oldTexture == null, Is.True, "古いテクスチャが破棄されていません。");
            Assert.That(view.Texture, Is.Not.Null);
        }

        [Test]
        public void Pump_RendererReturnsNull_ShowsFailedPlaceholder()
        {
            _renderer.ReturnNull = true;
            var view = new ExpressionThumbnailView();
            _service.Bind(view, CreateModel(), CreateClip("smile", 0.5f));

            _service.Pump();

            Assert.That(view.StatusText, Is.EqualTo(ExpressionThumbnailView.FailedMessage));
            Assert.That(_service.PendingCount, Is.Zero);
        }

        [Test]
        public void Dispose_AfterRender_DestroysTexturesAndRenderer()
        {
            var view = new ExpressionThumbnailView();
            _service.Bind(view, CreateModel(), CreateClip("smile", 0.5f));
            _service.Pump();
            var texture = view.Texture;
            Assert.That(_service.CachedTextureCount, Is.EqualTo(1));

            _service.Dispose();

            Assert.That(texture == null, Is.True, "表示用テクスチャが破棄されていません。");
            Assert.That(_renderer.Disposed, Is.True);
            Assert.That(_service.CachedTextureCount, Is.Zero);
            Assert.That(_renderer.LiveTextureCount, Is.Zero, "描画結果（キャプチャ解像度）のテクスチャが残っています。");
        }

        [Test]
        public void Dispose_Twice_DoesNotThrow()
        {
            _service.Dispose();

            Assert.DoesNotThrow(() => _service.Dispose());
            Assert.DoesNotThrow(() => _service.Bind(new ExpressionThumbnailView(), CreateModel(), CreateClip("smile", 0.5f)));
        }

        [Test]
        public void Downscale_CaptureResolution_ReturnsDisplayResolution()
        {
            var source = new Texture2D(ExpressionThumbnailService.CaptureResolution, ExpressionThumbnailService.CaptureResolution, TextureFormat.RGBA32, false);
            _tracked.Add(source);

            var result = ExpressionThumbnailService.Downscale(source, ExpressionThumbnailService.DisplayResolution);
            _tracked.Add(result);

            Assert.That(result.width, Is.EqualTo(ExpressionThumbnailService.DisplayResolution));
            Assert.That(result.height, Is.EqualTo(ExpressionThumbnailService.DisplayResolution));
        }

        // ====================================================================
        // ヘルパー
        // ====================================================================

        private GameObject CreateModel()
        {
            var model = new GameObject("ThumbnailModel");
            _tracked.Add(model);
            return model;
        }

        private AnimationClip CreateClip(string blendShapeName, float value)
        {
            var clip = new AnimationClip { name = "Thumbnail_" + blendShapeName };
            SetBlendShapeCurve(clip, blendShapeName, value);
            _tracked.Add(clip);
            return clip;
        }

        private static void SetBlendShapeCurve(AnimationClip clip, string blendShapeName, float normalizedValue)
        {
            var binding = EditorCurveBinding.FloatCurve("Body", typeof(SkinnedMeshRenderer), "blendShape." + blendShapeName);
            AnimationUtility.SetEditorCurve(clip, binding, AnimationCurve.Constant(0f, 0f, normalizedValue * 100f));
        }
    }
}
