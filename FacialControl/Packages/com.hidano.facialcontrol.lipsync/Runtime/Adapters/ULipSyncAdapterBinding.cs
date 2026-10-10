using System;
using System.Collections.Generic;
using System.Reflection;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Adapters.Playable;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.LipSync.Adapters.Devices;
using Hidano.FacialControl.LipSync.Adapters.PhonemeEntries;
using UnityEngine;

namespace Hidano.FacialControl.LipSync.Adapters
{
    [Serializable]
    [FacialAdapterBinding(displayName: "uLipSync")]
    public sealed class ULipSyncAdapterBinding
        : AdapterBindingBase,
            IAdapterBindingInitialDefaults,
            IAdapterBindingDefaultLayer,
            IAdapterBindingDefaultLayerInputs
    {
        private static readonly string[] DefaultPhonemeIds = { "A", "I", "U", "E", "O" };

        // phoneme overlay 入力源は固定 prefix "lipsync-overlay" で登録する。
        // レイヤーの入力源 id / GetDefaultLayerInputSources / サンプル / docs はすべてこの prefix を使うため、
        // binding の Slug（任意値）で登録すると FacialController.ResolveLayerInputSourcesFromRegistry の
        // TryResolve("lipsync-overlay:a") がヒットせず集約に乗らない。
        private static readonly AdapterSlug PhonemeOverlaySlug =
            AdapterSlug.Parse(LipSyncPhonemeOverlayInputSource.SlugPrefix);

        private const string DefaultSlug = "ulipsync";
        private const string DefaultLayerNameValue = "lipsync";
        private const string OverlayLayerNameValue = "overlay";

        /// <inheritdoc />
        public string DefaultLayerName => DefaultLayerNameValue;

        /// <inheritdoc />
        public string DefaultLayerInputSourceId => string.IsNullOrEmpty(Slug) ? DefaultSlug : Slug;

        /// <inheritdoc />
        /// <remarks>
        /// AIUEO の各音素ウェイトは独立に加算されるため、レイヤー内では Blend (加算 + clamp) が自然。
        /// </remarks>
        public ExclusionMode DefaultLayerExclusionMode => ExclusionMode.Blend;
        private const string DefaultProfileResourcePath =
            "FacialControl/LipSync/Default uLipSync Profile";

        [SerializeField]
        private uLipSync.Profile _analyzerProfile;

        [SerializeReference]
        private List<PhonemeEntryBase> _phonemeEntries = new List<PhonemeEntryBase>();

        [SerializeField]
        private string _targetMeshHint;

        [SerializeField]
        private float _maxWeightScale = 1f;

        // 発話ゲート設定の版。0 はフィールド追加前のアセット（[SerializeReference] の復元でフィールド初期化子に
        // 頼らず、0 なら既定値を入れ直す）。
        private const int CurrentVoiceGateSettingsVersion = 1;

        [SerializeField]
        private int _voiceGateSettingsVersion = CurrentVoiceGateSettingsVersion;

        [SerializeField]
        private bool _voiceGateEnabled = true;

        [SerializeField]
        private float _voiceOnThreshold = ULipSyncVoiceGate.DefaultOnThreshold;

        [SerializeField]
        private float _voiceOffThreshold = ULipSyncVoiceGate.DefaultOffThreshold;

        [SerializeField]
        private float _voiceHoldTime = ULipSyncVoiceGate.DefaultHoldTime;

        [SerializeField]
        private float _voiceAttackTime = ULipSyncVoiceGate.DefaultAttackTime;

        [SerializeField]
        private float _voiceReleaseTime = ULipSyncVoiceGate.DefaultReleaseTime;

        [SerializeField]
        private float _voiceStaleTimeout = ULipSyncVoiceGate.DefaultStaleTimeout;

        [SerializeField]
        private List<string> _suppressBlendShapeNames = new List<string>();

        [NonSerialized]
        private ULipSyncVoiceGate _voiceGate;

        [NonSerialized]
        private string[] _gateLayerNames;

        [NonSerialized]
        private FacialController _facialController;

        [NonSerialized]
        private bool _gateActive;

        [NonSerialized]
        private bool _analyzerUpdated;

        [NonSerialized]
        private DeviceDescriptor _runtimeDescriptor;

        [NonSerialized]
        private bool _hasConfiguredDescriptor;

        [NonSerialized]
        private IAsioDriverEnumerator _asioEnumerator;

        [NonSerialized]
        private IMicrophoneDeviceEnumerator _microphoneEnumerator;

        [NonSerialized]
        private AudioSource _audioSource;

        [NonSerialized]
        private uLipSync.uLipSync _analyzer;

        [NonSerialized]
        private uLipSync.uLipSyncMicrophone _microphoneInput;

        [NonSerialized]
        private uLipSync.uLipSyncAsioInput _asioInput;

        [NonSerialized]
        private ULipSyncEventBridge _eventBridge;

        [NonSerialized]
        private FacialControlULipSyncBlendShape _blendShape;

        [NonSerialized]
        private ULipSyncProvider _provider;

        [NonSerialized]
        private List<string> _registeredPhonemeSlots;

        [NonSerialized]
        private GameObject _hostGameObject;

        [NonSerialized]
        private bool _started;

        [NonSerialized]
        private IInputSourceRegistry _inputSourceRegistry;

        [NonSerialized]
        private AdapterSlug _registeredSlug;

        [NonSerialized]
        private bool _swapPending;

        [NonSerialized]
        private DeviceDescriptor _pendingDescriptor;

        [NonSerialized]
        private bool _addedAudioSource;

        [NonSerialized]
        private HashSet<string> _loggedWarnings;

        public ULipSyncAdapterBinding()
        {
            Slug = DefaultSlug;
        }

        /// <summary>発話ゲートを使うか。OFF なら入力源は常に有効（従来挙動）。</summary>
        public bool VoiceGateEnabled
        {
            get { EnsureVoiceGateSettings(); return _voiceGateEnabled; }
            set { EnsureVoiceGateSettings(); _voiceGateEnabled = value; }
        }

        /// <summary>発話開始のしきい値（activity）。</summary>
        public float VoiceOnThreshold
        {
            get { EnsureVoiceGateSettings(); return _voiceOnThreshold; }
            set { EnsureVoiceGateSettings(); _voiceOnThreshold = value; }
        }

        /// <summary>発話継続のしきい値（activity）。On より大きい値は On と同じ扱い。</summary>
        public float VoiceOffThreshold
        {
            get { EnsureVoiceGateSettings(); return _voiceOffThreshold; }
            set { EnsureVoiceGateSettings(); _voiceOffThreshold = value; }
        }

        /// <summary>Off を下回ってから発話終了とみなすまでの秒数。</summary>
        public float VoiceHoldTime
        {
            get { EnsureVoiceGateSettings(); return _voiceHoldTime; }
            set { EnsureVoiceGateSettings(); _voiceHoldTime = value; }
        }

        /// <summary>gate weight が 0 → 1 に上がる秒数。</summary>
        public float VoiceAttackTime
        {
            get { EnsureVoiceGateSettings(); return _voiceAttackTime; }
            set { EnsureVoiceGateSettings(); _voiceAttackTime = value; }
        }

        /// <summary>gate weight が 1 → 0 に下がる秒数。</summary>
        public float VoiceReleaseTime
        {
            get { EnsureVoiceGateSettings(); return _voiceReleaseTime; }
            set { EnsureVoiceGateSettings(); _voiceReleaseTime = value; }
        }

        /// <summary>uLipSync のコールバックが途絶えてから activity = 0 とみなす秒数（0 以下で無効）。</summary>
        public float VoiceStaleTimeout
        {
            get { EnsureVoiceGateSettings(); return _voiceStaleTimeout; }
            set { EnsureVoiceGateSettings(); _voiceStaleTimeout = value; }
        }

        /// <summary>発話中に 0 で押さえ込む BlendShape 名。変更は次回の起動から反映する。</summary>
        public List<string> SuppressBlendShapeNames
        {
            get => _suppressBlendShapeNames ??= new List<string>();
            set => _suppressBlendShapeNames = value ?? new List<string>();
        }

        /// <summary>発話ゲートの状態（activity / Speaking / weight）。起動前は null。</summary>
        public ULipSyncVoiceGate VoiceGate => _voiceGate;

        /// <summary>発話ゲートがレイヤー weight を書くレイヤー名。起動前は空。</summary>
        public IReadOnlyList<string> GateLayerNames => _gateLayerNames ?? Array.Empty<string>();

        /// <summary>
        /// 発話ゲート設定が版 0（フィールド追加前のアセット）なら既定値を入れる。
        /// </summary>
        public void EnsureVoiceGateSettings()
        {
            if (_voiceGateSettingsVersion >= CurrentVoiceGateSettingsVersion)
            {
                return;
            }

            _voiceGateEnabled = true;
            _voiceOnThreshold = ULipSyncVoiceGate.DefaultOnThreshold;
            _voiceOffThreshold = ULipSyncVoiceGate.DefaultOffThreshold;
            _voiceHoldTime = ULipSyncVoiceGate.DefaultHoldTime;
            _voiceAttackTime = ULipSyncVoiceGate.DefaultAttackTime;
            _voiceReleaseTime = ULipSyncVoiceGate.DefaultReleaseTime;
            _voiceStaleTimeout = ULipSyncVoiceGate.DefaultStaleTimeout;
            _suppressBlendShapeNames ??= new List<string>();
            _voiceGateSettingsVersion = CurrentVoiceGateSettingsVersion;
        }

        /// <inheritdoc />
        public IEnumerable<(string id, float weight)> GetDefaultLayerInputSources(string layerName)
        {
            if (!string.Equals(layerName, OverlayLayerNameValue, StringComparison.Ordinal))
            {
                yield break;
            }

            for (int i = 0; i < PhonemeOverlaySlots.ReservedNames.Length; i++)
            {
                string slot = GetReservedPhonemeSlot(i);
                yield return ($"{LipSyncPhonemeOverlayInputSource.SlugPrefix}:{slot}", 1f);
            }
        }

        /// <summary>
        /// Inspector で binding を新規追加した直後に呼ばれ、
        /// AIUEO 5 音素分の <see cref="ExpressionPhonemeEntry"/> をプリセットする。
        /// ExpressionId は未割り当てのまま追加され、ユーザーが Inspector で割り当てる。
        /// </summary>
        public void ApplyInitialDefaults()
        {
            EnsureVoiceGateSettings();

            if (_phonemeEntries == null)
            {
                _phonemeEntries = new List<PhonemeEntryBase>(DefaultPhonemeIds.Length);
            }

            if (_phonemeEntries.Count > 0)
            {
                return;
            }

            for (int i = 0; i < DefaultPhonemeIds.Length; i++)
            {
                _phonemeEntries.Add(new ExpressionPhonemeEntry
                {
                    PhonemeId = DefaultPhonemeIds[i],
                    MaxWeight = 100f,
                });
            }
        }

        public uLipSync.uLipSync Analyzer => _analyzer;

        public ULipSyncProvider Provider => _provider;

        public bool IsStarted => _started;

        public void Configure(
            DeviceDescriptor deviceDescriptor,
            uLipSync.Profile analyzerProfile,
            IReadOnlyList<PhonemeEntryBase> phonemeEntries,
            IAsioDriverEnumerator asioEnumerator = null,
            IMicrophoneDeviceEnumerator microphoneEnumerator = null)
        {
            _runtimeDescriptor = deviceDescriptor;
            _hasConfiguredDescriptor = true;
            _analyzerProfile = analyzerProfile;
            _phonemeEntries = phonemeEntries == null
                ? new List<PhonemeEntryBase>()
                : new List<PhonemeEntryBase>(phonemeEntries);
            _asioEnumerator = asioEnumerator;
            _microphoneEnumerator = microphoneEnumerator;
        }

        public void Configure(
            uLipSync.Profile analyzerProfile,
            IReadOnlyList<PhonemeEntryBase> phonemeEntries,
            IAsioDriverEnumerator asioEnumerator = null,
            IMicrophoneDeviceEnumerator microphoneEnumerator = null)
        {
            _analyzerProfile = analyzerProfile;
            _phonemeEntries = phonemeEntries == null
                ? new List<PhonemeEntryBase>()
                : new List<PhonemeEntryBase>(phonemeEntries);
            _asioEnumerator = asioEnumerator;
            _microphoneEnumerator = microphoneEnumerator;
        }

        public override void OnStart(in AdapterBuildContext ctx)
        {
            ClearLoggedWarnings();

            if (_started)
            {
                return;
            }

            EnsureVoiceGateSettings();

            if (ctx.HostGameObject == null)
            {
                Debug.LogError("[ULipSyncAdapterBinding] HostGameObject is null.");
                return;
            }

            if (!_hasConfiguredDescriptor)
            {
                _runtimeDescriptor = LipSyncDeviceStore.Load();
            }

            if (_phonemeEntries == null || _phonemeEntries.Count == 0)
            {
                Debug.LogWarning(
                    $"[ULipSyncAdapterBinding] No phoneme entries are configured. Slug='{Slug}'");
                return;
            }

            if (!AdapterSlug.TryParse(Slug, out var slug))
            {
                Debug.LogError(
                    $"[ULipSyncAdapterBinding] Slug '{Slug ?? "<null>"}' is not a valid AdapterSlug.");
                return;
            }

            if (HasReservedSlotRegistrationConflict(ctx, slug))
            {
                return;
            }

            WarnIfLegacyLipSyncSourceAlsoRegistered(ctx, slug);

            DeviceResolution resolution = ResolveDevice(_runtimeDescriptor);
            if (resolution.Kind == DeviceKind.Unresolved)
            {
                LogUnresolvedDevice(_runtimeDescriptor, resolution);
                return;
            }

            if (string.IsNullOrWhiteSpace(_runtimeDescriptor.DeviceName))
            {
                Debug.Log(
                    $"[ULipSyncAdapterBinding] リップシンクデバイスが未選択のため、既定のマイク '{resolution.DeviceNameMatched}' を使用します。"
                    + " 別のデバイスを使う場合は binding Inspector で選択してください。");
            }

            uLipSync.Profile profile = ResolveAnalyzerProfile();
            if (profile == null)
            {
                Debug.LogError(
                    "[ULipSyncAdapterBinding] Analyzer profile is not configured and the default profile " +
                    "could not be loaded.");
                return;
            }

            PhonemeSnapshot[] snapshots = BuildSnapshots(ctx);
            try
            {
                _hostGameObject = ctx.HostGameObject;
                _audioSource = _hostGameObject.GetComponent<AudioSource>();
                if (_audioSource == null)
                {
                    _audioSource = _hostGameObject.AddComponent<AudioSource>();
                    _addedAudioSource = true;
                }

                _analyzer = _hostGameObject.AddComponent<uLipSync.uLipSync>();
                _analyzer.profile = profile;

                AddInputComponent(_hostGameObject, resolution);

                // uLipSync 公式の音量正規化・SmoothDamp・sum=1 正規化を委譲する算出器。
                // min/max/smoothness/usePhonemeBlend は uLipSync 既定値のまま据え置き、
                // FacialControl 側では一切加工しない。External モードのため SkinnedMeshRenderer 不要。
                _blendShape = _hostGameObject.AddComponent<FacialControlULipSyncBlendShape>();
                _blendShape.ConfigurePhonemes(BuildPhonemeIdList(snapshots));
                _analyzer.onLipSyncUpdate.AddListener(_blendShape.OnLipSyncUpdate);
                _analyzer.onLipSyncUpdate.AddListener(OnAnalyzerLipSyncUpdate);

                _provider = new ULipSyncProvider(
                    _blendShape,
                    snapshots,
                    ctx.BlendShapeNames.Count);
                _provider.SetSuppressIndices(ResolveSuppressIndices(ctx.BlendShapeNames));
                _inputSourceRegistry = ctx.InputSourceRegistry;
                _registeredSlug = PhonemeOverlaySlug;
                RegisterPhonemeOverlayInputSources(ctx, slug);

                InitializeVoiceGate(ctx);

                _provider.RequestZeroOutputForNextFrame();
                _started = true;
            }
            catch (Exception exception)
            {
                Debug.LogError($"[ULipSyncAdapterBinding] OnStart failed: {exception}");
                RollbackStartedResources();
            }
        }

        /// <inheritdoc />
        /// <remarks>
        /// 発話ゲートを 1 フレーム進め、gate weight を対象レイヤーの weight に書く。
        /// weight が 0 の間は入力源の出力を止めて下位レイヤーをそのまま通す。ヒープ確保なし。
        /// </remarks>
        public override void OnLateTick(float deltaTime)
        {
            if (!_started || _provider == null || _voiceGate == null)
            {
                return;
            }

            if (!_voiceGateEnabled)
            {
                if (_gateActive)
                {
                    // 実行中に OFF にした場合はレイヤー weight を一度 1 に戻す。
                    WriteGateLayerWeights(1f);
                    _gateActive = false;
                }

                _analyzerUpdated = false;
                _provider.OutputEnabled = true;
                _provider.SuppressActive = true;
                return;
            }

            if (!_gateActive)
            {
                _voiceGate.Reset();
                _gateActive = true;
            }

            ApplyVoiceGateSettings();
            float activity = _blendShape != null ? _blendShape.CurrentVolume : 0f;
            float weight = _voiceGate.Tick(deltaTime, activity, _analyzerUpdated);
            _analyzerUpdated = false;

            WriteGateLayerWeights(weight);
            bool open = weight > 0f;
            _provider.OutputEnabled = open;
            _provider.SuppressActive = open;
        }

        public override void OnFixedTick(float fixedDeltaTime)
        {
            if (!_started || !_swapPending)
            {
                return;
            }

            _swapPending = false;

            DeviceDescriptor descriptor = _pendingDescriptor;
            DestroyInputComponents();

            DeviceResolution resolution = ResolveDevice(descriptor);
            if (resolution.Kind == DeviceKind.Unresolved)
            {
                LogUnresolvedDevice(descriptor, resolution);
                return;
            }

            try
            {
                AddInputComponent(_hostGameObject, resolution);
            }
            catch (Exception exception)
            {
                Debug.LogError($"[ULipSyncAdapterBinding] SwapDevice failed: {exception}");
                DestroyInputComponents();
            }
        }

        public void SwapDevice(string deviceName, int disambiguatorIndex)
        {
            if (!_started || _provider == null)
            {
                Debug.LogError("[ULipSyncAdapterBinding] SwapDevice requires a started binding.");
                return;
            }

            _provider.RequestZeroOutputForNextFrame();
            _pendingDescriptor = new DeviceDescriptor
            {
                DeviceName = deviceName,
                DisambiguatorIndex = disambiguatorIndex,
            };
            _swapPending = true;
        }

        public override void Dispose()
        {
            RollbackStartedResources();
        }

        private void OnAnalyzerLipSyncUpdate(uLipSync.LipSyncInfo info)
        {
            _analyzerUpdated = true;
        }

        private void InitializeVoiceGate(in AdapterBuildContext ctx)
        {
            _voiceGate = new ULipSyncVoiceGate();
            ApplyVoiceGateSettings();
            _gateLayerNames = ULipSyncLayerSetup.FindGateLayerNames(
                ULipSyncLayerSetup.AddTargetLayerInputs(
                    ULipSyncLayerSetup.FromProfile(ctx.Profile),
                    ctx.AdapterBindings),
                Slug);
            _facialController = ctx.HostGameObject.GetComponent<FacialController>();
            _gateActive = false;
            _analyzerUpdated = false;

            if (!_voiceGateEnabled)
            {
                return;
            }

            // ゲートが開くまで入力源を止め、下位レイヤーをそのまま通す。
            _provider.OutputEnabled = false;
            _provider.SuppressActive = false;

            if (_gateLayerNames.Length == 0)
            {
                Debug.LogWarning(
                    $"[ULipSyncAdapterBinding] uLipSync の入力源（{LipSyncPhonemeOverlayInputSource.SlugPrefix}:a〜o）だけを "
                    + "inputSources に宣言したレイヤーがありません（他の入力源と同居するレイヤーの weight は書きません）。"
                    + $"発話ゲートは入力源の有効 / 無効だけで制御します。Slug='{Slug}'");
            }
        }

        private void ApplyVoiceGateSettings()
        {
            _voiceGate.OnThreshold = _voiceOnThreshold;
            _voiceGate.OffThreshold = _voiceOffThreshold;
            _voiceGate.HoldTime = _voiceHoldTime;
            _voiceGate.AttackTime = _voiceAttackTime;
            _voiceGate.ReleaseTime = _voiceReleaseTime;
            _voiceGate.StaleTimeout = _voiceStaleTimeout;
        }

        private void WriteGateLayerWeights(float weight)
        {
            if (_facialController == null || _gateLayerNames == null)
            {
                return;
            }

            for (int i = 0; i < _gateLayerNames.Length; i++)
            {
                _facialController.SetLayerWeight(_gateLayerNames[i], weight);
            }
        }

        private List<int> ResolveSuppressIndices(IReadOnlyList<string> blendShapeNames)
        {
            var indices = new List<int>();
            if (_suppressBlendShapeNames == null || _suppressBlendShapeNames.Count == 0)
            {
                return indices;
            }

            Dictionary<string, int> nameToIndex = BuildNameToIndex(blendShapeNames);
            List<string> missing = null;
            for (int i = 0; i < _suppressBlendShapeNames.Count; i++)
            {
                string name = _suppressBlendShapeNames[i];
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                if (nameToIndex.TryGetValue(name, out int index))
                {
                    if (!indices.Contains(index))
                    {
                        indices.Add(index);
                    }
                }
                else
                {
                    (missing ??= new List<string>()).Add(name);
                }
            }

            if (missing != null)
            {
                Debug.LogWarning(
                    $"[ULipSyncAdapterBinding] Suppress Blend Shape Names のうちメッシュに無い BlendShape があります: "
                    + $"{string.Join(", ", missing)}. Slug='{Slug}'");
            }

            return indices;
        }

        private DeviceResolution ResolveDevice(DeviceDescriptor descriptor)
        {
            return DeviceResolver.Resolve(
                descriptor,
                _asioEnumerator ?? new DefaultAsioDriverEnumerator(),
                _microphoneEnumerator ?? new DefaultMicrophoneDeviceEnumerator());
        }

        private static void LogUnresolvedDevice(DeviceDescriptor descriptor, DeviceResolution resolution)
        {
            Debug.LogError(
                $"[ULipSyncAdapterBinding] Device '{descriptor.DeviceName}' could not be resolved. " +
                $"DisambiguatorIndex={descriptor.DisambiguatorIndex}, " +
                $"ASIO=[{string.Join(", ", resolution.AvailableAsio)}], " +
                $"Microphone=[{string.Join(", ", resolution.AvailableMic)}]");
        }

        private uLipSync.Profile ResolveAnalyzerProfile()
        {
            if (_analyzerProfile != null)
            {
                return _analyzerProfile;
            }

            return Resources.Load<uLipSync.Profile>(DefaultProfileResourcePath);
        }

        private void AddInputComponent(GameObject hostGameObject, DeviceResolution resolution)
        {
            if (resolution.Kind == DeviceKind.Microphone)
            {
                _microphoneInput = hostGameObject.AddComponent<uLipSync.uLipSyncMicrophone>();
                _microphoneInput.index = resolution.ResolvedIndex;
                _microphoneInput.UpdateMicInfo();
                // UpdateMicInfo は Microphone.devices の状態に応じて index を再設定する経路があり、
                // batchmode / 未接続環境では解決済み index が 0 に巻き戻ることがある。
                // DeviceResolver の解決結果を最終 source-of-truth として再度上書きする。
                _microphoneInput.index = resolution.ResolvedIndex;
                return;
            }

            if (resolution.Kind == DeviceKind.Asio)
            {
                _asioInput = hostGameObject.AddComponent<uLipSync.uLipSyncAsioInput>();
                SetAsioField("lipSync", _analyzer);
                SetAsioField("selectedDeviceIndex", resolution.ResolvedIndex);
            }
        }

        private void DestroyInputComponents()
        {
            if (_microphoneInput != null)
            {
                UnityEngine.Object.Destroy(_microphoneInput);
                _microphoneInput = null;
            }

            if (_asioInput != null)
            {
                _asioInput.StopRecord();
                UnityEngine.Object.Destroy(_asioInput);
                _asioInput = null;
            }
        }

        private void SetAsioField(string fieldName, object value)
        {
            if (_asioInput == null)
            {
                return;
            }

            FieldInfo field = typeof(uLipSync.uLipSyncAsioInput).GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            field?.SetValue(_asioInput, value);
        }

        private static List<string> BuildPhonemeIdList(PhonemeSnapshot[] snapshots)
        {
            var ids = new List<string>(snapshots.Length);
            for (int i = 0; i < snapshots.Length; i++)
            {
                ids.Add(snapshots[i].PhonemeId);
            }

            return ids;
        }

        private PhonemeSnapshot[] BuildSnapshots(in AdapterBuildContext ctx)
        {
            var snapshots = new List<PhonemeSnapshot>(_phonemeEntries.Count);
            Dictionary<string, int> nameToIndex = BuildNameToIndex(ctx.BlendShapeNames);
            SkinnedMeshRenderer[] renderers =
                ctx.HostGameObject.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            SkinnedMeshRenderer targetRenderer = ResolveRenderer(ctx.HostGameObject, renderers);
            SavedBlendShapeWeights[] savedWeights = HasAnimationClipEntries()
                ? SaveBlendShapeWeights(renderers)
                : null;

            try
            {
                for (int i = 0; i < _phonemeEntries.Count; i++)
                {
                    PhonemeEntryBase entry = _phonemeEntries[i];
                    if (entry == null)
                    {
                        Debug.LogWarning($"[ULipSyncAdapterBinding] Phoneme entry at index {i} is null. Skipping.");
                        continue;
                    }

                    if (string.IsNullOrEmpty(entry.PhonemeId))
                    {
                        Debug.LogWarning(
                            $"[ULipSyncAdapterBinding] PhonemeId is empty at index {i}. Skipping.");
                        continue;
                    }

                    float[] weights = new float[ctx.BlendShapeNames.Count];
                    if (entry is BlendShapePhonemeEntry blendShapeEntry)
                    {
                        if (TryFillBlendShapeSnapshot(
                                blendShapeEntry,
                                targetRenderer,
                                nameToIndex,
                                weights))
                        {
                            snapshots.Add(new PhonemeSnapshot(entry.PhonemeId, weights));
                        }

                        continue;
                    }

                    if (entry is AnimationClipPhonemeEntry animationEntry)
                    {
                        if (TryFillAnimationClipSnapshot(
                                animationEntry,
                                renderers,
                                savedWeights,
                                ctx,
                                nameToIndex,
                                weights))
                        {
                            snapshots.Add(new PhonemeSnapshot(entry.PhonemeId, weights));
                        }

                        continue;
                    }

                    if (entry is ExpressionPhonemeEntry expressionEntry
                        && TryFillExpressionSnapshot(
                            expressionEntry,
                            nameToIndex,
                            ctx,
                            weights))
                    {
                        snapshots.Add(new PhonemeSnapshot(entry.PhonemeId, weights));
                    }
                }
            }
            finally
            {
                RestoreBlendShapeWeights(savedWeights);
            }

            return snapshots.ToArray();
        }

        private bool TryFillBlendShapeSnapshot(
            BlendShapePhonemeEntry entry,
            SkinnedMeshRenderer targetRenderer,
            IReadOnlyDictionary<string, int> nameToIndex,
            float[] weights)
        {
            if (targetRenderer == null || targetRenderer.sharedMesh == null)
            {
                Debug.LogWarning(
                    $"[ULipSyncAdapterBinding] BlendShape '{entry.BlendShapeName}' could not be resolved.");
                return false;
            }

            int index = FindBlendShapeIndex(nameToIndex, entry.BlendShapeName);
            int meshIndex = targetRenderer.sharedMesh.GetBlendShapeIndex(entry.BlendShapeName);
            if (index < 0 || meshIndex < 0)
            {
                Debug.LogWarning(
                    $"[ULipSyncAdapterBinding] BlendShape '{entry.BlendShapeName}' could not be resolved.");
                return false;
            }

            weights[index] = NormalizeWeight(entry.MaxWeight);
            return true;
        }

        private bool TryFillAnimationClipSnapshot(
            AnimationClipPhonemeEntry entry,
            SkinnedMeshRenderer[] renderers,
            SavedBlendShapeWeights[] savedWeights,
            in AdapterBuildContext ctx,
            IReadOnlyDictionary<string, int> nameToIndex,
            float[] weights)
        {
            if (entry.Clip == null)
            {
                Debug.LogWarning(
                    $"[ULipSyncAdapterBinding] AnimationClip for phoneme '{entry.PhonemeId}' is null. Skipping.");
                return false;
            }

            if (renderers == null || renderers.Length == 0)
            {
                Debug.LogWarning(
                    $"[ULipSyncAdapterBinding] No SkinnedMeshRenderer was found for phoneme '{entry.PhonemeId}'.");
                return false;
            }

            ClearBlendShapeWeights(savedWeights);
            // AnimationClip の "終端" を採取することで、frame 0 (初期状態 = 口閉じ等) ではなく
            // 表情完成時点の BlendShape weight をスナップショットに格納する。
            // Clip.length が 0 の場合は frame 0 にフォールバックする。
            float sampleTime = entry.Clip.length > 0f ? entry.Clip.length : 0f;
            entry.Clip.SampleAnimation(ctx.HostGameObject, sampleTime);

            float scale = NormalizeWeight(entry.MaxWeight);
            bool anyNonZero = false;
            for (int i = 0; i < ctx.BlendShapeNames.Count; i++)
            {
                string bsName = ctx.BlendShapeNames[i];
                for (int r = 0; r < renderers.Length; r++)
                {
                    SkinnedMeshRenderer renderer = renderers[r];
                    if (renderer == null || renderer.sharedMesh == null)
                    {
                        continue;
                    }

                    int meshIndex = renderer.sharedMesh.GetBlendShapeIndex(bsName);
                    if (meshIndex >= 0)
                    {
                        float w = Mathf.Clamp01(
                            (renderer.GetBlendShapeWeight(meshIndex) / 100f) * scale);
                        weights[i] = w;
                        if (w > 0f)
                        {
                            anyNonZero = true;
                        }
                        break;
                    }
                }
            }

            if (!anyNonZero)
            {
                if (TryFindExpressionByPhonemeIdHeuristic(ctx, entry.PhonemeId, out Expression expression)
                    && TryFillExpressionSnapshotByExpression(
                        entry.PhonemeId,
                        expression,
                        nameToIndex,
                        entry.MaxWeight,
                        weights))
                {
                    LogAnimationClipFallbackWarning(entry.PhonemeId, entry.Clip.name);
                    return true;
                }

                // sample 後に 1 つも BlendShape weight が立たないクリップは、リップシンク中に何も
                // 出力しない (= ContributeMask が空のまま) ため、ユーザーが「リップシンクが動かない」
                // と感じる元になる。Clip 自体に BlendShape カーブが無いか、HostGameObject の
                // SkinnedMeshRenderer 構造と Clip 内の rendererPath が一致しない可能性が高い。
                Debug.LogWarning(
                    $"[ULipSyncAdapterBinding] AnimationClip '{entry.Clip.name}' (phoneme '{entry.PhonemeId}') の "
                    + $"sample 結果が全 BlendShape で 0 でした (sampleTime={sampleTime}). "
                    + "Clip が BlendShape カーブを持たない、または HostGameObject ('"
                    + ctx.HostGameObject.name
                    + "') の SkinnedMeshRenderer 構造と Clip の rendererPath が一致していない可能性があります。");
            }

            return anyNonZero;
        }

        private bool TryFillExpressionSnapshot(
            ExpressionPhonemeEntry entry,
            IReadOnlyDictionary<string, int> nameToIndex,
            in AdapterBuildContext ctx,
            float[] weights)
        {
            string expressionId = entry.ExpressionId;
            if (string.IsNullOrEmpty(expressionId))
            {
                if (IsDefaultPhonemeId(entry.PhonemeId)
                    && TryFindExpressionByPhonemeIdHeuristic(ctx, entry.PhonemeId, out Expression autoLinkedExpression))
                {
                    return TryFillExpressionSnapshotByExpression(
                        entry.PhonemeId,
                        autoLinkedExpression,
                        nameToIndex,
                        entry.MaxWeight,
                        weights);
                }

                LogExpressionResolutionWarning(
                    entry.PhonemeId,
                    expressionId,
                    ExpressionWarningCause.EmptyExpressionId);
                return false;
            }

            Expression? expression = ctx.Profile.FindExpressionById(expressionId);
            if (!expression.HasValue)
            {
                LogExpressionResolutionWarning(
                    entry.PhonemeId,
                    expressionId,
                    ExpressionWarningCause.ExpressionNotFound);
                return false;
            }

            return TryFillExpressionSnapshotByExpression(
                entry.PhonemeId,
                expression.Value,
                nameToIndex,
                entry.MaxWeight,
                weights);
        }

        private static bool IsDefaultPhonemeId(string phonemeId)
        {
            for (int i = 0; i < DefaultPhonemeIds.Length; i++)
            {
                if (string.Equals(DefaultPhonemeIds[i], phonemeId, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryFillExpressionSnapshotByExpression(
            string phonemeId,
            Expression expression,
            IReadOnlyDictionary<string, int> nameToIndex,
            float maxWeight,
            float[] weights)
        {
            ReadOnlySpan<BlendShapeMapping> mappings = expression.BlendShapeValues.Span;
            if (mappings.Length == 0)
            {
                LogExpressionResolutionWarning(
                    phonemeId,
                    expression.Id,
                    ExpressionWarningCause.EmptyBlendShapeValues);
                return false;
            }

            float scale = NormalizeWeight(maxWeight);
            bool anyNonZero = false;
            for (int i = 0; i < mappings.Length; i++)
            {
                int index = FindBlendShapeIndex(nameToIndex, mappings[i].Name);
                if (index < 0)
                {
                    continue;
                }

                float weight = Mathf.Clamp01(mappings[i].Value * scale);
                weights[index] = weight;
                if (weight > 0f)
                {
                    anyNonZero = true;
                }
            }

            return anyNonZero;
        }

        private bool TryFindExpressionByPhonemeIdHeuristic(
            in AdapterBuildContext ctx,
            string phonemeId,
            out Expression expression)
        {
            expression = default;
            if (string.IsNullOrEmpty(phonemeId))
            {
                return false;
            }

            ReadOnlySpan<Expression> expressions = ctx.Profile.Expressions.Span;
            for (int i = 0; i < expressions.Length; i++)
            {
                Expression candidate = expressions[i];
                if (string.Equals(candidate.Id, phonemeId, StringComparison.Ordinal)
                    || string.Equals(candidate.Name, phonemeId, StringComparison.Ordinal))
                {
                    expression = candidate;
                    return true;
                }
            }

            // Japanese display names such as "a" in kana intentionally do not match AIUEO phoneme ids.
            return false;
        }

        private void LogExpressionResolutionWarning(
            string phonemeId,
            string expressionId,
            ExpressionWarningCause cause)
        {
            string normalizedPhonemeId = string.IsNullOrEmpty(phonemeId) ? "<empty>" : phonemeId;
            string normalizedExpressionId = string.IsNullOrEmpty(expressionId) ? "<empty>" : expressionId;
            string key = cause switch
            {
                ExpressionWarningCause.EmptyExpressionId => $"expr-empty:{normalizedPhonemeId}",
                ExpressionWarningCause.ExpressionNotFound =>
                    $"expr-not-found:{normalizedPhonemeId}:{normalizedExpressionId}",
                ExpressionWarningCause.EmptyBlendShapeValues =>
                    $"expr-empty-bs:{normalizedPhonemeId}:{normalizedExpressionId}",
                _ => $"expr-unknown:{normalizedPhonemeId}:{normalizedExpressionId}",
            };

            if (!TryMarkWarningLogged(key))
            {
                return;
            }

            string message = cause switch
            {
                ExpressionWarningCause.EmptyExpressionId =>
                    $"Expression is not assigned (ExpressionId='{normalizedExpressionId}'). "
                    + "Assign an Expression in the Inspector.",
                ExpressionWarningCause.ExpressionNotFound =>
                    $"ExpressionId='{normalizedExpressionId}' does not exist in the profile.",
                ExpressionWarningCause.EmptyBlendShapeValues =>
                    $"ExpressionId='{normalizedExpressionId}' has no BlendShape values.",
                _ => "Expression could not be resolved.",
            };

            Debug.LogWarning(
                $"[ULipSyncAdapterBinding] {message} PhonemeId='{normalizedPhonemeId}'. "
                + "The phoneme entry will be skipped; configure the Expression assignment in the Inspector.");
        }

        private void LogAnimationClipFallbackWarning(string phonemeId, string clipName)
        {
            string normalizedPhonemeId = string.IsNullOrEmpty(phonemeId) ? "<empty>" : phonemeId;
            if (!TryMarkWarningLogged($"clip-fallback:{normalizedPhonemeId}"))
            {
                return;
            }

            string normalizedClipName = string.IsNullOrEmpty(clipName) ? "<null>" : clipName;
            Debug.LogWarning(
                $"[ULipSyncAdapterBinding] AnimationClip '{normalizedClipName}' "
                + $"for phoneme '{normalizedPhonemeId}' sampled all zero values; fallback 採用済み. "
                + "Use ExpressionPhonemeEntry as a more reliable alternative.");
        }

        private bool TryMarkWarningLogged(string key)
        {
            if (_loggedWarnings == null)
            {
                _loggedWarnings = new HashSet<string>(StringComparer.Ordinal);
            }

            return _loggedWarnings.Add(key);
        }

        private void ClearLoggedWarnings()
        {
            if (_loggedWarnings == null)
            {
                _loggedWarnings = new HashSet<string>(StringComparer.Ordinal);
                return;
            }

            _loggedWarnings.Clear();
        }

        private SkinnedMeshRenderer ResolveRenderer(
            GameObject hostGameObject,
            SkinnedMeshRenderer[] renderers)
        {
            if (renderers == null || renderers.Length == 0)
            {
                return null;
            }

            if (!string.IsNullOrEmpty(_targetMeshHint))
            {
                for (int i = 0; i < renderers.Length; i++)
                {
                    if (string.Equals(renderers[i].name, _targetMeshHint, StringComparison.Ordinal)
                        || string.Equals(renderers[i].gameObject.name, _targetMeshHint, StringComparison.Ordinal)
                        || string.Equals(GetRelativePath(hostGameObject.transform, renderers[i].transform),
                            _targetMeshHint,
                            StringComparison.Ordinal))
                    {
                        return renderers[i];
                    }
                }

                Debug.LogWarning(
                    $"[ULipSyncAdapterBinding] Target mesh hint '{_targetMeshHint}' was not resolved. " +
                    "Using the first SkinnedMeshRenderer.");
            }

            return renderers[0];
        }

        private bool HasAnimationClipEntries()
        {
            for (int i = 0; i < _phonemeEntries.Count; i++)
            {
                if (_phonemeEntries[i] is AnimationClipPhonemeEntry)
                {
                    return true;
                }
            }

            return false;
        }

        private static SavedBlendShapeWeights[] SaveBlendShapeWeights(SkinnedMeshRenderer[] renderers)
        {
            if (renderers == null || renderers.Length == 0)
            {
                return Array.Empty<SavedBlendShapeWeights>();
            }

            var savedWeights = new SavedBlendShapeWeights[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
            {
                SkinnedMeshRenderer renderer = renderers[i];
                Mesh mesh = renderer != null ? renderer.sharedMesh : null;
                int blendShapeCount = mesh != null ? mesh.blendShapeCount : 0;
                var weights = new float[blendShapeCount];
                for (int j = 0; j < blendShapeCount; j++)
                {
                    weights[j] = renderer.GetBlendShapeWeight(j);
                }

                savedWeights[i] = new SavedBlendShapeWeights(renderer, weights);
            }

            return savedWeights;
        }

        private static void ClearBlendShapeWeights(SavedBlendShapeWeights[] savedWeights)
        {
            if (savedWeights == null)
            {
                return;
            }

            for (int i = 0; i < savedWeights.Length; i++)
            {
                SkinnedMeshRenderer renderer = savedWeights[i].Renderer;
                if (renderer == null)
                {
                    continue;
                }

                int blendShapeCount = savedWeights[i].Weights.Length;
                for (int j = 0; j < blendShapeCount; j++)
                {
                    renderer.SetBlendShapeWeight(j, 0f);
                }
            }
        }

        private static void RestoreBlendShapeWeights(SavedBlendShapeWeights[] savedWeights)
        {
            if (savedWeights == null)
            {
                return;
            }

            for (int i = 0; i < savedWeights.Length; i++)
            {
                SkinnedMeshRenderer renderer = savedWeights[i].Renderer;
                if (renderer == null)
                {
                    continue;
                }

                float[] weights = savedWeights[i].Weights;
                for (int j = 0; j < weights.Length; j++)
                {
                    renderer.SetBlendShapeWeight(j, weights[j]);
                }
            }
        }

        private static string GetRelativePath(Transform root, Transform target)
        {
            if (root == null || target == null)
            {
                return string.Empty;
            }

            if (root == target)
            {
                return string.Empty;
            }

            var names = new List<string>();
            Transform current = target;
            while (current != null && current != root)
            {
                names.Add(current.name);
                current = current.parent;
            }

            if (current != root)
            {
                return target.name;
            }

            names.Reverse();
            return string.Join("/", names);
        }

        private float NormalizeWeight(float maxWeight)
        {
            return Mathf.Clamp01((maxWeight / 100f) * Mathf.Max(0f, _maxWeightScale));
        }

        private static Dictionary<string, int> BuildNameToIndex(IReadOnlyList<string> blendShapeNames)
        {
            var nameToIndex = new Dictionary<string, int>(blendShapeNames.Count);
            for (int i = 0; i < blendShapeNames.Count; i++)
            {
                string name = blendShapeNames[i];
                if (!string.IsNullOrEmpty(name) && !nameToIndex.ContainsKey(name))
                {
                    nameToIndex.Add(name, i);
                }
            }

            return nameToIndex;
        }

        private static int FindBlendShapeIndex(
            IReadOnlyDictionary<string, int> nameToIndex,
            string blendShapeName)
        {
            if (string.IsNullOrEmpty(blendShapeName))
            {
                return -1;
            }

            return nameToIndex.TryGetValue(blendShapeName, out int index) ? index : -1;
        }

        private void RegisterPhonemeOverlayInputSources(in AdapterBuildContext ctx, AdapterSlug slug)
        {
            if (_registeredPhonemeSlots == null)
            {
                _registeredPhonemeSlots = new List<string>(PhonemeOverlaySlots.ReservedNames.Length);
            }
            else
            {
                _registeredPhonemeSlots.Clear();
            }

            ReadOnlySpan<string> declaredSlots = ctx.Profile.Slots.Span;
            ReadOnlySpan<string> reservedSlots = PhonemeOverlaySlots.ReservedNames;
            for (int i = 0; i < reservedSlots.Length; i++)
            {
                string slot = reservedSlots[i];
                if (!ContainsSlot(declaredSlots, slot))
                {
                    continue;
                }

                string phonemeId = PhonemeOverlaySlots.MapReservedToPhonemeId(slot);
                if (!_provider.TryGetPhonemeIndex(phonemeId, out _))
                {
                    continue;
                }

                var id = InputSourceId.Parse($"{LipSyncPhonemeOverlayInputSource.SlugPrefix}:{slot}");
                // Expression の Override は音素の口形状 snapshot を差し替え（駆動 weight は使い回し）、
                // Suppress は当該 slot の出力を止める
                // （precedence: Override → Suppress → DefaultOverlays → LipSync default）。
                var source = new LipSyncPhonemeOverlayInputSource(
                    id,
                    phonemeId,
                    _provider,
                    ctx.BlendShapeNames.Count,
                    slot,
                    ctx.BlendShapeNames,
                    ctx.Profile,
                    ctx.ActiveExpressionProvider,
                    "emotion");
                // binding の slug ではなく固定 prefix "lipsync-overlay" で登録する（id 体系をレイヤーと一致させる）。
                ctx.InputSourceRegistry.Register(PhonemeOverlaySlug, slot, source);
                _registeredPhonemeSlots.Add(slot);
            }

            if (_registeredPhonemeSlots.Count == 0)
            {
                Debug.LogWarning(
                    "[ULipSyncAdapterBinding] No reserved phoneme overlay slots are declared in FacialProfile.Slots. " +
                    "LipSync overlay input source registration was skipped.");
            }
        }

        private static bool HasReservedSlotRegistrationConflict(
            in AdapterBuildContext ctx,
            AdapterSlug slug)
        {
            ReadOnlySpan<string> declaredSlots = ctx.Profile.Slots.Span;
            ReadOnlySpan<string> reservedSlots = PhonemeOverlaySlots.ReservedNames;
            for (int i = 0; i < reservedSlots.Length; i++)
            {
                string slot = reservedSlots[i];
                if (!ContainsSlot(declaredSlots, slot))
                {
                    continue;
                }

                // 登録キーと同じ固定 prefix で重複検知する（binding slug ではなく "lipsync-overlay:{slot}"）。
                string id = $"{LipSyncPhonemeOverlayInputSource.SlugPrefix}:{slot}";
                if (ctx.InputSourceRegistry.TryResolve(id, out _))
                {
                    Debug.LogError(
                        $"[ULipSyncAdapterBinding] Input source slug '{id}' is already registered. " +
                        "Duplicate binding initialization was skipped.");
                    return true;
                }
            }

            return false;
        }

        internal static bool WarnIfLegacyLipSyncSourceAlsoRegistered(
            in AdapterBuildContext ctx,
            AdapterSlug slug)
        {
            if (!ctx.InputSourceRegistry.TryResolve(slug.Value, out _))
            {
                return false;
            }

            ReadOnlySpan<string> declaredSlots = ctx.Profile.Slots.Span;
            ReadOnlySpan<string> reservedSlots = PhonemeOverlaySlots.ReservedNames;
            for (int i = 0; i < reservedSlots.Length; i++)
            {
                string slot = reservedSlots[i];
                if (!ContainsSlot(declaredSlots, slot))
                {
                    continue;
                }

                Debug.LogWarning(
                    $"[ULipSyncAdapterBinding] Legacy LipSync input source '{slug.Value}' is already registered " +
                    $"while phoneme overlay slot '{slot}' is declared. This can double-write the same phoneme " +
                    "BlendShape in one frame. Remove the legacy lipsync layer/input source and migrate to " +
                    $"'{slug.Value}:{slot}' plus overlay input sources.");
                return true;
            }

            return false;
        }

        private static bool ContainsSlot(ReadOnlySpan<string> slots, string slot)
        {
            for (int i = 0; i < slots.Length; i++)
            {
                if (string.Equals(slots[i], slot, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static string GetReservedPhonemeSlot(int index)
        {
            return PhonemeOverlaySlots.ReservedNames[index];
        }

        private readonly struct SavedBlendShapeWeights
        {
            public readonly SkinnedMeshRenderer Renderer;
            public readonly float[] Weights;

            public SavedBlendShapeWeights(SkinnedMeshRenderer renderer, float[] weights)
            {
                Renderer = renderer;
                Weights = weights;
            }
        }

        private enum ExpressionWarningCause
        {
            EmptyExpressionId,
            ExpressionNotFound,
            EmptyBlendShapeValues,
        }

        private void RollbackStartedResources()
        {
            if (_provider != null)
            {
                _provider.Dispose();
                _provider = null;
            }

            if (_inputSourceRegistry != null && _registeredPhonemeSlots != null)
            {
                for (int i = _registeredPhonemeSlots.Count - 1; i >= 0; i--)
                {
                    _inputSourceRegistry.Unregister(_registeredSlug, _registeredPhonemeSlots[i]);
                }
            }

            _registeredPhonemeSlots?.Clear();
            _inputSourceRegistry = null;
            _registeredSlug = default;

            if (_eventBridge != null)
            {
                _eventBridge.Dispose();
                _eventBridge = null;
            }

            if (_blendShape != null)
            {
                if (_analyzer != null)
                {
                    _analyzer.onLipSyncUpdate.RemoveListener(_blendShape.OnLipSyncUpdate);
                }

                UnityEngine.Object.Destroy(_blendShape);
                _blendShape = null;
            }

            DestroyInputComponents();

            if (_analyzer != null)
            {
                _analyzer.onLipSyncUpdate.RemoveListener(OnAnalyzerLipSyncUpdate);
                UnityEngine.Object.Destroy(_analyzer);
                _analyzer = null;
            }

            if (_addedAudioSource && _audioSource != null)
            {
                UnityEngine.Object.Destroy(_audioSource);
            }

            _audioSource = null;
            _hostGameObject = null;
            _addedAudioSource = false;
            _swapPending = false;
            _voiceGate = null;
            _gateLayerNames = null;
            _facialController = null;
            _gateActive = false;
            _analyzerUpdated = false;
            _started = false;
        }
    }
}
