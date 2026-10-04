# Gap Analysis: rec-full-input-coverage

- 実行日: 2026-10-04
- エンジン: **claude-subagent-fallback**（validate-gap-agent）。codex-first の書き込み監査ベースライン記録（呼び出し A）の Bash 実行が許可されなかったため、コマンド定義どおり codex を起動せずフォールバックした
- 対象 spec: `.kiro/specs/rec-full-input-coverage/`（language = ja、requirements 生成済み・未承認。未承認のまま分析を進めた）
- 分析手法: gap-analysis.md の枠組み（現状調査 → 要件実現性 → 実装アプローチ A/B/C → 工数・リスク）に従い、core / rec / 各拡張パッケージの Runtime ソースと先行 2 spec の design.md を読み合わせて作成。分析中にファイルは一切変更していない

---

## 1. 現状調査（Current State）

### 1.1 具象 `IInputSource` 実装の棚卸し（Runtime アセンブリ、テスト・Samples~ 除く）

`FacialControl/Packages/com.hidano.facialcontrol*/Runtime/**` を `class X : ... IInputSource|ValueProviderInputSourceBase|ExpressionTriggerInputSourceBase|TimelineAnalogInputSource` で走査した結果、**具象 15 型**（うち private nested 2 型）。抽象型は `ValueProviderInputSourceBase` / `ExpressionTriggerInputSourceBase` の 2 つ。

| # | 型 | パッケージ / asmdef | 系統 | registry 登録 | 現状の REC 到達 | 備考 |
|---|---|---|---|---|---|---|
| 1 | `LayerUseCase.LayerExpressionSource`（private nested） | core / `Hidano.FacialControl.Application`（`Runtime/Application/UseCases/LayerUseCase.cs:679`） | 系1 合成アダプタ（sourceIdx=0 予約枠） | なし（LayerUseCase 内部で直接 bind） | **未記録・未遮断** | `Id => "input"` が inputsystem の予約 id `input` と同名（後述の衝突リスク） |
| 2 | `AnalogBlendShapeInputSource` | core / Adapters | VP（IAnalog 直参照辞書から導出） | 呼び元次第 | 未記録 | **Runtime に構築箇所なし**（テストのみ。`new AnalogBlendShapeInputSource(` は Tests 配下のみ） |
| 3 | `AnalogExpressionInputSource` | core / Adapters（構築は inputsystem binding L586） | VP（`InputActionAnalogSource` を直参照） | `input:analog-expression` | 未記録・未遮断（VP） | 入力側 analog は wrapper 経由で registry 登録済み＝アナログとしては観測済み |
| 4 | `OverlayInputSource` | core / Adapters（構築は inputsystem binding L661 と core DI `PhonemeOverlayInputSourceRegistration.cs:44`） | VP（派生値: `IActiveExpressionProvider`=系2 の active から snapshot 解決、内部クロスフェード状態あり） | `input:overlay:{slot}` / phoneme slot 用 | 未記録 | Req 1.3 の判定対象。再生時に系2 注入から再導出可能。ただし **layer weight が live analog で駆動される**（後述 §2.5） |
| 5 | `OscInputSource` | osc（`Runtime/Adapters/InputSources/OscInputSource.cs`） | VP（OscDoubleBuffer pull、staleness で false） | `osc`（OscReceiverAdapterBinding L1044）/ ifacialmocap slug（IFacialMocapReceiverAdapterBinding L367） | **未記録・未遮断** | heartbeat 自動マッピング時に `_runtimeRegistry.Replace(_runtimeSlug, new OscInputSource(...))`（L2203）を**占有検査なしで**実行 |
| 6 | `GazeVector2InputSource` | osc | IInputSource + IAnalog（gaze 2 軸） | `slug:gaze…` | 観測済み（AnalogObservationSampler） | 既存対応 |
| 7 | `ExpressionTriggerInputSource` | inputsystem | Trigger | `input` | 観測・遮断・注入済み | 既存対応 |
| 8 | `InputSystemAdapterBinding.AnalogInputSourceWrapper`（private nested） | inputsystem（L784） | IInputSource + IAnalog（`InputActionAnalogSource` のラッパ） | `input:{action}` | 観測済み | 既存対応。ただし内側の `InputActionAnalogSource` は #3 と overlay weight 駆動（L291-318）から**直参照**もされる |
| 9 | `LipSyncPhonemeOverlayInputSource` | lipsync | VP（ULipSyncProvider 直参照、無音で false、Override 時に `ContributeMask` が切替わる） | `lipsync-overlay:{slot}`（ULipSyncAdapterBinding L1057） | **未記録・未遮断** | Req 2.3 の mask 変化の実例 |
| 10 | `AnalogAxesInputSource` | ifacialmocap | IInputSource + IAnalog（頭部 N 軸、BlendShapeCount 0） | `slug:head` | 観測済み | 既存対応 |
| 11 | `TimelineBakedValueSink` | timeline | VP（`SetValues` push、`Invalidate` で false） | `slug:{layer}`（TimelineAdapterBinding L91） | **未記録・未遮断** | |
| 12 | `TimelineExpressionStateSink` | timeline | Trigger（blendShapeCount 0） | `slug:{layer}-state`（L92） | 観測・遮断済み | 既存対応（遮断は既知制限として文書化済み） |
| 13 | `TimelineAnalogInputSource`（非 sealed） | timeline | IInputSource + IAnalog | `slug:{sub}`（L141） | 観測済み | |
| 14 | `TimelineGazeInputSource`（#13 派生、`IInjectedInputSource`） | timeline | IInputSource + IAnalog + Injected | `FacialTimelineReceiver` が占有検査付き `Replace`（L298-314）で装着 | 注入ソース | 他者占有の実例。REC と排他 |
| 15 | `RecPlaybackAnalogSource` | rec | IInputSource + IAnalog + Injected | REC 再生時に Replace/Register | 再生注入用 | 明示的除外の候補 |

**IInputSource を実装しない `IAnalogInputSource` 単独実装**（Req 7.1 の列挙対象外になる）:
- `InputActionAnalogSource`（inputsystem）: #8 ラッパ経由で registry へ、かつ #3 / overlay layer weight 駆動から直参照。
- `ArKitOscAnalogSource`（osc）: `ArKitOscAdapterBinding.AnalogSource` プロパティで公開されるだけで、**registry 登録も Runtime 消費者も見つからない**。
- `OscFloatAnalogSource`（osc）: Runtime に構築箇所なし（テストのみ）。
- `AnalogBonePoseProvider`（core Bone）は IAnalog 直参照辞書を持つが Runtime に構築箇所なし。

**Editor アセンブリの実装**: `com.hidano.facialcontrol.timeline/Editor/BakeSimulationHarness.cs:321` に `OfflineExpressionSource : ExpressionTriggerInputSourceBase`。Req 7.1 は「Runtime アセンブリ」限定だが、Boundary Context は「Editor 専用パッケージも列挙から除外しない」と書いており、timeline Editor asmdef を含めるとこの型が分類対象に入る（§3 の矛盾 2）。

### 1.2 既存の観測面・遮断面・注入面（再利用資産）

| 資産 | 場所 | 状態 | 本 spec への適合 |
|---|---|---|---|
| `IFacialInputObservationBus` / `IFacialInputObserver`（OnTriggerOn/Off, OnAnalogSample） | core `Runtime/Domain/Adapters/` | 実装済み・per-FC・HasObservers 早期 return | VP サンプル・系1 イベント用のメソッド追加が必要（インターフェース拡張 = rec 側 `RecordingUseCase` と Fake の追随） |
| `AnalogObservationSampler` | core `Runtime/Adapters/InputSources/AnalogObservationSampler.cs` | registry 全 IAnalog を毎フレーム pull、float ビット比較で変化時のみ publish | VP 版サンプラーの設計テンプレート。ただし VP は「消費値」を Aggregator 内で得る方が正確（§2.2） |
| **`ILayerSourceValueObserver` + `LayerInputSourceAggregator.SetSourceValueObserver`** | core `Runtime/Domain/Adapters/ILayerSourceValueObserver.cs`, `LayerInputSourceAggregator.cs:286,332` | **既に存在**。Aggregate ループで各 (layer, source) の `TryWriteValues` 直後に `isValid` と pre-weight 値 span を同期通知（null 時 0 コスト） | Req 2.1/2.2/8.7 の「消費値・有効性」観測点そのもの。唯一の利用者は timeline Editor `BakeSimulationHarness`（L79）。欠けているのは (a) `ContributeMask` の受け渡し（Req 2.3）、(b) LayerUseCase が `_aggregator` を private 保持しており FacialController から observer を差し込む経路がない、(c) 単一 observer スロット（多重配信はバス側で） |
| `ExpressionTriggerInputSourceBase` の Suspend/Resume/Inject/ResetToExpressionStack | core Domain | 実装済み | 系1 ゲート・VP ゲートの設計テンプレート（bool 1 個 + Core 切り出し） |
| `IInjectedInputSource` + 占有規則 | core Domain | 実装済み。`RecAnalogInjector` / `FacialTimelineReceiver` が遵守 | VP の Replace 注入にそのまま適用可。**`OscReceiverAdapterBinding` の heartbeat `Replace` は遵守していない**（§2.3） |
| Replace / Unregister 再バインド伝搬 | `FacialController.SubscribeDeclaredLayerInputSources` → `HandleLayerInputSourceRebound` → `LayerUseCase.Bind/UnbindLateInputSource` | 実装済み。profile 宣言 id は解決成否に関わらず全 Subscribe | VP ソースの Replace 注入は**そのまま到達**する（レイヤー経路は registry 経由） |
| `RecAnalogInjector` / `RecTriggerInjector` / `PlaybackUseCase`（trigger→analog の Begin/End 順序） | rec | 実装済み | VP ポート・系1 ポートを同型で追加し、順序を 4 種へ拡張 |
| `RecBinaryFormat`（formatVersion=1、kind 1〜6/255） | rec Domain | version 不一致は `TryRead` が error（Req 6.5 の挙動は既に実装） | 新 kind 追加 → `CurrentFormatVersion = 2` |
| `RecEvent`（`byte AxisCount`）/ `RecordingUseCase.OnAnalogSample`（>255 を警告スキップ）/ `RecBinaryFormat.GetMaxRecordSize`（maxAxisCount ≤ 255） | rec | AnalogSample は u8 固定 | VP 用に別レコード（u16 count）が必要（Req 6.3） |
| `RecEventChunkQueue` | rec Domain | `axisFloatCapacityPerSegment` 既定 **128**（`RecStreamWriter` 既定）。`Enqueue` は `CanWrite` を 1 回だけ判定して次セグメントへ移った後**再判定なしで Write** | 1 サンプルが 128 float を超えると新セグメントでも収まらず `Span.CopyTo` が例外。**容量を BlendShape 総数で決める必要**（Req 6.7） |
| `RecTimelineSeek.BuildBaselineAt` | rec Domain | 途中再生用にイベントを baseline へ畳む | 新 kind の畳み込みが必要（**requirements に記述なし**、§3 ギャップ 5） |
| `RecValidation.FindMissingExpressionIds` | rec Domain | Trigger の expressionId を走査 | 系1 イベント / 基準にも拡張（Req 4.10） |
| `TestAssemblyCatalog`（`AppDomain.GetAssemblies` + `GetLoadableTypes`） / `TestSizeDeclarationTests`（Small、reflection 走査） | core `Tests/Testing/`, `Tests/Small/Testing/` | reflection ベース網羅テストの前例 | Req 7 のゲートテストはこの型をほぼ流用できる。Small 禁止 API 一覧（`scripts/check-test-sizes.ps1` L100-112）に reflection は含まれず Small 可 |
| rec Tests EditMode asmdef | `com.hidano.facialcontrol.rec/Tests/EditMode/*.asmdef` | 参照は core + rec のみ | `AppDomain` 走査ならコンパイル参照不要（Editor ドメインには全 Runtime asmdef がロード済み）。`typeof` で固定したいなら Osc/InputSystem/LipSync/IFacialMocap/Timeline を参照追加（Req 7.9 で許容） |
| CI | `.github/workflows/ci.yml` | Small は全 push、Medium は PR/main。lipsync は同一プロジェクト内で assembly-names フィルタのみ | ゲートテストを Small にすれば Req 7.10 を満たす。lipsync asmdef は `com.hidano.ulipsync-asio` が manifest にあるため常にコンパイルされる |

### 1.3 系1 経路の現状

- `FacialController.Activate/Deactivate`（L1096-1120）→ `ExpressionUseCase.Activate/Deactivate`（Application、プレーンクラス）。観測フック・ゲートともになし。`_activeByLayer: Dictionary<string, List<Expression>>` を LastWins/Blend で更新。
- 消費: `LayerUseCase.UpdateWeights`（L136）が `CollectActiveExpressions` → `GroupByLayer` → `LayerExpressionSource.UpdateExpressions`。**`UpdateExpressions` は変化検出のたびに必ず遷移を開始**する（L724-745）。Req 4.7 / 5.4 の「遷移を経ない定常状態」確立 API は存在しない。
- 基準状態の取得: `GetActiveExpressions()`（alloc）/ `CollectActiveExpressions(buffer)`（レイヤー順序情報が失われる）。レイヤー別の順序付きリストを公開する API はない（`TryGetTopActiveExpression` は top のみ）。
- 識別子: `LayerExpressionSource.Id` は `"input"`。Aggregator snapshot / `ILayerSourceValueObserver` の `sourceId` にもこの名が流れる。系1 の記録用 id を `"input"` にすると `TryGetExpressionTriggerSourceById("input")` が inputsystem の sink を返すなど衝突する。
- `LayerUseCase._layerSuppressed`（layerOverrideMask）は系2 のみから計算（L148-150）。系1 注入でも挙動は不変（既存の M-25 の暫定状態）。

### 1.4 その他の統合点・規約

- `FacialController.LateUpdate`: `_analogObservationSampler.Sample()` → `UpdateWeights`（Aggregate）→ 出力。VP 観測を Aggregator 内フックで行う場合、通知はアナログより後のフレーム内タイミングになる（同一フレームなので時系列上の問題はない）。
- `RecCharacterBinding.CaptureBaseline`（L614）は registry を走査し `ActiveExpressionIds` / `TryReadAxes` を直接呼ぶ。VP の基準値を同じ方式で取ると `TryWriteValues` をフレーム外で呼ぶことになり、`OscInputSource`（`_lastObservedTick` 更新）・`LipSyncPhonemeOverlayInputSource`（`_currentContributeMask` 更新、`TryComposePhonemeWeights` の再計算）・`OverlayInputSource`（`RefreshResolution`）に副作用がある。
- `rec` の Documentation~/README.md は「記録される内容」「再生中の遮断仕様」「既知制限」節を持つ（Req 10.4-10.6 の追記先）。
- timeline パッケージの REC Export（Editor、rec Editor 依存）は `.fcrec` を読む。formatVersion 2 / 新 kind に追随しないと Export が読めなくなる（spec の Out of scope に近いが**コンパイル／動作影響**として要認識）。

---

## 2. 要件実現性分析（Requirement → Asset Map）

タグ: **Missing**（存在しない）/ **Unknown**（調査が必要）/ **Constraint**（既存構造の制約）

### 2.1 Req 1 / 7: 網羅分類とゲートテスト

| 項目 | 既存資産 | ギャップ |
|---|---|---|
| 具象 `IInputSource` 列挙 | `TestAssemblyCatalog.GetLoadableTypes`（Testing asmdef、noEngineReferences） | Missing: Runtime アセンブリの選別規則（`Hidano.FacialControl*` かつ `.Tests` / `.Testing` を含まない。Editor asmdef を含めるか = §3 矛盾 2）。private nested 型（#1, #8）は `GetTypes()` で拾えるが `typeof` できないため、分類一覧は **型 FullName 文字列**で持つ必要がある |
| 分類一覧の置き場 | なし | Missing。選択肢: (a) rec Tests 内の static テーブル（最小。ただし「設計成果物」としては tests 配下に埋もれる）、(b) rec Runtime に文字列ベースの `RecInputSourceCoverage` カタログ（README 生成にも流用可。rec Runtime は拡張を参照できないため文字列必須）、(c) 各実装に属性付与 → core/拡張の改修になり Req 8.4 と衝突 |
| 7.2 テストアセンブリ除外 | `IsProjectTestAssemblyName` | 流用可 |
| 7.4-7.6 二重分類・陳腐化・空理由の検出 | なし | Missing（テーブル検証ロジック。小） |
| 7.8 Small 配置 | 禁止 API に reflection なし | 可。ただし `UNITY_INCLUDE_TESTS` 下で全 Runtime asmdef がロード済みであることが前提（Editor ドメインでは成立） |
| IAnalog 単独実装の抜け | — | Constraint: Req 7.1 は `IInputSource` のみ。`InputActionAnalogSource` のような**直参照で合成に効く IAnalog 単独実装**は列挙されない。要件拡張の要否をユーザー判断へ |

### 2.2 Req 2: VP の観測

| 項目 | 既存資産 | ギャップ |
|---|---|---|
| 2.1 消費値の観測 | `ILayerSourceValueObserver`（pre-weight 値 + isValid、消費点そのもの） | **ほぼ存在**。Missing: LayerUseCase → FacialController へ observer を差し込む公開経路、VP 型判定（`source is ValueProviderInputSourceBase`。#1 系1 と Trigger 型は除外）、バスへの新メソッド `PublishValueProviderSample(...)` |
| 2.2 有効性変化 | `isValid` 引数あり | 変化検出（前フレーム比較）は Missing |
| 2.3 ContributeMask 変化 | observer に mask 引数なし。Aggregator は `TryWriteValues` 後に `source.ContributeMask` を読む | Missing: interface に `BitArray contributeMask`（または `IInputSource source`）を追加。利用者は timeline Editor `BakeSimulationHarness` と core Small テストのみ（改修コスト小）。mask の変化検出は BitArray ビット比較（長さ = BlendShapeCount） |
| 2.4 派生クラス無改修 | Aggregator 側フックは派生を触らない | 可 |
| 2.5/2.6 変化時のみ記録 | `AnalogObservationSampler.AreBitsEqual` のパターン | Missing: (layer, source) スロット単位の前回値バッファ（BlendShapeCount × VP 数）。再走査時のみ alloc |
| 2.8 float ビット一致 | 同上 | 可 |
| 代替案 B（registry 走査型 VP サンプラー） | `AnalogObservationSampler` 同型 | Constraint: `TryWriteValues` を毎フレーム二重評価（lipsync の compose は CPU 2 倍、Osc の staleness 判定に副作用）、Aggregator の `Tick` 順序と乖離し得るため「消費値」の保証が弱い |

### 2.3 Req 3: VP の遮断・注入

| 項目 | 既存資産 | ギャップ |
|---|---|---|
| 3.1 開始時スナップショットで全 VP 遮断 | `RecAnalogInjector.BeginInjection` の registry 走査 | VP 版の `RecValueProviderInjector` + `RecPlaybackValueProviderSource : ValueProviderInputSourceBase, IInjectedInputSource`（値バッファ・valid フラグ・**可変 ContributeMask**（事前確保 BitArray を事件ごとに書換）） Missing |
| 3.2 ライブ値非反映（registry 経路） | Replace → `HandleLayerInputSourceRebound` → `BindLateInputSource` | 可（宣言 id は全 Subscribe 済み） |
| 3.3 直参照経路の遮断面 | なし | Missing / Unknown。実例: (a) **OSC heartbeat の `Replace`**（osc L2203）が注入中の `osc` エントリを占有検査なしで上書き → 再生中にライブ OSC が復活し、`EndInjection` の参照同一性ガードで復元が no-op になる。対処は osc binding の改修（Req 8.4 の例外文書化）か、registry 側で「`IInjectedInputSource` エントリへの非注入者 Replace を拒否」する core 変更（Req 8.1 の「既存挙動不変」と緊張）。(b) `ApplyOverlayLayerWeights`（inputsystem L291-318）は `InputActionAnalogSource` 直参照で `FacialController.SetLayerWeight` を叩く → **レイヤー weight 経路は観測も遮断もされない**（§2.5）。(c) `AnalogExpressionInputSource` の `InputActionAnalogSource` 直参照は、自身が VP として Replace されれば無害化される |
| 3.4 同一コードパスへ供給 | Aggregator が注入ソースを通常ソースとして消費 | 可 |
| 3.5 ベースライン外の初期状態 | analog は 0 埋め seed | 設計判断: VP は「無効（TryWriteValues=false）」を初期状態にする選択肢がある（0 埋め有効だと ContributeMask ぶん下位レイヤーを 0 で上書きする）。要件は固定していない |
| 3.6/3.7 StopPlayback 唯一の解放点・一貫順序 | `PlaybackUseCase` の trigger→analog | 4 種（trigger→analog→VP→系1、逆順解放）へ拡張。Missing |
| 3.8 占有規則 | `IInjectedInputSource` | 流用可。timeline の `FacialTimelineReceiver` が同時に VP 系 sink（`TimelineBakedValueSink`）を registry へ Register しているが、こちらは Replace でなく Register のため衝突しない |
| 代替案（core ゲート） | `ExpressionTriggerInputSourceBase` の Suspend パターン | Constraint: `ValueProviderInputSourceBase.TryWriteValues` が `abstract` で派生が直接 override しているため、基底にゲートを入れるには (i) abstract を `TryWriteValuesCore` へ改名 = **拡張 6 型 + テスト Fake 全改修**（Req 8.4 違反）、または (ii) 基底に Suspend 状態と注入バッファだけ持たせ **Aggregator 側**で `vp.IsSuspended` を見て注入値を使う（派生無改修・直参照消費者にも効くが、Aggregator に VP 固有分岐が入る） |

### 2.4 Req 4: 系1 の観測・遮断・注入

| 項目 | 既存資産 | ギャップ |
|---|---|---|
| 4.1/4.2 観測面と識別子 | なし | Missing。配置候補: (a) `ExpressionUseCase`（Application、`IActiveExpressionProvider` 実装。observer interface を Domain に置く）、(b) `FacialController.Activate/Deactivate`（Adapters）。(a) は `ExpressionUseCase` を直接使うテスト／将来利用者も捕捉。識別子は `"input"` 以外の予約語が必要（例: `expression-usecase`） |
| 4.4/4.5 遮断面 | Suspend パターン | Missing（bool 1 個 + null 検証先行） |
| 4.6 注入経路 | `Activate` 本体 | Missing: `InjectActivate/InjectDeactivate`（ゲート迂回・観測者通知） |
| 4.7 基準状態の遷移なし確立 | `ResetToExpressionStack`（系2 のみ） | **Missing**: `ExpressionUseCase.ResetActiveExpressions(...)` に加え、`LayerExpressionSource.UpdateExpressions` が常に遷移を開始するため `LayerUseCase` 側に「次回 UpdateWeights で snap」する API/フラグが必要。`HasBeenActive` との整合も要検討 |
| 4.9 停止後の維持 | 系2 と同構造 | 可 |
| 4.10 欠落 expressionId | `RecValidation` / `PlaybackUseCase.IsMissingExpressionId` | 系1 イベント種別へ拡張 |
| 基準捕捉 | `CollectActiveExpressions`（順序なし） | Missing: レイヤー別順序付き列挙 API |

### 2.5 要件外だが「全入力例外なし」方針に抵触し得る経路（Unknown → ユーザー判断）

1. **レイヤー weight / 入力源 weight のランタイム変更**: `FacialController.SetLayerWeight`（inputsystem の overlay binding が毎 LateTick で live analog から駆動）、`LayerUseCase.SetInputSourceWeight`。ブレンド結果に直接効くが、観測面も遮断面もなく、requirements にも記述がない。`OverlayInputSource` を「派生値として除外」しても、この weight 経路が live のまま残ると Req 3.3 の完全再現が崩れる。
2. `SetActiveBoneSnapshots` / `AnalogBonePoseProvider`（ボーン経路）: ブレンド出力外。既存 spec で Out of Boundary。
3. `RecTimelineSeek.BuildBaselineAt`（途中再生）: 新 kind の畳み込み規則（VP は最後の値/有効性/mask、系1 は最終 active 集合）が未定義。
4. timeline Editor の REC Export が formatVersion 2 を読めるか。

### 2.6 Req 5 / 6 / 8 / 9

| 項目 | ギャップ |
|---|---|
| 5.1 VP 基準値の捕捉タイミング | Unknown（研究項目）。(i) `TryWriteValues` をフレーム外で呼ぶ（副作用あり、§1.4）、(ii) 記録開始後の最初の Aggregate で観測した値を基準にする（`RecStreamWriter.Open(baseline)` が基準を即書くため、Open の遅延か「基準レコードは後から書くが時刻付きイベントより前に出現」を保証するバッファリングが必要）、(iii) core が常時 last-frame キャッシュを持つ（観測者ゼロ時コスト = Req 8.2 違反） |
| 5.2 系1 基準 | §2.4 の順序付き列挙 API |
| 6.1/6.2 新 kind のラウンドトリップ | Missing: `RecEventKind` 追加（例: ValueSample / ValueInvalidate / MaskChange / ExpressionActivate / ExpressionDeactivate / BaselineValue / BaselineExpression）、`RecEvent` の拡張（u16 count。現 struct は `byte AxisCount`）、`RecTimeline` の axes 格納（既存 `analogAxesByEvent` を流用可）、`RecBinaryFormat` 読み書き、`RecFileReader`、`RecPlaybackScheduler.Dispatch`（未知 kind は throw） |
| 6.3 255 超 | u16 count の新レコード。`RecEvent.AxisCount: byte` との二重表現を避けるため struct 設計を要検討 |
| 6.4 formatVersion | 新 kind 追加 → 2 へ。読込側の version チェックは既存 |
| 6.6 記録サイズ | 設計判断: dense（BlendShapeCount 全値）vs sparse（ContributeMask の立った index のみ `u16 idx + f32`）。OSC は mask = マッピング済み index（52 程度）に対し BlendShapeCount は mesh 総数（数百）になり得るため sparse の効果が大きい。ただし `OscInputSource` は mask 外 index にも 0 を書き得る（`TryWriteValues` は mapping index 全てを書く。mask と一致） |
| 6.7 GC ゼロ | Constraint: `RecEventChunkQueue` の axis 容量を**セッション開始時に最大 VP BlendShapeCount × 余裕**で確保する必要（既定 128 では破綻）。`RecStreamWriter` の `buffer` 拡張は writer スレッド側で許容 |
| 8.2 未使用時ゼロコスト | Aggregator フックは `sourceValueObserver?.` 1 回（既存）。系1 は bool 分岐 1 個。Replace 系は未使用時コストなし |
| 8.4 拡張無改修 | §2.3 (a) の OSC heartbeat Replace は原則の例外候補。lipsync / ifacialmocap / timeline は無改修で成立見込み |
| 8.6 10 体スケール | per-FC バス・per-FC Aggregator のため線形。前回値バッファは FC ごとに VP 数 × BlendShapeCount float |
| 9.7 EditMode 検証 | `ExpressionUseCase` / `LayerUseCase` / `LayerInputSourceAggregator` / 各 Injector はプレーンクラス → Fake で EditMode 可。FacialController 配線とブレンド完全再現（`BlendedOutputSpan`）は PlayMode（既存 `RecCharacterBindingPlayModeTests` [MediumTest] に追記） |
| 9.8 既存 GC ゲート | `FacialControllerGcZeroGateTests`（Medium/PlayMode）、`RecGcZeroGateTests`（Medium）。大きな値ベクトルを含む記録中の GC ゼロ検証を追加 |

---

## 3. 要件と既存アーキテクチャの矛盾・設計を阻害し得る点

1. **Req 8.1（既存コードパス不変）× Req 3.3（直参照経路の遮断）× Req 8.4（拡張無改修）**: OSC heartbeat の `Replace`（osc binding）は占有規則を守らない。解決は「osc binding を改修（8.4 の例外を文書化）」か「registry が非注入者の上書きを拒否（8.1 と緊張。既存の正常系では注入エントリが存在しないため実害は小さいが『挙動不変』の文言には抵触）」の二者。設計で決める必要。
2. **Boundary Context（Editor 専用パッケージも列挙から除外しない）× Req 7.1（Runtime アセンブリから列挙）**: timeline **Editor** asmdef の `OfflineExpressionSource` が該当。Editor を含めるなら分類（ベイクシミュレーション用 → 除外）が必要、含めないなら Boundary の文言を修正。
3. **Req 1.1 の「具象 IInputSource」に private nested 型が含まれる**（`LayerExpressionSource`, `AnalogInputSourceWrapper`）: 分類一覧を `typeof` で書けない。文字列 FullName 方式（Nested は `Outer+Inner`）が前提になる。
4. **`LayerExpressionSource.Id == "input"`**: 系1 の識別子（Req 4.2）に流用できない。`ILayerSourceValueObserver` 経由で `"input"` の値が 2 系統（系1 の LayerExpressionSource と inputsystem の sink）から届くため、VP 観測は id ではなく**型判定**で VP を選別する必要がある。
5. **要件に無い入力**: レイヤー weight / 入力源 weight のランタイム変更（§2.5-1）は「FacialControl で動く入力は例外なく」の方針に照らすと記録・遮断対象候補だが、requirements は Activate/Deactivate しか系1 として扱っていない。`OverlayInputSource` の除外判断と直結するため、設計前にスコープ確認が必要。
6. **途中再生（`RecTimelineSeek`）と timeline REC Export** への影響が requirements に記載なし。新 kind を畳み込まない／読めないと既存機能が黙って劣化する。
7. **Req 5.1（開始時点の消費値を基準に）**: 「消費値」はフレーム内の Aggregate でしか得られず、`StartRecording` 時点では直前フレームの値が core に保持されていない。基準の定義（直前フレームの観測値 / 開始後最初の観測値 / フレーム外の再評価値）を設計で決める必要。
8. **Req 4.7（遷移を経ない定常状態）**: 系1 の消費側 `LayerExpressionSource` は snap API を持たないため core Application の改修が必須（Req 8.1 の「面の追加に限定」の範囲内と解釈可能だが、`UpdateExpressions` のロジック変更にならないよう別経路で追加する必要）。

---

## 4. 実装アプローチ（Options）

### Option A: 既存コンポーネント拡張（最小新規）
- **VP 観測**: `LayerInputSourceAggregator` の既存 `ILayerSourceValueObserver` に `BitArray contributeMask`（または `IInputSource source`）を追加し、`LayerUseCase` に `SetSourceValueObserver` の委譲メソッドを追加。`FacialController` が observer 実装（変化検出 + VP 型判定）を持ち `IFacialInputObservationBus.PublishValueProviderSample(...)` を呼ぶ。
- **VP 遮断・注入**: `RecAnalogInjector` と同型の `RecValueProviderInjector`（registry Replace + `IInjectedInputSource`）。直参照経路（OSC heartbeat）は osc binding に占有検査を 1 箇所追加（8.4 の例外）。
- **系1**: `ExpressionUseCase` に observer / Suspend / Inject / Reset を追加（trigger 基底と同型）、`LayerUseCase` に snap API を追加。
- **rec**: `RecEventKind` / `RecBinaryFormat` / `RecBaselineState` / `RecTimeline` / `PlaybackUseCase` / `RecCharacterBinding.CaptureBaseline` / `RecTimelineSeek` を拡張。formatVersion 2。
- ✅ 既存パターン（観測面後付け・Replace 注入・Suspend ゲート）の延長で把握しやすい。Aggregator フックが既にあるため VP 観測の core 変更が小さい。
- ❌ `IFacialInputObserver` / `ILayerSourceValueObserver` / `IRecEventVisitor` / `RecEvent` の破壊的変更が同時に走る（preview 段階で許容）。`RecBinaryFormat.cs`（800 行）と `FacialController.cs`（1300 行超）がさらに肥大。OSC binding 改修が原則違反になる。

### Option B: 新規コンポーネント中心（責務分離）
- **core**: `ValueProviderObservationSampler`（registry 走査、`AnalogObservationSampler` 同型）と `ExpressionStateGate`（系1 ゲート・観測を `ExpressionUseCase` の外側ラッパとして新設し `FacialController` が経由）を新設。Aggregator は無改修。
- **VP 遮断**: core に `ValueProviderInputSourceBase` の Suspend 状態 + 注入バッファを追加し、**Aggregator 側**で `IsSuspended` を見て注入値を消費（Replace を使わない）。直参照消費者にも効き、OSC heartbeat の Replace が来ても suspended 状態は新インスタンスに引き継がれない点だけが課題（新インスタンスは未遮断 → スナップショット外として既知制限扱い）。
- **rec**: `RecValueProviderInjector` / `RecExpressionStateInjector` / 新ポート 2 本 / `RecBinaryFormatV2`（kind 別 reader/writer をファイル分割）。
- ✅ 既存 Aggregator・RecBinaryFormat の肥大を避け、単体テストが独立。拡張パッケージ完全無改修で Req 8.4 を満たす見込み。
- ❌ registry 走査型 VP サンプラーは `TryWriteValues` 二重評価（lipsync compose の CPU、Osc staleness 副作用）で「消費値」保証が弱い（Req 8.7 と緊張）。Aggregator に VP 固有分岐が入る（Domain の汎用性低下）。ファイル数増。

### Option C: ハイブリッド（段階実装）
- **フェーズ 1（観測）**: Option A の Aggregator フック拡張で VP 消費値・有効性・mask を観測（唯一の消費点で二重評価なし）。系1 は `ExpressionUseCase` 直接拡張。
- **フェーズ 2（遮断・注入）**: VP は Replace 注入（既存機構）を主経路にしつつ、直参照経路向けの保険として `ValueProviderInputSourceBase` に Suspend 状態のみ追加し Aggregator で尊重する（二段防御）。OSC heartbeat は registry 側の占有尊重で止めるか osc 改修かを 8.1/8.4 のどちらを優先するかで決める。
- **フェーズ 3（永続化・ゲート）**: formatVersion 2 を新 kind 群と sparse 値レコードで導入。網羅性ゲートテストは `TestAssemblyCatalog` 流用の Small テスト。分類一覧は文字列 FullName テーブル。
- ✅ 観測は正確性優先、注入は到達性優先で分けられる。各フェーズでテスト緑を維持しやすい。
- ❌ 二段防御は「どちらが効いたか」の診断が複雑。計画・文書量が最大。

---

## 5. 工数・リスク

| 領域 | 工数 | リスク | 根拠 |
|---|---|---|---|
| 網羅分類 + ゲートテスト（Req 1, 7） | S | Low | `TestAssemblyCatalog` 流用。テーブル検証ロジックは小 |
| VP 観測面（Req 2） | M | Medium | Aggregator フックは既存だが interface 変更・前回値バッファ・型判定・基準捕捉タイミングの決定が必要 |
| VP 遮断・注入（Req 3） | L | **High** | 直参照経路（OSC heartbeat / overlay weight）の扱い、可変 mask の注入、初期状態の意味論、8.1/8.4 のどちらを曲げるかの判断 |
| 系1 観測・遮断・注入・基準（Req 4, 5.2） | M | Medium | `ExpressionUseCase` はプレーンクラスで素直だが、`LayerExpressionSource` の snap API と id 衝突回避が必要 |
| `.fcrec` v2 + ラウンドトリップ + 途中再生畳み込み（Req 6） | L | Medium | 新 kind 5〜7 種、u16 count、sparse 判断、`RecTimelineSeek` 拡張、timeline REC Export への影響確認 |
| GC ゼロ・10 体スケール（Req 6.7, 8.5, 8.6） | M | Medium | `RecEventChunkQueue` 容量設計、大ベクトルの前回値バッファ。計測は既存ゲートの拡張 |
| 文書整合（Req 10） | S | Low | 既存 design.md 2 本 + rec README/Documentation~ |
| **合計** | **XL** | **High** | 2 つの新しい入力カテゴリを観測・遮断・注入・永続化の全層に通し、既存 interface を複数同時に破壊的変更する |

---

## 6. 設計フェーズへの引き継ぎ

### 6.1 設計前にユーザー確認が必要な判断（要件の穴・矛盾）
1. レイヤー weight / 入力源 weight のランタイム変更（overlay binding の `SetLayerWeight` 駆動を含む）を本 spec の対象にするか。これが `OverlayInputSource` の分類結論を左右する。
2. 直参照経路の遮断で優先する制約: Req 8.1（registry 挙動不変）か Req 8.4（osc 無改修）か。
3. Editor asmdef（timeline の `OfflineExpressionSource`）を網羅列挙に含めるか（Boundary と Req 7.1 の整合）。
4. `IAnalogInputSource` 単独実装（`InputActionAnalogSource` 等）を網羅ゲートの列挙対象に加えるか。
5. 途中再生（`StartPlayback(offset)`）と timeline REC Export の新 kind 対応を本 spec に含めるか。

### 6.2 Research Needed
- VP 基準値の捕捉方式（フレーム外 `TryWriteValues` の副作用の実測 / 開始後初フレーム方式での `RecStreamWriter.Open` 遅延の設計 / 最初の時刻付きイベントより前に基準を書く不変条件の維持）。
- ベースライン外 VP の初期状態（0 埋め有効 vs 無効）。無効が妥当なら analog の 0 埋め決定との整合説明。
- sparse 値レコードのサイズ試算（OSC: mask 52 vs BlendShapeCount 数百、60fps、10 体）と `RecEventChunkQueue` の容量方針。
- `ValueProviderInputSourceBase` に Suspend 状態を持たせた場合の `OverlayInputSource.Tick`（クロスフェード進行）との相互作用。
- `OscReceiverAdapterBinding` 以外の binding に、ランタイム `Replace` / 再 `Register` を行う箇所がないか（ifacialmocap の heartbeat 相当、lipsync のデバイス再選択）。
- `RecIdTable` の source id 空間に系1 予約 id を入れる際の `TryGetExpressionTriggerSourceById` との衝突回避（id 名の予約規約）。

### 6.3 設計で確定すべき主要事項
- VP 観測点（Aggregator フック vs registry 走査）と `ILayerSourceValueObserver` の拡張形。
- VP 遮断の主経路（Replace / 基底 Suspend + Aggregator 尊重 / 二段）。
- 系1 の観測・ゲート配置（`ExpressionUseCase` vs `FacialController`）、予約 id、`LayerExpressionSource` の snap API。
- `.fcrec` v2 のレコード設計（kind 一覧、u16 count、sparse index、flags）と `RecEvent` struct の拡張形。
- 分類一覧の所在（rec Tests / rec Runtime 文字列カタログ）と README 連携。
- `PlaybackUseCase` の 4 種ポート確立・解放順序。

---

## Summary

- **最大の発見**: VP の「消費値 + 有効性」を観測する点は `LayerInputSourceAggregator.SetSourceValueObserver` / `ILayerSourceValueObserver` として**既に core に存在**する（timeline Editor のベイクが利用中）。欠けているのは ContributeMask の受け渡し、LayerUseCase → FacialController への配線、VP 型判定と変化検出、バスの新メソッドのみ。Req 2 の core 側コストは小さい。
- **直参照経路が 2 本実在**し、どちらも requirements の想定より厄介: (1) `OscReceiverAdapterBinding` の heartbeat `Replace` が `IInjectedInputSource` 占有規則を無視して注入ソースを上書きする（再生中に live OSC が復活し復元ガードも no-op 化）。(2) inputsystem の overlay binding が `InputActionAnalogSource` 直参照で `FacialController.SetLayerWeight` を毎フレーム駆動しており、**レイヤー weight は観測も遮断もされない**（requirements に記述なし。`OverlayInputSource` の除外判断に直結）。
- **系1 には ResetToExpressionStack 相当がない**: `LayerExpressionSource.UpdateExpressions` は常に遷移を開始するため Req 4.7 の「遷移を経ない定常状態」には core Application の新 API が必須。さらに `LayerExpressionSource.Id == "input"` が inputsystem 予約 id と同名で、系1 識別子にも VP 選別にも id は使えない（型判定が必要）。
- **`.fcrec` は formatVersion 2 が必要**（新 kind 追加）。読込側の version 拒否は既に実装済み。`RecEventChunkQueue` の axis 容量既定 128 と `Enqueue` の単回 `CanWrite` 判定は 255 超の値ベクトルで破綻するため容量設計が必須。`RecTimelineSeek`（途中再生）と timeline REC Export の新 kind 追随は requirements に無く、設計前にスコープ確認が必要。
- **網羅ゲートテストは `TestAssemblyCatalog` 流用で Small 配置可能**。具象 IInputSource は Runtime で 15 型（private nested 2 型を含むため分類一覧は FullName 文字列方式）。Editor asmdef の `OfflineExpressionSource` と `IAnalogInputSource` 単独実装の扱いが Boundary / Req 7.1 と食い違う。

**設計を阻む矛盾（要ユーザー判断）**: Req 8.1（registry 挙動不変）vs Req 8.4（osc 無改修）のどちらで OSC heartbeat Replace を止めるか / レイヤー weight 経路を対象に含めるか / Editor asmdef・IAnalog 単独実装を列挙に含めるか / 途中再生・REC Export の新 kind 対応を含めるか。

**実装戦略の選択肢**: A（既存拡張: Aggregator フック + Replace 注入 + ExpressionUseCase 直接拡張 + osc 1 箇所改修。最小新規だが interface の同時破壊的変更と osc 改修が原則違反）/ B（新規中心: registry 走査型 VP サンプラー + 基底 Suspend を Aggregator が尊重 + 系1 ラッパ。拡張無改修だが消費値の二重評価で正確性が弱い）/ C（ハイブリッド: 観測は Aggregator フック、遮断は Replace 主経路 + 基底 Suspend 保険、段階実装）。全体工数 XL・リスク High。

---

# 設計フェーズ research: rec-full-input-coverage

- 実行日: 2026-10-04
- Discovery Scope: **Extension**（brownfield。`.kiro/settings/rules/design-discovery-light.md` を適用。外部ライブラリの追加が無いため WebSearch は行わず、実コード読解のみ）
- 前提（Gate A でユーザー確定・再議論しない）: (1) レイヤー weight / 入力源 weight 変更は HID-80 へ分離、(2) ゲートの列挙は全パッケージ Runtime + Editor の `IInputSource` 具象 + `IAnalogInputSource` 単独具象、(3) `.fcrec` は formatVersion 1 据え置きで在置き変更・`RecTimelineSeek` と timeline REC Export は新 kind に追随、(4) OSC heartbeat `Replace` は osc 側で廃止（in-place 更新）

## Summary

- **Key Findings**:
  - `LayerInputSourceAggregator` のフックは `TryWriteValues` 直後・`layerMask.Or(source.ContributeMask)` 前にあり、`IInputSource source` を 1 引数追加するだけで VP 型判定と最新 mask 読取（lipsync は `TryWriteValues` 内で `_currentContributeMask` を更新）が揃う。Option B（registry 走査）が抱える二重評価は回避できる
  - `OverlayInputSource` は `Layer2ActiveExpressionProvider`（系2）から解決する派生値だが、`RefreshResolution` → `_fromValues` / `_elapsedTime` のクロスフェード内部状態を持つ。基準確立の `ResetToExpressionStack` はこの内部状態を再現しないため「再導出で同一」は成立せず、観測対象（値提供型）に分類するのが唯一の整合解
  - `AnalogExpressionInputSource` / `AnalogBlendShapeInputSource` は `IAnalogInputSource` を直参照する。入力側 wrapper を Replace しても直参照は切れないため、派生値として除外すると再生中にライブ値が残る。自身を VP として Replace すれば遮断される → 観測対象
  - `OscInputSource` の `BlendShapeCount` は `contributeMask.Length`（= mesh BlendShape 名数）で heartbeat 前後とも一定。mapping 表と mask を in-place 置換できる構造であり、`OscDoubleBuffer.Resize` 後も `GetReadBuffer()` を毎回読むため透過
  - `RecEventChunkQueue.Enqueue` は `CanWrite` を 1 回だけ判定し、次セグメントでも収まらないと `Span.CopyTo` が例外。容量を BlendShapeCount 由来にするだけでなく、単一レコード超過時の専用セグメント確保を入れる必要がある

## Research Log

### VP 観測点: Aggregator フック vs registry 走査
- **Context**: Req 2.1 / 8.7「消費値を観測・二重評価なし」
- **Sources Consulted**: `LayerInputSourceAggregator.cs` L291-420、`ILayerSourceValueObserver.cs`、`AnalogObservationSampler.cs`、`LipSyncPhonemeOverlayInputSource.cs` L127-140、`OscInputSource.cs` L130-188、`OverlayInputSource.cs` L197-218
- **Findings**: registry 走査型は各 VP の `TryWriteValues` をフレームに 2 回呼ぶ。lipsync は compose を 2 回実行、OSC は `_lastObservedTick` 更新で staleness 判定の時刻が走査側に寄る、overlay は `RefreshResolution` が 2 回走る（冪等だがコスト）。Aggregator フックは唯一の消費点で、同じ scratch を観測できる。欠けているのは `IInputSource` 参照の受け渡しのみ
- **Implications**: `ILayerSourceValueObserver.OnSourceValuesObserved` に `IInputSource source` を追加（実装 2 件の追随）。observer が null なら追加コストなし

### VP 遮断の主経路: Replace 単段 vs 基底 Suspend 二段
- **Context**: Req 3.3「直参照経路の遮断面」、gap analysis Option C の二段防御
- **Findings**: 直参照経路を実コードで洗い直すと、(a) OSC heartbeat `Replace`（Req 3.9 で osc 側に根絶）、(b) overlay layer weight（HID-80、対象外）、(c) VP 内部の `IAnalogInputSource` / `IActiveExpressionProvider` 直参照（VP 自身を Replace すれば無害化）の 3 種しかない。`ValueProviderInputSourceBase.TryWriteValues` は `abstract` で派生が直接 override しているため、基底にゲートを入れるには派生全改修（Req 8.4 違反）か Aggregator に VP 固有分岐を足す必要がある
- **Implications**: Replace 単段で十分。二段目は「どちらが効いたか」の診断を複雑にするだけで、根拠となる直参照経路が残らないため不採用

### OverlayInputSource の再導出可否
- **Context**: Req 1.3
- **Findings**: `RefreshResolution` はターゲット切替時に `_currentValues` から `_fromValues` を取り、active 表情の `TransitionDuration` でクロスフェードする。記録開始時に遷移途中なら `elapsed` と `from` が基準に含まれない。再生開始時に `ResetToExpressionStack` でトリガーを定常化しても overlay は「現在値 → 新ターゲット」のクロスフェードを開始し、収束窓が生じる（Req 3.3 / 3.8 違反）。音素予約 slot のインスタンスは `_phonemeSlotInert` で常時無効
- **Implications**: 観測対象（値提供型）に分類。再生中は Replace でレイヤーから外れ、停止時に原本復元 → 次の `RefreshResolution` で現状態からクロスフェード（ライブ引き継ぎの値ジャンプ許容と同じ扱い）

### 基準捕捉タイミング（Req 5.1）
- **Context**: gap analysis §2.6 / §3-7
- **Alternatives**: (i) `StartRecording` でフレーム外 `TryWriteValues`、(ii) 記録開始後の最初の Aggregate 観測を基準にする（`Open` 遅延 or 基準レコード後書き）、(iii) core が常時 last-frame キャッシュ（Req 8.2 違反）
- **Findings**: (i) の副作用を実コードで確認: `OscInputSource` は `_lastObservedTick` / `_lastDataTime` 更新（同フレーム内の再読取で結果不変）、`LipSyncPhonemeOverlayInputSource` は compose 再計算（provider の現在値に対し冪等）、`OverlayInputSource` は `RefreshResolution`（`Tick` を呼ばなければ `_elapsedTime` は進まない。切替検出は LateUpdate で起きるのと同じ結果）、`TimelineBakedValueSink` は純読取。いずれも「同一フレーム内で冪等な読取」。(ii) は `AnalogObservationSampler` の初回 publish（状態なし → 全件 publish）が LateUpdate 冒頭で時刻付きイベントを先に出すため、基準先行不変条件を守るにはフレーム境界通知 + イベントバッファリングが必要で機構が大きい
- **Implications**: (i) を採用。アナログ基準（`TryReadAxes`）と同じ方式・同じ性質（同一フレームの LateUpdate までに届いた値は t≈0 の時刻付きイベントになる）。初回フレームの全件 publish はアナログと同様に許容

### ベースライン外 VP の初期状態
- **Findings**: Aggregator は有効 source の mask を `layerMask.Or` し、`LayerBlender` が mask の立った index をそのレイヤーの寄与とみなす。0 埋め**有効**だと mask ぶん下位レイヤーを 0 で上書きする。無効（`TryWriteValues` = false）は「寄与なし」で、ソース不在と同じ結果
- **Implications**: 無効 + mask 全 false。アナログの 0 埋めとは「ライブ値を読まない確定的な中立状態」という規則が共通（軸の中立 = 0、VP の中立 = 無効）

### `.fcrec` レコード設計と記録サイズ試算
- **Alternatives**: dense（BlendShapeCount 全値）/ sparse by index（u16 idx + f32）/ sparse by mask（mask 順の値列 + mask 変化時のみ mask バイト列）
- **Findings**: Aggregator は mask 外の index も加算するが、現行 6 実装は mask 外に書かない（OSC は mapping index = mask、lipsync / overlay / analog-expression は snapshot index のみ、timeline は baked index のみ）。試算（60 fps、1 体）: OSC ARKit 52ch を dense 300ch で保存 → 1+8+2+1+2+1200 = 1214 B/フレーム ≈ 73 KB/s ≈ 4.4 MB/分。sparse by index → 1+8+2+1+2+52×6 = 326 B ≈ 1.2 MB/分。sparse by mask → 1+8+2+1+2+52×4 = 222 B ≈ 0.8 MB/分（mask 38 B は変化時のみ）。10 体で 8 MB/分
- **Implications**: sparse by mask を採用し、1 レコード = (flags, [mask], [values]) の自己記述形式。mask 変化時は値も全量含める（読取側が差分畳み込みを持たなくてよい）。u16 count で 255 超に対応。mask 外非ゼロは契約上の既知制限

### `RecEventChunkQueue` 容量方針
- **Findings**: 既定 `axisFloatCapacityPerSegment = 128`。VP 1 件が最大 BlendShapeCount 個の float を持つ。mask バイト列の運搬経路が無い（float 配列に bit を詰めるのは可能だが不透明）
- **Implications**: byte 区画を追加し、容量は `RecCharacterBinding` が `controller.BlendShapeCount = N` から `float = max(128, 4N)`、`byte = max(64, 4·ceil(N/8))` で決める。単一レコードが容量超過なら専用セグメントを確保（`GrowthCount++`、例外なし）

### 系1 予約 id の選定
- **Findings**: `InputSourceId` は `^[a-zA-Z0-9_.\-:]{1,64}$`、`AdapterSlug` は `^[a-zA-Z0-9_.-]{1,64}$`。registry の id は slug または slug:sub でいずれもこの集合に閉じる。`LayerExpressionSource.Id == "input"` は inputsystem と同名で流用不可。`RecIdTable` は非空白のみ要求
- **Implications**: `@expression`（`@` は両規約の許容外）。構造的に衝突しない。新 kind（9 / 10 / 11）で経路を区別するため、id 衝突が起きても誤配はしないが、分類・診断の一意性のために規約外文字を選ぶ

### 系1 観測・ゲートの配置と `LayerExpressionSource` スナップ
- **Alternatives**: (a) `ExpressionUseCase`（Application）に 4 面、(b) `FacialController.Activate/Deactivate`（Adapters）にゲート、(c) ラッパクラス
- **Findings**: `ExpressionUseCase` はプレーンクラスで Small テスト可能、`FacialController` を経由しない利用者（テスト・将来の Application 直接利用）も捕捉できる。`LayerExpressionSource.UpdateExpressions` は変化検出のたびに必ず遷移を開始し、`LayerUseCase` は `_expressionUseCase` を保持している
- **Implications**: (a) を採用。スナップは `ExpressionUseCase.ResetGeneration`（int）を `LayerUseCase.UpdateWeights` が比較し、変化フレームだけ `SnapToExpressions` を使う。FacialController に新 API を増やさず、既存 `UpdateExpressions` のロジックも触らない。`FacialController` は `IExpressionActivationGate` を公開するだけ

### 系1 の途中再生畳み込み
- **Findings**: Activate は LastWins レイヤーで既存を Clear するため、順序付き id 列から Deactivate を単純 remove で畳むと誤る（例: A → B（LastWins）→ B off = 空、単純畳み込みだと [A]）。`RecTimelineSeek` は rec Domain で `FacialProfile`（core Domain）を参照できる（`RecValidation` が既に参照）
- **Implications**: `BuildBaselineAt(timeline, offset, profile)` に profile を渡し、レイヤー排他意味論で畳む。`ResetActiveExpressions(ids)` は Activate 意味論で順に積むため、基準列（レイヤー別順序付き）から同一状態を復元できる

### 分類一覧の所在と Editor アセンブリの列挙
- **Findings**: `TestAssemblyCatalog`（`Hidano.FacialControl.Testing`、`noEngineReferences`）は `AppDomain.CurrentDomain.GetAssemblies()` を走査する前例。Editor ドメインには全パッケージの Runtime / Editor asmdef（`Hidano.FacialControl.*`）がロード済み。`scripts/check-test-sizes.ps1` の Small 禁止 API に reflection / AppDomain は含まれない。private nested 型（`LayerUseCase+LayerExpressionSource`、`InputSystemAdapterBinding+AnalogInputSourceWrapper`）は `GetTypes()` で列挙されるが `typeof` できない
- **Implications**: 分類正本は rec Runtime Domain の文字列カタログ（型 FullName + アセンブリ名）。ゲートテストは rec Tests/EditMode に `[SmallTest]` で置き、拡張 asmdef の参照追加は不要。アセンブリ名を持たせることで「アセンブリ未ロード」を陳腐化と区別して報告できる

### OSC heartbeat の in-place 更新の成立条件
- **Sources**: `OscReceiverAdapterBinding.cs` L700-750（OnStart）、L2165-2204（PublishRuntimeMappings）、`OscInputSource.cs` L87-124、`OscDoubleBuffer.cs` L96-111、`IFacialMocapReceiverAdapterBinding.cs` L177 / L367 / L540
- **Findings**: `OnStart` は `hasBlendShapeMappings` のときだけ `OscInputSource` を Register する。heartbeat のみの構成では最初の heartbeat の `Replace` が実質 Register。`PublishRuntimeMappings` は `_runtimeMappings.Length == 0` のとき source を更新せず return する（古い mapping が残る）。ifacialmocap は構築時 1 回の Register のみで heartbeat 相当は無し。lipsync のデバイス再選択・timeline の takeover は `Replace`（timeline は占有検査付き）/ `Unregister` で、heartbeat 型の無検査 `Replace` は osc のみ
- **Implications**: `OnStart` で無条件に構築・Register（空マッピング = 無効）し、以後は `UpdateMapping` のみ。registry エントリの参照は構築時から不変（Req 3.9 の文言どおり）。空マッピング時の無効化は従来の「古い mapping が残る」挙動の修正を伴う（設計で明記）

## Architecture Pattern Evaluation

| Option | Description | Strengths | Risks / Limitations | Notes |
|--------|-------------|-----------|---------------------|-------|
| A: 既存拡張 | Aggregator フック拡張 + Replace 注入 + ExpressionUseCase 直接拡張 + osc in-place | 消費値を正確に観測、新規型が少ない、既存パターンの延長 | 複数 interface の同時破壊的変更 | **採用**（Gate A で osc 改修が確定し「原則違反」の懸念が解消） |
| B: 新規中心 | registry 走査 VP サンプラー + 基底 Suspend を Aggregator が尊重 | 拡張完全無改修 | `TryWriteValues` 二重評価、Aggregator に VP 固有分岐、OSC heartbeat が新インスタンスを作る限り遮断が外れる | 不採用 |
| C: ハイブリッド | A の観測 + Replace 主経路 + 基底 Suspend 保険 | 到達性の二重保証 | 直参照経路が残らないため保険の根拠が無い。診断が複雑 | 不採用（保険部分のみ） |

## Design Decisions

### Decision: VP 観測は Aggregator フックに `IInputSource` を追加して行う
- **Alternatives**: registry 走査サンプラー / フックに `BitArray mask` だけ追加
- **Selected**: `OnSourceValuesObserved(..., IInputSource source, bool isValid, span)`。`ValueProviderObservationSampler` が `is ValueProviderInputSourceBase` で選別し `source.ContributeMask` を読む
- **Rationale**: 型判定と mask 読取を 1 引数で満たす。id ベース選別は `"input"` 衝突で不可
- **Trade-offs**: 実装 2 件の追随。observer に source 参照を渡すが Domain 内で閉じる
- **Follow-up**: `FacialController` の HasObservers エッジ着脱で観測者ゼロ時のフックを null に保つ（既存 GC ゲートで確認）

### Decision: VP 遮断は Replace 単段
- **Rationale**: 上記 research「VP 遮断の主経路」
- **Trade-offs**: 再生中に新規登録された VP は遮断されない（既知制限を継承）

### Decision: `OverlayInputSource` / `AnalogExpressionInputSource` / `AnalogBlendShapeInputSource` は観測対象
- **Rationale**: 再導出不成立（クロスフェード状態）/ 直参照のライブ残存。コストは記録サイズのみ（変化時のみ記録）
- **Trade-offs**: overlay の値は系2 トリガーと二重に記録される（冗長だが無害）

### Decision: 系1 は `ExpressionUseCase` が `IExpressionActivationGate` を実装し、スナップは `ResetGeneration` で `LayerUseCase` が追従
- **Alternatives**: FacialController にゲート / ラッパ / LayerUseCase に明示 Snap API
- **Rationale**: 状態オーナーに面を置く（trigger 基底と同型）。FacialController の公開面増加を 2 プロパティに抑える
- **Trade-offs**: `UpdateWeights` に int 比較 1 回が増える

### Decision: 基準捕捉はフレーム外 `TryWriteValues`、ベースライン外 VP は無効
- **Rationale**: 上記 research 2 項
- **Follow-up**: `IInputSource` 契約文書に「`TryWriteValues` は同一フレーム内で冪等」を追記する（実装変更なし）

### Decision: `.fcrec` は mask 順疎値 + flags の自己記述レコード（kind 7 / 8）、系1 は kind 9 / 10 / 11
- **Rationale**: サイズ試算、reader の単純性、u16 count
- **Trade-offs**: mask 外非ゼロは非再現（契約化）

### Decision: 分類正本は rec Runtime の `RecInputSourceCoverageCatalog`、ゲートは rec Tests/EditMode の Small
- **Rationale**: 設計成果物として利用者に見え、README の除外表と 1:1。`AppDomain` 走査で拡張参照不要
- **Trade-offs**: Runtime に文字列データが載る（数百バイト）

### Decision: 4 ポートは T → E → A → V の同一順序で確立・解放
- **Rationale**: ゲート型（registry 非接触）→ Replace 型の順。既存の「確立・解放とも同一順序」判断を踏襲

## Risks & Mitigations

- `ILayerSourceValueObserver` 変更で timeline Editor のベイクが壊れる — コンパイル追随のみ。`BakeSimulationHarness` のテストを緑に保つ
- heartbeat 構成で `OnStart` から `osc` が registry に居るようになり、レイヤー束縛が heartbeat 前に起きる — 空マッピングは無効扱いで寄与ゼロ（従来の「未登録」と同じブレンド結果）。空レイヤー警告の有無は従来と同じ（無効 source のみ → 警告）
- `RecEventChunkQueue` の byte 区画追加で SPSC 同期が複雑化 — 既存の `PublishedCount` を単一の公開点として維持し、float / byte の書込は公開前に完了させる
- 系1 の基準列が長い（Blend レイヤーで多数 active）場合の `ResetActiveExpressions` コスト — 非毎フレームで許容
- 再生中の再記録で注入 VP が観測される（意図どおりだが記録サイズ増） — 文書化

## References

- `.kiro/specs/rec-recording-playback/design.md`（観測面・注入面・`.fcrec` の契約オーナー）
- `.kiro/specs/rec-playback-input-exclusivity/design.md`（遮断面・排他ライフサイクル）
- `docs/testing.md` / `docs/test-policy.md`（Small / Medium 規約、テスト分類）
- `scripts/check-test-sizes.ps1` L100-112（Small 禁止 API）
- Linear HID-35（本 spec の起点）、HID-80（レイヤー weight 分離）

---

# 設計レビュー対応 research: rec-full-input-coverage

- 実行日: 2026-10-04
- 契機: codex 設計レビュー NO-GO（Critical 3 件）。design.md をマージモードで修正した際の調査記録。固定済みのユーザー決定（Gate A 決定 1〜4）は変更していない

## Critical 1: 網羅性ゲートが未ロードアセンブリを検出できない

- **Context**: ゲートは `AppDomain.CurrentDomain.GetAssemblies()` に依存しており、どこからも参照されない asmdef が EditMode ドメインに未ロードなら、その型は黙って列挙から欠落する
- **Sources Consulted**: `com.hidano.facialcontrol/Tests/Testing/TestAssemblyCatalog.cs`（`FindProjectTestAssemblies` / `IsProjectTestAssemblyName` / `GetLoadableTypes`）、`com.hidano.facialcontrol.rec/Tests/EditMode/Hidano.FacialControl.Rec.Tests.EditMode.asmdef`（参照: Rec 4 asmdef + core 4 asmdef + Testing）、全パッケージの asmdef 一覧（Glob）
- **Findings**:
  - product asmdef（テスト・Testing 除く）は 20 件。Runtime 11: Domain / Application / Adapters / Osc / InputSystem / LipSync / IFacialMocap / Rec.Domain / Rec.Application / Rec.Adapters / Timeline。Editor 9: Editor / Osc.Editor / InputSystem.Editor / LipSync.Editor / IFacialMocap.Editor / Rec.Editor / Timeline.Editor / RoutingEditor / ExpressionCreator
  - Editor 専用 asmdef のうち `RoutingEditor` / `ExpressionCreator` は名前が `.Editor` で終わらない。EditorOnly 判定を名前末尾で行うと誤判定するため、カタログ側に `IsEditorOnly` を宣言する
  - Unity Editor は `Library/ScriptAssemblies` の全 asmdef をドメインリロード時にロードする（`[InitializeOnLoad]` や `TypeCache` が参照無しの asmdef でも機能する根拠）。実運用上は未ロードは起きにくいが、設計はこの挙動に依存せず明示検査で担保する
- **Alternatives**:
  - (a) rec Tests asmdef から全 product asmdef を参照し `typeof(<公開型>)` でアンカー: 確実にロードされるが、`ExpressionCreator`（`com.hidano.scene-view-style-camera-controller` 依存）や `LipSync`（uLipSync 依存）を rec テストのコンパイル依存に取り込み、将来パッケージを追加するたび asmdef 編集が必要。private nested 型はアンカーに使えない
  - (b) 期待アセンブリ名リスト（カタログ `ProductAssemblies`）+ 双方向包含検査: 「期待 ⊆ ロード済み」で未ロードを失敗、「ロード済み ⊆ 期待」でリスト未更新の新 asmdef を失敗。参照追加なし・Small 可
- **Selected**: (b)。リストはカタログ（rec Runtime Domain）に置き、エントリの `AssemblyName` がリスト内であることも検査して正本を 1 箇所に保つ。検証ロジックは `RecInputSourceCoverageGate` 純関数に分離し、負例（未ロード名 1 件 / 未宣言名 1 件）を Small で固定する
- **Implications**: Req 7.9 の「参照追加を許容」は使わない。`TestAssemblyCatalog` に `IsProjectProductAssemblyName` / `FindProjectProductAssemblies` / `TryFindLoadedAssembly` を追加

## Critical 2: formatVersion 1 据え置き（固定済み決定の明示化）

- **Context**: レビューは版の繰り上げまたは互換フラグを求めたが、Gate A 決定 3「v1 を一度もリリースしていないので v1 のまま改変してよい」は固定済みのユーザー決定であり変更しない。レビューアが見落とせないよう design.md の Migration Strategy / Data Models / Error Handling に根拠と失敗挙動を明記した
- **Sources Consulted**: `RecBinaryFormat.cs`（`CurrentFormatVersion = 1`、`TryRead` L219-、未知 kind は `Unknown REC record kind N.` でエラー L506-508、書込側は `ArgumentOutOfRangeException`）、`RecFileReader.cs`（`TryRead(filePath)` が `RecBinaryFormat.TryRead` の失敗を LogError）、`RecCharacterBinding.cs` L262（`Load` が失敗時に再生を開始しない）
- **Findings**:
  - リーダーは本リポジトリ内に 4 箇所（`RecBinaryFormat` / `RecFileReader` / `RecTimelineSeek` / timeline Editor `RecEventSequenceAdapter`）のみで、すべて本 spec で同時改修する
  - 旧構造ファイル（kind 1〜6 のみ）は kind 1〜6 のレイアウトが不変のため構造上は読める。設計は「読める」を保証とせず非サポートと明記し、読めた場合の挙動（kind 8 / 11 不在 → VP / 系1 の基準は空）を確定的に記述した。逆方向（新構造ファイルを旧リーダーで読む）は既存の未知 kind エラーで失敗する
  - 「黙ってスキップ」を許すのは timeline Editor の Export アダプタのみ（Req 6.9）。core リーダーのスキップは構造の食い違いを隠すため禁止
- **Implications**: 既知制限 7 を追加。rec README「ファイル形式」の文言を再収録案内まで含めて更新

## Critical 3: 明示的除外の妥当性をゲートが検証しない

- **Context**: 除外 7 型（#14〜#20）の根拠が文書のみで、将来の配線変更で除外型がブレンド出力に到達してもテストが失敗しない
- **Sources Consulted**: `IInputSourceRegistry.cs`（`Register / Replace` は `IInputSource` を取る）、`IInjectedInputSource.cs`、`ArKitOscAdapterBinding.cs` L40-190（`OnStart` で `new ArKitOscAnalogSource` を構築・`AnalogSource` で公開・`OnTick` で Tick。registry 呼出なし）、`InputSystemAdapterBinding.cs` L280-320（`ApplyOverlayLayerWeights` が `InputActionAnalogSource` 直参照で `SetLayerWeight`）/ L470-530（wrapper 構築と Register）/ L531-598（辞書を `AnalogExpressionInputSource` へ引き渡し Register）、Runtime の `IAnalogInputSource` フィールド保持者（`AnalogBonePoseProvider` / `GazeBonePoseProvider` / `AnalogObservationSampler` / `AnalogExpressionInputSource` / `AnalogBlendShapeInputSource`）、`new ArKitOscAnalogSource( / new OscFloatAnalogSource( / new InputActionAnalogSource(` の全出現（Runtime は `ArKitOscAdapterBinding` L157 と `InputSystemAdapterBinding` L482 のみ。`OscFloatAnalogSource` はテストのみ）、osc / inputsystem の既存テスト（`ArKitOscAdapterBindingTests` は EditMode `[MediumTest]`、`OscReceiverAdapterBindingTests` が EditMode で `OnStart` を実行、`InputSystemAdapterBinding.OnStart` は PlayMode `InputSystemAdapterBindingIntegrationTests` のみ）
- **Findings（区分ごとの検証可能性）**:
  - InjectionSource: `typeof(IInjectedInputSource).IsAssignableFrom` で Small 検証可
  - EditorOnly: 名前末尾 `.Editor` は `RoutingEditor` / `ExpressionCreator` で破綻するためカタログ宣言 `IsEditorOnly` で判定。加えて Runtime product アセンブリの `GetReferencedAssemblies()` に Editor 専用名が無いことで「Runtime から到達不能」を構造的に検証（Runtime asmdef が `#if UNITY_EDITOR` で UnityEditor を参照することはあり得るが、Editor 専用の product asmdef を参照することは asmdef の `includePlatforms` 上あり得ない）
  - NotRegisteredAtRuntime: 「Runtime に構築箇所が無い」契約は `OscFloatAnalogSource` には成立するが、`ArKitOscAnalogSource` は `ArKitOscAdapterBinding.OnStart` で構築されるため不成立。よって (1) `IInputSource` 非実装（直接登録が型レベルで不可能。wrapper は列挙対象）、(2) IL 走査で直接参照元が許容集合内（#19 は空、#18 は `ArKitOscAdapterBinding`）、(3) `ArKitOscAdapterBinding.OnStart` を Fake registry で実行し Register / Replace 0 回を Medium EditMode で固定、の 3 層にした。`OnStart` は `AddComponent<OscReceiverHost>` と `StartReceiving`（loopback UDP）を伴うため Small 不可・Medium 可（既存 `OscReceiverAdapterBindingTests` と同じ起動手順）
  - WrappedByObservedSource: `AnalogExpressionInputSource` は `IAnalogInputSource` 辞書（インターフェース型）経由で消費するため、具象型への call を IL で探しても消費者としては現れない。直接参照元は `InputSystemAdapterBinding`（wrapper 構築 / 辞書構築 / overlay weight）に閉じるので、IL 走査の許容集合を `InputSystemAdapterBinding` とし、「触った結果どこへ渡すか」は PlayMode Medium（登録型がリテラル集合 {wrapper, ExpressionTriggerInputSource, AnalogExpressionInputSource, OverlayInputSource} に閉じる、辞書の値が wrapper 登録済み id に閉じる）で固定する。`InputSystem.*` は Small 禁止のため PlayMode
  - IL 走査: `MethodBody.GetILAsByteArray()` + `System.Reflection.Emit.OpCodes`（netstandard 2.1 の System.Reflection.Primitives、Unity Mono で利用可）のオペランド長表で命令を進め、`newobj / call / callvirt / ldftn / ldvirtftn / ldfld / stfld / ldsfld / stsfld / ldtoken / castclass / isinst / box / unbox.any` のトークンを `Module.ResolveMethod / ResolveField / ResolveType` で解決する。reflection のみで Small 可。検出限界は動的構築とインターフェース経由呼出（既知制限 8）
- **Alternatives considered**: フィールド / ローカル / シグネチャの型走査のみ（簡易だが `new Wrapper(new X(...))` の即時引き渡しを見逃す）→ IL 走査を採用。各区分を Medium だけで検証（Small ゲートが CI の全 push で回る利点を失う）→ Small 契約を主、Medium を registry 到達の補強とした
- **Implications**: カタログに `RecExclusionReason` / `WrapperTypeFullName` / `AllowedDirectReferrers`（理由必須）を追加。除外 7 行すべてに契約テスト名を付記。`ExclusionReason` の値追加時は契約テストの追加が必須（`ExclusionContract_EveryExclusionReason_HasContract` が未知の値を拒否）

---

# 設計レビュー（2 回目）対応 research: rec-full-input-coverage

- 実行日: 2026-10-04
- 契機: codex 設計レビュー 2 回目 NO-GO（Critical 3 件）。design.md をマージモードで修正した際の調査記録。固定済みのユーザー決定（Gate A 決定 1〜4: HID-80 分離 / Runtime + Editor 列挙 / formatVersion 1 据え置き / OSC in-place `UpdateMapping`）と 1 回目レビューの修正内容は変更していない

## Critical 1（2 回目）: ゲートが「Editor asmdef は EditMode ドメインに常にロードされる」ことを前提にしている

- **Context**: レビューは「アセンブリが未ロードなら型が黙って欠落する」と指摘。1 回目レビューで導入した双方向包含検査は既に未ロードを失敗に変換するが、design.md ではその性質（fail-loud）が前提の裏返しとして読める書き方だった
- **Sources Consulted**: `docs/testing.md`「CI での回し方」（Linux セルフホストランナー、`-runTests -testPlatform EditMode -testCategory Small`、全 push / PR）、`TestAssemblyCatalog.cs`（`AppDomain.CurrentDomain.GetAssemblies()` 走査）、全パッケージの asmdef（`includePlatforms` と `defineConstraints`）
- **Findings**:
  - Unity はプロジェクト内の全 asmdef（パッケージの Editor asmdef を含む）を `Library/ScriptAssemblies` にコンパイルし、Editor AppDomain にドメインリロード時に一括ロードする。`includePlatforms = ["Editor"]` の asmdef は Editor がプラットフォームであるため常にコンパイル対象。EditMode テストはこの AppDomain で走るため、ローカルでも Linux CI の batchmode でもロード集合は同一。いずれかの asmdef がコンパイルできなければテスト実行自体が始まらない
  - 未ロードが起きるのは defineConstraints 未成立・platform 除外・asmdef 改名 / 削除のときであり、いずれも「期待 ⊆ ロード済み」検査が当該アセンブリ名を挙げて失敗する。つまり設計はロードを前提にしておらず、「ロードされていなければ失敗する」ことを前提にしている
- **Selected**: 設計変更なし。design.md に (a) fail-loud 性質の明示（Components / Testing Strategy / Error Handling）、(b) 上記 Unity の事実と CI 条件、(c) 正例 `FindProjectProductAssemblies_CatalogProductAssemblies_AllLoaded`（実 AppDomain で 20 件全ロード）と負例 `Gate_MissingExpectedAssembly_FailsWithAssemblyName`（人工的な 1 件欠落で名前付き失敗）の対、(d) Revalidation Triggers に「パッケージ追加 = `ProductAssemblies` 更新、さもなければゲート失敗」を追記した。拡張 asmdef へのコンパイル参照は追加しない（1 回目レビューの決定を維持）

## Critical 2（2 回目）: IL スキャナが解決失敗を無視し、ジェネリック / 特殊 IL で参照を取りこぼす

- **Context**: 1 回目対応では IL 走査を Small 契約の主、Medium を補強と位置付け、`Resolve*` の例外は無視としていた。レビューは「無視された解決失敗 = 見逃し」を指摘
- **Sources Consulted**: `System.Reflection.Module.ResolveMethod / ResolveField / ResolveType / ResolveMember`（`genericTypeArguments` / `genericMethodArguments` 引数でジェネリック文脈を受ける）、`System.Reflection.Emit.OpCodes`（1 byte / 2 byte opcode、`OperandType` でオペランド長が決まる。`InlineSwitch` は可変長）、既存の `ArKitOscAdapterBindingTests` / `OscReceiverAdapterBindingTests`（EditMode `[MediumTest]`、`OnStart` を実行）、`InputSystemAdapterBindingIntegrationTests`（PlayMode `[MediumTest]`）、osc Tests に Fake `IInputSourceRegistry` が無いこと（rec / timeline Tests には test-local の Fake がある）
- **Findings**:
  - 除外根拠「合成パイプラインに到達しない」を最も直接に固定するのは、binding の `OnStart` が Fake registry に登録した実行時型の集合である。これは動的構築やインターフェース経由の消費を含めて「結果」を観測するため、IL 走査の検出限界に依存しない
  - #19 `OscFloatAnalogSource` は Runtime に構築箇所が無いため、既存案では IL 走査のみが契約だった。osc で registry に触る binding は `OscReceiverAdapterBinding` と `ArKitOscAdapterBinding` の 2 つなので、両者の登録集合を Fake registry で固定すれば osc 内の `IAnalogInputSource` 単独実装 2 型の非登録が閉じる。`OscReceiverAdapterBindingTests` は既存の EditMode Medium fixture で `OnStart` を実行しているため追記先として適切
  - fail-closed 化: `Resolve*` の例外・opcode 表に無い値・オペランド途中切れ・`calli` は標準例外（`InvalidOperationException`。型名・メソッド名・IL offset・理由）で走査を中断する。`GetMethodBody()` が null（abstract / extern / interface）は本体なしであり解決失敗ではない。ジェネリック文脈は宣言型・メソッドの `GetGenericArguments()` を常に渡す（ジェネリック定義本体は定義の型パラメータが文脈になる）
  - 主契約名をカタログの `RuntimeRegistrationContractTest` に宣言させ、Small がロード済みテストアセンブリ（PlayMode テスト asmdef も Editor ドメインにロードされる）に fixture 型 FullName とメソッド名が実在することを reflection で検査すれば、主契約の削除・改名を Small（全 push）で検出できる。コンパイル参照は不要
- **Alternatives considered**: IL 走査を廃止して Medium のみ（Small で回る「誰が触るか」の早期検出を失う）/ IL 走査を主のまま fail-closed 化のみ（検出限界 2 種が根拠の穴として残る）→ 主従を反転し、IL 走査は fail-closed の補助とした
- **Implications**: design.md の Components / Testing Strategy / Error Handling / 分類表（契約テスト列は主契約を先頭、IL を「補助」）/ Traceability 1.2 / 1.4 / 7.3–7.7 / 既知制限 8 を更新。osc `OscReceiverAdapterBindingTests` に `OnStart_FakeRegistry_RegisteredTypesAreOnlyCatalogObservedTypes` を追加（osc Tests に Fake registry を新設）。`ProductAssemblyIlScannerTests` を新設し fail-closed とジェネリック文脈解決を固定

## Critical 3（2 回目）: formatVersion 1 のまま旧構造ファイルが「成功したように見える」

- **Context**: kind 1〜6 のみの旧構造ファイルは構造上 `TryRead` を通過し、VP / 系1 の基準が空のまま再生が始まる。Gate A 決定 3（formatVersion 1 据え置き）は変更しない前提で、旧構造ファイルを確定的に拒否する自己識別マーカーが必要
- **Sources Consulted**: `RecBinaryFormat.cs`（`HeaderSize = 16`、offset 6 の `flags` u16 は `DefaultFlags = 0` で書かれ `TryRead` は値を検証しない。コードコメント「将来ビットを割り当てても formatVersion を上げずに済む」）、`RecStreamWriter.cs` L293（`RecBinaryFormat.WriteHeader` を呼ぶ唯一の記録経路）、`RecFileReader.cs`（`TryRead` 失敗を LogError）、timeline Editor `RecToTimelineExporter` / `RecTimelineExportWindow`（`RecFileReader.TryRead` 経由）、rec `README.md` L95（flags の予約記述）、rec Tests（`Header.Flags` を assert するテストは無い）
- **Alternatives**:
  - (a) 基準セクションの必須マーカーレコード: writer は kind 8（VP 基準）と kind 11（系1 基準）を 0 件でも必ず出力し、reader は両者の存在を要求する。kind 8 / 11 は設計上「エントリ単位レコード」（1 レコード = 1 source / 1 expression）なので、0 件を表すには「count 付きセクションレコード」へ再定義する必要があり、`RecEvent` モデル・per-entry レイアウト・`GetSerializedSize` が変わる。拒否判定はファイル全体の走査後
  - (b) ヘッダ `flags` bit0 を必須ビットにする: `RecHeaderFlags.FullInputBaseline = 0x0001`。writer（`WriteHeader`、1 箇所）は常に立て、reader（`TryRead`、1 箇所）は magic / formatVersion の直後に `(flags & Required) == Required` を検査し、欠落なら既存エラー様式の文字列で false を返す。追加バイト 0。予約済みフィールドの本来の用途（版を上げずにビットを割り当てる）と一致し、Gate A 決定 3 と整合。bit1〜15 は従来どおり検証しない
- **Selected**: (b)。理由: 予約フィールドが存在し用途が明記済み、書込 / 読込点が各 1 箇所、拒否がレコード内容（基準 0 件を含む）に依存せず走査前に完了する。(a) はレコードモデルの再定義を伴い、本 spec の kind 8 / 11 レイアウトと `RecEvent` の決定を動かす
- **Implications**: `RecHeaderFlags`（rec Domain Models）を新設、`RecBinaryFormat.RequiredHeaderFlags` / `DefaultFlags` を更新。エラー文言 `REC file header flags 0x{flags:X4} lack the required FullInputBaseline bit 0x0001. Files recorded before value-provider/expression baseline support are not supported; re-record with the current version.`。テスト: `WriteHeader_Always_SetsFullInputBaselineFlag` / `Serialize_EmptyValueProviderAndExpressionBaseline_RoundTrips` / `TryRead_HeaderWithoutFullInputBaselineFlag_ReturnsError` / `TryRead_PreCoverageFileWithKinds1To6Only_ReturnsError` / `RecStreamWriterTests.Open_Always_WritesHeaderWithFullInputBaselineFlag` / `RecFileReaderTests.TryRead_FileWithoutFullInputBaselineFlag_LogsErrorAndReturnsFalse`。design.md の Migration Strategy は「構造上は読めてしまう」の文言を拒否保証に置換、既知制限 7 を置換、rec README「ファイル形式」行に flags 記述の更新と拒否エラーの案内を追加。formatVersion は 1 のまま（Gate A 決定 3 不変）

---

# 設計レビュー（3 回目）対応 research: rec-full-input-coverage

- 実行日: 2026-10-04
- 契機: codex 設計レビュー 3 回目（Critical 2 件: 入力源識別スコープが未定義 / 4 ポート `BeginInjection` にトランザクション性がない）。design.md をマージモードで修正した際の調査記録。固定済みのユーザー決定（Gate A 決定 1〜4: HID-80 分離 / Runtime + Editor 列挙 / formatVersion 1 据え置き + `RecHeaderFlags.FullInputBaseline` / OSC in-place `UpdateMapping`）と 1 回目・2 回目レビューの修正内容は変更していない

## Critical 1（3 回目）: 入力源識別スコープが未定義

- **Context**: `ValueProviderObservationSampler` / `RecValueProviderInjector` / baseline / kind 7・8 が文字列 `sourceId` だけをキーにしているのに、(a) 重複 id、(b) 同一インスタンスの複数レイヤー宣言、(c) 複数インスタンス / Replace 後の同一性に関する一意性契約が書かれていない、という指摘。キー設計を変えずに、不変条件を実コードから確認して明文化し、ゲートテストで固定する方針で対応
- **Sources Consulted（実コード）**:
  - `com.hidano.facialcontrol/Runtime/Adapters/InputSources/InputSourceRegistry.cs`: `Dictionary<string, IInputSource> _entries` 1 本。`RegisterInternal` は `_entries.ContainsKey(key)` のとき `Debug.LogError("[InputSourceRegistry] duplicate registration for id '{key}'; later registration wins.")` → `_entries[key] = source` → `NotifySubscribers`。例外も拒否もしない。`_registeredIds` は重複追加しない。`ReplaceInternal` は登録済みなら上書き（Info ログ）、未登録なら新規登録。`IInputSourceRegistry.cs` の XML doc も「同一 id への重複登録は LogError を出し、後勝ちで上書きする」と明記
  - `com.hidano.facialcontrol/Tests/EditMode/Adapters/InputSources/InputSourceRegistryTests.cs`（`[SmallTest]`）: `Register_DuplicatePrimarySlug_LogsErrorAndOverwrites` / `Register_DuplicateCompositeSlug_LogsErrorAndOverwrites`（`TryResolve` が 2 回目のインスタンスを返す）/ `RegisteredIds_AfterDuplicateRegister_DoesNotDuplicate` が既に存在する
  - `com.hidano.facialcontrol/Runtime/Domain/Services/LayerInputSourceAggregator.cs` L316-337: `for (int s = 0; s < sourceCount; s++) { source = _registry.GetSource(l, s); source.Tick; scratch.Clear; sourceIsValid = source.TryWriteValues(scratch); sourceValueObserver?.OnSourceValuesObserved(l, s, …) }`。**`TryWriteValues` とフックは (layer, source) スロットごと**。`FacialController.cs` L514-520 は各レイヤーの宣言 id を `_inputSourceRegistry.TryResolve(decl.Id)` で解決して `(l, source, weight)` に展開するため、同一 id の複数レイヤー宣言では同一インスタンスが複数スロットに置かれる。`LayerInputSourceRegistry.TryAddSource` は**同一レイヤー内**の同 id 重複のみ拒否する
  - `com.hidano.facialcontrol/Runtime/Adapters/InputSources/AnalogObservationSampler.cs`: `Dictionary<string, int> _trackedSourceIndices` と `RegisteredIds` 走査。id 文字列のみをキーにし、`AxisCount` 変化で `HasSample = false`。同 id 別インスタンスは `tracked.Source = analogSource` で差し替えるだけ（値比較は継続）
  - `com.hidano.facialcontrol.rec/Runtime/Adapters/Playback/RecAnalogInjector.cs`: `Dictionary<string, RecPlaybackAnalogSource> _attachedSources`（id キー）。`EndInjection` は `_registry.TryResolve(sourceId, out current) && ReferenceEquals(current, playbackSource)` のときのみ `ReplacedSource` へ `Replace` / `Unregister`、不一致は `WarnRestoreMismatchOnce`。`AttachPlaybackSource` は `currentSource is IInjectedInputSource` で占有スキップ
  - `FacialController.InputSourceRegistry` は FC ごとに 1 個、`RecCharacterBinding.EnsurePlaybackSession(controller)` は controller ごとに Injector 群を構築（`_runtimeController` 参照一致で再利用）
  - `com.hidano.facialcontrol.rec/Runtime/Domain/Services/RecIdTable.cs`: `GetOrAddSourceId` は冪等（既存 index を返す）。`AddDefinedId` は同一 value が別 index に既登録なら `InvalidOperationException("Id '{value}' was already assigned to index {existingIndex}.")`、同一 index に別 value なら同じく `InvalidOperationException`。`RecBaselineState.cs` は `TriggerEntry` / `AnalogEntry` の SourceId 重複を検証しない（`RecBinaryFormat.BuildBaselineAnalogEntries` は `latestAxes[sourceId]` で後勝ちに畳む）。rec Tests に `RecIdTableTests` / `RecBaselineStateTests` は存在しない
- **Findings**:
  - 一意性の担保点は core の per-FC registry であり、REC が別に id 空間を管理する必要はない。「重複 Register は拒否される」と書くのは誤りで、実挙動は「LogError + 後勝ち上書き」。REC の観測・基準・注入は「その時点で registry が返すインスタンス」を対象にすれば一貫する（アナログ経路の既存前提と同じ）
  - 同一インスタンスの複数レイヤー宣言では sampler に 1 フレーム複数回コールバックが来る。dedupe の実現案: (i) `FacialController` が `LateUpdate` ごとに `sampler.BeginFrame()` を呼びフレームトークンで「id ごとフレーム初回のみ評価」する、(ii) 比較対象を「当該 id の最終 publish 状態」に固定し、1 回目で `Tracked` を更新するので 2 回目は無変化 no-op になる。(ii) は新規 API も FacialController 側の配線も不要で、Aggregator が source 単位 1 回評価に変わっても挙動が変わらない。非冪等な `TryWriteValues`（2 回目が異なる値）では (ii) は 2 回目も publish するが、現行 6 実装はすべてフレーム内冪等（基準捕捉の契約前提として既に design.md に記載）であり、非冪等ケースは既知制限 6 として「後者で統一される」と書き直せば整合する。(i) は「1 回目勝ち」になり再生側は前者で統一される。どちらもライブ（スロットごとに別値）とは一致しないため、仕組みが単純な (ii) を採用
  - Replace 後の同一性は既存 `RecAnalogInjector` の規則（id がキー、`ReferenceEquals` が復元ガード）で既に成立しており、`RecRegistryInjection` へ共通化して VP にも適用する。sampler 側は `Tracked.Instance` に参照を持ち、別参照が来たら `HasSample = false` にして全量 publish する（既に design.md にあった「Replace 直後に同 id で別インスタンスが来たらリセット」の実現手段を明示）
  - 記録内の id 一意性: 生成経路（`CaptureBaseline` の `RegisteredIds` 走査、`RecTimelineSeek` の id 辞書、`TryRead` の sourceIdx 辞書）はいずれも構造的に重複を生まないが、契約として `RecBaselineState` 構築時（VP エントリ）と `TryRead`（同一 sourceIdx の kind 8）で拒否する。既存の kind 6 後勝ち畳み込みは変更しない（既存挙動維持）
- **Selected**: キー設計は変更せず、design.md に「Architecture → 入力源識別スコープ（id 一意性契約）」を新設して 4 点（registry の後勝ち上書き実挙動 / per-FC スコープ / フレーム内 dedupe 規則 (ii) / 参照ガード）を固定。テスト: 既存 `InputSourceRegistryTests` 3 件を前提テストとして参照、`ValueProviderObservationSamplerTests.Sample_SameSourceBoundToTwoLayers_PublishesOnce` / `Sample_SameIdDifferentInstance_RepublishesFullState` / `Sample_SameSourceTwoLayersNonIdempotentValues_PublishesBoth`、`LayerInputSourceAggregatorTests.Aggregate_SameInstanceInTwoLayers_ObserverCalledOncePerSlot`、新設 `RecBaselineStateTests.Constructor_DuplicateValueProviderSourceId_ThrowsArgumentException` / `TryGetValueProviderEntry_KnownId_ReturnsSingleEntry`、新設 `RecIdTableTests.AddDefinedId_SameSourceIdAtDifferentIndex_ThrowsInvalidOperationException`、`RecBinaryFormatTests.TryRead_DuplicateBaselineValueProviderForSameSource_ReturnsError`、`RecValueProviderInjectorTests.EndInjection_WhenCurrentEntryIsNoLongerOwned_LogsWarningAndPreservesCurrentSource`
- **Implications**: Traceability 2.5–2.6 / 3.1 / 5.1 / 8.6 を更新、Revalidation Triggers に registry の重複挙動と Aggregator の評価粒度を追加、既知制限 6 を (ii) の挙動に書き直し、`RecBaselineState` に `TryGetValueProviderEntry` と 4 引数コンストラクタ（重複拒否）を追加

## Critical 2（3 回目）: 4 ポート `BeginInjection` にトランザクション性がない

- **Context**: 現行 `PlaybackUseCase.StartPlayback`（`PlaybackUseCase.cs` L95-99）は `_triggerPort.BeginInjection(baseline); _analogPort.BeginInjection(baseline); _scheduler.Load(...)` を void 戻りで順に呼ぶ。4 ポート化で `RecExpressionInjector`（gate 解決）/ `RecValueProviderInjector`（BlendShapeCount 解決）が「ポート全体として失敗」し得るため、途中失敗で部分的排他が残る経路（Req 3.7 / 4.4 違反）を塞ぐ必要がある
- **Sources Consulted**: `PlaybackUseCase.cs`（`State` 遷移、`StopPlayback` は `Idle` 以外で T → A に `EndInjection`、`Load` 冒頭で `StopPlayback`）、`ITriggerInjectionPort.cs` / `IAnalogInjectionPort.cs`（`void BeginInjection` / `Inject*` / `void EndInjection`）、`RecTriggerInjector.cs`（`BeginInjection` 冒頭で `EndInjection()`、per-id `WarnMissingSourceOnce`、`_isInjecting` フラグ）、`RecAnalogInjector.cs`（同様、per-id warn-once）、`RecPlaybackState.cs`（`Idle / Playing / Completed`）、`RecCharacterBinding.cs` L467-493（`EnsurePlaybackSession` が `new PlaybackUseCase(_triggerInjector, _analogInjector)`）、`PlaybackUseCaseTests.cs`（`[SmallTest]`。`FakeTriggerInjectionPort` / `FakeAnalogInjectionPort` が `BeginInjectionCallCount` / `EndInjectionCallCount` と共有呼出ログを持つ。既存テスト `StartPlayback_WhenLoaded_EstablishesTriggerThenAnalogExclusivityBeforeFiringEvents` / `StopPlayback_WhenPlaying_ReleasesTriggerThenAnalogExclusivity` / `Tick_WhenPlaybackCompletesNaturally_DoesNotReleaseExclusivityUntilStopPlayback` / `StartPlayback_WhenTimelineCompletesImmediately_DoesNotReleaseExclusivity`）、`RecGcZeroGateTests.cs`（`NullTriggerInjectionPort` / `NullAnalogInjectionPort` Fake）
- **Alternatives（ポート契約の形状）**:
  - (a) 既存名のまま戻り値だけ `bool BeginInjection(RecBaselineState)` に変える: 最小差分だが preflight（副作用なしの事前検査）を表現できず、「false なら副作用なし」の契約が名前から読めない
  - (b) 各ポートに `bool TryBeginInjection(RecBaselineState)` + `bool CanBeginInjection(out string reason)` + `void EndInjection()` を持つ共通基底 `IInjectionPort` を置き、4 ポート interface がそれを継承する: `PlaybackUseCase` が 4 ポートを `IInjectionPort[]` の同一ループで preflight / 確立 / ロールバックできる。既存 2 interface のシグネチャ変更（破壊的、preview 許容。実装 2 + Fake 4 が同一リポジトリ内）
  - (c) `PlaybackUseCase` 側で try/catch による例外ベースのロールバック: 現行 Injector は失敗を例外で表さない（warn-once + skip）ため成立しない
  - → (b) を採用。`CanBeginInjection` を分けるのは、preflight を「どのポートの `TryBeginInjection` も呼ばない」で成立させるため（確立してから失敗を知る方式では、1 番目のポートの再入吸収による副作用が既に発生している）
- **Alternatives（ロールバック順）**: 確立済みポートの逆順（LIFO。V → A → E → T の部分列）を採用。`StopPlayback` の T → E → A → V 同順解放（rec-playback-input-exclusivity の判断）は変更しない。4 ポートの解放は互いに依存しない同期処理（ライブフレームを挟まない）なので両者は意味上同値で、操作の違い（完成した集合の解放 vs 途中まで積んだスタックの巻き戻し）として併存させる
- **Alternatives（`Completed` からの再開）**: 従来は各ポートの `BeginInjection` 冒頭の再入吸収に依存していたが、途中失敗時に未到達ポートが前セッションの排他を保持し続ける（ロールバックは確立済みポートしか触らない）。確立前に全ポートを T → E → A → V で解放して `Idle` を経由する方式にすれば、確立は常に「どのポートも確立されていない状態」から始まり、ロールバック後の不変条件（`Idle` ⇒ 全ポート未確立）が成立する。同期処理のためライブフレームの隙間は生じない。preflight 不合格のときは解放も行わず `Completed` と前セッションの排他をそのまま残す（「副作用なし」を優先。利用者は `StopPlayback` で解放できる）
- **占有を preflight 条件にしない理由**: レビューは「他 `IInjectedInputSource` 所有者との占有衝突が無いこと」を preflight に含めることを提案したが、占有規則は id 単位の先着優先（Warning + スキップ）であり、Timeline gaze 注入（`FacialTimelineReceiver`）との共存は design.md「Out of Boundary」で「優先順位変更なし」と固定済み。占有を開始拒否に格上げすると Timeline と REC の同時使用が不可能になり既存受け入れテスト（`AnalogInjector_BeginInjection_WhenAnotherInjectedSourceAlreadyOccupiesId_LogsWarningAndSkips`）とも矛盾する。よって preflight は依存解決可否（gate / registry / `BlendShapeCount`）のみを検査し、占有は従来どおり per-id 規則に委ねる旨を明記した
- **Selected**: (b) + 逆順ロールバック + `Completed` 再開時の全解放経由。Error Handling の「gate 未解決は warn-once + no-op」を廃止し preflight 拒否へ置換（黙った no-op は系1 が遮断されないまま再生を始める）。`RecValueProviderInjector` の第 2 引数を `Func<int>` にして preflight が呼出時点の `BlendShapeCount` を見られるようにした
- **Implications**: `IInjectionPort.cs` 新設、`ITriggerInjectionPort` / `IAnalogInjectionPort` 変更、`RecTriggerInjector` / `RecAnalogInjector` 追随（挙動不変、`TryBeginInjection` は常に true）、`PlaybackUseCase` に State 不変条件（`State != Idle` ⇔ 4 ポート全確立）を明記。テスト: `StartPlayback_PreflightFails_NoPortBegun` / `StartPlayback_PreflightFails_LogsAllFailingPortsInSingleError` / `StartPlayback_ThirdPortFails_RollsBackFirstTwoInReverseOrder` / `StartPlayback_FourthPortFails_RollsBackThreeInReverseOrder` / `StartPlayback_AllPortsSucceed_StateIsPlaying` / `StartPlayback_FromCompleted_ReleasesAllPortsBeforeReestablishing` / `StartPlayback_FromCompletedPreflightFails_KeepsStateAndExclusivity`、既存 2 ポート経路の回帰として `StartPlayback_WhenLoaded_EstablishesTriggerThenAnalogExclusivityBeforeFiringEvents` / `StopPlayback_WhenPlaying_ReleasesTriggerThenAnalogExclusivity` を E / V Fake 常時成功で維持。Injector 側は `CanBeginInjection_*` / `TryBeginInjection_*` を追加（既存 `BeginInjection_*` は改名）。Traceability 3.7 / 4.4–4.5 / 5.4 / 9.3 を更新
