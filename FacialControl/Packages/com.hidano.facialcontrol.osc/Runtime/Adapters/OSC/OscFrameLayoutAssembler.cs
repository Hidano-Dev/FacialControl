using System;
using System.Collections.Generic;

namespace Hidano.FacialControl.Adapters.OSC
{
    /// <summary>
    /// 受信側で、1 つのバージョンの対応表をチャンクから組み立てる。チャンクは順不同・重複ありで届いてよい。
    /// 欠けたチャンクは <see cref="GetMissingChunkIndices"/> で得て、それだけを再要求する。
    /// </summary>
    public sealed class OscFrameLayoutAssembler
    {
        private OscFrameLayoutEntry[][] _chunks = Array.Empty<OscFrameLayoutEntry[]>();

        /// <summary>組み立て中のバージョン。未設定なら <see cref="OscFrameLayoutVersion.Unknown"/>。</summary>
        public int Version { get; private set; } = OscFrameLayoutVersion.Unknown;

        /// <summary>チャンクの総数。まだ 1 つも届いていなければ 0。</summary>
        public int ChunkCount { get; private set; }

        public int ReceivedChunkCount { get; private set; }

        public bool IsComplete => ChunkCount > 0 && ReceivedChunkCount == ChunkCount;

        /// <summary>組み立てるバージョンを切り替え、届いたチャンクを捨てる。</summary>
        public void Reset(int version)
        {
            Version = version;
            ChunkCount = 0;
            ReceivedChunkCount = 0;
            _chunks = Array.Empty<OscFrameLayoutEntry[]>();
        }

        /// <summary>
        /// チャンクを加える。バージョンが違う、チャンク総数が 0 以下か
        /// <see cref="OscIndexedFrameCodec.MaxLayoutChunkCount"/> を超える、チャンク番号が範囲外なら捨てて false。
        /// 既に届いているチャンクの重複は無視して true。同じバージョンでチャンク総数が変わった場合
        /// （送信側が分け方を変えた）は、それまでのチャンクを捨てて新しい分け方で組み立て直す。
        /// </summary>
        public bool TryAddChunk(
            int version,
            int chunkIndex,
            int chunkCount,
            IReadOnlyList<OscFrameLayoutEntry> entries)
        {
            if (version == OscFrameLayoutVersion.Unknown || version != Version)
            {
                return false;
            }

            if (chunkCount <= 0
                || chunkCount > OscIndexedFrameCodec.MaxLayoutChunkCount
                || chunkIndex < 0
                || chunkIndex >= chunkCount)
            {
                return false;
            }

            if (ChunkCount != chunkCount)
            {
                ChunkCount = chunkCount;
                ReceivedChunkCount = 0;
                _chunks = new OscFrameLayoutEntry[chunkCount][];
            }

            if (_chunks[chunkIndex] != null)
            {
                return true;
            }

            int count = entries != null ? entries.Count : 0;
            var copy = count == 0 ? Array.Empty<OscFrameLayoutEntry>() : new OscFrameLayoutEntry[count];
            for (int i = 0; i < count; i++)
            {
                copy[i] = entries[i];
            }

            _chunks[chunkIndex] = copy;
            ReceivedChunkCount++;
            return true;
        }

        /// <summary>
        /// まだ届いていないチャンク番号を <paramref name="destination"/> に入れる（クリアしてから追加）。
        /// チャンク総数が分からない（1 つも届いていない）間は空のまま返すので、そのときは全チャンクを要求する。
        /// </summary>
        public void GetMissingChunkIndices(List<int> destination)
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            destination.Clear();
            for (int i = 0; i < ChunkCount; i++)
            {
                if (_chunks[i] == null)
                {
                    destination.Add(i);
                }
            }
        }

        /// <summary>
        /// 全チャンクが揃っていれば、チャンク番号順に項目を連結して対応表を組み立てる。揃っているのに項目の
        /// 並びが不正で組み立てられない場合は、届いたチャンクを捨てて false を返す（次は全チャンクを要求し直す）。
        /// </summary>
        public bool TryBuild(out OscFrameLayout layout)
        {
            layout = null;
            if (!IsComplete)
            {
                return false;
            }

            int total = 0;
            for (int i = 0; i < _chunks.Length; i++)
            {
                total += _chunks[i].Length;
            }

            var entries = new List<OscFrameLayoutEntry>(total);
            for (int i = 0; i < _chunks.Length; i++)
            {
                entries.AddRange(_chunks[i]);
            }

            if (OscFrameLayout.TryFromEntries(Version, entries, out layout))
            {
                return true;
            }

            Reset(Version);
            return false;
        }
    }
}
