using System;
using System.Collections.Generic;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Osc.Tests.EditMode.Testing
{
    internal sealed class FakeInputSourceRegistry : IInputSourceRegistry
    {
        private readonly Dictionary<string, IInputSource> _sources = new Dictionary<string, IInputSource>(StringComparer.Ordinal);
        public readonly List<IInputSource> RegisteredSources = new List<IInputSource>();
        public int RegisterCallCount { get; private set; }
        public int ReplaceCallCount { get; private set; }
        public int UnregisterCallCount { get; private set; }
        public IReadOnlyList<string> RegisteredIds => new List<string>(_sources.Keys);

        public void Register(AdapterSlug slug, IInputSource source) => Register(slug.Value, source);
        public void Replace(AdapterSlug slug, IInputSource source) => Replace(slug.Value, source);
        public void Register(AdapterSlug slug, string sub, IInputSource source) => Register(slug.Value + ":" + sub, source);
        public void Replace(AdapterSlug slug, string sub, IInputSource source) => Replace(slug.Value + ":" + sub, source);
        public void Unregister(AdapterSlug slug) => Unregister(slug.Value);
        public void Unregister(AdapterSlug slug, string sub) => Unregister(slug.Value + ":" + sub);
        public bool TryResolve(string layerInputSourceId, out IInputSource source) => _sources.TryGetValue(layerInputSourceId, out source);
        public void Subscribe(string id, Action<IInputSource> handler) { }

        private void Register(string id, IInputSource source)
        {
            RegisterCallCount++;
            _sources[id] = source;
            RegisteredSources.Add(source);
        }

        private void Replace(string id, IInputSource source)
        {
            ReplaceCallCount++;
            _sources[id] = source;
            RegisteredSources.Add(source);
        }

        private void Unregister(string id)
        {
            UnregisterCallCount++;
            _sources.Remove(id);
        }
    }
}
