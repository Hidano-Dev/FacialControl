using System;
using System.Collections.Generic;
using System.Text;

namespace Hidano.FacialControl.Adapters.OSC
{
    public static class OscAddressFormatter
    {
        public const string VRChatParameterPrefix = "/avatar/parameters/";
        public const string ARKitParameterPrefix = "/ARKit/";
        public const char VRChatGazeXAxis = 'X';
        public const char VRChatGazeYAxis = 'Y';

        public static byte[] GetOrAddAddressUtf8(
            Dictionary<string, byte[]> addressBytesPool,
            string address)
        {
            if (addressBytesPool == null)
            {
                throw new ArgumentNullException(nameof(addressBytesPool));
            }

            if (address == null)
            {
                throw new ArgumentNullException(nameof(address));
            }

            if (addressBytesPool.TryGetValue(address, out byte[] addressBytes))
            {
                return addressBytes;
            }

            addressBytes = Encoding.UTF8.GetBytes(address);
            addressBytesPool.Add(address, addressBytes);
            return addressBytes;
        }
    }
}
