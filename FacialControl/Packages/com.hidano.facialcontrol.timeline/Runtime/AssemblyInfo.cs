using System.Runtime.CompilerServices;

// FacialTimelineDiagnostics の書き込み口（ReplaceArea / Clear）は internal。テストからのみ直接書き込む。
[assembly: InternalsVisibleTo("Hidano.FacialControl.Timeline.Tests.EditMode")]
[assembly: InternalsVisibleTo("Hidano.FacialControl.Timeline.Tests.PlayMode")]
