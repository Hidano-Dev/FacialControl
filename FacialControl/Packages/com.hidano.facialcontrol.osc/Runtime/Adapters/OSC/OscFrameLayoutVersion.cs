using System;
using System.Collections.Generic;

namespace Hidano.FacialControl.Adapters.OSC
{
    /// <summary>
    /// 対応表のバージョン。値フレームごとに付け、受信側は手元の対応表とバージョンが一致する
    /// フレームだけを適用する。
    /// </summary>
    public static class OscFrameLayoutVersion
    {
        /// <summary>「対応表なし」を表す値。<see cref="Compute"/> はこの値を返さない。</summary>
        public const int Unknown = 0;

        /// <summary>
        /// 送信元（UUID と起動時刻）と項目の並びからバージョンを計算する。送信側の再起動では起動時刻が、
        /// モデル差し替えや BlendShape 構成の変更では項目の並びが変わるのでバージョンも変わる。
        /// ハッシュが <paramref name="previousVersion"/> と偶然一致した場合もずらして必ず別の値にする。
        /// </summary>
        public static int Compute(
            SenderIdentity identity,
            IReadOnlyList<OscFrameLayoutEntry> entries,
            int previousVersion = Unknown)
        {
            uint hash = HeartbeatHashHelper.Fnv1aOffsetBasis;

            Span<byte> uuidBytes = stackalloc byte[SenderIdentity.UuidByteLength];
            if (!identity.Uuid.TryWriteBytes(uuidBytes))
            {
                throw new InvalidOperationException("Failed to write the sender UUID.");
            }

            for (int i = 0; i < uuidBytes.Length; i++)
            {
                hash = HeartbeatHashHelper.AppendByte(hash, uuidBytes[i]);
            }

            unchecked
            {
                ulong startedAt = (ulong)identity.StartedAtUnixMs;
                for (int shift = 0; shift < 64; shift += 8)
                {
                    hash = HeartbeatHashHelper.AppendByte(hash, (byte)(startedAt >> shift));
                }
            }

            int count = entries != null ? entries.Count : 0;
            for (int i = 0; i < count; i++)
            {
                OscFrameLayoutEntry entry = entries[i];
                hash = HeartbeatHashHelper.AppendByte(hash, (byte)entry.Kind);
                hash = HeartbeatHashHelper.AppendFnv1aString(hash, entry.Value);
            }

            int version = unchecked((int)hash);
            if (version == Unknown)
            {
                version = 1;
            }

            if (version == previousVersion)
            {
                version = unchecked(version + 1);
                if (version == Unknown)
                {
                    version = 1;
                }
            }

            return version;
        }
    }
}
