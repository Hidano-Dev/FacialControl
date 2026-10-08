using System.Collections.Generic;
using Hidano.FacialControl.Osc.Editor.AdapterBindings;
using NUnit.Framework;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Osc.Tests.EditMode.Adapters.AdapterBindings
{
    /// <summary>
    /// OSC Receiver の対象レイヤー Dropdown の選択肢と保存値の対応を守る
    /// （未指定・レイヤー名・プロファイルに無い現在値を取り違えない、現在値を勝手に消さない）。
    /// </summary>
    [TestFixture]
    [SmallTest]
    public sealed class OscReceiverTargetLayerChoicesTests : SizedTestFixture
    {
        private static readonly List<string> Layers = new List<string> { "emotion", "lipsync", "eye" };

        [Test]
        public void BuildTargetLayerChoices_Unspecified_SelectsUnspecifiedChoice()
        {
            List<string> choices = OscReceiverAdapterBindingDrawer.BuildTargetLayerChoices(Layers, "", out int selected);

            CollectionAssert.AreEqual(
                new[] { OscReceiverAdapterBindingDrawer.TargetLayerUnspecifiedChoice, "emotion", "lipsync", "eye" },
                choices);
            Assert.AreEqual(0, selected);
        }

        [Test]
        public void BuildTargetLayerChoices_ExistingLayer_SelectsThatLayer()
        {
            List<string> choices = OscReceiverAdapterBindingDrawer.BuildTargetLayerChoices(Layers, "eye", out int selected);

            Assert.AreEqual("eye", choices[selected]);
            Assert.AreEqual(4, choices.Count);
        }

        [Test]
        public void BuildTargetLayerChoices_MissingLayer_KeepsCurrentWithSuffix()
        {
            List<string> choices = OscReceiverAdapterBindingDrawer.BuildTargetLayerChoices(Layers, "face", out int selected);

            Assert.AreEqual("face" + OscReceiverAdapterBindingDrawer.TargetLayerMissingSuffix, choices[selected]);
            Assert.AreEqual(selected, choices.Count - 1);
        }

        [Test]
        public void ResolveTargetLayerValue_EachChoice_ReturnsStoredValue()
        {
            Assert.AreEqual(string.Empty, OscReceiverAdapterBindingDrawer.ResolveTargetLayerValue(Layers, "eye", 0));
            Assert.AreEqual("lipsync", OscReceiverAdapterBindingDrawer.ResolveTargetLayerValue(Layers, "eye", 2));
            Assert.AreEqual("face", OscReceiverAdapterBindingDrawer.ResolveTargetLayerValue(Layers, "face", 4));
        }

        [Test]
        public void ResolveTargetLayerValue_NullLayers_TreatsNonZeroAsCurrent()
        {
            Assert.AreEqual("face", OscReceiverAdapterBindingDrawer.ResolveTargetLayerValue(null, "face", 1));
            Assert.AreEqual(string.Empty, OscReceiverAdapterBindingDrawer.ResolveTargetLayerValue(null, null, 1));
        }
    }
}
