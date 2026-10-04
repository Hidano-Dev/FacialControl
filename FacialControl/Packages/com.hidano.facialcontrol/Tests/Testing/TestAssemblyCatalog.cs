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
        private const string TestingAssemblyName = "Hidano.FacialControl.Testing";

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

        /// <summary>
        /// 繧ｭ繝ｼ繝ｫ繝峨お繝励Μ繧ｱ繝ｼ繧ｷ繝ｧ繝ｳ縺ｮ Runtime / Editor product 繧｢繧ｻ繝ｳ繝悶Μ繧貞・謖吶☆繧九・
        /// </summary>
        public static List<Assembly> FindProjectProductAssemblies()
        {
            var result = new List<Assembly>();
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.IsDynamic)
                {
                    continue;
                }

                if (IsProjectProductAssemblyName(assembly.GetName().Name))
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

        /// <summary>繝ｭ繝ｼ繝峨Ο繝ｼ繝峨＆繧後◆ product 繧｢繧ｻ繝ｳ繝悶Μ蜷阪°繧呈､懈ｴ九☆繧九・/summary>
        public static bool IsProjectProductAssemblyName(string assemblyName)
        {
            return assemblyName != null
                && assemblyName.StartsWith(AssemblyPrefix, StringComparison.Ordinal)
                && !IsProjectTestAssemblyName(assemblyName)
                && !string.Equals(assemblyName, TestingAssemblyName, StringComparison.Ordinal);
        }

        /// <summary>蜷阪↓荳閧ｦ縺吶ｋ AppDomain 縺ｮ繝ｭ繝ｼ繝峨�繧｢繧ｻ繝ｳ繝悶Μ繧貞・謖吶☆繧九・/summary>
        public static bool TryFindLoadedAssembly(string assemblyName, out Assembly assembly)
        {
            assembly = null;
            if (assemblyName == null)
            {
                return false;
            }

            foreach (Assembly loadedAssembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!loadedAssembly.IsDynamic
                    && string.Equals(loadedAssembly.GetName().Name, assemblyName, StringComparison.Ordinal))
                {
                    assembly = loadedAssembly;
                    return true;
                }
            }

            return false;
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
