using System;
using System.Collections.Generic;
using NUnit.Framework;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;

namespace Hidano.FacialControl.Tests.EditMode.Domain.Services
{
    /// <summary>
    /// <see cref="ExpressionResolver"/> のテスト。
    /// SnapshotId → BlendShape 値 / Bone スナップショット列の preallocated 解決の振る舞いを検証する。
    /// </summary>
    [TestFixture]
    public class ExpressionResolverTests
    {
        private static ExpressionSnapshot BuildSnapshot(
            string id,
            BlendShapeSnapshot[] blendShapes,
            BoneSnapshot[] bones)
        {
            return new ExpressionSnapshot(
                id: id,
                transitionDuration: 0.25f,
                transitionCurvePreset: TransitionCurvePreset.Linear,
                blendShapes: blendShapes,
                bones: bones,
                rendererPaths: new[] { "Body" });
        }

        [Test]
        public void TryResolve_KnownId_FillsOutputs()
        {
            var blendShapes = new[]
            {
                new BlendShapeSnapshot("Body", "Smile", 0.5f),
                new BlendShapeSnapshot("Body", "Anger", 0.25f),
            };
            var bones = new[]
            {
                new BoneSnapshot("Head", 0f, 0f, 0f, 10f, 0f, 0f, 1f, 1f, 1f),
            };
            var snapshot = BuildSnapshot("expr-known", blendShapes, bones);

            var dict = new Dictionary<string, ExpressionSnapshot>(StringComparer.Ordinal)
            {
                { "expr-known", snapshot },
            };
            var resolver = new ExpressionResolver(dict);

            var bsBuffer = new float[2];
            var boneBuffer = new BoneSnapshot[1];

            bool ok = resolver.TryResolve("expr-known", bsBuffer, boneBuffer);

            Assert.IsTrue(ok);
            Assert.AreEqual(0.5f, bsBuffer[0]);
            Assert.AreEqual(0.25f, bsBuffer[1]);
            Assert.AreEqual("Head", boneBuffer[0].BonePath);
            Assert.AreEqual(10f, boneBuffer[0].EulerX);
            Assert.AreEqual(1, resolver.SnapshotCount);
        }

        [Test]
        public void TryResolve_UnknownId_ReturnsFalse()
        {
            var snapshot = BuildSnapshot(
                "expr-1",
                new[] { new BlendShapeSnapshot("Body", "Smile", 1f) },
                Array.Empty<BoneSnapshot>());

            var dict = new Dictionary<string, ExpressionSnapshot>(StringComparer.Ordinal)
            {
                { "expr-1", snapshot },
            };
            var resolver = new ExpressionResolver(dict);

            var bsBuffer = new float[4];
            var boneBuffer = new BoneSnapshot[4];

            bool ok = resolver.TryResolve("expr-unknown", bsBuffer, boneBuffer);

            Assert.IsFalse(ok);
            // 出力バッファは未変更（default のまま）
            Assert.AreEqual(0f, bsBuffer[0]);
            Assert.AreEqual(default(BoneSnapshot), boneBuffer[0]);
        }

        [Test]
        public void TryResolve_OutputBufferTooSmall_ReturnsFalse()
        {
            var blendShapes = new[]
            {
                new BlendShapeSnapshot("Body", "A", 0.1f),
                new BlendShapeSnapshot("Body", "B", 0.2f),
                new BlendShapeSnapshot("Body", "C", 0.3f),
            };
            var bones = new[]
            {
                new BoneSnapshot("Head", 0f, 0f, 0f, 0f, 0f, 0f, 1f, 1f, 1f),
                new BoneSnapshot("Neck", 0f, 0f, 0f, 0f, 0f, 0f, 1f, 1f, 1f),
            };
            var snapshot = BuildSnapshot("expr-big", blendShapes, bones);
            var dict = new Dictionary<string, ExpressionSnapshot>(StringComparer.Ordinal)
            {
                { "expr-big", snapshot },
            };
            var resolver = new ExpressionResolver(dict);

            // BlendShape バッファ不足
            var smallBs = new float[2];
            var bigBone = new BoneSnapshot[2];
            Assert.IsFalse(resolver.TryResolve("expr-big", smallBs, bigBone));
            // 部分書込みされていないこと（Span への書込み前に false 復帰）
            Assert.AreEqual(0f, smallBs[0]);
            Assert.AreEqual(0f, smallBs[1]);

            // Bone バッファ不足
            var bigBs = new float[3];
            var smallBone = new BoneSnapshot[1];
            Assert.IsFalse(resolver.TryResolve("expr-big", bigBs, smallBone));
            Assert.AreEqual(0f, bigBs[0]);
            Assert.AreEqual(default(BoneSnapshot), smallBone[0]);
        }

        [Test]
        public void TryResolve_NullSnapshotId_ReturnsFalse()
        {
            var resolver = new ExpressionResolver(new Dictionary<string, ExpressionSnapshot>());

            bool ok = resolver.TryResolve(null, new float[1], new BoneSnapshot[1]);

            Assert.IsFalse(ok);
        }

        [Test]
        public void Ctor_NullDictionary_BehavesAsEmpty()
        {
            var resolver = new ExpressionResolver(null);

            Assert.AreEqual(0, resolver.SnapshotCount);
            Assert.IsFalse(resolver.TryResolve("any", new float[1], new BoneSnapshot[1]));
        }

        [Test]
        public void TryGetSnapshot_KnownId_ReturnsSnapshot()
        {
            var snapshot = BuildSnapshot(
                "expr-x",
                new[] { new BlendShapeSnapshot("Body", "Smile", 0.75f) },
                Array.Empty<BoneSnapshot>());
            var resolver = new ExpressionResolver(new Dictionary<string, ExpressionSnapshot>(StringComparer.Ordinal)
            {
                { "expr-x", snapshot },
            });

            bool ok = resolver.TryGetSnapshot("expr-x", out var resolved);

            Assert.IsTrue(ok);
            Assert.AreEqual("expr-x", resolved.Id);
            Assert.AreEqual(0.25f, resolved.TransitionDuration);
            Assert.AreEqual(1, resolved.BlendShapes.Length);
            Assert.AreEqual(0.75f, resolved.BlendShapes.Span[0].Value);
        }
    }
}
