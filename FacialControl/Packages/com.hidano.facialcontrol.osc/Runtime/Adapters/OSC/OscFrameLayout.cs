using System;
using System.Collections.Generic;

namespace Hidano.FacialControl.Adapters.OSC
{
    /// <summary>
    /// 対応表の 1 項目の種類。値は <c>/_facialcontrol/layout</c> の int 引数としてそのまま送る。
    /// </summary>
    public enum OscFrameLayoutEntryKind
    {
        /// <summary>BlendShape 名。値フレームの slot を 1 つ使う。</summary>
        BlendShape = 0,

        /// <summary>gaze チャネル id。値フレームの slot を X / Y の 2 つ使う。</summary>
        GazeChannel = 1,

        /// <summary>直前の <see cref="GazeChannel"/> の属性（<c>bone.left=...</c> / <c>range=...</c> 等）。slot は使わない。</summary>
        GazeAttribute = 2
    }

    /// <summary>
    /// 対応表の 1 項目。対応表は項目の並びとして送受信し、受信側で <see cref="OscFrameLayout"/> に組み立てる。
    /// </summary>
    public readonly struct OscFrameLayoutEntry : IEquatable<OscFrameLayoutEntry>
    {
        public readonly OscFrameLayoutEntryKind Kind;
        public readonly string Value;

        /// <summary>
        /// OSC 文字列は NUL 終端なので、値に含まれる U+0000 は U+FFFD に置き換える
        /// （そのまま送ると読み取り側で値が途中で切れ、チャンク全体が壊れる）。
        /// </summary>
        public OscFrameLayoutEntry(OscFrameLayoutEntryKind kind, string value)
        {
            Kind = kind;
            Value = SanitizeValue(value);
        }

        private static string SanitizeValue(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value.IndexOf('\0') < 0 ? value : value.Replace('\0', '\uFFFD');
        }

        public static bool IsDefinedKind(int kind)
        {
            return kind == (int)OscFrameLayoutEntryKind.BlendShape
                || kind == (int)OscFrameLayoutEntryKind.GazeChannel
                || kind == (int)OscFrameLayoutEntryKind.GazeAttribute;
        }

        public bool Equals(OscFrameLayoutEntry other)
        {
            return Kind == other.Kind && string.Equals(Value, other.Value, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is OscFrameLayoutEntry other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((int)Kind * 397) ^ (Value != null ? StringComparer.Ordinal.GetHashCode(Value) : 0);
            }
        }

        public override string ToString()
        {
            return $"{Kind}:{Value}";
        }
    }

    /// <summary>
    /// 対応表に載る gaze チャネル 1 件。属性は <see cref="GazeChannelAttributes"/> の <c>key=value</c> 形式の文字列。
    /// </summary>
    public sealed class OscFrameLayoutGazeChannel
    {
        private readonly string[] _attributes;

        public OscFrameLayoutGazeChannel(string id, IReadOnlyList<string> attributes = null)
        {
            Id = id ?? string.Empty;
            if (attributes == null || attributes.Count == 0)
            {
                _attributes = Array.Empty<string>();
                return;
            }

            _attributes = new string[attributes.Count];
            for (int i = 0; i < attributes.Count; i++)
            {
                _attributes[i] = attributes[i] ?? string.Empty;
            }
        }

        public string Id { get; }

        public IReadOnlyList<string> Attributes => _attributes;
    }

    /// <summary>
    /// 値フレームの slot と名前の対応表。slot は BlendShape を項目の順に並べ、その後ろに
    /// gaze チャネルごとの X / Y を並べる。<see cref="Version"/> が一致する値フレームにだけ使う。
    /// </summary>
    public sealed class OscFrameLayout
    {
        /// <summary>gaze チャネル 1 件が使う slot 数（X, Y）。</summary>
        public const int GazeSlotsPerChannel = 2;

        private readonly string[] _blendShapeNames;
        private readonly OscFrameLayoutGazeChannel[] _gazeChannels;

        public OscFrameLayout(
            int version,
            IReadOnlyList<string> blendShapeNames,
            IReadOnlyList<OscFrameLayoutGazeChannel> gazeChannels = null)
        {
            Version = version;
            _blendShapeNames = CopyNames(blendShapeNames);
            _gazeChannels = CopyGazeChannels(gazeChannels);
        }

        public int Version { get; }

        public IReadOnlyList<string> BlendShapeNames => _blendShapeNames;

        public IReadOnlyList<OscFrameLayoutGazeChannel> GazeChannels => _gazeChannels;

        /// <summary>値フレームの slot 総数。</summary>
        public int SlotCount => _blendShapeNames.Length + (_gazeChannels.Length * GazeSlotsPerChannel);

        /// <summary><paramref name="gazeChannelIndex"/> 番目の gaze チャネルの X の slot。Y はその次。</summary>
        public int GetGazeSlotIndex(int gazeChannelIndex)
        {
            if (gazeChannelIndex < 0 || gazeChannelIndex >= _gazeChannels.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(gazeChannelIndex));
            }

            return _blendShapeNames.Length + (gazeChannelIndex * GazeSlotsPerChannel);
        }

        /// <summary>送信用の項目の並びに展開する。各 gaze チャネルの属性はそのチャネルの直後に置く。</summary>
        public OscFrameLayoutEntry[] ToEntries()
        {
            return ToEntries(_blendShapeNames, _gazeChannels);
        }

        /// <summary>
        /// 対応表を作る前（バージョン計算の前）に項目の並びを得るための展開。
        /// </summary>
        public static OscFrameLayoutEntry[] ToEntries(
            IReadOnlyList<string> blendShapeNames,
            IReadOnlyList<OscFrameLayoutGazeChannel> gazeChannels)
        {
            int blendShapeCount = blendShapeNames != null ? blendShapeNames.Count : 0;
            int count = blendShapeCount;
            int gazeCount = gazeChannels != null ? gazeChannels.Count : 0;
            for (int i = 0; i < gazeCount; i++)
            {
                OscFrameLayoutGazeChannel channel = gazeChannels[i];
                count += 1 + (channel != null ? channel.Attributes.Count : 0);
            }

            var entries = new OscFrameLayoutEntry[count];
            int write = 0;
            for (int i = 0; i < blendShapeCount; i++)
            {
                entries[write++] = new OscFrameLayoutEntry(OscFrameLayoutEntryKind.BlendShape, blendShapeNames[i]);
            }

            for (int i = 0; i < gazeCount; i++)
            {
                OscFrameLayoutGazeChannel channel = gazeChannels[i];
                entries[write++] = new OscFrameLayoutEntry(OscFrameLayoutEntryKind.GazeChannel, channel?.Id);
                if (channel == null)
                {
                    continue;
                }

                for (int a = 0; a < channel.Attributes.Count; a++)
                {
                    entries[write++] = new OscFrameLayoutEntry(OscFrameLayoutEntryKind.GazeAttribute, channel.Attributes[a]);
                }
            }

            return entries;
        }

        /// <summary>
        /// 受信した項目の並びから対応表を組み立てる。項目は BlendShape をすべて並べた後に gaze チャネル
        /// （とその属性）を並べる順でなければならない。gaze チャネルの後に BlendShape がある、チャネルより
        /// 前に属性がある、または未知の種類が含まれる場合は false。
        /// </summary>
        public static bool TryFromEntries(
            int version,
            IReadOnlyList<OscFrameLayoutEntry> entries,
            out OscFrameLayout layout)
        {
            layout = null;
            if (entries == null)
            {
                return false;
            }

            var blendShapeNames = new List<string>(entries.Count);
            var gazeIds = new List<string>();
            var gazeAttributes = new List<List<string>>();
            for (int i = 0; i < entries.Count; i++)
            {
                OscFrameLayoutEntry entry = entries[i];
                switch (entry.Kind)
                {
                    case OscFrameLayoutEntryKind.BlendShape:
                        if (gazeIds.Count > 0)
                        {
                            return false;
                        }

                        blendShapeNames.Add(entry.Value);
                        break;
                    case OscFrameLayoutEntryKind.GazeChannel:
                        gazeIds.Add(entry.Value);
                        gazeAttributes.Add(new List<string>());
                        break;
                    case OscFrameLayoutEntryKind.GazeAttribute:
                        if (gazeAttributes.Count == 0)
                        {
                            return false;
                        }

                        gazeAttributes[gazeAttributes.Count - 1].Add(entry.Value);
                        break;
                    default:
                        return false;
                }
            }

            var gazeChannels = new OscFrameLayoutGazeChannel[gazeIds.Count];
            for (int i = 0; i < gazeIds.Count; i++)
            {
                gazeChannels[i] = new OscFrameLayoutGazeChannel(gazeIds[i], gazeAttributes[i]);
            }

            layout = new OscFrameLayout(version, blendShapeNames, gazeChannels);
            return true;
        }

        private static string[] CopyNames(IReadOnlyList<string> names)
        {
            if (names == null || names.Count == 0)
            {
                return Array.Empty<string>();
            }

            var copy = new string[names.Count];
            for (int i = 0; i < names.Count; i++)
            {
                copy[i] = names[i] ?? string.Empty;
            }

            return copy;
        }

        private static OscFrameLayoutGazeChannel[] CopyGazeChannels(IReadOnlyList<OscFrameLayoutGazeChannel> channels)
        {
            if (channels == null || channels.Count == 0)
            {
                return Array.Empty<OscFrameLayoutGazeChannel>();
            }

            var copy = new OscFrameLayoutGazeChannel[channels.Count];
            for (int i = 0; i < channels.Count; i++)
            {
                copy[i] = channels[i] ?? new OscFrameLayoutGazeChannel(string.Empty);
            }

            return copy;
        }
    }
}
