using System.Collections.Generic;
using Hidano.FacialControl.Adapters.AdapterBindings;
using Hidano.FacialControl.Adapters.AdapterBindings.ARKit;
using Hidano.FacialControl.Adapters.OSC;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Osc.Editor.AdapterBindings;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Osc.Tests.EditMode.Adapters.AdapterBindings
{
    /// <summary>
    /// OSC 系 drawer の <c>GetHeaderSummary</c> が、実際の SerializedProperty（フィールド名）から
    /// 受信ポート・送信先を読み取れることを検証する（フィールド名の変更で要約が黙って壊れないことを守る）。
    /// </summary>
    [TestFixture]
    [MediumTest]
    public sealed class OscAdapterBindingDrawerHeaderSummaryTests : SizedTestFixture
    {
        private OscHeaderSummaryTestHolder _holder;

        [TearDown]
        public void TearDown()
        {
            if (_holder != null)
            {
                Object.DestroyImmediate(_holder);
                _holder = null;
            }
        }

        private SerializedProperty CreateProperty(AdapterBindingBase binding)
        {
            _holder = ScriptableObject.CreateInstance<OscHeaderSummaryTestHolder>();
            _holder.Binding = binding;
            var serializedObject = new SerializedObject(_holder);
            SerializedProperty property = serializedObject.FindProperty(nameof(OscHeaderSummaryTestHolder.Binding));
            Assert.IsNotNull(property);
            return property;
        }

        [Test]
        public void ReceiverDrawer_GetHeaderSummary_ReadsPortField()
        {
            var property = CreateProperty(new OscReceiverAdapterBinding { Port = 9123 });

            var summary = new OscReceiverAdapterBindingDrawer().GetHeaderSummary(property);

            Assert.AreEqual(":9123", summary.Text);
        }

        [Test]
        public void SenderDrawer_GetHeaderSummary_ReadsEndpointsField()
        {
            var binding = new OscSenderAdapterBinding
            {
                Endpoints = new List<OscSenderEndpointConfig>
                {
                    new OscSenderEndpointConfig("192.168.0.20", 9010),
                    new OscSenderEndpointConfig("192.168.0.21", 9011),
                    new OscSenderEndpointConfig("192.168.0.22", 9012, enabled: false),
                },
            };
            var property = CreateProperty(binding);

            var summary = new OscSenderAdapterBindingDrawer().GetHeaderSummary(property);

            Assert.AreEqual("192.168.0.20:9010 他 1 件", summary.Text);
            StringAssert.Contains("192.168.0.22:9012" + OscAdapterBindingHeaderSummaryFormatter.InvalidMark, summary.Tooltip);
        }

        [Test]
        public void ArKitDrawer_GetHeaderSummary_ReadsPortField()
        {
            var property = CreateProperty(new ArKitOscAdapterBinding { Port = 9200 });

            var summary = new ArKitOscAdapterBindingDrawer().GetHeaderSummary(property);

            Assert.AreEqual(":9200", summary.Text);
        }
    }
}
