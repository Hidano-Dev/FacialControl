using UnityEngine;

namespace Hidano.FacialControl.ExpressionCreator
{
    public readonly struct PreviewInputFrame
    {
        public readonly EventType EventType;
        public readonly int Button;
        public readonly Vector2 MousePosition;
        public readonly Vector2 Delta;
        public readonly Vector2 ScrollDelta;
        public readonly bool Alt;

        public PreviewInputFrame(
            EventType eventType,
            int button,
            Vector2 mousePosition,
            Vector2 delta,
            Vector2 scrollDelta,
            bool alt)
        {
            EventType     = eventType;
            Button        = button;
            MousePosition = mousePosition;
            Delta         = delta;
            ScrollDelta   = scrollDelta;
            Alt           = alt;
        }
    }
}
