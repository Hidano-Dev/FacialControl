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
    /// Analog / Gaze チャネルを ChannelSubId の registry エントリへ Replace で乗っ取り、解放時に原本を復元する（D3 / D4）。
    /// </summary>
    /// <remarks>
    /// <para>乗っ取り先は ChannelSubId（REC の source id）そのもの。最初の <c>:</c> で slug / sub に分けて Replace する。
    /// 独自 id での別途登録はしない（乗っ取りが登録そのもの）。</para>
    /// <para>占有規則（<see cref="IInjectedInputSource"/>）に従い、既存エントリが注入型なら触らない。
    /// 復元は現エントリが自分の sink と参照同一のときだけ行う。</para>
    /// <para>Analog の値は registry 購読型の消費者（<c>IRegistryAttachableAnalogConsumer</c>）が Replace 通知で読む先を差し替えることで届く。
    /// 本クラスは消費者を知らず、Replace 以外のことをしない。</para>
    /// <para>メインスレッド専用。呼び出しはセッション開始 / 終了時のみ。</para>
    /// </remarks>
    public sealed class TimelineChannelTakeover : IDisposable
    {
        private const string AttachedAnalogDetail = "registry 購読型の消費者に Timeline の値を反映します。";
        private const string AttachedGazeDetail = "目線の入力を Timeline の値で置き換えます。";
        private const string NotFoundDetail =
            "この id の入力源が登録されていないため、このチャネルは再生されません。REC したときと同じ AdapterBinding が Profile にあるか確認してください。";
        private const string OccupiedDetail =
            "他の注入者（REC 再生など）がこの入力源を使用中のため、このチャネルは再生されません。";
        private const string AxisCountInvalidDetail = "軸数が 0 のため、このチャネルは再生されません。再 Export してください。";
        private const string InvalidIdDetail = "ChannelSubId を入力源 id として解釈できないため、このチャネルは再生されません。";

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
        /// チャネルごとに乗っ取りを試み、Analog / Gaze 領域の診断を置換する。既に乗っ取り中なら先に解放する。
        /// </summary>
        public void Attach(IReadOnlyList<TimelineChannelDescriptor> channels, FacialTimelineDiagnostics diagnostics)
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

            var analogItems = new List<TimelineDiagnosticItem>();
            var gazeItems = new List<TimelineDiagnosticItem>();
            for (int i = 0; i < channels.Count; i++)
            {
                TimelineChannelDescriptor channel = channels[i];
                bool isGaze = channel.Kind == FacialValueChannelKind.Gaze;
                List<TimelineDiagnosticItem> items = isGaze ? gazeItems : analogItems;
                TimelineDiagnosticArea area = isGaze ? TimelineDiagnosticArea.Gaze : TimelineDiagnosticArea.Analog;
                string id = channel.ChannelSubId;

                TimelineDiagnosticCode status = TryAttach(channel, isGaze, out string detail);
                TimelineDiagnosticSeverity severity = status == TimelineDiagnosticCode.AnalogTakeoverAttached
                    || status == TimelineDiagnosticCode.GazeTakeoverAttached
                    ? TimelineDiagnosticSeverity.Info
                    : TimelineDiagnosticSeverity.Warning;
                items.Add(new TimelineDiagnosticItem(area, status, severity, id, detail));
                _entries.Add(new TimelineTakeoverEntry(id, channel.Kind, severity == TimelineDiagnosticSeverity.Info, status));
            }

            diagnostics.ReplaceArea(TimelineDiagnosticArea.Analog, analogItems.ToArray());
            diagnostics.ReplaceArea(TimelineDiagnosticArea.Gaze, gazeItems.ToArray());
        }

        /// <summary>
        /// 乗っ取ったエントリを原本へ戻す。現エントリが自分の sink と参照同一でなければ Warning を出して触らない。二重呼び出しは no-op。
        /// </summary>
        public void Release()
        {
            for (int i = _takeovers.Count - 1; i >= 0; i--)
            {
                Takeover takeover = _takeovers[i];
                TimelineAnalogInputSource sink = takeover.Sink;
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
            sink = index >= 0 ? _takeovers[index].Sink : null;
            return sink != null;
        }

        public bool TryGetGazeSink(string channelSubId, out TimelineGazeInputSource sink)
        {
            int index = Find(channelSubId, FacialValueChannelKind.Gaze);
            sink = index >= 0 ? _takeovers[index].Sink as TimelineGazeInputSource : null;
            return sink != null;
        }

        public void Dispose()
        {
            Release();
        }

        private TimelineDiagnosticCode TryAttach(TimelineChannelDescriptor channel, bool isGaze, out string detail)
        {
            TimelineDiagnosticCode notFound = isGaze ? TimelineDiagnosticCode.GazeSourceNotFound : TimelineDiagnosticCode.AnalogSourceNotFound;
            string id = channel.ChannelSubId;

            if (!isGaze && channel.AxisCount <= 0)
            {
                detail = AxisCountInvalidDetail;
                return TimelineDiagnosticCode.AnalogAxisCountInvalid;
            }

            if (Find(id, channel.Kind) >= 0 || !InputSourceId.TryParse(id, out InputSourceId sinkId)
                || !TrySplit(id, out AdapterSlug targetSlug, out string targetSub))
            {
                detail = InvalidIdDetail;
                return notFound;
            }

            if (!_registry.TryResolve(id, out IInputSource original) || original == null)
            {
                detail = NotFoundDetail;
                return notFound;
            }

            if (original is IInjectedInputSource)
            {
                detail = OccupiedDetail;
                return isGaze ? TimelineDiagnosticCode.GazeOccupied : TimelineDiagnosticCode.AnalogOccupied;
            }

            TimelineAnalogInputSource sink = isGaze
                ? new TimelineGazeInputSource(sinkId)
                : new TimelineAnalogInputSource(sinkId, channel.AxisCount);
            sink.AttachReplacement(original);
            ReplaceEntry(targetSlug, targetSub, sink);
            _takeovers.Add(new Takeover(id, channel.Kind, targetSlug, targetSub, sink));

            detail = isGaze ? AttachedGazeDetail : AttachedAnalogDetail;
            return isGaze ? TimelineDiagnosticCode.GazeTakeoverAttached : TimelineDiagnosticCode.AnalogTakeoverAttached;
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
                TimelineAnalogInputSource sink)
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
            public TimelineAnalogInputSource Sink { get; }
        }
    }
}
