# Changelog

[Keep a Changelog](https://keepachangelog.com/ja/1.1.0/) の形式に準拠し、[セマンティックバージョニング](https://semver.org/lang/ja/) に従う。

## [1.0.0] - 2026-09-25

初回リリース。

### Added

- `RecCharacterBinding` (MonoBehaviour) — `FacialController` と同居させるだけで録画・再生を行う facade。Inspector から Start / Stop Recording、Load Recording、Start / Stop Playback を操作できる
- `RecordingUseCase` — core の `IFacialInputObservationBus` を購読し、表情トリガー on/off・アナログ値・Gaze・録画開始時の baseline を記録する
- `PlaybackUseCase` — 記録を時刻順に再生し、live のトリガー入力源とアナログ入力源を再生中だけ遮断・置換する
- `.fcrec` 独自バイナリ形式（`formatVersion = 1`）と、専用スレッドでストリーム書き込みする `RecStreamWriter`。末尾切れファイルの復元にも対応
- 保存先 `StreamingAssets/FacialControl/{キャラクター名}/recordings/`
- 録画・再生のホットパスで GC アロケーション 0 を検証する PlayMode gate テスト
