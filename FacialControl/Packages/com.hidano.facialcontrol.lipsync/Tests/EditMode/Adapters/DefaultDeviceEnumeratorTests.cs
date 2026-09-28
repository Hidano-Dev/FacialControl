using System;
using Hidano.FacialControl.LipSync.Adapters.Devices;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.LipSync.Tests.EditMode.Adapters
{
    [TestFixture]
    [SmallTest]
    public class DefaultDeviceEnumeratorTests : SizedTestFixture
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        [Test]
        public void GetDriverNames_ProviderReturnsNames_ReturnsProviderNames()
        {
            var enumerator = new DefaultAsioDriverEnumerator(() => new[] { "ASIO Fireface" });

            var names = enumerator.GetDriverNames();

            CollectionAssert.AreEqual(new[] { "ASIO Fireface" }, names);
        }

        [Test]
        public void GetDriverNames_ProviderReturnsNull_ReturnsEmptyArray()
        {
            var enumerator = new DefaultAsioDriverEnumerator(() => null);

            var names = enumerator.GetDriverNames();

            CollectionAssert.IsEmpty(names);
        }

        [Test]
        public void GetDriverNames_ProviderThrows_ReturnsEmptyArray()
        {
            var enumerator = new DefaultAsioDriverEnumerator(
                () => throw new InvalidOperationException("asio enumeration failed"));
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("Failed to enumerate ASIO drivers"));

            var names = enumerator.GetDriverNames();

            CollectionAssert.IsEmpty(names);
        }
#else
        [Test]
        public void GetDriverNames_NonWindows_ReturnsEmptyArray()
        {
            var enumerator = new DefaultAsioDriverEnumerator();

            var names = enumerator.GetDriverNames();

            CollectionAssert.IsEmpty(names);
        }
#endif

        [Test]
        public void GetDeviceNames_DefaultEnumerator_ReturnsStringArray()
        {
            var enumerator = new DefaultMicrophoneDeviceEnumerator();

            var names = enumerator.GetDeviceNames();

            Assert.That(names, Is.Not.Null);
        }
    }
}
