# com.hidano.facialcontrol

3D キャラクターの表情をリアルタイムに制御する Unity 向けコアパッケージ。表情の定義・レイヤー合成・遷移補間・Overlay・Gaze・Editor 拡張を提供し、入力源（コントローラ / OSC / リップシンク / フェイシャルキャプチャ / Timeline）は `com.hidano.facialcontrol.*` サブパッケージを **Adapter Binding** として追加する。

## 主な機能

- **1 アセット完結の設定** — `FacialCharacterProfileSO` に表情ライブラリ / レイヤー / ベース表情 / 目線 / Adapter Bindings をまとめて保存。JSON はランタイム用に自動書き出しされ、ユーザーが直接触る必要はない
- **マルチレイヤー合成** — レイヤーごとに優先度と排他モード（`lastWins` = クロスフェード / `blend` = 加算）を設定し、複数の入力源を重み付きで合成
- **表情遷移** — 遷移時間 0〜1 秒（既定 1/15 秒）、カーブは Linear / EaseIn / EaseOut / EaseInOut。遷移中の割り込みは現在値から新遷移を開始
- **Overlay slot** — まばたきや音素の口形状など「表情の上に重ねる」要素を slot 単位で管理。表情ごとに Default / Suppress / Override の 3 状態を選べる
- **Gaze チャネル** — Vector2 入力を目ボーンの yaw / pitch に変換。入力源は各 binding が宣言し、Profile の目線タブでドロップダウン選択
- **Adapter Binding 拡張点** — `AdapterBindingBase` 派生クラスに `[FacialAdapterBinding]` を付けるだけで Inspector の Add メニューに自動列挙。binding は `ctx.InputSourceRegistry.Register(slug, source)` で入力源を公開する
- **Editor ツール** — UI Toolkit の Profile Inspector、ARKit 52 / PerfectSync 検出ツール、新規プロファイル作成ダイアログ。BlendShape スライダーで AnimationClip を作る Expression 作成ツールは `com.hidano.facialcontrol.expression-creator`、入力源とレイヤーをノードで配線するルーティングエディタは `com.hidano.facialcontrol.routing-editor` として別パッケージで提供
- **毎フレーム GC ゼロ** — 定常状態の `LateUpdate` 全経路、10 体同時制御、Gaze 適用を PlayMode の gate テストで GC.Alloc = 0 に固定

## 動作要件

- Unity 6000.3 以降
- `Animator` を持つキャラクターと、BlendShape を持つ `SkinnedMeshRenderer`（子階層から自動探索）
- モデル形式は問わない。BlendShape 名で対応付けるため FBX でも VRM でも動作する。VRM ランタイム（表情 / LookAt）が同じ BlendShape や目ボーンを書き換える場合は片方を無効にすること

## インストール

VContainer（OpenUPM 配布）に依存するため、npmjs と OpenUPM の 2 つの scoped registry を `Packages/manifest.json` に追加する。

```json
{
    "scopedRegistries": [
        { "name": "npmjs", "url": "https://registry.npmjs.org", "scopes": ["com.hidano"] },
        { "name": "OpenUPM", "url": "https://package.openupm.com", "scopes": ["jp.hadashikick.vcontainer"] }
    ],
    "dependencies": {
        "jp.hadashikick.vcontainer": "1.17.0",
        "com.hidano.facialcontrol": "1.0.0"
    }
}
```

| 依存パッケージ | バージョン | 用途 | 取得元 |
|---|---|---|---|
| `jp.hadashikick.vcontainer` | 1.17.0 以上 | キャラクターごとの DI スコープ（asmdef 参照。package.json には書けないため手動追加） | OpenUPM |

Expression 作成ツール（`com.hidano.facialcontrol.expression-creator`）とルーティングエディタ（`com.hidano.facialcontrol.routing-editor`）は Editor 専用の別パッケージ。必要なら `dependencies` に追加する。

## クイックスタート

1. Project ウィンドウで **Create → FacialControl → Facial Character Profile** を作成
2. Inspector 上部の **参照モデル** にキャラクターの prefab を割り当てる（BlendShape / ボーン名の候補表示と目ボーン自動解決に使う）
3. **表情ライブラリ** タブで Expression を追加し、AnimationClip を割り当てる。Clip は `com.hidano.facialcontrol.expression-creator` の **Tools → FacialControl → Expression 作成** のスライダーからベイクできる
4. **レイヤー** タブでレイヤーと入力源 id を設定する（サブパッケージの binding を Add すると既定レイヤーが自動追加される）
5. **Adapter Bindings** タブで入力源（Input System / OSC Receiver / uLipSync など）を **Add** し、slug と各設定を埋める
6. キャラクターの GameObject に **Add Component → FacialControl → Facial Controller** を追加し、Profile を結線
7. Play。Profile を編集するたび `StreamingAssets/FacialControl/{SO 名}/profile.json` が自動更新される

```csharp
// スクリプトから Expression を切り替える
var profile = _facialController.CurrentProfile.Value;
var smile = profile.FindExpressionById("smile");   // Expression? を返す
if (smile.HasValue)
{
    _facialController.Activate(smile.Value);
    // ... 解除するとき
    _facialController.Deactivate(smile.Value);
}
```

詳細は [Documentation~/quickstart.md](Documentation~/quickstart.md)、JSON の構造は [Documentation~/json-schema.md](Documentation~/json-schema.md) を参照。

## 入力源 id の規約

Adapter Binding は Inspector で Add した時点で displayName 由来の slug（例 `Input System` → `input-system`）を持ち、`<slug>` または `<slug>:<sub>` の id で入力源を登録する。レイヤーの `inputSources[].id` はこの id を参照する。slug は `^[a-zA-Z0-9_.-]{1,64}$`、同一 Profile 内で重複するとアセット保存がブロックされる。Gaze の入力源は `{slug}:{channelId}` または `.left` / `.right` 付きで宣言される（既定チャネル id は `gaze`）。

## Overlay slot の 3 状態

| suppress | snapshot | 意味 |
|---|---|---|
| false | なし | Profile の Default Overlays にフォールバック |
| true | なし | この表情の間は slot を抑制 |
| false | あり | この表情専用の snapshot で上書き |

slot 名は Profile の `slots[]` に宣言したものだけが有効。音素用の予約 slot `a / i / u / e / o` は **Phoneme slots を初期化** ボタンで追加できる。

## アーキテクチャ

```
Runtime/
├── Domain/        # 値オブジェクト・入力源の基底クラス・合成サービス・バス（Unity.Collections / UnityEngine に依存）
├── Application/   # ProfileUseCase / ExpressionUseCase / LayerUseCase
└── Adapters/      # FacialController / FacialCharacterProfileSO / JSON パーサ / 入力源レジストリ / DI スコープ / ボーン適用
Editor/            # UI Toolkit Inspector / ARKit 検出 / ルーティング配線ロジック（Windows/Routing/Logic）/ 自動エクスポート
Templates/         # default_profile.json
Samples~/          # MultiSourceBlendBasicSample
```

- `FacialController` は `LateUpdate` で入力を集約し、`SkinnedMeshRenderer.SetBlendShapeWeight` へ直接書き込む（PlayableGraph は使わない）。合成後の値は `IFacialOutputBus` に流れ、OSC 送信などの出力系 binding が購読する
- 同じ `SkinnedMeshRenderer` を複数の `FacialController` が掴んだ場合は祖先側だけを有効にし、他方を警告付きで無効化する
- binding のライフサイクル（`OnStart` / `OnTick` / `OnLateTick` / `OnFixedTick` / `Dispose`）は VContainer のキャラクターごとの子スコープが駆動する。例外を出した binding は以後スキップされる

## Editor メニュー

| メニュー | 内容 |
|---|---|
| Tools → FacialControl → 新規プロファイル作成 | 命名規則（VRM / ARKit）を選んで `profile.json` の雛形を生成 |
| Tools → FacialControl → ARKit 検出ツール | ARKit 52 / PerfectSync 命名を検出して Expression を自動生成 |
| Create → FacialControl → Adapter Runtime Settings Collection | 環境依存設定（OSC の endpoint 等）をキャラクター Profile から分離する sub-asset コンテナ |

**Tools → FacialControl → Expression 作成** と Profile Inspector の **ルーティングを編集** ボタンは、それぞれ `com.hidano.facialcontrol.expression-creator` / `com.hidano.facialcontrol.routing-editor` を追加すると現れる。ルーティングエディタは core の `RoutingEditorLauncher` に起動ハンドラを登録する方式で、core 側は拡張パッケージを参照しない。

## ドキュメント

- [クイックスタート](Documentation~/quickstart.md)
- [JSON スキーマ](Documentation~/json-schema.md)
- [Adapter Runtime Settings](Documentation~/adapter-runtime-settings.md)

## ライセンス

[MIT License](LICENSE.md)
