using System;
using System.Collections.Generic;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Editor.Windows.Routing.Logic
{
    public readonly struct InvalidDeclarationRef : IEquatable<InvalidDeclarationRef>
    {
        public InvalidDeclarationRef(int layerIndex, int declarationIndex, string id)
        {
            LayerIndex = layerIndex;
            DeclarationIndex = declarationIndex;
            Id = id ?? string.Empty;
        }

        public int LayerIndex { get; }

        public int DeclarationIndex { get; }

        public string Id { get; }

        public bool Equals(InvalidDeclarationRef other)
        {
            return LayerIndex == other.LayerIndex
                && DeclarationIndex == other.DeclarationIndex
                && string.Equals(Id, other.Id, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is InvalidDeclarationRef other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = LayerIndex;
                hash = (hash * 397) ^ DeclarationIndex;
                hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(Id);
                return hash;
            }
        }
    }

    public interface IInvalidIdValidator
    {
        IReadOnlyList<InvalidDeclarationRef> Validate(
            FacialCharacterProfileSO profile,
            ISet<string> validCanonicalIds);
    }

    /// <summary>
    /// Profile の各レイヤー inputSources 宣言のうち、既知の canonical id に一致しないものを列挙する。
    /// </summary>
    /// <remarks>
    /// <see cref="IAdapterBindingDynamicInputs"/> を実装する binding（Timeline 等、実行時に id を導出する binding）
    /// については、その <c>{Slug}:</c> prefix に一致する宣言 id を有効扱いにする。
    /// prefix の後ろに sub が無い id（<c>timeline:</c>）は従来どおり不正扱い。
    /// </remarks>
    public sealed class InvalidIdValidator : IInvalidIdValidator
    {
        public IReadOnlyList<InvalidDeclarationRef> Validate(
            FacialCharacterProfileSO profile,
            ISet<string> validCanonicalIds)
        {
            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }

            if (validCanonicalIds == null)
            {
                throw new ArgumentNullException(nameof(validCanonicalIds));
            }

            var invalidDeclarations = new List<InvalidDeclarationRef>();
            IList<LayerDefinitionSerializable> layers = profile.Layers;
            if (layers == null)
            {
                return invalidDeclarations;
            }

            List<string> dynamicPrefixes = CollectDynamicInputPrefixes(profile.AdapterBindings);

            for (int layerIndex = 0; layerIndex < layers.Count; layerIndex++)
            {
                LayerDefinitionSerializable layer = layers[layerIndex];
                IList<InputSourceDeclarationSerializable> declarations = layer?.inputSources;
                if (declarations == null)
                {
                    continue;
                }

                for (int declarationIndex = 0; declarationIndex < declarations.Count; declarationIndex++)
                {
                    string id = declarations[declarationIndex]?.id ?? string.Empty;
                    if (validCanonicalIds.Contains(id))
                    {
                        continue;
                    }

                    if (MatchesDynamicPrefix(id, dynamicPrefixes))
                    {
                        continue;
                    }

                    invalidDeclarations.Add(new InvalidDeclarationRef(layerIndex, declarationIndex, id));
                }
            }

            return invalidDeclarations;
        }

        /// <summary>
        /// <see cref="IAdapterBindingDynamicInputs"/> を実装し、有効な slug を持つ binding の <c>{Slug}:</c> prefix を集める。
        /// </summary>
        private static List<string> CollectDynamicInputPrefixes(IReadOnlyList<AdapterBindingBase> bindings)
        {
            var prefixes = new List<string>();
            if (bindings == null)
            {
                return prefixes;
            }

            for (int i = 0; i < bindings.Count; i++)
            {
                AdapterBindingBase binding = bindings[i];
                if (!(binding is IAdapterBindingDynamicInputs))
                {
                    continue;
                }

                if (!AdapterSlug.TryParse(binding.Slug, out AdapterSlug slug))
                {
                    continue;
                }

                prefixes.Add(slug.Value + ":");
            }

            return prefixes;
        }

        private static bool MatchesDynamicPrefix(string id, List<string> prefixes)
        {
            for (int i = 0; i < prefixes.Count; i++)
            {
                string prefix = prefixes[i];
                if (id.Length > prefix.Length && id.StartsWith(prefix, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
