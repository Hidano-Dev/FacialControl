using Hidano.FacialControl.LipSync.Adapters.Devices;
using NUnit.Framework;
using UnityEngine;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.LipSync.Tests.EditMode.Adapters
{
    [TestFixture]
    [MediumTest]
    public class DefaultPlayerPrefsBackendTests : SizedTestFixture
    {
        private const string TestStringKey = "Hidano.FacialControl.Tests.DefaultPlayerPrefsBackend.String";
        private const string TestIntKey = "Hidano.FacialControl.Tests.DefaultPlayerPrefsBackend.Int";

        [SetUp]
        public void SetUp()
        {
            PlayerPrefs.DeleteKey(TestStringKey);
            PlayerPrefs.DeleteKey(TestIntKey);
        }

        [TearDown]
        public void TearDown()
        {
            PlayerPrefs.DeleteKey(TestStringKey);
            PlayerPrefs.DeleteKey(TestIntKey);
            PlayerPrefs.Save();
        }

        [Test]
        public void SetString_ValueWritten_ProxiesToPlayerPrefs()
        {
            var backend = new DefaultPlayerPrefsBackend();

            backend.SetString(TestStringKey, "proxied-value");

            Assert.That(PlayerPrefs.GetString(TestStringKey, "missing"), Is.EqualTo("proxied-value"));
        }

        [Test]
        public void GetString_ValueWrittenViaPlayerPrefs_ProxiesFromPlayerPrefs()
        {
            PlayerPrefs.SetString(TestStringKey, "direct-value");
            var backend = new DefaultPlayerPrefsBackend();

            var value = backend.GetString(TestStringKey, "missing");

            Assert.That(value, Is.EqualTo("direct-value"));
        }

        [Test]
        public void GetString_KeyMissing_ReturnsProvidedDefault()
        {
            var backend = new DefaultPlayerPrefsBackend();

            var value = backend.GetString(TestStringKey, "fallback");

            Assert.That(value, Is.EqualTo("fallback"));
        }

        [Test]
        public void SetInt_ValueWritten_ProxiesToPlayerPrefs()
        {
            var backend = new DefaultPlayerPrefsBackend();

            backend.SetInt(TestIntKey, 42);

            Assert.That(PlayerPrefs.GetInt(TestIntKey, -1), Is.EqualTo(42));
        }

        [Test]
        public void GetInt_ValueWrittenViaPlayerPrefs_ProxiesFromPlayerPrefs()
        {
            PlayerPrefs.SetInt(TestIntKey, 7);
            var backend = new DefaultPlayerPrefsBackend();

            var value = backend.GetInt(TestIntKey, -1);

            Assert.That(value, Is.EqualTo(7));
        }

        [Test]
        public void GetInt_KeyMissing_ReturnsProvidedDefault()
        {
            var backend = new DefaultPlayerPrefsBackend();

            var value = backend.GetInt(TestIntKey, 99);

            Assert.That(value, Is.EqualTo(99));
        }

        [Test]
        public void Save_AfterSetCalls_DoesNotThrow()
        {
            var backend = new DefaultPlayerPrefsBackend();
            backend.SetString(TestStringKey, "to-persist");
            backend.SetInt(TestIntKey, 1);

            Assert.DoesNotThrow(() => backend.Save());
        }
    }
}
