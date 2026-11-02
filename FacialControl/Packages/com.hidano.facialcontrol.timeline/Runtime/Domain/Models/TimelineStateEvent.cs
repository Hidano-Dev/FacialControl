using System;
using UnityEngine;

namespace Hidano.FacialControl.Timeline.Domain.Models
{
    [Serializable]
    public struct TimelineStateEvent
    {
        public const byte KindOn = 0;
        public const byte KindOff = 1;

        [SerializeField] private double timeSeconds;
        [SerializeField] private byte kind;
        [SerializeField] private string expressionId;
        [SerializeField] private string layerName;
        [SerializeField] private bool startsActive;

        public TimelineStateEvent(double timeSeconds, byte kind, string expressionId, string layerName)
            : this(timeSeconds, kind, expressionId, layerName, startsActive: false)
        {
        }

        public TimelineStateEvent(double timeSeconds, byte kind, string expressionId, string layerName, bool startsActive)
        {
            this.timeSeconds = timeSeconds;
            this.kind = kind;
            this.expressionId = expressionId;
            this.layerName = layerName;
            this.startsActive = startsActive;
        }

        public double TimeSeconds => timeSeconds;

        public byte Kind => kind;

        public string ExpressionId => expressionId;

        public string LayerName => layerName;

        /// <summary>
        /// On のみ。Timeline の先頭で、遷移なしで最初から有効になる（<see cref="Clips.FacialExpressionClip.StartsActive"/>）。
        /// </summary>
        public bool StartsActive => startsActive;

        public bool IsOn => Kind == KindOn;

        public bool IsOff => Kind == KindOff;
    }
}
