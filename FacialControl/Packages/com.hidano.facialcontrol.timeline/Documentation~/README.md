# com.hidano.facialcontrol.timeline ドキュメント

使い方と概要は [パッケージ README](../README.md) を参照。ここでは入力源 id、ベイク、REC 書き出しの詳細を補足する。

## 登録される入力源 id

`TimelineAdapterBinding`（既定 slug `timeline`）は `OnStart` で次の入力源を core のレジストリに登録する。

| id | 型 | 役割 |
|---|---|---|
| `timeline:{layer}` | `TimelineBakedValueSink` | ベイク済み BlendShape 値（疎な ContributeMask 付き） |
| `timeline:{layer}:state` | `TimelineExpressionStateSink` | どの Expression がアクティブかの状態のみ。値は出さない |
| `timeline:{sub}` | `TimelineAnalogInputSource` | アナログチャネル。Timeline が publish している間だけ有効 |
| `timeline:gaze-{n}` | `TimelineGazeInputSource` | Gaze チャネルの診断用 id。実際の反映は `TakeoverSourceId` の乗っ取りで行う |

Gaze の source id 規約は core の `GazeSourceIdConvention`（`{slug}:{channelId}[.left|.right]`）に従い、binding は `IGazeSourceProvider` として `Sub` をチャネル id に宣言する。

## Mixer の動作

- `FacialTrackMixerBehaviour`: 順再生ではフレーム差分で on/off イベントを発火し、シーク時はスタックを再構成して差分を発火する。同時にそのレイヤーのベイク値をサンプルする。Graph 停止 / Pause / Destroy で `ReleaseAll()`
- `FacialValueMixerBehaviour`: アクティブなクリップ（重なりは後ろ優先）のカーブを評価し、アナログ sink の `SetAxes` または Gaze sink の `Publish(x, y)` に渡す。クリップ外では sink を無効化

## ベイク

- `TimelineBakeService.Bake(timeline, profile, sampleRate = 60)` が TimelineAsset のサブアセット `FacialTimelineBake` を生成 / 更新する
- ソースハッシュは `FacialTimelineHashCalculator`（FNV-1a 64bit、seed `facial-timeline-hash/v1`）で、トラック構造・クリップ・カーブ・Profile の Expression 定義・sampleRate から算出する
- `TimelineBakeDirtyWatcher` が TimelineAsset / Profile の保存時と Play 突入前に stale なベイクを再生成する。Play 中に検出したハッシュ不一致は Edit モード復帰時に修復してダイアログで報告する
- 劣化動作: ベイク欠落時は値の再生を止めて状態再生のみ継続（警告）。ハッシュ不一致時は古いベイクのまま再生（警告）

## REC → Timeline 変換

`RecToTimelineExporter.TryExportTimelineAsset(recordingPath, profile, outputAssetPath, ...)`

- Trigger on/off は Expression id ごとのスタックでペアにし `FacialExpressionClip` にする。閉じていないクリップは duration まで延ばす。レイヤーは `profile.GetEffectiveLayer`、見つからなければ `emotion` → 先頭レイヤー → `Expressions` の順でフォールバックして警告
- アナログは sourceId ごとに `FacialValueTrack` 1 本 + クリップ 1 つ。`ChannelSubId` には REC の sourceId がそのまま入るため、`TimelineAdapterBinding.channelDefinitions[].Sub` を同じ文字列にする
- Gaze 判定は、ウィンドウの override 指定、または Profile の Gaze チャネル（チャネル id / sourceIdLeft / sourceIdRight）との一致に加え、全サンプルが 2 軸であること。混在時はアナログにフォールバックして警告
- 値提供型・系1 のレコード kind（7 / 9 / 10）は Export 対象外として無視する。これらの kind が含まれていても読込は失敗せず、変換可能なレコードの Export を継続する
- REC の baseline とトリガーの sourceId は変換しない
