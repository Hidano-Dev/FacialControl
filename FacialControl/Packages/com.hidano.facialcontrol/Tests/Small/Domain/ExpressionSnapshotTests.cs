using System;
using NUnit.Framework;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Tests.EditMode.Domain
{
    /// <summary>
    /// ExpressionSnapshot のテスト。
    /// AnimationClip 由来 snapshot の Domain 受け皿としての振る舞いを検証する。
    /// </summary>
    [TestFixture]
    public class ExpressionSnapshotTests
    {
        [Test]
        public void Ctor_Defensive_Copies_BlendShapes()
        {
            var blendShapes = new[]
            {
                new BlendShapeSnapshot("Body/Face", "Smile", 0.5f),
                new BlendShapeSnapshot("Body/Face", "Anger", 0.25f),
            };
            var bones = new[]
            {
                new BoneSnapshot("Head", 0f, 0f, 0f, 10f, 0f, 0f, 1f, 1f, 1f),
            };
            var rendererPaths = new[] { "Body/Face" };

            var snapshot = new ExpressionSnapshot(
                "expr-001",
                0.5f,
                TransitionCurvePreset.EaseInOut,
                blendShapes,
                bones,
                rendererPaths);

            // 元配列を破壊しても snapshot 内部は不変であること（防御コピー）
            blendShapes[0] = new BlendShapeSnapshot("X", "Y", 9999f);
            bones[0] = new BoneSnapshot("X", 9f, 9f, 9f, 9f, 9f, 9f, 9f, 9f, 9f);
            rendererPaths[0] = "Mutated";

            Assert.AreEqual(2, snapshot.BlendShapes.Length);
            Assert.AreEqual("Smile", snapshot.BlendShapes.Span[0].Name);
            Assert.AreEqual(0.5f, snapshot.BlendShapes.Span[0].Value);
            Assert.AreEqual("Anger", snapshot.BlendShapes.Span[1].Name);

            Assert.AreEqual(1, snapshot.Bones.Length);
            Assert.AreEqual("Head", snapshot.Bones.Span[0].BonePath);
            Assert.AreEqual(10f, snapshot.Bones.Span[0].EulerX);

            Assert.AreEqual(1, snapshot.RendererPaths.Length);
            Assert.AreEqual("Body/Face", snapshot.RendererPaths.Span[0]);
        }

        [Test]
        public void Ctor_NullArrays_ProducesEmptyMemory()
        {
            var snapshot = new ExpressionSnapshot(
                "expr-002",
                0.25f,
                TransitionCurvePreset.Linear,
                blendShapes: null,
                bones: null,
                rendererPaths: null);

            Assert.AreEqual(0, snapshot.BlendShapes.Length);
            Assert.AreEqual(0, snapshot.Bones.Length);
            Assert.AreEqual(0, snapshot.RendererPaths.Length);
            Assert.IsTrue(snapshot.BlendShapes.IsEmpty);
            Assert.IsTrue(snapshot.Bones.IsEmpty);
            Assert.IsTrue(snapshot.RendererPaths.IsEmpty);
        }

        [Test]
        public void TransitionCurvePreset_DefaultsTo_Linear()
        {
            // enum の数値割当が Linear=0 であることを保証する（デフォルト値で Linear が得られる）
            TransitionCurvePreset preset = default;

            Assert.AreEqual(TransitionCurvePreset.Linear, preset);
            Assert.AreEqual(0, (int)TransitionCurvePreset.Linear);
            Assert.AreEqual(1, (int)TransitionCurvePreset.EaseIn);
            Assert.AreEqual(2, (int)TransitionCurvePreset.EaseOut);
            Assert.AreEqual(3, (int)TransitionCurvePreset.EaseInOut);
        }

        [Test]
        public void TransitionDuration_Default_Is_OneFifteenth()
        {
            // 引数省略時のフォールバック値が Expression.DefaultTransitionDuration (1/15 秒) 
            var snapshot = ExpressionSnapshot.CreateDefault("expr-003");

            Assert.AreEqual("expr-003", snapshot.Id);
            Assert.AreEqual(Expression.DefaultTransitionDuration, snapshot.TransitionDuration);
            Assert.AreEqual(TransitionCurvePreset.Linear, snapshot.TransitionCurvePreset);
            Assert.IsTrue(snapshot.BlendShapes.IsEmpty);
            Assert.IsTrue(snapshot.Bones.IsEmpty);
            Assert.IsTrue(snapshot.RendererPaths.IsEmpty);
        }

        [Test]
        public void Ctor_NullId_NormalizedToEmpty()
        {
            var snapshot = new ExpressionSnapshot(
                id: null,
                transitionDuration: 0.25f,
                transitionCurvePreset: TransitionCurvePreset.Linear,
                blendShapes: null,
                bones: null,
                rendererPaths: null);

            Assert.AreEqual(string.Empty, snapshot.Id);
        }

        [Test]
        public void RendererPaths_NormalizesNullEntriesToEmpty()
        {
            var rendererPaths = new[] { "Body/Face", null, "Body/Head" };

            var snapshot = new ExpressionSnapshot(
                "expr-004",
                0.25f,
                TransitionCurvePreset.Linear,
                blendShapes: null,
                bones: null,
                rendererPaths: rendererPaths);

            Assert.AreEqual(3, snapshot.RendererPaths.Length);
            Assert.AreEqual("Body/Face", snapshot.RendererPaths.Span[0]);
            Assert.AreEqual(string.Empty, snapshot.RendererPaths.Span[1]);
            Assert.AreEqual("Body/Head", snapshot.RendererPaths.Span[2]);
        }
    }
}
