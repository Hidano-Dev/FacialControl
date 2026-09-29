using System;

namespace Hidano.FacialControl.Rec.Adapters.Recording
{
    /// <summary>
    /// 保存済み録画 1 件の情報。<see cref="Name"/> はそのまま <c>RecCharacterBinding.LoadRecording</c> に渡せる。
    /// </summary>
    public readonly struct RecRecordingEntry
    {
        public RecRecordingEntry(string name, string filePath, DateTime lastWriteTimeUtc)
        {
            Name = name;
            FilePath = filePath;
            LastWriteTimeUtc = lastWriteTimeUtc;
        }

        /// <summary>拡張子を除いたテイク名。</summary>
        public string Name { get; }

        /// <summary>録画ファイルの絶対パス。</summary>
        public string FilePath { get; }

        /// <summary>録画ファイルの最終更新日時（UTC）。</summary>
        public DateTime LastWriteTimeUtc { get; }
    }
}
