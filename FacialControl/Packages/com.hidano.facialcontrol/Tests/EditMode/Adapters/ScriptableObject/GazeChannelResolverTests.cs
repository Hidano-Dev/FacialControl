using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Adapters.ScriptableObject;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using NUnit.Framework;
using System;
using System.Collections;
using UnityEngine;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.EditMode.Adapters.ScriptableObjectTests
{
    [SmallTest]
    public sealed class GazeChannelResolverTests : SizedTestFixture
    {
        [Test]
        public void TryResolve_AutomaticSidePair_SelectsOrdinalFirstSlug()
        {
            var registry = new InputSourceRegistry();
            var z = new Source("z:gaze.left"); var zr = new Source("z:gaze.right");
            var a = new Source("a:gaze.left"); var ar = new Source("a:gaze.right");
            registry.Register(AdapterSlug.Parse("z"), GazeSourceIdConvention.ComposeSub("gaze", GazeSide.Left), z);
            registry.Register(AdapterSlug.Parse("z"), GazeSourceIdConvention.ComposeSub("gaze", GazeSide.Right), zr);
            registry.Register(AdapterSlug.Parse("a"), GazeSourceIdConvention.ComposeSub("gaze", GazeSide.Left), a);
            registry.Register(AdapterSlug.Parse("a"), GazeSourceIdConvention.ComposeSub("gaze", GazeSide.Right), ar);
            Assert.That(GazeChannelResolver.TryResolve(new GazeChannel { id = "gaze" }, registry, out var result), Is.True);
            Assert.That(result.SelectedSlug, Is.EqualTo("a"));
            Assert.That(result.LeftSource, Is.SameAs(a));
            Assert.That(result.RightSource, Is.SameAs(ar));
        }

        [Test]
        public void TryResolve_LegacyProviderSlugAndDistinctSourceIds_AreIgnored()
        {
            // 目線メニューの設定はどの目線制御にも効かせるため、旧データの provider / source ID 指定は読まない (HID-191)。
            var registry = new InputSourceRegistry();
            var source = new Source("ifacialmocap:gaze");
            registry.Register(AdapterSlug.Parse("ifacialmocap"), "gaze", source);
            var channel = new GazeChannel
            {
                id = "gaze",
                providerSlug = "inputsystem",
                useDistinctLeftRight = true,
                sourceIdLeft = "inputsystem:gaze.left",
                sourceIdRight = "inputsystem:gaze.right",
            };

            Assert.That(GazeChannelResolver.TryResolve(channel, registry, out var result), Is.True);
            Assert.That(result.SelectedSlug, Is.EqualTo("ifacialmocap"));
            Assert.That(result.LeftSource, Is.SameAs(source));
            Assert.That(result.RightSource, Is.SameAs(source));
        }

        [Test]
        public void TryResolve_NoMatchingProvider_ReturnsFalse()
        {
            var registry = new InputSourceRegistry();
            Assert.That(GazeChannelResolver.TryResolve(new GazeChannel { id = "missing", providerSlug = "provider" }, registry, out _), Is.False);
        }

        private sealed class Source : IInputSource, IAnalogInputSource
        {
            public Source(string id) { Id = id; ContributeMask = new BitArray(0); }
            public string Id { get; }
            public InputSourceType Type => InputSourceType.ValueProvider;
            public int BlendShapeCount => 0;
            public BitArray ContributeMask { get; }
            public bool IsValid => true;
            public int AxisCount => 2;
            public void Tick(float deltaTime) { }
            public bool TryWriteValues(Span<float> output) => false;
            public bool TryReadScalar(out float value) { value = 0; return true; }
            public bool TryReadVector2(out float x, out float y) { x = y = 0; return true; }
            public bool TryReadAxes(Span<float> output) => true;
        }
    }
}
