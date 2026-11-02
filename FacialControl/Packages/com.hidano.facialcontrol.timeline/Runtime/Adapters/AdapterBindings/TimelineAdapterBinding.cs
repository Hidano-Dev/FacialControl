using System;
using System.Collections.Generic;
using Hidano.FacialControl.Domain.Adapters;
using UnityEngine;

namespace Hidano.FacialControl.Timeline.Adapters.AdapterBindings
{
    /// <summary>
    /// 旧「Timeline からの受信を有効にする」binding（廃止。HID-408）。既存 Profile のデシリアライズのためだけに型を残す。
    /// </summary>
    /// <remarks>
    /// <para>Timeline の再生は Facial Character トラックに Bind した FacialController から文脈を受け取るため、Profile の
    /// Adapter Bindings への追加は要らない。既存 Profile に残ったこの binding は何もしない（<see cref="OnStart"/> は no-op）。
    /// Adapter Bindings の追加メニューには出さない（<c>FacialAdapterBinding</c> 属性を外した）。</para>
    /// <para>旧 Profile の <c>timeline:*</c> 宣言を Routing の検証で不正扱いにしないため <see cref="IAdapterBindingDynamicInputs"/> は残す。
    /// slug は旧 <c>:state</c> 宣言の掃除（Editor）でだけ読む。</para>
    /// </remarks>
    [Serializable]
    public sealed class TimelineAdapterBinding : AdapterBindingBase, IAdapterBindingDynamicInputs
    {
        private const string DefaultSlug = "timeline";

        [SerializeField] private bool enabled = true;

        // legacy: 再生には使わない。旧 Profile のデシリアライズ用。
        [SerializeField, HideInInspector] private List<string> targetLayerNames = new List<string>();
        [SerializeField, HideInInspector] private List<TimelineValueChannelConfig> channelDefinitions = new List<TimelineValueChannelConfig>();

        public TimelineAdapterBinding()
        {
            Slug = DefaultSlug;
        }

        /// <summary>旧フラグ（再生には使わない）。</summary>
        public bool Enabled
        {
            get => enabled;
            set => enabled = value;
        }

        /// <summary>旧フィールド（Target Layer Names / Channel Definitions）のどちらかに値が残っているか。</summary>
        public bool HasLegacyFields =>
            (targetLayerNames != null && targetLayerNames.Count > 0)
            || (channelDefinitions != null && channelDefinitions.Count > 0);

        /// <summary>何もしない（Timeline の再生は Facial Character トラックの binding から文脈を受け取る）。</summary>
        public override void OnStart(in AdapterBuildContext ctx)
        {
        }

        public override void Dispose()
        {
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
