#if FACIALCONTROL_HAS_LIPSYNC_MODULE
using System.Collections.Generic;
using System.Linq;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.LipSync.Adapters;
using NUnit.Framework;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.EditMode.Domain
{
    [TestFixture]
    [SmallTest]
    public class IAdapterBindingDefaultLayerInputsContractTests : SizedTestFixture
    {
        [Test]
        public void GetDefaultLayerInputSources_OverlayLayer_ReturnsPhonemeSlotIds()
        {
            var binding = new DummyDefaultLayerInputs();

            var sources = binding.GetDefaultLayerInputSources("overlay").ToArray();

            Assert.AreEqual(5, sources.Length);
            Assert.AreEqual(("overlay:a", 1.0f), sources[0]);
            Assert.AreEqual(("overlay:i", 1.0f), sources[1]);
            Assert.AreEqual(("overlay:u", 1.0f), sources[2]);
            Assert.AreEqual(("overlay:e", 1.0f), sources[3]);
            Assert.AreEqual(("overlay:o", 1.0f), sources[4]);
        }

        [Test]
        public void ULipSyncAdapterBinding_OverlayLayer_ReturnsLipSyncOverlaySlotIds()
        {
            var binding = new ULipSyncAdapterBinding();

            Assert.That(binding, Is.InstanceOf<IAdapterBindingDefaultLayerInputs>());

            var sources = ((IAdapterBindingDefaultLayerInputs)binding)
                .GetDefaultLayerInputSources("overlay")
                .ToArray();

            Assert.AreEqual(5, sources.Length);
            Assert.AreEqual(("lipsync-overlay:a", 1.0f), sources[0]);
            Assert.AreEqual(("lipsync-overlay:i", 1.0f), sources[1]);
            Assert.AreEqual(("lipsync-overlay:u", 1.0f), sources[2]);
            Assert.AreEqual(("lipsync-overlay:e", 1.0f), sources[3]);
            Assert.AreEqual(("lipsync-overlay:o", 1.0f), sources[4]);
        }

        private sealed class DummyDefaultLayerInputs : IAdapterBindingDefaultLayerInputs
        {
            public IEnumerable<(string id, float weight)> GetDefaultLayerInputSources(string layerName)
            {
                if (layerName != "overlay")
                {
                    return Enumerable.Empty<(string id, float weight)>();
                }

                return new[]
                {
                    ($"overlay:{PhonemeOverlaySlots.A}", 1.0f),
                    ($"overlay:{PhonemeOverlaySlots.I}", 1.0f),
                    ($"overlay:{PhonemeOverlaySlots.U}", 1.0f),
                    ($"overlay:{PhonemeOverlaySlots.E}", 1.0f),
                    ($"overlay:{PhonemeOverlaySlots.O}", 1.0f)
                };
            }
        }
    }
}
#endif
