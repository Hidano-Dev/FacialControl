# com.hidano.facialcontrol.timeline ドキュメント

使い方（4 手順）、Receiver Inspector の診断の読み方、旧設定の移行、既知の制約は [パッケージ README](../README.md) を参照。ここでは内部の id 規約、再生セッション、ベイク、REC 書き出しの詳細を補足する。

## 4 手順のおさらい

1. REC で録画する
2. **Tools → FacialControl → Timeline → REC Export** で TimelineAsset を書き出す（出力は TimelineAsset 1 つで完結）
3. PlayableDirector に TimelineAsset をセットする（トラック binding は自動）
4. FacialController と同じ GameObject に `FacialTimelineReceiver` を追加する

前提は Profile SO の Adapter Bindings に `TimelineAdapterBinding`（Slug と Enabled だけを持つ受信許可フラグ）が入っていること。Profile に Timeline 専用の設定は書かない。

## 再生セッション

`FacialTimelineReceiver` が再生セッションの集約点になる。Mixer は Play 中に毎フレーム `BeginPlaybackSession(timeline, director)` を呼び（冪等）、セッションが `Active` のときだけ sink へ書く。Edit ではセッションを開始せず、プレビューだけを描く。

| 状態 | 意味 |
|---|---|
| `Idle` | セッション無し（初期状態 / `ReleaseAll` 後） |
| `Pending` | FacialController の初期化待ち。ログは出さずに次のフレームで再試行する |
| `Active` | 再生中 |
| `Failed` | 再生できない原因が確定した（`ReleaseAll` まで再試行しない）。原因は診断の Error として残る |

`Failed` の判定順: (1) binding 未接続・無効、Receiver が FacialController と別の GameObject、別 Director との競合 → (2) Bake 参照が Conflict / LegacyExport → (3) 旧 `:state` 宣言（`LegacyStateDeclaration`）。Bake の Profile 内容ハッシュ不一致は `ProfileMismatch`、Profile は一致して Timeline 構造だけ違えば `BakeStale` で、どちらも Warning のまま `Active` を維持し Bake の値を出す。

セッション開始時の処理:

1. `TimelineAssetScanner` が TimelineAsset を Unity 非依存のトラック記述子列に写し、`TimelineChannelDeriver` が Profile と照合してレイヤー（root の Facial Expression Track のうち名前が Profile のレイヤーと一致するもの）とチャネル（root の Facial Value Track）を導出する
2. `FacialTimelineBakeLocator` が全 Facial トラックの Bake 参照の一致を検証する（Found / Missing / LegacyExport / Conflict / OverrideUsed）
3. `TimelineLayerConnector` がレイヤーごとに値 sink / state sink を生成し、値 sink を registry に登録して FacialController のレイヤーへ weight 1 で後付け接続する。state sink は状態入力源（overlay suppress の active provider と REC の観測）としてだけ登録する
4. `TimelineChannelTakeover` が Analog / Gaze チャネルを `ChannelSubId` の registry エントリへ `Replace` で乗っ取る

`ReleaseAll`（Director の停止・Pause・Graph の破棄・Receiver の無効化 / 破棄）で乗っ取りの復元 → レイヤー接続の解放 → `Idle` の順に戻す。セッション資源（導出結果・sink・Bake → sink のバインディング）は `(TimelineAsset, Bake, Profile)` が同じ間プールし、Pause / Resume で作り直さない。セッション開始後の毎フレーム処理はヒープ確保しない。

## 入力源 id

| id | 型 | 役割 |
|---|---|---|
| `timeline:{layer}` | `TimelineBakedValueSink` | ベイク済み BlendShape 値（疎な ContributeMask 付き）。registry に登録し、レイヤー入力源へ後付け接続する |
| `timeline:{layer}:state` | `TimelineExpressionStateSink` | どの Expression がアクティブかの状態のみ。registry にもレイヤー入力源にも繋がず、状態入力源としてだけ登録する |
| `ChannelSubId`（例 `osc:lt`、`osc:gaze`） | `TimelineAnalogInputSource` / `TimelineGazeInputSource` | REC の入力源 id そのもの。既存の registry エントリを再生中だけ乗っ取る。独自 id での別途登録はしない |

`{layer}` はレイヤー名が `[a-zA-Z0-9_.-]` のみで `:` を含まず、state 形が 64 文字以内ならレイヤー名そのもの、それ以外（非 ASCII など）は `layer{index}` になる（`LayerSinkIdFallback` を Info で記録）。

Layer.inputSources に `timeline:{layer}` が宣言済みなら宣言の weight で接続されたものとして自動接続をスキップする（`LayerConnectionSkippedDeclared`）。`timeline:{layer}:state` の宣言は非互換で、検出したら何も接続せず `LegacyStateDeclaration`（Error）で止まる。Routing エディタの入力源 id 検証は `timeline:` 系の id を不正扱いしない。

## Mixer の動作

- `FacialTrackMixerBehaviour`: Play では順再生のフレーム差分で on/off イベントを発火し、シーク時はスタックを再構成して差分を発火する。同時にそのレイヤーのベイク値をサンプルする。Graph 停止 / Pause / Destroy で `ReleaseAll()`。Edit ではプレビュー bridge だけを呼ぶ
- `FacialValueMixerBehaviour`: アクティブなクリップ（重なりは後ろ優先）のカーブを評価し、乗っ取った Analog sink の `SetAxes` または Gaze sink の `Publish(x, y)` に渡す。クリップ外では sink を無効化

Analog の乗っ取りが消費者に届くのは、registry の差し替えを購読する消費者（core の `IRegistryAttachableAnalogConsumer` を実装した `AnalogExpressionInputSource` / `AnalogBlendShapeInputSource`）。InputSystem の analog expression 経路は binding 構築時に registry へ接続済みで、設定変更は要らない。

## ベイク

- `TimelineBakeService.Bake(timeline, profileAsset, sampleRate = 60)` が TimelineAsset のサブアセット `FacialTimelineBake`（Hierarchy 非表示）を生成 / 更新し、`BakeReferenceWriter` が root と子の全 Facial トラックに同じ Bake 参照を書く。Receiver の BakeAsset 欄は上書き用で、通常は空のままでよい
- Profile は Runtime と同じ読込経路（StreamingAssets の profile.json 優先、無ければ SO）で解決する（`TimelineProfileSource`）
- ハッシュは `FacialTimelineHashCalculator`（FNV-1a 64bit）。Profile 内容ハッシュ（SchemaVersion / Layers / LayerInputSources / Expressions（id 順）/ Slots / DefaultOverlays / BaseExpression / GazeChannels）を Bake の `ProfileContentHashHex` に保存し、Source ハッシュ（seed `facial-timeline-hash/v2`）は Timeline 構造 + Profile 内容ハッシュ + sampleRate から算出する
- `TimelineBakeService.IsStale` は `BakeStaleReason`（`None` / `ProfileChanged` / `TimelineChanged`）を返す
- Editor のイベント購読は `TimelineEditorServices`（`[InitializeOnLoad]`）が一元管理し、変更検知を `TimelineEditorServices.ChangeWatcher`（`TimelineEditChangeWatcher`）に流す。Clip / Track 編集・Undo / Redo・ObjectChange・Profile の JSON 書き出しを 0.3 秒デバウンスで合流させ、再ベイクを 1 回だけ実行する。未保存の TimelineAsset はスキップする
- Play 突入直前（ExitingEditMode）は Watcher の保留分を即時実行した後、シーン上の (Timeline, Profile SO) ごとに Profile の JSON 書き出し（AutoExport の冪等入口 `ExportIfEnabled`）→ Profile 解決 → 鮮度照合 → 必要なら再ベイク、を直列に行う
- Play 中に検出した不一致は Play 終了後（Edit 復帰時）に無言で修復し、Console に Info を 1 行出す。ダイアログは出さない

## Edit プレビュー

`TimelinePreviewCompositor` が controller / Profile / Bake からオフラインの `LayerUseCase` を構築し、Play と同じ合成規則で SkinnedMeshRenderer と目ボーンへ書く。Gaze チャネルは Bake の値チャネルの sub（REC の入力源 id）を、`GazeSourceIdConvention` のチャネル id または GazeChannel の明示 source id（左 / 右）との完全一致で解決する（トラック順に依存しない）。Analog チャネル経由の出力は Edit プレビューに出ない（既知の制約）。

## REC → Timeline 変換

`RecToTimelineExporter.TryExportTimelineAsset(recordingPath, profileAsset, outputAssetPath, out result, existingTimeline = null, sourceKindOverrides = null)`

- シーン上の PlayableDirector / FacialTimelineReceiver には触れない。出力の TimelineAsset には Bake サブアセットと全 Facial トラックの Bake 参照が入る
- Trigger on/off は Expression id ごとのスタックでペアにし `FacialExpressionClip` にする。閉じていないクリップは duration まで延ばす。レイヤーは `profile.GetEffectiveLayer`、見つからなければ `emotion` → 先頭レイヤー → `Expressions` の順でフォールバックして警告
- アナログ / Gaze は sourceId ごとに `FacialValueTrack` 1 本 + クリップ 1 つ。`ChannelSubId` には REC の sourceId がそのまま入り、再生時の乗っ取り先になる
- チャネル種別の判定は source id ごとに種別・理由・軸数を返す。判定順: GazeChannel の明示 source id と完全一致（`ExplicitGazeSourceId`）→ 規約 id のチャネルが GazeChannels にある（`ConventionGazeChannel`）→ Profile の binding の gaze 宣言（`GazeProviderDeclaration`）→ 2 軸でなければ Analog（`NonTwoAxisSamples`、警告して継続）→ 既定 Analog（`DefaultAnalog`）。`sourceKindOverrides` で上書きした場合は `Overridden`（プログラム・テスト用途。ウィンドウからは指定しない）
- 値提供型・系1 のレコード kind（7 / 9 / 10）は Export 対象外として無視する。これらの kind が含まれていても読込は失敗せず、変換可能なレコードの Export を継続する
- REC の baseline とトリガーの sourceId は変換しない
