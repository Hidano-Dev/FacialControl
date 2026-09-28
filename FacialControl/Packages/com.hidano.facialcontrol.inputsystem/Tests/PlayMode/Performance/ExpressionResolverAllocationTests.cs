using System;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Profiling;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.InputSystem.Tests.PlayMode.Performance
{
    /// <summary>
    //: <see cref="ExpressionResolver"/> の <c>TryResolve</c> ホットパスが
    /// 100 frames 連続呼出で 0-alloc であることを検証する PlayMode/Performance テスト
    /// 。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="ExpressionResolver"/> は構築時に snapshot 辞書を内部にコピーし、
    /// <c>TryResolve(string, Span&lt;float&gt;, Span&lt;BoneSnapshot&gt;)</c> ではマネージドヒープ確保を
    /// 行わずに出力バッファへ値を展開する。本テストは事前確保された出力バッファに対し
    /// 100 frame 相当の繰り返し解決でアロケーションが発生しないことを保証する。
    /// </para>
    /// </remarks>
    [TestFixture]
    [MediumTest]
    public class ExpressionResolverAllocationTests : SizedTestFixture
    {
        private const int FrameCount = 100;
        private const int BlendShapeCount = 52; // ARKit 52 相当
        private const int BoneCount = 8;

        private ExpressionResolver _resolver;
        private float[] _blendShapeBuffer;
        private BoneSnapshot[] _boneBuffer;
        private string _snapshotId;

        [SetUp]
        public void SetUp()
        {
            _snapshotId = "expr-1";

            var blendShapes = new BlendShapeSnapshot[BlendShapeCount];
            for (int i = 0; i < BlendShapeCount; i++)
            {
                blendShapes[i] = new BlendShapeSnapshot(
                    rendererPath: "Body",
                    name: $"blendShape_{i}",
                    value: i * 0.01f);
            }

            var bones = new BoneSnapshot[BoneCount];
            for (int i = 0; i < BoneCount; i++)
            {
                bones[i] = new BoneSnapshot(
                    bonePath: $"Armature/Bone{i}",
                    positionX: 0f, positionY: 0f, positionZ: 0f,
                    eulerX: 0f, eulerY: i * 5f, eulerZ: 0f,
                    scaleX: 1f, scaleY: 1f, scaleZ: 1f);
            }

            var snapshot = new ExpressionSnapshot(
                id: _snapshotId,
                transitionDuration: 0.25f,
                transitionCurvePreset: TransitionCurvePreset.Linear,
                blendShapes: blendShapes,
                bones: bones,
                rendererPaths: new[] { "Body" });

            var dict = new Dictionary<string, ExpressionSnapshot>(StringComparer.Ordinal)
            {
                { _snapshotId, snapshot },
            };

            _resolver = new ExpressionResolver(dict);
            _blendShapeBuffer = new float[BlendShapeCount];
            _boneBuffer = new BoneSnapshot[BoneCount];
        }

        [Test]
        public void TryResolve_100Frames_ZeroGCAllocation_ProfilerRecorder()
        {
            // ウォームアップ
            bool warmup = _resolver.TryResolve(_snapshotId, _blendShapeBuffer, _boneBuffer);
            Assert.IsTrue(warmup);

            using var recorder = ProfilerRecorder.StartNew(
                ProfilerCategory.Memory, "GC.Alloc");

            for (int frame = 0; frame < FrameCount; frame++)
            {
                bool ok = _resolver.TryResolve(_snapshotId, _blendShapeBuffer, _boneBuffer);
                Assert.IsTrue(ok);
            }

            long gcAlloc = recorder.LastValue;

            Assert.AreEqual(0, gcAlloc,
                $"ExpressionResolver.TryResolve で GC アロケーションが検出されました: {gcAlloc} bytes");
        }

        [Test]
        public void TryResolve_UnknownId_ZeroGCAllocation()
        {
            const string unknownId = "expr-unknown";

            // ウォームアップ
            bool warmup = _resolver.TryResolve(unknownId, _blendShapeBuffer, _boneBuffer);
            Assert.IsFalse(warmup, "未登録 SnapshotId は false を返すべき");

            using var recorder = ProfilerRecorder.StartNew(
                ProfilerCategory.Memory, "GC.Alloc");

            for (int frame = 0; frame < FrameCount; frame++)
            {
                bool ok = _resolver.TryResolve(unknownId, _blendShapeBuffer, _boneBuffer);
                Assert.IsFalse(ok);
            }

            long gcAlloc = recorder.LastValue;

            Assert.AreEqual(0, gcAlloc,
                $"未登録 SnapshotId 経路の TryResolve で GC アロケーションが検出されました: {gcAlloc} bytes");
        }

        [Test]
        public void TryGetSnapshot_100Frames_ZeroGCAllocation()
        {
            // ウォームアップ
            bool warmup = _resolver.TryGetSnapshot(_snapshotId, out _);
            Assert.IsTrue(warmup);

            using var recorder = ProfilerRecorder.StartNew(
                ProfilerCategory.Memory, "GC.Alloc");

            for (int frame = 0; frame < FrameCount; frame++)
            {
                bool ok = _resolver.TryGetSnapshot(_snapshotId, out _);
                Assert.IsTrue(ok);
            }

            long gcAlloc = recorder.LastValue;

            Assert.AreEqual(0, gcAlloc,
                $"ExpressionResolver.TryGetSnapshot で GC アロケーションが検出されました: {gcAlloc} bytes");
        }
    }
}
