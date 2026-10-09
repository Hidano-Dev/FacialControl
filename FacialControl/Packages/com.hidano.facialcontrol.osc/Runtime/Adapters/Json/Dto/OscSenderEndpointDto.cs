using System;
using Hidano.FacialControl.Adapters.OSC;
using Hidano.FacialControl.Domain.Models;
using UnityEngine;

namespace Hidano.FacialControl.Adapters.Json.Dto
{
    [Serializable]
    public sealed class OscSenderEndpointDto : ISerializationCallbackReceiver
    {
        public string ip = OscSenderEndpointConfig.DefaultEndpoint;
        public int port = OscConfiguration.DefaultSendPort;
        public bool enabled = true;

        public OscSenderEndpointDto()
        {
        }

        public OscSenderEndpointDto(
            string ip,
            int port,
            bool enabled = true)
        {
            this.ip = ip;
            this.port = port;
            this.enabled = enabled;
            ApplyDefaults();
        }

        public OscSenderEndpointConfig ToConfig()
        {
            ApplyDefaults();
            return new OscSenderEndpointConfig(ip, port, enabled);
        }

        public void ApplyDefaults()
        {
            if (string.IsNullOrWhiteSpace(ip))
            {
                ip = OscSenderEndpointConfig.DefaultEndpoint;
            }
            else
            {
                ip = ip.Trim();
            }

            if (port <= 0 || port > 65535)
            {
                port = OscConfiguration.DefaultSendPort;
            }
        }

        public void OnBeforeSerialize()
        {
            ApplyDefaults();
        }

        public void OnAfterDeserialize()
        {
            ApplyDefaults();
        }
    }
}
