using System;
using System.Collections.Generic;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Adapters.Playable;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Timeline.Adapters;
using Hidano.FacialControl.Timeline.Adapters.Session;
using UnityEngine;

namespace Hidano.FacialControl.Timeline.Adapters.AdapterBindings
{
    [Serializable]
    [FacialAdapterBinding(displayName: "Timeline")]
    public sealed class TimelineAdapterBinding : AdapterBindingBase, IGazeSourceProvider
    {
        private const string DefaultSlug = "timeline";

        // legacy: 再生には使わない（レイヤー / チャネルは TimelineAsset のトラックから導出する）。Inspector からの撤去は 6.3。
        [SerializeField] private List<string> targetLayerNames = new List<string>();
        [SerializeField] private List<TimelineValueChannelConfig> channelDefinitions = new List<TimelineValueChannelConfig>();

        [NonSerialized] private FacialTimelineReceiver _receiver;

        public TimelineAdapterBinding()
        {
            Slug = DefaultSlug;
        }

        public IReadOnlyList<string> TargetLayerNames => targetLayerNames;

        public IReadOnlyList<TimelineValueChannelConfig> ChannelDefinitions => channelDefinitions;

        public FacialTimelineReceiver Receiver => _receiver;

        public override void OnStart(in AdapterBuildContext ctx)
        {
            string slugText = string.IsNullOrWhiteSpace(Slug) ? DefaultSlug : Slug;
            if (!AdapterSlug.TryParse(slugText, out AdapterSlug slug))
            {
                Debug.LogError(
                    $"[TimelineAdapterBinding] Slug '{slugText}' is invalid. Timeline sinks were not registered.");
                return;
            }

            Slug = slug.Value;

            _receiver = ctx.HostGameObject.GetComponent<FacialTimelineReceiver>();
            if (_receiver == null)
            {
                _receiver = ctx.HostGameObject.AddComponent<FacialTimelineReceiver>();
            }

            // レイヤー / チャネルは再生セッション開始時に TimelineAsset のトラックから導出する。ここでは接続コンテキストを渡すだけ。
            _receiver.AttachBinding(new TimelineBindingContext(
                slug,
                ctx.Profile,
                ctx.BlendShapeNames,
                ctx.InputSourceRegistry,
                ctx.HostGameObject.GetComponent<FacialController>(),
                enabled: true));
        }

        public IEnumerable<GazeSourceDeclaration> GetGazeSourceDeclarations()
        {
            if (channelDefinitions == null)
            {
                yield break;
            }

            for (int i = 0; i < channelDefinitions.Count; i++)
            {
                TimelineValueChannelConfig channel = channelDefinitions[i];
                if (channel == null || !channel.IsGaze
                    || !GazeSourceIdConvention.IsValidChannelId(channel.Sub))
                {
                    continue;
                }

                yield return new GazeSourceDeclaration(channel.Sub, providesLeftRightPair: false);
            }
        }

        public override void Dispose()
        {
            if (_receiver == null)
            {
                return;
            }

            _receiver.DetachBinding();

            if (UnityEngine.Application.isPlaying)
            {
                UnityEngine.Object.Destroy(_receiver);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(_receiver);
            }

            _receiver = null;
        }
    }

    [Serializable]
    public sealed class TimelineValueChannelConfig
    {
        [SerializeField] private string sub = string.Empty;
        [SerializeField] private int axisCount = 1;
        [SerializeField] private bool isGaze;
        [SerializeField] private string takeoverSourceId = string.Empty;

        public string Sub
        {
            get => sub;
            set => sub = value ?? string.Empty;
        }

        public int AxisCount
        {
            get => axisCount;
            set => axisCount = value;
        }

        public bool IsGaze
        {
            get => isGaze;
            set => isGaze = value;
        }

        public string TakeoverSourceId
        {
            get => takeoverSourceId;
            set => takeoverSourceId = value ?? string.Empty;
        }
    }
}
