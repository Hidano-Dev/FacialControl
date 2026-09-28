using Hidano.FacialControl.Domain.Interfaces;

namespace Hidano.FacialControl.LipSync.Adapters.Devices
{
    /// <summary>
    /// LipSync デバイス選択の保存先。メンバは <see cref="ISaveStorage"/>（PlayerPrefs 相当のキー・値保存）と同一で、
    /// モジュール内部向けの名前を維持するために継承のみ行う。
    /// </summary>
    internal interface IPlayerPrefsBackend : ISaveStorage
    {
    }
}
