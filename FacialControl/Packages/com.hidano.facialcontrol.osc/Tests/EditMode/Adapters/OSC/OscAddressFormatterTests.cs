using System;
using System.Collections.Generic;
using System.Text;
using Hidano.FacialControl.Adapters.OSC;
using NUnit.Framework;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.EditMode.Adapters
{
    [TestFixture]
    [SmallTest]
    public class OscAddressFormatterTests : SizedTestFixture
    {
        [Test]
        public void GetOrAddAddressUtf8_AddressWithMultibyteAndSymbols_ReturnsUtf8Bytes()
        {
            const string address = "/manual/笑顔_口.あ";
            var pool = new Dictionary<string, byte[]>();

            byte[] bytes = OscAddressFormatter.GetOrAddAddressUtf8(pool, address);

            Assert.AreEqual(address, Encoding.UTF8.GetString(bytes));
        }

        [Test]
        public void GetOrAddAddressUtf8_SameAddress_ReturnsCachedBytes()
        {
            var pool = new Dictionary<string, byte[]>();

            byte[] first = OscAddressFormatter.GetOrAddAddressUtf8(pool, "/manual/smile");
            byte[] second = OscAddressFormatter.GetOrAddAddressUtf8(pool, "/manual/smile");

            Assert.AreSame(first, second);
            Assert.AreEqual(1, pool.Count);
        }

        [Test]
        public void GetOrAddAddressUtf8_NullPool_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => OscAddressFormatter.GetOrAddAddressUtf8(null, "/manual/smile"));
        }

        [Test]
        public void GetOrAddAddressUtf8_NullAddress_ThrowsArgumentNullException()
        {
            var pool = new Dictionary<string, byte[]>();

            Assert.Throws<ArgumentNullException>(() => OscAddressFormatter.GetOrAddAddressUtf8(pool, null));
        }
    }
}
