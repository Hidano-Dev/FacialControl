using System;
using System.Collections.Generic;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.LipSync.Adapters;
using NUnit.Framework;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.LipSync.Tests.EditMode.Adapters
{
    /// <summary>
    /// 発話ゲートの対象レイヤー解決と、キャプチャ系入力源とのレイヤー構成の警告を固定する。
    /// </summary>
    [TestFixture]
    [SmallTest]
    public sealed class ULipSyncLayerSetupTests : SizedTestFixture
    {
        private const string BindingSlug = "ulipsync";
        private const string CaptureSlug = "ifacialmocap";

        [TestCase("lipsync-overlay:a", "lipsync-overlay", true)]
        [TestCase("lipsync-overlay", "lipsync-overlay", true)]
        [TestCase("lipsync-overlayx:a", "lipsync-overlay", false)]
        [TestCase("ifacialmocap", "lipsync-overlay", false)]
        public void DeclaresSlug_InputSourceId_MatchesSlugOrComposite(string id, string slug, bool expected)
        {
            Assert.That(ULipSyncLayerSetup.DeclaresSlug(id, slug), Is.EqualTo(expected));
        }

        [Test]
        public void FindGateLayerNames_OverlayAndLegacySlugLayers_ReturnsThemInOrder()
        {
            var layers = new[]
            {
                Layer("capture", 1, CaptureSlug),
                Layer("lipsync", 2, "lipsync-overlay:a", "lipsync-overlay:i"),
                Layer("legacy", 3, BindingSlug),
            };

            string[] names = ULipSyncLayerSetup.FindGateLayerNames(layers, BindingSlug);

            Assert.That(names, Is.EqualTo(new[] { "lipsync", "legacy" }));
        }

        [Test]
        public void FindGateLayerNames_LayerMixedWithOtherSources_IsExcluded()
        {
            var layers = new[]
            {
                Layer("overlay", 3, "overlay:a", "lipsync-overlay:a"),
                Layer("lipsync", 2, "lipsync-overlay:a"),
            };

            string[] names = ULipSyncLayerSetup.FindGateLayerNames(layers, BindingSlug);

            Assert.That(names, Is.EqualTo(new[] { "lipsync" }),
                "他の入力源と同居するレイヤーの weight を書くと、無言の間それらも消えるため対象外。");
        }

        [Test]
        public void CollectWarnings_ULipSyncOnlyInMixedLayer_WarnsLayerWeightIsNotWritten()
        {
            var layers = new[] { Layer("overlay", 3, "overlay:a", "lipsync-overlay:a") };

            List<string> warnings = ULipSyncLayerSetup.CollectWarnings(layers, BindingSlug, null);

            Assert.That(warnings.Count, Is.EqualTo(1));
            Assert.That(warnings[0], Does.Contain("'overlay'"));
            Assert.That(warnings[0], Does.Contain("weight"));
        }

        [Test]
        public void FromProfile_LayerInputSources_CarriesNamesPrioritiesAndIds()
        {
            var profile = new FacialProfile(
                "1.0",
                layers: new[]
                {
                    new LayerDefinition("capture", 1, ExclusionMode.Blend),
                    new LayerDefinition("lipsync", 2, ExclusionMode.Blend),
                },
                layerInputSources: new[]
                {
                    new[] { new InputSourceDeclaration(CaptureSlug, 1f, null) },
                    new[] { new InputSourceDeclaration("lipsync-overlay:a", 1f, null) },
                });

            ULipSyncLayerSetup.Layer[] layers = ULipSyncLayerSetup.FromProfile(profile);

            Assert.That(ULipSyncLayerSetup.FindGateLayerNames(layers, BindingSlug), Is.EqualTo(new[] { "lipsync" }));
            Assert.That(layers[0].Priority, Is.EqualTo(1));
        }

        [Test]
        public void CollectWarnings_RecommendedSetup_ReturnsNoWarnings()
        {
            var layers = new[]
            {
                Layer("capture", 1, CaptureSlug),
                Layer("lipsync", 2, "lipsync-overlay:a"),
            };

            List<string> warnings = ULipSyncLayerSetup.CollectWarnings(layers, BindingSlug, new[] { CaptureSlug });

            Assert.That(warnings, Is.Empty);
        }

        [Test]
        public void CollectWarnings_NoULipSyncLayer_WarnsMissingLayer()
        {
            var layers = new[] { Layer("capture", 1, CaptureSlug) };

            List<string> warnings = ULipSyncLayerSetup.CollectWarnings(layers, BindingSlug, new[] { CaptureSlug });

            Assert.That(warnings.Count, Is.EqualTo(1));
            Assert.That(warnings[0], Does.Contain("lipsync-overlay"));
        }

        [Test]
        public void CollectWarnings_CapturePriorityEqualToULipSync_Warns()
        {
            var layers = new[]
            {
                Layer("capture", 2, CaptureSlug + ":head"),
                Layer("lipsync", 2, "lipsync-overlay:a"),
            };

            List<string> warnings = ULipSyncLayerSetup.CollectWarnings(layers, BindingSlug, new[] { CaptureSlug });

            Assert.That(warnings.Count, Is.EqualTo(1));
            Assert.That(warnings[0], Does.Contain("capture"));
        }

        [Test]
        public void CollectWarnings_CaptureInSameLayerAsULipSync_Warns()
        {
            var layers = new[] { Layer("lipsync", 2, "lipsync-overlay:a", CaptureSlug) };

            List<string> warnings = ULipSyncLayerSetup.CollectWarnings(layers, BindingSlug, new[] { CaptureSlug });

            Assert.That(warnings.Exists(w => w.Contains("同居") && w.Contains(CaptureSlug)), Is.True);
        }

        [Test]
        public void CollectWarnings_CaptureBelowHighestOfMultipleULipSyncLayers_UsesMaxPriority()
        {
            var layers = new[]
            {
                Layer("lipsync-low", 1, "lipsync-overlay:a"),
                Layer("capture", 2, CaptureSlug),
                Layer("lipsync-high", 3, "lipsync-overlay:i"),
            };

            List<string> warnings = ULipSyncLayerSetup.CollectWarnings(layers, BindingSlug, new[] { CaptureSlug });

            Assert.That(warnings, Is.Empty, "uLipSync のレイヤーが複数あれば最大 priority と比べる。");
        }

        [Test]
        public void AddTargetLayerInputs_OscReceiverWithoutTargetLayer_AddsSlugToFirstLayerAndExcludesItFromGate()
        {
            var layers = new[]
            {
                Layer("lipsync", 2, "lipsync-overlay:a"),
                Layer("capture", 1),
            };
            var bindings = new AdapterBindingBase[] { new StubTargetLayerBinding { Slug = "osc", Target = null } };

            ULipSyncLayerSetup.Layer[] resolved = ULipSyncLayerSetup.AddTargetLayerInputs(layers, bindings);

            Assert.That(resolved[0].InputSourceIds, Does.Contain("osc"), "対象レイヤー未指定は先頭レイヤーへ補われる。");
            Assert.That(ULipSyncLayerSetup.FindGateLayerNames(resolved, BindingSlug), Is.Empty,
                "キャプチャが補われたレイヤーの weight を書くと無言の間キャプチャも消える。");
            List<string> warnings = ULipSyncLayerSetup.CollectWarnings(resolved, BindingSlug, new[] { "osc" });
            Assert.That(warnings.Exists(w => w.Contains("同居")), Is.True);
        }

        [Test]
        public void AddTargetLayerInputs_OscReceiverTargetingCaptureLayer_KeepsLipSyncLayerAsGate()
        {
            var layers = new[]
            {
                Layer("lipsync", 2, "lipsync-overlay:a"),
                Layer("capture", 1),
            };
            var bindings = new AdapterBindingBase[] { new StubTargetLayerBinding { Slug = "osc", Target = "capture" } };

            ULipSyncLayerSetup.Layer[] resolved = ULipSyncLayerSetup.AddTargetLayerInputs(layers, bindings);

            Assert.That(resolved[1].InputSourceIds, Does.Contain("osc"));
            Assert.That(ULipSyncLayerSetup.FindGateLayerNames(resolved, BindingSlug), Is.EqualTo(new[] { "lipsync" }));
        }

        [Serializable]
        private sealed class StubTargetLayerBinding : AdapterBindingBase, IAdapterBindingTargetLayerInput
        {
            public string Target;

            public string TargetLayerName => Target;

            public string TargetLayerInputSourceId => Slug;

            public string ConfiguredTargetLayerInputSourceId => Slug;
        }

        private static ULipSyncLayerSetup.Layer Layer(string name, int priority, params string[] ids)
        {
            return new ULipSyncLayerSetup.Layer(name, priority, ids);
        }
    }
}
