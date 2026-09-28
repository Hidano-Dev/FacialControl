using Hidano.FacialControl.Adapters.Json.Dto;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Hidano.FacialControl.Testing;
using Object = UnityEngine.Object;

namespace Hidano.FacialControl.Tests.EditMode.Adapters.ScriptableObjectTests.Serializable
{
    [TestFixture]
    [SmallTest]
    public class OverlaySlotBindingSerializableTests : SizedTestFixture
    {
        [Test]
        public void TypeDefinition_DoesNotExposeLegacyExpressionIdField()
        {
            var field = typeof(OverlaySlotBindingSerializable).GetField("expressionId");

            Assert.That(field, Is.Null);
        }

        [Test]
        public void UnitySerialization_LegacyExpressionIdKey_IsIgnoredAndNewFieldsKeepDefaults()
        {
            var host = ScriptableObject.CreateInstance<SerializationHost>();
            try
            {
                EditorJsonUtility.FromJsonOverwrite(
                    "{\"MonoBehaviour\":{\"binding\":{\"slot\":\"blink\",\"expressionId\":\"legacy_overlay\"}}}",
                    host);

                Assert.That(host.binding, Is.Not.Null);
                Assert.That(host.binding.slot, Is.EqualTo("blink"));
                Assert.That(host.binding.suppress, Is.False);
                Assert.That(host.binding.animationClip, Is.Null);
                Assert.That(IsEffectivelyEmpty(host.binding.cachedSnapshot), Is.True,
                    "cachedSnapshot is auto-instantiated by Unity serialization but must be effectively empty when not explicitly assigned.");
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void GetState_DefaultFallback_ReturnsDefaultFallback()
        {
            var binding = new OverlaySlotBindingSerializable
            {
                slot = "blink",
            };

            Assert.That(binding.GetState(), Is.EqualTo(OverlaySlotBindingState.DefaultFallback));
        }

        [Test]
        public void GetState_SuppressIgnoresAnimationClip_ReturnsSuppress()
        {
            var clip = new AnimationClip { name = "OverlaySlotBindingSerializable_SuppressClip" };
            try
            {
                var binding = new OverlaySlotBindingSerializable
                {
                    slot = "blink",
                    suppress = true,
                    animationClip = clip,
                };

                Assert.That(binding.GetState(), Is.EqualTo(OverlaySlotBindingState.Suppress));
            }
            finally
            {
                Object.DestroyImmediate(clip);
            }
        }

        [Test]
        public void GetState_AnimationClipAssigned_ReturnsOverride()
        {
            var clip = new AnimationClip { name = "OverlaySlotBindingSerializable_OverrideClip" };
            try
            {
                var binding = new OverlaySlotBindingSerializable
                {
                    slot = "blink",
                    animationClip = clip,
                };

                Assert.That(binding.GetState(), Is.EqualTo(OverlaySlotBindingState.Override));
            }
            finally
            {
                Object.DestroyImmediate(clip);
            }
        }

        private static bool IsEffectivelyEmpty(OverlaySnapshotDto snapshot)
        {
            if (snapshot == null) return true;
            return (snapshot.blendShapes == null || snapshot.blendShapes.Count == 0)
                && (snapshot.bones == null || snapshot.bones.Count == 0)
                && (snapshot.rendererPaths == null || snapshot.rendererPaths.Count == 0);
        }

        private sealed class SerializationHost : ScriptableObject
        {
            public OverlaySlotBindingSerializable binding = new OverlaySlotBindingSerializable();
        }
    }
}
