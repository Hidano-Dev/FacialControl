using Hidano.FacialControl.LipSync.Adapters.Devices;
using Hidano.FacialControl.Testing;
using NUnit.Framework;

namespace Hidano.FacialControl.LipSync.Tests.Shared
{
    internal abstract class LipSyncDeviceStoreTestBase : SizedTestFixture
    {
        protected FakePlayerPrefsBackend Backend { get; private set; }

        [SetUp]
        public virtual void SetUp()
        {
            Backend = new FakePlayerPrefsBackend();
            InstallBackend(Backend);
        }

        [TearDown]
        public virtual void TearDown()
        {
            UninstallBackend();
            Backend = null;
        }

        protected virtual void InstallBackend(IPlayerPrefsBackend backend)
        {
        }

        protected virtual void UninstallBackend()
        {
        }
    }
}
