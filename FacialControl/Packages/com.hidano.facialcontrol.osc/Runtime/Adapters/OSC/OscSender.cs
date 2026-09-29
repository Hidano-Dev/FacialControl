using System;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using UnityEngine;
using uOSC;

namespace Hidano.FacialControl.Adapters.OSC
{
    /// <summary>heartbeat と同一 frame bundle に載せる metadata。</summary>
    public readonly struct OscHeartbeatPayload
    {
        public string[] HeartbeatNames { get; }
        public int HeartbeatNameCount { get; }
        public string PresetName { get; }
        public string CustomPrefix { get; }
        public string[] GazeAdvertisementPairs { get; }
        public int GazeAdvertisementPairCount { get; }

        public OscHeartbeatPayload(
            string[] heartbeatNames,
            int heartbeatNameCount,
            string presetName,
            string customPrefix,
            string[] gazeAdvertisementPairs,
            int gazeAdvertisementPairCount)
        {
            if (heartbeatNames == null)
                throw new ArgumentNullException(nameof(heartbeatNames));
            if (heartbeatNameCount < 0 || heartbeatNameCount > heartbeatNames.Length)
                throw new ArgumentOutOfRangeException(nameof(heartbeatNameCount));
            if (gazeAdvertisementPairCount < 0 ||
                (gazeAdvertisementPairs == null
                    ? gazeAdvertisementPairCount != 0
                    : gazeAdvertisementPairCount > gazeAdvertisementPairs.Length / 2))
            {
                throw new ArgumentOutOfRangeException(nameof(gazeAdvertisementPairCount));
            }

            HeartbeatNames = heartbeatNames;
            HeartbeatNameCount = heartbeatNameCount;
            PresetName = presetName;
            CustomPrefix = customPrefix;
            GazeAdvertisementPairs = gazeAdvertisementPairs;
            GazeAdvertisementPairCount = gazeAdvertisementPairCount;
        }
    }

    /// <summary>
    /// uOsc クライアントをラップし、BlendShape 値を OSC メッセージとして送信する。
    /// uOscClient の内部スレッドで非同期送信を行い、メインスレッドの負荷をゼロにする。
    /// </summary>
    public class OscSender : MonoBehaviour
    {
        private const string BlendShapeNamesAddress = "/_facialcontrol/blendshape_names";
        private const string PresetAddress = "/_facialcontrol/preset";
        private const string GazeAdvertisementAddress = "/_facialcontrol/gaze";

        private static readonly byte[] SenderIdentityAddressUtf8 =
            Encoding.UTF8.GetBytes(SenderIdentity.OscAddress);

        private static readonly byte[] BlendShapeNamesAddressUtf8 =
            Encoding.UTF8.GetBytes(BlendShapeNamesAddress);

        private static readonly byte[] PresetAddressUtf8 =
            Encoding.UTF8.GetBytes(PresetAddress);

        private static readonly byte[] GazeAdvertisementAddressUtf8 =
            Encoding.UTF8.GetBytes(GazeAdvertisementAddress);

        [SerializeField]
        private string _endpoint = "127.0.0.1";

        [SerializeField]
        private int _port = OscConfiguration.DefaultSendPort;

        [SerializeField]
        private bool _autoStart = true;

        private uOSC.uOscClient _client;
        private UdpDatagramSender _udpBundleSender;
        private IDatagramSender _bundleSenderOverride;
        private IPEndPoint _bundleEndpoint;
        private string _bundleEndpointAddress;
        private int _bundleEndpointPort;
        private OscBundleBuilder _bundleBuilder;
        private bool _initialized;
        private bool _sending;
        private bool _configured;

        // マッピング情報: インデックス → OSC アドレス
        private string[] _oscAddresses;
        private byte[][] _oscAddressUtf8;

        // マッピング情報を保持
        private OscMapping[] _mappings;

        /// <summary>
        /// 送信先エンドポイント（IP アドレス / host）。
        /// </summary>
        public string Endpoint
        {
            get => _endpoint;
            set => _endpoint = value;
        }

        /// <summary>
        /// 送信先ポート番号。
        /// </summary>
        public int Port
        {
            get => _port;
            set => _port = value;
        }

        /// <summary>
        /// クライアントが稼働中かどうか。
        /// </summary>
        public bool IsRunning => _client != null && _client.isRunning;

        /// <summary>uOSC client owned by this sender after StartSending.</summary>
        public uOSC.uOscClient Client => _client;

        /// <summary>
        /// 初期化済みかどうか。
        /// </summary>
        public bool IsInitialized => _initialized;

        /// <summary>
        /// <see cref="Configure(string, int, OscMapping[])"/> が呼ばれて送信開始済みなら true。
        /// </summary>
        public bool IsConfigured => _configured;

        /// <summary>
        /// マッピング数（送信対象の BlendShape 数）。
        /// </summary>
        public int MappingCount => _oscAddresses != null ? _oscAddresses.Length : 0;

        /// <summary>
        /// OscSender を初期化する。
        /// マッピング情報から OSC アドレスのルックアップテーブルを構築する。
        /// </summary>
        /// <param name="mappings">OSC アドレスマッピング配列。</param>
        public void Initialize(OscMapping[] mappings)
        {
            Initialize(mappings, addressUtf8: null);
        }

        public void Initialize(OscMapping[] mappings, byte[][] addressUtf8)
        {
            if (mappings == null)
                throw new ArgumentNullException(nameof(mappings));

            if (addressUtf8 != null && addressUtf8.Length != mappings.Length)
                throw new ArgumentException("Address byte table length must match mapping length.", nameof(addressUtf8));

            _mappings = mappings;
            _oscAddresses = new string[mappings.Length];
            _oscAddressUtf8 = new byte[mappings.Length][];
            for (int i = 0; i < mappings.Length; i++)
            {
                _oscAddresses[i] = mappings[i].OscAddress;
                _oscAddressUtf8[i] = addressUtf8 != null && addressUtf8[i] != null
                    ? addressUtf8[i]
                    : Encoding.UTF8.GetBytes(_oscAddresses[i]);
            }

            if (_bundleBuilder == null)
            {
                _bundleBuilder = new OscBundleBuilder();
            }

            _initialized = true;
        }

        /// <summary>
        /// OscConfiguration から初期化する。
        /// </summary>
        /// <param name="config">OSC 設定（ポート番号・マッピング含む）。</param>
        public void Initialize(OscConfiguration config)
        {
            _port = config.SendPort;
            var mappingSpan = config.Mapping.Span;
            var mappings = new OscMapping[mappingSpan.Length];
            for (int i = 0; i < mappingSpan.Length; i++)
            {
                mappings[i] = mappingSpan[i];
            }
            Initialize(mappings);
        }

        /// <summary>
        /// エンドポイント・ポート・マッピングをまとめて設定し、送信を開始する。
        /// AdapterBinding 経路から <c>ctx.HostGameObject.AddComponent&lt;OscSender&gt;()</c> 後に呼び出す統合 API。
        /// </summary>
        public void Configure(string endpoint, int port, OscMapping[] mappings)
        {
            Configure(endpoint, port, mappings, addressUtf8: null);
        }

        /// <summary>
        /// エンドポイント・ポート・マッピング（+ 事前構築済み UTF-8 アドレス table）をまとめて設定し、送信を開始する。
        /// </summary>
        public void Configure(string endpoint, int port, OscMapping[] mappings, byte[][] addressUtf8)
        {
            if (mappings == null) throw new ArgumentNullException(nameof(mappings));

            _endpoint = endpoint;
            _port = port;
            Initialize(mappings, addressUtf8);
            StartSending();
            _configured = true;
        }

        /// <summary>
        /// 送信を開始する。uOscClient を起動する。
        /// </summary>
        public void StartSending()
        {
            if (!_initialized)
            {
                Debug.LogError("[FacialControl] OscSender が初期化されていません。Initialize() を先に呼び出してください。");
                return;
            }

            if (_sending)
                return;

            EnsureClient();
            EnsureBundleClient();
            _client.address = _endpoint;
            _client.port = _port;

            // uOscClient は OnEnable で自動的に StartClient を呼ぶため、
            // enabled を制御してライフサイクルを管理する
            if (!_client.isRunning)
            {
                _client.StartClient();
            }
            _sending = true;
        }

        /// <summary>
        /// 送信を停止する。uOscClient を停止する。
        /// </summary>
        public void StopSending()
        {
            if (_client != null && _client.isRunning)
            {
                _client.StopClient();
            }

            CloseBundleClient();
            _sending = false;
        }

        /// <summary>
        /// 全 BlendShape 値を OSC メッセージとして送信する。
        /// 各マッピングに対応する OSC アドレスに float 値を送信する。
        /// uOscClient の内部キューに追加され、別スレッドで非同期送信される。
        /// </summary>
        /// <param name="values">送信する BlendShape 値の配列。マッピングと同じ長さが必要。</param>
        public void SendAll(float[] values)
        {
            if (!_initialized || _client == null || !_client.isRunning)
                return;

            if (values == null)
                return;

            int count = Math.Min(values.Length, _oscAddresses.Length);
            for (int i = 0; i < count; i++)
            {
                _client.Send(_oscAddresses[i], values[i]);
            }
        }

        /// <summary>
        /// 送信元識別ヘッダと BlendShape 値群を 1 つの OSC bundle として送信する。
        /// </summary>
        public void SendBundle(SenderIdentity identity, float[] values, int count)
        {
            SendBundle(
                identity.Uuid.ToByteArray(),
                identity.StartedAtUnixMs.ToString(CultureInfo.InvariantCulture),
                values,
                count);
        }

        /// <summary>
        /// 事前構築済みの送信元識別 payload と BlendShape 値群を 1 つの OSC bundle として送信する。
        /// </summary>
        public void SendBundle(
            byte[] senderUuidBytes,
            string startedAtUnixMs,
            float[] values,
            int count)
        {
            SendBundle(
                senderUuidBytes,
                startedAtUnixMs,
                values,
                count,
                heartbeatNames: null,
                heartbeatNameCount: 0);
        }

        /// <summary>
        /// 事前構築済みの送信元識別 payload、BlendShape 値群、必要なら heartbeat を 1 つの OSC bundle として送信する。
        /// </summary>
        public void SendBundle(
            byte[] senderUuidBytes,
            string startedAtUnixMs,
            float[] values,
            int count,
            string[] heartbeatNames,
            int heartbeatNameCount)
        {
            SendBundle(
                senderUuidBytes,
                startedAtUnixMs,
                _oscAddressUtf8,
                values,
                count,
                heartbeatNames,
                heartbeatNameCount);
        }

        /// <summary>
        /// Sends a frame bundle using a caller-provided address table. This is used when the
        /// current frame contains a compact subset of the configured sender mappings.
        /// </summary>
        public void SendBundle(
            byte[] senderUuidBytes,
            string startedAtUnixMs,
            byte[][] addressUtf8,
            float[] values,
            int count)
        {
            SendBundle(
                senderUuidBytes,
                startedAtUnixMs,
                addressUtf8,
                values,
                count,
                heartbeatNames: null,
                heartbeatNameCount: 0);
        }

        /// <summary>
        /// Sends a frame bundle using a caller-provided address table plus an optional heartbeat.
        /// </summary>
        public void SendBundle(
            byte[] senderUuidBytes,
            string startedAtUnixMs,
            byte[][] addressUtf8,
            float[] values,
            int count,
            string[] heartbeatNames,
            int heartbeatNameCount)
        {
            if (!_initialized || _client == null || !_client.isRunning)
                return;

            if (senderUuidBytes == null || senderUuidBytes.Length != SenderIdentity.UuidByteLength)
                return;

            if (string.IsNullOrEmpty(startedAtUnixMs) || addressUtf8 == null || values == null)
                return;

            bool includeHeartbeat = heartbeatNames != null;
            if (includeHeartbeat &&
                (heartbeatNameCount < 0 || heartbeatNameCount > heartbeatNames.Length))
            {
                return;
            }

            int messageCount = Math.Min(Math.Min(Math.Max(count, 0), values.Length), addressUtf8.Length);
            ulong timestamp = Timestamp.Now.value;
            int packetCount = includeHeartbeat
                ? _bundleBuilder.BuildFrameBundle(
                    timestamp,
                    SenderIdentityAddressUtf8,
                    senderUuidBytes,
                    startedAtUnixMs,
                    addressUtf8,
                    values,
                    messageCount,
                    BlendShapeNamesAddressUtf8,
                    heartbeatNames,
                    heartbeatNameCount)
                : _bundleBuilder.BuildFrameBundle(
                    timestamp,
                    SenderIdentityAddressUtf8,
                    senderUuidBytes,
                    startedAtUnixMs,
                    addressUtf8,
                    values,
                    messageCount);

            EnsureBundleClient();
            for (int i = 0; i < packetCount; i++)
            {
                OscBundlePacket packet = _bundleBuilder.GetPacket(i);
                SendBundlePacket(packet);
            }
        }

        /// <summary>frame bundle に heartbeat / preset / gaze 広告を同一 timestamp で載せて送信する。</summary>
        public void SendBundle(
            byte[] senderUuidBytes,
            string startedAtUnixMs,
            byte[][] addressUtf8,
            float[] values,
            int count,
            in OscHeartbeatPayload heartbeat)
        {
            if (!_initialized || _client == null || !_client.isRunning)
                return;

            if (senderUuidBytes == null || senderUuidBytes.Length != SenderIdentity.UuidByteLength)
                return;

            if (string.IsNullOrEmpty(startedAtUnixMs) || addressUtf8 == null || values == null)
                return;

            int messageCount = Math.Min(Math.Min(Math.Max(count, 0), values.Length), addressUtf8.Length);
            int packetCount = _bundleBuilder.BuildFrameBundle(
                Timestamp.Now.value,
                SenderIdentityAddressUtf8,
                senderUuidBytes,
                startedAtUnixMs,
                addressUtf8,
                values,
                messageCount,
                BlendShapeNamesAddressUtf8,
                heartbeat.HeartbeatNames,
                heartbeat.HeartbeatNameCount,
                heartbeat.PresetName == null ? null : PresetAddressUtf8,
                heartbeat.PresetName,
                heartbeat.CustomPrefix,
                heartbeat.GazeAdvertisementPairs == null ? null : GazeAdvertisementAddressUtf8,
                heartbeat.GazeAdvertisementPairs,
                heartbeat.GazeAdvertisementPairCount);

            EnsureBundleClient();
            for (int i = 0; i < packetCount; i++)
            {
                OscBundlePacket packet = _bundleBuilder.GetPacket(i);
                SendBundlePacket(packet);
            }
        }

        /// <summary>
        /// Sends a frame bundle using a caller-provided address table plus heartbeat and preset metadata.
        /// </summary>
        public void SendBundle(
            byte[] senderUuidBytes,
            string startedAtUnixMs,
            byte[][] addressUtf8,
            float[] values,
            int count,
            string[] heartbeatNames,
            int heartbeatNameCount,
            string presetName,
            string customPrefix)
        {
            if (!_initialized || _client == null || !_client.isRunning)
                return;

            if (senderUuidBytes == null || senderUuidBytes.Length != SenderIdentity.UuidByteLength)
                return;

            if (string.IsNullOrEmpty(startedAtUnixMs) || addressUtf8 == null || values == null)
                return;

            if (heartbeatNames == null ||
                heartbeatNameCount < 0 ||
                heartbeatNameCount > heartbeatNames.Length ||
                string.IsNullOrEmpty(presetName))
            {
                return;
            }

            int messageCount = Math.Min(Math.Min(Math.Max(count, 0), values.Length), addressUtf8.Length);
            ulong timestamp = Timestamp.Now.value;
            int packetCount = _bundleBuilder.BuildFrameBundle(
                timestamp,
                SenderIdentityAddressUtf8,
                senderUuidBytes,
                startedAtUnixMs,
                addressUtf8,
                values,
                messageCount,
                BlendShapeNamesAddressUtf8,
                heartbeatNames,
                heartbeatNameCount,
                PresetAddressUtf8,
                presetName,
                customPrefix);

            EnsureBundleClient();
            for (int i = 0; i < packetCount; i++)
            {
                OscBundlePacket packet = _bundleBuilder.GetPacket(i);
                SendBundlePacket(packet);
            }
        }

        /// <summary>
        /// 指定インデックスの BlendShape 値を単一の OSC メッセージとして送信する。
        /// </summary>
        /// <param name="index">マッピングインデックス。</param>
        /// <param name="value">送信する値。</param>
        public void SendSingle(int index, float value)
        {
            if (!_initialized || _client == null || !_client.isRunning)
                return;

            if (index < 0 || index >= _oscAddresses.Length)
                return;

            _client.Send(_oscAddresses[index], value);
        }

        private void OnEnable()
        {
            if (_autoStart && _initialized)
            {
                StartSending();
            }
        }

        private void OnDisable()
        {
            StopSending();
        }

        private void EnsureClient()
        {
            if (_client != null)
                return;

            // Each OscSender owns a client so multiple sender hosts on one GameObject stay independent.
            _client = gameObject.AddComponent<uOSC.uOscClient>();
            _client.StopClient();
        }

        private void EnsureBundleClient()
        {
            if (_bundleEndpoint == null ||
                _bundleEndpointPort != _port ||
                !string.Equals(_bundleEndpointAddress, _endpoint, StringComparison.Ordinal))
            {
                IPAddress ipAddress = ResolveIpAddress(_endpoint);
                IPEndPoint endpoint = new IPEndPoint(ipAddress, _port);

                if (_udpBundleSender != null && _udpBundleSender.AddressFamily != ipAddress.AddressFamily)
                {
                    CloseBundleClient();
                }

                _bundleEndpoint = endpoint;
                _bundleEndpointAddress = _endpoint;
                _bundleEndpointPort = _port;
            }

            if (_bundleSenderOverride == null && _udpBundleSender == null)
            {
                _udpBundleSender = new UdpDatagramSender(_bundleEndpoint.AddressFamily);
            }
        }

        /// <summary>
        /// bundle 送信に使う <see cref="IDatagramSender"/> を差し替える（テスト用）。
        /// 差し替え中は UDP ソケットを開かない。<c>null</c> を渡すと既定の <see cref="UdpDatagramSender"/> に戻る。
        /// 送信先エンドポイントの解決（<see cref="EnsureBundleClient"/>）は差し替え後も同じ経路で行われる。
        /// </summary>
        internal void SetBundleSender(IDatagramSender sender)
        {
            _bundleSenderOverride = sender;
            if (sender != null && _udpBundleSender != null)
            {
                _udpBundleSender.Dispose();
                _udpBundleSender = null;
            }
        }

        private void SendBundlePacket(in OscBundlePacket packet)
        {
            IDatagramSender sender = _bundleSenderOverride ?? _udpBundleSender;
            sender.Send(packet.Buffer, packet.Length, _bundleEndpoint);
        }

        private static IPAddress ResolveIpAddress(string address)
        {
            if (IPAddress.TryParse(address, out IPAddress parsed))
            {
                return parsed;
            }

            IPAddress[] addresses = Dns.GetHostAddresses(address);
            for (int i = 0; i < addresses.Length; i++)
            {
                if (addresses[i].AddressFamily == AddressFamily.InterNetwork)
                {
                    return addresses[i];
                }
            }

            if (addresses.Length > 0)
            {
                return addresses[0];
            }

            throw new ArgumentException($"OSC endpoint address '{address}' could not be resolved.", nameof(address));
        }

        private void CloseBundleClient()
        {
            if (_udpBundleSender != null)
            {
                _udpBundleSender.Dispose();
                _udpBundleSender = null;
            }

            _bundleEndpoint = null;
            _bundleEndpointAddress = null;
            _bundleEndpointPort = 0;
        }

        private void OnDestroy()
        {
            StopSending();
            if (_bundleBuilder != null)
            {
                _bundleBuilder.Dispose();
                _bundleBuilder = null;
            }

            // OscSenderHost 統合に伴い、本クラスが gameObject.AddComponent で生やした
            // uOscClient のライフサイクル管理も担う。同 GameObject 上に追加した uOscClient を破棄して
            // socket close を保証する。
            if (_client != null)
            {
                if (UnityEngine.Application.isPlaying)
                {
                    UnityEngine.Object.Destroy(_client);
                }
                else
                {
                    UnityEngine.Object.DestroyImmediate(_client);
                }
                _client = null;
            }

            _configured = false;
        }
    }
}
