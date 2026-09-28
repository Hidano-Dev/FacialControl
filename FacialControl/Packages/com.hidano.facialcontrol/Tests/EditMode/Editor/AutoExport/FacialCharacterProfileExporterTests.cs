using System;
using System.Collections.Generic;
using System.IO;
using Hidano.FacialControl.Adapters.Json;
using Hidano.FacialControl.Adapters.Json.Dto;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Editor.AutoExport;
using Hidano.FacialControl.Editor.Sampling;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Hidano.FacialControl.Testing;
using Object = UnityEngine.Object;

namespace Hidano.FacialControl.Tests.EditMode.Editor.AutoExport
{
    /// <summary>
    /// <see cref="FacialCharacterProfileExporter"/> のテスト。
    /// SampleAnimationClipsIntoCachedSnapshots によるベース表情 / Overlay clip の cachedSnapshot ベイク（注入サンプラ経由・clip null 時の空化）と、
    /// ExportProfileJson が cachedSnapshot / transitionDuration / slots / defaultOverlays を profile.json へ正しく書き出すことを検証する。
    /// </summary>
    [TestFixture]
    [MediumTest]
    public class FacialCharacterProfileExporterTests : SizedTestFixture
    {
        // ---- SampleAnimationClipsIntoCachedSnapshots: ベース表情 ----

        [Test]
        public void SampleAnimationClipsIntoCachedSnapshots_BaseExpressionClip_BakesCachedSnapshot()
        {
            var so = UnityEngine.ScriptableObject.CreateInstance<FacialCharacterProfileSO>();
            var clip = new AnimationClip { name = "BaseExpression_BakeClip" };
            var sampler = new FixedSnapshotRecordingSampler(CreateSnapshot(
                "base-expression",
                new BlendShapeSnapshot("Body/Face", "Brow_Angry", 64.5f),
                new BlendShapeSnapshot("Body/Face", "Mouth_Frown", 22.0f)));

            try
            {
                so.BaseExpression.animationClip = clip;
                so.BaseExpression.cachedSnapshot = BaseExpressionSerializable.CreateEmptySnapshot();

                FacialCharacterProfileExporter.SampleAnimationClipsIntoCachedSnapshots(so, sampler);

                ExpressionSnapshotDto snapshot = so.BaseExpression.cachedSnapshot;
                Assert.That(snapshot, Is.Not.Null);
                Assert.That(snapshot.blendShapes, Has.Count.EqualTo(2));
                Assert.That(snapshot.blendShapes[0].rendererPath, Is.EqualTo("Body/Face"));
                Assert.That(snapshot.blendShapes[0].name, Is.EqualTo("Brow_Angry"));
                Assert.That(snapshot.blendShapes[0].value, Is.EqualTo(64.5f).Within(1e-6f));
                Assert.That(snapshot.blendShapes[1].name, Is.EqualTo("Mouth_Frown"));
                Assert.That(snapshot.blendShapes[1].value, Is.EqualTo(22.0f).Within(1e-6f));
            }
            finally
            {
                Object.DestroyImmediate(clip);
                Object.DestroyImmediate(so);
            }
        }

        [Test]
        public void SampleAnimationClipsIntoCachedSnapshots_BaseExpressionClipNull_RegeneratesEmptySnapshot()
        {
            var so = UnityEngine.ScriptableObject.CreateInstance<FacialCharacterProfileSO>();
            var sampler = new FixedSnapshotRecordingSampler(CreateSnapshot(
                "unused",
                new BlendShapeSnapshot("Body/Face", "ShouldNotBeSampled", 100f)));

            try
            {
                so.BaseExpression.animationClip = null;
                so.BaseExpression.cachedSnapshot = new ExpressionSnapshotDto
                {
                    blendShapes = new List<BlendShapeSnapshotDto>
                    {
                        new BlendShapeSnapshotDto
                        {
                            rendererPath = "Body/Face",
                            name = "StaleSmile",
                            value = 100f,
                        },
                    },
                    bones = new List<BoneSnapshotDto>(),
                    rendererPaths = new List<string> { "Body/Face" },
                };

                FacialCharacterProfileExporter.SampleAnimationClipsIntoCachedSnapshots(so, sampler);

                Assert.That(sampler.SampleSnapshotCallCount, Is.EqualTo(0));
                Assert.That(so.BaseExpression.cachedSnapshot, Is.Not.Null);
                Assert.That(so.BaseExpression.cachedSnapshot.blendShapes, Is.Not.Null);
                Assert.That(so.BaseExpression.cachedSnapshot.blendShapes, Is.Empty);
            }
            finally
            {
                Object.DestroyImmediate(so);
            }
        }

        [Test]
        public void SampleAnimationClipsIntoCachedSnapshots_BaseExpressionClip_UsesInjectedSampler()
        {
            var so = UnityEngine.ScriptableObject.CreateInstance<FacialCharacterProfileSO>();
            var clip = new AnimationClip { name = "BaseExpression_InjectedSamplerClip" };
            var sampler = new FixedSnapshotRecordingSampler(CreateSnapshot(
                "base-expression",
                new BlendShapeSnapshot("Body/Face", "SamplerOnlyShape", 12.5f)));

            try
            {
                so.BaseExpression.animationClip = clip;
                so.BaseExpression.cachedSnapshot = BaseExpressionSerializable.CreateEmptySnapshot();

                FacialCharacterProfileExporter.SampleAnimationClipsIntoCachedSnapshots(so, sampler);

                Assert.That(sampler.SampleSnapshotCallCount, Is.EqualTo(1));
                Assert.That(sampler.LastClip, Is.SameAs(clip));
                Assert.That(so.BaseExpression.cachedSnapshot.blendShapes, Has.Count.EqualTo(1));
                Assert.That(so.BaseExpression.cachedSnapshot.blendShapes[0].name, Is.EqualTo("SamplerOnlyShape"));
                Assert.That(so.BaseExpression.cachedSnapshot.blendShapes[0].value, Is.EqualTo(12.5f).Within(1e-6f));
            }
            finally
            {
                Object.DestroyImmediate(clip);
                Object.DestroyImmediate(so);
            }
        }

        // ---- SampleAnimationClipsIntoCachedSnapshots: Overlay clip ----

        [Test]
        public void SampleAnimationClipsIntoCachedSnapshots_OverlayClips_BakesAndClearsByState()
        {
            var so = UnityEngine.ScriptableObject.CreateInstance<FacialCharacterProfileSO>();
            var blinkClip = new AnimationClip { name = "BlinkOverlay" };
            var sparkleClip = new AnimationClip { name = "SparkleOverlay" };
            var blushClip = new AnimationClip { name = "SuppressedBlushOverlay" };
            var sampler = new PerIdRecordingSampler();

            try
            {
                so.DefaultOverlays.Add(new OverlaySlotBindingSerializable
                {
                    slot = "blink",
                    animationClip = blinkClip,
                    cachedSnapshot = CreateOverlaySnapshotDto("Face", "StaleBlink", 99f),
                });
                so.DefaultOverlays.Add(new OverlaySlotBindingSerializable
                {
                    slot = "mouth",
                    cachedSnapshot = CreateOverlaySnapshotDto("Face", "StaleMouth", 99f),
                });
                so.Expressions.Add(new ExpressionSerializable
                {
                    id = "smile",
                    name = "Smile",
                    layer = "emotion",
                    overlays = new List<OverlaySlotBindingSerializable>
                    {
                        new OverlaySlotBindingSerializable
                        {
                            slot = "sparkle",
                            animationClip = sparkleClip,
                            cachedSnapshot = CreateOverlaySnapshotDto("Face", "StaleSparkle", 99f),
                        },
                        new OverlaySlotBindingSerializable
                        {
                            slot = "blush",
                            suppress = true,
                            animationClip = blushClip,
                            cachedSnapshot = CreateOverlaySnapshotDto("Face", "StaleBlush", 99f),
                        },
                    },
                });

                FacialCharacterProfileExporter.SampleAnimationClipsIntoCachedSnapshots(so, sampler);

                Assert.That(sampler.SampleSnapshotCallCount, Is.EqualTo(2));
                Assert.That(sampler.SampledIds, Is.EquivalentTo(new[] { "blink", "sparkle" }));
                AssertOverlaySnapshot(so.DefaultOverlays[0].cachedSnapshot, "blink_Sampled", 10f);
                AssertEmptyOverlaySnapshot(so.DefaultOverlays[1].cachedSnapshot);
                AssertOverlaySnapshot(so.Expressions[0].overlays[0].cachedSnapshot, "sparkle_Sampled", 20f);
                AssertEmptyOverlaySnapshot(so.Expressions[0].overlays[1].cachedSnapshot);
            }
            finally
            {
                Object.DestroyImmediate(blinkClip);
                Object.DestroyImmediate(sparkleClip);
                Object.DestroyImmediate(blushClip);
                Object.DestroyImmediate(so);
            }
        }

        // ---- ExportProfileJson ----

        [Test]
        public void ExportProfileJson_ExpressionCachedSnapshotTransitionDuration_UsesInspectorSliderValue()
        {
            var so = UnityEngine.ScriptableObject.CreateInstance<FacialCharacterProfileSO>();
            string assetName = "ExporterTransitionDuration_" + Guid.NewGuid().ToString("N");
            string profilePath = FacialCharacterProfileSO.GetStreamingAssetsProfilePath(assetName);
            string profileDirectory = Path.GetDirectoryName(profilePath);

            try
            {
                so.name = assetName;
                so.Expressions.Add(new ExpressionSerializable
                {
                    id = "smile",
                    name = "Smile",
                    layer = "emotion",
                    transitionDuration = 0.6f,
                    cachedSnapshot = new ExpressionSnapshotDto
                    {
                        transitionDuration = 0.25f,
                        transitionCurvePreset = TransitionCurvePreset.Linear.ToString(),
                        blendShapes = new List<BlendShapeSnapshotDto>
                        {
                            new BlendShapeSnapshotDto
                            {
                                rendererPath = "Body/Face",
                                name = "Smile",
                                value = 72f,
                            },
                        },
                        bones = new List<BoneSnapshotDto>(),
                        rendererPaths = new List<string> { "Body/Face" },
                    },
                });

                bool exported = FacialCharacterProfileExporter.ExportProfileJson(so);

                Assert.That(exported, Is.True);
                Assert.That(File.Exists(profilePath), Is.True);

                string json = File.ReadAllText(profilePath);
                var dto = new SystemTextJsonParser().ParseProfileSnapshotV2(json);
                Assert.That(dto.expressions, Has.Count.EqualTo(1));
                Assert.That(dto.expressions[0].snapshot.transitionDuration, Is.EqualTo(0.6f).Within(1e-6f),
                    "Inspector スライダー値 (ExpressionSerializable.transitionDuration) が JSON 出力 DTO の真値であるべき。");
                Assert.That(dto.expressions[0].snapshot.blendShapes, Has.Count.EqualTo(1));
                Assert.That(dto.expressions[0].snapshot.blendShapes[0].name, Is.EqualTo("Smile"));
            }
            finally
            {
                if (Directory.Exists(profileDirectory))
                {
                    Directory.Delete(profileDirectory, true);
                }

                Object.DestroyImmediate(so);
            }
        }

        [Test]
        public void ExportProfileJson_BaseExpressionCachedSnapshot_EmitsBaseExpressionToJson()
        {
            var so = UnityEngine.ScriptableObject.CreateInstance<FacialCharacterProfileSO>();
            string assetName = "ExporterBaseExpression_" + Guid.NewGuid().ToString("N");
            string profilePath = FacialCharacterProfileSO.GetStreamingAssetsProfilePath(assetName);
            string profileDirectory = Path.GetDirectoryName(profilePath);

            try
            {
                so.name = assetName;
                so.BaseExpression.cachedSnapshot = new ExpressionSnapshotDto
                {
                    blendShapes = new List<BlendShapeSnapshotDto>
                    {
                        new BlendShapeSnapshotDto
                        {
                            rendererPath = "Body/Face",
                            name = "Brow_Angry",
                            value = 0.645f,
                        },
                    },
                    bones = new List<BoneSnapshotDto>(),
                    rendererPaths = new List<string> { "Body/Face" },
                };

                bool exported = FacialCharacterProfileExporter.ExportProfileJson(so);

                Assert.That(exported, Is.True);

                string json = File.ReadAllText(profilePath);
                var dto = new SystemTextJsonParser().ParseProfileSnapshotV2(json);

                Assert.That(dto.baseExpression, Is.Not.Null,
                    "SO でベイクしたベース表情は profile.json へ書き出される必要がある。");
                Assert.That(dto.baseExpression.blendShapes, Has.Count.EqualTo(1));
                Assert.That(dto.baseExpression.blendShapes[0].name, Is.EqualTo("Brow_Angry"));
                Assert.That(dto.baseExpression.blendShapes[0].value, Is.EqualTo(0.645f).Within(1e-6f));
                Assert.That(dto.rendererPaths, Contains.Item("Body/Face"),
                    "ベース表情の rendererPath は top-level rendererPaths にマージされる。");
            }
            finally
            {
                if (Directory.Exists(profileDirectory))
                {
                    Directory.Delete(profileDirectory, true);
                }

                Object.DestroyImmediate(so);
            }
        }

        [Test]
        public void ExportProfileJson_BaseExpressionEmpty_EmitsEmptyBaseExpression()
        {
            var so = UnityEngine.ScriptableObject.CreateInstance<FacialCharacterProfileSO>();
            string assetName = "ExporterBaseExpressionEmpty_" + Guid.NewGuid().ToString("N");
            string profilePath = FacialCharacterProfileSO.GetStreamingAssetsProfilePath(assetName);
            string profileDirectory = Path.GetDirectoryName(profilePath);

            try
            {
                so.name = assetName;

                bool exported = FacialCharacterProfileExporter.ExportProfileJson(so);

                Assert.That(exported, Is.True);

                string json = File.ReadAllText(profilePath);
                var dto = new SystemTextJsonParser().ParseProfileSnapshotV2(json);

                Assert.That(dto.baseExpression, Is.Not.Null);
                Assert.That(dto.baseExpression.blendShapes, Is.Empty);
            }
            finally
            {
                if (Directory.Exists(profileDirectory))
                {
                    Directory.Delete(profileDirectory, true);
                }

                Object.DestroyImmediate(so);
            }
        }

        [Test]
        public void ExportProfileJson_OverlaySlotsAndBindings_WritesNewSchema()
        {
            var so = UnityEngine.ScriptableObject.CreateInstance<FacialCharacterProfileSO>();
            var blinkClip = new AnimationClip { name = "BlinkOverlayExport" };
            var sparkleClip = new AnimationClip { name = "SparkleOverlayExport" };
            var sampler = new PerIdRecordingSampler();
            string assetName = "ExporterOverlayClips_" + Guid.NewGuid().ToString("N");
            string profilePath = FacialCharacterProfileSO.GetStreamingAssetsProfilePath(assetName);
            string profileDirectory = Path.GetDirectoryName(profilePath);

            try
            {
                so.name = assetName;
                SetSlots(so, "blink", "mouth", "sparkle", "blush");
                so.DefaultOverlays.Add(new OverlaySlotBindingSerializable
                {
                    slot = "blink",
                    animationClip = blinkClip,
                });
                so.DefaultOverlays.Add(new OverlaySlotBindingSerializable
                {
                    slot = "mouth",
                });
                so.Expressions.Add(new ExpressionSerializable
                {
                    id = "smile",
                    name = "Smile",
                    layer = "emotion",
                    transitionDuration = 0.4f,
                    overlays = new List<OverlaySlotBindingSerializable>
                    {
                        new OverlaySlotBindingSerializable
                        {
                            slot = "sparkle",
                            animationClip = sparkleClip,
                        },
                        new OverlaySlotBindingSerializable
                        {
                            slot = "blush",
                            suppress = true,
                        },
                    },
                });

                FacialCharacterProfileExporter.SampleAnimationClipsIntoCachedSnapshots(so, sampler);

                bool exported = FacialCharacterProfileExporter.ExportProfileJson(so);

                Assert.That(exported, Is.True);
                Assert.That(File.Exists(profilePath), Is.True);

                string json = File.ReadAllText(profilePath);
                StringAssert.Contains("\"slots\"", json);
                StringAssert.Contains("\"defaultOverlays\"", json);

                var dto = new SystemTextJsonParser().ParseProfileSnapshotV2(json);
                Assert.That(dto.slots, Is.EqualTo(new List<string> { "blink", "mouth", "sparkle", "blush" }));
                Assert.That(dto.defaultOverlays, Has.Count.EqualTo(2));
                Assert.That(dto.defaultOverlays[0].slot, Is.EqualTo("blink"));
                Assert.That(dto.defaultOverlays[0].suppress, Is.False);
                AssertOverlaySnapshot(dto.defaultOverlays[0].snapshot, "blink_Sampled", 10f);
                Assert.That(dto.defaultOverlays[1].slot, Is.EqualTo("mouth"));
                Assert.That(dto.defaultOverlays[1].snapshot, Is.Null);

                var overlays = dto.expressions[0].snapshot.overlays;
                Assert.That(overlays, Has.Count.EqualTo(2));
                Assert.That(overlays[0].slot, Is.EqualTo("sparkle"));
                Assert.That(overlays[0].suppress, Is.False);
                AssertOverlaySnapshot(overlays[0].snapshot, "sparkle_Sampled", 20f);
                Assert.That(overlays[1].slot, Is.EqualTo("blush"));
                Assert.That(overlays[1].suppress, Is.True);
                Assert.That(overlays[1].snapshot, Is.Null);
            }
            finally
            {
                if (!string.IsNullOrEmpty(profileDirectory) && Directory.Exists(profileDirectory))
                {
                    Directory.Delete(profileDirectory, true);
                }

                Object.DestroyImmediate(blinkClip);
                Object.DestroyImmediate(sparkleClip);
                Object.DestroyImmediate(so);
            }
        }

        // ---- ヘルパー: ベース表情 ----

        private static ExpressionSnapshot CreateSnapshot(string id, params BlendShapeSnapshot[] blendShapes)
        {
            return new ExpressionSnapshot(
                id,
                Expression.DefaultTransitionDuration,
                TransitionCurvePreset.Linear,
                blendShapes,
                Array.Empty<BoneSnapshot>(),
                CollectRendererPaths(blendShapes));
        }

        private static string[] CollectRendererPaths(IReadOnlyList<BlendShapeSnapshot> blendShapes)
        {
            var paths = new List<string>();
            for (int i = 0; i < blendShapes.Count; i++)
            {
                string path = blendShapes[i].RendererPath ?? string.Empty;
                if (!paths.Contains(path))
                {
                    paths.Add(path);
                }
            }

            return paths.ToArray();
        }

        // ---- ヘルパー: Overlay ----

        private static void SetSlots(FacialCharacterProfileSO so, params string[] slots)
        {
            var serializedObject = new SerializedObject(so);
            var slotsProperty = serializedObject.FindProperty("_slots");
            slotsProperty.arraySize = slots.Length;
            for (int i = 0; i < slots.Length; i++)
            {
                slotsProperty.GetArrayElementAtIndex(i).stringValue = slots[i];
            }

            serializedObject.ApplyModifiedProperties();
        }

        private static OverlaySnapshotDto CreateOverlaySnapshotDto(
            string rendererPath,
            string blendShapeName,
            float value)
        {
            return new OverlaySnapshotDto
            {
                rendererPaths = new List<string> { rendererPath },
                blendShapes = new List<BlendShapeSnapshotDto>
                {
                    new BlendShapeSnapshotDto
                    {
                        rendererPath = rendererPath,
                        name = blendShapeName,
                        value = value,
                    },
                },
                bones = new List<BoneSnapshotDto>(),
            };
        }

        private static void AssertOverlaySnapshot(OverlaySnapshotDto snapshot, string blendShapeName, float value)
        {
            Assert.That(snapshot, Is.Not.Null);
            Assert.That(snapshot.blendShapes, Has.Count.EqualTo(1));
            Assert.That(snapshot.blendShapes[0].name, Is.EqualTo(blendShapeName));
            Assert.That(snapshot.blendShapes[0].value, Is.EqualTo(value).Within(1e-6f));
        }

        private static void AssertEmptyOverlaySnapshot(OverlaySnapshotDto snapshot)
        {
            Assert.That(snapshot, Is.Not.Null);
            Assert.That(snapshot.blendShapes, Is.Not.Null);
            Assert.That(snapshot.blendShapes, Is.Empty);
        }

        // ---- Fake サンプラ ----

        /// <summary>
        /// コンストラクタで与えた固定 snapshot を常に返す Fake。呼び出し回数と最後に渡された clip を記録する。
        /// ベース表情ベイクのテストで使用。
        /// </summary>
        private sealed class FixedSnapshotRecordingSampler : IExpressionAnimationClipSampler
        {
            private readonly ExpressionSnapshot _snapshot;

            public int SampleSnapshotCallCount { get; private set; }
            public AnimationClip LastClip { get; private set; }

            public FixedSnapshotRecordingSampler(ExpressionSnapshot snapshot)
            {
                _snapshot = snapshot;
            }

            public ExpressionSnapshot SampleSnapshot(string snapshotId, AnimationClip clip)
            {
                SampleSnapshotCallCount++;
                LastClip = clip;
                return _snapshot;
            }

            public ClipSummary SampleSummary(AnimationClip clip)
            {
                return new ClipSummary(
                    Array.Empty<string>(),
                    Array.Empty<string>(),
                    Expression.DefaultTransitionDuration,
                    TransitionCurvePreset.Linear);
            }
        }

        /// <summary>
        /// snapshotId ごとに "{id}_Sampled" という BlendShape 1 本の snapshot を生成して返す Fake
        /// （"sparkle" のみ値 20、他は 10）。サンプリングされた id の一覧を記録する。Overlay clip ベイクのテストで使用。
        /// </summary>
        private sealed class PerIdRecordingSampler : IExpressionAnimationClipSampler
        {
            private readonly List<string> _sampledIds = new List<string>();

            public IReadOnlyList<string> SampledIds => _sampledIds;
            public int SampleSnapshotCallCount { get; private set; }

            public ExpressionSnapshot SampleSnapshot(string snapshotId, AnimationClip clip)
            {
                SampleSnapshotCallCount++;
                _sampledIds.Add(snapshotId);
                float value = string.Equals(snapshotId, "sparkle", StringComparison.Ordinal) ? 20f : 10f;
                return new ExpressionSnapshot(
                    snapshotId,
                    Expression.DefaultTransitionDuration,
                    TransitionCurvePreset.Linear,
                    new[]
                    {
                        new BlendShapeSnapshot("Face", snapshotId + "_Sampled", value),
                    },
                    Array.Empty<BoneSnapshot>(),
                    new[] { "Face" });
            }

            public ClipSummary SampleSummary(AnimationClip clip)
            {
                return new ClipSummary(
                    Array.Empty<string>(),
                    Array.Empty<string>(),
                    Expression.DefaultTransitionDuration,
                    TransitionCurvePreset.Linear);
            }
        }
    }
}
