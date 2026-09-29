using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Hidano.FacialControl.Adapters.Playable;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.EditMode.Adapters.Playable
{
    /// <summary>
    /// <see cref="FacialControllerRendererOwnership"/> の登録・競合検出を検証する。
    /// 同一 SkinnedMeshRenderer を複数の FacialController が制御対象にしている状態を
    /// 初期化時に検出できることが目的。
    /// </summary>
    [TestFixture]
    [MediumTest]
    public class FacialControllerRendererOwnershipTests : SizedTestFixture
    {
        private readonly List<GameObject> _created = new List<GameObject>();
        private readonly List<Mesh> _meshes = new List<Mesh>();

        [SetUp]
        public void SetUp()
        {
            FacialControllerRendererOwnership.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            FacialControllerRendererOwnership.Clear();

            for (int i = 0; i < _created.Count; i++)
            {
                if (_created[i] != null)
                {
                    Object.DestroyImmediate(_created[i]);
                }
            }
            _created.Clear();

            for (int i = 0; i < _meshes.Count; i++)
            {
                if (_meshes[i] != null)
                {
                    Object.DestroyImmediate(_meshes[i]);
                }
            }
            _meshes.Clear();
        }

        [Test]
        public void FindConflict_NoRegistrations_ReturnsNull()
        {
            var controller = CreateController("Solo");
            var renderer = CreateRenderer("Face");

            var conflict = FacialControllerRendererOwnership.FindConflict(
                controller, new[] { renderer });

            Assert.That(conflict, Is.Null);
        }

        [Test]
        public void FindConflict_SharedRenderer_ReturnsRegisteredOwner()
        {
            var first = CreateController("First");
            var second = CreateController("Second");
            var renderer = CreateRenderer("Face");

            FacialControllerRendererOwnership.Register(first, new[] { renderer });

            var conflict = FacialControllerRendererOwnership.FindConflict(
                second, new[] { renderer });

            Assert.That(conflict, Is.SameAs(first));
        }

        [Test]
        public void FindConflict_DisjointRenderers_ReturnsNull()
        {
            var first = CreateController("First");
            var second = CreateController("Second");
            var faceRenderer = CreateRenderer("Face");
            var bodyRenderer = CreateRenderer("Body");

            FacialControllerRendererOwnership.Register(first, new[] { faceRenderer });

            var conflict = FacialControllerRendererOwnership.FindConflict(
                second, new[] { bodyRenderer });

            Assert.That(conflict, Is.Null);
        }

        [Test]
        public void FindConflict_SelfIsRegisteredOwner_ReturnsNull()
        {
            var controller = CreateController("Solo");
            var renderer = CreateRenderer("Face");

            FacialControllerRendererOwnership.Register(controller, new[] { renderer });

            var conflict = FacialControllerRendererOwnership.FindConflict(
                controller, new[] { renderer });

            Assert.That(conflict, Is.Null);
        }

        [Test]
        public void FindConflict_AfterUnregister_ReturnsNull()
        {
            var first = CreateController("First");
            var second = CreateController("Second");
            var renderer = CreateRenderer("Face");

            FacialControllerRendererOwnership.Register(first, new[] { renderer });
            FacialControllerRendererOwnership.Unregister(first);

            var conflict = FacialControllerRendererOwnership.FindConflict(
                second, new[] { renderer });

            Assert.That(conflict, Is.Null);
        }

        [Test]
        public void FindConflict_OwnerWasDestroyed_ReturnsNull()
        {
            var first = CreateController("First");
            var second = CreateController("Second");
            var renderer = CreateRenderer("Face");

            FacialControllerRendererOwnership.Register(first, new[] { renderer });
            Object.DestroyImmediate(first.gameObject);

            var conflict = FacialControllerRendererOwnership.FindConflict(
                second, new[] { renderer });

            Assert.That(conflict, Is.Null);
        }

        [Test]
        public void FindConflict_PartialOverlap_ReturnsRegisteredOwner()
        {
            var first = CreateController("First");
            var second = CreateController("Second");
            var faceRenderer = CreateRenderer("Face");
            var bodyRenderer = CreateRenderer("Body");

            FacialControllerRendererOwnership.Register(first, new[] { faceRenderer });

            var conflict = FacialControllerRendererOwnership.FindConflict(
                second, new[] { bodyRenderer, faceRenderer });

            Assert.That(conflict, Is.SameAs(first));
        }

        [Test]
        public void Register_NullRenderers_DoesNotThrow()
        {
            var controller = CreateController("Solo");

            Assert.DoesNotThrow(() => FacialControllerRendererOwnership.Register(controller, null));
            Assert.DoesNotThrow(() => FacialControllerRendererOwnership.Unregister(controller));
        }

        [Test]
        public void Register_ReplacesPreviousRenderersOfSameOwner()
        {
            var first = CreateController("First");
            var second = CreateController("Second");
            var faceRenderer = CreateRenderer("Face");
            var bodyRenderer = CreateRenderer("Body");

            FacialControllerRendererOwnership.Register(first, new[] { faceRenderer });
            FacialControllerRendererOwnership.Register(first, new[] { bodyRenderer });

            Assert.That(
                FacialControllerRendererOwnership.FindConflict(second, new[] { faceRenderer }),
                Is.Null);
            Assert.That(
                FacialControllerRendererOwnership.FindConflict(second, new[] { bodyRenderer }),
                Is.SameAs(first));
        }

        private FacialController CreateController(string name)
        {
            var go = new GameObject(name);
            _created.Add(go);
            go.AddComponent<Animator>();
            return go.AddComponent<FacialController>();
        }

        private SkinnedMeshRenderer CreateRenderer(string name)
        {
            var go = new GameObject(name);
            _created.Add(go);
            var renderer = go.AddComponent<SkinnedMeshRenderer>();

            var mesh = new Mesh
            {
                vertices = new[] { Vector3.zero, Vector3.right, Vector3.up },
                triangles = new[] { 0, 1, 2 },
            };
            mesh.AddBlendShapeFrame(name + "_shape", 100f, new Vector3[3], null, null);
            _meshes.Add(mesh);
            renderer.sharedMesh = mesh;

            return renderer;
        }
    }
}
