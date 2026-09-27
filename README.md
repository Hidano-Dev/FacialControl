# FacialControl

3D キャラクターの表情をリアルタイムに制御する Unity 向けライブラリのモノレポ。VTuber 配信でのフェイシャルキャプチャ連動、コントローラによる表情切り替え、GUI での表情 AnimationClip 作成を主なユースケースとし、コアと機能別サブパッケージを npmjs.com の `com.hidano` スコープで配布する。

## パッケージ構成

| パッケージ | 役割 | 追加の依存 |
|---|---|---|
| `com.hidano.facialcontrol` | コア。表情プロファイル・マルチレイヤー合成・遷移・Overlay・Gaze・Profile Inspector | `jp.hadashikick.vcontainer`（OpenUPM） |
| `com.hidano.facialcontrol.expression-creator` | Expression 作成ツール（BlendShape スライダーで AnimationClip をベイク、プレビュー PNG 書き出し） | `com.hidano.scene-view-style-camera-controller` |
| `com.hidano.facialcontrol.routing-editor` | 入力源とレイヤーの配線を編集するノードグラフ（Profile Inspector から起動） | なし |
| `com.hidano.facialcontrol.inputsystem` | Unity InputSystem 経由のキーボード / コントローラ入力 | `com.unity.inputsystem` |
| `com.hidano.facialcontrol.osc` | OSC 送受信（VRChat / ARKit 互換、自動マッピング） | `com.hidano.uosc` |
| `com.hidano.facialcontrol.lipsync` | uLipSync 連携（Windows） | `com.hidano.ulipsync-asio` |
| `com.hidano.facialcontrol.ifacialmocap` | iFacialMocap (iOS) 受信 | `.osc` |
| `com.hidano.facialcontrol.rec` | 入力の記録・再生 | なし |
| `com.hidano.facialcontrol.timeline` | Timeline トラックからの表情駆動、REC の Timeline 書き出し | `com.unity.timeline`, `.rec` |

入力・出力系のサブパッケージは **Adapter Binding** として `FacialCharacterProfileSO` の Inspector から Add する。Scene に追加の MonoBehaviour を置く必要はない（REC のみ `RecCharacterBinding` を Add Component する）。`expression-creator` / `routing-editor` は Editor ツールのみのパッケージで、ランタイムには何も追加しない。

## 動作要件

- Unity 6000.3 以降（Windows で検証。レンダーパイプライン非依存）
- `Animator` を持つキャラクターと BlendShape を持つ `SkinnedMeshRenderer`。FBX / VRM を問わず BlendShape 名で対応付ける

## インストール

`Packages/manifest.json` に scoped registry を追加し、必要なパッケージだけを `dependencies` に列挙する。

```json
{
    "scopedRegistries": [
        { "name": "npmjs", "url": "https://registry.npmjs.org", "scopes": ["com.hidano"] },
        { "name": "OpenUPM", "url": "https://package.openupm.com", "scopes": ["jp.hadashikick.vcontainer"] }
    ],
    "dependencies": {
        "jp.hadashikick.vcontainer": "1.17.0",
        "com.hidano.facialcontrol": "1.0.0",
        "com.hidano.facialcontrol.inputsystem": "1.0.0",
        "com.hidano.facialcontrol.osc": "1.0.0"
    }
}
```

Git URL で直接取り込む場合はモノレポのサブディレクトリを `?path=` で指定する。

```json
"com.hidano.facialcontrol": "https://github.com/NHidano/FacialControl.git?path=FacialControl/Packages/com.hidano.facialcontrol#v1.0.0"
```

## クイックスタート

### 最速: InputSystem サンプル

1. `com.hidano.facialcontrol.inputsystem` の **Multi Source Blend Demo** を Package Manager から Import
2. `Assets/Samples/FacialControl InputSystem/1.0.0/Multi Source Blend Demo/MultiSourceBlendDemo.unity` を開く
3. Hierarchy の **Character** の子にお手持ちのモデルをドラッグ
4. Profile の目線タブで **参照モデルから目ボーンを自動解決** を押し、Play。キーボード `1`〜`5` / ゲームパッドで表情、WASD / 左スティックで目線、LT / RT でアナログと Overlay

### ゼロから作る

1. **Create → FacialControl → Facial Character Profile** で Profile を作り、参照モデルを割り当てる
2. `com.hidano.facialcontrol.expression-creator` の **Tools → FacialControl → Expression 作成** で BlendShape スライダーから AnimationClip をベイクし、表情ライブラリタブに登録
3. Adapter Bindings タブで入力源（Input System / OSC Receiver / uLipSync / iFacialMocap Receiver / Timeline）を Add
4. キャラクターに **Add Component → FacialControl → Facial Controller** を付けて Profile を結線し、Play

```csharp
_facialController.Activate(expression);
_facialController.Deactivate(expression);
```

詳しい手順は [コアのクイックスタート](FacialControl/Packages/com.hidano.facialcontrol/Documentation~/quickstart.md) を参照。

## アーキテクチャ

```
FacialCharacterProfileSO ─┬─ 表情ライブラリ（Expression / Slots / Default Overlays）
                          ├─ レイヤー（優先度・排他モード・入力源 id と重み）
                          ├─ ベース表情
                          ├─ 目線（Gaze チャネル → 目ボーン）
                          └─ Adapter Bindings（入力源 / 出力先。slug で入力源 id を公開）
                                    │
FacialController（LateUpdate）──── 入力源を集約 → レイヤー合成 → SkinnedMeshRenderer へ書き込み
                                    └→ IFacialOutputBus（合成後の値を OSC Sender 等へ配信）
```

- コアは Domain / Application / Adapters / Editor のレイヤード構成で、asmdef で依存方向を固定する
- 各 binding のライフサイクルは VContainer のキャラクターごとの子スコープが駆動する。binding は `IInputSourceRegistry` に `<slug>` / `<slug>:<sub>` の id で入力源を登録し、レイヤーはその id を参照する
- 定常状態の毎フレーム処理は GC アロケーション 0 を PlayMode の gate テストで固定している。OSC 受信は uOSC を通さない自前の UDP 受信ループで、受信スレッドはメインスレッドをブロックしない
- 10 体同時制御をパフォーマンステストで検証している

## ドキュメント

| 内容 | 場所 |
|---|---|
| コア README / クイックスタート / JSON スキーマ | [`com.hidano.facialcontrol`](FacialControl/Packages/com.hidano.facialcontrol/README.md) |
| Expression 作成ツール | [`com.hidano.facialcontrol.expression-creator`](FacialControl/Packages/com.hidano.facialcontrol.expression-creator/README.md) |
| ルーティングエディタ | [`com.hidano.facialcontrol.routing-editor`](FacialControl/Packages/com.hidano.facialcontrol.routing-editor/README.md) |
| InputSystem | [`com.hidano.facialcontrol.inputsystem`](FacialControl/Packages/com.hidano.facialcontrol.inputsystem/README.md) |
| OSC | [`com.hidano.facialcontrol.osc`](FacialControl/Packages/com.hidano.facialcontrol.osc/README.md) |
| uLipSync | [`com.hidano.facialcontrol.lipsync`](FacialControl/Packages/com.hidano.facialcontrol.lipsync/README.md) |
| iFacialMocap | [`com.hidano.facialcontrol.ifacialmocap`](FacialControl/Packages/com.hidano.facialcontrol.ifacialmocap/README.md) |
| REC | [`com.hidano.facialcontrol.rec`](FacialControl/Packages/com.hidano.facialcontrol.rec/README.md) |
| Timeline | [`com.hidano.facialcontrol.timeline`](FacialControl/Packages/com.hidano.facialcontrol.timeline/README.md) |
| npm 公開手順 | [`docs/npm-publish.md`](docs/npm-publish.md) |
| 要件定義 / 技術仕様 / Backlog | [`docs/`](docs/) |

## 開発

Unity プロジェクトは `FacialControl/` 配下。各パッケージは `Packages/` に埋め込みで置かれ、EditMode / PlayMode テストを GitHub Actions（セルフホストランナー）で実行する。開発方針は [`CLAUDE.md`](CLAUDE.md) を参照。

## ライセンス

[MIT License](LICENSE)
