using System;
using NUnit.Framework;
using UnityEngine;
using Hidano.FacialControl.Adapters.Playable;
using Hidano.FacialControl.Domain.Models;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.EditMode.Adapters
{
    /// <summary>
    /// P06-T02: AnimationClipCache の単体テスト。
    /// LRU 動作、キャッシュヒット / ミスを検証する。
    /// </summary>
    [TestFixture]
    [SmallTest]
    public class AnimationClipCacheTests : SizedTestFixture
    {
        private AnimationClipCache _cache;

        [TearDown]
        public void TearDown()
        {
            _cache?.Dispose();
            _cache = null;
        }

        // ================================================================
        // コンストラクタ
        // ================================================================

        [Test]
        public void Constructor_DefaultCapacity_Creates16EntryCache()
        {
            _cache = new AnimationClipCache();

            Assert.AreEqual(16, _cache.Capacity);
        }

        [Test]
        public void Constructor_CustomCapacity_CreatesWithSpecifiedSize()
        {
            _cache = new AnimationClipCache(8);

            Assert.AreEqual(8, _cache.Capacity);
        }

        [Test]
        public void Constructor_ZeroCapacity_ThrowsArgumentOutOfRangeException()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
            {
                var cache = new AnimationClipCache(0);
                cache.Dispose();
            });
        }

        [Test]
        public void Constructor_NegativeCapacity_ThrowsArgumentOutOfRangeException()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
            {
                var cache = new AnimationClipCache(-1);
                cache.Dispose();
            });
        }

        // ================================================================
        // GetOrCreate — キャッシュミス
        // ================================================================

        [Test]
        public void GetOrCreate_FirstCall_ReturnsNonNullAnimationClip()
        {
            _cache = new AnimationClipCache();
            var blendShapeValues = new[]
            {
                new BlendShapeMapping("eyeBlink_L", 1.0f),
                new BlendShapeMapping("eyeBlink_R", 1.0f)
            };

            var clip = _cache.GetOrCreate("expr-001", blendShapeValues);

            Assert.IsNotNull(clip);
            Assert.IsInstanceOf<AnimationClip>(clip);
        }

        [Test]
        public void GetOrCreate_CacheMiss_IncrementsCount()
        {
            _cache = new AnimationClipCache();
            var blendShapeValues = new[]
            {
                new BlendShapeMapping("mouthOpen", 0.5f)
            };

            _cache.GetOrCreate("expr-001", blendShapeValues);

            Assert.AreEqual(1, _cache.Count);
        }

        [Test]
        public void GetOrCreate_DifferentIds_CreatesSeparateEntries()
        {
            _cache = new AnimationClipCache();
            var values1 = new[] { new BlendShapeMapping("eyeBlink_L", 1.0f) };
            var values2 = new[] { new BlendShapeMapping("mouthOpen", 0.8f) };

            var clip1 = _cache.GetOrCreate("expr-001", values1);
            var clip2 = _cache.GetOrCreate("expr-002", values2);

            Assert.AreNotSame(clip1, clip2);
            Assert.AreEqual(2, _cache.Count);
        }

        // ================================================================
        // GetOrCreate — キャッシュヒット
        // ================================================================

        [Test]
        public void GetOrCreate_SameId_ReturnsCachedClip()
        {
            _cache = new AnimationClipCache();
            var blendShapeValues = new[]
            {
                new BlendShapeMapping("eyeBlink_L", 1.0f)
            };

            var clip1 = _cache.GetOrCreate("expr-001", blendShapeValues);
            var clip2 = _cache.GetOrCreate("expr-001", blendShapeValues);

            Assert.AreSame(clip1, clip2);
        }

        [Test]
        public void GetOrCreate_CacheHit_DoesNotIncrementCount()
        {
            _cache = new AnimationClipCache();
            var blendShapeValues = new[]
            {
                new BlendShapeMapping("eyeBlink_L", 1.0f)
            };

            _cache.GetOrCreate("expr-001", blendShapeValues);
            _cache.GetOrCreate("expr-001", blendShapeValues);

            Assert.AreEqual(1, _cache.Count);
        }

        // ================================================================
        // LRU エビクション
        // ================================================================

        [Test]
        public void GetOrCreate_ExceedsCapacity_EvictsLeastRecentlyUsed()
        {
            _cache = new AnimationClipCache(2);
            var values = new[] { new BlendShapeMapping("test", 0.5f) };

            // expr-001 と expr-002 をキャッシュ
            var clip1 = _cache.GetOrCreate("expr-001", values);
            _cache.GetOrCreate("expr-002", values);

            // expr-003 を追加 → expr-001 がエビクトされる
            _cache.GetOrCreate("expr-003", values);

            Assert.AreEqual(2, _cache.Count);

            // expr-001 は再生成される（別インスタンス）
            var clip1Again = _cache.GetOrCreate("expr-001", values);
            Assert.AreNotSame(clip1, clip1Again);
        }

        [Test]
        public void GetOrCreate_AccessRefreshesLru_EvictsCorrectEntry()
        {
            _cache = new AnimationClipCache(2);
            var values = new[] { new BlendShapeMapping("test", 0.5f) };

            // expr-001 と expr-002 をキャッシュ
            _cache.GetOrCreate("expr-001", values);
            var clip2 = _cache.GetOrCreate("expr-002", values);

            // expr-001 にアクセスして LRU を更新
            _cache.GetOrCreate("expr-001", values);

            // expr-003 を追加 → expr-002 がエビクトされる（expr-001 はアクセス済み）
            _cache.GetOrCreate("expr-003", values);

            // expr-002 は再生成される（別インスタンス）
            var clip2Again = _cache.GetOrCreate("expr-002", values);
            Assert.AreNotSame(clip2, clip2Again);
        }

        [Test]
        public void GetOrCreate_CapacityOne_AlwaysReplacesEntry()
        {
            _cache = new AnimationClipCache(1);
            var values = new[] { new BlendShapeMapping("test", 0.5f) };

            var clip1 = _cache.GetOrCreate("expr-001", values);
            _cache.GetOrCreate("expr-002", values);

            // expr-001 はエビクト済み
            var clip1Again = _cache.GetOrCreate("expr-001", values);
            Assert.AreNotSame(clip1, clip1Again);
            Assert.AreEqual(1, _cache.Count);
        }

        // ================================================================
        // AnimationClip の生成内容
        // ================================================================

        [Test]
        public void GetOrCreate_EmptyBlendShapeValues_ReturnsClip()
        {
            _cache = new AnimationClipCache();
            var blendShapeValues = Array.Empty<BlendShapeMapping>();

            var clip = _cache.GetOrCreate("expr-empty", blendShapeValues);

            Assert.IsNotNull(clip);
        }

        // ================================================================
        // Clear
        // ================================================================

        [Test]
        public void Clear_EmptyCache_DoesNotThrow()
        {
            _cache = new AnimationClipCache();

            Assert.DoesNotThrow(() => _cache.Clear());
        }

        [Test]
        public void Clear_WithEntries_ResetsCountToZero()
        {
            _cache = new AnimationClipCache();
            var values = new[] { new BlendShapeMapping("test", 0.5f) };

            _cache.GetOrCreate("expr-001", values);
            _cache.GetOrCreate("expr-002", values);
            _cache.Clear();

            Assert.AreEqual(0, _cache.Count);
        }

        [Test]
        public void Clear_AfterClear_ReturnsNewClip()
        {
            _cache = new AnimationClipCache();
            var values = new[] { new BlendShapeMapping("test", 0.5f) };

            var clip1 = _cache.GetOrCreate("expr-001", values);
            _cache.Clear();
            var clip2 = _cache.GetOrCreate("expr-001", values);

            Assert.AreNotSame(clip1, clip2);
        }

        // ================================================================
        // Dispose
        // ================================================================

        [Test]
        public void Dispose_DoesNotThrow()
        {
            var cache = new AnimationClipCache();
            var values = new[] { new BlendShapeMapping("test", 0.5f) };
            cache.GetOrCreate("expr-001", values);

            Assert.DoesNotThrow(() => cache.Dispose());
        }

        [Test]
        public void Dispose_CalledTwice_DoesNotThrow()
        {
            var cache = new AnimationClipCache();
            cache.Dispose();

            Assert.DoesNotThrow(() => cache.Dispose());
        }

        // ================================================================
        // エラーケース
        // ================================================================

        [Test]
        public void GetOrCreate_NullExpressionId_ThrowsArgumentNullException()
        {
            _cache = new AnimationClipCache();
            var values = new[] { new BlendShapeMapping("test", 0.5f) };

            Assert.Throws<ArgumentNullException>(() => _cache.GetOrCreate(null, values));
        }

        [Test]
        public void GetOrCreate_NullBlendShapeValues_ThrowsArgumentNullException()
        {
            _cache = new AnimationClipCache();

            Assert.Throws<ArgumentNullException>(() => _cache.GetOrCreate("expr-001", null));
        }

        // ================================================================
        // ContainsKey
        // ================================================================

        [Test]
        public void ContainsKey_ExistingEntry_ReturnsTrue()
        {
            _cache = new AnimationClipCache();
            var values = new[] { new BlendShapeMapping("test", 0.5f) };

            _cache.GetOrCreate("expr-001", values);

            Assert.IsTrue(_cache.ContainsKey("expr-001"));
        }

        [Test]
        public void ContainsKey_NonExistingEntry_ReturnsFalse()
        {
            _cache = new AnimationClipCache();

            Assert.IsFalse(_cache.ContainsKey("expr-001"));
        }

        [Test]
        public void ContainsKey_AfterEviction_ReturnsFalse()
        {
            _cache = new AnimationClipCache(1);
            var values = new[] { new BlendShapeMapping("test", 0.5f) };

            _cache.GetOrCreate("expr-001", values);
            _cache.GetOrCreate("expr-002", values);

            Assert.IsFalse(_cache.ContainsKey("expr-001"));
            Assert.IsTrue(_cache.ContainsKey("expr-002"));
        }
    }
}
