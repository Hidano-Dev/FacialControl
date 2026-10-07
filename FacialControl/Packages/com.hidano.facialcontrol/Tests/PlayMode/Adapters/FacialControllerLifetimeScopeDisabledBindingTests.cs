using System;
using System.Collections;
using System.Collections.Generic;
using Hidano.FacialControl.Adapters.DependencyInjection;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
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
    /// <see cref="FacialControllerLifetimeScope.Build"/> が無効（<see cref="AdapterBindingBase.Disabled"/>）の binding の
    /// host を作らず、OnStart / Tick / Dispose のいずれも呼ばないこと、有効に戻せば同じ設定のまま起動することを守る。
    /// </summary>
    [TestFixture]
    [MediumTest]
    public class FacialControllerLifetimeScopeDisabledBindingTests : SizedTestFixture
    {
        private TestAppLifetimeScope _appScope;
        private GameObject _appScopeGameObject;
        private readonly List<GameObject> _hostGameObjects = new List<GameObject>();
        private readonly List<FacialControllerLifetimeScope> _scopes = new List<FacialControllerLifetimeScope>();

        [SetUp]
        public void SetUp()
        {
            _appScopeGameObject = new GameObject("FacialControllerLifetimeScopeDisabledBindingTestsAppScope");
            _appScopeGameObject.SetActive(false);
            _appScope = _appScopeGameObject.AddComponent<TestAppLifetimeScope>();
            _appScope.autoRun = false;
            _appScopeGameObject.SetActive(true);
            _appScope.Build();
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _scopes.Count; i++)
            {
                _scopes[i]?.Dispose();
            }
            _scopes.Clear();

            if (_appScope != null)
            {
                _appScope.DisposeCore();
                _appScope = null;
            }

            if (_appScopeGameObject != null)
            {
                UnityEngine.Object.DestroyImmediate(_appScopeGameObject);
                _appScopeGameObject = null;
            }

            for (int i = 0; i < _hostGameObjects.Count; i++)
            {
                if (_hostGameObjects[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(_hostGameObjects[i]);
                }
            }
            _hostGameObjects.Clear();
        }

        [UnityTest]
        public IEnumerator Build_DisabledBinding_NeverStartsTicksOrDisposes()
        {
            var enabled = new CountingAdapterBinding { Slug = "enabled" };
            var disabled = new CountingAdapterBinding { Slug = "disabled", Disabled = true };

            FacialControllerLifetimeScope scope = BuildScope(enabled, disabled);

            Assert.AreEqual(1, enabled.OnStartCount);
            Assert.AreEqual(0, disabled.OnStartCount, "無効の binding は OnStart されない。");

            yield return null;
            yield return null;

            Assert.That(enabled.OnTickCount, Is.GreaterThanOrEqualTo(1));
            Assert.AreEqual(0, disabled.OnTickCount, "無効の binding は Tick されない。");

            scope.Dispose();
            _scopes.Remove(scope);

            Assert.AreEqual(1, enabled.DisposeCount);
            Assert.AreEqual(0, disabled.DisposeCount, "OnStart していない binding は Dispose もされない。");
        }

        [Test]
        public void Build_ReEnabledBinding_StartsWithSameSettings()
        {
            var binding = new CountingAdapterBinding { Slug = "toggle", Setting = 42, Disabled = true };

            FacialControllerLifetimeScope first = BuildScope(binding);
            Assert.AreEqual(0, binding.OnStartCount);
            first.Dispose();
            _scopes.Remove(first);

            binding.Disabled = false;
            BuildScope(binding);

            Assert.AreEqual(1, binding.OnStartCount);
            Assert.AreEqual(42, binding.SettingAtStart, "無効にしていた間も設定値は保持される。");
        }

        private FacialControllerLifetimeScope BuildScope(params AdapterBindingBase[] bindings)
        {
            var host = new GameObject("FacialControllerLifetimeScopeDisabledBindingTestsHost");
            _hostGameObjects.Add(host);
            FacialControllerLifetimeScope scope = FacialControllerLifetimeScope.Build(
                _appScope,
                new FacialProfile("2.0"),
                new[] { "Blink" },
                bindings,
                host,
                "FacialControllerLifetimeScopeDisabledBindingTestsChild");
            _scopes.Add(scope);
            return scope;
        }

        private sealed class TestAppLifetimeScope : LifetimeScope
        {
            protected override void Configure(IContainerBuilder builder)
            {
                builder.RegisterInstance<ITimeProvider>(new ManualTimeProvider());
            }
        }

        [Serializable]
        private sealed class CountingAdapterBinding : AdapterBindingBase
        {
            public int Setting;
            public int SettingAtStart;
            public int OnStartCount;
            public int OnTickCount;
            public int DisposeCount;

            public override void OnStart(in AdapterBuildContext ctx)
            {
                OnStartCount++;
                SettingAtStart = Setting;
            }

            public override void OnTick(float deltaTime)
            {
                OnTickCount++;
            }

            public override void Dispose()
            {
                DisposeCount++;
            }
        }
    }
}
