# FacialControl Routing Editor

`com.hidano.facialcontrol` のルーティングエディタ。`FacialCharacterProfileSO` の Adapter Binding が公開する入力源 id と、レイヤーの `inputSources[]` の対応関係をノードグラフで確認・編集する Editor 拡張。slug を手打ちせずに配線でき、解決できない id は孤立ノードとして可視化される。

## 依存パッケージ

| パッケージ | バージョン | 用途 |
|---|---|---|
| `com.hidano.facialcontrol` | 1.0.0 | Profile / Adapter Binding の定義と、配線ロジック（`Editor/Windows/Routing/Logic`）。Inspector の起動点 `RoutingEditorLauncher` |

## 開き方

`FacialCharacterProfileSO` の Inspector 上部にある **ルーティングを編集** ボタンを押す。ボタンは本パッケージを導入したときだけ表示される（core 単体では非表示）。

## グラフの見方

| ノード | 内容 |
|---|---|
| **Adapter** | Profile の Adapter Bindings に Add した binding。出力ポートが binding の公開する入力源 id（`<slug>` / `<slug>:<sub>`）。自動配線に対応する binding は Auto Wire できる |
| **Layer** | Profile のレイヤー。名前・優先度・排他モード（`lastWins` / `blend`）・override mask をノード上で編集できる。入力ポートに繋いだエッジが `inputSources[]` の宣言 |
| **Composite Output** | レイヤーの合成順（優先度順）。Layer からの合成エッジは構造上常に存在するため削除できない |
| **孤立ノード** | レイヤーが宣言しているが、どの binding も公開していない入力源 id。表示のみで削除できず、Profile も改変しない。slug の打ち間違いや binding の削除漏れを見つけるためのもの |

## 操作

- **エッジを作る**: Adapter の出力ポートから Layer の入力ポートへドラッグ。レイヤーに入力源宣言が追加される
- **エッジを消す**: エッジを選択して Delete。宣言が削除される
- **重みを変える**: エッジのポート上のスライダーで `inputSources[].weight` を編集する。ドラッグ中は 1 つの Undo にまとめられる
- **Undo / Redo**: すべての編集は `SerializedObject` 経由で Profile に書き込まれ、Unity の Undo に対応する。Inspector 側の変更もウィンドウに即時反映される

## core との境界

配線の列挙・検証・書き込みロジック（`SourcePortEnumerator` / `RoutingGraphModelBuilder` / `WiringSerializedMapper` / `AutoWireService` / `InvalidIdValidator` / `LayerPriorityNormalizer` / `PhonemeSlotInitializer`）は core の Inspector も使うため core 側に残している。本パッケージは GraphView の描画とイベント処理だけを担い、書き込みは core の `IWiringSerializedMapper` に委譲する。

core Editor は本パッケージを参照しない（参照すると asmdef が循環する）。起動経路は本パッケージが `[InitializeOnLoad]` で `RoutingEditorLauncher.OpenHandler` に `RoutingEditorWindow.Open` を登録することで繋がる。

## ライセンス

[MIT License](LICENSE.md)
