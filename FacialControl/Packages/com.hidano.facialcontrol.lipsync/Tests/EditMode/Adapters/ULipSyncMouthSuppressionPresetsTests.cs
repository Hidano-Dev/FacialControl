using System.Collections.Generic;
using Hidano.FacialControl.LipSync.Adapters;
using NUnit.Framework;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.LipSync.Tests.EditMode.Adapters
{
    /// <summary>
    /// 「フェイシャルキャプチャの口まわりを追加」の追加規則を固定する。
    /// </summary>
    [TestFixture]
    [SmallTest]
    public sealed class ULipSyncMouthSuppressionPresetsTests : SizedTestFixture
    {
        [Test]
        public void FacialCaptureMouthBlendShapeNames_Always_HasTwentyOneMouthShapesWithoutEmotionShapes()
        {
            IReadOnlyList<string> names = ULipSyncMouthSuppressionPresets.FacialCaptureMouthBlendShapeNames;

            Assert.That(names.Count, Is.EqualTo(21));
            Assert.That(names, Does.Contain("jawOpen"));
            Assert.That(names, Does.Contain("mouthStretchRight"));
            foreach (string name in names)
            {
                Assert.That(name, Does.Not.StartWith("mouthSmile"));
                Assert.That(name, Does.Not.StartWith("mouthFrown"));
                Assert.That(name, Does.Not.StartWith("mouthDimple"));
            }
        }

        [TestCase("mouthPress_L", "mouthPressLeft")]
        [TestCase("mouthUpperUp_R", "mouthUpperUpRight")]
        [TestCase("jawOpen", "jawOpen")]
        public void ToArKitName_CaptureName_ConvertsSideSuffix(string captureName, string expected)
        {
            Assert.That(ULipSyncMouthSuppressionPresets.ToArKitName(captureName), Is.EqualTo(expected));
        }

        [TestCase("mouthPress_L", true)]
        [TestCase("jawOpen", true)]
        [TestCase("mouthSmile_L", false)]
        [TestCase("eyeBlink_L", false)]
        public void IsFacialCaptureMouthName_CaptureName_MatchesMouthShapesOnly(string captureName, bool expected)
        {
            Assert.That(ULipSyncMouthSuppressionPresets.IsFacialCaptureMouthName(captureName), Is.EqualTo(expected));
        }

        [Test]
        public void AppendFacialCaptureMouthNames_NoMeshFilter_KeepsExistingAndAppendsWithoutDuplicates()
        {
            var existing = new List<string> { "custom", "jawOpen" };

            List<string> result = ULipSyncMouthSuppressionPresets.AppendFacialCaptureMouthNames(existing, null, null);

            Assert.That(result[0], Is.EqualTo("custom"));
            Assert.That(result[1], Is.EqualTo("jawOpen"));
            Assert.That(result.Count, Is.EqualTo(22), "既存 2 件 + 21 件 − 重複 1 件。");
            Assert.That(result.FindAll(n => n == "jawOpen").Count, Is.EqualTo(1));
        }

        [Test]
        public void AppendFacialCaptureMouthNames_MeshFilter_AddsOnlyNamesOnMesh()
        {
            var mesh = new HashSet<string> { "jawOpen", "mouthClose", "eyeBlinkLeft" };

            List<string> result = ULipSyncMouthSuppressionPresets.AppendFacialCaptureMouthNames(null, null, mesh);

            Assert.That(result, Is.EqualTo(new[] { "jawOpen", "mouthClose" }));
        }

        [Test]
        public void AppendFacialCaptureMouthNames_MappingOverride_AddsTargetsOfMouthCaptureNamesOnly()
        {
            var mappings = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("jawOpen", "Mouth_Open"),
                new KeyValuePair<string, string>("mouthPress_L", "Mouth_Press_L"),
                new KeyValuePair<string, string>("mouthSmile_L", "Mouth_Smile_L"),
                new KeyValuePair<string, string>("eyeBlink_L", "Eye_Blink_L"),
            };
            var mesh = new HashSet<string> { "Mouth_Open", "Mouth_Press_L", "Mouth_Smile_L", "Eye_Blink_L" };

            List<string> result = ULipSyncMouthSuppressionPresets.AppendFacialCaptureMouthNames(null, mappings, mesh);

            Assert.That(result, Is.EqualTo(new[] { "Mouth_Open", "Mouth_Press_L" }));
        }
    }
}
