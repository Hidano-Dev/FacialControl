using System.Collections.Generic;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Editor.Thumbnails;
using Hidano.FacialControl.Testing;
using NUnit.Framework;
using UnityEngine;

namespace Hidano.FacialControl.Tests.EditMode.Editor.Thumbnails
{
    /// <summary>
    /// <see cref="ExpressionThumbnailRenderer.ApplyExpression"/>。描画そのものは GPU が要るため対象外。
    /// サムネイルがランタイムと同じ見た目になるよう、BlendShape を RendererPath ではなく名前で
    /// 全 SkinnedMeshRenderer に適用することを守る。
    /// </summary>
    [TestFixture]
    [SmallTest]
    public class ExpressionThumbnailRendererTests : SizedTestFixture
    {
        private readonly List<Object> _tracked = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _tracked.Count; i++)
            {
                if (_tracked[i] != null)
                    Object.DestroyImmediate(_tracked[i]);
            }
            _tracked.Clear();
        }

        [Test]
        public void ApplyExpression_RendererPathMismatch_AppliesByBlendShapeName()
        {
            var root = new GameObject("Model");
            _tracked.Add(root);
            var face = CreateSkinnedChild(root, "Face", "smile", "blink");
            var snapshot = CreateSnapshot(new BlendShapeSnapshot("Some/Other/Path", "smile", 0.4f));

            ExpressionThumbnailRenderer.ApplyExpression(root, null, snapshot);

            Assert.That(face.GetBlendShapeWeight(0), Is.EqualTo(40f).Within(1e-4f));
            Assert.That(face.GetBlendShapeWeight(1), Is.EqualTo(0f));
        }

        [Test]
        public void ApplyExpression_SameNameOnMultipleRenderers_AppliesToAll()
        {
            var root = new GameObject("Model");
            _tracked.Add(root);
            var face = CreateSkinnedChild(root, "Face", "smile");
            var teeth = CreateSkinnedChild(root, "Teeth", "smile");
            var snapshot = CreateSnapshot(new BlendShapeSnapshot("Face", "smile", 1f));

            ExpressionThumbnailRenderer.ApplyExpression(root, null, snapshot);

            Assert.That(face.GetBlendShapeWeight(0), Is.EqualTo(100f).Within(1e-4f));
            Assert.That(teeth.GetBlendShapeWeight(0), Is.EqualTo(100f).Within(1e-4f));
        }

        [Test]
        public void ApplyExpression_DuplicateNameInSnapshot_UsesLargerMagnitude()
        {
            var root = new GameObject("Model");
            _tracked.Add(root);
            var face = CreateSkinnedChild(root, "Face", "smile");
            var snapshot = CreateSnapshot(
                new BlendShapeSnapshot("Face", "smile", 0.2f),
                new BlendShapeSnapshot("Body", "smile", 0.7f));

            ExpressionThumbnailRenderer.ApplyExpression(root, null, snapshot);

            Assert.That(face.GetBlendShapeWeight(0), Is.EqualTo(70f).Within(1e-4f));
        }

        [Test]
        public void ApplyExpression_RendererWithoutMesh_DoesNotThrow()
        {
            var root = new GameObject("Model");
            _tracked.Add(root);
            var child = new GameObject("Empty");
            child.transform.SetParent(root.transform);
            child.AddComponent<SkinnedMeshRenderer>();
            var snapshot = CreateSnapshot(new BlendShapeSnapshot("Face", "smile", 1f));

            Assert.DoesNotThrow(() => ExpressionThumbnailRenderer.ApplyExpression(root, null, snapshot));
        }

        private SkinnedMeshRenderer CreateSkinnedChild(GameObject root, string name, params string[] blendShapeNames)
        {
            var child = new GameObject(name);
            child.transform.SetParent(root.transform);
            var renderer = child.AddComponent<SkinnedMeshRenderer>();

            var mesh = new Mesh
            {
                vertices = new[] { Vector3.zero, Vector3.right, Vector3.up },
                triangles = new[] { 0, 1, 2 },
            };
            var deltas = new Vector3[3];
            for (int i = 0; i < blendShapeNames.Length; i++)
            {
                mesh.AddBlendShapeFrame(blendShapeNames[i], 100f, deltas, deltas, deltas);
            }
            _tracked.Add(mesh);
            renderer.sharedMesh = mesh;
            return renderer;
        }

        private static ExpressionSnapshot CreateSnapshot(params BlendShapeSnapshot[] blendShapes)
        {
            return new ExpressionSnapshot("expr", 0.1f, TransitionCurvePreset.Linear, blendShapes, null, null);
        }
    }
}
