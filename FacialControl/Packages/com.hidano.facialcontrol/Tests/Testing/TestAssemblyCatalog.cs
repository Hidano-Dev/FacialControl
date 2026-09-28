using System;
using System.Collections.Generic;
using System.Reflection;

namespace Hidano.FacialControl.Testing
{
    /// <summary>
    /// 現在の AppDomain にロードされた FacialControl のテストアセンブリを列挙する。
    /// サイズ宣言漏れの検査（<c>TestSizeDeclarationTests</c>）が対象アセンブリを決めるために使う。
    /// </summary>
    public static class TestAssemblyCatalog
    {
        private const string AssemblyPrefix = "Hidano.FacialControl";
        private const string TestsMarker = ".Tests";

        /// <summary>
        /// 名前が <c>Hidano.FacialControl</c> で始まり <c>.Tests</c> を含むアセンブリ（EditMode / PlayMode / Small / Shared）を返す。
        /// このアセンブリ自身（<c>Hidano.FacialControl.Testing</c>）はテストを含まないため対象外。
        /// </summary>
        public static List<Assembly> FindProjectTestAssemblies()
        {
            var result = new List<Assembly>();
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.IsDynamic)
                {
                    continue;
                }

                if (IsProjectTestAssemblyName(assembly.GetName().Name))
                {
                    result.Add(assembly);
                }
            }

            result.Sort((a, b) => string.CompareOrdinal(a.GetName().Name, b.GetName().Name));
            return result;
        }

        /// <summary>アセンブリ名がプロジェクトのテストアセンブリの命名（<c>Hidano.FacialControl*.Tests*</c>）に合うか。</summary>
        public static bool IsProjectTestAssemblyName(string assemblyName)
        {
            return assemblyName != null
                && assemblyName.StartsWith(AssemblyPrefix, StringComparison.Ordinal)
                && assemblyName.IndexOf(TestsMarker, StringComparison.Ordinal) >= 0;
        }

        /// <summary>
        /// 型のロードに失敗する要素を除いて型を列挙する（<see cref="ReflectionTypeLoadException"/> 対策）。
        /// </summary>
        public static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
        {
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types;
            }

            foreach (Type type in types)
            {
                if (type != null)
                {
                    yield return type;
                }
            }
        }
    }
}
