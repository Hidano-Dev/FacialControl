# Changelog

[Keep a Changelog](https://keepachangelog.com/ja/1.1.0/) の形式に準拠し、[セマンティックバージョニング](https://semver.org/lang/ja/) に従う。

## [Unreleased]

### Added

- Gaze 広告（`/_facialcontrol/gaze`）に、チャネルごとの目ボーン path（`bone.left=<path>` / `bone.right=<path>`、送信側で指定した側のみ）と可動範囲（`range=<lookUp>,<lookDown>,<outerYaw>,<innerYaw>`、毎回）を属性ペアとして載せるようにした。`OscSenderAdapterBinding` は `IGazeChannelSettingsConsumer` で Profile の目線設定を受け取り、heartbeat のたびに設定の変化を確かめて、変わっていれば広告を組み直す
- `OscReceiverAdapterBinding` が広告の属性ペアを `IGazeChannelOverrideProvider` として公開し、`FacialController` がローカルの目線設定より優先して使う。FacialControl 同士の送受信では、受信側は目線タブを設定しなくても送信側と同じ目ボーン・可動範囲で目線が動く。属性ペアの解析は広告の中身が変わったときだけ行う
- `GazeChannelOverrideTable` — 広告の属性ペアからチャネルごとの上書きを保持し、内容が変わったときだけ version を進める

### Changed

- 属性ペアを知らない旧バージョンの受信側は、新しい送信側の広告を受け取ると未知の形式として警告を 1 回出してスキップする（route と目線の動作は従来どおり）

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
