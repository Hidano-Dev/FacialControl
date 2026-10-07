namespace Hidano.FacialControl.Adapters.IFacialMocap
{
    /// <summary>
    /// iFacialMocap の BlendShape 受信値（0〜1 に正規化済み）へマッピングごとに掛ける調整値。
    /// </summary>
    /// <remarks>
    /// 適用順は「入力範囲の再マップ → ×Weight → clamp01」。
    /// <list type="number">
    /// <item><description>再マップ: <c>(v - Min) / (Max - Min)</c> を 0〜1 にクランプする。
    /// Min 未満は 0、Max 以上は 1 になる（受信値の有効範囲を切り出して 0〜1 に引き伸ばす）。
    /// <c>Min == Max</c> のときは <c>v &gt;= Max</c> で 1、それ以外は 0 の段階関数とする。</description></item>
    /// <item><description>Weight を掛ける。</description></item>
    /// <item><description>最終値を 0〜1 にクランプする（BlendShape 入力源の値域）。</description></item>
    /// </list>
    /// <see cref="Identity"/>（Min 0 / Max 1 / Weight 1）では 0〜1 の入力をそのまま返す。
    /// </remarks>
    public readonly struct IFacialMocapValueTuning
    {
        public const float DefaultMin = 0f;
        public const float DefaultMax = 1f;
        public const float DefaultWeight = 1f;

        /// <summary>従来どおりの出力になる既定値（Min 0 / Max 1 / Weight 1）。</summary>
        public static IFacialMocapValueTuning Identity =>
            new IFacialMocapValueTuning(DefaultMin, DefaultMax, DefaultWeight);

        public readonly float Min;
        public readonly float Max;
        public readonly float Weight;

        /// <summary>
        /// 調整値を作る。<paramref name="min"/> / <paramref name="max"/> は 0〜1 にクランプし、
        /// <paramref name="min"/> が <paramref name="max"/> を超える場合は入れ替える。
        /// </summary>
        public IFacialMocapValueTuning(float min, float max, float weight)
        {
            min = Clamp01(min);
            max = Clamp01(max);
            if (min > max)
            {
                float tmp = min;
                min = max;
                max = tmp;
            }

            Min = min;
            Max = max;
            Weight = weight;
        }

        /// <summary>正規化済みの受信値 <paramref name="value"/> に再マップ → ×Weight → clamp01 を適用する。</summary>
        public float Apply(float value)
        {
            float range = Max - Min;
            float remapped;
            if (range > 0f)
            {
                remapped = Clamp01((value - Min) / range);
            }
            else
            {
                remapped = value >= Max ? 1f : 0f;
            }

            return Clamp01(remapped * Weight);
        }

        private static float Clamp01(float value)
        {
            if (value < 0f)
            {
                return 0f;
            }

            return value > 1f ? 1f : value;
        }
    }
}
