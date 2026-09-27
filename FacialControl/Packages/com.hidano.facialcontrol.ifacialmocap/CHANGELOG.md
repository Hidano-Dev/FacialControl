# Changelog

[Keep a Changelog](https://keepachangelog.com/ja/1.1.0/) の形式に準拠し、[セマンティックバージョニング](https://semver.org/lang/ja/) に従う。

## [1.0.0] - 2026-09-25

初回リリース。

### Added

- `IFacialMocapReceiverAdapterBinding`（"iFacialMocap Receiver"）— iFacialMocap の UDP テキストプロトコル（標準 `-` / v2 `&`）を受信し、ARKit 互換 52 BlendShape（`<slug>`）、視線（`<slug>:gaze.left` / `.right`）、頭部（`<slug>:head`）を入力源として登録
- `IFacialMocapReceiverHost` — 受信スレッドでの UDP listen、ハンドシェイク送信、最新フレーム保持
- `IFacialMocapPacketParser` / `IFacialMocapBlendShapeCatalog` / `EyeGazeConverter` など Unity 非依存のプロトコル層
- `IFacialMocapRuntimeSettingsSO` と `IFacialMocapOptionsDto` — 環境依存設定の sub-asset 化と JSON 相互変換
- UI Toolkit の Drawer（Runtime Settings / BlendShape Mappings / Gaze 反転）
- サンプル `IFacialMocapReceiverDemo`（実装 Scene と受信疎通確認用の診断 bootstrap）
