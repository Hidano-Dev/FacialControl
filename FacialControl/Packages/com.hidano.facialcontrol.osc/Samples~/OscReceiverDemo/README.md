# OscReceiverDemo

`OscReceiverAdapterBinding` の受信専用サンプル。`OscReceiverDemo.unity` を開き、お手持ちのキャラモデルを Scene に置いて Play すると、`127.0.0.1:9000` で受信した OSC をモデルの BlendShape と目ボーンに反映する。

## 同梱されているもの

| ファイル | 役割 |
|---|---|
| `OscReceiverDemo.unity` | `FacialController` と `OscReceiverDemoProfile` を結線済みの最小 Scene |
| `OscReceiverDemoProfile.asset` | `OscReceiverAdapterBinding`（slug `osc`）を持つ `FacialCharacterProfileSO`。レイヤーは 1 つ、`mappings` は空（自動マッピング前提） |
| `OscReceiverDemoSettings.asset` | `OscRuntimeSettingsSO` を sub-asset に持つ `AdapterRuntimeSettingsCollectionSO`。listen ポートや staleness はここ |
| `OscReceiverDemoBootstrap.cs` | `Application.runInBackground = true` を有効化する helper |
| `OscReceiverOptions.json` | 手動 mapping を含む設定例を JSON で表した参考ファイル（ランタイムは読まない） |

> キャラモデル（FBX / VRM / prefab）は同梱していない。

## 受信内容

- listen `127.0.0.1:9000`（使用中なら空きポートへ自動繰り上げ）
- BlendShape: 送信側 heartbeat とモデルの BlendShape 名の積集合から自動マッピング
- Gaze: `/_facialcontrol/gaze` 広告から `Gaze_VRChat_XY` / `Gaze_ARKit_8BS` の route を自動生成。目ボーンへの反映は Profile の目線タブの設定が必要
- staleness 1 秒で base 表情へ復帰（`RevertToBase`）、bundle は `AtomicSwap`

## 手順

1. `OscReceiverDemo.unity` を開く
2. お手持ちのモデル prefab を Hierarchy の **`Character` の子** に配置する
3. **目ボーンを設定**（Gaze を反映する場合）: `OscReceiverDemoProfile.asset` の **参照モデル** にモデルを割り当て、**目線** タブのチャネル `gaze` で **参照モデルから目ボーンを自動解決** を押す。自動解決できないモデルは左右の目ボーン path を手入力する
4. listen ポートを変えるときは `OscReceiverDemoSettings.asset` の sub-asset **OscReceiverSettings → Receiver → Listen Port** を編集する
5. Play。送信側（`OscOutputDemo` 等）から `127.0.0.1:9000` へ送ると反映される

## FacialControl 以外の送信元から受ける場合

heartbeat と Gaze 広告が無いため自動マッピングは働かない。`OscReceiverDemoProfile.asset` の **OSC Receiver → Mappings** に `Normal_BlendShape` entry（BlendShape 名と完全な OSC アドレス）と、必要なら `Gaze_VRChat_XY` / `Gaze_ARKit_8BS` entry を追加する。`OscReceiverOptions.json` に手動 mapping の記述例がある。

## トラブルシューティング

- **何も動かない**: `Character` 配下に `SkinnedMeshRenderer` があるか、送信側の heartbeat が届いているか、BlendShape 名が一致しているか（不一致は警告ログに出る）を確認
- **目線だけ動かない**: Gaze 広告が届いているか、目線タブの目ボーン path が入っているかを確認。外部送信元の場合は Gaze mapping を手動設定する。Gaze だけ途絶した場合は最後の値を保持する
- **送信側と同居させる**: `OscOutputDemo` 側の Settings で **Suppress Loopback** を OFF にする
