using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Hidano.FacialControl.Adapters.Bone;

namespace Hidano.FacialControl.Tests.PlayMode.Adapters.Bone
{
    /// <summary>
    /// BoneTransformResolver のテスト（Red、PlayMode）。
    ///
    /// PlayMode 配置の理由: 実 <see cref="UnityEngine.Transform"/> 階層を持つ
    /// GameObject ツリーが必要。
    ///
    /// 検証項目:
    ///   - 名前から Transform を解決でき、未解決時に <c>Debug.LogWarning</c> + null 返却
    ///     （throw しない）。
    ///   - 多バイト boneName（「頭」「左目」等）が解決できる。
    ///   - 命名規則を強制しない（任意の prefix/suffix で OK）。
    ///   - <c>Prime(boneNames)</c> 一括解決後、ホットパスでは辞書ルックアップのみ
    ///     （GC alloc ゼロ）。
    ///   - 同名 Transform が複数存在する場合は「最初の発見を採用 + 1 回だけ警告」
    ///     (M-7 backlog 対応)。曖昧性を避けたいときは相対 path を渡せば警告なしで解決される。
    ///   - 相対 path (<c>"Hips/Spine/Head"</c> 等) が <see cref="UnityEngine.Transform.Find"/>
    ///     経由で解決できる。
    /// </summary>
    [TestFixture]
    public class BoneTransformResolverTests
    {
        private GameObject _root;

        [TearDown]
        public void TearDown()
        {
            if (_root != null)
            {
                UnityEngine.Object.DestroyImmediate(_root);
                _root = null;
            }
        }

        // ================================================================
        // Resolve: 基本動作（名前 → Transform）
        // ================================================================

        [Test]
        public void Resolve_ExistingBoneName_ReturnsTransform()
        {
            // ルート → Hips → Spine → Head の階層を構築
            _root = BuildHierarchy("Root", "Hips", "Spine", "Head");
            var resolver = new BoneTransformResolver(_root.transform);

            var result = resolver.Resolve("Head");

            Assert.IsNotNull(result, "Head が解決できること");
            Assert.AreEqual("Head", result.name);
        }

        [Test]
        public void Resolve_RootBoneName_ReturnsRootTransform()
        {
            // ルート自身も解決対象に含む
            _root = BuildHierarchy("Root", "Hips");
            var resolver = new BoneTransformResolver(_root.transform);

            var result = resolver.Resolve("Root");

            Assert.IsNotNull(result, "ルート Transform 自身も解決できること");
            Assert.AreEqual("Root", result.name);
        }

        [Test]
        public void Resolve_NestedDeepBoneName_ReturnsTransform()
        {
            // 階層深いボーンも再帰探索で解決できる
            _root = BuildHierarchy("Root", "Hips", "Spine", "Chest", "Neck", "Head", "LeftEye");
            var resolver = new BoneTransformResolver(_root.transform);

            var result = resolver.Resolve("LeftEye");

            Assert.IsNotNull(result, "深い階層のボーンも解決できること");
            Assert.AreEqual("LeftEye", result.name);
        }

        // ================================================================
        // Resolve: 解決失敗時の振る舞い（Warning + null、throw しない）
        // ================================================================

        [Test]
        public void Resolve_NonExistentBoneName_LogsWarningAndReturnsNull()
        {
            _root = BuildHierarchy("Root", "Hips", "Head");
            var resolver = new BoneTransformResolver(_root.transform);

            // 未解決時は throw せず Warning + null
            LogAssert.Expect(LogType.Warning, new Regex("NonExistentBone"));

            Transform result = null;
            Assert.DoesNotThrow(() => result = resolver.Resolve("NonExistentBone"));
            Assert.IsNull(result, "未解決 boneName は null を返すこと");
        }

        [Test]
        public void Resolve_NullBoneName_DoesNotThrow_ReturnsNull()
        {
            _root = BuildHierarchy("Root", "Head");
            var resolver = new BoneTransformResolver(_root.transform);

            // null boneName でも throw しない（防御的契約）
            LogAssert.ignoreFailingMessages = true;
            Transform result = null;
            Assert.DoesNotThrow(() => result = resolver.Resolve(null));
            Assert.IsNull(result);
            LogAssert.ignoreFailingMessages = false;
        }

        // ================================================================
        // Resolve: 多バイト boneName
        // ================================================================

        [Test]
        public void Resolve_MultiByteBoneName_Head_ReturnsTransform()
        {
            // 多バイト文字「頭」が解決できる
            _root = BuildHierarchy("Root", "腰", "背骨", "頭");
            var resolver = new BoneTransformResolver(_root.transform);

            var result = resolver.Resolve("頭");

            Assert.IsNotNull(result, "多バイト boneName『頭』が解決できること");
            Assert.AreEqual("頭", result.name);
        }

        [Test]
        public void Resolve_MultiByteBoneName_LeftEyeWithUnderscore_ReturnsTransform()
        {
            // 多バイト + 特殊記号の組合せも解決できる
            _root = BuildHierarchy("Root", "頭", "左目_あ");
            var resolver = new BoneTransformResolver(_root.transform);

            var result = resolver.Resolve("左目_あ");

            Assert.IsNotNull(result, "多バイト + 特殊記号 boneName が解決できること");
            Assert.AreEqual("左目_あ", result.name);
        }

        // ================================================================
        // 命名規則を強制しない
        // ================================================================

        [Test]
        public void Resolve_BoneNameWithArbitraryPrefix_ReturnsTransform()
        {
            // Resolver は prefix/suffix の決まりを強制しない
            _root = BuildHierarchy("Root", "MyRig.Armature.Head", "MyRig.Armature.Eye_L");
            var resolver = new BoneTransformResolver(_root.transform);

            var head = resolver.Resolve("MyRig.Armature.Head");
            var eye = resolver.Resolve("MyRig.Armature.Eye_L");

            Assert.IsNotNull(head, "任意 prefix の boneName でも解決できること");
            Assert.IsNotNull(eye, "任意 suffix の boneName でも解決できること");
        }

        [Test]
        public void Resolve_BoneNameWithMixed_ReturnsTransform()
        {
            // mixamo 風 / Unity Humanoid 風どちらの命名でも解決可能
            _root = BuildHierarchy("Root", "mixamorig:Hips", "mixamorig:Spine", "mixamorig:Head");
            var resolver = new BoneTransformResolver(_root.transform);

            var result = resolver.Resolve("mixamorig:Head");

            Assert.IsNotNull(result, "コロン区切りの命名でも解決できること");
        }

        // ================================================================
        // Prime: 一括ウォームアップ後、ホットパスは辞書ルックアップのみ（GC alloc ゼロ）
        // ================================================================

        [Test]
        public void Prime_ThenResolve_NoGCAllocation()
        {
            _root = BuildHierarchy("Root", "Hips", "Spine", "Chest", "Neck", "Head", "LeftEye", "RightEye");
            var resolver = new BoneTransformResolver(_root.transform);

            var boneNames = new List<string> { "Head", "LeftEye", "RightEye", "Neck", "Hips" };
            resolver.Prime(boneNames);

            // ウォームアップ（JIT 等を排除）
            for (int i = 0; i < 5; i++)
            {
                _ = resolver.Resolve("Head");
                _ = resolver.Resolve("LeftEye");
                _ = resolver.Resolve("RightEye");
            }

            // GC 計測（Profiler API による厳密計測）
            var recorder = Unity.Profiling.ProfilerRecorder.StartNew(
                Unity.Profiling.ProfilerCategory.Memory, "GC.Alloc");

            for (int frame = 0; frame < 60; frame++)
            {
                _ = resolver.Resolve("Head");
                _ = resolver.Resolve("LeftEye");
                _ = resolver.Resolve("RightEye");
                _ = resolver.Resolve("Neck");
                _ = resolver.Resolve("Hips");
            }

            long gcAlloc = recorder.LastValue;
            recorder.Dispose();

            Assert.AreEqual(0, gcAlloc,
                $"Prime 後のホットパス Resolve で GC アロケーションが検出されました: {gcAlloc} bytes");
        }

        [Test]
        public void Prime_MissingBoneName_LogsWarningButContinues()
        {
            // Prime に未解決ボーン名が混じっても続行（warning のみ）
            _root = BuildHierarchy("Root", "Hips", "Head");
            var resolver = new BoneTransformResolver(_root.transform);

            LogAssert.Expect(LogType.Warning, new Regex("MissingBone"));

            var boneNames = new List<string> { "Head", "MissingBone" };
            Assert.DoesNotThrow(() => resolver.Prime(boneNames));

            // 解決可能なボーンは引き続き正しく返る
            Assert.IsNotNull(resolver.Resolve("Head"));
        }

        // ================================================================
        // 同名 Transform が複数: 最初の発見を採用 + 警告 (M-7)
        // ================================================================

        [Test]
        public void Resolve_DuplicateBoneNames_ReturnsFirstFoundAndWarns()
        {
            // 同名 "Eye" を 2 箇所に持つツリー：
            // Root
            //   ├── LeftBranch
            //   │     └── Eye   (← 最初に発見されるべき)
            //   └── RightBranch
            //         └── Eye
            _root = new GameObject("Root");
            var leftBranch = new GameObject("LeftBranch");
            leftBranch.transform.SetParent(_root.transform);
            var leftEye = new GameObject("Eye");
            leftEye.transform.SetParent(leftBranch.transform);

            var rightBranch = new GameObject("RightBranch");
            rightBranch.transform.SetParent(_root.transform);
            var rightEye = new GameObject("Eye");
            rightEye.transform.SetParent(rightBranch.transform);

            var resolver = new BoneTransformResolver(_root.transform);

            // M-7: 同名複数衝突時は警告を発火する（照合は衝突したボーン名のみ）。
            LogAssert.Expect(LogType.Warning, new Regex("Eye"));

            var result = resolver.Resolve("Eye");

            Assert.IsNotNull(result, "同名複数でも最初の発見を返すこと");
            Assert.AreSame(leftEye.transform, result,
                "同名複数の場合、最初に発見された Transform (LeftBranch 配下) を返すこと");
        }

        // ================================================================
        // 相対 path 解決 (S-1)
        // ================================================================

        [Test]
        public void Resolve_RelativePath_ReturnsCorrectTransform()
        {
            // Root/Hips/Spine/Head の階層で、相対 path "Hips/Spine/Head" → Head Transform
            _root = BuildHierarchy("Root", "Hips", "Spine", "Head");
            var resolver = new BoneTransformResolver(_root.transform);

            var result = resolver.Resolve("Hips/Spine/Head");

            Assert.IsNotNull(result, "相対 path 'Hips/Spine/Head' が解決できること");
            Assert.AreEqual("Head", result.name);
        }

        [Test]
        public void Resolve_RelativePath_DisambiguatesSameNameWithoutWarning()
        {
            // 同名 "Eye" を 2 箇所に持つツリーで、相対 path で右目を一意指定する：
            // Root
            //   ├── LeftBranch / Eye
            //   └── RightBranch / Eye  ← "RightBranch/Eye" で曖昧性なく解決
            _root = new GameObject("Root");
            var leftBranch = new GameObject("LeftBranch");
            leftBranch.transform.SetParent(_root.transform);
            var leftEye = new GameObject("Eye");
            leftEye.transform.SetParent(leftBranch.transform);

            var rightBranch = new GameObject("RightBranch");
            rightBranch.transform.SetParent(_root.transform);
            var rightEye = new GameObject("Eye");
            rightEye.transform.SetParent(rightBranch.transform);

            var resolver = new BoneTransformResolver(_root.transform);

            // 相対 path 経路は Transform.Find に委譲され、複数同名警告の対象外。
            // LogAssert.Expect は宣言しない。万一警告が出ると LogAssert が失敗する。
            var result = resolver.Resolve("RightBranch/Eye");

            Assert.IsNotNull(result, "相対 path で右目側を解決できること");
            Assert.AreSame(rightEye.transform, result,
                "相対 path は LeftBranch 側ではなく RightBranch 側の Eye を返すこと");
        }

        [Test]
        public void Resolve_RelativePath_NonExistent_ReturnsNull()
        {
            _root = BuildHierarchy("Root", "Hips", "Head");
            var resolver = new BoneTransformResolver(_root.transform);

            LogAssert.Expect(LogType.Warning, new Regex("Hips/MissingChild"));

            var result = resolver.Resolve("Hips/MissingChild");

            Assert.IsNull(result, "存在しない相対 path は null を返すこと");
        }

        // ================================================================
        // 相対 path が完全一致しない場合の suffix フォールバック
        //
        // 同じ Face Mesh を持つ衣装違いモデルのように、ボーン名は同じでも
        // 上位階層だけが異なるモデル間で path を使い回せるようにする。
        // ================================================================

        [Test]
        public void Resolve_RelativePath_UpperHierarchyDiffers_FallsBackToSuffixMatch()
        {
            // モデル側は Root/Armature/Neck/Head だが、path は別モデルの階層 "Rig/Neck/Head"。
            // 末尾 2 セグメント (Neck/Head) が一致するのでフォールバックで解決する。
            _root = BuildHierarchy("Root", "Armature", "Neck", "Head");
            var resolver = new BoneTransformResolver(_root.transform);

            LogAssert.Expect(LogType.Warning, new Regex("Rig/Neck/Head"));

            var result = resolver.Resolve("Rig/Neck/Head");

            Assert.IsNotNull(result, "階層が異なっても末尾一致で解決できること");
            Assert.AreEqual("Head", result.name);
        }

        [Test]
        public void Resolve_RelativePath_OnlyLeafMatches_FallsBackToLeafName()
        {
            // 末端のボーン名しか一致しないケース。1 セグメント一致でも解決する。
            _root = BuildHierarchy("Root", "Head");
            var resolver = new BoneTransformResolver(_root.transform);

            LogAssert.Expect(LogType.Warning, new Regex("Foo/Bar/Head"));

            var result = resolver.Resolve("Foo/Bar/Head");

            Assert.IsNotNull(result, "末端名だけの一致でも解決できること");
            Assert.AreEqual("Head", result.name);
        }

        [Test]
        public void Resolve_RelativePath_PrefersLongestSuffixMatch()
        {
            // Root
            //   ├── Head        (末尾 1 セグメント一致)
            //   └── Neck
            //         └── Head  (末尾 2 セグメント一致 ← こちらを採用)
            _root = new GameObject("Root");
            var shallowHead = new GameObject("Head");
            shallowHead.transform.SetParent(_root.transform);

            var neck = new GameObject("Neck");
            neck.transform.SetParent(_root.transform);
            var deepHead = new GameObject("Head");
            deepHead.transform.SetParent(neck.transform);

            var resolver = new BoneTransformResolver(_root.transform);

            LogAssert.Expect(LogType.Warning, new Regex("Rig/Neck/Head"));

            var result = resolver.Resolve("Rig/Neck/Head");

            Assert.AreSame(deepHead.transform, result,
                "一致セグメント数が多い Neck/Head を優先すること");
        }

        [Test]
        public void Resolve_RelativePath_AmbiguousSuffixMatch_WarnsAndReturnsFirst()
        {
            // 同名 "Eye" が同じスコアで 2 件。最初の発見を採用しつつ件数を警告に含める。
            _root = new GameObject("Root");
            var left = new GameObject("Left");
            left.transform.SetParent(_root.transform);
            var leftEye = new GameObject("Eye");
            leftEye.transform.SetParent(left.transform);

            var right = new GameObject("Right");
            right.transform.SetParent(_root.transform);
            var rightEye = new GameObject("Eye");
            rightEye.transform.SetParent(right.transform);

            var resolver = new BoneTransformResolver(_root.transform);

            LogAssert.Expect(LogType.Warning, new Regex("Eye"));

            var result = resolver.Resolve("Rig/Eye");

            Assert.AreSame(leftEye.transform, result, "同スコア複数なら最初の発見を返すこと");
        }

        [Test]
        public void Resolve_RelativePath_FallbackResult_IsCachedAndWarnsOnce()
        {
            _root = BuildHierarchy("Root", "Armature", "Neck", "Head");
            var resolver = new BoneTransformResolver(_root.transform);

            // 警告は 1 回だけ。2 回目以降はキャッシュから返るため再警告しない。
            LogAssert.Expect(LogType.Warning, new Regex("Rig/Neck/Head"));

            var first = resolver.Resolve("Rig/Neck/Head");
            var second = resolver.Resolve("Rig/Neck/Head");

            Assert.IsNotNull(first);
            Assert.AreSame(first, second, "フォールバック結果もキャッシュされること");
        }

        [Test]
        public void Resolve_RelativePath_LeafNameNotFoundAnywhere_ReturnsNull()
        {
            // 末端名すら存在しない場合はフォールバックしても解決できず、従来どおり null + 警告。
            _root = BuildHierarchy("Root", "Armature", "Neck", "Head");
            var resolver = new BoneTransformResolver(_root.transform);

            LogAssert.Expect(LogType.Warning, new Regex("Rig/Neck/Tail"));

            var result = resolver.Resolve("Rig/Neck/Tail");

            Assert.IsNull(result, "末端名が存在しなければ null を返すこと");
        }

        [Test]
        public void Resolve_SameNameMultipleCalls_ReturnsSameInstance()
        {
            // キャッシュにより同じ名前は同じ Transform インスタンスを返す
            _root = BuildHierarchy("Root", "Hips", "Head");
            var resolver = new BoneTransformResolver(_root.transform);

            var first = resolver.Resolve("Head");
            var second = resolver.Resolve("Head");

            Assert.AreSame(first, second, "同名連続呼出はキャッシュにより同インスタンスを返すこと");
        }

        // ================================================================
        // ヘルパー
        // ================================================================

        /// <summary>
        /// 指定された名前を線形に親子チェーンで連結する。
        /// 例: BuildHierarchy("Root", "Hips", "Head") → Root/Hips/Head
        /// </summary>
        private static GameObject BuildHierarchy(params string[] names)
        {
            if (names == null || names.Length == 0)
            {
                throw new ArgumentException("名前を 1 件以上指定してください", nameof(names));
            }

            var root = new GameObject(names[0]);
            Transform current = root.transform;
            for (int i = 1; i < names.Length; i++)
            {
                var child = new GameObject(names[i]);
                child.transform.SetParent(current);
                current = child.transform;
            }
            return root;
        }
    }
}
