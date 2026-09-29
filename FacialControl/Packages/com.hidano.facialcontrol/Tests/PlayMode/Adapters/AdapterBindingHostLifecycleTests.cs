using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Hidano.FacialControl.Adapters.DependencyInjection;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;
using Hidano.FacialControl.Tests.Shared;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VContainer;
using VContainer.Unity;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.PlayMode.Adapters
{
    /// <summary>
    /// task 7.1: <see cref="AdapterBindingHost"/> が VContainer の PlayerLoop に entry point として
    /// 挿入され、複数 binding の lifecycle dispatch と例外 isolation を PlayMode で満たすことを検証する。
    /// </summary>
    [TestFixture]
    [MediumTest]
    public class AdapterBindingHostLifecycleTests : SizedTestFixture
    {
        private static readonly Regex AdapterBindingHostErrorPattern =
            new Regex("AdapterBindingHost", RegexOptions.IgnoreCase);

        private TestLifetimeScope _scope;
        private GameObject _scopeGameObject;
        private GameObject _hostGameObject;

        [TearDown]
        public void TearDown()
        {
            DisposeScope();
            DestroyHostGameObject();
        }

        [UnityTest]
        public IEnumerator LifetimeScope_ThreeBindings_DispatchesLifecycleFromVContainerPlayerLoop()
        {
            var recorder = new LifecycleRecorder();
            var bindings = new[]
            {
                new TrackingAdapterBinding("A", recorder),
                new TrackingAdapterBinding("B", recorder),
                new TrackingAdapterBinding("C", recorder)
            };

            BuildScope(bindings);

            AssertPhaseOrder(recorder.Events, "Start", "A", "B", "C");
            Assert.That(bindings[0].OnStartCount, Is.EqualTo(1));
            Assert.That(bindings[1].OnStartCount, Is.EqualTo(1));
            Assert.That(bindings[2].OnStartCount, Is.EqualTo(1));

            yield return new WaitForFixedUpdate();
            yield return null;

            foreach (TrackingAdapterBinding binding in bindings)
            {
                Assert.That(binding.OnStartCount, Is.EqualTo(1),
                    "IInitializable / IStartable の二重 dispatch 抑止により OnStart は 1 回だけ呼ばれること。");
                Assert.That(binding.OnTickCount, Is.GreaterThanOrEqualTo(1));
                Assert.That(binding.OnLateTickCount, Is.GreaterThanOrEqualTo(1));
                Assert.That(binding.OnFixedTickCount, Is.GreaterThanOrEqualTo(1));
            }

            AssertPhaseOrder(recorder.Events, "FixedTick", "A", "B", "C");
            AssertPhaseOrder(recorder.Events, "Tick", "A", "B", "C");
            AssertPhaseOrder(recorder.Events, "LateTick", "A", "B", "C");
            Assert.That(
                FirstIndex(recorder.Events, "A:Start"),
                Is.LessThan(FirstIndex(recorder.Events, "A:FixedTick")));
            Assert.That(
                FirstIndex(recorder.Events, "A:Start"),
                Is.LessThan(FirstIndex(recorder.Events, "A:Tick")));
            Assert.That(
                FirstIndex(recorder.Events, "A:Start"),
                Is.LessThan(FirstIndex(recorder.Events, "A:LateTick")));
        }

        [UnityTest]
        public IEnumerator Tick_BindingThrows_SkipsFailedBindingAndContinuesOtherBindings()
        {
            LogAssert.Expect(LogType.Error, AdapterBindingHostErrorPattern);

            var recorder = new LifecycleRecorder();
            var throwing = new TrackingAdapterBinding("Throwing", recorder)
            {
                ThrowOnTick = true
            };
            var healthyA = new TrackingAdapterBinding("HealthyA", recorder);
            var healthyB = new TrackingAdapterBinding("HealthyB", recorder);

            BuildScope(throwing, healthyA, healthyB);

            yield return WaitUntilOrTimeout(() => throwing.OnTickCount == 1);
            int throwingLateTickCountAfterSkip = throwing.OnLateTickCount;

            yield return null;

            Assert.That(throwing.OnTickCount, Is.EqualTo(1),
                "例外発生後の binding は _skipped=true に遷移し、以後の Tick は no-op になること。");
            Assert.That(throwing.OnLateTickCount, Is.EqualTo(throwingLateTickCountAfterSkip),
                "Tick 例外で skip が確定した後は LateTick も追加 dispatch されないこと。");
            Assert.That(healthyA.OnTickCount, Is.GreaterThanOrEqualTo(2));
            Assert.That(healthyB.OnTickCount, Is.GreaterThanOrEqualTo(2));
            Assert.That(healthyA.OnLateTickCount, Is.GreaterThanOrEqualTo(1));
            Assert.That(healthyB.OnLateTickCount, Is.GreaterThanOrEqualTo(1));
        }

        private void BuildScope(params AdapterBindingBase[] bindings)
        {
            _hostGameObject = new GameObject("AdapterBindingHostLifecycleTestsHost");
            AdapterBuildContext ctx = new AdapterBuildContext(
                new FacialProfile("2.0"),
                new[] { "Blink", "Smile" },
                new InputSourceRegistry(),
                new FacialOutputBus(),
                new ManualTimeProvider(),
                _hostGameObject,
                lipSyncProvider: null);

            _scopeGameObject = new GameObject("AdapterBindingHostLifecycleTestsScope");
            _scopeGameObject.SetActive(false);
            _scope = _scopeGameObject.AddComponent<TestLifetimeScope>();
            _scope.autoRun = false;
            _scope.ConfigureAction = builder =>
            {
                foreach (AdapterBindingBase binding in bindings)
                {
                    AdapterBindingBase captured = binding;
                    builder.RegisterEntryPoint<AdapterBindingHost>(
                        _ => new AdapterBindingHost(captured, ctx),
                        Lifetime.Scoped);
                }
            };
            _scopeGameObject.SetActive(true);
            _scope.Build();
        }

        private void DisposeScope()
        {
            if (_scope == null)
            {
                return;
            }

            GameObject go = _scope.gameObject;
            _scope.DisposeCore();
            _scope = null;

            if (go != null)
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
            _scopeGameObject = null;
        }

        private void DestroyHostGameObject()
        {
            if (_hostGameObject != null)
            {
                UnityEngine.Object.DestroyImmediate(_hostGameObject);
                _hostGameObject = null;
            }
        }

        private static void AssertPhaseOrder(
            IReadOnlyList<string> events,
            string phase,
            params string[] bindingNames)
        {
            int previous = -1;
            for (int i = 0; i < bindingNames.Length; i++)
            {
                int current = FirstIndex(events, bindingNames[i] + ":" + phase);
                Assert.That(current, Is.GreaterThan(previous),
                    $"{phase} は binding 登録順に dispatch されること。");
                previous = current;
            }
        }

        private static int FirstIndex(IReadOnlyList<string> events, string value)
        {
            for (int i = 0; i < events.Count; i++)
            {
                if (events[i] == value)
                {
                    return i;
                }
            }

            Assert.Fail($"Expected lifecycle event '{value}' was not recorded.");
            return -1;
        }

        private static IEnumerator WaitUntilOrTimeout(Func<bool> predicate, int maxFrames = 10)
        {
            for (int i = 0; i < maxFrames; i++)
            {
                if (predicate())
                {
                    yield break;
                }

                yield return null;
            }

            Assert.Fail("Expected PlayerLoop dispatch did not occur within the timeout.");
        }

        private sealed class TestLifetimeScope : LifetimeScope
        {
            public Action<IContainerBuilder> ConfigureAction;

            protected override void Configure(IContainerBuilder builder)
            {
                ConfigureAction?.Invoke(builder);
            }
        }

        private sealed class LifecycleRecorder
        {
            public readonly List<string> Events = new List<string>();

            public void Add(string bindingName, string phase)
            {
                Events.Add(bindingName + ":" + phase);
            }
        }

        [Serializable]
        private sealed class TrackingAdapterBinding : AdapterBindingBase
        {
            private readonly string _name;
            private readonly LifecycleRecorder _recorder;

            public int OnStartCount;
            public int OnTickCount;
            public int OnLateTickCount;
            public int OnFixedTickCount;
            public bool ThrowOnTick;

            public TrackingAdapterBinding(string name, LifecycleRecorder recorder)
            {
                _name = name;
                _recorder = recorder;
            }

            public override void OnStart(in AdapterBuildContext ctx)
            {
                OnStartCount++;
                _recorder.Add(_name, "Start");
            }

            public override void OnTick(float deltaTime)
            {
                OnTickCount++;
                _recorder.Add(_name, "Tick");
                if (ThrowOnTick)
                {
                    throw new InvalidOperationException("TrackingAdapterBinding.OnTick");
                }
            }

            public override void OnLateTick(float deltaTime)
            {
                OnLateTickCount++;
                _recorder.Add(_name, "LateTick");
            }

            public override void OnFixedTick(float fixedDeltaTime)
            {
                OnFixedTickCount++;
                _recorder.Add(_name, "FixedTick");
            }
        }
    }
}
