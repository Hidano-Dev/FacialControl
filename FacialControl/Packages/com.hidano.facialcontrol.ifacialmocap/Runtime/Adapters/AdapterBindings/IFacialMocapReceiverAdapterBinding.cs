using System;
using System.Collections;
using System.Collections.Generic;
using Hidano.FacialControl.Adapters.Bone;
using Hidano.FacialControl.Adapters.IFacialMocap;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Adapters.OSC;
using Hidano.FacialControl.Adapters.RuntimeSettings;
using Hidano.FacialControl.Adapters.ScriptableObject;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using UnityEngine;

namespace Hidano.FacialControl.Adapters.AdapterBindings
{
    /// <summary>
    /// iFacialMocap 名 → メッシュ BlendShape 名の上書きマッピング 1 件と、その受信値の調整
    /// （使用する/しない・入力範囲 Min/Max・Weight）。
    /// 空リストのときは <see cref="IFacialMocapBlendShapeCatalog"/> の既定変換を全 52 件に適用する。
    /// </summary>
    /// <remarks>
    /// 調整値は <see cref="tuningVersion"/> が 0 のとき（調整項目の追加前に保存されたアセットや、
    /// 空リストへ Inspector の「+」で最初に追加した 0 初期化の要素）は無視し、既定値（使用する / Min 0 /
    /// Max 1 / Weight 1）で扱う。struct のフィールドは 0 初期化されるため、Max と Weight の既定値 1 を
    /// フィールド値だけでは表せないことへの対処。Drawer で調整値を編集すると <see cref="CurrentTuningVersion"/>
    /// が書き込まれる。コードから調整値を設定する場合は 6 引数の ctor を使う（オブジェクト初期化子で
    /// <see cref="enabled"/> 等だけを代入すると <see cref="tuningVersion"/> が 0 のままで無視される）。
    /// </remarks>
    [Serializable]
    public struct IFacialMocapBlendShapeMapping
    {
        /// <summary>調整値（<see cref="enabled"/> / <see cref="rangeMin"/> / <see cref="rangeMax"/> / <see cref="weight"/>）が有効な版。</summary>
        public const int CurrentTuningVersion = 1;

        [Tooltip("iFacialMocap の BlendShape 名（例: eyeBlink_L）。")]
        public string ifacialMocapName;

        [Tooltip("反映先メッシュ BlendShape 名。空ならスキップ。")]
        public string blendShapeName;

        [Tooltip("オフのマッピングは値を出力しない（マッピング自体は残す）。")]
        public bool enabled;

        [Tooltip("受信値の有効範囲の下限（0〜1）。下限以下は 0 になる。")]
        public float rangeMin;

        [Tooltip("受信値の有効範囲の上限（0〜1）。上限以上は 1 になる。")]
        public float rangeMax;

        [Tooltip("範囲を再マップした値に掛ける倍率。結果は 0〜1 にクランプする。")]
        public float weight;

        [Tooltip("0 なら調整値を無視して既定値で扱う（旧アセット互換）。")]
        public int tuningVersion;

        /// <summary>調整値は既定（使用する / Min 0 / Max 1 / Weight 1）で作る。</summary>
        public IFacialMocapBlendShapeMapping(string ifacialMocapName, string blendShapeName)
            : this(
                ifacialMocapName,
                blendShapeName,
                true,
                IFacialMocapValueTuning.DefaultMin,
                IFacialMocapValueTuning.DefaultMax,
                IFacialMocapValueTuning.DefaultWeight)
        {
        }

        public IFacialMocapBlendShapeMapping(
            string ifacialMocapName,
            string blendShapeName,
            bool enabled,
            float rangeMin,
            float rangeMax,
            float weight)
        {
            this.ifacialMocapName = ifacialMocapName;
            this.blendShapeName = blendShapeName;
            this.enabled = enabled;
            this.rangeMin = rangeMin;
            this.rangeMax = rangeMax;
            this.weight = weight;
            tuningVersion = CurrentTuningVersion;
        }

        /// <summary>調整値が保存されているか（false なら既定値で扱う）。</summary>
        public bool HasTuning => tuningVersion >= CurrentTuningVersion;

        /// <summary>実際に使う「使用する/しない」。調整値が無ければ true。</summary>
        public bool EffectiveEnabled => !HasTuning || enabled;

        /// <summary>実際に使う調整値。調整値が無ければ <see cref="IFacialMocapValueTuning.Identity"/>。</summary>
        public IFacialMocapValueTuning EffectiveTuning =>
            HasTuning ? new IFacialMocapValueTuning(rangeMin, rangeMax, weight) : IFacialMocapValueTuning.Identity;
    }

    /// <summary>
    /// iFacialMocap (iOS) の UDP テキストを受信し、BlendShape・視線・頭部を FacialController に流し込む
    /// <see cref="AdapterBindingBase"/> 具象。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="OnStart"/> で <c>ctx.HostGameObject.AddComponent&lt;IFacialMocapReceiverHost&gt;()</c> を実行し、
    /// BlendShape は <c>.osc</c> の <see cref="OscDoubleBuffer"/> + <see cref="OscInputSource"/>、
    /// 視線は左右 <see cref="GazeVector2InputSource"/>、頭部は <see cref="AnalogAxesInputSource"/> を
    /// <see cref="IInputSourceRegistry"/> に登録する。
    /// </para>
    /// <para>
    /// <see cref="OnFixedTick"/> で host の最新フレームを取得し、新規フレームのときだけ buffer へ書き込んで
    /// <see cref="OscDoubleBuffer.Swap"/> し、視線/頭部を push する（新規でないフレームは前値を保持）。
    /// </para>
    /// <para>
    /// 視線→目ボーン、頭部→頭ボーンの結線は Profile 側（<c>GazeChannel</c> /
    /// <c>AnalogBindingEntry</c> の BonePose）の責務。本 binding は入力源の登録までを担う。
    /// </para>
    /// </remarks>
    [Serializable]
    [FacialAdapterBinding(displayName: "iFacialMocap Receiver")]
    public sealed class IFacialMocapReceiverAdapterBinding : AdapterBindingBase, IGazeSourceProvider
    {
        public const string HeadSub = "head";

        private const int HeadAxisCountRotationOnly = 3;
        private const int HeadAxisCountWithPosition = 6;

        [SerializeField]
        private IFacialMocapRuntimeSettingsSO _settings;

        [Tooltip("iFacialMocap 名 → メッシュ BlendShape 名の上書き。空なら ARKit 既定変換を全 52 件に適用。")]
        [SerializeField]
        private List<IFacialMocapBlendShapeMapping> _mappings = new List<IFacialMocapBlendShapeMapping>();

        [Tooltip("視線(左右ヨー)の符号を反転する。モデルの目ボーンの向きに合わせて調整（アバター固有）。")]
        [SerializeField]
        private bool _gazeInvertYaw;

        [Tooltip("視線(上下ピッチ)の符号を反転する。モデルの目ボーンの向きに合わせて調整（アバター固有）。")]
        [SerializeField]
        private bool _gazeInvertPitch;

        [NonSerialized]
        private IFacialMocapRuntimeSettingsSO _runtimeSettings;

        [NonSerialized]
        private IInputSourceRegistry _registry;

        [NonSerialized]
        private AdapterSlug _slug;

        [NonSerialized]
        private IFacialMocapReceiverHost _helperHost;

        [NonSerialized]
        private OscDoubleBuffer _buffer;

        [NonSerialized]
        private OscInputSource _inputSource;

        [NonSerialized]
        private GazeVector2InputSource _gazeLeft;

        [NonSerialized]
        private GazeVector2InputSource _gazeRight;

        [NonSerialized]
        private AnalogAxesInputSource _headSource;

        [NonSerialized]
        private Dictionary<string, int> _ifmNameToSlot;

        /// <summary>slot ごとの受信値調整（<see cref="_ifmNameToSlot"/> の値で引く）。</summary>
        [NonSerialized]
        private IFacialMocapValueTuning[] _slotTunings;

        [NonSerialized]
        private IFacialMocapFrame _frame;

        [NonSerialized]
        private ITimeProvider _timeProvider;

        [NonSerialized]
        private EyeGazeConverter _eyeGaze;

        [NonSerialized]
        private FailSafeMode _failSafeMode;

        [NonSerialized]
        private float _stalenessSeconds;

        [NonSerialized]
        private int _headAxisCount;

        [NonSerialized]
        private int _lastSequence;

        [NonSerialized]
        private double _lastDataTime;

        [NonSerialized]
        private bool _started;

        /// <summary>Inspector の Add ドロップダウンが <c>Activator.CreateInstance</c> で使う既定 ctor。</summary>
        public IFacialMocapReceiverAdapterBinding()
        {
        }

        public IFacialMocapRuntimeSettingsSO Settings
        {
            get => _settings;
            set => _settings = value;
        }

        /// <summary>有効な Settings 参照（未代入なら診断用 runtime SO にフォールバック）。</summary>
        public IFacialMocapRuntimeSettingsSO EffectiveSettings =>
            _settings != null ? _settings : _runtimeSettings;

        public List<IFacialMocapBlendShapeMapping> Mappings
        {
            get => _mappings;
            set => _mappings = value ?? new List<IFacialMocapBlendShapeMapping>();
        }

        /// <summary>視線(左右ヨー)の符号を反転する（アバター固有）。Profile に保存される。</summary>
        public bool GazeInvertYaw
        {
            get => _gazeInvertYaw;
            set => _gazeInvertYaw = value;
        }

        /// <summary>視線(上下ピッチ)の符号を反転する（アバター固有）。Profile に保存される。</summary>
        public bool GazeInvertPitch
        {
            get => _gazeInvertPitch;
            set => _gazeInvertPitch = value;
        }

        public IFacialMocapReceiverHost HelperHost => _helperHost;

        public OscDoubleBuffer Buffer => _buffer;

        public OscInputSource InputSource => _inputSource;

        public GazeVector2InputSource GazeLeftSource => _gazeLeft;

        public GazeVector2InputSource GazeRightSource => _gazeRight;

        public AnalogAxesInputSource HeadSource => _headSource;

        public bool IsStarted => _started;

        /// <inheritdoc />
        public IEnumerable<GazeSourceDeclaration> GetGazeSourceDeclarations()
        {
            yield return new GazeSourceDeclaration(GazeSourceIdConvention.DefaultChannelId, true);
        }

        /// <summary>テスト/診断用に設定とマッピングを流し込む。</summary>
        public void Configure(IFacialMocapRuntimeSettingsSO settings, List<IFacialMocapBlendShapeMapping> mappings = null)
        {
            _settings = settings;
            if (mappings != null)
            {
                _mappings = mappings;
            }
        }

        private IFacialMocapRuntimeSettingsSO EnsureRuntimeSettings()
        {
            if (_runtimeSettings == null)
            {
                _runtimeSettings = UnityEngine.ScriptableObject.CreateInstance<IFacialMocapRuntimeSettingsSO>();
                _runtimeSettings.hideFlags = HideFlags.HideAndDontSave;
            }

            return _runtimeSettings;
        }

        /// <inheritdoc />
        public override void OnStart(in AdapterBuildContext ctx)
        {
            if (_started)
            {
                return;
            }

            if (ctx.HostGameObject == null)
            {
                Debug.LogError("[IFacialMocapReceiverAdapterBinding] HostGameObject が null のため起動できません。");
                return;
            }

            IFacialMocapRuntimeSettingsSO settings = EffectiveSettings;
            if (settings == null)
            {
                Debug.LogWarning(
                    $"[IFacialMocapReceiverAdapterBinding] Settings が未代入のため起動しません。slug='{Slug}'");
                return;
            }

            if (!settings.ReceiverEnabled)
            {
                Debug.LogWarning(
                    $"[IFacialMocapReceiverAdapterBinding] Settings.ReceiverEnabled=false のため起動しません。slug='{Slug}'");
                return;
            }

            if (!AdapterSlug.TryParse(Slug, out AdapterSlug slug))
            {
                Debug.LogError(
                    $"[IFacialMocapReceiverAdapterBinding] Slug '{Slug}' が AdapterSlug 規約を満たしません。");
                return;
            }

            _registry = ctx.InputSourceRegistry;
            _slug = slug;
            _timeProvider = ctx.TimeProvider;
            _eyeGaze = settings.EyeGaze;
            _failSafeMode = settings.FailSafeMode;
            _stalenessSeconds = settings.StalenessSeconds;
            _lastDataTime = ctx.TimeProvider.UnscaledTimeSeconds;
            _lastSequence = 0;
            _frame = new IFacialMocapFrame();

            bool registeredAny = false;
            registeredAny |= BuildBlendShapeSource(ctx, settings, slug);

            if (settings.EnableGaze)
            {
                _gazeLeft = RegisterGazeSource(slug, GazeSide.Left);
                _gazeRight = RegisterGazeSource(slug, GazeSide.Right);
                registeredAny |= _gazeLeft != null || _gazeRight != null;
            }

            if (settings.EnableHead)
            {
                _headAxisCount = settings.IncludeHeadPosition ? HeadAxisCountWithPosition : HeadAxisCountRotationOnly;
                _headSource = RegisterHeadSource(slug, _headAxisCount);
                registeredAny |= _headSource != null;
            }

            if (!registeredAny)
            {
                Debug.LogWarning(
                    $"[IFacialMocapReceiverAdapterBinding] 登録できる入力源がありません（BlendShape/視線/頭部すべて無効または未解決）。slug='{Slug}'");
                return;
            }

            _helperHost = ctx.HostGameObject.AddComponent<IFacialMocapReceiverHost>();
            _helperHost.Configure(
                settings.ListenPort,
                settings.DeviceAddress,
                settings.SendHandshake,
                settings.DataVersion,
                settings.HandshakeIntervalSeconds);

            _started = true;
        }

        private bool BuildBlendShapeSource(in AdapterBuildContext ctx, IFacialMocapRuntimeSettingsSO settings, AdapterSlug slug)
        {
            IReadOnlyList<string> meshNames = ctx.BlendShapeNames;
            var meshNameToIndex = new Dictionary<string, int>(meshNames?.Count ?? 0, StringComparer.Ordinal);
            if (meshNames != null)
            {
                for (int i = 0; i < meshNames.Count; i++)
                {
                    string name = meshNames[i];
                    if (!string.IsNullOrEmpty(name) && !meshNameToIndex.ContainsKey(name))
                    {
                        meshNameToIndex.Add(name, i);
                    }
                }
            }

            _ifmNameToSlot = new Dictionary<string, int>(StringComparer.Ordinal);
            var mappingIndexToMeshIndex = new List<int>();
            var slotTunings = new List<IFacialMocapValueTuning>();
            var disabledNames = new HashSet<string>(StringComparer.Ordinal);

            foreach (IFacialMocapBlendShapeMapping mapping in EnumerateMappings())
            {
                string ifmName = mapping.ifacialMocapName;
                string targetName = mapping.blendShapeName;
                if (string.IsNullOrEmpty(ifmName) || string.IsNullOrEmpty(targetName))
                {
                    continue;
                }

                if (_ifmNameToSlot.ContainsKey(ifmName) || disabledNames.Contains(ifmName))
                {
                    continue;
                }

                // オフのマッピングは slot を割り当てず ContributeMask にも立てない（値を出力しない）。
                // 名前は採用済みとして扱い、同じ iFacialMocap 名の後続マッピングへ出力先が移らないようにする。
                // 出力先がメッシュに無くなっていても（モデル差し替え・リネーム）予約するため、ターゲット解決より先に判定する。
                if (!mapping.EffectiveEnabled)
                {
                    disabledNames.Add(ifmName);
                    continue;
                }

                if (!meshNameToIndex.TryGetValue(targetName, out int meshIndex))
                {
                    continue;
                }

                _ifmNameToSlot.Add(ifmName, mappingIndexToMeshIndex.Count);
                mappingIndexToMeshIndex.Add(meshIndex);
                slotTunings.Add(mapping.EffectiveTuning);
            }

            if (mappingIndexToMeshIndex.Count == 0)
            {
                Debug.LogWarning(
                    $"[IFacialMocapReceiverAdapterBinding] 有効でメッシュに一致する BlendShape マッピングが 0 件（オフ {disabledNames.Count} 件）のため BlendShape 入力源を登録しません。slug='{Slug}'");
                _ifmNameToSlot = null;
                return false;
            }

            _slotTunings = slotTunings.ToArray();

            // ContributeMask はメッシュの BlendShape 総数長で作る。null 渡しの自動生成に任せると
            // 長さが「最大 mapped index + 1」に縮み、末尾に非マッピング BlendShape を持つモデルで
            // LayerInputSourceAggregator の BitArray.Or (長さ完全一致要求) が ArgumentException を投げる。
            int meshCount = meshNames?.Count ?? 0;
            var contributeMask = new BitArray(meshCount);
            for (int i = 0; i < mappingIndexToMeshIndex.Count; i++)
            {
                int meshIndex = mappingIndexToMeshIndex[i];
                if (meshIndex >= 0 && meshIndex < meshCount)
                {
                    contributeMask[meshIndex] = true;
                }
            }

            _buffer = new OscDoubleBuffer(mappingIndexToMeshIndex.Count);
            _inputSource = new OscInputSource(
                _buffer,
                settings.StalenessSeconds,
                ctx.TimeProvider,
                settings.FailSafeMode,
                contributeMask,
                mappingIndexToMeshIndex.ToArray());
            _registry.Register(slug, _inputSource);
            return true;
        }

        private IEnumerable<IFacialMocapBlendShapeMapping> EnumerateMappings()
        {
            if (_mappings != null && _mappings.Count > 0)
            {
                for (int i = 0; i < _mappings.Count; i++)
                {
                    yield return _mappings[i];
                }

                yield break;
            }

            // 既定: カタログ全 52 件を ARKit 正準名へ変換して反映
            string[] catalog = IFacialMocapBlendShapeCatalog.Names;
            for (int i = 0; i < catalog.Length; i++)
            {
                yield return new IFacialMocapBlendShapeMapping(
                    catalog[i],
                    IFacialMocapBlendShapeCatalog.ToArKitName(catalog[i]));
            }
        }

        private GazeVector2InputSource RegisterGazeSource(AdapterSlug slug, GazeSide side)
        {
            string sub = GazeSourceIdConvention.ComposeSub(GazeSourceIdConvention.DefaultChannelId, side);
            string id = GazeSourceIdConvention.Compose(slug.Value, GazeSourceIdConvention.DefaultChannelId, side);
            if (!InputSourceId.TryParse(id, out InputSourceId sourceId))
            {
                Debug.LogWarning(
                    $"[IFacialMocapReceiverAdapterBinding] Gaze source id '{id}' が不正のためスキップします。");
                return null;
            }

            var source = new GazeVector2InputSource(sourceId);
            _registry.Register(slug, sub, source);
            return source;
        }

        private AnalogAxesInputSource RegisterHeadSource(AdapterSlug slug, int axisCount)
        {
            string id = slug.Value + ":" + HeadSub;
            if (!InputSourceId.TryParse(id, out InputSourceId sourceId))
            {
                Debug.LogWarning(
                    $"[IFacialMocapReceiverAdapterBinding] Head source id '{id}' が不正のためスキップします。");
                return null;
            }

            var source = new AnalogAxesInputSource(sourceId, axisCount);
            _registry.Register(slug, HeadSub, source);
            return source;
        }

        /// <inheritdoc />
        public override void OnFixedTick(float fixedDeltaTime)
        {
            if (!_started || _helperHost == null)
            {
                return;
            }

            int sequence = _helperHost.TryReadLatest(_frame);
            bool isNew = sequence != 0 && sequence != _lastSequence;
            if (isNew)
            {
                _lastSequence = sequence;
                _lastDataTime = _timeProvider != null ? _timeProvider.UnscaledTimeSeconds : 0d;
                ApplyFrame();
            }
            else
            {
                ApplyStalenessFailSafe();
            }
        }

        /// <inheritdoc />
        public override void OnLateTick(float deltaTime)
        {
            // 目線の目ボーン適用は core の FacialController に集約したため、本 binding は
            // gaze 入力源(<slug>:gaze.left/right)を registry 登録するのみで LateTick では何もしない。
        }

        private void ApplyFrame()
        {
            if (_buffer != null && _inputSource != null && _ifmNameToSlot != null && _slotTunings != null)
            {
                List<IFacialMocapBlendShapeSample> samples = _frame.BlendShapes;
                for (int i = 0; i < samples.Count; i++)
                {
                    IFacialMocapBlendShapeSample sample = samples[i];
                    if (_ifmNameToSlot.TryGetValue(sample.Name, out int slot))
                    {
                        float normalized = Mathf.Clamp01(sample.Value / IFacialMocapProtocol.BlendShapeMaxValue);
                        _buffer.Write(slot, _slotTunings[slot].Apply(normalized));
                    }
                }

                // 新規フレームを書き込んだときだけ swap し read バッファへ反映する
                // （非新規フレームでは swap せず前値を保持 → OscInputSource の staleness が機能する）。
                _buffer.Swap();
            }

            if (_gazeLeft != null && _frame.LeftEye.HasValue)
            {
                _gazeLeft.Publish(ApplyGazeInvert(_eyeGaze.Convert(_frame.LeftEye)));
            }

            if (_gazeRight != null && _frame.RightEye.HasValue)
            {
                _gazeRight.Publish(ApplyGazeInvert(_eyeGaze.Convert(_frame.RightEye)));
            }

            if (_headSource != null && _frame.Head.HasValue)
            {
                Span<float> axes = stackalloc float[HeadAxisCountWithPosition];
                IFacialMocapTransformSample head = _frame.Head;
                axes[0] = head.EulerX;
                axes[1] = head.EulerY;
                axes[2] = head.EulerZ;
                axes[3] = head.PositionX;
                axes[4] = head.PositionY;
                axes[5] = head.PositionZ;
                _headSource.Publish(axes.Slice(0, _headAxisCount));
            }
        }

        /// <summary>
        /// 視線 Vector2 にアバター固有の符号反転（<see cref="_gazeInvertYaw"/> / <see cref="_gazeInvertPitch"/>）を適用する。
        /// 反転設定は Profile（本 binding）側に保持され、入力源(アダプタ SO)には依存しない。
        /// </summary>
        private Vector2 ApplyGazeInvert(Vector2 gaze)
        {
            if (_gazeInvertYaw)
            {
                gaze.x = -gaze.x;
            }

            if (_gazeInvertPitch)
            {
                gaze.y = -gaze.y;
            }

            return gaze;
        }

        private void ApplyStalenessFailSafe()
        {
            if (_stalenessSeconds <= 0f || _failSafeMode != FailSafeMode.RevertToBase || _timeProvider == null)
            {
                return;
            }

            if (_timeProvider.UnscaledTimeSeconds - _lastDataTime <= _stalenessSeconds)
            {
                return;
            }

            // BlendShape の staleness は OscInputSource 内部で処理されるため、ここでは視線/頭部のみ 0 復帰。
            _gazeLeft?.PublishZero();
            _gazeRight?.PublishZero();
            _headSource?.PublishZero();
        }

        /// <inheritdoc />
        public override void Dispose()
        {
            if (_registry != null && _slug.Value != null)
            {
                _registry.Unregister(_slug);
                _registry.Unregister(
                    _slug,
                    GazeSourceIdConvention.ComposeSub(GazeSourceIdConvention.DefaultChannelId, GazeSide.Left));
                _registry.Unregister(
                    _slug,
                    GazeSourceIdConvention.ComposeSub(GazeSourceIdConvention.DefaultChannelId, GazeSide.Right));
                _registry.Unregister(_slug, HeadSub);
            }

            if (_helperHost != null)
            {
                if (UnityEngine.Application.isPlaying)
                {
                    UnityEngine.Object.Destroy(_helperHost);
                }
                else
                {
                    UnityEngine.Object.DestroyImmediate(_helperHost);
                }

                _helperHost = null;
            }

            if (_buffer != null)
            {
                _buffer.Dispose();
                _buffer = null;
            }

            _inputSource = null;
            _gazeLeft = null;
            _gazeRight = null;
            _headSource = null;
            _ifmNameToSlot = null;
            _slotTunings = null;
            _frame = null;
            _registry = null;
            _timeProvider = null;
            _slug = default;
            _lastSequence = 0;
            _lastDataTime = 0d;
            _started = false;
        }
    }
}
