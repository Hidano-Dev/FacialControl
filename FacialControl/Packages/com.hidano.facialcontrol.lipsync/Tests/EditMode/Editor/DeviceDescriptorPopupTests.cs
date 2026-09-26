using System.Reflection;
using Hidano.FacialControl.LipSync.Adapters.Devices;
using Hidano.FacialControl.LipSync.Editor.Inspector;
using Hidano.FacialControl.LipSync.Tests.Shared;
using NUnit.Framework;

namespace Hidano.FacialControl.LipSync.Tests.EditMode.Editor
{
    /// <summary>
    /// <see cref="DeviceDescriptorPopup"/> の smoke テスト。
    /// 「列挙器を与えて生成でき、初期値が <see cref="DeviceDescriptorPopup.CurrentDescriptor"/> に反映される」
    /// 「手入力の確定が変更コールバックへ届く」だけを守る。UI ツリーの細部は検証しない。
    /// </summary>
    public class DeviceDescriptorPopupTests
    {
        private DeviceDescriptor _capturedDescriptor;
        private int _changedCallCount;

        [SetUp]
        public void SetUp()
        {
            _capturedDescriptor = default;
            _changedCallCount = 0;
        }

        [Test]
        public void Create_WithInitialValueAndEnumerators_ReflectsInitialDescriptorWithoutThrowing()
        {
            var initial = new DeviceDescriptor
            {
                DeviceName = "USB Mic",
                DisambiguatorIndex = 4,
            };

            DeviceDescriptorPopup popup = null;
            Assert.DoesNotThrow(() => popup = CreatePopup(
                initial,
                new FakeAsioDriverEnumerator("ASIO Main", "ASIO Backup"),
                new FakeMicrophoneDeviceEnumerator("Built-in Mic", "USB Mic")));

            Assert.That(popup.CurrentDescriptor.DeviceName, Is.EqualTo("USB Mic"));
            Assert.That(popup.CurrentDescriptor.DisambiguatorIndex, Is.EqualTo(4));
            Assert.That(_changedCallCount, Is.EqualTo(0), "生成時に変更コールバックを発火してはならない。");
        }

        [Test]
        public void ManualOverride_Changed_UpdatesDeviceName()
        {
            // panel 未接続では TextField の value 代入で ChangeEvent が発火しないため、
            // 変更ハンドラを直接呼び出して公開コールバックへの到達を確認する。
            var popup = CreatePopup(
                default,
                new FakeAsioDriverEnumerator("ASIO Main"),
                new FakeMicrophoneDeviceEnumerator("USB Mic"));

            InvokePrivate(popup, "ApplyDeviceNameFromManualOverride", "Disconnected Mic");

            Assert.That(_capturedDescriptor.DeviceName, Is.EqualTo("Disconnected Mic"));
            Assert.That(popup.CurrentDescriptor.DeviceName, Is.EqualTo("Disconnected Mic"));
            Assert.That(_changedCallCount, Is.EqualTo(1));
        }

        private DeviceDescriptorPopup CreatePopup(
            DeviceDescriptor initialValue,
            IAsioDriverEnumerator asioEnumerator,
            IMicrophoneDeviceEnumerator microphoneEnumerator)
        {
            return new DeviceDescriptorPopup(
                initialValue,
                descriptor =>
                {
                    _capturedDescriptor = descriptor;
                    _changedCallCount++;
                },
                asioEnumerator,
                microphoneEnumerator);
        }

        private static void InvokePrivate(
            DeviceDescriptorPopup popup,
            string methodName,
            params object[] args)
        {
            MethodInfo method = typeof(DeviceDescriptorPopup).GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            method.Invoke(popup, args);
        }
    }
}
