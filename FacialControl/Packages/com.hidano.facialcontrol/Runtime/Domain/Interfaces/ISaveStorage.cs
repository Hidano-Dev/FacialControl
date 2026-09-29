namespace Hidano.FacialControl.Domain.Interfaces
{
    /// <summary>
    /// キー・値形式の永続化ストレージの抽象（<c>UnityEngine.PlayerPrefs</c> 相当）。
    /// Domain 層に置くことで、Unity API に触れずに保存ロジックを記述・テストできる。
    /// 本番実装は各モジュールの Adapters 層（例: LipSync の <c>DefaultPlayerPrefsBackend</c>）、
    /// テストは Tests/Shared の <c>InMemorySaveStorage</c> を使い分ける。
    /// </summary>
    public interface ISaveStorage
    {
        string GetString(string key, string defaultValue);
        int GetInt(string key, int defaultValue);
        void SetString(string key, string value);
        void SetInt(string key, int value);

        /// <summary>保留中の変更を永続化する。</summary>
        void Save();
    }
}
