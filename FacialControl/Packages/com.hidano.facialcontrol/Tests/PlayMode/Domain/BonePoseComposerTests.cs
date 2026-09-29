using NUnit.Framework;
using UnityEngine;
using Hidano.FacialControl.Domain.Services;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.PlayMode.Domain
{
    /// <summary>
    /// BonePoseComposer の Unity 突合テスト（Red、PlayMode）。
    ///
    /// PlayMode 配置の理由: Unity の <see cref="UnityEngine.Quaternion.Euler(float, float, float)"/> と
    /// 数値突合するため Engine 参照が必要。Domain は Unity 非依存だが、テストでは Adapters 側相当として
    /// Unity Quaternion を真値に取り、自前実装の (qx, qy, qz, qw) と突合する。
    ///
    /// 検証項目:
    ///   - X/Y/Z 各軸 -180°〜180° の grid（10° 刻み）で <c>EulerToQuaternion(x, y, z)</c> の出力が
    ///     <c>UnityEngine.Quaternion.Euler(x, y, z)</c> と誤差 ≤ 1e-5 で一致すること。
    ///   - <c>Compose(basisQ, eulerXYZ)</c> が <c>basisQ * Quaternion.Euler(eulerXYZ)</c> と
    ///     数値等価であること（Hamilton 積）。
    ///   - 体が傾いた basis（非自明な basis quaternion）でも結果が正しく合成されること
    ///     （body tilt が gaze に漏れないことの構造的保証）。
    /// </summary>
    [TestFixture]
    [MediumTest]
    public class BonePoseComposerTests : SizedTestFixture
    {
        private const float Tolerance = 1e-5f;

        // ================================================================
        // EulerToQuaternion: Unity Quaternion.Euler との突合
        // ================================================================

        [Test]
        public void EulerToQuaternion_Identity_MatchesUnityQuaternionIdentity()
        {
            BonePoseComposer.EulerToQuaternion(
                0f, 0f, 0f,
                out float qx, out float qy, out float qz, out float qw);

            var expected = Quaternion.Euler(0f, 0f, 0f);
            AssertQuaternionApproximatelyEqual(expected, qx, qy, qz, qw);
        }

        [Test]
        public void EulerToQuaternion_PureXAxis_MatchesUnity()
        {
            // X 軸単独
            for (float x = -180f; x <= 180f; x += 10f)
            {
                BonePoseComposer.EulerToQuaternion(
                    x, 0f, 0f,
                    out float qx, out float qy, out float qz, out float qw);

                var expected = Quaternion.Euler(x, 0f, 0f);
                AssertQuaternionApproximatelyEqual(
                    expected, qx, qy, qz, qw,
                    $"X={x}");
            }
        }

        [Test]
        public void EulerToQuaternion_PureYAxis_MatchesUnity()
        {
            // Y 軸単独
            for (float y = -180f; y <= 180f; y += 10f)
            {
                BonePoseComposer.EulerToQuaternion(
                    0f, y, 0f,
                    out float qx, out float qy, out float qz, out float qw);

                var expected = Quaternion.Euler(0f, y, 0f);
                AssertQuaternionApproximatelyEqual(
                    expected, qx, qy, qz, qw,
                    $"Y={y}");
            }
        }

        [Test]
        public void EulerToQuaternion_PureZAxis_MatchesUnity()
        {
            // Z 軸単独
            for (float z = -180f; z <= 180f; z += 10f)
            {
                BonePoseComposer.EulerToQuaternion(
                    0f, 0f, z,
                    out float qx, out float qy, out float qz, out float qw);

                var expected = Quaternion.Euler(0f, 0f, z);
                AssertQuaternionApproximatelyEqual(
                    expected, qx, qy, qz, qw,
                    $"Z={z}");
            }
        }

        [Test]
        public void EulerToQuaternion_FullGrid10Degrees_MatchesUnity()
        {
            // X/Y/Z 各軸 -180°〜180° の 10° 刻み grid（37×37×37 ≒ 50653 サンプル）
            for (float x = -180f; x <= 180f; x += 10f)
            {
                for (float y = -180f; y <= 180f; y += 10f)
                {
                    for (float z = -180f; z <= 180f; z += 10f)
                    {
                        BonePoseComposer.EulerToQuaternion(
                            x, y, z,
                            out float qx, out float qy, out float qz, out float qw);

                        var expected = Quaternion.Euler(x, y, z);
                        AssertQuaternionApproximatelyEqual(
                            expected, qx, qy, qz, qw,
                            $"Euler=({x}, {y}, {z})");
                    }
                }
            }
        }

        // ================================================================
        // Compose: basisQ * Quaternion.Euler(eulerXYZ) と数値等価
        // ================================================================

        [Test]
        public void Compose_IdentityBasis_EqualsEulerToQuaternion()
        {
            // basis が identity の場合、Compose の結果は EulerToQuaternion(eulerXYZ) と一致すること。
            var samples = new (float x, float y, float z)[]
            {
                (0f, 0f, 0f),
                (15f, 0f, 0f),
                (0f, 30f, 0f),
                (0f, 0f, 45f),
                (10f, 20f, 30f),
                (-90f, 45f, 60f),
                (180f, -180f, 180f),
            };

            foreach (var (ex, ey, ez) in samples)
            {
                BonePoseComposer.Compose(
                    0f, 0f, 0f, 1f,
                    ex, ey, ez,
                    out float ox, out float oy, out float oz, out float ow);

                var expected = Quaternion.identity * Quaternion.Euler(ex, ey, ez);
                AssertQuaternionApproximatelyEqual(
                    expected, ox, oy, oz, ow,
                    $"identity-basis Euler=({ex}, {ey}, {ez})");
            }
        }

        [Test]
        public void Compose_TiltedBodyBasis_MatchesHamiltonProduct()
        {
            // basis が体の傾き相当（例: ZXY 順で構成された任意の rotation）でも、
            // 結果は basisQ * Quaternion.Euler(eulerXYZ) と数値等価でなければならない。
            // これは body tilt が gaze offset へ正しく合成されることを保証する。
            var basisAngles = new (float x, float y, float z)[]
            {
                (10f, 0f, 0f),       // 軽い前傾
                (0f, 20f, 0f),       // 横向き
                (0f, 0f, 15f),       // ロール
                (15f, 25f, -10f),    // 複合姿勢
                (-30f, 60f, 45f),    // 大きな傾き
            };

            var offsetAngles = new (float x, float y, float z)[]
            {
                (5f, 10f, 0f),
                (0f, -15f, 0f),
                (12f, 0f, 0f),
                (-8f, 22f, 4f),
            };

            foreach (var (bx, by, bz) in basisAngles)
            {
                var basisQ = Quaternion.Euler(bx, by, bz);

                foreach (var (ex, ey, ez) in offsetAngles)
                {
                    BonePoseComposer.Compose(
                        basisQ.x, basisQ.y, basisQ.z, basisQ.w,
                        ex, ey, ez,
                        out float ox, out float oy, out float oz, out float ow);

                    var expected = basisQ * Quaternion.Euler(ex, ey, ez);
                    AssertQuaternionApproximatelyEqual(
                        expected, ox, oy, oz, ow,
                        $"basis=({bx}, {by}, {bz}) offset=({ex}, {ey}, {ez})");
                }
            }
        }

        [Test]
        public void Compose_ZeroOffsetWithTiltedBasis_PreservesBasis()
        {
            // offset=(0, 0, 0) のとき、Compose の出力は basis をそのまま返す（identity 合成）。
            // body tilt が gaze に漏れないことの最小ケース。
            var basisQ = Quaternion.Euler(20f, -35f, 10f);

            BonePoseComposer.Compose(
                basisQ.x, basisQ.y, basisQ.z, basisQ.w,
                0f, 0f, 0f,
                out float ox, out float oy, out float oz, out float ow);

            AssertQuaternionApproximatelyEqual(basisQ, ox, oy, oz, ow);
        }

        [Test]
        public void Compose_NonCommutative_OrderIsBasisFirst()
        {
            // Quaternion 積は非可換。Compose は basisQ * offsetQ の順であり、
            // offsetQ * basisQ とは一般に異なる。
            // この性質が壊れる（順序が逆転する）と body tilt が gaze に漏れる。
            var basisQ = Quaternion.Euler(0f, 60f, 0f);
            var offsetEuler = (x: 30f, y: 0f, z: 0f);

            BonePoseComposer.Compose(
                basisQ.x, basisQ.y, basisQ.z, basisQ.w,
                offsetEuler.x, offsetEuler.y, offsetEuler.z,
                out float ox, out float oy, out float oz, out float ow);

            var basisFirst = basisQ * Quaternion.Euler(offsetEuler.x, offsetEuler.y, offsetEuler.z);
            var offsetFirst = Quaternion.Euler(offsetEuler.x, offsetEuler.y, offsetEuler.z) * basisQ;

            AssertQuaternionApproximatelyEqual(basisFirst, ox, oy, oz, ow);

            // basisFirst と offsetFirst が一致しないことを確認（順序の意味を保証する）。
            float diff =
                Mathf.Abs(basisFirst.x - offsetFirst.x) +
                Mathf.Abs(basisFirst.y - offsetFirst.y) +
                Mathf.Abs(basisFirst.z - offsetFirst.z) +
                Mathf.Abs(basisFirst.w - offsetFirst.w);
            Assert.Greater(diff, Tolerance, "basis*offset と offset*basis が同一になっている。順序が壊れている可能性。");
        }

        // ================================================================
        // ヘルパー
        // ================================================================

        private static void AssertQuaternionApproximatelyEqual(
            Quaternion expected,
            float actualX, float actualY, float actualZ, float actualW,
            string context = null)
        {
            // quaternion q と -q は同じ回転を表す（double cover）。
            // 比較時は両符号を許容する。
            float dot =
                expected.x * actualX +
                expected.y * actualY +
                expected.z * actualZ +
                expected.w * actualW;

            float sign = dot >= 0f ? 1f : -1f;
            float dx = expected.x - sign * actualX;
            float dy = expected.y - sign * actualY;
            float dz = expected.z - sign * actualZ;
            float dw = expected.w - sign * actualW;

            float maxDiff = Mathf.Max(
                Mathf.Abs(dx),
                Mathf.Max(Mathf.Abs(dy), Mathf.Max(Mathf.Abs(dz), Mathf.Abs(dw))));

            if (maxDiff > Tolerance)
            {
                Assert.Fail(
                    $"Quaternion mismatch (max-component diff={maxDiff:G6}, tol={Tolerance:G6}). " +
                    $"Expected=({expected.x:G7}, {expected.y:G7}, {expected.z:G7}, {expected.w:G7}) " +
                    $"Actual=({actualX:G7}, {actualY:G7}, {actualZ:G7}, {actualW:G7}) " +
                    (context != null ? $"Context: {context}" : string.Empty));
            }
        }
    }
}
