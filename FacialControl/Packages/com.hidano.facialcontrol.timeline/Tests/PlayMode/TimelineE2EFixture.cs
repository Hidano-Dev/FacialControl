#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Hidano.FacialControl.Adapters.Playable;
using Hidano.FacialControl.Adapters.ScriptableObject;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Timeline.Adapters;
using Hidano.FacialControl.Timeline.Adapters.AdapterBindings;
using Hidano.FacialControl.Timeline.Adapters.Assets;
using Hidano.FacialControl.Timeline.Editor;
using Hidano.FacialControl.Timeline.Tests.Shared;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Hidano.FacialControl.Timeline.Tests.PlayMode
{
    /// <summary>
    /// e2e のキャラクター配置（4 手順の「Receiver を FacialController と同じ GameObject に置く」と Director の置き場所）。
    /// </summary>
    internal enum TimelineE2EPlacement
    {
        /// <summary>FacialController / Receiver / Director をすべて同じ GameObject に置く。</summary>
        SameObject = 0,

        /// <summary>FacialController と Receiver を同じ GameObject に置き、Director は親 GameObject に置く。</summary>
        DirectorOnParent = 1,

        /// <summary>Receiver と Director を FacialController の子 GameObject に置く（配置の誤り）。</summary>
        ReceiverOnChild = 2,
    }

    /// <summary>
    /// e2e 用の fixture。REC 記録（.fcrec）→ Profile SO（アセット化）→ REC Export（TimelineAsset + Bake）までを作り、
    /// キャラクター（BlendShape 付きメッシュ + 明示目ボーン + FacialController / Receiver / Director）を配置する。
    /// </summary>
    /// <remarks>
    /// <para>生成物は <c>Assets/TimelineE2E_{guid}/</c> に置き、<see cref="Dispose"/> で GameObject・メッシュ・アセット・
    /// StreamingAssets の profile.json（生成されていれば）を削除する。</para>
    /// <para>Profile には Timeline 専用の設定（レイヤー宣言・Target Layer Names・Bake 参照）を一切書かない。
    /// AdapterBindings は Timeline binding（既定値）と Fake analog binding（InputSystem の analog expression と同形）のみ。</para>
    /// </remarks>
    internal sealed class TimelineE2EFixture : IDisposable
    {
        public const string EmotionLayer = "emotion";
        public const string OverlayLayer = "overlay";
        public const string SmileExpressionId = RecFixtureWriter.TriggerExpressionId;
        public const string SquintExpressionId = "squint";
        public const string SmileBlendShape = "Smile";
        public const string SquintBlendShape = "Squint";
        public const string BlinkBlendShape = "Blink";
        public const float SmileExpressionValue = 1f;
        public const float SquintExpressionValue = 0.8f;
        public const string LeftEyeName = "LeftEye";
        public const string RightEyeName = "RightEye";

        private static readonly FieldInfo AdapterBindingsField = typeof(FacialCharacterProfileSO).GetField(
            "_adapterBindings",
            BindingFlags.Instance | BindingFlags.NonPublic);

        private readonly List<TimelineE2ECharacter> _characters = new List<TimelineE2ECharacter>();
        private readonly string _folderPath;

        private TimelineE2EFixture(
            string folderPath,
            FacialCharacterProfileSO profileAsset,
            TimelineAdapterBinding timelineBinding,
            FakeAnalogAdapterBinding fakeBinding,
            RecFixtureWriter.Recording recording,
            string recordingPath,
            RecToTimelineExporter.ExportResult exportResult)
        {
            _folderPath = folderPath;
            ProfileAsset = profileAsset;
            TimelineBinding = timelineBinding;
            FakeBinding = fakeBinding;
            Recording = recording;
            RecordingPath = recordingPath;
            ExportResult = exportResult;
        }

        public FacialCharacterProfileSO ProfileAsset { get; }

        /// <summary>Profile SO の AdapterBindings に入っている Timeline binding（既定値のまま）。</summary>
        public TimelineAdapterBinding TimelineBinding { get; }

        /// <summary>Profile SO の AdapterBindings に入っている Fake analog binding（slug <c>osc</c>）。</summary>
        public FakeAnalogAdapterBinding FakeBinding { get; }

        public RecFixtureWriter.Recording Recording { get; }

        /// <summary>.fcrec の絶対パス。</summary>
        public string RecordingPath { get; }

        public RecToTimelineExporter.ExportResult ExportResult { get; }

        public TimelineAsset Timeline => ExportResult.Timeline;

        public FacialTimelineBakeAsset Bake => ExportResult.BakeAsset;

        /// <summary>
        /// 記録を書き出し、Profile SO をアセット化して REC Export まで行う。
        /// </summary>
        /// <param name="recording">記録内容（null なら既定）。</param>
        /// <param name="configureProfile">アセット化前に Profile SO を追加で編集する（null なら既定構成のまま）。</param>
        public static TimelineE2EFixture Create(
            RecFixtureWriter.Recording recording = null,
            Action<FacialCharacterProfileSO> configureProfile = null)
        {
            recording ??= new RecFixtureWriter.Recording();

            string guid = Guid.NewGuid().ToString("N");
            string folderName = "TimelineE2E_" + guid;
            AssetDatabase.CreateFolder("Assets", folderName);
            string folderPath = "Assets/" + folderName;

            var profileAsset = ScriptableObject.CreateInstance<FacialCharacterProfileSO>();
            profileAsset.name = "TimelineE2EProfile_" + guid;
            ConfigureDefaultProfile(profileAsset);

            var timelineBinding = new TimelineAdapterBinding();
            var fakeBinding = new FakeAnalogAdapterBinding { AnalogExpressionId = SquintExpressionId };
            List<AdapterBindingBase> bindings = GetWritableAdapterBindings(profileAsset);
            bindings.Add(timelineBinding);
            bindings.Add(fakeBinding);

            configureProfile?.Invoke(profileAsset);
            AssetDatabase.CreateAsset(profileAsset, folderPath + "/" + profileAsset.name + ".asset");

            string recordingPath = Path.GetFullPath(folderPath + "/recording.fcrec");
            RecFixtureWriter.Write(recordingPath, recording);
            AssetDatabase.Refresh();

            if (!RecToTimelineExporter.TryExportTimelineAsset(
                    recordingPath,
                    profileAsset,
                    folderPath + "/Exported.playable",
                    out RecToTimelineExporter.ExportResult result))
            {
                AssetDatabase.DeleteAsset(folderPath);
                throw new InvalidOperationException("fixture: REC Export に失敗しました。");
            }

            return new TimelineE2EFixture(
                folderPath,
                profileAsset,
                timelineBinding,
                fakeBinding,
                recording,
                recordingPath,
                result);
        }

        /// <summary>Profile SO の AdapterBindings（protected のシリアライズリスト）を書き換え可能な参照で返す。</summary>
        public static List<AdapterBindingBase> GetWritableAdapterBindings(FacialCharacterProfileSO profileAsset)
        {
            return (List<AdapterBindingBase>)AdapterBindingsField.GetValue(profileAsset);
        }

        /// <summary>emotion レイヤーの inputSources 宣言。</summary>
        public List<InputSourceDeclarationSerializable> EmotionInputSources => ProfileAsset.Layers[0].inputSources;

        /// <summary>
        /// 現在の TimelineAsset と Profile SO で Bake を焼き直し、全 Facial トラックへ同じ Bake 参照を書いて保存する
        /// （Editor の再ベイクと同じ経路: <see cref="TimelineBakeService.UpdateBakeAsset"/> + <see cref="BakeReferenceWriter.Apply"/>）。
        /// </summary>
        public void Rebake()
        {
            TimelineBakeService.UpdateBakeAsset(Timeline, ProfileAsset, Bake);
            BakeReferenceWriter.Apply(Timeline, Bake);
            EditorUtility.SetDirty(Timeline);
            EditorUtility.SetDirty(Bake);
            AssetDatabase.SaveAssetIfDirty(Bake);
            AssetDatabase.SaveAssetIfDirty(Timeline);
        }

        /// <summary>Profile SO の変更を保存する（Editor で Profile を編集して保存した状態を再現する）。</summary>
        public void SaveProfile()
        {
            EditorUtility.SetDirty(ProfileAsset);
            AssetDatabase.SaveAssetIfDirty(ProfileAsset);
            TimelineProfileSource.InvalidateCache(ProfileAsset);
        }

        /// <summary>
        /// キャラクターを配置して有効化する（FacialController は OnEnable で Profile SO から初期化される）。
        /// </summary>
        public TimelineE2ECharacter Spawn(TimelineE2EPlacement placement = TimelineE2EPlacement.SameObject)
        {
            var character = TimelineE2ECharacter.Create(placement, ProfileAsset, Timeline);
            _characters.Add(character);
            return character;
        }

        public void Dispose()
        {
            for (int i = _characters.Count - 1; i >= 0; i--)
            {
                _characters[i].Dispose();
            }

            _characters.Clear();

            string profileJsonPath = FacialCharacterProfileSO.GetStreamingAssetsProfilePath(ProfileAsset != null ? ProfileAsset.name : null);
            string profileJsonFolder = string.IsNullOrEmpty(profileJsonPath) ? null : Path.GetDirectoryName(profileJsonPath);

            if (ProfileAsset != null)
            {
                TimelineProfileSource.InvalidateCache(ProfileAsset);
            }

            AssetDatabase.DeleteAsset(_folderPath);
            if (Directory.Exists(_folderPath))
            {
                Directory.Delete(_folderPath, true);
            }

            if (File.Exists(_folderPath + ".meta"))
            {
                File.Delete(_folderPath + ".meta");
            }

            if (!string.IsNullOrEmpty(profileJsonFolder) && Directory.Exists(profileJsonFolder))
            {
                Directory.Delete(profileJsonFolder, true);
                if (File.Exists(profileJsonFolder + ".meta"))
                {
                    File.Delete(profileJsonFolder + ".meta");
                }
            }

            AssetDatabase.Refresh();
        }

        private static void ConfigureDefaultProfile(FacialCharacterProfileSO profileAsset)
        {
            profileAsset.Layers.Add(new LayerDefinitionSerializable
            {
                name = EmotionLayer,
                priority = 0,
                exclusionMode = ExclusionMode.LastWins,
                inputSources = new List<InputSourceDeclarationSerializable>
                {
                    // InputSystem の analog expression と同じ宣言（Timeline の宣言は書かない）。
                    new InputSourceDeclarationSerializable { id = "osc:analog-expression", weight = 1f },
                },
            });
            profileAsset.Layers.Add(new LayerDefinitionSerializable
            {
                name = OverlayLayer,
                priority = 1,
                exclusionMode = ExclusionMode.LastWins,
            });

            profileAsset.Expressions.Add(new ExpressionSerializable
            {
                id = SmileExpressionId,
                name = "Smile",
                layer = EmotionLayer,
                transitionDuration = 0.1f,
                blendShapeValues = new List<BlendShapeMappingSerializable>
                {
                    new BlendShapeMappingSerializable { name = SmileBlendShape, value = SmileExpressionValue },
                },
            });
            profileAsset.Expressions.Add(new ExpressionSerializable
            {
                id = SquintExpressionId,
                name = "Squint",
                layer = OverlayLayer,
                transitionDuration = 0.1f,
                blendShapeValues = new List<BlendShapeMappingSerializable>
                {
                    new BlendShapeMappingSerializable { name = SquintBlendShape, value = SquintExpressionValue },
                },
            });

            // GazeChannels は既定（"gaze" 1 本）のまま、目ボーンだけ明示する（Humanoid Avatar を使わない fixture のため）。
            GazeChannel gaze = profileAsset.GazeChannels[0];
            gaze.leftEyeBonePath = LeftEyeName;
            gaze.rightEyeBonePath = RightEyeName;
        }
    }

    /// <summary>
    /// e2e で配置したキャラクター。BlendShape と目ボーンの読み取り、Director の時刻評価を提供する。
    /// </summary>
    internal sealed class TimelineE2ECharacter : IDisposable
    {
        private readonly GameObject _top;
        private readonly Mesh _mesh;

        private TimelineE2ECharacter(
            GameObject top,
            GameObject root,
            FacialController controller,
            FacialTimelineReceiver receiver,
            PlayableDirector director,
            SkinnedMeshRenderer renderer,
            Transform leftEye,
            Transform rightEye,
            Mesh mesh)
        {
            _top = top;
            Root = root;
            Controller = controller;
            Receiver = receiver;
            Director = director;
            Renderer = renderer;
            LeftEye = leftEye;
            RightEye = rightEye;
            _mesh = mesh;
        }

        /// <summary>FacialController を持つ GameObject。</summary>
        public GameObject Root { get; }

        public FacialController Controller { get; }

        /// <summary>ユーザーが配置した Receiver（配置に応じて Root または子 GameObject 上）。</summary>
        public FacialTimelineReceiver Receiver { get; }

        public PlayableDirector Director { get; }

        public SkinnedMeshRenderer Renderer { get; }

        public Transform LeftEye { get; }

        public Transform RightEye { get; }

        public static TimelineE2ECharacter Create(
            TimelineE2EPlacement placement,
            FacialCharacterProfileSO profileAsset,
            TimelineAsset timeline)
        {
            GameObject stage = null;
            var root = new GameObject("TimelineE2ECharacter");
            root.SetActive(false);
            if (placement == TimelineE2EPlacement.DirectorOnParent)
            {
                stage = new GameObject("TimelineE2EStage");
                stage.SetActive(false);
                root.transform.SetParent(stage.transform, false);
            }

            root.AddComponent<Animator>();

            var face = new GameObject("Face");
            face.transform.SetParent(root.transform, false);
            var renderer = face.AddComponent<SkinnedMeshRenderer>();
            var mesh = new Mesh
            {
                name = "TimelineE2E_Mesh",
                vertices = new[] { Vector3.zero, Vector3.right, Vector3.up },
                triangles = new[] { 0, 1, 2 },
            };
            string[] blendShapeNames =
            {
                TimelineE2EFixture.SmileBlendShape,
                TimelineE2EFixture.SquintBlendShape,
                TimelineE2EFixture.BlinkBlendShape,
            };
            for (int i = 0; i < blendShapeNames.Length; i++)
            {
                mesh.AddBlendShapeFrame(blendShapeNames[i], 100f, new Vector3[3], null, null);
            }

            renderer.sharedMesh = mesh;

            Transform leftEye = CreateChild(root.transform, TimelineE2EFixture.LeftEyeName, new Vector3(-0.03f, 0f, 0f));
            Transform rightEye = CreateChild(root.transform, TimelineE2EFixture.RightEyeName, new Vector3(0.03f, 0f, 0f));

            var controller = root.AddComponent<FacialController>();
            controller.CharacterSO = profileAsset;

            GameObject directorHost;
            GameObject receiverHost;
            switch (placement)
            {
                case TimelineE2EPlacement.DirectorOnParent:
                    directorHost = stage;
                    receiverHost = root;
                    break;
                case TimelineE2EPlacement.ReceiverOnChild:
                    directorHost = new GameObject("TimelineHost");
                    directorHost.transform.SetParent(root.transform, false);
                    receiverHost = directorHost;
                    break;
                default:
                    directorHost = root;
                    receiverHost = root;
                    break;
            }

            var director = directorHost.AddComponent<PlayableDirector>();
            director.playOnAwake = false;
            director.timeUpdateMode = DirectorUpdateMode.Manual;
            director.extrapolationMode = DirectorWrapMode.Hold;
            director.playableAsset = timeline;

            var receiver = receiverHost.AddComponent<FacialTimelineReceiver>();

            root.SetActive(true);
            if (stage != null)
            {
                stage.SetActive(true);
            }

            return new TimelineE2ECharacter(
                stage != null ? stage : root,
                root,
                controller,
                receiver,
                director,
                renderer,
                leftEye,
                rightEye,
                mesh);
        }

        /// <summary>renderer の BlendShape weight（0..100）。</summary>
        public float GetBlendShapeWeight(string blendShapeName)
        {
            int index = Renderer.sharedMesh.GetBlendShapeIndex(blendShapeName);
            if (index < 0)
            {
                throw new ArgumentException($"BlendShape '{blendShapeName}' は fixture のメッシュに存在しません。", nameof(blendShapeName));
            }

            return Renderer.GetBlendShapeWeight(index);
        }

        /// <summary>
        /// Director（Manual 更新）を指定時刻で評価し、FacialController の LateUpdate が反映されるまで 1 フレーム待つ。
        /// </summary>
        public IEnumerator EvaluateAt(double timeSeconds)
        {
            if (!Director.playableGraph.IsValid() || Director.state != PlayState.Playing)
            {
                Director.Play();
            }

            Director.time = timeSeconds;
            Director.Evaluate();
            yield return null;
        }

        /// <summary>Director を停止する（再生セッション終了）。</summary>
        public IEnumerator StopDirector()
        {
            Director.Stop();
            yield return null;
        }

        public void Dispose()
        {
            if (_top != null)
            {
                UnityEngine.Object.DestroyImmediate(_top);
            }

            if (_mesh != null)
            {
                UnityEngine.Object.DestroyImmediate(_mesh);
            }
        }

        private static Transform CreateChild(Transform parent, string name, Vector3 localPosition)
        {
            var child = new GameObject(name).transform;
            child.SetParent(parent, false);
            child.localPosition = localPosition;
            child.localRotation = Quaternion.identity;
            return child;
        }
    }
}
#endif
