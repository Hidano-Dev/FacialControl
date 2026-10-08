using System;
using System.Collections.Generic;
using System.Text;
using Hidano.FacialControl.Adapters.ScriptableObject;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Timeline.Clips;
using Hidano.FacialControl.Timeline.Tracks;
using UnityEngine;
using UnityEngine.Timeline;

namespace Hidano.FacialControl.Timeline.Domain.Services
{
    public static class FacialTimelineHashCalculator
    {
        public const float DefaultSampleRate = 60f;

        private const ulong FnvOffsetBasis = 14695981039346656037UL;
        private const ulong FnvPrime = 1099511628211UL;

        /// <summary>
        /// Profile スナップショットの内容ハッシュ。Timeline 構造には依存しない。
        /// </summary>
        /// <remarks>
        /// 対象: SchemaVersion / Layers（順序込み）/ LayerInputSources（id と weight、レイヤー順）/
        /// Expressions（id 順ソート）/ Slots / DefaultOverlays / BaseExpression /
        /// GazeChannels（id / sourceIdLeft / sourceIdRight / 目ボーンパス、順序込み）。
        /// </remarks>
        public static ulong ComputeProfileContentHash(in FacialProfile profile, ReadOnlySpan<GazeChannel> gazeChannels)
        {
            Fnv1A64Writer writer = Fnv1A64Writer.Create();
            writer.WriteString("facial-profile-content-hash/v1");

            WriteProfile(ref writer, profile);
            WriteGazeChannels(ref writer, gazeChannels);

            return writer.Hash;
        }

        public static string ComputeProfileContentHashHex(in FacialProfile profile, ReadOnlySpan<GazeChannel> gazeChannels)
        {
            return ComputeProfileContentHash(profile, gazeChannels).ToString("x16");
        }

        /// <summary>
        /// Bake の Source ハッシュ（Timeline 構造 + Profile 内容ハッシュ + sampleRate）。
        /// </summary>
        public static ulong ComputeHash(
            TimelineAsset timeline,
            in FacialProfile profile,
            ReadOnlySpan<GazeChannel> gazeChannels,
            float sampleRate = DefaultSampleRate)
        {
            if (timeline == null)
            {
                throw new ArgumentNullException(nameof(timeline));
            }

            Fnv1A64Writer writer = Fnv1A64Writer.Create();
            writer.WriteString("facial-timeline-hash/v2");

            WriteTimeline(ref writer, timeline);
            writer.WriteUInt64(ComputeProfileContentHash(profile, gazeChannels));
            writer.WriteSingle(sampleRate);

            return writer.Hash;
        }

        public static string ComputeHashHex(
            TimelineAsset timeline,
            in FacialProfile profile,
            ReadOnlySpan<GazeChannel> gazeChannels,
            float sampleRate = DefaultSampleRate)
        {
            return ComputeHash(timeline, profile, gazeChannels, sampleRate).ToString("x16");
        }

        /// <summary>
        /// GazeChannels を持たない（空として扱う）Source ハッシュ。
        /// </summary>
        public static ulong ComputeHash(
            TimelineAsset timeline,
            FacialProfile profile,
            float sampleRate = DefaultSampleRate)
        {
            return ComputeHash(timeline, profile, ReadOnlySpan<GazeChannel>.Empty, sampleRate);
        }

        /// <summary>
        /// GazeChannels を持たない（空として扱う）Source ハッシュの hex。
        /// </summary>
        public static string ComputeHashHex(
            TimelineAsset timeline,
            FacialProfile profile,
            float sampleRate = DefaultSampleRate)
        {
            return ComputeHash(timeline, profile, sampleRate).ToString("x16");
        }

        /// <summary>
        /// GazeChannel の読み取り専用リストを配列へ写す（null は空配列）。呼び出し側が span へ渡すための補助。
        /// </summary>
        public static GazeChannel[] ToGazeChannelArray(IReadOnlyList<GazeChannel> gazeChannels)
        {
            if (gazeChannels == null || gazeChannels.Count == 0)
            {
                return Array.Empty<GazeChannel>();
            }

            var copy = new GazeChannel[gazeChannels.Count];
            for (int i = 0; i < copy.Length; i++)
            {
                copy[i] = gazeChannels[i];
            }

            return copy;
        }

        private static void WriteTimeline(ref Fnv1A64Writer writer, TimelineAsset timeline)
        {
            writer.WriteString("timeline");

            foreach (TrackAsset rootTrack in timeline.GetOutputTracks())
            {
                WriteTrackRecursive(ref writer, rootTrack, depth: 0);
            }
        }

        private static void WriteTrackRecursive(ref Fnv1A64Writer writer, TrackAsset track, int depth)
        {
            if (track == null)
            {
                return;
            }

            if (track is FacialExpressionTrack expressionTrack)
            {
                WriteExpressionTrack(ref writer, expressionTrack, depth);
            }
            else if (track is FacialValueTrack valueTrack)
            {
                WriteValueTrack(ref writer, valueTrack, depth);
            }

            foreach (TrackAsset childTrack in track.GetChildTracks())
            {
                WriteTrackRecursive(ref writer, childTrack, depth + 1);
            }
        }

        private static void WriteExpressionTrack(
            ref Fnv1A64Writer writer,
            FacialExpressionTrack track,
            int depth)
        {
            writer.WriteString("expression-track");
            writer.WriteInt32(depth);
            writer.WriteString(track.name);

            TimelineClip[] clips = CopyClips(track);
            writer.WriteInt32(clips.Length);

            for (int i = 0; i < clips.Length; i++)
            {
                TimelineClip clip = clips[i];
                var asset = clip.asset as FacialExpressionClip;

                writer.WriteString("expression-clip");
                writer.WriteDouble(clip.start);
                writer.WriteDouble(clip.duration);
                writer.WriteString(asset != null ? asset.ExpressionId : string.Empty);
            }
        }

        private static void WriteValueTrack(
            ref Fnv1A64Writer writer,
            FacialValueTrack track,
            int depth)
        {
            writer.WriteString("value-track");
            writer.WriteInt32(depth);
            writer.WriteString(track.ChannelSubId);
            writer.WriteInt32((int)track.ChannelKind);

            TimelineClip[] clips = CopyClips(track);
            writer.WriteInt32(clips.Length);

            for (int i = 0; i < clips.Length; i++)
            {
                TimelineClip clip = clips[i];
                var asset = clip.asset as FacialValueClip;
                AnimationCurve[] axes = asset != null ? asset.Axes : Array.Empty<AnimationCurve>();

                writer.WriteString("value-clip");
                writer.WriteDouble(clip.start);
                writer.WriteDouble(clip.duration);
                writer.WriteInt32(axes.Length);

                for (int axisIndex = 0; axisIndex < axes.Length; axisIndex++)
                {
                    WriteCurve(ref writer, axes[axisIndex]);
                }

                if (asset != null)
                {
                    WriteValueProviderClipData(ref writer, asset);
                }
            }
        }

        /// <summary>
        /// 値提供型の Clip データ（BlendShape 対応・寄与 mask・有効状態）を書く。どれも持たない Clip（Analog / Gaze と
        /// 本データ導入前の Clip）は何も書かず、既存 Timeline の Source ハッシュを変えない。
        /// </summary>
        private static void WriteValueProviderClipData(ref Fnv1A64Writer writer, FacialValueClip asset)
        {
            string[] names = asset.BlendShapeNames ?? Array.Empty<string>();
            int[] indices = asset.BlendShapeIndices ?? Array.Empty<int>();
            AnimationCurve[] contributes = asset.Contributes ?? Array.Empty<AnimationCurve>();
            AnimationCurve validity = asset.Validity;
            bool hasValidity = validity != null && validity.length > 0;
            if (names.Length == 0 && indices.Length == 0 && contributes.Length == 0 && !hasValidity)
            {
                return;
            }

            writer.WriteString("value-provider");
            writer.WriteInt32(names.Length);
            for (int i = 0; i < names.Length; i++)
            {
                writer.WriteString(names[i] ?? string.Empty);
            }

            writer.WriteInt32(indices.Length);
            for (int i = 0; i < indices.Length; i++)
            {
                writer.WriteInt32(indices[i]);
            }

            writer.WriteInt32(contributes.Length);
            for (int i = 0; i < contributes.Length; i++)
            {
                WriteCurve(ref writer, contributes[i]);
            }

            WriteCurve(ref writer, hasValidity ? validity : null);
        }

        private static void WriteProfile(ref Fnv1A64Writer writer, in FacialProfile profile)
        {
            writer.WriteString("profile");
            writer.WriteString(profile.SchemaVersion);

            WriteLayers(ref writer, profile);
            WriteExpressions(ref writer, profile);

            ReadOnlySpan<string> slots = profile.Slots.Span;
            writer.WriteString("slots");
            writer.WriteInt32(slots.Length);
            for (int i = 0; i < slots.Length; i++)
            {
                writer.WriteString(slots[i]);
            }

            writer.WriteString("default-overlays");
            WriteOverlays(ref writer, profile.DefaultOverlays.Span);

            ReadOnlySpan<BlendShapeSnapshot> baseExpression = profile.BaseExpression.Span;
            writer.WriteString("base-expression");
            writer.WriteInt32(baseExpression.Length);
            for (int i = 0; i < baseExpression.Length; i++)
            {
                WriteBlendShapeSnapshot(ref writer, baseExpression[i]);
            }
        }

        private static void WriteLayers(ref Fnv1A64Writer writer, in FacialProfile profile)
        {
            ReadOnlySpan<LayerDefinition> layers = profile.Layers.Span;
            writer.WriteString("layers");
            writer.WriteInt32(layers.Length);
            for (int i = 0; i < layers.Length; i++)
            {
                writer.WriteString(layers[i].Name);
                writer.WriteInt32(layers[i].Priority);
                writer.WriteInt32((int)layers[i].ExclusionMode);
            }

            ReadOnlySpan<InputSourceDeclaration[]> layerInputSources = profile.LayerInputSources.Span;
            writer.WriteString("layer-input-sources");
            writer.WriteInt32(layerInputSources.Length);
            for (int layerIndex = 0; layerIndex < layerInputSources.Length; layerIndex++)
            {
                InputSourceDeclaration[] declarations = layerInputSources[layerIndex] ?? Array.Empty<InputSourceDeclaration>();
                writer.WriteInt32(declarations.Length);
                for (int i = 0; i < declarations.Length; i++)
                {
                    writer.WriteString(declarations[i].Id);
                    writer.WriteSingle(declarations[i].Weight);
                }
            }
        }

        private static void WriteOverlays(ref Fnv1A64Writer writer, ReadOnlySpan<OverlaySlotBinding> overlays)
        {
            writer.WriteInt32(overlays.Length);
            for (int i = 0; i < overlays.Length; i++)
            {
                OverlaySlotBinding overlay = overlays[i];
                writer.WriteString(overlay.Slot);
                writer.WriteBoolean(overlay.Suppress);
                writer.WriteBoolean(overlay.Snapshot.HasValue);
                if (overlay.Snapshot.HasValue)
                {
                    WriteExpressionSnapshot(ref writer, overlay.Snapshot.Value);
                }
            }
        }

        private static void WriteExpressionSnapshot(ref Fnv1A64Writer writer, ExpressionSnapshot snapshot)
        {
            writer.WriteString(snapshot.Id);
            writer.WriteSingle(snapshot.TransitionDuration);
            writer.WriteInt32((int)snapshot.TransitionCurvePreset);

            ReadOnlySpan<BlendShapeSnapshot> blendShapes = snapshot.BlendShapes.Span;
            writer.WriteInt32(blendShapes.Length);
            for (int i = 0; i < blendShapes.Length; i++)
            {
                WriteBlendShapeSnapshot(ref writer, blendShapes[i]);
            }

            ReadOnlySpan<BoneSnapshot> bones = snapshot.Bones.Span;
            writer.WriteInt32(bones.Length);
            for (int i = 0; i < bones.Length; i++)
            {
                BoneSnapshot bone = bones[i];
                writer.WriteString(bone.BonePath);
                writer.WriteSingle(bone.PositionX);
                writer.WriteSingle(bone.PositionY);
                writer.WriteSingle(bone.PositionZ);
                writer.WriteSingle(bone.EulerX);
                writer.WriteSingle(bone.EulerY);
                writer.WriteSingle(bone.EulerZ);
                writer.WriteSingle(bone.ScaleX);
                writer.WriteSingle(bone.ScaleY);
                writer.WriteSingle(bone.ScaleZ);
            }

            ReadOnlySpan<string> rendererPaths = snapshot.RendererPaths.Span;
            writer.WriteInt32(rendererPaths.Length);
            for (int i = 0; i < rendererPaths.Length; i++)
            {
                writer.WriteString(rendererPaths[i]);
            }
        }

        private static void WriteBlendShapeSnapshot(ref Fnv1A64Writer writer, BlendShapeSnapshot snapshot)
        {
            writer.WriteString(snapshot.RendererPath);
            writer.WriteString(snapshot.Name);
            writer.WriteSingle(snapshot.Value);
        }

        private static void WriteGazeChannels(ref Fnv1A64Writer writer, ReadOnlySpan<GazeChannel> gazeChannels)
        {
            writer.WriteString("gaze-channels");
            writer.WriteInt32(gazeChannels.Length);
            for (int i = 0; i < gazeChannels.Length; i++)
            {
                GazeChannel channel = gazeChannels[i];
                writer.WriteBoolean(channel != null);
                if (channel == null)
                {
                    continue;
                }

                writer.WriteString(channel.id);
                writer.WriteString(channel.sourceIdLeft);
                writer.WriteString(channel.sourceIdRight);
                writer.WriteString(channel.leftEyeBonePath);
                writer.WriteString(channel.rightEyeBonePath);
            }
        }

        private static void WriteExpressions(ref Fnv1A64Writer writer, in FacialProfile profile)
        {
            writer.WriteString("expressions");

            Expression[] expressions = Copy(profile.Expressions);
            Array.Sort(expressions, CompareExpressionById);

            writer.WriteInt32(expressions.Length);

            for (int i = 0; i < expressions.Length; i++)
            {
                Expression expression = expressions[i];
                writer.WriteString(expression.Id);
                writer.WriteString(expression.Layer);
                writer.WriteInt32((int)expression.OverrideMask);
                writer.WriteString(expression.SnapshotId);
                writer.WriteSingle(expression.TransitionDuration);

                TransitionCurve transitionCurve = expression.TransitionCurve;
                writer.WriteInt32((int)transitionCurve.Type);
                CurveKeyFrame[] transitionKeys = Copy(transitionCurve.Keys);
                writer.WriteInt32(transitionKeys.Length);
                for (int keyIndex = 0; keyIndex < transitionKeys.Length; keyIndex++)
                {
                    CurveKeyFrame key = transitionKeys[keyIndex];
                    writer.WriteSingle(key.Time);
                    writer.WriteSingle(key.Value);
                    writer.WriteSingle(key.InTangent);
                    writer.WriteSingle(key.OutTangent);
                }

                BlendShapeMapping[] mappings = Copy(expression.BlendShapeValues);
                writer.WriteInt32(mappings.Length);
                for (int mappingIndex = 0; mappingIndex < mappings.Length; mappingIndex++)
                {
                    BlendShapeMapping mapping = mappings[mappingIndex];
                    writer.WriteString(mapping.Name);
                    writer.WriteSingle(mapping.Value);
                    writer.WriteString(mapping.Renderer);
                }

                WriteOverlays(ref writer, expression.Overlays.Span);
            }
        }

        private static int CompareExpressionById(Expression left, Expression right)
        {
            return StringComparer.Ordinal.Compare(left.Id, right.Id);
        }

        private static TimelineClip[] CopyClips(TrackAsset track)
        {
            int count = 0;
            foreach (TimelineClip _ in track.GetClips())
            {
                count++;
            }

            if (count == 0)
            {
                return Array.Empty<TimelineClip>();
            }

            var clips = new TimelineClip[count];
            int index = 0;
            foreach (TimelineClip clip in track.GetClips())
            {
                clips[index++] = clip;
            }

            return clips;
        }

        private static T[] Copy<T>(ReadOnlyMemory<T> memory)
        {
            if (memory.Length == 0)
            {
                return Array.Empty<T>();
            }

            var copy = new T[memory.Length];
            memory.Span.CopyTo(copy);
            return copy;
        }

        private static void WriteCurve(ref Fnv1A64Writer writer, AnimationCurve curve)
        {
            writer.WriteBoolean(curve != null);
            if (curve == null)
            {
                return;
            }

            writer.WriteInt32((int)curve.preWrapMode);
            writer.WriteInt32((int)curve.postWrapMode);

            Keyframe[] keys = curve.keys;
            writer.WriteInt32(keys.Length);
            for (int keyIndex = 0; keyIndex < keys.Length; keyIndex++)
            {
                Keyframe key = keys[keyIndex];
                writer.WriteSingle(key.time);
                writer.WriteSingle(key.value);
                writer.WriteSingle(key.inTangent);
                writer.WriteSingle(key.outTangent);
                writer.WriteSingle(key.inWeight);
                writer.WriteSingle(key.outWeight);
                writer.WriteInt32((int)key.weightedMode);
            }
        }

        private struct Fnv1A64Writer
        {
            private static readonly byte[] EmptyStringBytes = Array.Empty<byte>();

            public ulong Hash;

            public static Fnv1A64Writer Create()
            {
                return new Fnv1A64Writer
                {
                    Hash = FnvOffsetBasis,
                };
            }

            public void WriteBoolean(bool value)
            {
                WriteByte(value ? (byte)1 : (byte)0);
            }

            public void WriteByte(byte value)
            {
                Hash ^= value;
                Hash *= FnvPrime;
            }

            public void WriteInt32(int value)
            {
                WriteBytes(BitConverter.GetBytes(value));
            }

            public void WriteUInt64(ulong value)
            {
                WriteBytes(BitConverter.GetBytes(value));
            }

            public void WriteSingle(float value)
            {
                WriteInt32(BitConverter.SingleToInt32Bits(value));
            }

            public void WriteDouble(double value)
            {
                WriteBytes(BitConverter.GetBytes(value));
            }

            public void WriteString(string value)
            {
                if (value == null)
                {
                    WriteBoolean(false);
                    return;
                }

                WriteBoolean(true);
                byte[] bytes = value.Length == 0 ? EmptyStringBytes : Encoding.UTF8.GetBytes(value);
                WriteInt32(bytes.Length);
                WriteBytes(bytes);
            }

            private void WriteBytes(byte[] bytes)
            {
                for (int i = 0; i < bytes.Length; i++)
                {
                    WriteByte(bytes[i]);
                }
            }
        }
    }
}
