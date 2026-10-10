using System;

namespace Hidano.FacialControl.LipSync.Adapters
{
    /// <summary>
    /// 発話ゲート。uLipSync の正規化済み音量（activity）から「発話中か」を判定し、
    /// レイヤー weight に書く gate weight（0〜1）を Attack / Release で線形に動かす。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 無言時は下位レイヤー（iFacialMocap 等のキャプチャ）の口をそのまま出し、発話中だけ
    /// uLipSync のレイヤーで口を置き換えるために使う。判定はヒステリシス付き:
    /// <c>activity &gt;= On</c> で発話開始、発話中は <c>activity &gt;= Off</c> の間継続し、
    /// 下回った状態が <see cref="HoldTime"/> 続くと発話終了。Off が On より大きい設定は On と同じ扱い。
    /// しきい値が 0 以下のときは <c>activity &gt; 0</c> で判定する（<c>&gt;= 0</c> は常に成立するため）。
    /// </para>
    /// <para>
    /// uLipSync のコールバックが <see cref="StaleTimeout"/> 秒届かなければ activity = 0 として扱い、
    /// Hold / Release を経て閉じる（マイク停止・デバイス抜けで口が開いたまま残らないように）。
    /// </para>
    /// <para>毎フレーム処理でヒープ確保しない。</para>
    /// </remarks>
    public sealed class ULipSyncVoiceGate
    {
        public const float DefaultOnThreshold = 0.05f;
        public const float DefaultOffThreshold = 0.02f;
        public const float DefaultHoldTime = 0.2f;
        public const float DefaultAttackTime = 0.05f;
        public const float DefaultReleaseTime = 0.2f;
        public const float DefaultStaleTimeout = 0.5f;

        private float _belowOffElapsed;
        private float _sinceLastInput;

        /// <summary>発話開始のしきい値。</summary>
        public float OnThreshold { get; set; } = DefaultOnThreshold;

        /// <summary>発話継続のしきい値。On より大きい値は On と同じ扱い。</summary>
        public float OffThreshold { get; set; } = DefaultOffThreshold;

        /// <summary>Off を下回ってから発話終了とみなすまでの秒数。</summary>
        public float HoldTime { get; set; } = DefaultHoldTime;

        /// <summary>gate weight が 0 → 1 に上がる秒数（0 以下で即時）。</summary>
        public float AttackTime { get; set; } = DefaultAttackTime;

        /// <summary>gate weight が 1 → 0 に下がる秒数（0 以下で即時）。</summary>
        public float ReleaseTime { get; set; } = DefaultReleaseTime;

        /// <summary>入力が途絶えてから activity = 0 とみなすまでの秒数（0 以下で無効）。</summary>
        public float StaleTimeout { get; set; } = DefaultStaleTimeout;

        /// <summary>直近の <see cref="Tick"/> で判定に使った activity（0〜1。途絶中は 0）。</summary>
        public float Activity { get; private set; }

        /// <summary>発話中か。</summary>
        public bool IsSpeaking { get; private set; }

        /// <summary>レイヤー weight に書く値（0〜1）。</summary>
        public float Weight { get; private set; }

        /// <summary>入力が <see cref="StaleTimeout"/> 以上途絶えているか。</summary>
        public bool IsStale => StaleTimeout > 0f && _sinceLastInput >= StaleTimeout;

        /// <summary>
        /// 1 フレーム進める。
        /// </summary>
        /// <param name="deltaTime">前フレームからの経過秒数（負値は 0 扱い）。</param>
        /// <param name="activity">現在の activity（0〜1 にクランプする）。</param>
        /// <param name="inputArrived">前回の Tick 以降に入力（uLipSync のコールバック）が届いたか。</param>
        /// <returns>更新後の <see cref="Weight"/>。</returns>
        public float Tick(float deltaTime, float activity, bool inputArrived)
        {
            float dt = deltaTime > 0f ? deltaTime : 0f;

            if (inputArrived)
            {
                _sinceLastInput = 0f;
            }
            else
            {
                _sinceLastInput += dt;
            }

            float a = IsStale ? 0f : Clamp01(activity);
            Activity = a;

            float on = OnThreshold;
            float off = OffThreshold > on ? on : OffThreshold;

            if (!IsSpeaking)
            {
                if (Passes(a, on))
                {
                    IsSpeaking = true;
                    _belowOffElapsed = 0f;
                }
            }
            else if (Passes(a, off))
            {
                _belowOffElapsed = 0f;
            }
            else
            {
                _belowOffElapsed += dt;
                if (_belowOffElapsed >= HoldTime)
                {
                    IsSpeaking = false;
                    _belowOffElapsed = 0f;
                }
            }

            float target = IsSpeaking ? 1f : 0f;
            float weight = Weight;
            if (weight < target)
            {
                weight = AttackTime > 0f ? Math.Min(target, weight + dt / AttackTime) : target;
            }
            else if (weight > target)
            {
                weight = ReleaseTime > 0f ? Math.Max(target, weight - dt / ReleaseTime) : target;
            }

            Weight = weight;
            return weight;
        }

        /// <summary>判定状態と weight を初期状態（無言・weight 0）に戻す。</summary>
        public void Reset()
        {
            IsSpeaking = false;
            Weight = 0f;
            Activity = 0f;
            _belowOffElapsed = 0f;
            _sinceLastInput = 0f;
        }

        private static bool Passes(float activity, float threshold)
        {
            return threshold <= 0f ? activity > 0f : activity >= threshold;
        }

        private static float Clamp01(float value)
        {
            if (value < 0f || float.IsNaN(value))
            {
                return 0f;
            }

            return value > 1f ? 1f : value;
        }
    }
}
