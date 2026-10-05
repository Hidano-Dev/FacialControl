using System;
using System.Collections.Generic;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Adapters.Playable;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Timeline.Adapters.Assets;
using Hidano.FacialControl.Timeline.Adapters.InputSources;
using Hidano.FacialControl.Timeline.Domain.Diagnostics;
using Hidano.FacialControl.Timeline.Domain.Models;
using Hidano.FacialControl.Timeline.Domain.Services;

namespace Hidano.FacialControl.Timeline.Adapters.Session
{
    /// <summary>
    /// <see cref="TimelineLayerConnector.Connect"/> の結果。
    /// </summary>
    public enum ConnectOutcome
    {
        /// <summary>接続処理を完了した（個々のレイヤーの成否は診断の LayerConnection 領域に出る）。</summary>
        Connected = 0,

        /// <summary>旧 <c>{slug}:{layer}:state</c> 宣言（または state id のレイヤー接続）を検出し、何も接続せずに中断した。</summary>
        LegacyStateDeclaration = 1,

        /// <summary>FacialController が未初期化のため何もしなかった（呼び出し側が後で再試行する）。</summary>
        ControllerNotInitialized = 2,
    }

    /// <summary>
    /// 導出済みレイヤーごとに Timeline の値 sink / state sink を生成し、registry 登録と FacialController への接続 / 解放を行う（D2）。
    /// </summary>
    /// <remarks>
    /// <para>値 sink は registry に登録し、宣言経路で既に接続されていればそれに任せ（<see cref="TimelineDiagnosticCode.LayerConnectionSkippedDeclared"/>）、
    /// 未接続なら weight 1 で後付け接続する。state sink はレイヤー入力源にも registry にも繋がず、状態入力源
    /// （overlay suppress の active provider と観測）としてだけ登録する。</para>
    /// <para>旧 <c>:state</c> 宣言は互換維持しない。Profile の宣言に 1 件でもあれば何も登録せずに中断し、
    /// 静的走査をすり抜けて state id がレイヤーに接続されていた場合も登録済みを全て戻して中断する。</para>
    /// <para>同じ (Bake, ホスト BlendShape 名, Profile, id) で再接続する場合は前回の sink を再利用する。</para>
    /// <para>メインスレッド専用。呼び出しはセッション開始 / 終了時のみ（確保はその時点に閉じる）。</para>
    /// </remarks>
    public sealed class TimelineLayerConnector : IDisposable
    {
        public const int DefaultMaxStackDepth = 16;

        private const string LegacyStateDetailFormat =
            "Layer.inputSources から '{0}' を削除してください。Receiver Inspector の『旧 timeline 宣言を削除』で除去できます。";
        private const string FallbackDetail =
            "レイヤー名を id に使えないため index 形の id を使います（ASCII 英数字・_ . - のみの名前なら名前形になります）。";
        private const string ConnectedDetail = "Timeline の値をこのレイヤーへ weight 1 で接続しました。";
        private const string SkippedDeclaredDetail =
            "Layer.inputSources の宣言（値 sink）で接続済みのため、宣言の weight を使います。";
        private const string FailedDetail =
            "このレイヤーへ接続できませんでした。FacialController の初期化状態とレイヤー名を確認してください。";

        private readonly FacialController _controller;
        private readonly IInputSourceRegistry _registry;
        private readonly AdapterSlug _slug;
        private readonly int _maxStackDepth;

        private readonly List<LayerConnection> _connections = new List<LayerConnection>();
        private readonly List<string> _connectedLayerNames = new List<string>();
        private readonly Dictionary<string, PooledSinks> _pool = new Dictionary<string, PooledSinks>(StringComparer.Ordinal);

        public TimelineLayerConnector(
            FacialController controller,
            IInputSourceRegistry registry,
            AdapterSlug slug,
            int maxStackDepth = DefaultMaxStackDepth)
        {
            if (slug.Value == null)
            {
                throw new ArgumentException("slug must be initialized.", nameof(slug));
            }

            _controller = controller != null ? controller : throw new ArgumentNullException(nameof(controller));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _slug = slug;
            _maxStackDepth = maxStackDepth;
            ConnectedLayerNames = _connectedLayerNames.AsReadOnly();
        }

        /// <summary>値 sink が接続済み（自前接続または宣言経路）のレイヤー名。</summary>
        public IReadOnlyList<string> ConnectedLayerNames { get; }

        /// <param name="derivation">導出結果（一致済みレイヤーのみ使う）。</param>
        /// <param name="profile">controller が保持する Profile と同じ値。旧 state 宣言の静的走査と state sink の Expression 解決に使う。</param>
        /// <param name="hostBlendShapeNames">ホストの BlendShape 名列。</param>
        /// <param name="bake">採用した Bake。null なら値 sink は空の名前集合で生成する（値再生は無効）。</param>
        /// <param name="diagnostics">LayerConnection 領域を置換する診断状態。</param>
        public ConnectOutcome Connect(
            TimelineDerivation derivation,
            FacialProfile profile,
            IReadOnlyList<string> hostBlendShapeNames,
            FacialTimelineBakeAsset bake,
            FacialTimelineDiagnostics diagnostics)
        {
            if (derivation == null)
            {
                throw new ArgumentNullException(nameof(derivation));
            }

            if (hostBlendShapeNames == null)
            {
                throw new ArgumentNullException(nameof(hostBlendShapeNames));
            }

            if (diagnostics == null)
            {
                throw new ArgumentNullException(nameof(diagnostics));
            }

            Disconnect();

            if (!_controller.IsInitialized)
            {
                return ConnectOutcome.ControllerNotInitialized;
            }

            var items = new List<TimelineDiagnosticItem>();
            if (CollectLegacyStateDeclarations(profile, items))
            {
                diagnostics.ReplaceArea(TimelineDiagnosticArea.LayerConnection, items.ToArray());
                return ConnectOutcome.LegacyStateDeclaration;
            }

            ReadOnlySpan<LayerDefinition> profileLayers = profile.Layers.Span;
            IReadOnlyList<TimelineLayerDescriptor> layers = derivation.Layers;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < layers.Count; i++)
            {
                TimelineLayerDescriptor layer = layers[i];
                if (!layer.IsMatched
                    || layer.LayerIndex >= profileLayers.Length
                    || !string.Equals(profileLayers[layer.LayerIndex].Name, layer.LayerName, StringComparison.Ordinal)
                    || !seen.Add(layer.LayerName))
                {
                    continue;
                }

                LayerDefinition definition = profileLayers[layer.LayerIndex];
                InputSourceId valueId = TimelineSinkIdConvention.ComposeValueId(
                    _slug, layer.LayerName, layer.LayerIndex, out bool usedFallback);
                InputSourceId stateId = TimelineSinkIdConvention.ComposeStateId(
                    _slug, layer.LayerName, layer.LayerIndex, out _);
                if (usedFallback)
                {
                    items.Add(Item(TimelineDiagnosticCode.LayerSinkIdFallback, TimelineDiagnosticSeverity.Info, layer.LayerName, FallbackDetail));
                }

                PooledSinks sinks = AcquireSinks(layer.LayerName, valueId, stateId, definition, hostBlendShapeNames, profile, bake);
                var connection = new LayerConnection(layer.LayerName, valueId.Value, stateId.Value, sinks.ValueSink, sinks.StateSink);
                _connections.Add(connection);

                _registry.Register(_slug, SubOf(valueId.Value), sinks.ValueSink);
                connection.ValueRegistered = true;

                if (_controller.IsLayerInputSourceBound(layer.LayerName, connection.ValueId))
                {
                    items.Add(Item(TimelineDiagnosticCode.LayerConnectionSkippedDeclared, TimelineDiagnosticSeverity.Info, layer.LayerName, SkippedDeclaredDetail));
                    _connectedLayerNames.Add(layer.LayerName);
                }
                else if (_controller.TryBindLayerInputSource(layer.LayerName, connection.ValueId, sinks.ValueSink, 1f))
                {
                    connection.ValueBoundBySelf = true;
                    items.Add(Item(TimelineDiagnosticCode.LayerConnected, TimelineDiagnosticSeverity.Info, layer.LayerName, ConnectedDetail));
                    _connectedLayerNames.Add(layer.LayerName);
                }
                else
                {
                    items.Add(Item(TimelineDiagnosticCode.LayerConnectionFailed, TimelineDiagnosticSeverity.Warning, layer.LayerName, FailedDetail));
                }

                if (_controller.IsLayerInputSourceBound(layer.LayerName, connection.StateId))
                {
                    // 静的走査をすり抜けた経路で state id がレイヤーに接続されている。非接続方針（D2）を守れないため中断する。
                    Disconnect();
                    var legacy = new[]
                    {
                        Item(
                            TimelineDiagnosticCode.LegacyStateDeclaration,
                            TimelineDiagnosticSeverity.Error,
                            LegacySubject(layer.LayerName, connection.StateId),
                            string.Format(LegacyStateDetailFormat, connection.StateId)),
                    };
                    diagnostics.ReplaceArea(TimelineDiagnosticArea.LayerConnection, legacy);
                    return ConnectOutcome.LegacyStateDeclaration;
                }

                connection.StateRegistered = _controller.TryRegisterLayerStateSource(
                    layer.LayerName, connection.StateId, sinks.StateSink);
            }

            diagnostics.ReplaceArea(TimelineDiagnosticArea.LayerConnection, items.ToArray());
            return ConnectOutcome.Connected;
        }

        /// <summary>
        /// 自前で接続したものだけ解放し、登録した値 sink を registry から外し、sink を TriggerOff / Invalidate する。二重呼び出しは no-op。
        /// </summary>
        public void Disconnect()
        {
            if (_connections.Count == 0)
            {
                _connectedLayerNames.Clear();
                return;
            }

            bool controllerAlive = _controller != null && _controller.IsInitialized;
            for (int i = _connections.Count - 1; i >= 0; i--)
            {
                LayerConnection connection = _connections[i];
                if (controllerAlive && connection.StateRegistered)
                {
                    _controller.UnregisterLayerStateSource(connection.LayerName, connection.StateId);
                }

                if (controllerAlive && connection.ValueBoundBySelf)
                {
                    _controller.UnbindLayerInputSource(connection.LayerName, connection.ValueId);
                }

                if (connection.ValueRegistered
                    && _registry.TryResolve(connection.ValueId, out IInputSource current)
                    && ReferenceEquals(current, connection.ValueSink))
                {
                    // 宣言経路で後付けされていれば、null 通知で core 側が自動で外す。
                    _registry.Unregister(_slug, SubOf(connection.ValueId));
                }

                TriggerOffAll(connection.StateSink);
                connection.ValueSink.Invalidate();
            }

            _connections.Clear();
            _connectedLayerNames.Clear();
        }

        public bool TryGetValueSink(string layerName, out TimelineBakedValueSink sink)
        {
            int index = FindConnection(layerName);
            sink = index >= 0 ? _connections[index].ValueSink : null;
            return sink != null;
        }

        public bool TryGetStateSink(string layerName, out TimelineExpressionStateSink sink)
        {
            int index = FindConnection(layerName);
            sink = index >= 0 ? _connections[index].StateSink : null;
            return sink != null;
        }

        public void Dispose()
        {
            Disconnect();
            _pool.Clear();
        }

        private bool CollectLegacyStateDeclarations(FacialProfile profile, List<TimelineDiagnosticItem> items)
        {
            ReadOnlySpan<LayerDefinition> profileLayers = profile.Layers.Span;
            ReadOnlySpan<InputSourceDeclaration[]> declarations = profile.LayerInputSources.Span;
            bool found = false;
            for (int layerIndex = 0; layerIndex < declarations.Length; layerIndex++)
            {
                InputSourceDeclaration[] layerDeclarations = declarations[layerIndex];
                if (layerDeclarations == null)
                {
                    continue;
                }

                string layerName = layerIndex < profileLayers.Length ? profileLayers[layerIndex].Name : string.Empty;
                for (int i = 0; i < layerDeclarations.Length; i++)
                {
                    string id = layerDeclarations[i].Id;
                    if (!TimelineSinkIdConvention.IsLegacyStateDeclaration(id, _slug))
                    {
                        continue;
                    }

                    found = true;
                    items.Add(Item(
                        TimelineDiagnosticCode.LegacyStateDeclaration,
                        TimelineDiagnosticSeverity.Error,
                        LegacySubject(layerName, id),
                        string.Format(LegacyStateDetailFormat, id)));
                }
            }

            if (found)
            {
                // 中断時の領域は旧宣言の Error だけにする（途中まで集めた Info は残さない）。
                items.RemoveAll(item => item.Code != TimelineDiagnosticCode.LegacyStateDeclaration);
            }

            return found;
        }

        private PooledSinks AcquireSinks(
            string layerName,
            InputSourceId valueId,
            InputSourceId stateId,
            LayerDefinition definition,
            IReadOnlyList<string> hostBlendShapeNames,
            FacialProfile profile,
            FacialTimelineBakeAsset bake)
        {
            if (_pool.TryGetValue(layerName, out PooledSinks pooled)
                && pooled.Matches(valueId.Value, stateId.Value, hostBlendShapeNames, profile, bake))
            {
                return pooled;
            }

            var stateSink = new TimelineExpressionStateSink(
                stateId,
                _maxStackDepth,
                definition.ExclusionMode,
                hostBlendShapeNames,
                profile);
            var valueSink = new TimelineBakedValueSink(
                valueId,
                hostBlendShapeNames,
                CollectBakedBlendShapeNames(bake, layerName));

            var created = new PooledSinks(valueSink, stateSink, valueId.Value, stateId.Value, hostBlendShapeNames, profile, bake);
            _pool[layerName] = created;
            return created;
        }

        private int FindConnection(string layerName)
        {
            if (string.IsNullOrEmpty(layerName))
            {
                return -1;
            }

            for (int i = 0; i < _connections.Count; i++)
            {
                if (string.Equals(_connections[i].LayerName, layerName, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

        private string SubOf(string id)
        {
            return id.Substring(_slug.Value.Length + 1);
        }

        private static void TriggerOffAll(TimelineExpressionStateSink sink)
        {
            IReadOnlyList<string> active = sink.ActiveExpressionIds;
            for (int i = active.Count - 1; i >= 0; i--)
            {
                sink.TriggerOff(active[i]);
            }
        }

        private static string LegacySubject(string layerName, string declaredId)
        {
            return layerName + " / " + declaredId;
        }

        private static TimelineDiagnosticItem Item(
            TimelineDiagnosticCode code,
            TimelineDiagnosticSeverity severity,
            string subject,
            string detail)
        {
            return new TimelineDiagnosticItem(TimelineDiagnosticArea.LayerConnection, code, severity, subject, detail);
        }

        private static string[] CollectBakedBlendShapeNames(FacialTimelineBakeAsset bake, string layerName)
        {
            if (bake == null || bake.ExpressionBakes == null)
            {
                return Array.Empty<string>();
            }

            for (int bakeIndex = 0; bakeIndex < bake.ExpressionBakes.Length; bakeIndex++)
            {
                ExpressionSourceBake expressionBake = bake.ExpressionBakes[bakeIndex];
                if (expressionBake == null || !string.Equals(expressionBake.LayerName, layerName, StringComparison.Ordinal))
                {
                    continue;
                }

                BlendShapeCurve[] curves = expressionBake.Curves ?? Array.Empty<BlendShapeCurve>();
                var names = new string[curves.Length];
                for (int i = 0; i < curves.Length; i++)
                {
                    names[i] = curves[i]?.BlendShapeName ?? string.Empty;
                }

                return names;
            }

            return Array.Empty<string>();
        }

        private sealed class LayerConnection
        {
            public LayerConnection(
                string layerName,
                string valueId,
                string stateId,
                TimelineBakedValueSink valueSink,
                TimelineExpressionStateSink stateSink)
            {
                LayerName = layerName;
                ValueId = valueId;
                StateId = stateId;
                ValueSink = valueSink;
                StateSink = stateSink;
            }

            public string LayerName { get; }
            public string ValueId { get; }
            public string StateId { get; }
            public TimelineBakedValueSink ValueSink { get; }
            public TimelineExpressionStateSink StateSink { get; }
            public bool ValueRegistered { get; set; }
            public bool ValueBoundBySelf { get; set; }
            public bool StateRegistered { get; set; }
        }

        private sealed class PooledSinks
        {
            private readonly string _valueId;
            private readonly string _stateId;
            private readonly IReadOnlyList<string> _hostBlendShapeNames;
            private readonly FacialProfile _profile;
            private readonly FacialTimelineBakeAsset _bake;

            public PooledSinks(
                TimelineBakedValueSink valueSink,
                TimelineExpressionStateSink stateSink,
                string valueId,
                string stateId,
                IReadOnlyList<string> hostBlendShapeNames,
                FacialProfile profile,
                FacialTimelineBakeAsset bake)
            {
                ValueSink = valueSink;
                StateSink = stateSink;
                _valueId = valueId;
                _stateId = stateId;
                _hostBlendShapeNames = hostBlendShapeNames;
                _profile = profile;
                _bake = bake;
            }

            public TimelineBakedValueSink ValueSink { get; }
            public TimelineExpressionStateSink StateSink { get; }

            public bool Matches(
                string valueId,
                string stateId,
                IReadOnlyList<string> hostBlendShapeNames,
                FacialProfile profile,
                FacialTimelineBakeAsset bake)
            {
                // Profile は値型だが中身は配列参照なので、同じ配列を指しているか（= 同じ Profile スナップショットか）で比べる。
                return string.Equals(_valueId, valueId, StringComparison.Ordinal)
                    && string.Equals(_stateId, stateId, StringComparison.Ordinal)
                    && ReferenceEquals(_hostBlendShapeNames, hostBlendShapeNames)
                    && ReferenceEquals(_bake, bake)
                    && _profile.Layers.Equals(profile.Layers)
                    && _profile.Expressions.Equals(profile.Expressions);
            }
        }
    }
}
