using Hidano.FacialControl.LipSync.Adapters.Devices;
using Hidano.FacialControl.Testing;
using NUnit.Framework;

namespace Hidano.FacialControl.LipSync.Tests.EditMode.Adapters
{
    /// <summary>
    /// 本番の <see cref="DefaultMicrophoneDeviceEnumerator"/> は <c>Microphone.devices</c>（実音声デバイス）を列挙するため、
    /// Small（決定的・インメモリ）ではなく Medium の smoke として分ける。
    /// </summary>
    [TestFixture]
    [MediumTest]
    public class DefaultMicrophoneDeviceEnumeratorTests : SizedTestFixture
    {
        [Test]
        public void GetDeviceNames_DefaultEnumerator_ReturnsStringArray()
        {
            var enumerator = new DefaultMicrophoneDeviceEnumerator();

            var names = enumerator.GetDeviceNames();

            Assert.That(names, Is.Not.Null);
        }
    }
}
