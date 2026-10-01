using System;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Adapters.OSC;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Models;
using UnityEngine;

namespace Hidano.FacialControl.Adapters.AdapterBindings.ARKit
{
    /// <summary>
    /// ARKit / PerfectSync の OSC float 経路を 1 binding に集約した
    /// <see cref="AdapterBindingBase"/> 具象。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="OnStart"/> で <c>ctx.HostGameObject.AddComponent&lt;OscReceiverHost&gt;()</c> を実行し、
    /// helper を <see cref="OscReceiverHost.Configure"/> で構成 → 既存
    /// <see cref="ArKitOscAnalogSource"/> を helper の <see cref="OscReceiver"/> に subscribe させる。
    /// </para>
    /// <para>
    /// ARKit 命名検出 (<c>ARKitDetector</c>) は binding には含めない。binding はあくまで OSC float 経路の受信を担う。
    /// </para>
    /// <para>
    /// <see cref="Dispose"/> で <c>analogSource.Dispose()</c> → <c>Object.Destroy(_helperHost)</c> →
    /// <see cref="OscDoubleBuffer.Dispose"/> の順で解放する。
    /// </para>
    /// </remarks>
    [Serializable]
    [FacialAdapterBinding(displayName: "ARKit / PerfectSync")]
    public sealed class ArKitOscAdapterBinding : AdapterBindingBase
    {
        [SerializeField]
        private string _endpoint = "127.0.0.1";

        [SerializeField]
        private int _port = OscConfiguration.DefaultReceivePort;

        [SerializeField]
        private float _stalenessSeconds;

        [SerializeField]
        private string[] _arkitParameterNames;

        [NonSerialized]
        private OscReceiverHost _helperHost;

        [NonSerialized]
        private OscDoubleBuffer _buffer;

        [NonSerialized]
        private ArKitOscAnalogSource _analogSource;

        [NonSerialized]
        private bool _started;

        /// <summary>
        /// パラメータレスコンストラクタ。Inspector の Add ドロップダウンで <c>Activator.CreateInstance</c> から
        /// 生成される必要があるため明示する。
        /// </summary>
        public ArKitOscAdapterBinding()
        {
        }

        /// <summary>送信元エンドポイント（IP/host）。診断用。</summary>
        public string Endpoint
        {
            get => _endpoint;
            set => _endpoint = value;
        }

        /// <summary>受信 UDP ポート。</summary>
        public int Port
        {
            get => _port;
            set => _port = value;
        }

        /// <summary>staleness 判定秒数（0 で staleness 無効）。</summary>
        public float StalenessSeconds
        {
            get => _stalenessSeconds;
            set => _stalenessSeconds = value;
        }

        /// <summary>ARKit パラメータ名配列（e.g. <c>jawOpen</c>, <c>eyeBlinkLeft</c>, ... PerfectSync 52ch）。</summary>
        public string[] ArKitParameterNames
        {
            get => _arkitParameterNames;
            set => _arkitParameterNames = value;
        }

        /// <summary>OnStart で確保した helper MonoBehaviour（テスト/診断用、未開始は null）。</summary>
        public OscReceiverHost HelperHost => _helperHost;

        /// <summary>OnStart で構築した <see cref="ArKitOscAnalogSource"/>（テスト/診断用、未開始は null）。</summary>
        public ArKitOscAnalogSource AnalogSource => _analogSource;

        /// <summary>OnStart 済みかどうか。</summary>
        public bool IsStarted => _started;

        /// <summary>
        /// Runtime / テストから endpoint・port・parameter names・staleness をまとめて設定する。
        /// </summary>
        /// <remarks>
        /// PropertyDrawer が完成するまでは inline serialized 化された設定を直接持たないため、
        /// テストや手動配線では本メソッドで一括設定する（task 9.4 の PropertyDrawer 実装後に inline 化予定）。
        /// </remarks>
        public void Configure(string endpoint, int port, string[] arkitParameterNames, float stalenessSeconds = 0f)
        {
            if (arkitParameterNames == null) throw new ArgumentNullException(nameof(arkitParameterNames));
            if (stalenessSeconds < 0f)
                throw new ArgumentOutOfRangeException(nameof(stalenessSeconds), stalenessSeconds,
                    "stalenessSeconds は 0 以上を指定してください。");

            _endpoint = endpoint;
            _port = port;
            _arkitParameterNames = arkitParameterNames;
            _stalenessSeconds = stalenessSeconds;
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
                Debug.LogError("[ArKitOscAdapterBinding] HostGameObject が null のため ARKit binding を起動できません。");
                return;
            }

            if (_arkitParameterNames == null || _arkitParameterNames.Length == 0)
            {
                Debug.LogWarning(
                    $"[ArKitOscAdapterBinding] ARKit parameter names が未設定のため subscribe しません。slug='{Slug}'");
                return;
            }

            if (!AdapterSlug.TryParse(Slug, out _))
            {
                Debug.LogError(
                    $"[ArKitOscAdapterBinding] Slug '{Slug}' が AdapterSlug 規約を満たしません。");
                return;
            }

            // ARKit binding は OSC mapping を使わず、receiver 直登録の analog listener 経由で値を受け取る。
            // OscReceiverHost は buffer / mappings の non-null を要求するため空のものを渡す。
            _buffer = new OscDoubleBuffer(0);

            _helperHost = ctx.HostGameObject.AddComponent<OscReceiverHost>();
            _helperHost.Configure(_endpoint, _port, _buffer, Array.Empty<OscMapping>());

            var sourceId = InputSourceId.Parse(Slug);
            _analogSource = new ArKitOscAnalogSource(
                sourceId,
                _helperHost.Receiver,
                _arkitParameterNames,
                _stalenessSeconds);

            _started = true;
        }

        /// <inheritdoc />
        public override void OnTick(float deltaTime)
        {
            if (!_started || _analogSource == null)
            {
                return;
            }

            _analogSource.Tick(deltaTime);
        }

        /// <inheritdoc />
        public override void OnFixedTick(float fixedDeltaTime)
        {
            if (!_started)
            {
                return;
            }

            // OSC receiver の write/read バッファ swap を進める（OscReceiverAdapterBinding と同様の自前 tick 化）。
            if (_helperHost != null)
            {
                _helperHost.Tick();
            }
        }

        /// <inheritdoc />
        public override void Dispose()
        {
            if (_analogSource != null)
            {
                _analogSource.Dispose();
                _analogSource = null;
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

            _started = false;
        }
    }
}
