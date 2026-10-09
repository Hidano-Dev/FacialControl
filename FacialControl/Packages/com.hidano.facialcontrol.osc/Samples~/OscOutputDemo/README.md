# OscOutputDemo

`OscSenderAdapterBinding` の送信側サンプル。`OscOutputDemo.unity` を開き、お手持ちのキャラモデルを Scene に置いて Play すると、sin 波のデモ信号で動かした全 BlendShape と Gaze が OSC bundle として送信される。

## 同梱されているもの

| ファイル | 役割 |
|---|---|
| `OscOutputDemo.unity` | `FacialController` と `OscOutputDemoProfile` を結線済みの最小 Scene |
| `OscOutputDemoProfile.asset` | デモ信号源 binding（slug `demo`）と `OscSenderAdapterBinding`（slug `osc-output`）を持つ `FacialCharacterProfileSO`。送信先リストは binding 本体にある |
| `OscOutputDemoBootstrap.cs` | `Application.runInBackground = true` と、sin 波で `demo:blendshape` / `demo:gaze` を登録するデモ信号 binding |
| `OscSenderOptions.json` | 設定内容を JSON で表した参考ファイル（ランタイムは読まない） |

> キャラモデル（FBX / VRM / prefab）は同梱していない。

## 送信される内容

- endpoint `127.0.0.1:9000` と `127.0.0.1:9001` の両方へ同じ内容を送る
- 毎フレーム `/_facialcontrol/sender_id` と値フレーム `/_facialcontrol/values`（BlendShape → gaze の X / Y の順に並べた値の配列）
- 対応表（BlendShape 名・gaze チャネル）は受信側の要求に応じて返す。BlendShape 名に 2 バイト文字や独自名があってもそのまま届く
- BlendShape はモデルの全 BlendShape（binding の **BlendShape Names (Optional Filter)** が空のため）
- Gaze は Profile の目線タブに宣言された既定チャネル `gaze`
- 対応表の gaze チャネルには目線タブの可動範囲と、指定されていれば目ボーン path を載せる。受信側はこれを自分の目線タブより優先するので、Humanoid 以外のモデルでも目線タブの設定は送信側だけでよい
- loopback 抑制 ON

## 手順

1. `OscOutputDemo.unity` を開く
2. お手持ちのモデル prefab を Hierarchy の **`Character` の子** に配置する。`FacialController` が子の `SkinnedMeshRenderer` を自動探索する
3. 送信先を変えるときは `OscOutputDemoProfile.asset` の **OSC Sender → 送信先** を編集する
4. Play。`OscReceiverDemo`（別プロセス、受信ポートを送信先に合わせる）で表情が再現されることを確認する

## 補足

- 一部の BlendShape だけ送りたい場合は `OscOutputDemoProfile.asset` の **OSC Sender → BlendShape Names (Optional Filter)** に名前を列挙する
- heartbeat 間隔と loopback 抑制は既定値のまま（上級設定アセットは割り当てていない）。変えたい場合は `AdapterRuntimeSettingsCollection` に `OscSenderRuntimeSettingsSO` を追加し、**OSC Sender → 上級設定** に割り当てる
- 同一プロセスで `OscReceiverDemo` も動かす場合は、上記の上級設定アセットで **Suppress Loopback** を OFF にする
- 受信側は対応表を受け取ってから値を反映する。最初の数フレームは反映されない
- デモ信号 binding は動作確認専用。実運用では Input System / iFacialMocap などの入力源 binding に差し替える
