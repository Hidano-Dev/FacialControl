using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Hidano.FacialControl.Adapters.DependencyInjection;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;
using Hidano.FacialControl.Tests.Shared;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.TestTools;
using VContainer.Unity;
#if FACIALCONTROL_HAS_INPUTSYSTEM_MODULE && FACIALCONTROL_HAS_OSC_MODULE
using Hidano.FacialControl.Adapters.AdapterBindings;
using Hidano.FacialControl.Adapters.AdapterBindings.ARKit;
using Hidano.FacialControl.Adapters.AdapterBindings.InputSystem;
using Hidano.FacialControl.Testing;
#endif

namespace Hidano.FacialControl.Tests.PlayMode.Performance
{
    /// <summary>
    /// <see cref="AdapterBindingHost"/> の GC 契約テスト。
    /// 通常フレーム / 例外後のスキップ継続フレームは 0 バイト、例外発生フレームは 1 KB 未満であることを
    /// 10 体 × 3 binding 構成で検証する。binding は Mock 派生（TrackingAdapterBinding）と、
    /// InputSystem / OSC モジュールが存在する場合は実 binding（OscReceiver / InputSystem / ArKitOsc）の両方で確認する。
    /// </summary>
    [TestFixture]
    [MediumTest]
    public class AdapterBindingHostAllocationTests : SizedTestFixture
    {
        private const int CharacterCount = 10;
        private const int BindingsPerCharacter = 3;
        private const int FramesToMeasure = 60;
        private const long ExceptionFrameAllocationBudgetBytes = 1024;

        private static readonly Regex AdapterBindingHostOnTickErrorPattern =
            new Regex("AdapterBindingHost.*OnTick", RegexOptions.IgnoreCase);

        private readonly List<GameObject> _hostGameObjects = new List<GameObject>(CharacterCount);

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _hostGameObjects.Count; i++)
            {
                if (_hostGameObjects[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(_hostGameObjects[i]);
                }
            }

            _hostGameObjects.Clear();
        }

        // ---- Mock binding（TrackingAdapterBinding）構成 ----

        [Test]
        public void Tick_NormalSteadyState_ThreeBindingsTenCharactersAllocatesZeroBytes()
        {
            AdapterBindingHost[] hosts = BuildHosts(null, CreateTrackingBinding, out _);

            InitializeHosts(hosts);
            ExecuteFrames(hosts, 2);
            StabilizeManagedHeap();

            long allocated = MeasureManagedAllocationByTotalMemory(() => ExecuteFrames(hosts, FramesToMeasure));

            Assert.LessOrEqual(allocated, 0,
                $"Normal steady-state managed allocation was detected over 60 frames: {allocated} bytes");
        }

        [Test]
        public void Tick_SkippedSteadyState_AfterExceptionAllocatesZeroBytes()
        {
            var throwingBinding = new ThrowOnNextTickAdapterBinding();
            AdapterBindingHost[] hosts = BuildHosts(throwingBinding, CreateTrackingBinding, out _);

            InitializeHosts(hosts);

            // Warm Unity TestRunner の log capture pipeline を別 host 群で先に起動し、
            // 本 fixture の measurement window が log buffer 初期化アロケに巻き込まれないようにする。
            WarmUpExceptionLogCapture(CreateTrackingBinding);

            throwingBinding.ThrowOnNextTick = true;
            using (new ErrorStackTraceScope(StackTraceLogType.None))
            {
                LogAssert.Expect(LogType.Error, AdapterBindingHostOnTickErrorPattern);
                ExecuteFrames(hosts, 1);
            }

            Assert.That(throwingBinding.OnTickCount, Is.EqualTo(1));

            ExecuteFrames(hosts, 2);
            StabilizeManagedHeap();

            long allocated = MeasureManagedAllocationByTotalMemory(() => ExecuteFrames(hosts, FramesToMeasure));

            Assert.LessOrEqual(allocated, 0,
                $"Skipped steady-state managed allocation was detected over 60 frames: {allocated} bytes");
        }

        [Test]
        public void Tick_ExceptionFrame_AllocatesLessThanOneKilobyte()
        {
            var throwingBinding = new ThrowOnNextTickAdapterBinding();
            AdapterBindingHost[] hosts = BuildHosts(throwingBinding, CreateTrackingBinding, out int throwingHostIndex);

            InitializeHosts(hosts);
            ExecuteFrames(hosts, 2);
            WarmUpExceptionLogCapture(CreateTrackingBinding);
            StabilizeManagedHeap();

            throwingBinding.ThrowOnNextTick = true;
            long allocated;
            // Unity TestRunner keeps error stack traces in managed result buffers; this keeps the
            // sample focused on AdapterBindingHost's exception-frame path rather than runner storage.
            using (new ErrorStackTraceScope(StackTraceLogType.None))
            {
                LogAssert.Expect(LogType.Error, AdapterBindingHostOnTickErrorPattern);
                allocated = MeasureManagedAllocationByTotalMemory(() =>
                {
                    ((ITickable)hosts[throwingHostIndex]).Tick();
                });
            }

            Assert.That(throwingBinding.OnTickCount, Is.EqualTo(3));
            Assert.Less(allocated, ExceptionFrameAllocationBudgetBytes,
                $"Exception frame managed allocation exceeded the budget: {allocated} bytes");
        }

#if FACIALCONTROL_HAS_INPUTSYSTEM_MODULE && FACIALCONTROL_HAS_OSC_MODULE
        // ---- 実 binding（OSC + InputSystem + ARKit）構成 ----
        // Mock 派生で確立した 0-alloc 契約が実具象（OscReceiverAdapterBinding / InputSystemAdapterBinding /
        // ArKitOscAdapterBinding）でも維持されていることを同じ 3 binding × 10 体構成で確認する。

        [Test]
        public void Tick_NormalSteadyState_RealBindingsAllocateZeroBytes()
        {
            AdapterBindingHost[] hosts = BuildHosts(throwingBinding: null, CreateRealBinding, out _);

            InitializeHosts(hosts);
            ExecuteFrames(hosts, 2);
            StabilizeManagedHeap();

            long allocated = MeasureManagedAllocationByProfilerRecorder(() => ExecuteFrames(hosts, FramesToMeasure));

            Assert.LessOrEqual(allocated, 0,
                "Real-binding normal steady-state managed allocation was detected over " +
                FramesToMeasure + " frames with " + CharacterCount + " characters × " +
                BindingsPerCharacter + " real bindings: " + allocated + " bytes");
        }

        [Test]
        public void Tick_SkippedSteadyState_AfterRealBindingExceptionAllocatesZeroBytes()
        {
            var throwingBinding = new ThrowOnNextTickAdapterBinding();
            AdapterBindingHost[] hosts = BuildHosts(throwingBinding, CreateRealBinding, out _);

            InitializeHosts(hosts);

            throwingBinding.ThrowOnNextTick = true;
            LogAssert.Expect(LogType.Error, AdapterBindingHostOnTickErrorPattern);
            ExecuteFrames(hosts, 1);

            Assert.That(throwingBinding.OnTickCount, Is.EqualTo(1));

            ExecuteFrames(hosts, 2);
            StabilizeManagedHeap();

            long allocated = MeasureManagedAllocationByProfilerRecorder(() => ExecuteFrames(hosts, FramesToMeasure));

            Assert.LessOrEqual(allocated, 0,
                "Real-binding skipped steady-state managed allocation was detected over " +
                FramesToMeasure + " frames: " + allocated + " bytes");
        }

        [Test]
        public void Tick_ExceptionFrame_RealBindingsAllocateLessThanOneKilobyte()
        {
            var throwingBinding = new ThrowOnNextTickAdapterBinding();
            AdapterBindingHost[] hosts = BuildHosts(throwingBinding, CreateRealBinding, out int throwingHostIndex);

            InitializeHosts(hosts);
            ExecuteFrames(hosts, 2);
            WarmUpExceptionLogCapture(CreateRealBinding);
            StabilizeManagedHeap();

            throwingBinding.ThrowOnNextTick = true;
            long allocated;
            // Unity TestRunner keeps error stack traces in managed result buffers; this keeps the
            // sample focused on AdapterBindingHost's exception-frame path rather than runner storage.
            using (new ErrorStackTraceScope(StackTraceLogType.None))
            {
                LogAssert.Expect(LogType.Error, AdapterBindingHostOnTickErrorPattern);
                allocated = MeasureManagedAllocationByProfilerRecorder(() =>
                {
                    ((ITickable)hosts[throwingHostIndex]).Tick();
                });
            }

            Assert.That(throwingBinding.OnTickCount, Is.EqualTo(3));
            Assert.Less(allocated, ExceptionFrameAllocationBudgetBytes,
                "Real-binding exception frame managed allocation exceeded the budget: " + allocated + " bytes");
        }

        // 各 character につき OSC + InputSystem + ARKit を 1 binding ずつ生成する。
        // どれも external resource（UDP socket, InputActionAsset）を要する OnStart を完走させない
        // 最小設定で構築するため、各 binding の OnStart は warning ブランチで早期 return し
        // 内部 _started フラグは false のまま維持される。これにより
        // 各具象の Tick / LateTick / FixedTick はすべて early-return path を辿り、
        // AdapterBindingHost 側の dispatch + virtual 呼び出しの 0-alloc 契約のみが検証対象となる。
        private static AdapterBindingBase CreateRealBinding(int slot, int characterIndex)
        {
            switch (slot)
            {
                case 0:
                    // 受信ポートは binding 本体の既定値で起動できてしまうため、不正値 0 を入れて
                    // 警告ブランチで早期 return させる。
                    return new OscReceiverAdapterBinding
                    {
                        Slug = "real-osc-" + characterIndex,
                        Port = 0,
                    };
                case 1:
                    return new InputSystemAdapterBinding
                    {
                        Slug = "real-input-system-" + characterIndex,
                    };
                case 2:
                    return new ArKitOscAdapterBinding
                    {
                        Slug = "real-arkit-" + characterIndex,
                    };
                default:
                    throw new InvalidOperationException(
                        "Unexpected real binding slot index: " + slot);
            }
        }
#endif

        // ---- ヘルパー ----

        /// <summary>Mock 構成用の binding ファクトリ。slot / character に依らず TrackingAdapterBinding を返す。</summary>
        private static AdapterBindingBase CreateTrackingBinding(int slot, int characterIndex)
        {
            return new TrackingAdapterBinding();
        }

        /// <summary>
        /// CharacterCount × BindingsPerCharacter 個の host を構築する。
        /// throwingBinding が指定された場合は character 0 / slot 0 に差し込み、その host index を返す。
        /// それ以外の slot は bindingFactory(slot, characterIndex) で生成する。
        /// </summary>
        private AdapterBindingHost[] BuildHosts(
            ThrowOnNextTickAdapterBinding throwingBinding,
            Func<int, int, AdapterBindingBase> bindingFactory,
            out int throwingHostIndex)
        {
            var hosts = new AdapterBindingHost[CharacterCount * BindingsPerCharacter];
            throwingHostIndex = -1;

            var profile = new FacialProfile("2.0");
            var blendShapeNames = new[] { "Blink", "Smile", "MouthOpen" };

            int index = 0;
            for (int character = 0; character < CharacterCount; character++)
            {
                var hostGameObject = new GameObject("AdapterBindingHostAllocationTestsHost_" + character);
                _hostGameObjects.Add(hostGameObject);

                var context = new AdapterBuildContext(
                    profile,
                    blendShapeNames,
                    new InputSourceRegistry(),
                    new FacialOutputBus(),
                    new ManualTimeProvider(),
                    hostGameObject,
                    lipSyncProvider: null);

                for (int slot = 0; slot < BindingsPerCharacter; slot++)
                {
                    AdapterBindingBase bindingInstance;
                    if (throwingBinding != null && character == 0 && slot == 0)
                    {
                        bindingInstance = throwingBinding;
                        throwingHostIndex = index;
                    }
                    else
                    {
                        bindingInstance = bindingFactory(slot, character);
                    }

                    hosts[index] = new AdapterBindingHost(bindingInstance, context);
                    index++;
                }
            }

            return hosts;
        }

        /// <summary>
        /// 計測対象とは別の host 群で例外を 1 回発生させ、Unity TestRunner の log capture pipeline を先に初期化する。
        /// </summary>
        private void WarmUpExceptionLogCapture(Func<int, int, AdapterBindingBase> bindingFactory)
        {
            var warmupBinding = new ThrowOnNextTickAdapterBinding
            {
                ThrowOnNextTick = true,
            };
            AdapterBindingHost[] warmupHosts = BuildHosts(warmupBinding, bindingFactory, out int warmupHostIndex);

            InitializeHosts(warmupHosts);
            using (new ErrorStackTraceScope(StackTraceLogType.None))
            {
                LogAssert.Expect(LogType.Error, AdapterBindingHostOnTickErrorPattern);
                ((ITickable)warmupHosts[warmupHostIndex]).Tick();
            }
        }

        private static void InitializeHosts(AdapterBindingHost[] hosts)
        {
            for (int i = 0; i < hosts.Length; i++)
            {
                ((IInitializable)hosts[i]).Initialize();
                ((IStartable)hosts[i]).Start();
            }
        }

        private static void ExecuteFrames(AdapterBindingHost[] hosts, int frameCount)
        {
            for (int frame = 0; frame < frameCount; frame++)
            {
                for (int i = 0; i < hosts.Length; i++)
                {
                    ((ITickable)hosts[i]).Tick();
                }

                for (int i = 0; i < hosts.Length; i++)
                {
                    ((ILateTickable)hosts[i]).LateTick();
                }

                for (int i = 0; i < hosts.Length; i++)
                {
                    ((IFixedTickable)hosts[i]).FixedTick();
                }
            }
        }

        /// <summary>GC.GetTotalMemory の前後差分でマネージド確保量を測る（Mock 構成のテストで使用）。</summary>
        private static long MeasureManagedAllocationByTotalMemory(Action action)
        {
            long before = GC.GetTotalMemory(forceFullCollection: false);
            action();
            long after = GC.GetTotalMemory(forceFullCollection: false);
            return after - before;
        }

        /// <summary>
        /// ProfilerRecorder の "GC.Alloc"（現在スレッドのみ）でマネージド確保量を測る（実 binding 構成のテストで使用）。
        /// </summary>
        private static long MeasureManagedAllocationByProfilerRecorder(Action action)
        {
            using var recorder = ProfilerRecorder.StartNew(
                ProfilerCategory.Memory,
                "GC.Alloc",
                1,
                ProfilerRecorderOptions.SumAllSamplesInFrame
                    | ProfilerRecorderOptions.CollectOnlyOnCurrentThread);

            action();
            return recorder.LastValue;
        }

        private static void StabilizeManagedHeap()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        private sealed class ErrorStackTraceScope : IDisposable
        {
            private readonly StackTraceLogType _previous;

            public ErrorStackTraceScope(StackTraceLogType stackTraceLogType)
            {
                _previous = UnityEngine.Application.GetStackTraceLogType(LogType.Error);
                UnityEngine.Application.SetStackTraceLogType(LogType.Error, stackTraceLogType);
            }

            public void Dispose()
            {
                UnityEngine.Application.SetStackTraceLogType(LogType.Error, _previous);
            }
        }

        [Serializable]
        private sealed class TrackingAdapterBinding : AdapterBindingBase
        {
            private int _tickCount;

            public override void OnTick(float deltaTime)
            {
                _tickCount++;
            }

            public override void OnLateTick(float deltaTime)
            {
                _tickCount++;
            }

            public override void OnFixedTick(float fixedDeltaTime)
            {
                _tickCount++;
            }
        }

        [Serializable]
        private sealed class ThrowOnNextTickAdapterBinding : AdapterBindingBase
        {
            private readonly Exception _exception =
                new InvalidOperationException("AdapterBindingHostAllocationTests.OnTick");

            public int OnTickCount;
            public bool ThrowOnNextTick;

            public override void OnTick(float deltaTime)
            {
                OnTickCount++;
                if (!ThrowOnNextTick)
                {
                    return;
                }

                ThrowOnNextTick = false;
                throw _exception;
            }
        }
    }
}
