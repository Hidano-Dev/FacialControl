using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Tests.Shared;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Hidano.FacialControl.Tests.EditMode.Adapters.InputSources
{
    [TestFixture]
    public class InputSourceRegistryTests
    {
        private static readonly Regex DuplicateLogPattern =
            new Regex("InputSourceRegistry.*duplicate", RegexOptions.IgnoreCase);

        private static readonly Regex ReplaceLogPattern =
            new Regex("InputSourceRegistry.*replaced", RegexOptions.IgnoreCase);

        private static readonly Regex ReentrantMutationLogPattern =
            new Regex("InputSourceRegistry.*mutation", RegexOptions.IgnoreCase);

        private class StubInputSource : IInputSource
        {
            public StubInputSource(string id)
            {
                Id = id;
                Type = InputSourceType.ValueProvider;
                BlendShapeCount = 0;
                ContributeMask = ContributeMaskTestHelper.AllSetContributeMask(BlendShapeCount);
            }

            public string Id { get; }
            public InputSourceType Type { get; }
            public int BlendShapeCount { get; }
            public BitArray ContributeMask { get; }

            public void Tick(float deltaTime) { }
            public bool TryWriteValues(Span<float> output) => false;
        }

        private sealed class StubInjectedInputSource : StubInputSource, IInjectedInputSource
        {
            public StubInjectedInputSource(string id, IInputSource replacedSource)
                : base(id)
            {
                ReplacedSource = replacedSource;
            }

            public IInputSource ReplacedSource { get; }
        }

        [Test]
        public void Register_PrimarySlug_TryResolveBySlugReturnsRegisteredSource()
        {
            var registry = new InputSourceRegistry();
            var source = new StubInputSource("osc-primary");
            var slug = AdapterSlug.Parse("osc");

            registry.Register(slug, source);

            Assert.IsTrue(registry.TryResolve("osc", out var resolved));
            Assert.AreSame(source, resolved);
        }

        [Test]
        public void Register_PrimarySlug_TryResolveByCompositeFormReturnsFalse()
        {
            var registry = new InputSourceRegistry();
            registry.Register(AdapterSlug.Parse("osc"), new StubInputSource("osc"));

            Assert.IsFalse(registry.TryResolve("osc:vrchat", out var resolved));
            Assert.IsNull(resolved);
        }

        [Test]
        public void Register_CompositeSlug_TryResolveByCompositeIdReturnsRegisteredSource()
        {
            var registry = new InputSourceRegistry();
            var source = new StubInputSource("osc-vrchat");
            var slug = AdapterSlug.Parse("osc");

            registry.Register(slug, "vrchat", source);

            Assert.IsTrue(registry.TryResolve("osc:vrchat", out var resolved));
            Assert.AreSame(source, resolved);
        }

        [Test]
        public void Register_CompositeSlug_TryResolvePrimaryFormReturnsFalse()
        {
            var registry = new InputSourceRegistry();
            registry.Register(AdapterSlug.Parse("osc"), "vrchat", new StubInputSource("osc:vrchat"));

            Assert.IsFalse(registry.TryResolve("osc", out var resolved));
            Assert.IsNull(resolved);
        }

        [Test]
        public void Register_PrimaryAndCompositeForSameSlug_BothCoexist()
        {
            var registry = new InputSourceRegistry();
            var primary = new StubInputSource("osc");
            var composite = new StubInputSource("osc:vmc");
            var slug = AdapterSlug.Parse("osc");

            registry.Register(slug, primary);
            registry.Register(slug, "vmc", composite);

            Assert.IsTrue(registry.TryResolve("osc", out var primaryResolved));
            Assert.AreSame(primary, primaryResolved);
            Assert.IsTrue(registry.TryResolve("osc:vmc", out var compositeResolved));
            Assert.AreSame(composite, compositeResolved);
        }

        [Test]
        public void Register_DuplicatePrimarySlug_LogsErrorAndOverwrites()
        {
            LogAssert.Expect(LogType.Error, DuplicateLogPattern);
            var registry = new InputSourceRegistry();
            var slug = AdapterSlug.Parse("osc");
            var first = new StubInputSource("first");
            var second = new StubInputSource("second");

            registry.Register(slug, first);
            registry.Register(slug, second);

            Assert.IsTrue(registry.TryResolve("osc", out var resolved));
            Assert.AreSame(second, resolved);
        }

        [Test]
        public void Register_DuplicateCompositeSlug_LogsErrorAndOverwrites()
        {
            LogAssert.Expect(LogType.Error, DuplicateLogPattern);
            var registry = new InputSourceRegistry();
            var slug = AdapterSlug.Parse("osc");
            var first = new StubInputSource("first");
            var second = new StubInputSource("second");

            registry.Register(slug, "vrchat", first);
            registry.Register(slug, "vrchat", second);

            Assert.IsTrue(registry.TryResolve("osc:vrchat", out var resolved));
            Assert.AreSame(second, resolved);
        }

        [Test]
        public void Replace_ExistingPrimarySlug_LogsAndPreservesRegisteredIdOrder()
        {
            LogAssert.Expect(LogType.Log, ReplaceLogPattern);
            var registry = new InputSourceRegistry();
            var first = new StubInputSource("first");
            var second = new StubInputSource("second");
            var slug = AdapterSlug.Parse("osc");
            registry.Register(slug, first);
            registry.Register(AdapterSlug.Parse("input-system"), new StubInputSource("other"));

            registry.Replace(slug, second);

            Assert.IsTrue(registry.TryResolve("osc", out var resolved));
            Assert.AreSame(second, resolved);
            CollectionAssert.AreEqual(new[] { "osc", "input-system" }, registry.RegisteredIds);
        }

        [Test]
        public void Replace_UnregisteredPrimarySlug_LogsAndRegistersNewSource()
        {
            LogAssert.Expect(LogType.Log, ReplaceLogPattern);
            var registry = new InputSourceRegistry();
            var source = new StubInputSource("new-primary");
            var slug = AdapterSlug.Parse("osc");

            registry.Replace(slug, source);

            Assert.IsTrue(registry.TryResolve("osc", out var resolved));
            Assert.AreSame(source, resolved);
            CollectionAssert.AreEqual(new[] { "osc" }, registry.RegisteredIds);
        }

        [Test]
        public void Replace_ExistingCompositeSlug_LogsAndPreservesRegisteredIdOrder()
        {
            LogAssert.Expect(LogType.Log, ReplaceLogPattern);
            var registry = new InputSourceRegistry();
            var first = new StubInputSource("first");
            var second = new StubInputSource("second");
            var slug = AdapterSlug.Parse("osc");
            registry.Register(slug, "vrchat", first);
            registry.Register(AdapterSlug.Parse("input-system"), new StubInputSource("other"));

            registry.Replace(slug, "vrchat", second);

            Assert.IsTrue(registry.TryResolve("osc:vrchat", out var resolved));
            Assert.AreSame(second, resolved);
            CollectionAssert.AreEqual(new[] { "osc:vrchat", "input-system" }, registry.RegisteredIds);
        }

        [Test]
        public void Replace_UnregisteredCompositeSlug_LogsAndRegistersNewSource()
        {
            LogAssert.Expect(LogType.Log, ReplaceLogPattern);
            var registry = new InputSourceRegistry();
            var source = new StubInputSource("new-composite");
            var slug = AdapterSlug.Parse("osc");

            registry.Replace(slug, "vrchat", source);

            Assert.IsTrue(registry.TryResolve("osc:vrchat", out var resolved));
            Assert.AreSame(source, resolved);
            CollectionAssert.AreEqual(new[] { "osc:vrchat" }, registry.RegisteredIds);
        }

        [Test]
        public void TryResolve_UnregisteredPrimaryId_ReturnsFalseAndNull()
        {
            var registry = new InputSourceRegistry();

            Assert.IsFalse(registry.TryResolve("missing", out var resolved));
            Assert.IsNull(resolved);
        }

        [Test]
        public void TryResolve_UnregisteredCompositeId_ReturnsFalseAndNull()
        {
            var registry = new InputSourceRegistry();
            registry.Register(AdapterSlug.Parse("osc"), new StubInputSource("primary"));

            Assert.IsFalse(registry.TryResolve("osc:unknown-sub", out var resolved));
            Assert.IsNull(resolved);
        }

        [Test]
        public void TryResolve_NullOrEmptyId_ReturnsFalseAndNull()
        {
            var registry = new InputSourceRegistry();
            registry.Register(AdapterSlug.Parse("osc"), new StubInputSource("primary"));

            Assert.IsFalse(registry.TryResolve(null, out var nullResolved));
            Assert.IsNull(nullResolved);
            Assert.IsFalse(registry.TryResolve(string.Empty, out var emptyResolved));
            Assert.IsNull(emptyResolved);
        }

        [Test]
        public void Unregister_PrimarySlug_RemovesRegisteredSource()
        {
            var registry = new InputSourceRegistry();
            var slug = AdapterSlug.Parse("osc");
            registry.Register(slug, new StubInputSource("primary"));

            registry.Unregister(slug);

            Assert.IsFalse(registry.TryResolve("osc", out var resolved));
            Assert.IsNull(resolved);
            CollectionAssert.DoesNotContain(registry.RegisteredIds, "osc");
        }

        [Test]
        public void Unregister_PrimarySlug_NotifiesSubscribersWithNull()
        {
            var registry = new InputSourceRegistry();
            var slug = AdapterSlug.Parse("osc");
            var notifications = new List<IInputSource>();
            registry.Register(slug, new StubInputSource("primary"));
            registry.Subscribe("osc", notifications.Add);

            registry.Unregister(slug);

            Assert.AreEqual(1, notifications.Count);
            Assert.IsNull(notifications[0]);
        }

        [Test]
        public void Unregister_UnregisteredSlug_DoesNotThrow()
        {
            var registry = new InputSourceRegistry();

            Assert.DoesNotThrow(() => registry.Unregister(AdapterSlug.Parse("missing")));
            Assert.AreEqual(0, registry.RegisteredIds.Count);
        }

        [Test]
        public void RegisteredIds_EmptyRegistry_IsEmpty()
        {
            var registry = new InputSourceRegistry();

            Assert.IsNotNull(registry.RegisteredIds);
            Assert.AreEqual(0, registry.RegisteredIds.Count);
        }

        [Test]
        public void RegisteredIds_AfterMixedRegistration_ContainsAllKeys()
        {
            var registry = new InputSourceRegistry();
            registry.Register(AdapterSlug.Parse("osc"), new StubInputSource("a"));
            registry.Register(AdapterSlug.Parse("osc"), "vrchat", new StubInputSource("b"));
            registry.Register(AdapterSlug.Parse("input-system"), new StubInputSource("c"));

            var ids = registry.RegisteredIds;
            Assert.AreEqual(3, ids.Count);
            CollectionAssert.Contains(ids, "osc");
            CollectionAssert.Contains(ids, "osc:vrchat");
            CollectionAssert.Contains(ids, "input-system");
        }

        [Test]
        public void RegisteredIds_EnumerationIsStableAcrossMultipleSnapshots()
        {
            var registry = new InputSourceRegistry();
            registry.Register(AdapterSlug.Parse("osc"), new StubInputSource("a"));
            registry.Register(AdapterSlug.Parse("input-system"), new StubInputSource("b"));
            registry.Register(AdapterSlug.Parse("osc"), "vrchat", new StubInputSource("c"));

            var first = new List<string>(registry.RegisteredIds);
            var second = new List<string>(registry.RegisteredIds);

            CollectionAssert.AreEqual(first, second);
        }

        [Test]
        public void RegisteredIds_AfterDuplicateRegister_DoesNotDuplicate()
        {
            LogAssert.Expect(LogType.Error, DuplicateLogPattern);
            var registry = new InputSourceRegistry();
            var slug = AdapterSlug.Parse("osc");
            registry.Register(slug, new StubInputSource("first"));
            registry.Register(slug, new StubInputSource("second"));

            Assert.AreEqual(1, registry.RegisteredIds.Count);
        }

        [Test]
        public void Replace_HandlerAttemptsRegister_LogsErrorAndIgnoresMutation()
        {
            LogAssert.Expect(LogType.Log, ReplaceLogPattern);
            LogAssert.Expect(LogType.Error, ReentrantMutationLogPattern);
            var registry = new InputSourceRegistry();
            var slug = AdapterSlug.Parse("osc");
            registry.Register(slug, new StubInputSource("first"));
            registry.Subscribe("osc", _ => registry.Register(AdapterSlug.Parse("blocked"), new StubInputSource("blocked")));

            registry.Replace(slug, new StubInputSource("second"));

            Assert.IsFalse(registry.TryResolve("blocked", out var blocked));
            Assert.IsNull(blocked);
            CollectionAssert.DoesNotContain(registry.RegisteredIds, "blocked");
        }

        [Test]
        public void Replace_HandlerAttemptsSubscribe_LogsErrorAndIgnoresMutation()
        {
            LogAssert.Expect(LogType.Log, ReplaceLogPattern);
            LogAssert.Expect(LogType.Error, ReentrantMutationLogPattern);
            var registry = new InputSourceRegistry();
            var slug = AdapterSlug.Parse("osc");
            registry.Register(slug, new StubInputSource("first"));
            int lateSubscriberCalls = 0;
            registry.Subscribe("osc", _ => registry.Subscribe("osc", __ => lateSubscriberCalls++));

            registry.Replace(slug, new StubInputSource("second"));
            LogAssert.Expect(LogType.Log, ReplaceLogPattern);
            LogAssert.Expect(LogType.Error, ReentrantMutationLogPattern);
            registry.Replace(slug, new StubInputSource("third"));

            Assert.AreEqual(0, lateSubscriberCalls);
        }

        [Test]
        public void Register_NullPrimarySource_Throws()
        {
            var registry = new InputSourceRegistry();

            Assert.Throws<ArgumentNullException>(
                () => registry.Register(AdapterSlug.Parse("osc"), source: null));
        }

        [Test]
        public void Register_NullCompositeSource_Throws()
        {
            var registry = new InputSourceRegistry();

            Assert.Throws<ArgumentNullException>(
                () => registry.Register(AdapterSlug.Parse("osc"), "vrchat", source: null));
        }

        [Test]
        public void Replace_NullPrimarySource_Throws()
        {
            var registry = new InputSourceRegistry();

            Assert.Throws<ArgumentNullException>(
                () => registry.Replace(AdapterSlug.Parse("osc"), source: null));
        }

        [Test]
        public void Replace_NullCompositeSource_Throws()
        {
            var registry = new InputSourceRegistry();

            Assert.Throws<ArgumentNullException>(
                () => registry.Replace(AdapterSlug.Parse("osc"), "vrchat", source: null));
        }

        [Test]
        public void Register_NullOrEmptySub_Throws()
        {
            var registry = new InputSourceRegistry();

            Assert.Throws<ArgumentException>(
                () => registry.Register(AdapterSlug.Parse("osc"), sub: null, source: new StubInputSource("x")));
            Assert.Throws<ArgumentException>(
                () => registry.Register(AdapterSlug.Parse("osc"), sub: string.Empty, source: new StubInputSource("x")));
        }

        [Test]
        public void Replace_NullOrEmptySub_Throws()
        {
            var registry = new InputSourceRegistry();

            Assert.Throws<ArgumentException>(
                () => registry.Replace(AdapterSlug.Parse("osc"), sub: null, source: new StubInputSource("x")));
            Assert.Throws<ArgumentException>(
                () => registry.Replace(AdapterSlug.Parse("osc"), sub: string.Empty, source: new StubInputSource("x")));
        }

        [Test]
        public void IInputSourceRegistry_InterfaceContract_IsHonoredByImplementation()
        {
            IInputSourceRegistry registry = new InputSourceRegistry();
            var slug = AdapterSlug.Parse("osc");
            var primary = new StubInputSource("primary");
            var composite = new StubInputSource("composite");
            var replacedPrimary = new StubInputSource("replaced-primary");
            var replacedComposite = new StubInputSource("replaced-composite");

            registry.Register(slug, primary);
            registry.Register(slug, "vrchat", composite);
            LogAssert.Expect(LogType.Log, ReplaceLogPattern);
            LogAssert.Expect(LogType.Log, ReplaceLogPattern);
            registry.Replace(slug, replacedPrimary);
            registry.Replace(slug, "vrchat", replacedComposite);

            Assert.IsTrue(registry.TryResolve("osc", out var primaryResolved));
            Assert.AreSame(replacedPrimary, primaryResolved);
            Assert.IsTrue(registry.TryResolve("osc:vrchat", out var compositeResolved));
            Assert.AreSame(replacedComposite, compositeResolved);
            Assert.AreEqual(2, registry.RegisteredIds.Count);

            registry.Unregister(slug);

            Assert.IsFalse(registry.TryResolve("osc", out var removed));
            Assert.IsNull(removed);
            Assert.IsTrue(registry.TryResolve("osc:vrchat", out compositeResolved));
            Assert.AreSame(replacedComposite, compositeResolved);
            Assert.AreEqual(1, registry.RegisteredIds.Count);
        }

        [Test]
        public void InjectedInputSource_MarkerContract_PreservesReplacedSourceReference()
        {
            var original = new StubInputSource("original");
            var injected = new StubInjectedInputSource("injected", original);

            Assert.That(injected, Is.InstanceOf<IInjectedInputSource>());
            Assert.That(injected.ReplacedSource, Is.SameAs(original));
        }

        [Test]
        public void InjectedInputSource_MarkerContract_AllowsNullForRegisterWithoutOriginal()
        {
            var injected = new StubInjectedInputSource("injected", replacedSource: null);

            Assert.That(injected.ReplacedSource, Is.Null);
        }
    }
}
