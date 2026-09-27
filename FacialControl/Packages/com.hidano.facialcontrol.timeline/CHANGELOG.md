# Changelog

[Keep a Changelog](https://keepachangelog.com/ja/1.1.0/) の形式に準拠し、[セマンティックバージョニング](https://semver.org/lang/ja/) に従う。

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
