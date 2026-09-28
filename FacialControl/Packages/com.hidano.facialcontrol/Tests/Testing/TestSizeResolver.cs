using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using NUnit.Framework.Interfaces;
using NUnit.Framework.Internal;

namespace Hidano.FacialControl.Testing
{
    /// <summary>
    /// テストメソッドと fixture 型からサイズ宣言を収集する。
    /// <see cref="TestSizeAttribute"/> と、サイズ名を持つ素の <see cref="CategoryAttribute"/>（<c>[Category("Small")]</c> 等）の両方を認識する。
    /// </summary>
    public static class TestSizeResolver
    {
        /// <summary>
        /// メソッドと fixture 型（基底型を含む）に宣言されたサイズの集合を返す。正しく宣言されたテストは要素数 1 になる。
        /// </summary>
        /// <param name="method">テストメソッド。null 可（fixture のみ調べる）。</param>
        /// <param name="fixtureType">テストを実行する具象 fixture 型。null 可。</param>
        public static HashSet<TestSize> CollectDeclaredSizes(MethodInfo method, Type fixtureType)
        {
            var sizes = new HashSet<TestSize>();
            if (method != null)
            {
                AddFromAttributes(method.GetCustomAttributes(true), sizes);
            }

            if (fixtureType != null)
            {
                AddFromAttributes(fixtureType.GetCustomAttributes(true), sizes);
            }

            return sizes;
        }

        /// <summary>
        /// 現在実行中のテストに宣言されたサイズの集合を返す。
        /// メソッド側は <see cref="TestContext.CurrentContext"/> の Category プロパティ、fixture 側は型の属性から求める。
        /// </summary>
        public static HashSet<TestSize> CollectFromCurrentTest(Type fixtureType)
        {
            var sizes = new HashSet<TestSize>();
            var context = TestContext.CurrentContext;
            if (context != null && context.Test != null && context.Test.Properties != null)
            {
                AddFromCategories(context.Test.Properties[PropertyNames.Category], sizes);
            }

            if (fixtureType != null)
            {
                AddFromAttributes(fixtureType.GetCustomAttributes(true), sizes);
            }

            return sizes;
        }

        /// <summary>サイズ集合を人が読める文字列にする（エラーメッセージ用）。</summary>
        public static string Describe(IEnumerable<TestSize> sizes)
        {
            var names = new List<string>();
            foreach (var size in sizes)
            {
                names.Add(TestSizes.ToCategory(size));
            }

            names.Sort(StringComparer.Ordinal);
            return names.Count == 0 ? "(none)" : string.Join(", ", names);
        }

        private static void AddFromAttributes(object[] attributes, HashSet<TestSize> sizes)
        {
            foreach (object attribute in attributes)
            {
                if (attribute is TestSizeAttribute sizeAttribute)
                {
                    sizes.Add(sizeAttribute.Size);
                }
                else if (attribute is CategoryAttribute category && TestSizes.TryParseCategory(category.Name, out TestSize parsed))
                {
                    sizes.Add(parsed);
                }
            }
        }

        private static void AddFromCategories(System.Collections.IEnumerable categories, HashSet<TestSize> sizes)
        {
            if (categories == null)
            {
                return;
            }

            foreach (object category in categories)
            {
                if (category is string name && TestSizes.TryParseCategory(name, out TestSize parsed))
                {
                    sizes.Add(parsed);
                }
            }
        }
    }
}
