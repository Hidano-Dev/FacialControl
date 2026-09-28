using System;
using NUnit.Framework;
using Hidano.FacialControl.Domain.Models;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.EditMode.Domain
{
    /// <summary>
    /// <see cref="FacialProfile"/> の構築・バリデーション・防御的コピー・検索 API を検証する。
    /// レイヤー参照のフォールバック、RendererPaths、
    /// ベース表情 (<see cref="FacialProfile.BaseExpression"/>) の保持契約、
    /// および Slots 宣言と overlay 参照の整合性検証 (<see cref="FacialProfile.ValidateSlotReferences"/>) を含む。
    /// </summary>
    [TestFixture]
    [SmallTest]
    public class FacialProfileTests : SizedTestFixture
    {
        // --- ヘルパー ---

        private static LayerDefinition[] CreateDefaultLayers()
        {
            return new[]
            {
                new LayerDefinition("emotion", 0, ExclusionMode.LastWins),
                new LayerDefinition("lipsync", 1, ExclusionMode.Blend),
                new LayerDefinition("eye", 2, ExclusionMode.LastWins)
            };
        }

        private static LayerDefinition[] CreateEmotionOnlyLayers()
        {
            return new[]
            {
                new LayerDefinition("emotion", 0, ExclusionMode.LastWins),
            };
        }

        private static Expression CreateExpression(
            string id = "expr-1",
            string name = "smile",
            string layer = "emotion")
        {
            return new Expression(id, name, layer);
        }

        private static Expression CreateExpressionWithOverlay(string slot)
        {
            return new Expression(
                id: "smile",
                name: "Smile",
                layer: "emotion",
                transitionDuration: Expression.DefaultTransitionDuration,
                transitionCurve: default,
                blendShapeValues: null,
                overlays: new[]
                {
                    new OverlaySlotBinding(slot, suppress: false, snapshot: null),
                });
        }

        private static void AssertInvalidReference(
            InvalidSlotReference reference,
            string slot,
            string reason)
        {
            Assert.AreEqual(slot, reference.Slot);
            Assert.AreEqual(reason, reference.Reason);
        }

        // --- 正常系: 構築 ---

        [Test]
        public void Constructor_ValidParameters_CreatesInstance()
        {
            var layers = CreateDefaultLayers();
            var expressions = new[] { CreateExpression() };

            var profile = new FacialProfile("1.0", layers, expressions);

            Assert.AreEqual("1.0", profile.SchemaVersion);
            Assert.AreEqual(3, profile.Layers.Length);
            Assert.AreEqual(1, profile.Expressions.Length);
        }

        [Test]
        public void Constructor_EmptyExpressions_CreatesInstance()
        {
            var layers = CreateDefaultLayers();

            var profile = new FacialProfile("1.0", layers, Array.Empty<Expression>());

            Assert.AreEqual(0, profile.Expressions.Length);
        }

        [Test]
        public void Constructor_EmptyLayers_CreatesInstance()
        {
            var profile = new FacialProfile("1.0", Array.Empty<LayerDefinition>(), Array.Empty<Expression>());

            Assert.AreEqual(0, profile.Layers.Length);
        }

        [Test]
        public void Constructor_DefaultExpressions_IsEmpty()
        {
            var layers = CreateDefaultLayers();

            var profile = new FacialProfile("1.0", layers);

            Assert.AreEqual(0, profile.Expressions.Length);
        }

        [Test]
        public void Constructor_DefaultLayers_IsEmpty()
        {
            var profile = new FacialProfile("1.0");

            Assert.AreEqual(0, profile.Layers.Length);
        }

        [Test]
        public void Constructor_MultipleExpressions_CreatesInstance()
        {
            var layers = CreateDefaultLayers();
            var expressions = new[]
            {
                CreateExpression("id-1", "smile", "emotion"),
                CreateExpression("id-2", "wink", "eye"),
                CreateExpression("id-3", "talk", "lipsync")
            };

            var profile = new FacialProfile("1.0", layers, expressions);

            Assert.AreEqual(3, profile.Expressions.Length);
        }

        // --- バリデーション ---

        [Test]
        public void Constructor_NullSchemaVersion_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new FacialProfile(null, CreateDefaultLayers()));
        }

        [Test]
        public void Constructor_EmptySchemaVersion_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() =>
                new FacialProfile("", CreateDefaultLayers()));
        }

        [Test]
        public void Constructor_WhitespaceSchemaVersion_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() =>
                new FacialProfile("   ", CreateDefaultLayers()));
        }

        [Test]
        public void Constructor_NullLayers_TreatedAsEmpty()
        {
            var profile = new FacialProfile("1.0", null);

            Assert.AreEqual(0, profile.Layers.Length);
        }

        [Test]
        public void Constructor_NullExpressions_TreatedAsEmpty()
        {
            var profile = new FacialProfile("1.0", CreateDefaultLayers(), null);

            Assert.AreEqual(0, profile.Expressions.Length);
        }

        // --- 防御的コピー ---

        [Test]
        public void Layers_IsDefensiveCopy_OriginalArrayModificationDoesNotAffect()
        {
            var layers = CreateDefaultLayers();
            var profile = new FacialProfile("1.0", layers);

            layers[0] = new LayerDefinition("modified", 99, ExclusionMode.Blend);

            Assert.AreEqual("emotion", profile.Layers.Span[0].Name);
        }

        [Test]
        public void Expressions_IsDefensiveCopy_OriginalArrayModificationDoesNotAffect()
        {
            var layers = CreateDefaultLayers();
            var expressions = new[] { CreateExpression("id-1", "smile", "emotion") };
            var profile = new FacialProfile("1.0", layers, expressions);

            expressions[0] = CreateExpression("id-modified", "modified", "emotion");

            Assert.AreEqual("id-1", profile.Expressions.Span[0].Id);
        }

        // --- レイヤー検証 ---

        [Test]
        public void ValidateLayerReferences_AllExpressionsReferenceDefinedLayers_ReturnsEmptyList()
        {
            var layers = CreateDefaultLayers();
            var expressions = new[]
            {
                CreateExpression("id-1", "smile", "emotion"),
                CreateExpression("id-2", "wink", "eye")
            };
            var profile = new FacialProfile("1.0", layers, expressions);

            var invalidRefs = profile.ValidateLayerReferences();

            Assert.AreEqual(0, invalidRefs.Count);
        }

        [Test]
        public void ValidateLayerReferences_ExpressionReferencesUndefinedLayer_ReturnsInvalidReference()
        {
            var layers = CreateDefaultLayers();
            var expressions = new[]
            {
                CreateExpression("id-1", "smile", "nonexistent")
            };
            var profile = new FacialProfile("1.0", layers, expressions);

            var invalidRefs = profile.ValidateLayerReferences();

            Assert.AreEqual(1, invalidRefs.Count);
            Assert.AreEqual("id-1", invalidRefs[0].ExpressionId);
            Assert.AreEqual("nonexistent", invalidRefs[0].ReferencedLayer);
        }

        [Test]
        public void ValidateLayerReferences_MultipleInvalidReferences_ReturnsAll()
        {
            var layers = CreateDefaultLayers();
            var expressions = new[]
            {
                CreateExpression("id-1", "expr1", "unknown1"),
                CreateExpression("id-2", "expr2", "unknown2"),
                CreateExpression("id-3", "expr3", "emotion")
            };
            var profile = new FacialProfile("1.0", layers, expressions);

            var invalidRefs = profile.ValidateLayerReferences();

            Assert.AreEqual(2, invalidRefs.Count);
        }

        // --- フォールバックロジック ---

        [Test]
        public void GetEffectiveLayer_ExpressionWithDefinedLayer_ReturnsOriginalLayer()
        {
            var layers = CreateDefaultLayers();
            var expression = CreateExpression("id-1", "smile", "emotion");
            var profile = new FacialProfile("1.0", layers, new[] { expression });

            var effectiveLayer = profile.GetEffectiveLayer(expression);

            Assert.AreEqual("emotion", effectiveLayer);
        }

        [Test]
        public void GetEffectiveLayer_ExpressionWithUndefinedLayer_FallsBackToEmotion()
        {
            var layers = CreateDefaultLayers();
            var expression = CreateExpression("id-1", "smile", "nonexistent");
            var profile = new FacialProfile("1.0", layers, new[] { expression });

            var effectiveLayer = profile.GetEffectiveLayer(expression);

            Assert.AreEqual("emotion", effectiveLayer);
        }

        [Test]
        public void GetEffectiveLayer_NoEmotionLayerDefined_ReturnsFirstLayer()
        {
            var layers = new[]
            {
                new LayerDefinition("custom1", 0, ExclusionMode.LastWins),
                new LayerDefinition("custom2", 1, ExclusionMode.Blend)
            };
            var expression = CreateExpression("id-1", "smile", "nonexistent");
            var profile = new FacialProfile("1.0", layers, new[] { expression });

            var effectiveLayer = profile.GetEffectiveLayer(expression);

            Assert.AreEqual("custom1", effectiveLayer);
        }

        [Test]
        public void GetEffectiveLayer_NoLayersDefined_ReturnsExpressionOriginalLayer()
        {
            var expression = CreateExpression("id-1", "smile", "nonexistent");
            var profile = new FacialProfile("1.0", Array.Empty<LayerDefinition>(), new[] { expression });

            var effectiveLayer = profile.GetEffectiveLayer(expression);

            Assert.AreEqual("nonexistent", effectiveLayer);
        }

        // --- Expression 検索 ---

        [Test]
        public void FindExpressionById_ExistingId_ReturnsExpression()
        {
            var layers = CreateDefaultLayers();
            var expressions = new[]
            {
                CreateExpression("id-1", "smile", "emotion"),
                CreateExpression("id-2", "wink", "eye")
            };
            var profile = new FacialProfile("1.0", layers, expressions);

            var result = profile.FindExpressionById("id-1");

            Assert.IsTrue(result.HasValue);
            Assert.AreEqual("smile", result.Value.Name);
        }

        [Test]
        public void FindExpressionById_NonExistingId_ReturnsNull()
        {
            var layers = CreateDefaultLayers();
            var expressions = new[] { CreateExpression("id-1", "smile", "emotion") };
            var profile = new FacialProfile("1.0", layers, expressions);

            var result = profile.FindExpressionById("non-existing");

            Assert.IsFalse(result.HasValue);
        }

        [Test]
        public void FindExpressionById_NullId_ThrowsArgumentNullException()
        {
            var profile = new FacialProfile("1.0", CreateDefaultLayers());

            Assert.Throws<ArgumentNullException>(() => profile.FindExpressionById(null));
        }

        [Test]
        public void GetExpressionsByLayer_ExistingLayer_ReturnsMatchingExpressions()
        {
            var layers = CreateDefaultLayers();
            var expressions = new[]
            {
                CreateExpression("id-1", "smile", "emotion"),
                CreateExpression("id-2", "sad", "emotion"),
                CreateExpression("id-3", "wink", "eye")
            };
            var profile = new FacialProfile("1.0", layers, expressions);

            var results = profile.GetExpressionsByLayer("emotion");

            Assert.AreEqual(2, results.Length);
            Assert.AreEqual("id-1", results.Span[0].Id);
            Assert.AreEqual("id-2", results.Span[1].Id);
        }

        [Test]
        public void GetExpressionsByLayer_NoMatchingExpressions_ReturnsEmpty()
        {
            var layers = CreateDefaultLayers();
            var expressions = new[] { CreateExpression("id-1", "smile", "emotion") };
            var profile = new FacialProfile("1.0", layers, expressions);

            var results = profile.GetExpressionsByLayer("eye");

            Assert.AreEqual(0, results.Length);
        }

        [Test]
        public void GetExpressionsByLayer_NullLayer_ThrowsArgumentNullException()
        {
            var profile = new FacialProfile("1.0", CreateDefaultLayers());

            Assert.Throws<ArgumentNullException>(() => profile.GetExpressionsByLayer(null));
        }

        // --- 日本語・特殊文字 ---

        [Test]
        public void Constructor_JapaneseSchemaVersion_CreatesInstance()
        {
            var profile = new FacialProfile("バージョン1.0", CreateDefaultLayers());

            Assert.AreEqual("バージョン1.0", profile.SchemaVersion);
        }

        [Test]
        public void FindExpressionById_JapaneseId_ReturnsExpression()
        {
            var layers = CreateDefaultLayers();
            var expressions = new[] { CreateExpression("表情-001", "笑顔", "emotion") };
            var profile = new FacialProfile("1.0", layers, expressions);

            var result = profile.FindExpressionById("表情-001");

            Assert.IsTrue(result.HasValue);
            Assert.AreEqual("笑顔", result.Value.Name);
        }

        // --- レイヤー検索 ---

        [Test]
        public void FindLayerByName_ExistingLayer_ReturnsLayerDefinition()
        {
            var layers = CreateDefaultLayers();
            var profile = new FacialProfile("1.0", layers);

            var result = profile.FindLayerByName("emotion");

            Assert.IsTrue(result.HasValue);
            Assert.AreEqual("emotion", result.Value.Name);
            Assert.AreEqual(0, result.Value.Priority);
        }

        [Test]
        public void FindLayerByName_NonExistingLayer_ReturnsNull()
        {
            var layers = CreateDefaultLayers();
            var profile = new FacialProfile("1.0", layers);

            var result = profile.FindLayerByName("nonexistent");

            Assert.IsFalse(result.HasValue);
        }

        [Test]
        public void FindLayerByName_NullName_ThrowsArgumentNullException()
        {
            var profile = new FacialProfile("1.0", CreateDefaultLayers());

            Assert.Throws<ArgumentNullException>(() => profile.FindLayerByName(null));
        }

        // --- RendererPaths ---

        [Test]
        public void Constructor_WithRendererPaths_StoresPaths()
        {
            var paths = new[] { "Armature/Body", "Armature/Face" };

            var profile = new FacialProfile("1.0", CreateDefaultLayers(), null, paths);

            Assert.AreEqual(2, profile.RendererPaths.Length);
            Assert.AreEqual("Armature/Body", profile.RendererPaths.Span[0]);
            Assert.AreEqual("Armature/Face", profile.RendererPaths.Span[1]);
        }

        [Test]
        public void Constructor_NullRendererPaths_TreatedAsEmpty()
        {
            var profile = new FacialProfile("1.0", CreateDefaultLayers(), null, null);

            Assert.AreEqual(0, profile.RendererPaths.Length);
        }

        [Test]
        public void Constructor_DefaultRendererPaths_IsEmpty()
        {
            var profile = new FacialProfile("1.0", CreateDefaultLayers());

            Assert.AreEqual(0, profile.RendererPaths.Length);
        }

        [Test]
        public void RendererPaths_IsDefensiveCopy_OriginalArrayModificationDoesNotAffect()
        {
            var paths = new[] { "Armature/Body", "Armature/Face" };
            var profile = new FacialProfile("1.0", CreateDefaultLayers(), null, paths);

            paths[0] = "Modified/Path";

            Assert.AreEqual("Armature/Body", profile.RendererPaths.Span[0]);
        }

        [Test]
        public void Constructor_EmptyRendererPaths_CreatesInstance()
        {
            var profile = new FacialProfile("1.0", CreateDefaultLayers(), null, Array.Empty<string>());

            Assert.AreEqual(0, profile.RendererPaths.Length);
        }

        [Test]
        public void Constructor_ThreeArguments_BackwardCompatible()
        {
            var profile = new FacialProfile("1.0", CreateDefaultLayers(), new[] { CreateExpression() });

            Assert.AreEqual(3, profile.Layers.Length);
            Assert.AreEqual(1, profile.Expressions.Length);
            Assert.AreEqual(0, profile.RendererPaths.Length);
        }

        // --- BaseExpression ---
        //
        // ベース表情は「どのレイヤーも contribute しない BlendShape index に残す初期値」であり、
        // JSON / SO からランタイム合成パイプラインへ運ぶ担体を Domain 層が持つ必要がある。

        [Test]
        public void Constructor_BaseExpressionSnapshots_ExposesBaseExpression()
        {
            var baseExpression = new[]
            {
                new BlendShapeSnapshot("Body", "Brow_Angry", 0.645f),
                new BlendShapeSnapshot("Face", "Eye_Narrow", 0.2825f),
            };

            var profile = new FacialProfile(
                "1.0",
                CreateEmotionOnlyLayers(),
                baseExpression: baseExpression);

            Assert.That(profile.BaseExpression.Length, Is.EqualTo(2));
            Assert.That(profile.BaseExpression.Span[0].Name, Is.EqualTo("Brow_Angry"));
            Assert.That(profile.BaseExpression.Span[0].Value, Is.EqualTo(0.645f).Within(1e-6f));
            Assert.That(profile.BaseExpression.Span[1].Name, Is.EqualTo("Eye_Narrow"));
            Assert.That(profile.BaseExpression.Span[1].Value, Is.EqualTo(0.2825f).Within(1e-6f));
        }

        [Test]
        public void Constructor_NullBaseExpression_ExposesEmptyBaseExpression()
        {
            var profile = new FacialProfile("1.0", CreateEmotionOnlyLayers());

            Assert.That(profile.BaseExpression.Length, Is.EqualTo(0));
        }

        [Test]
        public void Constructor_BaseExpression_CopiesDefensively()
        {
            var baseExpression = new[]
            {
                new BlendShapeSnapshot("Body", "Brow_Angry", 0.5f),
            };

            var profile = new FacialProfile(
                "1.0",
                CreateEmotionOnlyLayers(),
                baseExpression: baseExpression);

            // 呼出側の配列を書き換えてもプロファイル側は不変であること。
            baseExpression[0] = new BlendShapeSnapshot("Body", "Mutated", 1f);

            Assert.That(profile.BaseExpression.Span[0].Name, Is.EqualTo("Brow_Angry"));
            Assert.That(profile.BaseExpression.Span[0].Value, Is.EqualTo(0.5f).Within(1e-6f));
        }

        [Test]
        public void Constructor_EmptyBaseExpression_ExposesEmptyBaseExpression()
        {
            var profile = new FacialProfile(
                "1.0",
                CreateEmotionOnlyLayers(),
                baseExpression: Array.Empty<BlendShapeSnapshot>());

            Assert.That(profile.BaseExpression.Length, Is.EqualTo(0));
        }

        // --- Slots ---

        [Test]
        public void Constructor_NullSlots_InitializesEmptySlots()
        {
            var profile = new FacialProfile("1.0", slots: null);

            Assert.AreEqual(0, profile.Slots.Length);
        }

        [Test]
        public void ValidateSlotReferences_DuplicateSlots_ReturnsDuplicateReference()
        {
            var profile = new FacialProfile(
                "1.0",
                slots: new[] { "blink", "blink" });

            var invalidRefs = profile.ValidateSlotReferences();

            Assert.AreEqual(1, invalidRefs.Count);
            AssertInvalidReference(
                invalidRefs[0],
                "blink",
                InvalidSlotReference.DuplicateReason);
        }

        [Test]
        public void ValidateSlotReferences_ExpressionOverlayUndeclaredSlot_ReturnsUndeclaredReference()
        {
            var expression = CreateExpressionWithOverlay("mouth");
            var profile = new FacialProfile(
                "1.0",
                expressions: new[] { expression },
                slots: new[] { "blink" });

            var invalidRefs = profile.ValidateSlotReferences();

            Assert.AreEqual(1, invalidRefs.Count);
            AssertInvalidReference(
                invalidRefs[0],
                "mouth",
                InvalidSlotReference.UndeclaredReason);
        }

        [Test]
        public void ValidateSlotReferences_DefaultOverlayUndeclaredSlot_ReturnsUndeclaredReference()
        {
            var profile = new FacialProfile(
                "1.0",
                defaultOverlays: new[]
                {
                    new OverlaySlotBinding("mouth", suppress: false, snapshot: null),
                },
                slots: new[] { "blink" });

            var invalidRefs = profile.ValidateSlotReferences();

            Assert.AreEqual(1, invalidRefs.Count);
            AssertInvalidReference(
                invalidRefs[0],
                "mouth",
                InvalidSlotReference.UndeclaredReason);
        }

        [Test]
        public void Slots_IsDefensiveCopy_OriginalArrayModificationDoesNotAffect()
        {
            var slots = new[] { "blink" };
            var profile = new FacialProfile("1.0", slots: slots);

            slots[0] = "mouth";

            Assert.AreEqual("blink", profile.Slots.Span[0]);
        }
    }
}
