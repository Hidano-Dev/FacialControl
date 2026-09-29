using System;
using System.Collections.Generic;
using NUnit.Framework;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Editor.Windows;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.EditMode.Editor
{
    /// <summary>
    /// P-Q1-02/03: 雛形 Expression 生成（<see cref="ProfileCreationData.BuildSampleExpressions"/>）の検証。
    /// 命名規則プリセット（VRM / ARKit / None）と <see cref="ProfileCreationData.BuildProfile"/> の統合挙動を検証する。
    /// </summary>
    [TestFixture]
    [SmallTest]
    public class SampleExpressionsTests : SizedTestFixture
    {
        // --- BuildSampleExpressions: VRM プリセット ---

        [Test]
        public void BuildSampleExpressions_Vrm_ReturnsThreeExpressions()
        {
            var samples = ProfileCreationData.BuildSampleExpressions(
                ProfileCreationData.NamingConvention.VRM);

            Assert.AreEqual(3, samples.Length);
        }

        [Test]
        public void BuildSampleExpressions_Vrm_ContainsSmileAngryBlinkNames()
        {
            var samples = ProfileCreationData.BuildSampleExpressions(
                ProfileCreationData.NamingConvention.VRM);

            var names = new HashSet<string>();
            for (int i = 0; i < samples.Length; i++)
                names.Add(samples[i].Name);

            Assert.IsTrue(names.Contains("smile"));
            Assert.IsTrue(names.Contains("angry"));
            Assert.IsTrue(names.Contains("blink"));
        }

        [Test]
        public void BuildSampleExpressions_Vrm_SmileHasFclAllJoy()
        {
            var samples = ProfileCreationData.BuildSampleExpressions(
                ProfileCreationData.NamingConvention.VRM);

            var smile = FindByName(samples, "smile");
            var bsArray = smile.BlendShapeValues.Span;
            Assert.AreEqual(1, bsArray.Length);
            Assert.AreEqual("Fcl_ALL_Joy", bsArray[0].Name);
            Assert.AreEqual(1.0f, bsArray[0].Value);
        }

        [Test]
        public void BuildSampleExpressions_Vrm_BlinkAssignedToEyeLayer()
        {
            var samples = ProfileCreationData.BuildSampleExpressions(
                ProfileCreationData.NamingConvention.VRM);

            var blink = FindByName(samples, "blink");
            Assert.AreEqual("eye", blink.Layer);
        }

        [Test]
        public void BuildSampleExpressions_Vrm_BlinkHasFastTransition()
        {
            var samples = ProfileCreationData.BuildSampleExpressions(
                ProfileCreationData.NamingConvention.VRM);

            var blink = FindByName(samples, "blink");
            Assert.AreEqual(0.08f, blink.TransitionDuration);
        }

        [Test]
        public void BuildSampleExpressions_Vrm_SmileAngryInEmotionLayer()
        {
            var samples = ProfileCreationData.BuildSampleExpressions(
                ProfileCreationData.NamingConvention.VRM);

            Assert.AreEqual("emotion", FindByName(samples, "smile").Layer);
            Assert.AreEqual("emotion", FindByName(samples, "angry").Layer);
        }

        [Test]
        public void BuildSampleExpressions_Vrm_UniqueIds()
        {
            var samples = ProfileCreationData.BuildSampleExpressions(
                ProfileCreationData.NamingConvention.VRM);

            var ids = new HashSet<string>();
            for (int i = 0; i < samples.Length; i++)
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(samples[i].Id));
                Assert.IsTrue(ids.Add(samples[i].Id), "Expression ID が重複しています。");
            }
        }

        // --- BuildSampleExpressions: ARKit プリセット ---

        [Test]
        public void BuildSampleExpressions_ARKit_ReturnsThreeExpressions()
        {
            var samples = ProfileCreationData.BuildSampleExpressions(
                ProfileCreationData.NamingConvention.ARKit);

            Assert.AreEqual(3, samples.Length);
        }

        [Test]
        public void BuildSampleExpressions_ARKit_SmileHasLeftRightShapes()
        {
            var samples = ProfileCreationData.BuildSampleExpressions(
                ProfileCreationData.NamingConvention.ARKit);

            var smile = FindByName(samples, "smile");
            var bsArray = smile.BlendShapeValues.Span;
            Assert.AreEqual(2, bsArray.Length);

            var names = new HashSet<string> { bsArray[0].Name, bsArray[1].Name };
            Assert.IsTrue(names.Contains("mouthSmile_L"));
            Assert.IsTrue(names.Contains("mouthSmile_R"));
        }

        [Test]
        public void BuildSampleExpressions_ARKit_BlinkHasEyeBlinkLeftRight()
        {
            var samples = ProfileCreationData.BuildSampleExpressions(
                ProfileCreationData.NamingConvention.ARKit);

            var blink = FindByName(samples, "blink");
            var bsArray = blink.BlendShapeValues.Span;
            Assert.AreEqual(2, bsArray.Length);

            var names = new HashSet<string> { bsArray[0].Name, bsArray[1].Name };
            Assert.IsTrue(names.Contains("eyeBlink_L"));
            Assert.IsTrue(names.Contains("eyeBlink_R"));
        }

        // --- BuildSampleExpressions: None プリセット ---

        [Test]
        public void BuildSampleExpressions_None_ReturnsEmptyArray()
        {
            var samples = ProfileCreationData.BuildSampleExpressions(
                ProfileCreationData.NamingConvention.None);

            Assert.AreEqual(0, samples.Length);
        }

        // --- BuildProfile: 雛形有効時の統合挙動 ---

        [Test]
        public void BuildProfile_IncludeSamplesTrue_Vrm_ContainsThreeExpressions()
        {
            var data = ProfileCreationData.CreateDefault("test");
            data.IncludeSampleExpressions = true;
            data.Naming = ProfileCreationData.NamingConvention.VRM;

            var profile = data.BuildProfile();

            Assert.AreEqual(3, profile.Expressions.Length);
        }

        [Test]
        public void BuildProfile_IncludeSamplesTrue_None_ReturnsEmptyExpressions()
        {
            var data = ProfileCreationData.CreateDefault("test");
            data.IncludeSampleExpressions = true;
            data.Naming = ProfileCreationData.NamingConvention.None;

            var profile = data.BuildProfile();

            Assert.AreEqual(0, profile.Expressions.Length);
        }

        [Test]
        public void BuildProfile_IncludeSamplesFalse_Vrm_ReturnsEmptyExpressions()
        {
            var data = ProfileCreationData.CreateDefault("test");
            data.IncludeSampleExpressions = false;
            data.Naming = ProfileCreationData.NamingConvention.VRM;

            var profile = data.BuildProfile();

            Assert.AreEqual(0, profile.Expressions.Length);
        }

        [Test]
        public void BuildProfile_DefaultPropertyValues_IncludeSamplesFalseNamingNone()
        {
            // CreateDefault は既存互換のため雛形を含まない（既存テスト維持）。
            // ダイアログ UI は明示的に IncludeSampleExpressions=true + Naming=VRM をセットする。
            var data = ProfileCreationData.CreateDefault("test");

            Assert.IsFalse(data.IncludeSampleExpressions);
            Assert.AreEqual(ProfileCreationData.NamingConvention.None, data.Naming);
        }

        // --- Helper ---

        private static Expression FindByName(Expression[] samples, string name)
        {
            for (int i = 0; i < samples.Length; i++)
            {
                if (samples[i].Name == name)
                    return samples[i];
            }
            throw new InvalidOperationException($"雛形 Expression '{name}' が見つかりません。");
        }
    }
}
