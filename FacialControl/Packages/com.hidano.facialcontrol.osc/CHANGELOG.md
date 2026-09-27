# Changelog

[Keep a Changelog](https://keepachangelog.com/ja/1.1.0/) の形式に準拠し、[セマンティックバージョニング](https://semver.org/lang/ja/) に従う。

## [1.0.0] - 2026-09-25

初回リリース。

### Added

- `OscReceiverAdapterBinding`（"OSC Receiver"）— VRChat / ARKit 互換アドレスで BlendShape と Gaze を受信。mode 別 mapping（`Normal_BlendShape` / `Gaze_VRChat_XY` / `Gaze_ARKit_8BS`）、bundle の atomic swap、staleness fail-safe、sender identity によるゾンビ送信元排除、listen ポートの自動繰り上げ
- heartbeat（`/_facialcontrol/blendshape_names`）と Gaze 広告（`/_facialcontrol/gaze`）による自動マッピング。手入力 mapping との共存
- `OscSenderAdapterBinding`（"OSC Sender"）— 合成後の BlendShape と Profile の Gaze チャネルを OSC bundle として複数 endpoint へ送信。プリセット通知、heartbeat、loopback 抑制、MTU 分割
- `OscRuntimeSettingsSO` — Receiver / Sender の環境依存設定を `AdapterRuntimeSettingsCollectionSO` の sub-asset として保持
- uOSC を通さない自前の UDP 受信ループとゼロアロケーション寄りのパーサ。受信スレッドは Unity API を呼ばない
- `ArKitOscAdapterBinding`（"ARKit / PerfectSync"、実験的）
- UI Toolkit の Drawer（Receiver / Sender / ARKit）と JSON DTO
- サンプル `OscOutputDemo` / `OscReceiverDemo`
- PlayMode テスト — 実 UDP 送受信、GC アロケーション、bundle MTU 分割、staleness と重み合成の原子性
