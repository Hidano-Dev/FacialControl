using Hidano.FacialControl.LipSync.Adapters.Devices;
using Hidano.FacialControl.LipSync.Tests.Shared;
using NUnit.Framework;
using UnityEngine;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.LipSync.Tests.EditMode.Adapters
{
    [TestFixture]
    [MediumTest]
    internal class LipSyncDeviceStoreTests : SizedTestFixture
    {
        private FakePlayerPrefsBackend _backend;

        [SetUp]
        public void SetUp()
        {
            _backend = new FakePlayerPrefsBackend();
            LipSyncDeviceStore.SetBackend(_backend);
        }

        [TearDown]
        public void TearDown()
        {
            LipSyncDeviceStore.ResetBackend();
            _backend = null;

            PlayerPrefs.DeleteKey(LipSyncDeviceStore.KeyName);
            PlayerPrefs.DeleteKey(LipSyncDeviceStore.KeyDisambiguator);
        }

        [Test]
        public void KeyName_Constant_HasExpectedValue()
        {
            Assert.That(LipSyncDeviceStore.KeyName, Is.EqualTo("Hidano.FacialControl.LipSync.MicDevice.Name"));
        }

        [Test]
        public void KeyDisambiguator_Constant_HasExpectedValue()
        {
            Assert.That(LipSyncDeviceStore.KeyDisambiguator, Is.EqualTo("Hidano.FacialControl.LipSync.MicDevice.Disambiguator"));
        }

        [Test]
        public void Load_KeyMissing_ReturnsDefaultDescriptor()
        {
            var descriptor = LipSyncDeviceStore.Load();

            Assert.That(descriptor.DeviceName, Is.EqualTo(string.Empty));
            Assert.That(descriptor.DisambiguatorIndex, Is.EqualTo(0));
        }

        [Test]
        public void Load_KeyMissing_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => LipSyncDeviceStore.Load());
        }

        [Test]
        public void Save_Load_RoundTrip_PreservesValues()
        {
            var saved = new DeviceDescriptor
            {
                DeviceName = "Microphone-A",
                DisambiguatorIndex = 3,
            };

            LipSyncDeviceStore.Save(saved);
            var loaded = LipSyncDeviceStore.Load();

            Assert.That(loaded.DeviceName, Is.EqualTo("Microphone-A"));
            Assert.That(loaded.DisambiguatorIndex, Is.EqualTo(3));
        }

        [Test]
        public void Save_InvokesBackendSave()
        {
            var descriptor = new DeviceDescriptor
            {
                DeviceName = "Mic-B",
                DisambiguatorIndex = 1,
            };

            LipSyncDeviceStore.Save(descriptor);

            Assert.That(_backend.SaveCallCount, Is.EqualTo(1));
        }

        [Test]
        public void Save_NullDeviceName_NormalizesToEmptyString()
        {
            var descriptor = new DeviceDescriptor
            {
                DeviceName = null,
                DisambiguatorIndex = 2,
            };

            LipSyncDeviceStore.Save(descriptor);
            var loaded = LipSyncDeviceStore.Load();

            Assert.That(loaded.DeviceName, Is.EqualTo(string.Empty));
            Assert.That(loaded.DisambiguatorIndex, Is.EqualTo(2));
        }

        [Test]
        public void Save_NullDeviceName_PersistsEmptyStringToBackend()
        {
            var descriptor = new DeviceDescriptor
            {
                DeviceName = null,
                DisambiguatorIndex = 0,
            };

            LipSyncDeviceStore.Save(descriptor);

            Assert.That(_backend.ContainsStringKey(LipSyncDeviceStore.KeyName), Is.True);
            Assert.That(_backend.GetString(LipSyncDeviceStore.KeyName, "fallback"), Is.EqualTo(string.Empty));
        }

        [Test]
        public void Save_DoesNotWriteToRealPlayerPrefs_WhenBackendIsFake()
        {
            var descriptor = new DeviceDescriptor
            {
                DeviceName = "Should-Not-Persist",
                DisambiguatorIndex = 99,
            };

            LipSyncDeviceStore.Save(descriptor);

            Assert.That(PlayerPrefs.HasKey(LipSyncDeviceStore.KeyName), Is.False);
            Assert.That(PlayerPrefs.HasKey(LipSyncDeviceStore.KeyDisambiguator), Is.False);
        }

        [Test]
        public void SetBackend_ThenLoad_UsesProvidedBackendValues()
        {
            _backend.SetString(LipSyncDeviceStore.KeyName, "From-Fake");
            _backend.SetInt(LipSyncDeviceStore.KeyDisambiguator, 7);

            var loaded = LipSyncDeviceStore.Load();

            Assert.That(loaded.DeviceName, Is.EqualTo("From-Fake"));
            Assert.That(loaded.DisambiguatorIndex, Is.EqualTo(7));
        }

        [Test]
        public void Load_OnlyDeviceNameKeyPresent_ReturnsZeroDisambiguator()
        {
            _backend.SetString(LipSyncDeviceStore.KeyName, "OnlyName");

            var descriptor = LipSyncDeviceStore.Load();

            Assert.That(descriptor.DeviceName, Is.EqualTo("OnlyName"));
            Assert.That(descriptor.DisambiguatorIndex, Is.EqualTo(0));
        }

        [Test]
        public void Load_OnlyDisambiguatorKeyPresent_ReturnsEmptyDeviceName()
        {
            _backend.SetInt(LipSyncDeviceStore.KeyDisambiguator, 11);

            var descriptor = LipSyncDeviceStore.Load();

            Assert.That(descriptor.DeviceName, Is.EqualTo(string.Empty));
            Assert.That(descriptor.DisambiguatorIndex, Is.EqualTo(11));
        }

        [Test]
        public void SetBackend_ReplacesActiveBackend_SaveTargetsNewBackendOnly()
        {
            var replacement = new FakePlayerPrefsBackend();
            LipSyncDeviceStore.SetBackend(replacement);

            LipSyncDeviceStore.Save(new DeviceDescriptor { DeviceName = "After-Replace", DisambiguatorIndex = 6 });

            Assert.That(replacement.ContainsStringKey(LipSyncDeviceStore.KeyName), Is.True);
            Assert.That(replacement.GetString(LipSyncDeviceStore.KeyName, "fallback"), Is.EqualTo("After-Replace"));
            Assert.That(_backend.ContainsStringKey(LipSyncDeviceStore.KeyName), Is.False);
        }

        [Test]
        public void SetBackend_Null_FallsBackToDefaultBackend()
        {
            LipSyncDeviceStore.SetBackend(null);

            Assert.DoesNotThrow(() => LipSyncDeviceStore.Load());
        }

        [Test]
        public void ResetBackend_AfterSetBackend_DetachesFakeBackend()
        {
            _backend.SetString(LipSyncDeviceStore.KeyName, "Fake-Value");
            _backend.SetInt(LipSyncDeviceStore.KeyDisambiguator, 5);

            LipSyncDeviceStore.ResetBackend();
            var loaded = LipSyncDeviceStore.Load();

            Assert.That(loaded.DeviceName, Is.Not.EqualTo("Fake-Value"));
        }
    }
}
