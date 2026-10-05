using System;
using System.Collections.Generic;
using System.Text;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Timeline.Clips;
using Hidano.FacialControl.Timeline.Tracks;
using UnityEngine;
using UnityEngine.Timeline;

namespace Hidano.FacialControl.Timeline.Editor.Validation
{
    public enum FacialTimelineIssueKind
    {
        MissingExpressionId = 0,
        GazeOutOfRange = 1,
        EmptyClip = 2,
        EmptyParentTrack = 3,
    }

    public readonly struct FacialTimelineValidationIssue
    {
        internal FacialTimelineValidationIssue(
            FacialTimelineIssueKind kind,
            string trackName,
            string clipName,
            double timeSeconds,
            string message,
            TrackAsset track,
            TimelineClip clip)
        {
            Kind = kind;
            TrackName = trackName ?? string.Empty;
            ClipName = clipName ?? string.Empty;
            TimeSeconds = timeSeconds;
            Message = message ?? string.Empty;
            Track = track;
            Clip = clip;
        }

        public FacialTimelineIssueKind Kind { get; }

        public string TrackName { get; }

        public string ClipName { get; }

        public double TimeSeconds { get; }

        public string Message { get; }

        internal TrackAsset Track { get; }

        internal TimelineClip Clip { get; }
    }

    public sealed class FacialTimelineValidationReport
    {
        private readonly FacialTimelineValidationIssue[] _issues;

        internal FacialTimelineValidationReport(FacialTimelineValidationIssue[] issues)
        {
            _issues = issues ?? Array.Empty<FacialTimelineValidationIssue>();
        }

        public IReadOnlyList<FacialTimelineValidationIssue> Issues => _issues;

        public bool HasIssues => _issues.Length > 0;

        public string GetTrackMessage(TrackAsset track)
        {
            return BuildMessage(track, null);
        }

        public string GetClipMessage(TimelineClip clip)
        {
            return BuildMessage(null, clip);
        }

        private string BuildMessage(TrackAsset track, TimelineClip clip)
        {
            if (_issues.Length == 0)
            {
                return string.Empty;
            }

            var builder = new StringBuilder();
            for (int i = 0; i < _issues.Length; i++)
            {
                FacialTimelineValidationIssue issue = _issues[i];
                if (!ReferenceEquals(track, issue.Track) && !ReferenceEquals(clip, issue.Clip))
                {
                    continue;
                }

                if (builder.Length > 0)
                {
                    builder.AppendLine();
                }

                builder.Append(issue.Message);
            }

            return builder.ToString();
        }
    }

    public static class FacialTimelineValidator
    {
        private const float GazeTolerance = 0.0001f;

        public static FacialTimelineValidationReport Validate(TimelineAsset timeline, FacialProfile profile)
        {
            return ValidateInternal(timeline, profile, hasProfile: true);
        }

        public static FacialTimelineValidationReport Validate(TimelineAsset timeline)
        {
            if (timeline == null)
            {
                throw new ArgumentNullException(nameof(timeline));
            }

            return TryResolveProfile(timeline, out FacialProfile profile)
                ? ValidateInternal(timeline, profile, hasProfile: true)
                : ValidateInternal(timeline, default, hasProfile: false);
        }

        public static FacialTimelineValidationReport Validate(TrackAsset track)
        {
            if (track == null)
            {
                throw new ArgumentNullException(nameof(track));
            }

            TimelineAsset timeline = track.timelineAsset;
            if (timeline == null)
            {
                return new FacialTimelineValidationReport(Array.Empty<FacialTimelineValidationIssue>());
            }

            return Validate(timeline);
        }

        private static FacialTimelineValidationReport ValidateInternal(
            TimelineAsset timeline,
            FacialProfile profile,
            bool hasProfile)
        {
            if (timeline == null)
            {
                throw new ArgumentNullException(nameof(timeline));
            }

            var issues = new List<FacialTimelineValidationIssue>();
            foreach (TrackAsset rootTrack in timeline.GetOutputTracks())
            {
                switch (rootTrack)
                {
                    case FacialExpressionTrack expressionTrack:
                        ValidateExpressionTrack(expressionTrack, profile, hasProfile, issues);
                        break;
                    case FacialValueTrack valueTrack:
                        ValidateValueTrack(valueTrack, issues);
                        break;
                }
            }

            return new FacialTimelineValidationReport(issues.ToArray());
        }

        private static void ValidateExpressionTrack(
            FacialExpressionTrack rootTrack,
            FacialProfile profile,
            bool hasProfile,
            List<FacialTimelineValidationIssue> issues)
        {
            if (rootTrack == null)
            {
                return;
            }

            bool hasRootClips = HasClips(rootTrack);
            bool hasChildExpressionTracks = false;
            foreach (TrackAsset childTrack in rootTrack.GetChildTracks())
            {
                if (childTrack is FacialExpressionTrack)
                {
                    hasChildExpressionTracks = true;
                    break;
                }
            }

            if (!hasRootClips && hasChildExpressionTracks)
            {
                issues.Add(new FacialTimelineValidationIssue(
                    FacialTimelineIssueKind.EmptyParentTrack,
                    rootTrack.name,
                    string.Empty,
                    0d,
                    "The parent expression track has no clips. Move at least one clip back to the parent track so Timeline compiles the mixer.",
                    rootTrack,
                    null));
            }

            ValidateExpressionClips(rootTrack, profile, hasProfile, issues);
            foreach (TrackAsset childTrack in rootTrack.GetChildTracks())
            {
                if (childTrack is FacialExpressionTrack expressionChildTrack)
                {
                    ValidateExpressionClips(expressionChildTrack, profile, hasProfile, issues);
                }
            }
        }

        private static void ValidateExpressionClips(
            FacialExpressionTrack track,
            FacialProfile profile,
            bool hasProfile,
            List<FacialTimelineValidationIssue> issues)
        {
            foreach (TimelineClip clip in track.GetClips())
            {
                if (!(clip.asset is FacialExpressionClip expressionClip))
                {
                    continue;
                }

                if (clip.duration <= 0d)
                {
                    issues.Add(new FacialTimelineValidationIssue(
                        FacialTimelineIssueKind.EmptyClip,
                        track.name,
                        clip.displayName,
                        clip.start,
                        "Expression clips must have a positive duration.",
                        track,
                        clip));
                }

                string expressionId = expressionClip.ExpressionId;
                if (string.IsNullOrWhiteSpace(expressionId))
                {
                    issues.Add(new FacialTimelineValidationIssue(
                        FacialTimelineIssueKind.MissingExpressionId,
                        track.name,
                        clip.displayName,
                        clip.start,
                        "ExpressionId is empty. Assign a valid expression id.",
                        track,
                        clip));
                    continue;
                }

                if (!hasProfile)
                {
                    continue;
                }

                if (!profile.FindExpressionById(expressionId).HasValue)
                {
                    issues.Add(new FacialTimelineValidationIssue(
                        FacialTimelineIssueKind.MissingExpressionId,
                        track.name,
                        clip.displayName,
                        clip.start,
                        $"Expression '{expressionId}' does not exist in the bound profile.",
                        track,
                        clip));
                }
            }
        }

        private static void ValidateValueTrack(FacialValueTrack track, List<FacialTimelineValidationIssue> issues)
        {
            foreach (TimelineClip clip in track.GetClips())
            {
                if (!(clip.asset is FacialValueClip valueClip))
                {
                    continue;
                }

                AnimationCurve[] axes = valueClip.Axes ?? Array.Empty<AnimationCurve>();
                bool hasAnyCurve = false;
                for (int i = 0; i < axes.Length; i++)
                {
                    if (axes[i] != null)
                    {
                        hasAnyCurve = true;
                        break;
                    }
                }

                bool isEmptyClip = clip.duration <= 0d || axes.Length == 0 || !hasAnyCurve;
                if (track.ChannelKind == FacialValueChannelKind.Gaze && axes.Length < 2)
                {
                    isEmptyClip = true;
                }

                if (isEmptyClip)
                {
                    string message = track.ChannelKind == FacialValueChannelKind.Gaze
                        ? "Gaze clips must have a positive duration and both X/Y curves."
                        : "Value clips must have a positive duration and at least one curve.";
                    issues.Add(new FacialTimelineValidationIssue(
                        FacialTimelineIssueKind.EmptyClip,
                        track.name,
                        clip.displayName,
                        clip.start,
                        message,
                        track,
                        clip));
                    continue;
                }

                if (track.ChannelKind != FacialValueChannelKind.Gaze)
                {
                    continue;
                }

                for (int axisIndex = 0; axisIndex < axes.Length; axisIndex++)
                {
                    AnimationCurve curve = axes[axisIndex];
                    if (curve == null)
                    {
                        continue;
                    }

                    Keyframe[] keys = curve.keys;
                    for (int keyIndex = 0; keyIndex < keys.Length; keyIndex++)
                    {
                        float value = keys[keyIndex].value;
                        if (value >= -1f - GazeTolerance && value <= 1f + GazeTolerance)
                        {
                            continue;
                        }

                        double absoluteTime = clip.start + keys[keyIndex].time;
                        issues.Add(new FacialTimelineValidationIssue(
                            FacialTimelineIssueKind.GazeOutOfRange,
                            track.name,
                            clip.displayName,
                            absoluteTime,
                            $"Gaze key values should stay within [-1, 1]. Axis {axisIndex} has {value:0.###} at {absoluteTime:0.###}s.",
                            track,
                            clip));
                        axisIndex = axes.Length;
                        break;
                    }
                }
            }
        }

        private static bool HasClips(TrackAsset track)
        {
            foreach (TimelineClip _ in track.GetClips())
            {
                return true;
            }

            return false;
        }

        private static bool TryResolveProfile(TimelineAsset timeline, out FacialProfile profile)
        {
            return TimelineProfileSource.TryResolveForTimeline(timeline, out _, out profile);
        }
    }
}
