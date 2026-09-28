using System;
using System.Collections.Generic;
using NUnit.Framework;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Application.UseCases;

namespace Hidano.FacialControl.Tests.EditMode.Application
{
    [TestFixture]
    public class ProfileUseCaseTests
    {
        // --- Fake 実装 ---

        private class FakeJsonParser : IJsonParser
        {
            private readonly Dictionary<string, FacialProfile> _profiles = new();
            private readonly Dictionary<string, FacialControlConfig> _configs = new();

            public void RegisterProfile(string json, FacialProfile profile)
            {
                _profiles[json] = profile;
            }

            public void RegisterConfig(string json, FacialControlConfig config)
            {
                _configs[json] = config;
            }

            public bool ThrowOnParse { get; set; }
            public string ThrowMessage { get; set; } = "パースエラー";

            public FacialProfile ParseProfile(string json)
            {
                if (ThrowOnParse)
                    throw new InvalidOperationException(ThrowMessage);
                if (_profiles.TryGetValue(json, out var profile))
                    return profile;
                throw new InvalidOperationException("未登録の JSON です。");
            }

            public string SerializeProfile(FacialProfile profile)
            {
                return "{}";
            }

            public FacialControlConfig ParseConfig(string json)
            {
                if (_configs.TryGetValue(json, out var config))
                    return config;
                throw new InvalidOperationException("未登録の JSON です。");
            }

            public string SerializeConfig(FacialControlConfig config)
            {
                return "{}";
            }
        }

        private class FakeProfileRepository : IProfileRepository
        {
            private readonly Dictionary<string, FacialProfile> _store = new();
            public bool ThrowOnLoad { get; set; }
            public string ThrowMessage { get; set; } = "ファイルが見つかりません。";

            // JSON テキストを返すための辞書
            private readonly Dictionary<string, string> _jsonStore = new();

            public void RegisterProfile(string path, FacialProfile profile)
            {
                _store[path] = profile;
            }

            public void RegisterJson(string path, string json)
            {
                _jsonStore[path] = json;
            }

            public string GetJson(string path)
            {
                return _jsonStore.TryGetValue(path, out var json) ? json : null;
            }

            public FacialProfile LoadProfile(string path)
            {
                if (ThrowOnLoad)
                    throw new InvalidOperationException(ThrowMessage);
                if (_store.TryGetValue(path, out var profile))
                    return profile;
                throw new InvalidOperationException("ファイルが見つかりません: " + path);
            }

            public void SaveProfile(string path, FacialProfile profile)
            {
                _store[path] = profile;
            }
        }

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

        private static Expression CreateExpression(
            string id = "expr-1",
            string name = "smile",
            string layer = "emotion")
        {
            return new Expression(id, name, layer);
        }

        private static FacialProfile CreateProfile(
            LayerDefinition[] layers = null,
            Expression[] expressions = null)
        {
            return new FacialProfile(
                "1.0",
                layers ?? CreateDefaultLayers(),
                expressions ?? new[] { CreateExpression() });
        }

        private FakeProfileRepository _repository;
        private ProfileUseCase _useCase;

        [SetUp]
        public void SetUp()
        {
            _repository = new FakeProfileRepository();
            _useCase = new ProfileUseCase(_repository);
        }

        // --- コンストラクタ ---

        [Test]
        public void Constructor_NullRepository_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new ProfileUseCase(null));
        }

        [Test]
        public void Constructor_ValidRepository_CreatesInstance()
        {
            var useCase = new ProfileUseCase(_repository);

            Assert.IsNotNull(useCase);
        }

        // --- LoadProfile ---

        [Test]
        public void LoadProfile_ValidPath_ReturnsProfile()
        {
            var profile = CreateProfile();
            _repository.RegisterProfile("test.json", profile);

            var result = _useCase.LoadProfile("test.json");

            Assert.AreEqual("1.0", result.SchemaVersion);
            Assert.AreEqual(3, result.Layers.Length);
            Assert.AreEqual(1, result.Expressions.Length);
        }

        [Test]
        public void LoadProfile_NullPath_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                _useCase.LoadProfile(null));
        }

        [Test]
        public void LoadProfile_EmptyPath_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() =>
                _useCase.LoadProfile(""));
        }

        [Test]
        public void LoadProfile_WhitespacePath_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() =>
                _useCase.LoadProfile("   "));
        }

        [Test]
        public void LoadProfile_NonExistentPath_ThrowsException()
        {
            Assert.Throws<InvalidOperationException>(() =>
                _useCase.LoadProfile("nonexistent.json"));
        }

        [Test]
        public void LoadProfile_StoresCurrentProfile()
        {
            var profile = CreateProfile();
            _repository.RegisterProfile("test.json", profile);

            _useCase.LoadProfile("test.json");

            // GetExpression で現在のプロファイルが使われることを確認
            var expr = _useCase.GetExpression("expr-1");
            Assert.IsTrue(expr.HasValue);
            Assert.AreEqual("smile", expr.Value.Name);
        }

        [Test]
        public void LoadProfile_StoresCurrentPath()
        {
            var profile = CreateProfile();
            _repository.RegisterProfile("test.json", profile);

            _useCase.LoadProfile("test.json");

            Assert.AreEqual("test.json", _useCase.CurrentPath);
        }

        [Test]
        public void LoadProfile_ValidatesLayerReferences_NoInvalidReferences()
        {
            var layers = CreateDefaultLayers();
            var expressions = new[]
            {
                CreateExpression("id-1", "smile", "emotion"),
                CreateExpression("id-2", "wink", "eye")
            };
            var profile = new FacialProfile("1.0", layers, expressions);
            _repository.RegisterProfile("test.json", profile);

            // レイヤー参照が正しい場合、例外なく読み込める
            var result = _useCase.LoadProfile("test.json");

            Assert.AreEqual(2, result.Expressions.Length);
        }

        [Test]
        public void LoadProfile_MultipleExpressions_AllAccessible()
        {
            var layers = CreateDefaultLayers();
            var expressions = new[]
            {
                CreateExpression("id-1", "smile", "emotion"),
                CreateExpression("id-2", "wink", "eye"),
                CreateExpression("id-3", "talk", "lipsync")
            };
            var profile = new FacialProfile("1.0", layers, expressions);
            _repository.RegisterProfile("test.json", profile);

            _useCase.LoadProfile("test.json");

            Assert.IsTrue(_useCase.GetExpression("id-1").HasValue);
            Assert.IsTrue(_useCase.GetExpression("id-2").HasValue);
            Assert.IsTrue(_useCase.GetExpression("id-3").HasValue);
        }

        [Test]
        public void LoadProfile_OverwritesPreviousProfile()
        {
            var profile1 = new FacialProfile("1.0", CreateDefaultLayers(),
                new[] { CreateExpression("old-id", "old", "emotion") });
            var profile2 = new FacialProfile("2.0", CreateDefaultLayers(),
                new[] { CreateExpression("new-id", "new", "emotion") });
            _repository.RegisterProfile("path1.json", profile1);
            _repository.RegisterProfile("path2.json", profile2);

            _useCase.LoadProfile("path1.json");
            _useCase.LoadProfile("path2.json");

            Assert.IsFalse(_useCase.GetExpression("old-id").HasValue);
            Assert.IsTrue(_useCase.GetExpression("new-id").HasValue);
            Assert.AreEqual("path2.json", _useCase.CurrentPath);
        }

        // --- ReloadProfile ---

        [Test]
        public void ReloadProfile_AfterLoad_ReloadsFromSamePath()
        {
            var profile = CreateProfile();
            _repository.RegisterProfile("test.json", profile);
            _useCase.LoadProfile("test.json");

            // リポジトリのプロファイルを更新
            var updatedProfile = new FacialProfile("2.0", CreateDefaultLayers(),
                new[] { CreateExpression("new-id", "updated", "emotion") });
            _repository.RegisterProfile("test.json", updatedProfile);

            var result = _useCase.ReloadProfile();

            Assert.AreEqual("2.0", result.SchemaVersion);
            Assert.IsTrue(_useCase.GetExpression("new-id").HasValue);
        }

        [Test]
        public void ReloadProfile_NoProfileLoaded_ThrowsInvalidOperationException()
        {
            Assert.Throws<InvalidOperationException>(() =>
                _useCase.ReloadProfile());
        }

        [Test]
        public void ReloadProfile_PreservesCurrentPath()
        {
            var profile = CreateProfile();
            _repository.RegisterProfile("test.json", profile);
            _useCase.LoadProfile("test.json");

            _useCase.ReloadProfile();

            Assert.AreEqual("test.json", _useCase.CurrentPath);
        }

        // --- GetExpression ---

        [Test]
        public void GetExpression_ExistingId_ReturnsExpression()
        {
            var profile = CreateProfile();
            _repository.RegisterProfile("test.json", profile);
            _useCase.LoadProfile("test.json");

            var result = _useCase.GetExpression("expr-1");

            Assert.IsTrue(result.HasValue);
            Assert.AreEqual("smile", result.Value.Name);
        }

        [Test]
        public void GetExpression_NonExistingId_ReturnsNull()
        {
            var profile = CreateProfile();
            _repository.RegisterProfile("test.json", profile);
            _useCase.LoadProfile("test.json");

            var result = _useCase.GetExpression("nonexistent");

            Assert.IsFalse(result.HasValue);
        }

        [Test]
        public void GetExpression_NullId_ThrowsArgumentNullException()
        {
            var profile = CreateProfile();
            _repository.RegisterProfile("test.json", profile);
            _useCase.LoadProfile("test.json");

            Assert.Throws<ArgumentNullException>(() =>
                _useCase.GetExpression(null));
        }

        [Test]
        public void GetExpression_NoProfileLoaded_ThrowsInvalidOperationException()
        {
            Assert.Throws<InvalidOperationException>(() =>
                _useCase.GetExpression("expr-1"));
        }

        // --- GetExpressionsByLayer ---

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
            _repository.RegisterProfile("test.json", profile);
            _useCase.LoadProfile("test.json");

            var results = _useCase.GetExpressionsByLayer("emotion");

            Assert.AreEqual(2, results.Length);
            Assert.AreEqual("id-1", results.Span[0].Id);
            Assert.AreEqual("id-2", results.Span[1].Id);
        }

        [Test]
        public void GetExpressionsByLayer_NoMatchingExpressions_ReturnsEmpty()
        {
            var profile = CreateProfile();
            _repository.RegisterProfile("test.json", profile);
            _useCase.LoadProfile("test.json");

            var results = _useCase.GetExpressionsByLayer("lipsync");

            Assert.AreEqual(0, results.Length);
        }

        [Test]
        public void GetExpressionsByLayer_NullLayer_ThrowsArgumentNullException()
        {
            var profile = CreateProfile();
            _repository.RegisterProfile("test.json", profile);
            _useCase.LoadProfile("test.json");

            Assert.Throws<ArgumentNullException>(() =>
                _useCase.GetExpressionsByLayer(null));
        }

        [Test]
        public void GetExpressionsByLayer_NoProfileLoaded_ThrowsInvalidOperationException()
        {
            Assert.Throws<InvalidOperationException>(() =>
                _useCase.GetExpressionsByLayer("emotion"));
        }

        // --- CurrentProfile ---

        [Test]
        public void CurrentProfile_AfterLoad_ReturnsLoadedProfile()
        {
            var profile = CreateProfile();
            _repository.RegisterProfile("test.json", profile);
            _useCase.LoadProfile("test.json");

            var current = _useCase.CurrentProfile;

            Assert.IsTrue(current.HasValue);
            Assert.AreEqual("1.0", current.Value.SchemaVersion);
        }

        [Test]
        public void CurrentProfile_BeforeLoad_ReturnsNull()
        {
            Assert.IsFalse(_useCase.CurrentProfile.HasValue);
        }

        // --- 日本語パス ---

        [Test]
        public void LoadProfile_JapanesePath_ReturnsProfile()
        {
            var profile = CreateProfile();
            _repository.RegisterProfile("テスト/プロファイル.json", profile);

            var result = _useCase.LoadProfile("テスト/プロファイル.json");

            Assert.AreEqual("1.0", result.SchemaVersion);
        }

        // --- レイヤー検証結果の取得 ---

        [Test]
        public void LoadProfile_InvalidLayerReferences_StillLoadsProfile()
        {
            var layers = CreateDefaultLayers();
            var expressions = new[]
            {
                CreateExpression("id-1", "smile", "nonexistent")
            };
            var profile = new FacialProfile("1.0", layers, expressions);
            _repository.RegisterProfile("test.json", profile);

            // 未定義レイヤー参照があってもプロファイルは読み込まれる
            var result = _useCase.LoadProfile("test.json");

            Assert.AreEqual(1, result.Expressions.Length);
        }

        [Test]
        public void GetInvalidLayerReferences_AfterLoad_ReturnsInvalidReferences()
        {
            var layers = CreateDefaultLayers();
            var expressions = new[]
            {
                CreateExpression("id-1", "smile", "nonexistent"),
                CreateExpression("id-2", "wink", "eye")
            };
            var profile = new FacialProfile("1.0", layers, expressions);
            _repository.RegisterProfile("test.json", profile);
            _useCase.LoadProfile("test.json");

            var invalidRefs = _useCase.GetInvalidLayerReferences();

            Assert.AreEqual(1, invalidRefs.Count);
            Assert.AreEqual("id-1", invalidRefs[0].ExpressionId);
            Assert.AreEqual("nonexistent", invalidRefs[0].ReferencedLayer);
        }

        [Test]
        public void GetInvalidLayerReferences_NoProfileLoaded_ThrowsInvalidOperationException()
        {
            Assert.Throws<InvalidOperationException>(() =>
                _useCase.GetInvalidLayerReferences());
        }
    }
}
