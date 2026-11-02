using System;
using System.Collections.Generic;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Testing;
using Hidano.FacialControl.Timeline.Adapters;
using Hidano.FacialControl.Timeline.Adapters.AdapterBindings;
using NUnit.Framework;
using UnityEditor;

namespace Hidano.FacialControl.Timeline.Tests.EditMode
{
    /// <summary>
    /// 廃止した <see cref="TimelineAdapterBinding"/>（HID-408）が、既存 Profile のデシリアライズ用の型としてだけ残り、
    /// 追加メニューに出ず、再生に関与しないことを検証する。
    /// </summary>
    /// <remarks>TypeCache（Editor API）を使うため Medium。</remarks>
    [MediumTest]
    public sealed class TimelineAdapterBindingTests : SizedTestFixture
    {
        [Test]
        public void Type_IsSerializableWithoutFacialAdapterBindingAttribute()
        {
            Assert.That(
                typeof(TimelineAdapterBinding).GetCustomAttributes(typeof(SerializableAttribute), inherit: false),
                Has.Length.EqualTo(1),
                "既存 Profile の SerializeReference を読めるよう型は残す");
            Assert.That(
                typeof(TimelineAdapterBinding).GetCustomAttributes(typeof(FacialAdapterBindingAttribute), inherit: false),
                Is.Empty,
                "Adapter Bindings の追加メニューに出さない");
        }

        [Test]
        public void TypeCache_DoesNotDiscoverTimelineAdapterBinding()
        {
            var discovered = new List<Type>(TypeCache.GetTypesWithAttribute<FacialAdapterBindingAttribute>());

            CollectionAssert.DoesNotContain(discovered, typeof(TimelineAdapterBinding));
        }

        [Test]
        public void Type_KeepsDynamicInputsMarker()
        {
            Assert.That(typeof(IAdapterBindingDynamicInputs).IsAssignableFrom(typeof(TimelineAdapterBinding)), Is.True,
                "旧 Profile の timeline:* 宣言を Routing の検証で不正扱いにしない");
        }

        [Test]
        public void NewInstance_KeepsDefaultSlugAndNoLegacyFields()
        {
            var binding = new TimelineAdapterBinding();

            Assert.That(binding.Slug, Is.EqualTo("timeline"));
            Assert.That(binding.HasLegacyFields, Is.False);
        }

        [Test]
        public void OnStartAndDispose_DoNotThrowOrCreateReceiver()
        {
            var binding = new TimelineAdapterBinding();

            Assert.DoesNotThrow(() => binding.OnStart(default));
            Assert.DoesNotThrow(() => binding.Dispose());
            Assert.That(UnityEngine.Object.FindObjectsByType<FacialTimelineReceiver>(UnityEngine.FindObjectsSortMode.None), Is.Empty,
                "Receiver を作らない（Timeline の再生は Facial Character トラックの binding から文脈を受け取る）");
        }
    }
}
