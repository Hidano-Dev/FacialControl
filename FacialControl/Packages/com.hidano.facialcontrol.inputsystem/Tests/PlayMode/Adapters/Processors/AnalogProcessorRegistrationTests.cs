using System;
using NUnit.Framework;
using Hidano.FacialControl.Adapters.Processors;

namespace Hidano.FacialControl.InputSystem.Tests.PlayMode.Adapters.Processors
{
    /// <summary>
    /// <see cref="AnalogProcessorRegistration"/> が 6 種の
    /// <see cref="UnityEngine.InputSystem.InputProcessor{TValue}"/> を Editor / Runtime 双方の初期化フェーズで
    /// <see cref="UnityEngine.InputSystem.InputSystem.RegisterProcessor{T}(string)"/> 経由で登録していることを検証する。
    /// </summary>
    /// <remarks>
    /// PlayMode 開始時に <see cref="UnityEngine.RuntimeInitializeOnLoadMethodAttribute"/> によって
    /// <c>AnalogProcessorRegistration.Register()</c> が走るため、テスト到達時点で全 6 processor が
    /// 公開 API <see cref="UnityEngine.InputSystem.InputSystem.TryGetProcessor(string)"/> で名前解決できる必要がある。
    /// </remarks>
    [TestFixture]
    public class AnalogProcessorRegistrationTests
    {
        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            // PlayMode 入時の RuntimeInitializeOnLoadMethod が確実に実行されているはずだが、
            // 念のため type を参照することで static constructor の発火も保証する。
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(
                typeof(AnalogProcessorRegistration).TypeHandle);
        }

        [Test]
        public void ProcessorNames_HasSixDistinctEntries()
        {
            Assert.AreEqual(6, AnalogProcessorRegistration.ProcessorNames.Length,
                "AnalogProcessorRegistration.ProcessorNames must enumerate exactly 6 processors.");
            CollectionAssert.AllItemsAreUnique(AnalogProcessorRegistration.ProcessorNames);
        }

        [Test]
        public void Register_DeadZoneProcessor_IsResolvableByName()
        {
            Assert.AreEqual(
                typeof(AnalogDeadZoneProcessor),
                LookupRegisteredProcessor(AnalogProcessorRegistration.DeadZoneProcessorName));
        }

        [Test]
        public void Register_ScaleProcessor_IsResolvableByName()
        {
            Assert.AreEqual(
                typeof(AnalogScaleProcessor),
                LookupRegisteredProcessor(AnalogProcessorRegistration.ScaleProcessorName));
        }

        [Test]
        public void Register_OffsetProcessor_IsResolvableByName()
        {
            Assert.AreEqual(
                typeof(AnalogOffsetProcessor),
                LookupRegisteredProcessor(AnalogProcessorRegistration.OffsetProcessorName));
        }

        [Test]
        public void Register_ClampProcessor_IsResolvableByName()
        {
            Assert.AreEqual(
                typeof(AnalogClampProcessor),
                LookupRegisteredProcessor(AnalogProcessorRegistration.ClampProcessorName));
        }

        [Test]
        public void Register_CurveProcessor_IsResolvableByName()
        {
            Assert.AreEqual(
                typeof(AnalogCurveProcessor),
                LookupRegisteredProcessor(AnalogProcessorRegistration.CurveProcessorName));
        }

        [Test]
        public void Register_InvertProcessor_IsResolvableByName()
        {
            Assert.AreEqual(
                typeof(AnalogInvertProcessor),
                LookupRegisteredProcessor(AnalogProcessorRegistration.InvertProcessorName));
        }

        // ============================================================
        // ヘルパー
        // ============================================================

        /// <summary>
        /// InputSystem の公開 API <see cref="UnityEngine.InputSystem.InputSystem.TryGetProcessor(string)"/> で
        /// 指定名に登録された CLR <see cref="Type"/> を返す（未登録なら null）。
        /// </summary>
        private static Type LookupRegisteredProcessor(string name)
        {
            return UnityEngine.InputSystem.InputSystem.TryGetProcessor(name);
        }
    }
}
