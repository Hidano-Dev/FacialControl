# Requirements Document

## Project Description (Input)
Linear HID-144（親。子 HID-138 / HID-139 / HID-140 / HID-141 / HID-142 / HID-143）: Timeline 再生 UX の統合 — 「Profile は 1 つ、Receiver は 1 つ、Clip を動かせばそのまま動く」。REC で録画した表情・目線を REC Export の TimelineAsset として Play モードで再現するまでの設定が Profile SO / FacialTimelineReceiver / PlayableDirector / Bake / Export ウィンドウに飛び散っており、中身を熟知していないと使えない。設定を不要化・集約し、無言で止まる経路を無くす。

対象 Unity プロジェクト: `FacialControl/`。主パッケージ `com.hidano.facialcontrol.timeline`（Runtime: TimelineAdapterBinding / FacialTimelineReceiver / FacialTrackMixerBehaviour / FacialValueMixerBehaviour / Tracks / InputSources / FacialTimelineBakeAsset、Editor: TimelineBakeDirtyWatcher / FacialTimelineEditorPreview / RecToTimelineExporter / RecTimelineExportWindow / TimelineBakeService / BakeSimulationHarness）。必要に応じ core `com.hidano.facialcontrol` の InvalidIdValidator / SourcePortEnumerator / FacialController のレイヤー接続（inputSources 解決）/ AdapterBindingsListView に限定して触る。

方針1（HID-138 / HID-140）: Profile SO は REC 時に使ったものがそのまま使える。TimelineAdapterBinding は「Timeline からの受信を有効にするフラグ」程度に格下げ（Slug と有効/無効のみ。PropertyDrawer を用意して Profile Inspector に表示できる）。Target Layer Names / Channel Definitions は Profile から撤去し、再生開始時（BeginPlaybackSession）に TimelineAsset のトラック（FacialExpressionTrack 名、FacialValueTrack の ChannelSubId / ChannelKind / クリップ軸数）から導出する。レイヤーへの接続（Layer.inputSources への `timeline:{layer}` 宣言）はユーザーに書かせず Receiver が再生開始時に自動接続する。InvalidIdValidator が `timeline:` id を不正扱いしないようにする。Analog チャネルの Play 時の消費先を定義する（Gaze と同じ乗っ取り方式に揃える等、設計で決める）。

方針2（HID-139）: Timeline 再生に関わる設定・状態・診断は FacialTimelineReceiver に集約する。BakeAsset の手動登録を廃止し、PlayableDirector にバインドされた TimelineAsset から FacialTimelineBake を自動解決する（Track / Marker 側にサブアセット参照を持たせる方式を推奨。TimelineAsset 本体の継承は不採用）。Receiver.BakeAsset は任意の上書きに格下げ。Value sink の BlendShape 名は OnStart で固定せず BeginPlaybackSession 時に Bake から取る。Receiver の Inspector に Director バインド / Bake の有無と鮮度 / Profile との整合 / レイヤー接続 の診断結果と直し方を表示する。Exporter / DirtyWatcher がシーンオブジェクトを変更するときは Undo.RecordObject + EditorUtility.SetDirty。TimelineAdapterBinding.Dispose は自分が AddComponent した Receiver だけ破棄する。

方針3（HID-141）: Timeline ウィンドウでの Clip の移動・トリム・追加・削除を変更検知（ObjectChangeEvents / Undo.postprocessModifications / TimelineEditor コールバック）+ デバウンスで自動再ベイクし、保存や Play を挟まずに Edit プレビューと次の Play 再生の両方へ反映する。Play 終了後の「再ベイクされました」ダイアログを廃止し Bake をユーザーから見えない内部キャッシュに留める。Edit モードでは BeginPlaybackSession を呼ばず、Receiver 未構成は例外ではなく 1 回だけの警告にする。Bake と Runtime が同じ Profile ソース（StreamingAssets の profile.json / SO）を見るようにする。Edit プレビューと Play の結果を一致させる。

方針4（HID-142）: 無言の early return を残さない。Timeline binding が有効なのに sink がレイヤーに接続されていない / トラック名がどのレイヤーにも一致しない / Bake が無い / Receiver が FacialController と別 GameObject、を Play 開始時に 1 回ずつ明示する。`timeline:{layer}:state` sink の ContributeMask 長 0 による LayerInputSourceAggregator の ArgumentException（推測段階）を実在確認し、実在すれば修正する。

HID-143: REC Export ウィンドウの Source Overrides（Auto / Analog / Gaze）は、Channel Definitions が TimelineAsset から導出される前提で必要性を見直す。自動判定に REC 側の gaze 広告情報も使い、残すなら行ごとに判定結果と理由を表示し、上書きが効かない行（トリガー専用）は非表示かグレーアウトにする。Export の ChannelSubId（`slug:sub`）と binding の IsValidChannelId の id 形式不一致を解消する。Edit プレビューの Gaze チャネル対応を index ではなく id で行う。Export の出力は TimelineAsset 1 つで完結させ、Export 時の Director / Receiver 配線指定を不要にする。

受け入れ条件: (1) 「REC で録画 → REC Export → Director に TimelineAsset をセット → Receiver を FacialController に追加」の 4 手順だけで Play モードで表情・目線が再現される（Profile は録画時のまま）。(2) Timeline ウィンドウで Clip を動かした直後に Edit プレビューと次の Play 再生の両方が新しいタイミングになる。(3) 手順のどれかが欠けたとき Console か Receiver の Inspector に欠けている項目が表示される。(4) 上記を REC Export の出力をそのまま使う PlayMode end-to-end テスト（FacialController.Initialize → SO の AdapterBindings → レイヤー接続 → Director 再生 → SkinnedMeshRenderer の BlendShape 変化）で固定する。

スコープ外: レイヤー weight / 入力源 weight のランタイム変更の REC 対応（HID-80、別 spec `rec-weight-coverage` が並走中。core の LayerUseCase / FacialController の weight API は触らない）。Gaze は表情と一体不可分なので Non-Goals にしない。既存テスト方針は `docs/test-policy.md` / `docs/testing.md`（全 fixture に Small / Medium 属性、`pwsh ./scripts/check-test-sizes.ps1`）に従う。

## Introduction

本仕様は、REC で録画した表情・目線を REC Export の TimelineAsset として Play モードで再現するまでのユーザー体験を「Profile は 1 つ、Receiver は 1 つ、Clip を動かせばそのまま動く」に統合する。現状は Profile SO の TimelineAdapterBinding（Target Layer Names / Channel Definitions）、Layer.inputSources への `timeline:{layer}` 宣言、FacialTimelineReceiver への BakeAsset 手動登録、PlayableDirector、REC Export ウィンドウの配線指定に設定が分散し、どれかが欠けると無言で再生が止まる。本仕様では設定を TimelineAsset からの導出と Receiver への集約で不要化し、Clip 編集を自動再ベイクで即時反映し、欠落をすべて明示する。

対象 Unity プロジェクトは `FacialControl/`（Unity 6000.3.19f1）。主たる変更対象は `FacialControl/Packages/com.hidano.facialcontrol.timeline`（Runtime / Editor / Tests）で、core `com.hidano.facialcontrol` は InvalidIdValidator / SourcePortEnumerator / FacialController のレイヤー接続（inputSources 解決）/ AdapterBindingsListView に限定して変更する。

本文書中の「Timeline 統合」は `com.hidano.facialcontrol.timeline` パッケージ全体（Runtime + Editor）を指す。個別コンポーネントの責務を特定できる箇所は、そのコンポーネント名（FacialTimelineReceiver、TimelineAdapterBinding、TimelineBakeDirtyWatcher 等）を主語にする。

## Boundary Context

- **In scope**:
  - Profile SO 側設定（Target Layer Names / Channel Definitions / `timeline:` の inputSources 宣言）の撤去と、TimelineAsset からの再生時導出
  - FacialTimelineReceiver への設定・状態・診断の集約と Inspector 診断表示
  - Bake の自動解決・内部キャッシュ化・Clip 編集時の自動再ベイク
  - Edit プレビューと Play 再生の結果一致
  - 無言 early return の排除と Play 開始時の明示診断
  - REC Export ウィンドウの Source Overrides 見直し、ChannelSubId と IsValidChannelId の id 形式統一、Export 出力の TimelineAsset 1 つへの完結
  - Expression（トリガー）・連続値（Analog）・Gaze の 3 種すべて（Gaze は表情と一体不可分として同じ手順・同じ診断で扱う）
  - REC Export の出力をそのまま使う PlayMode end-to-end テスト
- **Out of scope**:
  - レイヤー weight / 入力源 weight のランタイム変更の REC 対応（HID-80、別 spec `rec-weight-coverage`）。core の LayerUseCase / FacialController の weight API は変更しない
  - 音声解析・リップシンク本体・OSC 伝送など Timeline 以外の入力経路の変更
  - Timeline の新しいトラック種別・Clip 種別の追加
  - ランタイム UI の提供
- **Adjacent expectations**:
  - `rec-weight-coverage` spec が並走する。本仕様は REC の記録フォーマット（.fcrec）と core の weight API に手を入れず、REC Export が出力する TimelineAsset の構造変更は本仕様側で完結させる
  - core の `FacialController` のレイヤー接続（inputSources 解決）は、Timeline 統合が再生開始時に入力源を自動接続・解放できる経路を提供する範囲で変更する。既存の OSC / InputSystem / LipSync 等の binding の接続挙動は変えない
  - 既存テスト方針（`docs/test-policy.md` / `docs/testing.md`）と Small / Medium / Large のサイズ属性、`pwsh ./scripts/check-test-sizes.ps1` の静的チェックに従う

## Requirements

### Requirement 1: 最小手順での Play 再現
**Objective:** As a Unity エンジニア, I want 「REC で録画 → REC Export → Director に TimelineAsset をセット → Receiver を FacialController に追加」の 4 手順だけで Play モードに録画内容が再現されること, so that 内部構造（Bake / sink / inputSources）を知らなくても Timeline 再生を使える

#### Acceptance Criteria
1. When ユーザーが REC Export で生成した TimelineAsset を PlayableDirector にバインドし、FacialController と同じ GameObject に FacialTimelineReceiver を追加し、録画時と同じ Profile SO で Play モードを開始する, the Timeline 統合 shall 追加設定なしに表情（Expression トリガーと連続値）と目線（Gaze）を SkinnedMeshRenderer の BlendShape 変化として再現する
2. The Timeline 統合 shall 上記 4 手順以外の手入力（Target Layer Names / Channel Definitions / Layer.inputSources への `timeline:` 宣言 / BakeAsset の手動登録 / Export 時の Director・Receiver 配線指定）を要求しない
3. The Timeline 統合 shall Gaze を表情と同じ 4 手順・同じ診断の中で扱い、Gaze のみに追加の設定手順を要求しない
4. While Play モードで Timeline を再生している, the Timeline 統合 shall 毎フレーム処理でヒープ確保を発生させない（再生セッション開始時の確保は許容する）
5. When 録画時に使った Profile SO を変更せずに Play を開始する, the Timeline 統合 shall Profile SO に対する Timeline 専用の編集を要求せず再生する

### Requirement 2: Profile 側設定の撤去と TimelineAdapterBinding の格下げ
**Objective:** As a Unity エンジニア, I want TimelineAdapterBinding が「Timeline からの受信を有効にするフラグ」程度の設定だけを持つこと, so that Profile SO を REC 時のまま流用でき、Profile に Timeline 固有の構成を書かなくてよい

#### Acceptance Criteria
1. The TimelineAdapterBinding shall シリアライズ設定として Slug と有効/無効フラグのみを保持し、Target Layer Names と Channel Definitions を Profile 側に保持しない
2. When Profile Inspector の AdapterBindings 一覧に TimelineAdapterBinding が表示される, the Profile Inspector shall PropertyDrawer により Slug と有効/無効フラグのみを表示する
3. When 再生セッションが開始される, the FacialTimelineReceiver shall PlayableDirector にバインドされた TimelineAsset のトラック構成（FacialExpressionTrack 名、FacialValueTrack の ChannelSubId / ChannelKind / クリップ軸数）から対象レイヤーと値チャネル定義を導出する
4. If 旧形式の Profile SO（Target Layer Names / Channel Definitions を保持）が読み込まれた, then the TimelineAdapterBinding shall 旧フィールドの値に依存せず再生を継続し、旧フィールドが残っていることを Console に 1 回警告する（旧データの移行方式は設計が判定し根拠を文書化する）
5. When 入力源 id の検証（InvalidIdValidator）が `timeline:` プレフィックスの id を評価する, the InvalidIdValidator shall その id を不正扱いしない
6. While TimelineAdapterBinding の有効/無効フラグが無効である, the TimelineAdapterBinding shall sink の登録・Receiver の生成・レイヤー接続を行わず、無効であることを Receiver の診断に表示する
7. If Profile SO に TimelineAdapterBinding が含まれていない状態で FacialTimelineReceiver が配置された GameObject の Play を開始する, then the FacialTimelineReceiver shall Timeline binding が無いことと追加手順を Console に 1 回明示する

### Requirement 3: レイヤーへの自動接続とチャネルの消費先
**Objective:** As a Unity エンジニア, I want Timeline の sink が再生開始時にレイヤーへ自動接続されること, so that Layer.inputSources に `timeline:{layer}` を手で宣言しなくてよい

#### Acceptance Criteria
1. When 再生セッションが開始される, the FacialTimelineReceiver shall 導出した各レイヤーに対し `timeline:{layer}` と `timeline:{layer}:state` の sink を FacialController のレイヤー入力源へ自動接続し、ユーザーに Layer.inputSources の宣言を要求しない
2. When 再生セッションが終了する（Director の停止、ReleaseAll、Play 終了、Receiver の無効化・破棄）, the FacialTimelineReceiver shall 自動接続した sink を取り外し、接続前のレイヤー入力源構成を復元する
3. If Layer.inputSources に同じ id の `timeline:` 宣言がすでに存在する, then the FacialTimelineReceiver shall 既存宣言を優先して重複登録せず、自動接続をスキップしたことを診断に記録する
4. The Timeline 統合 shall Analog（非 Gaze）チャネルの Play 時の消費先を定義する（Gaze と同じ既存入力源の乗っ取り方式に揃えるか、レイヤー入力源として接続するかは設計が判定し根拠を文書化する）
5. If Analog チャネルの消費先が解決できない, then the FacialTimelineReceiver shall 該当チャネルの ChannelSubId と解決できなかった理由を Console に 1 回明示し、他のチャネルの再生を継続する
6. When Gaze チャネルを含む TimelineAsset を再生する, the FacialTimelineReceiver shall 既存の Gaze 乗っ取り（takeover）方式で Gaze 入力源を置き換え、再生セッション終了時に元の入力源を復元する
7. When Gaze チャネルの乗っ取り先（takeover 対象の入力源 id）を決定する, the FacialTimelineReceiver shall ユーザーの手入力ではなく TimelineAsset と Profile の情報から導出する（導出規則は設計が判定し根拠を文書化する）

### Requirement 4: Bake の自動解決と内部キャッシュ化
**Objective:** As a Unity エンジニア, I want Bake が TimelineAsset から自動で解決され、ユーザーから見えない内部キャッシュとして扱われること, so that BakeAsset を手で登録・更新する必要がない

#### Acceptance Criteria
1. When 再生セッションが開始される, the FacialTimelineReceiver shall PlayableDirector にバインドされた TimelineAsset から FacialTimelineBake を Editor 専用 API を使わずに解決する（Track / Marker 側にサブアセット参照を持たせる方式を推奨とし、TimelineAsset 本体の継承は採用しない。最終方式は設計が判定し根拠を文書化する）
2. The FacialTimelineReceiver shall BakeAsset フィールドを任意の上書きとして扱い、未設定時は自動解決結果を、設定時は上書き値を使用する
3. When 再生セッションが開始される, the FacialTimelineReceiver shall Value sink の BlendShape 名を OnStart 時点で固定せず、解決した Bake から取得して確定する
4. The Timeline 統合 shall Bake をユーザーが直接操作しない内部キャッシュとして扱い、Project ウィンドウでの手動登録・手動更新・手動削除の操作手順を要求しない
5. When Bake を生成または更新する, the TimelineBakeService shall Runtime の再生が参照する Profile ソース（StreamingAssets の profile.json または Profile SO）と同一のソースを参照する（どちらを正とするかの決定規則は設計が判定し根拠を文書化する）
6. If 解決した Bake のソースハッシュが現在の TimelineAsset と Profile から計算したハッシュと一致しない, then the FacialTimelineReceiver shall 鮮度不一致を診断に記録し、値再生を継続するか停止するかを Console に 1 回明示する
7. When REC Export が TimelineAsset を出力する, the RecToTimelineExporter shall 再生側が Bake を自動解決できる情報を同じ TimelineAsset 内に含める

### Requirement 5: Receiver への集約と Inspector 診断
**Objective:** As a Unity エンジニア, I want Timeline 再生に関わる設定・状態・診断が FacialTimelineReceiver の Inspector で一望できること, so that 何が欠けているかと直し方をその場で把握できる

#### Acceptance Criteria
1. The FacialTimelineReceiver shall Timeline 再生に関わる設定・状態・診断を単一コンポーネントとして保持し、Profile SO / PlayableDirector / REC Export ウィンドウへ Timeline 再生のための設定を分散させない
2. When FacialTimelineReceiver が Inspector に表示される, the Receiver Inspector shall 次の診断項目を判定結果（問題なし / 要対応）と直し方の説明つきで表示する: (a) PlayableDirector のバインド有無とバインド先 TimelineAsset、(b) Bake の有無と鮮度、(c) Profile との整合（Timeline binding の有無と有効性、トラック名とレイヤー名の一致）、(d) レイヤー接続状態、(e) FacialController との同一 GameObject 配置
3. While Edit モードである, the Receiver Inspector shall 静的に判定できる診断項目（Director バインド、Bake の有無・鮮度、Profile 整合、配置）を Play を挟まずに表示する
4. When 診断項目の入力が変化する（Director のバインド変更、再ベイク完了、Profile SO の変更、Receiver の移動）, the Receiver Inspector shall 表示を更新する
5. While Play モードで再生セッションが開始されている, the Receiver Inspector shall 実際に接続されたレイヤーと sink、Gaze 乗っ取りの状態を表示する
6. The Receiver Inspector shall UI Toolkit で実装する

### Requirement 6: Clip 編集の自動再ベイクと即時反映
**Objective:** As a Unity エンジニア, I want Timeline ウィンドウで Clip を動かした直後に Edit プレビューと次の Play 再生が新しいタイミングになること, so that 保存や Play を挟まずに編集結果を確認できる

#### Acceptance Criteria
1. When Timeline ウィンドウで FacialExpressionTrack / FacialValueTrack の Clip が移動・トリム・追加・削除される, the TimelineBakeDirtyWatcher shall 変更を検知し、デバウンス後に自動再ベイクする（ObjectChangeEvents / Undo.postprocessModifications / TimelineEditor コールバックのどれを組み合わせるか、およびデバウンス間隔は設計が判定し根拠を文書化する）
2. When 自動再ベイクが完了する, the Timeline 統合 shall 保存や Play を挟まずに Edit プレビューの表示を新しい Bake で更新する
3. When 自動再ベイクの完了後に Play モードを開始する, the FacialTimelineReceiver shall 新しい Bake を用いて再生する
4. The Timeline 統合 shall Play 終了後の「再ベイクされました」ダイアログを表示しない
5. While 自動再ベイクがデバウンス待ちまたは実行中である, the TimelineBakeDirtyWatcher shall 連続した編集ごとに再ベイクを重複実行しない
6. If 自動再ベイクが失敗する, then the TimelineBakeDirtyWatcher shall 失敗理由を Console に出し、前回の Bake を保持したまま次の変更検知を継続する
7. When Clip 編集が Undo / Redo される, the TimelineBakeDirtyWatcher shall 通常の編集と同様に変更として検知し再ベイクする

### Requirement 7: Edit プレビューと Play 再生の一致
**Objective:** As a Unity エンジニア, I want Edit プレビューで見た結果が Play モードでも同じになること, so that Edit で確認した表情・目線を信頼して Play へ進める

#### Acceptance Criteria
1. The Timeline 統合 shall 同一の TimelineAsset・Profile・時刻に対し、Edit プレビューと Play 再生が同じ BlendShape 値を出力する（比較の許容誤差と比較対象の時刻サンプルは設計が判定し根拠を文書化する）
2. While Edit モードである, the FacialTimelineEditorPreview shall BeginPlaybackSession を呼ばない
3. If Edit モードで FacialTimelineReceiver が未構成である, then the FacialTimelineEditorPreview shall 例外を投げず、1 回だけ警告してプレビューを継続する
4. When Edit プレビューが Gaze チャネルを解決する, the FacialTimelineEditorPreview shall トラックの index ではなくチャネル id で解決する
5. The FacialTimelineEditorPreview shall Bake および Runtime と同一の Profile ソースを参照する
6. When Edit プレビューが Profile の情報を要する（レイヤー排他モード、Expression の定義）, the FacialTimelineEditorPreview shall Profile を参照して Play 再生と同じ合成規則を適用する

### Requirement 8: 無言 early return の排除と Play 開始時の明示診断
**Objective:** As a Unity エンジニア, I want 手順のどれかが欠けたときに欠けている項目が Console または Receiver の Inspector に表示されること, so that 無言で止まる原因を推測せずに直せる

#### Acceptance Criteria
1. If Play 開始時に Timeline binding が有効であるが sink がどのレイヤーにも接続されていない, then the FacialTimelineReceiver shall 未接続のレイヤー名と直し方を Console に 1 回明示する
2. If Play 開始時に TimelineAsset の FacialExpressionTrack 名が Profile のどのレイヤー名にも一致しない, then the FacialTimelineReceiver shall 不一致のトラック名と Profile のレイヤー名一覧を Console に 1 回明示し、該当トラックの再生をスキップして他トラックの再生を継続する
3. If Play 開始時に Bake が解決できない, then the FacialTimelineReceiver shall Bake が無いことと再ベイクの手順を Console に 1 回明示し、値再生が無効になることを示す
4. If Play 開始時に FacialTimelineReceiver が FacialController と別の GameObject に配置されている, then the FacialTimelineReceiver shall 配置の誤りと直し方を Console に 1 回明示する
5. If Play 開始時に PlayableDirector が存在しない、または TimelineAsset がバインドされていない, then the FacialTimelineReceiver shall その旨を Console に 1 回明示する
6. The Timeline 統合 shall 上記のケースを含む異常経路において、ログも診断表示もなく処理を打ち切る経路を持たない
7. The Timeline 統合 shall 同一原因の警告を同じ再生セッション中に繰り返さない（1 セッション 1 回）
8. The Timeline 統合 shall `timeline:{layer}:state` sink の ContributeMask 長 0 が LayerInputSourceAggregator で ArgumentException を起こすかをテストで実在確認し、実在する場合は ContributeMask 長を他の入力源と揃えて修正し、実在しない場合は確認結果を設計文書に記録する
9. When Play 開始時の診断がすべて問題なしである, the FacialTimelineReceiver shall 診断状態（Inspector から参照できる値）を問題なしとして保持し、不要な警告を出さない

### Requirement 9: Editor 操作の Undo / Dirty とコンポーネントのライフサイクル
**Objective:** As a Unity エンジニア, I want Exporter / DirtyWatcher によるシーン変更が Undo 可能で保存対象として追跡され、ユーザーが置いた Receiver が勝手に消えないこと, so that Editor 操作の予期しない副作用が起きない

#### Acceptance Criteria
1. When RecToTimelineExporter または TimelineBakeDirtyWatcher がシーン上のオブジェクトやコンポーネントを変更する, the Timeline 統合 shall 変更前に Undo.RecordObject を呼び、変更後に EditorUtility.SetDirty でダーティにする
2. When TimelineAdapterBinding.Dispose が呼ばれる and FacialTimelineReceiver を TimelineAdapterBinding 自身が AddComponent していた, the TimelineAdapterBinding shall その Receiver を破棄する
3. When TimelineAdapterBinding.Dispose が呼ばれる and FacialTimelineReceiver をユーザーが配置していた, the TimelineAdapterBinding shall ReleaseAll で接続と乗っ取りを解放し、Receiver コンポーネントは破棄しない
4. When FacialTimelineReceiver が無効化または破棄される, the FacialTimelineReceiver shall 自動接続した sink と Gaze 乗っ取りをすべて解放する

### Requirement 10: REC Export ウィンドウの見直しと id 形式の統一
**Objective:** As a Unity エンジニア, I want REC Export の出力が TimelineAsset 1 つで完結し、再生側で不正扱いされないこと, so that Export 後に Director / Receiver の配線や id の手直しをしなくてよい

#### Acceptance Criteria
1. The RecTimelineExportWindow shall Channel Definitions が TimelineAsset から導出される前提で Source Overrides（Auto / Analog / Gaze）の必要性を見直し、不要なら撤去し、残す場合は行ごとに自動判定結果とその理由を表示する（存続の判定は設計が行い根拠を文書化する）
2. Where Source Overrides を残す, the RecTimelineExportWindow shall 上書きが効かない行（トリガー専用の入力源）を非表示またはグレーアウトにする
3. When チャネル種別（Analog / Gaze）を自動判定する, the RecToTimelineExporter shall REC 側の gaze 広告情報を判定に用いる
4. The Timeline 統合 shall Export の ChannelSubId と binding 側 IsValidChannelId の id 形式を統一し、Export が出力した id を再生側が不正扱いしない（`slug:sub` 形式を許容するか sub のみに正規化するかは設計が判定し根拠を文書化する）
5. The RecToTimelineExporter shall 出力を TimelineAsset 1 つで完結させ、Export 時に PlayableDirector / FacialTimelineReceiver の配線指定を要求しない
6. When Export が完了する, the RecTimelineExportWindow shall 残りの手順（Director へのセット、Receiver の追加）を表示する
7. If 既存の Export 出力（本仕様以前の形式の TimelineAsset）が再生される, then the FacialTimelineReceiver shall 旧形式であることを診断に表示し、再 Export で解決できることを示す

### Requirement 11: end-to-end PlayMode テストとテスト方針準拠
**Objective:** As a 開発者, I want 受け入れ条件を REC Export の出力をそのまま使う PlayMode end-to-end テストで固定すること, so that 設定の分散や無言停止が再発しない

#### Acceptance Criteria
1. The Timeline 統合 shall REC Export の出力 TimelineAsset をそのまま使用する PlayMode end-to-end テストを持ち、FacialController.Initialize → Profile SO の AdapterBindings → レイヤー接続 → PlayableDirector 再生 → SkinnedMeshRenderer の BlendShape 変化 の経路を検証する
2. The end-to-end テスト shall Expression（トリガー）・連続値（Analog）・Gaze の 3 種が再現されることを検証する
3. The end-to-end テスト shall Clip のタイミング変更と再ベイクの後に再生結果のタイミングが変わることを検証する
4. The テスト群 shall 手順欠落（Timeline binding 無効 / Receiver 未配置 / Bake 解決不可 / トラック名不一致 / Receiver が別 GameObject）の各ケースで診断状態が要対応になることを、ログ文言の完全一致ではなく診断状態の値で検証する
5. The テスト群 shall Edit プレビューと Play 再生の BlendShape 値が一致することを検証する
6. The テスト群 shall 全 fixture に Small / Medium / Large のサイズ属性を 1 つ付け、`pwsh ./scripts/check-test-sizes.ps1` を通過する
7. The テスト群 shall テスト対象クラス単位の既存 `{Target}Tests.cs` に追記し、spec や task 単位のテストファイルを新設しない（新規クラスに対する新規ファイルは除く）
8. The テスト群 shall PlayMode を MonoBehaviour ライフサイクル・PlayableDirector 再生・フレーム進行が必要なテストに限り、トラック導出・id 検証・Bake 解決などのロジックは EditMode で検証する
