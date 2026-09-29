using System.Collections.Generic;
using Hidano.FacialControl.LipSync.Adapters.Devices;
using Hidano.FacialControl.Testing;

namespace Hidano.FacialControl.LipSync.Tests.Shared
{
    internal sealed class FakePlayerPrefsBackend : IPlayerPrefsBackend, IFakeDependency
    {
        private readonly Dictionary<string, string> _strings = new Dictionary<string, string>();
        private readonly Dictionary<string, int> _ints = new Dictionary<string, int>();

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
    }
}
