using System;
using NUnit.Framework;
using Unity.Collections;
using Hidano.FacialControl.Adapters.Playable;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.EditMode.Adapters
{
    /// <summary>
    /// P06-T01: NativeArrayPool の単体テスト。
    /// 確保・再利用・解放を検証する。
    /// </summary>
    [TestFixture]
    [SmallTest]
    public class NativeArrayPoolTests : SizedTestFixture
    {
        // ================================================================
        // Allocate — 確保
        // ================================================================

        [Test]
        public void Allocate_ValidSize_ReturnsNativeArrayWithCorrectLength()
        {
            using var pool = new NativeArrayPool<float>(16);

            var array = pool.Allocate();

            Assert.AreEqual(16, array.Length);
        }

        [Test]
        public void Allocate_ZeroSize_ReturnsEmptyNativeArray()
        {
            using var pool = new NativeArrayPool<float>(0);

            var array = pool.Allocate();

            Assert.AreEqual(0, array.Length);
        }

        [Test]
        public void Allocate_MultipleCalls_ReturnsDifferentInstances()
        {
            using var pool = new NativeArrayPool<float>(8);

            var array1 = pool.Allocate();
            var array2 = pool.Allocate();

            // 異なるバッファを返す（同じプールから別々に確保）
            // 片方の値を変更してもう片方に影響しないことで検証
            array1[0] = 1.0f;
            Assert.AreEqual(0f, array2[0]);
        }

        [Test]
        public void Allocate_ReturnsZeroInitializedArray()
        {
            using var pool = new NativeArrayPool<float>(4);

            var array = pool.Allocate();

            for (int i = 0; i < array.Length; i++)
            {
                Assert.AreEqual(0f, array[i]);
            }
        }

        // ================================================================
        // Return + Allocate — 再利用
        // ================================================================

        [Test]
        public void Allocate_AfterReturn_ReusesNativeArray()
        {
            using var pool = new NativeArrayPool<float>(8);

            var array1 = pool.Allocate();
            pool.Return(array1);
            var array2 = pool.Allocate();

            // 返却後の再確保では同じバッファを再利用する
            Assert.AreEqual(array1, array2);
        }

        [Test]
        public void Return_ClearsValues_BeforeReuse()
        {
            using var pool = new NativeArrayPool<float>(4);

            var array1 = pool.Allocate();
            array1[0] = 1.0f;
            array1[1] = 0.5f;
            pool.Return(array1);

            var array2 = pool.Allocate();

            // 再利用時に値がゼロクリアされている
            for (int i = 0; i < array2.Length; i++)
            {
                Assert.AreEqual(0f, array2[i]);
            }
        }

        [Test]
        public void Allocate_MultipleReturnAndAllocate_ReusesCorrectly()
        {
            using var pool = new NativeArrayPool<float>(4);

            // 複数回の確保→返却→再確保サイクル
            var array1 = pool.Allocate();
            var array2 = pool.Allocate();
            pool.Return(array1);
            pool.Return(array2);

            var array3 = pool.Allocate();
            var array4 = pool.Allocate();

            // 返却されたバッファが再利用される
            Assert.IsTrue(array3.IsCreated);
            Assert.IsTrue(array4.IsCreated);
            Assert.AreEqual(4, array3.Length);
            Assert.AreEqual(4, array4.Length);
        }

        // ================================================================
        // Resize — サイズ変更
        // ================================================================

        [Test]
        public void Resize_LargerSize_AllocatesNewSize()
        {
            using var pool = new NativeArrayPool<float>(4);

            pool.Resize(8);
            var array = pool.Allocate();

            Assert.AreEqual(8, array.Length);
        }

        [Test]
        public void Resize_SmallerSize_AllocatesNewSize()
        {
            using var pool = new NativeArrayPool<float>(8);

            pool.Resize(4);
            var array = pool.Allocate();

            Assert.AreEqual(4, array.Length);
        }

        [Test]
        public void Resize_ClearsExistingPool()
        {
            using var pool = new NativeArrayPool<float>(4);

            var array1 = pool.Allocate();
            pool.Return(array1);
            pool.Resize(8);

            // リサイズ後は旧サイズのバッファが再利用されない
            var array2 = pool.Allocate();
            Assert.AreEqual(8, array2.Length);
        }

        [Test]
        public void Resize_SameSize_DoesNothing()
        {
            using var pool = new NativeArrayPool<float>(4);

            var array1 = pool.Allocate();
            pool.Return(array1);
            pool.Resize(4);

            // 同じサイズへのリサイズはプールを維持
            var array2 = pool.Allocate();
            Assert.AreEqual(4, array2.Length);
            Assert.AreEqual(array1, array2);
        }

        // ================================================================
        // Dispose — 解放
        // ================================================================

        [Test]
        public void Dispose_DoesNotThrow()
        {
            var pool = new NativeArrayPool<float>(8);
            var array1 = pool.Allocate();
            pool.Return(array1);

            // Dispose が例外なく完了する
            Assert.DoesNotThrow(() => pool.Dispose());
        }

        [Test]
        public void Dispose_CalledTwice_DoesNotThrow()
        {
            var pool = new NativeArrayPool<float>(4);
            pool.Allocate();

            pool.Dispose();

            Assert.DoesNotThrow(() => pool.Dispose());
        }

        [Test]
        public void Dispose_WithOutstandingAllocations_DoesNotThrow()
        {
            var pool = new NativeArrayPool<float>(4);
            pool.Allocate();

            // 未返却のバッファがあっても Dispose が例外なく完了する
            Assert.DoesNotThrow(() => pool.Dispose());
        }

        // ================================================================
        // Size プロパティ
        // ================================================================

        [Test]
        public void Size_ReturnsConfiguredSize()
        {
            using var pool = new NativeArrayPool<float>(16);

            Assert.AreEqual(16, pool.Size);
        }

        [Test]
        public void Size_AfterResize_ReturnsNewSize()
        {
            using var pool = new NativeArrayPool<float>(4);

            pool.Resize(32);

            Assert.AreEqual(32, pool.Size);
        }

        // ================================================================
        // エラーケース
        // ================================================================

        [Test]
        public void Constructor_NegativeSize_ThrowsArgumentOutOfRangeException()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
            {
                var pool = new NativeArrayPool<float>(-1);
                pool.Dispose();
            });
        }

        [Test]
        public void Resize_NegativeSize_ThrowsArgumentOutOfRangeException()
        {
            using var pool = new NativeArrayPool<float>(4);

            Assert.Throws<ArgumentOutOfRangeException>(() => pool.Resize(-1));
        }
    }
}
