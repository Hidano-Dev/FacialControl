#if FACIALCONTROL_HAS_LIPSYNC_MODULE
using System;
using System.Collections.Generic;
using System.Linq;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Editor.Windows.Routing.Logic;
using Hidano.FacialControl.LipSync.Adapters;
using NUnit.Framework;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.EditMode.Editor.Windows.Routing.Logic
{
    [Serializable]
    internal sealed class MultiSourceBindingStub :
        AdapterBindingBase,
        IAdapterBindingDefaultLayer,
        IAdapterBindingDefaultLayerInputs
    {
        public string DefaultLayerName => "lipsync";

        public string DefaultLayerInputSourceId => "legacy-source";

        public ExclusionMode DefaultLayerExclusionMode => ExclusionMode.Blend;

        public IEnumerable<(string id, float weight)> GetDefaultLayerInputSources(string layerName)
        {
            if (string.Equals(layerName, "overlay", StringComparison.Ordinal))
            {
                return new[]
                {
                    ("overlay:a", 1f),
                    ("overlay:a", 0.5f),
                    ("overlay:i", 1f),
                    ("invalid id", 1f),
                };
            }

            if (string.Equals(layerName, "fx", StringComparison.Ordinal))
            {
                return new[]
                {
                    ("fx:blink", 1f),
                };
            }

            return Array.Empty<(string id, float weight)>();
        }
    }

    [Serializable]
    internal sealed class LayerNameSensitiveBindingStub :
        AdapterBindingBase,
        IAdapterBindingDefaultLayer,
        IAdapterBindingDefaultLayerInputs
    {
        public string DefaultLayerName => "lipsync";

        public string DefaultLayerInputSourceId => string.Empty;

        public ExclusionMode DefaultLayerExclusionMode => ExclusionMode.LastWins;

        public IEnumerable<(string id, float weight)> GetDefaultLayerInputSources(string layerName)
        {
            if (string.Equals(layerName, "fx", StringComparison.Ordinal))
            {
                return new[]
                {
                    ("fx:smile", 1f),
                };
            }

            return Array.Empty<(string id, float weight)>();
        }
    }

    [Serializable]
    internal sealed class LegacyOnlyBindingStub :
        AdapterBindingBase,
        IAdapterBindingDefaultLayer
    {
        public string DefaultLayerName => "legacy-layer";

        public string DefaultLayerInputSourceId => "legacy-only";

        public ExclusionMode DefaultLayerExclusionMode => ExclusionMode.LastWins;
    }

    [Serializable]
    internal sealed class DeclaredInputsBindingStub :
        AdapterBindingBase,
        IAdapterBindingDeclaredInputs
    {
        public IEnumerable<string> GetDeclaredInputSourceIds()
        {
            return new[]
            {
                "input-system",
                "input-system:analog-expression",
                "input-system:overlay:blink",
            };
        }
    }

    [TestFixture]
    [SmallTest]
    public class SourcePortEnumeratorTests : SizedTestFixture
    {
        [Test]
        public void Enumerate_ULipSyncBinding_ReturnsOverlayPhonemePortsAndLegacySingleSource()
        {
            var enumerator = new SourcePortEnumerator();
            var binding = new ULipSyncAdapterBinding();

            IReadOnlyList<SourcePort> ports = enumerator.Enumerate(binding, new[] { "overlay" });

            CollectionAssert.AreEqual(
                new[]
                {
                    "lipsync-overlay:a",
                    "lipsync-overlay:i",
                    "lipsync-overlay:u",
                    "lipsync-overlay:e",
                    "lipsync-overlay:o",
                    "ulipsync",
                },
                ports.Select(port => port.CanonicalId).ToArray());
            CollectionAssert.AreEqual(
                new[] { "a", "i", "u", "e", "o", "ulipsync" },
                ports.Select(port => port.Label).ToArray());
        }

        [Test]
        public void Enumerate_NoLayerMatchOnDefaultName_StillEnumeratesFromAllLayerNames()
        {
            var enumerator = new SourcePortEnumerator();
            var binding = new LayerNameSensitiveBindingStub();

            IReadOnlyList<SourcePort> ports = enumerator.Enumerate(binding, new[] { "emotion", "fx" });

            CollectionAssert.AreEqual(
                new[] { "fx:smile" },
                ports.Select(port => port.CanonicalId).ToArray());
        }

        [Test]
        public void EnumerateForLayer_OverlayOnULipSyncBinding_ReturnsOnlyOverlayPhonemePorts()
        {
            var enumerator = new SourcePortEnumerator();
            var binding = new ULipSyncAdapterBinding();

            IReadOnlyList<SourcePort> ports = enumerator.EnumerateForLayer(binding, "overlay");

            CollectionAssert.AreEqual(
                new[]
                {
                    "lipsync-overlay:a",
                    "lipsync-overlay:i",
                    "lipsync-overlay:u",
                    "lipsync-overlay:e",
                    "lipsync-overlay:o",
                },
                ports.Select(port => port.CanonicalId).ToArray());
        }

        [Test]
        public void Enumerate_MultiSourceLegacyAndOverlayDerivedIds_AggregatesDistinctCanonicalIds()
        {
            var enumerator = new SourcePortEnumerator();
            var binding = new MultiSourceBindingStub { Slug = "multi-source" };

            IReadOnlyList<SourcePort> ports = enumerator.Enumerate(binding, new[] { "overlay", "fx" });

            CollectionAssert.AreEqual(
                new[]
                {
                    "overlay:a",
                    "overlay:i",
                    "fx:blink",
                    "legacy-source",
                },
                ports.Select(port => port.CanonicalId).ToArray());
            Assert.That(ports.All(port => string.Equals(port.BindingSlug, "multi-source", StringComparison.Ordinal)));
        }

        [Test]
        public void Enumerate_LegacySingleSourceBinding_ReturnsSingleWeightOnePort()
        {
            var enumerator = new SourcePortEnumerator();
            var binding = new LegacyOnlyBindingStub { Slug = "legacy-binding" };

            IReadOnlyList<SourcePort> ports = enumerator.Enumerate(binding, Array.Empty<string>());

            Assert.That(ports.Count, Is.EqualTo(1));
            Assert.That(ports[0].CanonicalId, Is.EqualTo("legacy-only"));
            Assert.That(ports[0].Label, Is.EqualTo("legacy-only"));
            Assert.That(ports[0].BindingSlug, Is.EqualTo("legacy-binding"));
        }

        [Test]
        public void Enumerate_DeclaredInputsBinding_ReturnsDeclaredCanonicalIdsAsPorts()
        {
            var enumerator = new SourcePortEnumerator();
            var binding = new DeclaredInputsBindingStub { Slug = "input-system" };

            IReadOnlyList<SourcePort> ports = enumerator.Enumerate(binding, Array.Empty<string>());

            CollectionAssert.AreEqual(
                new[]
                {
                    "input-system",
                    "input-system:analog-expression",
                    "input-system:overlay:blink",
                },
                ports.Select(port => port.CanonicalId).ToArray());
            CollectionAssert.AreEqual(
                new[] { "input-system", "analog-expression", "blink" },
                ports.Select(port => port.Label).ToArray());
            Assert.That(ports.All(port => string.Equals(port.BindingSlug, "input-system", StringComparison.Ordinal)));
        }

        [Test]
        public void EnumerateCanonicalIds_MultipleBindings_ReturnsDistinctSet()
        {
            var enumerator = new SourcePortEnumerator();
            AdapterBindingBase[] bindings =
            {
                new MultiSourceBindingStub { Slug = "multi-a" },
                new LegacyOnlyBindingStub { Slug = "legacy-b" },
            };

            ISet<string> canonicalIds = enumerator.EnumerateCanonicalIds(bindings, new[] { "overlay", "fx" });

            CollectionAssert.AreEquivalent(
                new[]
                {
                    "overlay:a",
                    "overlay:i",
                    "fx:blink",
                    "legacy-source",
                    "legacy-only",
                },
                canonicalIds);
        }
    }
}
#endif
