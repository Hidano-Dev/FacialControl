# OscOutputDemo

`OscSenderAdapterBinding` の送信側サンプル。`OscOutputDemo.unity` を開き、お手持ちのキャラモデルを Scene に置いて Play すると、sin 波のデモ信号で動かした全 BlendShape と Gaze が OSC bundle として送信される。

## 同梱されているもの

| ファイル | 役割 |
|---|---|
| `OscOutputDemo.unity` | `FacialController` と `OscOutputDemoProfile` を結線済みの最小 Scene |
| `OscOutputDemoProfile.asset` | デモ信号源 binding（slug `demo`）と `OscSenderAdapterBinding`（slug `osc-output`）を持つ `FacialCharacterProfileSO` |
| `OscOutputDemoSettings.asset` | `OscRuntimeSettingsSO` を sub-asset に持つ `AdapterRuntimeSettingsCollectionSO`。endpoint / heartbeat / loopback 抑制はここ |
| `OscOutputDemoBootstrap.cs` | `Application.runInBackground = true` と、sin 波で `demo:blendshape` / `demo:gaze` を登録するデモ信号 binding |
| `OscSenderOptions.json` | 設定内容を JSON で表した参考ファイル（ランタイムは読まない） |

> キャラモデル（FBX / VRM / prefab）は同梱していない。

## 送信される内容

- VRChat 形式 endpoint `127.0.0.1:9000`: `/avatar/parameters/{BlendShape 名}` と `/avatar/parameters/gazeX` / `gazeY`
- ARKit 形式 endpoint `127.0.0.1:9001`: `/ARKit/{BlendShape 名}` と `eyeLook*` 8 アドレス
- BlendShape はモデルの全 BlendShape（binding の **BlendShape Names (Optional Filter)** が空のため）
- Gaze は Profile の目線タブに宣言された既定チャネル `gaze`
- heartbeat `/_facialcontrol/blendshape_names`（5 秒周期）、`/_facialcontrol/preset`、`/_facialcontrol/gaze` 広告、`/_facialcontrol/sender_id` を同梱
- loopback 抑制 ON

## 手順

1. `OscOutputDemo.unity` を開く
2. お手持ちのモデル prefab を Hierarchy の **`Character` の子** に配置する。`FacialController` が子の `SkinnedMeshRenderer` を自動探索する
3. 送信先を変えるときは `OscOutputDemoSettings.asset` の sub-asset **OscOutputSettings → Sender → Endpoints** を編集する
4. Play。受信側で `/avatar/parameters/...` または `/ARKit/...` が届くことを確認する

## 補足

- 一部の BlendShape だけ送りたい場合は `OscOutputDemoProfile.asset` の **OSC Sender → BlendShape Names (Optional Filter)** に名前を列挙する
- 同一プロセスで `OscReceiverDemo` も動かす場合は Settings の **Suppress Loopback** を OFF にする
- 受信側の自動マッピングは heartbeat 到着後に成立する。最初の数フレームは反映されない
- デモ信号 binding は動作確認専用。実運用では Input System / iFacialMocap などの入力源 binding に差し替える
