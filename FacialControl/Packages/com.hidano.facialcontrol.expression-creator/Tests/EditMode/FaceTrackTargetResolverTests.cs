using System.Collections.Generic;
using Hidano.FacialControl.ExpressionCreator;
using NUnit.Framework;
using UnityEngine;

namespace Hidano.FacialControl.ExpressionCreator.Tests.EditMode
{
    [TestFixture]
    public class FaceTrackTargetResolverTests
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

        private GameObject CreateRoot(string name = "Model")
        {
            var root = new GameObject(name);
            _trackedObjects.Add(root);
            return root;
        }

        private static GameObject AddChild(GameObject parent, string name)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent.transform);
            return child;
        }

        [Test]
        public void Resolve_NullRoot_ReturnsNull()
        {
            Assert.IsNull(FaceTrackTargetResolver.Resolve(null));
        }

        [Test]
        public void Resolve_NoCandidates_ReturnsNull()
        {
            var root = CreateRoot();
            AddChild(root, "Spine");
            AddChild(root, "Arm_L");

            Assert.IsNull(FaceTrackTargetResolver.Resolve(root));
        }

        [Test]
        public void Resolve_GenericModelWithNeckJoint_ReturnsNeck()
        {
            var root = CreateRoot();
            var armature = AddChild(root, "Armature");
            var neck = AddChild(armature, "neck_01");

            Assert.AreSame(neck.transform, FaceTrackTargetResolver.Resolve(root));
        }

        [Test]
        public void Resolve_GenericModelWithHeadAndNeck_PrefersHead()
        {
            var root = CreateRoot();
            var armature = AddChild(root, "Armature");
            var neck = AddChild(armature, "Neck");
            var head = AddChild(neck, "Head");

            Assert.AreSame(head.transform, FaceTrackTargetResolver.Resolve(root));
        }

        [Test]
        public void Resolve_NameMatchIsCaseInsensitive()
        {
            var root = CreateRoot();
            var armature = AddChild(root, "Armature");
            var head = AddChild(armature, "J_Bip_C_HEAD");

            Assert.AreSame(head.transform, FaceTrackTargetResolver.Resolve(root));
        }

        [Test]
        public void Resolve_RendererNamedHead_SkippedInFavorOfJoint()
        {
            var root = CreateRoot();
            var headMesh = AddChild(root, "HeadMesh");
            headMesh.AddComponent<SkinnedMeshRenderer>();
            var armature = AddChild(root, "Armature");
            var neck = AddChild(armature, "neck");

            Assert.AreSame(neck.transform, FaceTrackTargetResolver.Resolve(root));
        }

        [Test]
        public void Resolve_OnlyRendererNamedHead_ReturnsNull()
        {
            var root = CreateRoot();
            var headMesh = AddChild(root, "Head");
            headMesh.AddComponent<SkinnedMeshRenderer>();

            Assert.IsNull(FaceTrackTargetResolver.Resolve(root));
        }

        [Test]
        public void Resolve_RootNamedHead_Excluded()
        {
            var root = CreateRoot("HeadModel");
            AddChild(root, "Spine");

            Assert.IsNull(FaceTrackTargetResolver.Resolve(root));
        }
    }
}
