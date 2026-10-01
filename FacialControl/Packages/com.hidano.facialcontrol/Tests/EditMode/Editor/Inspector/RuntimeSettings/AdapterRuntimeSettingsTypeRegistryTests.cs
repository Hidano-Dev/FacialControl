using System;
using System.Linq;
using Hidano.FacialControl.Adapters.RuntimeSettings;
using Hidano.FacialControl.Editor.Inspector.RuntimeSettings;
using NUnit.Framework;
using UnityEngine;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.EditMode.Editor.Inspector.RuntimeSettings
{
    /// <summary>
    /// task 6.3 の観測可能完了条件: <see cref="AdapterRuntimeSettingsTypeRegistry"/> が
    /// <see cref="AdapterRuntimeSettingsBase"/> 派生の具象型 (<see cref="OscReceiverRuntimeSettingsSO"/>
    /// を含む) を列挙し、abstract Base 自身と移行用の旧型は含まないことを検証する。
    /// </summary>
    [TestFixture]
    [SmallTest]
    public class AdapterRuntimeSettingsTypeRegistryTests : SizedTestFixture
    {
        public sealed class FakeRegistryConcreteSettings : AdapterRuntimeSettingsBase
        {
        }

        public abstract class FakeRegistryAbstractSettings : AdapterRuntimeSettingsBase
        {
        }

        [HideInAdapterRuntimeSettingsMenu]
        public sealed class FakeRegistryHiddenSettings : AdapterRuntimeSettingsBase
        {
        }

        [CreateAssetMenu(menuName = "FacialControlTests/RegistryConcreteWithMenuName")]
        public sealed class FakeRegistryConcreteWithMenuNameSettings : AdapterRuntimeSettingsBase
        {
        }

#if FACIALCONTROL_HAS_OSC_MODULE
        [Test]
        public void GetConcreteTypes_IncludesSplitOscSettingsTypes()
        {
            var types = AdapterRuntimeSettingsTypeRegistry.GetConcreteTypes();

            Assert.IsNotNull(types,
                "GetConcreteTypes() は IReadOnlyList を返さなければならない。");
            CollectionAssert.Contains(types, typeof(OscReceiverRuntimeSettingsSO),
                "registry の列挙結果に OscReceiverRuntimeSettingsSO が含まれていない。");
            CollectionAssert.Contains(types, typeof(OscSenderRuntimeSettingsSO),
                "registry の列挙結果に OscSenderRuntimeSettingsSO が含まれていない。");
        }

        [Test]
        public void GetConcreteTypes_LegacyOscRuntimeSettingsSO_IsExcluded()
        {
            var types = AdapterRuntimeSettingsTypeRegistry.GetConcreteTypes();

            CollectionAssert.DoesNotContain(types, typeof(OscRuntimeSettingsSO),
                "移行用に残した旧 OscRuntimeSettingsSO は Add 一覧に出してはならない。");
        }
#endif

        [Test]
        public void GetConcreteTypes_HiddenAttributeType_IsExcluded()
        {
            var types = AdapterRuntimeSettingsTypeRegistry.GetConcreteTypes();

            CollectionAssert.DoesNotContain(types, typeof(FakeRegistryHiddenSettings),
                "HideInAdapterRuntimeSettingsMenu を付けた型は列挙結果に含まれてはならない。");
        }

        [Test]
        public void GetConcreteTypes_DoesNotIncludeAbstractBase()
        {
            var types = AdapterRuntimeSettingsTypeRegistry.GetConcreteTypes();

            CollectionAssert.DoesNotContain(types, typeof(AdapterRuntimeSettingsBase),
                "abstract Base 自身は具象型列挙の結果に含まれてはならない。");
            CollectionAssert.DoesNotContain(types, typeof(FakeRegistryAbstractSettings),
                "派生 abstract 型も具象型列挙の結果に含まれてはならない。");
        }

        [Test]
        public void GetConcreteTypes_IncludesNestedFakeConcreteType()
        {
            var types = AdapterRuntimeSettingsTypeRegistry.GetConcreteTypes();

            CollectionAssert.Contains(types, typeof(FakeRegistryConcreteSettings),
                "派生 concrete 型は TypeCache 経由で列挙されるべき。");
        }

        [Test]
        public void GetConcreteTypes_SortsItemsByDisplayNameAlphabetically()
        {
            var types = AdapterRuntimeSettingsTypeRegistry.GetConcreteTypes();

            var displayNames = types.Select(AdapterRuntimeSettingsTypeRegistry.GetDisplayName).ToList();
            var expected = displayNames
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();

            CollectionAssert.AreEqual(expected, displayNames,
                "GetConcreteTypes() の戻り値は displayName で OrdinalIgnoreCase 昇順 sort されているべき。");
        }

        [Test]
        public void GetDisplayName_TypeWithCreateAssetMenu_ReturnsMenuName()
        {
            string displayName = AdapterRuntimeSettingsTypeRegistry
                .GetDisplayName(typeof(FakeRegistryConcreteWithMenuNameSettings));

            Assert.AreEqual(
                "FacialControlTests/RegistryConcreteWithMenuName",
                displayName,
                "[CreateAssetMenu] menuName が指定されている型はその文字列を displayName とすべき。");
        }

        [Test]
        public void GetDisplayName_TypeWithoutCreateAssetMenu_ReturnsTypeName()
        {
            string displayName = AdapterRuntimeSettingsTypeRegistry
                .GetDisplayName(typeof(FakeRegistryConcreteSettings));

            Assert.AreEqual(
                nameof(FakeRegistryConcreteSettings),
                displayName,
                "[CreateAssetMenu] 未指定型は型名 (Type.Name) を displayName とすべき。");
        }

        [Test]
        public void GetDisplayName_NullType_ReturnsEmptyString()
        {
            string displayName = AdapterRuntimeSettingsTypeRegistry.GetDisplayName(null);

            Assert.AreEqual(string.Empty, displayName,
                "null 引数に対しては NullReference を投げず空文字を返すべき。");
        }
    }
}
