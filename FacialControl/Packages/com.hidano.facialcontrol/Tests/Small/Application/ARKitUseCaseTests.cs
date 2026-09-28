using System;
using NUnit.Framework;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;
using Hidano.FacialControl.Application.UseCases;

namespace Hidano.FacialControl.Tests.EditMode.Application
{
    [TestFixture]
    public class ARKitUseCaseTests
    {
        private ARKitUseCase _useCase;

        [SetUp]
        public void SetUp()
        {
            _useCase = new ARKitUseCase();
        }

        // --- DetectAndGenerate ---

        [Test]
        public void DetectAndGenerate_NullBlendShapeNames_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                _useCase.DetectAndGenerate(null));
        }

        [Test]
        public void DetectAndGenerate_EmptyBlendShapeNames_ReturnsEmptyResult()
        {
            var result = _useCase.DetectAndGenerate(Array.Empty<string>());

            Assert.AreEqual(0, result.DetectedNames.Length);
            Assert.AreEqual(0, result.GeneratedExpressions.Length);
        }

        [Test]
        public void DetectAndGenerate_NoARKitNames_ReturnsEmptyResult()
        {
            var names = new[] { "customShape1", "customShape2", "myBlendShape" };

            var result = _useCase.DetectAndGenerate(names);

            Assert.AreEqual(0, result.DetectedNames.Length);
            Assert.AreEqual(0, result.GeneratedExpressions.Length);
        }

        [Test]
        public void DetectAndGenerate_ARKit52Names_DetectsAndGenerates()
        {
            var names = new[] { "eyeBlinkLeft", "eyeBlinkRight", "jawOpen", "mouthSmileLeft" };

            var result = _useCase.DetectAndGenerate(names);

            Assert.AreEqual(4, result.DetectedNames.Length);
            // eye グループと mouth グループの 2 つの Expression が生成される
            Assert.GreaterOrEqual(result.GeneratedExpressions.Length, 2);
        }

        [Test]
        public void DetectAndGenerate_PerfectSyncNames_DetectsAndGenerates()
        {
            var names = new[] { "tongueUp", "tongueDown", "cheekSuckLeft" };

            var result = _useCase.DetectAndGenerate(names);

            Assert.AreEqual(3, result.DetectedNames.Length);
            Assert.GreaterOrEqual(result.GeneratedExpressions.Length, 1);
        }

        [Test]
        public void DetectAndGenerate_MixedNames_DetectsOnlyKnownNames()
        {
            var names = new[]
            {
                "eyeBlinkLeft", "customShape", "jawOpen", "unknownParam", "tongueUp"
            };

            var result = _useCase.DetectAndGenerate(names);

            Assert.AreEqual(3, result.DetectedNames.Length);
            CollectionAssert.Contains(result.DetectedNames, "eyeBlinkLeft");
            CollectionAssert.Contains(result.DetectedNames, "jawOpen");
            CollectionAssert.Contains(result.DetectedNames, "tongueUp");
        }

        [Test]
        public void DetectAndGenerate_GeneratedExpressions_HaveValidIds()
        {
            var names = new[] { "eyeBlinkLeft", "jawOpen" };

            var result = _useCase.DetectAndGenerate(names);

            foreach (var expr in result.GeneratedExpressions)
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(expr.Id));
            }
        }

        [Test]
        public void DetectAndGenerate_GeneratedExpressions_HaveCorrectLayerGroups()
        {
            var names = new[] { "eyeBlinkLeft", "eyeBlinkRight", "jawOpen", "browDownLeft" };

            var result = _useCase.DetectAndGenerate(names);

            // eye, mouth, brow の 3 グループ
            Assert.AreEqual(3, result.GeneratedExpressions.Length);

            bool hasEye = false;
            bool hasMouth = false;
            bool hasBrow = false;
            foreach (var expr in result.GeneratedExpressions)
            {
                if (expr.Layer == "eye") hasEye = true;
                if (expr.Layer == "mouth") hasMouth = true;
                if (expr.Layer == "brow") hasBrow = true;
            }
            Assert.IsTrue(hasEye, "eye グループの Expression が生成されること");
            Assert.IsTrue(hasMouth, "mouth グループの Expression が生成されること");
            Assert.IsTrue(hasBrow, "brow グループの Expression が生成されること");
        }

        [Test]
        public void DetectAndGenerate_GeneratedExpressions_HaveDefaultTransitionDuration()
        {
            var names = new[] { "eyeBlinkLeft" };

            var result = _useCase.DetectAndGenerate(names);

            Assert.AreEqual(1, result.GeneratedExpressions.Length);
            Assert.AreEqual(Expression.DefaultTransitionDuration, result.GeneratedExpressions[0].TransitionDuration, 0.001f);
        }

        [Test]
        public void DetectAndGenerate_GeneratedExpressions_HaveBlendShapeValues()
        {
            var names = new[] { "eyeBlinkLeft", "eyeBlinkRight" };

            var result = _useCase.DetectAndGenerate(names);

            Assert.AreEqual(1, result.GeneratedExpressions.Length);
            var expr = result.GeneratedExpressions[0];
            Assert.AreEqual(2, expr.BlendShapeValues.Length);

            var bsSpan = expr.BlendShapeValues.Span;
            Assert.AreEqual(1f, bsSpan[0].Value, 0.001f);
            Assert.AreEqual(1f, bsSpan[1].Value, 0.001f);
        }

        [Test]
        public void DetectAndGenerate_AllARKit52_DetectsAll52()
        {
            var result = _useCase.DetectAndGenerate(ARKitDetector.ARKit52Names);

            Assert.AreEqual(52, result.DetectedNames.Length);
            Assert.GreaterOrEqual(result.GeneratedExpressions.Length, 1);
        }

        [Test]
        public void DetectAndGenerate_AllPerfectSync_DetectsAll()
        {
            // ARKit52 + PerfectSync の全パラメータ
            var allNames = new string[ARKitDetector.ARKit52Names.Length + ARKitDetector.PerfectSyncNames.Length];
            Array.Copy(ARKitDetector.ARKit52Names, 0, allNames, 0, ARKitDetector.ARKit52Names.Length);
            Array.Copy(ARKitDetector.PerfectSyncNames, 0, allNames, ARKitDetector.ARKit52Names.Length, ARKitDetector.PerfectSyncNames.Length);

            var result = _useCase.DetectAndGenerate(allNames);

            Assert.AreEqual(allNames.Length, result.DetectedNames.Length);
        }

        // --- GenerateOscMapping ---

        [Test]
        public void GenerateOscMapping_NullDetectedNames_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                _useCase.GenerateOscMapping(null));
        }

        [Test]
        public void GenerateOscMapping_EmptyDetectedNames_ReturnsEmptyArray()
        {
            var result = _useCase.GenerateOscMapping(Array.Empty<string>());

            Assert.AreEqual(0, result.Length);
        }

        [Test]
        public void GenerateOscMapping_SingleName_GeneratesMapping()
        {
            var result = _useCase.GenerateOscMapping(new[] { "eyeBlinkLeft" });

            Assert.AreEqual(1, result.Length);
            Assert.AreEqual("eyeBlinkLeft", result[0].BlendShapeName);
            Assert.AreEqual("eye", result[0].Layer);
            Assert.IsTrue(result[0].OscAddress.Contains("eyeBlinkLeft"));
        }

        [Test]
        public void GenerateOscMapping_VRChatOscAddressFormat()
        {
            var result = _useCase.GenerateOscMapping(new[] { "eyeBlinkLeft" });

            // VRChat OSC 互換形式: /avatar/parameters/{name}
            Assert.AreEqual("/avatar/parameters/eyeBlinkLeft", result[0].OscAddress);
        }

        [Test]
        public void GenerateOscMapping_MultipleNames_GeneratesAllMappings()
        {
            var names = new[] { "eyeBlinkLeft", "jawOpen", "browDownLeft" };

            var result = _useCase.GenerateOscMapping(names);

            Assert.AreEqual(3, result.Length);
        }

        [Test]
        public void GenerateOscMapping_CorrectLayerAssignment()
        {
            var names = new[] { "eyeBlinkLeft", "jawOpen", "browDownLeft", "cheekPuff", "noseSneerLeft" };

            var result = _useCase.GenerateOscMapping(names);

            Assert.AreEqual(5, result.Length);

            foreach (var mapping in result)
            {
                string expectedLayer = ARKitDetector.GetLayerGroup(mapping.BlendShapeName);
                Assert.AreEqual(expectedLayer, mapping.Layer,
                    $"{mapping.BlendShapeName} のレイヤーが正しいこと");
            }
        }

        [Test]
        public void GenerateOscMapping_UnknownNames_SkippedSilently()
        {
            var names = new[] { "eyeBlinkLeft", "unknownParam", "jawOpen" };

            var result = _useCase.GenerateOscMapping(names);

            // unknownParam はスキップされる
            Assert.AreEqual(2, result.Length);
        }

        [Test]
        public void GenerateOscMapping_PerfectSyncNames_GeneratesMapping()
        {
            var names = new[] { "tongueUp", "cheekSuckLeft" };

            var result = _useCase.GenerateOscMapping(names);

            Assert.AreEqual(2, result.Length);
            Assert.AreEqual("/avatar/parameters/tongueUp", result[0].OscAddress);
            Assert.AreEqual("mouth", result[0].Layer);
            Assert.AreEqual("/avatar/parameters/cheekSuckLeft", result[1].OscAddress);
            Assert.AreEqual("cheek", result[1].Layer);
        }

        // --- DetectAndGenerate + GenerateOscMapping 統合フロー ---

        [Test]
        public void EndToEnd_DetectThenGenerateMapping_WorksCorrectly()
        {
            var blendShapeNames = new[]
            {
                "eyeBlinkLeft", "eyeBlinkRight",
                "jawOpen", "mouthSmileLeft",
                "browDownLeft",
                "customShape"
            };

            // 1. 検出 + Expression 生成
            var detectResult = _useCase.DetectAndGenerate(blendShapeNames);

            Assert.AreEqual(5, detectResult.DetectedNames.Length);
            Assert.GreaterOrEqual(detectResult.GeneratedExpressions.Length, 2);

            // 2. OSC マッピング生成
            var mappings = _useCase.GenerateOscMapping(detectResult.DetectedNames);

            Assert.AreEqual(5, mappings.Length);
            foreach (var mapping in mappings)
            {
                Assert.IsTrue(mapping.OscAddress.StartsWith("/avatar/parameters/"));
                Assert.IsFalse(string.IsNullOrEmpty(mapping.Layer));
            }
        }

        [Test]
        public void DetectAndGenerate_GeneratedExpressions_HaveUniqueIds()
        {
            var names = new[] { "eyeBlinkLeft", "jawOpen", "browDownLeft" };

            var result = _useCase.DetectAndGenerate(names);

            var ids = new System.Collections.Generic.HashSet<string>();
            foreach (var expr in result.GeneratedExpressions)
            {
                Assert.IsTrue(ids.Add(expr.Id), $"ID '{expr.Id}' が重複しないこと");
            }
        }

        [Test]
        public void DetectAndGenerate_GeneratedExpressions_HaveARKitPrefixedNames()
        {
            var names = new[] { "eyeBlinkLeft" };

            var result = _useCase.DetectAndGenerate(names);

            Assert.AreEqual(1, result.GeneratedExpressions.Length);
            Assert.IsTrue(result.GeneratedExpressions[0].Name.StartsWith("ARKit_"),
                "生成された Expression の名前は 'ARKit_' プレフィックスを持つこと");
        }
    }
}
