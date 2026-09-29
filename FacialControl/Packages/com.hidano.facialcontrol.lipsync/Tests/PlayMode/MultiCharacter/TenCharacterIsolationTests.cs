using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
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

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.LipSync.Tests.PlayMode.MultiCharacter
{
    [TestFixture]
    [MediumTest]
    public class TenCharacterIsolationTests : SizedTestFixture
    {
        private const int CharacterCount = 10;
        private const int SwapCharacterIndex = 4;
        // 音量・音素ウェイトの SmoothDamp は uLipSync 公式へ委譲済み。
        // 旧 provider の「初回フレーム即時 snap」は廃止されたため、定常値まで収束させる投入回数。
        private const int SettleFrameCount = 256;
        // uLipSync 公式の SmoothDamp は Time.deltaTime に依存する。batchmode(-nographics) では
        // 1 フレームが 1ms 未満になり得て、同じ投入回数でも収束量が実行ごとに変わる
        // （過去の赤: 0.4 期待に対し 0.32 / 0.2846）。captureDeltaTime で 60 FPS 相当に固定し決定的にする。
        private const float FixedDeltaTimeSeconds = 1f / 60f;
        private const double FrameBudgetMs = 16.6;
        private const string Slug = "ulipsync";
        private const string OverlayASlug = "lipsync-overlay:a";
        private const string BlendShapeName = "Mouth_A";
        private const string PhonemeId = "A";

        private CharacterRuntime[] _characters = Array.Empty<CharacterRuntime>();
        private uLipSync.Profile _profile;

        [TearDown]
        public void TearDown()
        {
            Time.captureDeltaTime = 0f;

            for (int i = 0; i < _characters.Length; i++)
            {
                _characters[i]?.Dispose();
            }

            _characters = Array.Empty<CharacterRuntime>();

            if (_profile != null)
            {
                UnityEngine.Object.DestroyImmediate(_profile);
                _profile = null;
            }
        }

        [UnityTest]
        public IEnumerator TenIndependentBindings_OneSwap_DoesNotAffectOthers()
        {
            // captureDeltaTime は次フレームから Time.deltaTime に反映されるため、1 フレーム進めてから投入を始める。
            Time.captureDeltaTime = FixedDeltaTimeSeconds;
            yield return null;
            Assert.That(Time.deltaTime, Is.EqualTo(FixedDeltaTimeSeconds).Within(1e-6f),
                "SmoothDamp の収束を決定的にするため Time.deltaTime は固定されているべき。");

            _profile = CreateAnalyzerProfile();
            _characters = CreateCharacters(_profile);

            StartAllCharacters();
            PrimeProviderOutputs();
            CaptureInputChains();

            CharacterRuntime swapTarget = _characters[SwapCharacterIndex];
            Stopwatch stopwatch = Stopwatch.StartNew();

            swapTarget.Binding.SwapDevice(swapTarget.SwapDeviceName, 0);
            AssertTargetZeroSettled(swapTarget);
            AssertOtherCharactersContinue(SwapCharacterIndex, "pending swap");

            swapTarget.Binding.OnFixedTick(0.02f);
            stopwatch.Stop();

            Assert.That(stopwatch.Elapsed.TotalMilliseconds, Is.LessThan(FrameBudgetMs),
                "1 件の hot-swap は 60 FPS 相当の 1 フレーム予算内に収まるべき。");

            yield return null;

            AssertTargetSwapCompleted(swapTarget);
            AssertOtherInputChainsUnaffected(SwapCharacterIndex);
            AssertOtherCharactersContinue(SwapCharacterIndex, "completed swap");
        }

        private void StartAllCharacters()
        {
            for (int i = 0; i < _characters.Length; i++)
            {
                CharacterRuntime character = _characters[i];
                AdapterBuildContext ctx = character.CreateContext();

                character.Binding.OnStart(in ctx);
                character.IsStarted = true;

                Assert.That(character.Binding.IsStarted, Is.True,
                    $"Character {i} の ULipSyncAdapterBinding は起動済みであるべき。");
                Assert.That(character.Binding.Provider, Is.Not.Null);
                Assert.That(character.Binding.Analyzer, Is.Not.Null);
                Assert.That(character.HostGameObject.GetComponent<uLipSync.uLipSyncMicrophone>(), Is.Not.Null);
                Assert.That(character.Registry.TryResolve(OverlayASlug, out IInputSource source), Is.True);
                Assert.That(source, Is.InstanceOf<LipSyncPhonemeOverlayInputSource>());
                CollectionAssert.AreEqual(new[] { OverlayASlug }, character.Registry.RegisteredIds);
            }
        }

        private void PrimeProviderOutputs()
        {
            for (int i = 0; i < _characters.Length; i++)
            {
                CharacterRuntime character = _characters[i];
                character.EmitPhonemeFrame();

                var output = new float[] { -1f };
                IInputSource inputSource = ResolveOverlaySource(character);
                Assert.That(inputSource.TryWriteValues(output), Is.False,
                    $"Character {i} の初回読み出しは OnStart の zero settle で無音扱いになるべき。");
                Assert.That(output[0], Is.EqualTo(-1f),
                    $"Character {i} の無音フレームでは output が変更されないべき。");

                // SmoothDamp（uLipSync 委譲）を定常値まで収束させてから出力を検証する。
                // LipSyncUpdateEvent モードでは onLipSyncUpdate 受信時のみ SmoothDamp が進むため、
                // 収束後は追加投入なしで定常値を保持する。
                for (int f = 0; f < SettleFrameCount; f++)
                {
                    character.EmitPhonemeFrame();
                }

                AssertOutputContinues(character, i, "primed output");
            }
        }

        private void CaptureInputChains()
        {
            for (int i = 0; i < _characters.Length; i++)
            {
                CharacterRuntime character = _characters[i];
                Assert.That(character.Registry.TryResolve(OverlayASlug, out IInputSource registrySource), Is.True);

                character.ProviderBeforeSwap = character.Binding.Provider;
                character.InputSourceBeforeSwap = registrySource;
                character.RegistrySourceBeforeSwap = registrySource;
                character.AnalyzerBeforeSwap = character.Binding.Analyzer;
            }
        }

        private static void AssertTargetZeroSettled(CharacterRuntime target)
        {
            var providerOutput = new float[] { -1f };
            target.Binding.Provider.GetLipSyncValues(providerOutput);

            Assert.That(providerOutput[0], Is.EqualTo(0f).Within(1e-6f),
                "SwapDevice は対象 binding の provider に 1 フレーム分のゼロ値を通知するべき。");
        }

        private void AssertTargetSwapCompleted(CharacterRuntime target)
        {
            Assert.That(target.Binding.Provider, Is.SameAs(target.ProviderBeforeSwap),
                "SwapDevice は provider インスタンスを再生成しないべき。");
            Assert.That(target.Binding.Analyzer, Is.SameAs(target.AnalyzerBeforeSwap),
                "SwapDevice は uLipSync analyzer を維持するべき。");
            Assert.That(target.Registry.TryResolve(OverlayASlug, out IInputSource resolved), Is.True);
            Assert.That(resolved, Is.SameAs(target.RegistrySourceBeforeSwap));

            var microphones = target.HostGameObject.GetComponents<uLipSync.uLipSyncMicrophone>();
            Assert.That(microphones.Length, Is.EqualTo(1));
            Assert.That(microphones[0].index, Is.EqualTo(1),
                "Swap 先の DeviceDescriptor は同じ binding 内で新しい microphone index に解決されるべき。");
        }

        private void AssertOtherInputChainsUnaffected(int excludedIndex)
        {
            for (int i = 0; i < _characters.Length; i++)
            {
                if (i == excludedIndex)
                {
                    continue;
                }

                CharacterRuntime character = _characters[i];
                Assert.That(character.Binding.Provider, Is.SameAs(character.ProviderBeforeSwap),
                    $"Character {i} の Provider は他 character の swap で差し替わらないべき。");
                Assert.That(character.Binding.Analyzer, Is.SameAs(character.AnalyzerBeforeSwap),
                    $"Character {i} の analyzer は他 character の swap で差し替わらないべき。");
                Assert.That(character.Registry.TryResolve(OverlayASlug, out IInputSource resolved), Is.True);
                Assert.That(resolved, Is.SameAs(character.RegistrySourceBeforeSwap),
                    $"Character {i} の registry 登録は他 character の swap 後も同じ source を解決するべき。");
                CollectionAssert.AreEqual(new[] { OverlayASlug }, character.Registry.RegisteredIds);
            }
        }

        private void AssertOtherCharactersContinue(int excludedIndex, string phase)
        {
            for (int i = 0; i < _characters.Length; i++)
            {
                if (i == excludedIndex)
                {
                    continue;
                }

                AssertOutputContinues(_characters[i], i, phase);
            }
        }

        private static void AssertOutputContinues(
            CharacterRuntime character,
            int characterIndex,
            string phase)
        {
            var output = new float[] { -1f };

            IInputSource inputSource = ResolveOverlaySource(character);
            Assert.That(inputSource.TryWriteValues(output), Is.True,
                $"Character {characterIndex} は {phase} 中も Provider 出力を継続するべき。");
            Assert.That(output[0], Is.EqualTo(ExpectedWeight(characterIndex)).Within(1e-6f),
                $"Character {characterIndex} の出力値は {phase} 中も自身の phoneme entry に対応するべき。");
        }

        private static IInputSource ResolveOverlaySource(CharacterRuntime character)
        {
            Assert.That(character.Registry.TryResolve(OverlayASlug, out IInputSource source), Is.True);
            return source;
        }

        private CharacterRuntime[] CreateCharacters(uLipSync.Profile profile)
        {
            var characters = new CharacterRuntime[CharacterCount];
            for (int i = 0; i < characters.Length; i++)
            {
                string deviceName = $"Unit Test Mic {i}";
                string swapDeviceName = $"Unit Test Mic {i} Backup";
                var hostGameObject = new GameObject($"FacialCharacter_{i}");
                hostGameObject.SetActive(false);

                Mesh mesh = CreateSkinnedMeshChild(hostGameObject, i);
                var registry = new InputSourceRegistry();
                var binding = new ULipSyncAdapterBinding { Slug = Slug };
                binding.Configure(
                    new DeviceDescriptor
                    {
                        DeviceName = deviceName,
                        DisambiguatorIndex = 0,
                    },
                    profile,
                    new PhonemeEntryBase[]
                    {
                        new BlendShapePhonemeEntry
                        {
                            PhonemeId = PhonemeId,
                            BlendShapeName = BlendShapeName,
                            MaxWeight = 40f + i,
                        },
                    },
                    new FakeAsioDriverEnumerator(),
                    new FakeMicrophoneDeviceEnumerator(deviceName, swapDeviceName));

                characters[i] = new CharacterRuntime(
                    hostGameObject,
                    mesh,
                    registry,
                    binding,
                    deviceName,
                    swapDeviceName);
            }

            return characters;
        }

        private static AdapterBuildContext CreateContext(GameObject hostGameObject, InputSourceRegistry registry)
        {
            return new AdapterBuildContext(
                profile: new FacialProfile("1.0", slots: new[] { PhonemeOverlaySlots.A }),
                blendShapeNames: new List<string> { BlendShapeName },
                inputSourceRegistry: registry,
                facialOutputBus: new FacialOutputBus(),
                timeProvider: new UnityTimeProvider(),
                hostGameObject: hostGameObject,
                lipSyncProvider: null);
        }

        private static uLipSync.Profile CreateAnalyzerProfile()
        {
            uLipSync.Profile profile = ScriptableObject.CreateInstance<uLipSync.Profile>();
            profile.AddMfcc(PhonemeId);
            return profile;
        }

        private static Mesh CreateSkinnedMeshChild(GameObject hostGameObject, int characterIndex)
        {
            var meshObject = new GameObject("FaceMesh");
            meshObject.transform.SetParent(hostGameObject.transform, false);
            var renderer = meshObject.AddComponent<SkinnedMeshRenderer>();

            var mesh = new Mesh { name = $"TenCharacterIsolationTestsMesh_{characterIndex}" };
            mesh.vertices = new[]
            {
                Vector3.zero,
                Vector3.right,
                Vector3.up,
            };
            mesh.triangles = new[] { 0, 1, 2 };

            Vector3[] deltaVertices = new[]
            {
                Vector3.up * 0.01f,
                Vector3.up * 0.01f,
                Vector3.up * 0.01f,
            };
            Vector3[] deltaNormals = new Vector3[deltaVertices.Length];
            Vector3[] deltaTangents = new Vector3[deltaVertices.Length];
            mesh.AddBlendShapeFrame(BlendShapeName, 100f, deltaVertices, deltaNormals, deltaTangents);
            renderer.sharedMesh = mesh;
            return mesh;
        }

        private static float ExpectedWeight(int characterIndex)
        {
            return (40f + characterIndex) / 100f;
        }

        private sealed class CharacterRuntime : IDisposable
        {
            public CharacterRuntime(
                GameObject hostGameObject,
                Mesh mesh,
                InputSourceRegistry registry,
                ULipSyncAdapterBinding binding,
                string deviceName,
                string swapDeviceName)
            {
                HostGameObject = hostGameObject;
                Mesh = mesh;
                Registry = registry;
                Binding = binding;
                DeviceName = deviceName;
                SwapDeviceName = swapDeviceName;
            }

            public GameObject HostGameObject { get; }
            public Mesh Mesh { get; }
            public InputSourceRegistry Registry { get; }
            public ULipSyncAdapterBinding Binding { get; }
            public string DeviceName { get; }
            public string SwapDeviceName { get; }
            public bool IsStarted { get; set; }
            public ULipSyncProvider ProviderBeforeSwap { get; set; }
            public IInputSource InputSourceBeforeSwap { get; set; }
            public IInputSource RegistrySourceBeforeSwap { get; set; }
            public uLipSync.uLipSync AnalyzerBeforeSwap { get; set; }

            public AdapterBuildContext CreateContext()
            {
                return TenCharacterIsolationTests.CreateContext(HostGameObject, Registry);
            }

            public void EmitPhonemeFrame()
            {
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
                Binding.Analyzer.onLipSyncUpdate.Invoke(info);
            }

            public void Dispose()
            {
                if (Binding != null && IsStarted)
                {
                    try
                    {
                        Binding.Dispose();
                    }
                    catch (Exception)
                    {
                    }

                    IsStarted = false;
                }

                if (HostGameObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(HostGameObject);
                }

                if (Mesh != null)
                {
                    UnityEngine.Object.DestroyImmediate(Mesh);
                }
            }
        }
    }
}
