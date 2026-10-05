using System;
using System.Collections.Generic;

namespace Hidano.FacialControl.Timeline.Domain.Diagnostics
{
    /// <summary>
    /// 同一原因の Console 警告を 1 セッション（Play）/ 1 診断エポック（Edit）に 1 回だけ通すゲート（D10）。
    /// </summary>
    /// <remarks>
    /// キーは (所有者 instanceID, 診断コード, 件名)。件名の null と空文字は同じキーとして扱う。
    /// Inspector の表示はこのゲートに依らず常に現在値を出す。メインスレッド専用。
    /// </remarks>
    public sealed class TimelineOnceWarningGate
    {
        private readonly HashSet<Key> _passed = new HashSet<Key>();

        /// <summary>
        /// 同じキーの初回呼び出しのみ true を返す。
        /// </summary>
        public bool TryPass(int ownerInstanceId, TimelineDiagnosticCode code, string subject)
        {
            return _passed.Add(new Key(ownerInstanceId, code, subject ?? string.Empty));
        }

        /// <summary>
        /// 通過済みのキーをすべて忘れる（Play セッション境界 / Edit エポック境界で呼ぶ）。
        /// </summary>
        public void ResetEpoch()
        {
            _passed.Clear();
        }

        private readonly struct Key : IEquatable<Key>
        {
            private readonly int _ownerInstanceId;
            private readonly TimelineDiagnosticCode _code;
            private readonly string _subject;

            public Key(int ownerInstanceId, TimelineDiagnosticCode code, string subject)
            {
                _ownerInstanceId = ownerInstanceId;
                _code = code;
                _subject = subject;
            }

            public bool Equals(Key other)
            {
                return _ownerInstanceId == other._ownerInstanceId
                    && _code == other._code
                    && string.Equals(_subject, other._subject, StringComparison.Ordinal);
            }

            public override bool Equals(object obj)
            {
                return obj is Key other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = _ownerInstanceId;
                    hash = (hash * 397) ^ (int)_code;
                    hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(_subject);
                    return hash;
                }
            }
        }
    }
}
