# Gap Analysis: timeline-playback-ux

- 作成日: 2026-10-05
- 対象 spec: `.kiro/specs/timeline-playback-ux/`（requirements.md は generated / 未承認。承認前のため、本分析は要件修正の材料も含む）
- 分析エンジン: Claude サブエージェント（validate-gap-agent）。codex 経路は本実行では使用していない（codex-first 手順の呼び出し A（Bash による書き込み監査ベースライン記録）が実行許可されず、コマンド定義どおり codex を起動せずフォールバック）
- 調査方法: 下記「参照ファイル」を全文または該当範囲を読んでの静的解析。Unity 上での実行確認は行っていない。実行で確認すべき項目は【要実行確認】、コードから直接読み取れず推定した箇所は【推測】と明記する
- パス表記: 本文は Unity プロジェクト `FacialControl/` からの相対

---

## 1. 分析サマリー

- 現状の Timeline 再生は「Profile SO の binding 設定（Target Layer Names / Channel Definitions）→ Layer.inputSources の `timeline:` 宣言 → Receiver への BakeAsset 手動登録 → Director への Track binding（Export 時に Director/Receiver を渡した場合のみ自動）」の 4 系統が揃わないと動かず、揃わない場合の大半が無言 return で終わる。要件が挙げる症状はコード上すべて確認できた
- 既存資産として再利用できるのは、sink 群（`TimelineBakedValueSink` / `TimelineExpressionStateSink` / `TimelineAnalogInputSource` / `TimelineGazeInputSource`）、Gaze 乗っ取り（`FacialTimelineReceiver.AttachGazeTakeovers` + `IInjectedInputSource` 契約）、Bake 生成（`TimelineBakeService` / `BakeSimulationHarness`）、ハッシュ鮮度判定（`FacialTimelineHashCalculator`、Runtime asmdef 内）、Timeline 検証（`FacialTimelineValidator` + TrackEditor/ClipEditor）、core の後付けバインド（`LayerUseCase.BindLateInputSource` / `UnbindLateInputSource`）
- 大きな欠落は 5 つ: (1) Receiver が Director / Track binding / Bake を自力で解決する経路が無い、(2) core の `FacialController` に「宣言なしでレイヤーへ入力源を接続・解放する public API」が無い、(3) Edit 時の Clip 変更検知と内部再ベイクが無い（保存時 / Play 突入時 / Play 終了ダイアログのみ）、(4) Receiver Inspector が無い（既定 Inspector に `bakeAsset` フィールドのみ）、(5) FacialController + 実 SkinnedMeshRenderer を通す PlayMode e2e が無い（既存 PlayMode テストは Fake/Noop registry に binding を直差し）
- 要件どおりに進めると矛盾・スコープ逸脱が出る箇所が 3 区分で計 15 件ある（§4）。特に **Req 3.4（Analog 消費先）**、**Req 7.1（Edit/Play 一致）**、**Req 10.3（REC 側 gaze 広告情報）**、**Req 1.1（4 手順に Track binding が含まれない）** は設計前に要件側の判断が必要
- Req 8.8 の `ContributeMask` 長 0 問題は、静的解析では「`timeline:{layer}:state` がレイヤー入力源に接続された状態で Expression が ON になると `LayerInputSourceAggregator` の `BitArray.Or` が `ArgumentException` を投げる」経路が実在する（§5）。Req 3.1 が `:state` sink の自動接続を求めているため、この修正が自動接続の前提条件になる

---

## 2. 既存資産マップ（現状の実装）

### 2.1 Runtime（`Packages/com.hidano.facialcontrol.timeline/Runtime`）

| コンポーネント | 現状 | 要件との関係 |
|---|---|---|
| `Adapters/AdapterBindings/TimelineAdapterBinding.cs` | `[SerializeField] List<string> targetLayerNames`、`List<TimelineValueChannelConfig> channelDefinitions`（Sub / AxisCount / IsGaze / TakeoverSourceId）。`OnStart` で層ごとに `TimelineExpressionStateSink`（`{slug}:{layer}:state`、blendShapeCount=0）と `TimelineBakedValueSink`（`{slug}:{layer}`、BlendShape 名は **`_receiver.BakeAsset` から OnStart 時に固定**）、チャネルごとに Analog / Gaze sink（Gaze は `{slug}:gaze-{n}` 診断 id）を registry に登録し `_receiver.Configure(...)`。Receiver が無ければ `AddComponent`。`Dispose` は **無条件に** `ReleaseAll` + `Destroy(_receiver)`。`IGazeSourceProvider` 実装は `channelDefinitions` 依存。有効/無効フラグは無い（`AdapterBindingBase` は `Slug` のみ） | Req 2.1/2.2/2.4/2.6、4.3、9.2/9.3 の変更対象 |
| `Adapters/FacialTimelineReceiver.cs` | `[SerializeField] FacialTimelineBakeAsset bakeAsset`（手動登録）。`Configure` で sink 群を受け取り、`BeginPlaybackSession(TimelineAsset)` は **`_hasProfile` が false なら `InvalidOperationException`**（Edit モードで必ず該当）。`InspectBake` が Missing / HashMismatch を 1 セッション 1 回警告し `BakeIssueDetected` を Editor へ通知。Gaze 乗っ取りは `AttachGazeTakeovers`（`GazeSourceIdConvention.TryParse` → `registry.Replace`、`IInjectedInputSource` 占有チェック）/ `ReleaseGazeTakeover`（参照同一性で復元）。`LastBakeInspectionStatus` が唯一の診断状態。`OnDisable/OnDestroy → ReleaseAll` | Req 3.6、4.2、4.6、8.7、9.4 の土台。Req 2.3/3.1/4.1/5/8 は未実装 |
| `Playables/FacialTrackMixerBehaviour.cs` | `ProcessFrame`: `_stateEvents.Length==0` → return、`ResolveReceiver(playerData)==null` → return、Edit なら `ApplyPreview`、**無条件に `receiver.BeginPlaybackSession(timeline)`**、`!TryGetExpressionSink(_layerName)` → return（トラック名不一致が無言）。`OnGraphStop/Pause/Destroy → ReleaseAll` | Req 7.2/7.3、8.1/8.2/8.6 の無言 return 箇所 |
| `Playables/FacialValueMixerBehaviour.cs` | 同様に receiver null → return、`BeginPlaybackSession` 無条件、`TryResolveSink` 失敗（`ChannelSubId` 不一致）→ return、sink null → return | Req 3.5、8.6 |
| `Tracks/FacialExpressionTrack.cs` | `[TrackBindingType(typeof(FacialTimelineReceiver))]`。Bake 参照フィールド無し。`CreateTrackMixer` で `TimelineStateEventCollector.Collect(this)` | Req 4.1 の候補置き場 |
| `Tracks/FacialValueTrack.cs` | `channelSubId` / `channelKind`。**`[TrackBindingType]` 無し**（Director の Inspector に binding スロットが出ない。binding は `SetGenericBinding` でのみ設定可能）。Bake 参照無し | Req 1.1/2.3/4.1 |
| `Adapters/InputSources/TimelineExpressionStateSink.cs` | `ExpressionTriggerInputSourceBase(blendShapeCount: 0, blendShapeNames: 空)` → `ContributeMask` 長 0 | Req 8.8（§5） |
| `Adapters/InputSources/TimelineAnalogInputSource.cs` | `IInputSource` + `IAnalogInputSource`。`BlendShapeCount=0`、`ContributeMask` 長 0、`TryWriteValues` は常に false（レイヤー値には寄与しない） | Req 3.4 |
| `Adapters/InputSources/TimelineGazeInputSource.cs` | Analog 派生 + `IInjectedInputSource`（`ReplacedSource` 保持） | Req 3.6 |
| `Adapters/InputSources/TimelineBakedValueSink.cs` | 疎 `ContributeMask`、`bakedBlendShapeNames` を **構築時** に受ける | Req 4.3（再構築 or 遅延初期化が必要） |
| `Adapters/Assets/FacialTimelineBakeAsset.cs` | `SourceHashHex` / `ProfileAssetGuid` / `SampleRate` / `ExpressionBakes[]`（layerName + curves）/ `ValueBakes[]`（sub + isGaze + axes）/ `StateEvents[]`。TimelineAsset のサブアセット（`AddObjectToAsset`）として保存 | Req 4 |
| `Domain/Services/FacialTimelineHashCalculator.cs` | Timeline 構造 + Profile Expressions + sampleRate の FNV-1a。**Runtime asmdef** にあり Receiver から実行可能 | Req 4.6、5.2(b) |
| `EditorPreview/FacialTimelineEditorPreviewBridge.cs` | Runtime → Editor の delegate 橋渡し（`ApplyPreview` / `GatherProperties`） | Req 7 |

### 2.2 Editor（`Packages/com.hidano.facialcontrol.timeline/Editor`）

| コンポーネント | 現状 | 要件との関係 |
|---|---|---|
| `TimelineBakeDirtyWatcher.cs` | 検知経路は 3 つのみ: `AssetModificationProcessor.OnWillSaveAssets`（保存時）、`PlayModeStateChange.ExitingEditMode`（Play 突入前にシーン上の Director を走査）、`EnteredEditMode`（Play 中の HashMismatch を `DisplayDialog` で報告しつつ修復）。**Edit 中の Clip 操作は検知しない**。`UpdateLoadedReceiverReferences` が `receiver.BakeAsset = bake` を **Undo/SetDirty 無し** で書く。Profile 解決は Bake の `ProfileAssetGuid` → Director にバインドされた Receiver の `FacialController.CharacterSO` の順 | Req 6.1〜6.7、9.1 |
| `FacialTimelineEditorPreview.cs` | `ApplyPreview`: `receiver.BakeAsset` null → 1 回警告して return、controller null → **無言 return**。`ApplyBlendShapes` は **全レイヤーの ExpressionBakes を単純加算し clamp01×100**（レイヤー優先度 / weight / overlay / base expression を考慮しない）。`ApplyGaze` は **`gazeChannels[target.ChannelIndex]`（Profile GazeChannels の index）と Bake の gaze 一覧 index を突き合わせる**（Req 7.4 の index 結合を確認） | Req 7.1〜7.6 |
| `BakeSimulationHarness.cs` | レイヤー単位で `OfflineExpressionSource` + `LayerInputSourceRegistry/Aggregator` を回しカーブ化。Profile は `FacialProfile` 値渡し | Req 7.1 の一致の片側 |
| `TimelineBakeService.cs` | `Bake(timeline, FacialCharacterProfileSO)` は **`profileAsset.BuildFallbackProfile()`（SO）** を使う。`IsStale` あり | Req 4.5 |
| `RecToTimelineExporter.cs` | `TryExportTimelineAsset(..., existingTimeline, director, receiver, sourceKindOverrides)`。Bake サブアセットを生成・更新。`director != null` なら `director.playableAsset=` を、`receiver != null` なら `receiver.BakeAsset=` と全 output track の `SetGenericBinding` を **Undo/SetDirty 無し** で実行。`track.ChannelSubId = REC の sourceId（例 `osc:gaze`、slug 込み）`。Gaze 判定は Profile `GazeChannels`（id / sourceIdLeft / sourceIdRight）+ `GazeSourceIdConvention.TryParse` + 全サンプル 2 軸 | Req 4.7、9.1、10.3〜10.5 |
| `RecTimelineExportWindow.cs` | Profile / Output Timeline / **Playable Director / Timeline Receiver** の ObjectField と Source Overrides（Auto/Analog/Gaze の EnumField、行ごとに理由表示なし） | Req 10.1/10.2/10.5/10.6 |
| `Validation/FacialTimelineValidator.cs` + `TrackEditors/*` | Timeline ウィンドウ上の errorText 表示（MissingExpressionId / GazeOutOfRange / EmptyClip / EmptyParentTrack）。Profile は `TimelineEditor.inspectedDirector` → Receiver → `CharacterSO.BuildFallbackProfile()`。`ClipEditor.OnClipChanged` / `TrackEditor.OnTrackChanged` は **未 override** | Req 6.1 の検知フック候補、Req 8.2 の Edit 側表示に流用可 |
| `FacialTimelinePreviewGazeTargets.cs` | 目ボーン解決。channel index ベース | Req 7.4 |
| Receiver の CustomEditor | **存在しない** | Req 5 は新規 |

### 2.3 core（`Packages/com.hidano.facialcontrol`）の統合面

| 項目 | 現状 |
|---|---|
| `FacialController.InitializeInternal` | `BuildAdapterBindingsChildScope`（VContainer child scope で各 binding の `OnStart`）→ `ResolveLayerInputSourcesFromRegistry(profile)`（**`profile.LayerInputSources` の宣言 id だけ** を registry から解決、未解決は Warning）→ `PopulateLayer2Provider`（系2 を overlay suppress 用 provider に流す）→ `new LayerUseCase(..., additionalSources, declaredIds)` → `SubscribeDeclaredLayerInputSources`（宣言 id ごとに `registry.Subscribe` → `HandleLayerInputSourceRebound` → `LayerUseCase.BindLateInputSource/UnbindLateInputSource`）。**宣言の無い id をレイヤーへ接続する public API は無い**（public は `Initialize` / `InitializeWithProfile` / `Activate` / `Deactivate` / `LoadCharacter` / `ReloadProfile` / `SetInputSourceWeight` / `SetLayerWeight` / `TryGetExpressionTriggerSourceById` / `SetActiveBoneSnapshots`） |
| `FacialController` の Profile ソース | `Initialize` → `LoadProfileFromCharacterSO(so)` → `so.LoadProfile()` = **StreamingAssets `FacialControl/{CharacterAssetName}/profile.json` を優先**、無ければ `BuildFallbackProfile()`。Bake / Validator / Exporter / EditorPreview は SO 側 `BuildFallbackProfile()` → Req 4.5 の食い違いを確認 |
| `HandleLayerInputSourceRebound` | `BindLateInputSource` + `UpdateObservedTriggerSource` のみ。**`_layer2Provider`（overlay suppress の active 取得）は更新しない** → 後付けで接続した系2（`:state` sink）は overlay suppress に乗らない |
| `LayerUseCase.BindLateInputSource(layerIdx, declaredId, source, weight)` / `UnbindLateInputSource(layerIdx, id)` | 既存。同 slotId はその場置換、無ければ末尾追加（容量超過時 `ExpandCapacityByOne` で再確保 = セッション開始時の確保として許容範囲）。Unbind は weight 列を詰める |
| `LayerInputSourceAggregator.AggregateInternal` | `sourceIsValid` のとき `layerMask.Or(source.ContributeMask)`（長さ検査なし）。`layerWeightSum` に加算、Σw>1 で Saturated |
| Gaze 入力源の再解決 | `SubscribeGazeInputSources` が全 active binding slug × `{slug}:{channel}[.left/.right]` を先読み購読し、Replace 時に `SetupGazeBoneProvider` を再構築 → Receiver の `registry.Replace` による乗っ取りは目ボーンへ届く（Req 3.6 の既存方式が有効） |
| Analog 入力源の消費者 | `AnalogExpressionInputSource` / `AnalogBlendShapeInputSource`（InputSystem binding が **構築時に直接参照** を保持。registry Subscribe 無し）、`AnalogBonePoseProvider`、`GazeChannelResolver`（gaze 専用）、`AnalogObservationSampler`（REC 観測）。**Timeline の `timeline:{sub}` を読む消費者は存在しない** |
| `Editor/Windows/Routing/Logic/InvalidIdValidator.cs` | `SourcePortEnumerator.EnumerateCanonicalIds(bindings, layerNames)` の集合に無い id を不正扱い。集合は `IAdapterBindingDefaultLayer(Inputs)` と `IAdapterBindingDeclaredInputs` の実装からのみ生成。`TimelineAdapterBinding` はどちらも未実装 → **`timeline:` id は現状すべて不正扱い**（Req 2.5 確認） |
| `Editor/Inspector/AdapterBindings/AdapterBindingsListView.cs` | `[CustomPropertyDrawer]` があれば行本文に使う。`IAdapterBindingHeaderSummaryProvider` でヘッダー要約。既存 Drawer 例: `OscReceiverAdapterBindingDrawer`（Slug フィールド + 設定）、lipsync / inputsystem / ifacialmocap にも各 1 つ |
| `FacialCharacterProfileSO` | `AdapterBindings`（SerializeReference）、`GazeChannels`、`Layers`。旧データ移行の既存パターン: `_legacyGazeConfigs` を隠しフィールドとして残し `OnAfterDeserialize` で 1 回警告 |
| `InputSourceId` | `^[a-zA-Z0-9_.\-:]{1,64}$`（ASCII のみ、`:` 複数可）。**日本語・空白を含むレイヤー名を `timeline:{layer}` にすると `Parse` が `FormatException`**（現状も同じだが、トラック名からの自動導出で顕在化する） |
| `GazeSourceIdConvention.IsValidChannelId` | `:` を含む id を拒否 → Export の `ChannelSubId`（`osc:gaze` 等）は Gaze チャネル id として不正（Req 10.4 確認） |

### 2.4 テスト資産

- EditMode: `TimelineAdapterBindingTests`（**reflection で `targetLayerNames` / `channelDefinitions` を直接操作**、`Dispose_ReleasesAndDestroysReceiver` が「Receiver を破棄する」ことを固定）、`FacialTimelineReceiverTests`（`Configure` の tuple 署名に依存）、`TimelineBakeDirtyWatcherTests`（`HandleEnteredEditModeNow_..._DisplaysDialog` がダイアログ表示を固定。`Assets/<guid>/` に Profile / Timeline を実アセットとして生成する fixture あり）、`RecToTimelineExportWorkflowTests`（`director` / `receiver` 引数で配線されることを固定。`.fcrec` fixture 生成あり）
- PlayMode: `TimelineLiveEquivalenceIntegrationTests` / `TimelineDegradationIntegrationTests` / `TimelineGcZeroGateTests` はいずれも `binding.OnStart(Fake/Noop registry)` + 手組み `LayerInputSourceAggregator`。FacialController・SkinnedMeshRenderer・Profile SO を通さない。PlayMode テスト asmdef は `TimelineBakeService`（Editor asmdef）を参照できている
- 再利用できる他パッケージの fixture: core PlayMode（`FacialControllerGazeChannelTests` / `FacialControllerOutputPipelineRegressionTests` / `MultiRendererTests` 等が `AddBlendShapeFrame` でメッシュを作り `AddComponent<FacialController>` + SO で初期化）、rec PlayMode（`RecCharacterBindingPlayModeTests`）
- Small の静的チェック: 既存 `TimelineAdapterBindingTests` は `[SmallTest]` のまま `new GameObject` + `AddComponent` を使っている【推測: `check-test-sizes.ps1` は GameObject 生成を禁止 API に含めていない】

---

## 3. Requirement ごとの分類

凡例: **既存**=既存資産で満たせる / **拡張**=既存コンポーネントの変更 / **新規**=新コンポーネント。Gap タグ: Missing / Unknown / Constraint

### Req 1: 最小手順での Play 再現
| AC | 分類 | 根拠・ギャップ |
|---|---|---|
| 1.1 | 新規（統合） | 現状は Profile 設定・inputSources 宣言・BakeAsset 登録・Track binding の 4 系統が必須。**Missing**: 要件の 4 手順には「Track → Receiver の generic binding」が含まれていないが、Mixer は `playerData`（= Director の generic binding）からしか Receiver を解決しない。`FacialValueTrack` は `TrackBindingType` 無しで Inspector から設定不可。設計で「再生開始時に Receiver が Director を見つけ全 Facial トラックに `SetGenericBinding(track, this)` する」等の自動化が必要（§4-A1） |
| 1.2 | 拡張 | Req 2〜4・10 の達成で成立 |
| 1.3 | 既存+拡張 | Gaze 乗っ取り機構は既存。takeover id の導出（3.7）が新規 |
| 1.4 | 既存（ゲート）+ Constraint | `TimelineGcZeroGateTests` が hot path のゼロ確保を固定。自動接続・診断・Bake 解決は **セッション開始時** に閉じる必要あり。`BindLateInputSource` の容量拡張確保はセッション開始時で許容範囲 |
| 1.5 | 拡張 | Req 2.1/2.4 の達成で成立。ただし **Profile に TimelineAdapterBinding を 1 つ追加する操作は残る**（Req 2.7 がその欠落を診断する前提） |

### Req 2: Profile 側設定の撤去と binding の格下げ
| AC | 分類 | 根拠・ギャップ |
|---|---|---|
| 2.1 | 拡張 | `targetLayerNames` / `channelDefinitions` 撤去、`enabled` フラグ追加（`AdapterBindingBase` に無い）。**Constraint**: 2.4 と両立させるなら旧フィールドは隠しフィールドとして残す必要（§4-A4） |
| 2.2 | 新規 | `[CustomPropertyDrawer(typeof(TimelineAdapterBinding))]` は無い。osc / lipsync の Drawer がパターン |
| 2.3 | 新規 | TimelineAsset からの導出サービス（root の `FacialExpressionTrack.name` → レイヤー、`FacialValueTrack.ChannelSubId/ChannelKind/クリップ軸数` → チャネル）。**Unknown**: Export は重なりを `{layer} Lane n` の子トラックに出す。`TimelineStateEventCollector.Collect` が子トラックを親に畳んでいるか未読【推測: 畳んでいる】。導出は root トラックのみを対象にするルールが要る。**Constraint**: 非 ASCII のレイヤー名は `InputSourceId.Parse` で例外（§4-C1） |
| 2.4 | 拡張 | `_legacyGazeConfigs` 方式（隠しフィールド + 1 回警告）が既存パターン。フィールドを物理削除すると検知不能 |
| 2.5 | 拡張（core Editor） | `TimelineAdapterBinding` に `IAdapterBindingDeclaredInputs` を実装するか、`SourcePortEnumerator` / `InvalidIdValidator` に prefix 許容を足す。**Unknown**: `IAdapterBindingDeclaredInputs.GetDeclaredInputSourceIds()` はレイヤー名を受け取らないので、`{slug}:{layer}` を静的宣言できない（`IAdapterBindingDefaultLayerInputs.GetDefaultLayerInputSources(layerName)` 経路ならレイヤー名が渡る）。Req 3.1 で宣言不要になるなら 2.5 の対象は「旧 Profile に残った宣言」のみ（§4-C8） |
| 2.6 | 拡張 | `OnStart` 冒頭で早期 return + Receiver 診断へ反映。Receiver は `controller.CharacterSO.AdapterBindings` から binding を探せる（既存 `ResolveProfileSource` と同系） |
| 2.7 | 新規（診断） | Receiver 側で `CharacterSO.AdapterBindings` に `TimelineAdapterBinding` が無いことを検出して 1 回警告 |

### Req 3: レイヤーへの自動接続とチャネルの消費先
| AC | 分類 | 根拠・ギャップ |
|---|---|---|
| 3.1 | **新規（core API）** | `LayerUseCase.BindLateInputSource` は既存だが `FacialController._layerUseCase` は private。`FacialController` に接続/解放 API（例: `TryBindLayerInputSource(layerName, id, source, weight)` / `UnbindLayerInputSource(layerName, id)`）の追加が必要。**Constraint**: 後付け系2 は `_layer2Provider` に乗らない（§4-B1）。**Constraint**: `:state` sink を接続する前に Req 8.8 の修正が必須（§5） |
| 3.2 | 拡張 | `UnbindLateInputSource` + Gaze 復元 + `ReleaseAll` の組合せ。Director 停止 / Play 終了 / Receiver 無効化の各経路は Mixer の `OnGraphStop/Pause/Destroy` と Receiver の `OnDisable/OnDestroy` に既にフックがある |
| 3.3 | 拡張 | `LayerInputSourceRegistry.FindSourceIndex(layerIdx, id)` で既存宣言を検出可能（ただし `_registry` は LayerUseCase 内部 → core API に「既に同 id が接続済みか」を返す口が要る） |
| 3.4 | **新規 + スコープ判断** | 現状 Analog チャネルは誰にも消費されない（§2.3）。乗っ取り方式は `AnalogExpressionInputSource` が直接参照保持のため registry Replace を見ない（§4-B2） |
| 3.5 | 新規（診断） | Mixer の `TryResolveSink` 失敗を Receiver 診断に昇格 |
| 3.6 | 既存 | `AttachGazeTakeovers` / `ReleaseGazeTakeover` がそのまま使える |
| 3.7 | 新規 | 導出候補: (a) `ChannelSubId` が `GazeSourceIdConvention.TryParse` に通る（REC の sourceId がそのまま入っている）ならそれを takeover id にする、(b) `ChannelSubId` がチャネル id のみなら Profile `GazeChannels[].providerSlug` / `sourceIdLeft/Right` または active binding slug から合成（`FacialController.SubscribeGazeInputSources` と同じ 3 候補）。複数 binding があると曖昧（§4-C4） |

### Req 4: Bake の自動解決と内部キャッシュ化
| AC | 分類 | 根拠・ギャップ |
|---|---|---|
| 4.1 | 新規 | Bake はサブアセットだが Runtime から辿る参照が無い（`AssetDatabase.LoadAllAssetsAtPath` は Editor 専用）。候補: (a) `FacialExpressionTrack` / `FacialValueTrack` に `[SerializeField] FacialTimelineBakeAsset` を持たせ `timeline.GetOutputTracks()` から取る、(b) `Marker` 派生を `timeline.markerTrack` に置く（`GetMarkers()` は Runtime 可）、(c) クリップ無しの専用 TrackAsset。(§4-C2) |
| 4.2 | 拡張 | `bakeAsset` を override として残し、null なら自動解決 |
| 4.3 | 拡張 | `TimelineBakedValueSink` は構築時に `bakedBlendShapeNames` を受ける → `BeginPlaybackSession` で再構築（registry への再登録 + レイヤー再接続）か、名前の遅延設定 API を追加。**Constraint**: `ValueProviderInputSourceBase` の `BlendShapeCount` は構築後不変。ホスト BlendShape 名（`ctx.BlendShapeNames`）は OnStart 時点で確定しているので、変動するのは baked 名側のみ |
| 4.4 | 拡張 | Exporter / DirtyWatcher の `FindBakeAsset` + `AddObjectToAsset` は既存。Project ウィンドウでサブアセットが見える点は `HideFlags.HideInHierarchy` で隠せる【要実行確認: TimelineAsset のサブアセットに HideFlags が効くか】 |
| 4.5 | 拡張（判断必要） | Bake = `BuildFallbackProfile()`（SO）、Runtime = `LoadProfile()`（JSON 優先）。ハッシュに Profile Expressions が入るため、JSON と SO が食い違えば常に HashMismatch。候補: Bake / Validator / Preview / Exporter を `LoadProfile()` に揃える（Editor でも `Application.streamingAssetsPath` は有効）、または Timeline 経路だけ SO を正にする（§4-C3） |
| 4.6 | 既存 | `InspectBake` の HashMismatch 分岐（継続 + 1 回警告） |
| 4.7 | 拡張 | Exporter の Bake 生成は既存。4.1 で決めた参照の書き込みを追加 |

### Req 5: Receiver への集約と Inspector 診断
| AC | 分類 | 根拠・ギャップ |
|---|---|---|
| 5.1 | 拡張 | Receiver に Director 参照（自動検出 or SerializeField）と診断状態モデルを追加 |
| 5.2 | **新規** | `FacialTimelineReceiver` の CustomEditor は無い。(a) Director: 同 GO → 親 → `FindObjectsByType<PlayableDirector>` で `playableAsset` の Facial トラックを持つもの、の探索規則が必要。(b) `FacialTimelineHashCalculator` は Runtime で計算可。(c) `CharacterSO.AdapterBindings` / `CharacterSO.Layers` と Timeline の root トラック名。(d) Edit では「接続予定」、Play では実接続。(e) `GetComponent<FacialController>()` |
| 5.3 | 新規 | Edit 時は Receiver が未 Configure なので、診断は Receiver 単体 + シーン参照で静的に計算する必要（既存 `ResolveProfileSource` / DirtyWatcher の `TryResolveDirectorProfile` が参考） |
| 5.4 | 新規 | 更新トリガ候補: `Undo.undoRedoPerformed`、`EditorApplication.hierarchyChanged`、`ObjectChangeEvents.changesPublished`、再ベイク完了イベント（DirtyWatcher に新設）、`EditorApplication.update` でのハッシュ比較ポーリング。**Unknown**: Director の `playableAsset` 変更を直接通知する API は無い【推測】→ ObjectChangeEvents か再描画時再評価 |
| 5.5 | 新規 | 診断状態モデル（Runtime 側）を Inspector が読む。`ActiveExpressionIds` / `GazeTakeoverBinding.IsAttached` は既存 |
| 5.6 | 既存パターン | `AdapterBindingsListView` 等が UI Toolkit |

### Req 6: Clip 編集の自動再ベイクと即時反映
| AC | 分類 | 根拠・ギャップ |
|---|---|---|
| 6.1 | **新規** | Edit 中の検知経路が無い。Unity Timeline 1.8 の `ClipEditor.OnClipChanged(TimelineClip)` / `TrackEditor.OnTrackChanged(TrackAsset)`（既存 `FacialExpressionClipEditor` / `FacialValueClipEditor` / `FacialExpressionTrackEditor` で override 可能）、`ObjectChangeEvents.changesPublished`、`Undo.postprocessModifications`。**Unknown**: OnClipChanged が Undo/Redo・Inspector からの `ExpressionId` 編集・スクリプト編集で呼ばれるか未確認【要実行確認】 |
| 6.2 | 既存+拡張 | `UpdateBakeAsset` は同一サブアセットへ in-place コピーするため、`FacialTimelineEditorPreview` は次の `ApplyPreview` で新カーブを読む。Timeline ウィンドウの再評価には `TimelineEditor.Refresh(RefreshReason.ContentsModified)` 等が必要【要実行確認】 |
| 6.3 | 既存 | 4.1 で Bake 参照が TimelineAsset 内にあれば Play は新 Bake を読む（`ExitingEditMode` の再ベイクも既存） |
| 6.4 | 拡張 | `HandleEnteredEditModeNow` / `DisplayDialog` / `RepairRunResult.HasDialog` の撤去。**Constraint**: `TimelineBakeDirtyWatcherTests.HandleEnteredEditModeNow_..._DisplaysDialog` が破綻するため書き換え |
| 6.5 | 新規 | デバウンス（`EditorApplication.update` + 時刻 or `delayCall`）。`_suppressSaveHook` の既存ガードは保存ループ防止用で再利用可 |
| 6.6 | 拡張 | `TryAutoRebake` の `failureReason` は既存。Console 出力と「前回 Bake 保持」（失敗時に `created` のみ破棄する現行ロジック）が土台 |
| 6.7 | 新規 | `Undo.undoRedoPerformed` 購読 |

### Req 7: Edit プレビューと Play 再生の一致
| AC | 分類 | 根拠・ギャップ |
|---|---|---|
| 7.1 | **新規（大）** | 現プレビューは全レイヤー加算 + clamp01 のみ。ランタイムは `LayerBlender`（優先度 / レイヤー weight / overlay / base expression / 他 live 入力の加重和）。一致させるには Bake をレイヤー単位のまま持ち、Edit 側でも `LayerUseCase` 相当の合成を回す必要（`BakeSimulationHarness` がレイヤー単体の Aggregator 利用例）。比較時刻・許容誤差は設計判断（§4-B3） |
| 7.2 | 拡張 | Mixer の `receiver.BeginPlaybackSession(timeline)` を `Application.isPlaying` で分岐 |
| 7.3 | 拡張 | 現状は `InvalidOperationException`（`_hasProfile` false）。7.2 の分岐で例外経路自体が消える。警告の 1 回化は `MissingBakeWarnings`（instanceID HashSet）と同方式 |
| 7.4 | 拡張 | `ApplyGaze` / `FacialTimelinePreviewGazeTargets.Resolve` の `ChannelIndex` 結合を `ValueChannelBake.Sub`（= チャネル id）での辞書解決に変更 |
| 7.5 | 拡張 | 4.5 の決定に従う |
| 7.6 | 新規 | 7.1 と同じ |

### Req 8: 無言 early return の排除と Play 開始時の明示診断
| AC | 分類 | 根拠・ギャップ |
|---|---|---|
| 8.1〜8.5 | 新規（診断） | 無言箇所の棚卸し: `FacialTrackMixerBehaviour.ProcessFrame`（receiver null / sink 不一致）、`FacialValueMixerBehaviour.ProcessFrame`（receiver null / sub 不一致 / sink null）、`FacialTimelineReceiver.SampleExpressionValues`（value sink 無し）、`FacialTimelineEditorPreview.ApplyPreview/GatherProperties`（receiver null / controller null）、`TimelineBakeDirtyWatcher.TryResolveDirectorProfile` 系の `continue`。Mixer は毎フレーム呼ばれるため、診断は Receiver 側の「セッション単位 1 回」状態に集約する必要（1.4 の GC 制約も同じ理由） |
| 8.6 | 拡張 | 上記の全経路に診断状態の更新を入れる |
| 8.7 | 既存+拡張 | `_playbackSessionBegun` ガードが 1 セッション 1 回の既存機構。Edit モードの「1 回」定義は別途必要（§4-C6） |
| 8.8 | **拡張（core or timeline）** | §5。静的解析では実在。修正候補: `TimelineExpressionStateSink` の mask 長を `ctx.BlendShapeNames.Count` に揃える（timeline 側のみ）/ `LayerInputSourceAggregator` に長さ不一致の防御（core） |
| 8.9 | 新規 | 診断状態モデル |

### Req 9: Undo / Dirty とライフサイクル
| AC | 分類 | 根拠・ギャップ |
|---|---|---|
| 9.1 | 拡張 | Exporter（`director.playableAsset=`、`receiver.BakeAsset=`、`SetGenericBinding`）と DirtyWatcher（`UpdateLoadedReceiverReferences`）に `Undo.RecordObject` + `EditorUtility.SetDirty` が無いことを確認。10.5 で Exporter の配線自体が消えれば対象は DirtyWatcher と Receiver 自動バインド（Edit 時に行う場合）に縮む |
| 9.2 / 9.3 | 拡張 | `Dispose` は無条件 Destroy。`OnStart` で `GetComponent` ヒット時と `AddComponent` 時を区別するフラグが必要。**Constraint**: `TimelineAdapterBindingTests.Dispose_ReleasesAndDestroysReceiver` の前提変更 |
| 9.4 | 拡張 | `OnDisable/OnDestroy → ReleaseAll` は既存。3.2 の解放を `ReleaseAll` に統合 |

### Req 10: REC Export ウィンドウの見直しと id 形式の統一
| AC | 分類 | 根拠・ギャップ |
|---|---|---|
| 10.1 / 10.2 | 拡張 | `_sourceOverrideModes` と `CreateSourceRow` が対象。トリガー専用 source の判別には REC の SourceIds だけでは不足（REC は Trigger イベントの sourceId も持つ。Analog イベントを持つ source だけ Value Track になる）→ 行の種別判定は `readResult.Timeline` の AnalogValue イベント有無から導出【推測: `RecEventSequenceAdapter` 経由で取れる】 |
| 10.3 | **要件修正 or スコープ追加** | `.fcrec` には gaze の広告情報が無い（`RecEvent.IdDefinitionKind` は None/Source/Expression、ヘッダー flags は FullInputBaseline のみ）。Exporter は既に Profile `GazeChannels` + `GazeSourceIdConvention.TryParse` を使っている。未使用なのは Profile の各 binding の `IGazeSourceProvider.GetGazeSourceDeclarations()`（§4-A2） |
| 10.4 | 拡張 | `ChannelSubId`（`osc:gaze`）vs `IsValidChannelId`（`:` 拒否）の不一致を確認。`slug:sub` を許容すると 3.7(a) の導出根拠になり、sub 正規化すると失われる（§4-C5） |
| 10.5 | 拡張 | `TryExportTimelineAsset` の `director` / `receiver` 引数と Window の 2 ObjectField を撤去。`RecToTimelineExportWorkflowTests` の配線アサートを書き換え |
| 10.6 | 拡張 | `SetStatus` の文言追加 |
| 10.7 | 新規（診断） | 4.1 の参照が無い TimelineAsset = 旧形式として判定可能 |

### Req 11: e2e PlayMode テストとテスト方針準拠
| AC | 分類 | 根拠・ギャップ |
|---|---|---|
| 11.1〜11.3 | **新規** | FacialController + SO + 実 SkinnedMeshRenderer を通す PlayMode テストが Timeline パッケージに無い。部品は揃っている: core PlayMode の `AddBlendShapeFrame` メッシュ生成 + `AddComponent<FacialController>` パターン、`TimelineBakeDirtyWatcherTests.CreateFixture` の実アセット生成、`RecToTimelineExportWorkflowTests` の `.fcrec` fixture、PlayMode asmdef からの Editor（Exporter）参照。**Constraint**: Profile SO に `TimelineAdapterBinding` を SerializeReference で追加する fixture、Director の `timeUpdateMode=Manual` + `Evaluate` でフレーム制御、Gaze は目ボーン回転で検証 |
| 11.4 | 新規 | 診断状態モデル（enum/struct）を Runtime に置く必要 |
| 11.5 | 新規 | 7.1 依存 |
| 11.6 / 11.8 | 既存方針 | `docs/testing.md` の Small / Medium。導出・id 検証・Bake 解決は TimelineAsset を `CreateInstance` で作って EditMode（Small 可否は静的チェック次第）|
| 11.7 | Constraint | 既存 `{Target}Tests.cs`: `TimelineAdapterBindingTests` / `FacialTimelineReceiverTests` / `TimelineBakeDirtyWatcherTests` / `RecToTimelineExporterTests` / `RecToTimelineExportWorkflowTests` / `FacialTrackMixerBehaviourTests` / `FacialTimelineHashCalculatorTests` 等に追記。新規クラス（Inspector / 導出サービス / 診断モデル / Bake 参照）は新ファイル |

---

## 4. 実装戦略上の重大な矛盾・分岐

### A. 要件の記述修正で解消できるもの

- **A1. Req 1.1 の「4 手順」に Track → Receiver の binding が含まれていない。** Mixer は Director の generic binding からしか Receiver を取れず、`FacialValueTrack` は Inspector から設定できない。要件に「Timeline 統合が再生開始時（または Edit 時）に Facial トラックの binding を Receiver へ自動設定する」を AC として追加するか、手順 3 を「Director に TimelineAsset をセット（トラック binding は自動）」と明記する必要がある。逆に自動化しない場合、4 手順では再現しない
- **A2. Req 10.3「REC 側の gaze 広告情報」は `.fcrec` に存在しない。** 使えるのは Export 時に渡される Profile SO の情報（`GazeChannels`、各 binding の `IGazeSourceProvider` 宣言）と source id 規約。記述を「Profile の binding が宣言する gaze source（`IGazeSourceProvider`）と `GazeSourceIdConvention`」に修正するか、`.fcrec` 拡張を B に昇格させる（後者は Boundary Context の「.fcrec に手を入れない」と衝突）
- **A3. Req 8.8「推測段階」→ 静的解析で経路実在を確認済み（§5）。** 「テストで実在確認し、実在すれば修正」の構造は維持できるが、設計時点で「実在する前提で修正方式を決める」と書き換えると迷いが減る
- **A4. Req 2.1「Slug と有効/無効のみを保持」と Req 2.4「旧フィールドが残っていることを警告」は、旧フィールドを物理削除すると両立しない。** Unity は消えたフィールドのデータを無言で捨てる。警告を出すには `[SerializeField, HideInInspector]` の legacy フィールドを残す（`FacialCharacterProfileSO._legacyGazeConfigs` と同方式）必要がある。2.1 を「Inspector で編集可能な設定は Slug と有効/無効のみ」に調整するか、2.4 の警告要件を外すかの選択
- **A5. Req 3.1 が `timeline:{layer}:state` を「レイヤー入力源へ接続」としている点。** state sink は値を持たず（`BlendShapeCount=0`）、接続すると `layerWeightSum` に weight が足されて Saturated 判定や `hasAnyValidSource` に影響するだけで BlendShape には寄与しない。state sink の実用途は overlay suppress の active 取得（`_layer2Provider`）と REC 観測（`UpdateObservedTriggerSource`）。要件を「状態供給先（active provider / 観測）へ登録」と書き直すと、§5 の例外も接続しないことで回避できる。ただし memory 上「overlay suppress の系2統合は未実装」とあり、現時点でどこへ繋げば suppress が効くかは設計で確認が要る【推測含む】

### B. 元の依頼に無いスコープ追加が必要になるもの

- **B1. Req 3.1 自動接続のための core API 新設。** 依頼文は「FacialController のレイヤー接続（inputSources 解決）に限定して触る」としており範囲内だが、現状の public 面には接続 API が無く、`FacialController` に `TryBindLayerInputSource / UnbindLayerInputSource`（+ 接続済み判定）の **新設** が必要。さらに後付け系2 を overlay suppress に反映するには `HandleLayerInputSourceRebound` → `_layer2Provider` の更新も要り、これは「既存 OSC / InputSystem の接続挙動は変えない」との境界に近い
- **B2. Req 3.4 Analog 消費先。** 「Gaze と同じ乗っ取り方式」は、Analog の実消費者 `AnalogExpressionInputSource` / `AnalogBlendShapeInputSource` が InputSystem binding 構築時の直接参照で動いており registry Replace を見ないため、**core（or inputsystem パッケージ）側に再解決の仕組みを足さないと効かない**（`GazeBonePoseProvider` が Subscribe で再構築されるのとは対照的）。選択肢: (a) core/inputsystem に analog 再解決を追加（スコープ追加）、(b) Analog は本 spec では「Play 時の消費先なし」と診断表示し別 Issue へ、(c) Bake 時に analog→Expression のマッピングを使って BlendShape カーブ化（InputSystem binding の設定に依存するためパッケージ依存方向の問題あり）。【推測】REC の `RecAnalogInjector` も同じ理由で InputSystem analog-expression へは効いていない可能性があるが、本 spec の範囲外
- **B3. Req 7.1 / 7.6 Edit プレビューと Play の一致。** 現プレビューは合成規則をほぼ再現していないため、Editor 側に `LayerBlender` 相当（優先度・レイヤー weight・overlay・base expression）を再現する実装が必要。Bake 自体はレイヤー単位で保持されているので入力は揃っているが、作業量は Req 中最大級。「同一の TimelineAsset・Profile・時刻」に加え「他の live 入力が無いこと」が前提条件になる
- **B4. Req 11.1 e2e のための fixture 整備。** `.fcrec` 生成（rec の writer API）、Profile SO への `TimelineAdapterBinding` 追加、BlendShape 付きメッシュ生成、Director 手動 Evaluate を 1 本の PlayMode テストに束ねるヘルパーが Timeline パッケージ内に無い。Tests/Shared asmdef は存在するが中身は空
- **B5. Req 5 Receiver Inspector の新規 UI Toolkit Editor。** 依頼どおりだが完全新規で、診断状態モデル（Runtime）と Editor の二層になる

### C. 解釈分岐（設計で決める必要があるもの）

- **C1. Req 2.3 トラック名 → レイヤー導出と id 形式。** (i) Export は重なりを `{layer} Lane n` 子トラックに出すため、導出は root トラック限定で子は親レイヤーに畳む必要【推測: `TimelineStateEventCollector` が既にそうしている】。(ii) `InputSourceId` は ASCII のみ。日本語・空白を含むレイヤー名で `InputSourceId.Parse("timeline:" + layerName)` が例外になる。sink id をレイヤー index やサニタイズ名にする、またはレイヤー名を id に含めない設計が要る（プロダクトは 2 バイト文字を許容する方針）
- **C2. Req 4.1 Bake 参照の置き場。** Track フィールド（全トラックが同じサブアセットを指す。トラック追加時の補完が必要）/ Marker（1 個で済むが markerTrack の生成と Timeline ウィンドウでの表示が出る）/ 専用 TrackAsset（行として見える）。Runtime 解決可能性はいずれも満たす
- **C3. Req 4.5 Profile ソースの正。** Runtime は JSON 優先、Editor 系は SO。JSON は `FacialCharacterProfileExporter`（AutoExport）が書くため通常は SO の写しだが遅延し得る。Bake 側を `LoadProfile()` に揃える案と、Timeline 経路だけ SO 固定にする案（Runtime が JSON で差し替えられる設計思想と衝突）
- **C4. Req 3.7 Gaze takeover id の導出。** `ChannelSubId` が REC の sourceId（`osc:gaze`）なら解析してそのまま使える。チャネル id のみの場合は Profile `GazeChannels` の `providerSlug` / `sourceIdLeft/Right`、無ければ active binding slug 全候補（複数 binding で曖昧）
- **C5. Req 10.4 `slug:sub` 許容 vs sub 正規化。** 許容すれば C4(a) が成立、正規化すれば takeover 先情報を別途 Track に持たせる必要。両者は相互依存
- **C6. Req 8.7「1 セッション 1 回」と Edit モードの「1 回」。** Edit にはセッションが無い。「Receiver インスタンスごと 1 回（`MissingBakeWarnings` 方式、ドメインリロードでリセット）」「Bake 更新ごとにリセット」等の定義が必要
- **C7. Req 6.1 変更検知の組合せ。** `OnClipChanged` / `OnTrackChanged` は Timeline ウィンドウ操作で呼ばれる（Unity Timeline 1.8 docs で API 存在を確認）。Undo/Redo・Inspector での `ExpressionId` 変更・スクリプト編集は別経路（`Undo.undoRedoPerformed` / `ObjectChangeEvents`）。どこまで拾うかで実装量が変わる
- **C8. Req 2.5 の対象範囲。** Req 3.1 で `timeline:` 宣言が不要になるなら、2.5 は「旧 Profile に残った宣言を不正扱いしない」だけになる。その場合 `IAdapterBindingDeclaredInputs` 実装（slug prefix を静的宣言）か、Routing エディタ側の prefix 許容のどちらが自然か
- **C9. Receiver の Director 解決規則（Req 5.2a）。** 同 GameObject / 親 / シーン全体から「Facial トラックを持つ TimelineAsset をバインドしている Director」を探すのか、Receiver に `PlayableDirector` の SerializeField を持たせ自動補完するのか。複数 Director が同じ Receiver を指す構成の扱いも未定義

---

## 5. Req 8.8 の静的解析結果（ContributeMask 長 0）

- `TimelineExpressionStateSink` は `ExpressionTriggerInputSourceBase(blendShapeCount: 0, ...)` で構築され、基底の `_emptyMask = new BitArray(0)` を `ContributeMask` として返す
- `ExpressionTriggerInputSourceBase.TryWriteValues` は `_activeExpressionIds.Count == 0 && _isComplete` のときだけ false。スタックに Expression が積まれる、または遷移中なら `BlendShapeCount` に関係なく true
- `LayerInputSourceAggregator.AggregateInternal` は `sourceIsValid` のとき `layerMask.Or(source.ContributeMask)`（`layerMask` 長 = blendShapeCount、長さ検査なし）。`BitArray.Or` は長さ不一致で `ArgumentException` を投げる
- したがって **`timeline:{layer}:state` がレイヤー入力源として接続され、クリップが Expression を ON にした瞬間から毎フレーム例外** になる経路が実在する。現状の README はユーザーに `timeline:{layer}` / `:state` の両 id を登録済みと案内しており、`:state` を inputSources に書いたユーザーは踏む
- `TimelineAnalogInputSource` も mask 長 0 だが `TryWriteValues` が常に false なので安全
- 同種の不具合は iFacialMocap の null mask で過去に実機再現している（memory: 2026-08-07 「Array lengths must be the same」）
- 修正候補: (a) `TimelineExpressionStateSink` の mask 長を `ctx.BlendShapeNames.Count`（全 false）に揃える（timeline 側のみ、Req 8.8 の文言どおり）、(b) Aggregator に長さ不一致の防御を入れる（core、他 binding も守れる）、(c) A5 のとおり state sink をレイヤーに接続しない
- 【要実行確認】EditMode テスト（`LayerInputSourceRegistry` + Aggregator に state sink を直差しし `TriggerOn` 後に `Aggregate`）で例外を再現してから修正する

---

## 6. 実装アプローチの選択肢

### Option A: 既存コンポーネントの拡張中心
- `TimelineAdapterBinding.OnStart` に導出ロジックを残し、Receiver は現状の `Configure` 署名を維持。`FacialTimelineReceiver` に Director 検索・Bake 解決・診断・自動接続をすべて追加
- 長所: ファイル数が増えない、既存テストの修正範囲が読みやすい
- 短所: `FacialTimelineReceiver`（既に 660 行）が 1,000 行超級になり、Edit 診断（MonoBehaviour 外から静的計算したい）と Play 接続が同じクラスに同居する。OnStart 時点では TimelineAsset が分からない（Director がまだ再生していない）ため、OnStart で sink を作る現構造と Req 2.3「再生開始時に導出」が噛み合わない

### Option B: 新規コンポーネントへ責務分割
- Runtime: `TimelineChannelDeriver`（TimelineAsset → レイヤー / チャネル定義の純粋関数）、`FacialTimelineBakeLocator`（Track/Marker 参照からの Bake 解決 + 鮮度）、`TimelineLayerConnector`（core 新 API を使った接続 / 解放と重複判定）、`FacialTimelineDiagnostics`（状態 enum/struct と 1 回警告の管理）、Receiver はこれらのファサード
- Editor: `FacialTimelineReceiverInspector`（UI Toolkit）、`TimelineEditChangeWatcher`（OnClipChanged / Undo / ObjectChangeEvents + デバウンス → `TimelineBakeDirtyWatcher.Rebake`）、`FacialTimelineEditorPreview` の合成部を `TimelinePreviewCompositor` に分離
- core: `FacialController` に接続 / 解放 / 接続済み判定の 3 メソッド（+ 必要なら `_layer2Provider` 更新）
- 長所: 導出・解決・診断を EditMode（Small / Medium）で単体テストでき、Req 11.4/11.8 と相性が良い。Edit 診断と Play 接続の分離が自然
- 短所: ファイルと asmdef 内の公開面が増える。`FacialTimelineReceiver` と binding の責務境界を設計で明文化しないと二重管理になる

### Option C: ハイブリッド（段階導入）
- 第 1 段: core API 新設 + Req 8.8 修正 + Receiver の Director/Bake/接続の自動化（Option B の Runtime 部分）+ e2e テスト。Profile 側フィールドは legacy として残し警告のみ
- 第 2 段: Receiver Inspector と Edit 変更検知 / 再ベイク、ダイアログ撤去
- 第 3 段: Edit プレビュー合成の一致（7.1）、Export ウィンドウ整理、Analog 消費先（B2 の判断後）
- 長所: 受け入れ条件 (1)(3)(4) を先に固定でき、B2 / B3 の判断を後ろに置ける
- 短所: 段間で README / テストが一時的に二重構造になる。spec を 1 本で回すなら tasks の依存関係設計が重要

---

## 7. Effort / Risk（領域別）

| 領域 | Effort | Risk | 一行根拠 |
|---|---|---|---|
| Req 2（binding 格下げ + Drawer + 導出） | M | Medium | Drawer はパターンあり。導出は C1（非 ASCII id / 子トラック）の決定次第 |
| Req 3.1〜3.3（core 接続 API + 自動接続） | M | Medium | `LayerUseCase` に土台あり。`_layer2Provider` 連動と Req 8.8 が前提 |
| Req 3.4（Analog 消費先） | S〜L | **High** | 選択肢により「診断表示のみ」から core 改修まで幅が大きい（B2） |
| Req 4（Bake 自動解決 / Profile ソース統一） | M | Medium | 参照置き場と Profile 正の 2 判断が波及（Validator / Preview / Exporter） |
| Req 5（Receiver Inspector + 診断モデル） | M | Low〜Medium | 新規だがパターン（UI Toolkit、`BakeInspectionStatus`）あり。更新トリガの網羅が未知 |
| Req 6（Edit 変更検知 + 再ベイク + ダイアログ撤去） | M | Medium | Timeline Editor コールバックの発火条件が未検証（C7）。保存ループ回避のガードは既存 |
| Req 7（Edit/Play 一致） | L | **High** | 合成規則の再現が新規実装。比較テストの許容誤差設計も必要（B3） |
| Req 8（無言 return 排除） | S〜M | Low | 箇所は棚卸し済み。1.4 の GC 制約内で状態集約が必要 |
| Req 9（Undo / Dispose） | S | Low | 箇所特定済み。既存テスト 1 件の前提変更 |
| Req 10（Export 見直し） | S〜M | Low〜Medium | A2 の要件解釈次第。テスト 2 ファイルの書き換え |
| Req 11（e2e） | M | Medium | 部品は他パッケージにあるが Timeline 内で束ねる作業。Gaze 検証は目ボーン回転で行う必要 |
| 全体 | **L〜XL** | Medium〜High | core 変更 + Editor 新規 + e2e の 3 面同時。B2 / B3 の判断で L と XL が分かれる |

---

## 8. 設計フェーズへの持ち越し（Research Needed / 判断事項）

1. **【要実行確認】Req 8.8**: state sink をレイヤーに直差しした Aggregator テストで `ArgumentException` を再現する（§5）
2. **【要実行確認】Timeline Editor コールバック**: `ClipEditor.OnClipChanged` / `TrackEditor.OnTrackChanged` が Undo/Redo、Inspector 経由の `ExpressionId` 変更、クリップ削除で呼ばれるか。Unity Timeline 1.8 API 定義は docs で確認済み（出典は末尾）
3. **【要実行確認】TimelineAsset のサブアセットに `HideFlags.HideInHierarchy` を付けた場合の Project ウィンドウ表示と `AssetDatabase.LoadAllAssetsAtPath` の挙動**（Req 4.4）
4. **【要実行確認】`TimelineEditor.Refresh` の種別**（`RefreshReason.ContentsModified` 等）で Edit プレビューが再評価されるか（Req 6.2）
5. **【要確認】`TimelineStateEventCollector.Collect` が子トラック（`{layer} Lane n`）を親レイヤーへ畳んでいるか**（C1 の前提）
6. **【判断】Analog チャネルの消費先**（B2）: 本 spec で core/inputsystem の analog 再解決まで踏むか、診断表示 + 別 Issue にするか
7. **【判断】Edit/Play 一致の定義**（B3）: 比較対象を「Timeline sink のみ接続 / 他 live 入力なし / レイヤー weight 既定」に限定するか、許容誤差（既存 PlayMode の Linear 1e-4 / Curve 2e-2 が参考値）
8. **【判断】Profile ソースの正**（C3）と **Bake 参照の置き場**（C2）
9. **【判断】`timeline:{layer}` の id 形式**（C1-ii）: 非 ASCII レイヤー名の扱い
10. **【判断】Gaze takeover id 導出規則**（C4）と `ChannelSubId` 形式（C5）の同時決定
11. **【判断】Req 10.3 の読み替え**（A2）と Req 1.1 の Track binding 自動化の明記（A1）
12. **【確認】`check-test-sizes.ps1` が Small で `GameObject` / `ScriptableObject.CreateInstance` を許容しているか**（Req 11.6 の配置判断）
13. **【確認】Receiver が Director を自動発見する際、`FindObjectsByType<PlayableDirector>` のコストと複数 Director の扱い**（C9）

---

## 9. 既存テストへの影響一覧

- `Tests/EditMode/TimelineAdapterBindingTests.cs`: reflection で `targetLayerNames` / `channelDefinitions` を触る 3 テスト、`Dispose_ReleasesAndDestroysReceiver` が前提変更
- `Tests/EditMode/FacialTimelineReceiverTests.cs`: `Configure` の tuple 署名、`BeginPlaybackSession(profile, timeline)` 直呼び
- `Tests/EditMode/TimelineBakeDirtyWatcherTests.cs`: `HandleEnteredEditModeNow_..._DisplaysDialog`
- `Tests/EditMode/RecToTimelineExportWorkflowTests.cs`: `director` / `receiver` 引数と `receiver.BakeAsset` / `GetGenericBinding` のアサート
- `Tests/PlayMode/*` 3 ファイル: `MutableTargetLayerNames` reflection ヘルパーに依存。e2e 化に合わせて fixture を差し替えるか、導出サービスに TimelineAsset を渡す形へ移行
- 既存の `LogAssert.Expect(..., Regex(@"\[FacialTimelineReceiver\].*BakeAsset"))` はログ文言一致（test-policy 上は「実装のテスト」）。診断状態の値アサートへ置き換える好機

---

## 10. 参照ファイル（リポジトリ相対）

Timeline Runtime（`FacialControl/Packages/com.hidano.facialcontrol.timeline/Runtime/`）:
- `Adapters/AdapterBindings/TimelineAdapterBinding.cs`、`Adapters/FacialTimelineReceiver.cs`
- `Playables/FacialTrackMixerBehaviour.cs`、`Playables/FacialValueMixerBehaviour.cs`
- `Tracks/FacialExpressionTrack.cs`、`Tracks/FacialValueTrack.cs`
- `Adapters/InputSources/TimelineExpressionStateSink.cs`、`TimelineAnalogInputSource.cs`、`TimelineGazeInputSource.cs`、`TimelineBakedValueSink.cs`
- `Adapters/Assets/FacialTimelineBakeAsset.cs`、`Domain/Services/FacialTimelineHashCalculator.cs`、`EditorPreview/FacialTimelineEditorPreviewBridge.cs`

Timeline Editor（`FacialControl/Packages/com.hidano.facialcontrol.timeline/Editor/`）:
- `TimelineBakeDirtyWatcher.cs`、`FacialTimelineEditorPreview.cs`、`BakeSimulationHarness.cs`、`TimelineBakeService.cs`
- `RecToTimelineExporter.cs`、`RecTimelineExportWindow.cs`、`FacialTimelinePreviewGazeTargets.cs`
- `Validation/FacialTimelineValidator.cs`、`TrackEditors/FacialExpressionTrackEditor.cs`、`TrackEditors/FacialExpressionClipEditor.cs`
- `../README.md`、`../Documentation~/README.md`

Timeline Tests（`FacialControl/Packages/com.hidano.facialcontrol.timeline/Tests/`）:
- `EditMode/TimelineAdapterBindingTests.cs`、`EditMode/FacialTimelineReceiverTests.cs`、`EditMode/TimelineBakeDirtyWatcherTests.cs`、`EditMode/RecToTimelineExportWorkflowTests.cs`
- `PlayMode/TimelineLiveEquivalenceIntegrationTests.cs`、`PlayMode/TimelineDegradationIntegrationTests.cs`、`PlayMode/TimelineGcZeroGateTests.cs`

core（`FacialControl/Packages/com.hidano.facialcontrol/`）:
- `Runtime/Adapters/Playable/FacialController.cs`（L163-266 ライフサイクル / Initialize、L297-342 InitializeInternal、L522-563 ResolveLayerInputSourcesFromRegistry、L571-593 PopulateLayer2Provider、L990-1037 SubscribeDeclaredLayerInputSources / HandleLayerInputSourceRebound、L1039-1086 SubscribeGazeInputSources、L1484〜 Cleanup、L1552 LoadProfileFromCharacterSO）
- `Runtime/Application/UseCases/LayerUseCase.cs`（L332-443 BindLateInputSource / UnbindLateInputSource）
- `Runtime/Domain/Services/LayerInputSourceAggregator.cs`（L306-368、特に L343 `layerMask.Or`）
- `Runtime/Domain/Services/LayerInputSourceRegistry.cs`（L198-215 FindSourceIndex、L267-318 TryAddSource）
- `Runtime/Domain/Services/ExpressionTriggerInputSourceBase.cs`（L150-203 コンストラクタ、L387-400 TryWriteValues）
- `Runtime/Adapters/ScriptableObject/FacialCharacterProfileSO.cs`（L147-180 BuildFallbackProfile / LoadProfile）
- `Runtime/Domain/Models/InputSourceId.cs`、`Runtime/Domain/Models/GazeSourceIdConvention.cs`
- `Runtime/Domain/Adapters/AdapterBindingBase.cs`、`AdapterBuildContext.cs`、`IInputSourceRegistry.cs`、`IAdapterBindingDeclaredInputs.cs`、`IAdapterBindingDefaultLayer.cs`、`GazeSourceContracts.cs`
- `Runtime/Adapters/DependencyInjection/FacialControllerLifetimeScope.cs`、`Runtime/Adapters/InputSources/AnalogExpressionInputSource.cs`、`Runtime/Adapters/ScriptableObject/GazeChannelResolver.cs`
- `Editor/Windows/Routing/Logic/InvalidIdValidator.cs`、`SourcePortEnumerator.cs`、`RoutingGraphModelBuilder.cs`
- `Editor/Inspector/AdapterBindings/AdapterBindingsListView.cs`、`IAdapterBindingHeaderSummaryProvider.cs`、`Editor/Inspector/GazeProviderEnumerator.cs`

他パッケージ（参考パターン）:
- `FacialControl/Packages/com.hidano.facialcontrol.osc/Editor/AdapterBindings/OscReceiverAdapterBindingDrawer.cs`（PropertyDrawer 例）
- `FacialControl/Packages/com.hidano.facialcontrol.inputsystem/Runtime/Adapters/AdapterBindings/InputSystemAdapterBinding.cs`（`IAdapterBindingDeclaredInputs` 実装例、Analog 直接参照）
- `FacialControl/Packages/com.hidano.facialcontrol.rec/Runtime/Adapters/Playback/RecAnalogInjector.cs`（注入 / 復元パターン）
- `FacialControl/Packages/com.hidano.facialcontrol.rec/Runtime/Domain/Services/RecBinaryFormat.cs`（ヘッダー flags / IdDefine の確認）
- `FacialControl/Packages/com.hidano.facialcontrol.rec/Tests/PlayMode/RecCharacterBindingPlayModeTests.cs`（FacialController e2e パターン）

外部参照（Unity Timeline 1.8 API）:
- https://docs.unity3d.com/Packages/com.unity.timeline@1.8/api/UnityEditor.Timeline.ClipEditor.html （`OnClipChanged(TimelineClip)`）
- https://docs.unity3d.com/Packages/com.unity.timeline@1.7/api/UnityEditor.Timeline.TrackEditor.html （`OnTrackChanged(TrackAsset)`）
- https://docs.unity3d.com/Packages/com.unity.timeline@1.8/changelog/CHANGELOG.html （1.8.2 で OnClipChanged の ExposedReference 変更時の発火修正）

---

## 11. Gate A 判定（オーケストレータ追記、2026-10-05）

ユーザー不在のため、親セッションの委任（「どんどん進めてくれ」）と HID-144 の確定方針に基づきオーケストレータが代行判定した。判断根拠は `.kiro/orchestration/timeline-playback-ux/log.md` の Gate A 節に記録。

- **A1〜A5 は要件修正で解消**（差し戻し 1 回目で requirements.md に反映）
  - A1: Facial トラックの Receiver binding は再生開始時（および Edit 時の診断）に Receiver が自動設定する AC を Req 1 に追加。手順 3 は「Director に TimelineAsset をセット（トラック binding は自動）」
  - A2: Req 10.3 は「Profile の binding が宣言する gaze source（`IGazeSourceProvider`）と `GazeSourceIdConvention`」に読み替え。`.fcrec` は触らない（Boundary 維持）
  - A3: Req 8.8 は「静的解析で実在確認済み。再現テストを先に書いて修正する」に書き換え
  - A4: Req 2.1 は「Inspector で編集可能な設定は Slug と有効/無効のみ」。旧フィールドは `[SerializeField, HideInInspector]` の legacy として残し 1 回警告（既存 `_legacyGazeConfigs` 方式）
  - A5: Req 3.1 は「`timeline:{layer}` の値 sink をレイヤー入力源へ自動接続し、`:state` sink は状態供給先（overlay suppress の active provider / REC 観測）へ登録する。`:state` をレイヤー入力源として接続するかは設計が判定し、接続する場合は Req 8.8 の修正を前提とする」に書き換え
- **B1（core 接続 API 新設）**: 依頼文の「FacialController のレイヤー接続に限定して触る」の範囲内と判定し採用。後付け系2 の `_layer2Provider` 反映も「Timeline の sink が overlay suppress に乗る」ために必要な最小限として許容（M-25 の系統統合そのものには踏み込まない）
- **B2（Analog 消費先）**: 「設計が判定する」のまま維持するが、制約を要件に明記: (i) 乗っ取り方式（registry Replace）を Gaze と統一して採用し、(ii) 直接参照を保持する既存消費者（`AnalogExpressionInputSource` / `AnalogBlendShapeInputSource`）が registry の差し替えを追えない場合は、core 側の消費者に registry 経由の再解決を追加するか、本 spec では「再生で反映されない Analog チャネル」として Receiver 診断に表示し backlog へ送るかを設計が判定する。(iii) `rec-weight-coverage` が並走中のため `InputSystemAdapterBinding` の weight 経路（`ApplyOverlayLayerWeights`）には触らない
- **B3（Edit/Play 一致）**: ユーザー決定 3「Edit プレビューと Play 再生で見える結果を一致させる」を直接実現する要件なので採用。ただし比較条件を「同一 TimelineAsset・Profile・時刻、Timeline の sink 以外の live 入力なし、レイヤー weight 既定」に限定し、合成は Domain 層の既存 `LayerBlender` を再利用して再実装しないことを要件に明記
- **C1〜C9** は設計判断として design へ送る。オーケストレータの方向性: C3 は「Runtime と同じ `LoadProfile()`（JSON 優先）に Editor 系を揃える」（JSON ファースト方針）、C2 は Track 側参照を優先候補、C4/C5 は `ChannelSubId` に REC の source id（`slug:sub`）を保持して binding 側で許容する方向を優先候補とする（最終決定は設計）
