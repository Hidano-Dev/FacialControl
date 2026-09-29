using NUnit.Framework;
using Hidano.FacialControl.Adapters.Input;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.EditMode.Adapters.Input
{
    /// <summary>
    /// <see cref="InputDeviceCategorizer"/> の path 解析と fallback フラグ伝搬を検証する EditMode テスト。
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item><c>&lt;Keyboard&gt;</c> 始まり → Keyboard、wasFallback=false</item>
    ///   <item><c>&lt;Gamepad&gt;</c> / <c>&lt;XRController&gt;</c> 等 → Controller、wasFallback=false</item>
    ///   <item>未認識 prefix / null / 空文字 → Controller、wasFallback=true</item>
    /// </list>
    /// </remarks>
    [TestFixture]
    [SmallTest]
    public class InputDeviceCategorizerTests : SizedTestFixture
    {
        [Test]
        public void Categorize_Keyboard_ReturnsKeyboard()
        {
            DeviceCategory category = InputDeviceCategorizer.Categorize("<Keyboard>/space", out bool wasFallback);

            Assert.That(category, Is.EqualTo(DeviceCategory.Keyboard));
            Assert.That(wasFallback, Is.False);
        }

        [Test]
        public void Categorize_Gamepad_ReturnsController()
        {
            DeviceCategory category = InputDeviceCategorizer.Categorize("<Gamepad>/buttonSouth", out bool wasFallback);

            Assert.That(category, Is.EqualTo(DeviceCategory.Controller));
            Assert.That(wasFallback, Is.False);
        }

        [Test]
        public void Categorize_XRController_ReturnsController()
        {
            DeviceCategory category = InputDeviceCategorizer.Categorize("<XRController>{LeftHand}/trigger", out bool wasFallback);

            Assert.That(category, Is.EqualTo(DeviceCategory.Controller));
            Assert.That(wasFallback, Is.False);
        }

        [Test]
        public void Categorize_UnknownPrefix_ReturnsControllerWithFallbackFlag()
        {
            DeviceCategory category = InputDeviceCategorizer.Categorize("<UnknownDevice>/someControl", out bool wasFallback);

            Assert.That(category, Is.EqualTo(DeviceCategory.Controller));
            Assert.That(wasFallback, Is.True);
        }

        [Test]
        public void Categorize_NullOrEmpty_ReturnsControllerWithFallbackFlag()
        {
            DeviceCategory categoryNull = InputDeviceCategorizer.Categorize(null, out bool wasFallbackNull);
            DeviceCategory categoryEmpty = InputDeviceCategorizer.Categorize(string.Empty, out bool wasFallbackEmpty);

            Assert.That(categoryNull, Is.EqualTo(DeviceCategory.Controller));
            Assert.That(wasFallbackNull, Is.True);
            Assert.That(categoryEmpty, Is.EqualTo(DeviceCategory.Controller));
            Assert.That(wasFallbackEmpty, Is.True);
        }
    }
}
