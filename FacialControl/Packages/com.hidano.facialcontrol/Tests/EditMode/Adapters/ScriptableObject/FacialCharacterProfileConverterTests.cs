using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Testing;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Tests.EditMode.Adapters.ScriptableObjectTests
{
    [TestFixture]
    [SmallTest]
    public class FacialCharacterProfileConverterTests : SizedTestFixture
    {
        [Test]
        public void ToFacialProfile_DuplicateLayerNames_KeepsFirstAndWarns()
        {
            LogAssert.Expect(LogType.Warning, new Regex("重複|duplicate", RegexOptions.IgnoreCase));

            var profile = FacialCharacterProfileConverter.ToFacialProfile(
                "1.0",
                new[]
                {
                    CreateLayer("emotion", 0),
                    CreateLayer("emotion", 1),
                    CreateLayer("eye", 2),
                },
                Array.Empty<ExpressionSerializable>(),
                new List<string>());

            Assert.That(profile.Layers.Length, Is.EqualTo(2));
            Assert.That(profile.Layers.Span[0].Name, Is.EqualTo("emotion"));
            Assert.That(profile.Layers.Span[1].Name, Is.EqualTo("eye"));
        }

        private static LayerDefinitionSerializable CreateLayer(string name, int priority)
        {
            return new LayerDefinitionSerializable
            {
                name = name,
                priority = priority,
                inputSources = new List<InputSourceDeclarationSerializable>
                {
                    new InputSourceDeclarationSerializable { id = "input", weight = 1f },
                },
            };
        }
    }
}
