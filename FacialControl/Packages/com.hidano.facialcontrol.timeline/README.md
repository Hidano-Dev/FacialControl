# FacialControl Timeline

`com.hidano.facialcontrol` の Unity Timeline 連携パッケージ。Timeline のトラックから表情の on/off・アナログ値・Gaze を駆動し、`com.hidano.facialcontrol.rec` で記録した演技を Timeline アセットへ書き出せる。

## 依存パッケージ

| パッケージ | バージョン | 用途 |
|---|---|---|
| `com.hidano.facialcontrol` | 1.0.0 | 入力源レジストリ・レイヤー合成・Gaze チャネル |
| `com.hidano.facialcontrol.rec` | 1.0.0 | REC → Timeline 書き出し（Editor のみ参照） |
| `com.unity.timeline` | 1.8.9 以上 | Track / Clip / Mixer |

Runtime asmdef は core と `Unity.Timeline` のみを参照し、rec への参照は Editor asmdef に閉じている。

## 使い方（4 手順）

前提: キャラクターに `FacialController` があり、`FacialCharacterProfileSO` の **Adapter Bindings** に **Timeline**（`TimelineAdapterBinding`、既定 slug `timeline`、**Enabled** オン）が入っていること。Profile に Timeline 専用の設定（レイヤー名・チャネル定義・`timeline:` の入力源宣言）は書かない。

1. **REC で録画する** — `com.hidano.facialcontrol.rec` で演技を `.fcrec` に記録する
2. **REC Export** — **Tools → FacialControl → Timeline → REC Export** で `.fcrec` と録画時の Profile SO を選び、TimelineAsset を書き出す。出力は TimelineAsset 1 つで完結する（Bake サブアセットと全 Facial トラックの Bake 参照を含む）。Director / Receiver の指定欄は無い
3. **PlayableDirector に TimelineAsset をセットする** — トラックの binding は設定しなくてよい（次の手順の Receiver が自動で埋める）
4. **FacialController と同じ GameObject に `FacialTimelineReceiver` を追加する**

これで Play を開始すると、表情（Expression のトリガーと BlendShape 値）・アナログ値・目線（Gaze）が録画どおりに再現される。

- レイヤーは **Facial Expression Track のトラック名 = Profile のレイヤー名** で自動導出される（子トラック `{layer} Lane n` は親レイヤーに畳まれる）。名前が一致しないトラックは診断 `TrackLayerUnmatched` で知らせる
- 値 sink（`timeline:{layer}`）は再生開始時にレイヤー入力源へ weight 1 で自動接続され、終了時に接続前の構成へ戻る。状態（どの Expression が on か）は overlay suppress と REC の観測にだけ供給され、レイヤー入力源にはならない
- アナログ / Gaze は **Facial Value Track** の `ChannelSubId`（REC の入力源 id `slug:sub` そのもの）が指す registry エントリを再生中だけ乗っ取り（`Replace`）、終了時に元へ戻す
- Director は Receiver の上書き欄 → 同じ GameObject → 親階層 → シーン走査の順で解決する。Track binding は空のトラックだけ自分に設定し、他オブジェクトを指す binding は触らない
- Profile SO の Adapter Bindings に Timeline が無い場合は Receiver が `BindingMissing` を出す。Timeline binding の **Enabled** をオフにすると Timeline からの受信を止める（`BindingDisabled`）

TimelineAsset を手で作る場合も規則は同じ。**Facial Expression Track** をレイヤー名で作り、**Facial Expression Clip** に Expression id を設定する。アナログ / Gaze は **Facial Value Track** の `ChannelSubId` を乗っ取り先の入力源 id にし、**Facial Value Clip** の `AnimationCurve` で各軸を描く。

## Clip 編集と自動再ベイク

Timeline ウィンドウで Clip を移動・トリム・追加・削除・Undo すると、0.3 秒のデバウンス後に Bake が 1 回だけ自動で作り直され、全 Facial トラックの Bake 参照も書き直される。Profile SO / profile.json の変更でも再ベイクされる。Play 突入直前は Profile の JSON 書き出し（AutoExport 有効時）→ 鮮度照合 → 必要なら再ベイク、の順で直列に処理するため、Play では controller が読むのと同じ Profile で焼いた Bake が使われる。未保存の TimelineAsset は再ベイクの対象外（Receiver Inspector が保存を案内する）。

Edit モードのスクラブは Play と同じレイヤー合成規則（オフラインの `LayerUseCase`）で SkinnedMeshRenderer と目ボーンへ反映する。

## Receiver Inspector の診断の読み方

`FacialTimelineReceiver` の Inspector は Play を待たずに構成を検査し、領域ごとに **重大度アイコン + 件名 + 直し方** を表示する（問題の無い領域は 1 行に畳む）。Play 中はセッション状態・接続レイヤー・乗っ取り一覧も表示する。

| 重大度 | 意味 |
|---|---|
| Error | 再生できない（セッションは `Failed`）。Console にも最初の 1 件だけ出る |
| Warning | 再生は続くが、結果が期待とずれる可能性がある。同じ原因は Play 1 回につき 1 度だけ Console に出る |
| Info / Ok | 参考情報 / 問題なし |

主な診断コード:

| 領域 | コード | 重大度 | 直し方 |
|---|---|---|---|
| Director | `DirectorMissing` / `TimelineNotBound` / `DirectorAmbiguous` | Error | Director を置く / TimelineAsset をセットする / 候補が複数なら Receiver の Director 上書き欄で指定する |
| TrackBinding | `TrackBindingAutoAssigned` / `TrackBindingForeign` | Info / Warning | 自動設定済み / 他オブジェクトを指すトラックは意図を確認する（「トラック binding を今設定」で空欄を埋める） |
| Bake | `BakeLegacyExport` / `BakeReferenceConflict` | Error | トラックの Bake 参照が無い / 食い違っている。Edit で開けば自動再ベイクされる。Play 前に直すなら「今再ベイク」または再 Export |
| Bake | `BakeMissing` | Warning | TimelineAsset に Facial トラックが無い。Facial Expression / Value Track を追加する |
| Bake | `BakeStale` / `UnsavedTimeline` | Warning / Info | 自動再ベイクを待つ / TimelineAsset を保存する |
| Bake | `BakeOverrideUsed` / `BakeOverrideDiffers` | Info / Warning | Receiver の Bake 上書き欄を使用中 / トラックの参照と異なる。意図しなければ空にする |
| Profile | `ProfileMismatch` | Warning | Bake を焼いた Profile と現在の Profile が違う。古い Bake のまま再生し、Edit で自動再ベイクされる |
| ProfileBinding | `BindingMissing` / `BindingDisabled` | Error | Profile SO の Adapter Bindings に Timeline を追加する / Enabled をオンにする |
| ProfileBinding | `BindingLegacyFields` | Warning | 旧 Target Layer Names / Channel Definitions が残っている（再生には使われない。保存すると消える） |
| LayerMatch | `TrackLayerUnmatched` / `LayerSinkIdFallback` | Warning / Info | トラック名を Profile のレイヤー名に合わせる / 非 ASCII などのレイヤー名には `timeline:layer{n}` 形の id を使う |
| LayerConnection | `LegacyStateDeclaration` | Error | 旧 `timeline:{layer}:state` 宣言を削除する（「旧 timeline 宣言を削除」ボタン、Undo 可） |
| LayerConnection | `LayerConnected` / `LayerConnectionSkippedDeclared` / `LayerConnectionFailed` | Info / Info / Warning | 接続済み / 旧 `timeline:{layer}` 宣言の weight を使用中 / controller の初期化とレイヤー名を確認する |
| Analog / Gaze | `*TakeoverAttached` / `*SourceNotFound` / `*Occupied` | Info / Warning / Warning | 乗っ取り中 / `ChannelSubId` の入力源が registry に無い / REC 再生などが占有中 |
| Placement | `ReceiverNotOnControllerObject` / `ControllerMissing` | Error | Receiver を FacialController と同じ GameObject に置く |
| Session | `SessionConflict` | Error | 同じ Receiver を別の Director が再生している。片方を止める |

ボタン: **トラック binding を今設定** / **今再ベイク** / **旧 timeline 宣言を削除**（`LegacyStateDeclaration` があるときだけ有効）。

## 旧設定からの移行

| 旧設定 | 対応 |
|---|---|
| Layer.inputSources の `timeline:{layer}:state` 宣言 | **削除が必須**。残っていると Play が `LegacyStateDeclaration`（Error）で止まる。Receiver Inspector の「旧 timeline 宣言を削除」で除去する（Undo 可） |
| Layer.inputSources の `timeline:{layer}` 値 sink 宣言 | 削除は任意。残っていれば宣言の weight で接続され（`LayerConnectionSkippedDeclared`）、自動接続はスキップする |
| binding の Target Layer Names / Channel Definitions | 再生には使われない。Play で 1 回警告し、Profile Inspector で保存すると空になる |
| 旧 Export の TimelineAsset（トラックに Bake 参照が無い） | Edit で開けば（Inspector 評価・プレビュー・保存・Play 突入のいずれか）自動再ベイクされ、全トラックに Bake 参照が書かれて新形式になる。Edit を経ずに Play すると `BakeLegacyExport` で止まる |
| 旧 Bake（Profile 内容ハッシュが空） | 最初は一度 `ProfileMismatch`（Profile 一致で Timeline 側だけ違えば `BakeStale`）の Warning になり、Bake の値のまま再生する。Edit に戻ると無言で再ベイクされ解消する |
| Receiver の BakeAsset / Director の手動設定 | 不要。設定してあれば上書きとして使われる（`BakeOverrideUsed`） |

## 既知の制約

- **Edit プレビューの Analog は analog expression 宣言のある binding だけ**。`IAnalogExpressionBindingDeclaration` を実装した binding（InputSystem）の消費者は Edit でも再現し、それ以外のアナログ消費者は Play でのみ反映される
- **InputSystem 以外で registry を購読しない analog 消費者には Timeline の Analog が届かない場合がある**。Timeline は registry の `Replace` でアナログ入力源を乗っ取るため、core の `AnalogExpressionInputSource` / `AnalogBlendShapeInputSource`（`IRegistryAttachableAnalogConsumer` で registry に接続済みのもの）には届くが、構築時に入力源を直接掴んだまま registry を購読しない独自の消費者は差し替えを追えない

## 再生時の挙動

- **表情と BlendShape 値**: Timeline は独立した入力源として登録され、live 入力とレイヤー上で合成される。優先関係はレイヤーの排他モードと入力源 weight に従う
- **停止時**: アクティブな Expression をすべて off にし、値・アナログ・Gaze の出力を無効化し、レイヤー接続と乗っ取りを元に戻す
- **毎フレームの確保**: セッション開始後の再生（`ProcessFrame`）はヒープ確保しない。Pause / Resume でもセッション資源を作り直さない

## REC からの書き出し

**Tools → FacialControl → Timeline → REC Export** で `.fcrec` を TimelineAsset に変換する。

- トリガーの on/off は Expression ごとに **Facial Expression Clip** になり、重なりは `{layer} Lane n` の子トラックへ振り分けられる
- アナログ / Gaze は入力源 id ごとに **Facial Value Track** 1 本になり、サンプルがキーフレームになる。`ChannelSubId` には REC の入力源 id がそのまま入る
- Gaze かどうかは自動で判定し、ウィンドウに入力源 id ごとの判定結果と理由を読み取り専用で表示する（判定順: GazeChannel の明示 source id と一致 → 規約 id が GazeChannels にある → binding の gaze 宣言 → 2 軸でなければ Analog → 既定 Analog）。トリガー専用の入力源は表示しない
- Export 完了後、ウィンドウに残りの手順（Director へのセット、Receiver の追加）を表示する
- 値提供型・系1 のレコード kind（7 / 9 / 10）は Export 対象外として無視される。これらの kind が含まれていても読み込みは失敗せず、変換可能なレコードの Export を継続する
- weight のレコード kind（レイヤー weight / 入力源 weight の時刻付きイベント 12 / 13、基準エントリ 14 / 15）も Export 対象外として無視される。weight を含む `.fcrec` も読み込みは失敗せず、トリガーとアナログ / Gaze だけが Export される。Timeline には weight を表すトラックが無いため、書き出した Timeline の再生では録画中の weight 変化は再現されない（再生中の weight はプロファイルの宣言値とライブの書込に従う）。時刻付き weight イベントを読み捨てたときは、Export 1 回につき 1 回だけ件数付きの Warning（`[RecToTimelineExporter] ... weight record(s) ...`）を出す。weight 変化まで含めて再現したい場合は REC の再生を使う
- 出力先は `Assets/` または `Packages/` 配下。既存アセットの上書きは確認ダイアログを出す
- REC の baseline（weight の基準エントリを含む）とトリガーの入力源 id は Timeline には変換されない

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
├── Domain/     # 導出（TimelineChannelDeriver）/ sink id 規約 / 診断モデル / FacialTimelineHashCalculator（FNV-1a 64bit）
├── Tracks/     # FacialExpressionTrack / FacialValueTrack
├── Clips/      # FacialExpressionClip / FacialValueClip
├── Playables/  # Mixer / ClipBehaviour
└── Adapters/   # TimelineAdapterBinding / FacialTimelineReceiver / Scanner / BakeLocator / LayerConnector / ChannelTakeover / 診断 Evaluator / 各 sink / FacialTimelineBakeAsset
Editor/         # TimelineEditorServices / TimelineEditChangeWatcher / TimelineBakeService / RecToTimelineExporter / RecTimelineExportWindow / Receiver Inspector / Validator / Edit プレビュー（Compositor）
Tests/          # EditMode 単体 + PlayMode（4 手順の e2e / Edit-Play 一致 / GC ゼロ gate / 劣化動作 / live 等価性）
```

サンプルは同梱しない。

## ライセンス

[MIT License](LICENSE.md)
