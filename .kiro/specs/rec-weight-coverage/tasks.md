# Implementation Plan

対象 Unity プロジェクト: `FacialControl/`（パッケージ `com.hidano.facialcontrol` / `com.hidano.facialcontrol.rec` / `com.hidano.facialcontrol.inputsystem`（Tests のみ）/ `com.hidano.facialcontrol.timeline`（Editor の契約追随のみ））。テストは EditMode を優先し、MonoBehaviour ライフサイクル・フレーム進行・InputSystem・実 I/O を要するものだけ PlayMode に置く。全 fixture に `[SmallTest]` / `[MediumTest]` を付け `SizedTestFixture` を継承する。

- [ ] 1. 基盤: core の weight 観測・遮断契約
- [x] 1.1 weight 観測契約とバス配信を追加する
  - レイヤー weight / 入力源 weight の変化を通知する観測契約（`ILayerWeightObserver`）、遮断・基準確立・注入・収集の契約（`IWeightInjectionGate`、sourceIdx 0 の予約スロット id `@expression` を持つ `WeightSlotIds`）、基準収集用の値構造体（`LayerWeightEntry` / `InputSourceWeightEntry`）を core Domain に置く
  - 1 体スコープの入力観測者契約に weight の 2 メソッドを追加し、観測バス契約が weight 観測契約を継承するようにする。観測バスは既存メソッドと同じく観測者ゼロで早期 return・publish 中の購読変更を遅延適用・観測者の例外を隔離して配信する
  - リポジトリ内で入力観測者 / 観測バス契約を実装している Fake（rec Tests EditMode / PlayMode、core Tests、timeline Tests）をコンパイルが通るよう追随させる（挙動は追加メソッドの no-op 記録のみ）
  - 観測バスの Small テストで、weight 2 メソッドが観測者へ届くこと・観測者ゼロで何もしないこと・例外が隔離されることが緑になる
  - _Requirements: 2.1, 2.2, 2.6, 3.1, 3.2, 9.3_

- [x] 1.2 入力源 weight バッファにライブ書込ゲートを追加する
  - 入力源 weight のダブルバッファに「ライブ書込の遮断 / 解除」「遮断を迂回する書込」を追加する。遮断は in-flight カウンタ + フラグ + 有界スピン待ちで行い、遮断の呼出が返った後にライブ書込が read 側へ到達しないことを保証する。遮断・解除は冪等
  - 単発ライブ書込とバルク commit の両方が in-flight カウンタに参加する。バルク書込スコープは commit 時点で遮断中なら蓄積を破棄し dirty を進めない（遮断前に開いたスコープの遮断後 commit も破棄）。遮断中の単発ライブ書込は値を変えず dirty も進めない
  - 容量拡張（resize）も同じフェンスで保護する: Resizing フラグを立て in-flight 0 を待ってから配列を差し替え、resize 中に到達したライブ書込は破棄する。遮断中の resize は遮断状態を維持する
  - 遮断未使用時の `SetWeight` の追加コストは in-flight の Interlocked 2 回とフラグ読取 1 回に限る（既存の任意スレッド契約を維持）
  - Small テストで「遮断中のライブ書込は swap 後も読めない」「迂回書込は swap 後に読める」「遮断中の bulk commit は破棄される」「遮断前に開いたスコープの遮断後 commit も破棄される」「遮断中の resize は遮断を維持する」「遮断・解除の冪等」「解除後のライブ書込が反映される」が緑になる（遮断点がバッファの入口にあるため、呼出元パッケージを改修せずに全ライブ書込が遮断される）
  - _Requirements: 5.1, 5.3, 5.4, 6.1, 9.2_

- [x] 1.3 入力源 weight ゲートの競合テストを追加する
  - ワーカースレッド複数本がライブ書込を連打する中で、メインスレッドが遮断 → 迂回書込で基準値を設定 → swap → 読取、を多数回反復し、読取値が常に基準値であることを検証する Medium（EditMode）テストを追加する（スレッドを使うため Small にしない。理由をコメントに書く）。同じ形で「バルク commit 連打 × 遮断」「単発ライブ書込連打 × resize（例外・配列破壊なし・既存スロット weight 保持）」も検証する。LayerUseCase 経由（ワーカーがスロット weight を連打する中で gate の遮断 → レイヤー・スロット両方の基準設定 → 重み更新 → 両系統が基準値）の反復検証は 2.2 の後に追加する（`_Depends: 2.2_`）
  - テストが安定して緑であり、`pwsh ./scripts/check-test-sizes.ps1` が通る
  - _Requirements: 5.3, 5.5, 10.9_

- [ ] 2. コア: LayerUseCase の weight 面と FacialController 配線
- [x] 2.1 LayerUseCase に消費点の weight 観測を実装する
  - 観測者を後付け設定できるようにし、設定時に前回通知値を現在値へ同期（通知なし）する。毎フレームの重み更新で、Aggregate 直後にレイヤー weight と各 (layer, source) スロットの weight を前回通知値とビット比較し、変化したものだけ観測者へ通知する（レイヤー昇順 → スロット昇順、1 対象につき高々 1 回）
  - スロットのキーは sourceIdx 0 が予約 id `@expression`、sourceIdx ≥ 1 がレイヤー入力源 registry のスロット id（宣言 id）。レイヤーはレイヤー名。文字列は既存参照を渡し、毎フレームの確保を行わない
  - 前回通知値の配列は構築時に事前確保し `NaN` を未観測の番兵とする。観測者が未設定なら比較ループを実行しない
  - Small テストで「同一フレーム複数書込は最終値 1 回」「同値は非通知」「接続時は非通知で同期」「sourceIdx 0 は `@expression`、宣言スロットは宣言 id で通知」「観測者なしでは通知配列に触れない」が緑になる
  - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5, 2.6, 2.7, 3.3, 9.2, 9.5_

- [x] 2.2 LayerUseCase に weight の遮断・基準確立・注入・収集を実装する
  - `IWeightInjectionGate` を実装する: 遮断（レイヤー weight のメインスレッドフラグ + 入力源 weight バッファの遮断、冪等）、宣言値へのリセット（全レイヤー 1、全スロットは宣言 weight、sourceIdx 0 は 1。通知なし）、基準設定（遮断迂回・通知なし = 前回通知値も更新）、注入（遮断迂回・前回通知値は更新せず次フレームで変化として通知）、収集（全レイヤー / 全スロットの実効値）
  - ライブの `SetLayerWeight` は遮断中に no-op（辞書も更新しない）。スロットの宣言 weight を保持する配列を構築時に埋める
  - 未知のレイヤー名 / スロット id に対する基準・注入は false を返し状態を変えない
  - 遮断は「レイヤー側フラグ → 入力源 weight バッファの遮断（フェンス）」の順で行い、バッファ遮断の戻りを両系統共通の線形化点とする（レイヤー weight のライブ書込はメインスレッド専用）。解除は逆順
  - Small テストで「遮断中の SetLayerWeight は no-op」「遮断後にレイヤー weight とスロット weight の両方をライブ書込しても次の重み更新でどちらも基準値のまま」「注入は遮断中でも反映され次フレームに通知（再生中の再記録に注入が残る）」「基準設定とリセットは通知しない」「未知スロットは false」「収集が全スロットを安定キーで返す」が緑になる
  - _Requirements: 3.5, 4.5, 4.6, 5.1, 5.2, 5.6, 6.1, 6.6, 9.1, 9.7_

- [x] 2.3 後付けバインドと解除の遮断中挙動を実装する
  - 遮断中でないときの後付けバインドは従来どおり（既存スロットの置換で宣言 weight を再適用、新規スロットは初期 weight）。遮断中は既存スロットの置換で weight を書き換えない（宣言 weight は配列にだけ記録）。新規スロットの追加は遮断中でも初期 weight（宣言値）を遮断を迂回して書き、宣言 weight 配列と前回通知値配列（未観測）を拡張する。解除時の詰め直しも遮断を迂回し、当該レイヤーの前回通知値を未観測へ戻す
  - 既存テスト `BindLateInputSource_ReplacingExistingId_KeepsOtherSourceWeights` / `BindLateInputSource_AppliesDeclaredWeight_ScalesOutput` が緑のまま、新規 Small テストで「遮断中でない置換は宣言 weight を再適用する」「遮断中の置換は現在の weight を維持する」「遮断中の新規スロットは宣言 weight で参加する」「遮断中の解除で残るスロットの weight が詰め直され再通知される」が緑になる
  - _Requirements: 6.3, 6.7, 9.1_

- [x] 2.4 レイヤー名重複の読み捨てと一意性フラグを実装する
  - JSON パーサと SO コンバータで `layers` のレイヤー名重複を検出し、後続の重複レイヤーを読み捨てて Warning を 1 回出す（既存の `inputSources` / `gaze.channels` 重複と同じ流儀。例外にしない）
  - LayerUseCase はパイプライン構築時にレイヤー名の一意性を計算し、weight ゲート契約の `LayerNamesAreUnique` として公開する（REC 側はこれが false のとき録画・再生を開始しない。4.2 / 4.3 で配線）
  - Small テストで「重複名の JSON / SO から構築した profile の Layers 名が一意で Warning が出る」「直接構築した重複名 profile では `LayerNamesAreUnique` が false、一意なら true」が緑になる
  - _Requirements: 2.6, 4.1, 4.5_

- [x] 2.5 FacialController に gate 公開と観測者着脱を配線する
  - 初期化済みのとき weight ゲートを公開するプロパティを追加し、LateUpdate の観測者有無のエッジ検出で LayerUseCase の weight 観測者（観測バス）を着脱する
  - 既存の `SetLayerWeight` / `SetInputSourceWeight` / `BeginInputSourceWeightBatch` は変更しない
  - 既存の `FacialControllerGcZeroGateTests` が緑のまま（観測者ゼロで通知ループが走らない）、EditMode テストで初期化前は gate が null・初期化後に非 null であることが緑になる
  - _Requirements: 2.1, 9.2, 9.4_

- [ ] 3. rec Domain: weight レコード・基準・5 ポート契約
- [ ] 3.1 (P) weight のイベント種別・基準エントリ・id 表を追加する
  - レコード種別に weight の時刻付き 2 種（レイヤー / 入力源）と基準 2 種を追加し、イベント構造体にレイヤー id index と `Layer` の id 定義種別、新 factory（weight 種別は float ペイロード 1 個、時刻付き判定に含める）を追加する
  - ヘッダ flags に weight 基準の必須ビット（bit1）を追加する
  - 基準状態にレイヤー weight エントリと入力源 weight エントリ（重複は例外）、検索メソッドを追加する。既存コンストラクタは空で委譲する
  - id 表にレイヤー id を追加し、記録側とライター側が同じ順序になる seed（基準のレイヤー名とスロット id）を実装する
  - Small テストで factory / 重複拒否 / seed 順序 / `Layer` 種別の定義が緑になる
  - _Requirements: 4.1, 4.2, 7.1, 7.3_
  - _Boundary: rec Domain Models（RecEvent / RecEventKind / RecHeaderFlags / RecBaselineState / RecIdTable）_

- [ ] 3.2 `.fcrec` の weight レコード読み書きとヘッダ必須ビットを実装する
  - タイムラインモデルにレイヤー id 一覧と weight イベントの index 検証を追加する（既存コンストラクタは空で委譲）
  - バイナリ形式に weight の 4 種と `Layer` id 定義の serialize / deserialize、サイズ計算、基準先行不変条件への包含、同一対象の基準重複拒否を実装する。必須ヘッダ flags を bit0 | bit1 にし、writer は常に書き、reader は欠落をレコード走査前に読込エラーにする。未知 kind はエラーのまま
  - Small テストで「weight 基準と時刻付きイベントのビット一致ラウンドトリップ」「ヘッダに bit1 が無いファイルはレコード内容に関わらず拒否」「weight 基準が時刻付きより後なら拒否」「同一対象の基準重複は拒否」「未知レイヤー index は拒否」が緑になる
  - _Requirements: 3.4, 4.3, 7.1, 7.2, 7.3, 7.4, 7.5, 7.8_

- [ ] 3.3 途中再生の畳み込みとスケジューラの weight 配信を実装する
  - 途中再生の基準構築で weight の時刻付きイベントを基準へ畳み込み、レイヤー / スロットの最終値を基準にする
  - イベント訪問者契約に weight の 2 メソッドを追加し、スケジューラが weight イベントをレイヤー名 / スロット id と値で配信する
  - Small テストで「オフセット前の weight イベントが最終値に畳まれる」「weight イベントが訪問者へ配信される」が緑になる
  - _Requirements: 6.2, 7.6_

- [ ] 3.4 weight 注入ポート契約と 5 ポートの再生ユースケースを実装する
  - 共通ライフサイクル契約を継承する weight 注入ポート契約（レイヤー weight / 入力源 weight の注入メソッド）を追加する
  - 再生ユースケースに 5 ポートコンストラクタを追加し、確立順（weight → trigger → expression → analog → valueProvider）と解放順（trigger → expression → analog → valueProvider → weight）の 2 配列で preflight・確立・ロールバック・解放を回す。ロールバックは確立済みの逆順。`Completed` からの再開時の全解放も解放順。既存 4 ポート / 2 ポートコンストラクタは Null weight ポートへ委譲する。weight イベントの訪問で weight ポートへ注入し、欠落 expressionId のフィルタは weight 基準を無加工で引き継ぐ
  - 既存 4 ポート / 2 ポートコンストラクタは残し、weight 遮断・注入を行わない互換（Null weight ポート委譲）であることを XML doc に明記する
  - 既存の `PlaybackUseCaseTests` / `PlaybackUseCaseFourPortTests` が緑のまま、Fake 5 ポートの Small テストで「確立順が W→T→E→A→V」「解放順が T→E→A→V→W」「Completed からの再開で weight が最後に解放されてから再確立」「weight preflight 不合格でどのポートも確立しない」「2 番目以降の確立失敗で weight ポートが逆順解放される」「4 ポートコンストラクタでは weight の訪問が no-op（Null weight ポート）」が緑になる
  - _Requirements: 5.5, 5.7, 5.8, 6.2, 6.3, 6.4, 6.5_

- [ ] 4. rec Application / Adapters: 記録・注入・配線・分類
- [ ] 4.1 記録ユースケースとストリームライターの weight 対応を実装する
  - 記録ユースケースの weight 観測 2 メソッドで、レイヤー id（必要なら `Layer` 定義を先行追記）とスロット id を解決し、1 float のペイロードで時刻付きイベントを追記する。スクラッチは事前確保し毎フレームの確保をしない
  - ストリームライターは基準セクションで `Layer` 定義と weight 基準 2 種を既存の基準の後・時刻付きイベントの前に書き、基準レコード数の集計に含める
  - Small テストで「レイヤー weight サンプルが Layer 定義 → kind 12 の順で追記される」「入力源 weight サンプルが kind 13 で追記される」「Open が weight 基準を時刻付きの前に書く」「定常状態の記録で確保ゼロ」が緑になる
  - _Requirements: 3.1, 3.2, 3.4, 4.3, 7.5, 9.5_

- [ ] 4.2 weight 注入ポートの実装を追加する
  - gate を遅延解決するデリゲートを受け取り、preflight は「gate 解決可否」と「レイヤー名が一意か」のみ（副作用なし。不合格は理由付き）、確立は「解除 → gate 解決 → ライブ遮断 → 宣言値リセット → 基準 weight 設定」の順で行い、途中失敗は自ポートの副作用を残さず false を返す。時刻付き注入は gate へ委譲し、未知の対象は id 単位 1 回の Warning でスキップ。解放は遮断解除のみ（値は維持）で冪等
  - Fake gate を使った Small テストで「gate 未解決は preflight 不合格・確立も副作用なし」「レイヤー名重複は preflight 不合格（理由に duplicate layer names）」「確立の呼出順（Suspend → Reset → Baseline）」「未知対象は warn-once で継続」「解放の冪等」が緑になる
  - _Requirements: 2.6, 4.4, 4.5, 4.6, 5.1, 5.5, 5.6, 6.1, 6.2, 6.5, 6.6, 9.7_

- [ ] 4.3 基準捕捉とキャラクター binding を 5 ポートへ配線する
  - 基準捕捉に weight gate 引数を追加し、全レイヤー weight と全スロット weight を基準エントリに写す（gate が null なら空）
  - キャラクター binding の再生セッション構築で weight 注入ポートを生成して 5 ポートで再生ユースケースを作り、録画開始時の基準捕捉に weight gate を渡す。gate が「レイヤー名が一意でない」と報告したら録画開始を Warning 付きで拒否する
  - Small テストで「weight gate ありの捕捉が全レイヤー・全スロットを含む」「gate なしは空」、既存の binding テストに「再生セッションが weight 注入ポートを構築する」「レイヤー名重複のプロファイルでは録画開始が Warning 付きで false」が緑になる
  - _Requirements: 2.6, 4.1, 4.2, 6.4_

- [ ] 4.4 (P) weight 書込経路の分類正本とゲートテストを追加する
  - 入力源分類カタログに weight 書込経路の正本（型 FullName + メンバー名 + アセンブリ名 + 分類 Gated / Excluded + 除外区分 + 理由。設計書の分類表 14 経路と 1:1）を追加する。既存エントリの理由文（`OverlayInputSource`、`InputActionAnalogSource` の許容 referrer）を「weight 経路は遮断・注入の対象」に更新し、件数は変えない
  - Small ゲートテストで「全エントリの型とメンバーがロード済み product アセンブリに実在する（陳腐化は名前付きで失敗）」「型 + メンバーの重複なし」「Excluded は除外区分と理由を持つ」「人工的な陳腐化エントリが失敗する」が緑になり、既存の `RecInputSourceCoverageCatalogTests` が無変更で緑のまま
  - _Requirements: 1.1, 1.2, 1.3, 8.1, 8.2, 8.3, 8.4, 8.5_
  - _Boundary: RecInputSourceCoverageCatalog / RecWeightWritePathCatalogTests_
  - _Depends: 2.2, 2.3, 2.5_

- [ ] 5. 統合・受け入れ・文書
- [ ] 5.1 (P) timeline REC Export の契約追随
  - REC Export の変換で weight の時刻付き kind を Export 対象外としてスキップし例外を投げないようにする。timeline README の REC Export 節に weight kind を無視する旨を追記する
  - EditMode テストで「weight kind を含む REC が読め、変換可能なレコードだけが Export される」が緑になる
  - _Requirements: 7.7, 11.7_
  - _Boundary: timeline Editor RecEventSequenceAdapter_
  - _Depends: 3.2_

- [ ] 5.2 rec の PlayMode 受け入れテストを追加する
  - 実 `FacialController` + `RecCharacterBinding` で「レイヤー weight と入力源 weight を変える操作列の記録→停止→読込→再生でブレンド出力がフレーム 0 から一致」「再生中のスクリプト `SetLayerWeight` / `SetInputSourceWeight` / バルクが出力に影響しない」「停止後は停止時点の weight が維持され以後のライブ書込が反映される」「記録開始時に既定値以外の weight がある構成で基準が確立されライブ値を引き継がない」「値提供型の Replace と原本復元の前後でスロット weight が変わらない」「途中再生で畳み込んだ weight が確立される」「本 spec 以前の構造のファイルが読込拒否される」「weight gate 未解決では部分的排他が残らない」を Medium PlayMode テストとして追加する
  - GC ゲートに「全レイヤー・全スロット weight を毎フレーム変化させる記録・再生の定常フレームで確保ゼロ」を追加し、Fake ポートに Null weight ポートを追加する。`RecFileReaderTests` に weight 基準ビット欠落ファイルの拒否を追加する
  - 上記すべてと既存の rec / core PlayMode スイートが緑になる
  - _Requirements: 10.1, 10.3, 10.4, 10.5, 10.6, 10.7, 10.8, 10.10, 7.4, 9.5_

- [ ] 5.3 inputsystem Overlay の受け入れテストを追加する
  - inputsystem の PlayMode テスト asmdef に rec の Domain / Application / Adapters 参照を追加し、`com.hidano.facialcontrol.rec` 存在時に定義されるシンボルを `versionDefines` に登録する。テストファイルはそのシンボルで囲む
  - 実 `FacialController`（Overlay モードの `InputSystemAdapterBinding` を持つテスト用 SO）+ 仮想 Gamepad + `RecCharacterBinding` で、「トリガー操作を含む記録→再生でレイヤー weight とブレンド出力が記録どおり再現される」「再生中にトリガーを引いても overlay レイヤー weight と出力が変わらない」「停止後はトリガーに追従する」を Medium PlayMode テストとして追加する（HID-137 の受け入れ条件）
  - テストが緑になり、既存の `InputSystemAdapterBindingIntegrationTests` が緑のまま（inputsystem Runtime は無改修のまま overlay 駆動が遮断される）
  - _Requirements: 10.2, 5.4, 9.4_

- [ ] 5.4 既存 spec 文書と rec / timeline ドキュメントの整合を更新する
  - rec-full-input-coverage の design（Non-Goals / Out of Boundary / 直参照経路 / 分類表の前提文と #4・#17 / 既知制限 1）と requirements（Out of scope / Req 1.3 / Req 10.7）、rec-recording-playback の design（overlay weight 未到達の 2 箇所）に、本 spec で上書きされた旨の注記を付ける（削除せず注記）
  - rec の README / Documentation~ から HID-80 の既知制限を削除し、記録内容の表に weight 系統、kind 表に 12〜15、ヘッダ flags bit1、再生中の遮断節に weight（遮断 → 宣言値リセット → 基準 → 注入、停止時は値維持）、既知制限に「再生開始後の新規スロットは遮断対象外」「レイヤー名はプロファイル内で一意が前提」を追加する。CHANGELOG に変更を記載する
  - 文書に「weight はライブのまま残る」旨の記述が残っていないことを grep で確認できる
  - _Requirements: 11.1, 11.2, 11.3, 11.4, 11.5, 11.6_

- [ ] 5.5 全体回帰を実行して weight 経路の 5 点成立を確認する
  - `pwsh ./scripts/check-test-sizes.ps1`、EditMode 全件、PlayMode 全件を batchmode で実行し、結果 XML で failed = 0 を確認する（既存の GC ゲート・rec 系 3 spec の受け入れテスト・inputsystem 統合テストを含む）
  - 設計書の分類表の Gated 経路すべてについて、記録・基準・遮断・注入・ラウンドトリップを固定するテストが存在することを一覧で確認する
  - _Requirements: 1.4, 10.10, 9.6_
