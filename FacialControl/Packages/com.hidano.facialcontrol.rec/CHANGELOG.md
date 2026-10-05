# Changelog

[Keep a Changelog](https://keepachangelog.com/ja/1.1.0/) の形式に準拠し、[セマンティックバージョニング](https://semver.org/lang/ja/) に従う。

## [Unreleased]

### Added

- `rec-weight-coverage` によりレイヤー weight / 入力源 weight の基準・時刻付きレコード（kind 12〜15）とヘッダ `flags` bit1（`WeightBaseline`）を追加し、再生中はライブ書込を遮断して宣言値リセット → 基準確立 → REC 注入を行うよう文書化した。停止後は停止時点の weight を維持する。timeline REC Export の kind 12〜15 対応は timeline トラック合流後の follow-up とする

- REC の記録・遮断・注入対象を FacialControl で動く全入力源に拡張した（HID-35）。値提供型入力源（OSC / iFacialMocap / uLipSync 音素オーバーレイ / Timeline ベイク値 / Overlay）は mask 順の疎な値を差分形式（`RecValueProviderFlags` の IsValid / HasMask / HasValues）で記録し、再生中は `RecPlaybackValueProviderSource` に置換する。系1（`FacialController.Activate/Deactivate` 直呼び）は予約 source `@expression` で記録し、再生中は `IExpressionActivationGate` 経由で遮断・注入する。基準状態（`RecBaselineState`）に値提供型と系1のエントリを追加した（4 引数コンストラクタ。2 引数版は「値提供型 / 系1の基準なし」として互換維持）
- `.fcrec` に record kind 7〜11（`ValueProviderSample` / `BaselineValueProvider` / `ExpressionActivate` / `ExpressionDeactivate` / `BaselineExpression`）を追加し、ヘッダの `flags` bit0（`RecHeaderFlags.FullInputBaseline`）を必須化した。formatVersion は 1 のまま（v1 は未リリースのため在置き変更）。bit0 の無い旧 dev ファイルは `TryRead` が拒否する
- 網羅性ゲート: `RecInputSourceCoverageCatalog` が全 `IInputSource` 実装を「観測対象 13 / 明示的除外 7」に分類し、`RecInputSourceCoverageCatalogTests` / `RecInputSourceExclusionContractTests`（Small）で漏れを検出する

- `RecCharacterBinding.StartPlayback(double startOffsetSeconds)` / `PlaybackUseCase.StartPlayback(double)` — 録画の途中から再生する。開始位置より前のイベントを畳み込んで状態を再構築する（トリガーは最終的な on/off、アナログは各入力源の最後の値）。遷移途中だった表情は遷移の進行度までは再現せず、その時点の目標状態から始まる
- `RecTimelineSeek.BuildBaselineAt` と `RecPlaybackScheduler.Load(RecTimeline, double)` / `GetStartEventIndex` / `IsValidStartOffset` — 上記の Domain 側の実装
- `RecCharacterBinding.LastRecordingName` — 直近の録画で実際に保存したテイク名（連番付与後）
- `RecCharacterBinding.LoadRecording()` の名前省略 — 直近に録画したテイクを読み込む。Inspector の Load Recording も Load Target が空なら同じ動作になる
- 保存済み録画の列挙 API — `RecCharacterBinding.GetRecordings()`（`RecRecordingEntry`: テイク名・パス・更新日時）/ `GetRecordingNames()`。更新日時の新しい順に並び、録画中のテイクは含めない。ランタイム asmdef にあるので uGUI の Dropdown 等の選択肢にそのまま使える。下位 API として `RecSidecarPath.ListRecordings` / `TryListRecordings`
- `RecCharacterBinding.RecordingClock` — 記録に使う `IRecClock` を差し替えられるようにした（null なら既定の `RecStopwatchClock`）。外部タイムコード同期の下準備で、タイムコードの受信自体はスコープ外
- `RecCharacterBinding.RecordingStartOffsetSeconds`（Inspector の Recording Start Offset Seconds） — 記録タイムスタンプと録画長に加算する開始オフセット（秒、0 以上）
- `RecordingUseCase` のコンストラクタに `startOffsetSeconds`（既定 0）を追加した。クロック値を検証してから加算する。あわせて差し替えクロックの逆行はクランプし、例外・NaN・負値は直前の値で置き換えて 1 回だけ警告する
- `RecStopwatchClock`（Adapters） — 従来 `RecCharacterBinding` の private だった既定クロックを公開した
- `RecCharacterBinding` に UnityEvent（uGUI Button の OnClick 等）から直接選べる void 版の操作 API `Record()` / `Record(string)` / `Load()` / `Load(string)` / `Play()` と、Inspector の Default Recording Name に対応する `RecordingName` プロパティを追加した。引数なしの `Record()` / `Load()` は `RecordingName` を読む（`Load()` は `RecordingName` で直近に録画したテイクを連番付与後の名前で読み込み、空なら直近に録画したテイク）。既存の `bool` 版 `StartRecording` / `LoadRecording` / `StartPlayback` のシグネチャは変更なし

### Changed

- **破壊的（preview）**: 注入ポートを共通ライフサイクル `IInjectionPort { CanBeginInjection(out reason); TryBeginInjection(baseline); EndInjection() }` に統一し、`ITriggerInjectionPort` / `IAnalogInjectionPort` の `void BeginInjection` を廃止した。`PlaybackUseCase` は 4 ポート（trigger → expression → analog → valueProvider）を all-or-nothing で確立し、途中失敗は逆順に解放して Idle に戻る（2 ポートのコンストラクタは互換のため残す）。`IRecEventSink.AppendEvent` に `maskBytes` 引数、`IRecEventVisitor` に `VisitValueProviderSample` / `VisitExpressionActivate` / `VisitExpressionDeactivate` を追加した。`ILayerSourceValueObserver.OnSourceValuesObserved` に `IInputSource source` 引数を追加した（core）
- osc 受信 binding の heartbeat は registry の `Replace` ではなく `OscInputSource.UpdateMapping` による in-place 更新になった（再生中の注入ソースを追い出さないため）
- 既知の制限から、ランタイムのレイヤー weight / 入力源 weight 変更（HID-80）を削除し、weight 対応を記録した（初期記述は rec-weight-coverage task 5.1（skip）により上書き）
- `RecCharacterBinding.LoadRecording` は読み込みに失敗すると、前に読み込んだテイクを破棄するようにした（従来は失敗後の `StartPlayback` が前のテイクを再生していた）。録画中に名前を省略して呼んだ場合は、録画を止めて確定したテイクを読み込むようにした（従来は確定前の 1 つ前のテイクを読むことがあった）
- 同名の録画がすでにある場合は上書きせず、`{名前}-2`, `{名前}-3`… と連番を付けて保存するようにした。実際に保存した名前とパスは `LastRecordingName` / `LastRecordingPath` と Inspector の Path 表示に反映される
- `RecStreamWriter` は出力先の予約（連番付与）と `FileMode.CreateNew` でのオープンをライタースレッドで行い、既存ファイルを決して上書きしない（呼び出し元をストレージ I/O 待ちでブロックしない）。実際に開いたパスは `OutputFilePath`、開けなかったことは `HasOutputFailed` で分かる。同じパスへ同時に録画を始めた場合も連番を取り直して両方のテイクを残す。`RecCharacterBinding.StartRecording` はファイルを開く前に true を返し、オープン失敗には気づいた時点（Update または StopRecording）で録画を止めて警告を出す（従来はオープン失敗が背景スレッドのログだけで、成功扱いのまま録画が失われていた。オープン後の書き込み失敗は従来どおりライタースレッドのログのみ）。`LastRecordingName` / `LastRecordingPath` はファイルを開いた後に反映され、録画中のテイクのパスは `CurrentRecordingPath` で分かる
- `RecCharacterBinding` の Default Recording Name の初期値を `take` から空にし、Recording Name / Default Recording Name の両方が空なら `take-yyyyMMdd-HHmmss` で命名する（README の記述と実装を一致させた）。Inspector の Recording Name 欄を Default Recording Name で埋めるのをやめた
- 録画名の解決ロジックを `RecRecordingNaming` に切り出した
- Inspector の Load Recording は、テイク名の文字入力ではなく保存済み録画から選ぶ **Load Target** ドロップダウンで対象を指定するようにした。一覧は Inspector の表示時・録画停止時・Refresh ボタンで更新し、録画を止めると保存したテイクが選択される。Recording Name 欄は Start Recording 専用になった

### Fixed

- `RecCharacterBinding` は `FacialController` の `InputSourceRegistry` が別インスタンスになっていたら（`InitializeWithProfile` 等の再初期化後）再生セッションを作り直すようにした。従来は同じ controller なら注入体を再利用し、旧 registry へ置換・復元していた
- `.fcrec` の読込で、値提供型レコード（kind 7 / 8）の未知 flag bit や kind 8 の `HasMask | HasValues` 欠落、時刻付きレコード（kind 1〜3 / 7 / 9 / 10）の負数・NaN・無限大の timestamp を、末尾切れ復旧や例外ではなく明示エラーで拒否するようにした。`BaselineExpression`（kind 11）の source index は予約 ID `@expression` の実 index を書く（従来は常に 0）
- Windows で連番テイク（`{名前}-2` 以降）の `LastRecordingPath` の区切り文字が `LoadedRecordingPath` と一致しなかった（`Application.streamingAssetsPath` 由来の `/` が `\` に正規化されていた）

## [1.0.0] - 2026-09-25

初回リリース。

### Added

- `RecCharacterBinding` (MonoBehaviour) — `FacialController` と同居させるだけで録画・再生を行う facade。Inspector から Start / Stop Recording、Load Recording、Start / Stop Playback を操作できる
- `RecordingUseCase` — core の `IFacialInputObservationBus` を購読し、表情トリガー on/off・アナログ値・Gaze・録画開始時の baseline を記録する
- `PlaybackUseCase` — 記録を時刻順に再生し、live のトリガー入力源とアナログ入力源を再生中だけ遮断・置換する
- `.fcrec` 独自バイナリ形式（`formatVersion = 1`）と、専用スレッドでストリーム書き込みする `RecStreamWriter`。末尾切れファイルの復元にも対応
- 保存先 `StreamingAssets/FacialControl/{キャラクター名}/recordings/`
- 録画・再生のホットパスで GC アロケーション 0 を検証する PlayMode gate テスト
