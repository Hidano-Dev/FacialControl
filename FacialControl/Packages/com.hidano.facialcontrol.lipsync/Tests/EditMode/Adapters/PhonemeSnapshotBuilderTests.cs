using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;
using Hidano.FacialControl.LipSync.Adapters;
using Hidano.FacialControl.LipSync.Adapters.PhonemeEntries;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.LipSync.Tests.EditMode.Adapters
{
    [TestFixture]
    [MediumTest]
    public class PhonemeSnapshotBuilderTests : SizedTestFixture
    {
        private const string TempFolderParent = "Assets";
        private const string TempFolderName = "__Temp_ULipSyncPhonemeSnapshotBuilderTests";
        private static readonly string TempFolderPath = TempFolderParent + "/" + TempFolderName;

        private readonly List<UnityEngine.Object> _createdObjects =
            new List<UnityEngine.Object>();

        private readonly List<string> _createdAssetPaths = new List<string>();

        [TearDown]
        public void TearDown()
        {
            for (int i = _createdAssetPaths.Count - 1; i >= 0; i--)
            {
                AssetDatabase.DeleteAsset(_createdAssetPaths[i]);
            }

            _createdAssetPaths.Clear();

            if (AssetDatabase.IsValidFolder(TempFolderPath))
            {
                var remaining = AssetDatabase.FindAssets(string.Empty, new[] { TempFolderPath });
                if (remaining == null || remaining.Length == 0)
                {
                    AssetDatabase.DeleteAsset(TempFolderPath);
                }
            }

            for (int i = _createdObjects.Count - 1; i >= 0; i--)
            {
                if (_createdObjects[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(_createdObjects[i]);
                }
            }

            _createdObjects.Clear();
        }

        [Test]
        public void Build_BlendShapeEntryDirectFill_FillsCorrectIndex()
        {
            GameObject host = CreateHost();
            AddRenderer(host, "FaceMesh", "Mouth_A", "Mouth_I");
            ULipSyncAdapterBinding binding = CreateBinding(
                new BlendShapePhonemeEntry
                {
                    PhonemeId = "A",
                    BlendShapeName = "Mouth_A",
                    MaxWeight = 80f,
                });

            PhonemeSnapshot[] snapshots = BuildSnapshots(
                binding,
                CreateContext(host, "Mouth_A", "Mouth_I"));

            Assert.That(snapshots, Has.Length.EqualTo(1));
            Assert.That(snapshots[0].PhonemeId, Is.EqualTo("A"));
            AssertWeights(snapshots[0].Weights, 0.8f, 0f);
        }

        [Test]
        public void Build_AnimationClipEntryTimeZero_ExtractsBlendShapeWeights()
        {
            GameObject host = CreateHost();
            SkinnedMeshRenderer renderer = AddRenderer(host, "FaceMesh", "Mouth_A", "Mouth_I");
            renderer.SetBlendShapeWeight(0, 13f);
            renderer.SetBlendShapeWeight(1, 33f);

            AnimationClip clip = CreateClip(
                Curve("FaceMesh", "Mouth_A", 60f));
            ULipSyncAdapterBinding binding = CreateBinding(
                new AnimationClipPhonemeEntry
                {
                    PhonemeId = "A",
                    Clip = clip,
                    MaxWeight = 50f,
                });

            PhonemeSnapshot[] snapshots = BuildSnapshots(
                binding,
                CreateContext(host, "Mouth_A", "Mouth_I"));

            Assert.That(snapshots, Has.Length.EqualTo(1));
            AssertWeights(snapshots[0].Weights, 0.3f, 0f);
        }

        [Test]
        public void Build_MixedEntries_BothApplyConsistently()
        {
            GameObject host = CreateHost();
            AddRenderer(host, "FaceMesh", "Mouth_A", "Mouth_I");

            AnimationClip clip = CreateClip(
                Curve("FaceMesh", "Mouth_I", 40f));
            ULipSyncAdapterBinding binding = CreateBinding(
                new BlendShapePhonemeEntry
                {
                    PhonemeId = "A",
                    BlendShapeName = "Mouth_A",
                    MaxWeight = 100f,
                },
                new AnimationClipPhonemeEntry
                {
                    PhonemeId = "I",
                    Clip = clip,
                    MaxWeight = 25f,
                });

            PhonemeSnapshot[] snapshots = BuildSnapshots(
                binding,
                CreateContext(host, "Mouth_A", "Mouth_I"));

            Assert.That(snapshots, Has.Length.EqualTo(2));
            Assert.That(snapshots[0].PhonemeId, Is.EqualTo("A"));
            AssertWeights(snapshots[0].Weights, 1f, 0f);
            Assert.That(snapshots[1].PhonemeId, Is.EqualTo("I"));
            AssertWeights(snapshots[1].Weights, 0f, 0.1f);
        }

        [Test]
        public void Build_UnresolvedBlendShapeName_LogsWarningAndSkips()
        {
            GameObject host = CreateHost();
            AddRenderer(host, "FaceMesh", "Mouth_A");
            ULipSyncAdapterBinding binding = CreateBinding(
                new BlendShapePhonemeEntry
                {
                    PhonemeId = "A",
                    BlendShapeName = "MissingBlendShape",
                    MaxWeight = 100f,
                });

            LogAssert.Expect(
                LogType.Warning,
                new Regex("ULipSyncAdapterBinding.*MissingBlendShape"));

            PhonemeSnapshot[] snapshots = BuildSnapshots(binding, CreateContext(host, "Mouth_A"));

            Assert.That(snapshots, Is.Empty);
        }

        [Test]
        public void BuildSnapshots_WithExpressionPhonemeEntry_ResolvesFromProfile()
        {
            GameObject host = CreateHost();
            SkinnedMeshRenderer renderer = AddRenderer(host, "FaceMesh", "Mouth_A", "Mouth_I", "Unused");
            renderer.SetBlendShapeWeight(0, 45f);
            renderer.SetBlendShapeWeight(1, 55f);
            renderer.SetBlendShapeWeight(2, 65f);

            var entry = new ExpressionPhonemeEntry
            {
                PhonemeId = "A",
                MaxWeight = 50f,
            };
            SetPrivateField(entry, "_expressionId", "expr-a");
            ULipSyncAdapterBinding binding = CreateBinding(
                entry);
            var profile = new FacialProfile(
                "1.0",
                expressions: new[]
                {
                    new Expression(
                        "expr-a",
                        "A",
                        "lipsync",
                        blendShapeValues: new[]
                        {
                            new BlendShapeMapping("Mouth_A", 0.8f),
                            new BlendShapeMapping("Mouth_I", 0.25f),
                            new BlendShapeMapping("MissingBlendShape", 1f),
                        }),
                });

            PhonemeSnapshot[] snapshots = BuildSnapshots(
                binding,
                CreateContext(host, profile, "Mouth_A", "Mouth_I", "Unused"));

            Assert.That(snapshots, Has.Length.EqualTo(1));
            Assert.That(snapshots[0].PhonemeId, Is.EqualTo("A"));
            AssertWeights(snapshots[0].Weights, 0.4f, 0.125f, 0f);
            Assert.That(renderer.GetBlendShapeWeight(0), Is.EqualTo(45f).Within(1e-6f));
            Assert.That(renderer.GetBlendShapeWeight(1), Is.EqualTo(55f).Within(1e-6f));
            Assert.That(renderer.GetBlendShapeWeight(2), Is.EqualTo(65f).Within(1e-6f));
        }

        [Test]
        public void BuildSnapshots_WithEmptyAiueoExpressionId_HeuristicAutoLinksWithoutPersisting()
        {
            GameObject host = CreateHost();
            AddRenderer(host, "FaceMesh", "Mouth_A", "Mouth_I");
            ExpressionPhonemeEntry entry = CreateExpressionEntry("A", string.Empty);
            ULipSyncAdapterBinding binding = CreateBinding(entry);
            var profile = new FacialProfile(
                "1.0",
                expressions: new[]
                {
                    new Expression(
                        "A",
                        "\u3042",
                        "lipsync",
                        blendShapeValues: new[]
                        {
                            new BlendShapeMapping("Mouth_A", 0.8f),
                            new BlendShapeMapping("Mouth_I", 0.25f),
                        }),
                });

            PhonemeSnapshot[] snapshots = BuildSnapshots(
                binding,
                CreateContext(host, profile, "Mouth_A", "Mouth_I"));

            Assert.That(snapshots, Has.Length.EqualTo(1));
            Assert.That(snapshots[0].PhonemeId, Is.EqualTo("A"));
            AssertWeights(snapshots[0].Weights, 0.8f, 0.25f);
            Assert.That(entry.ExpressionId, Is.Empty);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void BuildSnapshots_WithEmptyAiueoExpressionIdAndNoHeuristicMatch_LogsWarningAndSkips()
        {
            GameObject host = CreateHost();
            AddRenderer(host, "FaceMesh", "Mouth_A");
            ExpressionPhonemeEntry entry = CreateExpressionEntry("A", string.Empty);
            ULipSyncAdapterBinding binding = CreateBinding(entry);
            var profile = new FacialProfile(
                "1.0",
                expressions: new[]
                {
                    new Expression(
                        "\u3042",
                        "\u3042",
                        "lipsync",
                        blendShapeValues: new[]
                        {
                            new BlendShapeMapping("Mouth_A", 1f),
                        }),
                });

            LogAssert.Expect(
                LogType.Warning,
                new Regex("ULipSyncAdapterBinding.*PhonemeId='A'"));

            PhonemeSnapshot[] snapshots = BuildSnapshots(
                binding,
                CreateContext(host, profile, "Mouth_A"));

            Assert.That(snapshots, Is.Empty);
            Assert.That(entry.ExpressionId, Is.Empty);
        }

        [Test]
        public void BuildSnapshots_WithExpressionPhonemeEntryEmptyExpressionId_LogsWarningSkipsAndContinues()
        {
            GameObject host = CreateHost();
            AddRenderer(host, "FaceMesh", "Mouth_A", "Mouth_I");
            ULipSyncAdapterBinding binding = CreateBinding(
                CreateExpressionEntry("A", string.Empty),
                new BlendShapePhonemeEntry
                {
                    PhonemeId = "I",
                    BlendShapeName = "Mouth_I",
                    MaxWeight = 75f,
                });

            LogAssert.Expect(
                LogType.Warning,
                new Regex("ULipSyncAdapterBinding.*PhonemeId='A'"));

            PhonemeSnapshot[] snapshots = BuildSnapshots(
                binding,
                CreateContext(host, "Mouth_A", "Mouth_I"));

            Assert.That(snapshots, Has.Length.EqualTo(1));
            Assert.That(snapshots[0].PhonemeId, Is.EqualTo("I"));
            AssertWeights(snapshots[0].Weights, 0f, 0.75f);
        }

        [Test]
        public void BuildSnapshots_WithExpressionPhonemeEntryMissingExpression_LogsWarningSkipsAndContinues()
        {
            GameObject host = CreateHost();
            AddRenderer(host, "FaceMesh", "Mouth_A", "Mouth_I");
            ULipSyncAdapterBinding binding = CreateBinding(
                CreateExpressionEntry("A", "missing-expression"),
                new BlendShapePhonemeEntry
                {
                    PhonemeId = "I",
                    BlendShapeName = "Mouth_I",
                    MaxWeight = 75f,
                });

            LogAssert.Expect(
                LogType.Warning,
                new Regex("ULipSyncAdapterBinding.*ExpressionId='missing-expression'"));

            PhonemeSnapshot[] snapshots = BuildSnapshots(
                binding,
                CreateContext(host, "Mouth_A", "Mouth_I"));

            Assert.That(snapshots, Has.Length.EqualTo(1));
            Assert.That(snapshots[0].PhonemeId, Is.EqualTo("I"));
            AssertWeights(snapshots[0].Weights, 0f, 0.75f);
        }

        [Test]
        public void BuildSnapshots_WithExpressionPhonemeEntryEmptyBlendShapeValues_LogsWarningSkipsAndContinues()
        {
            GameObject host = CreateHost();
            AddRenderer(host, "FaceMesh", "Mouth_A", "Mouth_I");
            ULipSyncAdapterBinding binding = CreateBinding(
                CreateExpressionEntry("A", "expr-empty"),
                new BlendShapePhonemeEntry
                {
                    PhonemeId = "I",
                    BlendShapeName = "Mouth_I",
                    MaxWeight = 75f,
                });
            var profile = new FacialProfile(
                "1.0",
                expressions: new[]
                {
                    new Expression("expr-empty", "A", "lipsync", blendShapeValues: Array.Empty<BlendShapeMapping>()),
                });

            LogAssert.Expect(
                LogType.Warning,
                new Regex("ULipSyncAdapterBinding.*ExpressionId='expr-empty'"));

            PhonemeSnapshot[] snapshots = BuildSnapshots(
                binding,
                CreateContext(host, profile, "Mouth_A", "Mouth_I"));

            Assert.That(snapshots, Has.Length.EqualTo(1));
            Assert.That(snapshots[0].PhonemeId, Is.EqualTo("I"));
            AssertWeights(snapshots[0].Weights, 0f, 0.75f);
        }

        [Test]
        public void BuildSnapshots_WithEmptyAiueoExpressionIdAndNameMatch_HeuristicAutoLinksByExpressionName()
        {
            // Expression の Id ではなく Name が音素 id と一致する場合もヒューリスティックで自動リンクされる。
            GameObject host = CreateHost();
            AddRenderer(host, "FaceMesh", "Mouth_A", "Mouth_I");
            ExpressionPhonemeEntry entry = CreateExpressionEntry("A", string.Empty);
            ULipSyncAdapterBinding binding = CreateBinding(entry);
            var profile = new FacialProfile(
                "1.0",
                expressions: new[]
                {
                    new Expression(
                        "expr-a",
                        "A",
                        "lipsync",
                        blendShapeValues: new[]
                        {
                            new BlendShapeMapping("Mouth_A", 0.6f),
                            new BlendShapeMapping("Mouth_I", 0.4f),
                        }),
                });

            PhonemeSnapshot[] snapshots = BuildSnapshots(
                binding,
                CreateContext(host, profile, "Mouth_A", "Mouth_I"));

            Assert.That(snapshots, Has.Length.EqualTo(1));
            Assert.That(snapshots[0].PhonemeId, Is.EqualTo("A"));
            AssertWeights(snapshots[0].Weights, 0.6f, 0.4f);
            Assert.That(entry.ExpressionId, Is.Empty);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void BuildSnapshots_AnimationClipFallback_WhenSampleAllZeroAndPhonemeMatchExpression()
        {
            GameObject host = CreateHost();
            SkinnedMeshRenderer renderer = AddRenderer(host, "FaceMesh", "Mouth_A", "Mouth_I");
            renderer.SetBlendShapeWeight(0, 12f);
            renderer.SetBlendShapeWeight(1, 34f);

            AnimationClip clip = CreateClip();
            clip.name = "LipSync_A";
            ULipSyncAdapterBinding binding = CreateBinding(
                new AnimationClipPhonemeEntry
                {
                    PhonemeId = "A",
                    Clip = clip,
                    MaxWeight = 50f,
                });
            var profile = new FacialProfile(
                "1.0",
                expressions: new[]
                {
                    new Expression(
                        "A",
                        "\u3042",
                        "lipsync",
                        blendShapeValues: new[]
                        {
                            new BlendShapeMapping("Mouth_A", 0.8f),
                            new BlendShapeMapping("Mouth_I", 0.25f),
                        }),
                });

            LogAssert.Expect(
                LogType.Warning,
                new Regex("ULipSyncAdapterBinding.*'LipSync_A'"));

            PhonemeSnapshot[] snapshots = BuildSnapshots(
                binding,
                CreateContext(host, profile, "Mouth_A", "Mouth_I"));

            Assert.That(snapshots, Has.Length.EqualTo(1));
            Assert.That(snapshots[0].PhonemeId, Is.EqualTo("A"));
            AssertWeights(snapshots[0].Weights, 0.4f, 0.125f);
            Assert.That(renderer.GetBlendShapeWeight(0), Is.EqualTo(12f).Within(1e-6f));
            Assert.That(renderer.GetBlendShapeWeight(1), Is.EqualTo(34f).Within(1e-6f));
        }

        [Test]
        public void BuildSnapshots_AnimationClipFallback_WhenSampleAllZeroAndNoMatch_PreservesExistingWarning()
        {
            GameObject host = CreateHost();
            AddRenderer(host, "FaceMesh", "Mouth_A");

            AnimationClip clip = CreateClip();
            clip.name = "LipSync_A";
            ULipSyncAdapterBinding binding = CreateBinding(
                new AnimationClipPhonemeEntry
                {
                    PhonemeId = "A",
                    Clip = clip,
                    MaxWeight = 100f,
                });
            var profile = new FacialProfile(
                "1.0",
                expressions: new[]
                {
                    new Expression(
                        "\u3042",
                        "\u3042",
                        "lipsync",
                        blendShapeValues: new[]
                        {
                            new BlendShapeMapping("Mouth_A", 1f),
                        }),
                });

            LogAssert.Expect(
                LogType.Warning,
                new Regex("ULipSyncAdapterBinding.*'LipSync_A'"));

            PhonemeSnapshot[] snapshots = BuildSnapshots(
                binding,
                CreateContext(host, profile, "Mouth_A"));

            Assert.That(snapshots, Is.Empty);
        }

        [Test]
        public void BuildSnapshots_AnimationClipNonZeroWithMatchingExpression_UsesSampledClipWeights()
        {
            GameObject host = CreateHost();
            SkinnedMeshRenderer renderer = AddRenderer(host, "FaceMesh", "Mouth_A", "Mouth_I");
            renderer.SetBlendShapeWeight(0, 17f);
            renderer.SetBlendShapeWeight(1, 29f);

            AnimationClip clip = CreateClip(
                Curve("FaceMesh", "Mouth_A", 60f));
            clip.name = "LipSync_A";
            ULipSyncAdapterBinding binding = CreateBinding(
                new AnimationClipPhonemeEntry
                {
                    PhonemeId = "A",
                    Clip = clip,
                    MaxWeight = 50f,
                });
            var profile = new FacialProfile(
                "1.0",
                expressions: new[]
                {
                    new Expression(
                        "A",
                        "A",
                        "lipsync",
                        blendShapeValues: new[]
                        {
                            new BlendShapeMapping("Mouth_A", 0.1f),
                            new BlendShapeMapping("Mouth_I", 1f),
                        }),
                });

            PhonemeSnapshot[] snapshots = BuildSnapshots(
                binding,
                CreateContext(host, profile, "Mouth_A", "Mouth_I"));

            Assert.That(snapshots, Has.Length.EqualTo(1));
            Assert.That(snapshots[0].PhonemeId, Is.EqualTo("A"));
            AssertWeights(snapshots[0].Weights, 0.3f, 0f);
            Assert.That(renderer.GetBlendShapeWeight(0), Is.EqualTo(17f).Within(1e-6f));
            Assert.That(renderer.GetBlendShapeWeight(1), Is.EqualTo(29f).Within(1e-6f));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Build_AfterAnimationClipSampling_RestoresSmrWeights()
        {
            GameObject host = CreateHost();
            SkinnedMeshRenderer face = AddRenderer(host, "FaceMesh", "Mouth_A");
            SkinnedMeshRenderer accessory = AddRenderer(host, "AccessoryMesh", "AccessorySmile");
            face.SetBlendShapeWeight(0, 13f);
            accessory.SetBlendShapeWeight(0, 45f);

            AnimationClip clip = CreateClip(
                Curve("FaceMesh", "Mouth_A", 80f),
                Curve("AccessoryMesh", "AccessorySmile", 90f));
            ULipSyncAdapterBinding binding = CreateBinding(
                new AnimationClipPhonemeEntry
                {
                    PhonemeId = "A",
                    Clip = clip,
                    MaxWeight = 100f,
                });

            PhonemeSnapshot[] snapshots = BuildSnapshots(binding, CreateContext(host, "Mouth_A"));

            Assert.That(snapshots, Has.Length.EqualTo(1));
            AssertWeights(snapshots[0].Weights, 0.8f);
            Assert.That(face.GetBlendShapeWeight(0), Is.EqualTo(13f).Within(1e-6f));
            Assert.That(accessory.GetBlendShapeWeight(0), Is.EqualTo(45f).Within(1e-6f));
        }

        [Test]
        public void Build_TargetMeshHintMissing_FallsBackToFirstSmr()
        {
            GameObject host = CreateHost();
            AddRenderer(host, "FirstMesh", "Mouth_A");
            AddRenderer(host, "SecondMesh", "Mouth_A");

            AnimationClip clip = CreateClip(
                Curve("FirstMesh", "Mouth_A", 20f),
                Curve("SecondMesh", "Mouth_A", 80f));
            ULipSyncAdapterBinding binding = CreateBinding(
                new AnimationClipPhonemeEntry
                {
                    PhonemeId = "A",
                    Clip = clip,
                    MaxWeight = 100f,
                });
            SetPrivateField(binding, "_targetMeshHint", "MissingMesh");

            LogAssert.Expect(
                LogType.Warning,
                new Regex("ULipSyncAdapterBinding.*'MissingMesh'"));

            PhonemeSnapshot[] snapshots = BuildSnapshots(binding, CreateContext(host, "Mouth_A"));

            Assert.That(snapshots, Has.Length.EqualTo(1));
            AssertWeights(snapshots[0].Weights, 0.2f);
        }

        [Test]
        public void BackwardCompatibility_BlendShapeSerializeReferenceFixture_LoadsAndBuildsExpectedSnapshots()
        {
            GameObject host = CreateHost();
            AddRenderer(host, "FaceMesh", "Mouth_A", "Mouth_I");
            var entries = new PhonemeEntryBase[]
            {
                new BlendShapePhonemeEntry
                {
                    PhonemeId = "A",
                    BlendShapeName = "Mouth_A",
                    MaxWeight = 87.5f,
                },
            };

            PhonemeEntryFixtureAsset fixture = SaveAndReloadFixture("BlendShapeOnly", entries);
            Assert.That(fixture.Entries, Has.Count.EqualTo(1));
            Assert.That(fixture.Entries[0], Is.InstanceOf<BlendShapePhonemeEntry>());
            AssertCommonFieldsAreBackwardCompatible(fixture.Entries[0], "A", 87.5f);
            Assert.That(((BlendShapePhonemeEntry)fixture.Entries[0]).BlendShapeName, Is.EqualTo("Mouth_A"));
            AssertSerializedEntryTypes(
                fixture,
                typeof(BlendShapePhonemeEntry));

            PhonemeSnapshot[] snapshots = BuildSnapshots(
                CreateBinding(fixture.Entries.ToArray()),
                CreateContext(host, "Mouth_A", "Mouth_I"));

            AssertSnapshotsBitExact(
                snapshots,
                ExpectedSnapshot("A", 0.875f, 0f));
        }

        [Test]
        public void BackwardCompatibility_AnimationClipSerializeReferenceFixture_LoadsAndBuildsExpectedSnapshots()
        {
            GameObject host = CreateHost();
            SkinnedMeshRenderer renderer = AddRenderer(host, "FaceMesh", "Mouth_A", "Mouth_I");
            renderer.SetBlendShapeWeight(0, 21f);
            renderer.SetBlendShapeWeight(1, 42f);
            AnimationClip clip = CreateAssetClip("LegacyClip_I", Curve("FaceMesh", "Mouth_I", 66f));
            var entries = new PhonemeEntryBase[]
            {
                new AnimationClipPhonemeEntry
                {
                    PhonemeId = "I",
                    Clip = clip,
                    MaxWeight = 50f,
                },
            };

            PhonemeEntryFixtureAsset fixture = SaveAndReloadFixture("AnimationClipOnly", entries, clip);
            Assert.That(fixture.Entries, Has.Count.EqualTo(1));
            Assert.That(fixture.Entries[0], Is.InstanceOf<AnimationClipPhonemeEntry>());
            AssertCommonFieldsAreBackwardCompatible(fixture.Entries[0], "I", 50f);
            Assert.That(((AnimationClipPhonemeEntry)fixture.Entries[0]).Clip, Is.Not.Null);
            AssertSerializedEntryTypes(
                fixture,
                typeof(AnimationClipPhonemeEntry));

            PhonemeSnapshot[] snapshots = BuildSnapshots(
                CreateBinding(fixture.Entries.ToArray()),
                CreateContext(host, "Mouth_A", "Mouth_I"));

            AssertSnapshotsBitExact(
                snapshots,
                ExpectedSnapshot("I", 0f, 0.33f));
            Assert.That(renderer.GetBlendShapeWeight(0), Is.EqualTo(21f).Within(1e-6f));
            Assert.That(renderer.GetBlendShapeWeight(1), Is.EqualTo(42f).Within(1e-6f));
        }

        [Test]
        public void BackwardCompatibility_MixedSerializeReferenceFixture_LoadsAndBuildsExpectedSnapshots()
        {
            GameObject host = CreateHost();
            AddRenderer(host, "FaceMesh", "Mouth_A", "Mouth_I", "Mouth_U");
            AnimationClip clip = CreateAssetClip("LegacyClip_U", Curve("FaceMesh", "Mouth_U", 25f));
            var entries = new PhonemeEntryBase[]
            {
                new BlendShapePhonemeEntry
                {
                    PhonemeId = "A",
                    BlendShapeName = "Mouth_A",
                    MaxWeight = 100f,
                },
                new AnimationClipPhonemeEntry
                {
                    PhonemeId = "U",
                    Clip = clip,
                    MaxWeight = 80f,
                },
            };

            PhonemeEntryFixtureAsset fixture = SaveAndReloadFixture("Mixed", entries, clip);
            Assert.That(fixture.Entries, Has.Count.EqualTo(2));
            Assert.That(fixture.Entries[0], Is.InstanceOf<BlendShapePhonemeEntry>());
            Assert.That(fixture.Entries[1], Is.InstanceOf<AnimationClipPhonemeEntry>());
            AssertCommonFieldsAreBackwardCompatible(fixture.Entries[0], "A", 100f);
            AssertCommonFieldsAreBackwardCompatible(fixture.Entries[1], "U", 80f);
            AssertSerializedEntryTypes(
                fixture,
                typeof(BlendShapePhonemeEntry),
                typeof(AnimationClipPhonemeEntry));

            PhonemeSnapshot[] snapshots = BuildSnapshots(
                CreateBinding(fixture.Entries.ToArray()),
                CreateContext(host, "Mouth_A", "Mouth_I", "Mouth_U"));

            AssertSnapshotsBitExact(
                snapshots,
                ExpectedSnapshot("A", 1f, 0f, 0f),
                ExpectedSnapshot("U", 0f, 0f, 0.2f));
        }

        private GameObject CreateHost()
        {
            var host = new GameObject("PhonemeSnapshotBuilderTestsHost");
            _createdObjects.Add(host);
            return host;
        }

        private SkinnedMeshRenderer AddRenderer(
            GameObject host,
            string childName,
            params string[] blendShapeNames)
        {
            var child = new GameObject(childName);
            child.transform.SetParent(host.transform, false);
            var renderer = child.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = CreateMesh(childName + "Mesh", blendShapeNames);
            return renderer;
        }

        private Mesh CreateMesh(string name, params string[] blendShapeNames)
        {
            var mesh = new Mesh { name = name };
            _createdObjects.Add(mesh);
            mesh.vertices = new[]
            {
                Vector3.zero,
                Vector3.right,
                Vector3.up,
            };
            mesh.triangles = new[] { 0, 1, 2 };

            var deltaVertices = new[]
            {
                Vector3.up * 0.01f,
                Vector3.up * 0.01f,
                Vector3.up * 0.01f,
            };
            var deltaNormals = new Vector3[deltaVertices.Length];
            var deltaTangents = new Vector3[deltaVertices.Length];
            for (int i = 0; i < blendShapeNames.Length; i++)
            {
                mesh.AddBlendShapeFrame(
                    blendShapeNames[i],
                    100f,
                    deltaVertices,
                    deltaNormals,
                    deltaTangents);
            }

            return mesh;
        }

        private AnimationClip CreateClip(params ClipCurve[] curves)
        {
            var clip = new AnimationClip();
            _createdObjects.Add(clip);
            for (int i = 0; i < curves.Length; i++)
            {
                clip.SetCurve(
                    curves[i].RelativePath,
                    typeof(SkinnedMeshRenderer),
                    "blendShape." + curves[i].BlendShapeName,
                    new AnimationCurve(new Keyframe(0f, curves[i].Weight)));
            }

            return clip;
        }

        private AnimationClip CreateAssetClip(string name, params ClipCurve[] curves)
        {
            var clip = new AnimationClip { name = name };
            for (int i = 0; i < curves.Length; i++)
            {
                clip.SetCurve(
                    curves[i].RelativePath,
                    typeof(SkinnedMeshRenderer),
                    "blendShape." + curves[i].BlendShapeName,
                    new AnimationCurve(new Keyframe(0f, curves[i].Weight)));
            }

            return clip;
        }

        private static ClipCurve Curve(
            string relativePath,
            string blendShapeName,
            float weight)
        {
            return new ClipCurve(relativePath, blendShapeName, weight);
        }

        private ULipSyncAdapterBinding CreateBinding(params PhonemeEntryBase[] entries)
        {
            var binding = new ULipSyncAdapterBinding();
            binding.Configure(
                default,
                null,
                entries);
            return binding;
        }

        private static ExpressionPhonemeEntry CreateExpressionEntry(string phonemeId, string expressionId)
        {
            var entry = new ExpressionPhonemeEntry
            {
                PhonemeId = phonemeId,
                MaxWeight = 100f,
            };
            SetPrivateField(entry, "_expressionId", expressionId);
            return entry;
        }

        private static AdapterBuildContext CreateContext(
            GameObject host,
            params string[] blendShapeNames)
        {
            return CreateContext(host, new FacialProfile("1.0"), blendShapeNames);
        }

        private static AdapterBuildContext CreateContext(
            GameObject host,
            FacialProfile profile,
            params string[] blendShapeNames)
        {
            return new AdapterBuildContext(
                profile,
                new List<string>(blendShapeNames),
                new InputSourceRegistry(),
                new FacialOutputBus(),
                new UnityTimeProvider(),
                host,
                null);
        }

        /// <summary>
        /// private な BuildSnapshots を reflection で呼ぶ。assert 対象は snapshot の数値結果
        /// （BlendShape weight / 後方互換 fixture）であり公開契約に相当するが、
        /// 公開 API 経由（OnStart → Provider）で同じ値を観測するには PlayMode 移行が必要なため保留中。
        /// </summary>
        private static PhonemeSnapshot[] BuildSnapshots(
            ULipSyncAdapterBinding binding,
            AdapterBuildContext ctx)
        {
            MethodInfo method = typeof(ULipSyncAdapterBinding).GetMethod(
                "BuildSnapshots",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);

            try
            {
                return (PhonemeSnapshot[])method.Invoke(binding, new object[] { ctx });
            }
            catch (TargetInvocationException exception)
            {
                if (exception.InnerException != null)
                {
                    throw exception.InnerException;
                }

                throw;
            }
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            field.SetValue(target, value);
        }

        private static void AssertWeights(float[] actual, params float[] expected)
        {
            Assert.That(actual, Has.Length.EqualTo(expected.Length));
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.That(actual[i], Is.EqualTo(expected[i]).Within(1e-6f), "index " + i);
            }
        }

        private PhonemeEntryFixtureAsset SaveAndReloadFixture(
            string fixtureName,
            IReadOnlyList<PhonemeEntryBase> entries,
            params AnimationClip[] clips)
        {
            if (!AssetDatabase.IsValidFolder(TempFolderPath))
            {
                AssetDatabase.CreateFolder(TempFolderParent, TempFolderName);
            }

            string assetPath = TempFolderPath
                + "/PhonemeSnapshotBuilder_"
                + fixtureName
                + "_"
                + Guid.NewGuid().ToString("N")
                + ".asset";
            _createdAssetPaths.Add(assetPath);

            var fixture = ScriptableObject.CreateInstance<PhonemeEntryFixtureAsset>();
            AssetDatabase.CreateAsset(fixture, assetPath);
            for (int i = 0; i < clips.Length; i++)
            {
                AssetDatabase.AddObjectToAsset(clips[i], fixture);
            }

            using (var serialized = new SerializedObject(fixture))
            {
                SerializedProperty entriesProperty =
                    serialized.FindProperty(nameof(PhonemeEntryFixtureAsset.Entries));
                entriesProperty.arraySize = entries.Count;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                serialized.Update();

                for (int i = 0; i < entries.Count; i++)
                {
                    SerializedProperty entry = entriesProperty.GetArrayElementAtIndex(i);
                    entry.managedReferenceValue = entries[i];
                }

                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            EditorUtility.SetDirty(fixture);
            for (int i = 0; i < clips.Length; i++)
            {
                EditorUtility.SetDirty(clips[i]);
            }

            AssetDatabase.SaveAssets();
            Resources.UnloadAsset(fixture);

            PhonemeEntryFixtureAsset loaded =
                AssetDatabase.LoadAssetAtPath<PhonemeEntryFixtureAsset>(assetPath);
            Assert.That(loaded, Is.Not.Null);
            return loaded;
        }

        private static void AssertCommonFieldsAreBackwardCompatible(
            PhonemeEntryBase entry,
            string expectedPhonemeId,
            float expectedMaxWeight)
        {
            Assert.That(entry.PhonemeId, Is.EqualTo(expectedPhonemeId));
            Assert.That(entry.MaxWeight, Is.EqualTo(expectedMaxWeight).Within(1e-6f));
            Assert.That(
                typeof(PhonemeEntryBase).GetField(nameof(PhonemeEntryBase.PhonemeId)).FieldType,
                Is.EqualTo(typeof(string)));
            Assert.That(
                typeof(PhonemeEntryBase).GetField(nameof(PhonemeEntryBase.MaxWeight)).FieldType,
                Is.EqualTo(typeof(float)));
        }

        private static void AssertSerializedEntryTypes(
            PhonemeEntryFixtureAsset fixture,
            params Type[] expectedTypes)
        {
            using (var serialized = new SerializedObject(fixture))
            {
                SerializedProperty entriesProperty =
                    serialized.FindProperty(nameof(PhonemeEntryFixtureAsset.Entries));
                Assert.That(entriesProperty.arraySize, Is.EqualTo(expectedTypes.Length));

                for (int i = 0; i < expectedTypes.Length; i++)
                {
                    SerializedProperty entry = entriesProperty.GetArrayElementAtIndex(i);
                    Assert.That(entry.propertyType, Is.EqualTo(SerializedPropertyType.ManagedReference));
                    Assert.That(entry.managedReferenceValue, Is.InstanceOf(expectedTypes[i]));
                    Assert.That(
                        entry.managedReferenceFullTypename,
                        Does.Contain(expectedTypes[i].FullName));
                }
            }
        }

        private static ExpectedPhonemeSnapshot ExpectedSnapshot(string phonemeId, params float[] weights)
        {
            return new ExpectedPhonemeSnapshot(phonemeId, weights);
        }

        private static void AssertSnapshotsBitExact(
            PhonemeSnapshot[] actual,
            params ExpectedPhonemeSnapshot[] expected)
        {
            Assert.That(actual, Has.Length.EqualTo(expected.Length));
            for (int snapshotIndex = 0; snapshotIndex < expected.Length; snapshotIndex++)
            {
                Assert.That(actual[snapshotIndex].PhonemeId, Is.EqualTo(expected[snapshotIndex].PhonemeId));
                Assert.That(actual[snapshotIndex].Weights, Has.Length.EqualTo(expected[snapshotIndex].Weights.Length));
                for (int weightIndex = 0; weightIndex < expected[snapshotIndex].Weights.Length; weightIndex++)
                {
                    Assert.That(
                        FloatBits(actual[snapshotIndex].Weights[weightIndex]),
                        Is.EqualTo(FloatBits(expected[snapshotIndex].Weights[weightIndex])),
                        "snapshot " + snapshotIndex + ", weight " + weightIndex);
                }
            }
        }

        private static int FloatBits(float value)
        {
            return BitConverter.ToInt32(BitConverter.GetBytes(value), 0);
        }

        private readonly struct ClipCurve
        {
            public readonly string RelativePath;
            public readonly string BlendShapeName;
            public readonly float Weight;

            public ClipCurve(string relativePath, string blendShapeName, float weight)
            {
                RelativePath = relativePath;
                BlendShapeName = blendShapeName;
                Weight = weight;
            }
        }

        private readonly struct ExpectedPhonemeSnapshot
        {
            public readonly string PhonemeId;
            public readonly float[] Weights;

            public ExpectedPhonemeSnapshot(string phonemeId, float[] weights)
            {
                PhonemeId = phonemeId;
                Weights = weights;
            }
        }

    }

}
