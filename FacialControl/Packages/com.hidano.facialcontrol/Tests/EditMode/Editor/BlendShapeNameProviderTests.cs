using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Hidano.FacialControl.Editor.Common;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.EditMode.Editor
{
    /// <summary>
    /// P-Q1-04: <see cref="BlendShapeNameProvider"/> の検証。
    /// 参照モデル配下の SkinnedMeshRenderer から BlendShape 名を収集し、
    /// 重複排除・Ordinal ソート・null 安全を保証する。
    /// </summary>
    [TestFixture]
    [SmallTest]
    public class BlendShapeNameProviderTests : SizedTestFixture
    {
        private readonly List<Object> _trackedObjects = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _trackedObjects.Count; i++)
            {
                if (_trackedObjects[i] != null)
                    Object.DestroyImmediate(_trackedObjects[i]);
            }
            _trackedObjects.Clear();
        }

        // --- GameObject オーバーロード ---

        [Test]
        public void GetBlendShapeNames_NullGameObject_ReturnsEmptyArray()
        {
            var names = BlendShapeNameProvider.GetBlendShapeNames((GameObject)null);

            Assert.IsNotNull(names);
            Assert.AreEqual(0, names.Length);
        }

        [Test]
        public void GetBlendShapeNames_GameObjectWithoutSmr_ReturnsEmptyArray()
        {
            var go = TrackGameObject(new GameObject("Empty"));

            var names = BlendShapeNameProvider.GetBlendShapeNames(go);

            Assert.AreEqual(0, names.Length);
        }

        [Test]
        public void GetBlendShapeNames_GameObjectWithSingleSmr_ReturnsShapeNames()
        {
            var go = TrackGameObject(new GameObject("Model"));
            var smr = go.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = CreateMeshWithShapes("Joy", "Angry", "Sad");

            var names = BlendShapeNameProvider.GetBlendShapeNames(go);

            Assert.AreEqual(3, names.Length);
            CollectionAssert.AreEqual(new[] { "Angry", "Joy", "Sad" }, names);
        }

        [Test]
        public void GetBlendShapeNames_GameObjectWithChildSmr_FindsNamesInChildren()
        {
            var root = TrackGameObject(new GameObject("Root"));
            var child = TrackGameObject(new GameObject("Child"));
            child.transform.SetParent(root.transform);
            var smr = child.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = CreateMeshWithShapes("Joy");

            var names = BlendShapeNameProvider.GetBlendShapeNames(root);

            Assert.AreEqual(1, names.Length);
            Assert.AreEqual("Joy", names[0]);
        }

        [Test]
        public void GetBlendShapeNames_GameObjectWithInactiveChildSmr_StillFindsNames()
        {
            var root = TrackGameObject(new GameObject("Root"));
            var child = TrackGameObject(new GameObject("InactiveChild"));
            child.transform.SetParent(root.transform);
            child.SetActive(false);
            var smr = child.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = CreateMeshWithShapes("Joy");

            var names = BlendShapeNameProvider.GetBlendShapeNames(root);

            Assert.AreEqual(1, names.Length);
        }

        // --- SkinnedMeshRenderer[] オーバーロード ---

        [Test]
        public void GetBlendShapeNames_NullArray_ReturnsEmptyArray()
        {
            var names = BlendShapeNameProvider.GetBlendShapeNames((SkinnedMeshRenderer[])null);

            Assert.IsNotNull(names);
            Assert.AreEqual(0, names.Length);
        }

        [Test]
        public void GetBlendShapeNames_EmptyArray_ReturnsEmptyArray()
        {
            var names = BlendShapeNameProvider.GetBlendShapeNames(new SkinnedMeshRenderer[0]);

            Assert.IsNotNull(names);
            Assert.AreEqual(0, names.Length);
        }

        [Test]
        public void GetBlendShapeNames_MultipleSmr_DeduplicatesAcrossRenderers()
        {
            var go1 = TrackGameObject(new GameObject("Model1"));
            var smr1 = go1.AddComponent<SkinnedMeshRenderer>();
            smr1.sharedMesh = CreateMeshWithShapes("Joy", "Angry");

            var go2 = TrackGameObject(new GameObject("Model2"));
            var smr2 = go2.AddComponent<SkinnedMeshRenderer>();
            smr2.sharedMesh = CreateMeshWithShapes("Joy", "Sad");

            var names = BlendShapeNameProvider.GetBlendShapeNames(new[] { smr1, smr2 });

            Assert.AreEqual(3, names.Length);
            CollectionAssert.AreEqual(new[] { "Angry", "Joy", "Sad" }, names);
        }

        [Test]
        public void GetBlendShapeNames_OrdinalSort_UppercaseBeforeLowercase()
        {
            var go = TrackGameObject(new GameObject("Model"));
            var smr = go.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = CreateMeshWithShapes("banana", "Apple", "cherry");

            var names = BlendShapeNameProvider.GetBlendShapeNames(go);

            // Ordinal 比較: 大文字が小文字より前
            CollectionAssert.AreEqual(new[] { "Apple", "banana", "cherry" }, names);
        }

        [Test]
        public void GetBlendShapeNames_SmrWithNullMesh_Skipped()
        {
            var go = TrackGameObject(new GameObject("Model"));
            var smr = go.AddComponent<SkinnedMeshRenderer>();
            // sharedMesh 未設定 → スキップされるはず

            var names = BlendShapeNameProvider.GetBlendShapeNames(new[] { smr });

            Assert.AreEqual(0, names.Length);
        }

        [Test]
        public void GetBlendShapeNames_NullSmrInArray_Skipped()
        {
            var go = TrackGameObject(new GameObject("Model"));
            var smr = go.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = CreateMeshWithShapes("Joy");

            var names = BlendShapeNameProvider.GetBlendShapeNames(new[] { null, smr, null });

            Assert.AreEqual(1, names.Length);
            Assert.AreEqual("Joy", names[0]);
        }

        [Test]
        public void GetBlendShapeNames_JapaneseShapeNames_HandledCorrectly()
        {
            var go = TrackGameObject(new GameObject("Model"));
            var smr = go.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = CreateMeshWithShapes("笑顔", "怒り", "悲しみ");

            var names = BlendShapeNameProvider.GetBlendShapeNames(go);

            Assert.AreEqual(3, names.Length);
            CollectionAssert.Contains(names, "笑顔");
            CollectionAssert.Contains(names, "怒り");
            CollectionAssert.Contains(names, "悲しみ");
        }

        // --- ヘルパー ---

        /// <summary>
        /// 指定された BlendShape 名を持つ最小限の Mesh を動的生成する。
        /// </summary>
        private Mesh CreateMeshWithShapes(params string[] shapeNames)
        {
            var mesh = new Mesh();
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            mesh.triangles = new[] { 0, 1, 2 };

            var zeroDeltas = new Vector3[3];
            for (int i = 0; i < shapeNames.Length; i++)
            {
                mesh.AddBlendShapeFrame(shapeNames[i], 100f, zeroDeltas, null, null);
            }
            _trackedObjects.Add(mesh);
            return mesh;
        }

        private GameObject TrackGameObject(GameObject go)
        {
            _trackedObjects.Add(go);
            return go;
        }
    }
}
