using System.Collections;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Hidano.FacialControl.Adapters.IFacialMocap;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.IFacialMocap.Tests.PlayMode
{
    /// <summary>
    /// 実 UDP loopback で <see cref="IFacialMocapReceiverHost"/> が受信→パース→最新フレーム保持を
    /// 行うことを検証する PlayMode テスト。
    /// </summary>
    [MediumTest]
    public class IFacialMocapReceiverHostTests : SizedTestFixture
    {
        private static int s_port = 19330;

        private GameObject _go;
        private IFacialMocapReceiverHost _host;
        private UdpClient _sender;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("IFacialMocapReceiverHostTests");
        }

        [TearDown]
        public void TearDown()
        {
            if (_host != null)
            {
                _host.Stop();
            }

            if (_sender != null)
            {
                _sender.Dispose();
                _sender = null;
            }

            if (_go != null)
            {
                Object.DestroyImmediate(_go);
                _go = null;
            }
        }

        [UnityTest]
        public IEnumerator Loopback_ReceivesAndParsesFrame()
        {
            int port = ++s_port;
            _host = _go.AddComponent<IFacialMocapReceiverHost>();
            _host.Configure(port, null, false, IFacialMocapDataVersion.Standard, 1f);

            yield return new WaitForSeconds(0.2f);

            _sender = new UdpClient();
            byte[] data = Encoding.ASCII.GetBytes(
                "jawOpen-80|mouthSmile_L-40|=head#1,2,3,0,0,0|rightEye#5,6,7|leftEye#8,9,10|");
            var endpoint = new IPEndPoint(IPAddress.Loopback, port);

            var frame = new IFacialMocapFrame();
            bool received = false;
            for (int attempt = 0; attempt < 20 && !received; attempt++)
            {
                _sender.Send(data, data.Length, endpoint);
                yield return new WaitForSeconds(0.05f);
                int sequence = _host.TryReadLatest(frame);
                if (sequence != 0 && frame.BlendShapes.Count > 0)
                {
                    received = true;
                }
            }

            Assert.That(received, Is.True, "loopback で送ったパケットを受信できるべき。");
            Assert.That(frame.Head.HasValue, Is.True);
            Assert.That(frame.RightEye.HasValue, Is.True);

            float jaw = float.NaN;
            for (int i = 0; i < frame.BlendShapes.Count; i++)
            {
                if (frame.BlendShapes[i].Name == "jawOpen")
                {
                    jaw = frame.BlendShapes[i].Value;
                }
            }

            Assert.That(jaw, Is.EqualTo(80f).Within(0.01f));
        }
    }
}
