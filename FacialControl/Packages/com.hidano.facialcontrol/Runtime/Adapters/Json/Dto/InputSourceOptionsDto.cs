namespace Hidano.FacialControl.Adapters.Json.Dto
{
    /// <summary>
    /// 入力源 options の全 DTO 型の共通基底（マーカー）。
    /// id ごとに派生 DTO を定義する（例: <see cref="OscOptionsDto"/>, <see cref="LipSyncOptionsDto"/>）。
    /// JsonUtility 互換のため class とする。
    /// </summary>
    [System.Serializable]
    public abstract class InputSourceOptionsDto
    {
    }
}
