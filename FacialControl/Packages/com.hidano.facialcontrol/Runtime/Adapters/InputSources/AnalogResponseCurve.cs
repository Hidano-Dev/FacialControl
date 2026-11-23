using UnityEngine;

namespace Hidano.FacialControl.Adapters.InputSources
{
    /// <summary>
    /// アナログ入力 (0..1 の連続値) に応答カーブを掛けるヘルパー (HID-427)。
    /// 半押しでは表情をあまり出さず奥で一気に出す、といったバイアスを掛けるのに使う。
    /// </summary>
    /// <remarks>
    /// カーブが null またはキーが無い場合は入力をそのまま返す (既存データは直線と同じ扱い)。
    /// <see cref="AnimationCurve.Evaluate"/> はヒープ確保をしないため、毎フレーム呼んでよい。
    /// </remarks>
    public static class AnalogResponseCurve
    {
        /// <summary><paramref name="curve"/> で <paramref name="value"/> を変換する。カーブが無ければ入力をそのまま返す。</summary>
        public static float Evaluate(AnimationCurve curve, float value)
        {
            if (curve == null || curve.length == 0)
            {
                return value;
            }

            return curve.Evaluate(value);
        }

        /// <summary>既定の応答カーブ (0→0、1→1 の直線。入力をそのまま使うのと同じ)。</summary>
        public static AnimationCurve CreateLinear()
        {
            return AnimationCurve.Linear(0f, 0f, 1f, 1f);
        }
    }
}
