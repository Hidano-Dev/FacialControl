using System.Collections.Generic;
using System.Reflection;
using Hidano.FacialControl.Testing;
using NUnit.Framework;
using NUnit.Framework.Interfaces;

namespace Hidano.FacialControl.Tests.Small.Testing
{
    /// <summary>
    /// 設計上の不変条件（test-policy 区分 C）: すべてのテストはサイズ（Small / Medium / Large）をちょうど 1 つ宣言する。
    /// 宣言漏れは CI の Small ジョブでこのテストが失敗することで検出される
    /// （<c>-testCategory</c> で絞り込んだジョブは未宣言テストを一切実行しないため、静的な検査が必要）。
    /// </summary>
    [SmallTest]
    public sealed class TestSizeDeclarationTests : SizedTestFixture
    {
        [Test]
        public void ProjectTestAssemblies_AreDiscoverable()
        {
            var assemblies = TestAssemblyCatalog.FindProjectTestAssemblies();

            Assert.That(assemblies, Is.Not.Empty, "テストアセンブリが 1 つも見つかりません。命名規約（Hidano.FacialControl*.Tests*）を確認してください。");
            Assert.That(assemblies, Has.Some.Matches<Assembly>(a => a == typeof(TestSizeDeclarationTests).Assembly),
                "この検査自身を含むアセンブリが対象に含まれていません。");
        }

        [Test]
        public void AllTestMethods_DeclareExactlyOneSize()
        {
            var violations = new List<string>();
            int inspected = 0;

            foreach (Assembly assembly in TestAssemblyCatalog.FindProjectTestAssemblies())
            {
                foreach (var type in TestAssemblyCatalog.GetLoadableTypes(assembly))
                {
                    if (type.IsAbstract || type.IsInterface || type.IsGenericTypeDefinition)
                    {
                        continue;
                    }

                    foreach (MethodInfo method in type.GetMethods(
                        BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                    {
                        if (!IsTestMethod(method))
                        {
                            continue;
                        }

                        inspected++;
                        var sizes = TestSizeResolver.CollectDeclaredSizes(method, type);
                        if (sizes.Count != 1)
                        {
                            violations.Add($"{type.FullName}.{method.Name} : {TestSizeResolver.Describe(sizes)}");
                        }
                    }
                }
            }

            Assert.That(inspected, Is.GreaterThan(0), "検査対象のテストメソッドが見つかりません。");
            Assert.That(violations, Is.Empty,
                "サイズ未宣言（または複数宣言）のテストがあります。fixture クラスに [SmallTest] / [MediumTest] / [LargeTest] を付けてください:\n"
                + string.Join("\n", violations));
        }

        /// <summary>NUnit / Unity Test Framework がテストとして組み立てる属性（Test, TestCase, TestCaseSource, Theory, UnityTest）を持つか。</summary>
        private static bool IsTestMethod(MethodInfo method)
        {
            foreach (object attribute in method.GetCustomAttributes(true))
            {
                if (attribute is ISimpleTestBuilder || attribute is ITestBuilder)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
