using UnityEngine.Playables;

namespace Hidano.FacialControl.Timeline.Playables
{
    /// <summary>
    /// <see cref="Tracks.FacialCharacterTrack"/> の Mixer。何もしない（再生は子トラックの Mixer が親の binding を引いて行う）。
    /// </summary>
    /// <remarks>
    /// 子トラックの Mixer は本トラックの Playable の入力に繋がり、グラフの時間の進みが Timeline 直下と同じには届かない。
    /// そのため子の Mixer は自分の時刻ではなく Timeline の時刻（<see cref="FacialMixerTarget.TrackTime"/>）で評価する。
    /// </remarks>
    public sealed class FacialCharacterMixerBehaviour : PlayableBehaviour
    {
    }
}
