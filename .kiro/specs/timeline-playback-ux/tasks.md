# Implementation Plan

対象 Unity プロジェクト: `FacialControl/`（Unity 6000.3.19f1）。主変更は timeline パッケージ `Packages/com.hidano.facialcontrol.timeline`、限定変更は core `Packages/com.hidano.facialcontrol`、最小変更は inputsystem `Packages/com.hidano.facialcontrol.inputsystem`。各タスクの括弧内に対象パッケージと層を示す。

全タスク共通の完了条件:
- TDD（Red → Green → Refactor）。テストは対象クラス単位の既存 `{Target}Tests.cs` に追記し、新規クラスのみ新規ファイルを作る
- fixture に `[SmallTest]` / `[MediumTest]` を 1 つ付け（基底が無ければ `SizedTestFixture` 継承）、`pwsh ./scripts/check-test-sizes.ps1` を通過する
- 対象パッケージの EditMode テストが緑（PlayMode テストを含むタスクは PlayMode も緑）の状態でコミットする
- timeline Runtime Domain の新規ファイルに `UnityEngine` / `Unity.Timeline` 型を持ち込まない（D14。既存例外は `FacialTimelineHashCalculator` / `TimelineStateEventCollector` のみ）
- ログ文言の完全一致（`LogAssert.Expect(Regex)`）ではなく診断状態の値でアサートする

---

## 第 1 段: Play 再現の経路（受け入れ条件 1 / 3 / 4）

- [x] 1. state sink の ContributeMask 長 0 による Aggregator 例外を再現テストで固定してから修正する（timeline Runtime Adapters）
  - 既存 `TimelineExpressionStateSinkTests`（Small）に、state sink を LayerInputSourceRegistry + WeightBuffer + Aggregator へ sourceIdx 0 として直差しし、Expression ON 後に Aggregate すると ArgumentException になる赤テストを先に書く
  - state sink が構築時にホスト BlendShape 名列を受け取り、ContributeMask 長を BlendShape 数（全 false）に揃える。値出力は従来どおり何も書かない
  - 既存の構築箇所（binding の OnStart / 既存テスト）へ BlendShape 名列を渡し、ビルドを通す
  - 完了条件: 再現テストが修正前に赤・修正後に緑、`ContributeMask.Length == BlendShape 数` のアサートが緑、timeline EditMode 全件緑
  - _Requirements: 8.8_

- [x] 2. core の後付け接続 API・Analog 消費者の registry 再解決・Profile 書き出しの冪等入口を追加する（core パッケージ）
- [x] 2.1 レイヤー入力源の後付け接続済み判定と系2 provider の増減 API を追加する（core Application）
  - LayerUseCase に「指定レイヤーに指定 id の後付け入力源が接続済みか」を返す判定を追加する
  - Layer2ActiveExpressionProvider に単一 source の追加（同 (layer, source) は重複追加しない）と削除を追加し、既存の一括設定は維持する
  - 既存 `LayerUseCaseTests` / `Layer2ActiveExpressionProviderTests`（Small）に Bind 後 true / Unbind 後 false、重複追加しない、削除で消えることを追記する
  - 完了条件: 既存 API の挙動テストがすべて緑のまま、新規テストが緑
  - _Requirements: 3.8_

- [x] 2.2 FacialController に宣言の無い入力源を接続 / 解放 / 判定し、系2 入力源を active provider と観測へ登録する public API を追加する（core Adapters）
  - 値入力源の接続（weight 指定）/ 解放 / 接続済み判定、状態入力源の登録 / 解除の 5 口を追加する。未初期化・レイヤー名不一致・id 不正は例外ではなく false + Warning
  - 接続時に観測登録と系2 provider への追加を行い、解放は逆操作にする。既存の宣言経路（registry 購読による解決・再束縛）は呼ばず変更しない
  - ホスト BlendShape 名の収集を public static に公開する（Edit プレビュー合成で再利用するため）
  - 既存 `FacialControllerTests`（Medium）に Bind → Aggregate 反映、Unbind → slot / weight 復元、未初期化で false、IsBound の真偽、既存宣言 binding の接続結果が不変、を追記する
  - 完了条件: 5 口のテストが緑、既存の接続挙動テストが変更なしで緑
  - _Requirements: 3.1, 3.3, 3.8_

- [x] 2.3 (P) 動的 id を持つ binding のマーカーを定義し、ルーティング検証が `timeline:` 系 id を不正扱いしないようにする（core Domain + core Editor）
  - Domain に「`{Slug}:*` の id を実行時に導出して登録する binding」を示すマーカー interface を追加する
  - InvalidIdValidator が、Profile の AdapterBindings のうちマーカーを実装する binding の `{Slug}:` prefix に一致する宣言 id を有効扱いにする（呼び出し側の変更なし）
  - 既存 `InvalidIdValidatorTests`（Small）に prefix 許容（一致 / 不一致 slug / マーカー無し binding は従来どおり）を追記する
  - 完了条件: マーカー binding を含む Profile で `timeline:emotion` / `timeline:layer0` が不正と判定されない
  - _Requirements: 2.5_
  - _Boundary: IAdapterBindingDynamicInputs, InvalidIdValidator_

- [x] 2.4 (P) Analog 消費者が registry の差し替えに追従する公開契約を定義し、Analog Expression 消費者で実装する（core Domain + core Adapters）
  - Domain に registry を後付けで接続 / 切断し接続状態を返す公開契約（`IRegistryAttachableAnalogConsumer`）を追加する。参照は Domain の registry 契約と slug のみ
  - AnalogExpressionInputSource が契約を実装する: 構築時に解決した binding ごとに `{slug}:{SourceId}` を購読し、通知 source が Analog なら読む先を差し替え、null なら構築時 source に戻し、Analog でない非 null は無視。切断は世代番号で旧 handler を no-op 化し全 source を元に戻す。コンストラクタ署名・値書き込み・ContributeMask は不変
  - 新規 `AnalogExpressionInputSourceTests`（Small。既存ファイルが無いため新規）に Attach → Replace で新 source の値を書く、Unregister（null）で元に戻る、非 Analog の Replace は無視、Attach 2 回で購読数不変、Detach 後は通知を無視、Attach 前は従来どおり、契約型を実装している、を書く
  - 完了条件: 上記 7 ケース緑、既存 Analog Expression の挙動テストが変更なしで緑
  - _Requirements: 3.4, 3.5, 11.1, 11.2_
  - _Boundary: IRegistryAttachableAnalogConsumer, AnalogExpressionInputSource_

- [x] 2.5 Analog BlendShape 消費者にも同じ registry 再解決契約を実装する（core Adapters）
  - AnalogBlendShapeInputSource が 2.4 と同じ契約・同じ差し替え規則を実装する（ContributeMask 不変、hot path に分岐を増やさない）
  - 既存 `AnalogBlendShapeInputSourceTests`（Small）に Replace 追従 / null 復元 / 冪等 / Detach / 契約型実装を追記する
  - 完了条件: 両消費者で契約の `IsAssignableFrom` が true、追記テスト緑
  - 2.4 の契約定義に依存するため並列不可
  - _Requirements: 3.4, 3.5_

- [x] 2.6 (P) Profile JSON 書き出しに SO 単位の冪等入口と完了イベントを追加する（core Editor）
  - AutoExporter に「有効な SO（CharacterAssetName 非空）に対し、保存 → スナップショット採取 → JSON 生成 → 既存 profile.json と文字列比較 → 異なるときだけ書く」入口を追加し、書いたときだけ true を返して完了イベントを 1 回発火する。購読者の例外は LogException にして継続
  - 既存の全件書き出しはループ本体をこの入口に委譲し、契機（ExitingEditMode / ビルド前）・件数集計・例外時 Warning は不変にする
  - 既存 `FacialCharacterProfileAutoExporterTests`（Medium）に 初回 true + 発火 1 回 + ファイル生成、同内容 2 回目 false + 不発火 + LastWriteTimeUtc 不変、内容変更後 true + 発火、CharacterAssetName 空で false + ファイル無し、全件書き出しの戻り値 = true 件数、全件 → 単体 / 逆順どちらでも 2 回目 no-op、を追記する
  - 完了条件: 同一内容で連続呼び出ししても profile.json のタイムスタンプが変わらない
  - _Requirements: 1.5, 4.5, 7.1, 11.5_
  - _Boundary: FacialCharacterProfileAutoExporter_

- [x] 3. InputSystem の analog expression 経路が registry の差し替えを追従することを 1 行の接続と PlayMode テストで固定する（inputsystem パッケージ）
  - InputSystemAdapterBinding の analog expression sink 構築直後に、2.4 の契約で registry を接続する呼び出しを 1 行追加する。他のメソッド（analog source 構築 / 登録 id / wrapper / Overlay / weight 経路 / Dispose）は変更しない
  - PlayMode テスト用に外から値を設定できる stub analog source（`IInputSource` + `IAnalogInputSource`）を inputsystem の Tests/PlayMode 配下に新設する
  - 既存 `InputSystemAdapterBindingIntegrationTests`（PlayMode Medium）に、Analog モードの action 1 本で実 binding を OnStart → registry から analog expression 消費者を取得 → `{slug}:{actionName}` を stub へ Replace で値が stub × Expression 値に追従 → Unregister で構築時の値に戻る、Replace 元 wrapper へ戻しても構築時と同じ値、を追記する。Overlay 経路のテストは変更しない
  - 完了条件: 追記 PlayMode テスト緑、inputsystem の既存 PlayMode / EditMode 全件緑、diff が接続 1 行 + テスト + stub のみ
  - _Depends: 2.4_
  - _Requirements: 3.4, 11.1, 11.2_

- [x] 4. Timeline 構成の導出・id 規約・診断モデル・Profile 内容ハッシュを Unity 非依存の Domain として用意する（timeline Runtime Domain）
- [x] 4.1 レイヤー sink id の規約（名前優先・index フォールバック・旧 state 宣言判定）を実装する（timeline Runtime Domain）
  - レイヤー名が `[a-zA-Z0-9_.-]` のみで `:` を含まず state 形が 64 文字以内なら `timeline:{name}`、それ以外は `timeline:layer{index}` を合成し、フォールバック使用を呼び出し側へ返す。値 id と state id（`:state` 終端）の両方を合成する
  - 宣言 id が「`{slug}:` で始まり `:state` で終わる」（名前形 / index 形の双方）かを判定する旧 state 宣言判定を提供する。InputSourceId として解釈できない文字列には false
  - 新規 `TimelineSinkIdConventionTests`（Small。TimelineAsset を生成しない）に ASCII 名 / 非 ASCII 名 / `:` 含み / 64 文字境界 / state 合成 / 旧 state 判定（名前形・index 形・値 sink 宣言 false・他 slug false）を書く
  - 完了条件: 非 ASCII レイヤー名でも InputSourceId の解析が例外にならず、同一入力で決定的な id が返る
  - _Requirements: 2.3_

- [x] 4.2 トラック記述子 DTO と、DTO 列 + Profile からレイヤー / チャネルを導出する純粋関数を実装する（timeline Runtime Domain）
  - Scanner が返す Unity 非依存のトラック記述子（index・種別・名前・ChannelSubId・ChannelKind・最大軸数・bake 参照有無と同一性キー・子フラグと親 index）、導出済みレイヤー記述子、チャネル記述子、導出結果（一致レイヤー / 未一致トラック名 / チャネル / 不正 ChannelSubId / Facial トラック有無）を定義する
  - 導出は root の Expression トラックのみをレイヤー候補にし（子トラックは除外）、Profile のレイヤー名と一致したものだけを `LayerIndex` 付きで返し、未一致はトラック名を記録する。チャネルは root の Value トラックのみ、ChannelSubId は REC の source id をそのまま保持、重複は先勝ちで後続を不正として記録、軸数 0 は無効。Profile が default なら全トラックを未一致にする
  - 新規 `TimelineChannelDeriverTests`（Small。DTO 列を手組みし TimelineAsset を生成しない）に root のみ対象 / 子を含めない / 未一致トラック名 / ChannelSubId 重複 / 軸数最大 / 空列 / Profile 未解決 を書く
  - 完了条件: DTO と導出のソースに `using UnityEngine` が無く、全ケース緑
  - _Requirements: 2.3, 8.2, 10.4_

- [x] 4.3 (P) 診断コード・重大度・領域と、領域単位で置換できる診断状態モデルを実装する（timeline Runtime Domain）
  - 診断コード enum（Director / TrackBinding / Bake / Profile / ProfileBinding / LayerMatch / LayerConnection / Analog / Gaze / Placement / Session の各項目。design.md の一覧どおり）、重大度（Info / Ok / Warning / Error）、領域 enum、診断項目（領域・コード・重大度・件名・直し方）を定義する
  - 診断状態モデルは項目一覧・Revision・全体重大度（最大値、空なら Ok）・Error 有無・変更イベント・コード（+ 件名）での Contains を公開し、領域単位の置換と全消去で Revision を進めてイベントを発火する
  - 新規 `FacialTimelineDiagnosticsTests`（Small）に 領域置換で Revision 増加とイベント発火、他領域が残る、Overall の算出、Contains の真偽、Clear、を書く
  - 完了条件: テストがログ文言ではなくコード値で診断を検証できる状態になっている
  - _Requirements: 2.6, 2.7, 5.2, 8.9, 11.4_
  - _Boundary: FacialTimelineDiagnostics, TimelineDiagnosticCode_

- [x] 4.4 (P) 同一原因の警告を 1 セッション / 1 エポックに 1 回だけ通す警告ゲートを実装する（timeline Runtime Domain）
  - (所有者 instanceID, 診断コード, 件名) をキーに初回のみ true を返し、エポックリセットで再び通す
  - 新規 `TimelineOnceWarningGateTests`（Small）に 初回 true / 2 回目 false、件名違いは別キー、所有者違いは別キー、リセット後に再び true、を書く
  - 完了条件: 同一キーの連続呼び出しが 1 回しか通らない
  - _Requirements: 8.7_
  - _Boundary: TimelineOnceWarningGate_

- [x] 4.5 (P) Profile スナップショットの内容ハッシュを Timeline 構造から分離し、Bake に保存する（timeline Runtime Domain 既存例外ファイル + Bake アセット）
  - ハッシュ計算に「Profile 内容ハッシュ」（SchemaVersion / Layers / LayerInputSources / Expressions（id 順）/ Slots / DefaultOverlays / BaseExpression / GazeChannels）の ulong と hex を追加し、既存の Source ハッシュは Timeline 構造 + Profile 内容ハッシュ + sampleRate で計算するよう変更する
  - Bake アセットに Profile 内容ハッシュ hex を additive に追加し（既存フィールド不変）、TimelineBakeService の Bake が Source ハッシュと併せて書き込む
  - 既存 `FacialTimelineHashCalculatorTests`（Small）に Layers / LayerInputSources / GazeChannels / Expressions の変更でハッシュが変わる、Expressions の順序に依存しない、Timeline のみの変更で Profile 内容ハッシュが不変、Source ハッシュが Profile 内容ハッシュを含む、を追記する
  - 完了条件: 新規に焼いた Bake の Profile 内容ハッシュが非空で、同じ Profile から再計算した値と一致する
  - _Requirements: 1.5, 4.5, 4.6, 7.1, 7.5_
  - _Boundary: FacialTimelineHashCalculator, FacialTimelineBakeAsset, TimelineBakeService_

- [x] 5. TimelineAsset の走査・Bake 解決・Director / Track binding 解決・レイヤー接続・乗っ取り・静的診断を Adapters サービスとして実装する（timeline Runtime Adapters）
- [x] 5.1 Facial トラックに Bake 参照の保持口を持たせ、Value トラックに Receiver 型の binding 属性を付ける（timeline Runtime Tracks）
  - Bake 参照保持の契約（取得 / 設定）を定義し、Expression トラックと Value トラックが HideInInspector のシリアライズフィールドで実装する
  - Value トラックに Receiver を binding 型とする属性を付け、Director Inspector に binding 欄が出るようにする
  - 既存 `FacialTimelineTrackAssetTests` に Bake 参照のシリアライズ往復（設定 → 保存 → 再読込で同一参照）と null 既定を追記する
  - 完了条件: 両トラックで Bake 参照が保存・復元され、Inspector には表示されない
  - _Requirements: 4.1, 1.6_

- [x] 5.2 TimelineAsset を Unity 非依存のトラック記述子列と実トラック列に写す Scanner を実装する（timeline Runtime Adapters）
  - root の出力トラック順に Facial トラック（Expression / Value）を列挙し、各 root の直後に子トラックを子フラグ + 親 index 付きで並べる。Facial 以外は含めない。Value の最大軸数は全 Clip の軸数の最大、bake 参照の有無と同一性キーは 5.1 の保持口から読む。null 入力は空結果
  - 記述子列と同じ index で実トラック列を返し、Adapters 側が逆引きできるようにする。アセットは変更しない
  - 新規 `TimelineAssetScannerTests`（Small。`CreateInstance<TimelineAsset>` + CreateTrack のみ）に root / 子の順序と親 index、Facial 以外の除外、最大軸数、bake 参照有無と同一性キー、null 入力、を書く
  - 完了条件: 走査結果を 4.2 の導出に渡して期待どおりのレイヤー / チャネルが得られる
  - _Requirements: 2.3, 4.1, 8.2_

- [x] 5.3 全 Facial トラックの Bake 参照一致を検証して Bake を解決する Locator を実装する（timeline Runtime Adapters）
  - 5.2 の走査結果の全 holder（root + 子）を検査し、Facial トラック無しは Missing、全 null は LegacyExport、非 null 参照が 1 種で null 無しは Found、それ以外（混在・部分欠落）は Conflict（走査順に依存する採用はしない）。上書き Bake が指定されていれば検証後に OverrideUsed とし、トラック参照と不一致なら OverrideDiffers を立てる
  - 結果に採用 Bake / トラック参照 / holder 数 / null 数 / 参照種類数 を含める
  - 新規 `FacialTimelineBakeLocatorTests`（Small）に Found / Missing / LegacyExport / Conflict（混在）/ Conflict（部分欠落）/ OverrideUsed 一致 / OverrideUsed 不一致 / OverrideUsed 部分欠落 / トラック順入れ替えで同じ結果、を書く
  - 完了条件: 同一入力で決定的な状態が返り、Conflict では採用 Bake が null
  - _Requirements: 4.1, 4.2, 6.3, 8.3, 10.7_

- [x] 5.4 (P) Director の解決規則と Facial トラックの generic binding 自動設定を実装する（timeline Runtime Adapters）
  - Director 解決は 上書き → 同 GameObject → 親階層 → シーン走査（Facial トラックを持つ TimelineAsset をバインドし、いずれかの Facial トラックの binding が自分を指す Director）の順。シーン走査で候補 2 つ以上は Ambiguous、無ければ NotFound
  - binding 書込口（Runtime は直接設定、Editor 実装は後続タスク）を抽象化し、binding が null の Facial トラック（root + 子）にだけ Receiver を設定、既に自分なら既存扱い、他オブジェクトなら触らず報告に記録する
  - 新規 `TimelineTrackBindingResolverTests`（Medium。Director / Receiver を GameObject に配置）に 解決順 4 段、Ambiguous、未設定のみ設定、他者設定を上書きしない、Facial トラック無しの Timeline、を書く
  - 完了条件: 自動設定後に Director の全 Facial トラック binding が Receiver を指し、他者 binding は不変
  - _Depends: 5.2_
  - _Requirements: 1.6, 5.2, 8.5_
  - _Boundary: TimelineTrackBindingResolver, ITrackBindingWriter_

- [x] 5.5 導出結果から sink を生成し、registry 登録と FacialController への接続 / 解放を行う Connector を実装する（timeline Runtime Adapters）
  - 接続前に Profile の宣言を 4.1 の旧 state 宣言判定で静的走査し、1 件でも一致すれば何も登録せず LegacyStateDeclaration（Error、件名 = レイヤー名 + 宣言 id、直し方付き）を記録して中断する
  - レイヤーごとに値 id / state id を合成（フォールバック時は LayerSinkIdFallback を Info で記録）、Bake の名前から値 sink を、BlendShape 名列と Profile から state sink を生成（セッションプールがあれば再利用）。値 sink のみ registry に登録し、既存宣言で接続済みなら LayerConnectionSkippedDeclared（Info）、未接続なら 2.2 の API で weight 1 で接続（成功 LayerConnected / 失敗 LayerConnectionFailed）。state id がレイヤーに接続済みなら登録済みを全部戻して LegacyStateDeclaration で中断、そうでなければ状態入力源として登録（registry には登録しない）
  - 解放は接続したレイヤーの解放・状態登録解除・値 sink の登録解除・TriggerOff / Invalidate を行い、二重解放は no-op
  - 新規 `TimelineLayerConnectorTests`（Medium。FacialController を Fake Profile で初期化）に Connect → Aggregate 反映、値 sink の既存宣言ありでスキップ Info、旧 state 宣言ありで中断 + registry / レイヤー構成が不変、index 形の state 宣言でも検出、state sink が registry に無い、Disconnect で slot / weight 復元、state sink が overlay suppress の active provider から観測される、を書く
  - 完了条件: Connect が Connected を返した状態で state sink がレイヤー入力源に接続されていることがない
  - _Depends: 1, 2.2, 4.1, 4.2, 4.3_
  - _Requirements: 3.1, 3.2, 3.3, 4.3, 8.1, 8.8_

- [x] 5.6 (P) Analog / Gaze チャネルを ChannelSubId の registry エントリへ Replace で乗っ取り、復元する Takeover を実装する（timeline Runtime Adapters）
  - Timeline の Analog 入力源を注入型（占有規則共有）にし、差し替え元の退避 / 接続 / 解除の実装を Gaze 入力源から基底へ移動する
  - チャネルごとに ChannelSubId を最初の `:` で slug / sub に分割して解決し、未登録は AnalogSourceNotFound / GazeSourceNotFound（Warning）、既存が注入型なら AnalogOccupied / GazeOccupied（Warning）、成功なら原本を退避して Replace し AnalogTakeoverAttached / GazeTakeoverAttached（Info）。Gaze は 2 軸前提で不足は 0 埋め。独自 id での別途登録はしない
  - 解放は参照同一性を確認して原本を Replace で戻す（他者占有なら Warning + スキップ）。Inspector 表示用のエントリ一覧を公開する
  - 新規 `TimelineChannelTakeoverTests`（Small。Fake registry）に Replace / 占有スキップ / 参照同一性復元 / ChannelSubId をそのまま使う / 2.4 の契約で Attach 済みの Analog Expression 消費者を置いた registry で Attach → 値書き込みがクリップ値を反映し Release で元に戻る、を書く
  - 完了条件: Attach 後に消費者の出力が Timeline sink の値になり、Release 後に原本の値に戻る
  - _Depends: 2.4, 4.3_
  - _Requirements: 1.3, 3.2, 3.4, 3.5, 3.6, 3.7, 10.4_
  - _Boundary: TimelineChannelTakeover, TimelineAnalogInputSource, TimelineGazeInputSource_

- [x] 5.7 Edit / Play 開始前に静的に判定できる診断を評価する Evaluator と評価コンテキストを実装する（timeline Runtime Adapters）
  - 評価コンテキスト（Director / Timeline / Controller / Profile SO / Profile と有無 / GazeChannels / Bake 解決結果 / 導出結果 / Bake に記録された Profile 内容ハッシュ）を定義する
  - Director 領域（DirectorMissing / TimelineNotBound / DirectorAmbiguous）、TrackBinding 領域（AutoAssigned / Foreign）、Bake 領域（Fresh / Missing / Stale / LegacyExport / ReferenceConflict / OverrideUsed / OverrideDiffers）、Profile 領域（Matched / Mismatch。空文字も不一致）、ProfileBinding 領域（BindingMissing / BindingSlugInvalid。Enabled と legacy は 6.3 で追加）、LayerMatch 領域（TrackLayerUnmatched）、Placement 領域（ReceiverNotOnControllerObject / ControllerMissing / ControllerNotInitialized）を領域単位で診断状態へ置換する。Severity は design.md の分類どおり
  - 新規 `TimelineDiagnosticsEvaluatorTests`（Medium。GameObject + Receiver + Director を組む）に Director 無し / Timeline 未バインド / Receiver 別 GameObject / トラック名不一致 / Bake の LegacyExport と Conflict / Profile 内容ハッシュ不一致（Warning）/ binding 無し の各ケースで `Contains(code)`、全問題なしで Overall が Ok、を書く
  - 完了条件: 各欠落ケースが診断コードの値で判定でき、問題なしの状態値が保持される
  - _Depends: 4.3, 4.5, 5.3, 5.4_
  - _Requirements: 2.7, 5.2, 5.3, 8.2, 8.3, 8.4, 8.5, 8.9, 10.7, 11.4_

- [x] 6. Receiver をファサード化し、binding を受信許可フラグに格下げし、Mixer を Edit / Play で分岐させる（timeline Runtime）
- [x] 6.1 Receiver を再生セッションの集約点に書き換え、binding と Mixer の呼び出しを新 API へ切り替える（timeline Runtime Adapters + Playables）
  - binding が渡す接続コンテキスト（slug / Profile / BlendShape 名 / registry / controller / 有効フラグ）を定義し、Receiver に接続 / 切断の内部 API、`BeginPlaybackSession(timeline, director)`、ReleaseAll、セッション状態（Idle / Pending / Active / Failed）、診断状態、上書き Director / Bake、直近の Bake 解決結果、乗っ取りエントリ、接続レイヤー名、Mixer 向けの sink 解決（署名維持）を持たせる
  - セッション開始は冪等。Failed 判定順は (1) binding 未接続 / 無効 / Receiver 別 GameObject / 別 timeline の SessionConflict → (2) Locator が Conflict / LegacyExport → (3) Connector が LegacyStateDeclaration。Profile は controller が保持する値を使い、Bake の Profile 内容ハッシュ不一致は ProfileMismatch（Warning）、Profile 一致で Source ハッシュのみ不一致は BakeStale（Warning）でいずれも Active を維持。controller 未初期化は Pending でログ無し再試行。Console 出力は最初の Error 1 件のみ、Inspector には全件。Bake → sink のバインディングはセッション開始時に全レイヤー分を先行構築し、`(Timeline, Bake, Profile)` が同じ間はセッション資源をプールする
  - 旧 `Configure(tuple 列)` と `BeginPlaybackSession(profile, timeline)` を撤去し、binding の OnStart は接続コンテキストを渡すだけ、Dispose は切断（ReleaseAll 含む）を呼ぶ形に切り替える（Enabled / legacy フィールドの仕上げは 6.3）。Mixer は Director を graph resolver から得て新署名を呼ぶ（Edit / Play 分岐は 6.4）。Gaze 乗っ取りは 5.6 の Takeover へ委譲し、既存の Receiver 内実装を撤去する
  - 既存 `FacialTimelineReceiverTests`（Medium）を 接続 → Begin の状態遷移、Pending 再試行、Failed 条件 3 系の判定順と Console 1 回、BakeStale / ProfileMismatch が Warning で Active を維持し Bake の値を出す、ReleaseAll で registry / レイヤー構成が復元、へ書き換え、gaze 乗っ取りテストは `TimelineChannelTakeoverTests` へ移管し、ログ文言一致のアサートは診断コードへ置換する。既存 `TimelineAdapterBindingTests` の Configure 前提箇所を接続コンテキスト前提へ書き換える。既存 PlayMode 3 件（`TimelineLiveEquivalenceIntegrationTests` / `TimelineDegradationIntegrationTests` / `TimelineGcZeroGateTests`）の `MutableTargetLayerNames` reflection 呼び出しを撤去し（導出が自動化されるため設定不要）、緑を維持する（fixture の本移行は 10.6）
  - 完了条件: Profile に Target Layer Names を書かずに、トラック名がレイヤー名と一致する TimelineAsset の再生で状態が Active になり、timeline EditMode / PlayMode 全件緑
  - _Depends: 5.3, 5.5, 5.6, 5.7_
  - _Requirements: 1.1, 1.2, 1.4, 3.2, 4.2, 4.3, 4.6, 5.1, 5.5, 8.3, 8.6, 10.7, 11.4_

- [x] 6.2 Receiver のライフサイクルで Director 解決・Track binding 自動設定・Play 開始時の静的診断・全解放を行う（timeline Runtime Adapters）
  - Play の OnEnable で 5.4 により Director を解決して Facial トラックの binding を自動設定し、設定が発生し graph が有効なら RebuildGraph する。Start で 5.7 の静的診断を評価し、Error / Warning を 4.4 のゲートで 1 回ずつ Console に出す（Info は出さない）。OnDisable / OnDestroy は ReleaseAll、ReleaseAll 時にゲートの Play エポックをリセットする
  - Edit / Play 共通の静的診断評価（Runtime は controller の Profile、Editor は Profile を渡す overload）を公開する
  - 既存 `FacialTimelineReceiverTests` に OnEnable 後に binding が自分を指す、Receiver を別 GameObject に置いたときの診断、Director 無しの診断、同一セッションで同じ警告が 2 回出ない（ゲート）、無効化で接続 / 乗っ取りが解放される、を追記する
  - 完了条件: 4 手順のうち Receiver の配置だけで Track binding が埋まり、欠落ケースの診断が Play 開始時に 1 回ずつ出る
  - _Requirements: 1.6, 2.7, 8.1, 8.4, 8.5, 8.7, 9.4_

- [x] 6.3 binding を Slug + 有効フラグのみの受信許可フラグに格下げし、旧フィールドを legacy として残す（timeline Runtime Adapters）
  - 有効フラグ（既定 true）を追加し、Target Layer Names / Channel Definitions を HideInInspector の legacy フィールドに格下げ（値は再生に使わない）。どちらかが非空なら OnStart で binding インスタンスごと 1 回だけ Console に警告する
  - OnStart は Slug 検証 → legacy 警告 → 有効判定 → Receiver 取得または AddComponent（所有フラグ）→ 接続。無効時は Receiver を生成せず、既存 Receiver があれば無効の接続コンテキストだけ渡す。Dispose は切断後、所有している場合のみ Receiver を破棄する。2.3 のマーカーを実装し、Gaze 提供者 interface の実装を外す
  - 5.7 の Evaluator の ProfileBinding 領域に BindingDisabled（Error）/ BindingLegacyFields（Warning）を追加する
  - 既存 `TimelineAdapterBindingTests`（Medium）を 有効 false で Receiver 非生成 + BindingDisabled 診断、legacy フィールド（reflection で埋める）で 1 回警告、Dispose の所有判定 2 ケース（自分が追加した Receiver は破棄 / ユーザー配置は残して解放のみ）、へ書き換える
  - 完了条件: Profile Inspector で編集可能なシリアライズ項目が Slug と有効フラグだけになり、旧 Profile SO が警告 1 回で再生を継続する
  - _Requirements: 1.2, 2.1, 2.4, 2.6, 9.2, 9.3_

- [x] 6.4 Mixer を Edit プレビューと Play で分岐させ、無言 return を撤去して毎フレームの確保ゼロを維持する（timeline Runtime Playables）
  - 両 Mixer は Play 以外ではプレビュー bridge のみ呼んで戻り、セッション開始を呼ばない。Play ではセッション開始を呼び Active 以外なら戻る。sink 解決失敗は Receiver の診断に記録済みのため Mixer 自身はログを出さず、毎フレームの判定は bool キャッシュで確保ゼロにする
  - 既存 `TimelineGcZeroGateTests`（PlayMode Medium）でセッション開始後の ProcessFrame で確保 0 を確認し、Mixer のテスト（既存 `{Target}Tests` があれば追記）で Edit 相当の呼び出しがセッション状態を Idle のまま変えないことを固定する
  - 完了条件: Edit モードで Receiver のセッション状態が変化せず、Play の ProcessFrame で確保 0
  - _Requirements: 1.4, 7.2, 8.6_

- [x] 7. Editor 系の Profile ソースを統一し、Export と Bake が全 Facial トラックへ同一の Bake 参照を書くようにする（timeline Editor）
- [x] 7.1 Editor 系が Runtime と同じ Profile 読込経路を使う統一入口をキャッシュ付きで実装する（timeline Editor）
  - Profile SO から Runtime と同じ読込（StreamingAssets の profile.json 優先、無ければ SO）をそのまま呼んで返す統一入口を提供し、優先順位やパス規則を再実装しない。キャッシュキーは SO instanceID + profile.json の存在 / 最終更新時刻 + SO のダーティ状態。明示的な無効化を提供し、Profile SO / profile.json 保存時に無効化する
  - Timeline から Profile SO を解決する既存の順序（Bake の Profile GUID → Director にバインドされた Receiver の controller → 開いている Director）を 1 箇所にまとめる
  - 新規 `TimelineProfileSourceTests`（Medium）に JSON 有無で Runtime 読込と同じ Profile 内容ハッシュになる、JSON 更新（最終更新時刻変化）でキャッシュが無効化される、明示無効化、を書く
  - 完了条件: Editor 側で Profile を SO から直接構築する経路が本入口に置き換え可能な状態になっている
  - _Requirements: 4.5, 7.5_

- [x] 7.2 Bake サービスと Validator が統一 Profile 入口を使い、鮮度判定が不一致の種別を返すようにする（timeline Editor）
  - Bake サービスの SO overload と鮮度判定を 7.1 経由に変更し、鮮度判定は Profile 内容ハッシュ → Source ハッシュの順に比較して不一致種別（None / ProfileChanged / TimelineChanged）を返す
  - Validator の Profile 解決を 7.1 経由に変更する
  - 既存 `TimelineBakeServiceTests` / `FacialTimelineValidatorTests`（該当する既存 `{Target}Tests`）に Profile のみ変更で ProfileChanged、Timeline のみ変更で TimelineChanged、JSON と SO が食い違う fixture で JSON 側の Profile が使われる、を追記する
  - 完了条件: Editor 系（Bake / Validator）で SO フォールバックを直接呼ぶ箇所が無い
  - _Requirements: 4.5, 4.6, 7.5_

- [x] 7.3 全 Facial トラックへ Bake 参照を書く Writer を実装し、Export と Bake の後処理に組み込む（timeline Editor）
  - root + 子の全 Facial トラックに同じ Bake サブアセット参照を書き、Bake サブアセットに HideInHierarchy を付ける Writer を実装する（内部キャッシュなので Undo には載せず SetDirty のみ）
  - REC Exporter は Bake 生成 / 更新後に Writer を適用し、Profile は 7.1 経由で解決する。Bake サービスの通常ベイク経路でも焼いた後に Writer を適用する（既存署名の director / receiver 引数は 10.4 で撤去するまで維持）
  - 既存 `RecToTimelineExporterTests`（Medium）に Export 後の全 Facial トラック（子を含む）が同一の Bake を参照し、5.3 の Locator が Found を返す、Bake サブアセットが HideInHierarchy である、を追記する
  - 完了条件: Export 直後の TimelineAsset だけで Runtime が Bake を解決できる
  - _Requirements: 1.2, 4.1, 4.4, 4.7_

- [ ] 8. REC Export の出力をそのまま使う PlayMode end-to-end テストで受け入れ条件を固定する（timeline Tests）
- [ ] 8.1 e2e 用の共有 fixture（.fcrec 生成・Fake Analog binding・配置ヘルパー）とテスト asmdef の参照を用意する（timeline Tests/Shared + asmdef）
  - Tests/Shared の asmdef に core Domain / Application / Adapters と Rec Domain / Rec Adapters の参照を、Tests/PlayMode の asmdef に Rec / Application の参照を追加する。inputsystem は参照しない
  - `.fcrec` を trigger（smile）/ analog（`osc:lt` 1 軸）/ gaze（`osc:gaze` 2 軸）で書き出すヘルパー、外から値を設定できる Fake analog source、Fake analog source を `osc:lt` に登録し core の Analog Expression 消費者（binding `lt` → `squint`）を構築して 2.4 の契約で registry に接続し `osc:analog-expression` で登録する Fake binding（InputSystem の構成と同形、Dispose で切断）、BlendShape 3 個以上のメッシュ + 明示目ボーン + Profile SO（emotion / overlay レイヤー、smile / squint、GazeChannels 既定、AdapterBindings に Timeline binding と Fake binding、emotion の inputSources に `osc:analog-expression`）を `Assets/<guid>/` に保存し Export → FacialController / Receiver / Director 配置までを束ねる fixture を実装する。生成物は TearDown で削除
  - Fake 類は `[SmallTest]` 等の対象外だが、fixture を使う smoke（Export が成功し Locator が Found）を 8.2 の最初のケースとして書けることを確認する
  - 完了条件: fixture 一式がコンパイルされ、`pwsh ./scripts/check-test-sizes.ps1` が Small asmdef 参照違反を報告しない
  - _Requirements: 11.1, 11.6, 11.7, 11.8_

- [ ] 8.2 4 手順だけで Expression / Analog / Gaze が再現されることを e2e で固定する（timeline Tests/PlayMode）
  - 新規 `TimelinePlaybackEndToEndTests`（PlayMode Medium）で Director を Manual 更新にし、時刻設定 → Evaluate → 1 フレーム待ちで SkinnedMeshRenderer の BlendShape を検証する: smile のトリガーで Expression 値が出る、Analog クリップ値 × Expression 値が squint の BlendShape に出る、Gaze が目ボーン回転に出る。Receiver 同 GameObject + Director 別 GameObject の配置でも同じ結果
  - セッション終了（Director 停止 / ReleaseAll）で `osc:lt` が Fake に戻り squint が 0 に戻る、registry / レイヤー構成が復元される、診断の Overall が Ok（問題なしの状態値）を検証する
  - 完了条件: Profile に Timeline 専用設定を一切書かずに 3 種が再現され、受け入れ条件 (1)(4) が緑
  - _Requirements: 1.1, 1.2, 1.3, 1.5, 3.2, 3.4, 3.6, 8.9, 11.1, 11.2_

- [ ] 8.3 手順欠落と復旧、Clip 編集後のタイミング変化を e2e で固定する（timeline Tests/PlayMode）
  - `TimelinePlaybackEndToEndTests` に追記: Clip を移動して再ベイク（Bake サービス + 7.3 の Writer）した後の再生で BlendShape が変わる時刻が移動する、1 トラックだけ Bake 参照を別インスタンス（または null）にずらすと BakeReferenceConflict で Failed → 再ベイクで全トラック同一参照に戻り Active に復旧、旧 Profile（`:state` 宣言あり）で LegacyStateDeclaration の Failed → 宣言を除いた Profile で再現、Receiver を controller と別 GameObject に置くと ReceiverNotOnControllerObject、binding 無効で BindingDisabled、を診断コードの値で検証する
  - 完了条件: 受け入れ条件 (3) の各欠落ケースが診断状態で判定でき、第 1 段のロールバック基準（値 sink 宣言のみの旧 Profile が動き、`:state` 宣言ありは止まる）を満たす
  - _Requirements: 2.6, 3.3, 6.3, 8.1, 8.2, 8.3, 8.4, 10.7, 11.3, 11.4_

---

## 第 2 段: Editor の変更検知・自動再ベイク・Receiver Inspector（受け入れ条件 2 の「次の Play」と Inspector 表示）

- [ ] 9. Editor イベント購読を一元化し、Clip 編集の自動再ベイクと Receiver Inspector の診断表示を実装する（timeline Editor）
- [ ] 9.1 変更検知を合流させデバウンス後に再ベイクを 1 回だけ実行するインスタンス型 Watcher を実装する（timeline Editor）
  - 再ベイク実行口の抽象（結果: NoChange / ReferencesRepaired / Rebaked / Failed）と時計を注入でき、tick の要求 / 解放はコールバックで差し替えられる形にする（既定は後続の Services に接続）。デバウンス既定 0.3 秒、テストから短縮可
  - MarkDirty は 理由（ClipEdit / UndoRedo / ObjectChange / ProfileChanged / BakeReferenceInconsistent / ProfileMismatch）を受け、未保存（アセットパス空）の Timeline は UnsavedTimeline を返して予約せず、Play 遷移中は Ignored、同一 Timeline の連続は Coalesced。再ベイク中の MarkDirty は完了後に 1 回だけ再実行。実行前に 7.1 のキャッシュを無効化し、成功で BakeUpdated + Timeline ウィンドウの Refresh、失敗で RebakeFailed + Warning（前回 Bake 保持）。Pending が 0 → 1 で tick 要求、1 → 0 で解放。Timeline が解決した Profile SO の追跡（SO → Timeline の逆引き）を提供する
  - 新規 `TimelineEditChangeWatcherTests`（Medium。Fake 実行口 + Fake 時計）に MarkDirty → デバウンス経過 Tick で再ベイク 1 回、ハッシュ一致で no-op、連続 10 回で 1 回、再ベイク中の MarkDirty で完了後 1 回、失敗で RebakeFailed + 前回保持、ProfileChanged で再ベイク、BakeReferenceInconsistent でハッシュ一致でも参照修復と BakeUpdated、未保存 Timeline で UnsavedTimeline + PendingCount 不変、tick 要求 / 解放の回数、Dispose で pending 破棄、を書く
  - 完了条件: 連続した編集が 1 回の再ベイクに畳まれ、未保存 Timeline が予約されない
  - _Requirements: 6.1, 6.2, 6.5, 6.6, 6.7_

- [ ] 9.2 DirtyWatcher のダイアログを撤去し、再ベイク口の公開・参照修復・Undo 対応・Play 移行前の Profile 直列同期を実装する（timeline Editor + Editor asmdef）
  - timeline Editor asmdef に core Editor（`Hidano.FacialControl.Editor`）の参照を追加する
  - 再ベイクを public 化し（Timeline のみ / Profile SO 指定の 2 overload、結果と失敗理由を返す）、ハッシュ一致で焼き直さない場合も 7.3 の Writer を必ず適用して参照が変わったら ReferencesRepaired を返す。Receiver 参照の更新は Undo.RecordObject + SetDirty。鮮度判定は 7.1 の Profile で 7.2 の種別判定を使う
  - Play 終了後のダイアログ分岐と結果の HasDialog を撤去し、Edit 復帰時の修復は無言で実行して Console に Info を 1 行出す。Play 中に検出された HashMismatch / ProfileMismatch は Receiver の診断状態を走査して修復する
  - ExitingEditMode 処理は、シーン上の Director から解決した (Timeline, Profile SO) について SO ごとに 1 回 2.6 の冪等入口を呼び（例外は Warning で継続）→ 7.1 のキャッシュ無効化 → 再解決 → 鮮度照合と Locator 判定 → 必要なら再ベイク、の直列で処理する。Timeline 側が profile.json を書く経路は持たない。静的コンストラクタの購読は 9.3 で撤去するまで維持する
  - 既存 `TimelineBakeDirtyWatcherTests`（Medium）の DisplaysDialog テストを「Edit 復帰時に無言で修復・Info 1 行」へ置換し、全 holder に参照が書かれる、ハッシュ一致でも参照不一致なら ReferencesRepaired、Receiver 参照更新が Undo 可能、AutoExport 有効な Profile SO を変更後に ExitingEditMode 処理で profile.json の内容ハッシュと Bake の Profile 内容ハッシュが一致し Receiver のセッションが ProfileMatched になる、全件書き出し → 直列処理 / 直列処理 → 全件書き出し の両順で Bake のハッシュ・profile.json 内容・最終更新時刻（2 回目不変）が同一で完了イベントが合計 1 回、AutoExport 無効 SO では profile.json が生成されず SO フォールバックで照合が通る、を追記する
  - 完了条件: 購読順を入れ替えても Play 直前の Bake が controller の読む JSON と同じスナップショットで焼かれ、ダイアログが表示されない
  - _Depends: 2.6, 7.1, 7.2, 7.3_
  - _Requirements: 1.5, 4.4, 4.5, 6.3, 6.4, 6.6, 9.1_

- [ ] 9.3 Unity イベント購読の唯一の所有者となる Services を実装し、DirtyWatcher の自己購読を撤去する（timeline Editor）
  - `[InitializeOnLoad]` の静的サービスが 冪等な初期化（ObjectChangeEvents / Undo / playModeStateChanged / beforeAssemblyReload / quitting / 2.6 の完了イベント の固定 6 購読 + 9.1 の Watcher 生成）と Shutdown（全購読解除 + Watcher 破棄、二重呼び出しは no-op）を提供し、update は Watcher の pending がある間だけ参照カウントで購読する。テスト用に現在の購読数を公開する
  - 配送規則: ObjectChange は対象型（Facial トラック / Clip / TimelineAsset / Profile SO とその派生）を判定して MarkDirty、Undo / Redo は開いている Timeline と追跡中 Timeline を MarkDirty、完了イベントは追跡逆引きで ProfileChanged、ExitingEditMode は Watcher の FlushNow → 9.2 の直列処理、EnteredEditMode は無言修復 + 4.4 のゲートのエポックリセット、ExitingPlayMode は pending 破棄
  - DirtyWatcher の `[InitializeOnLoad]` と静的コンストラクタの購読（playModeStateChanged / Receiver の BakeIssueDetected）を撤去し、AssetModificationProcessor の保存経路は残す
  - 新規 `TimelineEditorServicesTests`（Medium）に 初期化 2 回で購読数不変、Shutdown で 0 になり以後の Undo / ObjectChange で再ベイクが走らない、Shutdown → 初期化（ドメインリロード相当）後に 1 回の変更で再ベイクが 1 回だけ、未保存 Timeline のスキップ、MarkDirty 10 連打 → FlushNow で再ベイク 1 回、pending が無い間は update 購読が無い（固定購読数のまま）、ExitingEditMode 相当で FlushNow → 直列処理 の順、2.6 の入口で JSON が実際に書かれると追跡中 Timeline が pending になる、を書く
  - 完了条件: timeline Editor asmdef 内で Unity イベントを購読するクラスが Services（と後続の Inspector インスタンス）だけになる
  - _Requirements: 6.1, 6.5, 6.7, 9.1, 11.7_

- [ ] 9.4 Timeline ウィンドウの Clip / Track 操作を変更通知として Watcher へ流し、Value トラックの Editor を新設する（timeline Editor TrackEditors）
  - 既存の Expression Clip / Expression Track / Value Clip の Editor に Clip 変更・Track 変更・作成の override を追加し、Services の Watcher へ ClipEdit で MarkDirty する（Editor 側は購読を持たない）。Value Track の Editor を新設し同じ通知を行う。作成時は兄弟トラックの Bake 参照を補完する
  - 既存の TrackEditor / ClipEditor テスト（`{Target}Tests` があれば追記、無ければ新設した Value Track Editor のみ新規）で 変更通知が MarkDirty を 1 回呼ぶ、作成で兄弟の Bake 参照が補完される、を固定する
  - 完了条件: Clip のドラッグ・トリム・追加・削除・Undo のいずれでも Watcher の pending が立つ
  - _Requirements: 6.1, 6.7_

- [ ] 9.5 (P) Profile SO の旧 timeline state 宣言を走査し Undo 可能に削除する Cleaner を実装する（timeline Editor）
  - Profile SO の各レイヤー inputSources を 4.1 の旧 state 宣言判定で走査し、state 宣言（削除対象）と値 sink 宣言（Info のみ）を区別して列挙する。削除は Undo.RecordObject → SerializedObject 経由で state 宣言の要素だけ削除 → ApplyModifiedProperties + SetDirty → 7.1 のキャッシュ無効化、削除件数を返す。値 sink 宣言とその weight は変更しない
  - 新規 `LegacyTimelineDeclarationCleanerTests`（Medium。AssetDatabase で SO を作る）に state 2 件 + 値 sink 1 件で Scan が 3 件（state 2）、削除が 2 を返し値 sink と weight が残る、Undo で 3 件に戻る、他 slug の state は対象外、を書く
  - 完了条件: Connector の Play 検出と Inspector の Edit 検出が同じ判定関数を使い、結果がずれない
  - _Requirements: 3.3, 9.1_
  - _Boundary: LegacyTimelineDeclarationCleaner_

- [ ] 9.6 Receiver Inspector を UI Toolkit で実装し、Edit で静的診断・操作ボタン・購読の所有を持たせる（timeline Editor Inspector）
  - Editor 用の binding 書込口（Undo.RecordObject(director) + SetDirty 付き）を実装する
  - CustomEditor の CreateInspectorGUI で (a) 上書きフィールド（Director / Bake。Bake がトラック参照と異なれば BakeOverrideDiffers を隣に表示）、(b) 領域ごとの Foldout に診断項目を重大度アイコン + 件名 + 直し方で表示（Ok は 1 行に畳む）、(c) Play 中はセッション状態・接続レイヤー・乗っ取り一覧、(d) ボタン「トラック binding を今設定」（Editor 書込口）/「今再ベイク」（9.2）/「旧 timeline 宣言を削除」（9.5。Edit で LegacyStateDeclaration があるときだけ有効）を構成する
  - Edit 評価は 7.1 で Profile を解決して Receiver の静的診断 overload を呼び、9.5 の Scan 結果を LayerConnection 領域（state 宣言は LegacyStateDeclaration Error、値 sink 宣言は LayerConnectionSkippedDeclared Info）に写し、BakeReferenceConflict / BakeLegacyExport / ProfileMismatch を検出したら Services の Watcher に MarkDirty して「自動再ベイク中」を併記、MarkDirty が UnsavedTimeline を返したら Bake 領域に Info で保存を案内する。Inspector 自身はデバウンスや再ベイクを持たない
  - 購読（Undo / hierarchyChanged / ObjectChangeEvents（Director・Profile SO・Receiver 関連のみ）/ Watcher の BakeUpdated / 診断の Changed / playModeStateChanged）は CreateInspectorGUI で登録し、root の DetachFromPanelEvent と OnDisable で解除（二重解除 no-op、各 handler 先頭に target null ガード）。再描画は 100 ms 後に合流
  - 新規 `FacialTimelineReceiverInspectorTests`（Medium。生成・保存・破棄の smoke のみ）と、既存 `TimelineDiagnosticsEvaluatorTests` に Edit 側で Conflict / ProfileMismatch が Watcher の IsPending を true にし UnsavedTimeline は false のまま、を追記する
  - 完了条件: Play を挟まずに Director バインド / Bake 有無・鮮度 / Profile 整合 / 配置 / 旧宣言 の判定結果と直し方が Inspector に出て、入力変化で更新される
  - _Depends: 9.1, 9.2, 9.5_
  - _Requirements: 3.3, 4.2, 5.1, 5.2, 5.3, 5.4, 5.5, 5.6, 9.1, 11.4_

- [ ] 9.7 (P) Timeline binding の PropertyDrawer を Slug + 有効フラグのみで実装する（timeline Editor Inspector）
  - CustomPropertyDrawer で Slug フィールド + 有効トグルを表示し、legacy フィールドが残っていれば HelpBox（「旧フィールドは再生に使われません。保存すると消えます」）を出す。ヘッダー要約提供 interface で「Timeline / 有効」を返す
  - 新規 `TimelineAdapterBindingDrawerTests`（Medium。生成・破棄の smoke と、legacy あり / なしで HelpBox の有無）を書く
  - 完了条件: Profile Inspector の AdapterBindings 一覧で Timeline binding が Slug と有効フラグだけを表示する
  - _Depends: 6.3, 9.2_
  - _Requirements: 2.1, 2.2, 2.4_
  - _Boundary: TimelineAdapterBindingDrawer_

---

## 第 3 段: Edit プレビューと Play の一致、REC Export ウィンドウ整理、既存 PlayMode の移行、ドキュメント（受け入れ条件 2 の「Edit プレビュー」と Req 7 / 10）

- [ ] 10. Edit プレビューを Play と同じ合成規則で描き、REC Export を TimelineAsset 1 つで完結させ、既存テストとドキュメントを新形式へ移行する
- [ ] 10.1 オフラインの LayerUseCase で Edit プレビューを合成する Compositor を実装し、Edit / Play の一致を PlayMode テストで固定する（timeline Editor）
  - controller / Profile SO / Profile / Bake / Timeline から ExpressionUseCase + LayerUseCase を構築し、レイヤーごとに値 sink と state sink を weight 1 で後付け接続、状態復元器に Bake のイベントを設定、ホスト BlendShape 名は 2.2 で公開した収集を使う。時刻評価は Bake 値を sink に書き → 状態を時刻へジャンプ → dt 0 で更新 → 合成出力を renderer へ書く。構築時に Profile 内容ハッシュを Bake と照合して ProfileCheck（Ok / ProfileMismatch）を保持し、描画可否は Locator が Found / OverrideUsed のときだけ true（ProfileCheck には依存しない）。キャッシュ再利用判定を提供する
  - 新規 `TimelinePreviewCompositorTests`（PlayMode Medium。8.1 の fixture を使う）に 同一スナップショット（SO 保存 → profile.json 書き出し → 再ベイク）で時刻 0 / 各 Clip の start・end ±1/60 s / 中点 / duration の Edit 合成と Director 再生の renderer 値が 0.01 以内、Gaze 回転が各成分 1e-3 以内、を書く
  - 完了条件: Timeline 以外の live 入力が無くレイヤー weight 既定の条件で、Edit 合成と Play の BlendShape 値が許容誤差内で一致する
  - _Requirements: 7.1, 7.6, 11.5_

- [ ] 10.2 Edit プレビューを Compositor 経由に置き換え、未構成時の 1 回警告と Profile 不一致の自動再ベイクを組み込む（timeline Editor）
  - 既存の Edit プレビューの BlendShape 単純加算を 10.1 の Compositor 呼び出しに置換し、Bake は 5.3 の Locator で解決、Compositor は controller ごとにキャッシュして BakeUpdated で破棄する。controller 未構成は例外ではなく 4.4 のゲートで 1 回警告してプレビューを継続（無言 return 廃止）。Locator が Conflict / LegacyExport なら描画せず MarkDirty（BakeReferenceInconsistent）、ProfileCheck が不一致なら描画を続けつつ Receiver の診断 Profile 領域に ProfileMismatch（Warning、「自動再ベイク中」併記）を書いて MarkDirty（ProfileMismatch）する
  - `TimelinePreviewCompositorTests` に JSON と SO を意図的に食い違わせた fixture（profile.json の Expression 値だけ書き換え）で Compositor が ProfileMismatch を報告しつつ描画し、同 fixture の Play セッションが ProfileMismatch（Warning）+ Active で同じ Bake の値を出す（不一致中も Edit / Play の renderer 値が一致）、MarkDirty → 再ベイク後に両方 Ok、を追記する。既存の Edit プレビューテストで Receiver 未構成が例外にならず警告 1 回であることを固定する
  - 完了条件: Clip を動かした直後に Edit プレビューが新しい Bake で描かれ、Edit でセッション開始が呼ばれない
  - _Requirements: 6.2, 7.2, 7.3, 7.5, 8.6, 11.5_

- [ ] 10.3 Edit プレビューの Gaze チャネル解決を index からチャネル id に変える（timeline Editor）
  - Bake の値チャネルの sub（REC source id）を GazeSourceIdConvention のチャネル id、または GazeChannel の明示 source id（左 / 右）との完全一致で解決する辞書に置き換え、index 結合を廃止する。Compositor の Gaze 評価もこの解決を使う
  - 既存 `FacialTimelinePreviewGazeTargetsTests`（Small）を チャネル id 解決（規約 id / 明示 source id / 一致なしは駆動しない / トラック順を入れ替えても同じ対応）へ書き換える
  - 完了条件: Gaze トラックの順序を入れ替えても目ボーンの対応が変わらない
  - _Requirements: 7.4_

- [ ] 10.4 REC Exporter の署名から Director / Receiver を撤去し、チャネル種別の判定理由を返す検出 API と Gaze 判定の拡張を実装する（timeline Editor）
  - Export の署名を 記録パス / Profile SO / 出力パス / 結果 / 既存 Timeline（任意）/ 種別上書き（任意、プログラム・テスト用途）に変更し、Director / Receiver への副作用を無くす
  - チャネル検出は source id ごとに 種別と理由（ExplicitGazeSourceId / ConventionGazeChannel / GazeProviderDeclaration / NonTwoAxisSamples / DefaultAnalog / Overridden）と軸数を返す。Gaze 判定順は GazeChannel の明示 source id 完全一致 → 規約 id が GazeChannels に存在 → Profile の各 binding の gaze 宣言（slug + channelId またはワイルドカード slug）→ 2 軸でなければ Analog（Warning 継続）→ 既定 Analog
  - 既存 `RecToTimelineExporterTests` / `RecToTimelineExportWorkflowTests`（Medium）の director / receiver 引数と Receiver の Bake / GenericBinding アサートを撤去し、新署名、検出理由 5 種、Director / Receiver 非依存、を書き換え・追記する。8.1 の fixture の Export 呼び出しも新署名へ更新する
  - 完了条件: Export が TimelineAsset 1 つで完結し、出力した ChannelSubId を再生側が不正扱いしない
  - _Requirements: 10.3, 10.4, 10.5_

- [ ] 10.5 REC Export ウィンドウから配線指定と Source Overrides を撤去し、検出結果と残り手順を表示する（timeline Editor Windows）
  - Director / Receiver の ObjectField と Source Overrides（Auto / Analog / Gaze）を撤去し、REC 読み込み後に 10.4 の検出結果（source id / 判定結果 / 理由）の読み取り専用リストを表示する。トリガー専用（Analog イベントを持たない）source は表示しない
  - Export 完了後にステータスへ「次の手順: 1) PlayableDirector に TimelineAsset をセット 2) FacialController と同じ GameObject に FacialTimelineReceiver を追加」を表示する
  - 既存のウィンドウテスト（`{Target}Tests` があれば追記、無ければ新規）で生成・破棄 smoke と、検出結果リストにトリガー専用 source が含まれないことを固定する
  - 完了条件: Export 時に Director / Receiver の指定欄が無く、上書き不可の行が表示されない
  - _Requirements: 10.1, 10.2, 10.5, 10.6_

- [ ] 10.6 (P) 既存 PlayMode 3 件の fixture を自動導出前提へ本移行し、GC ゲートを維持する（timeline Tests/PlayMode）
  - `TimelineLiveEquivalenceIntegrationTests` / `TimelineDegradationIntegrationTests` / `TimelineGcZeroGateTests` の fixture を、6.1 で最小撤去した reflection ヘルパーの残骸を含めて整理し、4.2 の導出 + 5.5 の Connector（または 8.1 の fixture）を直接組む形へ移行する。ログ文言一致のアサートは診断コードへ置換する
  - GC ゲートはセッション開始後の ProcessFrame で確保 0 を維持し、Pause / Resume（ReleaseAll → 再 Begin）で再確保しないことを追記する
  - 完了条件: 3 件に private への reflection が残らず、PlayMode 全件緑
  - _Requirements: 1.4, 11.7, 11.8_
  - _Boundary: timeline Tests/PlayMode 既存 3 fixture_

- [ ] 10.7 README / Documentation~ / CHANGELOG を新しい 4 手順と移行手順に更新する（timeline パッケージ）
  - README と Documentation~ に 4 手順（REC → Export → Director にセット → Receiver を追加）、Receiver Inspector の診断の読み方、旧設定の移行（`timeline:{layer}:state` 宣言は削除必須、値 sink 宣言は任意、旧 Export は Edit で開けば自動再ベイク、旧 Bake は一度 ProfileMismatch になる）を記載する
  - CHANGELOG に preview 段階の破壊的変更（Exporter 署名、Receiver の Configure 撤去、`:state` 宣言の非互換化、Bake の Profile 内容ハッシュ追加、Watcher のインスタンス化、AutoExport が同一内容で profile.json を書き直さなくなること、Analog 消費者の registry 再解決）を記載する
  - 完了条件: ドキュメントの手順どおりに操作すると追加設定なしで再生され、旧設定の直し方が参照できる
  - _Requirements: 1.2, 2.4, 3.3, 10.6, 10.7_
