# rec-weight-coverage 実装ギャップ分析

## 1. 調査範囲と前提

対象仕様は `rec-weight-coverage` であり、`spec.json` の `language` は `ja`、phase は `requirements-approved` である。

調査対象:

- `.kiro/steering/product.md`
- `.kiro/steering/tech.md`
- `.kiro/steering/structure.md`
- `rec-weight-coverage/requirements.md`
- `rec-full-input-coverage/{requirements.md,design.md,research.md}`
- `rec-recording-playback/design.md`
- `rec-playback-input-exclusivity/design.md`
- core / rec / inputsystem / timeline の指定実装およびテスト

既存アーキテクチャは Domain → Application → Adapters のクリーンアーキテクチャであり、core は rec、InputSystem、Timeline の Runtime を参照しない。毎フレームの定常処理は GC zero を目標とし、記録・再生は per-`FacialController` の状態を持つ。

本分析では、実装を変更せず、要求と現状実装の差分、設計上の選択肢、設計フェーズへ持ち越す調査事項を整理する。

---

## 2. 現在の実装状態

### 2.1 レイヤー weight

現状の主経路は次のとおり。

```text
FacialController.SetLayerWeight(layerName, weight)
    → LayerUseCase.SetLayerWeight(layerName, weight)
        → clamp01
        → _layerWeights[layerName] を更新
        → _layerInterWeights[layerIdx] を更新
        → 次回 UpdateWeights / LayerBlender で消費
```

`InputSystemAdapterBinding.ApplyOverlayLayerWeights` は `InputActionAnalogSource` の値を読み、毎 LateTick `FacialController.SetLayerWeight` を呼ぶ。したがって InputSystem Runtime を変更しなくても、core の `SetLayerWeight` が遮断面と観測面になれば overlay 経路を包含できる。

現状、レイヤー weight 変更専用の observer、記録イベント、再生遮断、注入経路は存在しない。

### 2.2 入力源 weight

現状の主経路は次のとおり。

```text
FacialController.SetInputSourceWeight(layerIdx, sourceIdx, weight)
    → LayerUseCase.SetInputSourceWeight
        → LayerInputSourceWeightBuffer.SetWeight
```

バルク経路は次のとおり。

```text
FacialController.BeginInputSourceWeightBatch()
    → LayerUseCase.BeginInputSourceWeightBatch()
        → LayerInputSourceWeightBuffer.BeginBulk()
        → BulkScope.SetWeight(...)
        → Dispose / CommitBulk
```

`LayerInputSourceWeightBuffer` は NativeArray の read/write ダブルバッファを持ち、`SetWeight` は clamp 後の値を write buffer に書き、`Interlocked.Increment(_dirtyTick)` を行う。Aggregator のフレーム開始時 `SwapIfDirty` によって read buffer へ反映される。この構造により、入力源 weight は任意スレッドから書ける既存契約を持つ。

一方、観測面はまだ存在しない。Aggregator の既存 `ILayerSourceValueObserver` は、入力源が生成した pre-weight の BlendShape 値を観測する面であり、weight の最終実効値を通知する契約ではない。

### 2.3 `(layer, source)` スロット

`LayerInputSourceRegistry` は次の情報を保持している。

- `_sources[flatIndex]`
- `_slotIds[flatIndex]`
- `layerIdx`
- `sourceIdx`
- `_sourceCounts`
- scratch buffer

`_slotIds` は、通常はレイヤー宣言の入力源 ID、未指定時は `source.Id` である。`GetSlotId` と `FindSourceIndex` が存在するため、REC の対象識別子として利用できる基礎はある。

`BindLateInputSource` は `declaredId` を優先してスロットを検索し、同一スロットへ Replace する。既存コメントにも、remove + append によるスロット詰まりと weight 列のずれを避ける意図が明記されている。このため REC の slot key は source オブジェクト参照や現在の source instance ではなく、少なくとも次の論理キーにすべきである。

```text
(layer stable id, declared slot id)
```

ただし現状の profile layer は主に配列 index と layer name で扱われており、独立した不変 layer ID の有無は未確認である。

### 2.4 Replace / Unregister

registry の `Replace` は同一登録キーを同一 registry slot に置換する。`LayerUseCase.BindLateInputSource` も同一 `slotId` の場合は `TryReplaceSource` を使い、weight を同じ slot に再設定する。

`UnbindLateInputSource` は registry の後続 slot 詰めに対応して weight buffer も詰め直す。したがって slot index は runtime の削除後には不変ではない。記録識別子を `(layerIdx, sourceIdx)` のみとすると、Unregister / compact 後に別 source を指す危険がある。

### 2.5 現在の observer 面

既存 observer 面は次のとおり。

- `IFacialInputObservationBus`
  - trigger
  - analog / gaze
  - value provider
  - expression activation
- `ILayerSourceValueObserver`
  - Aggregator が source の pre-weight 値を同期通知
- `IFacialOutputObserver`
  - post-blend 出力
- `ITriggerEventObserver`
  - trigger 系イベント

`ILayerSourceValueObserver` は、次の理由で weight 記録の直接の代替にはならない。

1. source 値と weight が別経路である。
2. weight=0 や source invalid の場合でも weight の変更を意味論的に観測する必要がある。
3. 1 フレーム内の複数書込を final weight に畳み込む必要がある。
4. layer weight は Aggregator より上流の `LayerBlender.LayerInput.Weight` に入る。
5. 任意スレッド書込を観測時点まで安全に保持する必要がある。

### 2.6 REC の記録・再生

`RecordingUseCase` は `IFacialInputObservationBus` を購読し、各入力イベントを `RecEvent` に変換する。既存の baseline は trigger、analog、value provider、expression を保持する。

`PlaybackUseCase` は現在、次の 4 ポートを管理している。

- trigger
- expression
- analog / gaze
- value provider

再生開始時は、

1. `RecTimelineSeek.BuildBaselineAt`
2. 4 ポートの preflight
3. 4 ポートの `TryBeginInjection`
4. 失敗時の逆順 rollback
5. scheduler load

という構造になっている。weight 対応では 5 番目のポートまたは同等の coordinator が必要になる。

`StopPlayback` が唯一の解放点となる既存方針、自然完了時は排他を維持する方針、開始途中の失敗で部分的排他を残さない方針を weight に拡張する必要がある。

### 2.7 `.fcrec` フォーマット

現状:

- `formatVersion = 1`
- header は 16 byte
- `RecHeaderFlags.FullInputBaseline = 0x0001`
- `RecBinaryFormat.TryRead` は header 検査後にレコードを読む
- core reader は未知 kind をエラーとする
- timeline の `RecEventSequenceAdapter` は Timeline に表現できない既存 kind を一部無視する

weight 用の kind は未定義である。weight baseline を持たない旧構造を受け入れると、再生時にライブ weight が残るため、要求 7.4 に反する。

`formatVersion` を 1 のまま据え置く既存方針と整合するには、header の予約 flags に weight baseline 必須ビットを追加し、旧 flags のファイルをレコード解析前に拒否する必要がある。

---

## 3. Requirement-to-Asset Map

凡例:

- **[Missing]**: 現状に必要な能力が存在しない
- **[Unknown]**: 設計上の確定情報または実装契約が不足
- **[Constraint]**: 既存仕様・境界による制約
- **[Partial]**: 一部能力は存在するが要求を満たさない

| Req | 主な資産 | 現状 | ギャップ / 設計論点 |
|---|---|---|---|
| 1 | `FacialController`, `LayerUseCase`, `LayerInputSourceWeightBuffer`, `BindLateInputSource`, `ApplyOverlayLayerWeights`, catalog / gate tests | [Partial] 書込経路は実装済みだが、weight 経路を一元的に分類する正本はない | [Missing] 全書込経路の観測・遮断分類。Replace / late-bind の weight 再設定を REC 自身の内部処理として扱うか明示が必要 |
| 2 | `ILayerSourceValueObserver`, `IFacialInputObservationBus`, `LayerInputSourceAggregator`, `LayerBlender` | [Partial] source 値・出力値の observer はあるが、weight observer はない | [Missing] layer weight と source weight の final 値 observer。[Unknown] layer identity と slot identity の安定契約 |
| 3 | `RecordingUseCase`, `IRecEventSink`, `RecStreamWriter` | [Missing] weight event の受付・差分抑制・float bit 保存がない | weight event の時刻順序、同一フレームの coalesce、再生中の注入イベント再記録を定義する必要 |
| 4 | `RecBaselineState`, `RecBaselineCapture`, `RecTimelineSeek`, `PlaybackUseCase` | [Partial] 既存 baseline / seek 畳み込みはあるが weight fields がない | [Missing] 全 layer と全開始時 slot の baseline、baseline 不在対象の確定値、観測者非通知 |
| 5 | `ExpressionTriggerInputSourceBase` 等の遮断、`PlaybackUseCase` lifecycle | [Missing] weight 用 live write gate がない | `SetLayerWeight`、単発 source weight、bulk commit を core 側で一貫遮断。任意スレッド書込の gate 原子性が必要 |
| 6 | 4 injection ports、`PlaybackUseCase` rollback、`BindLateInputSource` | [Partial] 4 種の all-or-nothing は存在するが weight が含まれない | [Missing] 5 番目の weight port / coordinator。Replace 前後の weight 維持、注入だけ gate bypass |
| 7 | `RecEvent`, `RecEventKind`, `RecBinaryFormat`, `RecFileReader`, `RecTimelineSeek`, timeline adapters | [Partial] extensible な kind / flags の基礎はある | [Missing] weight timed / baseline records。旧ファイル deterministic reject。timeline adapter の新 kind 無視 |
| 8 | `RecInputSourceCoverageCatalog`, `RecInputSourceCoverageGate` | [Partial] 4 系統の catalog / gate は存在する | weight を第 5 系統として分類し、経路削除・改名・重複分類を検出する契約が必要 |
| 9 | core asmdef、NativeArray double buffer、GC gate tests | [Constraint] package 分離、任意スレッド source weight、GC zero が既存制約 | [Unknown] layer weight の thread contract。observer dispatch と dirty tracking の allocation-free 実装 |
| 10 | core / rec / inputsystem / timeline tests、GC gate | [Partial] 既存各系統のテスト基盤はある | [Missing] 5 種 all-or-nothing、overlay gate、停止時引継ぎ、旧 format reject、bit exact round trip |
| 11 | 前身 spec、rec README / Documentation、catalog table | [Partial] HID-80 の既知制限が複数文書に残る | [Missing] 上書き注記と実装後の正本整合。単なる削除ではなく、どの spec が後続契約かを明記する必要 |

---

## 4. 要求別の実装ギャップ詳細

### Req 1: weight 書込経路の分類

観測・遮断対象にすべき経路:

1. `FacialController.SetLayerWeight`
2. `LayerUseCase.SetLayerWeight`
3. `FacialController.SetInputSourceWeight`
4. `LayerUseCase.SetInputSourceWeight`
5. `FacialController.BeginInputSourceWeightBatch`
6. `LayerInputSourceWeightBuffer.BulkScope.SetWeight` / commit
7. `LayerUseCase.BindLateInputSource` による既存 slot weight 更新
8. `LayerUseCase.BindLateInputSource` による新規 slot 初期 weight
9. `LayerUseCase.UnbindLateInputSource` の weight 列詰め
10. `InputSystemAdapterBinding.ApplyOverlayLayerWeights`
11. REC の baseline / injection による内部書込

REC 自身の baseline 確立と injection は、live write の観測対象ではあるが、live gate の遮断対象外である。これを「明示的除外」として catalog に記録する必要がある。

### Req 2: final weight の観測位置

推奨する観測意味論は「書込直後」ではなく「フレームで実際に消費された final 値」である。

- layer weight:
  - `LayerUseCase.UpdateWeights` が `LayerBlender.LayerInput.Weight` を構成する直前
  - または `LayerBlender.Blend` に渡す layer input 配列を確定した直後
- source weight:
  - `LayerInputSourceWeightBuffer.SwapIfDirty` 後
  - Aggregator が各 `(layer, source)` の weight を read buffer から読み、source 値へ乗算する直前

この位置なら、1 フレーム内に同一対象へ複数回書かれた場合でも、最後に合成へ使われる値だけを観測できる。

ただし `UpdateWeights` は main thread、source weight write は任意 thread である。したがって observer callback 自体は main thread の Aggregate / UpdateWeights 内で同期実行し、任意 thread 側では値の dirty 状態だけを保持する構造が適切である。

### Req 2.4 / Req 2.5: coalesce と no-op

各対象について次を事前確保する。

- `lastObservedLayerWeights[]`
- `lastObservedSourceWeights[]`
- `dirty` または generation 配列
- observer 用の slot metadata cache

フレーム内では current effective value と last notified value を比較し、`float` の bit pattern が同じ場合は通知しない。`==` だけでなく `BitConverter.SingleToInt32Bits` 相当の意味論を設計時に確認する必要がある。clamp 後の値を記録するため、NaN / infinity の扱いは既存 clamp 契約に合わせて先に決定する。

通知対象がない場合は callback loop を避ける fast path を維持する。

### Req 2.6: stable slot identity

推奨キー:

```text
LayerKey = profile layer の安定識別子
SlotKey = layer declaration の slot id
```

現状の registry には `_slotIds` があり、`declaredId` を優先するため、Replace の source instance 変更には対応できる。

ただし以下を設計で決める必要がある。

- LayerKey を layer name にするか、layer index + layer name にするか
- layer name の重複を許すか
- profile 再構築時に同じ layer が同一キーになる保証
- source.Id が同じ source を同一 layer に複数宣言した場合の slot key
- Unregister による compact 後に REC event が参照する slot をどう解決するか

最小変更案は `layerName + declaredSlotId` を論理キーとし、runtime の index は lookup cache としてのみ使う方式である。layer name の一意性が保証されないなら、profile 側に layer declaration key を追加する必要があり、これは Req 9 の「既存コードパス変更を限定」に対して大きい。

### Req 5.3: off-main-thread gate

source weight の live write は任意スレッドから呼び出せる。gate 判定を `SetWeight` の内部で行わなければ、次の競合が残る。

```text
worker thread: live SetWeight
main thread:   playback begins / gate enabled
worker thread: old write reaches buffer
```

推奨は、weight buffer または core の weight coordinator に次の state を置く方式である。

- `volatile` / `Interlocked` で読める liveBlocked state
- live write と injection write を明確に分離
- live write は gate を読んで blocked なら buffer を変更しない
- injection write は gate を無視して通常の clamp / dirty 処理へ進む
- gate の開始順序は「gate enable → baseline injection」
- Stop は「gate disable」のみで、weight の自動復元をしない

bulk scope は `BeginBulk` 時ではなく、個々の `BulkScope.SetWeight` と最終 commit の両方で live / injection の種別を保持する必要がある。既存 `Dictionary<int,float>` pool は低頻度 bulk 用だが、再生中の通常フレームに allocation を発生させないことを確認する。

layer weight は現在通常配列へ直接書いているため、off-main-thread を source weight と同じ契約にするか、main-thread-only と明示するかを設計で決める必要がある。ただし gate の原子性は overlay と script の両方に必要である。

### Req 6.4: 5-port all-or-nothing

既存 `PlaybackUseCase` は 4 ポートを配列化して preflight と rollback を行う。weight を追加する場合の候補は次のとおり。

1. `IWeightInjectionPort` を 5 番目に追加
2. 5 種を束ねる `IPlaybackExclusivityCoordinator` を新設
3. weight を既存 analog port に混在させる

3 は weight の対象・baseline・gate lifecycle が analog と異なるため不適切である。1 は最小変更で既存パターンに沿う。2 は「gate enable → baseline → source replacement」の順序や future port の追加を一箇所へ集約しやすい。

いずれの場合も次を保証する。

- 5 ポート全ての `CanBeginInjection` を先に実行
- preflight 失敗時は一つも開始しない
- begin 中に失敗した場合は成功済み全ポートを逆順解放
- scheduler load は 5 ポート確立後
- 完了時は解放しない
- `StopPlayback` のみが解放点
- weight gate が先に有効化され、weight baseline が先に適用される

既存の 2 ポート互換 constructor は source compatibility のため残っている。5 種 all-or-nothing をどの constructor にも適用するか、NullWeightPort を明示的に使うかは設計判断が必要である。

### Req 7.4: 旧 `.fcrec` の deterministic reject

推奨方式:

```text
formatVersion = 1 のまま
RecHeaderFlags.WeightBaseline = 0x0002 を追加
reader は FullInputBaseline | WeightBaseline を必須検査
```

`TryRead` の検査順は次のとおり。

1. magic
2. formatVersion
3. header flags
4. 旧 flags の場合はレコード走査前に false + error
5. 新 kind を含むレコード走査

これにより、weight baseline が存在しない旧構造を、既存レコードだけ読める場合も含めて確定拒否できる。`formatVersion` の版分岐や旧構造読み込み分岐は追加しない。

header flags は本 spec 実装後の writer が常に両方を設定する。`WeightBaseline` の有無だけでなく、baseline レコードの重複・欠落・時刻付き record より後に現れるケースも reader が検証する必要がある。

core reader は未知 kind をエラーにする。Timeline Editor の `RecEventSequenceAdapter` だけは weight kind を export 対象外として無視し、既存の Timeline 表現を壊さない。

---

## 5. 実装アプローチの選択肢

### Option A: 既存コンポーネントを拡張

変更候補:

- `LayerUseCase`
  - layer weight observer
  - source weight observer
  - live gate
  - injection API
- `LayerInputSourceWeightBuffer`
  - gate 判定
  - injection write
  - dirty / final value tracking
- `LayerInputSourceRegistry`
  - stable slot key の公開
- `RecordingUseCase`
  - weight callback
- `RecBaselineState`, `RecEvent`, `RecBinaryFormat`, `RecTimelineSeek`
  - weight baseline / event
- `PlaybackUseCase`
  - 5 番目の port
- timeline adapter
  - weight kind の skip

長所:

- 現在の observer、baseline、port、rollback パターンを再利用できる
- core の package boundary を維持しやすい
- 変更ファイル数を抑えられる
- `_slotIds`、double buffer、既存の `RecHeaderFlags` を活用できる

短所:

- `LayerUseCase` と `PlaybackUseCase` の責務が増える
- layer weight と source weight の thread / frame semantics が同じクラスに混在する
- live gate と injection bypass の誤用リスクが高い
- 既存の `ILayerSourceValueObserver` に無理に追加すると契約が不明瞭になる

評価:

- Effort: **L**
- Risk: **Medium-High**
- 理由: 既存パターンを使えるが、off-main-thread gate と final-value observation の境界を誤ると再現性が壊れる。

### Option B: weight 専用コンポーネントを新設

候補:

- `IWeightObserver`
- `IWeightWriteGate`
- `IWeightInjectionPort`
- `WeightStateCoordinator`
- `RecWeightBaseline` / `RecWeightEvent`
- `RecWeightInjector`

`LayerUseCase` と `LayerInputSourceWeightBuffer` は専用 coordinator に委譲し、REC は `IWeightInjectionPort` だけを参照する。

長所:

- layer weight と source weight の責任範囲を分離できる
- live / injection の書込種別を型で分離しやすい
- 5-port lifecycle と weight-specific baseline をテストしやすい
- 将来の weight 種別追加に拡張しやすい

短所:

- layer weight の現在配列、source weight buffer、registry と状態が二重化しやすい
- coordinator と既存 buffer の同期契約が複雑になる
- 新規インターフェース・ファイル・配線が増える
- core の薄い観測面追加という既存方針を超えて、構造変更になりやすい

評価:

- Effort: **XL**
- Risk: **High**
- 理由: 独立性は高いが、二重状態と書込経路の取りこぼしが最大リスク。

### Option C: Hybrid（推奨候補）

既存のデータ所有者を維持しつつ、横断責務だけを新設する。

- `LayerUseCase`
  - layer weight の実効値適用と observer 呼出し
  - live gate の入口
- `LayerInputSourceWeightBuffer`
  - source weight の thread-safe storage
  - live / injection の書込分離
  - final read point の metadata
- 新規 `IWeightObservationSink` または `IWeightObserver`
  - layer / source の final weight callback
- 新規 `IWeightInjectionPort`
  - 5 番目の port として PlaybackUseCase に接続
- `RecWeightInjector`
  - weight baseline 適用
  - timed event 注入
  - missing target の warn-once
- 既存 `RecBaselineState`, `RecEvent`, `RecBinaryFormat`, `RecTimelineSeek` を拡張
- catalog / gate に weight 経路を追加

長所:

- 既存 owner と buffer を維持できる
- REC 側の weight lifecycle を 1 port に閉じ込められる
- observer の final-value semantics を既存 pre-weight observer と混同しない
- 5-port all-or-nothing と core package boundary を両立しやすい
- Replace は既存 registry の slot identity を再利用できる

短所:

- 既存 owner の変更と新規 component の配線を両方行う
- layer weight と source weight の観測タイミングを別々に設計する必要がある
- injection port と core gate の契約を厳密に定義しないと、live write が混入する

評価:

- Effort: **L**
- Risk: **Medium**
- 理由: 変更範囲と分離のバランスが最もよく、既存の double buffer / registry / four-port pattern を壊さずに拡張できる。

---

## 6. 設計フェーズへの推奨事項

### 6.1 第一候補

Option C を第一候補として詳細設計する。

必須の境界:

```text
live write:
  public Set... / bulk
      → core live gate
      → clamp
      → state buffer
      → next final aggregation

REC injection:
  RecWeightInjector
      → explicit injection API
      → same clamp / state application
      → observer notification
```

live と injection を同じ public method の bool 引数で表現するのではなく、内部 interface または capability token で分離する。通常呼出しから injection bypass を誤って利用できないようにする。

### 6.2 stable identity

設計文書で以下を明記する。

- `LayerKey` の canonical representation
- `SlotKey` の canonical representation
- Replace 前後で key が不変であること
- source instance / `source.Id` だけを key にしないこと
- Unregister / compact 後のイベント解決規則
- late-added slot は開始時 snapshot 外として遮断対象外であること
- baseline に存在しない現在 slot の確定値

可能なら runtime index は REC file 内部の compact index、論理 key は ID table による string ID として分離する。少なくとも `(layerKey, slotKey)` の組を一意に ID table 化する。

### 6.3 final-value observer

推奨 callback の意味:

```text
OnLayerWeightConsumed(layerKey, effectiveWeight)
OnInputSourceWeightConsumed(layerKey, slotKey, effectiveWeight)
```

callback は次の箇所で main thread に同期発火する。

- layer: `LayerUseCase.UpdateWeights` で LayerBlender へ渡す effective layer weight が確定した時点
- source: `LayerInputSourceWeightBuffer.SwapIfDirty` 後、Aggregator が current read buffer を消費する時点

observer がない場合は既存の fast path を保持する。observer がある場合でも、次を事前確保する。

- target metadata
- previous effective values
- dirty / generation storage
- REC sink へ渡す scratch

`RecordingUseCase` 側で per-frame List や boxing を作らない。時刻・対象 index・float value を固定長または pooled event queue に格納し、writer thread へ渡す既存方式へ合わせる。

### 6.4 thread safety

設計時に以下の race test を必須化する。

- gate enable と worker `SetWeight` の同時実行
- bulk commit と gate enable の同時実行
- Replace / Unbind と Aggregate の境界
- Stop による gate disable と worker write
- injection と live write の同一フレーム競合

gate の state transition と write の visibility ordering を C# memory model に沿って定義する。単に `bool _blocked` を追加するだけでは不十分である。

### 6.5 5-port lifecycle

`PlaybackUseCase` の既存 preflight / rollback へ weight port を追加する。順序案:

1. recording load / seek baseline の生成
2. 5 ポート全ての preflight
3. weight live gate の有効化を含む `TryBeginInjection`
4. trigger / expression / analog / value provider の注入開始
5. 全 port 成功後に scheduler load
6. baseline の適用
7. 再生開始

ただし「gate は baseline より先」「baseline 通知は観測者へ出さない」という要求があるため、実際の port 内部順序を設計図で明示する。

開始途中の失敗時は weight gate を含む全ての確立済み resource を解放する。自然完了時は解放しない。`StopPlayback` のみが解放点である。

### 6.6 `.fcrec` 契約

推奨する record 構造:

- `BaselineLayerWeight`
- `BaselineInputSourceWeight`
- `LayerWeightSample`
- `InputSourceWeightSample`

対象識別子は ID table 経由で保持する。値は float の binary representation を直接保持し、JSON 化・decimal 化・量子化を行わない。

header flags:

```text
FullInputBaseline = 0x0001
WeightBaseline    = 0x0002
RequiredHeaderFlags = 0x0003
```

旧構造の reject は record kind の検査より前に行う。エラーメッセージは既存の `TryRead` → `RecFileReader` → `RecCharacterBinding.Load` のログ経路に合わせる。

### 6.7 途中再生

`RecTimelineSeek.BuildBaselineAt` は既存の trigger / analog / value provider / expression の fold に weight fold を追加する。

- baseline の各 weight を初期値として登録
- offset より前の weight event を順番に適用
- offset ちょうどの event は scheduler 側で発火
- layer と slot の最終値を baseline にする
- profile に存在しない target は warn-once で skip
- baseline に存在しない現在 target は設計で定めた確定値へ reset

seek 中に新しい collection を毎フレーム作らない。seek は開始時の低頻度処理なので allocation は許容できるが、通常 Tick と明確に分離する。

### 6.8 catalog / gate

`RecInputSourceCoverageCatalog` は現在の IInputSource 実装分類を正本としているため、weight を単なる source class として追加するのではなく、次のような別の「write path family」として記録するのが分かりやすい。

- layer weight write
- input-source weight write
- bulk input-source weight write
- overlay layer weight write
- late-bind / Replace weight write
- REC injection write（明示的除外または注入経路）

gate では次を検査する。

- catalog に記載された代表 API が存在する
- overlay の caller が core API を通る
- weight 経路に観測・遮断・注入・round-trip の契約がある
- 明示的除外には理由がある
- 削除・改名された API 名が残っていない
- 既存 4 系統の分類検査が退行していない

### 6.9 文書更新

実装後に次を同期する。

- `rec-full-input-coverage/design.md`
  - HID-80 の既知制限に上書き注記
  - 入力源分類表
  - Non-Goals / Out of Boundary
- `rec-full-input-coverage/requirements.md`
  - weight を対象外とする前提への上書き注記
- `rec-recording-playback/design.md`
  - overlay weight が未到達という記述への上書き注記
- rec `README.md` / `Documentation~/README.md`
  - 記録対象一覧
  - 5 種の排他
  - formatVersion 1 と旧ファイル reject
  - late-added slot の既知制限

単純に既存文言を削除せず、どの後続 spec が契約を上書きしたかを残す。

---

## 7. テスト上のギャップ

最低限必要な追加テスト群:

### core EditMode

- layer weight の clamp 後値が通知される
- source weight の clamp 後値が通知される
- 同一フレーム複数書込が final 値へ coalesce される
- 同値書込が通知されない
- bulk commit が一つの final state として観測される
- observer 未登録時の既存挙動と allocation が変わらない
- Replace 後も `(layerKey, slotKey)` が変わらない
- Unbind / compact 後に weight 列と slot key が一致する
- gate 中の layer / source / bulk live write が無視される
- off-main-thread source write が gate を越えて反映されない
- injection write は gate を迂回し、通常の clamp / notify を通る

### rec EditMode

- weight baseline が全 layer / 全開始時 slot を含む
- weight event が時刻・順序・値を保持する
- 同値フレームで event が増えない
- float bit exact round trip
- `BuildBaselineAt` が layer / slot weight を fold する
- missing target が warn-once skip される
- baseline 確立が observer event を生成しない
- 5 port preflight 失敗時に一つも排他が残らない
- begin 途中失敗時に成功済み port と weight gate が rollback される
- Completed では解放されず、Stop のみで解放される

### rec / format

- 新 header flags が書かれる
- `WeightBaseline` 欠落ファイルがレコード内容に関係なく reject される
- unknown core kind が reject される
- weight baseline が timed event より後の場合 reject される
- malformed weight payload が reject される

### timeline Editor

- weight kind を含む REC を読める
- weight kind は Timeline export 対象外として無視される
- 既存 export 対象 kind の動作が変わらない

### PlayMode / integration

- InputSystem overlay → 記録 → 再生で BlendShape output が一致する
- 再生中の live overlay が weight / output に影響しない
- 再生中の script API / bulk write が影響しない
- Stop 後は停止時点の weight を保持し、以後の live write が反映される
- REC 自身の Replace と原本復元の前後で weight が変化しない
- GC zero gate が既存 suite とともに緑になる

---

## 8. Effort / Risk 総括

- **推奨 Option C: L / Medium**
  - 既存の buffer、registry、observer、four-port lifecycle を利用できる。
  - 最大リスクは final-value の定義、off-main-thread gate、Replace / compact 後の stable identity。
- **Option A: L / Medium-High**
  - 最小変更だが、既存 UseCase に責務が集中する。
- **Option B: XL / High**
  - 分離は明確だが、状態二重化と広範な配線変更により、既存契約を壊す可能性が高い。

---

## 9. Research Needed

設計フェーズで確定すべき事項:

1. profile layer に重複しない安定 ID が存在するか。なければ layer name の一意性を契約化できるか。
2. layer weight API を任意スレッドから呼べる契約にするか、main-thread-only とするか。
3. `LayerInputSourceWeightBuffer` の `SetWeight` と `EnsureMaxSourcesPerLayer` の同時実行制約を、gate 導入後もどう維持するか。
4. bulk scope の nested usage、複数 thread、dispose 順序の既存想定。
5. weight baseline の「baseline に存在しない対象」の確定値。推奨候補は profile default または 0 だが、既存初期状態規則と合わせて決定する。
6. `RecEventKind` の新規番号割当と各レコードの byte layout。
7. weight ID table を既存 source ID table と共有するか、複合 key 用 table を新設するか。
8. `RecTimelineSeek` で baseline weight を immutable に保持する方法。
9. legacy two-port `PlaybackUseCase` constructor を残す場合の weight port semantics。
10. header flags の必須ビットを `FullInputBaseline | WeightBaseline` とすることへの既存 fixture の影響。
11. `RecInputSourceCoverageGate` が weight 経路を検査する最適な静的検査方法。
12. 10 体同時記録時の observer dispatch と event queue の上限・overflow 方針。

---

## 10. 結論

HID-80 の本質的なギャップは、weight の値そのものではなく、次の三つが既存 REC 契約へ接続されていないことである。

1. 合成に使われた final weight を、呼出元・スレッドを問わず一度だけ観測する面
2. 再生中の live weight write を core だけで遮断し、REC injection だけを通す面
3. weight baseline を含む `.fcrec` の識別・拒否・seek・5-port lifecycle

既存の `_slotIds`、Replace 同一 slot、NativeArray double buffer、4-port rollback、header flags 検査は再利用できる。したがって、新しい並列入力システムを作るより、既存所有者へ薄い weight observer / gate / injection port を追加する Option C が最も妥当である。

設計の成否は、`(layerKey, slotKey)` の安定性、final-value の観測点、off-main-thread gate のメモリ可視性、旧 `.fcrec` の header-level reject を先に固定できるかに依存する。

---

# 設計フェーズ research（rec-weight-coverage、2026-10-05）

## Summary
- **Feature**: `rec-weight-coverage`
- **Discovery Scope**: Extension（既存 REC / core の観測・遮断・注入・永続化パターンへの第 5 系統の追加。light discovery）
- **Key Findings**:
  - weight の消費点は 2 箇所に閉じている: レイヤー weight は `LayerUseCase.UpdateWeights` が `Aggregate` に渡す `_layerInterWeights[]`、入力源 weight は `LayerInputSourceWeightBuffer.SwapIfDirty` 後に Aggregator が `GetWeight(l, s)` で読む read buffer。ここで変化検出すれば「1 フレーム内の複数書込を最終値へ畳む」「同値は通知しない」が構造的に成立する（Req 2.4 / 2.5）
  - 入力源 weight の全ライブ書込は `LayerInputSourceWeightBuffer.SetWeight` / `BulkScope.SetWeight → CommitBulk` の 2 入口に収束する。レイヤー weight のライブ書込は `LayerUseCase.SetLayerWeight` 1 入口。遮断面はこの 3 入口に置けば inputsystem 無改修で overlay 駆動を含む全呼出元を遮断できる（Req 5.4）
  - `BindLateInputSource` の既存スロット置換は宣言 weight を再書込しており、REC 自身の Replace / 原本復元で注入済み weight を上書きする経路になっていた。既存テスト `BindLateInputSource_ReplacingExistingId_KeepsOtherSourceWeights` は同じ宣言 weight を渡しているため、「既存スロットの置換では weight を触らない」契約へ変更しても緑のまま（Req 6.3）
  - スロットの安定キーは registry が既に保持する `GetSlotId(l, s)`（宣言 id = `InputSourceRegistry` キー、Replace 前後で不変）で足りる。sourceIdx 0（`LayerExpressionSource`、`Id == "input"` で inputsystem の予約 id と衝突）だけは予約 id `@expression`（`ExpressionActivationSource.ReservedId`）で同定する（Req 2.6）
  - `.fcrec` はヘッダ `flags` に bit1 `WeightBaseline` を追加し `RequiredHeaderFlags = 0x0003` にすれば、HID-35 と同じ方式で旧構造（weight 基準なし）をレコード走査前に確定的に拒否できる（Req 7.4）。レイヤー名は新しい `IdDefine` 種別（`Layer = 3`）で id 表に載せ、区切り文字による複合 id 文字列を避ける

## Research Log

### 消費点の実コード確認（Req 2.3 / 2.4）
- **Context**: 「合成に実際に使われる weight の最終値を、呼出元・スレッドを問わず 1 回だけ観測する」位置の特定
- **Sources Consulted**: `LayerUseCase.UpdateWeights`（L152-272）、`LayerInputSourceAggregator.AggregateInternal`（L291-421）、`LayerInputSourceWeightBuffer.SwapIfDirty`（L134-150）
- **Findings**: `UpdateWeights` は `_aggregator.Aggregate(deltaTime, _layerPriorities, _layerInterWeights, _layerInputScratch)` を 1 回呼ぶ。`AggregateInternal` 冒頭の `SwapIfDirty` で任意スレッドの入力源 weight 書込がこのフレームの read buffer に確定し、以後 `GetWeight(l, s)` が返す値がそのまま加重和に使われる。レイヤー weight は `_layerInterWeights[l]` が `LayerBlender.LayerInput.Weight` に無加工で載る。したがって `Aggregate` 直後に `_layerInterWeights[l]` と `_weightBuffer.GetWeight(l, s)` を読めば「消費された最終値」である
- **Implications**: 観測は `LayerUseCase` が `Aggregate` 直後に行う（Aggregator / Blender は無改修）。変化検出用の「前回通知値」配列は `LayerUseCase` が事前確保し、`float.NaN` を未観測の番兵、`BitConverter.SingleToInt32Bits` の一致を同値判定に使う（Req 2.5 / 3.3 / 3.4）

### ライブ書込入口と任意スレッド契約（Req 5.1 / 5.3）
- **Context**: inputsystem 無改修での遮断（Req 5.4）と、任意スレッド書込の遮断の原子性
- **Sources Consulted**: `LayerInputSourceWeightBuffer.SetWeight` / `BeginBulk` / `CommitBulk`、`LayerUseCase.SetLayerWeight` / `SetInputSourceWeight` / `BeginInputSourceWeightBatch`、`FacialController` の同名 API、`InputSystemAdapterBinding.ApplyOverlayLayerWeights`（L291-318）
- **Findings**: 入力源 weight は `SetWeight`（即時 + `Interlocked.Increment(_dirtyTick)`）と `CommitBulk`（pending dict を一括 flush）の 2 入口。どちらも lock を持たない。レイヤー weight は `SetLayerWeight` が `Dictionary` と `float[]` を直接書くためメインスレッド前提（既存契約、文書化されていない）。overlay binding は `OnLateTick` から `FacialController.SetLayerWeight` を毎フレーム呼ぶのみで生の weight 配列には触れない
- **Implications**: 遮断フラグを buffer に置き、`SetWeight` は「in-flight カウンタを `Interlocked.Increment` → フラグ確認 → 書込 → `Decrement`」、`SuspendLiveWrites` は「フラグ設定 → in-flight が 0 になるまで `SpinWait`（有界）」とすることで、`SuspendLiveWrites` が返った後に古いライブ書込が buffer に到達しないことを保証する（単純な `bool` では「フラグ読取後・書込前」に suspend が割り込むとライブ値が基準を上書きし得る）。レイヤー weight はメインスレッド専用契約を明文化し、フラグ 1 個で足りる。bulk は `CommitBulk` 時点のフラグで pending を破棄する（スコープ内の個別 `SetWeight` は蓄積のみで副作用がないため、commit 時の 1 判定で十分）

### `BindLateInputSource` / `UnbindLateInputSource` の weight 書込（Req 1.1 / 6.3 / 6.7）
- **Context**: REC 自身の Replace（注入体装着・原本復元）は `InputSourceRegistry.Replace` → Subscribe → `FacialController.HandleLayerInputSourceRebound` → `BindLateInputSource(layerIdx, declaredId, source, declWeight)` に乗る。この経路の weight 書込が注入済み weight を上書きするか
- **Sources Consulted**: `LayerUseCase.BindLateInputSource`（L332-386）、`UnbindLateInputSource`（L413-443）、`LayerUseCaseTests` L900-1110
- **Findings**: 既存スロット置換（`TryReplaceSource` 成功）後に `_weightBuffer.SetWeight(layerIdx, existingIdx, weight)` で**宣言 weight を再書込**している。新規スロット（`TryAddSource`）は初期 weight を書く。`Unbind` は後続スロットの weight を詰め直す。既存テストは同じ宣言 weight を渡しており、置換で runtime 設定 weight が宣言値へ戻る挙動を固定したテストは無い
- **Implications**: (1) 既存スロットの置換では weight を触らない（スロットの weight は「(layer, 宣言 slot) の属性」でありインスタンスの属性ではない。runtime の `SetInputSourceWeight` が Replace で消える現行挙動は潜在不具合）。これで REC の Replace / 復元は原理的に weight を変えない（Req 6.3）。遮断中でも Replace が走るため、遮断に依存しない。(2) 新規スロットの初期 weight と `Unbind` の詰め直しは「構造書込」として遮断を迂回する（新規スロットは開始時スナップショット外の既知制限 Req 6.7。迂回しないと再生中に late-bind した入力源の weight が恒久的に 0 になる）。(3) 前回通知値の配列は `Unbind` の詰め直しに合わせて当該レイヤーを未観測（NaN）に戻し、次フレームで現在値を通知する

### スロット同定キーの安定性（Req 2.6）
- **Context**: Replace / 後勝ち上書き / compact を跨いで同じ (layer, slot) を指すキー
- **Sources Consulted**: `LayerInputSourceRegistry._slotIds` / `GetSlotId` / `FindSourceIndex`、`LayerUseCase.BuildAggregatorPipeline`（`bindingSlotIds.Add(null)` で sourceIdx 0 は `source.Id = "input"`）、`LayerInputSourceAggregator.ResolveCachedSourceId`、rec-full-input-coverage design「入力源識別スコープ」5
- **Findings**: sourceIdx ≥ 1 のスロット id は宣言 id（registry キー）で、`TryReplaceSource` は `_slotIds` を変えない（PR #46 で確立）。sourceIdx 0 の `LayerExpressionSource.Id` は `"input"` で、inputsystem の予約 slug `input` と文字列衝突する。レイヤー名の一意性を検証する箇所はプロファイル読込に存在しない（`FindLayerByName` / `_groupedByLayer[name]` は先勝ち）
- **Implications**: スロットキー = `(layerName, slotId)`。sourceIdx 0 は `WeightSlotIds.ExpressionSlotId = "@expression"`（系1 の予約 id と同じ文字列。系1 の消費アダプタがこのスロットである事実と整合し、registry の id 文字集合 `[a-zA-Z0-9_.\-:]` と交わらない）。レイヤー名は「プロファイル内で一意」を本 spec の前提契約として明記し、重複時は `SetLayerWeight` と同じ先勝ち解決（Revalidation Trigger）。`.fcrec` ではレイヤー名を `IdDefine` の新種別 `Layer` で id 表に載せ、`(layerIdx, slotIdx)` の 2 つの u16 で参照する（区切り文字を含む複合文字列は作らない）

### 既存ポートの確立順と weight ポートの位置（Req 5.5 / 6.4）
- **Context**: 「遮断 → 基準確立」（5.5）と、A / V の Replace が weight を変えないこと（6.3）の両立
- **Sources Consulted**: `PlaybackUseCase.StartPlayback`（preflight → T→E→A→V → 逆順ロールバック）、rec-full-input-coverage design「再生開始のトランザクション」
- **Findings**: 既存 4 ポートは `IInjectionPort[]` の同一ループで preflight / 確立 / ロールバック / 解放を回す。確立と `StopPlayback` 解放は同順、ロールバックのみ逆順
- **Implications**: weight ポートを**先頭**に置き `W → T → E → A → V` とする。W の `TryBeginInjection` 内で「`SuspendLiveWeights` → `ResetWeightsToDeclared` → 基準 weight 設定」の順に行うため、以後の A / V の Replace に伴う構造書込が注入済み weight を乱さない（上記の「既存スロット置換は weight を触らない」で二重に保証）。解放も `W → T → E → A → V`（同順規則を維持）。W の解放は `ResumeLiveWeights` のみで値を維持（Req 5.6）。確立途中失敗は既存どおり確立済みポートの逆順 `EndInjection`

### `.fcrec` の weight レコードと旧構造拒否（Req 7.1–7.5）
- **Context**: formatVersion 1 据え置きのまま weight レコードを追加し、weight 基準の無い旧構造を確定的に拒否する
- **Sources Consulted**: `RecBinaryFormat`（ヘッダ検査 L294-310、`IsBaselineKind` / `IsTimedKind`）、`RecHeaderFlags`、`RecEvent`（`IdDefinitionKind`、`PayloadFloatCount`）、`RecTimeline`（ペイロード検証）、`RecIdTable.CreateSeeded`、`RecStreamWriter.WriteBaseline`
- **Findings**: ヘッダ `flags` bit0 は HID-35 で必須化済み。bit1 以降は予約（writer 0・reader 非検査）。weight 値は float 1 個なので既存の float ペイロード区画（`RecTimeline._payloadByEvent`、`RecEventChunkQueue` の float 区画）に `PayloadFloatCount = 1` で載せられ、新しいペイロード種別は不要
- **Implications**: `RecHeaderFlags.WeightBaseline = 0x0002`、`RequiredHeaderFlags = FullInputBaseline | WeightBaseline`。kind 12 `LayerWeightSample`（f64 t, u16 layerIdx, f32 w）、13 `InputSourceWeightSample`（f64 t, u16 layerIdx, u16 slotIdx, f32 w）、14 `BaselineLayerWeight`（u16 layerIdx, f32 w）、15 `BaselineInputSourceWeight`（u16 layerIdx, u16 slotIdx, f32 w）、`IdDefine` の `idKind = 3 (Layer)`。基準先行不変条件・未知 kind エラー・同一対象の基準重複拒否は既存規則を拡張する

### 網羅性カタログへの weight 経路の載せ方（Req 8.1–8.3）
- **Context**: `RecInputSourceCoverageCatalog` は `IInputSource` 実装型の分類表で、weight 書込 API はこの型集合に現れない（HID-137 の見逃し要因）
- **Sources Consulted**: `RecInputSourceCoverageCatalog`、`RecInputSourceCoverageGate`、`RecInputSourceCoverageCatalogTests`
- **Findings**: カタログは型 FullName 文字列の静的リスト + reflection の双方向検査で陳腐化を機械的に検出する構造。weight は「型」ではなく「書込経路（型 + メンバー）」の集合
- **Implications**: 同じカタログクラスに第 2 の正本 `WeightWritePaths`（型 FullName + メンバー名 + アセンブリ名 + 分類 Gated / Excluded + 除外区分 + 理由）を置き、ゲートテストは各エントリの型とメンバーがロード済み product アセンブリに実在することを reflection で検査する（削除・改名で失敗 = Req 8.3）。既存の `IInputSource` 分類検査は変更しない（Req 8.4）。「再生中に遮断されるか」の動的検証はカタログではなく受け入れテスト（Req 10.2 / 10.3）が担う

### inputsystem overlay の受け入れテスト配置（Req 10.2）
- **Context**: 実 `InputSystemAdapterBinding`（Overlay）+ 仮想 Gamepad + REC の記録→再生を 1 本の PlayMode テストで固定したい
- **Sources Consulted**: `InputSystemAdapterBindingIntegrationTests`（仮想 `Gamepad` を `InputSystem.AddDevice`、`AdapterBuildContext` 手組み）、`RecCharacterBindingPlayModeTests`（実 `FacialController` + `TestCharacterProfileSO`）、osc PlayMode asmdef（`Hidano.FacialControl.InputSystem` を直接参照 + `versionDefines`）
- **Findings**: rec Runtime / rec Tests は拡張パッケージを参照しない方針（rec-full-input-coverage Allowed Dependencies）。一方、拡張パッケージ側の Tests が他パッケージを参照する前例は osc PlayMode asmdef にある
- **Implications**: overlay 受け入れテストは **inputsystem の PlayMode テスト asmdef** に置き、`Hidano.FacialControl.Rec.Domain / Application / Adapters` を参照に追加、`versionDefines` で `com.hidano.facialcontrol.rec` 存在時に `FACIALCONTROL_HAS_REC_MODULE` を定義してテストファイルを `#if` で囲む（requirements Boundary Context「inputsystem は受け入れ検証のテスト追記のみ」）

## Architecture Pattern Evaluation

| Option | Description | Strengths | Risks / Limitations | Notes |
|--------|-------------|-----------|---------------------|-------|
| A: 既存拡張 | `LayerUseCase` / `WeightBuffer` に観測・遮断・注入を追加、REC 側は 5 番目のポート | 変更ファイル最少、既存の gate / port / 基準パターンと同型 | `LayerUseCase` の責務増 | gap analysis Option A |
| B: 専用コンポーネント新設 | `WeightStateCoordinator` 等へ状態を委譲 | 責務分離 | weight 状態の二重化、既存 buffer との同期契約が複雑、Req 9.1 の「面の追加に限定」を超える | 不採用 |
| C: Hybrid（採用） | データ所有者（`LayerUseCase` / `WeightBuffer`）は維持し、契約（`ILayerWeightObserver` / `IWeightInjectionGate`）と REC 側の `RecWeightInjector` / `IWeightInjectionPort` を新設 | 既存 owner と double buffer を壊さない、REC の weight ライフサイクルを 1 ポートに閉じる、系1 ゲート（`ExpressionUseCase : IExpressionActivationGate`）と同型 | 観測点（消費点）と遮断点（入口）が別の層にあるため、設計で両者の関係を明示する必要 | gap analysis 推奨 |

## Design Decisions

### Decision: 観測は `LayerUseCase` の消費点で変化検出し、サンプラークラスを設けない
- **Context**: Req 2.3–2.5（実効値・消費粒度・同値非通知）と Req 4.6（基準確立の非通知）、Req 9.2（未使用時ゼロコスト）
- **Alternatives Considered**: 1. Adapters 層に `WeightObservationSampler` を置き毎フレーム全スロットを読む（VP / analog と同型） 2. `ILayerSourceValueObserver` に weight 引数を足す 3. `LayerUseCase` が前回通知値を保持し変化時のみ observer を呼ぶ
- **Selected Approach**: 3。`LayerUseCase` が `_lastNotifiedLayerWeights[]` / `_lastNotifiedSlotWeights[]` を事前確保し、`Aggregate` 直後に `_layerInterWeights[l]` / `_weightBuffer.GetWeight(l, s)` と比較、ビット不一致のみ `ILayerWeightObserver` へ通知する。observer が null なら比較ループ自体を実行しない
- **Rationale**: 基準確立（`ResetWeightsToDeclared` / `TrySetBaseline*`）が「書込 + 前回通知値の更新」で非通知を実現できるのは、前回通知値を書込側（`LayerUseCase`）が持つ場合だけ。Adapters 層のサンプラーでは基準確立と通常注入を区別できない。VP 観測を Aggregator に渡さなかった理由（pre-weight 値契約との混同）も回避できる
- **Trade-offs**: `LayerUseCase` に配列 2 本と比較ループが増える。観測者接続時に現在値へ同期（通知なし）するため、接続直後のフレームは変化分だけが記録される（基準捕捉と同一時点の値なので整合する）
- **Follow-up**: `LayerUseCaseTests` で「同一フレーム複数書込 → 最終値 1 回」「同値 → 非通知」「基準確立 → 非通知」「注入 → 通知」を固定

### Decision: 遮断フラグは `LayerInputSourceWeightBuffer` に置き、in-flight フェンスで任意スレッド書込を閉じる
- **Context**: Req 5.3（任意スレッドの単発・bulk 書込の遮断）、Req 5.5（遮断 → 基準確立）
- **Alternatives Considered**: 1. `volatile bool` のみ 2. `lock` 3. in-flight カウンタ + フラグ + 有界 SpinWait
- **Selected Approach**: 3。`SetWeight` は `Interlocked.Increment(_liveWritersInFlight)` → `Volatile.Read(_liveSuspended)` → 書込 → `Interlocked.Decrement`。`SuspendLiveWrites` は `Volatile.Write(_liveSuspended, 1)` → `SpinWait` で in-flight が 0 になるまで待つ（上限 1 ms 相当のスピン後も 0 でなければ Warning を出して続行）。`CommitBulk` はフラグが立っていれば pending を破棄してプールへ返す（dirty を進めない）
- **Rationale**: 1 では「フラグ読取 → suspend → 基準書込 → 古い書込到達」で基準が壊れる。2 は毎フレームの hot path に lock を入れることになり既存の lock-free 契約と性能を崩す。3 は hot path の追加コストが Interlocked 2 回で、既存の `_dirtyTick` と同程度
- **Trade-offs**: `SuspendLiveWrites` がワーカースレッドの書込完了を待つ（通常ナノ秒〜マイクロ秒）
- **Follow-up**: Medium テストでワーカースレッドの連打中に suspend → 基準設定 → 次フレーム読取が基準値であることを反復検証（確率的だが回帰検出に有効）

### Decision: 既存スロットの `BindLateInputSource` は weight を書き換えない（契約変更）
- **Context**: Req 6.3（REC の Replace / 復元で weight が変わらない）
- **Alternatives Considered**: 1. 置換時の weight 書込を遮断フラグに委ねる（復元時は遮断解除順に依存） 2. REC が Replace のたびに weight を再注入する 3. 置換では weight を触らない
- **Selected Approach**: 3。新規スロットのみ初期 weight（宣言値）を構造書込で設定する
- **Rationale**: スロット weight は (layer, 宣言 slot) の属性で、インスタンス差し替えで宣言値へ戻る現行挙動は runtime `SetInputSourceWeight` を黙って失う潜在不具合。遮断順に依存しないため解放順の制約が消える。既存テストは同じ宣言 weight を渡しており緑を維持
- **Trade-offs**: 「Replace で宣言 weight にリセットされる」挙動に依存する利用者がいれば挙動変更になる（リポジトリ内に該当なし）。Revalidation Trigger に記載
- **Follow-up**: `LayerUseCaseTests.BindLateInputSource_ReplacingExistingId_KeepsRuntimeWeightOfThatSlot` を追加

### Decision（設計レビュー 1 回目の指摘 1 で改訂）: `BindLateInputSource` の宣言 weight 再適用は遮断中だけ抑止し、W ポートを A / V の外側に置く
- **Context**: codex 設計レビュー 1 回目 Critical 1「既存スロット置換で weight を常に変更しない契約は、ライブの late-bind で宣言 weight の更新が反映されず Req 9.1 と既存テスト契約を損なう」
- **Alternatives Considered**: 1. 置換で常に weight 不変（初版） 2. 遮断中のみ不変、通常時は従来どおり再適用 3. REC 専用の構造更新 API を別に設ける
- **Selected Approach**: 2。あわせて `PlaybackUseCase` の解放順を `T → E → A → V → W` にし、確立 `W → T → E → A → V` と対にする（W が Replace 型ポートを外側から包む）。ロールバックは確立済みの逆順
- **Rationale**: ライブ契約（`BindLateInputSource_AppliesDeclaredWeight_ScalesOutput` 等）を変えずに Req 6.3 を満たすには、Replace が走る時点で必ず遮断中である必要があり、それは解放順で W を最後にすれば保証できる。rec-full-input-coverage の「確立・解放同順」は既存 4 ポートの相対順序として維持する
- **Trade-offs**: `PlaybackUseCase` が確立順と解放順の 2 配列を持つ。`Completed` からの再開時の全解放も解放順を使う
- **Follow-up**: `PlaybackUseCaseTests.StopPlayback_ReleasesTriggerExpressionAnalogValueProviderThenWeight`、`LayerUseCaseTests.BindLateInputSource_ReplacingExistingId_WhileSuspended_KeepsCurrentWeight` / `_WhileNotSuspended_AppliesDeclaredWeight`

### Decision（設計レビュー 2 回目の指摘 1 で追加）: weight バッファの同期プロトコルを明文化し、bulk commit と resize も in-flight フェンスに参加させる
- **Context**: codex 設計レビュー 2 回目 Critical 1「in-flight フェンスが単発 `SetWeight` 中心で、bulk commit・resize・Bind/Unbind との同期順序が不明。Suspend 後の既存 bulk の扱い、resize 中のワーカー書込の扱いが未決」
- **Alternatives Considered**: 1. 既存契約（resize はメインのみ、同時実行は未定義）を文書化するだけ 2. bulk commit と resize も同じ in-flight フェンスに参加させ、状態遷移表で全操作の扱いを固定 3. `lock` で全操作を直列化
- **Selected Approach**: 2。ワーカーから到達するのはライブ書込（`SetWeight` / `CommitBulk`）だけなので両者をカウンタに参加させ、状態を変える操作（Suspend / Resume / resize / 構造書込 / Swap）はメインスレッド直列とする。遮断前に開いた bulk スコープの遮断後 commit は破棄。resize は Resizing フラグ + in-flight 0 待ちで保護し、resize 中に到達したライブ書込は破棄（従来の未定義動作を確定的に）
- **Rationale**: 競合の組合せが「ワーカーのライブ書込 × メインの状態遷移」の 1 種類に閉じるため、フェンス 1 つで全部を閉じられる。`lock` は hot path の性能と既存のロックフリー契約を崩す
- **Trade-offs**: resize 中の数マイクロ秒の窓でライブ書込が落ちる（次の書込で回復）
- **Follow-up**: `LayerInputSourceWeightBufferConcurrencyTests` に bulk 連打 × Suspend、`SetWeight` 連打 × resize を追加

### Decision（設計レビュー 1 回目の指摘 2 で改訂、2 回目の指摘 2 で再改訂）: レイヤー名の一意性はプロファイル読込境界で確立し、重複が残るプロファイルでは REC を開始しない
- **Context**: codex 設計レビュー 1 回目 Critical 2「レイヤー名を識別子にしながら重複を『先勝ち』とする契約は、全レイヤー基準の収集で `RecBaselineState` の重複拒否に到達し、ラウンドトリップが成立しない」
- **Alternatives Considered**: 1. `FacialProfile` コンストラクタで重複を例外にする 2. レイヤー配列 index を記録キーにする 3. 読込境界（`SystemTextJsonParser` / `FacialCharacterProfileConverter`）で後続の重複レイヤーを読み捨て Warning + weight 面は先勝ちフォールバック 4. 3 に加え、重複が残るプロファイルでは REC の録画・再生を明示的に拒否する
- **Selected Approach**: 4（2 回目レビュー Critical 2「先勝ちでは一方の weight が記録から失われ、警告だけでは完全性を保証できない」への対応。レビューが示した 3 択のうち「重複時に REC を明示的に無効化」を採用）。`IWeightInjectionGate.LayerNamesAreUnique` を公開し、`RecWeightInjector.CanBeginInjection` と `RecCharacterBinding.StartRecording` が false のとき開始を拒否する
- **Rationale**: 既存の重複解決の流儀（`inputSources` 重複 id は last-wins、`gaze.channels` 重複 id は後続読み捨て）が「Warning + 確定的な解決」であり、例外化は既存プロファイルの初期化を壊す。index キーは `SetLayerWeight` が名前で解決する既存契約と食い違い、`.fcrec` の可読性も落とす。読込境界で一意化すれば実行時プロファイルでは重複が発生せず、万一残っても REC が開始を拒否するので「記録が欠ける」状態は構造的に起きない
- **Trade-offs**: core Adapters の読込経路 2 箇所に小さな変更が入る（Req 9.1 の「面の追加に限定」に対する明示的例外。レビューで指摘された識別契約の穴を塞ぐため）。重複プロファイルでは REC が使えない（Warning で理由を示す）
- **Follow-up**: `SystemTextJsonParserTests` / `FacialCharacterProfileConverterTests.Parse_DuplicateLayerNames_KeepsFirstAndWarns`、`LayerUseCaseTests.LayerNamesAreUnique_*`、`RecWeightInjectorTests.CanBeginInjection_DuplicateLayerNames_ReturnsFalseWithReason`、`RecCharacterBindingTests.StartRecording_DuplicateLayerNames_WarnsAndReturnsFalse`

### Decision: 基準に無い対象の確定値は「宣言値へのリセット」
- **Context**: Req 4.5（ライブの残存 weight を引き継がない）
- **Alternatives Considered**: 1. 0 2. 現在値維持 3. 宣言値（レイヤー 1.0、スロットは宣言 weight、sourceIdx 0 は 1.0）
- **Selected Approach**: 3（`IWeightInjectionGate.ResetWeightsToDeclared`）
- **Rationale**: 0 はレイヤー出力を消し「同一構成で再生しても何も出ない」事故を招く。宣言値は `BuildAggregatorPipeline` の初期化値と同じで、記録時に weight を一度も触っていない構成の再現と一致する。VP の「無効」・analog の「0 埋め」と同じ「ライブ値を読まない確定的な中立状態」の原則に従う
- **Follow-up**: `LayerUseCase` が late-bind スロットの宣言 weight を保持する配列を持つ

### Decision: weight ポートは確立で先頭・解放で末尾（確立 W → T → E → A → V、解放 T → E → A → V → W）
- **Context**: Req 5.5 / 6.3 / 6.4
- **Selected Approach**: 初版は「確立・解放とも先頭」としたが、設計レビュー 1 回目の指摘 1 の改訂（`BindLateInputSource` の宣言 weight 再適用を遮断中だけ抑止）に合わせ、解放では W を末尾にして A / V の原本復元を遮断中に走らせる
- **Rationale**: ゲート型（W / T / E）を Replace 型（A / V）より先に立てる既存規則の延長。既存 4 ポートの相対順序（確立・解放とも T → E → A → V）と逆順ロールバックは変えない

### Decision: 5 ポートコンストラクタを正とし、4 ポート / 2 ポートは Null weight ポートへ委譲する互換コンストラクタとして残す
- **Context**: Req 6.4、既存テスト（`PlaybackUseCaseFourPortTests` 等）の互換
- **Rationale**: 既存の 2 ポート互換コンストラクタと同じ扱い。本番配線（`RecCharacterBinding`）は 5 ポートのみを使い、`RecCharacterBindingTests` で weight ポートが構築されることを固定する

### Synthesis
- **Generalization**: 「観測面 + 遮断面 + 注入面 + 基準確立」の 4 面を `IWeightInjectionGate` 1 本に束ねる形は `IExpressionActivationGate` と同型（Suspend / Resume / Inject / Reset / Collect）。レイヤー weight と入力源 weight は対象キーが違うだけで同じライフサイクルなので 1 つの gate / 1 つのポートに収める
- **Build vs Adopt**: 新規外部依存なし。in-flight フェンスは `System.Threading.Interlocked` / `SpinWait` の標準 API
- **Simplification**: サンプラークラスを設けない（上記 Decision）。weight 用の独立 id 表を作らず、スロット id は既存 Source id 表、レイヤー名は `IdDefine` 種別の追加で表現する。bulk の遮断は commit 時 1 判定に単純化

## Risks & Mitigations
- in-flight フェンスの有界スピンが上限に達するケース（ワーカーが長時間 `SetWeight` を抱えることは構造上無いが） — Warning を出して続行し、基準確立後の次フレームで観測される差分として表面化する
- レイヤー名重複プロファイル — 先勝ち解決を契約として明記。重複検出は本 spec のスコープ外（Revalidation Trigger）
- `IFacialInputObserver` / `IRecEventVisitor` / `RecBaselineState` / `PlaybackUseCase` の破壊的変更 — preview 段階で許容。実装・Fake はすべて同一リポジトリ内で同時改修
- inputsystem Tests asmdef の rec 参照追加 — `versionDefines` + `#if` で rec 不在環境でもコンパイルを壊さない

## References
- `.kiro/specs/rec-full-input-coverage/design.md`（4 ポート契約、入力源識別スコープ、ヘッダ flags 方式）
- `.kiro/specs/rec-recording-playback/design.md`、`.kiro/specs/rec-playback-input-exclusivity/design.md`
- Linear HID-80 / HID-137
