using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Hidano.FacialControl.Adapters.OSC;
using Hidano.FacialControl.Adapters.Playable;
using Hidano.FacialControl.Domain.Models;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.PlayMode.Integration
{
    /// <summary>
    /// P14-T03: OSC 送受信の統合テスト。
    /// FacialController + OscReceiver / OscSender の連携動作を検証する。
    /// </summary>
    [TestFixture]
    [MediumTest]
    public class OscIntegrationTests : SizedTestFixture
    {
        private GameObject _controllerObj;
        private GameObject _receiverObj;
        private GameObject _senderObj;
        private OscDoubleBuffer _buffer;

        [TearDown]
        public void TearDown()
        {
            if (_buffer != null)
            {
                _buffer.Dispose();
                _buffer = null;
            }
            if (_senderObj != null)
            {
                Object.DestroyImmediate(_senderObj);
                _senderObj = null;
            }
            if (_receiverObj != null)
            {
                Object.DestroyImmediate(_receiverObj);
                _receiverObj = null;
            }
            if (_controllerObj != null)
            {
                Object.DestroyImmediate(_controllerObj);
                _controllerObj = null;
            }
        }

        // ================================================================
        // OscReceiver + FacialController 統合テスト
        // ================================================================

        [Test]
        public void OscReceiver_HandleMessage_WritesToDoubleBuffer()
        {
            var mappings = CreateTestMappings();
            _buffer = new OscDoubleBuffer(mappings.Length);

            _receiverObj = new GameObject("OscReceiverIntegrationTest");
            var receiver = _receiverObj.AddComponent<OscReceiver>();
            receiver.Initialize(_buffer, mappings);

            // OSC メッセージを手動で処理
            receiver.HandleOscMessage(new uOSC.Message("/avatar/parameters/smile", 0.8f));
            receiver.HandleOscMessage(new uOSC.Message("/avatar/parameters/frown", 0.4f));

            _buffer.Swap();
            var readBuf = _buffer.GetReadBuffer();

            Assert.AreEqual(0.8f, readBuf[0], 0.001f);
            Assert.AreEqual(0.4f, readBuf[1], 0.001f);
        }

        [Test]
        public void OscReceiver_VRChatAndARKitFormats_BothProcessed()
        {
            var mappings = new OscMapping[]
            {
                new OscMapping("/avatar/parameters/smile", "smile", "emotion"),
                new OscMapping("/ARKit/eyeBlinkLeft", "eyeBlinkLeft", "eye")
            };
            _buffer = new OscDoubleBuffer(mappings.Length);

            _receiverObj = new GameObject("OscReceiverIntegrationTest");
            var receiver = _receiverObj.AddComponent<OscReceiver>();
            receiver.Initialize(_buffer, mappings);

            // VRChat 形式
            receiver.HandleOscMessage(new uOSC.Message("/avatar/parameters/smile", 0.9f));
            // ARKit 形式 — アドレス完全一致
            receiver.HandleOscMessage(new uOSC.Message("/ARKit/eyeBlinkLeft", 0.7f));

            _buffer.Swap();
            var readBuf = _buffer.GetReadBuffer();

            Assert.AreEqual(0.9f, readBuf[0], 0.001f);
            Assert.AreEqual(0.7f, readBuf[1], 0.001f);
        }

        [Test]
        public void OscReceiver_MultipleMessagesPerFrame_AllCaptured()
        {
            var mappings = new OscMapping[]
            {
                new OscMapping("/avatar/parameters/a", "a", "emotion"),
                new OscMapping("/avatar/parameters/b", "b", "emotion"),
                new OscMapping("/avatar/parameters/c", "c", "emotion")
            };
            _buffer = new OscDoubleBuffer(mappings.Length);

            _receiverObj = new GameObject("OscReceiverIntegrationTest");
            var receiver = _receiverObj.AddComponent<OscReceiver>();
            receiver.Initialize(_buffer, mappings);

            // 1 フレーム間に複数回送受信
            receiver.HandleOscMessage(new uOSC.Message("/avatar/parameters/a", 0.1f));
            receiver.HandleOscMessage(new uOSC.Message("/avatar/parameters/b", 0.2f));
            receiver.HandleOscMessage(new uOSC.Message("/avatar/parameters/c", 0.3f));

            _buffer.Swap();
            var readBuf = _buffer.GetReadBuffer();

            Assert.AreEqual(0.1f, readBuf[0], 0.001f);
            Assert.AreEqual(0.2f, readBuf[1], 0.001f);
            Assert.AreEqual(0.3f, readBuf[2], 0.001f);
        }

        [Test]
        public void OscReceiver_DoubleBuffering_PreviousFrameRetained()
        {
            var mappings = new OscMapping[]
            {
                new OscMapping("/avatar/parameters/smile", "smile", "emotion")
            };
            _buffer = new OscDoubleBuffer(mappings.Length);

            _receiverObj = new GameObject("OscReceiverIntegrationTest");
            var receiver = _receiverObj.AddComponent<OscReceiver>();
            receiver.Initialize(_buffer, mappings);

            // フレーム 1: 値書き込み
            receiver.HandleOscMessage(new uOSC.Message("/avatar/parameters/smile", 0.5f));
            _buffer.Swap();
            Assert.AreEqual(0.5f, _buffer.GetReadBuffer()[0], 0.001f);

            // フレーム 2: 何も書き込まず。copy-forward により前フレーム値を保持する
            // （受信の無い tick で表情が一瞬素に戻る不具合の回帰防止）。
            _buffer.Swap();
            Assert.AreEqual(0.5f, _buffer.GetReadBuffer()[0], 0.001f);
        }

        // ================================================================
        // OscSender 統合テスト
        // ================================================================

        [Test]
        public void OscSender_InitializeWithMappings_SetsUpCorrectly()
        {
            _senderObj = new GameObject("OscSenderIntegrationTest");
            var sender = _senderObj.AddComponent<OscSender>();

            var mappings = CreateTestMappings();
            sender.Initialize(mappings);

            Assert.IsTrue(sender.IsInitialized);
            Assert.AreEqual(mappings.Length, sender.MappingCount);
        }

        [Test]
        public void OscSender_InitializeWithConfig_SetsPortAndMappings()
        {
            _senderObj = new GameObject("OscSenderIntegrationTest");
            var sender = _senderObj.AddComponent<OscSender>();

            var config = new OscConfiguration(
                sendPort: 9999,
                receivePort: 9001,
                preset: "vrchat",
                mapping: CreateTestMappings());
            sender.Initialize(config);

            Assert.AreEqual(9999, sender.Port);
            Assert.IsTrue(sender.IsInitialized);
        }

        // ================================================================
        // OscSender + OscReceiver ループバック統合テスト
        // ================================================================

        [UnityTest]
        public IEnumerator OscLoopback_SenderToReceiver_ValuesTransferred()
        {
            var mappings = new OscMapping[]
            {
                new OscMapping("/avatar/parameters/smile", "smile", "emotion"),
                new OscMapping("/avatar/parameters/frown", "frown", "emotion")
            };

            // Receiver セットアップ
            _buffer = new OscDoubleBuffer(mappings.Length);
            _receiverObj = new GameObject("OscReceiverLoop");
            var receiver = _receiverObj.AddComponent<OscReceiver>();
            receiver.Port = 19010; // テスト用ポート
            receiver.Initialize(_buffer, mappings);
            receiver.StartReceiving();

            yield return new WaitForSeconds(0.2f);

            // Sender セットアップ
            _senderObj = new GameObject("OscSenderLoop");
            var sender = _senderObj.AddComponent<OscSender>();
            sender.Endpoint = "127.0.0.1";
            sender.Port = 19010;
            sender.Initialize(mappings);
            sender.StartSending();

            yield return new WaitForSeconds(0.2f);

            // 送信 → 受信を確認
            bool received = false;
            for (int attempt = 0; attempt < 10; attempt++)
            {
                sender.SendAll(new float[] { 0.7f, 0.3f });
                yield return new WaitForSeconds(0.1f);

                _buffer.Swap();
                var readBuf = _buffer.GetReadBuffer();
                if (readBuf[0] > 0.01f)
                {
                    received = true;
                    Assert.AreEqual(0.7f, readBuf[0], 0.05f);
                    Assert.AreEqual(0.3f, readBuf[1], 0.05f);
                    break;
                }
            }

            Assert.IsTrue(received, "OSC ループバック送受信が成功すること");

            sender.StopSending();
            receiver.StopReceiving();
            yield return null;
        }

        // ================================================================
        // OscConfiguration 統合テスト
        // ================================================================

        [Test]
        public void OscReceiver_InitializeWithConfig_SetsPort()
        {
            var config = new OscConfiguration(
                sendPort: 9000,
                receivePort: 12345,
                preset: "vrchat",
                mapping: CreateTestMappings());

            _buffer = new OscDoubleBuffer(config.Mapping.Length);
            _receiverObj = new GameObject("OscReceiverConfigTest");
            var receiver = _receiverObj.AddComponent<OscReceiver>();
            receiver.Initialize(_buffer, config);

            Assert.AreEqual(12345, receiver.Port);
        }

        [Test]
        public void OscMappingIntegration_UnmappedAddress_Ignored()
        {
            var mappings = new OscMapping[]
            {
                new OscMapping("/avatar/parameters/smile", "smile", "emotion")
            };
            _buffer = new OscDoubleBuffer(mappings.Length);

            _receiverObj = new GameObject("OscReceiverIntegrationTest");
            var receiver = _receiverObj.AddComponent<OscReceiver>();
            receiver.Initialize(_buffer, mappings);

            // マッピングにないアドレス
            receiver.HandleOscMessage(new uOSC.Message("/avatar/parameters/unknown", 0.9f));
            _buffer.Swap();

            Assert.AreEqual(0f, _buffer.GetReadBuffer()[0], 0.001f);
        }

        // ================================================================
        // FacialController + OSC 統合シナリオ
        // ================================================================

        [UnityTest]
        public IEnumerator FacialControllerWithOsc_InitializeAndReceive_NoErrors()
        {
            // FacialController のセットアップ
            _controllerObj = new GameObject("FacialControllerOscTest");
            _controllerObj.AddComponent<Animator>();
            var childObj = new GameObject("Mesh");
            childObj.transform.SetParent(_controllerObj.transform);
            childObj.AddComponent<SkinnedMeshRenderer>();

            var controller = _controllerObj.AddComponent<FacialController>();
            var profile = CreateTestProfile();
            controller.InitializeWithProfile(profile);

            yield return null;

            Assert.IsTrue(controller.IsInitialized);

            // OSC レシーバーを同じオブジェクトに追加
            var mappings = CreateTestMappings();
            _buffer = new OscDoubleBuffer(mappings.Length);

            _receiverObj = new GameObject("OscReceiverForController");
            var receiver = _receiverObj.AddComponent<OscReceiver>();
            receiver.Initialize(_buffer, mappings);

            // メッセージを処理
            receiver.HandleOscMessage(new uOSC.Message("/avatar/parameters/smile", 0.5f));
            _buffer.Swap();

            var readBuf = _buffer.GetReadBuffer();
            Assert.AreEqual(0.5f, readBuf[0], 0.001f);

            // FacialController も依然として正常動作
            var expression = new Expression("test-osc", "OscTest", "emotion", 0.25f,
                TransitionCurve.Linear,
                new BlendShapeMapping[] { new BlendShapeMapping("smile", 1.0f) });
            controller.Activate(expression);

            var active = controller.GetActiveExpressions();
            Assert.AreEqual(1, active.Count);

            yield return null;
        }

        // ================================================================
        // ヘルパー
        // ================================================================

        private static OscMapping[] CreateTestMappings()
        {
            return new OscMapping[]
            {
                new OscMapping("/avatar/parameters/smile", "smile", "emotion"),
                new OscMapping("/avatar/parameters/frown", "frown", "emotion")
            };
        }

        private static FacialProfile CreateTestProfile()
        {
            var layers = new LayerDefinition[]
            {
                new LayerDefinition("emotion", 0, ExclusionMode.LastWins),
                new LayerDefinition("eye", 1, ExclusionMode.LastWins)
            };
            return new FacialProfile("1.0.0", layers);
        }
    }
}
