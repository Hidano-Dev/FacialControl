# Implementation Plan

## 全タスク共通の前提

- 対象 Unity プロジェクトは `FacialControl/`（リポジトリ直下の単一 Unity プロジェクト）。各タスクに改修パッケージと asmdef を明記する
- TDD（Red → Green → Refactor）: 各タスクは「記載したテストを先に書いて赤を確認 → 実装 → EditMode（PlayMode 指定のあるものは PlayMode）テストを実行して緑」の順で完了させる。新設 fixture には `[SmallTest]` / `[MediumTest]` を 1 つ付け `SizedTestFixture` を継承する。Small ではファイル I/O・AssetDatabase・シーン・MonoBehaviour ライフサイクル・`Time.*` を使わない（`docs/testing.md`）
- テスト実行は CLAUDE.md の手順（`D:/UnityEditors/6000.3.19f1/Editor/Unity.exe` の batchmode、`-runTests` と `-quit` は併用しない）に従い、結果は XML で判定する
- 既存コードパスは観測者ゼロ・遮断/注入未使用時に挙動不変（Req 8.1 / 8.2）。各タスクで既存テストスイートを緑のまま維持する。pre-existing の赤は本 spec の FAIL に巻き込まない
- design.md の「入力源分類表」「再生開始のトランザクション」「入力源識別スコープ」を契約として扱い、タスク内で再解釈しない

---

- [ ] 1. core 値提供型観測面（Aggregator フック拡張 → 配信契約 → サンプラー → FacialController 配線）
- [x] 1.1 消費点フックに消費元入力源インスタンスを渡せるようにする
  - 対象: `FacialControl/` の `com.hidano.facialcontrol`（`Hidano.FacialControl.Domain`）、追随のみ `com.hidano.facialcontrol.timeline`（`Hidano.FacialControl.Timeline.Editor`）
  - レイヤー入力源観測者の契約に「消費元入力源」引数を 1 個追加し、Aggregator が `TryWriteValues` 直後・レイヤー mask 合成前の既存フック位置で渡す（observer が null のとき追加コストなし）
  - 既存実装 2 件（timeline Editor のベイクシミュレーション用 observer、core Small テストの observer）をシグネチャ追随（新引数は無視）してコンパイルを維持する
  - テスト（core `Tests/Small/Domain/LayerInputSourceAggregatorTests` 追記、`[SmallTest]`）: `Aggregate_Observer登録_source引数に消費元インスタンスが渡される`、`Aggregate_ContributeMaskがTryWriteValues内で変化_フック時点で新maskが読める`、`Aggregate_SameInstanceInTwoLayers_ObserverCalledOncePerSlot`
  - 完了条件: 上記 3 テストと既存 Aggregator テスト・timeline Editor テストが EditMode で緑
  - _Requirements: 2.1, 2.3, 2.4, 8.7_

- [x] 1.2 値提供型サンプルと系1 イベントの観測配信契約を追加する
  - 対象: `com.hidano.facialcontrol`（`Hidano.FacialControl.Domain`）。コンパイル追随のみ `com.hidano.facialcontrol.rec`（`Hidano.FacialControl.Rec.Application` の記録ユースケースとテスト Fake を空実装で追随。本実装は 4.5）
  - 値提供型 1 件のフレーム消費粒度サンプル（有効性・有効性変化・値変化・mask 変化・値 span・mask。コール中のみ有効）を Domain に定義する
  - 系1 観測契約（アクティブ化 / 非アクティブ化の通知）と予約識別子 `@expression`（registry の id 許容文字集合と交わらない）を Domain Interfaces に定義する
  - 観測者契約に値提供型サンプル・系1 アクティブ化・系1 非アクティブ化の 3 通知を追加し、観測バス契約を系1 観測契約の継承 + 値提供型サンプル publish へ拡張する。バス実装は既存規則（`HasObservers` 早期 return・publish 中の購読変更の遅延適用・観測者例外の `Debug.LogException` 隔離）を新 3 メソッドにも適用する
  - テスト（core `Tests/Small/Domain/Services/FacialInputObservationBusTests` 追記、`[SmallTest]`）: 新 3 メソッドそれぞれについて、観測者ゼロで早期 return すること、publish 中の Subscribe / Unsubscribe が遅延適用されること、観測者の例外が他の観測者へ波及しないこと
  - 完了条件: 上記テストが緑、rec を含む全アセンブリがコンパイルでき既存 EditMode テストが緑
  - _Requirements: 2.1, 2.2, 2.3, 2.5, 4.1, 4.2, 8.3_

- [x] 1.3 値提供型観測サンプラーを実装する
  - 対象: `com.hidano.facialcontrol`（`Hidano.FacialControl.Adapters`、InputSources 配下）
  - Aggregator フックを受け、値提供型基底の派生だけを選別し（系1 消費アダプタ・トリガー型・アナログラッパは即 return）、入力源 id 単位の状態（最後に観測したインスタンス参照・値・mask・有効性・サンプル有無）と比較して変化時のみバスへ publish する。観測者ゼロなら即 return
  - 変化判定: 有効時は値の float ビット一致と mask のビット一致（長さ不一致は変化）、無効時は有効性のみ、無効→有効の復帰時は最後に publish した値・mask と比較、mask 変化時は値変化を強制 true。同 id で別インスタンスが来たら状態を破棄して全量 publish。同一インスタンスの複数スロット通知は最終 publish 状態との比較でフレーム内 dedupe
  - `Reset` で全 id の前回状態を破棄する。定常フレームで alloc ゼロ（新 id 追加時のみ確保）。span は Aggregator の scratch をコピーせず渡し、書き戻さない
  - テスト（core `Tests/EditMode/Adapters/InputSources/ValueProviderObservationSamplerTests` 新設、`[SmallTest]`、Fake 値提供型 + 実バス）: 値提供型以外の無視、値ビット変化のみ publish、有効性変化、mask 変化時の値強制、無効期間の畳み込み、`Sample_SameIdDifferentInstance_RepublishesFullState`、`Sample_SameSourceBoundToTwoLayers_PublishesOnce`、`Sample_SameSourceTwoLayersNonIdempotentValues_PublishesBoth`
  - 完了条件: 上記テストが EditMode で緑
  - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5, 2.6, 2.7, 2.8, 8.5, 8.6, 8.7_

- [ ] 1.4 FacialController にサンプラーを配線し、観測者ゼロ時の不変性を保つ
  - 対象: `com.hidano.facialcontrol`（`Hidano.FacialControl.Application` の LayerUseCase、`Hidano.FacialControl.Adapters` の FacialController）
  - LayerUseCase に観測者設定の委譲を追加し、プロファイル再設定で Aggregator を再構築したときも再適用する
  - FacialController は初期化時にサンプラーを構築し、`LateUpdate` 冒頭でバスの観測者有無のエッジを検出して観測者を着脱（着時に `Reset`）する。観測者ゼロ時は Aggregator フックが null のまま（追加コストは bool 読取 + 比較 1 回）。BlendShape 総数を公開する
  - テスト: core `Tests/EditMode/Application/LayerUseCaseTests` 追記（`[SmallTest]`）に、設定した観測者が Aggregator のコールバックを受け、プロファイル再設定後も受け続けることを検証。PlayMode `FacialControllerGcZeroGateTests`（`[MediumTest]`）が観測者ゼロで継続緑であることを確認
  - 完了条件: 上記 Small テストが緑、`FacialControllerGcZeroGateTests` が PlayMode で緑
  - _Requirements: 2.1, 2.4, 8.1, 8.2_

- [ ] 2. core 系1 経路（観測・遮断・注入・遷移なし基準確立）
- [ ] 2.1 (P) 表情ユースケースに系1 の観測・遮断・注入・基準確立の面を追加する
  - 対象: `com.hidano.facialcontrol`（`Hidano.FacialControl.Domain` の遮断契約、`Hidano.FacialControl.Application` の ExpressionUseCase）
  - 系1 遮断契約（遮断中フラグ・冪等な Suspend / Resume・ゲート迂回の Inject アクティブ化 / 非アクティブ化・観測者非通知の基準確立・レイヤー宣言順 × 内部順の id 収集・基準確立世代カウンタ）を Domain に定義し、ExpressionUseCase が実装する
  - 本体を private コアへ純リファクタし、ライブ面（ゲート適用・通知）/ 注入面（ゲート迂回・通知）/ 基準確立（通知なし・世代++）の 3 面から呼ぶ。遮断中のライブ呼出は null 検証後に無視・集合不変・非通知・ログなし。アクティブ化は常に通知、非アクティブ化は 1 件以上除去時のみ通知。観測者の着脱 API を追加。プロファイル再設定はゲート状態と観測者を維持し世代を進める
  - テスト（core `Tests/EditMode/Application/ExpressionUseCaseTests` 追記、`[SmallTest]`）: `Activate_観測者登録_予約idで通知`、`Deactivate_非アクティブid_通知なし`、`SuspendActivation_遮断中Activate_集合不変かつ非通知`、`InjectActivate_LastWinsレイヤー_ライブと同一結果`、`ResetActiveExpressions_Blendレイヤー順序_順序維持かつ非通知かつGeneration増加`、`CollectActiveExpressionIds_複数レイヤー_宣言順×内部順`
  - 完了条件: 上記 6 テストと既存 ExpressionUseCase テストが EditMode で緑
  - _Requirements: 4.1, 4.2, 4.4, 4.5, 4.6, 4.7, 4.9, 5.2, 5.6, 8.1, 8.2, 8.8_
  - _Boundary: ExpressionUseCase, IExpressionActivationGate_
  - _Depends: 1.2_

- [ ] 2.2 レイヤーユースケースが基準確立世代の変化を検出して遷移なしでスナップする
  - 対象: `com.hidano.facialcontrol`（`Hidano.FacialControl.Application` の LayerUseCase と内部の系1 消費アダプタ）
  - `UpdateWeights` 冒頭で世代カウンタを int 比較し、変化したフレームだけ通常の表情更新（必ず遷移開始）を「スナップ」に置き換える（現在値 = 目標値、遷移完了、mask を現在値から再構築、既にアクティブ経験のあるレイヤーが空列なら 0 へスナップ）
  - 既存の表情更新ロジック本体は変更しない
  - テスト（core `Tests/EditMode/Application/LayerUseCaseTests` 追記、`[SmallTest]`）: `UpdateWeights_ResetGeneration変化_遷移を経ず即target値`、`UpdateWeights_Reset後の空集合_HasBeenActiveなレイヤーが0へスナップ`
  - 完了条件: 上記 2 テストと既存 LayerUseCase テストが EditMode で緑
  - _Requirements: 4.7, 5.4, 8.1_

- [ ] 2.3 FacialController に系1 の観測者配線と遮断面の公開を追加する
  - 対象: `com.hidano.facialcontrol`（`Hidano.FacialControl.Adapters` の FacialController）
  - 初期化時に観測バスを系1 観測者として表情ユースケースへ配線し、系1 遮断面を公開プロパティで rec に見せる（未初期化時は null）。`Activate` / `Deactivate` 本体は不変
  - テスト: 既存の FacialController を対象とする Medium fixture（EditMode または PlayMode）に、初期化後に遮断面が非 null であること、`Activate` 呼出がバスの観測者へ予約 id `@expression` と表情 id で届くことを追記
  - 完了条件: 上記テストが緑、`FacialControllerGcZeroGateTests` が継続緑
  - _Requirements: 4.1, 4.2, 8.2_

- [ ] 3. rec Domain（レコードモデル → 基準状態 → タイムライン / キュー → バイナリ形式 → スケジューラ / 検証 / 途中再生）
- [ ] 3.1 (P) 新レコード種別とイベントモデル・フラグ定義を追加する
  - 対象: `com.hidano.facialcontrol.rec`（`Hidano.FacialControl.Rec.Domain`、Models 配下）
  - レコード種別に 7 ValueProviderSample / 8 BaselineValueProvider / 9 ExpressionActivate / 10 ExpressionDeactivate / 11 BaselineExpression を追加。値提供型フラグ（IsValid / HasMask / HasValues）とヘッダフラグ（FullInputBaseline = 0x0001）を定義する
  - イベント構造体に u16 値数・u16 mask バイト数・値提供型フラグ・float ペイロード長・時刻付き判定（2/3/4/7/9/10）と新 5 種の factory を追加する（既存 kind のレイアウトと u8 軸数は不変）
  - テスト（rec `Tests/EditMode/RecDomainContractsTests` 追記、`[SmallTest]`）: 各 factory が kind・index・フラグ・count を正しく保持すること、float ペイロード長が kind と HasValues に従うこと、時刻付き判定が新 kind を含むこと
  - 完了条件: 上記テストが EditMode で緑
  - _Requirements: 6.1, 6.3, 6.4_
  - _Boundary: RecEvent, RecEventKind, RecValueProviderFlags, RecHeaderFlags_

- [ ] 3.2 (P) 基準状態に値提供型エントリと系1 エントリを追加し、記録内 id 一意性を契約化する
  - 対象: `com.hidano.facialcontrol.rec`（`Hidano.FacialControl.Rec.Domain`）
  - 基準状態に値提供型エントリ列（入力源 id・有効性・LSB-first mask バイト列・mask 順の値）と系1 の順序付き表情 id 列を追加する 4 引数コンストラクタと、id から高々 1 件を返す参照 API を追加。値提供型エントリの同一 id 重複は `ArgumentException` で拒否（既存トリガー / アナログの後勝ち畳み込みは不変）
  - 既存 id 表の「同一 id を別 index へ定義すると `InvalidOperationException`」挙動をテストで固定する（実装変更なし）
  - テスト（rec `Tests/EditMode/RecBaselineStateTests` 新設・`RecIdTableTests` 新設、`[SmallTest]`）: `Constructor_DuplicateValueProviderSourceId_ThrowsArgumentException`、`TryGetValueProviderEntry_KnownId_ReturnsSingleEntry`、`AddDefinedId_SameSourceIdAtDifferentIndex_ThrowsInvalidOperationException`
  - 完了条件: 上記 3 テストが EditMode で緑、既存の基準状態利用箇所が旧 2 引数相当の呼出でも動作
  - _Requirements: 5.1, 5.2, 8.6_
  - _Boundary: RecBaselineState, RecIdTable_

- [ ] 3.3 タイムラインモデルを可変長ペイロード（float + mask バイト列）対応にし、シンク契約に mask を通す
  - 対象: `com.hidano.facialcontrol.rec`（`Hidano.FacialControl.Rec.Domain` の RecTimeline と IRecEventSink。実装側 RecStreamWriter / RecordingUseCase はシグネチャ追随のみ、本実装は 4.5 / 4.6）
  - イベント別の float ペイロードを汎用化し mask バイト列を併置、0-alloc で span を返す。検証: kind 7 の HasValues なら payload 長 = 値数、HasMask なら mask 長 = mask バイト数、系1 kind の入力源 / 表情 index が id 表内
  - イベントシンク契約の追記メソッドに mask バイト列 span を追加する
  - テスト（既存 rec Domain fixture へ追記、`[SmallTest]`）: 上記検証の正例 / 負例、span 取得が kind ごとに正しい長さを返すこと
  - 完了条件: 上記テストが緑、全アセンブリがコンパイルできる
  - _Requirements: 6.1, 6.2_

- [ ] 3.4 イベントチャンクキューに byte 区画と容量方針・単一レコード超過時の専用セグメントを追加する
  - 対象: `com.hidano.facialcontrol.rec`（`Hidano.FacialControl.Rec.Domain` の RecEventChunkQueue）
  - コンストラクタに float 容量 / byte 容量を取り、Enqueue / TryDequeue が float と byte の両ペイロードを運ぶ。SPSC の公開点は既存の公開カウンタ 1 箇所に保ち、両ペイロードの書込を公開前に完了させる
  - 次セグメントでも収まらないレコードはそのレコード専用サイズのセグメントを確保して格納し（成長カウント加算）、例外を投げない
  - テスト（rec `Tests/EditMode/RecEventChunkQueueTests` 追記、`[SmallTest]`）: byte ペイロードの FIFO 順保持、単一レコードが容量超過でも例外なしで取り出せ成長カウントが加算されること、定常（容量内）では成長カウントが増えないこと
  - 完了条件: 上記テストと既存キューテストが EditMode で緑
  - _Requirements: 6.7, 8.5_

- [ ] 3.5 バイナリ形式に kind 7〜11 の書込・読込と基準先行不変条件の拡張を実装する
  - 対象: `com.hidano.facialcontrol.rec`（`Hidano.FacialControl.Rec.Domain` の RecBinaryFormat）
  - kind 7（f64 t・u16 sourceIdx・u8 flags・条件付き mask / 値）、kind 8（常に mask + 値）、kind 9 / 10（f64 t・u16 sourceIdx・u16 expressionIdx）、kind 11（u16 sourceIdx・u16 expressionIdx、出現順 = アクティブ化順）を既存レイアウトの隣に追記型で実装。値は mask の立った index 昇順
  - 基準レコード（5 / 6 / 8 / 11）が最初の時刻付きレコード（2 / 3 / 4 / 7 / 9 / 10）より前であることを読込で検査。最大レコードサイズ算出を値数・mask バイト数込みに拡張。未知 kind は従来どおりエラー（スキップしない）
  - テスト（rec `Tests/EditMode/RecBinaryFormatTests` 追記、`[SmallTest]`）: kind 7〜11 のラウンドトリップ（flags の全組合せ、値数 > 255、mask バイト列、順序・タイムスタンプ・float ビット一致）、基準先行違反の読込エラー、`Serialize_EmptyValueProviderAndExpressionBaseline_RoundTrips`
  - 完了条件: 上記テストと既存バイナリ形式テストが EditMode で緑
  - _Requirements: 5.3, 6.1, 6.2, 6.3, 6.6_

- [ ] 3.6 ヘッダ必須ビットで旧構造ファイルを確定的に拒否し、同一入力源の基準重複を拒否する
  - 対象: `com.hidano.facialcontrol.rec`（`Hidano.FacialControl.Rec.Domain` の RecBinaryFormat）
  - 書込ヘッダの flags 既定値を必須ビット（FullInputBaseline）に変更し、読込では magic / formatVersion の直後・レコード走査の前に必須ビットの欠落を検査して design.md 記載のエラー文言で false を返す（予約 bit1〜15 は検証しない。formatVersion は 1 のまま）
  - 同一 sourceIdx の kind 8 が 2 件以上なら読込エラー（既存 kind 6 の後勝ち畳み込みは不変）
  - テスト（rec `Tests/EditMode/RecBinaryFormatTests` 追記、`[SmallTest]`）: `WriteHeader_Always_SetsFullInputBaselineFlag`、`TryRead_HeaderWithoutFullInputBaselineFlag_ReturnsError`、`TryRead_PreCoverageFileWithKinds1To6Only_ReturnsError`、`TryRead_DuplicateBaselineValueProviderForSameSource_ReturnsError`
  - 完了条件: 上記 4 テストが緑。既存テストで flags = 0 を前提にしていたものは新契約に合わせて更新済み
  - _Requirements: 5.1, 5.4, 6.4, 6.5_

- [ ] 3.7 再生スケジューラの訪問者契約と記録検証を新 kind へ拡張する
  - 対象: `com.hidano.facialcontrol.rec`（`Hidano.FacialControl.Rec.Domain` の RecPlaybackScheduler / IRecEventVisitor / RecValidation。実装側 PlaybackUseCase は空実装でコンパイル追随、本実装は 4.4）
  - 訪問者契約に値提供型サンプル（有効性・mask バイト列・値）・系1 アクティブ化・系1 非アクティブ化の 3 メソッドを追加し、Dispatch が kind 7 / 9 / 10 を対応メソッドへ振り分ける（未知 kind の例外は防御として維持）
  - 記録検証の欠落表情 id 検出を系1 イベントと基準の表情 id 列にも適用する
  - テスト（rec `Tests/EditMode/RecPlaybackSchedulerTests` 追記、RecValidation を検証する既存 fixture へ追記、`[SmallTest]`）: kind 7 / 9 / 10 が時刻到達で対応メソッドへ正しい引数で届くこと、系1 イベント / 基準の欠落 id が検出されること
  - 完了条件: 上記テストと既存スケジューラテストが EditMode で緑
  - _Requirements: 4.8, 4.10_

- [ ] 3.8 途中再生のベースライン畳み込みを値提供型と系1 へ拡張する
  - 対象: `com.hidano.facialcontrol.rec`（`Hidano.FacialControl.Rec.Domain` の RecTimelineSeek）
  - ベースライン構築にプロファイルを渡し、値提供型は id ごとに (有効性・mask・値) を kind 7 の flags に従って上書き（HasMask なら mask と値を置換、HasValues のみなら値を置換、IsValid は常に反映）、系1 はレイヤー排他意味論（LastWins は Clear して追加、Blend は同 id 除去後に追加、Deactivate は全レイヤーから除去、未知 id はスキップ）で畳み、レイヤー宣言順 × リスト順で平坦化する。トリガー / アナログの畳み込みは不変
  - テスト（rec `Tests/EditMode/RecTimelineSeekTests` 追記、`[SmallTest]`）: 値提供型の最後の状態畳み込み（HasMask / HasValues の組合せ）、系1 の LastWins / Blend 畳み込み（A → B（LastWins）→ B off = 空 のケースを含む）、オフセット以降のイベントが基準に入らないこと
  - 完了条件: 上記テストと既存 Seek テストが EditMode で緑
  - _Requirements: 6.8_

- [ ] 4. rec Application / Adapters（注入ポート統一 → 新注入体 → 再生 / 記録ユースケース → 永続化 → キャラクターバインディング）
- [ ] 4.1 (P) 注入ポート契約を共通ライフサイクル（副作用なし事前検査・bool 戻りの確立・冪等解放）に統一する
  - 対象: `com.hidano.facialcontrol.rec`（`Hidano.FacialControl.Rec.Domain` の Interfaces、`Hidano.FacialControl.Rec.Adapters` の既存 2 Injector）
  - 共通基底ポート契約（`CanBeginInjection(out reason)` / `TryBeginInjection(baseline)` / `EndInjection`）を新設し、既存トリガー / アナログポート契約の `void BeginInjection` を廃止して継承させる。値提供型ポート契約（状態注入）と系1 ポート契約（アクティブ化 / 非アクティブ化注入）も定義する
  - 既存 2 Injector を追随（トリガーは列挙デリゲートの有無、アナログは常に true を事前検査で返し、確立は従来本体のまま true を返す。挙動不変）。Replace 系注入の共通処理（複合 id 分解・Register / Replace / Unregister・占有検査・参照同一性復元・warn-once）を internal ヘルパーへ抽出しアナログ Injector が委譲する。再生ユースケースと Fake（PlaybackUseCaseTests / RecGcZeroGateTests 内）は新契約にコンパイル追随
  - テスト（rec `Tests/EditMode/RecTriggerInjectorTests` / `RecAnalogInjectorTests` 追記、`[SmallTest]`）: `CanBeginInjection_DependenciesAvailable_ReturnsTrue`、既存 `BeginInjection_*` を `TryBeginInjection_*` へ改名し戻り値 true を assert（挙動不変の固定）
  - 完了条件: 上記テストと既存 Injector / PlaybackUseCase / GC ゲートのテストが緑（全アセンブリがコンパイルできる）
  - _Requirements: 3.6, 3.7, 3.8, 8.8_
  - _Boundary: IInjectionPort, RecTriggerInjector, RecAnalogInjector, RecRegistryInjection_

- [ ] 4.2 値提供型の注入体と注入ポート実装を追加する
  - 対象: `com.hidano.facialcontrol.rec`（`Hidano.FacialControl.Rec.Adapters`、Playback 配下）
  - 注入体: 値提供型基底を継承し注入ソースマーカーを実装。事前確保した mask（参照不変で in-place 更新）と dense 値バッファを持ち、状態適用（mask バイト長 = ceil(N/8)、値数 = popcount の検証、不一致は false）と `TryWriteValues`（無効なら false、有効なら mask の立った index のみ書く）を alloc ゼロで行う
  - 注入ポート: 事前検査は BlendShape 総数デリゲートが正の値を返すこと（副作用なし・design.md の reason 文言）。確立は解放 → 総数再解決（0 以下なら false・副作用なし）→ 基準の値提供型エントリを seed 付きで装着（mask 長不一致は warn-once + 無効 seed）→ registry の未装着・値提供型派生・非注入ソースを無効 seed で装着 → true。状態注入は装着済み id のみ。解放は共通ヘルパーで参照同一性ガード付き復元 / 除去（冪等）
  - テスト（rec `Tests/EditMode/RecPlaybackValueProviderSourceTests` / `RecValueProviderInjectorTests` 新設、`[SmallTest]`、Fake registry）: 状態適用の長さ検証、mask 順 scatter、基準 seed と無効 seed、他者占有のスキップ、`CanBeginInjection_BlendShapeCountZero_ReturnsFalseWithReason`、`TryBeginInjection_BlendShapeCountZero_ReturnsFalseWithoutTouchingRegistry`、`EndInjection_WhenCurrentEntryIsNoLongerOwned_LogsWarningAndPreservesCurrentSource`
  - 完了条件: 上記テストが EditMode で緑
  - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5, 3.8, 5.5, 8.5, 8.8_

- [ ] 4.3 (P) 系1 の注入ポート実装を追加する
  - 対象: `com.hidano.facialcontrol.rec`（`Hidano.FacialControl.Rec.Adapters`、Playback 配下）
  - 事前検査は遮断面デリゲートが非 null を返すこと（副作用なし・design.md の reason 文言）。確立は解放 → 遮断面解決（null なら false・副作用なし）→ 保持 → Suspend → 基準の表情 id 列（無ければ空）で遷移なし基準確立 → true。注入は保持した遮断面へ委譲し、未知 id の false は id 単位 warn-once。解放は Resume のみ（アクティブ集合は維持、冪等）。「遮断面未解決なら warn-once + no-op」は採用しない
  - テスト（rec `Tests/EditMode/RecExpressionInjectorTests` 新設、`[SmallTest]`、Fake 遮断面）: `CanBeginInjection_GateUnresolved_ReturnsFalseWithReason`、`TryBeginInjection_GateUnresolved_ReturnsFalseWithoutSuspending`、`TryBeginInjection_GateResolved_SuspendsThenResetsAndReturnsTrue`、Resume 後に集合が維持されること、未知 id の注入が 1 回だけ警告されること
  - 完了条件: 上記テストが EditMode で緑
  - _Requirements: 4.4, 4.5, 4.6, 4.7, 4.8, 4.9, 5.5, 8.8_
  - _Boundary: RecExpressionInjector_
  - _Depends: 4.1, 2.1_

- [ ] 4.4 再生ユースケースを 4 ポートの all-or-nothing 確立へ拡張する
  - 対象: `com.hidano.facialcontrol.rec`（`Hidano.FacialControl.Rec.Application` の PlaybackUseCase）
  - コンストラクタで 4 ポート（trigger / expression / analog / valueProvider。null は `ArgumentNullException`）を受け、確立順の配列として保持し、事前検査・確立・ロールバック・解放を同一ループで行う
  - 再生開始: 既存ガード → 全ポートの事前検査を短絡せず全件評価し 1 件でも不合格なら `LogError` 1 回（失敗ポート名と reason を連結）+ false・状態とポート不変 → Completed からの再開は全ポートを T→E→A→V で解放して Idle 経由 → T→E→A→V で確立し i 番目が false なら確立済みを逆順解放・scheduler Reset・Idle・`LogError` 1 回 + false → 成功で scheduler Load → Playing / Completed。停止は Idle 以外で T→E→A→V に解放（唯一の解放点、Completed では解放しない）
  - 新訪問メソッド（値提供型状態 → 値提供型ポート、系1 → 系1 ポート）を実装し、欠落表情 id フィルタを系1 イベントと基準の表情 id 列にも適用。Load で受けたプロファイルを途中再生の畳み込みに渡す
  - テスト（rec `Tests/EditMode/PlaybackUseCaseTests` 追記、`[SmallTest]`。Fake 4 ポートは `CanBeginInjection` / `TryBeginInjection` の戻り値を設定でき呼出順を共有ログに記録する）: `StartPlayback_WhenLoaded_EstablishesExclusivityInTriggerExpressionAnalogValueProviderOrder`、`StopPlayback_WhenPlaying_ReleasesExclusivityInTriggerExpressionAnalogValueProviderOrder`、`StartPlayback_PreflightFails_NoPortBegun`、`StartPlayback_PreflightFails_LogsAllFailingPortsInSingleError`、`StartPlayback_ThirdPortFails_RollsBackFirstTwoInReverseOrder`、`StartPlayback_FourthPortFails_RollsBackThreeInReverseOrder`、`StartPlayback_AllPortsSucceed_StateIsPlaying`、`StartPlayback_FromCompleted_ReleasesAllPortsBeforeReestablishing`、`StartPlayback_FromCompletedPreflightFails_KeepsStateAndExclusivity`、系1 欠落 id のスキップ、既存 `Tick_WhenPlaybackCompletesNaturally_DoesNotReleaseExclusivityUntilStopPlayback` / `StartPlayback_WhenTimelineCompletesImmediately_DoesNotReleaseExclusivity` の 4 ポート化、既存 `StartPlayback_WhenLoaded_EstablishesTriggerThenAnalogExclusivityBeforeFiringEvents` / `StopPlayback_WhenPlaying_ReleasesTriggerThenAnalogExclusivity` を E / V Fake 常時成功で維持
  - 完了条件: 上記テストが EditMode で緑。状態不変条件（Idle 以外 ⇔ 4 ポート全確立）がテストで固定されている
  - _Requirements: 3.6, 3.7, 4.4, 4.8, 4.10, 5.4, 6.8, 8.8_

- [ ] 4.5 記録ユースケースに値提供型サンプルと系1 イベントの記録を追加する
  - 対象: `com.hidano.facialcontrol.rec`（`Hidano.FacialControl.Rec.Application` の RecordingUseCase）
  - 値提供型サンプル通知で flags を組み（IsValid / mask 変化 → HasMask / 値変化 → HasValues）、HasMask なら mask を LSB-first の byte スクラッチへ、HasValues なら mask の立った index 順で値を float スクラッチへパックして kind 7 を追記。スクラッチは記録開始時に BlendShape 総数ヒントぶん事前確保し超過時のみ再確保。変化判定は core に委ね比較しない。値は正規化・clamp・量子化しない
  - 系1 通知はトリガーと同じ id 表解決で kind 9 / 10 を追記。id 表のシードに基準の値提供型 id・予約 id `@expression`・基準の表情 id を加える
  - テスト（rec `Tests/EditMode/RecordingUseCaseTests` 追記、`[SmallTest]`、Fake シンク）: 値提供型サンプルの mask 順パックと flags、無変化通知が来ない前提でイベントが増えないこと、系1 の kind 9 / 10、id 表シード、記録値が float ビット単位で一致すること
  - 完了条件: 上記テストと既存記録テストが EditMode で緑
  - _Requirements: 2.5, 2.6, 2.8, 4.3, 5.1, 5.2, 5.3, 6.3, 6.7_

- [ ] 4.6 ストリームライターに新基準レコードの書込と容量引数を追加し、旧構造ファイルの拒否を確認する
  - 対象: `com.hidano.facialcontrol.rec`（`Hidano.FacialControl.Rec.Adapters` の RecStreamWriter。RecFileReader は実装変更なし）
  - コンストラクタに float 容量 / byte 容量を取り、Open で kind 8（値提供型基準）と kind 11（系1 基準）を kind 5 / 6 に続けて時刻付きイベントより前に書く。レコードバッファの確保は拡張した最大レコードサイズ算出で行い、byte ペイロードをキューからライタースレッドへ運ぶ
  - テスト: rec `Tests/EditMode/RecStreamWriterTests` 追記（`Open_Always_WritesHeaderWithFullInputBaselineFlag`、値提供型 / 系1 を含む基準が時刻付きイベントより前に書かれ読み戻せること）、rec `Tests/EditMode/RecFileReaderTests` 追記（`TryRead_FileWithoutFullInputBaselineFlag_LogsErrorAndReturnsFalse`。ファイル I/O のため `[MediumTest]`）
  - 完了条件: 上記テストと既存ライター / リーダーテストが EditMode で緑
  - _Requirements: 5.3, 6.1, 6.5, 6.7_

- [ ] 4.7 キャラクターバインディングの基準捕捉を拡張し、4 ポートと容量方針を配線する
  - 対象: `com.hidano.facialcontrol.rec`（`Hidano.FacialControl.Rec.Adapters` の RecCharacterBinding）
  - 基準捕捉: registry の値提供型派生へ `TryWriteValues` を 1 回（Tick なし）呼び、戻り値・mask・scratch を値提供型エントリに写す（BlendShape 総数不一致は warn + スキップ）。系1 は遮断面の id 収集を表情 id 列に写す
  - 再生セッション構築: 系1 Injector（遮断面デリゲート）と値提供型 Injector（registry + BlendShape 総数デリゲート）を構築し 4 ポートを再生ユースケースへ渡す。再生開始が false のときは追加ログを出さず false を返す。記録セッション構築: BlendShape 総数 N から float 容量 max(128, 4N)・byte 容量 max(64, 4·ceil(N/8)) を算出してライターに渡し、記録ユースケースへ総数ヒントを渡す。停止時 Info に成長カウントを含める
  - テスト（rec `Tests/EditMode/RecCharacterBindingTests` 追記、既存 fixture のサイズ属性に従う）: 基準に値提供型エントリ（有効性・mask・値）と系1 の表情 id 列が含まれること、再生セッションが 4 ポートで構築されること、容量算出が仕様どおりであること
  - 完了条件: 上記テストと既存バインディングテストが EditMode で緑
  - _Requirements: 3.7, 5.1, 5.2, 6.7, 8.6_

- [ ] 5. osc 受信 binding の heartbeat in-place 更新（Req 8.4 の唯一の拡張パッケージ改修）
- [ ] 5.1 (P) OSC 入力源にマッピング表と寄与集合の in-place 更新を追加する
  - 対象: `com.hidano.facialcontrol.osc`（`Hidano.FacialControl.Osc`、OscInputSource）
  - マッピング表（参照差し替え）と寄与 mask（参照不変でビット置換）を更新する API を追加。mask 長が BlendShape 総数と一致しない場合は `ArgumentException`。staleness 状態は維持。マッピング表が空のとき `TryWriteValues` は false
  - テスト（osc `Tests/EditMode/Adapters/InputSources/OscInputSourceTests` 追記、`[SmallTest]`）: `UpdateMapping_mask長一致_参照不変で寄与集合更新`、`TryWriteValues_空マッピング_false`、mask 長不一致で `ArgumentException`
  - 完了条件: 上記テストと既存 OscInputSource テストが EditMode で緑
  - _Requirements: 3.9_
  - _Boundary: OscInputSource_

- [ ] 5.2 受信 binding の heartbeat 経路から registry 差し替えを廃止する
  - 対象: `com.hidano.facialcontrol.osc`（`Hidano.FacialControl.Osc`、OscReceiverAdapterBinding）
  - 開始時に初期マッピングが 0 件でも OSC 入力源を空マッピング（mask 全 false）で無条件に構築・Register する。heartbeat によるマッピング集合変化では新インスタンス生成と Replace を行わず、登録済みインスタンスの in-place 更新のみ行う（全マッピング消失時も空表 + 全 false で無効化）。mask 長不一致の `ArgumentException` は捕捉して LogError し旧マッピングを維持。iFacialMocap binding は無改修
  - テスト（osc `Tests/EditMode/Adapters/AdapterBindings/OscReceiverAdapterBindingTests` 追記、既存 `[MediumTest]`）: heartbeat でマッピング集合が変化しても registry が返すインスタンス参照が構築時と同一であること、テスト専用の注入ソースマーカー実装を Replace で装着した状態で heartbeat を与えても注入ソースが registry に残り、装着解除後に原本が復元されること、初期マッピング 0 件でも開始時に登録されること
  - 完了条件: 上記テストと既存 binding テストが EditMode で緑
  - _Requirements: 3.3, 3.9, 3.10, 8.4, 9.9_

- [ ] 5.3 osc の Fake registry 登録契約（除外根拠の主契約）を追加する
  - 対象: `com.hidano.facialcontrol.osc`（osc Tests EditMode asmdef。rec への参照は追加しない）
  - Register / Replace / Unregister の呼出を記録する Fake 入力源 registry を osc Tests 内に新設する
  - テスト（既存 `[MediumTest]` fixture へ追記）: `ArKitOscAdapterBindingTests.OnStart_FakeRegistry_RegistersNoInputSource`（Register / Replace が 0 回、公開されるアナログソースが非 null で入力源契約を実装しない）、`OscReceiverAdapterBindingTests.OnStart_FakeRegistry_RegisteredTypesAreOnlyCatalogObservedTypes`（BlendShape マッピングと gaze を含む設定で、登録された全インスタンスの実行時型がリテラル集合 {OscInputSource, GazeVector2InputSource} に閉じ、ArKit / Float のアナログ単独実装を実行時型とする登録が無い）
  - 完了条件: 上記 2 テストが EditMode で緑。fixture の FullName とメソッド名を 7.2 のカタログ宣言に転記できる状態
  - _Requirements: 1.2, 1.4_

- [ ] 6. (P) inputsystem の Fake registry 登録契約（WrappedByObservedSource の主契約）を追加する
  - 対象: `com.hidano.facialcontrol.inputsystem`（inputsystem Tests PlayMode asmdef。rec への参照は追加しない。`InputSystem.*` は Small 禁止のため PlayMode）
  - Register / Replace / Unregister の呼出を記録する Fake 入力源 registry を inputsystem Tests 内に新設する
  - テスト（`InputSystemAdapterBindingIntegrationTests` 追記、既存 `[MediumTest]`）: `OnStart_FakeRegistry_RegisteredTypesAreOnlyCatalogObservedTypes` — analog / overlay / analog-expression を含む InputActionAsset で開始し、登録された全インスタンスの実行時型がリテラル集合 {アナログラッパ（nested）, ExpressionTriggerInputSource, AnalogExpressionInputSource, OverlayInputSource} に閉じ、InputActionAnalogSource を実行時型とする登録が無く、AnalogExpressionInputSource が受け取る辞書の値がすべて wrapper 登録済み id であること
  - 完了条件: 上記テストが PlayMode で緑。fixture の FullName とメソッド名を 7.2 のカタログ宣言に転記できる状態
  - _Requirements: 1.2, 1.4_
  - _Boundary: inputsystem Tests_

- [ ] 7. 網羅性ゲート（アセンブリ列挙 → 分類正本 → ゲートテスト → IL 走査 → 除外契約テスト）
- [ ] 7.1 (P) テストアセンブリカタログに product アセンブリの列挙を追加する
  - 対象: `com.hidano.facialcontrol`（`Hidano.FacialControl.Testing`、Tests/Testing 配下。engine 非依存）
  - 「`Hidano.FacialControl` で始まり、テストアセンブリでも Testing でもない」を product 判定とし、AppDomain のロード済みアセンブリを絞り込んで名前順に返す列挙と、名前からロード済みアセンブリを探す照会を追加する
  - テスト（core `Tests/Small/Testing` の既存 fixture へ追記、`[SmallTest]`）: product 判定がテスト / Testing アセンブリを除外し Runtime / Editor の product を含むこと、照会が未ロード名で false を返すこと
  - 完了条件: 上記テストが EditMode で緑
  - _Requirements: 7.1, 7.2_
  - _Boundary: TestAssemblyCatalog_

- [ ] 7.2 (P) 入力源分類の正本カタログを rec Domain に置く
  - 対象: `com.hidano.facialcontrol.rec`（`Hidano.FacialControl.Rec.Domain`、Models 配下）
  - 分類（Observed / Excluded）・観測カテゴリ（Trigger / Analog / ValueProvider / DirectActivation）・除外区分（InjectionSource / NotRegisteredAtRuntime / EditorOnly / WrappedByObservedSource）・product アセンブリ（名前 + Editor 専用フラグ）・許容直接参照元（型 FullName + 理由）・エントリ（型 FullName・アセンブリ名・分類・カテゴリ・除外区分・理由・wrapper 型・許容参照元・ランタイム登録契約テスト名）を定義する
  - design.md「入力源分類表」21 行と 1:1 のエントリ、および Runtime 11 + Editor 9 の期待アセンブリ名リストを静的データとして宣言する。NotRegisteredAtRuntime / WrappedByObservedSource の 3 行は 5.3 / 6 で追加した fixture の FullName::メソッド名を宣言する
  - テスト（rec `Tests/EditMode/RecInputSourceCoverageCatalogTests` 新設、`[SmallTest]`。ゲート本体は 7.3 で追記）: `Entries_AssemblyName_IsDeclaredProductAssembly`、エントリ数と除外 7 行の区分が分類表と一致すること
  - 完了条件: 上記テストが EditMode で緑
  - _Requirements: 1.1, 1.2, 1.3, 1.4, 7.7, 7.9_
  - _Boundary: RecInputSourceCoverageCatalog_
  - _Depends: 4.2, 5.3, 6_

- [ ] 7.3 網羅性ゲートテスト（型の分類漏れ + アセンブリの双方向包含）を実装する
  - 対象: `com.hidano.facialcontrol.rec`（rec Tests EditMode asmdef。拡張 asmdef への参照は追加しない）
  - 検証ロジックを入力（アセンブリ名集合・型集合・カタログ）を引数に取る internal static の純関数へ分離し、負例テストで人工データを注入できるようにする
  - 検査: 期待 ⊆ ロード済み（未ロード名を列挙して失敗）、ロード済み ⊆ 期待（未宣言名を列挙して失敗）、期待アセンブリの重複、通過後に非 abstract・非 interface・非ジェネリック定義の `IInputSource` 実装と `IInputSource` 非実装の `IAnalogInputSource` 実装（private nested 含む）を列挙し、未分類 / 二重 / 陳腐化 / 除外で理由空 / Observed でカテゴリ None / Excluded で区分 None / Observed で区分非 None を型名・アセンブリ名付きで失敗。メッセージに修正先（カタログ）を含める
  - テスト（rec `Tests/EditMode/RecInputSourceCoverageCatalogTests` 追記、`[SmallTest]`）: `FindProjectProductAssemblies_CatalogProductAssemblies_AllLoaded`、`FindProjectProductAssemblies_LoadedProductAssembly_IsDeclaredInCatalog`、`Entries_EnumeratedType_IsClassified`、`Entries_DuplicateFullName_Fails`、`Entries_StaleEntry_Fails`、`Entries_ExcludedWithEmptyReason_Fails`、`Entries_ObservedWithoutCategory_Fails`、`Entries_ExcludedWithoutExclusionReason_Fails`、`Gate_MissingExpectedAssembly_FailsWithAssemblyName`、`Gate_UndeclaredLoadedAssembly_FailsWithAssemblyName`
  - 完了条件: 上記テストが実 AppDomain に対して EditMode（Small カテゴリ）で緑。未分類の Fake 型を product アセンブリに一時追加すると型名付きで失敗することを手元で確認
  - _Requirements: 1.6, 7.1, 7.2, 7.3, 7.4, 7.5, 7.6, 7.7, 7.8, 7.9, 7.10_

- [ ] 7.4 (P) product アセンブリ IL スキャナ（fail-closed）を実装する
  - 対象: `com.hidano.facialcontrol.rec`（rec Tests EditMode asmdef、internal テスト補助）
  - 入力アセンブリ集合の全型（nested 含む）の全メソッド / コンストラクタ / アクセサ本体を IL byte 列で取得し、1 byte / 2 byte の opcode 表でオペランド長を進め、design.md 記載の命令群のトークンを宣言型 / メソッドのジェネリック引数を文脈として解決し、(参照元の最外殻型, 参照先型) の集合を返す。本体なし（abstract / extern / interface）は正当にスキップ、参照元 = 自型は除外、ジェネリック型は定義へ正規化し型引数も含める
  - fail-closed: 解決例外・未知 opcode・オペランド途中切れ・`calli` は型名・メソッド名・IL offset・理由を含む `InvalidOperationException` で走査を中断する（部分結果を返さない）。トークン解決関数と IL byte 列の入口をテストから差し替え可能にする
  - テスト（rec `Tests/EditMode/ProductAssemblyIlScannerTests` 新設、`[SmallTest]`）: `Scan_RecAdaptersAssembly_DetectsRecCharacterBindingToRecAnalogInjector`、`Scan_UnrelatedType_IsNotReported`、`Scan_GenericMethodInTestFixture_ResolvesViaGenericContext`、`Scan_ResolverThrows_FailsWithDeclaringTypeAndMethodName`、`Scan_UnknownOpcode_FailsWithMethodName`
  - 完了条件: 上記 5 テストが EditMode で緑。全 20 product アセンブリの走査が例外なく完走する
  - _Requirements: 1.2, 7.8_
  - _Boundary: ProductAssemblyIlScanner_

- [ ] 7.5 除外区分ごとの契約テストを実装する
  - 対象: `com.hidano.facialcontrol.rec`（rec Tests EditMode asmdef）
  - 各テストは当該区分の全エントリを走査し違反型を列挙して失敗する。InjectionSource は注入ソースマーカーの実装、EditorOnly はカタログの Editor 専用宣言 + Runtime product アセンブリの参照先に Editor 専用名が無いこと、NotRegisteredAtRuntime は `IInputSource` 非実装 + 主契約テストの宣言と実在（ロード済みテストアセンブリを reflection で照会）+ 補助の IL 走査で直接参照元が許容集合内、WrappedByObservedSource は wrapper 型が Observed / Analog で存在し両入力源契約を実装 + 主契約の実在 + 補助の IL 走査。共通に許容参照元の理由非空と、除外区分の全列挙値に契約があること
  - 失敗メッセージは区分・違反型・違反内容・修正先を含め、IL 走査の中断は契約違反と区別して「走査失敗」として報告する
  - テスト（rec `Tests/EditMode/RecInputSourceExclusionContractTests` 新設、`[SmallTest]`）: `ExclusionContract_InjectionSource_ImplementsIInjectedInputSource`、`ExclusionContract_EditorOnly_AssemblyIsDeclaredEditorOnly`、`ExclusionContract_EditorOnly_NoRuntimeAssemblyReferencesEditorAssembly`、`ExclusionContract_NotRegisteredAtRuntime_DoesNotImplementIInputSource`、`ExclusionContract_WrappedByObservedSource_WrapperIsObservedEntry`、`ExclusionContract_RuntimeRegistrationContract_IsDeclaredAndExists`、`ExclusionContract_AllowedReferrer_ReasonIsNotEmpty`、`ExclusionContract_EveryExclusionReason_HasContract`、`ExclusionContract_NotRegisteredAtRuntime_DirectReferrersWithinAllowList`、`ExclusionContract_WrappedByObservedSource_DirectReferrersWithinAllowList`
  - 完了条件: 上記 10 テストが EditMode で緑。IL 走査で見つかった追加 referrer があれば理由付きでカタログの許容集合へ追加済み
  - _Requirements: 1.2, 1.4, 7.3, 7.4, 7.5, 7.6, 7.7, 7.8_
  - _Depends: 5.3, 6, 7.2, 7.4_

- [ ] 8. timeline Editor の REC Export が新 kind を含むファイルを読めるようにする
  - 対象: `com.hidano.facialcontrol.timeline`（`Hidano.FacialControl.Timeline.Editor`、RecEventSequenceAdapter。observer シグネチャ追随は 1.1 で完了済み）
  - イベント変換で Export 対象外の kind 7 / 9 / 10 を例外にせずスキップ（配列を詰める）。基準の kind 8 / 11 は既存どおり変換対象外。kind 2 / 3 / 4 の変換は不変
  - テスト（timeline Editor の既存 REC Export テスト fixture へ追記。ファイル I/O を伴うなら `[MediumTest]`、インメモリの RecTimeline のみなら `[SmallTest]`）: kind 7 / 9 / 10 を含むタイムラインを変換しても例外が出ず、出力が kind 2 / 3 / 4 由来のみで順序が保たれること
  - 完了条件: 上記テストと既存 Export テスト・ベイクハーネステストが EditMode で緑
  - _Requirements: 6.9_

- [ ] 9. PlayMode 受け入れ検証と GC ゲート
- [ ] 9.1 キャラクターバインディングの PlayMode 受け入れテストを追加する
  - 対象: `com.hidano.facialcontrol.rec`（rec Tests PlayMode asmdef、`RecCharacterBindingPlayModeTests` 追記、`[MediumTest]`）
  - Fake 値提供型（registry 登録）と系1 `Activate` / `Deactivate` を含む操作列を記録 → 停止 → 読込 → 再生し、ブレンド出力がフレーム 0 から収録時と一致すること（同一プロファイル・同一レイヤー設定）
  - 再生中のライブ値提供型更新とライブ `Activate` が出力に反映されないこと、停止後に原本が registry へ復元され系1 のアクティブ集合が停止時点のまま維持されること
  - 有効 → 無効 → 有効の遷移と mask 変化が再現されること、255 を超える値数を持つサンプルがラウンドトリップすること、途中位置からの再生で値提供型の値・有効性・mask と系1 のアクティブ集合が基準として確立されること
  - 完了条件: 上記テストが PlayMode で緑、既存 `RecCharacterBindingPlayModeTests` が緑のまま
  - _Requirements: 1.5, 9.1, 9.2, 9.3, 9.4, 9.5, 9.6, 9.10_

- [ ] 9.2 (P) GC ゼロゲートを大ベクトル値提供型へ拡張し既存ゲートの継続緑を確認する
  - 対象: `com.hidano.facialcontrol.rec`（`RecGcZeroGateTests` 追記、`[MediumTest]`）、`com.hidano.facialcontrol`（`FacialControllerGcZeroGateTests` 実行のみ）
  - BlendShape 総数 300 相当の Fake 値提供型を毎フレーム変化させた記録と再生の定常フレームで GC 確保がゼロであること（計測は既存ゲートと同じ計測器を使う）。Null 注入ポート Fake は 4.1 で新契約に追随済み
  - `FacialControllerGcZeroGateTests` が観測者ゼロで継続緑であること（Aggregator フックが null のまま）
  - 完了条件: 両ゲートが PlayMode で緑
  - _Requirements: 6.7, 8.2, 8.5, 9.8_
  - _Boundary: RecGcZeroGateTests, FacialControllerGcZeroGateTests_

- [ ] 10. 既存 spec 文書とパッケージドキュメントの整合
- [ ] 10.1 (P) 先行 spec の設計文書を本 spec の実態に合わせて修正する
  - 対象: `.kiro/specs/rec-recording-playback/design.md`、`.kiro/specs/rec-playback-input-exclusivity/design.md`
  - rec-recording-playback: Non-Goals の「リップシンク由来の操作イベントは他入力と同様に観測面経由で記録される」を値提供型観測面経由の消費値記録へ修正、「拡張パッケージ内部の直接参照消費者への注入到達は対象外」を Replace 遮断で対象内へ変更し残る未到達を HID-80 のみと付記、Out of Boundary の「系1 経路の記録は対象外」を本 spec で上書きされた旨へ修正、Revalidation Triggers / Physical Data Model から観測者契約の形状と kind 7〜11 を本 spec へ参照。上書き箇所にはすべて上書き元 spec 名（rec-full-input-coverage）を付記
  - rec-playback-input-exclusivity: Non-Goals の「osc パッケージの改修」「記録機能・`.fcrec` の変更」に上書きの付記、0 埋め seed は値提供型には適用せず無効で確立する旨を付記
  - 完了条件: 上記箇所がすべて修正され、本 spec の方針と矛盾する記述が残っていない
  - _Requirements: 10.1, 10.2, 10.3_
  - _Boundary: 先行 spec design.md_

- [ ] 10.2 (P) rec パッケージの README と Documentation~ を更新する
  - 対象: `com.hidano.facialcontrol.rec` の `README.md` / `Documentation~/README.md`
  - 「記録される内容」を 4 系（トリガー / アナログ・gaze / 値提供型 / 系1）へ拡張し、明示的除外 7 型と理由の表を design.md「入力源分類表」と 1:1 で記載。既知制限に HID-80（レイヤー weight / 入力源 weight はライブのまま）、開始時スナップショット方式が値提供型・系1 にも適用されること、mask 外非ゼロの非再現、基準捕捉が Update 時点の読取であることを記載
  - 「ファイル形式」: formatVersion 1 据え置き、ヘッダ flags bit0 必須（writer は常に 1、reader は欠落を読込エラー）、本変更以前のファイルは design.md 記載のエラーで拒否され再収録が必要（互換・移行なし）。kind 表に 7〜11 と mask 順疎値の説明を追加。「再生中の入力遮断」に値提供型（無効 seed）と系1（Suspend + 基準確立）の項を追加
  - 完了条件: 上記節がすべて更新され、記載された除外表・kind 表・既知制限が実装（カタログ・バイナリ形式）と一致している
  - _Requirements: 10.4, 10.5, 10.6, 10.7_
  - _Boundary: rec ドキュメント_

- [ ] 10.3 (P) timeline パッケージの REC Export ドキュメントを更新する
  - 対象: `com.hidano.facialcontrol.timeline` の `README.md` / `Documentation~/README.md`（REC Export の節）
  - 値提供型・系1 のレコード kind（7 / 9 / 10）は Export 対象外として無視され、読込は失敗しない旨を記載
  - 完了条件: 両ファイルの REC Export 節に上記が記載されている
  - _Requirements: 10.8_
  - _Boundary: timeline ドキュメント_

- [ ] 11. 最終検証（テストサイズ静的チェック・全 EditMode・全 PlayMode）
  - 対象: `FacialControl/` 全体
  - `pwsh ./scripts/check-test-sizes.ps1` が通ること（新設 fixture のサイズ宣言、Small の禁止 API 不使用、Small asmdef の参照逸脱なし）
  - 全 EditMode テスト（サイズ問わず）と全 PlayMode テストを batchmode で実行し、結果 XML で failed = 0 を確認する。網羅性ゲート・除外契約テストが `Small` カテゴリに含まれ CI の `small-tests` ジョブで実行対象になること、core が rec を参照していないこと（asmdef）を確認する
  - rec-recording-playback / rec-playback-input-exclusivity の受け入れテスト、`FacialControllerGcZeroGateTests`、`RecGcZeroGateTests` を含む既存スイートが緑であること。観測対象 13 型すべてについて記録・基準・遮断・注入・ラウンドトリップの 5 点がテストで裏付けられていることを 9.1 / 4.x の結果から確認する
  - 完了条件: 静的チェック・EditMode・PlayMode の 3 結果がすべて緑で、結果 XML のパスを記録している
  - _Requirements: 1.5, 7.8, 7.10, 8.3, 9.7, 9.8_
