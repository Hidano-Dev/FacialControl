using System;
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
using VContainer.Unity;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.EditMode.Adapters
{
    /// <summary>
    /// task 3.2 の観測可能完了条件: <see cref="AdapterBindingHost"/> が VContainer の各
    /// lifecycle interface を介して binding の virtual method を呼び出し、例外を catch して
    /// 以降の Tick 系を no-op に遷移し、<c>Dispose</c> は <c>_skipped</c> の値に関わらず
    /// 必ず呼ばれる挙動を assert する。1 host = 1 binding の独立性も併せて検証する。
    /// </summary>
    /// <remarks>
    /// 本ファイルは Red 段階のテストであり、<c>Hidano.FacialControl.Adapters.DependencyInjection</c>
    /// 名前空間および <see cref="AdapterBindingHost"/> 実装が未作成のため、コンパイル時に
    /// CS0246 (型 / 名前空間 が見つからない) が発生して Red 状態となる（task 3.3 の Green 化対象）。
    /// </remarks>
    [TestFixture]
    [SmallTest]
    public class AdapterBindingHostTests : SizedTestFixture
    {
        private static readonly Regex LogErrorPattern = new Regex("AdapterBindingHost");

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

        private sealed class MockAdapterBinding : AdapterBindingBase
        {
            public int OnStartCount;
            public int OnTickCount;
            public int OnLateTickCount;
            public int OnFixedTickCount;
            public int DisposeCount;

            public bool ThrowOnStart;
            public bool ThrowOnTick;
            public bool ThrowOnLateTick;
            public bool ThrowOnFixedTick;
            public bool ThrowOnDispose;

            public override void OnStart(in AdapterBuildContext ctx)
            {
                OnStartCount++;
                if (ThrowOnStart)
                {
                    throw new InvalidOperationException("MockAdapterBinding.OnStart");
                }
            }

            public override void OnTick(float deltaTime)
            {
                OnTickCount++;
                if (ThrowOnTick)
                {
                    throw new InvalidOperationException("MockAdapterBinding.OnTick");
                }
            }

            public override void OnLateTick(float deltaTime)
            {
                OnLateTickCount++;
                if (ThrowOnLateTick)
                {
                    throw new InvalidOperationException("MockAdapterBinding.OnLateTick");
                }
            }

            public override void OnFixedTick(float fixedDeltaTime)
            {
                OnFixedTickCount++;
                if (ThrowOnFixedTick)
                {
                    throw new InvalidOperationException("MockAdapterBinding.OnFixedTick");
                }
            }

            public override void Dispose()
            {
                DisposeCount++;
                if (ThrowOnDispose)
                {
                    throw new InvalidOperationException("MockAdapterBinding.Dispose");
                }
            }
        }

        private GameObject _hostGameObject;

        [TearDown]
        public void TearDown()
        {
            if (_hostGameObject != null)
            {
                UnityEngine.Object.DestroyImmediate(_hostGameObject);
                _hostGameObject = null;
            }
        }

        private AdapterBuildContext CreateContext(string objName = "AdapterBindingHostTestsHost")
        {
            _hostGameObject = new GameObject(objName);
            return new AdapterBuildContext(
                new FacialProfile("1.0"),
                new List<string> { "A", "B" },
                new StubInputSourceRegistry(),
                new FacialOutputBus(),
                new ManualTimeProvider(),
                _hostGameObject,
                lipSyncProvider: null);
        }

        // ---------------------------------------------------------------
        // 正常系: 各 lifecycle interface が binding の virtual method に dispatch する
        // ---------------------------------------------------------------

        [Test]
        public void Start_DispatchesOnStartToBinding()
        {
            var ctx = CreateContext();
            var binding = new MockAdapterBinding();
            var host = new AdapterBindingHost(binding, ctx);

            ((IStartable)host).Start();

            Assert.AreEqual(1, binding.OnStartCount);
        }

        [Test]
        public void Tick_DispatchesOnTickToBinding()
        {
            var ctx = CreateContext();
            var binding = new MockAdapterBinding();
            var host = new AdapterBindingHost(binding, ctx);

            ((ITickable)host).Tick();

            Assert.AreEqual(1, binding.OnTickCount);
        }

        [Test]
        public void LateTick_DispatchesOnLateTickToBinding()
        {
            var ctx = CreateContext();
            var binding = new MockAdapterBinding();
            var host = new AdapterBindingHost(binding, ctx);

            ((ILateTickable)host).LateTick();

            Assert.AreEqual(1, binding.OnLateTickCount);
        }

        [Test]
        public void FixedTick_DispatchesOnFixedTickToBinding()
        {
            var ctx = CreateContext();
            var binding = new MockAdapterBinding();
            var host = new AdapterBindingHost(binding, ctx);

            ((IFixedTickable)host).FixedTick();

            Assert.AreEqual(1, binding.OnFixedTickCount);
        }

        [Test]
        public void Dispose_DispatchesDisposeToBinding_WhenNoExceptionOccurred()
        {
            var ctx = CreateContext();
            var binding = new MockAdapterBinding();
            var host = new AdapterBindingHost(binding, ctx);

            ((IDisposable)host).Dispose();

            Assert.AreEqual(1, binding.DisposeCount);
        }

        // ---------------------------------------------------------------
        // 例外 isolation: 各 lifecycle が例外を投げると _skipped = true に遷移し
        // 以後の Tick 系が no-op になる
        // ---------------------------------------------------------------

        [Test]
        public void Start_BindingThrows_ExceptionIsCaughtAndLogged()
        {
            LogAssert.Expect(LogType.Error, LogErrorPattern);
            var ctx = CreateContext();
            var binding = new MockAdapterBinding { ThrowOnStart = true };
            var host = new AdapterBindingHost(binding, ctx);

            Assert.DoesNotThrow(() => ((IStartable)host).Start());
            Assert.AreEqual(1, binding.OnStartCount);
        }

        [Test]
        public void Tick_AfterOnStartThrew_AllTickFamilyIsNoOp()
        {
            LogAssert.Expect(LogType.Error, LogErrorPattern);
            var ctx = CreateContext();
            var binding = new MockAdapterBinding { ThrowOnStart = true };
            var host = new AdapterBindingHost(binding, ctx);

            ((IStartable)host).Start();

            ((ITickable)host).Tick();
            ((ILateTickable)host).LateTick();
            ((IFixedTickable)host).FixedTick();

            Assert.AreEqual(0, binding.OnTickCount, "OnTick must be skipped after OnStart threw");
            Assert.AreEqual(0, binding.OnLateTickCount, "OnLateTick must be skipped after OnStart threw");
            Assert.AreEqual(0, binding.OnFixedTickCount, "OnFixedTick must be skipped after OnStart threw");
        }

        [Test]
        public void Tick_BindingThrows_ExceptionIsCaughtAndSubsequentTicksAreNoOp()
        {
            LogAssert.Expect(LogType.Error, LogErrorPattern);
            var ctx = CreateContext();
            var binding = new MockAdapterBinding { ThrowOnTick = true };
            var host = new AdapterBindingHost(binding, ctx);

            Assert.DoesNotThrow(() => ((ITickable)host).Tick());
            ((ITickable)host).Tick();
            ((ITickable)host).Tick();

            Assert.AreEqual(1, binding.OnTickCount, "Subsequent OnTick calls must be no-op once _skipped is true");
        }

        [Test]
        public void Tick_AfterOnTickThrew_OtherTickFamilyIsAlsoNoOp()
        {
            LogAssert.Expect(LogType.Error, LogErrorPattern);
            var ctx = CreateContext();
            var binding = new MockAdapterBinding { ThrowOnTick = true };
            var host = new AdapterBindingHost(binding, ctx);

            ((ITickable)host).Tick();

            ((ILateTickable)host).LateTick();
            ((IFixedTickable)host).FixedTick();

            Assert.AreEqual(1, binding.OnTickCount);
            Assert.AreEqual(0, binding.OnLateTickCount, "_skipped must propagate across all tick families");
            Assert.AreEqual(0, binding.OnFixedTickCount, "_skipped must propagate across all tick families");
        }

        [Test]
        public void LateTick_BindingThrows_ExceptionIsCaughtAndSubsequentLateTicksAreNoOp()
        {
            LogAssert.Expect(LogType.Error, LogErrorPattern);
            var ctx = CreateContext();
            var binding = new MockAdapterBinding { ThrowOnLateTick = true };
            var host = new AdapterBindingHost(binding, ctx);

            Assert.DoesNotThrow(() => ((ILateTickable)host).LateTick());
            ((ILateTickable)host).LateTick();

            Assert.AreEqual(1, binding.OnLateTickCount);
        }

        [Test]
        public void FixedTick_BindingThrows_ExceptionIsCaughtAndSubsequentFixedTicksAreNoOp()
        {
            LogAssert.Expect(LogType.Error, LogErrorPattern);
            var ctx = CreateContext();
            var binding = new MockAdapterBinding { ThrowOnFixedTick = true };
            var host = new AdapterBindingHost(binding, ctx);

            Assert.DoesNotThrow(() => ((IFixedTickable)host).FixedTick());
            ((IFixedTickable)host).FixedTick();

            Assert.AreEqual(1, binding.OnFixedTickCount);
        }

        // ---------------------------------------------------------------
        // Dispose は _skipped の値に関わらず必ず呼ばれる + Dispose 自体の例外も catch される
        // ---------------------------------------------------------------

        [Test]
        public void Dispose_IsCalledEvenAfterSkipByPriorLifecycleException()
        {
            LogAssert.Expect(LogType.Error, LogErrorPattern);
            var ctx = CreateContext();
            var binding = new MockAdapterBinding { ThrowOnTick = true };
            var host = new AdapterBindingHost(binding, ctx);

            ((ITickable)host).Tick(); // _skipped = true に遷移
            Assert.AreEqual(0, binding.DisposeCount);

            ((IDisposable)host).Dispose();

            Assert.AreEqual(1, binding.DisposeCount, "Dispose must be invoked once even after _skipped became true");
        }

        [Test]
        public void Dispose_IsCalledEvenAfterOnStartThrew()
        {
            LogAssert.Expect(LogType.Error, LogErrorPattern);
            var ctx = CreateContext();
            var binding = new MockAdapterBinding { ThrowOnStart = true };
            var host = new AdapterBindingHost(binding, ctx);

            ((IStartable)host).Start();
            ((IDisposable)host).Dispose();

            Assert.AreEqual(1, binding.DisposeCount, "Dispose must be invoked once even after OnStart threw");
        }

        [Test]
        public void Dispose_BindingThrows_ExceptionIsCaughtAndLogged()
        {
            LogAssert.Expect(LogType.Error, LogErrorPattern);
            var ctx = CreateContext();
            var binding = new MockAdapterBinding { ThrowOnDispose = true };
            var host = new AdapterBindingHost(binding, ctx);

            Assert.DoesNotThrow(() => ((IDisposable)host).Dispose());
            Assert.AreEqual(1, binding.DisposeCount);
        }

        // ---------------------------------------------------------------
        // 1 host = 1 binding の対応関係（複数 host を独立に Tick できる）
        // ---------------------------------------------------------------

        [Test]
        public void MultipleHosts_TickIndependently()
        {
            var ctx = CreateContext();
            var bindingA = new MockAdapterBinding();
            var bindingB = new MockAdapterBinding();
            var hostA = new AdapterBindingHost(bindingA, ctx);
            var hostB = new AdapterBindingHost(bindingB, ctx);

            ((ITickable)hostA).Tick();
            Assert.AreEqual(1, bindingA.OnTickCount);
            Assert.AreEqual(0, bindingB.OnTickCount, "Tick of host A must not affect binding B");

            ((ITickable)hostB).Tick();
            Assert.AreEqual(1, bindingA.OnTickCount, "Tick of host B must not affect binding A");
            Assert.AreEqual(1, bindingB.OnTickCount);
        }

        [Test]
        public void MultipleHosts_OneSkippedDoesNotAffectOther()
        {
            LogAssert.Expect(LogType.Error, LogErrorPattern);
            var ctx = CreateContext();
            var bindingA = new MockAdapterBinding { ThrowOnTick = true };
            var bindingB = new MockAdapterBinding();
            var hostA = new AdapterBindingHost(bindingA, ctx);
            var hostB = new AdapterBindingHost(bindingB, ctx);

            ((ITickable)hostA).Tick(); // host A: throw → _skipped = true
            ((ITickable)hostA).Tick(); // host A: no-op
            ((ITickable)hostB).Tick(); // host B: 正常 dispatch
            ((ITickable)hostB).Tick();

            Assert.AreEqual(1, bindingA.OnTickCount, "Host A must stop ticking after exception");
            Assert.AreEqual(2, bindingB.OnTickCount, "Host B must continue ticking independently");
        }

        [Test]
        public void MultipleHosts_DisposeIndependently()
        {
            var ctx = CreateContext();
            var bindingA = new MockAdapterBinding();
            var bindingB = new MockAdapterBinding();
            var hostA = new AdapterBindingHost(bindingA, ctx);
            var hostB = new AdapterBindingHost(bindingB, ctx);

            ((IDisposable)hostA).Dispose();
            Assert.AreEqual(1, bindingA.DisposeCount);
            Assert.AreEqual(0, bindingB.DisposeCount, "Dispose of host A must not affect binding B");

            ((IDisposable)hostB).Dispose();
            Assert.AreEqual(1, bindingA.DisposeCount);
            Assert.AreEqual(1, bindingB.DisposeCount);
        }
    }
}
