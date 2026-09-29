using System.Collections.Generic;
using System.Text.RegularExpressions;
using Hidano.FacialControl.Adapters.OSC;
using Hidano.FacialControl.Domain.Services;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.EditMode.Adapters
{
    [TestFixture]
    [SmallTest]
    public sealed class AddressPresetEstimatorTests : SizedTestFixture
    {
        [Test]
        public void Estimate_VrChatPresetName_ReturnsVrChat()
        {
            bool warnedUnknown = false;
            bool warnedMissingCustomPrefix = false;

            AddressPresetEstimator.EstimationResult result = AddressPresetEstimator.Estimate(
                AddressPresetEstimator.PresetVrChat,
                null,
                AllArKitNames(),
                ref warnedUnknown,
                ref warnedMissingCustomPrefix);

            Assert.AreEqual(AddressPresetKind.VRChat, result.Preset);
            Assert.IsNull(result.CustomPrefix);
            Assert.IsFalse(warnedUnknown);
            Assert.IsFalse(warnedMissingCustomPrefix);
        }

        [Test]
        public void Estimate_ArKitPresetName_ReturnsArKit()
        {
            bool warnedUnknown = false;
            bool warnedMissingCustomPrefix = false;

            AddressPresetEstimator.EstimationResult result = AddressPresetEstimator.Estimate(
                AddressPresetEstimator.PresetArKit,
                null,
                System.Array.Empty<string>(),
                ref warnedUnknown,
                ref warnedMissingCustomPrefix);

            Assert.AreEqual(AddressPresetKind.ARKit, result.Preset);
            Assert.IsNull(result.CustomPrefix);
            Assert.IsFalse(warnedUnknown);
            Assert.IsFalse(warnedMissingCustomPrefix);
        }

        [Test]
        public void Estimate_CustomPresetNameWithPrefix_ReturnsCustomAndPrefix()
        {
            bool warnedUnknown = false;
            bool warnedMissingCustomPrefix = false;

            AddressPresetEstimator.EstimationResult result = AddressPresetEstimator.Estimate(
                AddressPresetEstimator.PresetCustom,
                "/myapp/",
                System.Array.Empty<string>(),
                ref warnedUnknown,
                ref warnedMissingCustomPrefix);

            Assert.AreEqual(AddressPresetKind.Custom, result.Preset);
            Assert.AreEqual("/myapp/", result.CustomPrefix);
            Assert.IsFalse(warnedUnknown);
            Assert.IsFalse(warnedMissingCustomPrefix);
        }

        [Test]
        public void Estimate_CustomPresetNameWithoutPrefix_FallsBackToVrChatAndWarnsOnce()
        {
            bool warnedUnknown = false;
            bool warnedMissingCustomPrefix = false;
            LogAssert.Expect(LogType.Warning, new Regex("custom preset.*missing prefix.*VRChat fallback"));

            AddressPresetEstimator.EstimationResult first = AddressPresetEstimator.Estimate(
                AddressPresetEstimator.PresetCustom,
                null,
                AllArKitNames(),
                ref warnedUnknown,
                ref warnedMissingCustomPrefix);
            AddressPresetEstimator.EstimationResult second = AddressPresetEstimator.Estimate(
                AddressPresetEstimator.PresetCustom,
                string.Empty,
                AllArKitNames(),
                ref warnedUnknown,
                ref warnedMissingCustomPrefix);

            Assert.AreEqual(AddressPresetKind.VRChat, first.Preset);
            Assert.AreEqual(AddressPresetKind.VRChat, second.Preset);
            Assert.IsTrue(warnedMissingCustomPrefix);
            Assert.IsFalse(warnedUnknown);
        }

        [Test]
        public void Estimate_UnknownPresetName_FallsBackToNameBasedEstimateAndWarnsOnce()
        {
            bool warnedUnknown = false;
            bool warnedMissingCustomPrefix = false;
            LogAssert.Expect(LogType.Warning, new Regex("unknown preset 'mystery'"));

            AddressPresetEstimator.EstimationResult first = AddressPresetEstimator.Estimate(
                "mystery",
                null,
                AllArKitNames(),
                ref warnedUnknown,
                ref warnedMissingCustomPrefix);
            AddressPresetEstimator.EstimationResult second = AddressPresetEstimator.Estimate(
                "mystery",
                null,
                AllArKitNames(),
                ref warnedUnknown,
                ref warnedMissingCustomPrefix);

            Assert.AreEqual(AddressPresetKind.ARKit, first.Preset);
            Assert.AreEqual(AddressPresetKind.ARKit, second.Preset);
            Assert.IsTrue(warnedUnknown);
            Assert.IsFalse(warnedMissingCustomPrefix);
        }

        [Test]
        public void Estimate_NoPresetAndNoArKitNames_ReturnsVrChat()
        {
            bool warnedUnknown = false;
            bool warnedMissingCustomPrefix = false;

            AddressPresetEstimator.EstimationResult result = AddressPresetEstimator.Estimate(
                null,
                null,
                new[] { "Smile", "Angry", "Fun" },
                ref warnedUnknown,
                ref warnedMissingCustomPrefix);

            Assert.AreEqual(AddressPresetKind.VRChat, result.Preset);
            Assert.IsNull(result.CustomPrefix);
        }

        [Test]
        public void Estimate_NoPresetAndTwentySixArKitNames_ReturnsArKitAtFiftyPercentBoundary()
        {
            bool warnedUnknown = false;
            bool warnedMissingCustomPrefix = false;

            AddressPresetEstimator.EstimationResult result = AddressPresetEstimator.Estimate(
                null,
                null,
                FirstArKitNames(26),
                ref warnedUnknown,
                ref warnedMissingCustomPrefix);

            Assert.AreEqual(AddressPresetKind.ARKit, result.Preset);
            Assert.IsNull(result.CustomPrefix);
        }

        [Test]
        public void Estimate_NoPresetAndTwentyFiveArKitNames_ReturnsVrChatBelowFiftyPercentBoundary()
        {
            bool warnedUnknown = false;
            bool warnedMissingCustomPrefix = false;

            AddressPresetEstimator.EstimationResult result = AddressPresetEstimator.Estimate(
                null,
                null,
                FirstArKitNames(25),
                ref warnedUnknown,
                ref warnedMissingCustomPrefix);

            Assert.AreEqual(AddressPresetKind.VRChat, result.Preset);
            Assert.IsNull(result.CustomPrefix);
        }

        [Test]
        public void Estimate_NoPresetAndAllArKitNames_ReturnsArKit()
        {
            bool warnedUnknown = false;
            bool warnedMissingCustomPrefix = false;

            AddressPresetEstimator.EstimationResult result = AddressPresetEstimator.Estimate(
                null,
                null,
                AllArKitNames(),
                ref warnedUnknown,
                ref warnedMissingCustomPrefix);

            Assert.AreEqual(AddressPresetKind.ARKit, result.Preset);
            Assert.IsNull(result.CustomPrefix);
        }

        private static IReadOnlyList<string> AllArKitNames()
        {
            return ARKitDetector.ARKit52Names;
        }

        private static IReadOnlyList<string> FirstArKitNames(int count)
        {
            var names = new string[count];
            for (int i = 0; i < count; i++)
            {
                names[i] = ARKitDetector.ARKit52Names[i];
            }

            return names;
        }
    }
}
