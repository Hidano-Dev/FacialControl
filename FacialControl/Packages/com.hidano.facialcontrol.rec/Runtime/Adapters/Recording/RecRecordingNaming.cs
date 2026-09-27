using System;
using System.Globalization;

namespace Hidano.FacialControl.Rec.Adapters.Recording
{
    /// <summary>
    /// Resolves the recording name used for a new REC take.
    /// </summary>
    public static class RecRecordingNaming
    {
        public const string TimestampNamePrefix = "take-";
        public const string TimestampFormat = "yyyyMMdd-HHmmss";

        /// <summary>
        /// 明示名 → Default Recording Name → タイムスタンプ名の順で解決する。
        /// </summary>
        public static string Resolve(string requestedName, string defaultName, DateTime now)
        {
            if (!string.IsNullOrWhiteSpace(requestedName))
            {
                return requestedName.Trim();
            }

            if (!string.IsNullOrWhiteSpace(defaultName))
            {
                return defaultName.Trim();
            }

            return BuildTimestampName(now);
        }

        public static string BuildTimestampName(DateTime now)
        {
            return TimestampNamePrefix + now.ToString(TimestampFormat, CultureInfo.InvariantCulture);
        }
    }
}
