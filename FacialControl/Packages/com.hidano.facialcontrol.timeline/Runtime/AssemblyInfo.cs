using System.Runtime.CompilerServices;

// FacialTimelineDiagnostics の書き込み口（ReplaceArea / Clear）は internal。テストと、Edit 評価で Inspector 由来の領域
// （LayerConnection の旧宣言・Bake の UnsavedTimeline）を書く timeline Editor からだけ直接書き込む。
[assembly: InternalsVisibleTo("Hidano.FacialControl.Timeline.Editor")]
[assembly: InternalsVisibleTo("Hidano.FacialControl.Timeline.Tests.EditMode")]
[assembly: InternalsVisibleTo("Hidano.FacialControl.Timeline.Tests.PlayMode")]
