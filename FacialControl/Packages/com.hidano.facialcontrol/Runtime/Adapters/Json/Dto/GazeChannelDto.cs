namespace Hidano.FacialControl.Adapters.Json.Dto
{
    /// <summary>新しい profile.json の gaze.channels[] エントリです。</summary>
    /// <remarks>
    /// 旧キー <c>providerSlug</c> / <c>useDistinctLeftRight</c> / <c>sourceIdLeft</c> / <c>sourceIdRight</c> /
    /// 左右の初期回転 / yaw・pitch 軸は削除した (HID-196)。JSON に残っていても読み飛ばす。
    /// </remarks>
    [System.Serializable]
    public sealed class GazeChannelDto
    {
        public string id;
        public string leftEyeBonePath;
        public string rightEyeBonePath;

        public float lookUpAngle = 15f;
        public float lookDownAngle = 9f;
        public float outerYawAngle = 15f;
        public float innerYawAngle = 18f;
    }
}
