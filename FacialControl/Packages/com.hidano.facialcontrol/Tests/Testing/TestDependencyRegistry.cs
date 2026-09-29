using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Hidano.FacialControl.Testing
{
    /// <summary>
    /// テストに注入する外部依存（時刻・保存・通信など）の登録簿。
    /// サイズが Small のとき、<see cref="IFakeDependency"/> を実装しない依存の登録を <see cref="Assert.Fail(string)"/> で拒否する。
    /// </summary>
    public sealed class TestDependencyRegistry
    {
        private readonly List<Entry> _entries = new List<Entry>();

        public TestDependencyRegistry(TestSize size)
        {
            Size = size;
        }

        /// <summary>この登録簿が属するテストのサイズ。</summary>
        public TestSize Size { get; }

        /// <summary>登録済みの依存。</summary>
        public IReadOnlyList<Entry> Entries => _entries;

        /// <summary>
        /// 依存を登録し、そのまま返す（フィールド初期化と同時に登録できるようにするため）。
        /// Small で Fake 以外を渡すとテストを失敗させる。
        /// </summary>
        /// <param name="dependency">注入する依存。</param>
        /// <param name="role">エラーメッセージ用の役割名（例: "ITimeProvider"）。省略時は型名。</param>
        public T Register<T>(T dependency, string role = null) where T : class
        {
            if (dependency == null)
            {
                throw new ArgumentNullException(nameof(dependency));
            }

            string roleName = string.IsNullOrEmpty(role) ? typeof(T).Name : role;
            if (Size == TestSize.Small && !IsFake(dependency))
            {
                Assert.Fail(
                    $"[Small] 依存 '{roleName}' に Fake 以外の実装 {dependency.GetType().FullName} が注入されました。" +
                    $" Small テストでは {nameof(IFakeDependency)} を実装したインメモリ Fake のみ注入できます。" +
                    " 本番実装（Unity 時刻・PlayerPrefs・ファイル・UDP 等）が必要ならテストを Medium に変更してください。");
            }

            _entries.Add(new Entry(roleName, dependency));
            return dependency;
        }

        /// <summary>依存がインメモリ Fake（<see cref="IFakeDependency"/>）かどうか。</summary>
        public static bool IsFake(object dependency)
        {
            return dependency is IFakeDependency;
        }

        /// <summary>登録済み依存 1 件。</summary>
        public readonly struct Entry
        {
            public Entry(string role, object dependency)
            {
                Role = role;
                Dependency = dependency;
            }

            public string Role { get; }
            public object Dependency { get; }
        }
    }
}
