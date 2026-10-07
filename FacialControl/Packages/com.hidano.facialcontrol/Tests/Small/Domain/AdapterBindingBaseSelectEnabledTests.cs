using System;
using System.Collections.Generic;
using Hidano.FacialControl.Domain.Adapters;
using NUnit.Framework;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.EditMode.Domain
{
    /// <summary>
    /// <see cref="AdapterBindingBase.SelectEnabled"/> が無効（<see cref="AdapterBindingBase.Disabled"/>）の binding だけを
    /// 起動対象から外すことを守る。既定値が「有効」であること（既存アセットのマイグレーション不要）もここで守る。
    /// </summary>
    [TestFixture]
    [SmallTest]
    public class AdapterBindingBaseSelectEnabledTests : SizedTestFixture
    {
        [Test]
        public void Disabled_NewInstance_IsFalse()
        {
            var binding = new StubBinding();

            Assert.IsFalse(binding.Disabled, "既定は有効（Disabled = false）でなければならない。");
        }

        [Test]
        public void SelectEnabled_Null_ReturnsEmpty()
        {
            IReadOnlyList<AdapterBindingBase> result = AdapterBindingBase.SelectEnabled(null);

            Assert.IsNotNull(result);
            Assert.AreEqual(0, result.Count);
        }

        [Test]
        public void SelectEnabled_AllEnabled_ReturnsSameListInstance()
        {
            var bindings = new List<AdapterBindingBase> { new StubBinding { Slug = "a" }, new StubBinding { Slug = "b" } };

            IReadOnlyList<AdapterBindingBase> result = AdapterBindingBase.SelectEnabled(bindings);

            Assert.AreSame(bindings, result, "無効が無ければ確保せずにそのまま返す。");
        }

        [Test]
        public void SelectEnabled_DisabledBinding_IsExcludedAndOrderIsKept()
        {
            var a = new StubBinding { Slug = "a" };
            var b = new StubBinding { Slug = "b", Disabled = true };
            var c = new StubBinding { Slug = "c" };

            IReadOnlyList<AdapterBindingBase> result =
                AdapterBindingBase.SelectEnabled(new List<AdapterBindingBase> { a, b, c });

            CollectionAssert.AreEqual(new AdapterBindingBase[] { a, c }, result);
        }

        [Test]
        public void SelectEnabled_NullElementWithDisabledBinding_KeepsNullElement()
        {
            var a = new StubBinding { Slug = "a", Disabled = true };

            IReadOnlyList<AdapterBindingBase> result =
                AdapterBindingBase.SelectEnabled(new List<AdapterBindingBase> { null, a });

            CollectionAssert.AreEqual(new AdapterBindingBase[] { null }, result,
                "型欠落の null 要素は呼び出し側で警告するため残す。");
        }

        [Serializable]
        private sealed class StubBinding : AdapterBindingBase { }
    }
}
