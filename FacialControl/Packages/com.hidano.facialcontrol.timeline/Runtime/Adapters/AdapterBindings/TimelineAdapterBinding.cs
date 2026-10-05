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
    /// <summary>
    /// 「Timeline からの受信を有効にする」フラグ（Slug + 有効フラグ）。Receiver の取得 / 生成と所有管理だけを行う。
    /// </summary>
    /// <remarks>
    /// <para>レイヤー / チャネルは再生セッション開始時に TimelineAsset のトラックから導出するため、binding は設定を持たない。
    /// 旧フィールド（Target Layer Names / Channel Definitions）は旧 Profile のデシリアライズのためだけに残し、再生には使わない。</para>
    /// <para><c>timeline:*</c> の id を実行時に導出するため <see cref="IAdapterBindingDynamicInputs"/> を実装する。
    /// Analog / Gaze は registry の乗っ取りで再生するため Gaze 提供者 interface は実装しない。</para>
    /// </remarks>
    [Serializable]
    [FacialAdapterBinding(displayName: "Timeline")]
    public sealed class TimelineAdapterBinding : AdapterBindingBase, IAdapterBindingDynamicInputs
    {
        private const string DefaultSlug = "timeline";
        private const string LogPrefix = "[TimelineAdapterBinding] ";

        [SerializeField] private bool enabled = true;

        // legacy: 再生には使わない（レイヤー / チャネルは TimelineAsset のトラックから導出する）。旧 Profile のデシリアライズ用。
        [SerializeField, HideInInspector] private List<string> targetLayerNames = new List<string>();
        [SerializeField, HideInInspector] private List<TimelineValueChannelConfig> channelDefinitions = new List<TimelineValueChannelConfig>();

        [NonSerialized] private FacialTimelineReceiver _receiver;
        [NonSerialized] private bool _ownsReceiver;
        [NonSerialized] private bool _legacyWarningIssued;

        public TimelineAdapterBinding()
        {
            Slug = DefaultSlug;
        }

        /// <summary>Timeline からの受信を許可するか（既定 true）。</summary>
        public bool Enabled
        {
            get => enabled;
            set => enabled = value;
        }

        /// <summary>旧フィールド（Target Layer Names / Channel Definitions）のどちらかに値が残っているか。</summary>
        public bool HasLegacyFields =>
            (targetLayerNames != null && targetLayerNames.Count > 0)
            || (channelDefinitions != null && channelDefinitions.Count > 0);

        /// <summary>接続中の Receiver（無効時に既存 Receiver が無ければ null）。</summary>
        public FacialTimelineReceiver Receiver => _receiver;

        /// <summary>Receiver を本 binding が AddComponent したか（Dispose で破棄するのはこの場合のみ）。</summary>
        public bool OwnsReceiver => _ownsReceiver;

        public override void OnStart(in AdapterBuildContext ctx)
        {
            string slugText = string.IsNullOrWhiteSpace(Slug) ? DefaultSlug : Slug;
            if (!AdapterSlug.TryParse(slugText, out AdapterSlug slug))
            {
                Debug.LogError(LogPrefix + $"Slug '{slugText}' is invalid. Timeline からの受信を開始できません。");
                return;
            }

            Slug = slug.Value;

            if (HasLegacyFields && !_legacyWarningIssued)
            {
                _legacyWarningIssued = true;
                Debug.LogWarning(LogPrefix
                    + "旧フィールド（Target Layer Names / Channel Definitions）は再生に使われません。"
                    + "レイヤーとチャネルは TimelineAsset のトラックから自動で導出されます（再生は継続します）。");
            }

            GameObject host = ctx.HostGameObject;
            FacialTimelineReceiver existing = host.GetComponent<FacialTimelineReceiver>();
            if (!enabled)
            {
                // 無効時は Receiver を生成しない。既存 Receiver には無効の接続コンテキストだけ渡し、BindingDisabled を診断できるようにする。
                _receiver = existing;
                _ownsReceiver = false;
            }
            else if (existing != null)
            {
                _receiver = existing;
                _ownsReceiver = false;
            }
            else
            {
                _receiver = host.AddComponent<FacialTimelineReceiver>();
                _ownsReceiver = true;
            }

            if (_receiver == null)
            {
                return;
            }

            _receiver.AttachBinding(new TimelineBindingContext(
                slug,
                ctx.Profile,
                ctx.BlendShapeNames,
                ctx.InputSourceRegistry,
                host.GetComponent<FacialController>(),
                enabled));
        }

        public override void Dispose()
        {
            FacialTimelineReceiver receiver = _receiver;
            bool owns = _ownsReceiver;
            _receiver = null;
            _ownsReceiver = false;
            if (receiver == null)
            {
                return;
            }

            receiver.DetachBinding();
            if (!owns)
            {
                return;
            }

            if (UnityEngine.Application.isPlaying)
            {
                UnityEngine.Object.Destroy(receiver);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(receiver);
            }
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
