using System;
using System.Collections.Generic;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.InputSystem.Tests.PlayMode.Integration
{
    internal sealed class FakeInputSourceRegistry : IInputSourceRegistry
    {
        internal readonly struct Registration
        {
            public Registration(string operation, string id, IInputSource source)
            {
                Operation = operation;
                Id = id;
                Source = source;
            }

            public string Operation { get; }
            public string Id { get; }
            public IInputSource Source { get; }
        }

        private readonly Dictionary<string, IInputSource> _sources = new Dictionary<string, IInputSource>(StringComparer.Ordinal);
        private readonly List<string> _registeredIds = new List<string>();

        internal List<Registration> Calls { get; } = new List<Registration>();
        public IReadOnlyList<string> RegisteredIds => _registeredIds;

        public void Register(AdapterSlug slug, IInputSource source) => Register(slug.Value, source);
        public void Register(AdapterSlug slug, string sub, IInputSource source) => Register(slug.Value + ":" + sub, source);
        public void Replace(AdapterSlug slug, IInputSource source) => Replace(slug.Value, source);
        public void Replace(AdapterSlug slug, string sub, IInputSource source) => Replace(slug.Value + ":" + sub, source);
        public void Unregister(AdapterSlug slug) => Unregister(slug.Value);
        public void Unregister(AdapterSlug slug, string sub) => Unregister(slug.Value + ":" + sub);
        public bool TryResolve(string id, out IInputSource source) => _sources.TryGetValue(id, out source);
        public void Subscribe(string id, Action<IInputSource> handler) { }

        private void Register(string id, IInputSource source)
        {
            Record("Register", id, source);
            if (!_sources.ContainsKey(id)) _registeredIds.Add(id);
            _sources[id] = source;
        }

        private void Replace(string id, IInputSource source)
        {
            Record("Replace", id, source);
            if (!_sources.ContainsKey(id)) _registeredIds.Add(id);
            _sources[id] = source;
        }

        private void Unregister(string id)
        {
            Record("Unregister", id, null);
            if (_sources.Remove(id)) _registeredIds.Remove(id);
        }

        private void Record(string operation, string id, IInputSource source)
        {
            Calls.Add(new Registration(operation, id, source));
        }
    }
}
