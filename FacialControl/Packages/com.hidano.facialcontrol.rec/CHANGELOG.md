# Changelog

[Keep a Changelog](https://keepachangelog.com/ja/1.1.0/) の形式に準拠し、[セマンティックバージョニング](https://semver.org/lang/ja/) に従う。

## [Unreleased]

### Added

- `RecCharacterBinding.LastRecordingName` — 直近の録画で実際に保存したテイク名（連番付与後）
- `RecCharacterBinding` に UnityEvent（uGUI Button の OnClick 等）から直接選べる void 版の操作 API `Record()` / `Record(string)` / `Load()` / `Load(string)` / `Play()` と、Inspector の Default Recording Name に対応する `RecordingName` プロパティを追加した。引数なしの `Record()` / `Load()` は `RecordingName` を読む（`Load()` は `RecordingName` で直近に録画したテイクを連番付与後の名前で読み込み、空なら直近に録画したテイク）。既存の `bool` 版 `StartRecording` / `LoadRecording` / `StartPlayback` のシグネチャは変更なし
- `RecCharacterBinding.LoadRecording()` の名前省略 — 直近に録画したテイクを読み込む。Inspector の Load Recording も Recording Name が空なら同じ動作になる

### Changed

- `RecCharacterBinding.LoadRecording` は読み込みに失敗すると、前に読み込んだテイクを破棄するようにした（従来は失敗後の `StartPlayback` が前のテイクを再生していた）。録画中に名前を省略して呼んだ場合は、録画を止めて確定したテイクを読み込むようにした（従来は確定前の 1 つ前のテイクを読むことがあった）
- 同名の録画がすでにある場合は上書きせず、`{名前}-2`, `{名前}-3`… と連番を付けて保存するようにした。実際に保存した名前とパスは `LastRecordingName` / `LastRecordingPath` と Inspector の Path 表示に反映される
- `RecStreamWriter` は出力先の予約（連番付与）と `FileMode.CreateNew` でのオープンをライタースレッドで行い、既存ファイルを決して上書きしない（呼び出し元をストレージ I/O 待ちでブロックしない）。実際に開いたパスは `OutputFilePath`、開けなかったことは `HasOutputFailed` で分かる。同じパスへ同時に録画を始めた場合も連番を取り直して両方のテイクを残す。`RecCharacterBinding.StartRecording` はファイルを開く前に true を返し、オープン失敗には気づいた時点（Update または StopRecording）で録画を止めて警告を出す（従来はオープン失敗が背景スレッドのログだけで、成功扱いのまま録画が失われていた。オープン後の書き込み失敗は従来どおりライタースレッドのログのみ）。`LastRecordingName` / `LastRecordingPath` はファイルを開いた後に反映され、録画中のテイクのパスは `CurrentRecordingPath` で分かる
- `RecCharacterBinding` の Default Recording Name の初期値を `take` から空にし、Recording Name / Default Recording Name の両方が空なら `take-yyyyMMdd-HHmmss` で命名する（README の記述と実装を一致させた）。Inspector の Recording Name 欄を Default Recording Name で埋めるのをやめた
- 録画名の解決ロジックを `RecRecordingNaming` に切り出した

## [1.0.0] - 2026-09-25

初回リリース。

### Added

- `RecCharacterBinding` (MonoBehaviour) — `FacialController` と同居させるだけで録画・再生を行う facade。Inspector から Start / Stop Recording、Load Recording、Start / Stop Playback を操作できる
- `RecordingUseCase` — core の `IFacialInputObservationBus` を購読し、表情トリガー on/off・アナログ値・Gaze・録画開始時の baseline を記録する
- `PlaybackUseCase` — 記録を時刻順に再生し、live のトリガー入力源とアナログ入力源を再生中だけ遮断・置換する
- `.fcrec` 独自バイナリ形式（`formatVersion = 1`）と、専用スレッドでストリーム書き込みする `RecStreamWriter`。末尾切れファイルの復元にも対応
- 保存先 `StreamingAssets/FacialControl/{キャラクター名}/recordings/`
- 録画・再生のホットパスで GC アロケーション 0 を検証する PlayMode gate テスト
