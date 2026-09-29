using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Hidano.FacialControl.Adapters.Playable;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Models;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.PlayMode.Adapters.Playable
{
    /// <summary>
    /// 同一モデルの SkinnedMeshRenderer を複数の <see cref="FacialController"/> が
    /// 掴んでいるときに、階層上位の 1 つだけが生き残ることを検証する。
    /// <para>
    /// 実運用では「モデルを載せる空 GameObject」と「モデル prefab のルート」の両方に
    /// FacialController を付けてしまう誤設定が起きる。この状態では 2 つの LateUpdate が
    /// 同じ BlendShape を奪い合い、入力を受けていない側が 0 で上書きして表情が動かなくなる。
    /// </para>
    /// <para>
    /// 逆に、シーンルート直下へ 1 体ずつ並べた複数キャラ（兄弟関係）は renderer を共有しないため
    /// 競合ではない。全キャラが有効なまま動くことも本 fixture で固定する。
    /// </para>
    /// </summary>
    [TestFixture]
    [MediumTest]
    public class FacialControllerDuplicateOwnershipTests : SizedTestFixture
    {
        private readonly List<Object> _created = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            FacialControllerRendererOwnership.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            FacialControllerRendererOwnership.Clear();

            for (int i = 0; i < _created.Count; i++)
            {
                if (_created[i] != null)
                {
                    Object.DestroyImmediate(_created[i]);
                }
            }
            _created.Clear();
        }

        [UnityTest]
        public IEnumerator Initialize_AncestorFirstThenDescendant_DisablesDescendant()
        {
            BuildNestedHierarchy(out FacialController ancestor, out FacialController descendant);

            ancestor.Initialize();
            ExpectDuplicateWarning();
            descendant.Initialize();

            yield return null;

            Assert.That(ancestor.IsInitialized, Is.True, "階層上位の FacialController は生き残る");
            Assert.That(ancestor.enabled, Is.True);
            Assert.That(descendant.IsInitialized, Is.False, "階層下位の FacialController は初期化されない");
            Assert.That(descendant.enabled, Is.False, "階層下位の FacialController は無効化される");
        }

        [UnityTest]
        public IEnumerator Initialize_DescendantFirstThenAncestor_TakesOverAndDisablesDescendant()
        {
            BuildNestedHierarchy(out FacialController ancestor, out FacialController descendant);

            descendant.Initialize();
            Assert.That(descendant.IsInitialized, Is.True, "単独で先に初期化した時点では成立している");

            ExpectDuplicateWarning();
            ancestor.Initialize();

            yield return null;

            Assert.That(ancestor.IsInitialized, Is.True, "後から初期化した階層上位が引き継ぐ");
            Assert.That(ancestor.enabled, Is.True);
            Assert.That(descendant.IsInitialized, Is.False, "引き継がれた階層下位は初期化が解除される");
            Assert.That(descendant.enabled, Is.False);
        }

        [UnityTest]
        public IEnumerator Initialize_UnrelatedControllersSharingRenderer_KeepsFirstRegistered()
        {
            var mesh = CreateMeshWithBlendShape("smile");
            var faceObject = new GameObject("Face");
            _created.Add(faceObject);
            var renderer = faceObject.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = mesh;

            var first = CreateControllerHost("FirstHost");
            first.SkinnedMeshRenderers = new[] { renderer };
            var second = CreateControllerHost("SecondHost");
            second.SkinnedMeshRenderers = new[] { renderer };

            first.Initialize();
            ExpectDuplicateWarning();
            second.Initialize();

            yield return null;

            Assert.That(first.IsInitialized, Is.True, "親子関係が無い場合は先着が生き残る");
            Assert.That(first.enabled, Is.True);
            Assert.That(second.IsInitialized, Is.False);
            Assert.That(second.enabled, Is.False);
        }

        /// <summary>
        /// MagicaCloth の Burst 処理都合で、複数キャラはシーンルート直下に 1 体ずつ並べる構成が一般的。
        /// この兄弟関係は renderer を共有しないため競合として扱ってはならない。
        /// </summary>
        [UnityTest]
        public IEnumerator Initialize_SiblingRootsWithSeparateModels_KeepsBothEnabled()
        {
            BuildCharacterRoot("CharacterA", out FacialController first, out SkinnedMeshRenderer firstRenderer);
            BuildCharacterRoot("CharacterB", out FacialController second, out SkinnedMeshRenderer secondRenderer);

            first.Initialize();
            second.Initialize();

            yield return null;

            Assert.That(first.IsInitialized, Is.True, "兄弟ルートの 1 体目は初期化される");
            Assert.That(first.enabled, Is.True, "兄弟ルートの 1 体目は無効化されない");
            Assert.That(second.IsInitialized, Is.True, "兄弟ルートの 2 体目も初期化される");
            Assert.That(second.enabled, Is.True, "兄弟ルートの 2 体目も無効化されない");

            Assert.That(
                FacialControllerRendererOwnership.FindConflict(first, new[] { firstRenderer }),
                Is.Null,
                "別モデルの renderer は競合として検出されない");
            Assert.That(
                FacialControllerRendererOwnership.FindConflict(second, new[] { secondRenderer }),
                Is.Null,
                "別モデルの renderer は競合として検出されない");
        }

        [UnityTest]
        public IEnumerator Initialize_SingleController_StaysEnabled()
        {
            BuildNestedHierarchy(out FacialController ancestor, out FacialController descendant);
            Object.DestroyImmediate(descendant);

            ancestor.Initialize();

            yield return null;

            Assert.That(ancestor.IsInitialized, Is.True);
            Assert.That(ancestor.enabled, Is.True);
        }

        [UnityTest]
        public IEnumerator Reinitialize_AfterConflictingControllerRemoved_Succeeds()
        {
            BuildNestedHierarchy(out FacialController ancestor, out FacialController descendant);

            ancestor.Initialize();
            ExpectDuplicateWarning();
            descendant.Initialize();

            Assert.That(descendant.enabled, Is.False);

            // 誤って付けた側を残したまま、正しい側を取り除いたケース。
            Object.DestroyImmediate(ancestor);
            yield return null;

            descendant.enabled = true;
            descendant.Initialize();

            Assert.That(descendant.IsInitialized, Is.True, "競合相手が消えれば再初期化できる");
            Assert.That(descendant.enabled, Is.True);
        }

        /// <summary>
        /// Root(FacialController) -&gt; Model(FacialController) -&gt; Face(SkinnedMeshRenderer)
        /// の入れ子構造を作る。両 FacialController が同じ Face を自動検索で拾う。
        /// </summary>
        private void BuildNestedHierarchy(out FacialController ancestor, out FacialController descendant)
        {
            var root = new GameObject("DuplicateOwnershipRoot");
            _created.Add(root);
            root.AddComponent<Animator>();

            var model = new GameObject("Model");
            model.transform.SetParent(root.transform, false);
            model.AddComponent<Animator>();

            var faceObject = new GameObject("Face");
            faceObject.transform.SetParent(model.transform, false);
            var renderer = faceObject.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = CreateMeshWithBlendShape("smile");

            ancestor = root.AddComponent<FacialController>();
            ancestor.CharacterSO = CreateProfileAsset();

            descendant = model.AddComponent<FacialController>();
            descendant.CharacterSO = CreateProfileAsset();
        }

        /// <summary>
        /// {name}(FacialController) -&gt; Face(SkinnedMeshRenderer) のキャラクター 1 体分を
        /// シーンルート直下に作る。複数回呼べば互いに兄弟関係のルートになる。
        /// </summary>
        private void BuildCharacterRoot(string name, out FacialController controller, out SkinnedMeshRenderer renderer)
        {
            var root = new GameObject(name);
            _created.Add(root);
            root.AddComponent<Animator>();

            var faceObject = new GameObject("Face");
            faceObject.transform.SetParent(root.transform, false);
            renderer = faceObject.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = CreateMeshWithBlendShape("smile");

            controller = root.AddComponent<FacialController>();
            controller.CharacterSO = CreateProfileAsset();
        }

        private FacialController CreateControllerHost(string name)
        {
            var go = new GameObject(name);
            _created.Add(go);
            go.AddComponent<Animator>();
            var controller = go.AddComponent<FacialController>();
            controller.CharacterSO = CreateProfileAsset();
            return controller;
        }

        private FacialCharacterProfileSO CreateProfileAsset()
        {
            var asset = ScriptableObject.CreateInstance<DuplicateOwnershipProfileSO>();
            asset.ProfileToLoad = CreateProfile();
            _created.Add(asset);
            return asset;
        }

        private Mesh CreateMeshWithBlendShape(string blendShapeName)
        {
            var mesh = new Mesh
            {
                vertices = new[] { Vector3.zero, Vector3.right, Vector3.up },
                triangles = new[] { 0, 1, 2 },
            };
            mesh.AddBlendShapeFrame(blendShapeName, 100f, new Vector3[3], null, null);
            _created.Add(mesh);
            return mesh;
        }

        private static FacialProfile CreateProfile()
        {
            var layers = new[]
            {
                new LayerDefinition("emotion", 0, ExclusionMode.LastWins)
            };
            var expressions = new[]
            {
                new Expression(
                    "expr-happy",
                    "Happy",
                    "emotion",
                    0.05f,
                    TransitionCurve.Linear,
                    new[]
                    {
                        new BlendShapeMapping("smile", 1.0f)
                    })
            };

            return new FacialProfile("1.0.0", layers, expressions);
        }

        private static void ExpectDuplicateWarning()
        {
            LogAssert.Expect(
                LogType.Warning,
                new Regex(Regex.Escape(FacialController.DuplicateOwnershipLogPrefix)));
        }

        public sealed class DuplicateOwnershipProfileSO : FacialCharacterProfileSO
        {
            public FacialProfile ProfileToLoad;

            public override FacialProfile LoadProfile()
            {
                return ProfileToLoad;
            }
        }
    }
}
