# Changelog

[Keep a Changelog](https://keepachangelog.com/ja/1.1.0/) の形式に準拠し、[セマンティックバージョニング](https://semver.org/lang/ja/) に従う。

## [Unreleased]

### Added

- `FacialTimelinePreviewGazeTargets` / `FacialTimelinePreviewEyeTarget`（Editor）— スクラブプレビューで目線を書き込む目ボーンの解決。ランタイムの `GazeBonePoseProvider` と同じ規則（path 未指定の側は Humanoid の目ボーン、目ごとに先頭の channel だけ、path 指定が同じボーンを指せば path 側を優先）

### Fixed

- 目ボーン path が空の Gaze チャネルで、Timeline のスクラブプレビュー中に目が動かなかった。path が空の側は Humanoid Avatar の `LeftEye` / `RightEye` を使い、rest 回転と yaw / pitch 軸はプレビュー開始時の姿勢から導出する（HID-41 のランタイム fallback と同じ）
- 目ボーン path が空のとき、スクラブプレビューがキャラクター root の回転を復元対象として登録していた。空・空白の path では `Transform.Find` を呼ばない

## [1.0.0] - 2026-09-25

初回リリース。

### Added

- `TimelineAdapterBinding`（Adapter Bindings の "Timeline"、既定 slug `timeline`）— レイヤーごとの状態 / ベイク値 sink と、アナログ / Gaze チャネルの入力源を登録する
- `FacialExpressionTrack` / `FacialExpressionClip` — トラック名をレイヤー名として Expression の on/off を区間で駆動。子トラックは同一レイヤーの追加レーン
- `FacialValueTrack` / `FacialValueClip` — `AnimationCurve` でアナログ / Gaze の各軸を駆動。Gaze は live 入力源を再生中だけ乗っ取る
- `FacialTimelineReceiver` — Mixer と sink を仲介する MonoBehaviour。ベイク欠落 / ハッシュ不一致時の劣化動作と Editor への通知
- `FacialTimelineBakeAsset` と `TimelineBakeService` — TimelineAsset のサブアセットへ BlendShape 値と状態イベントをベイク。`TimelineBakeDirtyWatcher` が保存時 / Play 突入時に自動再ベイク
- Edit モードのスクラブでベイク結果を SkinnedMeshRenderer と目ボーンへ反映するプレビュー
- `RecToTimelineExporter` と **Tools → FacialControl → Timeline → REC Export** ウィンドウ — `.fcrec` を Expression クリップと Value トラックへ変換
- `FacialTimelineValidator` と Track / Clip の Custom Editor — Expression id 欠落・Gaze 範囲外・空クリップ・空親トラックを Timeline 上で警告
- 定常再生 / スクラブの GC アロケーション 0 gate と、Timeline 再生と live 入力の post-blend 等価性を検証する PlayMode テスト
