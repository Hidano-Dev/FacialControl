using System;
using System.Collections.Generic;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Tests.Shared;
using NUnit.Framework;
using UnityEngine;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.EditMode.Adapters
{
    /// <summary>
    /// task 3.1 の観測可能完了条件: <see cref="AdapterBuildContext"/> が
    /// <c>in</c> パラメータで関数に渡せ、boxing なしで field アクセスできることを検証する。
    /// 加えて、null 必須依存への <see cref="ArgumentNullException"/> もここで assert する。
    /// </summary>
    [TestFixture]
    [SmallTest]
    public class AdapterBuildContextTests : SizedTestFixture
    {
        private sealed class StubInputSourceRegistry : IInputSourceRegistry
        {
            // task 3.4 / 3.5 で IInputSourceRegistry に追加された API は本テスト fixture では未使用。
            // 呼ばれた場合に検出できるよう NotImplementedException を投げる。
            public IReadOnlyList<string> RegisteredIds =>
                throw new NotImplementedException();

            public void Register(AdapterSlug slug, IInputSource source) =>
                throw new NotImplementedException();

            public void Replace(AdapterSlug slug, IInputSource source) =>
                throw new NotImplementedException();

            public void Register(AdapterSlug slug, string sub, IInputSource source) =>
                throw new NotImplementedException();

            public void Replace(AdapterSlug slug, string sub, IInputSource source) =>
                throw new NotImplementedException();

            public void Unregister(AdapterSlug slug) =>
                throw new NotImplementedException();

            public void Unregister(AdapterSlug slug, string sub) =>
                throw new NotImplementedException();

            public bool TryResolve(string layerInputSourceId, out IInputSource source) =>
                throw new NotImplementedException();

            public void Subscribe(string id, System.Action<IInputSource> handler) =>
                throw new NotImplementedException();
        }

        private sealed class StubFacialOutputBus : IFacialOutputBus
        {
            public bool HasObservers => false;

            public void Subscribe(IFacialOutputObserver observer) =>
                throw new NotImplementedException();

            public void Unsubscribe(IFacialOutputObserver observer) =>
                throw new NotImplementedException();

            public void Publish(
                ReadOnlySpan<float> postBlendValues,
                ReadOnlySpan<GazeSnapshot> gazeSnapshots) =>
                throw new NotImplementedException();
        }

        private static AdapterBuildContext CreateValidContext(
            out FacialProfile profile,
            out IReadOnlyList<string> blendShapeNames,
            out IInputSourceRegistry registry,
            out IFacialOutputBus facialOutputBus,
            out ITimeProvider timeProvider,
            out GameObject host)
        {
            profile = new FacialProfile("1.0");
            blendShapeNames = new List<string> { "A", "B" };
            registry = new StubInputSourceRegistry();
            facialOutputBus = new StubFacialOutputBus();
            timeProvider = new ManualTimeProvider();
            host = new GameObject("AdapterBuildContextHost");

            return new AdapterBuildContext(
                profile,
                blendShapeNames,
                registry,
                facialOutputBus,
                timeProvider,
                host,
                lipSyncProvider: null);
        }

        private static int CountFieldRead(in AdapterBuildContext ctx)
        {
            // in 引数で受け取った struct から各 field を読み出す。
            // field 直接アクセスかつ readonly struct のため boxing は発生しない。
            int count = 0;
            if (ctx.BlendShapeNames != null) count++;
            if (ctx.InputSourceRegistry != null) count++;
            if (ctx.FacialOutputBus != null) count++;
            if (ctx.AdapterBindings != null) count++;
            if (ctx.TimeProvider != null) count++;
            if (ctx.HostGameObject != null) count++;
            // LipSyncProvider は null 許容
            if (ctx.LipSyncProvider == null) count++;
            // FacialProfile (struct) の field 読み取りも boxing なし
            if (ctx.Profile.SchemaVersion != null) count++;
            return count;
        }

        [Test]
        public void Constructor_ValidArgs_AssignsAllFields()
        {
            var ctx = CreateValidContext(
                out var profile,
                out var blendShapeNames,
                out var registry,
                out var facialOutputBus,
                out var timeProvider,
                out var host);

            try
            {
                Assert.That(ctx.Profile.SchemaVersion, Is.EqualTo(profile.SchemaVersion));
                Assert.That(ctx.BlendShapeNames, Is.SameAs(blendShapeNames));
                Assert.That(ctx.InputSourceRegistry, Is.SameAs(registry));
                Assert.That(ctx.FacialOutputBus, Is.SameAs(facialOutputBus));
                Assert.That(ctx.AdapterBindings, Is.Empty);
                Assert.That(ctx.TimeProvider, Is.SameAs(timeProvider));
                Assert.That(ctx.HostGameObject, Is.SameAs(host));
                Assert.That(ctx.LipSyncProvider, Is.Null);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void Constructor_NullBlendShapeNames_Throws()
        {
            var profile = new FacialProfile("1.0");
            var registry = new StubInputSourceRegistry();
            ITimeProvider timeProvider = new ManualTimeProvider();
            var host = new GameObject("AdapterBuildContextHost_NullBlendShapeNames");

            try
            {
                Assert.Throws<ArgumentNullException>(
                    () => new AdapterBuildContext(
                        profile,
                        blendShapeNames: null,
                        inputSourceRegistry: registry,
                        facialOutputBus: new StubFacialOutputBus(),
                        timeProvider: timeProvider,
                        hostGameObject: host,
                        lipSyncProvider: null));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void Constructor_NullInputSourceRegistry_Throws()
        {
            var profile = new FacialProfile("1.0");
            IReadOnlyList<string> blendShapeNames = new List<string>();
            ITimeProvider timeProvider = new ManualTimeProvider();
            var host = new GameObject("AdapterBuildContextHost_NullRegistry");

            try
            {
                Assert.Throws<ArgumentNullException>(
                    () => new AdapterBuildContext(
                        profile,
                        blendShapeNames,
                        inputSourceRegistry: null,
                        facialOutputBus: new StubFacialOutputBus(),
                        timeProvider: timeProvider,
                        hostGameObject: host,
                        lipSyncProvider: null));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void Constructor_NullFacialOutputBus_Throws()
        {
            var profile = new FacialProfile("1.0");
            IReadOnlyList<string> blendShapeNames = new List<string>();
            var registry = new StubInputSourceRegistry();
            ITimeProvider timeProvider = new ManualTimeProvider();
            var host = new GameObject("AdapterBuildContextHost_NullFacialOutputBus");

            try
            {
                Assert.Throws<ArgumentNullException>(
                    () => new AdapterBuildContext(
                        profile,
                        blendShapeNames,
                        inputSourceRegistry: registry,
                        facialOutputBus: null,
                        timeProvider: timeProvider,
                        hostGameObject: host,
                        lipSyncProvider: null));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void Constructor_NullTimeProvider_Throws()
        {
            var profile = new FacialProfile("1.0");
            IReadOnlyList<string> blendShapeNames = new List<string>();
            var registry = new StubInputSourceRegistry();
            var host = new GameObject("AdapterBuildContextHost_NullTime");

            try
            {
                Assert.Throws<ArgumentNullException>(
                    () => new AdapterBuildContext(
                        profile,
                        blendShapeNames,
                        inputSourceRegistry: registry,
                        facialOutputBus: new StubFacialOutputBus(),
                        timeProvider: null,
                        hostGameObject: host,
                        lipSyncProvider: null));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void Constructor_NullHostGameObject_Throws()
        {
            var profile = new FacialProfile("1.0");
            IReadOnlyList<string> blendShapeNames = new List<string>();
            var registry = new StubInputSourceRegistry();
            ITimeProvider timeProvider = new ManualTimeProvider();

            Assert.Throws<ArgumentNullException>(
                () => new AdapterBuildContext(
                    profile,
                    blendShapeNames,
                    inputSourceRegistry: registry,
                    facialOutputBus: new StubFacialOutputBus(),
                    timeProvider: timeProvider,
                    hostGameObject: null,
                    lipSyncProvider: null));
        }

        [Test]
        public void Constructor_NullLipSyncProvider_IsAllowed()
        {
            var ctx = CreateValidContext(
                out _,
                out _,
                out _,
                out _,
                out _,
                out var host);

            try
            {
                Assert.That(ctx.LipSyncProvider, Is.Null);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void InParameter_FieldsAreAccessibleWithoutBoxing()
        {
            var ctx = CreateValidContext(
                out _,
                out _,
                out _,
                out _,
                out _,
                out var host);

            try
            {
                // in 引数経由で全 field を読み取れることを確認。
                // readonly struct + 直接 field アクセスのため boxing は発生しない（コンパイル時に確認）。
                int readCount = CountFieldRead(in ctx);
                Assert.That(readCount, Is.EqualTo(8));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void Default_Struct_DoesNotThrow_OnFieldRead()
        {
            // default(AdapterBuildContext) は ctor を経由しないため reference 型 field は null。
            // 仕様上 unbuilt context の利用は想定外だが、struct の default 構築自体は許容され、
            // field 読み取りで例外を出さないことを確認しておく（boxing-free 検証の補助）。
            AdapterBuildContext ctx = default;
            Assert.That(ctx.BlendShapeNames, Is.Null);
            Assert.That(ctx.InputSourceRegistry, Is.Null);
            Assert.That(ctx.FacialOutputBus, Is.Null);
            Assert.That(ctx.AdapterBindings, Is.Null);
            Assert.That(ctx.TimeProvider, Is.Null);
            Assert.That(ctx.HostGameObject, Is.Null);
            Assert.That(ctx.LipSyncProvider, Is.Null);
            Assert.That(ctx.Profile.SchemaVersion, Is.Null);
        }
    }
}
