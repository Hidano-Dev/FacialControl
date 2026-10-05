using System.Collections.Generic;
using Hidano.FacialControl.Adapters.Playable;
using Hidano.FacialControl.Domain.Models;
using NUnit.Framework;
using UnityEngine;

namespace Hidano.FacialControl.Timeline.Tests.PlayMode
{
    /// <summary>
    /// 既存 PlayMode fixture 用の最小ホスト。Receiver がセッションを開始できるよう、Animator + BlendShape 付きメッシュを持つ
    /// GameObject に FacialController を置き、指定 Profile で初期化する（fixture の本移行は 10.6）。
    /// </summary>
    internal static class TimelinePlayModeControllerHost
    {
        public static GameObject Create(string name, FacialProfile profile, IReadOnlyList<string> blendShapeNames)
        {
            var root = new GameObject(name);
            root.AddComponent<Animator>();

            var face = new GameObject("Face");
            face.transform.SetParent(root.transform, false);
            var renderer = face.AddComponent<SkinnedMeshRenderer>();
            var mesh = new Mesh
            {
                name = name + "_Mesh",
                vertices = new[] { Vector3.zero, Vector3.right, Vector3.up },
                triangles = new[] { 0, 1, 2 },
            };
            for (int i = 0; i < blendShapeNames.Count; i++)
            {
                mesh.AddBlendShapeFrame(blendShapeNames[i], 100f, new Vector3[3], null, null);
            }

            renderer.sharedMesh = mesh;

            var controller = root.AddComponent<FacialController>();
            controller.InitializeWithProfile(profile);
            Assert.That(controller.IsInitialized, Is.True, "fixture: FacialController が初期化できること");
            return root;
        }

        public static void Destroy(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            SkinnedMeshRenderer renderer = root.GetComponentInChildren<SkinnedMeshRenderer>();
            Mesh mesh = renderer != null ? renderer.sharedMesh : null;
            Object.DestroyImmediate(root);
            if (mesh != null)
            {
                Object.DestroyImmediate(mesh);
            }
        }
    }
}
