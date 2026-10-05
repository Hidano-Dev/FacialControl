using System;
using System.Collections.Generic;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Timeline.Adapters.Assets;
using Hidano.FacialControl.Timeline.Adapters.Scanning;
using UnityEditor;
using UnityEditor.Timeline;
using UnityEngine;
using UnityEngine.Timeline;

namespace Hidano.FacialControl.Timeline.Editor
{
    /// <summary>再ベイク口の結果（D8）。</summary>
    public enum RebakeOutcome
    {
        /// <summary>ハッシュ一致かつ参照も一致。何もしていない。</summary>
        NoChange = 0,

        /// <summary>ハッシュ一致で焼き直していないが、Facial トラックの Bake 参照を修復した。</summary>
        ReferencesRepaired = 1,

        /// <summary>Bake を焼き直した。</summary>
        Rebaked = 2,

        /// <summary>失敗（前回の Bake を保持）。</summary>
        Failed = 3,
    }

    /// <summary><see cref="TimelineEditChangeWatcher.MarkDirty"/> の結果。</summary>
    public enum MarkDirtyResult
    {
        /// <summary>新たに予約した。</summary>
        Queued = 0,

        /// <summary>同じ Timeline の予約（または実行中の再ベイク）に畳んだ。</summary>
        Coalesced = 1,

        /// <summary>未保存（アセットパス空）の Timeline。サブアセットを保存できないため予約しない。</summary>
        UnsavedTimeline = 2,

        /// <summary>null / Facial トラックの無い Timeline / Play 遷移中 / 破棄済み。</summary>
        Ignored = 3,
    }

    /// <summary>MarkDirty の契機。Console の Info と <see cref="TimelineEditChangeWatcher.BakeUpdated"/> の引数に使うだけで処理は共通。</summary>
    public enum TimelineDirtyReason
    {
        ClipEdit = 0,
        UndoRedo = 1,
        ObjectChange = 2,
        ProfileChanged = 3,
        BakeReferenceInconsistent = 4,
        ProfileMismatch = 5,
    }

    /// <summary>Timeline 1 つを再ベイクする実行口（既定は <c>TimelineBakeDirtyWatcher.RebakeNow</c>）。</summary>
    public interface IRebakeExecutor
    {
        RebakeOutcome Rebake(TimelineAsset timeline, out FacialTimelineBakeAsset bake, out string failureReason);
    }

    /// <summary>
    /// 変更検知（Clip 編集 / Undo / ObjectChange / Profile 変更 / 参照不整合 / Profile 不一致）を Timeline 単位に合流させ、
    /// デバウンス後に再ベイクを 1 回だけ実行する（D8）。Unity イベントは購読せず、所有者（TimelineEditorServices）から
    /// <see cref="MarkDirty"/> / <see cref="Tick"/> / <see cref="FlushNow"/> を呼ばれるだけのインスタンス。
    /// </summary>
    /// <remarks>
    /// pending が 0 → 1 になったとき tick 要求、1 → 0 になったとき tick 解放のコールバックを呼ぶ
    /// （所有者は pending がある間だけ EditorApplication.update を購読する）。
    /// 再ベイク中に届いた MarkDirty は完了後に 1 回だけ再実行する。
    /// </remarks>
    public sealed class TimelineEditChangeWatcher : IDisposable
    {
        private const string LogPrefix = "[TimelineEditChangeWatcher] ";
        private const double DebounceEpsilon = 1e-6d;

        private readonly IRebakeExecutor _rebake;
        private readonly Func<double> _clock;
        private readonly Action _requestTick;
        private readonly Action _releaseTick;
        private readonly Dictionary<TimelineAsset, PendingEntry> _pending = new Dictionary<TimelineAsset, PendingEntry>();
        private readonly Dictionary<TimelineAsset, FacialCharacterProfileSO> _profileByTimeline =
            new Dictionary<TimelineAsset, FacialCharacterProfileSO>();
        private readonly List<TimelineAsset> _runBuffer = new List<TimelineAsset>();
        private bool _tickRequested;
        private bool _disposed;

        public TimelineEditChangeWatcher(IRebakeExecutor rebake, Func<double> clock)
            : this(rebake, clock, null, null)
        {
        }

        /// <param name="rebake">再ベイク実行口。</param>
        /// <param name="clock">現在時刻（秒）。null なら <see cref="EditorApplication.timeSinceStartup"/>。</param>
        /// <param name="requestTick">pending が 0 → 1 になったときに呼ぶ（null は no-op）。</param>
        /// <param name="releaseTick">pending が 1 → 0 になったときに呼ぶ（null は no-op）。</param>
        public TimelineEditChangeWatcher(IRebakeExecutor rebake, Func<double> clock, Action requestTick, Action releaseTick)
        {
            _rebake = rebake ?? throw new ArgumentNullException(nameof(rebake));
            _clock = clock ?? (() => EditorApplication.timeSinceStartup);
            _requestTick = requestTick;
            _releaseTick = releaseTick;
        }

        /// <summary>最後の MarkDirty から再ベイクまでの待ち時間（秒）。</summary>
        public double DebounceSeconds { get; set; } = 0.3d;

        /// <summary>Play 遷移中の判定（既定 <see cref="EditorApplication.isPlayingOrWillChangePlaymode"/>。テストから差し替え可）。</summary>
        public Func<bool> IsPlayModeTransition { get; set; } = () => EditorApplication.isPlayingOrWillChangePlaymode;

        /// <summary>再ベイク成功後の Timeline ウィンドウ再描画（テストから差し替え可）。</summary>
        public Action RefreshTimelineWindow { get; set; } = RefreshTimelineEditor;

        /// <summary>再ベイク（焼き直し、または参照修復）が成功したとき。</summary>
        public event Action<TimelineAsset, FacialTimelineBakeAsset, TimelineDirtyReason> BakeUpdated;

        /// <summary>再ベイクに失敗したとき（前回の Bake は保持される）。</summary>
        public event Action<TimelineAsset, string> RebakeFailed;

        public int PendingCount => _pending.Count;

        /// <summary>Profile SO を追跡中の Timeline（登録順は保証しない）。</summary>
        public IReadOnlyCollection<TimelineAsset> TrackedTimelines => _profileByTimeline.Keys;

        public bool IsPending(TimelineAsset timeline)
        {
            return timeline != null && _pending.ContainsKey(timeline);
        }

        public MarkDirtyResult MarkDirty(TimelineAsset timeline, TimelineDirtyReason reason)
        {
            if (_disposed || timeline == null)
            {
                return MarkDirtyResult.Ignored;
            }

            Func<bool> playProbe = IsPlayModeTransition;
            if (playProbe != null && playProbe())
            {
                return MarkDirtyResult.Ignored;
            }

            if (_pending.TryGetValue(timeline, out PendingEntry existing))
            {
                existing.LastMarkTime = _clock();
                existing.Reason = reason;
                if (existing.IsRebaking)
                {
                    existing.PendingAgain = true;
                }

                return MarkDirtyResult.Coalesced;
            }

            if (TimelineAssetScanner.Scan(timeline).Tracks.Count == 0)
            {
                return MarkDirtyResult.Ignored;
            }

            if (string.IsNullOrEmpty(AssetDatabase.GetAssetPath(timeline)))
            {
                return MarkDirtyResult.UnsavedTimeline;
            }

            _pending.Add(timeline, new PendingEntry(_clock(), reason));
            if (!_tickRequested)
            {
                _tickRequested = true;
                _requestTick?.Invoke();
            }

            return MarkDirtyResult.Queued;
        }

        /// <summary>Timeline が解決した Profile SO を登録する（SO 変更 → Timeline の逆引きに使う）。null は登録解除。</summary>
        public void TrackProfile(TimelineAsset timeline, FacialCharacterProfileSO profileAsset)
        {
            if (_disposed || timeline == null)
            {
                return;
            }

            if (profileAsset == null)
            {
                _profileByTimeline.Remove(timeline);
                return;
            }

            _profileByTimeline[timeline] = profileAsset;
        }

        /// <summary>指定 SO を追跡している Timeline を返す。</summary>
        public IReadOnlyList<TimelineAsset> GetTrackedTimelines(FacialCharacterProfileSO profileAsset)
        {
            var result = new List<TimelineAsset>();
            if (profileAsset == null)
            {
                return result;
            }

            foreach (KeyValuePair<TimelineAsset, FacialCharacterProfileSO> pair in _profileByTimeline)
            {
                if (pair.Key != null && ReferenceEquals(pair.Value, profileAsset))
                {
                    result.Add(pair.Key);
                }
            }

            return result;
        }

        /// <summary>指定 SO を追跡している全 Timeline を <see cref="TimelineDirtyReason.ProfileChanged"/> で MarkDirty し、予約に入った数を返す。</summary>
        public int MarkProfileChanged(FacialCharacterProfileSO profileAsset)
        {
            IReadOnlyList<TimelineAsset> timelines = GetTrackedTimelines(profileAsset);
            int marked = 0;
            for (int i = 0; i < timelines.Count; i++)
            {
                MarkDirtyResult result = MarkDirty(timelines[i], TimelineDirtyReason.ProfileChanged);
                if (result == MarkDirtyResult.Queued || result == MarkDirtyResult.Coalesced)
                {
                    marked++;
                }
            }

            return marked;
        }

        /// <summary>デバウンスが経過した pending を実行する（所有者の update から呼ばれる）。</summary>
        public void Tick()
        {
            if (_disposed || _pending.Count == 0 || EditorApplication.isCompiling)
            {
                return;
            }

            double now = _clock();
            _runBuffer.Clear();
            foreach (KeyValuePair<TimelineAsset, PendingEntry> pair in _pending)
            {
                // 時刻差の丸め誤差（100.3 - 100.0 < 0.3 等）で 1 周遅れないよう微小な許容を入れる。
                if (!pair.Value.IsRebaking && now - pair.Value.LastMarkTime + DebounceEpsilon >= DebounceSeconds)
                {
                    _runBuffer.Add(pair.Key);
                }
            }

            RunBuffered();
        }

        /// <summary>デバウンスを待たずに全 pending を実行する（ExitingEditMode / テスト）。実行中に再予約されたものは 1 回だけ追加で実行する。</summary>
        public void FlushNow()
        {
            if (_disposed)
            {
                return;
            }

            // 1 周目: 全 pending。2 周目: 再ベイク中に再予約されたもの（完了後 1 回）。
            for (int pass = 0; pass < 2 && _pending.Count > 0; pass++)
            {
                _runBuffer.Clear();
                foreach (KeyValuePair<TimelineAsset, PendingEntry> pair in _pending)
                {
                    if (!pair.Value.IsRebaking)
                    {
                        _runBuffer.Add(pair.Key);
                    }
                }

                RunBuffered();
            }
        }

        /// <summary>pending を破棄し tick を解放する。以後の MarkDirty は Ignored。二重呼び出しは no-op。</summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _pending.Clear();
            _profileByTimeline.Clear();
            ReleaseTickIfIdle();
            BakeUpdated = null;
            RebakeFailed = null;
        }

        /// <summary>pending を破棄する（Play 中の MarkDirty を捨てる用途）。Watcher は引き続き使える。</summary>
        public void DiscardPending()
        {
            _pending.Clear();
            ReleaseTickIfIdle();
        }

        private void RunBuffered()
        {
            if (_runBuffer.Count == 0)
            {
                return;
            }

            TimelineAsset[] targets = _runBuffer.ToArray();
            _runBuffer.Clear();
            for (int i = 0; i < targets.Length; i++)
            {
                if (_disposed)
                {
                    return;
                }

                Execute(targets[i]);
            }

            ReleaseTickIfIdle();
        }

        private void Execute(TimelineAsset timeline)
        {
            if (!_pending.TryGetValue(timeline, out PendingEntry entry))
            {
                return;
            }

            if (timeline == null)
            {
                // アセットが破棄された。
                _pending.Remove(timeline);
                return;
            }

            entry.IsRebaking = true;
            entry.PendingAgain = false;
            TimelineDirtyReason reason = entry.Reason;

            if (_profileByTimeline.TryGetValue(timeline, out FacialCharacterProfileSO trackedProfile) && trackedProfile != null)
            {
                TimelineProfileSource.InvalidateCache(trackedProfile);
            }

            RebakeOutcome outcome;
            FacialTimelineBakeAsset bake;
            string failureReason;
            try
            {
                outcome = _rebake.Rebake(timeline, out bake, out failureReason);
            }
            catch (Exception ex)
            {
                outcome = RebakeOutcome.Failed;
                bake = null;
                failureReason = ex.Message;
            }

            if (_disposed)
            {
                return;
            }

            entry.IsRebaking = false;
            if (entry.PendingAgain)
            {
                entry.PendingAgain = false;
            }
            else
            {
                _pending.Remove(timeline);
            }

            switch (outcome)
            {
                case RebakeOutcome.Rebaked:
                case RebakeOutcome.ReferencesRepaired:
                    if (reason == TimelineDirtyReason.BakeReferenceInconsistent
                        || reason == TimelineDirtyReason.ProfileMismatch
                        || reason == TimelineDirtyReason.ProfileChanged)
                    {
                        Debug.Log($"{LogPrefix}Timeline '{timeline.name}' を再ベイクしました（理由: {reason}）。");
                    }

                    BakeUpdated?.Invoke(timeline, bake, reason);
                    try
                    {
                        RefreshTimelineWindow?.Invoke();
                    }
                    catch (Exception ex)
                    {
                        Debug.LogException(ex);
                    }

                    break;
                case RebakeOutcome.Failed:
                    Debug.LogWarning($"{LogPrefix}Timeline '{timeline.name}' の再ベイクに失敗しました（前回の Bake を保持します）: {failureReason}");
                    RebakeFailed?.Invoke(timeline, failureReason ?? string.Empty);
                    break;
            }
        }

        private void ReleaseTickIfIdle()
        {
            if (_tickRequested && _pending.Count == 0)
            {
                _tickRequested = false;
                _releaseTick?.Invoke();
            }
        }

        private static void RefreshTimelineEditor()
        {
            TimelineEditor.Refresh(RefreshReason.ContentsModified | RefreshReason.SceneNeedsUpdate);
        }

        private sealed class PendingEntry
        {
            public PendingEntry(double lastMarkTime, TimelineDirtyReason reason)
            {
                LastMarkTime = lastMarkTime;
                Reason = reason;
            }

            public double LastMarkTime { get; set; }

            public TimelineDirtyReason Reason { get; set; }

            public bool IsRebaking { get; set; }

            public bool PendingAgain { get; set; }
        }
    }
}
