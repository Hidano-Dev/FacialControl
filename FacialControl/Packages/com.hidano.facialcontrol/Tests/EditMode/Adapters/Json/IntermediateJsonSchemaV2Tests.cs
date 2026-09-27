using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Hidano.FacialControl.Adapters.Json;
using Hidano.FacialControl.Adapters.Json.Dto;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Tests.EditMode.Adapters.Json
{
    /// <summary>
    /// プロファイル JSON DTO 群と <see cref="SystemTextJsonParser.ParseProfileSnapshotV2(string)"/> に対する契約テスト。
    ///
    /// 検証範囲:
    ///   1. <c>RoundTrip_FullSnapshot_PreservesAllFields</c>:
    ///      schemaVersion / layers / expressions / rendererPaths を全て埋めた
    ///      <see cref="ProfileSnapshotDto"/> を JSON へ書き出し、再パース後に同値であることを確認。
    ///   2. <c>Parse_SchemaVersionMismatch_ThrowsInvalidOperation</c>:
    ///      schemaVersion が "1.0" 以外の場合に <see cref="Debug.LogError(object)"/> + <see cref="NotSupportedException"/> を出すこと。
    ///   3. <c>Parse_MissingSnapshot_ProducesEmptySnapshot</c>:
    ///      <c>expressions[].snapshot</c> 欠落時に空 snapshot（duration=0.25 / Linear / 空配列）に正規化されること。
    ///   4. <c>RendererPaths_AreSubset_Of_TopLevelRendererPaths</c>:
    ///      <c>expressions[].snapshot.rendererPaths</c> がトップレベル <c>rendererPaths</c> の subset として保持されること。
    /// </summary>
    [TestFixture]
    public class IntermediateJsonSchemaV2Tests
    {
        // ================================================================
        // Test 1: RoundTrip_FullSnapshot_PreservesAllFields
        // ================================================================

        [Test]
        public void RoundTrip_FullSnapshot_PreservesAllFields()
        {
            var src = BuildFullProfileSnapshotDto();

            var json = JsonUtility.ToJson(src);
            var dst = JsonUtility.FromJson<ProfileSnapshotDto>(json);

            Assert.IsNotNull(dst, "再パース結果が null になってはならない");
            Assert.AreEqual(src.schemaVersion, dst.schemaVersion);

            Assert.AreEqual(src.slots.Count, dst.slots.Count, "slots count mismatch");
            for (int i = 0; i < src.slots.Count; i++)
            {
                Assert.AreEqual(src.slots[i], dst.slots[i]);
            }

            // layers[] の round-trip
            Assert.AreEqual(src.layers.Count, dst.layers.Count, "layers count mismatch");
            for (int i = 0; i < src.layers.Count; i++)
            {
                Assert.AreEqual(src.layers[i].name, dst.layers[i].name);
                Assert.AreEqual(src.layers[i].priority, dst.layers[i].priority);
                Assert.AreEqual(src.layers[i].exclusionMode, dst.layers[i].exclusionMode);
                Assert.AreEqual(src.layers[i].inputSources.Count, dst.layers[i].inputSources.Count);
                for (int j = 0; j < src.layers[i].inputSources.Count; j++)
                {
                    Assert.AreEqual(src.layers[i].inputSources[j].id, dst.layers[i].inputSources[j].id);
                    Assert.AreEqual(src.layers[i].inputSources[j].weight, dst.layers[i].inputSources[j].weight);
                }
            }

            // expressions[] の round-trip
            Assert.AreEqual(src.expressions.Count, dst.expressions.Count, "expressions count mismatch");
            for (int i = 0; i < src.expressions.Count; i++)
            {
                var s = src.expressions[i];
                var d = dst.expressions[i];

                Assert.AreEqual(s.id, d.id);
                Assert.AreEqual(s.name, d.name);
                Assert.AreEqual(s.layer, d.layer);

                Assert.AreEqual(s.layerOverrideMask.Count, d.layerOverrideMask.Count, "layerOverrideMask count mismatch");
                for (int j = 0; j < s.layerOverrideMask.Count; j++)
                {
                    Assert.AreEqual(s.layerOverrideMask[j], d.layerOverrideMask[j]);
                }

                Assert.IsNotNull(d.snapshot, "snapshot は round-trip で保持されること");
                Assert.AreEqual(s.snapshot.transitionDuration, d.snapshot.transitionDuration);
                Assert.AreEqual(s.snapshot.transitionCurvePreset, d.snapshot.transitionCurvePreset);

                Assert.AreEqual(s.snapshot.blendShapes.Count, d.snapshot.blendShapes.Count);
                for (int j = 0; j < s.snapshot.blendShapes.Count; j++)
                {
                    Assert.AreEqual(s.snapshot.blendShapes[j].rendererPath, d.snapshot.blendShapes[j].rendererPath);
                    Assert.AreEqual(s.snapshot.blendShapes[j].name, d.snapshot.blendShapes[j].name);
                    Assert.AreEqual(s.snapshot.blendShapes[j].value, d.snapshot.blendShapes[j].value);
                }

                Assert.AreEqual(s.snapshot.bones.Count, d.snapshot.bones.Count);
                for (int j = 0; j < s.snapshot.bones.Count; j++)
                {
                    Assert.AreEqual(s.snapshot.bones[j].bonePath, d.snapshot.bones[j].bonePath);
                    Assert.AreEqual(s.snapshot.bones[j].position, d.snapshot.bones[j].position);
                    Assert.AreEqual(s.snapshot.bones[j].rotationEuler, d.snapshot.bones[j].rotationEuler);
                    Assert.AreEqual(s.snapshot.bones[j].scale, d.snapshot.bones[j].scale);
                }

                Assert.AreEqual(s.snapshot.rendererPaths.Count, d.snapshot.rendererPaths.Count);
                for (int j = 0; j < s.snapshot.rendererPaths.Count; j++)
                {
                    Assert.AreEqual(s.snapshot.rendererPaths[j], d.snapshot.rendererPaths[j]);
                }
            }

            // top-level rendererPaths
            Assert.AreEqual(src.rendererPaths.Count, dst.rendererPaths.Count);
            for (int i = 0; i < src.rendererPaths.Count; i++)
            {
                Assert.AreEqual(src.rendererPaths[i], dst.rendererPaths[i]);
            }

            Assert.AreEqual(src.gaze.channels.Count, dst.gaze.channels.Count, "gaze channel count mismatch");
            Assert.AreEqual(src.gaze.channels[0].id, dst.gaze.channels[0].id);
            Assert.AreEqual(src.gaze.channels[0].useDistinctLeftRight, dst.gaze.channels[0].useDistinctLeftRight);
            Assert.AreEqual(src.gaze.channels[0].sourceIdLeft, dst.gaze.channels[0].sourceIdLeft);
            Assert.AreEqual(src.gaze.channels[0].sourceIdRight, dst.gaze.channels[0].sourceIdRight);
            Assert.AreEqual(src.gaze.channels[0].leftEyeBonePath, dst.gaze.channels[0].leftEyeBonePath);
            Assert.AreEqual(src.gaze.channels[0].rightEyeBonePath, dst.gaze.channels[0].rightEyeBonePath);

            // パーサ経由でも同じ DTO が得られることを確認（normalize は既定値のものなので no-op）
            var parser = new SystemTextJsonParser();
            var parsed = parser.ParseProfileSnapshotV2(json);
            Assert.IsNotNull(parsed);
            Assert.AreEqual(SystemTextJsonParser.SchemaVersionV2, parsed.schemaVersion);
            Assert.AreEqual(src.expressions.Count, parsed.expressions.Count);
            Assert.AreEqual(src.expressions[0].snapshot.blendShapes.Count, parsed.expressions[0].snapshot.blendShapes.Count);
        }

        [Test]
        public void FromJson_SlotsKey_PopulatesSlotsField()
        {
            var json =
                "{" +
                "\"schemaVersion\":\"1.0\"," +
                "\"slots\":[\"blink\",\"mouth\"]," +
                "\"layers\":[]," +
                "\"expressions\":[]," +
                "\"rendererPaths\":[]" +
                "}";

            var dto = JsonUtility.FromJson<ProfileSnapshotDto>(json);

            Assert.IsNotNull(dto.slots);
            Assert.AreEqual(2, dto.slots.Count);
            Assert.AreEqual("blink", dto.slots[0]);
            Assert.AreEqual("mouth", dto.slots[1]);
        }

        // ================================================================
        // Test 2: Parse_SchemaVersionMismatch_ThrowsInvalidOperation
        // ================================================================

        [Test]
        public void Parse_SchemaVersionMismatch_ThrowsInvalidOperation()
        {
            // schemaVersion = "2.0"（未サポート）を渡すと Debug.LogError + NotSupportedException が出る。
            var json = "{\"schemaVersion\":\"2.0\",\"layers\":[],\"expressions\":[],\"rendererPaths\":[]}";

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("strict"));

            var parser = new SystemTextJsonParser();
            var ex = Assert.Throws<NotSupportedException>(() => parser.ParseProfileSnapshotV2(json));
            StringAssert.Contains("'2.0'", ex.Message);
            StringAssert.Contains("'1.0'", ex.Message);
        }

        [Test]
        public void Parse_SchemaVersionMissing_ThrowsInvalidOperationWithMissingMarker()
        {
            // schemaVersion 自体が無いケースも拒否される。
            var json = "{\"layers\":[],\"expressions\":[],\"rendererPaths\":[]}";

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("strict"));

            var parser = new SystemTextJsonParser();
            var ex = Assert.Throws<NotSupportedException>(() => parser.ParseProfileSnapshotV2(json));
            StringAssert.Contains("<missing>", ex.Message);
        }

        // ================================================================
        // Test 3: Parse_MissingSnapshot_ProducesEmptySnapshot
        // ================================================================

        [Test]
        public void Parse_MissingSnapshot_ProducesEmptySnapshot()
        {
            // expressions[].snapshot 欠落 → 空 snapshot に正規化される。
            // 既定値: transitionDuration=Expression.DefaultTransitionDuration (1/15), transitionCurvePreset="Linear", blendShapes/bones/rendererPaths は空 List。
            var json =
                "{" +
                "\"schemaVersion\":\"1.0\"," +
                "\"layers\":[]," +
                "\"expressions\":[{\"id\":\"smile-id\",\"name\":\"Smile\",\"layer\":\"emotion\",\"layerOverrideMask\":[]}]," +
                "\"rendererPaths\":[]" +
                "}";

            var parser = new SystemTextJsonParser();
            var dto = parser.ParseProfileSnapshotV2(json);

            Assert.IsNotNull(dto);
            Assert.AreEqual(1, dto.expressions.Count);

            var expr = dto.expressions[0];
            Assert.IsNotNull(expr.snapshot, "snapshot 欠落でも null ではなく空 snapshot を返すこと");
            Assert.AreEqual(Expression.DefaultTransitionDuration, expr.snapshot.transitionDuration);
            Assert.AreEqual("Linear", expr.snapshot.transitionCurvePreset);
            Assert.IsNotNull(expr.snapshot.blendShapes);
            Assert.AreEqual(0, expr.snapshot.blendShapes.Count);
            Assert.IsNotNull(expr.snapshot.bones);
            Assert.AreEqual(0, expr.snapshot.bones.Count);
            Assert.IsNotNull(expr.snapshot.rendererPaths);
            Assert.AreEqual(0, expr.snapshot.rendererPaths.Count);
        }

        // ================================================================
        // Test 4: RendererPaths_AreSubset_Of_TopLevelRendererPaths
        // ================================================================

        [Test]
        public void RendererPaths_AreSubset_Of_TopLevelRendererPaths()
        {
            // expressions[].snapshot.rendererPaths はトップレベル rendererPaths の subset
            var json =
                "{" +
                "\"schemaVersion\":\"1.0\"," +
                "\"layers\":[]," +
                "\"expressions\":[" +
                "  {\"id\":\"e1\",\"name\":\"Smile\",\"layer\":\"emotion\",\"layerOverrideMask\":[]," +
                "   \"snapshot\":{\"transitionDuration\":0.25,\"transitionCurvePreset\":\"Linear\"," +
                "                  \"blendShapes\":[],\"bones\":[],\"rendererPaths\":[\"Body\"]}}," +
                "  {\"id\":\"e2\",\"name\":\"Wink\",\"layer\":\"eye\",\"layerOverrideMask\":[]," +
                "   \"snapshot\":{\"transitionDuration\":0.1,\"transitionCurvePreset\":\"EaseIn\"," +
                "                  \"blendShapes\":[],\"bones\":[],\"rendererPaths\":[\"Body\",\"Face\"]}}" +
                "]," +
                "\"rendererPaths\":[\"Body\",\"Face\",\"Head\"]" +
                "}";

            var parser = new SystemTextJsonParser();
            var dto = parser.ParseProfileSnapshotV2(json);

            Assert.IsNotNull(dto);
            Assert.AreEqual(3, dto.rendererPaths.Count);

            var topLevel = new HashSet<string>(dto.rendererPaths, StringComparer.Ordinal);
            for (int i = 0; i < dto.expressions.Count; i++)
            {
                var snapshotPaths = dto.expressions[i].snapshot.rendererPaths;
                for (int j = 0; j < snapshotPaths.Count; j++)
                {
                    Assert.IsTrue(
                        topLevel.Contains(snapshotPaths[j]),
                        $"expressions[{i}].snapshot.rendererPaths[{j}]='{snapshotPaths[j]}' が top-level rendererPaths に存在しない");
                }
            }
        }

        // ================================================================
        // Helpers
        // ================================================================

        private static ProfileSnapshotDto BuildFullProfileSnapshotDto()
        {
            return new ProfileSnapshotDto
            {
                schemaVersion = SystemTextJsonParser.SchemaVersionV2,
                slots = new List<string> { "blink", "mouth" },
                layers = new List<LayerDefinitionDto>
                {
                    new LayerDefinitionDto
                    {
                        name = "emotion",
                        priority = 0,
                        exclusionMode = "lastWins",
                        inputSources = new List<InputSourceDto>
                        {
                            new InputSourceDto { id = "input", weight = 1.0f },
                        },
                    },
                    new LayerDefinitionDto
                    {
                        name = "eye",
                        priority = 1,
                        exclusionMode = "blend",
                        inputSources = new List<InputSourceDto>
                        {
                            new InputSourceDto { id = "analog-blendshape", weight = 0.75f },
                        },
                    },
                },
                expressions = new List<ExpressionDto>
                {
                    new ExpressionDto
                    {
                        id = "550e8400-e29b-41d4-a716-446655440000",
                        name = "Smile",
                        layer = "emotion",
                        layerOverrideMask = new List<string> { "emotion", "eye" },
                        snapshot = new ExpressionSnapshotDto
                        {
                            transitionDuration = 0.25f,
                            transitionCurvePreset = "EaseInOut",
                            blendShapes = new List<BlendShapeSnapshotDto>
                            {
                                new BlendShapeSnapshotDto { rendererPath = "Body", name = "Smile",   value = 1.0f },
                                new BlendShapeSnapshotDto { rendererPath = "Body", name = "EyeShut", value = 0.5f },
                            },
                            bones = new List<BoneSnapshotDto>
                            {
                                new BoneSnapshotDto
                                {
                                    bonePath      = "Armature/Head",
                                    position      = new Vector3(0f, 0f, 0f),
                                    rotationEuler = new Vector3(0f, 5f, 0f),
                                    scale         = new Vector3(1f, 1f, 1f),
                                },
                            },
                            rendererPaths = new List<string> { "Body" },
                        },
                    },
                    new ExpressionDto
                    {
                        id = "661f9511-f30c-52e5-b827-557766551111",
                        name = "Anger",
                        layer = "emotion",
                        layerOverrideMask = new List<string> { "emotion" },
                        snapshot = new ExpressionSnapshotDto
                        {
                            transitionDuration = 0.15f,
                            transitionCurvePreset = "Linear",
                            blendShapes = new List<BlendShapeSnapshotDto>
                            {
                                new BlendShapeSnapshotDto { rendererPath = "Body", name = "Anger", value = 1.0f },
                            },
                            bones = new List<BoneSnapshotDto>(),
                            rendererPaths = new List<string> { "Body" },
                        },
                    },
                },
                rendererPaths = new List<string> { "Body", "Face" },
                gaze = new GazeSectionDto { channels = new List<GazeChannelDto>
                {
                    new GazeChannelDto
                    {
                        id = "gaze",
                        useDistinctLeftRight = true,
                        sourceIdLeft = "input:550e8400-e29b-41d4-a716-446655440000.left",
                        sourceIdRight = "osc:550e8400-e29b-41d4-a716-446655440000.right",
                        leftEyeBonePath = "Armature/Head/LeftEye",
                        leftEyeInitialRotation = Vector3.zero,
                        leftEyeYawAxisLocal = Vector3.up,
                        leftEyePitchAxisLocal = Vector3.right,
                        rightEyeBonePath = "Armature/Head/RightEye",
                        rightEyeInitialRotation = Vector3.zero,
                        rightEyeYawAxisLocal = Vector3.up,
                        rightEyePitchAxisLocal = Vector3.right,
                        lookUpAngle = 15f,
                        lookDownAngle = 9f,
                        outerYawAngle = 15f,
                        innerYawAngle = 18f,
                    },
                } },
            };
        }
    }
}
