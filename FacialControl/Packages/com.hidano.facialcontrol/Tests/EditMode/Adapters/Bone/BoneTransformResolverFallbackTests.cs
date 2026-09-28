using System;
using System.Text.RegularExpressions;
using Hidano.FacialControl.Adapters.Bone;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.EditMode.Adapters.Bone
{
    [SmallTest]
    public sealed class BoneTransformResolverFallbackTests : SizedTestFixture
    {
        private GameObject _root;

        [TearDown]
        public void TearDown()
        {
            if (_root != null)
            {
                UnityEngine.Object.DestroyImmediate(_root);
            }
        }

        [Test]
        public void Resolve_ExactRelativePath_ReturnsMatchingTransformWithoutWarning()
        {
            _root = BuildHierarchy("Model", "Armature", "Head", "Eye");
            var resolver = new BoneTransformResolver(_root.transform);

            Assert.That(resolver.Resolve("Armature/Head/Eye"), Is.SameAs(_root.transform.Find("Armature/Head/Eye")));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Resolve_MismatchedPath_FallsBackToLeafAndWarnsOnce()
        {
            _root = BuildHierarchy("Model", "Armature", "Head", "Eye");
            var resolver = new BoneTransformResolver(_root.transform);
            var expected = _root.transform.Find("Armature/Head/Eye");

            LogAssert.Expect(LogType.Warning, new Regex("OldRig/Head/Eye"));
            Assert.That(resolver.Resolve("OldRig/Head/Eye"), Is.SameAs(expected));
            Assert.That(resolver.Resolve("OldRig/Head/Eye"), Is.SameAs(expected));
        }

        [Test]
        public void Resolve_MissingPath_ReturnsNullAndKeepsExistingWarning()
        {
            _root = BuildHierarchy("Model", "Armature", "Head");
            var resolver = new BoneTransformResolver(_root.transform);

            LogAssert.Expect(LogType.Warning, new Regex("OldRig/Head/Missing"));
            Assert.That(resolver.Resolve("OldRig/Head/Missing"), Is.Null);
        }

        private static GameObject BuildHierarchy(params string[] names)
        {
            var root = new GameObject(names[0]);
            var current = root.transform;
            for (var i = 1; i < names.Length; i++)
            {
                var child = new GameObject(names[i]);
                child.transform.SetParent(current);
                current = child.transform;
            }

            return root;
        }
    }
}
