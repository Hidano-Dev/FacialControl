using System;
using System.Collections.Generic;
using Hidano.FacialControl.Adapters.Json.Dto;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Tests.EditMode.Adapters.ScriptableObjectTests.AdapterBindings;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
#if FACIALCONTROL_HAS_INPUTSYSTEM_MODULE && FACIALCONTROL_HAS_OSC_MODULE
using Hidano.FacialControl.Adapters.AdapterBindings;
using Hidano.FacialControl.Adapters.AdapterBindings.ARKit;
using Hidano.FacialControl.Adapters.AdapterBindings.InputSystem;
using Hidano.FacialControl.Adapters.RuntimeSettings;
#endif
#if FACIALCONTROL_HAS_LIPSYNC_MODULE
using System.IO;
using System.Linq;
using Hidano.FacialControl.Adapters.Json;
using Hidano.FacialControl.Editor.AutoExport;
using Hidano.FacialControl.Editor.Windows.Routing.Logic;
using Hidano.FacialControl.LipSync.Adapters;
#endif

namespace Hidano.FacialControl.Tests.EditMode.Adapters.ScriptableObjectTests
{
    /// <summary>
    /// <see cref="FacialCharacterProfileSO"/> のアセット永続化 round-trip テスト。
    /// <c>[SerializeReference] _adapterBindings</c> の具象型 identity / field 保存（Mock 型および
    /// InputSystem + OSC + ARKit の実 binding）、<c>_baseExpression</c> の AnimationClip / cachedSnapshot 保存、
    /// 自動配線後の SO から export した profile.json の配線 / slots 保持を検証する。
    /// </summary>
    [TestFixture]
    public class FacialCharacterProfileSOTests
    {
        private const string TempFolderParent = "Assets";
        private const string TempFolderName = "__Temp_FacialCharacterProfileSOTests";
        private static readonly string TempFolderPath = TempFolderParent + "/" + TempFolderName;

        private string _assetPath;

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(TempFolderPath))
            {
                AssetDatabase.CreateFolder(TempFolderParent, TempFolderName);
            }

            _assetPath = TempFolderPath + "/FacialCharacterProfileSO_" + Guid.NewGuid().ToString("N") + ".asset";
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

        #region AdapterBindings（Mock 型の SerializeReference round-trip）

        [Test]
        public void AdapterBindings_TwoConcreteTypes_RoundTripPreservesTypeAndFieldValues()
        {
            // 異なる派生型 2 種を _adapterBindings に追加し、
            // CreateAsset → LoadAssetAtPath → 内容（slug / 各 field）一致を assert する。
            var so = ScriptableObject.CreateInstance<TestFacialCharacterProfileSO>();
            so.WritableAdapterBindings.Add(new MockTriggerAdapterBinding
            {
                Slug = "trigger-1",
                TriggerThreshold = 7,
            });
            so.WritableAdapterBindings.Add(new MockAnalogAdapterBinding
            {
                Slug = "analog-1",
                AnalogScale = 0.5f,
            });

            AssetDatabase.CreateAsset(so, _assetPath);
            AssetDatabase.SaveAssets();
            Resources.UnloadAsset(so);

            var loaded = AssetDatabase.LoadAssetAtPath<TestFacialCharacterProfileSO>(_assetPath);

            Assert.That(loaded, Is.Not.Null, "SO は disk から再読み込みできるはず");
            Assert.That(loaded.AdapterBindings, Is.Not.Null);
            Assert.That(loaded.AdapterBindings.Count, Is.EqualTo(2));
            Assert.That(loaded.AdapterBindings[0], Is.InstanceOf<MockTriggerAdapterBinding>(),
                "Index 0 は MockTriggerAdapterBinding の concrete type を保持するはず");
            Assert.That(loaded.AdapterBindings[1], Is.InstanceOf<MockAnalogAdapterBinding>(),
                "Index 1 は MockAnalogAdapterBinding の concrete type を保持するはず");

            var trigger = (MockTriggerAdapterBinding)loaded.AdapterBindings[0];
            Assert.That(trigger.Slug, Is.EqualTo("trigger-1"));
            Assert.That(trigger.TriggerThreshold, Is.EqualTo(7));

            var analog = (MockAnalogAdapterBinding)loaded.AdapterBindings[1];
            Assert.That(analog.Slug, Is.EqualTo("analog-1"));
            Assert.That(analog.AnalogScale, Is.EqualTo(0.5f).Within(1e-6f));
        }

        [Test]
        public void AdapterBindings_SameTypeMultipleInstances_RoundTripsIndependently()
        {
            // 同型 binding を複数登録できる（OSC × 2 等を想定）。
            var so = ScriptableObject.CreateInstance<TestFacialCharacterProfileSO>();
            so.WritableAdapterBindings.Add(new MockTriggerAdapterBinding { Slug = "first", TriggerThreshold = 1 });
            so.WritableAdapterBindings.Add(new MockTriggerAdapterBinding { Slug = "second", TriggerThreshold = 2 });

            AssetDatabase.CreateAsset(so, _assetPath);
            AssetDatabase.SaveAssets();
            Resources.UnloadAsset(so);

            var loaded = AssetDatabase.LoadAssetAtPath<TestFacialCharacterProfileSO>(_assetPath);

            Assert.That(loaded.AdapterBindings.Count, Is.EqualTo(2));
            var first = (MockTriggerAdapterBinding)loaded.AdapterBindings[0];
            var second = (MockTriggerAdapterBinding)loaded.AdapterBindings[1];
            Assert.That(first.Slug, Is.EqualTo("first"));
            Assert.That(first.TriggerThreshold, Is.EqualTo(1));
            Assert.That(second.Slug, Is.EqualTo("second"));
            Assert.That(second.TriggerThreshold, Is.EqualTo(2));
            Assert.That(ReferenceEquals(first, second), Is.False,
                "同型でも独立した instance として round-trip するはず");
        }

        [Test]
        public void AdapterBindings_EmptyList_RoundTripsAsEmpty()
        {
            // zero 個の binding でも許容され、null ではなく空 collection として復元される。
            var so = ScriptableObject.CreateInstance<TestFacialCharacterProfileSO>();

            AssetDatabase.CreateAsset(so, _assetPath);
            AssetDatabase.SaveAssets();
            Resources.UnloadAsset(so);

            var loaded = AssetDatabase.LoadAssetAtPath<TestFacialCharacterProfileSO>(_assetPath);

            Assert.That(loaded, Is.Not.Null);
            Assert.That(loaded.AdapterBindings, Is.Not.Null,
                "空 list でも null ではなく空 collection として復元されるはず");
            Assert.That(loaded.AdapterBindings.Count, Is.EqualTo(0));
        }

        [Test]
        public void AdapterBindings_NullElementBetweenValidEntries_DoesNotBreakSubsequentLoad()
        {
            // null 要素（型欠落 simulation）を含んでいても asset 全体の load は中断されず、
            // null 要素は null のまま、前後の binding は完全な状態で round-trip する。
            var so = ScriptableObject.CreateInstance<TestFacialCharacterProfileSO>();
            so.WritableAdapterBindings.Add(new MockTriggerAdapterBinding { Slug = "ok-front", TriggerThreshold = 1 });
            so.WritableAdapterBindings.Add(null);
            so.WritableAdapterBindings.Add(new MockAnalogAdapterBinding { Slug = "ok-back", AnalogScale = 1.5f });

            AssetDatabase.CreateAsset(so, _assetPath);
            AssetDatabase.SaveAssets();
            Resources.UnloadAsset(so);

            var loaded = AssetDatabase.LoadAssetAtPath<TestFacialCharacterProfileSO>(_assetPath);

            Assert.That(loaded, Is.Not.Null,
                "null 要素を含んでいても SO 全体の load は失敗しない");
            Assert.That(loaded.AdapterBindings.Count, Is.EqualTo(3));

            Assert.That(loaded.AdapterBindings[0], Is.InstanceOf<MockTriggerAdapterBinding>());
            Assert.That(loaded.AdapterBindings[1], Is.Null,
                "null 要素は null のまま round-trip する");
            Assert.That(loaded.AdapterBindings[2], Is.InstanceOf<MockAnalogAdapterBinding>());

            var front = (MockTriggerAdapterBinding)loaded.AdapterBindings[0];
            Assert.That(front.Slug, Is.EqualTo("ok-front"));
            Assert.That(front.TriggerThreshold, Is.EqualTo(1));

            var back = (MockAnalogAdapterBinding)loaded.AdapterBindings[2];
            Assert.That(back.Slug, Is.EqualTo("ok-back"));
            Assert.That(back.AnalogScale, Is.EqualTo(1.5f).Within(1e-6f));
        }

        #endregion

#if FACIALCONTROL_HAS_INPUTSYSTEM_MODULE && FACIALCONTROL_HAS_OSC_MODULE
        #region AdapterBindings（InputSystem + OSC + ARKit の実 binding round-trip）

        [Test]
        public void AdapterBindings_InputSystemAndOscAndArKit_RoundTripPreservesConcreteTypeIdentity()
        {
            // 単一 SO に 3 種 binding を同時保持できることを round-trip で検証する。
            // OscReceiverAdapterBinding は OscRuntimeSettingsSO sub-asset 経由で環境設定を保持する。
            var so = ScriptableObject.CreateInstance<TestFacialCharacterProfileSO>();

            var input = new InputSystemAdapterBinding
            {
                Slug = "input-system",
                ActionMapName = "Expression",
            };
            so.WritableAdapterBindings.Add(input);

            var oscSettings = ScriptableObject.CreateInstance<OscRuntimeSettingsSO>();
            oscSettings.name = "OscRuntimeSettings";
            oscSettings.FromJson(
                "{\"listenEndpoint\":\"192.168.1.10\",\"listenPort\":39539,\"stalenessSeconds\":0.25}");

            var osc = new OscReceiverAdapterBinding
            {
                Slug = "osc",
                Settings = oscSettings,
            };
            so.WritableAdapterBindings.Add(osc);

            var arkit = new ArKitOscAdapterBinding
            {
                Slug = "arkit",
                Endpoint = "192.168.1.20",
                Port = 39540,
                StalenessSeconds = 0.5f,
                ArKitParameterNames = new[] { "jawOpen", "eyeBlinkLeft", "eyeBlinkRight" },
            };
            so.WritableAdapterBindings.Add(arkit);

            AssetDatabase.CreateAsset(so, _assetPath);
            AssetDatabase.AddObjectToAsset(oscSettings, so);
            AssetDatabase.SaveAssets();
            Resources.UnloadAsset(so);

            var loaded = AssetDatabase.LoadAssetAtPath<TestFacialCharacterProfileSO>(_assetPath);

            Assert.That(loaded, Is.Not.Null, "SO は disk から再読み込みできるはず");
            Assert.That(loaded.AdapterBindings, Is.Not.Null);
            Assert.That(loaded.AdapterBindings.Count, Is.EqualTo(3));

            // 3 種の concrete type identity が維持されていること。
            Assert.That(loaded.AdapterBindings[0], Is.InstanceOf<InputSystemAdapterBinding>(),
                "Index 0 は InputSystemAdapterBinding として round-trip するはず");
            Assert.That(loaded.AdapterBindings[1], Is.InstanceOf<OscReceiverAdapterBinding>(),
                "Index 1 は OscReceiverAdapterBinding として round-trip するはず");
            Assert.That(loaded.AdapterBindings[2], Is.InstanceOf<ArKitOscAdapterBinding>(),
                "Index 2 は ArKitOscAdapterBinding として round-trip するはず");

            var loadedInput = (InputSystemAdapterBinding)loaded.AdapterBindings[0];
            Assert.That(loadedInput.Slug, Is.EqualTo("input-system"));
            Assert.That(loadedInput.ActionMapName, Is.EqualTo("Expression"));

            var loadedOsc = (OscReceiverAdapterBinding)loaded.AdapterBindings[1];
            Assert.That(loadedOsc.Slug, Is.EqualTo("osc"));
            Assert.That(loadedOsc.Settings, Is.Not.Null,
                "OscRuntimeSettingsSO sub-asset 参照が round-trip するべき。");
            Assert.That(loadedOsc.Endpoint, Is.EqualTo("192.168.1.10"));
            Assert.That(loadedOsc.Port, Is.EqualTo(39539));
            Assert.That(loadedOsc.StalenessSeconds, Is.EqualTo(0.25f).Within(1e-6f));

            var loadedArKit = (ArKitOscAdapterBinding)loaded.AdapterBindings[2];
            Assert.That(loadedArKit.Slug, Is.EqualTo("arkit"));
            Assert.That(loadedArKit.Endpoint, Is.EqualTo("192.168.1.20"));
            Assert.That(loadedArKit.Port, Is.EqualTo(39540));
            Assert.That(loadedArKit.StalenessSeconds, Is.EqualTo(0.5f).Within(1e-6f));
            Assert.That(loadedArKit.ArKitParameterNames,
                Is.EqualTo(new[] { "jawOpen", "eyeBlinkLeft", "eyeBlinkRight" }));

            // 3 instance が独立した参照であること（[SerializeReference] が
            // polymorphic instance を共有してしまわないことを確認）。
            Assert.That(ReferenceEquals(loaded.AdapterBindings[0], loaded.AdapterBindings[1]), Is.False);
            Assert.That(ReferenceEquals(loaded.AdapterBindings[1], loaded.AdapterBindings[2]), Is.False);
            Assert.That(ReferenceEquals(loaded.AdapterBindings[0], loaded.AdapterBindings[2]), Is.False);

            // SerializedProperty レイヤでも 3 件の concrete FullTypeName が解決されていることを確認する。
            using (var serialized = new SerializedObject(loaded))
            {
                var listProp = serialized.FindProperty("_adapterBindings");
                Assert.That(listProp, Is.Not.Null, "_adapterBindings は SerializedObject から発見できるはず");
                Assert.That(listProp.arraySize, Is.EqualTo(3));

                var elemInput = listProp.GetArrayElementAtIndex(0);
                Assert.That(elemInput.propertyType, Is.EqualTo(SerializedPropertyType.ManagedReference));
                Assert.That(elemInput.managedReferenceFullTypename,
                    Does.Contain(typeof(InputSystemAdapterBinding).FullName));

                var elemOsc = listProp.GetArrayElementAtIndex(1);
                Assert.That(elemOsc.managedReferenceFullTypename,
                    Does.Contain(typeof(OscReceiverAdapterBinding).FullName));

                var elemArKit = listProp.GetArrayElementAtIndex(2);
                Assert.That(elemArKit.managedReferenceFullTypename,
                    Does.Contain(typeof(ArKitOscAdapterBinding).FullName));
            }
        }

        #endregion
#endif

        #region BaseExpression（AnimationClip / cachedSnapshot の保存）

        [Test]
        public void BaseExpression_Unset_ReturnsEmptyCachedBlendShapeSnapshot()
        {
            var so = ScriptableObject.CreateInstance<TestFacialCharacterProfileSO>();
            try
            {
                BaseExpressionSerializable baseExpression = so.BaseExpression;

                Assert.That(baseExpression, Is.Not.Null);
                Assert.That(baseExpression.cachedSnapshot, Is.Not.Null,
                    "Unset _baseExpression must be exposed as an empty cached snapshot.");
                Assert.That(baseExpression.cachedSnapshot.blendShapes, Is.Not.Null,
                    "Unset _baseExpression must expose an empty blendShapes list, not null.");
                Assert.That(baseExpression.cachedSnapshot.blendShapes, Is.Empty);
            }
            finally
            {
                Object.DestroyImmediate(so);
            }
        }

        [Test]
        public void BaseExpression_NullSerializedField_GetterDoesNotThrow()
        {
            var so = ScriptableObject.CreateInstance<TestFacialCharacterProfileSO>();
            try
            {
                so.OverrideBaseExpressionField(null);

                BaseExpressionSerializable baseExpression = null;
                Assert.DoesNotThrow(() => baseExpression = so.BaseExpression);
                Assert.That(baseExpression, Is.Not.Null);
                Assert.That(baseExpression.cachedSnapshot, Is.Not.Null);
                Assert.That(baseExpression.cachedSnapshot.blendShapes, Is.Not.Null);
            }
            finally
            {
                Object.DestroyImmediate(so);
            }
        }

        [Test]
        public void BaseExpression_AnimationClipAndCachedSnapshot_RoundTripsThroughAssetReload()
        {
            var so = ScriptableObject.CreateInstance<TestFacialCharacterProfileSO>();
            var clip = new AnimationClip { name = "BaseExpression_RoundTripClip" };

            try
            {
                so.BaseExpression.animationClip = clip;
                so.BaseExpression.cachedSnapshot = CreateBaseExpressionSnapshot();

                AssetDatabase.CreateAsset(so, _assetPath);
                AssetDatabase.AddObjectToAsset(clip, so);
                EditorUtility.SetDirty(so);
                EditorUtility.SetDirty(clip);
                AssetDatabase.SaveAssets();
                Resources.UnloadAsset(so);
                Resources.UnloadAsset(clip);

                var loaded = AssetDatabase.LoadAssetAtPath<TestFacialCharacterProfileSO>(_assetPath);
                Assert.That(loaded, Is.Not.Null);

                BaseExpressionSerializable loadedBaseExpression = loaded.BaseExpression;
                AnimationClip loadedClip = loadedBaseExpression.animationClip;
                ExpressionSnapshotDto loadedSnapshot = loadedBaseExpression.cachedSnapshot;

                Assert.That(loadedClip, Is.Not.Null);
                Assert.That(loadedClip.name, Is.EqualTo("BaseExpression_RoundTripClip"));
                Assert.That(loadedSnapshot, Is.Not.Null);
                Assert.That(loadedSnapshot.blendShapes, Has.Count.EqualTo(2));
                AssertBlendShape(loadedSnapshot.blendShapes[0], "Body", "Brow_Angry", 64.5f);
                AssertBlendShape(loadedSnapshot.blendShapes[1], "Face", "Eye_Narrow", 28.25f);

                using (var serialized = new SerializedObject(loaded))
                {
                    var rootProperty = serialized.FindProperty("_baseExpression");
                    Assert.That(rootProperty, Is.Not.Null,
                        "_baseExpression must be serialized at the FacialCharacterProfileSO root.");
                }
            }
            finally
            {
                if (so != null && !EditorUtility.IsPersistent(so))
                {
                    Object.DestroyImmediate(so);
                }

                if (clip != null && !EditorUtility.IsPersistent(clip))
                {
                    Object.DestroyImmediate(clip);
                }
            }
        }

        private static ExpressionSnapshotDto CreateBaseExpressionSnapshot()
        {
            return new ExpressionSnapshotDto
            {
                blendShapes = new List<BlendShapeSnapshotDto>
                {
                    new BlendShapeSnapshotDto
                    {
                        rendererPath = "Body",
                        name = "Brow_Angry",
                        value = 64.5f,
                    },
                    new BlendShapeSnapshotDto
                    {
                        rendererPath = "Face",
                        name = "Eye_Narrow",
                        value = 28.25f,
                    },
                },
            };
        }

        private static void AssertBlendShape(
            BlendShapeSnapshotDto actual,
            string expectedRendererPath,
            string expectedName,
            float expectedValue)
        {
            Assert.That(actual.rendererPath, Is.EqualTo(expectedRendererPath));
            Assert.That(actual.name, Is.EqualTo(expectedName));
            Assert.That(actual.value, Is.EqualTo(expectedValue).Within(1e-6f));
        }

        #endregion

#if FACIALCONTROL_HAS_LIPSYNC_MODULE
        #region 自動配線 → profile.json export の round-trip

        [Test]
        public void AutoWire_OverlayLayer_ExportProfileJsonWithoutManualFix_PreservesWiringAndSlots()
        {
            var profile = ScriptableObject.CreateInstance<FacialCharacterProfileSO>();
            string assetName = "RoutingAutoWireRoundTrip_" + Guid.NewGuid().ToString("N");
            string profilePath = FacialCharacterProfileSO.GetStreamingAssetsProfilePath(assetName);
            string profileDirectory = Path.GetDirectoryName(profilePath);

            try
            {
                profile.name = assetName;
                profile.Layers.Add(new LayerDefinitionSerializable
                {
                    name = "overlay",
                    inputSources =
                    {
                        new InputSourceDeclarationSerializable
                        {
                            id = "lipsync-overlay:a",
                            weight = 0.25f,
                            optionsJson = "{\"keep\":true}",
                        },
                    },
                });

                AddReferencedDefaultOverlays(profile);

                var autoWireService = new AutoWireService();
                var serializedObject = new SerializedObject(profile);
                autoWireService.AutoWire(
                    serializedObject,
                    new ULipSyncAdapterBinding(),
                    profile.Layers.Select(layer => layer.name).ToArray());

                bool exported = FacialCharacterProfileExporter.ExportProfileJson(profile);

                Assert.That(exported, Is.True, "自動配線後の SO は手修正なしで profile.json を出力できる必要があります。");
                Assert.That(File.Exists(profilePath), Is.True, "Exporter は profile.json を生成する必要があります。");

                string json = File.ReadAllText(profilePath);
                ProfileSnapshotDto parsed = new SystemTextJsonParser().ParseProfileSnapshotV2(json);

                Assert.That(parsed.layers, Has.Count.EqualTo(1));
                Assert.That(parsed.layers[0].name, Is.EqualTo("overlay"));
                CollectionAssert.AreEqual(
                    new[]
                    {
                        "lipsync-overlay:a",
                        "lipsync-overlay:i",
                        "lipsync-overlay:u",
                        "lipsync-overlay:e",
                        "lipsync-overlay:o",
                    },
                    parsed.layers[0].inputSources.Select(source => source.id).ToArray());
                Assert.That(parsed.layers[0].inputSources[0].weight, Is.EqualTo(0.25f).Within(1e-6f));
                Assert.That(parsed.layers[0].inputSources.Skip(1).All(source => Mathf.Approximately(source.weight, 1f)), Is.True);
                Assert.That(parsed.layers[0].inputSources[0].optionsJson, Is.EqualTo("{\"keep\":true}"));

                CollectionAssert.AreEqual(
                    new[] { "a", "i", "u", "e", "o" },
                    parsed.slots);
                CollectionAssert.AreEqual(
                    new[] { "a", "i", "u", "e", "o" },
                    parsed.defaultOverlays.Select(overlay => overlay.slot).ToArray());
            }
            finally
            {
                if (!string.IsNullOrEmpty(profileDirectory) && Directory.Exists(profileDirectory))
                {
                    Directory.Delete(profileDirectory, true);
                }

                Object.DestroyImmediate(profile);
            }
        }

        private static void AddReferencedDefaultOverlays(FacialCharacterProfileSO profile)
        {
            profile.DefaultOverlays.Add(new OverlaySlotBindingSerializable { slot = "a", suppress = true });
            profile.DefaultOverlays.Add(new OverlaySlotBindingSerializable { slot = "i", suppress = true });
            profile.DefaultOverlays.Add(new OverlaySlotBindingSerializable { slot = "u", suppress = true });
            profile.DefaultOverlays.Add(new OverlaySlotBindingSerializable { slot = "e", suppress = true });
            profile.DefaultOverlays.Add(new OverlaySlotBindingSerializable { slot = "o", suppress = true });
        }

        #endregion
#endif
    }

    /// <summary>
    /// Round-trip 検証用の Mock <see cref="AdapterBindingBase"/> 派生型（trigger 系）。
    /// <c>[SerializeReference]</c> の polymorphic 復元と field 値保存を確認する目的に限定する。
    /// </summary>
    [Serializable]
    public sealed class MockTriggerAdapterBinding : AdapterBindingBase
    {
        public int TriggerThreshold;
    }

    /// <summary>
    /// Round-trip 検証用の Mock <see cref="AdapterBindingBase"/> 派生型（analog 系）。
    /// </summary>
    [Serializable]
    public sealed class MockAnalogAdapterBinding : AdapterBindingBase
    {
        public float AnalogScale;
    }
}
