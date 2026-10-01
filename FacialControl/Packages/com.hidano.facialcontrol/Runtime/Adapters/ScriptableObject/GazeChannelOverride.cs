using System;

namespace Hidano.FacialControl.Adapters.ScriptableObject
{
    /// <summary>
    /// 外部 (OSC 送信側など) から受け取った、1 つの <see cref="GazeChannel"/> に対する上書き値。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 目ボーン path は左右それぞれ任意で、null・空白は「上書きなし」を表す。上書きなしの側は
    /// 受信側ローカルの規則 (ローカル path → Humanoid の目ボーン) で解決する。
    /// </para>
    /// <para>
    /// 可動範囲 (上下・外側・内側の角度) は 4 値をまとめて上書きする。<see cref="HasAngleLimits"/> が
    /// false のときはローカルの値を使う。
    /// </para>
    /// </remarks>
    public readonly struct GazeChannelOverride : IEquatable<GazeChannelOverride>
    {
        public const float MinAngle = 0f;
        public const float MaxAngle = 90f;

        public GazeChannelOverride(
            string leftEyeBonePath,
            string rightEyeBonePath,
            bool hasAngleLimits,
            float lookUpAngle,
            float lookDownAngle,
            float outerYawAngle,
            float innerYawAngle)
        {
            LeftEyeBonePath = IsBlank(leftEyeBonePath) ? null : leftEyeBonePath;
            RightEyeBonePath = IsBlank(rightEyeBonePath) ? null : rightEyeBonePath;
            HasAngleLimits = hasAngleLimits;
            LookUpAngle = hasAngleLimits ? ClampAngle(lookUpAngle) : 0f;
            LookDownAngle = hasAngleLimits ? ClampAngle(lookDownAngle) : 0f;
            OuterYawAngle = hasAngleLimits ? ClampAngle(outerYawAngle) : 0f;
            InnerYawAngle = hasAngleLimits ? ClampAngle(innerYawAngle) : 0f;
        }

        /// <summary>左目ボーンの上書き path。上書きしない場合は null。</summary>
        public string LeftEyeBonePath { get; }

        /// <summary>右目ボーンの上書き path。上書きしない場合は null。</summary>
        public string RightEyeBonePath { get; }

        /// <summary>可動範囲 4 値を上書きするとき true。</summary>
        public bool HasAngleLimits { get; }

        public float LookUpAngle { get; }
        public float LookDownAngle { get; }
        public float OuterYawAngle { get; }
        public float InnerYawAngle { get; }

        public bool HasLeftEyeBonePath => LeftEyeBonePath != null;
        public bool HasRightEyeBonePath => RightEyeBonePath != null;

        /// <summary>上書きする値が 1 つも無いとき true。</summary>
        public bool IsEmpty => !HasLeftEyeBonePath && !HasRightEyeBonePath && !HasAngleLimits;

        public GazeChannelOverride WithLeftEyeBonePath(string path)
        {
            return new GazeChannelOverride(
                path, RightEyeBonePath, HasAngleLimits,
                LookUpAngle, LookDownAngle, OuterYawAngle, InnerYawAngle);
        }

        public GazeChannelOverride WithRightEyeBonePath(string path)
        {
            return new GazeChannelOverride(
                LeftEyeBonePath, path, HasAngleLimits,
                LookUpAngle, LookDownAngle, OuterYawAngle, InnerYawAngle);
        }

        public GazeChannelOverride WithAngleLimits(
            float lookUpAngle,
            float lookDownAngle,
            float outerYawAngle,
            float innerYawAngle)
        {
            return new GazeChannelOverride(
                LeftEyeBonePath, RightEyeBonePath, true,
                lookUpAngle, lookDownAngle, outerYawAngle, innerYawAngle);
        }

        /// <summary>
        /// <paramref name="local"/> の複製に上書き値を適用して返す。<paramref name="local"/> 自体は変更しない。
        /// </summary>
        /// <remarks>
        /// 目ボーン path を上書きした側の InitialRotation / YawAxisLocal / PitchAxisLocal は
        /// <paramref name="local"/> の値 (ローカル path 用にエディタで保存した値) のまま複製する。
        /// 上書き path のボーンに合う値は呼出側 (FacialController) が実行時に導出して差し替える。
        /// </remarks>
        public GazeChannel ApplyTo(GazeChannel local)
        {
            if (local == null)
            {
                throw new ArgumentNullException(nameof(local));
            }

            GazeChannel merged = local.Clone();
            if (HasLeftEyeBonePath)
            {
                merged.leftEyeBonePath = LeftEyeBonePath;
            }

            if (HasRightEyeBonePath)
            {
                merged.rightEyeBonePath = RightEyeBonePath;
            }

            if (HasAngleLimits)
            {
                merged.lookUpAngle = LookUpAngle;
                merged.lookDownAngle = LookDownAngle;
                merged.outerYawAngle = OuterYawAngle;
                merged.innerYawAngle = InnerYawAngle;
            }

            return merged;
        }

        public bool Equals(GazeChannelOverride other)
        {
            return string.Equals(LeftEyeBonePath, other.LeftEyeBonePath, StringComparison.Ordinal)
                && string.Equals(RightEyeBonePath, other.RightEyeBonePath, StringComparison.Ordinal)
                && HasAngleLimits == other.HasAngleLimits
                && LookUpAngle.Equals(other.LookUpAngle)
                && LookDownAngle.Equals(other.LookDownAngle)
                && OuterYawAngle.Equals(other.OuterYawAngle)
                && InnerYawAngle.Equals(other.InnerYawAngle);
        }

        public override bool Equals(object obj)
        {
            return obj is GazeChannelOverride other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + (LeftEyeBonePath != null ? StringComparer.Ordinal.GetHashCode(LeftEyeBonePath) : 0);
                hash = hash * 31 + (RightEyeBonePath != null ? StringComparer.Ordinal.GetHashCode(RightEyeBonePath) : 0);
                hash = hash * 31 + HasAngleLimits.GetHashCode();
                hash = hash * 31 + LookUpAngle.GetHashCode();
                hash = hash * 31 + LookDownAngle.GetHashCode();
                hash = hash * 31 + OuterYawAngle.GetHashCode();
                hash = hash * 31 + InnerYawAngle.GetHashCode();
                return hash;
            }
        }

        private static bool IsBlank(string value)
        {
            return string.IsNullOrWhiteSpace(value);
        }

        private static float ClampAngle(float value)
        {
            if (float.IsNaN(value) || value < MinAngle)
            {
                return MinAngle;
            }

            return value > MaxAngle ? MaxAngle : value;
        }
    }
}
