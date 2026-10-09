using System;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Adapters.OSC
{
    [Serializable]
    public class OscSenderEndpointConfig
    {
        public const string DefaultEndpoint = "127.0.0.1";

        public string endpoint = DefaultEndpoint;
        public int port = OscConfiguration.DefaultSendPort;
        public bool enabled = true;

        public OscSenderEndpointConfig()
        {
        }

        public OscSenderEndpointConfig(
            string endpoint,
            int port,
            bool enabled = true)
        {
            this.endpoint = endpoint ?? string.Empty;
            this.port = port;
            this.enabled = enabled;
        }
    }
}
