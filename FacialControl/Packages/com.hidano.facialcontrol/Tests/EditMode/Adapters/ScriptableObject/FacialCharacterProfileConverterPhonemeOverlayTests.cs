using System;
using System.Collections.Generic;
using Hidano.FacialControl.Adapters.Json;
using Hidano.FacialControl.Adapters.Json.Dto;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Domain.Models;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Hidano.FacialControl.Testing;
using Object = UnityEngine.Object;
using RoundTripProfileSO = Hidano.FacialControl.Tests.EditMode.Adapters.ScriptableObjectTests.AdapterBindings.TestFacialCharacterProfileSO;

namespace Hidano.FacialControl.Tests.EditMode.Adapters.ScriptableObjectTests
{
    [TestFixture]
    [MediumTest]
    public class FacialCharacterProfileConverterPhonemeOverlayTests : SizedTestFixture
    {
        private const string TempFolderParent = "Assets";
        private const string TempFolderName = "__Temp_FacialCharacterProfileSO_PhonemeOverlayRoundTrip";
        private static readonly string TempFolderPath = TempFolderParent + "/" + TempFolderName;

        private string _assetPath;

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(TempFolderPath))
            {
                AssetDatabase.CreateFolder(TempFolderParent, TempFolderName);
            }

            _assetPath = TempFolderPath + "/PhonemeOverlayRoundTrip_" + Guid.NewGuid().ToString("N") + ".asset";
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

        [Test]
        public void SystemTextJsonParser_PhonemeOverlayBindings_RoundTripPreservesSlotSuppressAndSnapshot()
        {
            var parser = new SystemTextJsonParser();
            var profile = BuildDomainProfile();

            var serialized = parser.SerializeProfile(profile);
            var reparsed = parser.ParseProfile(serialized);
            var reserialized = parser.SerializeProfile(reparsed);

            Assert.That(reserialized, Is.EqualTo(serialized));
            StringAssert.Contains(@"""schemaVersion"": ""1.0""", serialized);
            StringAssert.Contains(@"""slot"": ""a""", serialized);
            StringAssert.Contains(@"""slot"": ""i""", serialized);
            StringAssert.Contains(@"""slot"": ""u""", serialized);
            StringAssert.Contains(@"""suppress"": true", serialized);
            StringAssert.Contains(@"""snapshot""", serialized);

            var overlays = reparsed.FindExpressionById("smile").Value.Overlays.Span;
            Assert.That(overlays.Length, Is.EqualTo(3));
            AssertDefaultFallback(overlays[0], PhonemeOverlaySlots.A);
            AssertSuppress(overlays[1], PhonemeOverlaySlots.I);
            AssertSnapshot(overlays[2], PhonemeOverlaySlots.U, "Mouth_U_Overlay", 0.82f);
        }

        [Test]
        public void FacialCharacterProfileConverter_PhonemeOverlayBindings_RoundTripThroughDtoPreservesBindings()
        {
            var parser = new SystemTextJsonParser();
            var profile = BuildDomainProfile();

            ProfileSnapshotDto dto = FacialCharacterProfileConverter.ToProfileSnapshotDto(profile);
            string json = parser.SerializeProfileSnapshot(dto);
            var reparsed = parser.ParseProfile(json);

            Assert.That(dto.schemaVersion, Is.EqualTo(SystemTextJsonParser.SchemaVersionV2));
            Assert.That(dto.slots, Is.EqualTo(new List<string>
            {
                PhonemeOverlaySlots.A,
                PhonemeOverlaySlots.I,
                PhonemeOverlaySlots.U,
                PhonemeOverlaySlots.E,
                PhonemeOverlaySlots.O,
            }));

            var overlayDtos = dto.expressions[0].snapshot.overlays;
            Assert.That(overlayDtos, Has.Count.EqualTo(3));
            Assert.That(overlayDtos[0].slot, Is.EqualTo(PhonemeOverlaySlots.A));
            Assert.That(overlayDtos[0].suppress, Is.False);
            Assert.That(overlayDtos[0].snapshot, Is.Null);
            Assert.That(overlayDtos[1].slot, Is.EqualTo(PhonemeOverlaySlots.I));
            Assert.That(overlayDtos[1].suppress, Is.True);
            Assert.That(overlayDtos[1].snapshot, Is.Null);
            Assert.That(overlayDtos[2].slot, Is.EqualTo(PhonemeOverlaySlots.U));
            Assert.That(overlayDtos[2].suppress, Is.False);
            Assert.That(overlayDtos[2].snapshot, Is.Not.Null);
            Assert.That(overlayDtos[2].snapshot.blendShapes[0].name, Is.EqualTo("Mouth_U_Overlay"));

            var overlays = reparsed.FindExpressionById("smile").Value.Overlays.Span;
            AssertDefaultFallback(overlays[0], PhonemeOverlaySlots.A);
            AssertSuppress(overlays[1], PhonemeOverlaySlots.I);
            AssertSnapshot(overlays[2], PhonemeOverlaySlots.U, "Mouth_U_Overlay", 0.82f);
        }

        [Test]
        public void FacialCharacterProfileSO_PhonemeOverlayBindings_RoundTripThroughAssetReloadPreservesClipSnapshotAndSuppress()
        {
            var so = ScriptableObject.CreateInstance<RoundTripProfileSO>();
            var clip = new AnimationClip { name = "PhonemeOverlay_U_Clip" };

            try
            {
                so.SchemaVersion = SystemTextJsonParser.SchemaVersionV2;
                so.Layers.Add(new LayerDefinitionSerializable
                {
                    name = "emotion",
                    priority = 0,
                    exclusionMode = ExclusionMode.LastWins,
                    inputSources = { new InputSourceDeclarationSerializable { id = "input", weight = 1f } },
                });
                so.RendererPaths.Add("Face");
                AddPhonemeSlots(so);
                so.Expressions.Add(new ExpressionSerializable
                {
                    id = "smile",
                    name = "Smile",
                    layer = "emotion",
                    transitionDuration = 0.2f,
                    overlays = new List<OverlaySlotBindingSerializable>
                    {
                        new OverlaySlotBindingSerializable { slot = PhonemeOverlaySlots.A },
                        new OverlaySlotBindingSerializable
                        {
                            slot = PhonemeOverlaySlots.I,
                            suppress = true,
                        },
                        new OverlaySlotBindingSerializable
                        {
                            slot = PhonemeOverlaySlots.U,
                            animationClip = clip,
                            cachedSnapshot = CreateSnapshotDto("Mouth_U_Overlay", 0.82f),
                        },
                    },
                });

                AssetDatabase.CreateAsset(so, _assetPath);
                AssetDatabase.AddObjectToAsset(clip, so);
                EditorUtility.SetDirty(so);
                EditorUtility.SetDirty(clip);
                AssetDatabase.SaveAssets();
                Resources.UnloadAsset(so);
                Resources.UnloadAsset(clip);

                var loaded = AssetDatabase.LoadAssetAtPath<RoundTripProfileSO>(_assetPath);
                Assert.That(loaded, Is.Not.Null);

                var loadedExpression = loaded.Expressions[0];
                Assert.That(loadedExpression.overlays, Has.Count.EqualTo(3));
                Assert.That(loadedExpression.overlays[0].slot, Is.EqualTo(PhonemeOverlaySlots.A));
                Assert.That(loadedExpression.overlays[0].suppress, Is.False);
                Assert.That(loadedExpression.overlays[1].slot, Is.EqualTo(PhonemeOverlaySlots.I));
                Assert.That(loadedExpression.overlays[1].suppress, Is.True);
                Assert.That(loadedExpression.overlays[2].slot, Is.EqualTo(PhonemeOverlaySlots.U));
                Assert.That(loadedExpression.overlays[2].animationClip, Is.Not.Null);
                Assert.That(loadedExpression.overlays[2].animationClip.name, Is.EqualTo("PhonemeOverlay_U_Clip"));
                Assert.That(loadedExpression.overlays[2].cachedSnapshot, Is.Not.Null);
                Assert.That(loadedExpression.overlays[2].cachedSnapshot.blendShapes, Has.Count.EqualTo(1));
                Assert.That(loadedExpression.overlays[2].cachedSnapshot.blendShapes[0].name, Is.EqualTo("Mouth_U_Overlay"));

                var profile = loaded.BuildFallbackProfile();
                var overlays = profile.FindExpressionById("smile").Value.Overlays.Span;
                AssertDefaultFallback(overlays[0], PhonemeOverlaySlots.A);
                AssertSuppress(overlays[1], PhonemeOverlaySlots.I);
                AssertSnapshot(overlays[2], PhonemeOverlaySlots.U, "Mouth_U_Overlay", 0.82f);
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

        private static FacialProfile BuildDomainProfile()
        {
            var layers = new[]
            {
                new LayerDefinition("emotion", 0, ExclusionMode.LastWins),
            };
            var layerInputSources = new[]
            {
                new[] { new InputSourceDeclaration("input", 1f, null) },
            };
            var expression = new Expression(
                "smile",
                "Smile",
                "emotion",
                transitionDuration: 0.2f,
                transitionCurve: TransitionCurve.Linear,
                blendShapeValues: new[] { new BlendShapeMapping("Smile", 1f, "Face") },
                overlays: new[]
                {
                    new OverlaySlotBinding(PhonemeOverlaySlots.A, suppress: false, snapshot: null),
                    new OverlaySlotBinding(PhonemeOverlaySlots.I, suppress: true, snapshot: null),
                    new OverlaySlotBinding(
                        PhonemeOverlaySlots.U,
                        suppress: false,
                        snapshot: CreateSnapshot("phoneme-u", "Mouth_U_Overlay", 0.82f)),
                });

            return new FacialProfile(
                SystemTextJsonParser.SchemaVersionV2,
                layers,
                new[] { expression },
                rendererPaths: new[] { "Face" },
                layerInputSources: layerInputSources,
                slots: new[]
                {
                    PhonemeOverlaySlots.A,
                    PhonemeOverlaySlots.I,
                    PhonemeOverlaySlots.U,
                    PhonemeOverlaySlots.E,
                    PhonemeOverlaySlots.O,
                });
        }

        private static void AddPhonemeSlots(FacialCharacterProfileSO so)
        {
            var serializedObject = new SerializedObject(so);
            var slots = serializedObject.FindProperty("_slots");
            slots.arraySize = PhonemeOverlaySlots.ReservedNames.Length;

            for (int i = 0; i < PhonemeOverlaySlots.ReservedNames.Length; i++)
            {
                slots.GetArrayElementAtIndex(i).stringValue = PhonemeOverlaySlots.ReservedNames[i];
            }

            serializedObject.ApplyModifiedProperties();
        }

        private static ExpressionSnapshot CreateSnapshot(string id, string blendShapeName, float value)
        {
            return new ExpressionSnapshot(
                id,
                transitionDuration: 0.1f,
                transitionCurvePreset: TransitionCurvePreset.Linear,
                blendShapes: new[]
                {
                    new BlendShapeSnapshot("Face", blendShapeName, value),
                },
                bones: null,
                rendererPaths: new[] { "Face" });
        }

        private static OverlaySnapshotDto CreateSnapshotDto(string blendShapeName, float value)
        {
            return new OverlaySnapshotDto
            {
                transitionDuration = 0.1f,
                transitionCurvePreset = "Linear",
                rendererPaths = new List<string> { "Face" },
                blendShapes = new List<BlendShapeSnapshotDto>
                {
                    new BlendShapeSnapshotDto
                    {
                        rendererPath = "Face",
                        name = blendShapeName,
                        value = value,
                    },
                },
            };
        }

        private static void AssertDefaultFallback(OverlaySlotBinding binding, string slot)
        {
            Assert.That(binding.Slot, Is.EqualTo(slot));
            Assert.That(binding.Suppress, Is.False);
            Assert.That(binding.Snapshot.HasValue, Is.False);
            Assert.That(binding.IsDefaultFallback, Is.True);
        }

        private static void AssertSuppress(OverlaySlotBinding binding, string slot)
        {
            Assert.That(binding.Slot, Is.EqualTo(slot));
            Assert.That(binding.Suppress, Is.True);
            Assert.That(binding.Snapshot.HasValue, Is.False);
        }

        private static void AssertSnapshot(
            OverlaySlotBinding binding,
            string slot,
            string expectedBlendShapeName,
            float expectedValue)
        {
            Assert.That(binding.Slot, Is.EqualTo(slot));
            Assert.That(binding.Suppress, Is.False);
            Assert.That(binding.Snapshot.HasValue, Is.True);
            Assert.That(binding.Snapshot.Value.BlendShapes.Length, Is.EqualTo(1));
            Assert.That(binding.Snapshot.Value.BlendShapes.Span[0].RendererPath, Is.EqualTo("Face"));
            Assert.That(binding.Snapshot.Value.BlendShapes.Span[0].Name, Is.EqualTo(expectedBlendShapeName));
            Assert.That(binding.Snapshot.Value.BlendShapes.Span[0].Value, Is.EqualTo(expectedValue).Within(1e-6f));
        }
    }
}
