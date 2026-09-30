using System;
using System.Collections.Generic;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Adapters.ScriptableObject;
using Hidano.FacialControl.Domain.Interfaces;
using UnityEngine;

namespace Hidano.FacialControl.Adapters.Bone
{
    /// <summary>
    /// <see cref="GazeChannel"/> を毎フレーム評価し、左右目ボーンに直接 localRotation を書込む
    /// 目線ボーン専用 provider。アナログ入力 (Vector2) を yaw / pitch 角度に変換し、
    /// 設定された外側/内側/上下の角度制限と各ボーンの参照モデル時取得 local 軸を用いて
    /// <c>Quaternion.AngleAxis</c> 合成で姿勢を計算する。
    /// </summary>
    /// <remarks>
    /// <para>
    /// AnalogBonePoseProvider と異なり <see cref="IBonePoseProvider"/> 経由で snapshot を流す
    /// のではなく、bone の <see cref="Transform.localRotation"/> を直接書換える。これは目線回転が
    /// Euler 加算では正しく表現できず、各ボーン固有の rest pose と parent frame 軸を用いた
    /// quaternion 合成が必要なためである。
    /// </para>
    /// <para>
    /// 適用順は <c>localRotation = AngleAxis(yaw, yawAxisLocal) * AngleAxis(pitch, pitchAxisLocal) * Euler(rest)</c>。
    /// yaw を最も外側 (最後に適用) にすることで、視線左右が常に「水平」に動くように見える。
    /// </para>
    /// <para>
    /// 左右非対称制限: input.x が外側方向のとき outerYawAngle、内側方向のとき innerYawAngle で線形駆動する。
    /// 「外側」はキャラの鼻から見て当該の眼が遠ざかる側、すなわち左目では input.x &lt; 0、右目では input.x &gt; 0 の方向。
    /// </para>
    /// <para>
    /// <see cref="Dispose"/> 時には書込み開始前のオリジナル localRotation に各ボーンを復元する。
    /// </para>
    /// <para>
    /// 入力源 (<see cref="IAnalogInputSource"/>) と <see cref="GazeChannel"/> のペアは
    /// <see cref="GazeBoneBinding"/> として呼出側で解決済みの状態で渡す。これにより本クラスは
    /// Unity InputSystem・OSC・ARKit などの具体的な入力方式に依存しない。
    /// </para>
    /// </remarks>
    public sealed class GazeBonePoseProvider : IDisposable
    {
        private readonly BoneTransformResolver _resolver;
        private readonly EyeBinding[] _bindings;
        private bool _disposed;

        /// <summary>
        /// <see cref="GazeBonePoseProvider"/> を構築する。目ボーン path が未指定の側は駆動しない。
        /// </summary>
        /// <param name="resolver">ボーン名から Transform を解決するリゾルバー (FacialController と同じものを共有)。</param>
        /// <param name="bindings"><see cref="GazeChannel"/> と入力源のペア配列。</param>
        public GazeBonePoseProvider(
            BoneTransformResolver resolver,
            IReadOnlyList<GazeBoneBinding> bindings)
            : this(resolver, bindings, default, useEyeFallback: false)
        {
        }

        /// <summary>
        /// <see cref="GazeBonePoseProvider"/> を構築する。目ボーン path が未指定の側は
        /// <paramref name="eyeFallback"/> の目ボーン (Humanoid の LeftEye / RightEye) を駆動する。
        /// </summary>
        /// <param name="resolver">ボーン名から Transform を解決するリゾルバー (FacialController と同じものを共有)。</param>
        /// <param name="bindings"><see cref="GazeChannel"/> と入力源のペア配列。</param>
        /// <param name="eyeFallback">
        /// path 未指定時に使う目ボーン。rest 回転と yaw / pitch 軸は <paramref name="eyeFallback"/> が
        /// 構築時に導出した値を使い、GazeChannel に保存された InitialRotation / YawAxisLocal / PitchAxisLocal は使わない。
        /// 目ごとに、path 未指定の最初の channel だけが fallback の目ボーンを駆動する。path 指定の binding が
        /// 同じボーンを指す場合は path 指定側を優先し、fallback 側は駆動しない。
        /// </param>
        public GazeBonePoseProvider(
            BoneTransformResolver resolver,
            IReadOnlyList<GazeBoneBinding> bindings,
            GazeEyeBoneFallback eyeFallback)
            : this(resolver, bindings, eyeFallback, useEyeFallback: true)
        {
        }

        /// <param name="useEyeFallback">
        /// false のとき path 未指定の目は fallback を探さずに駆動しない。fallback を要求しない旧オーバーロードで
        /// <see cref="HasUnresolvedFallbackEye"/> を立てないために使う。
        /// </param>
        private GazeBonePoseProvider(
            BoneTransformResolver resolver,
            IReadOnlyList<GazeBoneBinding> bindings,
            GazeEyeBoneFallback eyeFallback,
            bool useEyeFallback)
        {
            _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
            if (bindings == null) throw new ArgumentNullException(nameof(bindings));

            var list = new List<EyeBinding>(bindings.Count * 2);
            // fallback の目ボーンは、path 未指定の最初の channel だけが使う。path 未指定の channel が
            // 複数あっても同じ Humanoid の目を毎フレーム奪い合わないようにするため。
            bool leftFallbackClaimed = false;
            bool rightFallbackClaimed = false;
            for (int i = 0; i < bindings.Count; i++)
            {
                var cfg = bindings[i].Channel;
                var leftSource = bindings[i].LeftSource ?? bindings[i].Source;
                var rightSource = bindings[i].RightSource ?? bindings[i].Source;
                if (cfg == null || (leftSource == null && rightSource == null)) continue;

                if (leftSource != null)
                {
                    TryAddEye(
                        list,
                        cfg,
                        leftSource,
                        cfg.leftEyeBonePath,
                        cfg.leftEyeInitialRotation,
                        cfg.leftEyeYawAxisLocal,
                        cfg.leftEyePitchAxisLocal,
                        isLeftEye: true,
                        useEyeFallback,
                        eyeFallback.Left,
                        ref leftFallbackClaimed);
                }
                if (rightSource != null)
                {
                    TryAddEye(
                        list,
                        cfg,
                        rightSource,
                        cfg.rightEyeBonePath,
                        cfg.rightEyeInitialRotation,
                        cfg.rightEyeYawAxisLocal,
                        cfg.rightEyePitchAxisLocal,
                        isLeftEye: false,
                        useEyeFallback,
                        eyeFallback.Right,
                        ref rightFallbackClaimed);
                }
            }

            RemoveFallbackEyesOwnedByPath(list);
            _bindings = list.Count == 0 ? Array.Empty<EyeBinding>() : list.ToArray();
        }

        /// <summary>
        /// 目ボーン path が未指定で、かつ fallback の目ボーン (Humanoid の LeftEye / RightEye) も
        /// 無かったために駆動できない目が 1 つ以上あるとき true。警告の要否判定に使う。
        /// </summary>
        public bool HasUnresolvedFallbackEye { get; private set; }

        private void TryAddEye(
            List<EyeBinding> list,
            GazeChannel cfg,
            IAnalogInputSource source,
            string bonePath,
            Vector3 initialRotation,
            Vector3 yawAxisLocal,
            Vector3 pitchAxisLocal,
            bool isLeftEye,
            bool useEyeFallback,
            GazeEyeBoneFallback.FallbackEye fallbackEye,
            ref bool fallbackClaimed)
        {
            if (!string.IsNullOrWhiteSpace(bonePath))
            {
                list.Add(new EyeBinding(
                    source,
                    bonePath,
                    null,
                    Quaternion.Euler(initialRotation),
                    SafeNormalize(yawAxisLocal, Vector3.up),
                    SafeNormalize(pitchAxisLocal, Vector3.right),
                    isLeftEye,
                    cfg.outerYawAngle,
                    cfg.innerYawAngle,
                    cfg.lookUpAngle,
                    cfg.lookDownAngle));
                return;
            }

            if (!useEyeFallback || fallbackClaimed)
            {
                return;
            }

            if (fallbackEye.Bone == null)
            {
                HasUnresolvedFallbackEye = true;
                return;
            }

            fallbackClaimed = true;
            list.Add(new EyeBinding(
                source,
                string.Empty,
                fallbackEye.Bone,
                fallbackEye.RestRotation,
                fallbackEye.YawAxisLocal,
                fallbackEye.PitchAxisLocal,
                isLeftEye,
                cfg.outerYawAngle,
                cfg.innerYawAngle,
                cfg.lookUpAngle,
                cfg.lookDownAngle));
        }

        /// <summary>
        /// path 指定の binding と同じ Transform を指す fallback binding を取り除く。
        /// path 指定と fallback が混在するときだけ path を解決する (構築時の 1 回のみ)。
        /// </summary>
        private void RemoveFallbackEyesOwnedByPath(List<EyeBinding> list)
        {
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (!string.IsNullOrEmpty(list[i].BonePath))
                {
                    continue;
                }

                var fallbackTarget = list[i].CachedTarget;
                for (int j = 0; j < list.Count; j++)
                {
                    if (!string.IsNullOrEmpty(list[j].BonePath)
                        && _resolver.Resolve(list[j].BonePath) == fallbackTarget)
                    {
                        list.RemoveAt(i);
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// per-frame に呼出され、各 GazeChannel の入力を読んで両目の <see cref="Transform.localRotation"/> を計算/書込みする。
        /// </summary>
        public void Apply()
        {
            if (_disposed)
            {
                return;
            }

            for (int i = 0; i < _bindings.Length; i++)
            {
                ref var b = ref _bindings[i];
                var target = ResolveTarget(ref b);
                if (target == null)
                {
                    continue;
                }

                if (!b.HasInitialSnapshot)
                {
                    b.InitialLocalRotation = target.localRotation;
                    b.HasInitialSnapshot = true;
                }

                if (!GazeInputReader.TryReadXY(b.Source, out float ix, out float iy))
                {
                    target.localRotation = b.RestRotation;
                    continue;
                }

                float yawDeg;
                if (b.IsLeftEye)
                {
                    // 左目: 後段の `yawDeg = -yawDeg` 反転と Unity 既定 (+Y/+X) 軸配置の組合せを踏まえ、
                    // input.x > 0 のとき左目は外側、input.x < 0 のとき内側方向に振れる。
                    yawDeg = ix >= 0f
                        ? ix * b.OuterYawAngle
                        : ix * b.InnerYawAngle;
                }
                else
                {
                    // 右目: 同様に input.x > 0 のとき内側、input.x < 0 のとき外側に振れる。
                    yawDeg = ix >= 0f
                        ? ix * b.InnerYawAngle
                        : ix * b.OuterYawAngle;
                }

                float pitchDeg = iy >= 0f
                    ? iy * b.LookUpAngle
                    : iy * b.LookDownAngle;

                // Unity の Quaternion.AngleAxis は左手系で「軸方向を見て時計回り = 正」。
                // 参照モデルから自動取得した yawAxisLocal (+Y) / pitchAxisLocal (+X) と組み合わせると、
                // input.x > 0 で視線が左、input.y > 0 で視線が下に振れる (上下左右とも反転)。
                // InputActionAsset 側 Invert で毎回吸収するのは手間なのでコード側で符号反転する。
                yawDeg = -yawDeg;
                pitchDeg = -pitchDeg;

                var yawRot = Quaternion.AngleAxis(yawDeg, b.YawAxisLocal);
                var pitchRot = Quaternion.AngleAxis(pitchDeg, b.PitchAxisLocal);

                target.localRotation = yawRot * pitchRot * b.RestRotation;
            }
        }

        /// <summary>
        /// 書込中だった bone の <see cref="Transform.localRotation"/> を最初の書込み直前の値に戻す。
        /// </summary>
        public void RestoreInitialRotations()
        {
            // 同じボーンを複数の binding が書く場合、後の binding の snapshot は前の binding の書込み後の値になる。
            // 逆順に戻すことで、最後に最初の binding の snapshot (書込み前の値) が残るようにする。
            for (int i = _bindings.Length - 1; i >= 0; i--)
            {
                ref var b = ref _bindings[i];
                if (!b.HasInitialSnapshot)
                {
                    continue;
                }
                var target = ResolveTarget(ref b);
                if (target == null)
                {
                    continue;
                }
                target.localRotation = b.InitialLocalRotation;
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed) return;
            RestoreInitialRotations();
            _disposed = true;
        }

        private Transform ResolveTarget(ref EyeBinding b)
        {
            if (b.CachedTarget != null)
            {
                return b.CachedTarget;
            }
            b.CachedTarget = _resolver.Resolve(b.BonePath);
            return b.CachedTarget;
        }

        private static Vector3 SafeNormalize(Vector3 v, Vector3 fallback)
        {
            if (v.sqrMagnitude < 1e-8f)
            {
                return fallback;
            }
            return v.normalized;
        }

        private struct EyeBinding
        {
            public readonly IAnalogInputSource Source;
            public readonly string BonePath;
            public readonly Quaternion RestRotation;
            public readonly Vector3 YawAxisLocal;
            public readonly Vector3 PitchAxisLocal;
            public readonly bool IsLeftEye;
            public readonly float OuterYawAngle;
            public readonly float InnerYawAngle;
            public readonly float LookUpAngle;
            public readonly float LookDownAngle;

            public Transform CachedTarget;
            public Quaternion InitialLocalRotation;
            public bool HasInitialSnapshot;

            public EyeBinding(
                IAnalogInputSource source,
                string bonePath,
                Transform preResolvedTarget,
                Quaternion restRotation,
                Vector3 yawAxisLocal,
                Vector3 pitchAxisLocal,
                bool isLeftEye,
                float outerYawAngle,
                float innerYawAngle,
                float lookUpAngle,
                float lookDownAngle)
            {
                Source = source;
                BonePath = bonePath;
                RestRotation = restRotation;
                YawAxisLocal = yawAxisLocal;
                PitchAxisLocal = pitchAxisLocal;
                IsLeftEye = isLeftEye;
                OuterYawAngle = Mathf.Max(0f, outerYawAngle);
                InnerYawAngle = Mathf.Max(0f, innerYawAngle);
                LookUpAngle = Mathf.Max(0f, lookUpAngle);
                LookDownAngle = Mathf.Max(0f, lookDownAngle);

                CachedTarget = preResolvedTarget;
                InitialLocalRotation = Quaternion.identity;
                HasInitialSnapshot = false;
            }
        }
    }
}
