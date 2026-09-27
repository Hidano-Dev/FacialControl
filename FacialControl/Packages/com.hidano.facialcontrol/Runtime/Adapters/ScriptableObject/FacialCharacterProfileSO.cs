using System;
using System.Collections.Generic;
using System.IO;
using Hidano.FacialControl.Adapters.FileSystem;
using Hidano.FacialControl.Adapters.Json;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Models;
using UnityEngine;
using GazeChannel = Hidano.FacialControl.Adapters.ScriptableObject.GazeChannel;

namespace Hidano.FacialControl.Adapters.ScriptableObject.Serializable
{
    [CreateAssetMenu(fileName = "NewFacialCharacterProfile", menuName = "FacialControl/Facial Character Profile")]
    public class FacialCharacterProfileSO : UnityEngine.ScriptableObject, IFacialCharacterProfile, ISerializationCallbackReceiver
    {
        public const string StreamingAssetsRootFolder = "FacialControl";
        public const string ProfileJsonFileName = "profile.json";

        [SerializeField] protected string _schemaVersion = SystemTextJsonParser.SchemaVersionV2;
        [SerializeField] protected List<LayerDefinitionSerializable> _layers = new List<LayerDefinitionSerializable>();
        [SerializeField] protected List<ExpressionSerializable> _expressions = new List<ExpressionSerializable>();
        [SerializeField] protected BaseExpressionSerializable _baseExpression = new BaseExpressionSerializable();
        [SerializeField] protected List<string> _rendererPaths = new List<string>();
        [SerializeField] protected List<GazeChannel> _gazeChannels = new List<GazeChannel>
        {
            new GazeChannel { id = "gaze" }
        };
        [SerializeField, HideInInspector] protected List<LegacyGazeConfigEntry> _legacyGazeConfigs;
        [NonSerialized] private List<string> _migratedLegacyGazeConfigIds;
        [NonSerialized] private bool _legacyMigrationDetected;
        [NonSerialized] private bool _legacyMigrationWarningIssued;
        [NonSerialized] private List<string> _unusedLegacyIds;
        [NonSerialized] private bool _unusedLegacyDetected;
        [NonSerialized] private bool _unusedLegacyWarning;
        // 依存側置換までのソース互換用。旧 root リストは保存しない。
        // 旧 SO スキーマを検出するためだけに旧キーを受け取る。通常の Gaze API には公開しない。

        // 既存の拡張コードとのコンパイル互換用。Unity のシリアライズ対象にはしない。
        [SerializeField] private List<string> _slots = new();
        [SerializeField] protected List<OverlaySlotBindingSerializable> _defaultOverlays = new List<OverlaySlotBindingSerializable>();
        [SerializeReference] protected List<AdapterBindingBase> _adapterBindings = new List<AdapterBindingBase>();

#if UNITY_EDITOR
        [SerializeField] protected GameObject _referenceModel;
        public GameObject ReferenceModel { get => _referenceModel; set => _referenceModel = value; }
#endif

        public string CharacterAssetName => name;
        public string SchemaVersion { get => _schemaVersion; set => _schemaVersion = value; }
        public List<LayerDefinitionSerializable> Layers => _layers;
        public List<ExpressionSerializable> Expressions => _expressions;
        public BaseExpressionSerializable BaseExpression
        {
            get
            {
                if (_baseExpression == null)
                {
                    _baseExpression = new BaseExpressionSerializable();
                }

                _baseExpression.EnsureCachedSnapshot();
                return _baseExpression;
            }
        }

        public List<string> RendererPaths => _rendererPaths;
        public IReadOnlyList<GazeChannel> GazeChannels
        {
            get
            {
                if (_gazeChannels == null || _gazeChannels.Count == 0)
                    _gazeChannels = new List<GazeChannel> { CreateDefaultGazeChannel() };
                else if (_gazeChannels[0] == null)
                    _gazeChannels[0] = CreateDefaultGazeChannel();

                if (!string.Equals(_gazeChannels[0].id, "gaze", System.StringComparison.Ordinal))
                    _gazeChannels[0].id = "gaze";
                return _gazeChannels;
            }
        }
        public bool HasLegacyGazeConfigs => _legacyMigrationDetected;

        /// <summary>
        /// 旧 SO スキーマの移行データが復元されたかを示す。
        /// 旧データは自動変換せず、呼び出し側が警告して読み捨てるために使用する。
        /// </summary>

        /// <summary>旧 _gazeConfigs YAML を新しい gaze channel へ一度だけ移行する。</summary>
        public void OnAfterDeserialize()
        {
            if (_legacyGazeConfigs == null || _legacyGazeConfigs.Count == 0)
                return;

            _legacyMigrationDetected = true;
            _migratedLegacyGazeConfigIds = new List<string>(_legacyGazeConfigs.Count);
            for (int i = 0; i < _legacyGazeConfigs.Count; i++)
            {
                var legacy = _legacyGazeConfigs[i];
                if (legacy == null) continue;
                _migratedLegacyGazeConfigIds.Add(legacy.expressionId ?? string.Empty);
            }

            var legacyEntries = new List<LegacyGazeConfigEntry>(_legacyGazeConfigs);

            if (_gazeChannels == null)
                _gazeChannels = new List<GazeChannel>();

            // The old root list was the source of truth. Replace the default
            // channel list with its entries, preserving every legacy setting.
            _gazeChannels.Clear();
            _gazeChannels.Add(CreateDefaultGazeChannel());

            foreach (var legacy in legacyEntries)
            {
                if (legacy == null) continue;
                var migrated = legacy.ToChannel();
                if (string.Equals(migrated.id, "gaze", StringComparison.Ordinal))
                    _gazeChannels[0] = migrated;
                else
                    _gazeChannels.Add(migrated);
            }

            // Keep the new marker field as an empty serialized list. This makes the
            // old key disappear on the next save while retaining the migration signal
            // for this load, and prevents the old object graph from being serialized.
            _legacyGazeConfigs.Clear();
            if (!_legacyMigrationWarningIssued)
            {
                _legacyMigrationWarningIssued = true;
                Debug.LogWarning($"{name}: 旧 _gazeConfigs を gaze.channels へ移行しました。移行ガイドを確認し、アセットを保存してください。");
            }
        }

        public void OnBeforeSerialize() { }

        /// <summary>Overlay slot 識別子の宣言一覧。Expression / DefaultOverlays / Adapter Binding の overlaySlot はここに宣言された値だけを参照できる。</summary>
        public IReadOnlyList<string> Slots => _slots ?? (_slots = new List<string>());
        public List<OverlaySlotBindingSerializable> DefaultOverlays
            => _defaultOverlays ?? (_defaultOverlays = new List<OverlaySlotBindingSerializable>());
        public IReadOnlyList<AdapterBindingBase> AdapterBindings => _adapterBindings;

        private static GazeChannel CreateDefaultGazeChannel()
        {
            return new GazeChannel { id = "gaze" };
        }

        public virtual FacialProfile BuildFallbackProfile()
        {
            return FacialCharacterProfileConverter.ToFacialProfile(
                schemaVersion: _schemaVersion,
                layers: _layers,
                expressions: _expressions,
                rendererPaths: _rendererPaths,
                defaultOverlays: DefaultOverlays,
                slots: Slots,
                // StreamingAssets の profile.json が未生成でも SO の bake 済みベース表情を適用する。
                baseExpression: BaseExpression.EnsureCachedSnapshot().blendShapes);
        }

        public static string GetStreamingAssetsProfilePath(string assetName)
        {
            if (string.IsNullOrWhiteSpace(assetName)) return null;
            return Path.Combine(UnityEngine.Application.streamingAssetsPath, StreamingAssetsRootFolder, assetName, ProfileJsonFileName);
        }

        public virtual FacialProfile LoadProfile()
        {
            string path = GetStreamingAssetsProfilePath(CharacterAssetName);
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return BuildFallbackProfile();
            try
            {
                var repo = new FileProfileRepository(new SystemTextJsonParser());
                return repo.LoadProfile(path);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning(name + ": StreamingAssets JSON load failed, using SO data. " + ex.Message);
                return BuildFallbackProfile();
            }
        }
    }
}
