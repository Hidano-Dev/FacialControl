using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEngine;

namespace Hidano.FacialControl.Tests.EditMode.Editor.Inspector
{
    [TestFixture]
    public class SampleAssetsAreInSyncTests
    {
        private const string DevStreamingProfilePath =
            "Assets/StreamingAssets/FacialControl/MultiSourceBlendDemoCharacter/profile.json";

        private const string ImportedSampleAssetPath =
            "Assets/Samples/FacialControl InputSystem/1.0.0/Multi Source Blend Demo/MultiSourceBlendDemoCharacter.asset";

        private const string PackageSampleProfilePath =
            "Packages/com.hidano.facialcontrol.inputsystem/Samples~/MultiSourceBlendDemo/StreamingAssets/FacialControl/MultiSourceBlendDemoCharacter/profile.json";

        private const string PackageSampleAssetPath =
            "Packages/com.hidano.facialcontrol.inputsystem/Samples~/MultiSourceBlendDemo/MultiSourceBlendDemoCharacter.asset";

        // 第 3 コピー: dev プロジェクト側 Sample import 結果。dev 起動時に
        // Assets/StreamingAssets/.. へコピーされる前のテンプレートとして配置されるが、
        // Sample Import 経路の整合性検証では dev StreamingAssets の正本と一致している必要がある。
        private const string ImportedSampleStreamingProfilePath =
            "Assets/Samples/FacialControl InputSystem/1.0.0/Multi Source Blend Demo/StreamingAssets/FacialControl/MultiSourceBlendDemoCharacter/profile.json";

        [Test]
        public void ProfileJson_DevStreamingAssetsAndPackageSample_AreByteIdentical()
        {
            string devPath = ResolveProjectPath(DevStreamingProfilePath);
            string packagePath = ResolveProjectPath(PackageSampleProfilePath);
            AssertFileExists(devPath, DevStreamingProfilePath);
            AssertFileExists(packagePath, PackageSampleProfilePath);

            byte[] devBytes = File.ReadAllBytes(devPath);
            byte[] packageBytes = File.ReadAllBytes(packagePath);

            Assert.That(
                devBytes,
                Is.EqualTo(packageBytes),
                "MultiSourceBlendDemo profile.json drift detected between dev StreamingAssets and package Samples~.\n" +
                $"Dev: {DevStreamingProfilePath}\n" +
                $"Samples~: {PackageSampleProfilePath}");
        }

        [Test]
        public void MultiSourceBlendDemoCharacterAsset_DoesNotContainLegacyBlinkOverlayExpression()
        {
            AssertDoesNotContainLegacyBlinkOverlayExpression(ImportedSampleAssetPath, required: false);
            AssertDoesNotContainLegacyBlinkOverlayExpression(PackageSampleAssetPath, required: false);
        }

        [Test]
        public void MultiSourceBlendDemoCharacterAsset_YamlKeySetMatchesPackageSample_WhenPackageAssetExists()
        {
            string importedPath = ResolveProjectPath(ImportedSampleAssetPath);
            string packagePath = ResolveProjectPath(PackageSampleAssetPath);
            if (!File.Exists(importedPath))
            {
                Assert.Pass("Imported sample asset does not exist (Assets/Samples removed); YAML key-set comparison is not applicable.");
            }

            if (!File.Exists(packagePath))
            {
                Assert.Pass("Package Samples~ MultiSourceBlendDemoCharacter.asset does not exist; YAML key-set comparison is not applicable.");
            }

            IReadOnlyCollection<string> importedKeys = ExtractYamlKeySet(File.ReadAllText(importedPath, Encoding.UTF8));
            IReadOnlyCollection<string> packageKeys = ExtractYamlKeySet(File.ReadAllText(packagePath, Encoding.UTF8));

            CollectionAssert.AreEquivalent(
                importedKeys,
                packageKeys,
                "MultiSourceBlendDemoCharacter.asset YAML key set drift detected between imported dev sample and package Samples~.\n" +
                $"Dev sample: {ImportedSampleAssetPath}\n" +
                $"Samples~: {PackageSampleAssetPath}");
        }

        [Test]
        public void ProfileJson_DefaultOverlaysSuppress_IsFalseInAllSampleCopies()
        {
            AssertDefaultOverlaysSuppressFalseInProfile(DevStreamingProfilePath, required: true);
            AssertDefaultOverlaysSuppressFalseInProfile(PackageSampleProfilePath, required: true);
            AssertDefaultOverlaysSuppressFalseInProfile(ImportedSampleStreamingProfilePath, required: false);
        }

        [Test]
        public void MultiSourceBlendDemoCharacterAsset_DefaultOverlaysSuppress_IsFalseInAllSampleCopies()
        {
            AssertDefaultOverlaysSuppressFalseInAsset(ImportedSampleAssetPath, required: false);
            AssertDefaultOverlaysSuppressFalseInAsset(PackageSampleAssetPath, required: false);
        }

        [Test]
        public void ProfileJson_ImportedSampleStreamingAssets_AreByteIdenticalToDev()
        {
            // dev `Assets/StreamingAssets/...` と Sample import 配下の同名 profile.json は
            // 3 点同期 (dev / Samples~ / Sample import 結果) の最後の 1 つ。Assets/Samples が
            // 存在しない場合は Import 経路の検証対象が無いため skip-pass する。
            string devPath = ResolveProjectPath(DevStreamingProfilePath);
            string importedPath = ResolveProjectPath(ImportedSampleStreamingProfilePath);
            AssertFileExists(devPath, DevStreamingProfilePath);
            if (!File.Exists(importedPath))
            {
                Assert.Pass($"Imported sample profile does not exist ({ImportedSampleStreamingProfilePath}); byte-identical comparison skipped.");
            }

            byte[] devBytes = File.ReadAllBytes(devPath);
            byte[] importedBytes = File.ReadAllBytes(importedPath);

            Assert.That(
                devBytes,
                Is.EqualTo(importedBytes),
                "MultiSourceBlendDemo profile.json drift detected between dev StreamingAssets and imported sample copy.\n" +
                $"Dev: {DevStreamingProfilePath}\n" +
                $"Imported sample: {ImportedSampleStreamingProfilePath}");
        }

        [Test]
        public void ProfileJson_ImportedSampleStreamingAssets_DoesNotContainLegacyExpressionIdField()
        {
            // 第 3 コピーが旧 `"expressionId"` フィールドを保持していると Sample Import 経路で
            // SystemTextJsonParser.RejectLegacyExpressionIdInOverlays が FormatException を投げ、
            // 起動できなくなる。legacy gaze の expressionId は別スコープなので除外する。
            // Assets/Samples が存在しない場合は検証対象が無いため skip-pass する。
            string importedPath = ResolveProjectPath(ImportedSampleStreamingProfilePath);
            if (!File.Exists(importedPath))
            {
                Assert.Pass($"Imported sample profile does not exist ({ImportedSampleStreamingProfilePath}); legacy field check skipped.");
            }

            string text = NormalizeLineEndings(File.ReadAllText(importedPath, Encoding.UTF8));
            string stripped = StripGazeConfigsSection(text);

            StringAssert.DoesNotContain(
                "\"expressionId\"",
                stripped,
                $"{ImportedSampleStreamingProfilePath} still contains a legacy \"expressionId\" field outside gaze scope.");
        }

        [Test]
        public void MultiSourceBlendDemoCharacterAsset_DefaultOverlaysBlinkSnapshot_HasNonZeroMabataki()
        {
            // .asset の `_defaultOverlays[0].cachedSnapshot.blendShapes` に `まばたき` が含まれ、
            // かつ value が 0 でないことを検証する。preview.2 移行中に旧 blink_overlay
            // (`まばたき=1.0`) の値が新 `_defaultOverlays` へ転写されないまま 0.0 に化けると、
            // Sample 起動時に blink overlay が見た目に乗らない (Req 7.6 違反)。
            AssertMabatakiNonZeroInDefaultOverlays(ImportedSampleAssetPath, required: false);
            AssertMabatakiNonZeroInDefaultOverlays(PackageSampleAssetPath, required: false);
        }

        private static void AssertDefaultOverlaysSuppressFalseInProfile(string relativePath, bool required)
        {
            string path = ResolveProjectPath(relativePath);
            if (!File.Exists(path))
            {
                if (required)
                {
                    Assert.Fail($"Required sample profile is missing: {relativePath}");
                }

                return;
            }

            string text = NormalizeLineEndings(File.ReadAllText(path, Encoding.UTF8));
            string section = ExtractJsonArraySection(text, "\"defaultOverlays\"", relativePath);
            int suppressCount = 0;

            using (var reader = new StringReader(section))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    string trimmed = line.Trim();
                    if (!trimmed.StartsWith("\"suppress\"", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    suppressCount++;
                    Assert.That(
                        trimmed,
                        Does.Contain("false"),
                        $"{relativePath}: defaultOverlays suppress must be false, but found '{trimmed}'.");
                    Assert.That(
                        trimmed,
                        Does.Not.Contain("true"),
                        $"{relativePath}: defaultOverlays suppress must not be true.");
                }
            }

            Assert.That(
                suppressCount,
                Is.GreaterThan(0),
                $"{relativePath}: defaultOverlays section does not contain a suppress field.");
        }

        private static void AssertDefaultOverlaysSuppressFalseInAsset(string relativePath, bool required)
        {
            string path = ResolveProjectPath(relativePath);
            if (!File.Exists(path))
            {
                if (required)
                {
                    Assert.Fail($"Required sample asset is missing: {relativePath}");
                }

                return;
            }

            string text = NormalizeLineEndings(File.ReadAllText(path, Encoding.UTF8));
            string section = ExtractDefaultOverlaysSection(text, relativePath);
            int suppressCount = 0;

            using (var reader = new StringReader(section))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    string trimmed = line.Trim();
                    if (!trimmed.StartsWith("suppress:", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    suppressCount++;
                    string value = trimmed.Substring("suppress:".Length).Trim();
                    Assert.That(
                        value,
                        Is.EqualTo("0").Or.EqualTo("false"),
                        $"{relativePath}: _defaultOverlays suppress must be false/0, but found '{value}'.");
                }
            }

            Assert.That(
                suppressCount,
                Is.GreaterThan(0),
                $"{relativePath}: _defaultOverlays section does not contain a suppress field.");
        }

        private static void AssertMabatakiNonZeroInDefaultOverlays(string relativePath, bool required)
        {
            string path = ResolveProjectPath(relativePath);
            if (!File.Exists(path))
            {
                if (required)
                {
                    Assert.Fail($"Required sample asset is missing: {relativePath}");
                }

                return;
            }

            string text = NormalizeLineEndings(File.ReadAllText(path, Encoding.UTF8));
            int defaultOverlaysIdx = text.IndexOf("\n  _defaultOverlays:\n", StringComparison.Ordinal);
            Assert.That(
                defaultOverlaysIdx,
                Is.GreaterThanOrEqualTo(0),
                $"{relativePath}: '_defaultOverlays' section not found in asset YAML.");

            // _defaultOverlays セクションの直後 (次の同階層 key `_adapterBindings:` まで) を切り出す
            int sectionStart = defaultOverlaysIdx;
            int sectionEnd = text.IndexOf("\n  _adapterBindings:", sectionStart, StringComparison.Ordinal);
            if (sectionEnd < 0)
            {
                sectionEnd = text.Length;
            }
            string section = text.Substring(sectionStart, sectionEnd - sectionStart);

            // section 中の `name: まばたき` 直後の `value: <num>` を探す
            int nameIdx = section.IndexOf("name: まばたき", StringComparison.Ordinal);
            Assert.That(
                nameIdx,
                Is.GreaterThanOrEqualTo(0),
                $"{relativePath}: '_defaultOverlays[*].cachedSnapshot.blendShapes' does not contain a 'まばたき' entry.\n" +
                "preview.2 移行で旧 blink_overlay の snapshot が転写されていない可能性があります。");

            int valueIdx = section.IndexOf("value:", nameIdx, StringComparison.Ordinal);
            Assert.That(
                valueIdx,
                Is.GreaterThanOrEqualTo(0),
                $"{relativePath}: 'まばたき' entry found but no 'value:' line follows.");

            int valueStart = valueIdx + "value:".Length;
            int valueEnd = section.IndexOf('\n', valueStart);
            if (valueEnd < 0)
            {
                valueEnd = section.Length;
            }
            string valueStr = section.Substring(valueStart, valueEnd - valueStart).Trim();
            Assert.That(
                float.TryParse(valueStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float value),
                Is.True,
                $"{relativePath}: 'まばたき' value '{valueStr}' is not a valid float.");
            Assert.That(
                value,
                Is.Not.EqualTo(0f),
                $"{relativePath}: 'まばたき' value in _defaultOverlays cachedSnapshot is 0 — preview.2 移行で旧 blink_overlay の '{nameof(value)}=1.0' が転写されず blink overlay が機能しません (Req 7.6 違反)。");
        }

        private static string ExtractDefaultOverlaysSection(string text, string relativePath)
        {
            int defaultOverlaysIdx = text.IndexOf("\n  _defaultOverlays:\n", StringComparison.Ordinal);
            Assert.That(
                defaultOverlaysIdx,
                Is.GreaterThanOrEqualTo(0),
                $"{relativePath}: '_defaultOverlays' section not found in asset YAML.");

            int sectionStart = defaultOverlaysIdx;
            int sectionEnd = text.IndexOf("\n  _adapterBindings:", sectionStart, StringComparison.Ordinal);
            if (sectionEnd < 0)
            {
                sectionEnd = text.Length;
            }

            return text.Substring(sectionStart, sectionEnd - sectionStart);
        }

        private static string ExtractJsonArraySection(string json, string propertyName, string relativePath)
        {
            int propertyIdx = json.IndexOf(propertyName, StringComparison.Ordinal);
            Assert.That(
                propertyIdx,
                Is.GreaterThanOrEqualTo(0),
                $"{relativePath}: JSON property {propertyName} was not found.");

            int bracketStart = json.IndexOf('[', propertyIdx);
            Assert.That(
                bracketStart,
                Is.GreaterThanOrEqualTo(0),
                $"{relativePath}: JSON property {propertyName} does not start an array.");

            int depth = 0;
            bool inString = false;
            bool escaped = false;

            for (int i = bracketStart; i < json.Length; i++)
            {
                char c = json[i];
                if (inString)
                {
                    if (escaped)
                    {
                        escaped = false;
                    }
                    else if (c == '\\')
                    {
                        escaped = true;
                    }
                    else if (c == '"')
                    {
                        inString = false;
                    }

                    continue;
                }

                if (c == '"')
                {
                    inString = true;
                }
                else if (c == '[')
                {
                    depth++;
                }
                else if (c == ']')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return json.Substring(bracketStart, i - bracketStart + 1);
                    }
                }
            }

            Assert.Fail($"{relativePath}: JSON property {propertyName} array was not closed.");
            return string.Empty;
        }

        // legacy gaze セクションを
        // 文字列レベルで除去する。JSON 階層解析ではないため、本サンプル profile.json の構造
        // ルート配列に限定して有効。
        private static string StripGazeConfigsSection(string json)
        {
            int idx = json.IndexOf("\"legacyGazeKey\"", StringComparison.Ordinal);
            if (idx < 0)
            {
                return json;
            }

            int bracketStart = json.IndexOf('[', idx);
            if (bracketStart < 0)
            {
                return json;
            }

            int depth = 0;
            for (int i = bracketStart; i < json.Length; i++)
            {
                char c = json[i];
                if (c == '[') depth++;
                else if (c == ']')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return json.Substring(0, idx) + json.Substring(i + 1);
                    }
                }
            }

            return json;
        }

        private static void AssertDoesNotContainLegacyBlinkOverlayExpression(string relativePath, bool required)
        {
            string path = ResolveProjectPath(relativePath);
            if (!File.Exists(path))
            {
                if (required)
                {
                    Assert.Fail($"Required sample asset is missing: {relativePath}");
                }

                return;
            }

            string text = NormalizeLineEndings(File.ReadAllText(path, Encoding.UTF8));
            StringAssert.DoesNotContain(
                "\n  - id: blink_overlay\n",
                text,
                $"{relativePath} still contains the legacy blink_overlay Expression entry.");
        }

        private static IReadOnlyCollection<string> ExtractYamlKeySet(string yaml)
        {
            var keys = new SortedSet<string>(StringComparer.Ordinal);
            var stack = new List<YamlKeyFrame>();

            using (var reader = new StringReader(NormalizeLineEndings(yaml)))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    string trimmed = line.Trim();
                    if (trimmed.StartsWith("%", StringComparison.Ordinal) ||
                        trimmed.StartsWith("---", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    int indent = CountLeadingSpaces(line);
                    bool sequenceEntry = trimmed.StartsWith("- ", StringComparison.Ordinal);
                    string candidate = sequenceEntry ? trimmed.Substring(2).TrimStart() : trimmed;
                    int colonIndex = candidate.IndexOf(':');
                    if (colonIndex <= 0)
                    {
                        continue;
                    }

                    string key = candidate.Substring(0, colonIndex).Trim();
                    if (!IsYamlKey(key))
                    {
                        continue;
                    }

                    TrimStack(stack, indent, sequenceEntry);

                    string keyPath = stack.Count == 0
                        ? key
                        : string.Join(".", stack.Select(frame => frame.Key).Concat(new[] { key }));
                    keys.Add(keyPath);

                    string value = candidate.Substring(colonIndex + 1).Trim();
                    if (value.Length == 0)
                    {
                        stack.Add(new YamlKeyFrame(indent, key));
                    }
                }
            }

            return keys;
        }

        private static void TrimStack(List<YamlKeyFrame> stack, int indent, bool sequenceEntry)
        {
            while (stack.Count > 0)
            {
                int previousIndent = stack[stack.Count - 1].Indent;
                bool shouldPop = sequenceEntry ? previousIndent > indent : previousIndent >= indent;
                if (!shouldPop)
                {
                    return;
                }

                stack.RemoveAt(stack.Count - 1);
            }
        }

        private static bool IsYamlKey(string key)
        {
            if (key.Length == 0)
            {
                return false;
            }

            return key.IndexOf('"') < 0 &&
                   key.IndexOf('\'') < 0 &&
                   key.IndexOf('{') < 0 &&
                   key.IndexOf('}') < 0;
        }

        private static int CountLeadingSpaces(string line)
        {
            int count = 0;
            while (count < line.Length && line[count] == ' ')
            {
                count++;
            }

            return count;
        }

        private static string ResolveProjectPath(string relativePath)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, ".."));
            string normalized = relativePath.Replace('/', Path.DirectorySeparatorChar);
            return Path.GetFullPath(Path.Combine(projectRoot, normalized));
        }

        private static void AssertFileExists(string path, string relativePath)
        {
            Assert.That(File.Exists(path), Is.True, $"Required sample file is missing: {relativePath}");
        }

        private static string NormalizeLineEndings(string value)
        {
            return value.Replace("\r\n", "\n").Replace("\r", "\n");
        }

        private readonly struct YamlKeyFrame
        {
            public YamlKeyFrame(int indent, string key)
            {
                Indent = indent;
                Key = key;
            }

            public int Indent { get; }
            public string Key { get; }
        }
    }
}
