namespace Hidano.FacialControl.Timeline.Domain.Diagnostics
{
    /// <summary>
    /// 診断項目 1 件。
    /// </summary>
    public readonly struct TimelineDiagnosticItem
    {
        public TimelineDiagnosticItem(
            TimelineDiagnosticArea area,
            TimelineDiagnosticCode code,
            TimelineDiagnosticSeverity severity,
            string subject,
            string detail)
        {
            Area = area;
            Code = code;
            Severity = severity;
            Subject = subject ?? string.Empty;
            Detail = detail ?? string.Empty;
        }

        public TimelineDiagnosticArea Area { get; }

        public TimelineDiagnosticCode Code { get; }

        public TimelineDiagnosticSeverity Severity { get; }

        /// <summary>件名（トラック名 / レイヤー名 / ChannelSubId / source id など）。</summary>
        public string Subject { get; }

        /// <summary>直し方（日本語）。Inspector と Console で同じ文を使う。</summary>
        public string Detail { get; }
    }
}
