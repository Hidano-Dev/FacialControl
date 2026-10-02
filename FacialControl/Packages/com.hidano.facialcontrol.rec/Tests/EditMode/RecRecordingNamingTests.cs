using System;
using Hidano.FacialControl.Rec.Adapters.Recording;
using NUnit.Framework;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Rec.Tests.EditMode
{
    [TestFixture]
    [SmallTest]
    public class RecRecordingNamingTests : SizedTestFixture
    {
        private static readonly DateTime FixedNow = new DateTime(2026, 9, 27, 16, 55, 36);

        [Test]
        public void Resolve_RequestedNameGiven_ReturnsTrimmedRequestedName()
        {
            string resolved = RecRecordingNaming.Resolve("  take01  ", "fallback", FixedNow);

            Assert.That(resolved, Is.EqualTo("take01"));
        }

        [Test]
        public void Resolve_RequestedNameEmpty_FallsBackToDefaultName()
        {
            string resolved = RecRecordingNaming.Resolve("   ", " session ", FixedNow);

            Assert.That(resolved, Is.EqualTo("session"));
        }

        [Test]
        public void Resolve_BothNamesEmpty_ReturnsTimestampName()
        {
            string resolved = RecRecordingNaming.Resolve(null, "", FixedNow);

            Assert.That(resolved, Is.EqualTo("take-20260927-165536"));
        }

        [Test]
        public void BuildTimestampName_FixedDateTime_ReturnsTakePrefixedYyyyMmddHhmmss()
        {
            string name = RecRecordingNaming.BuildTimestampName(new DateTime(2026, 1, 2, 3, 4, 5));

            Assert.That(name, Is.EqualTo("take-20260102-030405"));
        }
    }
}
