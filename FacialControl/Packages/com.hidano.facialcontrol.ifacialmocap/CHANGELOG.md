# Changelog

[Keep a Changelog](https://keepachangelog.com/ja/1.1.0/) の形式に準拠し、[セマンティックバージョニング](https://semver.org/lang/ja/) に従う。

## [Unreleased]

### Added

- BlendShape Mappings の 1 件ごとに Enabled トグル・Range（`MinMaxSlider`、0〜1）・Weight を追加。受信値に「Range の再マップ（`(v - Min) / (Max - Min)` を 0〜1 にクランプ）→ ×Weight → 0〜1 クランプ」の順で適用し、Enabled がオフのマッピングは値を出力しない（マッピング自体は残る）。調整値の計算は `IFacialMocapValueTuning`
- 調整項目の追加前に保存されたマッピング（`tuningVersion` 0）と Inspector の「+」で追加した要素は既定値（Enabled / 0〜1 / 1）として扱い、従来と同じ出力になる。Drawer で調整値を編集すると `tuningVersion` が書き込まれる

## [1.0.0] - 2026-09-25

初回リリース。

### Added

- `IFacialMocapReceiverAdapterBinding`（"iFacialMocap Receiver"）— iFacialMocap の UDP テキストプロトコル（標準 `-` / v2 `&`）を受信し、ARKit 互換 52 BlendShape（`<slug>`）、視線（`<slug>:gaze.left` / `.right`）、頭部（`<slug>:head`）を入力源として登録
- `IFacialMocapReceiverHost` — 受信スレッドでの UDP listen、ハンドシェイク送信、最新フレーム保持
- `IFacialMocapPacketParser` / `IFacialMocapBlendShapeCatalog` / `EyeGazeConverter` など Unity 非依存のプロトコル層
- `IFacialMocapRuntimeSettingsSO` と `IFacialMocapOptionsDto` — 環境依存設定の sub-asset 化と JSON 相互変換
- UI Toolkit の Drawer（Runtime Settings / BlendShape Mappings / Gaze 反転）
- サンプル `IFacialMocapReceiverDemo`（実装 Scene と受信疎通確認用の診断 bootstrap）
