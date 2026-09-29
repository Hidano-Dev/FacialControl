using System;
using NUnit.Framework;
using Hidano.FacialControl.Adapters.Json;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Editor.Windows;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.EditMode.Editor
{
    /// <summary>
    /// P17-T01: 新規プロファイル JSON 生成の検証。
    /// デフォルトレイヤー構成、空 Expression リスト、スキーマバージョン "2.0" を検証する。
    /// </summary>
    [TestFixture]
    [SmallTest]
    public class ProfileCreationTests : SizedTestFixture
    {
        private SystemTextJsonParser _parser;

        [SetUp]
        public void SetUp()
        {
            _parser = new SystemTextJsonParser();
        }

        // --- ProfileCreationData 構造体テスト ---

        [Test]
        public void CreateDefaultData_ReturnsThreeDefaultLayers()
        {
            var data = ProfileCreationData.CreateDefault("テストプロファイル");

            Assert.AreEqual(3, data.Layers.Length);
            Assert.AreEqual("emotion", data.Layers[0].Name);
            Assert.AreEqual("lipsync", data.Layers[1].Name);
            Assert.AreEqual("eye", data.Layers[2].Name);
        }

        [Test]
        public void CreateDefaultData_ReturnsCorrectProfileName()
        {
            var data = ProfileCreationData.CreateDefault("マイプロファイル");

            Assert.AreEqual("マイプロファイル", data.ProfileName);
        }

        [Test]
        public void CreateDefaultData_DefaultLayerPriorities()
        {
            var data = ProfileCreationData.CreateDefault("test");

            Assert.AreEqual(0, data.Layers[0].Priority);
            Assert.AreEqual(1, data.Layers[1].Priority);
            Assert.AreEqual(2, data.Layers[2].Priority);
        }

        [Test]
        public void CreateDefaultData_DefaultExclusionModes()
        {
            var data = ProfileCreationData.CreateDefault("test");

            Assert.AreEqual(ExclusionMode.LastWins, data.Layers[0].ExclusionMode);
            Assert.AreEqual(ExclusionMode.Blend, data.Layers[1].ExclusionMode);
            Assert.AreEqual(ExclusionMode.LastWins, data.Layers[2].ExclusionMode);
        }

        // --- プロファイル生成テスト ---

        [Test]
        public void BuildProfile_SchemaVersionIsV2()
        {
            var data = ProfileCreationData.CreateDefault("test");

            var profile = data.BuildProfile();

            Assert.AreEqual(SystemTextJsonParser.SchemaVersionV2, profile.SchemaVersion);
        }

        [Test]
        public void BuildProfile_HasCorrectLayerCount()
        {
            var data = ProfileCreationData.CreateDefault("test");

            var profile = data.BuildProfile();

            Assert.AreEqual(3, profile.Layers.Length);
        }

        [Test]
        public void BuildProfile_HasEmptyExpressionList()
        {
            var data = ProfileCreationData.CreateDefault("test");

            var profile = data.BuildProfile();

            Assert.AreEqual(0, profile.Expressions.Length);
        }

        [Test]
        public void BuildProfile_LayerNamesMatch()
        {
            var data = ProfileCreationData.CreateDefault("test");

            var profile = data.BuildProfile();

            var layers = profile.Layers.Span;
            Assert.AreEqual("emotion", layers[0].Name);
            Assert.AreEqual("lipsync", layers[1].Name);
            Assert.AreEqual("eye", layers[2].Name);
        }

        [Test]
        public void BuildProfile_LayerPrioritiesMatch()
        {
            var data = ProfileCreationData.CreateDefault("test");

            var profile = data.BuildProfile();

            var layers = profile.Layers.Span;
            Assert.AreEqual(0, layers[0].Priority);
            Assert.AreEqual(1, layers[1].Priority);
            Assert.AreEqual(2, layers[2].Priority);
        }

        [Test]
        public void BuildProfile_LayerExclusionModesMatch()
        {
            var data = ProfileCreationData.CreateDefault("test");

            var profile = data.BuildProfile();

            var layers = profile.Layers.Span;
            Assert.AreEqual(ExclusionMode.LastWins, layers[0].ExclusionMode);
            Assert.AreEqual(ExclusionMode.Blend, layers[1].ExclusionMode);
            Assert.AreEqual(ExclusionMode.LastWins, layers[2].ExclusionMode);
        }

        // --- カスタムレイヤー構成テスト ---

        [Test]
        public void BuildProfile_CustomLayers_CorrectLayerCount()
        {
            var layers = new[]
            {
                new ProfileCreationData.LayerEntry("custom1", 0, ExclusionMode.Blend),
                new ProfileCreationData.LayerEntry("custom2", 5, ExclusionMode.LastWins)
            };
            var data = new ProfileCreationData("カスタム", layers);

            var profile = data.BuildProfile();

            Assert.AreEqual(2, profile.Layers.Length);
        }

        [Test]
        public void BuildProfile_CustomLayers_CorrectNames()
        {
            var layers = new[]
            {
                new ProfileCreationData.LayerEntry("表情", 0, ExclusionMode.LastWins),
                new ProfileCreationData.LayerEntry("口", 1, ExclusionMode.Blend)
            };
            var data = new ProfileCreationData("日本語レイヤー", layers);

            var profile = data.BuildProfile();

            var layerSpan = profile.Layers.Span;
            Assert.AreEqual("表情", layerSpan[0].Name);
            Assert.AreEqual("口", layerSpan[1].Name);
        }

        // --- JSON シリアライズラウンドトリップテスト ---

        [Test]
        public void BuildProfile_SerializeAndParse_SchemaVersionRoundTrips()
        {
            var data = ProfileCreationData.CreateDefault("test");
            var profile = data.BuildProfile();

            var json = _parser.SerializeProfile(profile);
            var parsed = _parser.ParseProfile(json);

            Assert.AreEqual(SystemTextJsonParser.SchemaVersionV2, parsed.SchemaVersion);
        }

        [Test]
        public void BuildProfile_SerializeAndParse_LayerCountRoundTrips()
        {
            var data = ProfileCreationData.CreateDefault("test");
            var profile = data.BuildProfile();

            var json = _parser.SerializeProfile(profile);
            var parsed = _parser.ParseProfile(json);

            Assert.AreEqual(3, parsed.Layers.Length);
        }

        [Test]
        public void BuildProfile_SerializeAndParse_EmptyExpressionsRoundTrips()
        {
            var data = ProfileCreationData.CreateDefault("test");
            var profile = data.BuildProfile();

            var json = _parser.SerializeProfile(profile);
            var parsed = _parser.ParseProfile(json);

            Assert.AreEqual(0, parsed.Expressions.Length);
        }

        [Test]
        public void BuildProfile_SerializeAndParse_LayerDetailsRoundTrip()
        {
            var data = ProfileCreationData.CreateDefault("test");
            var profile = data.BuildProfile();

            var json = _parser.SerializeProfile(profile);
            var parsed = _parser.ParseProfile(json);

            var layers = parsed.Layers.Span;
            Assert.AreEqual("emotion", layers[0].Name);
            Assert.AreEqual(0, layers[0].Priority);
            Assert.AreEqual(ExclusionMode.LastWins, layers[0].ExclusionMode);

            Assert.AreEqual("lipsync", layers[1].Name);
            Assert.AreEqual(1, layers[1].Priority);
            Assert.AreEqual(ExclusionMode.Blend, layers[1].ExclusionMode);

            Assert.AreEqual("eye", layers[2].Name);
            Assert.AreEqual(2, layers[2].Priority);
            Assert.AreEqual(ExclusionMode.LastWins, layers[2].ExclusionMode);
        }

        // --- バリデーションテスト ---

        [Test]
        public void CreateDefault_NullName_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                ProfileCreationData.CreateDefault(null));
        }

        [Test]
        public void CreateDefault_EmptyName_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() =>
                ProfileCreationData.CreateDefault(""));
        }

        [Test]
        public void CreateDefault_WhitespaceName_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() =>
                ProfileCreationData.CreateDefault("   "));
        }

        [Test]
        public void Constructor_NullLayers_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new ProfileCreationData("test", null));
        }

        [Test]
        public void Constructor_EmptyLayers_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() =>
                new ProfileCreationData("test", Array.Empty<ProfileCreationData.LayerEntry>()));
        }

        // --- ファイル名生成テスト ---

        [Test]
        public void JsonFileName_ReturnsProfileNameWithExtension()
        {
            var data = ProfileCreationData.CreateDefault("myprofile");

            Assert.AreEqual("myprofile.json", data.JsonFileName);
        }

        [Test]
        public void JsonRelativePath_ReturnsCorrectPath()
        {
            var data = ProfileCreationData.CreateDefault("myprofile");

            Assert.AreEqual("FacialControl/myprofile.json", data.JsonRelativePath);
        }
    }
}
