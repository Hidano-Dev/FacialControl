using System;
using System.Collections.Generic;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using UnityEngine;

namespace Hidano.FacialControl.Timeline.Adapters.Session
{
    /// <summary>
    /// Timeline のレイヤー weight トラック（<see cref="Tracks.FacialLayerWeightTrack"/>）を再生する間、
    /// live のレイヤー weight 書き込みを止めて Timeline の値を注入する（REC 再生の <c>RecWeightInjector</c> と同じ gate を使う）。
    /// </summary>
    /// <remarks>
    /// <para>開始時に全レイヤーの weight を控えてから宣言値 1 にし、終了時に控えた値へ戻して live の書き込みを再開する。
    /// 入力源 weight には触れない（<see cref="IWeightInjectionGate.ResetWeightsToDeclared"/> は使わない）。</para>
    /// <para>live の weight が既に止まっている（REC 再生中など）ときは奪わない。メインスレッド専用。
    /// 確保は開始時と未知レイヤーの初回警告に閉じる。</para>
    /// </remarks>
    public sealed class TimelineLayerWeightOverride
    {
        /// <summary>レイヤー weight の宣言値（Profile にレイヤー weight の宣言は無く、常に 1）。</summary>
        public const float DeclaredLayerWeight = 1f;

        private const string LogPrefix = "[FacialTimelineReceiver] ";

        private readonly List<LayerWeightEntry> _restoreWeights = new List<LayerWeightEntry>();
        private readonly HashSet<string> _warnedLayers = new HashSet<string>(StringComparer.Ordinal);
        private IWeightInjectionGate _gate;

        /// <summary>live の weight を止めて注入中か。</summary>
        public bool IsActive => _gate != null;

        /// <summary>
        /// live のレイヤー weight を止め、全レイヤーを宣言値にする。止められなかった（gate 無し / レイヤー名重複 /
        /// 既に他者が停止中）なら false を返し、以降の <see cref="Apply"/> は何もしない。
        /// </summary>
        public bool Begin(IWeightInjectionGate gate)
        {
            End();
            if (gate == null || !gate.LayerNamesAreUnique || !gate.SuspendLiveWeights())
            {
                return false;
            }

            _gate = gate;
            _warnedLayers.Clear();
            gate.CollectLayerWeights(_restoreWeights);
            for (int i = 0; i < _restoreWeights.Count; i++)
            {
                gate.TryInjectLayerWeight(_restoreWeights[i].LayerName, DeclaredLayerWeight);
            }

            return true;
        }

        /// <summary>レイヤー weight を注入する。未開始なら何もしない。存在しないレイヤーはレイヤーごとに 1 回だけ警告する。</summary>
        public void Apply(string layerName, float weight)
        {
            if (_gate == null || string.IsNullOrEmpty(layerName) || _gate.TryInjectLayerWeight(layerName, weight))
            {
                return;
            }

            if (_warnedLayers.Add(layerName))
            {
                Debug.LogWarning(
                    $"{LogPrefix}Layer weight track for layer '{layerName}' was skipped because the layer does not exist in the current profile.");
            }
        }

        /// <summary>開始時の weight へ戻し、live の書き込みを再開する。未開始なら何もしない（二重呼び出し可）。</summary>
        public void End()
        {
            IWeightInjectionGate gate = _gate;
            _gate = null;
            if (gate == null)
            {
                return;
            }

            for (int i = 0; i < _restoreWeights.Count; i++)
            {
                gate.TryInjectLayerWeight(_restoreWeights[i].LayerName, _restoreWeights[i].Weight);
            }

            _restoreWeights.Clear();
            gate.ResumeLiveWeights();
        }
    }
}
