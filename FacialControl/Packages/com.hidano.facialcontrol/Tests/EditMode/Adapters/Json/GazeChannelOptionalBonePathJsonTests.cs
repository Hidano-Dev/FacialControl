using System.Collections.Generic;
using Hidano.FacialControl.Adapters.Json;
using Hidano.FacialControl.Adapters.Json.Dto;
using Hidano.FacialControl.Adapters.ScriptableObject;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using NUnit.Framework;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.EditMode.Adapters.Json
{
    /// <summary>
    /// gaze.channels[] の目ボーン path が任意であること (省略・空文字が JSON 往復で保持されること) を検証する。
    /// </summary>
    [SmallTest]
    public sealed class GazeChannelOptionalBonePathJsonTests : SizedTestFixture
    {
        private SystemTextJsonParser _parser;

        [SetUp]
        public void SetUp()
        {
            _parser = new SystemTextJsonParser();
        }

        [Test]
        public void ToGazeChannels_BonePathsOmittedInJson_ReturnsEmptyPaths()
        {
            var json = @"{
                ""schemaVersion"": ""1.0"",
                ""layers"": [],
                ""expressions"": [],
                ""rendererPaths"": [],
                ""gaze"": { ""channels"": [
                    { ""id"": ""gaze"", ""lookUpAngle"": 12 }
                ] }
            }";

            var channels = FacialCharacterProfileConverter.ToGazeChannels(_parser.ParseProfileSnapshotV2(json));

            Assert.That(channels, Has.Count.EqualTo(1));
            Assert.That(channels[0].leftEyeBonePath, Is.Empty);
            Assert.That(channels[0].rightEyeBonePath, Is.Empty);
            Assert.That(channels[0].lookUpAngle, Is.EqualTo(12f));
        }

        [Test]
        public void SerializeParse_EmptyAndSpecifiedBonePaths_ArePreserved()
        {
            var source = new List<GazeChannel>
            {
                new GazeChannel { id = "gaze" },
                new GazeChannel { id = "camera", rightEyeBonePath = "Head/RightEye" },
            };
            var dto = new ProfileSnapshotDto
            {
                schemaVersion = SystemTextJsonParser.SchemaVersionV2,
                layers = new List<LayerDefinitionDto>(),
                expressions = new List<ExpressionDto>(),
                rendererPaths = new List<string>(),
                gaze = new GazeSectionDto { channels = FacialCharacterProfileConverter.ToGazeChannelDtos(source) },
            };

            string json = _parser.SerializeProfileSnapshot(dto);
            var channels = FacialCharacterProfileConverter.ToGazeChannels(_parser.ParseProfileSnapshotV2(json));

            Assert.That(channels, Has.Count.EqualTo(2));
            Assert.That(channels[0].id, Is.EqualTo("gaze"));
            Assert.That(channels[0].leftEyeBonePath, Is.Empty);
            Assert.That(channels[0].rightEyeBonePath, Is.Empty);
            Assert.That(channels[1].id, Is.EqualTo("camera"));
            Assert.That(channels[1].leftEyeBonePath, Is.Empty);
            Assert.That(channels[1].rightEyeBonePath, Is.EqualTo("Head/RightEye"));
        }
    }
}
