# クイックスタート

FacialControl で 3D キャラクターの表情をリアルタイム制御するまでの最短手順。用意するのは **`FacialCharacterProfileSO` 1 個** と、入力に応じたサブパッケージの binding だけで、JSON を手で触る必要はない。

> **最速確認**: `com.hidano.facialcontrol.inputsystem` を入れて Package Manager から `Multi Source Blend Demo` サンプルを Import し、Scene の `Character` にモデルを子として置くだけで動く。詳細はサンプルの README を参照。

## 1. インストール

| パッケージ | 役割 | 必須依存 |
|---|---|---|
| `com.hidano.facialcontrol` | コア | `jp.hadashikick.vcontainer`（OpenUPM） |
| `com.hidano.facialcontrol.expression-creator` | Expression 作成ツール（Editor のみ） | `com.hidano.scene-view-style-camera-controller` |
| `com.hidano.facialcontrol.routing-editor` | ルーティングエディタ（Editor のみ） | なし |
| `com.hidano.facialcontrol.inputsystem` | キーボード / コントローラ入力 | `com.unity.inputsystem` |
| `com.hidano.facialcontrol.osc` | OSC 送受信（VRChat / ARKit 互換） | `com.hidano.uosc` |
| `com.hidano.facialcontrol.lipsync` | uLipSync 連携（Windows） | `com.hidano.ulipsync-asio` |
| `com.hidano.facialcontrol.ifacialmocap` | iFacialMocap 受信 | `com.hidano.facialcontrol.osc` |
| `com.hidano.facialcontrol.rec` | 入力の記録・再生 | なし |
| `com.hidano.facialcontrol.timeline` | Timeline 連携 | `com.unity.timeline`, `.rec` |

`Packages/manifest.json` に scoped registry（`com.hidano` → npmjs、`jp.hadashikick.vcontainer` → OpenUPM）を追加し、必要なパッケージだけを `dependencies` に書く。具体例はパッケージ README を参照。

## 2. Profile を作る

1. Project ウィンドウで右クリック → **Create → FacialControl → Facial Character Profile**
2. Inspector 上部の **参照モデル** にキャラクター prefab を割り当てる。BlendShape 名の候補表示と、目ボーンの自動解決に使う
3. **表情ライブラリ** タブで Expression を追加する
   - **id**（スクリプトや入力 binding から参照する文字列）、**名前**、**所属レイヤー**、**AnimationClip**、**遷移時間** を設定
   - AnimationClip は `com.hidano.facialcontrol.expression-creator` の **Tools → FacialControl → Expression 作成** で、モデルを見ながら BlendShape スライダーを動かしてベイクできる
   - まばたきや音素を重ねたい場合は **Slots** を宣言し、**Default Overlays** と各 Expression の **Overlays** で Default / Suppress / Override を選ぶ
4. **レイヤー** タブでレイヤー名・優先度・排他モード・入力源 id を確認する。サブパッケージの binding を Add すると既定レイヤーが自動追加されるので、通常は手で書く必要はない
5. **Adapter Bindings** タブで **Add** から入力源を追加し、slug と設定を埋める（例: `Input System` → InputActionAsset とキーバインディング）

## 3. Scene に結線する

1. キャラクターのルート（`Animator` を持つ GameObject）を選択
2. **Add Component → FacialControl → Facial Controller** を追加
3. **Character SO** に 2 で作った Profile をドラッグ
4. Play。`OnEnable` で初期化され、`OnDisable` で後始末される

BlendShape を持つ `SkinnedMeshRenderer` は子階層から自動探索される。明示したい場合は `Skinned Mesh Renderers` に割り当てる。

> **JSON について**: Profile を編集するたび Editor が `StreamingAssets/FacialControl/{SO 名}/profile.json` を書き出す。Play 突入時とビルド時にも全 Profile を再書き出しするので、ランタイムが読む JSON は常に最新になる。ビルド後に表情を差し替えたい場合だけ、この JSON を置き換える。

## 4. 目線（Gaze）

目線は Expression ではなく、Profile の **目線** タブにある独立したチャネルで扱う。

1. 既定チャネル `gaze` をそのまま使う（複数チャネルは上級者向け）
2. **入力ソース** ドロップダウンで、利用する binding が宣言した Gaze 入力源を選ぶ。空欄なら登録済みの入力源から自動解決される
3. 目ボーン path は任意。空欄のままなら、実行時に Humanoid Avatar の目ボーン（`LeftEye` / `RightEye`）を使い、初期回転と yaw / pitch 軸もその場で導出する。非 Humanoid モデルや Eye 未マップのモデルでは、**参照モデルから目ボーンを自動解決** を押すか path を手入力する（自動解決は Humanoid の Eye ボーン、無ければ名前検索から、左右の目ボーン path・初期回転・yaw / pitch 軸を保存する）
4. 上下 / 外側 / 内側の可動角は必要に応じて調整する

目ボーンへの適用は `FacialController` が毎フレーム行う。binding 側は Vector2 入力源を登録するだけでよい。

## 5. スクリプトから表情を切り替える

```csharp
using Hidano.FacialControl.Adapters.Playable;
using UnityEngine;

public class MyExpressionController : MonoBehaviour
{
    [SerializeField] private FacialController _facialController;

    private void TriggerSmile()
    {
        if (!_facialController.IsInitialized || !_facialController.CurrentProfile.HasValue)
            return;

        var profile = _facialController.CurrentProfile.Value;
        var smile = profile.FindExpressionById("smile");
        if (smile.HasValue)
            _facialController.Activate(smile.Value);
    }
}
```

| メソッド | 説明 |
|---|---|
| `Activate(Expression)` / `Deactivate(Expression)` | Expression の on / off（レイヤーの排他モードに従う） |
| `LoadCharacter(FacialCharacterProfileSO)` | Profile を切り替えて再初期化 |
| `ReloadProfile()` | 現在の Profile を再読み込み |
| `GetActiveExpressions()` | アクティブな Expression の一覧 |
| `SetLayerWeight(string layerName, float weight)` | レイヤー全体の重み |
| `SetInputSourceWeight(int layerIdx, int sourceIdx, float weight)` | レイヤー内の入力源の重み（`sourceIdx` 0 は内部 Expression 用） |
| `SetActiveBoneSnapshots(ReadOnlyMemory<BoneSnapshot>)` | ボーンポーズの上書き |

| プロパティ | 型 | 説明 |
|---|---|---|
| `IsInitialized` | `bool` | 初期化済みか |
| `CurrentProfile` | `FacialProfile?` | 現在の Profile |
| `CharacterSO` | `FacialCharacterProfileSO` | 結線中の Profile アセット |
| `InputSourceRegistry` | `IInputSourceRegistry` | binding が登録した入力源 |

## 6. ARKit / PerfectSync

ARKit 52 / PerfectSync 命名の BlendShape を持つモデルは、キャプチャ入力の binding が BlendShape 名で直接駆動する。iFacialMocap などのキャプチャアプリから受け取る場合は `com.hidano.facialcontrol.ifacialmocap` を使う。`com.hidano.facialcontrol.osc` は FacialControl 同士の送受信専用で、外部の OSC 送信元は受けない。命名の判定には core の `ARKitDetector`（ARKit 52 + PerfectSync 13 の名前表と完全一致検出）を使う。

## トラブルシューティング

- **表情が変化しない**: `FacialController` の Character SO が空でないか、モデルの BlendShape 名が Expression の Clip と一致しているか、ルートに `Animator` があるかを確認
- **入力源 id の警告が出る**: レイヤーの `inputSources[].id` と binding の slug（`<slug>` / `<slug>:<sub>`）が一致していない。Adapter Bindings タブの slug か、ルーティングエディタ（`com.hidano.facialcontrol.routing-editor`）で配線を確認
- **同じモデルに `FacialController` が 2 つ付いている**: 祖先側だけが有効になり、他方は警告付きで無効化される。Inspector にも警告が出る
- **目線が動かない**: 非 Humanoid モデルなら目線タブで目ボーン path が入っているか（空欄で Humanoid の目ボーンも解決できないときは起動時に警告が 1 回出る）、入力ソースが binding の宣言と一致しているかを確認

## 次のステップ

- [JSON スキーマ](json-schema.md) — ビルド後の差し替えや外部ツール連携向け
- [Adapter Runtime Settings](adapter-runtime-settings.md) — endpoint やデバイス名など環境依存設定の分離
