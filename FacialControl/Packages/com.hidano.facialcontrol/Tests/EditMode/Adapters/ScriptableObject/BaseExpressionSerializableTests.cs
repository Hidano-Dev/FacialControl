using System.Collections.Generic;
using Hidano.FacialControl.Adapters.Json.Dto;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using NUnit.Framework;
using UnityEngine;
using Hidano.FacialControl.Testing;
using Object = UnityEngine.Object;

namespace Hidano.FacialControl.Tests.EditMode.Adapters.ScriptableObjectTests
{
    [TestFixture]
    [SmallTest]
    public class BaseExpressionSerializableTests : SizedTestFixture
    {
        [Test]
        public void IsEmpty_NewInstance_ReturnsTrueAndProvidesEmptyBlendShapeList()
        {
            var baseExpression = new BaseExpressionSerializable();

            Assert.That(baseExpression.IsEmpty, Is.True);
            Assert.That(baseExpression.cachedSnapshot, Is.Not.Null);
            Assert.That(baseExpression.cachedSnapshot.blendShapes, Is.Not.Null);
            Assert.That(baseExpression.cachedSnapshot.blendShapes, Is.Empty);
        }

        [Test]
        public void IsEmpty_NullCachedSnapshot_ReturnsTrue()
        {
            var baseExpression = new BaseExpressionSerializable
            {
                cachedSnapshot = null,
            };

            Assert.That(baseExpression.IsEmpty, Is.True);
        }

        [Test]
        public void EnsureCachedSnapshot_NullBlendShapes_CreatesEmptyList()
        {
            var baseExpression = new BaseExpressionSerializable
            {
                cachedSnapshot = new ExpressionSnapshotDto(),
            };

            ExpressionSnapshotDto snapshot = baseExpression.EnsureCachedSnapshot();

            Assert.That(snapshot, Is.SameAs(baseExpression.cachedSnapshot));
            Assert.That(snapshot.blendShapes, Is.Not.Null);
            Assert.That(snapshot.blendShapes, Is.Empty);
            Assert.That(baseExpression.IsEmpty, Is.True);
        }

        [Test]
        public void Fields_AssignedValues_RetainAnimationClipAndSnapshot()
        {
            var clip = new AnimationClip { name = "BaseExpressionSerializable_Clip" };
            try
            {
                var snapshot = new ExpressionSnapshotDto
                {
                    blendShapes = new List<BlendShapeSnapshotDto>
                    {
                        new BlendShapeSnapshotDto
                        {
                            rendererPath = "Body",
                            name = "Brow_Angry",
                            value = 64.5f,
                        },
                    },
                };

                var baseExpression = new BaseExpressionSerializable
                {
                    animationClip = clip,
                    cachedSnapshot = snapshot,
                };

                Assert.That(baseExpression.animationClip, Is.SameAs(clip));
                Assert.That(baseExpression.cachedSnapshot, Is.SameAs(snapshot));
                Assert.That(baseExpression.IsEmpty, Is.False);
                Assert.That(baseExpression.cachedSnapshot.blendShapes[0].name, Is.EqualTo("Brow_Angry"));
                Assert.That(baseExpression.cachedSnapshot.blendShapes[0].value, Is.EqualTo(64.5f).Within(1e-6f));
            }
            finally
            {
                Object.DestroyImmediate(clip);
            }
        }
    }
}
