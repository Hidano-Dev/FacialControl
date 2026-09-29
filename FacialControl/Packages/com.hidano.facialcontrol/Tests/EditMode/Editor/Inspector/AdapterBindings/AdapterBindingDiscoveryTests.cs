using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Editor.Inspector.AdapterBindings;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.EditMode.Editor.Inspector.AdapterBindings
{
    // ---------------------------------------------------------------
    // Mock 型定義（namespace scope に置いて FQTN を安定化させる）。
    // 単独 displayName 1 種 + 同名 displayName ペア 1 組 の計 3 種。
    // displayName の "ZZZ_DiscoveryTest_*" prefix は他の concrete binding
    // （MockTriggerAdapterBinding 等）との sort 衝突を避けるため。
    // ---------------------------------------------------------------

    [Serializable]
    [FacialAdapterBinding(displayName: "ZZZ_DiscoveryTest_AAA_Single")]
    public sealed class MockDiscoverySingleBinding : AdapterBindingBase { }

    [Serializable]
    [FacialAdapterBinding(displayName: "ZZZ_DiscoveryTest_BBB_Duplicate")]
    public sealed class MockDiscoveryDuplicateAlphaBinding : AdapterBindingBase { }

    [Serializable]
    [FacialAdapterBinding(displayName: "ZZZ_DiscoveryTest_BBB_Duplicate")]
    public sealed class MockDiscoveryDuplicateBetaBinding : AdapterBindingBase { }

    /// <summary>
    /// task 5.1 の観測可能完了条件: <see cref="AdapterBindingDiscovery"/> が
    /// <c>[FacialAdapterBinding]</c> 付き具象型を <see cref="UnityEditor.TypeCache"/>
    /// 経由で列挙し、displayName 順 sort + 重複検出 + suffix 付与 + LogWarning を
    /// 行う挙動を assert する。
    /// </summary>
    /// <remarks>
    /// 本ファイルは Red 段階のテストであり、
    /// <c>Hidano.FacialControl.Editor.Inspector.AdapterBindings.AdapterBindingDiscovery</c>
    /// および <c>AdapterBindingDescriptor</c> がまだ未実装のため、
    /// コンパイル時に CS0246 / CS0234 が発生して Red 状態となる（task 5.2 の Green 化対象）。
    /// </remarks>
    [TestFixture]
    [SmallTest]
    public class AdapterBindingDiscoveryTests : SizedTestFixture
    {
        private const string SingleDisplayName = "ZZZ_DiscoveryTest_AAA_Single";
        private const string DuplicateDisplayName = "ZZZ_DiscoveryTest_BBB_Duplicate";

        private static readonly Type SingleType = typeof(MockDiscoverySingleBinding);
        private static readonly Type DuplicateAlphaType = typeof(MockDiscoveryDuplicateAlphaBinding);
        private static readonly Type DuplicateBetaType = typeof(MockDiscoveryDuplicateBetaBinding);

        // ---------------------------------------------------------------
        // GetDescriptors: 列挙と sort
        // ---------------------------------------------------------------

        [Test]
        public void GetDescriptors_IncludesAllAttributedMockTypes()
        {
            var descriptors = AdapterBindingDiscovery.GetDescriptors();

            Assert.IsNotNull(descriptors,
                "AdapterBindingDiscovery.GetDescriptors() は IReadOnlyList を返さなければならない。");

            var types = descriptors.Select(d => d.Type).ToList();
            CollectionAssert.Contains(types, SingleType,
                $"discovery 結果に {SingleType.FullName} が含まれていない。");
            CollectionAssert.Contains(types, DuplicateAlphaType,
                $"discovery 結果に {DuplicateAlphaType.FullName} が含まれていない。");
            CollectionAssert.Contains(types, DuplicateBetaType,
                $"discovery 結果に {DuplicateBetaType.FullName} が含まれていない。");
        }

        [Test]
        public void GetDescriptors_SortsItemsByDisplayNameAlphabetically()
        {
            var descriptors = AdapterBindingDiscovery.GetDescriptors();

            // 全ての descriptor が DisplayName で OrdinalIgnoreCase 昇順に sort されていること。
            var displayNames = descriptors.Select(d => d.DisplayName).ToList();
            var expected = displayNames
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();

            CollectionAssert.AreEqual(expected, displayNames,
                "GetDescriptors() の戻り値は DisplayName で alphabetical (OrdinalIgnoreCase) sort されているべき。");
        }

        [Test]
        public void GetDescriptors_SortsSingleBeforeDuplicatesByDisplayName()
        {
            // "ZZZ_DiscoveryTest_AAA_Single" < "ZZZ_DiscoveryTest_BBB_Duplicate" で
            // sort 順が安定することを確認する。
            var descriptors = AdapterBindingDiscovery.GetDescriptors();

            int singleIndex = -1;
            int firstDuplicateIndex = -1;
            for (int i = 0; i < descriptors.Count; i++)
            {
                if (descriptors[i].Type == SingleType)
                {
                    singleIndex = i;
                }
                else if ((descriptors[i].Type == DuplicateAlphaType
                          || descriptors[i].Type == DuplicateBetaType)
                         && firstDuplicateIndex < 0)
                {
                    firstDuplicateIndex = i;
                }
            }

            Assert.GreaterOrEqual(singleIndex, 0, "Single Mock 型が discovery 結果に存在しない。");
            Assert.GreaterOrEqual(firstDuplicateIndex, 0, "Duplicate Mock 型が discovery 結果に存在しない。");

            // "AAA" (Single の sort key) < "BBB" (Duplicate の sort key) なので
            // Single が必ず Duplicate ペアより前。
            Assert.Less(singleIndex, firstDuplicateIndex,
                $"DisplayName '{SingleDisplayName}' は '{DuplicateDisplayName}' より前に並ぶべき。");
        }

        // ---------------------------------------------------------------
        // 重複 displayName の suffix 付与
        // ---------------------------------------------------------------

        [Test]
        public void GetDescriptors_DuplicateDisplayName_AppendsFullTypeNameSuffixToEachEntry()
        {
            var descriptors = AdapterBindingDiscovery.GetDescriptors();

            var duplicateAlpha = descriptors.FirstOrDefault(d => d.Type == DuplicateAlphaType);
            var duplicateBeta = descriptors.FirstOrDefault(d => d.Type == DuplicateBetaType);

            Assert.AreEqual(DuplicateAlphaType, duplicateAlpha.Type,
                "DuplicateAlpha 型の descriptor が見つからない。");
            Assert.AreEqual(DuplicateBetaType, duplicateBeta.Type,
                "DuplicateBeta 型の descriptor が見つからない。");

            // OriginalDisplayName は attribute 上の生 displayName を保持。
            Assert.AreEqual(DuplicateDisplayName, duplicateAlpha.OriginalDisplayName);
            Assert.AreEqual(DuplicateDisplayName, duplicateBeta.OriginalDisplayName);

            // DisplayName は重複時に "{originalDisplayName} ({FullTypeName})" の形式に suffix 付与される。
            StringAssert.Contains(DuplicateDisplayName, duplicateAlpha.DisplayName,
                "重複 displayName の DisplayName は元の displayName を含むべき。");
            StringAssert.Contains(DuplicateAlphaType.FullName, duplicateAlpha.DisplayName,
                "重複 displayName の DisplayName は具象型の FullName を suffix として含むべき。");
            StringAssert.Contains("(", duplicateAlpha.DisplayName,
                "重複時の DisplayName suffix は '(' を含むべき (例: 'foo (FQTN)')。");
            StringAssert.Contains(")", duplicateAlpha.DisplayName);

            StringAssert.Contains(DuplicateDisplayName, duplicateBeta.DisplayName);
            StringAssert.Contains(DuplicateBetaType.FullName, duplicateBeta.DisplayName);

            // Alpha と Beta が同じ DisplayName にならないこと（disambiguation の主目的）。
            Assert.AreNotEqual(duplicateAlpha.DisplayName, duplicateBeta.DisplayName,
                "重複 displayName の disambiguation 結果として DisplayName は別文字列でなければならない。");
        }

        [Test]
        public void GetDescriptors_SingleDisplayName_DoesNotAppendFullTypeNameSuffix()
        {
            var descriptors = AdapterBindingDiscovery.GetDescriptors();

            var single = descriptors.FirstOrDefault(d => d.Type == SingleType);
            Assert.AreEqual(SingleType, single.Type, "Single Mock 型の descriptor が見つからない。");

            Assert.AreEqual(SingleDisplayName, single.OriginalDisplayName);
            Assert.AreEqual(SingleDisplayName, single.DisplayName,
                "重複していない displayName には suffix が付与されてはならない。");
        }

        // ---------------------------------------------------------------
        // LogWarning（重複 displayName の Debug.LogWarning + FQTN 列挙）
        // ---------------------------------------------------------------

        [Test]
        public void Refresh_DuplicateDisplayName_LogsWarningListingFullyQualifiedTypeNames()
        {
            // 同 displayName が複数あれば Debug.LogWarning で両方の FQTN を列挙する。
            // 静的初期化時の log は test 開始時には既に消費済みなので、
            // 明示的な Refresh() で再 scan + 再警告を起こして LogAssert で捕捉する。
            LogAssert.Expect(LogType.Warning, new Regex(Regex.Escape(DuplicateDisplayName)));
            LogAssert.Expect(LogType.Warning, new Regex(Regex.Escape(DuplicateAlphaType.FullName)));
            LogAssert.Expect(LogType.Warning, new Regex(Regex.Escape(DuplicateBetaType.FullName)));

            AdapterBindingDiscovery.Refresh();
        }

        // ---------------------------------------------------------------
        // FindByType
        // ---------------------------------------------------------------

        [Test]
        public void FindByType_KnownAttributedType_ReturnsDescriptor()
        {
            var descriptor = AdapterBindingDiscovery.FindByType(SingleType);

            Assert.IsTrue(descriptor.HasValue,
                "FindByType は登録済み型に対して null 以外の descriptor を返すべき。");
            Assert.AreEqual(SingleType, descriptor.Value.Type);
            Assert.AreEqual(SingleDisplayName, descriptor.Value.OriginalDisplayName);
        }

        [Test]
        public void FindByType_TypeWithoutAttribute_ReturnsNull()
        {
            // string 型は AdapterBindingBase 派生でも [FacialAdapterBinding] 付きでもない。
            var descriptor = AdapterBindingDiscovery.FindByType(typeof(string));

            Assert.IsFalse(descriptor.HasValue,
                "FindByType は未登録型に対して null を返すべき。");
        }

        [Test]
        public void FindByType_NullType_ReturnsNull()
        {
            var descriptor = AdapterBindingDiscovery.FindByType(null);

            Assert.IsFalse(descriptor.HasValue,
                "FindByType は null 引数に対して null を返すべき (null reference を投げない)。");
        }

        // ---------------------------------------------------------------
        // OnDescriptorsRebuilt event
        // ---------------------------------------------------------------

        [Test]
        public void OnDescriptorsRebuilt_FiresOnRefresh()
        {
            // 重複 displayName 由来の LogWarning は Refresh() で再発する。
            // 本テストの主目的は event 発火検証なので、LogAssert で warning を消費しておく。
            LogAssert.Expect(LogType.Warning, new Regex(Regex.Escape(DuplicateDisplayName)));

            int callCount = 0;
            Action handler = () => callCount++;

            AdapterBindingDiscovery.OnDescriptorsRebuilt += handler;
            try
            {
                AdapterBindingDiscovery.Refresh();
            }
            finally
            {
                AdapterBindingDiscovery.OnDescriptorsRebuilt -= handler;
            }

            Assert.AreEqual(1, callCount,
                "Refresh() 後に OnDescriptorsRebuilt が一度発火するべき。");
        }
    }
}
