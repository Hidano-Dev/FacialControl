using System;
using Hidano.FacialControl.Rec.Domain.Models;
using Hidano.FacialControl.Rec.Domain.Services;
using Hidano.FacialControl.Testing;
using NUnit.Framework;

namespace Hidano.FacialControl.Rec.Tests.EditMode
{
    [TestFixture]
    [SmallTest]
    public sealed class RecIdTableTests : SizedTestFixture
    {
        [Test]
        public void AddDefinedId_SameSourceIdAtDifferentIndex_ThrowsInvalidOperationException()
        {
            var table = new RecIdTable();
            table.AddDefinedId(0, RecEvent.IdDefinitionKind.Source, "osc");

            Assert.Throws<InvalidOperationException>(() =>
                table.AddDefinedId(1, RecEvent.IdDefinitionKind.Source, "osc"));
        }
    }
}
