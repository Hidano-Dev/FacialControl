using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;
using Hidano.FacialControl.LipSync.Adapters;
using Hidano.FacialControl.LipSync.Adapters.Devices;
using Hidano.FacialControl.LipSync.Adapters.PhonemeEntries;
using Hidano.FacialControl.LipSync.Tests.Shared;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Hidano.FacialControl.LipSync.Tests.PlayMode.Lifecycle
{
    /// <summary>
    /// <see cref="ULipSyncAdapterBinding"/> の PlayMode ライフサイクルテスト。
    /// OnStart / OnFixedTick / Dispose によるコンポーネント追加・除去と入力源登録、
    /// <see cref="LipSyncDeviceStore"/> 経由のデバイス解決と既定マイクへのフォールバック、
    /// FacialProfile の予約 slot 宣言に応じた phoneme overlay 入力源の登録・解除を検証する。
    /// </summary>
    /// <remarks>
    /// <see cref="LipSyncDeviceStoreTestBase"/> を継承して Fake PlayerPrefs backend を全テストに適用する。
    /// DeviceDescriptor を明示 Configure するテストでは DeviceStore は参照されないため副作用は無い。
    /// </remarks>
    [TestFixture]
    internal class ULipSyncAdapterBindingLifecycleTests : LipSyncDeviceStoreTestBase
    {
        private const string Slug = "ulipsync";
        private const string OverlayASlug = "lipsync-overlay:a";
        private const string MicDeviceName = "Unit Test Mic";
        private const string MissingDeviceName = "Missing Mic";
        private const string PrimaryMicDeviceName = "DeviceStore Primary Mic";
        private const string SecondaryMicDeviceName = "DeviceStore Secondary Mic";
        private const string BlendShapeName = "Mouth_A";
        private const string PhonemeId = "A";

        private static readonly string[] BlendShapeNames =
        {
            "Mouth_A",
            "Mouth_I",
            "Mouth_U",
            "Mouth_E",
            "Mouth_O",
        };

        private GameObject _hostGameObject;
        private InputSourceRegistry _registry;
        private ULipSyncAdapterBinding _binding;
        private uLipSync.Profile _profile;
        private Mesh _mesh;
        private bool _bindingStarted;

        protected override void InstallBackend(IPlayerPrefsBackend backend)
        {
            LipSyncDeviceStore.SetBackend(backend);
        }

        protected override void UninstallBackend()
        {
            LipSyncDeviceStore.ResetBackend();
            PlayerPrefs.DeleteKey(LipSyncDeviceStore.KeyName);
            PlayerPrefs.DeleteKey(LipSyncDeviceStore.KeyDisambiguator);
        }

        public override void SetUp()
        {
            base.SetUp();
            _registry = new InputSourceRegistry();
            _hostGameObject = new GameObject("ULipSyncAdapterBindingLifecycleTestsHost");
            _hostGameObject.SetActive(false);
            _profile = CreateAnalyzerProfile();
            CreateSkinnedMeshChild();
        }

        public override void TearDown()
        {
            if (_binding != null && _bindingStarted)
            {
                try
                {
                    _binding.Dispose();
                }
                catch (Exception)
                {
                    // TearDown では本体の assertion を優先する。
                }
            }

            _binding = null;
            _bindingStarted = false;

            if (_profile != null)
            {
                UnityEngine.Object.DestroyImmediate(_profile);
                _profile = null;
            }

            if (_hostGameObject != null)
            {
                UnityEngine.Object.DestroyImmediate(_hostGameObject);
                _hostGameObject = null;
            }

            if (_mesh != null)
            {
                UnityEngine.Object.DestroyImmediate(_mesh);
                _mesh = null;
            }

            base.TearDown();
        }

        #region Lifecycle（OnStart / OnFixedTick / Dispose）

        [Test]
        public void OnStart_ResolvedMicDevice_AddsAudioSourceAnalyzerAndMicrophoneInOrder()
        {
            _binding = CreateBinding();
            AdapterBuildContext ctx = CreateContext();

            _binding.OnStart(in ctx);
            _bindingStarted = true;

            Component[] components = _hostGameObject.GetComponents<Component>();
            int audioSourceIndex = IndexOfComponent<AudioSource>(components);
            int analyzerIndex = IndexOfComponent<uLipSync.uLipSync>(components);
            int microphoneIndex = IndexOfComponent<uLipSync.uLipSyncMicrophone>(components);

            Assert.That(audioSourceIndex, Is.GreaterThan(0),
                "OnStart は HostGameObject に AudioSource を AddComponent するべき。");
            Assert.That(analyzerIndex, Is.GreaterThan(audioSourceIndex),
                "OnStart の AddComponent 順序は AudioSource -> uLipSync.uLipSync であるべき。");
            Assert.That(microphoneIndex, Is.GreaterThan(analyzerIndex),
                "Mic 経路の AddComponent 順序は AudioSource -> uLipSync.uLipSync -> uLipSyncMicrophone であるべき。");

            Assert.That(_binding.Provider, Is.Not.Null, "OnStart 成功後は Provider が構築済みであるべき。");
            Assert.That(_binding.Analyzer, Is.Not.Null, "OnStart 成功後は Analyzer が構築済みであるべき。");
            Assert.That(_binding.Analyzer, Is.SameAs(components[analyzerIndex]));
            Assert.That(_binding.Analyzer.profile, Is.SameAs(_profile),
                "設定済み Analyzer Profile が uLipSync.uLipSync に注入されるべき。");

            var microphone = (uLipSync.uLipSyncMicrophone)components[microphoneIndex];
            Assert.That(microphone.index, Is.EqualTo(0),
                "Fake enumerator で解決した Mic の列挙 index が uLipSyncMicrophone に反映されるべき。");

            Assert.That(_registry.TryResolve(OverlayASlug, out IInputSource source), Is.True,
                "OnStart 成功後は phoneme overlay source が登録されるべき。");
            Assert.That(source, Is.InstanceOf<LipSyncPhonemeOverlayInputSource>());
            Assert.That(_registry.TryResolve(Slug, out _), Is.False);
        }

        [Test]
        public void OnStart_ResolvedMicDevice_FirstProviderReadIsZeroSettled()
        {
            _binding = CreateBinding();
            AdapterBuildContext ctx = CreateContext();

            _binding.OnStart(in ctx);
            _bindingStarted = true;

            var info = new uLipSync.LipSyncInfo
            {
                phoneme = PhonemeId,
                volume = 1f,
                rawVolume = 1f,
                phonemeRatios = new Dictionary<string, float>
                {
                    { PhonemeId, 1f },
                },
            };
            _binding.Analyzer.onLipSyncUpdate.Invoke(info);

            var output = new float[] { -1f };
            _binding.Provider.GetLipSyncValues(output);

            Assert.That(output[0], Is.EqualTo(0f).Within(1e-6f),
                "OnStart 末尾の RequestZeroOutputForNextFrame により初回 GetLipSyncValues はゼロ化されるべき。");

            _binding.Provider.GetLipSyncValues(output);
            Assert.That(output[0], Is.GreaterThan(0f),
                "zero settle は初回読み出しだけに適用され、受信済みの非ゼロ値は次回以降に読めるべき。");
        }

        [UnityTest]
        public IEnumerator Dispose_AfterStart_RemovesAllAddedComponentsAndUnregistersInputSource()
        {
            _binding = CreateBinding();
            AdapterBuildContext ctx = CreateContext();

            _binding.OnStart(in ctx);
            _bindingStarted = true;

            var audioSource = _hostGameObject.GetComponent<AudioSource>();
            var analyzer = _hostGameObject.GetComponent<uLipSync.uLipSync>();
            var microphone = _hostGameObject.GetComponent<uLipSync.uLipSyncMicrophone>();
            Assert.That(audioSource, Is.Not.Null);
            Assert.That(analyzer, Is.Not.Null);
            Assert.That(microphone, Is.Not.Null);
            Assert.That(_registry.TryResolve(OverlayASlug, out _), Is.True);

            _binding.Dispose();
            _bindingStarted = false;

            yield return null;

            Assert.That(_binding.IsStarted, Is.False);
            Assert.That(_binding.Provider, Is.Null);
            Assert.That(_binding.Analyzer, Is.Null);
            Assert.That(audioSource == null, Is.True);
            Assert.That(analyzer == null, Is.True);
            Assert.That(microphone == null, Is.True);
            Assert.That(_hostGameObject.GetComponent<AudioSource>(), Is.Null);
            Assert.That(_hostGameObject.GetComponent<uLipSync.uLipSync>(), Is.Null);
            Assert.That(_hostGameObject.GetComponent<uLipSync.uLipSyncMicrophone>(), Is.Null);
            Assert.That(_registry.TryResolve(OverlayASlug, out var removed), Is.False);
            Assert.That(removed, Is.Null);
            CollectionAssert.DoesNotContain(_registry.RegisteredIds, OverlayASlug);
        }

        [Test]
        public void OnFixedTick_WhenSwapIsNotPending_IsNoOp()
        {
            _binding = CreateBinding();
            AdapterBuildContext ctx = CreateContext();

            _binding.OnStart(in ctx);
            _bindingStarted = true;

            var analyzer = _binding.Analyzer;
            var provider = _binding.Provider;
            Assert.That(_registry.TryResolve(OverlayASlug, out IInputSource inputSource), Is.True);

            _binding.OnFixedTick(0.02f);
            _binding.OnFixedTick(0.02f);

            Assert.That(_binding.IsStarted, Is.True);
            Assert.That(_binding.Analyzer, Is.SameAs(analyzer));
            Assert.That(_binding.Provider, Is.SameAs(provider));
            Assert.That(_registry.TryResolve(OverlayASlug, out IInputSource resolved), Is.True);
            Assert.That(resolved, Is.SameAs(inputSource));
        }

        [Test]
        public void OnStart_UnresolvedDevice_LogsErrorAndDoesNotRegister()
        {
            _binding = CreateBinding(MissingDeviceName, _profile);
            AdapterBuildContext ctx = CreateContext();

            LogAssert.Expect(
                LogType.Error,
                new Regex("ULipSyncAdapterBinding.*could not be resolved"));
            _binding.OnStart(in ctx);

            Assert.That(_binding.IsStarted, Is.False);
            Assert.That(_binding.Provider, Is.Null);
            Assert.That(_binding.Analyzer, Is.Null);
            Assert.That(_hostGameObject.GetComponent<AudioSource>(), Is.Null);
            Assert.That(_hostGameObject.GetComponent<uLipSync.uLipSync>(), Is.Null);
            Assert.That(_hostGameObject.GetComponent<uLipSync.uLipSyncMicrophone>(), Is.Null);
            Assert.That(_hostGameObject.GetComponent<uLipSync.uLipSyncAsioInput>(), Is.Null);
            Assert.That(_registry.TryResolve(OverlayASlug, out var source), Is.False);
            Assert.That(source, Is.Null);
            CollectionAssert.DoesNotContain(_registry.RegisteredIds, OverlayASlug);
        }

        [Test]
        public void OnStart_AnalyzerProfileMissing_UsesPackagedDefaultProfile()
        {
            _binding = CreateBinding(MicDeviceName, null);
            AdapterBuildContext ctx = CreateContext();

            _binding.OnStart(in ctx);
            _bindingStarted = true;

            Assert.That(_binding.IsStarted, Is.True);
            Assert.That(_binding.Provider, Is.Not.Null);
            Assert.That(_binding.Analyzer, Is.Not.Null);
            Assert.That(_binding.Analyzer.profile, Is.Not.Null);
            Assert.That(_binding.Analyzer.profile.name, Is.EqualTo("Default uLipSync Profile"));
            Assert.That(_hostGameObject.GetComponent<AudioSource>(), Is.Not.Null);
            Assert.That(_hostGameObject.GetComponent<uLipSync.uLipSync>(), Is.Not.Null);
            Assert.That(_hostGameObject.GetComponent<uLipSync.uLipSyncMicrophone>(), Is.Not.Null);
            Assert.That(_hostGameObject.GetComponent<uLipSync.uLipSyncAsioInput>(), Is.Null);
            Assert.That(_registry.TryResolve(OverlayASlug, out IInputSource source), Is.True);
            Assert.That(source, Is.InstanceOf<LipSyncPhonemeOverlayInputSource>());
        }

        [Test]
        public void OnStart_DuplicateBindingOnSameCharacter_LogsErrorAndSkips()
        {
            _binding = CreateBinding();
            AdapterBuildContext ctx = CreateContext();

            _binding.OnStart(in ctx);
            _bindingStarted = true;

            int analyzerCountBefore = _hostGameObject.GetComponents<uLipSync.uLipSync>().Length;
            var duplicate = CreateBinding();

            LogAssert.Expect(
                LogType.Error,
                new Regex("ULipSyncAdapterBinding.*lipsync-overlay:a"));
            duplicate.OnStart(in ctx);

            Assert.That(duplicate.IsStarted, Is.False);
            Assert.That(duplicate.Provider, Is.Null);
            Assert.That(_hostGameObject.GetComponents<uLipSync.uLipSync>().Length, Is.EqualTo(analyzerCountBefore));
            Assert.That(_registry.TryResolve(OverlayASlug, out IInputSource source), Is.True);
            Assert.That(source, Is.InstanceOf<LipSyncPhonemeOverlayInputSource>());
        }

        #endregion

        #region DeviceStore（DeviceDescriptor 未 Configure 時のデバイス解決）

        [Test]
        public void OnStart_LoadsDeviceFromStore_InitializesMicWithStoredDeviceName()
        {
            LipSyncDeviceStore.Save(new DeviceDescriptor
            {
                DeviceName = PrimaryMicDeviceName,
                DisambiguatorIndex = 0,
            });

            _binding = CreateStoreBackedBinding(new FakeMicrophoneDeviceEnumerator(
                "Other Mic",
                PrimaryMicDeviceName,
                SecondaryMicDeviceName));
            AdapterBuildContext ctx = CreateContext();

            _binding.OnStart(in ctx);
            _bindingStarted = true;

            Assert.That(_binding.IsStarted, Is.True,
                "DeviceStore 経由で解決した DeviceName により binding が起動するべき。");

            var microphone = _hostGameObject.GetComponent<uLipSync.uLipSyncMicrophone>();
            Assert.That(microphone, Is.Not.Null,
                "OnStart は HostGameObject に uLipSyncMicrophone を AddComponent するべき。");
            Assert.That(microphone.index, Is.EqualTo(1),
                "Fake enumerator における PrimaryMicDeviceName の列挙 index (1) が uLipSyncMicrophone に反映されるべき。");
        }

        [Test]
        public void OnStart_DeviceStoreReturnsEmptyDeviceName_FallsBackToFirstMicrophoneAndStarts()
        {
            // DeviceStore に何も Save していないので Load は DeviceName="" を返す。
            // デバイス未選択のままでもリップシンクが全滅しないよう、既定のマイク
            // （マイク一覧の先頭）へフォールバックして binding は起動する。
            _binding = CreateStoreBackedBinding(new FakeMicrophoneDeviceEnumerator(
                PrimaryMicDeviceName,
                SecondaryMicDeviceName));
            AdapterBuildContext ctx = CreateContext();

            _binding.OnStart(in ctx);
            _bindingStarted = true;

            Assert.That(_binding.IsStarted, Is.True,
                "DeviceStore に DeviceName 未保存 (空文字) のとき、既定マイクへフォールバックして起動するべき。");

            var microphone = _hostGameObject.GetComponent<uLipSync.uLipSyncMicrophone>();
            Assert.That(microphone, Is.Not.Null,
                "フォールバック起動時も uLipSyncMicrophone が AddComponent されるべき。");
            Assert.That(microphone.index, Is.EqualTo(0),
                "フォールバックはマイク一覧の先頭 (index 0) を使用するべき。");
        }

        [Test]
        public void OnStart_EmptyDeviceNameAndNoMicrophones_LogsErrorAndDoesNotStart()
        {
            // デバイス未選択かつマイクが 1 台も無い場合は従来どおり未解決エラーで起動しない。
            _binding = CreateStoreBackedBinding(new FakeMicrophoneDeviceEnumerator());
            AdapterBuildContext ctx = CreateContext();

            LogAssert.Expect(
                LogType.Error,
                new Regex("ULipSyncAdapterBinding.*could not be resolved"));
            _binding.OnStart(in ctx);

            Assert.That(_binding.IsStarted, Is.False,
                "デバイス未選択かつマイク 0 台のとき、binding は起動してはならない。");
            Assert.That(_hostGameObject.GetComponent<uLipSync.uLipSyncMicrophone>(), Is.Null,
                "未解決のとき uLipSyncMicrophone は AddComponent されるべきでない。");
        }

        [Test]
        public void OnStart_FakeBackendInstalled_DoesNotWriteToRealPlayerPrefs()
        {
            LipSyncDeviceStore.Save(new DeviceDescriptor
            {
                DeviceName = PrimaryMicDeviceName,
                DisambiguatorIndex = 0,
            });

            _binding = CreateStoreBackedBinding(new FakeMicrophoneDeviceEnumerator(PrimaryMicDeviceName));
            AdapterBuildContext ctx = CreateContext();

            _binding.OnStart(in ctx);
            _bindingStarted = true;

            Assert.That(_binding.IsStarted, Is.True);
            Assert.That(PlayerPrefs.HasKey(LipSyncDeviceStore.KeyName), Is.False,
                "Fake backend 経由なので実 PlayerPrefs (DeviceName キー) に書き込まれてはならない。");
            Assert.That(PlayerPrefs.HasKey(LipSyncDeviceStore.KeyDisambiguator), Is.False,
                "Fake backend 経由なので実 PlayerPrefs (Disambiguator キー) に書き込まれてはならない。");
            Assert.That(Backend.ContainsStringKey(LipSyncDeviceStore.KeyName), Is.True,
                "Save 後は Fake backend に DeviceName キーが格納されているべき。");
            Assert.That(Backend.GetString(LipSyncDeviceStore.KeyName, "fallback"),
                Is.EqualTo(PrimaryMicDeviceName));
        }

        #endregion

        #region PhonemeOverlay（予約 slot 宣言に応じた overlay 入力源の登録・解除）

        [Test]
        public void OnStart_WithReservedSlotsDeclared_RegistersLipSyncPhonemeOverlayInputSources()
        {
            _binding = CreateAllReservedSlotsBinding();
            AdapterBuildContext ctx = CreateContext(PhonemeOverlaySlots.ReservedNames.ToArray());

            _binding.OnStart(in ctx);
            _bindingStarted = true;

            AssertRegistered(PhonemeOverlaySlots.A);
            AssertRegistered(PhonemeOverlaySlots.I);
            AssertRegistered(PhonemeOverlaySlots.U);
            AssertRegistered(PhonemeOverlaySlots.E);
            AssertRegistered(PhonemeOverlaySlots.O);
        }

        [Test]
        public void OnStart_NoReservedSlotsDeclared_LogsWarningAndSkips()
        {
            _binding = CreateAllReservedSlotsBinding();
            AdapterBuildContext ctx = CreateContext(Array.Empty<string>());

            LogAssert.Expect(
                LogType.Warning,
                new Regex("ULipSyncAdapterBinding.*slot"));
            _binding.OnStart(in ctx);
            _bindingStarted = true;

            Assert.That(_registry.RegisteredIds, Is.Empty);
        }

        [Test]
        public void OnStart_PartialSlotsDeclared_RegistersOnlyDeclaredSlots()
        {
            _binding = CreateAllReservedSlotsBinding();
            AdapterBuildContext ctx = CreateContext(new[] { PhonemeOverlaySlots.A, PhonemeOverlaySlots.U });

            _binding.OnStart(in ctx);
            _bindingStarted = true;

            AssertRegistered(PhonemeOverlaySlots.A);
            AssertRegistered(PhonemeOverlaySlots.U);
            AssertNotRegistered(PhonemeOverlaySlots.I);
            AssertNotRegistered(PhonemeOverlaySlots.E);
            AssertNotRegistered(PhonemeOverlaySlots.O);
        }

        [Test]
        public void OnStart_ReservedSlotsDeclared_DoesNotRegisterBareSlugSource()
        {
            _binding = CreateAllReservedSlotsBinding();
            AdapterBuildContext ctx = CreateContext(PhonemeOverlaySlots.ReservedNames.ToArray());

            _binding.OnStart(in ctx);
            _bindingStarted = true;

            Assert.That(_registry.TryResolve(Slug, out IInputSource source), Is.False);
            Assert.That(source, Is.Null);
            CollectionAssert.DoesNotContain(_registry.RegisteredIds, Slug);
        }

        [UnityTest]
        public IEnumerator Dispose_UnregistersAllPhonemeOverlaySlots()
        {
            _binding = CreateAllReservedSlotsBinding();
            AdapterBuildContext ctx = CreateContext(PhonemeOverlaySlots.ReservedNames.ToArray());

            _binding.OnStart(in ctx);
            _bindingStarted = true;
            Assert.That(_registry.RegisteredIds.Count, Is.EqualTo(PhonemeOverlaySlots.ReservedNames.Length));

            _binding.Dispose();
            _bindingStarted = false;

            yield return null;

            AssertNotRegistered(PhonemeOverlaySlots.A);
            AssertNotRegistered(PhonemeOverlaySlots.I);
            AssertNotRegistered(PhonemeOverlaySlots.U);
            AssertNotRegistered(PhonemeOverlaySlots.E);
            AssertNotRegistered(PhonemeOverlaySlots.O);
            Assert.That(_registry.RegisteredIds, Is.Empty);
        }

        private void AssertRegistered(string slot)
        {
            // 登録キーはレイヤーの入力源 id と同じ固定 prefix "lipsync-overlay:{slot}" でなければ
            // FacialController.ResolveLayerInputSourcesFromRegistry が解決できず集約に乗らない。
            string id = $"{LipSyncPhonemeOverlayInputSource.SlugPrefix}:{slot}";
            Assert.That(_registry.TryResolve(id, out IInputSource source), Is.True, id);
            Assert.That(source, Is.InstanceOf<LipSyncPhonemeOverlayInputSource>(), id);
        }

        private void AssertNotRegistered(string slot)
        {
            string id = $"{LipSyncPhonemeOverlayInputSource.SlugPrefix}:{slot}";
            Assert.That(_registry.TryResolve(id, out IInputSource source), Is.False, id);
            Assert.That(source, Is.Null, id);
        }

        #endregion

        #region 共通ヘルパー

        /// <summary>
        /// DeviceDescriptor を明示 Configure し、音素 A 1 件だけを持つ binding を生成する（Lifecycle 用）。
        /// </summary>
        private ULipSyncAdapterBinding CreateBinding()
        {
            return CreateBinding(MicDeviceName, _profile);
        }

        private ULipSyncAdapterBinding CreateBinding(string deviceName, uLipSync.Profile analyzerProfile)
        {
            var binding = new ULipSyncAdapterBinding { Slug = Slug };
            binding.Configure(
                new DeviceDescriptor
                {
                    DeviceName = deviceName,
                    DisambiguatorIndex = 0,
                },
                analyzerProfile,
                CreateSinglePhonemeEntries(),
                new FakeAsioDriverEnumerator(),
                new FakeMicrophoneDeviceEnumerator(MicDeviceName));
            return binding;
        }

        /// <summary>
        /// DeviceDescriptor を Configure せず、OnStart 時に <see cref="LipSyncDeviceStore"/> から
        /// デバイスを解決させる binding を生成する（DeviceStore 用）。
        /// </summary>
        private ULipSyncAdapterBinding CreateStoreBackedBinding(IMicrophoneDeviceEnumerator micEnumerator)
        {
            var binding = new ULipSyncAdapterBinding { Slug = Slug };
            binding.Configure(
                _profile,
                CreateSinglePhonemeEntries(),
                new FakeAsioDriverEnumerator(),
                micEnumerator);
            return binding;
        }

        /// <summary>
        /// 予約 slot a/i/u/e/o 全てに BlendShape 音素エントリを持つ binding を生成する（PhonemeOverlay 用）。
        /// </summary>
        private ULipSyncAdapterBinding CreateAllReservedSlotsBinding()
        {
            var binding = new ULipSyncAdapterBinding { Slug = Slug };
            binding.Configure(
                new DeviceDescriptor
                {
                    DeviceName = MicDeviceName,
                    DisambiguatorIndex = 0,
                },
                _profile,
                CreateAllReservedSlotPhonemeEntries(),
                new FakeAsioDriverEnumerator(),
                new FakeMicrophoneDeviceEnumerator(MicDeviceName));
            return binding;
        }

        private static PhonemeEntryBase[] CreateSinglePhonemeEntries()
        {
            return new PhonemeEntryBase[]
            {
                new BlendShapePhonemeEntry
                {
                    PhonemeId = PhonemeId,
                    BlendShapeName = BlendShapeName,
                    MaxWeight = 80f,
                },
            };
        }

        private static PhonemeEntryBase[] CreateAllReservedSlotPhonemeEntries()
        {
            ReadOnlySpan<string> slots = PhonemeOverlaySlots.ReservedNames;
            var entries = new PhonemeEntryBase[slots.Length];
            for (int i = 0; i < slots.Length; i++)
            {
                entries[i] = new BlendShapePhonemeEntry
                {
                    PhonemeId = PhonemeOverlaySlots.MapReservedToPhonemeId(slots[i]),
                    BlendShapeName = BlendShapeNames[i],
                    MaxWeight = 100f,
                };
            }

            return entries;
        }

        /// <summary>
        /// 予約 slot A のみを宣言し、BlendShape は Mouth_A 1 件のコンテキストを生成する。
        /// </summary>
        private AdapterBuildContext CreateContext()
        {
            return new AdapterBuildContext(
                profile: new FacialProfile("1.0", slots: new[] { PhonemeOverlaySlots.A }),
                blendShapeNames: new List<string> { BlendShapeName },
                inputSourceRegistry: _registry,
                facialOutputBus: new FacialOutputBus(),
                timeProvider: new UnityTimeProvider(),
                hostGameObject: _hostGameObject,
                lipSyncProvider: null);
        }

        /// <summary>
        /// 任意の slot 宣言と 5 音素分の BlendShape 名を持つコンテキストを生成する。
        /// </summary>
        private AdapterBuildContext CreateContext(string[] slots)
        {
            return new AdapterBuildContext(
                profile: new FacialProfile("1.0", slots: slots),
                blendShapeNames: BlendShapeNames,
                inputSourceRegistry: _registry,
                facialOutputBus: new FacialOutputBus(),
                timeProvider: new UnityTimeProvider(),
                hostGameObject: _hostGameObject,
                lipSyncProvider: null);
        }

        /// <summary>
        /// 予約 slot 全音素の MFCC を持つ Analyzer Profile を生成する。
        /// 音素 A のみを使うテストにとっても上位互換（Profile の MFCC 内容は FacialControl 側では参照しない）。
        /// </summary>
        private static uLipSync.Profile CreateAnalyzerProfile()
        {
            uLipSync.Profile profile = ScriptableObject.CreateInstance<uLipSync.Profile>();
            ReadOnlySpan<string> slots = PhonemeOverlaySlots.ReservedNames;
            for (int i = 0; i < slots.Length; i++)
            {
                profile.AddMfcc(PhonemeOverlaySlots.MapReservedToPhonemeId(slots[i]));
            }

            return profile;
        }

        /// <summary>
        /// 5 音素分の BlendShape を持つ SkinnedMeshRenderer 子オブジェクトを生成する。
        /// Mouth_A のみを使うテストにとっても上位互換。
        /// </summary>
        private void CreateSkinnedMeshChild()
        {
            var meshObject = new GameObject("FaceMesh");
            meshObject.transform.SetParent(_hostGameObject.transform, false);
            var renderer = meshObject.AddComponent<SkinnedMeshRenderer>();

            _mesh = new Mesh { name = "ULipSyncAdapterBindingLifecycleTestsMesh" };
            _mesh.vertices = new[]
            {
                Vector3.zero,
                Vector3.right,
                Vector3.up,
            };
            _mesh.triangles = new[] { 0, 1, 2 };

            Vector3[] deltaVertices = new[]
            {
                Vector3.up * 0.01f,
                Vector3.up * 0.01f,
                Vector3.up * 0.01f,
            };
            Vector3[] deltaNormals = new Vector3[deltaVertices.Length];
            Vector3[] deltaTangents = new Vector3[deltaVertices.Length];
            for (int i = 0; i < BlendShapeNames.Length; i++)
            {
                _mesh.AddBlendShapeFrame(
                    BlendShapeNames[i],
                    100f,
                    deltaVertices,
                    deltaNormals,
                    deltaTangents);
            }

            renderer.sharedMesh = _mesh;
        }

        private static int IndexOfComponent<T>(Component[] components)
            where T : Component
        {
            for (int i = 0; i < components.Length; i++)
            {
                if (components[i] is T)
                {
                    return i;
                }
            }

            return -1;
        }

        #endregion
    }
}
