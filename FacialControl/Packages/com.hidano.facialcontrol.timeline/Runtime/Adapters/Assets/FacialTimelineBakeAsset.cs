using System;
using Hidano.FacialControl.Timeline.Domain.Models;
using Hidano.FacialControl.Timeline.Domain.Services;
using UnityEngine;

namespace Hidano.FacialControl.Timeline.Adapters.Assets
{
    public sealed class FacialTimelineBakeAsset : ScriptableObject
    {
        [SerializeField] private string sourceHashHex = string.Empty;
        [SerializeField] private string profileAssetGuid = string.Empty;
        [SerializeField] private float sampleRate = FacialTimelineHashCalculator.DefaultSampleRate;
        [SerializeField] private ExpressionSourceBake[] expressionBakes = Array.Empty<ExpressionSourceBake>();
        [SerializeField] private ValueChannelBake[] valueBakes = Array.Empty<ValueChannelBake>();
        [SerializeField] private TimelineStateEvent[] stateEvents = Array.Empty<TimelineStateEvent>();
        [SerializeField] private string profileContentHashHex = string.Empty;

        public string SourceHashHex
        {
            get => sourceHashHex;
            set => sourceHashHex = value ?? string.Empty;
        }

        public string ProfileAssetGuid
        {
            get => profileAssetGuid;
            set => profileAssetGuid = value ?? string.Empty;
        }

        /// <summary>
        /// 焼いたときの Profile スナップショットの内容ハッシュ（<see cref="FacialTimelineHashCalculator.ComputeProfileContentHashHex"/>）。
        /// 本フィールド導入前に焼いた Bake では空文字。
        /// </summary>
        public string ProfileContentHashHex
        {
            get => profileContentHashHex;
            set => profileContentHashHex = value ?? string.Empty;
        }

        public float SampleRate
        {
            get => sampleRate;
            set => sampleRate = value;
        }

        public ExpressionSourceBake[] ExpressionBakes
        {
            get => expressionBakes;
            set => expressionBakes = value ?? Array.Empty<ExpressionSourceBake>();
        }

        public ValueChannelBake[] ValueBakes
        {
            get => valueBakes;
            set => valueBakes = value ?? Array.Empty<ValueChannelBake>();
        }

        public TimelineStateEvent[] StateEvents
        {
            get => stateEvents;
            set => stateEvents = value ?? Array.Empty<TimelineStateEvent>();
        }
    }

    [Serializable]
    public sealed class ExpressionSourceBake
    {
        [SerializeField] private string layerName = string.Empty;
        [SerializeField] private BlendShapeCurve[] curves = Array.Empty<BlendShapeCurve>();

        public string LayerName
        {
            get => layerName;
            set => layerName = value ?? string.Empty;
        }

        public BlendShapeCurve[] Curves
        {
            get => curves;
            set => curves = value ?? Array.Empty<BlendShapeCurve>();
        }
    }

    [Serializable]
    public sealed class BlendShapeCurve
    {
        [SerializeField] private string blendShapeName = string.Empty;
        [SerializeField] private AnimationCurve curve;

        public string BlendShapeName
        {
            get => blendShapeName;
            set => blendShapeName = value ?? string.Empty;
        }

        public AnimationCurve Curve
        {
            get => curve;
            set => curve = value;
        }
    }

    [Serializable]
    public sealed class ValueChannelBake
    {
        [SerializeField] private string sub = string.Empty;
        [SerializeField] private bool isGaze;
        [SerializeField] private AnimationCurve[] axes = Array.Empty<AnimationCurve>();

        public string Sub
        {
            get => sub;
            set => sub = value ?? string.Empty;
        }

        public bool IsGaze
        {
            get => isGaze;
            set => isGaze = value;
        }

        public AnimationCurve[] Axes
        {
            get => axes;
            set => axes = value ?? Array.Empty<AnimationCurve>();
        }
    }
}
