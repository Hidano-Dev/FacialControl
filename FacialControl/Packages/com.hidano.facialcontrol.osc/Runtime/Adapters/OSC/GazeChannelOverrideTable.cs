using System;
using System.Collections.Generic;
using Hidano.FacialControl.Adapters.ScriptableObject;

namespace Hidano.FacialControl.Adapters.OSC
{
    /// <summary>
    /// 値フレームの対応表の gaze チャネル属性から得た、チャネル id ごとの <see cref="GazeChannelOverride"/> を保持する。
    /// 内容が変わったときだけ <see cref="Version"/> を進める。
    /// </summary>
    /// <remarks>
    /// メインスレッド専用。<see cref="Update"/> は対応表を適用したときにだけ呼ぶ想定で、毎フレームは呼ばない。
    /// </remarks>
    public sealed class GazeChannelOverrideTable
    {
        private readonly Dictionary<string, GazeChannelOverride> _current =
            new Dictionary<string, GazeChannelOverride>(StringComparer.Ordinal);
        private readonly Dictionary<string, GazeChannelOverride> _scratch =
            new Dictionary<string, GazeChannelOverride>(StringComparer.Ordinal);
        private bool _warnedOnInvalidAttribute;

        /// <summary>上書きの内容が変わるたびに進む値。</summary>
        public int Version { get; private set; }

        /// <summary>上書きを持つチャネルの数。</summary>
        public int Count => _current.Count;

        /// <summary>
        /// 対応表の gaze チャネルで上書きを置き換える。有効な <c>range=</c> を持たないチャネルと、対応表に無い
        /// チャネルの上書きは消える。id が重複するチャネルは先のものを使う。内容が変わったら true を返し、
        /// <see cref="Version"/> を進める。
        /// </summary>
        public bool Update(IReadOnlyList<OscFrameLayoutGazeChannel> channels)
        {
            _scratch.Clear();
            if (channels != null)
            {
                for (int i = 0; i < channels.Count; i++)
                {
                    OscFrameLayoutGazeChannel channel = channels[i];
                    if (channel == null || string.IsNullOrEmpty(channel.Id) || _scratch.ContainsKey(channel.Id))
                    {
                        continue;
                    }

                    if (GazeChannelAttributes.TryParseOverride(
                            channel.Id,
                            channel.Attributes,
                            out GazeChannelOverride value,
                            ref _warnedOnInvalidAttribute))
                    {
                        _scratch.Add(channel.Id, value);
                    }
                }
            }

            if (HasSameContent(_current, _scratch))
            {
                return false;
            }

            _current.Clear();
            foreach (KeyValuePair<string, GazeChannelOverride> pair in _scratch)
            {
                _current.Add(pair.Key, pair.Value);
            }

            unchecked
            {
                Version++;
            }

            return true;
        }

        public bool TryGet(string channelId, out GazeChannelOverride value)
        {
            if (string.IsNullOrEmpty(channelId))
            {
                value = default;
                return false;
            }

            return _current.TryGetValue(channelId, out value);
        }

        /// <summary>上書きをすべて消す。消すものがあったときだけ <see cref="Version"/> を進める。</summary>
        public void Clear()
        {
            _scratch.Clear();
            _warnedOnInvalidAttribute = false;
            if (_current.Count == 0)
            {
                return;
            }

            _current.Clear();
            unchecked
            {
                Version++;
            }
        }

        private static bool HasSameContent(
            Dictionary<string, GazeChannelOverride> left,
            Dictionary<string, GazeChannelOverride> right)
        {
            if (left.Count != right.Count)
            {
                return false;
            }

            foreach (KeyValuePair<string, GazeChannelOverride> pair in right)
            {
                if (!left.TryGetValue(pair.Key, out GazeChannelOverride existing) || !existing.Equals(pair.Value))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
