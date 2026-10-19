#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Hidano.FacialControl.Adapters.Json.Dto;
using Hidano.FacialControl.Adapters.Playable;
using Hidano.FacialControl.Adapters.ScriptableObject;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Rec.Adapters.Playable;
using Hidano.FacialControl.Rec.Adapters.Recording;
using Hidano.FacialControl.Rec.Application.UseCases;
using Hidano.FacialControl.Timeline.Adapters;
using Hidano.FacialControl.Timeline.Adapters.AdapterBindings;
using Hidano.FacialControl.Timeline.Clips;
using Hidano.FacialControl.Timeline.Editor;
using Hidano.FacialControl.Timeline.Tests.Shared;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Hidano.FacialControl.Timeline.Tests.PlayMode
{
    /// <summary>
    /// 往復検証（Medium）の fixture。シナリオの .fcrec を書き、Profile SO をアセット化して REC Export まで行い、
    /// REC 再生のキャラクターと Timeline 再生のキャラクターで毎フレームの出力を記録する。
    /// </summary>
    /// <remarks>
    /// <para>生成物は <c>Assets/RecEquivalence_{guid}/</c> と、REC 再生が読む StreamingAssets のサイドカー
    /// （<c>StreamingAssets/FacialControl/{Profile 名}/</c>）に置き、<see cref="Dispose"/> で消す。</para>
    /// <para>どちらのキャラクターも同じ Profile SO・同じメッシュ・同じ目ボーンで作る。REC 再生側には Director / Receiver を置かず、
    /// Timeline 側には <see cref="RecCharacterBinding"/> を置かない。</para>
    /// </remarks>
    internal sealed class RecTimelineEquivalenceFixture : IDisposable
    {
        public const string RecordingName = "equivalence";

        /// <summary>1 フレームの dt の下限（フレーム数の上限の見積もりに使う）。</summary>
        private const double MinimumFrameSeconds = 1d / 144d;

        private readonly string _folderPath;
        private readonly string _sidecarCharacterFolder;
        private readonly List<EquivalenceCharacter> _characters = new List<EquivalenceCharacter>();

        private RecTimelineEquivalenceFixture(
            string folderPath,
            string sidecarCharacterFolder,
            RecEquivalenceScenario scenario,
            FacialCharacterProfileSO profileAsset,
            TimelineAsset timeline)
        {
            _folderPath = folderPath;
            _sidecarCharacterFolder = sidecarCharacterFolder;
            Scenario = scenario;
            ProfileAsset = profileAsset;
            Timeline = timeline;
        }

        public RecEquivalenceScenario Scenario { get; }

        public FacialCharacterProfileSO ProfileAsset { get; }

        public TimelineAsset Timeline { get; }

        public static IReadOnlyList<EquivalenceChannel> Channels()
        {
            var channels = new List<EquivalenceChannel>();
            for (int i = 0; i < RecEquivalenceSpec.MeshBlendShapes.Length; i++)
            {
                channels.Add(new EquivalenceChannel(RecEquivalenceSpec.MeshBlendShapes[i], EquivalenceChannelKind.BlendShape));
            }

            channels.Add(new EquivalenceChannel(RecEquivalenceSpec.LeftEyeName, EquivalenceChannelKind.BoneRotation));
            channels.Add(new EquivalenceChannel(RecEquivalenceSpec.RightEyeName, EquivalenceChannelKind.BoneRotation));
            return channels;
        }

        public static RecTimelineEquivalenceFixture Create(RecEquivalenceScenario scenario)
        {
            if (scenario == null)
            {
                throw new ArgumentNullException(nameof(scenario));
            }

            string guid = Guid.NewGuid().ToString("N");
            string folderName = "RecEquivalence_" + guid;
            AssetDatabase.CreateFolder("Assets", folderName);
            string folderPath = "Assets/" + folderName;

            var profileAsset = ScriptableObject.CreateInstance<FacialCharacterProfileSO>();
            profileAsset.name = "RecEquivalenceProfile_" + guid;
            ConfigureProfile(profileAsset);
            AssetDatabase.CreateAsset(profileAsset, folderPath + "/" + profileAsset.name + ".asset");

            string recordingPath = Path.GetFullPath(folderPath + "/recording.fcrec");
            RecFixtureWriter.Write(recordingPath, scenario.Timeline);

            if (!RecSidecarPath.TryBuildRecordingFilePath(profileAsset.CharacterAssetName, RecordingName, out string sidecarPath, out string error))
            {
                AssetDatabase.DeleteAsset(folderPath);
                throw new InvalidOperationException("fixture: REC のサイドカーのパスを作れません: " + error);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(sidecarPath));
            File.Copy(recordingPath, sidecarPath, true);
            string sidecarCharacterFolder = Path.GetDirectoryName(Path.GetDirectoryName(sidecarPath));
            AssetDatabase.Refresh();

            if (!RecToTimelineExporter.TryExportTimelineAsset(
                    recordingPath,
                    profileAsset,
                    folderPath + "/Exported.playable",
                    out RecToTimelineExporter.ExportResult result))
            {
                DeleteSidecar(sidecarCharacterFolder);
                AssetDatabase.DeleteAsset(folderPath);
                throw new InvalidOperationException("fixture: REC Export に失敗しました。");
            }

            return new RecTimelineEquivalenceFixture(folderPath, sidecarCharacterFolder, scenario, profileAsset, result.Timeline);
        }

        /// <summary>
        /// REC 再生のキャラクターで毎フレームの出力を記録する。フレーム k の値は、そのフレームの LateUpdate が書いた値で、
        /// 時刻は同じフレームの Update で REC 再生が進めた経過時間。
        /// </summary>
        public IEnumerator CaptureRecPlayback(RecEquivalenceFrameStepMode stepMode, EquivalenceFrameTrace trace)
        {
            EquivalenceCharacter character = Spawn(withRecBinding: true, withTimeline: false);
            var step = new RecEquivalenceFrameStep(stepMode);
            Time.captureDeltaTime = step.NextDelta();
            yield return null;
            yield return null;

            RecCharacterBinding binding = character.RecBinding;
            if (!binding.LoadRecording(RecordingName))
            {
                throw new InvalidOperationException("fixture: REC の読み込みに失敗しました（" + RecordingName + "）。");
            }

            if (!binding.StartPlayback())
            {
                throw new InvalidOperationException("fixture: REC 再生の開始に失敗しました。");
            }

            int maxFrames = MaxFrames();
            double pendingTime = binding.ElapsedSeconds;
            bool completed = false;
            var values = new float[trace.Stride];
            for (int frame = 0; ; frame++)
            {
                if (frame > maxFrames)
                {
                    throw new InvalidOperationException($"fixture: REC 再生が {maxFrames} フレームで終わりません（state={binding.PlaybackState}）。");
                }

                Time.captureDeltaTime = step.NextDelta();

                // コルーチンは全 Update の後に再開する。再開時点のメッシュは直前のフレームの LateUpdate が書いた値で、
                // その時刻は直前のフレームで読んだ経過時間（pendingTime）。
                yield return null;
                character.Read(values);
                trace.AddFrame(pendingTime, values);
                if (completed)
                {
                    break;
                }

                pendingTime = binding.ElapsedSeconds;
                completed = binding.PlaybackState == RecPlaybackState.Completed;
            }

            binding.StopPlayback();
            DestroyCharacter(character);
        }

        /// <summary>
        /// Timeline 再生のキャラクターで、<paramref name="times"/> の各時刻に Director を評価して出力を記録する。
        /// <paramref name="frameShift"/> を 1 にすると、各フレームで 1 フレーム先の時刻を評価する（わざと壊した入力の検証用）。
        /// </summary>
        public IEnumerator CaptureTimeline(IReadOnlyList<double> times, EquivalenceFrameTrace trace, int frameShift = 0)
        {
            EquivalenceCharacter character = Spawn(withRecBinding: false, withTimeline: true);
            Time.captureDeltaTime = 1f / 60f;
            yield return null;
            yield return null;

            var values = new float[trace.Stride];
            double previous = 0d;
            for (int frame = 0; frame < times.Count; frame++)
            {
                double time = times[frame];
                double evaluated = times[Math.Min(times.Count - 1, frame + frameShift)];
                float delta = (float)(time - previous);
                Time.captureDeltaTime = delta > 0f ? delta : 1f / 60f;
                previous = time;

                PlayableDirector director = character.Director;
                if (!director.playableGraph.IsValid() || director.state != PlayState.Playing)
                {
                    director.Play();
                }

                director.time = evaluated;
                director.Evaluate();

                // Evaluate で入った値を、このフレームの LateUpdate が書く。再開時点で読む。
                yield return null;
                character.Read(values);
                trace.AddFrame(time, values);
            }

            character.Director.Stop();
            yield return null;
            DestroyCharacter(character);
        }

        /// <summary>Timeline のトラックと Clip（レポートの手がかり）。</summary>
        public IReadOnlyList<string> DescribeTimeline()
        {
            var lines = new List<string>();
            foreach (TrackAsset track in Timeline.GetOutputTracks())
            {
                foreach (TimelineClip clip in track.GetClips())
                {
                    string label = clip.asset is FacialExpressionClip expressionClip
                        ? expressionClip.ExpressionId
                        : clip.asset != null ? clip.asset.GetType().Name : "(null)";
                    lines.Add($"{track.name}: [{clip.start:0.00000}, {clip.end:0.00000}] {label}");
                }
            }

            return lines;
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

            DeleteSidecar(_sidecarCharacterFolder);
            if (!string.IsNullOrEmpty(profileJsonFolder) && profileJsonFolder != _sidecarCharacterFolder)
            {
                DeleteSidecar(profileJsonFolder);
            }

            AssetDatabase.Refresh();
        }

        private int MaxFrames()
        {
            return (int)Math.Ceiling(Scenario.DurationSeconds / MinimumFrameSeconds) + 30;
        }

        private EquivalenceCharacter Spawn(bool withRecBinding, bool withTimeline)
        {
            var character = EquivalenceCharacter.Create(ProfileAsset, withRecBinding ? null : Timeline, withRecBinding);
            _characters.Add(character);
            return character;
        }

        private void DestroyCharacter(EquivalenceCharacter character)
        {
            _characters.Remove(character);
            character.Dispose();
        }

        private static void DeleteSidecar(string folder)
        {
            if (string.IsNullOrEmpty(folder))
            {
                return;
            }

            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }

            if (File.Exists(folder + ".meta"))
            {
                File.Delete(folder + ".meta");
            }
        }

        private static void ConfigureProfile(FacialCharacterProfileSO profileAsset)
        {
            profileAsset.Layers.Add(new LayerDefinitionSerializable
            {
                name = RecEquivalenceSpec.EmotionLayer,
                priority = 0,
                exclusionMode = ExclusionMode.LastWins,
                inputSources = new List<InputSourceDeclarationSerializable>
                {
                    new InputSourceDeclarationSerializable { id = RecEquivalenceSpec.TriggerSourceId, weight = 1f },
                },
            });
            profileAsset.Layers.Add(new LayerDefinitionSerializable
            {
                name = RecEquivalenceSpec.OverlayLayer,
                priority = 1,
                exclusionMode = ExclusionMode.LastWins,
                inputSources = new List<InputSourceDeclarationSerializable>
                {
                    new InputSourceDeclarationSerializable { id = RecEquivalenceSpec.AnalogExpressionSourceId, weight = 1f },
                },
            });
            profileAsset.Layers.Add(new LayerDefinitionSerializable
            {
                name = RecEquivalenceSpec.FaceLayer,
                priority = 2,
                exclusionMode = ExclusionMode.LastWins,
                inputSources = new List<InputSourceDeclarationSerializable>
                {
                    new InputSourceDeclarationSerializable { id = RecEquivalenceSpec.ValueProviderSourceId, weight = 1f },
                },
            });

            for (int i = 0; i < RecEquivalenceSpec.Expressions.Count; i++)
            {
                profileAsset.Expressions.Add(CreateExpression(RecEquivalenceSpec.Expressions[i]));
            }

            GazeChannel gaze = profileAsset.GazeChannels[0];
            gaze.leftEyeBonePath = RecEquivalenceSpec.LeftEyeName;
            gaze.rightEyeBonePath = RecEquivalenceSpec.RightEyeName;

            List<AdapterBindingBase> bindings = TimelineE2EFixture.GetWritableAdapterBindings(profileAsset);
            bindings.Add(new TimelineAdapterBinding());
            bindings.Add(new FakeAnalogAdapterBinding { AnalogExpressionId = RecEquivalenceSpec.SquintExpressionId });
            bindings.Add(new FakeValueProviderAdapterBinding());
            bindings.Add(new FakeTriggerAdapterBinding());
        }

        /// <summary>
        /// Expression を AnimationClip サンプリング済みの形（cachedSnapshot）で作る（<see cref="TimelineE2EFixture"/> と同じ理由）。
        /// </summary>
        private static ExpressionSerializable CreateExpression(RecEquivalenceSpec.ExpressionSpec spec)
        {
            string preset = RecEquivalenceSpec.CurvePresetName(spec.Curve);
            return new ExpressionSerializable
            {
                id = spec.Id,
                name = spec.Id,
                layer = spec.Layer,
                transitionDuration = spec.TransitionDuration,
                transitionCurve = new TransitionCurveSerializable { type = spec.Curve },
                cachedSnapshot = new ExpressionSnapshotDto
                {
                    transitionDuration = spec.TransitionDuration,
                    transitionCurvePreset = preset,
                    blendShapes = new List<BlendShapeSnapshotDto>
                    {
                        new BlendShapeSnapshotDto { rendererPath = string.Empty, name = spec.BlendShape, value = spec.Value },
                    },
                },
            };
        }
    }

    /// <summary>往復検証のキャラクター（BlendShape 付きメッシュ + 左右の目ボーン + FacialController）。</summary>
    internal sealed class EquivalenceCharacter : IDisposable
    {
        private readonly GameObject _root;
        private readonly Mesh _mesh;

        private EquivalenceCharacter(
            GameObject root,
            Mesh mesh,
            SkinnedMeshRenderer renderer,
            Transform leftEye,
            Transform rightEye,
            FacialController controller,
            RecCharacterBinding recBinding,
            PlayableDirector director)
        {
            _root = root;
            _mesh = mesh;
            Renderer = renderer;
            LeftEye = leftEye;
            RightEye = rightEye;
            Controller = controller;
            RecBinding = recBinding;
            Director = director;
        }

        public SkinnedMeshRenderer Renderer { get; }

        public Transform LeftEye { get; }

        public Transform RightEye { get; }

        public FacialController Controller { get; }

        public RecCharacterBinding RecBinding { get; }

        public PlayableDirector Director { get; }

        public static EquivalenceCharacter Create(FacialCharacterProfileSO profileAsset, TimelineAsset timeline, bool withRecBinding)
        {
            var root = new GameObject("RecEquivalenceCharacter");
            root.SetActive(false);
            root.AddComponent<Animator>();

            var face = new GameObject("Face");
            face.transform.SetParent(root.transform, false);
            var renderer = face.AddComponent<SkinnedMeshRenderer>();
            Mesh mesh = CreateMesh();
            renderer.sharedMesh = mesh;

            Transform leftEye = CreateChild(root.transform, RecEquivalenceSpec.LeftEyeName, new Vector3(-0.03f, 0f, 0f));
            Transform rightEye = CreateChild(root.transform, RecEquivalenceSpec.RightEyeName, new Vector3(0.03f, 0f, 0f));

            var controller = root.AddComponent<FacialController>();
            controller.CharacterSO = profileAsset;

            RecCharacterBinding recBinding = null;
            if (withRecBinding)
            {
                recBinding = root.AddComponent<RecCharacterBinding>();
                recBinding.FacialController = controller;
            }

            PlayableDirector director = null;
            if (timeline != null)
            {
                director = root.AddComponent<PlayableDirector>();
                director.playOnAwake = false;
                director.timeUpdateMode = DirectorUpdateMode.Manual;
                director.extrapolationMode = DirectorWrapMode.Hold;
                director.playableAsset = timeline;
                root.AddComponent<FacialTimelineReceiver>();
            }

            root.SetActive(true);
            return new EquivalenceCharacter(root, mesh, renderer, leftEye, rightEye, controller, recBinding, director);
        }

        /// <summary>全 BlendShape の weight と左右の目ボーンの localRotation（x, y, z, w）をチャンネル順に書く。</summary>
        public void Read(float[] values)
        {
            int index = 0;
            for (int i = 0; i < RecEquivalenceSpec.MeshBlendShapes.Length; i++)
            {
                values[index++] = Renderer.GetBlendShapeWeight(i);
            }

            index = WriteRotation(values, index, LeftEye.localRotation);
            WriteRotation(values, index, RightEye.localRotation);
        }

        public void Dispose()
        {
            if (_root != null)
            {
                UnityEngine.Object.DestroyImmediate(_root);
            }

            if (_mesh != null)
            {
                UnityEngine.Object.DestroyImmediate(_mesh);
            }
        }

        private static int WriteRotation(float[] values, int index, Quaternion rotation)
        {
            values[index++] = rotation.x;
            values[index++] = rotation.y;
            values[index++] = rotation.z;
            values[index++] = rotation.w;
            return index;
        }

        private static Mesh CreateMesh()
        {
            var mesh = new Mesh
            {
                name = "RecEquivalence_Mesh",
                vertices = new[] { Vector3.zero, Vector3.right, Vector3.up },
                triangles = new[] { 0, 1, 2 },
            };
            for (int i = 0; i < RecEquivalenceSpec.MeshBlendShapes.Length; i++)
            {
                mesh.AddBlendShapeFrame(RecEquivalenceSpec.MeshBlendShapes[i], 100f, new Vector3[3], null, null);
            }

            return mesh;
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
