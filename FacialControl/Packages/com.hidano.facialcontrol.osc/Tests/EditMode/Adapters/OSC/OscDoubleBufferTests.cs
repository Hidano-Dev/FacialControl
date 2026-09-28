using System;
using System.Threading;
using NUnit.Framework;
using Hidano.FacialControl.Adapters.OSC;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.EditMode.Adapters
{
    [TestFixture]
    [SmallTest]
    public class OscDoubleBufferTests : SizedTestFixture
    {
        [Test]
        public void Constructor_ValidSize_CreatesBuffer()
        {
            using var buffer = new OscDoubleBuffer(16);

            Assert.AreEqual(16, buffer.Size);
            Assert.AreEqual(16, buffer.GetReadBuffer().Length);
        }

        [Test]
        public void Constructor_ZeroSize_CreatesEmptyBuffer()
        {
            using var buffer = new OscDoubleBuffer(0);

            Assert.AreEqual(0, buffer.Size);
            Assert.AreEqual(0, buffer.GetReadBuffer().Length);
        }

        [Test]
        public void Constructor_NegativeSize_ThrowsArgumentOutOfRangeException()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
            {
                var buffer = new OscDoubleBuffer(-1);
                buffer.Dispose();
            });
        }

        [Test]
        public void Write_ValidIndexAndValue_StoresValueAfterSwap()
        {
            using var buffer = new OscDoubleBuffer(4);

            buffer.Write(0, 0.5f);
            buffer.Swap();

            Assert.AreEqual(0.5f, buffer.GetReadBuffer()[0], 0.0001f);
        }

        [Test]
        public void Write_MultipleValues_StoresAllAfterSwap()
        {
            using var buffer = new OscDoubleBuffer(4);

            buffer.Write(0, 0.1f);
            buffer.Write(1, 0.2f);
            buffer.Write(2, 0.3f);
            buffer.Write(3, 0.4f);
            buffer.Swap();

            var readBuffer = buffer.GetReadBuffer();
            Assert.AreEqual(0.1f, readBuffer[0], 0.0001f);
            Assert.AreEqual(0.2f, readBuffer[1], 0.0001f);
            Assert.AreEqual(0.3f, readBuffer[2], 0.0001f);
            Assert.AreEqual(0.4f, readBuffer[3], 0.0001f);
        }

        [Test]
        public void Write_OverwritesSameIndex_KeepsLatestValue()
        {
            using var buffer = new OscDoubleBuffer(4);

            buffer.Write(0, 0.1f);
            buffer.Write(0, 0.9f);
            buffer.Swap();

            Assert.AreEqual(0.9f, buffer.GetReadBuffer()[0], 0.0001f);
        }

        [Test]
        public void Write_IndexOutOfRange_DropsWrite()
        {
            using var buffer = new OscDoubleBuffer(4);
            int before = buffer.WriteTick;

            Assert.DoesNotThrow(() => buffer.Write(4, 0.5f));
            Assert.DoesNotThrow(() => buffer.Write(-1, 0.5f));
            buffer.Swap();

            Assert.AreEqual(before, buffer.WriteTick);
            Assert.AreEqual(0f, buffer.GetReadBuffer()[0]);
        }

        [Test]
        public void Swap_WrittenValuesNotVisibleBeforeSwap()
        {
            using var buffer = new OscDoubleBuffer(4);

            buffer.Write(0, 0.5f);

            Assert.AreEqual(0f, buffer.GetReadBuffer()[0]);
        }

        [Test]
        public void Swap_WithoutNewWrites_PreservesPreviousValues()
        {
            // 受信が 1 tick 途切れても直前フレームの値を保持する（copy-forward）。
            // 旧実装は Swap で write buffer をゼロクリアしていたため、受信の無い tick を
            // 挟むと全 BlendShape が 0 に落ち、表情が一瞬素に戻る不具合があった。
            using var buffer = new OscDoubleBuffer(4);

            buffer.Write(0, 0.5f);
            buffer.Swap();
            buffer.Swap();

            Assert.AreEqual(0.5f, buffer.GetReadBuffer()[0], 0.0001f);
        }

        [Test]
        public void Swap_PartialWrite_PreservesUnwrittenIndicesFromPreviousFrame()
        {
            // 部分 frame（bundle のパケット分断・ロスト等）を Swap しても、
            // その frame に含まれない index は前回値を保持する。
            using var buffer = new OscDoubleBuffer(4);

            buffer.Write(0, 0.5f);
            buffer.Write(1, 0.8f);
            buffer.Swap();

            buffer.Write(1, 0.9f);
            buffer.Swap();

            var readBuffer = buffer.GetReadBuffer();
            Assert.AreEqual(0.5f, readBuffer[0], 0.0001f, "未書込 index は前回値を保持する");
            Assert.AreEqual(0.9f, readBuffer[1], 0.0001f, "書込済 index は最新値になる");
        }

        [Test]
        public void Swap_ManyAlternatingPartialWrites_NeverDropsToZero()
        {
            // 交互に別 index だけを書き続けても、どちらの index も 0 に落ちないこと。
            using var buffer = new OscDoubleBuffer(2);

            buffer.Write(0, 0.5f);
            buffer.Write(1, 0.8f);
            buffer.Swap();

            for (int i = 0; i < 10; i++)
            {
                buffer.Write(i % 2, 0.5f + (i % 2) * 0.3f);
                buffer.Swap();

                var readBuffer = buffer.GetReadBuffer();
                Assert.AreEqual(0.5f, readBuffer[0], 0.0001f, $"iteration {i}: index 0 が 0 に落ちた");
                Assert.AreEqual(0.8f, readBuffer[1], 0.0001f, $"iteration {i}: index 1 が 0 に落ちた");
            }
        }

        [Test]
        public void Swap_ConcurrentWrites_NoCrashAndNoValueLoss()
        {
            // Swap（メインスレッド）と Write（受信スレッド）の並行実行で、
            // クラッシュせず、書込済みの値が 0 に巻き戻らないこと。
            using var buffer = new OscDoubleBuffer(4);
            Exception exception = null;
            const int iterations = 10000;

            buffer.Write(0, 1f);
            buffer.Swap();

            var writeThread = new Thread(() =>
            {
                try
                {
                    for (int i = 0; i < iterations; i++)
                    {
                        buffer.Write(0, 1f);
                    }
                }
                catch (Exception ex)
                {
                    exception = ex;
                }
            });

            writeThread.Start();
            for (int i = 0; i < iterations; i++)
            {
                buffer.Swap();
                float value = buffer.GetReadBuffer()[0];
                if (value != 1f)
                {
                    writeThread.Join();
                    Assert.Fail($"iteration {i}: 値が {value} に巻き戻った (期待 1.0)");
                }
            }

            writeThread.Join();
            Assert.IsNull(exception, $"Concurrent Swap/Write threw: {exception}");
        }

        [Test]
        public void Write_FromDifferentThread_DoesNotCorruptReadBuffer()
        {
            using var buffer = new OscDoubleBuffer(4);

            var writeThread = new Thread(() =>
            {
                buffer.Write(0, 0.5f);
                buffer.Write(1, 0.75f);
            });
            writeThread.Start();
            writeThread.Join();

            Assert.AreEqual(0f, buffer.GetReadBuffer()[0]);

            buffer.Swap();
            var readBuffer = buffer.GetReadBuffer();
            Assert.AreEqual(0.5f, readBuffer[0], 0.0001f);
            Assert.AreEqual(0.75f, readBuffer[1], 0.0001f);
        }

        [Test]
        public void Resize_LargerSize_AllocatesNewSize()
        {
            using var buffer = new OscDoubleBuffer(4);

            buffer.Resize(8);

            Assert.AreEqual(8, buffer.Size);
            Assert.AreEqual(8, buffer.GetReadBuffer().Length);
        }

        [Test]
        public void Resize_SmallerSize_AllocatesNewSize()
        {
            using var buffer = new OscDoubleBuffer(8);

            buffer.Resize(4);

            Assert.AreEqual(4, buffer.Size);
            Assert.AreEqual(4, buffer.GetReadBuffer().Length);
        }

        [Test]
        public void Resize_SameSize_DoesNothing()
        {
            using var buffer = new OscDoubleBuffer(4);

            buffer.Write(0, 0.5f);
            buffer.Swap();
            buffer.Resize(4);

            Assert.AreEqual(0.5f, buffer.GetReadBuffer()[0], 0.0001f);
        }

        [Test]
        public void Resize_PreservesOverlappingData()
        {
            using var buffer = new OscDoubleBuffer(4);

            buffer.Write(0, 0.5f);
            buffer.Swap();
            buffer.Resize(8);

            var readBuffer = buffer.GetReadBuffer();
            Assert.AreEqual(0.5f, readBuffer[0], 0.0001f);
            Assert.AreEqual(0f, readBuffer[4]);
        }

        [Test]
        public void Resize_NegativeSize_ThrowsArgumentOutOfRangeException()
        {
            using var buffer = new OscDoubleBuffer(4);

            Assert.Throws<ArgumentOutOfRangeException>(() => buffer.Resize(-1));
        }

        [Test]
        public void Resize_WritesWorkAfterResize()
        {
            using var buffer = new OscDoubleBuffer(4);

            buffer.Resize(8);
            buffer.Write(7, 0.9f);
            buffer.Swap();

            Assert.AreEqual(0.9f, buffer.GetReadBuffer()[7], 0.0001f);
        }

        [Test]
        public void Resize_ConcurrentWrites_NoNativeCrash()
        {
            using var buffer = new OscDoubleBuffer(8);
            Exception exception = null;
            int[] sizes = { 4, 16, 2, 32, 8 };
            const int iterations = 1000;

            var writeThread = new Thread(() =>
            {
                try
                {
                    for (int frame = 0; frame < iterations; frame++)
                    {
                        for (int i = 0; i < 32; i++)
                        {
                            buffer.Write(i, frame + i);
                        }
                    }
                }
                catch (Exception ex)
                {
                    exception = ex;
                }
            });

            var resizeThread = new Thread(() =>
            {
                try
                {
                    for (int i = 0; i < iterations; i++)
                    {
                        buffer.Resize(sizes[i % sizes.Length]);
                    }
                }
                catch (Exception ex)
                {
                    exception = ex;
                }
            });

            writeThread.Start();
            resizeThread.Start();
            writeThread.Join();
            resizeThread.Join();

            Assert.IsNull(exception, $"Concurrent Resize/Write threw: {exception}");
        }

        [Test]
        public void Write_IndexOutsideResizedBuffer_DropsWrite()
        {
            using var buffer = new OscDoubleBuffer(4);

            buffer.Write(0, 0.5f);
            buffer.Swap();
            buffer.Resize(1);
            int before = buffer.WriteTick;

            Assert.DoesNotThrow(() => buffer.Write(3, 0.9f));
            buffer.Swap();

            Assert.AreEqual(before, buffer.WriteTick);
            // 範囲外 Write は drop され（WriteTick 不変で検証）、既存 index の値は
            // copy-forward により Resize 前の値を保持する。
            Assert.AreEqual(0.5f, buffer.GetReadBuffer()[0], 0.0001f);
        }

        [Test]
        public void WriteTick_InitialValue_IsZero()
        {
            using var buffer = new OscDoubleBuffer(4);

            Assert.AreEqual(0, buffer.WriteTick);
        }

        [Test]
        public void Write_IncrementsWriteTick()
        {
            using var buffer = new OscDoubleBuffer(4);

            int before = buffer.WriteTick;
            buffer.Write(0, 0.5f);

            Assert.AreEqual(before + 1, buffer.WriteTick);
        }

        [Test]
        public void Write_IndexOutOfRange_DoesNotIncrementWriteTick()
        {
            using var buffer = new OscDoubleBuffer(4);

            int before = buffer.WriteTick;
            buffer.Write(4, 0.5f);

            Assert.AreEqual(before, buffer.WriteTick);
        }

        [Test]
        public void Swap_DoesNotIncrementWriteTick()
        {
            using var buffer = new OscDoubleBuffer(4);

            buffer.Write(0, 0.5f);
            int afterWrite = buffer.WriteTick;

            buffer.Swap();

            Assert.AreEqual(afterWrite, buffer.WriteTick);
        }

        [Test]
        public void Dispose_CalledTwice_DoesNotThrow()
        {
            var buffer = new OscDoubleBuffer(4);
            buffer.Dispose();

            Assert.DoesNotThrow(() => buffer.Dispose());
        }
    }
}
