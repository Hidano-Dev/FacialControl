using System;
using UnityEngine;

namespace Hidano.FacialControl.Adapters.Json.Dto
{
    [Serializable]
    public sealed class OscSenderOptionsDto : ISerializationCallbackReceiver
    {
        public const float DefaultLayoutRefreshIntervalSeconds = 5f;

        public OscSenderEndpointDto[] endpoints =
        {
            new OscSenderEndpointDto()
        };

        public string[] blendShapeMapping = new string[0];
        public bool suppressLoopback = true;
        /// <summary>gaze の設定の変更を確かめ、変わっていれば対応表を作り直す間隔（秒）。旧キーは <c>heartbeatIntervalSeconds</c>。</summary>
        public float layoutRefreshIntervalSeconds = DefaultLayoutRefreshIntervalSeconds;

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

            if (!ContainsJsonKey(json, nameof(layoutRefreshIntervalSeconds)))
            {
                LegacyIntervalDto legacy = JsonUtility.FromJson<LegacyIntervalDto>(json);
                dto.layoutRefreshIntervalSeconds = legacy != null
                    ? legacy.heartbeatIntervalSeconds
                    : DefaultLayoutRefreshIntervalSeconds;
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

            if (layoutRefreshIntervalSeconds <= 0f || float.IsNaN(layoutRefreshIntervalSeconds))
            {
                layoutRefreshIntervalSeconds = DefaultLayoutRefreshIntervalSeconds;
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

        /// <summary>旧キー <c>heartbeatIntervalSeconds</c> だけを読むための DTO（書き出しは新キーのみ）。</summary>
        [Serializable]
        private sealed class LegacyIntervalDto
        {
            public float heartbeatIntervalSeconds = DefaultLayoutRefreshIntervalSeconds;
        }

        private static bool ContainsJsonKey(string json, string key)
        {
            return !string.IsNullOrEmpty(json)
                && json.IndexOf("\"" + key + "\"", StringComparison.Ordinal) >= 0;
        }
    }
}
