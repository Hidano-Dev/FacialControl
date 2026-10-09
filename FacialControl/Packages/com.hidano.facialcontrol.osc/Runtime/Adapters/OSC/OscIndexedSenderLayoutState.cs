using System;
using System.Collections.Generic;

namespace Hidano.FacialControl.Adapters.OSC
{
    /// <summary>
    /// 受信側が送信元 1 つ（送信元 UUID 1 つ）について持つ対応表の状態。値フレームのバージョンを観測し、
    /// 揃っていなければ対応表要求を作り、届いたチャンクを組み立てる。時刻は呼び出し側から秒で受け取り、
    /// ソケットや Unity の API には触れない（要求の送信と対応表の適用は呼び出し側が行う）。
    /// </summary>
    public sealed class OscIndexedSenderLayoutState
    {
        private readonly OscLayoutRequestScheduler _scheduler;
        private readonly OscFrameLayoutAssembler _assembler = new OscFrameLayoutAssembler();
        private readonly List<int> _missingChunkIndices = new List<int>();
        private readonly int _maxRequestChunkIndices;

        public OscIndexedSenderLayoutState(
            Guid senderUuid,
            double retryIntervalSeconds = OscLayoutRequestScheduler.DefaultRetryIntervalSeconds,
            double warnAfterSeconds = OscLayoutRequestScheduler.DefaultWarnAfterSeconds,
            int maxRequestBytes = OscIndexedFrameCodec.DefaultMaxMessageBytes)
        {
            SenderUuid = senderUuid;
            _scheduler = new OscLayoutRequestScheduler(retryIntervalSeconds, warnAfterSeconds);
            _maxRequestChunkIndices = OscIndexedFrameCodec.GetMaxLayoutRequestChunkIndices(maxRequestBytes);
        }

        public Guid SenderUuid { get; }

        /// <summary>対応表要求の宛先（値フレームの送信元）。呼び出し側が決めて入れる。分からなければ null。</summary>
        public object RequestDestination { get; set; }

        /// <summary>最後に値フレームを観測した時刻（秒）。</summary>
        public double LastSeenSeconds { get; private set; }

        /// <summary>適用済み（<see cref="MarkApplied"/> 済み）の対応表のバージョン。</summary>
        public int AppliedVersion => _scheduler.ReadyVersion;

        /// <summary>対応表を待っているバージョン。無ければ <see cref="OscFrameLayoutVersion.Unknown"/>。</summary>
        public int PendingVersion => _scheduler.PendingVersion;

        public bool HasPending => _scheduler.HasPending;

        /// <summary><paramref name="version"/> の値フレームを適用してよいか（その対応表を適用済みか）。</summary>
        public bool CanApply(int version)
        {
            return _scheduler.IsReady(version);
        }

        /// <summary>値フレームのバージョンを観測する。適用済みでなければ対応表を待つ。</summary>
        public void ObserveValues(int version, double nowSeconds)
        {
            LastSeenSeconds = nowSeconds;
            _scheduler.ObserveFrameVersion(version, nowSeconds);
            if (_scheduler.HasPending && _assembler.Version != _scheduler.PendingVersion)
            {
                _assembler.Reset(_scheduler.PendingVersion);
            }
        }

        /// <summary>
        /// 対応表のチャンクを加える。待っているバージョンのチャンクでなければ捨てて false。
        /// </summary>
        public bool TryAddChunk(int version, int chunkIndex, int chunkCount, IReadOnlyList<OscFrameLayoutEntry> entries)
        {
            if (!_scheduler.HasPending || version != _scheduler.PendingVersion)
            {
                return false;
            }

            if (_assembler.Version != version)
            {
                _assembler.Reset(version);
            }

            return _assembler.TryAddChunk(version, chunkIndex, chunkCount, entries);
        }

        /// <summary>
        /// 今送るべき対応表要求があれば作って true。届いていないチャンクだけを要求し、その番号が 1 通に
        /// 収まらないか、チャンク総数がまだ分からなければ全チャンクを要求する。宛先が分からない間と、
        /// 対応表が揃って適用を待っている間は要求を作らない（再要求の間隔を消費しない）。
        /// </summary>
        public bool TryCreateRequest(double nowSeconds, out byte[] request)
        {
            request = null;
            if (RequestDestination == null || !_scheduler.HasPending)
            {
                return false;
            }

            int version = _scheduler.PendingVersion;
            if (_assembler.Version != version)
            {
                _assembler.Reset(version);
            }

            if (_assembler.IsComplete || !_scheduler.TryTakeRequest(nowSeconds))
            {
                return false;
            }

            _assembler.GetMissingChunkIndices(_missingChunkIndices);
            IReadOnlyList<int> indices = _missingChunkIndices.Count <= _maxRequestChunkIndices
                ? _missingChunkIndices
                : null;
            request = OscIndexedFrameCodec.WriteLayoutRequestMessage(SenderUuid, version, indices);
            return true;
        }

        /// <summary>
        /// 待っているバージョンの対応表が揃っていれば組み立てて true。項目の並びが不正なら届いたチャンクを
        /// 捨てて false（次の要求で全チャンクを取り直す）。
        /// </summary>
        public bool TryTakeCompletedLayout(out OscFrameLayout layout)
        {
            layout = null;
            if (!_scheduler.HasPending
                || _assembler.Version != _scheduler.PendingVersion
                || !_assembler.IsComplete)
            {
                return false;
            }

            return _assembler.TryBuild(out layout);
        }

        /// <summary>
        /// 揃った対応表を適用できなかったときに、届いたチャンクを捨てる（次の要求で全チャンクを取り直す）。
        /// </summary>
        public void DiscardAssembledChunks()
        {
            _assembler.Reset(_scheduler.HasPending ? _scheduler.PendingVersion : OscFrameLayoutVersion.Unknown);
        }

        /// <summary><paramref name="version"/> の対応表を適用したことを記録し、その値フレームを適用できるようにする。</summary>
        public void MarkApplied(int version)
        {
            _scheduler.MarkReady(version);
            if (!_scheduler.HasPending)
            {
                _assembler.Reset(OscFrameLayoutVersion.Unknown);
            }
        }

        /// <summary>揃わない状態が続いたときに 1 回だけ true。</summary>
        public bool TryTakeWarning(double nowSeconds)
        {
            return _scheduler.TryTakeWarning(nowSeconds);
        }

        public double GetPendingSeconds(double nowSeconds)
        {
            return _scheduler.GetPendingSeconds(nowSeconds);
        }

        /// <summary>届いたチャンク数とチャンク総数（警告文に添える用途）。</summary>
        public void GetChunkProgress(out int received, out int total)
        {
            received = _assembler.ReceivedChunkCount;
            total = _assembler.ChunkCount;
        }
    }
}
