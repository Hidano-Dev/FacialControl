using System;
using System.Linq;
using System.Collections.Generic;
using Hidano.FacialControl.Adapters.AdapterBindings.InputSystem;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.InputSystem.Adapters.ScriptableObject;
using NUnit.Framework;
using UnityEditor;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.InputSystem.Tests.EditMode.Adapters.AdapterBindings
{
    /// <summary>
    /// task 10.1 EditMode 観測可能完了条件:
    /// <see cref="InputSystemAdapterBinding"/> が <c>[Serializable]</c> +
    /// <c>[FacialAdapterBinding(displayName: "Input System")]</c> 付きであり、
    /// <c>UnityEditor.TypeCache.GetTypesWithAttribute&lt;FacialAdapterBindingAttribute&gt;()</c>
    /// で discovery 列挙されることを assert する。
    /// </summary>
    /// <remarks>
    /// 本ファイルは Red 段階のテストであり、
    /// <c>Hidano.FacialControl.Adapters.AdapterBindings.InputSystem.InputSystemAdapterBinding</c>
    /// が未実装のためコンパイル時に CS0246 / CS0234 が発生して Red 状態となる
    /// （task 10.2 の Green 化対象）。
    /// </remarks>
    [TestFixture]
    [MediumTest]
    public class InputSystemAdapterBindingTests : SizedTestFixture
    {
        private const string ExpectedDisplayName = "Input System";

        [Test]
        public void Type_HasSerializableAttribute_ForSerializeReferenceRoundTrip()
        {
            object[] attrs = typeof(InputSystemAdapterBinding)
                .GetCustomAttributes(typeof(SerializableAttribute), inherit: false);

            Assert.That(attrs.Length, Is.EqualTo(1),
                "InputSystemAdapterBinding に [Serializable] が付いていないと [SerializeReference] の round-trip が破綻する。");
        }

        [Test]
        public void Type_HasFacialAdapterBindingAttributeWithDisplayNameInputSystem()
        {
            object[] attrs = typeof(InputSystemAdapterBinding)
                .GetCustomAttributes(typeof(FacialAdapterBindingAttribute), inherit: false);

            Assert.That(attrs.Length, Is.EqualTo(1),
                "InputSystemAdapterBinding には [FacialAdapterBinding] が 1 件だけ付与されているべき。");

            var attr = (FacialAdapterBindingAttribute)attrs[0];
            Assert.That(attr.DisplayName, Is.EqualTo(ExpectedDisplayName),
                $"[FacialAdapterBinding] の displayName は \"{ExpectedDisplayName}\" であるべき。");
        }

        [Test]
        public void Type_DerivesFromAdapterBindingBase()
        {
            Assert.That(typeof(AdapterBindingBase).IsAssignableFrom(typeof(InputSystemAdapterBinding)), Is.True,
                "InputSystemAdapterBinding は AdapterBindingBase の派生でなければならない。");
        }

        [Test]
        public void Type_IsConcreteSealedClass()
        {
            Type type = typeof(InputSystemAdapterBinding);

            Assert.That(type.IsAbstract, Is.False,
                "InputSystemAdapterBinding は具象（非 abstract）クラスでなければならない。");
            Assert.That(type.IsSealed, Is.True,
                "InputSystemAdapterBinding は sealed でなければならない（拡張は別 binding で実現）。");
        }

        [Test]
        public void Type_HasParameterlessConstructor_ForActivatorCreateInstance()
        {
            // Inspector の Add ドロップダウンが Activator.CreateInstance 等で具象を生成できる必要がある。
            System.Reflection.ConstructorInfo ctor = typeof(InputSystemAdapterBinding)
                .GetConstructor(Type.EmptyTypes);

            Assert.That(ctor, Is.Not.Null,
                "Activator.CreateInstance で生成可能な parameterless constructor が必要。");
        }

        [Test]
        public void GetDeclaredInputSourceIds_TriggerAnalogOverlay_ReturnsSlugScopedIds()
        {
            var binding = new InputSystemAdapterBinding { Slug = "input-system" };
            binding.Configure(
                asset: null,
                actionMapName: "Expression",
                expressionBindings: new[]
                {
                    new ExpressionBindingEntry { bindingMode = BindingMode.Normal, actionName = "Smile", expressionId = "smile" },
                    new ExpressionBindingEntry { bindingMode = BindingMode.Analog, actionName = "Mouth", expressionId = "aa" },
                    new ExpressionBindingEntry { bindingMode = BindingMode.Overlay, actionName = "Blink", overlaySlot = "blink" },
                });

            string[] ids = ((IAdapterBindingDeclaredInputs)binding).GetDeclaredInputSourceIds().ToArray();

            CollectionAssert.AreEqual(
                new[] { "input-system", "input-system:analog-expression", "input-system:overlay:blink" },
                ids);
        }

        [Test]
        public void GetDeclaredInputSourceIds_NoSlug_ReturnsEmpty()
        {
            var binding = new InputSystemAdapterBinding();

            string[] ids = ((IAdapterBindingDeclaredInputs)binding).GetDeclaredInputSourceIds().ToArray();

            Assert.That(ids, Is.Empty);
        }

        [Test]
        public void GetDeclaredInputSourceIds_Gaze_UsesConventionChannelIdsWithoutActionAlias()
        {
            var binding = new InputSystemAdapterBinding { Slug = "input-system" };
            binding.Configure(null, "Expression", new[]
            {
                new ExpressionBindingEntry
                {
                    bindingMode = BindingMode.Gaze,
                    expressionId = "eye-look",
                    useDistinctLeftRight = true,
                    actionNameLeft = "LeftLook",
                    actionNameRight = "RightLook",
                },
            });

            string[] ids = binding.GetDeclaredInputSourceIds().ToArray();
            CollectionAssert.Contains(ids, "input-system:eye-look.left");
            CollectionAssert.Contains(ids, "input-system:eye-look.right");
            CollectionAssert.DoesNotContain(ids, "input-system:LeftLook");
            CollectionAssert.DoesNotContain(ids, "input-system:RightLook");

            var declarations = ((IGazeSourceProvider)binding).GetGazeSourceDeclarations().ToArray();
            Assert.That(declarations, Has.Length.EqualTo(1));
            Assert.That(declarations[0].ChannelId, Is.EqualTo("eye-look"));
            Assert.That(declarations[0].ProvidesLeftRightPair, Is.True);
        }

        [Test]
        public void TypeCache_DiscoversInputSystemAdapterBindingViaFacialAdapterBindingAttribute()
        {
            // 各アダプタ package の binding 具象は TypeCache で discovery 列挙される。
            System.Collections.Generic.List<Type> discovered = TypeCache
                .GetTypesWithAttribute<FacialAdapterBindingAttribute>()
                .ToList();

            CollectionAssert.Contains(discovered, typeof(InputSystemAdapterBinding),
                "TypeCache discovery で InputSystemAdapterBinding が列挙されるべき。");
        }

        [Test]
        public void TypeCache_DiscoveredEntry_DisplayNameMatchesInputSystem()
        {
            FacialAdapterBindingAttribute attr = TypeCache
                .GetTypesWithAttribute<FacialAdapterBindingAttribute>()
                .Where(t => t == typeof(InputSystemAdapterBinding))
                .Select(t => (FacialAdapterBindingAttribute)t
                    .GetCustomAttributes(typeof(FacialAdapterBindingAttribute), inherit: false)[0])
                .FirstOrDefault();

            Assert.That(attr, Is.Not.Null);
            Assert.That(attr.DisplayName, Is.EqualTo(ExpectedDisplayName));
        }
    }
}
