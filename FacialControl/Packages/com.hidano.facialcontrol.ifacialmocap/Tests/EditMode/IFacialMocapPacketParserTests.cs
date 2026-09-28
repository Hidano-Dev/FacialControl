using Hidano.FacialControl.Adapters.IFacialMocap;
using NUnit.Framework;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.IFacialMocap.Tests.EditMode
{
    [SmallTest]
    public class IFacialMocapPacketParserTests : SizedTestFixture
    {
        private static float GetBlendShape(IFacialMocapFrame frame, string name)
        {
            for (int i = 0; i < frame.BlendShapes.Count; i++)
            {
                if (frame.BlendShapes[i].Name == name)
                {
                    return frame.BlendShapes[i].Value;
                }
            }

            return float.NaN;
        }

        [Test]
        public void TryParse_Standard_ParsesBlendShapesHeadAndEyes()
        {
            const string packet =
                "browDown_L-0|jawOpen-50.5|mouthSmile_R-100|=head#1,2,3,0.1,0.2,0.3|rightEye#4,5,6|leftEye#7,8,9|";
            var frame = new IFacialMocapFrame();

            bool ok = IFacialMocapPacketParser.TryParse(packet, IFacialMocapDataVersion.Standard, frame);

            Assert.That(ok, Is.True);
            Assert.That(frame.BlendShapes.Count, Is.EqualTo(3));
            Assert.That(GetBlendShape(frame, "browDown_L"), Is.EqualTo(0f).Within(1e-4f));
            Assert.That(GetBlendShape(frame, "jawOpen"), Is.EqualTo(50.5f).Within(1e-4f));
            Assert.That(GetBlendShape(frame, "mouthSmile_R"), Is.EqualTo(100f).Within(1e-4f));

            Assert.That(frame.Head.HasValue, Is.True);
            Assert.That(frame.Head.EulerX, Is.EqualTo(1f).Within(1e-4f));
            Assert.That(frame.Head.EulerY, Is.EqualTo(2f).Within(1e-4f));
            Assert.That(frame.Head.EulerZ, Is.EqualTo(3f).Within(1e-4f));
            Assert.That(frame.Head.PositionX, Is.EqualTo(0.1f).Within(1e-4f));
            Assert.That(frame.Head.PositionY, Is.EqualTo(0.2f).Within(1e-4f));
            Assert.That(frame.Head.PositionZ, Is.EqualTo(0.3f).Within(1e-4f));

            Assert.That(frame.RightEye.HasValue, Is.True);
            Assert.That(frame.RightEye.EulerX, Is.EqualTo(4f).Within(1e-4f));
            Assert.That(frame.LeftEye.HasValue, Is.True);
            Assert.That(frame.LeftEye.EulerZ, Is.EqualTo(9f).Within(1e-4f));
        }

        [Test]
        public void TryParse_V2_UsesAmpersandSeparatorAndAllowsNegativeValues()
        {
            const string packet = "mouthSmile_R&-12.5|jawOpen&30|=head#0,0,0,0,0,0|";
            var frame = new IFacialMocapFrame();

            bool ok = IFacialMocapPacketParser.TryParse(packet, IFacialMocapDataVersion.V2, frame);

            Assert.That(ok, Is.True);
            Assert.That(GetBlendShape(frame, "mouthSmile_R"), Is.EqualTo(-12.5f).Within(1e-4f));
            Assert.That(GetBlendShape(frame, "jawOpen"), Is.EqualTo(30f).Within(1e-4f));
        }

        [Test]
        public void TryParse_TcpTerminator_IsStripped()
        {
            const string packet = "jawOpen-42|=head#0,0,0,0,0,0|___iFacialMocap";
            var frame = new IFacialMocapFrame();

            bool ok = IFacialMocapPacketParser.TryParse(packet, IFacialMocapDataVersion.Standard, frame);

            Assert.That(ok, Is.True);
            Assert.That(GetBlendShape(frame, "jawOpen"), Is.EqualTo(42f).Within(1e-4f));
        }

        [Test]
        public void TryParse_SkipsTokensWithoutSeparatorOrEmptyName()
        {
            const string packet = "faceObjGrp!Face|jawOpen-10|=";
            var frame = new IFacialMocapFrame();

            bool ok = IFacialMocapPacketParser.TryParse(packet, IFacialMocapDataVersion.Standard, frame);

            Assert.That(ok, Is.True);
            Assert.That(frame.BlendShapes.Count, Is.EqualTo(1));
            Assert.That(GetBlendShape(frame, "jawOpen"), Is.EqualTo(10f).Within(1e-4f));
        }

        [Test]
        public void TryParse_NullOrEmpty_ReturnsFalse()
        {
            var frame = new IFacialMocapFrame();
            Assert.That(IFacialMocapPacketParser.TryParse(null, IFacialMocapDataVersion.Standard, frame), Is.False);
            Assert.That(IFacialMocapPacketParser.TryParse(string.Empty, IFacialMocapDataVersion.Standard, frame), Is.False);
        }

        [Test]
        public void TryParse_EyeWithThreeComponents_LeavesPositionZero()
        {
            const string packet = "jawOpen-1|=rightEye#10,20,30|";
            var frame = new IFacialMocapFrame();

            IFacialMocapPacketParser.TryParse(packet, IFacialMocapDataVersion.Standard, frame);

            Assert.That(frame.RightEye.HasValue, Is.True);
            Assert.That(frame.RightEye.EulerY, Is.EqualTo(20f).Within(1e-4f));
            Assert.That(frame.RightEye.PositionX, Is.EqualTo(0f).Within(1e-4f));
        }

        [Test]
        public void TryParse_ClearsFrameBeforeParsing()
        {
            var frame = new IFacialMocapFrame();
            IFacialMocapPacketParser.TryParse("jawOpen-1|", IFacialMocapDataVersion.Standard, frame);
            IFacialMocapPacketParser.TryParse("mouthClose-2|", IFacialMocapDataVersion.Standard, frame);

            Assert.That(frame.BlendShapes.Count, Is.EqualTo(1));
            Assert.That(frame.BlendShapes[0].Name, Is.EqualTo("mouthClose"));
        }
    }
}
