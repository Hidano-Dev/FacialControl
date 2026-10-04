using System;
using System.Collections.Generic;
using System.Linq;
using Hidano.FacialControl.Adapters.AdapterBindings;
using Hidano.FacialControl.Adapters.AdapterBindings.ARKit;
using Hidano.FacialControl.Adapters.RuntimeSettings;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;
using Hidano.FacialControl.Tests.Shared;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

using Hidano.FacialControl.Testing;
using Hidano.FacialControl.Osc.Tests.EditMode.Testing;
namespace Hidano.FacialControl.Osc.Tests.EditMode.Adapters.AdapterBindings
{
    /// <summary>
    /// task 9.3 観測可能完了条件: <see cref="ArKitOscAdapterBinding"/> が
    /// <c>[Serializable]</c> + <c>[FacialAdapterBinding(displayName: "ARKit / PerfectSync")]</c>
    /// 付き sealed class で <see cref="AdapterBindingBase"/> 派生であり、
    /// 単一 <see cref="FacialCharacterProfileSO"/> に
    /// <see cref="OscReceiverAdapterBinding"/> + <see cref="ArKitOscAdapterBinding"/> を
    /// 同時に保持して round-trip できることを assert する。
    /// </summary>
    [TestFixture]
    [MediumTest]
    public class ArKitOscAdapterBindingTests : SizedTestFixture
    {
        private const string ExpectedDisplayName = "ARKit / PerfectSync";

        private const string TempFolderParent = "Assets";
        private const string TempFolderName = "__Temp_ArKitOscAdapterBindingTests";
        private static readonly string TempFolderPath = TempFolderParent + "/" + TempFolderName;

        private string _assetPath;

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(TempFolderPath))
            {
                AssetDatabase.CreateFolder(TempFolderParent, TempFolderName);
            }
            _assetPath = TempFolderPath + "/ArKitOscRoundTrip_" + Guid.NewGuid().ToString("N") + ".asset";
        }

        [TearDown]
        public void TearDown()
        {
            if (!string.IsNullOrEmpty(_assetPath))
            {
                AssetDatabase.DeleteAsset(_assetPath);
                _assetPath = null;
            }
            if (AssetDatabase.IsValidFolder(TempFolderPath))
            {
                var remaining = AssetDatabase.FindAssets(string.Empty, new[] { TempFolderPath });
                if (remaining == null || remaining.Length == 0)
                {
                    AssetDatabase.DeleteAsset(TempFolderPath);
                }
            }
        }

        // ============================================================
        // 型属性 / 継承 / discovery
        // ============================================================

        [Test]
        public void Type_HasSerializableAttribute_ForSerializeReferenceRoundTrip()
        {
            object[] attrs = typeof(ArKitOscAdapterBinding)
                .GetCustomAttributes(typeof(SerializableAttribute), inherit: false);

            Assert.That(attrs.Length, Is.EqualTo(1),
                "ArKitOscAdapterBinding に [Serializable] が付いていないと [SerializeReference] の round-trip が破綻する。");
        }

        [Test]
        public void Type_HasFacialAdapterBindingAttributeWithExpectedDisplayName()
        {
            object[] attrs = typeof(ArKitOscAdapterBinding)
                .GetCustomAttributes(typeof(FacialAdapterBindingAttribute), inherit: false);

            Assert.That(attrs.Length, Is.EqualTo(1),
                "ArKitOscAdapterBinding には [FacialAdapterBinding] が 1 件だけ付与されているべき。");

            var attr = (FacialAdapterBindingAttribute)attrs[0];
            Assert.That(attr.DisplayName, Is.EqualTo(ExpectedDisplayName),
                $"[FacialAdapterBinding] の displayName は \"{ExpectedDisplayName}\" であるべき。");
        }

        [Test]
        public void Type_DerivesFromAdapterBindingBase()
        {
            Assert.That(typeof(AdapterBindingBase).IsAssignableFrom(typeof(ArKitOscAdapterBinding)), Is.True,
                "ArKitOscAdapterBinding は AdapterBindingBase の派生でなければならない。");
        }

        [Test]
        public void Type_IsConcreteSealedClass()
        {
            Type type = typeof(ArKitOscAdapterBinding);

            Assert.That(type.IsAbstract, Is.False,
                "ArKitOscAdapterBinding は具象（非 abstract）クラスでなければならない。");
            Assert.That(type.IsSealed, Is.True,
                "ArKitOscAdapterBinding は sealed でなければならない。");
        }

        [Test]
        public void Type_HasParameterlessConstructor_ForActivatorCreateInstance()
        {
            System.Reflection.ConstructorInfo ctor = typeof(ArKitOscAdapterBinding)
                .GetConstructor(Type.EmptyTypes);

            Assert.That(ctor, Is.Not.Null,
                "Activator.CreateInstance で生成可能な parameterless constructor が必要。");
        }

        [Test]
        public void TypeCache_DiscoversArKitOscAdapterBindingViaFacialAdapterBindingAttribute()
        {
            List<Type> discovered = TypeCache
                .GetTypesWithAttribute<FacialAdapterBindingAttribute>()
                .ToList();

            CollectionAssert.Contains(discovered, typeof(ArKitOscAdapterBinding),
                "TypeCache discovery で ArKitOscAdapterBinding が列挙されるべき。");
        }

        [Test]
        public void OnStart_FakeRegistry_RegistersNoInputSource()
        {
            var registry = new FakeInputSourceRegistry();
            var binding = new ArKitOscAdapterBinding { Slug = "arkit" };
            binding.Configure("127.0.0.1", 39539, new[] { "jawOpen" });
            var host = new GameObject("ArKitOscFakeRegistryTests");

            try
            {
                binding.OnStart(new AdapterBuildContext(
                    new FacialProfile("2.0.0"), Array.Empty<string>(), registry,
                    new FacialOutputBus(), new ManualTimeProvider(), host, lipSyncProvider: null));

                Assert.That(registry.RegisterCallCount, Is.EqualTo(0));
                Assert.That(registry.ReplaceCallCount, Is.EqualTo(0));
                Assert.That(binding.AnalogSource, Is.Not.Null);
                Assert.That(typeof(IInputSource).IsAssignableFrom(binding.AnalogSource.GetType()), Is.False);
            }
            finally
            {
                binding.Dispose();
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        // ============================================================
        // 単一 SO で OscReceiverAdapterBinding + ArKitOscAdapterBinding 同時 round-trip
        // ============================================================

        [Test]
        public void RoundTrip_SingleSOWithBothOscAndArKitBindings_PreservesConcreteTypesAndFields()
        {
            // task 9.3 観測可能完了条件:
            // 単一 SO で OscReceiverAdapterBinding + ArKitOscAdapterBinding が同時に保持・round-trip できる。
            // OscReceiverAdapterBinding は受信ポートを本体に、上級設定を OscReceiverRuntimeSettingsSO sub-asset に保持する。
            var so = ScriptableObject.CreateInstance<OscArKitRoundTripTestProfileSO>();

            var oscSettings = ScriptableObject.CreateInstance<OscReceiverRuntimeSettingsSO>();
            oscSettings.name = "OscReceiverRuntimeSettings";
            oscSettings.FromJson("{\"stalenessSeconds\":0.5}");

            var osc = new OscReceiverAdapterBinding { Slug = "osc-vrchat", Port = 9001, AdvancedSettings = oscSettings };
            so.WritableAdapterBindings.Add(osc);

            string[] arkitNames = { "jawOpen", "eyeBlinkLeft", "eyeBlinkRight" };
            var arkit = new ArKitOscAdapterBinding { Slug = "arkit-perfect-sync" };
            arkit.Configure(
                endpoint: "127.0.0.1",
                port: 39539,
                arkitParameterNames: arkitNames,
                stalenessSeconds: 0.25f);
            so.WritableAdapterBindings.Add(arkit);

            AssetDatabase.CreateAsset(so, _assetPath);
            AssetDatabase.AddObjectToAsset(oscSettings, so);
            AssetDatabase.SaveAssets();
            Resources.UnloadAsset(so);

            var loaded = AssetDatabase.LoadAssetAtPath<OscArKitRoundTripTestProfileSO>(_assetPath);

            Assert.That(loaded, Is.Not.Null, "SO が disk から再読み込みできるべき。");
            Assert.That(loaded.AdapterBindings, Is.Not.Null);
            Assert.That(loaded.AdapterBindings.Count, Is.EqualTo(2),
                "Osc + ArKit の 2 binding が round-trip するべき。");

            Assert.That(loaded.AdapterBindings[0], Is.InstanceOf<OscReceiverAdapterBinding>(),
                "Index 0 は OscReceiverAdapterBinding の concrete type を保持するべき。");
            Assert.That(loaded.AdapterBindings[1], Is.InstanceOf<ArKitOscAdapterBinding>(),
                "Index 1 は ArKitOscAdapterBinding の concrete type を保持するべき。");

            var loadedOsc = (OscReceiverAdapterBinding)loaded.AdapterBindings[0];
            Assert.That(loadedOsc.Slug, Is.EqualTo("osc-vrchat"));
            Assert.That(loadedOsc.AdvancedSettings, Is.Not.Null,
                "OscReceiverRuntimeSettingsSO sub-asset 参照が round-trip するべき。");
            Assert.That(loadedOsc.Port, Is.EqualTo(9001));
            Assert.That(loadedOsc.StalenessSeconds, Is.EqualTo(0.5f).Within(1e-6f));

            var loadedArKit = (ArKitOscAdapterBinding)loaded.AdapterBindings[1];
            Assert.That(loadedArKit.Slug, Is.EqualTo("arkit-perfect-sync"));
            Assert.That(loadedArKit.Endpoint, Is.EqualTo("127.0.0.1"));
            Assert.That(loadedArKit.Port, Is.EqualTo(39539));
            Assert.That(loadedArKit.StalenessSeconds, Is.EqualTo(0.25f).Within(1e-6f));
            Assert.That(loadedArKit.ArKitParameterNames, Is.Not.Null);
            CollectionAssert.AreEqual(arkitNames, loadedArKit.ArKitParameterNames,
                "ARKit parameter names は順序を保ったまま round-trip するべき。");
        }
    }
}
