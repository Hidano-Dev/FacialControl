using NUnit.Framework;
using Hidano.FacialControl.Adapters.Json;
using Hidano.FacialControl.Domain.Models;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.EditMode.Editor
{
    /// <summary>
    /// P19-T03: RendererPaths 検出後のキャッシュ更新 → JSON ラウンドトリップ検証。
    /// FacialProfile に RendererPaths を差し替えて再構築 → シリアライズ → パース → RendererPaths が一致するか検証する。
    /// </summary>
    [TestFixture]
    [SmallTest]
    public class RendererPathsCacheUpdateTests : SizedTestFixture
    {
        private SystemTextJsonParser _parser;

        [SetUp]
        public void SetUp()
        {
            _parser = new SystemTextJsonParser();
        }

        // --- ヘルパー ---

        /// <summary>
        /// テスト用のプロファイルを生成する（RendererPaths なし）
        /// </summary>
        private FacialProfile CreateTestProfile()
        {
            var layers = new[]
            {
                new LayerDefinition("emotion", 0, ExclusionMode.LastWins),
                new LayerDefinition("lipsync", 1, ExclusionMode.Blend)
            };

            var expressions = new[]
            {
                new Expression(
                    "expr-001",
                    "smile",
                    "emotion",
                    0.25f,
                    TransitionCurve.Linear,
                    new[]
                    {
                        new BlendShapeMapping("mouth_smile", 0.8f),
                        new BlendShapeMapping("eye_squint", 0.3f)
                    }),
                new Expression(
                    "expr-002",
                    "blink",
                    "emotion",
                    0.1f,
                    TransitionCurve.Linear,
                    new[]
                    {
                        new BlendShapeMapping("eye_close_L", 1.0f),
                        new BlendShapeMapping("eye_close_R", 1.0f)
                    })
            };

            return new FacialProfile(SystemTextJsonParser.SchemaVersionV2, layers, expressions);
        }

        /// <summary>
        /// テスト用のプロファイルを生成する（RendererPaths あり）
        /// </summary>
        private FacialProfile CreateTestProfileWithRendererPaths(string[] rendererPaths)
        {
            var layers = new[]
            {
                new LayerDefinition("emotion", 0, ExclusionMode.LastWins),
                new LayerDefinition("lipsync", 1, ExclusionMode.Blend)
            };

            var expressions = new[]
            {
                new Expression(
                    "expr-001",
                    "smile",
                    "emotion",
                    0.25f,
                    TransitionCurve.Linear,
                    new[]
                    {
                        new BlendShapeMapping("mouth_smile", 0.8f),
                        new BlendShapeMapping("eye_squint", 0.3f)
                    })
            };

            return new FacialProfile(SystemTextJsonParser.SchemaVersionV2, layers, expressions, rendererPaths);
        }

        /// <summary>
        /// OnDetectRendererPathsClicked の _cachedProfile 更新ロジックを再現する
        /// </summary>
        private FacialProfile RebuildWithRendererPaths(FacialProfile original, string[] newPaths)
        {
            return new FacialProfile(
                original.SchemaVersion,
                original.Layers.ToArray(),
                original.Expressions.ToArray(),
                newPaths);
        }

        /// <summary>
        /// シリアライズ → 再パースのラウンドトリップを行う
        /// </summary>
        private FacialProfile RoundTrip(FacialProfile profile)
        {
            var json = _parser.SerializeProfile(profile);
            return _parser.ParseProfile(json);
        }

        // --- RendererPaths 差し替え → ラウンドトリップテスト ---

        [Test]
        public void RebuildWithPaths_EmptyToNonEmpty_RoundTrip_PathsMatch()
        {
            var original = CreateTestProfile();
            var newPaths = new[] { "Armature/Body", "Armature/Face" };
            var rebuilt = RebuildWithRendererPaths(original, newPaths);
            var parsed = RoundTrip(rebuilt);

            var resultPaths = parsed.RendererPaths;
            Assert.AreEqual(2, resultPaths.Length);
            Assert.AreEqual("Armature/Body", resultPaths.Span[0]);
            Assert.AreEqual("Armature/Face", resultPaths.Span[1]);
        }

        [Test]
        public void RebuildWithPaths_NonEmptyToNonEmpty_RoundTrip_PathsUpdated()
        {
            var original = CreateTestProfileWithRendererPaths(new[] { "Old/Path" });
            var newPaths = new[] { "New/Body", "New/Face", "New/Hair" };
            var rebuilt = RebuildWithRendererPaths(original, newPaths);
            var parsed = RoundTrip(rebuilt);

            var resultPaths = parsed.RendererPaths;
            Assert.AreEqual(3, resultPaths.Length);
            Assert.AreEqual("New/Body", resultPaths.Span[0]);
            Assert.AreEqual("New/Face", resultPaths.Span[1]);
            Assert.AreEqual("New/Hair", resultPaths.Span[2]);
        }

        [Test]
        public void RebuildWithPaths_LayersPreserved()
        {
            var original = CreateTestProfile();
            var newPaths = new[] { "Armature/Body" };
            var rebuilt = RebuildWithRendererPaths(original, newPaths);
            var parsed = RoundTrip(rebuilt);

            Assert.AreEqual(2, parsed.Layers.Length);
            Assert.AreEqual("emotion", parsed.Layers.Span[0].Name);
            Assert.AreEqual(0, parsed.Layers.Span[0].Priority);
            Assert.AreEqual(ExclusionMode.LastWins, parsed.Layers.Span[0].ExclusionMode);
            Assert.AreEqual("lipsync", parsed.Layers.Span[1].Name);
            Assert.AreEqual(1, parsed.Layers.Span[1].Priority);
            Assert.AreEqual(ExclusionMode.Blend, parsed.Layers.Span[1].ExclusionMode);
        }

        [Test]
        public void RebuildWithPaths_ExpressionsPreserved()
        {
            var original = CreateTestProfile();
            var newPaths = new[] { "Armature/Body" };
            var rebuilt = RebuildWithRendererPaths(original, newPaths);
            var parsed = RoundTrip(rebuilt);

            Assert.AreEqual(2, parsed.Expressions.Length);

            var expr0 = parsed.Expressions.Span[0];
            Assert.AreEqual("expr-001", expr0.Id);
            Assert.AreEqual("smile", expr0.Name);
            Assert.AreEqual("emotion", expr0.Layer);
            Assert.AreEqual(0.25f, expr0.TransitionDuration, 0.001f);
            Assert.AreEqual(2, expr0.BlendShapeValues.Length);
            Assert.AreEqual("mouth_smile", expr0.BlendShapeValues.Span[0].Name);
            Assert.AreEqual(0.8f, expr0.BlendShapeValues.Span[0].Value, 0.001f);

            var expr1 = parsed.Expressions.Span[1];
            Assert.AreEqual("expr-002", expr1.Id);
            Assert.AreEqual("blink", expr1.Name);
            Assert.AreEqual(2, expr1.BlendShapeValues.Length);
        }

        [Test]
        public void RebuildWithPaths_SchemaVersionPreserved()
        {
            var original = CreateTestProfile();
            var rebuilt = RebuildWithRendererPaths(original, new[] { "Body" });
            var parsed = RoundTrip(rebuilt);

            Assert.AreEqual(SystemTextJsonParser.SchemaVersionV2, parsed.SchemaVersion);
        }

        [Test]
        public void RebuildWithPaths_EmptyArray_RoundTrip_EmptyPaths()
        {
            var original = CreateTestProfileWithRendererPaths(new[] { "Old/Path" });
            var rebuilt = RebuildWithRendererPaths(original, new string[0]);
            var parsed = RoundTrip(rebuilt);

            Assert.AreEqual(0, parsed.RendererPaths.Length);
        }

        [Test]
        public void RebuildWithPaths_SinglePath_RoundTrip_PathMatch()
        {
            var original = CreateTestProfile();
            var rebuilt = RebuildWithRendererPaths(original, new[] { "Armature/Body" });
            var parsed = RoundTrip(rebuilt);

            Assert.AreEqual(1, parsed.RendererPaths.Length);
            Assert.AreEqual("Armature/Body", parsed.RendererPaths.Span[0]);
        }
    }
}
