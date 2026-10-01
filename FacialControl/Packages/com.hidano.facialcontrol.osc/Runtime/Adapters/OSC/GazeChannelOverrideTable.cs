using System;
using System.Collections.Generic;
using Hidano.FacialControl.Adapters.ScriptableObject;

namespace Hidano.FacialControl.Adapters.OSC
{
    /// <summary>
    /// gaze 広告の属性ペアから得た、チャネル id ごとの <see cref="GazeChannelOverride"/> を保持する。
    /// 内容が変わったときだけ <see cref="Version"/> を進める。
    /// </summary>
    /// <remarks>
    /// メインスレッド専用。<see cref="Update"/> は広告の中身が変わったとき (受信バイト列のハッシュ変化時)
    /// にだけ呼ぶ想定で、毎フレームは呼ばない。
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
        /// 広告 payload ([id, value, ...]) から、属性がそろったチャネル (<c>range=</c> まで届いたもの) の上書きを
        /// 取り込む。payload に現れないチャネルの上書きは残す (MTU 分割された広告の途中で消さないため)。
        /// 内容が変わったら true を返し、<see cref="Version"/> を進める。
        /// </summary>
        public bool Update(IReadOnlyList<string> payload)
        {
            GazeAdvertisementResolver.ParseChannelOverrides(payload, _scratch, ref _warnedOnInvalidAttribute);
            bool changed = false;
            foreach (KeyValuePair<string, GazeChannelOverride> pair in _scratch)
            {
                if (_current.TryGetValue(pair.Key, out GazeChannelOverride existing) && existing.Equals(pair.Value))
                {
                    continue;
                }

                _current[pair.Key] = pair.Value;
                changed = true;
            }

            if (changed)
            {
                unchecked
                {
                    Version++;
                }
            }

            return changed;
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
    }
}
