# FacialControl Timeline

`com.hidano.facialcontrol` の Unity Timeline 連携パッケージ。Timeline のトラックから表情の on/off・アナログ値・Gaze を駆動し、`com.hidano.facialcontrol.rec` で記録した演技を Timeline アセットへ書き出せる。

## 依存パッケージ

| パッケージ | バージョン | 用途 |
|---|---|---|
| `com.hidano.facialcontrol` | 1.0.0 | 入力源レジストリ・レイヤー合成・Gaze チャネル |
| `com.hidano.facialcontrol.rec` | 1.0.0 | REC → Timeline 書き出し（Editor のみ参照） |
| `com.unity.timeline` | 1.8.9 以上 | Track / Clip / Mixer |

Runtime asmdef は core と `Unity.Timeline` のみを参照し、rec への参照は Editor asmdef に閉じている。

## 使い方

1. `FacialCharacterProfileSO` の **Adapter Bindings** に **Timeline** を追加する（`TimelineAdapterBinding`、既定 slug `timeline`）
   - **Target Layer Names**: Timeline で駆動するレイヤー名（例 `emotion`）
   - **Channel Definitions**: アナログ / Gaze チャネル（`Sub` 名、軸数、`IsGaze`、Gaze の場合は乗っ取る live 入力源 id）
2. Play 開始時に binding が同 GameObject へ `FacialTimelineReceiver` を自動追加し、レイヤーごとに `timeline:{layer}` / `timeline:{layer}:state`、チャネルごとに `timeline:{sub}` の入力源を登録する
3. TimelineAsset に **Facial Expression Track** を追加し、トラック名をレイヤー名に合わせる。バインド先は `FacialTimelineReceiver`
   - **Facial Expression Clip** に Expression id を設定する。クリップの区間だけその Expression が on になる
   - 同じレイヤーに重なるクリップを置きたい場合は子トラックを追加する（同一レイヤーの追加レーンとして扱われる）
4. アナログ / Gaze は **Facial Value Track** を使う。`ChannelSubId` を binding の `Sub` に合わせ、**Facial Value Clip** の `AnimationCurve` で各軸を描く
5. TimelineAsset または Profile を保存すると、`TimelineBakeDirtyWatcher` が BlendShape 値を `FacialTimelineBake` サブアセットへ自動ベイクする。Edit モードのスクラブでもベイク結果がモデルに反映される

## 再生時の挙動

- **表情と BlendShape 値**: Timeline は独立した入力源として登録され、live 入力とレイヤー上で合成される。優先関係はレイヤーの排他モードと入力源 weight に従う
- **Gaze**: `TakeoverSourceId` で指定した live の Gaze 入力源を再生中だけ Timeline の値で乗っ取り、停止時に元へ戻す。指定先が見つからない、または REC 再生などに占有されている場合はそのチャネルを無効化して警告する
- **停止時**: アクティブな Expression をすべて off にし、値・アナログ・Gaze の出力を無効化する
- **ベイクが無い / 古い場合**: ベイク無しでは値の再生を止めて表情の on/off だけ続ける。ハッシュ不一致では古いベイクのまま再生し、Editor では Edit モード復帰時に自動修復する

## REC からの書き出し

**Tools → FacialControl → Timeline → REC Export** で `.fcrec` を TimelineAsset に変換する。

- トリガーの on/off は Expression ごとに **Facial Expression Clip** になり、重なりは `{layer} Lane n` の子トラックへ振り分けられる
- アナログ / Gaze は入力源 id ごとに **Facial Value Track** 1 本になり、サンプルがキーフレームになる。Gaze 判定は Profile の Gaze チャネル定義と照合し、ウィンドウ上で Auto / Analog / Gaze を上書きできる
- 出力先は `Assets/` または `Packages/` 配下。既存アセットの上書きは確認ダイアログを出す
- REC の baseline とトリガーの入力源 id は Timeline には変換されない

## 検証

`FacialTimelineValidator` が Timeline エディタ上でクリップとトラックを検証し、問題をエラー表示する。

| 種別 | 内容 |
|---|---|
| `MissingExpressionId` | Profile に無い Expression id |
| `GazeOutOfRange` | Gaze カーブが ±1 を超えている |
| `EmptyClip` | Expression id や カーブが空 |
| `EmptyParentTrack` | クリップの無い親トラック |

## 構成

```
Runtime/
├── Domain/     # TimelineStateEvent / FacialTimelineHashCalculator（FNV-1a 64bit）
├── Tracks/     # FacialExpressionTrack / FacialValueTrack
├── Clips/      # FacialExpressionClip / FacialValueClip
├── Playables/  # Mixer / ClipBehaviour
└── Adapters/   # TimelineAdapterBinding / FacialTimelineReceiver / 各 sink / FacialTimelineBakeAsset
Editor/         # TimelineBakeService / RecToTimelineExporter / RecTimelineExportWindow / Validator / DirtyWatcher / Edit モードプレビュー
Tests/          # EditMode 単体 + PlayMode（GC ゼロ gate / 劣化動作 / live 等価性）
```

サンプルは同梱しない。

## ライセンス

[MIT License](LICENSE.md)
