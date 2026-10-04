using System;
using System.Collections;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;
using Hidano.FacialControl.Rec.Adapters.Playback;
using Hidano.FacialControl.Rec.Domain.Models;
using NUnit.Framework;
using Hidano.FacialControl.Testing;

namespace Hidano.FacialControl.Rec.Tests.EditMode
{
    [TestFixture]
    [SmallTest]
    public sealed class RecValueProviderInjectorTests : SizedTestFixture
    {
        [Test]
        public void CanBeginInjection_BlendShapeCountZero_ReturnsFalseWithReason()
        {
            var injector = new RecValueProviderInjector(new InputSourceRegistry(), () => 0);

            Assert.That(injector.CanBeginInjection(out string reason), Is.False);
            Assert.That(reason, Is.EqualTo("value-provider injection requires an initialised FacialController (BlendShapeCount is 0)"));
        }

        [Test]
        public void TryBeginInjection_BlendShapeCountZero_ReturnsFalseWithoutTouchingRegistry()
        {
            var registry = new InputSourceRegistry();
            var live = new FakeValueProvider("input:values", 3);
            registry.Register(AdapterSlug.Parse("input"), "values", live);
            var injector = new RecValueProviderInjector(registry, () => 0);

            Assert.That(injector.TryBeginInjection(RecBaselineState.Empty), Is.False);
            Assert.That(registry.TryResolve("input:values", out IInputSource resolved), Is.True);
            Assert.That(resolved, Is.SameAs(live));
        }

        [Test]
        public void TryBeginInjection_BaselineAndRegisteredSources_SeedsAndReplacesValueProviders()
        {
            var registry = new InputSourceRegistry();
            var live = new FakeValueProvider("input:values", 4);
            registry.Register(AdapterSlug.Parse("input"), "values", live);
            var injector = new RecValueProviderInjector(registry, () => 4);
            var baseline = new RecBaselineState(
                null,
                null,
                new[] { new RecBaselineState.ValueProviderEntry("input:values", true, new byte[] { 0x05 }, new[] { 1f, 2f }) },
                null);

            Assert.That(injector.TryBeginInjection(baseline), Is.True);
            Assert.That(registry.TryResolve("input:values", out IInputSource resolved), Is.True);
            var playback = (RecPlaybackValueProviderSource)resolved;
            Span<float> output = stackalloc float[4];
            Assert.That(playback.TryWriteValues(output), Is.True);
            Assert.That(output.ToArray(), Is.EqualTo(new[] { 1f, 0f, 2f, 0f }));

            injector.EndInjection();
            Assert.That(registry.TryResolve("input:values", out resolved), Is.True);
            Assert.That(resolved, Is.SameAs(live));
        }

        [Test]
        public void TryBeginInjection_LiveSourceIdDiffersFromRegistryKey_PlaybackSourceTakesLiveId()
        {
            // OscInputSource は Id "osc" のまま binding slug（例 "ifm"）で登録される。レイヤー側は source.Id で
            // スロットを同定するため、注入体は registry キーではなく原本の Id を名乗らないと原本がレイヤーに残る。
            var registry = new InputSourceRegistry();
            var live = new FakeValueProvider("osc", 2);
            registry.Register(AdapterSlug.Parse("ifm"), live);
            var injector = new RecValueProviderInjector(registry, () => 2);

            Assert.That(injector.TryBeginInjection(RecBaselineState.Empty), Is.True);
            Assert.That(registry.TryResolve("ifm", out IInputSource resolved), Is.True);
            Assert.That(resolved, Is.InstanceOf<RecPlaybackValueProviderSource>());
            Assert.That(resolved.Id, Is.EqualTo("osc"));
            Assert.That(((RecPlaybackValueProviderSource)resolved).ReplacedSource, Is.SameAs(live));

            injector.EndInjection();
            Assert.That(registry.TryResolve("ifm", out resolved), Is.True);
            Assert.That(resolved, Is.SameAs(live));
        }

        [Test]
        public void TryBeginInjection_BaselineSourceWithoutLiveEntry_PlaybackSourceTakesRegistryKey()
        {
            var registry = new InputSourceRegistry();
            var injector = new RecValueProviderInjector(registry, () => 1);
            var baseline = new RecBaselineState(
                null, null,
                new[] { new RecBaselineState.ValueProviderEntry("input:orphan", true, new byte[] { 0x01 }, new[] { 1f }) },
                null);

            Assert.That(injector.TryBeginInjection(baseline), Is.True);
            Assert.That(registry.TryResolve("input:orphan", out IInputSource resolved), Is.True);
            Assert.That(resolved.Id, Is.EqualTo("input:orphan"));

            injector.EndInjection();
            Assert.That(registry.TryResolve("input:orphan", out _), Is.False);
        }

        private sealed class FakeValueProvider : ValueProviderInputSourceBase
        {
            public FakeValueProvider(string id, int count) : base(InputSourceId.Parse(id), count) { }

            public override bool TryWriteValues(Span<float> output) => false;
        }
    }
}
