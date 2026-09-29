using System;
using NUnit.Framework;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Playables;
using Hidano.FacialControl.Adapters.OSC;
using Hidano.FacialControl.Adapters.Playable;
using Hidano.FacialControl.Domain.Models;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.EditMode.Adapters
{
    /// <summary>
    /// OscReceiverPlayable のテスト。
    /// OscDoubleBuffer からの値読み取りと PlayableGraph への統合を検証する。
    /// </summary>
    [TestFixture]
    [SmallTest]
    public class OscReceiverPlayableTests : SizedTestFixture
    {
        private PlayableGraph _graph;
        private OscDoubleBuffer _buffer;

        [SetUp]
        public void SetUp()
        {
            _graph = PlayableGraph.Create("OscReceiverPlayableTestGraph");
        }

        [TearDown]
        public void TearDown()
        {
            if (_graph.IsValid())
            {
                _graph.Destroy();
            }
            if (_buffer != null)
            {
                _buffer.Dispose();
                _buffer = null;
            }
        }

        // ================================================================
        // Create — 生成
        // ================================================================

        [Test]
        public void Create_ValidParameters_ReturnsValidPlayable()
        {
            _buffer = new OscDoubleBuffer(3);
            var mappings = CreateTestMappings(3);

            var playable = OscReceiverPlayable.Create(_graph, _buffer, mappings, 3);

            Assert.IsTrue(playable.IsValid());
        }

        [Test]
        public void Create_NullBuffer_ThrowsArgumentNullException()
        {
            var mappings = CreateTestMappings(1);

            Assert.Throws<ArgumentNullException>(() =>
                OscReceiverPlayable.Create(_graph, null, mappings, 1));
        }

        [Test]
        public void Create_NullMappings_ThrowsArgumentNullException()
        {
            _buffer = new OscDoubleBuffer(1);

            Assert.Throws<ArgumentNullException>(() =>
                OscReceiverPlayable.Create(_graph, _buffer, null, 1));
        }

        [Test]
        public void Create_ZeroBlendShapeCount_CreatesValidPlayable()
        {
            _buffer = new OscDoubleBuffer(0);
            var mappings = Array.Empty<OscMapping>();

            var playable = OscReceiverPlayable.Create(_graph, _buffer, mappings, 0);

            Assert.IsTrue(playable.IsValid());
            var behaviour = playable.GetBehaviour();
            Assert.AreEqual(0, behaviour.BlendShapeCount);
        }

        // ================================================================
        // BlendShapeCount — プロパティ
        // ================================================================

        [Test]
        public void BlendShapeCount_AfterCreate_ReturnsCorrectCount()
        {
            _buffer = new OscDoubleBuffer(3);
            var mappings = CreateTestMappings(3);

            var playable = OscReceiverPlayable.Create(_graph, _buffer, mappings, 5);
            var behaviour = playable.GetBehaviour();

            Assert.AreEqual(5, behaviour.BlendShapeCount);
        }

        // ================================================================
        // OutputWeights — 出力バッファ
        // ================================================================

        [Test]
        public void OutputWeights_InitialState_AllZero()
        {
            _buffer = new OscDoubleBuffer(2);
            var mappings = CreateTestMappings(2);

            var playable = OscReceiverPlayable.Create(_graph, _buffer, mappings, 3);
            var behaviour = playable.GetBehaviour();

            Assert.AreEqual(3, behaviour.OutputWeights.Length);
            for (int i = 0; i < behaviour.OutputWeights.Length; i++)
            {
                Assert.AreEqual(0f, behaviour.OutputWeights[i]);
            }
        }

        // ================================================================
        // ReadFromBuffer — ダブルバッファからの値読み取り
        // ================================================================

        [Test]
        public void ReadFromBuffer_SingleValue_WritesToCorrectOutputIndex()
        {
            _buffer = new OscDoubleBuffer(3);
            var mappings = new[]
            {
                new OscMapping("/avatar/parameters/Joy", "Joy", "emotion"),
                new OscMapping("/avatar/parameters/Angry", "Angry", "emotion"),
                new OscMapping("/avatar/parameters/Sad", "Sad", "emotion")
            };
            // blendShapeNames: ["Joy", "Angry", "Sad"] (same order as mappings)

            var playable = OscReceiverPlayable.Create(_graph, _buffer, mappings, 3);
            var behaviour = playable.GetBehaviour();

            // OSC バッファのインデックス 0 に値を書き込み
            _buffer.Write(0, 0.75f);
            _buffer.Swap();

            behaviour.ReadFromBuffer();

            Assert.AreEqual(0.75f, behaviour.OutputWeights[0], 0.0001f);
        }

        [Test]
        public void ReadFromBuffer_MultipleValues_AllMapped()
        {
            _buffer = new OscDoubleBuffer(3);
            var mappings = new[]
            {
                new OscMapping("/avatar/parameters/Joy", "Joy", "emotion"),
                new OscMapping("/avatar/parameters/Angry", "Angry", "emotion"),
                new OscMapping("/avatar/parameters/Sad", "Sad", "emotion")
            };

            var playable = OscReceiverPlayable.Create(_graph, _buffer, mappings, 3);
            var behaviour = playable.GetBehaviour();

            _buffer.Write(0, 0.1f);
            _buffer.Write(1, 0.5f);
            _buffer.Write(2, 0.9f);
            _buffer.Swap();

            behaviour.ReadFromBuffer();

            Assert.AreEqual(0.1f, behaviour.OutputWeights[0], 0.0001f);
            Assert.AreEqual(0.5f, behaviour.OutputWeights[1], 0.0001f);
            Assert.AreEqual(0.9f, behaviour.OutputWeights[2], 0.0001f);
        }

        [Test]
        public void ReadFromBuffer_BufferLargerThanBlendShapeCount_OnlyReadsUpToCount()
        {
            // バッファは 5 要素だが BlendShape は 3 つ、マッピングも 3 つ
            _buffer = new OscDoubleBuffer(5);
            var mappings = new[]
            {
                new OscMapping("/avatar/parameters/Joy", "Joy", "emotion"),
                new OscMapping("/avatar/parameters/Angry", "Angry", "emotion"),
                new OscMapping("/avatar/parameters/Sad", "Sad", "emotion")
            };

            var playable = OscReceiverPlayable.Create(_graph, _buffer, mappings, 3);
            var behaviour = playable.GetBehaviour();

            _buffer.Write(0, 0.1f);
            _buffer.Write(1, 0.2f);
            _buffer.Write(2, 0.3f);
            _buffer.Write(3, 0.4f); // マッピング外
            _buffer.Write(4, 0.5f); // マッピング外
            _buffer.Swap();

            behaviour.ReadFromBuffer();

            Assert.AreEqual(3, behaviour.OutputWeights.Length);
            Assert.AreEqual(0.1f, behaviour.OutputWeights[0], 0.0001f);
            Assert.AreEqual(0.2f, behaviour.OutputWeights[1], 0.0001f);
            Assert.AreEqual(0.3f, behaviour.OutputWeights[2], 0.0001f);
        }

        [Test]
        public void ReadFromBuffer_NoDataInBuffer_OutputRemainsZero()
        {
            _buffer = new OscDoubleBuffer(2);
            var mappings = CreateTestMappings(2);

            var playable = OscReceiverPlayable.Create(_graph, _buffer, mappings, 2);
            var behaviour = playable.GetBehaviour();

            // 何も書き込まずにスワップ
            _buffer.Swap();
            behaviour.ReadFromBuffer();

            Assert.AreEqual(0f, behaviour.OutputWeights[0]);
            Assert.AreEqual(0f, behaviour.OutputWeights[1]);
        }

        [Test]
        public void ReadFromBuffer_CalledTwice_UpdatesWithLatestBuffer()
        {
            _buffer = new OscDoubleBuffer(1);
            var mappings = CreateTestMappings(1);

            var playable = OscReceiverPlayable.Create(_graph, _buffer, mappings, 1);
            var behaviour = playable.GetBehaviour();

            // フレーム 1
            _buffer.Write(0, 0.3f);
            _buffer.Swap();
            behaviour.ReadFromBuffer();
            Assert.AreEqual(0.3f, behaviour.OutputWeights[0], 0.0001f);

            // フレーム 2
            _buffer.Write(0, 0.8f);
            _buffer.Swap();
            behaviour.ReadFromBuffer();
            Assert.AreEqual(0.8f, behaviour.OutputWeights[0], 0.0001f);
        }

        [Test]
        public void ReadFromBuffer_AfterSwapWithNoWrite_OutputRetainsPreviousValues()
        {
            _buffer = new OscDoubleBuffer(1);
            var mappings = CreateTestMappings(1);

            var playable = OscReceiverPlayable.Create(_graph, _buffer, mappings, 1);
            var behaviour = playable.GetBehaviour();

            // フレーム 1: 値を書き込み
            _buffer.Write(0, 0.5f);
            _buffer.Swap();
            behaviour.ReadFromBuffer();
            Assert.AreEqual(0.5f, behaviour.OutputWeights[0], 0.0001f);

            // フレーム 2: 書き込みなし。copy-forward により前フレームの値を保持する
            // （受信の無い tick で 0 に落ちて表情が一瞬素に戻る不具合の回帰防止）。
            _buffer.Swap();
            behaviour.ReadFromBuffer();
            Assert.AreEqual(0.5f, behaviour.OutputWeights[0], 0.0001f);
        }

        [Test]
        public void ReadFromBuffer_ZeroBlendShapeCount_DoesNotThrow()
        {
            _buffer = new OscDoubleBuffer(0);
            var mappings = Array.Empty<OscMapping>();

            var playable = OscReceiverPlayable.Create(_graph, _buffer, mappings, 0);
            var behaviour = playable.GetBehaviour();

            Assert.DoesNotThrow(() => behaviour.ReadFromBuffer());
        }

        // ================================================================
        // ReadFromBuffer — 値クランプ
        // ================================================================

        [Test]
        public void ReadFromBuffer_ValueAboveOne_ClampedToOne()
        {
            _buffer = new OscDoubleBuffer(1);
            var mappings = CreateTestMappings(1);

            var playable = OscReceiverPlayable.Create(_graph, _buffer, mappings, 1);
            var behaviour = playable.GetBehaviour();

            _buffer.Write(0, 1.5f);
            _buffer.Swap();
            behaviour.ReadFromBuffer();

            Assert.AreEqual(1f, behaviour.OutputWeights[0], 0.0001f);
        }

        [Test]
        public void ReadFromBuffer_ValueBelowZero_ClampedToZero()
        {
            _buffer = new OscDoubleBuffer(1);
            var mappings = CreateTestMappings(1);

            var playable = OscReceiverPlayable.Create(_graph, _buffer, mappings, 1);
            var behaviour = playable.GetBehaviour();

            _buffer.Write(0, -0.5f);
            _buffer.Swap();
            behaviour.ReadFromBuffer();

            Assert.AreEqual(0f, behaviour.OutputWeights[0], 0.0001f);
        }

        // ================================================================
        // Dispose — リソース解放
        // ================================================================

        [Test]
        public void Dispose_ReleasesNativeArray()
        {
            _buffer = new OscDoubleBuffer(3);
            var mappings = CreateTestMappings(3);

            var playable = OscReceiverPlayable.Create(_graph, _buffer, mappings, 3);
            var behaviour = playable.GetBehaviour();

            behaviour.Dispose();

            Assert.IsFalse(behaviour.OutputWeights.IsCreated);
        }

        [Test]
        public void Dispose_CalledTwice_DoesNotThrow()
        {
            _buffer = new OscDoubleBuffer(3);
            var mappings = CreateTestMappings(3);

            var playable = OscReceiverPlayable.Create(_graph, _buffer, mappings, 3);
            var behaviour = playable.GetBehaviour();

            behaviour.Dispose();

            Assert.DoesNotThrow(() => behaviour.Dispose());
        }

        // ================================================================
        // ヘルパーメソッド
        // ================================================================

        private static OscMapping[] CreateTestMappings(int count)
        {
            var mappings = new OscMapping[count];
            for (int i = 0; i < count; i++)
            {
                mappings[i] = new OscMapping(
                    $"/avatar/parameters/BlendShape{i}",
                    $"BlendShape{i}",
                    "emotion");
            }
            return mappings;
        }
    }
}
