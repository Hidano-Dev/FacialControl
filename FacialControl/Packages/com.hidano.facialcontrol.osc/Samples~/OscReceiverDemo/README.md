# OscReceiverDemo

`OscReceiverAdapterBinding` の受信専用サンプル。`OscReceiverDemo.unity` を開き、お手持ちのキャラモデルを Scene に置いて Play すると、ポート `9000` で受信した OSC をモデルの BlendShape と目ボーンに反映する。

## 同梱されているもの

| ファイル | 役割 |
|---|---|
| `OscReceiverDemo.unity` | `FacialController` と `OscReceiverDemoProfile` を結線済みの最小 Scene |
| `OscReceiverDemoProfile.asset` | `OscReceiverAdapterBinding`（slug `osc`）を持つ `FacialCharacterProfileSO`。受信ポートは binding 本体にある。レイヤーは 1 つ。BlendShape・Gaze の割り当ては対応表から自動で作る |
| `OscReceiverDemoSettings.asset` | `OscReceiverRuntimeSettingsSO` を sub-asset に持つ `AdapterRuntimeSettingsCollectionSO`。staleness などの上級設定の例（割り当ては任意） |
| `OscReceiverDemoBootstrap.cs` | `Application.runInBackground = true` を有効化する helper |
| `OscReceiverOptions.json` | 上級設定の例を JSON で表した参考ファイル（ランタイムは読まない） |

> キャラモデル（FBX / VRM / prefab）は同梱していない。

## 受信内容

- 受信ポート `9000`、全インターフェースで受信（使用中なら空きポートへ自動繰り上げ）
- BlendShape: 送信側から受け取った値フレームの対応表で、名前が一致するモデルの BlendShape へ自動マッピング
- Gaze: 対応表の gaze チャネルから route を自動生成（左右共通）。目ボーン path と可動範囲も対応表で届き、受信側の目線タブより優先する。送信側で目ボーン path を指定していれば、受信側は目線タブを設定しなくてよい。送信側で path が未指定なら、受信側の目線タブの path → Humanoid の Eye ボーンの順で解決する
- staleness 1 秒で base 表情へ復帰（`RevertToBase`）、bundle は `AtomicSwap`

## 手順

1. `OscReceiverDemo.unity` を開く
2. お手持ちのモデル prefab を Hierarchy の **`Character` の子** に配置する
3. **目ボーンを設定**（Gaze を反映する非 Humanoid モデルで、送信側が目ボーン path を送っていない場合だけ。Humanoid で Eye がマップ済みなら不要）: `OscReceiverDemoProfile.asset` の **参照モデル** にモデルを割り当て、**目線** タブのチャネル `gaze` で **参照モデルから目ボーンを自動解決** を押す。自動解決できないモデルは左右の目ボーン path を手入力する
4. 受信ポートを変えるときは `OscReceiverDemoProfile.asset` の **OSC Receiver → 受信ポート** を編集する。staleness 等は **上級設定** に割り当てた `OscReceiverDemoSettings.asset` の sub-asset で変える（外せば既定値で動く）
5. Play。送信側（`OscOutputDemo` 等）から `127.0.0.1:9000` へ送ると反映される

FacialControl 以外の OSC 送信元（VRChat / ARKit 形式の名前つきアドレス）からは受けられない。

## トラブルシューティング

- **何も動かない**: `Character` 配下に `SkinnedMeshRenderer` があるか、対応表が届いているか（揃わないと警告ログが出る）、BlendShape 名が一致しているか（対応表を適用したログに一致数が出る）を確認
- **目線だけ動かない**: 送信側の目線タブにチャネルがあるか、非 Humanoid モデルなら送信側か受信側の目線タブに目ボーン path が入っているかを確認。送信側の path が受信側モデルに無い場合は警告ログが出る。Gaze だけ途絶した場合は最後の値を保持する
- **送信側と同居させる**: `OscOutputDemo` 側の **OSC Sender → 上級設定** に `OscSenderRuntimeSettingsSO` を割り当て、**Suppress Loopback** を OFF にする
