using System;
using UnityEngine;

namespace Hidano.FacialControl.Adapters.Json.Dto
{
    [Serializable]
    public sealed class OscSenderOptionsDto : ISerializationCallbackReceiver
    {
        public const float DefaultHeartbeatIntervalSeconds = 5f;

        public OscSenderEndpointDto[] endpoints =
        {
            new OscSenderEndpointDto()
        };

        public string[] blendShapeMapping = new string[0];
        public bool suppressLoopback = true;
        public float heartbeatIntervalSeconds = DefaultHeartbeatIntervalSeconds;

        public static OscSenderOptionsDto FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return new OscSenderOptionsDto();
            }

            bool hasSuppressLoopback = ContainsJsonKey(json, nameof(suppressLoopback));
            OscSenderOptionsDto dto = JsonUtility.FromJson<OscSenderOptionsDto>(json);
            if (dto == null)
            {
                dto = new OscSenderOptionsDto();
            }

            if (!hasSuppressLoopback)
            {
                dto.suppressLoopback = true;
            }

            dto.ApplyDefaults();
            return dto;
        }

        public string ToJson(bool prettyPrint = true)
        {
            ApplyDefaults();
            return JsonUtility.ToJson(this, prettyPrint);
        }

        public void ApplyDefaults()
        {
            if (endpoints == null)
            {
                endpoints = new[]
                {
                    new OscSenderEndpointDto()
                };
            }

            for (int i = 0; i < endpoints.Length; i++)
            {
                if (endpoints[i] == null)
                {
                    endpoints[i] = new OscSenderEndpointDto();
                }
                else
                {
                    endpoints[i].ApplyDefaults();
                }
            }

            if (blendShapeMapping == null)
            {
                blendShapeMapping = new string[0];
            }

            if (heartbeatIntervalSeconds <= 0f || float.IsNaN(heartbeatIntervalSeconds))
            {
                heartbeatIntervalSeconds = DefaultHeartbeatIntervalSeconds;
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

        private static bool ContainsJsonKey(string json, string key)
        {
            return !string.IsNullOrEmpty(json)
                && json.IndexOf("\"" + key + "\"", StringComparison.Ordinal) >= 0;
        }
    }
}
