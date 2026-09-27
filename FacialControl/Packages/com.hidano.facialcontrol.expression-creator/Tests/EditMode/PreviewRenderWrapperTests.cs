using NUnit.Framework;
using SceneViewStyleCameraController;
using UnityEngine;
using UnityEngine.Rendering;
using Hidano.FacialControl.ExpressionCreator;

namespace Hidano.FacialControl.ExpressionCreator.Tests.EditMode
{
    [TestFixture]
    public class PreviewRenderWrapperTests
    {
        private PreviewRenderWrapper _wrapper;
        private Rect _rect;

        [SetUp]
        public void SetUp()
        {
            _wrapper = new PreviewRenderWrapper();
            _rect = new Rect(0, 0, 512, 512);
        }

        [TearDown]
        public void TearDown()
        {
            _wrapper.Dispose();
        }

        [Test]
        public void HandleInput_AltLeftDrag_OrbitApplied()
        {
            var frame = new PreviewInputFrame(
                EventType.MouseDrag,
                button: 0,
                mousePosition: new Vector2(256, 256),
                delta: new Vector2(10f, 5f),
                scrollDelta: Vector2.zero,
                alt: true);

            var result = _wrapper.HandleInput(_rect, frame);

            Assert.IsTrue(result);
        }

        [Test]
        public void HandleInput_MiddleDrag_PanApplied()
        {
            var frame = new PreviewInputFrame(
                EventType.MouseDrag,
                button: 2,
                mousePosition: new Vector2(256, 256),
                delta: new Vector2(10f, 5f),
                scrollDelta: Vector2.zero,
                alt: false);

            var result = _wrapper.HandleInput(_rect, frame);

            Assert.IsTrue(result);
        }

        [Test]
        public void HandleInput_ScrollWheel_DollyApplied()
        {
            var frame = new PreviewInputFrame(
                EventType.ScrollWheel,
                button: 0,
                mousePosition: new Vector2(256, 256),
                delta: Vector2.zero,
                scrollDelta: new Vector2(0f, 3f),
                alt: false);

            var result = _wrapper.HandleInput(_rect, frame);

            Assert.IsTrue(result);
        }

        [Test]
        public void HandleInput_AltRightDrag_DollyApplied()
        {
            var frame = new PreviewInputFrame(
                EventType.MouseDrag,
                button: 1,
                mousePosition: new Vector2(256, 256),
                delta: new Vector2(0f, 10f),
                scrollDelta: Vector2.zero,
                alt: true);

            var result = _wrapper.HandleInput(_rect, frame);

            Assert.IsTrue(result);
        }

        [Test]
        public void HandleInput_OutsideRect_ReturnsFalse()
        {
            var frame = new PreviewInputFrame(
                EventType.MouseDrag,
                button: 0,
                mousePosition: new Vector2(600, 600),
                delta: new Vector2(10f, 5f),
                scrollDelta: Vector2.zero,
                alt: true);

            var result = _wrapper.HandleInput(_rect, frame);

            Assert.IsFalse(result);
        }

        [Test]
        public void HandleInput_InsideRect_Changed_ReturnsTrue()
        {
            var frame = new PreviewInputFrame(
                EventType.MouseDrag,
                button: 2,
                mousePosition: new Vector2(100, 100),
                delta: new Vector2(5f, 5f),
                scrollDelta: Vector2.zero,
                alt: false);

            var result = _wrapper.HandleInput(_rect, frame);

            Assert.IsTrue(result);
        }

        [Test]
        public void ResetCamera_RestoresInitialState()
        {
            var orbitFrame = new PreviewInputFrame(
                EventType.MouseDrag,
                button: 0,
                mousePosition: new Vector2(256, 256),
                delta: new Vector2(20f, 10f),
                scrollDelta: Vector2.zero,
                alt: true);
            var scrollFrame = new PreviewInputFrame(
                EventType.ScrollWheel,
                button: 0,
                mousePosition: new Vector2(256, 256),
                delta: Vector2.zero,
                scrollDelta: new Vector2(0f, 5f),
                alt: false);

            _wrapper.HandleInput(_rect, orbitFrame);
            _wrapper.HandleInput(_rect, scrollFrame);

            _wrapper.ResetCamera();

            var verifyFrame = new PreviewInputFrame(
                EventType.MouseDrag,
                button: 0,
                mousePosition: new Vector2(256, 256),
                delta: new Vector2(20f, 10f),
                scrollDelta: Vector2.zero,
                alt: true);

            var freshWrapper = new PreviewRenderWrapper();
            var resultAfterReset = _wrapper.HandleInput(_rect, verifyFrame);
            var resultFresh = freshWrapper.HandleInput(_rect, verifyFrame);
            freshWrapper.Dispose();

            Assert.AreEqual(resultFresh, resultAfterReset);
        }

        [Test]
        public void CapturePreviewTexture_SetupObject_ReturnsRequestedReadableTextureWithRenderedPixels()
        {
            IgnoreWhenGraphicsDeviceUnavailable();

            var source = CreatePreviewSource();
            Texture2D texture = null;

            try
            {
                _wrapper.Setup(source);

                texture = _wrapper.CapturePreviewTexture(64, 32);

                Assert.IsNotNull(texture);
                Assert.AreEqual(64, texture.width);
                Assert.AreEqual(32, texture.height);
                Assert.IsTrue(ContainsNonBackgroundPixel(texture));
            }
            finally
            {
                if (texture != null)
                    Object.DestroyImmediate(texture);
                DestroyPreviewSourceMaterial(source);
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void CapturePreviewTexture_WhenRenderTextureActiveExists_RestoresPreviousActiveTexture()
        {
            IgnoreWhenGraphicsDeviceUnavailable();

            var source = CreatePreviewSource();
            var previousActive = RenderTexture.active;
            var activeTexture = new RenderTexture(8, 8, 24, RenderTextureFormat.ARGB32);
            Texture2D texture = null;

            try
            {
                activeTexture.Create();
                RenderTexture.active = activeTexture;
                _wrapper.Setup(source);

                texture = _wrapper.CapturePreviewTexture(32, 32);

                Assert.IsNotNull(texture);
                Assert.AreSame(activeTexture, RenderTexture.active);
            }
            finally
            {
                if (texture != null)
                    Object.DestroyImmediate(texture);
                RenderTexture.active = previousActive;
                activeTexture.Release();
                Object.DestroyImmediate(activeTexture);
                DestroyPreviewSourceMaterial(source);
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void CapturePreviewTexture_WithoutSetup_ReturnsNull()
        {
            var texture = _wrapper.CapturePreviewTexture(16, 16);

            Assert.IsNull(texture);
        }

        [Test]
        public void CapturePreviewTexture_BlendShapeChangedInSameCallback_ReflectsChangeInCapture()
        {
            IgnoreWhenGraphicsDeviceUnavailable();

            // 全 Expression PNG 書き出しの再現: 同一エディタコールバック内で
            // SetBlendShapeWeight → キャプチャを繰り返しても各キャプチャに反映されること。
            var source = CreateBlendShapeQuadSource(out var mesh, out var material);
            Texture2D texture0 = null;
            Texture2D texture100 = null;

            try
            {
                _wrapper.Setup(source);
                var smr = _wrapper.PreviewInstance.GetComponentInChildren<SkinnedMeshRenderer>(true);

                smr.SetBlendShapeWeight(0, 0f);
                texture0 = _wrapper.CapturePreviewTexture(64, 64);

                // BlendShape でクアッドを視界外へ移動させる
                smr.SetBlendShapeWeight(0, 100f);
                texture100 = _wrapper.CapturePreviewTexture(64, 64);

                Assert.IsNotNull(texture0);
                Assert.IsNotNull(texture100);
                Assert.IsTrue(ContainsNonBackgroundPixel(texture0),
                    "weight 0 のキャプチャにクアッドが描画されていること");
                Assert.IsTrue(PixelsDiffer(texture0, texture100),
                    "同一コールバック内の BlendShape 変更がキャプチャに反映されること");
            }
            finally
            {
                if (texture0 != null)
                    Object.DestroyImmediate(texture0);
                if (texture100 != null)
                    Object.DestroyImmediate(texture100);
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(mesh);
                Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void Setup_WithoutTrackTargetPath_UsesDefaultFov()
        {
            var source = CreatePreviewSource();

            try
            {
                _wrapper.Setup(source);

                Assert.AreEqual(PreviewRenderWrapper.DefaultFov, _wrapper.CameraFieldOfView, 1e-5f);
            }
            finally
            {
                DestroyPreviewSourceMaterial(source);
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void Setup_WithTrackTargetPath_UsesFaceTrackFov()
        {
            var source = CreatePreviewSource();
            var head = new GameObject("head");
            head.transform.SetParent(source.transform);
            head.transform.localPosition = new Vector3(0f, 0.5f, 0f);

            try
            {
                _wrapper.Setup(source, "head");

                Assert.AreEqual(PreviewRenderWrapper.FaceTrackFov, _wrapper.CameraFieldOfView, 1e-5f);
                Assert.Less(PreviewRenderWrapper.FaceTrackFov, PreviewRenderWrapper.DefaultFov);
            }
            finally
            {
                DestroyPreviewSourceMaterial(source);
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void Setup_WithUnresolvableTrackTargetPath_FallsBackToDefaultFov()
        {
            var source = CreatePreviewSource();

            try
            {
                _wrapper.Setup(source, "no/such/joint");

                Assert.AreEqual(PreviewRenderWrapper.DefaultFov, _wrapper.CameraFieldOfView, 1e-5f);
            }
            finally
            {
                DestroyPreviewSourceMaterial(source);
                Object.DestroyImmediate(source);
            }
        }

        /// <summary>
        /// BlendShape（頂点を視界外へ移動させる "MoveAway"）付きの両面クアッド
        /// SkinnedMeshRenderer を持つプレビューソースを生成する。
        /// </summary>
        private static GameObject CreateBlendShapeQuadSource(out Mesh mesh, out Material material)
        {
            mesh = new Mesh { hideFlags = HideFlags.HideAndDontSave };
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3(0.5f, -0.5f, 0f),
                new Vector3(-0.5f, 0.5f, 0f),
                new Vector3(0.5f, 0.5f, 0f),
            };
            // 両面（カメラがどちら側にいても描画されるように表裏 4 枚）
            mesh.triangles = new[] { 0, 2, 1, 2, 3, 1, 1, 2, 0, 1, 3, 2 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var deltas = new[]
            {
                new Vector3(50f, 50f, 0f),
                new Vector3(50f, 50f, 0f),
                new Vector3(50f, 50f, 0f),
                new Vector3(50f, 50f, 0f),
            };
            mesh.AddBlendShapeFrame("MoveAway", 100f, deltas, null, null);

            var shader = Shader.Find("Universal Render Pipeline/Unlit")
                ?? Shader.Find("Unlit/Color");
            material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", Color.red);
            material.color = Color.red;

            var source = new GameObject("BlendShapeQuadSource") { hideFlags = HideFlags.HideAndDontSave };
            var smr = source.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = mesh;
            smr.localBounds = new Bounds(Vector3.zero, Vector3.one * 3f);
            smr.updateWhenOffscreen = true;
            smr.sharedMaterial = material;
            return source;
        }

        private static bool PixelsDiffer(Texture2D a, Texture2D b)
        {
            var pa = a.GetPixels32();
            var pb = b.GetPixels32();
            if (pa.Length != pb.Length)
                return true;

            for (int i = 0; i < pa.Length; i++)
            {
                if (pa[i].r != pb[i].r || pa[i].g != pb[i].g || pa[i].b != pb[i].b)
                    return true;
            }

            return false;
        }

        private static GameObject CreatePreviewSource()
        {
            var source = GameObject.CreatePrimitive(PrimitiveType.Cube);
            source.hideFlags = HideFlags.HideAndDontSave;
            var shader = Shader.Find("Universal Render Pipeline/Lit")
                ?? Shader.Find("Standard")
                ?? Shader.Find("Unlit/Color");
            var material = new Material(shader)
            {
                color = Color.red,
                hideFlags = HideFlags.HideAndDontSave
            };
            source.GetComponent<Renderer>().sharedMaterial = material;
            return source;
        }

        private static void IgnoreWhenGraphicsDeviceUnavailable()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                Assert.Ignore("Preview rendering requires an active graphics device.");
        }

        private static void DestroyPreviewSourceMaterial(GameObject source)
        {
            var renderer = source.GetComponent<Renderer>();
            if (renderer != null && renderer.sharedMaterial != null)
                Object.DestroyImmediate(renderer.sharedMaterial);
        }

        private static bool ContainsNonBackgroundPixel(Texture2D texture)
        {
            var background = PreviewRenderWrapper.DefaultBackgroundColor;
            var pixels = texture.GetPixels();
            for (int i = 0; i < pixels.Length; i++)
            {
                if (Mathf.Abs(pixels[i].r - background.r) > 0.02f
                    || Mathf.Abs(pixels[i].g - background.g) > 0.02f
                    || Mathf.Abs(pixels[i].b - background.b) > 0.02f)
                    return true;
            }

            return false;
        }
    }
}
