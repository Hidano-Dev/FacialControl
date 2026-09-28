using Hidano.FacialControl.LipSync.Adapters;
using NUnit.Framework;
using UnityEngine;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.LipSync.Tests.EditMode.Adapters
{
    [TestFixture]
    [MediumTest]
    public class ULipSyncEventBridgeTests : SizedTestFixture
    {
        [Test]
        public void Constructor_NullSource_ThrowsArgumentNullException()
        {
            Assert.That(() => new ULipSyncEventBridge(null), Throws.ArgumentNullException);
        }

        [Test]
        public void OnLipSyncUpdate_UnityEventInvoked_ForwardsToSubscriber()
        {
            var gameObject = new GameObject("ULipSyncEventBridgeTests");

            try
            {
                var source = gameObject.AddComponent<uLipSync.uLipSync>();
                using var bridge = new ULipSyncEventBridge(source);
                var info = new uLipSync.LipSyncInfo
                {
                    phoneme = "A",
                    volume = 0.5f,
                    rawVolume = 0.25f,
                };
                var callCount = 0;
                var received = default(uLipSync.LipSyncInfo);

                bridge.OnLipSyncUpdate += receivedInfo =>
                {
                    received = receivedInfo;
                    callCount++;
                };

                source.onLipSyncUpdate.Invoke(info);

                Assert.That(callCount, Is.EqualTo(1));
                Assert.That(received.phoneme, Is.EqualTo(info.phoneme));
                Assert.That(received.volume, Is.EqualTo(info.volume));
                Assert.That(received.rawVolume, Is.EqualTo(info.rawVolume));
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void Dispose_AfterUnityEventSubscription_RemovesUnityEventListener()
        {
            var gameObject = new GameObject("ULipSyncEventBridgeTests");

            try
            {
                var source = gameObject.AddComponent<uLipSync.uLipSync>();
                var bridge = new ULipSyncEventBridge(source);
                var callCount = 0;

                bridge.OnLipSyncUpdate += _ => callCount++;
                bridge.Dispose();

                source.onLipSyncUpdate.Invoke(new uLipSync.LipSyncInfo());

                Assert.That(callCount, Is.EqualTo(0));
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }
    }
}
