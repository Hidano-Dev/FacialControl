using NUnit.Framework;
using UnityEngine;
using Hidano.FacialControl.Adapters.Playable;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.EditMode.Adapters.Playable
{
    /// <summary>
    /// <see cref="FacialControllerConflictResolver"/> の解決規則を検証する。
    /// 同一 SkinnedMeshRenderer を 2 つの FacialController が掴んだときに
    /// どちらを生かすかは「階層上位（祖先側）優先、親子関係が無ければ先着優先」で決まる。
    /// </summary>
    [TestFixture]
    [SmallTest]
    public class FacialControllerConflictResolverTests : SizedTestFixture
    {
        private GameObject _root;

        [TearDown]
        public void TearDown()
        {
            if (_root != null)
            {
                Object.DestroyImmediate(_root);
                _root = null;
            }
        }

        [Test]
        public void Resolve_ExistingIsAncestorOfCandidate_ReturnsYieldToExisting()
        {
            BuildHierarchy(out Transform ancestor, out Transform descendant);

            var resolution = FacialControllerConflictResolver.Resolve(descendant, ancestor);

            Assert.That(resolution, Is.EqualTo(FacialControllerConflictResolution.YieldToExisting));
        }

        [Test]
        public void Resolve_CandidateIsAncestorOfExisting_ReturnsTakeOverFromExisting()
        {
            BuildHierarchy(out Transform ancestor, out Transform descendant);

            var resolution = FacialControllerConflictResolver.Resolve(ancestor, descendant);

            Assert.That(resolution, Is.EqualTo(FacialControllerConflictResolution.TakeOverFromExisting));
        }

        [Test]
        public void Resolve_UnrelatedHierarchies_ReturnsYieldToExisting()
        {
            _root = new GameObject("ConflictResolverTestsRoot");
            var left = new GameObject("Left");
            left.transform.SetParent(_root.transform, false);
            var right = new GameObject("Right");
            right.transform.SetParent(_root.transform, false);

            var resolution = FacialControllerConflictResolver.Resolve(left.transform, right.transform);

            Assert.That(resolution, Is.EqualTo(FacialControllerConflictResolution.YieldToExisting));
        }

        [Test]
        public void Resolve_SameTransform_ReturnsYieldToExisting()
        {
            _root = new GameObject("ConflictResolverTestsRoot");

            var resolution = FacialControllerConflictResolver.Resolve(_root.transform, _root.transform);

            Assert.That(resolution, Is.EqualTo(FacialControllerConflictResolution.YieldToExisting));
        }

        [Test]
        public void Resolve_ExistingIsNull_ReturnsNone()
        {
            _root = new GameObject("ConflictResolverTestsRoot");

            var resolution = FacialControllerConflictResolver.Resolve(_root.transform, null);

            Assert.That(resolution, Is.EqualTo(FacialControllerConflictResolution.None));
        }

        [Test]
        public void Resolve_CandidateIsNull_ReturnsNone()
        {
            _root = new GameObject("ConflictResolverTestsRoot");

            var resolution = FacialControllerConflictResolver.Resolve(null, _root.transform);

            Assert.That(resolution, Is.EqualTo(FacialControllerConflictResolution.None));
        }

        [Test]
        public void Resolve_GrandParentIsExisting_ReturnsYieldToExisting()
        {
            _root = new GameObject("ConflictResolverTestsRoot");
            var middle = new GameObject("Middle");
            middle.transform.SetParent(_root.transform, false);
            var leaf = new GameObject("Leaf");
            leaf.transform.SetParent(middle.transform, false);

            var resolution = FacialControllerConflictResolver.Resolve(leaf.transform, _root.transform);

            Assert.That(resolution, Is.EqualTo(FacialControllerConflictResolution.YieldToExisting));
        }

        /// <summary>
        /// Root（祖先） -&gt; Model（子孫）の 2 段階層を作る。
        /// </summary>
        private void BuildHierarchy(out Transform ancestor, out Transform descendant)
        {
            _root = new GameObject("ConflictResolverTestsRoot");
            var model = new GameObject("Model");
            model.transform.SetParent(_root.transform, false);

            ancestor = _root.transform;
            descendant = model.transform;
        }
    }
}
