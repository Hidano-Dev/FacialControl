using System;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using UnityEngine;

namespace Hidano.FacialControl.Timeline.Adapters.InputSources
{
    /// <summary>
    /// Timeline-owned gaze sink. Values remain unclamped and are invalid outside active playback.
    /// </summary>
    /// <remarks>差し替え元の退避 / 解除と占有規則（<see cref="IInjectedInputSource"/>）は基底 <see cref="TimelineAnalogInputSource"/> が持つ。</remarks>
    public sealed class TimelineGazeInputSource : TimelineAnalogInputSource
    {
        public TimelineGazeInputSource(InputSourceId id)
            : base(id, axisCount: 2)
        {
        }

        public void Publish(Vector2 value)
        {
            Publish(value.x, value.y);
        }

        public void Publish(float x, float y)
        {
            SetAxis(0, x);
            SetAxis(1, y);
        }

        public void PublishZero()
        {
            Publish(0f, 0f);
        }
    }
}
