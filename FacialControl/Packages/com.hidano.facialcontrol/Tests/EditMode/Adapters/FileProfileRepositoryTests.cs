using System;
using System.IO;
using NUnit.Framework;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Adapters.FileSystem;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.EditMode.Adapters
{
    [TestFixture]
    [MediumTest]
    public class FileProfileRepositoryTests : SizedTestFixture
    {
        // --- Fake 実装 ---

        private class FakeJsonParser : IJsonParser
        {
            public FacialProfile? LastParsedResult { get; set; }
            public string LastSerializedResult { get; set; } = "{}";
            public bool ThrowOnParse { get; set; }
            public bool ThrowOnSerialize { get; set; }

            public FacialProfile ParseProfile(string json)
            {
                if (ThrowOnParse)
                    throw new FormatException("パースエラー");
                if (LastParsedResult.HasValue)
                    return LastParsedResult.Value;
                // デフォルトのプロファイルを返す
                return new FacialProfile("1.0");
            }

            public string SerializeProfile(FacialProfile profile)
            {
                if (ThrowOnSerialize)
                    throw new FormatException("シリアライズエラー");
                return LastSerializedResult;
            }

            public FacialControlConfig ParseConfig(string json)
            {
                return new FacialControlConfig("1.0");
            }

            public string SerializeConfig(FacialControlConfig config)
            {
                return "{}";
            }
        }

        // --- ヘルパー ---

        private string _tempDir;
        private FakeJsonParser _parser;
        private FileProfileRepository _repository;

        private static FacialProfile CreateTestProfile()
        {
            var layers = new[]
            {
                new LayerDefinition("emotion", 0, ExclusionMode.LastWins),
                new LayerDefinition("lipsync", 1, ExclusionMode.Blend)
            };
            var expressions = new[]
            {
                new Expression("expr-1", "smile", "emotion")
            };
            return new FacialProfile("1.0", layers, expressions);
        }

        [SetUp]
        public void SetUp()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "FacialControl_Test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);

            _parser = new FakeJsonParser();
            _repository = new FileProfileRepository(_parser);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, true);
        }

        // --- コンストラクタ ---

        [Test]
        public void Constructor_NullParser_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new FileProfileRepository(null));
        }

        [Test]
        public void Constructor_ValidParser_CreatesInstance()
        {
            var repository = new FileProfileRepository(_parser);

            Assert.IsNotNull(repository);
        }

        // --- LoadProfile ---

        [Test]
        public void LoadProfile_ValidFile_ReturnsProfile()
        {
            var filePath = Path.Combine(_tempDir, "profile.json");
            File.WriteAllText(filePath, "{\"schemaVersion\":\"1.0\"}");

            var expectedProfile = CreateTestProfile();
            _parser.LastParsedResult = expectedProfile;

            var result = _repository.LoadProfile(filePath);

            Assert.AreEqual("1.0", result.SchemaVersion);
            Assert.AreEqual(2, result.Layers.Length);
            Assert.AreEqual(1, result.Expressions.Length);
        }

        [Test]
        public void LoadProfile_NullPath_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                _repository.LoadProfile(null));
        }

        [Test]
        public void LoadProfile_EmptyPath_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() =>
                _repository.LoadProfile(""));
        }

        [Test]
        public void LoadProfile_WhitespacePath_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() =>
                _repository.LoadProfile("   "));
        }

        [Test]
        public void LoadProfile_NonExistentFile_ThrowsFileNotFoundException()
        {
            var filePath = Path.Combine(_tempDir, "nonexistent.json");

            Assert.Throws<FileNotFoundException>(() =>
                _repository.LoadProfile(filePath));
        }

        [Test]
        public void LoadProfile_PassesFileContentToParser()
        {
            var filePath = Path.Combine(_tempDir, "profile.json");
            var jsonContent = "{\"schemaVersion\":\"1.0\",\"layers\":[],\"expressions\":[]}";
            File.WriteAllText(filePath, jsonContent);

            string capturedJson = null;
            var parser = new CapturingJsonParser(json => capturedJson = json);
            var repository = new FileProfileRepository(parser);

            repository.LoadProfile(filePath);

            Assert.AreEqual(jsonContent, capturedJson);
        }

        [Test]
        public void LoadProfile_ParserThrows_PropagatesException()
        {
            var filePath = Path.Combine(_tempDir, "invalid.json");
            File.WriteAllText(filePath, "invalid json content");

            _parser.ThrowOnParse = true;

            Assert.Throws<FormatException>(() =>
                _repository.LoadProfile(filePath));
        }

        [Test]
        public void LoadProfile_Utf8Content_ReadsCorrectly()
        {
            var filePath = Path.Combine(_tempDir, "profile.json");
            var jsonContent = "{\"schemaVersion\":\"1.0\",\"expressions\":[{\"name\":\"笑顔\"}]}";
            File.WriteAllText(filePath, jsonContent, System.Text.Encoding.UTF8);

            string capturedJson = null;
            var parser = new CapturingJsonParser(json => capturedJson = json);
            var repository = new FileProfileRepository(parser);

            repository.LoadProfile(filePath);

            Assert.IsTrue(capturedJson.Contains("笑顔"));
        }

        [Test]
        public void LoadProfile_JapaneseFilePath_ReadsCorrectly()
        {
            var japaneseDir = Path.Combine(_tempDir, "テスト");
            Directory.CreateDirectory(japaneseDir);
            var filePath = Path.Combine(japaneseDir, "プロファイル.json");
            File.WriteAllText(filePath, "{\"schemaVersion\":\"1.0\"}");

            var result = _repository.LoadProfile(filePath);

            Assert.AreEqual("1.0", result.SchemaVersion);
        }

        // --- SaveProfile ---

        [Test]
        public void SaveProfile_ValidPathAndProfile_WritesFile()
        {
            var filePath = Path.Combine(_tempDir, "output.json");
            var profile = CreateTestProfile();
            _parser.LastSerializedResult = "{\"schemaVersion\":\"1.0\"}";

            _repository.SaveProfile(filePath, profile);

            Assert.IsTrue(File.Exists(filePath));
            var content = File.ReadAllText(filePath);
            Assert.AreEqual("{\"schemaVersion\":\"1.0\"}", content);
        }

        [Test]
        public void SaveProfile_NullPath_ThrowsArgumentNullException()
        {
            var profile = CreateTestProfile();

            Assert.Throws<ArgumentNullException>(() =>
                _repository.SaveProfile(null, profile));
        }

        [Test]
        public void SaveProfile_EmptyPath_ThrowsArgumentException()
        {
            var profile = CreateTestProfile();

            Assert.Throws<ArgumentException>(() =>
                _repository.SaveProfile("", profile));
        }

        [Test]
        public void SaveProfile_WhitespacePath_ThrowsArgumentException()
        {
            var profile = CreateTestProfile();

            Assert.Throws<ArgumentException>(() =>
                _repository.SaveProfile("   ", profile));
        }

        [Test]
        public void SaveProfile_CreatesParentDirectories()
        {
            var filePath = Path.Combine(_tempDir, "sub", "dir", "profile.json");
            var profile = CreateTestProfile();
            _parser.LastSerializedResult = "{\"schemaVersion\":\"1.0\"}";

            _repository.SaveProfile(filePath, profile);

            Assert.IsTrue(File.Exists(filePath));
        }

        [Test]
        public void SaveProfile_OverwritesExistingFile()
        {
            var filePath = Path.Combine(_tempDir, "output.json");
            File.WriteAllText(filePath, "old content");
            var profile = CreateTestProfile();
            _parser.LastSerializedResult = "new content";

            _repository.SaveProfile(filePath, profile);

            var content = File.ReadAllText(filePath);
            Assert.AreEqual("new content", content);
        }

        [Test]
        public void SaveProfile_SerializerThrows_PropagatesException()
        {
            var filePath = Path.Combine(_tempDir, "output.json");
            var profile = CreateTestProfile();
            _parser.ThrowOnSerialize = true;

            Assert.Throws<FormatException>(() =>
                _repository.SaveProfile(filePath, profile));
        }

        [Test]
        public void SaveProfile_Utf8Encoding_WritesCorrectly()
        {
            var filePath = Path.Combine(_tempDir, "output.json");
            var profile = CreateTestProfile();
            _parser.LastSerializedResult = "{\"name\":\"笑顔\"}";

            _repository.SaveProfile(filePath, profile);

            var content = File.ReadAllText(filePath, System.Text.Encoding.UTF8);
            Assert.IsTrue(content.Contains("笑顔"));
        }

        [Test]
        public void SaveProfile_JapaneseFilePath_WritesCorrectly()
        {
            var japaneseDir = Path.Combine(_tempDir, "出力");
            Directory.CreateDirectory(japaneseDir);
            var filePath = Path.Combine(japaneseDir, "プロファイル.json");
            var profile = CreateTestProfile();
            _parser.LastSerializedResult = "{\"schemaVersion\":\"1.0\"}";

            _repository.SaveProfile(filePath, profile);

            Assert.IsTrue(File.Exists(filePath));
        }

        // --- ラウンドトリップ ---

        [Test]
        public void RoundTrip_SaveThenLoad_PreservesProfile()
        {
            var filePath = Path.Combine(_tempDir, "roundtrip.json");
            var profile = CreateTestProfile();

            var serializedJson = "{\"schemaVersion\":\"1.0\",\"layers\":[],\"expressions\":[]}";
            _parser.LastSerializedResult = serializedJson;

            _repository.SaveProfile(filePath, profile);

            // 読み込み時のパーサー戻り値を設定
            _parser.LastParsedResult = profile;

            var loaded = _repository.LoadProfile(filePath);

            Assert.AreEqual(profile.SchemaVersion, loaded.SchemaVersion);
            Assert.AreEqual(profile.Layers.Length, loaded.Layers.Length);
            Assert.AreEqual(profile.Expressions.Length, loaded.Expressions.Length);
        }

        // --- キャプチャ用 JsonParser ---

        private class CapturingJsonParser : IJsonParser
        {
            private readonly Action<string> _captureAction;

            public CapturingJsonParser(Action<string> captureAction)
            {
                _captureAction = captureAction;
            }

            public FacialProfile ParseProfile(string json)
            {
                _captureAction?.Invoke(json);
                return new FacialProfile("1.0");
            }

            public string SerializeProfile(FacialProfile profile)
            {
                return "{}";
            }

            public FacialControlConfig ParseConfig(string json)
            {
                return new FacialControlConfig("1.0");
            }

            public string SerializeConfig(FacialControlConfig config)
            {
                return "{}";
            }
        }
    }
}
