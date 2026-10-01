using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Hidano.FacialControl.Adapters.AdapterBindings;
using Hidano.FacialControl.Domain.Adapters;

namespace Hidano.FacialControl.Adapters.OSC
{
    /// <summary>
    /// Suppresses OSC sender endpoints that target receivers in the same adapter child scope.
    /// </summary>
    public sealed class LoopbackSuppressionPolicy
    {
        private const string LoopbackAddressKey = "loopback";
        private const string AnyAddressKey = "any";

        private readonly HashSet<EndpointKey> _listenEndpoints = new HashSet<EndpointKey>();
        private readonly HashSet<int> _loopbackListenPorts = new HashSet<int>();
        private readonly HashSet<int> _wildcardListenPorts = new HashSet<int>();
        private readonly HashSet<string> _localInterfaceAddresses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public int Count => _listenEndpoints.Count;

        public static LoopbackSuppressionPolicy FromBindings(IReadOnlyList<AdapterBindingBase> bindings)
        {
            var policy = new LoopbackSuppressionPolicy();
            if (bindings == null)
            {
                return policy;
            }

            for (int i = 0; i < bindings.Count; i++)
            {
                if (bindings[i] is OscReceiverAdapterBinding receiver)
                {
                    policy.AddReceiverEndpoint(receiver.Endpoint, receiver.Port);
                }
            }

            // 受信は全インターフェースで行うため、自機の LAN IP 宛ての送信も自分の受信に届く。
            if (policy._wildcardListenPorts.Count > 0)
            {
                policy.AddLocalInterfaceAddresses();
            }

            return policy;
        }

        /// <summary>
        /// 自機のインターフェースアドレスを登録する。全インターフェースで受信しているポートに対し、
        /// このアドレス宛ての送信も loopback として抑止する。
        /// </summary>
        public void AddLocalInterfaceAddress(string address)
        {
            EndpointAddress normalized = NormalizeAddress(address);
            if (normalized.Kind == EndpointKind.Exact)
            {
                _localInterfaceAddresses.Add(normalized.Key);
            }
        }

        private void AddLocalInterfaceAddresses()
        {
            try
            {
                NetworkInterface[] interfaces = NetworkInterface.GetAllNetworkInterfaces();
                for (int i = 0; i < interfaces.Length; i++)
                {
                    UnicastIPAddressInformationCollection addresses = interfaces[i].GetIPProperties().UnicastAddresses;
                    foreach (UnicastIPAddressInformation info in addresses)
                    {
                        IPAddress address = info.Address;
                        if (address.AddressFamily == AddressFamily.InterNetwork
                            || address.AddressFamily == AddressFamily.InterNetworkV6)
                        {
                            AddLocalInterfaceAddress(address.ToString());
                        }
                    }
                }
            }
            catch (Exception)
            {
                // インターフェース列挙に失敗した環境では、明示的な loopback / 同一アドレスの判定だけ行う。
            }
        }

        public bool AddReceiverEndpoint(string endpoint, int port)
        {
            if (!IsValidPort(port))
            {
                return false;
            }

            EndpointKey key = CreateKey(endpoint, port);
            bool added = _listenEndpoints.Add(key);
            if (key.Kind == EndpointKind.Loopback)
            {
                _loopbackListenPorts.Add(port);
            }
            else if (key.Kind == EndpointKind.Wildcard)
            {
                _wildcardListenPorts.Add(port);
            }

            return added;
        }

        public bool IsSuppressed(OscSenderEndpointConfig endpoint)
        {
            if (endpoint == null)
            {
                return false;
            }

            return IsSuppressed(endpoint.endpoint, endpoint.port);
        }

        public bool IsSuppressed(string endpoint, int port)
        {
            if (!IsValidPort(port) || _listenEndpoints.Count == 0)
            {
                return false;
            }

            EndpointKey sendKey = CreateKey(endpoint, port);
            if (_listenEndpoints.Contains(sendKey))
            {
                return true;
            }

            if (sendKey.Kind == EndpointKind.Loopback)
            {
                return _loopbackListenPorts.Contains(port) || _wildcardListenPorts.Contains(port);
            }

            if (sendKey.Kind == EndpointKind.Wildcard)
            {
                return _wildcardListenPorts.Contains(port) || _loopbackListenPorts.Contains(port);
            }

            return _wildcardListenPorts.Contains(port) && _localInterfaceAddresses.Contains(sendKey.AddressKey);
        }

        private static bool IsValidPort(int port)
        {
            return port >= 0 && port <= 65535;
        }

        private static EndpointKey CreateKey(string endpoint, int port)
        {
            EndpointAddress address = NormalizeAddress(endpoint);
            return new EndpointKey(address.Key, address.Kind, port);
        }

        private static EndpointAddress NormalizeAddress(string endpoint)
        {
            string value = string.IsNullOrWhiteSpace(endpoint)
                ? OscSenderEndpointConfig.DefaultEndpoint
                : endpoint.Trim();

            if (string.Equals(value, "localhost", StringComparison.OrdinalIgnoreCase))
            {
                return new EndpointAddress(LoopbackAddressKey, EndpointKind.Loopback);
            }

            if (IPAddress.TryParse(value, out IPAddress address))
            {
                if (address.IsIPv4MappedToIPv6)
                {
                    address = address.MapToIPv4();
                }

                if (IPAddress.IsLoopback(address))
                {
                    return new EndpointAddress(LoopbackAddressKey, EndpointKind.Loopback);
                }

                if (address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
                {
                    return new EndpointAddress(AnyAddressKey, EndpointKind.Wildcard);
                }

                return new EndpointAddress(address.ToString(), EndpointKind.Exact);
            }

            return new EndpointAddress(value.ToLowerInvariant(), EndpointKind.Exact);
        }

        private enum EndpointKind
        {
            Exact,
            Loopback,
            Wildcard
        }

        private readonly struct EndpointAddress
        {
            public readonly string Key;
            public readonly EndpointKind Kind;

            public EndpointAddress(string key, EndpointKind kind)
            {
                Key = key;
                Kind = kind;
            }
        }

        private readonly struct EndpointKey : IEquatable<EndpointKey>
        {
            public readonly string AddressKey;
            public readonly EndpointKind Kind;
            public readonly int Port;

            public EndpointKey(string addressKey, EndpointKind kind, int port)
            {
                AddressKey = addressKey ?? string.Empty;
                Kind = kind;
                Port = port;
            }

            public bool Equals(EndpointKey other)
            {
                return Port == other.Port
                    && Kind == other.Kind
                    && string.Equals(AddressKey, other.AddressKey, StringComparison.OrdinalIgnoreCase);
            }

            public override bool Equals(object obj)
            {
                return obj is EndpointKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = StringComparer.OrdinalIgnoreCase.GetHashCode(AddressKey);
                    hash = (hash * 397) ^ (int)Kind;
                    hash = (hash * 397) ^ Port;
                    return hash;
                }
            }
        }
    }
}
