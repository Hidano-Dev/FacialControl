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

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.LipSync.Tests.PlayMode.HotSwap
{
    [TestFixture]
    [MediumTest]
    public class DeviceHotSwapTests : SizedTestFixture
    {
        private const string Slug = "ulipsync";
        private const string OverlayASlug = "lipsync-overlay:a";
        private const string MicDeviceName = "Unit Test Mic";
        private const string MissingDeviceName = "Missing Mic";
        private const string BlendShapeName = "Mouth_A";
        private const string PhonemeId = "A";

        private GameObject _hostGameObject;
        private InputSourceRegistry _registry;
        private ULipSyncAdapterBinding _binding;
        private uLipSync.Profile _profile;
        private Mesh _mesh;
        private bool _bindingStarted;

        [SetUp]
        public void SetUp()
        {
            _registry = new InputSourceRegistry();
            _hostGameObject = new GameObject("DeviceHotSwapTestsHost");
            _hostGameObject.SetActive(false);
            _profile = CreateAnalyzerProfile();
            CreateSkinnedMeshChild();
        }

        [TearDown]
        public void TearDown()
        {
            if (_binding != null && _bindingStarted)
            {
                try
                {
                    _binding.Dispose();
                }
                catch (Exception)
                {
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
        }

        [UnityTest]
        public IEnumerator SwapDevice_MicToMicSecondDevice_ZeroSettlesThenRebinds()
        {
            _binding = CreateBinding();
            AdapterBuildContext ctx = CreateContext();
            _binding.OnStart(in ctx);
            _bindingStarted = true;

            var firstMicrophone = _hostGameObject.GetComponent<uLipSync.uLipSyncMicrophone>();
            Assert.That(firstMicrophone, Is.Not.Null);
            Assert.That(firstMicrophone.index, Is.EqualTo(0));

            // OnStart 末尾の RequestZeroOutputForNextFrame をフラッシュしてから phoneme frame を投入し、
            // 1 frame 進めることで SmoothDamp が dt>0 で前進し output が立ち上がるのを検証する。
            var providerOutput = new float[] { -1f };
            _binding.Provider.GetLipSyncValues(providerOutput);
            EmitPhonemeFrame();
            yield return null;
            _binding.Provider.GetLipSyncValues(providerOutput);
            Assert.That(providerOutput[0], Is.GreaterThan(0f));

            _binding.SwapDevice(MicDeviceName, 1);

            providerOutput[0] = -1f;
            _binding.Provider.GetLipSyncValues(providerOutput);
            Assert.That(providerOutput[0], Is.EqualTo(0f).Within(1e-6f));

            _binding.OnFixedTick(0.02f);
            yield return null;

            var microphones = _hostGameObject.GetComponents<uLipSync.uLipSyncMicrophone>();
            Assert.That(microphones.Length, Is.EqualTo(1));
            Assert.That(microphones[0], Is.Not.SameAs(firstMicrophone));
            Assert.That(microphones[0].index, Is.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator SwapDevice_UnresolvedTarget_EntersSilenceModeWithoutBrokenComponents()
        {
            _binding = CreateBinding();
            AdapterBuildContext ctx = CreateContext();
            _binding.OnStart(in ctx);
            _bindingStarted = true;

            var analyzer = _binding.Analyzer;
            var provider = _binding.Provider;
            Assert.That(_registry.TryResolve(OverlayASlug, out IInputSource inputSource), Is.True);
            Assert.That(_hostGameObject.GetComponent<uLipSync.uLipSyncMicrophone>(), Is.Not.Null);

            LogAssert.Expect(
                LogType.Error,
                new Regex("ULipSyncAdapterBinding.*Missing Mic.*could not be resolved"));
            _binding.SwapDevice(MissingDeviceName, 0);

            var providerOutput = new float[] { -1f };
            _binding.Provider.GetLipSyncValues(providerOutput);
            Assert.That(providerOutput[0], Is.EqualTo(0f).Within(1e-6f));

            _binding.OnFixedTick(0.02f);
            yield return null;

            Assert.That(_hostGameObject.GetComponent<uLipSync.uLipSyncMicrophone>(), Is.Null);
            Assert.That(_hostGameObject.GetComponent<uLipSync.uLipSyncAsioInput>(), Is.Null);
            Assert.That(_binding.Analyzer, Is.SameAs(analyzer));
            Assert.That(_binding.Provider, Is.SameAs(provider));
            Assert.That(_registry.TryResolve(OverlayASlug, out IInputSource resolved), Is.True);
            Assert.That(resolved, Is.SameAs(inputSource));
        }

        [UnityTest]
        public IEnumerator SwapDevice_PreservesULipSyncAndProviderInstances()
        {
            _binding = CreateBinding();
            AdapterBuildContext ctx = CreateContext();
            _binding.OnStart(in ctx);
            _bindingStarted = true;

            var analyzer = _binding.Analyzer;
            var provider = _binding.Provider;
            Assert.That(_registry.TryResolve(OverlayASlug, out IInputSource inputSource), Is.True);

            _binding.SwapDevice(MicDeviceName, 1);
            _binding.OnFixedTick(0.02f);
            yield return null;

            Assert.That(_binding.Analyzer, Is.SameAs(analyzer));
            Assert.That(_binding.Provider, Is.SameAs(provider));
            Assert.That(_registry.TryResolve(OverlayASlug, out IInputSource resolved), Is.True);
            Assert.That(resolved, Is.SameAs(inputSource));
            CollectionAssert.AreEqual(new[] { OverlayASlug }, _registry.RegisteredIds);
        }

        private ULipSyncAdapterBinding CreateBinding()
        {
            var binding = new ULipSyncAdapterBinding { Slug = Slug };
            binding.Configure(
                new DeviceDescriptor
                {
                    DeviceName = MicDeviceName,
                    DisambiguatorIndex = 0,
                },
                _profile,
                new PhonemeEntryBase[]
                {
                    new BlendShapePhonemeEntry
                    {
                        PhonemeId = PhonemeId,
                        BlendShapeName = BlendShapeName,
                        MaxWeight = 80f,
                    },
                },
                new FakeAsioDriverEnumerator(),
                new FakeMicrophoneDeviceEnumerator(MicDeviceName, "Other Mic", MicDeviceName));
            return binding;
        }

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

        private void EmitPhonemeFrame()
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
            _binding.Analyzer.onLipSyncUpdate.Invoke(info);
        }

        private static uLipSync.Profile CreateAnalyzerProfile()
        {
            uLipSync.Profile profile = ScriptableObject.CreateInstance<uLipSync.Profile>();
            profile.AddMfcc(PhonemeId);
            return profile;
        }

        private void CreateSkinnedMeshChild()
        {
            var meshObject = new GameObject("FaceMesh");
            meshObject.transform.SetParent(_hostGameObject.transform, false);
            var renderer = meshObject.AddComponent<SkinnedMeshRenderer>();

            _mesh = new Mesh { name = "DeviceHotSwapTestsMesh" };
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
            _mesh.AddBlendShapeFrame(BlendShapeName, 100f, deltaVertices, deltaNormals, deltaTangents);
            renderer.sharedMesh = _mesh;
        }
    }
}
