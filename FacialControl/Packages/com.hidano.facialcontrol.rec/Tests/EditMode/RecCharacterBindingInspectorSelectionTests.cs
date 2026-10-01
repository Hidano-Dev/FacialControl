using Hidano.FacialControl.Rec.Editor.Inspector;
using NUnit.Framework;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Rec.Tests.EditMode
{
    /// <summary>
    /// Load 対象ドロップダウンの選択解決（<see cref="RecCharacterBindingInspector.ResolveRecordingSelection"/>）。
    /// </summary>
    [TestFixture]
    [SmallTest]
    public class RecCharacterBindingInspectorSelectionTests : SizedTestFixture
    {
        private static readonly string[] Names = { "take-new", "take01", "take-old" };

        [Test]
        public void ResolveRecordingSelection_PreferredNameListed_ReturnsPreferredName()
        {
            string selected = RecCharacterBindingInspector.ResolveRecordingSelection(Names, "take01", "take-old");

            Assert.That(selected, Is.EqualTo("take01"));
        }

        [Test]
        public void ResolveRecordingSelection_PreferredNameMissing_KeepsCurrentValue()
        {
            string selected = RecCharacterBindingInspector.ResolveRecordingSelection(Names, "deleted", "take-old");

            Assert.That(selected, Is.EqualTo("take-old"));
        }

        [Test]
        public void ResolveRecordingSelection_NeitherListed_ReturnsNewestRecording()
        {
            string selected = RecCharacterBindingInspector.ResolveRecordingSelection(Names, null, "deleted");

            Assert.That(selected, Is.EqualTo("take-new"));
        }

        [Test]
        public void ResolveRecordingSelection_NoRecordings_ReturnsEmpty()
        {
            string selected = RecCharacterBindingInspector.ResolveRecordingSelection(new string[0], "take01", "take01");

            Assert.That(selected, Is.Empty);
        }
    }
}
