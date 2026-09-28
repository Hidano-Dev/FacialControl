using System;
using System.Linq;
using NUnit.Framework;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;

namespace Hidano.FacialControl.Tests.EditMode.Domain
{
    [TestFixture]
    public class ARKitDetectorTests
    {
        // --- ARKit 52 パラメータ定数確認 ---

        [Test]
        public void ARKit52Names_Count_Returns52()
        {
            var names = ARKitDetector.ARKit52Names;

            Assert.AreEqual(52, names.Length);
        }

        [Test]
        public void ARKit52Names_ContainsEyeBlinkLeft()
        {
            var names = ARKitDetector.ARKit52Names;

            Assert.IsTrue(Array.IndexOf(names, "eyeBlinkLeft") >= 0);
        }

        [Test]
        public void ARKit52Names_ContainsJawOpen()
        {
            var names = ARKitDetector.ARKit52Names;

            Assert.IsTrue(Array.IndexOf(names, "jawOpen") >= 0);
        }

        [Test]
        public void ARKit52Names_ContainsBrowDownLeft()
        {
            var names = ARKitDetector.ARKit52Names;

            Assert.IsTrue(Array.IndexOf(names, "browDownLeft") >= 0);
        }

        [Test]
        public void ARKit52Names_ContainsTongueOut()
        {
            var names = ARKitDetector.ARKit52Names;

            Assert.IsTrue(Array.IndexOf(names, "tongueOut") >= 0);
        }

        // --- PerfectSync パラメータ定数確認 ---

        [Test]
        public void PerfectSyncNames_Count_Returns13()
        {
            var names = ARKitDetector.PerfectSyncNames;

            Assert.AreEqual(13, names.Length);
        }

        [Test]
        public void PerfectSyncNames_ContainsTongueFlat()
        {
            var names = ARKitDetector.PerfectSyncNames;

            Assert.IsTrue(Array.IndexOf(names, "tongueFlat") >= 0);
        }

        [Test]
        public void PerfectSyncNames_DoesNotOverlapWithARKit52()
        {
            var arkit = ARKitDetector.ARKit52Names;
            var ps = ARKitDetector.PerfectSyncNames;

            foreach (var name in ps)
            {
                Assert.IsFalse(Array.IndexOf(arkit, name) >= 0,
                    $"PerfectSync パラメータ '{name}' が ARKit 52 と重複しています。");
            }
        }

        // --- 完全一致マッチング ---

        [Test]
        public void DetectARKit_AllARKit52Names_ReturnsAll52()
        {
            var blendShapeNames = ARKitDetector.ARKit52Names.ToArray();

            var result = ARKitDetector.DetectARKit(blendShapeNames);

            Assert.AreEqual(52, result.Length);
        }

        [Test]
        public void DetectARKit_PartialMatch_ReturnsOnlyMatched()
        {
            var blendShapeNames = new[] { "eyeBlinkLeft", "eyeBlinkRight", "unknownShape" };

            var result = ARKitDetector.DetectARKit(blendShapeNames);

            Assert.AreEqual(2, result.Length);
            Assert.IsTrue(result.Contains("eyeBlinkLeft"));
            Assert.IsTrue(result.Contains("eyeBlinkRight"));
        }

        [Test]
        public void DetectARKit_NoMatch_ReturnsEmpty()
        {
            var blendShapeNames = new[] { "customSmile", "customSad" };

            var result = ARKitDetector.DetectARKit(blendShapeNames);

            Assert.AreEqual(0, result.Length);
        }

        [Test]
        public void DetectARKit_CaseSensitive_NoPartialMatch()
        {
            // 大文字小文字が異なる場合はマッチしない（完全一致のみ）
            var blendShapeNames = new[] { "EyeBlinkLeft", "JAWOPEN" };

            var result = ARKitDetector.DetectARKit(blendShapeNames);

            Assert.AreEqual(0, result.Length);
        }

        [Test]
        public void DetectARKit_EmptyInput_ReturnsEmpty()
        {
            var blendShapeNames = Array.Empty<string>();

            var result = ARKitDetector.DetectARKit(blendShapeNames);

            Assert.AreEqual(0, result.Length);
        }

        // --- PerfectSync マッチング ---

        [Test]
        public void DetectPerfectSync_AllPerfectSyncNames_ReturnsAll()
        {
            var blendShapeNames = ARKitDetector.PerfectSyncNames.ToArray();

            var result = ARKitDetector.DetectPerfectSync(blendShapeNames);

            Assert.AreEqual(ARKitDetector.PerfectSyncNames.Length, result.Length);
        }

        [Test]
        public void DetectPerfectSync_NoMatch_ReturnsEmpty()
        {
            var blendShapeNames = new[] { "eyeBlinkLeft", "jawOpen" };

            var result = ARKitDetector.DetectPerfectSync(blendShapeNames);

            Assert.AreEqual(0, result.Length);
        }

        // --- 統合検出（ARKit + PerfectSync） ---

        [Test]
        public void DetectAll_ARKitAndPerfectSync_ReturnsBoth()
        {
            var arkit = new[] { "eyeBlinkLeft", "jawOpen" };
            var ps = ARKitDetector.PerfectSyncNames.Length > 0
                ? new[] { ARKitDetector.PerfectSyncNames[0] }
                : Array.Empty<string>();
            var blendShapeNames = arkit.Concat(ps).ToArray();

            var result = ARKitDetector.DetectAll(blendShapeNames);

            Assert.AreEqual(arkit.Length + ps.Length, result.Length);
        }

        [Test]
        public void DetectAll_MixedKnownAndUnknown_SkipsUnknown()
        {
            var blendShapeNames = new[] { "eyeBlinkLeft", "customShape", "jawOpen", "mySpecialBlend" };

            var result = ARKitDetector.DetectAll(blendShapeNames);

            Assert.AreEqual(2, result.Length);
            Assert.IsTrue(result.Contains("eyeBlinkLeft"));
            Assert.IsTrue(result.Contains("jawOpen"));
        }

        // --- 未対応パラメータのスキップ ---

        [Test]
        public void DetectARKit_UnknownParametersSilentlySkipped()
        {
            var blendShapeNames = new[]
            {
                "eyeBlinkLeft", "myCustomShape", "jawOpen",
                "特殊文字テスト", "browDownLeft"
            };

            var result = ARKitDetector.DetectARKit(blendShapeNames);

            Assert.AreEqual(3, result.Length);
        }

        [Test]
        public void DetectARKit_JapaneseBlendShapeNames_SkippedWithoutError()
        {
            var blendShapeNames = new[] { "あいう", "表情_怒り", "まばたき左" };

            var result = ARKitDetector.DetectARKit(blendShapeNames);

            Assert.AreEqual(0, result.Length);
        }

        // --- レイヤーグルーピング ---

        [Test]
        public void GetLayerGroup_EyeParameters_ReturnsEye()
        {
            Assert.AreEqual("eye", ARKitDetector.GetLayerGroup("eyeBlinkLeft"));
            Assert.AreEqual("eye", ARKitDetector.GetLayerGroup("eyeBlinkRight"));
            Assert.AreEqual("eye", ARKitDetector.GetLayerGroup("eyeLookDownLeft"));
            Assert.AreEqual("eye", ARKitDetector.GetLayerGroup("eyeWideLeft"));
            Assert.AreEqual("eye", ARKitDetector.GetLayerGroup("eyeSquintLeft"));
        }

        [Test]
        public void GetLayerGroup_MouthParameters_ReturnsMouth()
        {
            Assert.AreEqual("mouth", ARKitDetector.GetLayerGroup("jawOpen"));
            Assert.AreEqual("mouth", ARKitDetector.GetLayerGroup("mouthSmileLeft"));
            Assert.AreEqual("mouth", ARKitDetector.GetLayerGroup("mouthFunnel"));
            Assert.AreEqual("mouth", ARKitDetector.GetLayerGroup("tongueOut"));
        }

        [Test]
        public void GetLayerGroup_BrowParameters_ReturnsBrow()
        {
            Assert.AreEqual("brow", ARKitDetector.GetLayerGroup("browDownLeft"));
            Assert.AreEqual("brow", ARKitDetector.GetLayerGroup("browDownRight"));
            Assert.AreEqual("brow", ARKitDetector.GetLayerGroup("browInnerUp"));
            Assert.AreEqual("brow", ARKitDetector.GetLayerGroup("browOuterUpLeft"));
        }

        [Test]
        public void GetLayerGroup_NoseParameters_ReturnsNose()
        {
            Assert.AreEqual("nose", ARKitDetector.GetLayerGroup("noseSneerLeft"));
            Assert.AreEqual("nose", ARKitDetector.GetLayerGroup("noseSneerRight"));
        }

        [Test]
        public void GetLayerGroup_CheekParameters_ReturnsCheek()
        {
            Assert.AreEqual("cheek", ARKitDetector.GetLayerGroup("cheekPuff"));
            Assert.AreEqual("cheek", ARKitDetector.GetLayerGroup("cheekSquintLeft"));
        }

        [Test]
        public void GetLayerGroup_UnknownParameter_ReturnsNull()
        {
            Assert.IsNull(ARKitDetector.GetLayerGroup("unknownParam"));
            Assert.IsNull(ARKitDetector.GetLayerGroup("customShape"));
        }

        // --- レイヤー別グルーピング ---

        [Test]
        public void GroupByLayer_MixedParameters_GroupsCorrectly()
        {
            var names = new[] { "eyeBlinkLeft", "jawOpen", "browDownLeft", "eyeBlinkRight", "mouthSmileLeft" };

            var groups = ARKitDetector.GroupByLayer(names);

            Assert.IsTrue(groups.ContainsKey("eye"));
            Assert.IsTrue(groups.ContainsKey("mouth"));
            Assert.IsTrue(groups.ContainsKey("brow"));
            Assert.AreEqual(2, groups["eye"].Length);
            Assert.AreEqual(2, groups["mouth"].Length);
            Assert.AreEqual(1, groups["brow"].Length);
        }

        [Test]
        public void GroupByLayer_EmptyInput_ReturnsEmptyDictionary()
        {
            var names = Array.Empty<string>();

            var groups = ARKitDetector.GroupByLayer(names);

            Assert.AreEqual(0, groups.Count);
        }

        [Test]
        public void GroupByLayer_UnknownNames_ExcludedFromGroups()
        {
            var names = new[] { "customShape", "unknownBlend" };

            var groups = ARKitDetector.GroupByLayer(names);

            Assert.AreEqual(0, groups.Count);
        }

        // --- Expression 自動生成 ---

        [Test]
        public void GenerateExpressions_ARKit52Eye_CreatesEyeExpression()
        {
            var detectedNames = new[] { "eyeBlinkLeft", "eyeBlinkRight", "eyeWideLeft", "eyeWideRight" };

            var expressions = ARKitDetector.GenerateExpressions(detectedNames);

            Assert.Greater(expressions.Length, 0);
            // eye グループの Expression が生成される
            bool hasEyeExpression = false;
            for (int i = 0; i < expressions.Length; i++)
            {
                if (expressions[i].Layer == "eye")
                {
                    hasEyeExpression = true;
                    // BlendShape 値が含まれている
                    Assert.Greater(expressions[i].BlendShapeValues.Length, 0);
                }
            }
            Assert.IsTrue(hasEyeExpression, "eye レイヤーの Expression が生成されていません。");
        }

        [Test]
        public void GenerateExpressions_MixedGroups_CreatesPerLayer()
        {
            var detectedNames = new[] { "eyeBlinkLeft", "jawOpen", "browDownLeft" };

            var expressions = ARKitDetector.GenerateExpressions(detectedNames);

            var layers = new System.Collections.Generic.HashSet<string>();
            for (int i = 0; i < expressions.Length; i++)
            {
                layers.Add(expressions[i].Layer);
            }
            Assert.IsTrue(layers.Contains("eye"));
            Assert.IsTrue(layers.Contains("mouth"));
            Assert.IsTrue(layers.Contains("brow"));
        }

        [Test]
        public void GenerateExpressions_EmptyInput_ReturnsEmpty()
        {
            var detectedNames = Array.Empty<string>();

            var expressions = ARKitDetector.GenerateExpressions(detectedNames);

            Assert.AreEqual(0, expressions.Length);
        }

        [Test]
        public void GenerateExpressions_EachHasValidIdAndName()
        {
            var detectedNames = new[] { "eyeBlinkLeft", "jawOpen" };

            var expressions = ARKitDetector.GenerateExpressions(detectedNames);

            for (int i = 0; i < expressions.Length; i++)
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(expressions[i].Id));
                Assert.IsFalse(string.IsNullOrWhiteSpace(expressions[i].Name));
                Assert.IsFalse(string.IsNullOrWhiteSpace(expressions[i].Layer));
            }
        }

        [Test]
        public void GenerateExpressions_BlendShapeValuesAreDefaultOne()
        {
            var detectedNames = new[] { "eyeBlinkLeft", "eyeBlinkRight" };

            var expressions = ARKitDetector.GenerateExpressions(detectedNames);

            for (int i = 0; i < expressions.Length; i++)
            {
                var bsValues = expressions[i].BlendShapeValues.Span;
                for (int j = 0; j < bsValues.Length; j++)
                {
                    Assert.AreEqual(1f, bsValues[j].Value, 0.0001f,
                        $"BlendShape '{bsValues[j].Name}' の値が 1.0 ではありません。");
                }
            }
        }

        [Test]
        public void GenerateExpressions_UniqueIds()
        {
            var detectedNames = new[] { "eyeBlinkLeft", "jawOpen", "browDownLeft" };

            var expressions = ARKitDetector.GenerateExpressions(detectedNames);

            var ids = new System.Collections.Generic.HashSet<string>();
            for (int i = 0; i < expressions.Length; i++)
            {
                Assert.IsTrue(ids.Add(expressions[i].Id),
                    $"Expression ID '{expressions[i].Id}' が重複しています。");
            }
        }

        [Test]
        public void GenerateExpressions_DefaultTransitionDuration()
        {
            var detectedNames = new[] { "eyeBlinkLeft" };

            var expressions = ARKitDetector.GenerateExpressions(detectedNames);

            for (int i = 0; i < expressions.Length; i++)
            {
                Assert.AreEqual(Expression.DefaultTransitionDuration, expressions[i].TransitionDuration, 0.0001f);
            }
        }

        // --- Null 引数チェック ---

        [Test]
        public void DetectARKit_NullInput_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => ARKitDetector.DetectARKit(null));
        }

        [Test]
        public void DetectPerfectSync_NullInput_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => ARKitDetector.DetectPerfectSync(null));
        }

        [Test]
        public void DetectAll_NullInput_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => ARKitDetector.DetectAll(null));
        }

        [Test]
        public void GroupByLayer_NullInput_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => ARKitDetector.GroupByLayer(null));
        }

        [Test]
        public void GenerateExpressions_NullInput_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => ARKitDetector.GenerateExpressions(null));
        }

        [Test]
        public void GetLayerGroup_NullInput_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => ARKitDetector.GetLayerGroup(null));
        }

        // --- PerfectSync レイヤーグルーピング ---

        [Test]
        public void GetLayerGroup_PerfectSyncNames_AllHaveGroup()
        {
            var psNames = ARKitDetector.PerfectSyncNames;

            foreach (var name in psNames)
            {
                var group = ARKitDetector.GetLayerGroup(name);
                Assert.IsNotNull(group, $"PerfectSync パラメータ '{name}' のグループが null です。");
            }
        }

        // --- ARKit 52 全パラメータのグルーピング ---

        [Test]
        public void GetLayerGroup_AllARKit52_AllHaveGroup()
        {
            var arkitNames = ARKitDetector.ARKit52Names;

            foreach (var name in arkitNames)
            {
                var group = ARKitDetector.GetLayerGroup(name);
                Assert.IsNotNull(group, $"ARKit パラメータ '{name}' のグループが null です。");
            }
        }
    }
}
