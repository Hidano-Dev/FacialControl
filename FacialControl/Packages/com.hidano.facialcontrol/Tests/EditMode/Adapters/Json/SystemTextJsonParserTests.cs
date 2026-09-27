using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Hidano.FacialControl.Adapters.Json;
using Hidano.FacialControl.Adapters.Json.Dto;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Tests.EditMode.Adapters.Json
{
    /// <summary>
    /// <see cref="SystemTextJsonParser"/> の profile.json パース / シリアライズ契約テスト。
    /// schemaVersion 検証、Serialize → Parse → Serialize の round-trip、baseExpression、
    /// layers[].inputSources（必須フィールド / 検証エラー / options 抽出）、
    /// overlays（slots / suppress / snapshot）、gaze セクションの読み取りを扱う。
    /// </summary>
    [TestFixture]
    public class SystemTextJsonParserTests
    {
        private SystemTextJsonParser _parser;

        [SetUp]
        public void SetUp()
        {
            _parser = new SystemTextJsonParser();
        }

        #region schemaVersion / 基本パース

        [Test]
        public void ParseProfile_SchemaVersion10_ReturnsExpectedProfile()
        {
            var json = @"{
                ""schemaVersion"": ""1.0"",
                ""rendererPaths"": [""Body""],
                ""layers"": [
                    {""name"":""emotion"",""priority"":0,""exclusionMode"":""lastWins"",""inputSources"":[
                        {""id"":""input"",""weight"":1.0}
                    ]}
                ],
                ""expressions"": [
                    {
                        ""id"": ""550e8400-e29b-41d4-a716-446655440000"",
                        ""name"": ""Smile"",
                        ""layer"": ""emotion"",
                        ""layerOverrideMask"": [""emotion""],
                        ""snapshot"": {
                            ""transitionDuration"": 0.3,
                            ""transitionCurvePreset"": ""EaseInOut"",
                            ""blendShapes"": [
                                {""rendererPath"":""Body"",""name"":""Smile"",""value"":1.0}
                            ],
                            ""bones"": [],
                            ""rendererPaths"": [""Body""]
                        }
                    }
                ]
            }";

            var profile = _parser.ParseProfile(json);

            Assert.AreEqual(SystemTextJsonParser.SchemaVersionV2, profile.SchemaVersion);
            Assert.AreEqual(1, profile.Layers.Length);
            Assert.AreEqual("emotion", profile.Layers.Span[0].Name);

            Assert.AreEqual(1, profile.Expressions.Length);
            var expr = profile.Expressions.Span[0];
            Assert.AreEqual("550e8400-e29b-41d4-a716-446655440000", expr.Id);
            Assert.AreEqual("Smile", expr.Name);
            Assert.AreEqual("emotion", expr.Layer);
            Assert.AreEqual(0.3f, expr.TransitionDuration);
            Assert.AreEqual(TransitionCurveType.EaseInOut, expr.TransitionCurve.Type);

            Assert.AreEqual(1, expr.BlendShapeValues.Length);
            Assert.AreEqual("Smile", expr.BlendShapeValues.Span[0].Name);
            Assert.AreEqual(1.0f, expr.BlendShapeValues.Span[0].Value);
            Assert.AreEqual("Body", expr.BlendShapeValues.Span[0].Renderer);

            Assert.AreEqual(1, profile.RendererPaths.Length);
            Assert.AreEqual("Body", profile.RendererPaths.Span[0]);
        }

        [Test]
        public void ParseProfile_LayerOverrideMask_MapsToOverrideMaskBits()
        {
            // layers 宣言順: emotion(bit0), overlay(bit1)。
            // smile.layerOverrideMask=["overlay"] が OverrideMask=Bit1 に変換されることを検証する。
            var json = @"{
                ""schemaVersion"": ""1.0"",
                ""rendererPaths"": [""Body""],
                ""layers"": [
                    {""name"":""emotion"",""priority"":0,""exclusionMode"":""lastWins"",""inputSources"":[{""id"":""input"",""weight"":1.0}]},
                    {""name"":""overlay"",""priority"":1,""exclusionMode"":""lastWins"",""inputSources"":[{""id"":""input:overlay:blink"",""weight"":1.0}]}
                ],
                ""expressions"": [
                    {
                        ""id"": ""smile"",
                        ""name"": ""Smile"",
                        ""layer"": ""emotion"",
                        ""layerOverrideMask"": [""overlay""],
                        ""snapshot"": {
                            ""transitionDuration"": 0.1,
                            ""transitionCurvePreset"": ""Linear"",
                            ""blendShapes"": [],
                            ""bones"": [],
                            ""rendererPaths"": []
                        }
                    }
                ]
            }";

            var profile = _parser.ParseProfile(json);
            var expr = profile.Expressions.Span[0];

            Assert.AreEqual(LayerOverrideMask.Bit1, expr.OverrideMask,
                "overlay は layers index1 なので Bit1 が立つべき。");
        }

        [Test]
        public void ParseProfile_UnsupportedSchemaVersion_ThrowsNotSupportedException()
        {
            var json = @"{
                ""schemaVersion"": ""2.0"",
                ""layers"": [
                    {""name"":""emotion"",""priority"":0,""exclusionMode"":""lastWins"",""inputSources"":[
                        {""id"":""input"",""weight"":1.0}
                    ]}
                ],
                ""expressions"": [],
                ""rendererPaths"": [],
                ""gazeConfigs"": []
            }";

            LogAssert.Expect(LogType.Error, new Regex("strict"));

            var ex = Assert.Throws<NotSupportedException>(() => _parser.ParseProfile(json));
            StringAssert.Contains("'2.0'", ex.Message);
            StringAssert.Contains("'1.0'", ex.Message);
        }

        [Test]
        public void ParseProfile_MissingSchemaVersion_ThrowsNotSupportedExceptionAndLogsError()
        {
            var json = @"{
                ""layers"": [
                    {""name"":""emotion"",""priority"":0,""exclusionMode"":""lastWins"",""inputSources"":[
                        {""id"":""input"",""weight"":1.0}
                    ]}
                ],
                ""expressions"": []
            }";

            LogAssert.Expect(LogType.Error, new Regex("strict"));

            var ex = Assert.Throws<NotSupportedException>(() => _parser.ParseProfile(json));
            StringAssert.Contains("<missing>", ex.Message);
            StringAssert.Contains("'1.0'", ex.Message);
        }

        #endregion

        #region RoundTrip（Serialize → Parse → Serialize 文字列等価）

        [Test]
        public void SerializeParseSerialize_SampleJson_ProducesIdenticalString()
        {
            var profile = _parser.ParseProfile(JsonSchemaDefinition.SampleProfileJson);

            var s1 = _parser.SerializeProfile(profile);
            var p2 = _parser.ParseProfile(s1);
            var s2 = _parser.SerializeProfile(p2);

            Assert.AreEqual(s1, s2, "round-trip 後の JSON 文字列が一致すること");
        }

        [Test]
        public void SerializeParseSerialize_MinimalProfile_ProducesIdenticalString()
        {
            // in-memory 構築プロファイル（inputSources 未設定）。Serialize 側が placeholder を emit し、
            // Parse で LayerInputSources に取り込まれ、再 Serialize で同一出力になる。
            var layers = new[]
            {
                new LayerDefinition("emotion", 0, ExclusionMode.LastWins),
                new LayerDefinition("lipsync", 1, ExclusionMode.Blend)
            };
            var profile = new FacialProfile(SystemTextJsonParser.SchemaVersionV2, layers);

            var s1 = _parser.SerializeProfile(profile);
            var s2 = _parser.SerializeProfile(_parser.ParseProfile(s1));

            Assert.AreEqual(s1, s2);
        }

        [Test]
        public void SerializeParseSerialize_MultipleInputSourcesPerLayer_PreservesOrderAndString()
        {
            var json = @"{
    ""schemaVersion"": ""1.0"",
    ""layers"": [
        {""name"": ""emotion"", ""priority"": 0, ""exclusionMode"": ""lastWins"", ""inputSources"": [
            {""id"": ""input"", ""weight"": 0.5},
            {""id"": ""osc"", ""weight"": 0.5}
        ]}
    ],
    ""expressions"": [],
    ""rendererPaths"": []
}";

            var p1 = _parser.ParseProfile(json);
            var s1 = _parser.SerializeProfile(p1);
            var p2 = _parser.ParseProfile(s1);
            var s2 = _parser.SerializeProfile(p2);

            Assert.AreEqual(s1, s2);

            // 宣言順の保持 : [input, osc] が保たれる。
            Assert.AreEqual(2, p2.LayerInputSources.Span[0].Length);
            Assert.AreEqual("input", p2.LayerInputSources.Span[0][0].Id);
            Assert.AreEqual("osc", p2.LayerInputSources.Span[0][1].Id);
        }

        [Test]
        public void SerializeParseSerialize_WithOscOptions_PreservesOptionsAndString()
        {
            var json = @"{
    ""schemaVersion"": ""1.0"",
    ""layers"": [
        {""name"": ""emotion"", ""priority"": 0, ""exclusionMode"": ""lastWins"", ""inputSources"": [
            {""id"": ""osc"", ""weight"": 1.0, ""options"": {""stalenessSeconds"": 2.5}}
        ]}
    ],
    ""expressions"": [],
    ""rendererPaths"": []
}";

            var p1 = _parser.ParseProfile(json);
            var s1 = _parser.SerializeProfile(p1);
            var p2 = _parser.ParseProfile(s1);
            var s2 = _parser.SerializeProfile(p2);

            Assert.AreEqual(s1, s2);

            // options が round-trip で optionsJson として保持されていること。
            Assert.AreEqual(1, p2.LayerInputSources.Span[0].Length);
            var decl = p2.LayerInputSources.Span[0][0];
            Assert.AreEqual("osc", decl.Id);
            Assert.IsNotNull(decl.OptionsJson);
            StringAssert.Contains("stalenessSeconds", decl.OptionsJson);
            StringAssert.Contains("2.5", decl.OptionsJson);
        }

        [Test]
        public void ParseProfile_PreservesInputSourceDeclarationOrder()
        {
            var json = @"{
    ""schemaVersion"": ""1.0"",
    ""layers"": [
        {""name"": ""emotion"", ""priority"": 0, ""exclusionMode"": ""lastWins"", ""inputSources"": [
            {""id"": ""osc"", ""weight"": 0.3},
            {""id"": ""input"", ""weight"": 0.3},
            {""id"": ""analog-blendshape"", ""weight"": 0.4}
        ]}
    ],
    ""expressions"": [],
    ""rendererPaths"": []
}";

            var profile = _parser.ParseProfile(json);

            var span = profile.LayerInputSources.Span[0];
            Assert.AreEqual(3, span.Length);
            Assert.AreEqual("osc", span[0].Id);
            Assert.AreEqual("input", span[1].Id);
            Assert.AreEqual("analog-blendshape", span[2].Id);
        }

        [Test]
        public void SerializeProfile_EmitsInputSourcesInDeclarationOrder()
        {
            var profile = _parser.ParseProfile(@"{
    ""schemaVersion"": ""1.0"",
    ""layers"": [
        {""name"": ""emotion"", ""priority"": 0, ""exclusionMode"": ""lastWins"", ""inputSources"": [
            {""id"": ""osc"", ""weight"": 0.3},
            {""id"": ""input"", ""weight"": 0.7}
        ]}
    ],
    ""expressions"": [],
    ""rendererPaths"": []
}");

            var serialized = _parser.SerializeProfile(profile);

            // 出力中の "osc" が "input" より先に現れること。
            int oscIdx = serialized.IndexOf("\"osc\"", StringComparison.Ordinal);
            int ctrlIdx = serialized.IndexOf("\"input\"", StringComparison.Ordinal);
            Assert.Greater(oscIdx, 0);
            Assert.Greater(ctrlIdx, 0);
            Assert.Less(oscIdx, ctrlIdx, "宣言順が維持されて出力されること");
        }

        [Test]
        public void SerializeProfile_WeightIsAlwaysEmittedConsistently()
        {
            var layers = new[]
            {
                new LayerDefinition("emotion", 0, ExclusionMode.LastWins)
            };
            var profile = new FacialProfile(SystemTextJsonParser.SchemaVersionV2, layers);

            var serialized = _parser.SerializeProfile(profile);

            StringAssert.Contains("\"weight\":", serialized);
            StringAssert.Contains("1", serialized);
        }

        [Test]
        public void SerializeProfile_Always_EmitsSchemaVersion10()
        {
            var profile = new FacialProfile(SystemTextJsonParser.SchemaVersionV2);
            var serialized = _parser.SerializeProfile(profile);
            StringAssert.Contains("\"schemaVersion\": \"1.0\"", serialized);
        }

        [Test]
        public void ParseProfile_PopulatesLayerInputSourcesAlignedWithLayers()
        {
            var profile = _parser.ParseProfile(JsonSchemaDefinition.SampleProfileJson);

            Assert.AreEqual(profile.Layers.Length, profile.LayerInputSources.Length,
                "LayerInputSources の外側インデックスは Layers と揃う");

            Assert.AreEqual("input", profile.LayerInputSources.Span[0][0].Id);
            Assert.AreEqual("osc", profile.LayerInputSources.Span[0][1].Id);
            Assert.AreEqual("lipsync", profile.LayerInputSources.Span[1][0].Id);
            Assert.AreEqual("input", profile.LayerInputSources.Span[2][0].Id);
        }

        [Test]
        public void FacialProfile_WithExplicitLayerInputSources_RoundTripsThroughParser()
        {
            var layers = new[]
            {
                new LayerDefinition("emotion", 0, ExclusionMode.LastWins)
            };
            var layerInputSources = new[]
            {
                new[]
                {
                    new InputSourceDeclaration("input", 0.5f, null),
                    new InputSourceDeclaration("osc", 0.5f, "{\"stalenessSeconds\":1.0}")
                }
            };
            var profile = new FacialProfile(SystemTextJsonParser.SchemaVersionV2, layers, null, null, layerInputSources);

            var s1 = _parser.SerializeProfile(profile);
            var p2 = _parser.ParseProfile(s1);
            var s2 = _parser.SerializeProfile(p2);

            Assert.AreEqual(s1, s2);

            Assert.AreEqual(2, p2.LayerInputSources.Span[0].Length);
            Assert.AreEqual("input", p2.LayerInputSources.Span[0][0].Id);
            Assert.AreEqual(0.5f, p2.LayerInputSources.Span[0][0].Weight);
            Assert.AreEqual("osc", p2.LayerInputSources.Span[0][1].Id);
            Assert.AreEqual(0.5f, p2.LayerInputSources.Span[0][1].Weight);
            StringAssert.Contains("stalenessSeconds", p2.LayerInputSources.Span[0][1].OptionsJson);
        }

        #endregion

        #region BaseExpression

        [Test]
        public void ParseProfileSnapshotV2_BaseExpressionField_PopulatesSnapshotBlendShapes()
        {
            var dto = _parser.ParseProfileSnapshotV2(BuildProfileJsonWithBaseExpression());

            var baseExpression = GetBaseExpression(dto);

            Assert.That(baseExpression.blendShapes, Is.Not.Null);
            Assert.That(baseExpression.blendShapes, Has.Count.EqualTo(2));
            AssertBlendShape(baseExpression.blendShapes[0], "Body", "Brow_Angry", 64.5f);
            AssertBlendShape(baseExpression.blendShapes[1], "Face", "Eye_Narrow", 28.25f);
        }

        [Test]
        public void ParseProfileSnapshotV2_BaseExpressionMissing_CreatesEmptySnapshot()
        {
            var dto = _parser.ParseProfileSnapshotV2(@"{
                ""schemaVersion"": ""1.0"",
                ""layers"": [],
                ""expressions"": [],
                ""rendererPaths"": []
            }");

            var baseExpression = GetBaseExpression(dto);

            Assert.That(baseExpression.blendShapes, Is.Not.Null);
            Assert.That(baseExpression.blendShapes, Is.Empty);
        }

        [Test]
        public void SerializeProfileSnapshot_BaseExpression_EmitsBaseExpressionSchema()
        {
            var dto = CreateProfileSnapshotDto();
            SetBaseExpression(dto, CreateBaseExpressionSnapshot());

            string json = _parser.SerializeProfileSnapshot(dto);

            StringAssert.Contains(@"""baseExpression""", json);
            StringAssert.Contains(@"""blendShapes""", json);
            StringAssert.Contains(@"""Brow_Angry""", json);

            var parsed = _parser.ParseProfileSnapshotV2(json);
            var baseExpression = GetBaseExpression(parsed);

            Assert.That(baseExpression.blendShapes, Has.Count.EqualTo(2));
            AssertBlendShape(baseExpression.blendShapes[0], "Body", "Brow_Angry", 64.5f);
            AssertBlendShape(baseExpression.blendShapes[1], "Face", "Eye_Narrow", 28.25f);
        }

        [Test]
        public void SerializeProfileSnapshot_BaseExpression_DoesNotEmitAnimationClipPath()
        {
            var dto = CreateProfileSnapshotDto();
            SetBaseExpression(dto, CreateBaseExpressionSnapshot());

            string json = _parser.SerializeProfileSnapshot(dto);

            StringAssert.Contains(@"""baseExpression""", json);
            StringAssert.DoesNotContain("animationClip", json);
            StringAssert.DoesNotContain("BaseExpression_RoundTripClip.anim", json);
            StringAssert.DoesNotContain("Assets/BaseExpression_RoundTripClip.anim", json);
        }

        [Test]
        public void ParseProfile_BaseExpressionField_PopulatesProfileBaseExpression()
        {
            var profile = _parser.ParseProfile(BuildProfileJsonWithNormalizedBaseExpression());

            Assert.That(profile.BaseExpression.Length, Is.EqualTo(2),
                "profile.json の baseExpression は FacialProfile まで運ばれる必要がある。");
            Assert.That(profile.BaseExpression.Span[0].RendererPath, Is.EqualTo("Body"));
            Assert.That(profile.BaseExpression.Span[0].Name, Is.EqualTo("Brow_Angry"));
            Assert.That(profile.BaseExpression.Span[0].Value, Is.EqualTo(0.645f).Within(1e-6f));
            Assert.That(profile.BaseExpression.Span[1].Name, Is.EqualTo("Eye_Narrow"));
            Assert.That(profile.BaseExpression.Span[1].Value, Is.EqualTo(0.2825f).Within(1e-6f));
        }

        [Test]
        public void ParseProfile_BaseExpressionMissing_ProfileBaseExpressionIsEmpty()
        {
            var profile = _parser.ParseProfile(@"{
                ""schemaVersion"": ""1.0"",
                ""layers"": [],
                ""expressions"": [],
                ""rendererPaths"": []
            }");

            Assert.That(profile.BaseExpression.Length, Is.EqualTo(0),
                "baseExpression 欠如の既存 profile.json は空 base として読み込まれる（forward compat）。");
        }

        [Test]
        public void SerializeProfile_ProfileWithBaseExpression_RoundTripsBlendShapes()
        {
            var source = _parser.ParseProfile(BuildProfileJsonWithNormalizedBaseExpression());

            string json = _parser.SerializeProfile(source);

            StringAssert.Contains(@"""baseExpression""", json);
            StringAssert.Contains(@"""Brow_Angry""", json);

            var roundTripped = _parser.ParseProfile(json);
            Assert.That(roundTripped.BaseExpression.Length, Is.EqualTo(2));
            Assert.That(roundTripped.BaseExpression.Span[0].Name, Is.EqualTo("Brow_Angry"));
            Assert.That(roundTripped.BaseExpression.Span[0].Value, Is.EqualTo(0.645f).Within(1e-6f));
            Assert.That(roundTripped.BaseExpression.Span[1].Name, Is.EqualTo("Eye_Narrow"));
            Assert.That(roundTripped.BaseExpression.Span[1].Value, Is.EqualTo(0.2825f).Within(1e-6f));
        }

        /// <summary>
        /// ドメイン / JSON の正規化スケール (0..1) でベース表情を含むプロファイル JSON。
        /// </summary>
        private static string BuildProfileJsonWithNormalizedBaseExpression()
        {
            return @"{
                ""schemaVersion"": ""1.0"",
                ""layers"": [],
                ""expressions"": [],
                ""rendererPaths"": [""Body"", ""Face""],
                ""baseExpression"": {
                    ""blendShapes"": [
                        {""rendererPath"": ""Body"", ""name"": ""Brow_Angry"", ""value"": 0.645},
                        {""rendererPath"": ""Face"", ""name"": ""Eye_Narrow"", ""value"": 0.2825}
                    ]
                }
            }";
        }

        private static string BuildProfileJsonWithBaseExpression()
        {
            return @"{
                ""schemaVersion"": ""1.0"",
                ""layers"": [],
                ""expressions"": [],
                ""rendererPaths"": [""Body"", ""Face""],
                ""baseExpression"": {
                    ""blendShapes"": [
                        {""rendererPath"": ""Body"", ""name"": ""Brow_Angry"", ""value"": 64.5},
                        {""rendererPath"": ""Face"", ""name"": ""Eye_Narrow"", ""value"": 28.25}
                    ]
                }
            }";
        }

        private static ProfileSnapshotDto CreateProfileSnapshotDto()
        {
            return new ProfileSnapshotDto
            {
                schemaVersion = SystemTextJsonParser.SchemaVersionV2,
                layers = new List<LayerDefinitionDto>(),
                expressions = new List<ExpressionDto>(),
                rendererPaths = new List<string> { "Body", "Face" },
            };
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
                bones = new List<BoneSnapshotDto>(),
                rendererPaths = new List<string> { "Body", "Face" },
            };
        }

        private static ExpressionSnapshotDto GetBaseExpression(ProfileSnapshotDto dto)
        {
            var value = GetBaseExpressionField().GetValue(dto) as ExpressionSnapshotDto;
            Assert.That(value, Is.Not.Null,
                "ProfileSnapshotDto.baseExpression must be normalized to an empty ExpressionSnapshotDto when the JSON field is missing.");
            return value;
        }

        private static void SetBaseExpression(ProfileSnapshotDto dto, ExpressionSnapshotDto snapshot)
        {
            GetBaseExpressionField().SetValue(dto, snapshot);
        }

        private static FieldInfo GetBaseExpressionField()
        {
            var field = typeof(ProfileSnapshotDto).GetField(
                "baseExpression",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null,
                "ProfileSnapshotDto must expose root baseExpression for profile.json schema round-trip.");
            Assert.That(field.IsPublic, Is.True,
                "ProfileSnapshotDto.baseExpression must be public so JsonUtility can serialize the schema field.");
            Assert.That(field.FieldType, Is.EqualTo(typeof(ExpressionSnapshotDto)),
                "ProfileSnapshotDto.baseExpression must reuse ExpressionSnapshotDto.");
            return field;
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

        #region InputSources（必須フィールド契約）

        // layers[].inputSources は必須かつ非空。欠落 / 空配列は FormatException で中断し、
        // 例外メッセージに欠落フィールド名 'inputSources' を含める。Parser は暗黙に
        // input source を補完せず、他レイヤーの宣言を流用した補完も行わない。

        [Test]
        public void ParseProfile_LayerMissingInputSources_ThrowsFormatExceptionWithFieldNameInMessage()
        {
            var json = @"{
                ""schemaVersion"":""1.0"",
                ""layers"":[
                    {""name"":""emotion"",""priority"":0,""exclusionMode"":""lastWins""}
                ],
                ""expressions"":[]
            }";

            var ex = Assert.Throws<FormatException>(() => _parser.ParseProfile(json));
            Assert.IsTrue(
                ex.Message.Contains("inputSources"),
                $"例外メッセージには欠落フィールド名 'inputSources' を含める必要がある。実際: {ex.Message}");
        }

        [Test]
        public void ParseProfile_LayerInputSourcesEmptyArray_ThrowsFormatExceptionWithFieldNameInMessage()
        {
            // 空配列も「必須かつ非空」契約違反として FormatException になる。
            var json = @"{
                ""schemaVersion"":""1.0"",
                ""layers"":[
                    {""name"":""emotion"",""priority"":0,""exclusionMode"":""lastWins"",""inputSources"":[]}
                ],
                ""expressions"":[]
            }";

            var ex = Assert.Throws<FormatException>(() => _parser.ParseProfile(json));
            Assert.IsTrue(
                ex.Message.Contains("inputSources"),
                $"例外メッセージには欠落フィールド名 'inputSources' を含める必要がある。実際: {ex.Message}");
        }

        [Test]
        public void ParseProfile_OneLayerMissingInputSourcesAmongMany_ThrowsFormatException()
        {
            // 1 つでも欠落していればエラーとして扱う。
            var json = @"{
                ""schemaVersion"":""1.0"",
                ""layers"":[
                    {""name"":""emotion"",""priority"":0,""exclusionMode"":""lastWins"",""inputSources"":[{""id"":""input"",""weight"":1.0}]},
                    {""name"":""lipsync"",""priority"":1,""exclusionMode"":""blend""}
                ],
                ""expressions"":[]
            }";

            Assert.Throws<FormatException>(() => _parser.ParseProfile(json));
        }

        [Test]
        public void ParseProfile_OneLayerMissingInputSourcesAmongMany_MessageNamesMissingLayer()
        {
            // 他のレイヤーが正しく宣言されていても、1 つでも欠落していれば例外になる。
            // 他レイヤーの宣言を流用して暗黙補完することはない。
            var json = @"{
                ""schemaVersion"":""1.0"",
                ""layers"":[
                    {""name"":""emotion"",""priority"":0,""exclusionMode"":""lastWins"",""inputSources"":[{""id"":""input"",""weight"":1.0}]},
                    {""name"":""lipsync"",""priority"":1,""exclusionMode"":""blend""}
                ],
                ""expressions"":[]
            }";

            var ex = Assert.Throws<FormatException>(() => _parser.ParseProfile(json));
            Assert.IsTrue(
                ex.Message.Contains("inputSources"),
                $"例外メッセージには 'inputSources' を含める必要がある。実際: {ex.Message}");
            Assert.IsTrue(
                ex.Message.Contains("lipsync"),
                $"欠落したレイヤー名 'lipsync' を例外メッセージに含めることで、どのレイヤーの欠落かを診断可能にする。実際: {ex.Message}");
        }

        [Test]
        public void ParseProfile_LayerMissingInputSources_DoesNotReturnProfile()
        {
            // 契約: inputSources 欠落時、Parser は FacialProfile を構築せず必ず例外で中断する。
            // これにより FacialController 側で Aggregator / Registry を組み立てるための
            // LayerInputSources 情報が伝播せず、暗黙の Expression パイプラインフォールバックが成立し得ない。
            var json = @"{
                ""schemaVersion"":""1.0"",
                ""layers"":[
                    {""name"":""emotion"",""priority"":0,""exclusionMode"":""lastWins""}
                ],
                ""expressions"":[]
            }";

            FacialProfile? parsed = null;
            try
            {
                parsed = _parser.ParseProfile(json);
            }
            catch (FormatException)
            {
                // 期待どおり。
            }

            Assert.IsNull(
                parsed,
                "inputSources 欠落時に FacialProfile が構築されてはならない (暗黙フォールバック禁止)。");
        }

        [Test]
        public void ParseLayerInputSources_LayerMissingInputSources_ThrowsFormatException()
        {
            var json = @"{
                ""schemaVersion"":""1.0"",
                ""layers"":[
                    {""name"":""emotion"",""priority"":0,""exclusionMode"":""lastWins""}
                ],
                ""expressions"":[]
            }";

            Assert.Throws<FormatException>(() => _parser.ParseLayerInputSources(json));
        }

        [Test]
        public void ParseLayerInputSources_LayerMissingInputSources_ThrowsFormatExceptionWithFieldNameInMessage()
        {
            // ParseLayerInputSources の経路でも同じ契約が成立する必要がある。
            var json = @"{
                ""schemaVersion"":""1.0"",
                ""layers"":[
                    {""name"":""lipsync"",""priority"":1,""exclusionMode"":""blend""}
                ],
                ""expressions"":[]
            }";

            var ex = Assert.Throws<FormatException>(() => _parser.ParseLayerInputSources(json));
            Assert.IsTrue(
                ex.Message.Contains("inputSources"),
                $"例外メッセージには欠落フィールド名 'inputSources' を含める必要がある。実際: {ex.Message}");
        }

        [Test]
        public void ParseProfile_ValidInputSources_DoesNotThrow()
        {
            var json = @"{
                ""schemaVersion"":""1.0"",
                ""layers"":[
                    {""name"":""emotion"",""priority"":0,""exclusionMode"":""lastWins"",""inputSources"":[
                        {""id"":""input"",""weight"":1.0}
                    ]}
                ],
                ""expressions"":[]
            }";

            Assert.DoesNotThrow(() => _parser.ParseProfile(json));
        }

        [Test]
        public void ParseLayerInputSources_SingleEntry_ReturnsCorrectArray()
        {
            var json = @"{
                ""schemaVersion"":""1.0"",
                ""layers"":[
                    {""name"":""emotion"",""priority"":0,""exclusionMode"":""lastWins"",""inputSources"":[
                        {""id"":""input"",""weight"":0.75}
                    ]}
                ],
                ""expressions"":[]
            }";

            var result = _parser.ParseLayerInputSources(json);

            Assert.AreEqual(1, result.Length);
            Assert.AreEqual(1, result[0].Length);
            Assert.AreEqual("input", result[0][0].id);
            Assert.AreEqual(0.75f, result[0][0].weight);
        }

        [Test]
        public void ParseLayerInputSources_MultipleLayers_PreservesLayerOrder()
        {
            var json = @"{
                ""schemaVersion"":""1.0"",
                ""layers"":[
                    {""name"":""emotion"",""priority"":0,""exclusionMode"":""lastWins"",""inputSources"":[{""id"":""input"",""weight"":1.0}]},
                    {""name"":""lipsync"",""priority"":1,""exclusionMode"":""blend"",""inputSources"":[{""id"":""lipsync"",""weight"":1.0}]}
                ],
                ""expressions"":[]
            }";

            var result = _parser.ParseLayerInputSources(json);

            Assert.AreEqual(2, result.Length);
            Assert.AreEqual("input", result[0][0].id);
            Assert.AreEqual("lipsync", result[1][0].id);
        }

        [Test]
        public void ParseLayerInputSources_MultipleEntriesPerLayer_PreservesDeclarationOrder()
        {
            var json = @"{
                ""schemaVersion"":""1.0"",
                ""layers"":[
                    {""name"":""emotion"",""priority"":0,""exclusionMode"":""lastWins"",""inputSources"":[
                        {""id"":""input"",""weight"":0.5},
                        {""id"":""osc"",""weight"":0.5}
                    ]}
                ],
                ""expressions"":[]
            }";

            var result = _parser.ParseLayerInputSources(json);

            Assert.AreEqual(2, result[0].Length);
            Assert.AreEqual("input", result[0][0].id);
            Assert.AreEqual("osc", result[0][1].id);
        }

        #endregion

        #region InputSources（options 抽出）

        [Test]
        public void ParseLayerInputSources_OscOptions_CapturedAsRawJsonString()
        {
            // options は raw JSON 文字列として optionsJson に保持される。
            var json = @"{
                ""schemaVersion"":""1.0"",
                ""layers"":[
                    {""name"":""emotion"",""priority"":0,""exclusionMode"":""lastWins"",""inputSources"":[
                        {""id"":""osc"",""weight"":1.0,""options"":{""stalenessSeconds"":2.5}}
                    ]}
                ],
                ""expressions"":[]
            }";

            var result = _parser.ParseLayerInputSources(json);

            Assert.AreEqual(1, result.Length);
            Assert.AreEqual(1, result[0].Length);
            var entry = result[0][0];
            Assert.AreEqual("osc", entry.id);
            Assert.IsNotNull(entry.optionsJson);
            Assert.IsTrue(
                entry.optionsJson.Contains("stalenessSeconds"),
                $"optionsJson が stalenessSeconds を含むべき。実際: {entry.optionsJson}");
            Assert.IsTrue(
                entry.optionsJson.Contains("2.5"),
                $"optionsJson が値 2.5 を含むべき。実際: {entry.optionsJson}");
        }

#if FACIALCONTROL_HAS_OSC_MODULE
        [Test]
        public void ParseLayerInputSources_OptionsJson_CanBeDeserializedToTypedDto()
        {
            // 切り出した raw JSON が JsonUtility で OSC モジュールの DTO に逆シリアライズできること。
            var json = @"{
                ""schemaVersion"":""1.0"",
                ""layers"":[
                    {""name"":""emotion"",""priority"":0,""exclusionMode"":""lastWins"",""inputSources"":[
                        {""id"":""osc"",""weight"":1.0,""options"":{""stalenessSeconds"":2.5}}
                    ]}
                ],
                ""expressions"":[]
            }";

            var result = _parser.ParseLayerInputSources(json);
            var optionsJson = result[0][0].optionsJson;

            var options = JsonUtility.FromJson<OscOptionsDto>(optionsJson);

            Assert.IsNotNull(options);
            Assert.AreEqual(2.5f, options.stalenessSeconds);
        }
#endif

        [Test]
        public void ParseLayerInputSources_NestedOptionsObject_CapturedIncludingNestedBraces()
        {
            // ネストしたオブジェクトを含む options も brace マッチで正しく抽出できる。
            var json = @"{
                ""schemaVersion"":""1.0"",
                ""layers"":[
                    {""name"":""emotion"",""priority"":0,""exclusionMode"":""lastWins"",""inputSources"":[
                        {""id"":""x-custom"",""weight"":1.0,""options"":{""outer"":1,""nested"":{""inner"":42}}}
                    ]}
                ],
                ""expressions"":[]
            }";

            var result = _parser.ParseLayerInputSources(json);

            var optionsJson = result[0][0].optionsJson;
            Assert.IsTrue(optionsJson.Contains("outer"));
            Assert.IsTrue(optionsJson.Contains("nested"));
            Assert.IsTrue(optionsJson.Contains("inner"));
            Assert.IsTrue(optionsJson.Contains("42"));
        }

        [Test]
        public void ParseLayerInputSources_OptionsAbsent_OptionsJsonIsNullOrEmpty()
        {
            var json = @"{
                ""schemaVersion"":""1.0"",
                ""layers"":[
                    {""name"":""emotion"",""priority"":0,""exclusionMode"":""lastWins"",""inputSources"":[
                        {""id"":""input"",""weight"":1.0}
                    ]}
                ],
                ""expressions"":[]
            }";

            var result = _parser.ParseLayerInputSources(json);

            Assert.IsTrue(string.IsNullOrEmpty(result[0][0].optionsJson));
        }

        [Test]
        public void ParseLayerInputSources_OptionsWithStringValue_PreservesJsonString()
        {
            // options の値が文字列などの JSON エスケープを含む場合も raw JSON として保持される。
            var json = @"{
                ""schemaVersion"":""1.0"",
                ""layers"":[
                    {""name"":""emotion"",""priority"":0,""exclusionMode"":""lastWins"",""inputSources"":[
                        {""id"":""x-sensor"",""weight"":1.0,""options"":{""label"":""テスト""}}
                    ]}
                ],
                ""expressions"":[]
            }";

            var result = _parser.ParseLayerInputSources(json);

            Assert.IsNotNull(result[0][0].optionsJson);
            Assert.IsTrue(result[0][0].optionsJson.Contains("テスト"));
        }

        #endregion

        #region InputSources 検証エラー（警告 + skip / last-wins）

        // layers[].inputSources[] の不正エントリは例外にせず、警告を出して skip または last-wins を適用し、
        // 他の有効エントリと他レイヤーは正常に返す。
        // - id の regex 違反 ([a-zA-Z0-9_.-]{1,64} 不一致) → 警告 + skip
        // - 予約語 "legacy" (InputSourceId が拒否する識別子) → 警告 + skip
        // - 同一レイヤー内の重複 id → 警告 + 最後の出現を採用 (last-wins)
        // - slug 規約を満たす任意の識別子は parse 段階で受理し、解決失敗は実行時の
        //   InputSourceRegistry.TryResolve 側で処理する

        [Test]
        public void ParseLayerInputSources_IdViolatesRegex_LogsWarningAndSkips()
        {
            var json = @"{
                ""schemaVersion"":""1.0"",
                ""layers"":[
                    {""name"":""emotion"",""priority"":0,""exclusionMode"":""lastWins"",""inputSources"":[
                        {""id"":""invalid id with space"",""weight"":1.0},
                        {""id"":""input"",""weight"":1.0}
                    ]}
                ],
                ""expressions"":[]
            }";

            LogAssert.Expect(LogType.Warning, new Regex("inputSources.*invalid id with space"));

            var result = _parser.ParseLayerInputSources(json);

            Assert.AreEqual(1, result.Length);
            Assert.AreEqual(1, result[0].Length, "regex 違反エントリは skip されること");
            Assert.AreEqual("input", result[0][0].id);
        }

        [Test]
        public void ParseLayerInputSources_IdIsForbiddenReservedWord_LogsWarningAndSkips()
        {
            // InputSourceId は予約語 "legacy" を受理しない。他の有効エントリは保持される。
            var json = @"{
                ""schemaVersion"":""1.0"",
                ""layers"":[
                    {""name"":""emotion"",""priority"":0,""exclusionMode"":""lastWins"",""inputSources"":[
                        {""id"":""legacy"",""weight"":1.0},
                        {""id"":""osc"",""weight"":1.0}
                    ]}
                ],
                ""expressions"":[]
            }";

            LogAssert.Expect(LogType.Warning, new Regex("inputSources.*legacy"));

            var result = _parser.ParseLayerInputSources(json);

            Assert.AreEqual(1, result[0].Length);
            Assert.AreEqual("osc", result[0][0].id);
        }

        [Test]
        public void ParseLayerInputSources_OnlyForbiddenReservedWordId_ReturnsEmptyLayerEntries()
        {
            // 予約語 "legacy" だけを宣言した場合、宣言自体は存在するため schema チェックは通り
            // FormatException にはならないが、InputSourceId の検証で弾かれ警告 + skip となる。
            // 有効な input source としては 0 件であり、暗黙のフォールバック実体も生成されない。
            var json = @"{
                ""schemaVersion"":""1.0"",
                ""layers"":[
                    {""name"":""emotion"",""priority"":0,""exclusionMode"":""lastWins"",""inputSources"":[
                        {""id"":""legacy"",""weight"":1.0}
                    ]}
                ],
                ""expressions"":[]
            }";

            LogAssert.Expect(LogType.Warning, new Regex("legacy"));

            var result = _parser.ParseLayerInputSources(json);

            Assert.AreEqual(1, result.Length, "レイヤー数は保たれる。");
            Assert.AreEqual(
                0,
                result[0].Length,
                "予約語エントリは無効識別子として skip され、有効 input source としては 0 件となる (暗黙フォールバックなし)。");
        }

        [Test]
        public void InputSourceId_TryParse_ForbiddenReservedWord_ReturnsFalse()
        {
            // 下位レイヤー契約: 予約語 "legacy" は InputSourceId として受理されない。
            // これにより JSON からも `"id": "legacy"` による input source 生成が防がれる。
            bool accepted = InputSourceId.TryParse("legacy", out _);

            Assert.IsFalse(
                accepted,
                "InputSourceId.TryParse は予約語 'legacy' を拒否する必要がある。");
        }

        [Test]
        public void ParseLayerInputSources_IdExceedsMaxLength_LogsWarningAndSkips()
        {
            // 65 文字 ID は regex {1,64} に違反する。
            string tooLong = new string('a', 65);
            var json = @"{
                ""schemaVersion"":""1.0"",
                ""layers"":[
                    {""name"":""emotion"",""priority"":0,""exclusionMode"":""lastWins"",""inputSources"":[
                        {""id"":""" + tooLong + @""",""weight"":1.0},
                        {""id"":""input"",""weight"":1.0}
                    ]}
                ],
                ""expressions"":[]
            }";

            LogAssert.Expect(LogType.Warning, new Regex("inputSources"));

            var result = _parser.ParseLayerInputSources(json);

            Assert.AreEqual(1, result[0].Length);
            Assert.AreEqual("input", result[0][0].id);
        }

        [Test]
        public void ParseLayerInputSources_ArbitrarySlugIds_AreAcceptedWithoutWarning()
        {
            // `osc` / `my-binding` / `x-custom-sensor` のように slug 規約 (regex) を満たす
            // 任意の識別子は parse 段階で skip されない。
            var json = @"{
                ""schemaVersion"":""1.0"",
                ""layers"":[
                    {""name"":""emotion"",""priority"":0,""exclusionMode"":""lastWins"",""inputSources"":[
                        {""id"":""input"",""weight"":0.5},
                        {""id"":""x-custom-sensor"",""weight"":0.25},
                        {""id"":""my-binding"",""weight"":0.25}
                    ]}
                ],
                ""expressions"":[]
            }";

            var result = _parser.ParseLayerInputSources(json);

            Assert.AreEqual(3, result[0].Length);
            Assert.AreEqual("input", result[0][0].id);
            Assert.AreEqual("x-custom-sensor", result[0][1].id);
            Assert.AreEqual("my-binding", result[0][2].id);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ParseLayerInputSources_DuplicateId_LogsWarningAndKeepsLast()
        {
            var json = @"{
                ""schemaVersion"":""1.0"",
                ""layers"":[
                    {""name"":""emotion"",""priority"":0,""exclusionMode"":""lastWins"",""inputSources"":[
                        {""id"":""osc"",""weight"":0.25,""options"":{""stalenessSeconds"":1.0}},
                        {""id"":""input"",""weight"":1.0},
                        {""id"":""osc"",""weight"":0.75,""options"":{""stalenessSeconds"":2.5}}
                    ]}
                ],
                ""expressions"":[]
            }";

            LogAssert.Expect(LogType.Warning, new Regex("重複.*osc|osc.*重複|duplicate.*osc|osc.*duplicate"));

            var result = _parser.ParseLayerInputSources(json);

            Assert.AreEqual(2, result[0].Length, "重複 id は 1 エントリに畳まれる (last-wins)");

            var oscEntry = result[0].Single(e => e.id == "osc");
            Assert.AreEqual(0.75f, oscEntry.weight, "最後の出現の weight が採用される");
            Assert.IsTrue(oscEntry.optionsJson.Contains("2.5"),
                $"最後の出現の options が採用される。実際: {oscEntry.optionsJson}");
        }

        [Test]
        public void ParseLayerInputSources_DuplicateId_PreservesRelativeOrderOfLastOccurrence()
        {
            // 入力: [A1, B, A2, C]  → 出力: [B, A2, C] (A は最後の出現位置で保持)
            var json = @"{
                ""schemaVersion"":""1.0"",
                ""layers"":[
                    {""name"":""emotion"",""priority"":0,""exclusionMode"":""lastWins"",""inputSources"":[
                        {""id"":""osc"",""weight"":0.1},
                        {""id"":""lipsync"",""weight"":0.2},
                        {""id"":""osc"",""weight"":0.9},
                        {""id"":""input"",""weight"":1.0}
                    ]}
                ],
                ""expressions"":[]
            }";

            LogAssert.Expect(LogType.Warning, new Regex("osc"));

            var result = _parser.ParseLayerInputSources(json);

            Assert.AreEqual(3, result[0].Length);
            Assert.AreEqual("lipsync", result[0][0].id);
            Assert.AreEqual("osc", result[0][1].id);
            Assert.AreEqual(0.9f, result[0][1].weight);
            Assert.AreEqual("input", result[0][2].id);
        }

        [Test]
        public void ParseLayerInputSources_DuplicateIdAcrossLayers_NotTreatedAsDuplicate()
        {
            // 異なるレイヤーなら同 id は重複ではない。
            var json = @"{
                ""schemaVersion"":""1.0"",
                ""layers"":[
                    {""name"":""emotion"",""priority"":0,""exclusionMode"":""lastWins"",""inputSources"":[
                        {""id"":""osc"",""weight"":1.0}
                    ]},
                    {""name"":""lipsync"",""priority"":1,""exclusionMode"":""blend"",""inputSources"":[
                        {""id"":""osc"",""weight"":1.0}
                    ]}
                ],
                ""expressions"":[]
            }";

            var result = _parser.ParseLayerInputSources(json);

            Assert.AreEqual(2, result.Length);
            Assert.AreEqual(1, result[0].Length);
            Assert.AreEqual(1, result[1].Length);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ParseLayerInputSources_MixedInvalidEntries_DoesNotAbortLoad()
        {
            // 1 つのレイヤーに regex 違反 / 重複 が混在していても parse は成功する。
            // syntactic に valid な slug (`unknown-thing`) は skip されず保持される。
            var json = @"{
                ""schemaVersion"":""1.0"",
                ""layers"":[
                    {""name"":""emotion"",""priority"":0,""exclusionMode"":""lastWins"",""inputSources"":[
                        {""id"":""bad id"",""weight"":1.0},
                        {""id"":""unknown-thing"",""weight"":1.0},
                        {""id"":""osc"",""weight"":0.3},
                        {""id"":""osc"",""weight"":0.7}
                    ]}
                ],
                ""expressions"":[]
            }";

            LogAssert.Expect(LogType.Warning, new Regex("bad id"));
            LogAssert.Expect(LogType.Warning, new Regex("osc"));

            InputSourceDto[][] result = null;
            Assert.DoesNotThrow(() => result = _parser.ParseLayerInputSources(json));

            Assert.IsNotNull(result);
            Assert.AreEqual(1, result.Length);
            Assert.AreEqual(2, result[0].Length, "regex 違反のみ skip、その他は保持される");
            Assert.AreEqual("unknown-thing", result[0][0].id);
            Assert.AreEqual("osc", result[0][1].id);
            Assert.AreEqual(0.7f, result[0][1].weight);
        }

        #endregion

        #region Overlays（slots / suppress / snapshot）

        [Test]
        public void ParseProfile_OverlayWithExpressionIdField_ThrowsFormatException()
        {
            // overlay の参照は slot + suppress / snapshot で表現する。expressionId フィールドは受理しない。
            var ex = Assert.Throws<FormatException>(() =>
                _parser.ParseProfile(ExpressionIdOverlayJson));

            StringAssert.Contains("expressionId", ex.Message);
            StringAssert.Contains("defaultOverlays[0].expressionId", ex.Message);
        }

        [Test]
        public void ParseProfile_OverlaySuppressWithSnapshot_ThrowsFormatException()
        {
            var ex = Assert.Throws<FormatException>(() =>
                _parser.ParseProfile(SuppressWithSnapshotOverlayJson));

            StringAssert.Contains("blink", ex.Message);
            StringAssert.Contains("suppress=true", ex.Message);
            StringAssert.Contains("snapshot", ex.Message);
        }

        [Test]
        public void SerializeParseProfile_ThreeOverlayStates_PreservesEquivalentProfile()
        {
            var profile = BuildThreeStateProfile();

            var json = _parser.SerializeProfile(profile);
            StringAssert.Contains(@"""slots""", json);
            StringAssert.Contains(@"""suppress""", json);
            StringAssert.Contains(@"""snapshot""", json);
            Assert.IsFalse(json.Contains("expressionId"));

            var parsed = _parser.ParseProfile(json);

            AssertSlots(parsed, "blink", "blush", "sparkle", "default_blink");

            var anger = parsed.FindExpressionById("anger");
            Assert.IsTrue(anger.HasValue);
            Assert.AreEqual(3, anger.Value.Overlays.Length);

            var overlays = anger.Value.Overlays.Span;
            AssertDefaultFallback(overlays[0], "blink");
            AssertSuppress(overlays[1], "blush");
            AssertSnapshotOverride(overlays[2], "sparkle", "SparkleOverlay", "Face", 0.75f);

            Assert.AreEqual(1, parsed.DefaultOverlays.Length);
            AssertSnapshotOverride(
                parsed.DefaultOverlays.Span[0],
                "default_blink",
                "DefaultBlink",
                "Face",
                1f);
        }

        [Test]
        public void ParseProfile_WithoutSlots_NormalizesSlotsToEmpty()
        {
            var dto = _parser.ParseProfileSnapshotV2(ProfileWithoutSlotsJson);
            Assert.IsNotNull(dto.slots);
            Assert.AreEqual(0, dto.slots.Count);

            var profile = _parser.ParseProfile(ProfileWithoutSlotsJson);
            Assert.AreEqual(0, profile.Slots.Length);
        }

        [Test]
        public void ParseProfile_SampleProfileJson_RoundTripsEquivalentOverlaySchema()
        {
            var profile = _parser.ParseProfile(JsonSchemaDefinition.SampleProfileJson);

            AssertSlots(profile, "blink");

            var smile = profile.FindExpressionById("smile");
            Assert.IsTrue(smile.HasValue);
            Assert.AreEqual(1, smile.Value.Overlays.Length);
            AssertSnapshotOverride(
                smile.Value.Overlays.Span[0],
                "blink",
                "Fcl_EYE_Close_L",
                "Face",
                1f);

            var smileClosedEye = profile.FindExpressionById("smile_closed_eye");
            Assert.IsTrue(smileClosedEye.HasValue);
            Assert.AreEqual(1, smileClosedEye.Value.Overlays.Length);
            AssertSuppress(smileClosedEye.Value.Overlays.Span[0], "blink");

            Assert.AreEqual(1, profile.DefaultOverlays.Length);
            AssertDefaultFallback(profile.DefaultOverlays.Span[0], "blink");

            var serialized = _parser.SerializeProfile(profile);
            var reparsed = _parser.ParseProfile(serialized);

            AssertSlots(reparsed, "blink");
            AssertDefaultFallback(reparsed.DefaultOverlays.Span[0], "blink");
            AssertSuppress(
                reparsed.FindExpressionById("smile_closed_eye").Value.Overlays.Span[0],
                "blink");
            AssertSnapshotOverride(
                reparsed.FindExpressionById("smile").Value.Overlays.Span[0],
                "blink",
                "Fcl_EYE_Close_L",
                "Face",
                1f);
        }

        private static FacialProfile BuildThreeStateProfile()
        {
            var layers = new[]
            {
                new LayerDefinition("emotion", 0, ExclusionMode.LastWins),
            };
            var layerInputSources = new[]
            {
                new[] { new InputSourceDeclaration("input", 1f, null) },
            };
            var expressionOverlays = new[]
            {
                new OverlaySlotBinding("blink", suppress: false, snapshot: null),
                new OverlaySlotBinding("blush", suppress: true, snapshot: null),
                new OverlaySlotBinding(
                    "sparkle",
                    suppress: false,
                    snapshot: CreateSnapshot("sparkle-override", "SparkleOverlay", 0.75f)),
            };
            var expressions = new[]
            {
                new Expression(
                    "anger",
                    "Anger",
                    "emotion",
                    Expression.DefaultTransitionDuration,
                    default,
                    new[] { new BlendShapeMapping("Anger", 1f, "Face") },
                    expressionOverlays),
            };
            var defaultOverlays = new[]
            {
                new OverlaySlotBinding(
                    "default_blink",
                    suppress: false,
                    snapshot: CreateSnapshot("default-blink", "DefaultBlink", 1f)),
            };

            return new FacialProfile(
                SystemTextJsonParser.SchemaVersionV2,
                layers,
                expressions,
                rendererPaths: new[] { "Face" },
                layerInputSources: layerInputSources,
                defaultOverlays: defaultOverlays,
                slots: new[] { "blink", "blush", "sparkle", "default_blink" });
        }

        private static ExpressionSnapshot CreateSnapshot(string id, string blendShapeName, float value)
        {
            return new ExpressionSnapshot(
                id,
                transitionDuration: 0.08f,
                transitionCurvePreset: TransitionCurvePreset.Linear,
                blendShapes: new[]
                {
                    new BlendShapeSnapshot("Face", blendShapeName, value),
                },
                bones: null,
                rendererPaths: new[] { "Face" });
        }

        private static void AssertSlots(FacialProfile profile, params string[] expected)
        {
            Assert.AreEqual(expected.Length, profile.Slots.Length);
            var slots = profile.Slots.Span;
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.AreEqual(expected[i], slots[i]);
            }
        }

        private static void AssertDefaultFallback(OverlaySlotBinding binding, string slot)
        {
            Assert.AreEqual(slot, binding.Slot);
            Assert.IsFalse(binding.Suppress);
            Assert.IsFalse(binding.Snapshot.HasValue);
            Assert.IsTrue(binding.IsDefaultFallback);
        }

        private static void AssertSuppress(OverlaySlotBinding binding, string slot)
        {
            Assert.AreEqual(slot, binding.Slot);
            Assert.IsTrue(binding.Suppress);
            Assert.IsFalse(binding.Snapshot.HasValue);
            Assert.IsFalse(binding.IsDefaultFallback);
        }

        private static void AssertSnapshotOverride(
            OverlaySlotBinding binding,
            string slot,
            string expectedBlendShapeName,
            string expectedRendererPath,
            float expectedValue)
        {
            Assert.AreEqual(slot, binding.Slot);
            Assert.IsFalse(binding.Suppress);
            Assert.IsTrue(binding.Snapshot.HasValue);
            Assert.IsFalse(binding.IsDefaultFallback);

            var snapshot = binding.Snapshot.Value;
            Assert.AreEqual(1, snapshot.BlendShapes.Length);
            Assert.AreEqual(1, snapshot.RendererPaths.Length);
            Assert.AreEqual(expectedRendererPath, snapshot.RendererPaths.Span[0]);

            var blendShape = snapshot.BlendShapes.Span[0];
            Assert.AreEqual(expectedRendererPath, blendShape.RendererPath);
            Assert.AreEqual(expectedBlendShapeName, blendShape.Name);
            Assert.AreEqual(expectedValue, blendShape.Value, 0.0001f);
        }

        private const string ExpressionIdOverlayJson = @"{
    ""schemaVersion"": ""1.0"",
    ""slots"": [""blink""],
    ""layers"": [
        {""name"": ""emotion"", ""priority"": 0, ""exclusionMode"": ""lastWins"", ""inputSources"": [
            {""id"": ""input"", ""weight"": 1.0}
        ]}
    ],
    ""expressions"": [],
    ""rendererPaths"": [],
    ""defaultOverlays"": [
        {""slot"": ""blink"", ""expressionId"": ""blink_overlay""}
    ]
}";

        private const string SuppressWithSnapshotOverlayJson = @"{
    ""schemaVersion"": ""1.0"",
    ""slots"": [""blink""],
    ""layers"": [
        {""name"": ""emotion"", ""priority"": 0, ""exclusionMode"": ""lastWins"", ""inputSources"": [
            {""id"": ""input"", ""weight"": 1.0}
        ]}
    ],
    ""expressions"": [],
    ""rendererPaths"": [""Face""],
    ""defaultOverlays"": [
        {""slot"": ""blink"", ""suppress"": true, ""snapshot"": {
            ""transitionDuration"": 0.08,
            ""transitionCurvePreset"": ""Linear"",
            ""blendShapes"": [
                {""rendererPath"": ""Face"", ""name"": ""Blink"", ""value"": 1.0}
            ],
            ""bones"": [],
            ""rendererPaths"": [""Face""],
            ""overlays"": []
        }}
    ]
}";

        private const string ProfileWithoutSlotsJson = @"{
    ""schemaVersion"": ""1.0"",
    ""layers"": [],
    ""expressions"": [],
    ""rendererPaths"": [],
    ""defaultOverlays"": []
}";

        #endregion

        #region Gaze セクション

        [Test]
        public void ParseProfileSnapshotV2_GazeChannelWithDistinctLeftRight_PreservesValues()
        {
            var json = @"{
                ""schemaVersion"": ""1.0"",
                ""layers"": [],
                ""expressions"": [],
                ""rendererPaths"": [],
                ""gaze"": { ""channels"": [
                    {
                        ""id"": ""eye_look"",
                        ""useDistinctLeftRight"": true,
                        ""sourceIdLeft"": ""input:eye_look.left"",
                        ""sourceIdRight"": ""osc:eye_look.right"",
                        ""leftEyeBonePath"": ""Head/LeftEye"",
                        ""leftEyeInitialRotation"": {""x"":0,""y"":1,""z"":2},
                        ""leftEyeYawAxisLocal"": {""x"":0,""y"":1,""z"":0},
                        ""leftEyePitchAxisLocal"": {""x"":1,""y"":0,""z"":0},
                        ""rightEyeBonePath"": ""Head/RightEye"",
                        ""rightEyeInitialRotation"": {""x"":3,""y"":4,""z"":5},
                        ""rightEyeYawAxisLocal"": {""x"":0,""y"":1,""z"":0},
                        ""rightEyePitchAxisLocal"": {""x"":1,""y"":0,""z"":0},
                        ""lookUpAngle"": 16,
                        ""lookDownAngle"": 8,
                        ""outerYawAngle"": 17,
                        ""innerYawAngle"": 7
                    }
                ] }
            }";

            var dto = _parser.ParseProfileSnapshotV2(json);

            Assert.AreEqual(1, dto.gaze.channels.Count);
            var cfg = dto.gaze.channels[0];
            Assert.AreEqual("eye_look", cfg.id);
            Assert.AreEqual(true, cfg.useDistinctLeftRight);
            Assert.AreEqual("input:eye_look.left", cfg.sourceIdLeft);
            Assert.AreEqual("osc:eye_look.right", cfg.sourceIdRight);
            Assert.AreEqual("Head/LeftEye", cfg.leftEyeBonePath);
            Assert.AreEqual("Head/RightEye", cfg.rightEyeBonePath);
            Assert.AreEqual(1f, cfg.leftEyeInitialRotation.y);
            Assert.AreEqual(4f, cfg.rightEyeInitialRotation.y);
            Assert.AreEqual(16f, cfg.lookUpAngle);
            Assert.AreEqual(8f, cfg.lookDownAngle);
            Assert.AreEqual(17f, cfg.outerYawAngle);
            Assert.AreEqual(7f, cfg.innerYawAngle);
        }

        [Test]
        public void ParseProfileSnapshotV2_GazeChannelWithoutDistinctLeftRight_ParsesChannel()
        {
            var dto = _parser.ParseProfileSnapshotV2(GazeChannelWithoutDistinctLeftRightJson);

            Assert.AreEqual(1, dto.gaze.channels.Count);
            Assert.AreEqual("look_left", dto.gaze.channels[0].id);
            Assert.AreEqual(false, dto.gaze.channels[0].useDistinctLeftRight);
        }

        [Test]
        public void ParseProfileSnapshotV2_GazeChannelWithProviderSlug_PreservesValues()
        {
            var json = @"{
                ""schemaVersion"": ""1.0"",
                ""layers"": [],
                ""expressions"": [],
                ""rendererPaths"": [],
                ""gaze"": { ""channels"": [{
                    ""id"": ""gaze"",
                    ""providerSlug"": ""osc"",
                    ""useDistinctLeftRight"": true,
                    ""sourceIdLeft"": ""osc:gaze.left"",
                    ""sourceIdRight"": ""osc:gaze.right"",
                    ""leftEyeBonePath"": ""Head/LeftEye"",
                    ""rightEyeBonePath"": ""Head/RightEye"",
                    ""lookUpAngle"": 21,
                    ""lookDownAngle"": 11
                }] }
            }";

            var dto = _parser.ParseProfileSnapshotV2(json);

            Assert.That(dto.gaze, Is.Not.Null);
            Assert.That(dto.gaze.channels, Has.Count.EqualTo(1));
            Assert.That(dto.gaze.channels[0].id, Is.EqualTo("gaze"));
            Assert.That(dto.gaze.channels[0].providerSlug, Is.EqualTo("osc"));
            Assert.That(dto.gaze.channels[0].sourceIdLeft, Is.EqualTo("osc:gaze.left"));
            Assert.That(dto.gaze.channels[0].sourceIdRight, Is.EqualTo("osc:gaze.right"));
            Assert.That(dto.gaze.channels[0].lookUpAngle, Is.EqualTo(21f));
            Assert.That(dto.gaze.channels[0].lookDownAngle, Is.EqualTo(11f));
        }

        [Test]
        public void ParseProfileSnapshotV2_MissingGaze_NormalizesSectionAndChannels()
        {
            var dto = _parser.ParseProfileSnapshotV2(@"{
                ""schemaVersion"": ""1.0"",
                ""layers"": [], ""expressions"": [], ""rendererPaths"": []
            }");

            Assert.That(dto.gaze, Is.Not.Null);
            Assert.That(dto.gaze.channels, Is.Not.Null);
            Assert.That(dto.gaze.channels, Is.Empty);
        }

        [Test]
        public void ParseProfileSnapshotV2_ObsoleteGazeConfigsKey_IsIgnoredAndNormalizesGaze()
        {
            // 現行スキーマ外の "gaze_configs" キーは読み捨てられ（警告のみ）、gaze セクションは通常どおり正規化される。
            var json = @"{
                ""schemaVersion"": ""1.0"",
                ""layers"": [], ""expressions"": [], ""rendererPaths"": [],
                ""gaze_configs"": []
            }";

            LogAssert.Expect(LogType.Warning, new Regex("gaze"));

            ProfileSnapshotDto dto = null;
            Assert.DoesNotThrow(() => dto = _parser.ParseProfileSnapshotV2(json));

            Assert.That(dto, Is.Not.Null);
            Assert.That(dto.gaze, Is.Not.Null);
            Assert.That(dto.gaze.channels, Is.Not.Null);
            Assert.That(dto.gaze.channels, Is.Empty);
        }

        private const string GazeChannelWithoutDistinctLeftRightJson = @"{
    ""schemaVersion"": ""1.0"",
    ""layers"": [],
    ""expressions"": [],
    ""rendererPaths"": [],
    ""gaze"": { ""channels"": [
        {
            ""id"": ""look_left"",
            ""leftEyeBonePath"": ""Head/LeftEye"",
            ""leftEyeInitialRotation"": {""x"":0,""y"":0,""z"":0},
            ""leftEyeYawAxisLocal"": {""x"":0,""y"":1,""z"":0},
            ""leftEyePitchAxisLocal"": {""x"":1,""y"":0,""z"":0},
            ""rightEyeBonePath"": ""Head/RightEye"",
            ""rightEyeInitialRotation"": {""x"":0,""y"":0,""z"":0},
            ""rightEyeYawAxisLocal"": {""x"":0,""y"":1,""z"":0},
            ""rightEyePitchAxisLocal"": {""x"":1,""y"":0,""z"":0},
            ""lookUpAngle"": 15,
            ""lookDownAngle"": 9,
            ""outerYawAngle"": 15,
            ""innerYawAngle"": 18
        }
    ] }
}";

        #endregion

        #region JsonSchemaDefinition 定数

        [Test]
        public void JsonSchemaDefinition_Layer_InputSourcesFieldName_IsInputSources()
        {
            Assert.AreEqual("inputSources", JsonSchemaDefinition.Profile.Layer.InputSources);
        }

        [Test]
        public void JsonSchemaDefinition_InputSource_FieldNames_Correct()
        {
            Assert.AreEqual("id", JsonSchemaDefinition.Profile.InputSource.Id);
            Assert.AreEqual("weight", JsonSchemaDefinition.Profile.InputSource.Weight);
            Assert.AreEqual("options", JsonSchemaDefinition.Profile.InputSource.Options);
        }

        #endregion
    }
}
