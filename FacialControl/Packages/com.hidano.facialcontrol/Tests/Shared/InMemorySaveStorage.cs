using System.Collections.Generic;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Testing;

namespace Hidano.FacialControl.Tests.Shared
{
    /// <summary>
    /// <see cref="ISaveStorage"/> のインメモリ Fake。PlayerPrefs に触れずに保存・読み出しの振る舞いを検証する。
    /// </summary>
    public sealed class InMemorySaveStorage : ISaveStorage, IFakeDependency
    {
        private readonly Dictionary<string, string> _strings = new Dictionary<string, string>();
        private readonly Dictionary<string, int> _ints = new Dictionary<string, int>();

        /// <summary><see cref="Save"/> が呼ばれた回数。</summary>
        public int SaveCallCount { get; private set; }

        public string GetString(string key, string defaultValue)
        {
            return _strings.TryGetValue(key, out var value) ? value : defaultValue;
        }

        public int GetInt(string key, int defaultValue)
        {
            return _ints.TryGetValue(key, out var value) ? value : defaultValue;
        }

        public void SetString(string key, string value)
        {
            _strings[key] = value;
        }

        public void SetInt(string key, int value)
        {
            _ints[key] = value;
        }

        public void Save()
        {
            SaveCallCount++;
        }

        public bool ContainsStringKey(string key)
        {
            return _strings.ContainsKey(key);
        }

        public bool ContainsIntKey(string key)
        {
            return _ints.ContainsKey(key);
        }

        /// <summary>全データと呼び出し回数を初期状態に戻す。</summary>
        public void Clear()
        {
            _strings.Clear();
            _ints.Clear();
            SaveCallCount = 0;
        }
    }
}
