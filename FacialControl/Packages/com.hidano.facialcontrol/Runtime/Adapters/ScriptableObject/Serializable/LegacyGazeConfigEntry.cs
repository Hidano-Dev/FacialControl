using System;
namespace Hidano.FacialControl.Adapters.ScriptableObject.Serializable
{
    [Serializable] public sealed class LegacyGazeConfigEntry
    {
        public string expressionId;
        public string leftEyeBonePath;
        public string rightEyeBonePath;
        public float lookUpAngle = 15f, lookDownAngle = 9f, outerYawAngle = 15f, innerYawAngle = 18f;
        public GazeChannel ToChannel() => new GazeChannel { id = expressionId ?? string.Empty, leftEyeBonePath = leftEyeBonePath ?? string.Empty, rightEyeBonePath = rightEyeBonePath ?? string.Empty, lookUpAngle = lookUpAngle, lookDownAngle = lookDownAngle, outerYawAngle = outerYawAngle, innerYawAngle = innerYawAngle };
    }
}
