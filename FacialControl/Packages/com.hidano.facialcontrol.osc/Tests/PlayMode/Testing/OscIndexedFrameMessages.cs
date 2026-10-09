using System.Collections.Generic;
using Hidano.FacialControl.Adapters.AdapterBindings;
using Hidano.FacialControl.Adapters.OSC;

namespace Hidano.FacialControl.Osc.Tests.PlayMode.Testing
{
    /// <summary>
    /// 受信 binding へ sender_id・対応表・値フレームを uOSC 互換 facade（<see cref="OscReceiver.HandleOscMessage"/>）経由で
    /// 届けるテスト用の組み立て。受信 binding は値フレーム（<c>/_facialcontrol/values</c>）以外の名前つきアドレスを受けない。
    /// </summary>
    internal static class OscIndexedFrameMessages
    {
        /// <summary>テストで使う対応表のバージョン（<see cref="OscFrameLayoutVersion.Unknown"/> 以外なら何でもよい）。</summary>
        public const int LayoutVersion = 0x5A17;

        /// <summary>
        /// 対応表を適用済みにする。値フレームでバージョンを待たせ、対応表を 1 チャンクで届けてから tick で適用する。
        /// <paramref name="timestamp"/> はこの準備用 bundle の timestamp（後続のフレームと別の値にする）。
        /// </summary>
        public static void ApplyLayout(
            OscReceiverAdapterBinding binding,
            SenderIdentity sender,
            IReadOnlyList<string> blendShapeNames,
            IReadOnlyList<OscFrameLayoutGazeChannel> gazeChannels = null,
            ulong timestamp = 1000UL,
            int version = LayoutVersion)
        {
            OscReceiver receiver = binding.HelperHost.Receiver;
            receiver.HandleOscMessage(SenderId(sender, timestamp));
            receiver.HandleOscMessage(Values(version, 0, timestamp));
            receiver.HandleOscMessage(Layout(version, blendShapeNames, gazeChannels));
            binding.OnFixedTick(0.02f);
        }

        /// <summary>sender_id と値フレームを同じ bundle（同じ timestamp）として届ける。</summary>
        public static void SendFrame(
            OscReceiver receiver,
            SenderIdentity sender,
            ulong timestamp,
            params float[] slots)
        {
            receiver.HandleOscMessage(SenderId(sender, timestamp));
            receiver.HandleOscMessage(Values(LayoutVersion, 0, timestamp, slots));
        }

        public static uOSC.Message SenderId(SenderIdentity identity, ulong timestamp)
        {
            var message = new uOSC.Message(
                SenderIdentity.OscAddress,
                identity.SenderId.ToByteArray(),
                identity.StartedAtUnixMs);
            message.timestamp = new uOSC.Timestamp(timestamp);
            return message;
        }

        public static uOSC.Message Values(int version, int offset, ulong timestamp, params float[] values)
        {
            var arguments = new object[2 + values.Length];
            arguments[0] = version;
            arguments[1] = offset;
            for (int i = 0; i < values.Length; i++)
            {
                arguments[2 + i] = values[i];
            }

            var message = new uOSC.Message(OscControlAddresses.Values, arguments);
            message.timestamp = new uOSC.Timestamp(timestamp);
            return message;
        }

        /// <summary>対応表を 1 チャンクの <c>/_facialcontrol/layout</c> にする。</summary>
        public static uOSC.Message Layout(
            int version,
            IReadOnlyList<string> blendShapeNames,
            IReadOnlyList<OscFrameLayoutGazeChannel> gazeChannels = null)
        {
            OscFrameLayoutEntry[] entries = OscFrameLayout.ToEntries(blendShapeNames, gazeChannels);
            var arguments = new object[3 + (entries.Length * 2)];
            arguments[0] = version;
            arguments[1] = 0;
            arguments[2] = 1;
            for (int i = 0; i < entries.Length; i++)
            {
                arguments[3 + (i * 2)] = (int)entries[i].Kind;
                arguments[4 + (i * 2)] = entries[i].Value;
            }

            return new uOSC.Message(OscControlAddresses.Layout, arguments);
        }
    }
}
