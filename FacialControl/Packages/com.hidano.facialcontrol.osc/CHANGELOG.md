# Changelog

[Keep a Changelog](https://keepachangelog.com/ja/1.1.0/) の形式に準拠し、[セマンティックバージョニング](https://semver.org/lang/ja/) に従う。

## [Unreleased]

### Added

- OSC Receiver / OSC Sender / ARKit の Adapter を折り畳んだ状態でも、Foldout ヘッダーに接続先の要約を表示するようにした。受信は `:9001`、送信は `127.0.0.1:9000`（複数なら `127.0.0.1:9000 他 2 件`）。ランタイムと同じく、空のホストは `127.0.0.1` とみなし、同じホスト:ポートは 1 件に数える。無効な送信先（`enabled` = false、ポート範囲外）は件数から除き、全件はツールチップに出す（無効・重複は印付き）。旧形式の `OscRuntimeSettingsSO` が割り当てられたままならその値を表示し、旧アセットで受信 / 送信が無効なら無効として表示する（旧アセット側の変更は Inspector を開き直すと反映）
- Gaze 広告（`/_facialcontrol/gaze`）に、チャネルごとの目ボーン path（`bone.left=<path>` / `bone.right=<path>`、送信側で指定した側のみ）と可動範囲（`range=<lookUp>,<lookDown>,<outerYaw>,<innerYaw>`、毎回）を属性ペアとして載せるようにした。`OscSenderAdapterBinding` は `IGazeChannelSettingsConsumer` で Profile の目線設定を受け取り、heartbeat のたびに設定の変化を確かめて、変わっていれば広告を組み直す
- `OscReceiverAdapterBinding` が広告の属性ペアを `IGazeChannelOverrideProvider` として公開し、`FacialController` がローカルの目線設定より優先して使う。FacialControl 同士の送受信では、受信側は目線タブを設定しなくても送信側と同じ目ボーン・可動範囲で目線が動く。属性ペアの解析は広告の中身が変わったときだけ行う
- `GazeChannelOverrideTable` — 広告の属性ペアからチャネルごとの上書きを保持し、内容が変わったときだけ version を進める
- `OscReceiverAdapterBinding` に **対象レイヤー**（`TargetLayer`）を追加した。起動時に、対象レイヤーの入力源宣言へ受信 slug を自動で補う（Profile アセットは書き換えない）。未指定ならプロファイルの先頭レイヤー、指定したレイヤーが無ければ補わず初期化のたびに警告を出す。slug がどこかのレイヤーに手で宣言済みなら何もしない（従来の手動宣言はそのまま動く）。Inspector ではプロファイルのレイヤー一覧から選ぶ

### Changed

- 受信 slug をどのレイヤーにも宣言していない既存の Profile でも、起動時に先頭レイヤー（対象レイヤー未指定時）へ受信値が合成されるようになった。意図して受信をレイヤーに繋いでいなかった場合は、対象レイヤーを選び直すか、受信値を入れたいレイヤーへ slug を手で宣言する
- 属性ペアを知らない旧バージョンの受信側は、新しい送信側の広告を受け取ると未知の形式として警告を 1 回出してスキップする（route と目線の動作は従来どおり）
- **破壊的変更**: OSC の受信ポートを `OscReceiverAdapterBinding` 本体、送信先リストを `OscSenderAdapterBinding` 本体に移した。Adapter Bindings から直接確認・変更できる。新規の Sender binding は送信先 1 件（`127.0.0.1:9000`）で始まる
- **破壊的変更**: `OscRuntimeSettingsSO` を受信用 `OscReceiverRuntimeSettingsSO`（`stalenessSeconds` / `failSafeMode` / `consistencyCheckWarnLog` / `bundleMode` / `bundleAccumulationTimeoutMs`）と送信用 `OscSenderRuntimeSettingsSO`（`heartbeatIntervalSeconds` / `suppressLoopback`）に分けた。どちらも binding の Foldout「上級設定」から任意で割り当て、未割り当てなら既定値で動く
- **破壊的変更**: 受信 IP（`listenEndpoint`）を廃止した。受信は常に全インターフェース（`0.0.0.0` 相当）で行う。loopback 抑制は、同じ Profile の Receiver と同じポートへの送信のうち、宛先が loopback か自機のインターフェースアドレス（LAN IP 等）のものを抑止する。`OscReceiverOptionsDto` からも `listenEndpoint` を削除した（旧 JSON に残っていても無視される）
- **破壊的変更**: 受信 / 送信の有効フラグ（`receiverEnabled` / `senderEnabled`）を廃止した。binding を置いたかどうかで判断する（送信先ごとの `enabled` は残る）
- `OscRuntimeSettingsSO` は既存アセットの移行専用として残し、Collection の Add 一覧には出さない（`HideInAdapterRuntimeSettingsMenuAttribute`）
- サンプル `OscOutputDemo` は上級設定アセットなしの構成にした（`OscOutputDemoSettings.asset` を削除）。`OscReceiverDemo` の設定アセットは `OscReceiverRuntimeSettingsSO` に置き換えた

### Fixed

- Linux で、OSC 受信ポートの占有判定（`OscPortResolver.IsPortAvailable`）が SO_REUSEADDR 付きで占有されたポート（uOSC 等）を空きと誤判定し、ポートの自動繰り上げが起きなかった。Unity の Mono は Unix で全ソケットに SO_REUSEADDR を既定で付けるため、プローブ側で明示的に外すようにした（Windows の挙動は変わらない）

### Migration

既存の Profile で binding に `OscRuntimeSettingsSO` が割り当てられている場合:

1. そのままでも動く。起動時は旧アセットの値（受信ポート / 送信先 / 上級設定 / 有効フラグ）を優先し、移行を促す警告を出す
2. Profile の Inspector で OSC Receiver / OSC Sender を開き、警告欄の **旧設定から移行** を押す。受信ポート・送信先が binding 本体へ写り、既定値と異なる上級設定があれば同じ Collection に `OscReceiverRuntimeSettingsSO` / `OscSenderRuntimeSettingsSO` を作って割り当てる（旧設定が単独のアセットファイルなら、同じフォルダに別アセットとして作る）。旧アセットへの参照は外れる。上級設定を保存できない場合は移行を中止し、何も変えない
3. 受信と送信の両方を移行したら、Collection に残った旧 `OscRuntimeSettingsSO` sub-asset は削除してよい
4. 旧アセットで `senderEnabled` を false にしていた Sender は、送信先をすべて無効 (`enabled` = false) にして移すので送信しない状態が保たれる。`receiverEnabled` を false にしていた Receiver は移行後に起動する（警告を出す）。不要なら binding を外す

コードから設定していた場合: `OscReceiverAdapterBinding.Settings` → `Port` と `AdvancedSettings`、`OscSenderAdapterBinding.Settings` → `Endpoints` と `AdvancedSettings` に置き換える。旧設定（`LegacySettings`）が割り当てられている間は、`Port` / `Endpoints` への書き込みより旧設定の値が優先される。受信ポートが 1〜65535 の範囲外なら Receiver は警告を出して起動しない。`OscReceiverAdapterBinding.Endpoint` は読み取り専用（常に `0.0.0.0`）になった。

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
