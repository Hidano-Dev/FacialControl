namespace Hidano.FacialControl.Adapters.OSC
{
    public static class OscTypeTag
    {
        public const byte Int32 = (byte)'i';
        public const byte Float32 = (byte)'f';
        public const byte String = (byte)'s';
        public const byte Blob = (byte)'b';
        public const byte True = (byte)'T';
        public const byte False = (byte)'F';

        public static bool HasPayload(byte tag)
        {
            return tag == Int32 || tag == Float32 || tag == String || tag == Blob;
        }

        public static bool IsKnown(byte tag)
        {
            return HasPayload(tag) || tag == True || tag == False;
        }
    }

    public enum OscPacketError : byte
    {
        None,
        Truncated,
        Misaligned,
        BadAddress,
        BadTypeTags,
        UnknownTypeTag,
        ArgumentOutOfRange,
        BundleTooDeep
    }

    public enum OscControlKind : byte
    {
        None,
        SenderId,
        Heartbeat,
        Preset,
        GazeAdvertisement,
        Values,
        Layout
    }
}
