using System;
using NUnit.Framework;
using UnityEngine;
using Hidano.FacialControl.Adapters.OSC;
using Hidano.FacialControl.Domain.Models;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.PlayMode.Adapters.OSC
{
    /// <summary>
    /// <see cref="OscReceiver.RegisterAnalogListener"/> /
    /// <see cref="OscReceiver.UnregisterAnalogListener"/> の追加 API テスト。
    /// </summary>
    [TestFixture]
    [MediumTest]
    public class OscReceiverAnalogListenerTests : SizedTestFixture
    {
        private GameObject _receiverObj;
        private OscReceiver _receiver;
        private OscDoubleBuffer _buffer;

        [SetUp]
        public void SetUp()
        {
            _receiverObj = new GameObject("OscReceiverAnalogListenerTest");
            _receiver = _receiverObj.AddComponent<OscReceiver>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_buffer != null)
            {
                _buffer.Dispose();
                _buffer = null;
            }
            if (_receiverObj != null)
            {
                UnityEngine.Object.DestroyImmediate(_receiverObj);
                _receiverObj = null;
            }
        }

        private void InitializeReceiverWithEmptyMappings()
        {
            _buffer = new OscDoubleBuffer(0);
            _receiver.Initialize(_buffer, Array.Empty<OscMapping>());
        }

        // ============================================================
        // 引数バリデーション
        // ============================================================

        [Test]
        public void RegisterAnalogListener_NullAddress_ThrowsArgumentException()
        {
            InitializeReceiverWithEmptyMappings();

            Assert.Throws<ArgumentException>(() =>
                _receiver.RegisterAnalogListener(null, _ => { }));
        }

        [Test]
        public void RegisterAnalogListener_EmptyAddress_ThrowsArgumentException()
        {
            InitializeReceiverWithEmptyMappings();

            Assert.Throws<ArgumentException>(() =>
                _receiver.RegisterAnalogListener(string.Empty, _ => { }));
        }

        [Test]
        public void RegisterAnalogListener_NullListener_ThrowsArgumentNullException()
        {
            InitializeReceiverWithEmptyMappings();

            Assert.Throws<ArgumentNullException>(() =>
                _receiver.RegisterAnalogListener("/avatar/parameters/jawOpen", null));
        }

        [Test]
        public void UnregisterAnalogListener_NullArgs_DoesNotThrow()
        {
            InitializeReceiverWithEmptyMappings();

            Assert.DoesNotThrow(() => _receiver.UnregisterAnalogListener(null, _ => { }));
            Assert.DoesNotThrow(() => _receiver.UnregisterAnalogListener("/x", null));
            Assert.DoesNotThrow(() => _receiver.UnregisterAnalogListener(null, null));
        }

        [Test]
        public void UnregisterAnalogListener_UnregisteredListener_DoesNotThrow()
        {
            InitializeReceiverWithEmptyMappings();

            Action<float> listener = _ => { };
            Assert.DoesNotThrow(() =>
                _receiver.UnregisterAnalogListener("/avatar/parameters/jawOpen", listener));
        }

        // ============================================================
        // 通知動作
        // ============================================================

        [Test]
        public void RegisterAnalogListener_AfterReceive_InvokesListener()
        {
            InitializeReceiverWithEmptyMappings();

            float captured = 0f;
            int invokeCount = 0;
            _receiver.RegisterAnalogListener("/avatar/parameters/jawOpen", v =>
            {
                captured = v;
                invokeCount++;
            });

            _receiver.HandleOscMessage(new uOSC.Message("/avatar/parameters/jawOpen", 0.75f));

            Assert.AreEqual(1, invokeCount);
            Assert.AreEqual(0.75f, captured, 0.0001f);
        }

        [Test]
        public void RegisterAnalogListener_NonMatchingAddress_DoesNotInvokeListener()
        {
            InitializeReceiverWithEmptyMappings();

            int invokeCount = 0;
            _receiver.RegisterAnalogListener("/avatar/parameters/jawOpen", _ => invokeCount++);

            _receiver.HandleOscMessage(new uOSC.Message("/avatar/parameters/eyeBlinkLeft", 0.5f));

            Assert.AreEqual(0, invokeCount);
        }

        [Test]
        public void RegisterAnalogListener_MultipleListenersSameAddress_AllInvoked()
        {
            InitializeReceiverWithEmptyMappings();

            int countA = 0;
            int countB = 0;
            _receiver.RegisterAnalogListener("/x", _ => countA++);
            _receiver.RegisterAnalogListener("/x", _ => countB++);

            _receiver.HandleOscMessage(new uOSC.Message("/x", 1f));

            Assert.AreEqual(1, countA);
            Assert.AreEqual(1, countB);
        }

        [Test]
        public void UnregisterAnalogListener_AfterUnregister_NotInvoked()
        {
            InitializeReceiverWithEmptyMappings();

            int invokeCount = 0;
            Action<float> listener = _ => invokeCount++;
            _receiver.RegisterAnalogListener("/x", listener);

            _receiver.UnregisterAnalogListener("/x", listener);
            _receiver.HandleOscMessage(new uOSC.Message("/x", 0.5f));

            Assert.AreEqual(0, invokeCount);
        }

        [Test]
        public void UnregisterAnalogListener_OnlyOneOfMultiple_OthersStillInvoked()
        {
            InitializeReceiverWithEmptyMappings();

            int countA = 0;
            int countB = 0;
            Action<float> listenerA = _ => countA++;
            Action<float> listenerB = _ => countB++;
            _receiver.RegisterAnalogListener("/x", listenerA);
            _receiver.RegisterAnalogListener("/x", listenerB);

            _receiver.UnregisterAnalogListener("/x", listenerA);
            _receiver.HandleOscMessage(new uOSC.Message("/x", 0.5f));

            Assert.AreEqual(0, countA);
            Assert.AreEqual(1, countB);
        }

        [Test]
        public void HandleOscMessage_IntValue_ListenerReceivesAsFloat()
        {
            InitializeReceiverWithEmptyMappings();

            float captured = 0f;
            _receiver.RegisterAnalogListener("/x", v => captured = v);

            _receiver.HandleOscMessage(new uOSC.Message("/x", 7));

            Assert.AreEqual(7f, captured, 0.0001f);
        }

        // ============================================================
        // 既存ルーティングとの非破壊
        // ============================================================

        [Test]
        public void HandleOscMessage_ExistingMappingPreserved_BufferStillWritten()
        {
            // 既存の _addressToIndex ルーティングが壊れていないことを保証 。
            _buffer = new OscDoubleBuffer(1);
            var mappings = new[]
            {
                new OscMapping("/avatar/parameters/Joy", "Joy", "emotion")
            };
            _receiver.Initialize(_buffer, mappings);

            int analogInvokeCount = 0;
            _receiver.RegisterAnalogListener("/avatar/parameters/Joy", _ => analogInvokeCount++);

            _receiver.HandleOscMessage(new uOSC.Message("/avatar/parameters/Joy", 0.6f));
            _buffer.Swap();

            Assert.AreEqual(0.6f, _buffer.GetReadBuffer()[0], 0.0001f, "既存 BlendShape index ルーティングが保持されていない。");
            Assert.AreEqual(1, analogInvokeCount, "アドレス一致時に analog listener も発火する必要がある。");
        }

        [Test]
        public void HandleOscMessage_ListenerOnlyAddress_BufferUntouched()
        {
            _buffer = new OscDoubleBuffer(1);
            var mappings = new[]
            {
                new OscMapping("/avatar/parameters/Joy", "Joy", "emotion")
            };
            _receiver.Initialize(_buffer, mappings);

            int analogInvokeCount = 0;
            _receiver.RegisterAnalogListener("/avatar/parameters/jawOpen", _ => analogInvokeCount++);

            _receiver.HandleOscMessage(new uOSC.Message("/avatar/parameters/jawOpen", 0.9f));
            _buffer.Swap();

            Assert.AreEqual(0f, _buffer.GetReadBuffer()[0], 0.0001f, "未マップアドレスはバッファに書込まれない。");
            Assert.AreEqual(1, analogInvokeCount);
        }

        // ============================================================
        // 例外伝搬抑制
        // ============================================================

        [Test]
        public void HandleOscMessage_ListenerThrows_DoesNotPropagate()
        {
            InitializeReceiverWithEmptyMappings();

            _receiver.RegisterAnalogListener("/x", _ => throw new InvalidOperationException("test"));

            // 例外を握り潰して LogException を発出する設計
            UnityEngine.TestTools.LogAssert.Expect(LogType.Exception, "InvalidOperationException: test");
            Assert.DoesNotThrow(() => _receiver.HandleOscMessage(new uOSC.Message("/x", 1f)));
        }
    }
}
