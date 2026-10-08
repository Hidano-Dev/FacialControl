using System;
using System.Collections.Generic;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Timeline.Adapters.InputSources;
using Hidano.FacialControl.Timeline.Domain.Diagnostics;
using Hidano.FacialControl.Timeline.Domain.Models;
using Hidano.FacialControl.Timeline.Tracks;
using UnityEngine;

namespace Hidano.FacialControl.Timeline.Adapters.Session
{
    /// <summary>
    /// 乗っ取りエントリ（Inspector 表示用）。
    /// </summary>
    public readonly struct TimelineTakeoverEntry
    {
        public TimelineTakeoverEntry(string channelSubId, FacialValueChannelKind kind, bool isAttached, TimelineDiagnosticCode status)
        {
            ChannelSubId = channelSubId ?? string.Empty;
            Kind = kind;
            IsAttached = isAttached;
            Status = status;
        }

        /// <summary>乗っ取り先の registry id（REC の source id）。</summary>
        public string ChannelSubId { get; }

        public FacialValueChannelKind Kind { get; }

        public bool IsAttached { get; }

        /// <summary>Attach の結果を表す診断コード。</summary>
        public TimelineDiagnosticCode Status { get; }
    }

    /// <summary>
    /// Analog / Gaze / 値提供型チャネルを ChannelSubId の registry エントリへ Replace で乗っ取り、解放時に原本を復元する（D3 / D4）。
    /// </summary>
    /// <remarks>
    /// <para>乗っ取り先は ChannelSubId（REC の source id）そのもの。最初の <c>:</c> で slug / sub に分けて Replace する。
    /// 独自 id での別途登録はしない（乗っ取りが登録そのもの）。</para>
    /// <para>占有規則（<see cref="IInjectedInputSource"/>）に従い、既存エントリが注入型なら触らない。
    /// 復元は現エントリが自分の sink と参照同一のときだけ行う。</para>
    /// <para>Analog の値は registry 購読型の消費者（<c>IRegistryAttachableAnalogConsumer</c>）が Replace 通知で読む先を差し替えることで届く。
    /// 値提供型はレイヤーのスロットが Replace 通知で sink に差し替わる（REC 再生の注入と同じ経路）。
    /// 本クラスは消費者を知らず、Replace 以外のことをしない。</para>
    /// <para>メインスレッド専用。呼び出しはセッション開始 / 終了時のみ。</para>
    /// </remarks>
    public sealed class TimelineChannelTakeover : IDisposable
    {
        private const string AttachedAnalogDetail = "registry 購読型の消費者に Timeline の値を反映します。";
        private const string AttachedGazeDetail = "目線の入力を Timeline の値で置き換えます。";
        private const string AttachedValueProviderDetail = "この入力源の BlendShape 値を Timeline の値で置き換えます。";
        private const string NotFoundDetail =
            "この id の入力源が登録されていないため、このチャネルは再生されません。REC したときと同じ AdapterBinding が Profile にあるか確認してください。";
        private const string OccupiedDetail =
            "他の注入者（REC 再生など）がこの入力源を使用中のため、このチャネルは再生されません。";
        private const string AxisCountInvalidDetail = "軸数が 0 のため、このチャネルは再生されません。再 Export してください。";
        private const string InvalidIdDetail = "ChannelSubId を入力源 id として解釈できないため、このチャネルは再生されません。";
        private const string BlendShapeMismatchDetailFormat =
            "Clip の BlendShape {0} 個のうち {1} 個がこのモデルの BlendShape に対応しないため、その分は再生されません" +
            "（BlendShape 数 {2}）。録画時と同じモデルか確認し、Profile の参照モデルを設定して再 Export すると名前で対応付けます。";

        private const string NotDeclaredDetail =
            "この入力源 id は Profile のどのレイヤー（Layer.inputSources）にも宣言されていないため、Timeline の値は合成されません。" +
            "乗っ取りはレイヤーの宣言スロットにだけ届き、実行時に後付け接続されたスロットは置き換わりません（REC 再生も同じ）。" +
            "録画時に値を受けていたレイヤーの inputSources にこの id を宣言してください。";

        private static readonly IReadOnlyList<string> EmptyNames = Array.Empty<string>();

        private readonly IInputSourceRegistry _registry;
        private readonly AdapterSlug _slug;
        private readonly List<Takeover> _takeovers = new List<Takeover>();
        private readonly List<TimelineTakeoverEntry> _entries = new List<TimelineTakeoverEntry>();

        public TimelineChannelTakeover(IInputSourceRegistry registry, AdapterSlug slug)
        {
            if (slug.Value == null)
            {
                throw new ArgumentException("slug must be initialized.", nameof(slug));
            }

            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _slug = slug;
            Entries = _entries.AsReadOnly();
        }

        /// <summary>Timeline binding の slug（診断表示用）。乗っ取り先の slug は ChannelSubId から取る。</summary>
        public AdapterSlug Slug => _slug;

        /// <summary>直近の Attach の結果（Inspector 表示用）。Release で空になる。</summary>
        public IReadOnlyList<TimelineTakeoverEntry> Entries { get; }

        /// <summary>
        /// チャネルごとに乗っ取りを試み、Analog / Gaze / ValueProvider 領域の診断を置換する。既に乗っ取り中なら先に解放する。
        /// </summary>
        /// <param name="hostBlendShapeNames">ホストの BlendShape 名列（値提供型 sink の大きさと名前の対応付けに使う）。</param>
        /// <param name="declaredLayerSourceIds">
        /// Profile の Layer.inputSources に宣言された入力源 id の集合。渡されたとき、宣言の無い値提供型チャネルに
        /// <see cref="TimelineDiagnosticCode.ValueProviderNotDeclared"/> を出す（null なら判定しない）。
        /// </param>
        public void Attach(
            IReadOnlyList<TimelineChannelDescriptor> channels,
            FacialTimelineDiagnostics diagnostics,
            IReadOnlyList<string> hostBlendShapeNames = null,
            ICollection<string> declaredLayerSourceIds = null)
        {
            if (channels == null)
            {
                throw new ArgumentNullException(nameof(channels));
            }

            if (diagnostics == null)
            {
                throw new ArgumentNullException(nameof(diagnostics));
            }

            Release();

            IReadOnlyList<string> hostNames = hostBlendShapeNames ?? EmptyNames;
            var analogItems = new List<TimelineDiagnosticItem>();
            var gazeItems = new List<TimelineDiagnosticItem>();
            var valueProviderItems = new List<TimelineDiagnosticItem>();
            for (int i = 0; i < channels.Count; i++)
            {
                TimelineChannelDescriptor channel = channels[i];
                List<TimelineDiagnosticItem> items;
                TimelineDiagnosticArea area;
                switch (channel.Kind)
                {
                    case FacialValueChannelKind.Gaze:
                        items = gazeItems;
                        area = TimelineDiagnosticArea.Gaze;
                        break;
                    case FacialValueChannelKind.ValueProvider:
                        items = valueProviderItems;
                        area = TimelineDiagnosticArea.ValueProvider;
                        break;
                    default:
                        items = analogItems;
                        area = TimelineDiagnosticArea.Analog;
                        break;
                }

                string id = channel.ChannelSubId;
                TimelineDiagnosticCode status = TryAttach(channel, hostNames, out string detail, out ITimelineTakeoverSink sink);
                bool attached = sink != null;
                TimelineDiagnosticSeverity severity = attached ? TimelineDiagnosticSeverity.Info : TimelineDiagnosticSeverity.Warning;
                items.Add(new TimelineDiagnosticItem(area, status, severity, id, detail));
                _entries.Add(new TimelineTakeoverEntry(id, channel.Kind, attached, status));

                if (attached && sink is TimelineValueProviderInputSource valueProviderSink)
                {
                    AddBlendShapeMismatch(channel, valueProviderSink, valueProviderItems);
                    if (declaredLayerSourceIds != null && !declaredLayerSourceIds.Contains(id))
                    {
                        valueProviderItems.Add(new TimelineDiagnosticItem(
                            TimelineDiagnosticArea.ValueProvider,
                            TimelineDiagnosticCode.ValueProviderNotDeclared,
                            TimelineDiagnosticSeverity.Warning,
                            id,
                            NotDeclaredDetail));
                    }
                }
            }

            diagnostics.ReplaceArea(TimelineDiagnosticArea.Analog, analogItems.ToArray());
            diagnostics.ReplaceArea(TimelineDiagnosticArea.Gaze, gazeItems.ToArray());
            diagnostics.ReplaceArea(TimelineDiagnosticArea.ValueProvider, valueProviderItems.ToArray());
        }

        /// <summary>
        /// 乗っ取ったエントリを原本へ戻す。現エントリが自分の sink と参照同一でなければ Warning を出して触らない。二重呼び出しは no-op。
        /// </summary>
        public void Release()
        {
            for (int i = _takeovers.Count - 1; i >= 0; i--)
            {
                Takeover takeover = _takeovers[i];
                ITimelineTakeoverSink sink = takeover.Sink;
                if (!_registry.TryResolve(takeover.ChannelSubId, out IInputSource current)
                    || !ReferenceEquals(current, sink))
                {
                    Debug.LogWarning(
                        $"[TimelineChannelTakeover] '{takeover.ChannelSubId}' is no longer owned by Timeline. Restoration is skipped.");
                }
                else if (sink.ReplacedSource != null)
                {
                    ReplaceEntry(takeover.TargetSlug, takeover.TargetSub, sink.ReplacedSource);
                }

                sink.ClearReplacement();
                sink.Invalidate();
            }

            _takeovers.Clear();
            _entries.Clear();
        }

        public bool TryGetAnalogSink(string channelSubId, out TimelineAnalogInputSource sink)
        {
            int index = Find(channelSubId, FacialValueChannelKind.Analog);
            sink = index >= 0 ? _takeovers[index].Sink as TimelineAnalogInputSource : null;
            return sink != null;
        }

        public bool TryGetGazeSink(string channelSubId, out TimelineGazeInputSource sink)
        {
            int index = Find(channelSubId, FacialValueChannelKind.Gaze);
            sink = index >= 0 ? _takeovers[index].Sink as TimelineGazeInputSource : null;
            return sink != null;
        }

        public bool TryGetValueProviderSink(string channelSubId, out TimelineValueProviderInputSource sink)
        {
            int index = Find(channelSubId, FacialValueChannelKind.ValueProvider);
            sink = index >= 0 ? _takeovers[index].Sink as TimelineValueProviderInputSource : null;
            return sink != null;
        }

        public void Dispose()
        {
            Release();
        }

        private TimelineDiagnosticCode TryAttach(
            TimelineChannelDescriptor channel,
            IReadOnlyList<string> hostBlendShapeNames,
            out string detail,
            out ITimelineTakeoverSink attachedSink)
        {
            attachedSink = null;
            FacialValueChannelKind kind = channel.Kind;
            string id = channel.ChannelSubId;

            if (kind == FacialValueChannelKind.Analog && channel.AxisCount <= 0)
            {
                detail = AxisCountInvalidDetail;
                return TimelineDiagnosticCode.AnalogAxisCountInvalid;
            }

            if (Find(id, kind) >= 0 || !InputSourceId.TryParse(id, out InputSourceId sinkId)
                || !TrySplit(id, out AdapterSlug targetSlug, out string targetSub))
            {
                detail = InvalidIdDetail;
                return NotFoundCode(kind);
            }

            if (!_registry.TryResolve(id, out IInputSource original) || original == null)
            {
                detail = NotFoundDetail;
                return NotFoundCode(kind);
            }

            if (original is IInjectedInputSource)
            {
                detail = OccupiedDetail;
                return OccupiedCode(kind);
            }

            ITimelineTakeoverSink sink;
            switch (kind)
            {
                case FacialValueChannelKind.Gaze:
                    sink = new TimelineGazeInputSource(sinkId);
                    detail = AttachedGazeDetail;
                    break;
                case FacialValueChannelKind.ValueProvider:
                    sink = new TimelineValueProviderInputSource(sinkId, hostBlendShapeNames);
                    detail = AttachedValueProviderDetail;
                    break;
                default:
                    sink = new TimelineAnalogInputSource(sinkId, channel.AxisCount);
                    detail = AttachedAnalogDetail;
                    break;
            }

            sink.AttachReplacement(original);
            ReplaceEntry(targetSlug, targetSub, sink);
            _takeovers.Add(new Takeover(id, kind, targetSlug, targetSub, sink));
            attachedSink = sink;
            return AttachedCode(kind);
        }

        /// <summary>Clip の BlendShape のうちホストに対応しないものがあれば Warning を足す（対応した分は再生する）。</summary>
        private static void AddBlendShapeMismatch(
            TimelineChannelDescriptor channel,
            TimelineValueProviderInputSource sink,
            List<TimelineDiagnosticItem> items)
        {
            IReadOnlyList<TimelineBlendShapeBinding> bindings = channel.BlendShapeBindings;
            int unmapped = 0;
            for (int i = 0; i < bindings.Count; i++)
            {
                if (sink.ResolveHostIndex(bindings[i].Name, bindings[i].RecordedIndex) < 0)
                {
                    unmapped++;
                }
            }

            if (unmapped == 0)
            {
                return;
            }

            items.Add(new TimelineDiagnosticItem(
                TimelineDiagnosticArea.ValueProvider,
                TimelineDiagnosticCode.ValueProviderBlendShapeMismatch,
                TimelineDiagnosticSeverity.Warning,
                channel.ChannelSubId,
                string.Format(BlendShapeMismatchDetailFormat, bindings.Count, unmapped, sink.BlendShapeCount)));
        }

        private static TimelineDiagnosticCode NotFoundCode(FacialValueChannelKind kind)
        {
            switch (kind)
            {
                case FacialValueChannelKind.Gaze:
                    return TimelineDiagnosticCode.GazeSourceNotFound;
                case FacialValueChannelKind.ValueProvider:
                    return TimelineDiagnosticCode.ValueProviderSourceNotFound;
                default:
                    return TimelineDiagnosticCode.AnalogSourceNotFound;
            }
        }

        private static TimelineDiagnosticCode OccupiedCode(FacialValueChannelKind kind)
        {
            switch (kind)
            {
                case FacialValueChannelKind.Gaze:
                    return TimelineDiagnosticCode.GazeOccupied;
                case FacialValueChannelKind.ValueProvider:
                    return TimelineDiagnosticCode.ValueProviderOccupied;
                default:
                    return TimelineDiagnosticCode.AnalogOccupied;
            }
        }

        private static TimelineDiagnosticCode AttachedCode(FacialValueChannelKind kind)
        {
            switch (kind)
            {
                case FacialValueChannelKind.Gaze:
                    return TimelineDiagnosticCode.GazeTakeoverAttached;
                case FacialValueChannelKind.ValueProvider:
                    return TimelineDiagnosticCode.ValueProviderTakeoverAttached;
                default:
                    return TimelineDiagnosticCode.AnalogTakeoverAttached;
            }
        }

        private void ReplaceEntry(AdapterSlug slug, string sub, IInputSource source)
        {
            if (string.IsNullOrEmpty(sub))
            {
                _registry.Replace(slug, source);
            }
            else
            {
                _registry.Replace(slug, sub, source);
            }
        }

        private int Find(string channelSubId, FacialValueChannelKind kind)
        {
            if (string.IsNullOrEmpty(channelSubId))
            {
                return -1;
            }

            for (int i = 0; i < _takeovers.Count; i++)
            {
                if (_takeovers[i].Kind == kind
                    && string.Equals(_takeovers[i].ChannelSubId, channelSubId, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

        private static bool TrySplit(string channelSubId, out AdapterSlug slug, out string sub)
        {
            int separator = channelSubId.IndexOf(':');
            string slugText = separator < 0 ? channelSubId : channelSubId.Substring(0, separator);
            sub = separator < 0 ? string.Empty : channelSubId.Substring(separator + 1);
            return AdapterSlug.TryParse(slugText, out slug);
        }

        private readonly struct Takeover
        {
            public Takeover(
                string channelSubId,
                FacialValueChannelKind kind,
                AdapterSlug targetSlug,
                string targetSub,
                ITimelineTakeoverSink sink)
            {
                ChannelSubId = channelSubId;
                Kind = kind;
                TargetSlug = targetSlug;
                TargetSub = targetSub;
                Sink = sink;
            }

            public string ChannelSubId { get; }
            public FacialValueChannelKind Kind { get; }
            public AdapterSlug TargetSlug { get; }
            public string TargetSub { get; }
            public ITimelineTakeoverSink Sink { get; }
        }
    }
}
