using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.EditMode.Editor.Sampling
{
    /// <summary>
    /// <see cref="Hidano.FacialControl.Editor.Sampling"/> 名前空間が
    /// Runtime asmdef（Domain / Application / Adapters）から不可視であることを保証する。
    /// あわせて <c>Hidano.FacialControl.Editor.asmdef</c> の
    /// <c>includePlatforms</c> が <c>["Editor"]</c> 1 件であることを静的に検査する。
    /// </summary>
    [TestFixture]
    [MediumTest]
    public class EditorOnlyVisibilityTests : SizedTestFixture
    {
        private const string EditorSamplingNamespace = "Hidano.FacialControl.Editor.Sampling";

        private static readonly string[] RuntimeAssemblyNames =
        {
            "Hidano.FacialControl.Domain",
            "Hidano.FacialControl.Application",
            "Hidano.FacialControl.Adapters",
        };

        [Test]
        public void EditorSamplingNamespace_NotPresentIn_RuntimeAssemblies()
        {
            foreach (var assemblyName in RuntimeAssemblyNames)
            {
                var assembly = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => string.Equals(a.GetName().Name, assemblyName, StringComparison.Ordinal));

                Assert.IsNotNull(assembly,
                    $"Runtime assembly '{assemblyName}' should be loaded in the current AppDomain.");

                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    types = ex.Types.Where(t => t != null).ToArray();
                }

                var leaked = types
                    .Where(t => t.Namespace != null
                                && (t.Namespace == EditorSamplingNamespace
                                    || t.Namespace.StartsWith(EditorSamplingNamespace + ".", StringComparison.Ordinal)))
                    .Select(t => t.FullName)
                    .ToArray();

                Assert.IsEmpty(leaked,
                    $"Runtime assembly '{assemblyName}' must not expose any type in '{EditorSamplingNamespace}'. " +
                    $"Found: {string.Join(", ", leaked)}");
            }
        }

        [Test]
        public void EditorSamplingNamespace_IsHostedIn_EditorAssembly()
        {
            var samplerType = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(SafeGetTypes)
                .FirstOrDefault(t => t.FullName == EditorSamplingNamespace + ".AnimationClipExpressionSampler");

            Assert.IsNotNull(samplerType,
                "AnimationClipExpressionSampler must be discoverable via reflection from the test assembly (Editor asmdef reference).");

            var hostAssemblyName = samplerType.Assembly.GetName().Name;
            Assert.AreEqual("Hidano.FacialControl.Editor", hostAssemblyName,
                "AnimationClipExpressionSampler must reside in the Editor-only assembly so it is not linked into Runtime builds.");
        }

        [Test]
        public void EditorAsmdef_IncludePlatforms_IsEditorOnly()
        {
            var asmdefPath = ResolveEditorAsmdefPath();
            Assert.IsTrue(File.Exists(asmdefPath),
                $"Editor asmdef should exist at '{asmdefPath}'.");

            var json = File.ReadAllText(asmdefPath);

            // 最低限の static 検査: includePlatforms に "Editor" のみが入っていること、
            // および excludePlatforms が空であること。Newtonsoft 依存を避けるため正規表現で抽出する。
            var includeMatch = System.Text.RegularExpressions.Regex.Match(
                json,
                "\"includePlatforms\"\\s*:\\s*\\[(?<list>[^\\]]*)\\]",
                System.Text.RegularExpressions.RegexOptions.Singleline);
            Assert.IsTrue(includeMatch.Success,
                "includePlatforms field must exist in Hidano.FacialControl.Editor.asmdef.");

            var includeList = includeMatch.Groups["list"].Value;
            var entries = System.Text.RegularExpressions.Regex.Matches(includeList, "\"([^\"]*)\"");
            Assert.AreEqual(1, entries.Count,
                $"includePlatforms must contain exactly 1 entry. Actual list: '{includeList}'");
            Assert.AreEqual("Editor", entries[0].Groups[1].Value,
                "includePlatforms must be [\"Editor\"] to keep AnimationClipExpressionSampler out of Runtime builds.");

            var excludeMatch = System.Text.RegularExpressions.Regex.Match(
                json,
                "\"excludePlatforms\"\\s*:\\s*\\[(?<list>[^\\]]*)\\]",
                System.Text.RegularExpressions.RegexOptions.Singleline);
            Assert.IsTrue(excludeMatch.Success,
                "excludePlatforms field must exist in Hidano.FacialControl.Editor.asmdef.");
            var excludeEntries = System.Text.RegularExpressions.Regex.Matches(
                excludeMatch.Groups["list"].Value, "\"([^\"]*)\"");
            Assert.AreEqual(0, excludeEntries.Count,
                "excludePlatforms must be empty when includePlatforms is set; otherwise platform exposure is undefined.");
        }

        private static Type[] SafeGetTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                return ex.Types.Where(t => t != null).ToArray();
            }
            catch
            {
                return Array.Empty<Type>();
            }
        }

        private static string ResolveEditorAsmdefPath()
        {
            // AssetDatabase 経由で UPM パッケージ内の asmdef を解決する。
            // パッケージは "Packages/com.hidano.facialcontrol/Editor/Hidano.FacialControl.Editor.asmdef"。
            var guids = AssetDatabase.FindAssets("Hidano.FacialControl.Editor t:AssemblyDefinitionAsset");
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith("Hidano.FacialControl.Editor.asmdef", StringComparison.Ordinal))
                {
                    return Path.GetFullPath(path);
                }
            }
            // Fallback: 既知パスから直接解決
            return Path.GetFullPath("Packages/com.hidano.facialcontrol/Editor/Hidano.FacialControl.Editor.asmdef");
        }
    }
}
