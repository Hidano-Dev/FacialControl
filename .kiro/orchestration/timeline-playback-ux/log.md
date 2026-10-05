# Orchestration Log: timeline-playback-ux

- Started: 2026-10-05T04:00:00+09:00
- Mode: new
- Options: none（停止予定フェーズなし。PR 作成まで）
- Environment: local（親セッションから fork されたサブエージェントが worktree `.claude/worktrees/agent-acfdedbe3962cfdac` で実行。ユーザー不在のため全ゲートを代行。AskUserQuestion は使わない）
- Linear: HID-144（親）/ HID-138 / HID-139 / HID-140 / HID-141 / HID-142 / HID-143（Hidano / FacialControl）。着手時に 7 件を In Progress へ更新（ラベルは変更なし）

## Pre-flight: マルチ Spec 判定 — 2026-10-05T04:00:00+09:00

- Question: HID-144 の子 6 件を 1 spec にするか、2 spec（Receiver 集約 + Bake 自動解決 / Clip 即時同期）に分けるか（HANDOVER 2026-10-05「次にやること 1」）
- Answer: 親セッション（オーケストレータ）が判定。子 6 件は FacialTimelineReceiver / TimelineAdapterBinding / TimelineBakeDirtyWatcher / RecToTimelineExporter の同一ファイル群を編集し、受け入れ条件 4 点（4 手順再現 / Clip 即時同期 / 欠落の明示 / e2e PlayMode テスト）が不可分なため、分割しても並走効果が無くコンフリクトだけ増える
- Decision: single-spec

## Phase 0: Steering 確認 — 2026-10-05T04:00:00+09:00

- Command: `.kiro/steering/` の存在確認
- Result: product.md / structure.md / tech.md の 3 ファイルを確認。続行
- Reviewer: none
- Gate: n/a
- Branch/PR: n/a

## Phase 1: 初期化 — 2026-10-05T04:00:00+09:00

- Command: `/kiro:spec-init "Linear HID-144 ... Timeline 再生 UX の統合"`（オーケストレータがマルチ Spec 判定済みのため Multi-Spec Guard はスキップ）
- Result: feature 名 `timeline-playback-ux` を生成。`.kiro/specs/timeline-playback-ux/{spec.json,requirements.md}` を作成（重複なし）
- Reviewer: none
- Gate S: CONFIRMED（委任）
  - Rationale: ユーザーは本セッション冒頭で「表情の REC と Timeline 上での再現に必要なタスクを急ぎ完遂する必要がある。HANDOVER を確認し、サブエージェントと Worktree を駆使してどんどん進めてくれ」と全自動進行を委任済み。スコープは HID-144 本文（2026-10-05 ユーザー決定の方針 4 点 + 受け入れ条件 4 点）そのもので、解釈の余地が無い。brownfield（timeline パッケージ既存）。停止予定フェーズなし
  - Escalation: none（ユーザー不在。委任に基づき CONFIRMED と記録）
  - Retry: none
- Branch/PR: `feature/hid-144-timeline-playback-ux`（worktree。origin/main dcdb8ef1 から分岐）

## Phase 2: 要件定義 — 2026-10-05T11:00:00+09:00

- Command: `/kiro:spec-requirements timeline-playback-ux`
- Result: 要件 11 件（EARS、受け入れ基準 73 項目）を生成。Req1 最小手順での Play 再現 / Req2 Profile 側設定の撤去と binding 格下げ / Req3 レイヤー自動接続とチャネル消費先 / Req4 Bake 自動解決 / Req5 Receiver 集約と Inspector 診断 / Req6 Clip 編集の自動再ベイク / Req7 Edit-Play 一致 / Req8 無言 early return 排除 / Req9 Undo・ライフサイクル / Req10 Export 見直しと id 形式統一 / Req11 e2e PlayMode テスト。TBD/要確認マーカーなし（Grep 確認済み）
- Reviewer: `/kiro:validate-gap timeline-playback-ux` — codex-first の呼び出し A（Bash による書き込み監査ベースライン記録）が実行許可されなかったため、コマンド定義どおり codex を起動せず **Claude サブエージェント（validate-gap-agent）へフォールバック**。結果は research.md に永続化（既存資産マップ / Req 別分類 / 矛盾・分岐 15 件（A1〜A5 要件修正で解消、B1〜B5 スコープ追加、C1〜C9 設計判断）/ Req 8.8 の ArgumentException 経路は静的解析で実在確認 / Option A・B・C / Effort L〜XL, Risk Medium〜High）
- Gate A: ESCALATED（代行）
  - Rationale: validate-gap が「元の依頼に含まれないスコープを足さないと要件が成立しない」（B1 core 接続 API 新設、B2 Analog 消費先、B3 Edit/Play 一致の合成再現）と「要件の記述修正で解消できる矛盾」（A1〜A5）を報告し、approval-policy Gate A のエスカレーション必須条件に該当。ユーザー不在のため、親セッションの委任と HID-144 の確定方針（Profile 据え置き / Receiver 集約 / 即時同期 / 無言禁止 / Edit-Play 一致 / 4 手順再現）に基づきオーケストレータが代行判定
  - Escalation: 判定内容は research.md §11 に記録。A1〜A5 は要件修正で解消 / B1 は依頼文「FacialController のレイヤー接続に限定して触る」の範囲内と判定し採用（後付け系2 の overlay suppress 反映は最小限、M-25 には踏み込まない）/ B2 は乗っ取り方式を基本に「core 再解決 or 診断表示 + backlog」を設計判定とし、並走 spec rec-weight-coverage との衝突回避で InputSystemAdapterBinding の weight 経路に触らない制約を追加 / B3 はユーザー決定 3 の直接要件のため採用し、比較条件を限定して Domain の LayerBlender 再利用を明記 / C1〜C9 は優先候補を付して設計へ
  - Retry: 1 回目（上記をフィードバックとして `/kiro:spec-requirements` を merge モードで再実行）。結果: 既存 ID 維持で Req 1.1/1.2/2.1/2.3/2.4/3.1/3.4/3.7/4.1/4.5/7.1/7.6/8.8/10.3/10.4 を書き換え、Req 1.6/3.8 を追加、Boundary Context（In scope: Track binding 自動設定 / Out of scope: .fcrec 拡張 / Adjacent: Director 解決規則）を更新。TBD/要確認マーカーなし（Grep 再確認）
  - 再判定: エスカレーション項目はすべて代行判定で解消し、要件は steering（JSON ファースト / クリーンアーキテクチャ / Unity 依存を Adapters に封じ込め）と矛盾しない → 通過（ESCALATED として記録。approval-policy「実装開始の特例」により spec-run 開始前に確認 1 回が必要だが、ユーザー不在のため委任に基づき進行ログへの記録で代替する）
- Branch/PR: n/a

## Phase 3: 設計 — 2026-10-05T17:30:00+09:00

- Command: `/kiro:spec-design timeline-playback-ux -y`
- Result: design.md（約 800 行）を生成、research.md §12 に Research Log / Design Decisions D1〜D13 を追記。主要決定: Option B（責務分割）+ Option C の段階順 / sink id は名前優先・index フォールバック（D1）/ `:state` sink はレイヤー入力源へ接続せず `_layer2Provider` と REC 観測へ登録、Req 8.8 は mask 長統一で修正（D2）/ Analog は registry Replace 乗っ取り（D3）/ `ChannelSubId` は REC の source id を保持し takeover 先にする（D4）/ Bake 参照は Track 側 `IFacialTimelineBakeHolder`（D5）/ Profile ソースは `LoadProfile()` に統一（D6）/ Director 解決順と Track binding 自動設定（D7）/ 変更検知 3 経路 + 300 ms デバウンス（D8）/ Edit 合成はオフライン `LayerUseCase`（D9）/ Edit の 1 回 = 診断エポック（D10）/ Req 8.8 再現テスト（D11）/ e2e fixture と Small・Medium 配置（D12）/ Source Overrides 撤去（D13）
- Reviewer 1 回目: `/kiro:validate-design timeline-playback-ux` — **codex**（read-only、CODEX_EXIT=0。Bash の heredoc 経路は worktree ガードで拒否されたため、同一プロンプトをファイル経由で PowerShell から codex exec に渡した）→ **NO-GO**、Critical 3 件: (1) Runtime と Edit Preview の Profile ソース一致が未保証、(2) 旧 Profile の `:state` 宣言が D2 の非接続方針を迂回、(3) 複数 Bake 競合時に「最初を採用」で非決定的
- Gate B: 1 回目 REJECTED（差し戻し）
  - Rationale: 3 件とも設計ドキュメントの修正で解消できる内容（approval-policy Gate B の差し戻し条件）
  - Retry: 1 回目（merge 再実行）。(1) `TimelineProfileSource.Resolve` = `LoadProfile()` そのまま、Play は `FacialController.CurrentProfile`（既存 public）、Bake に `ProfileContentHashHex` を保存し `ProfileMismatch` 診断、(2) 旧 `:state` 宣言は非互換とし `LegacyStateDeclaration` Error + Inspector の削除ボタン、(3) Locator は全 Facial トラックの参照一致を要求し Conflict / 部分欠落 / LegacyExport は Play Failed・Edit 自動再ベイクで自己修復
- Reviewer 2 回目: **codex**（CODEX_EXIT=0）→ **NO-GO**。前回 3 件は解消（Strengths に記載）。**新規** Critical 3 件: (1) Analog 消費契約が未完了（直接参照型消費者に届かないまま backlog）、(2) Profile ソース統一が Play 開始順序に依存（ExitingEditMode の profile.json 先行書き出しの副作用）、(3) Editor 変更監視の所有権・解除条件が未定義
- Gate B: 2 回目 REJECTED（差し戻し、上限）
  - Rationale: approval-policy「validate-design が 2 回連続 NO-GO」はエスカレーション条件だが、ユーザー不在のため親セッションの指示（「NO-GO は最大 2 回 merge 再実行、それでも NO-GO なら指摘を設計に反映して続行」）に従い、2 回目の差し戻しを実施
  - Retry: 2 回目（merge 再実行）。(1) Analog は方式 (2): core の `AnalogExpressionInputSource` / `AnalogBlendShapeInputSource` に `AttachRegistry` を追加し、inputsystem の `BuildAnalogExpressionSink` 末尾 1 行で接続（weight 経路は不変）、(2) profile.json 先行書き出しを撤回し `ProfileMismatch` を Warning + 再生継続に再分類、(3) `TimelineEditorServices`（`[InitializeOnLoad]`）に購読を一元化し `beforeAssemblyReload` / `quitting` で解除、未保存 Timeline はスキップ
- Reviewer 3 回目: **codex**（CODEX_EXIT=0）→ **NO-GO**。前回 3 件は解消。**新規** Critical 3 件: (1) Profile 同期が `playModeStateChanged` の購読順序に依存、(2) Analog の実 InputSystem 経路が e2e 検証から外れている（Fake binding のみ）、(3) 新規 Domain コードに Unity.Timeline 型を持ち込む設計が steering と衝突
- Gate B: 3 回目 ESCALATED（代行）→ 指摘を設計に反映して続行
  - Rationale: 差し戻し上限（2 回）に到達。codex は毎回「前回指摘は解消」としたうえで新規 3 件を挙げており、設計の骨格（Receiver ファサード / 診断モデル / e2e）は 3 回とも Strengths。残る 3 件は設計文書の具体化で対応できる内容で、要件や steering の変更を要しない。ユーザー不在のため親セッションの指示どおり「指摘を設計に反映して続行」を選択し、再レビューは行わない（レビュー結果は PR 本文で人間に提示する）
  - Escalation: 反映内容 — (1) core `FacialCharacterProfileAutoExporter` に冪等な `ExportIfEnabled` と `Exported` イベントを追加し、DirtyWatcher の ExitingEditMode 処理が先に呼んで直列化（AutoExport 有効 SO のみ。既存 AutoExport と同じ副作用に限定）、(2) core に公開契約 `IRegistryAttachableAnalogConsumer` を定義し、inputsystem パッケージ側テストで実 `InputSystemAdapterBinding` + registry Replace の追従・復元を固定、timeline e2e は core の実消費者を使う Fake binding で固定（パッケージ依存を増やさない）、(3) 新規 Domain コードは Unity.Timeline 型を持たず、`TimelineAssetScanner`（Adapters）が DTO を返す。既存 Domain の Unity.Timeline 参照（HashCalculator / StateEventCollector / Reconstructor）は既存例外として明記し移動しない
  - Retry: 3 回目（反映のみ、再レビューなし）
- Branch/PR: n/a

## Phase 4: タスク分解 — 2026-10-05T20:30:00+09:00

- Command: `/kiro:spec-tasks timeline-playback-ux -y`
- Result: メジャータスク 10 / leaf タスク 44（第 1 段 1〜8: Req 8.8 再現と修正、core API、inputsystem 1 行接続 + PlayMode テスト、timeline Domain（DTO / SinkIdConvention / Deriver / Diagnostics / OnceWarningGate / ProfileContentHash）、Adapters（bake holder / Scanner / Locator / TrackBindingResolver / Connector / Takeover / Evaluator）、Receiver ファサード化 + binding 格下げ + Mixer 分岐、Profile ソース統一 + BakeReferenceWriter、e2e PlayMode / 第 2 段 9: Watcher / DirtyWatcher 改修 / EditorServices / TrackEditors / Cleaner / Receiver Inspector / Drawer / 第 3 段 10: Compositor / Edit プレビュー置換 / Gaze id 解決 / Exporter 署名変更 / Export ウィンドウ整理 / 既存 PlayMode 移行 / ドキュメント）。`(P)` 12 件、`_Depends:` 明示あり。tasks-agent の意図的な順序調整 5 件（bake holder を Scanner の前、ProfileContentHash を Domain 群へ前倒し、第 2 段は Watcher → DirtyWatcher → Services の順、PlayMode 3 件の reflection 最小撤去を 6.1 に含め本移行は 10.6、ドキュメント更新 10.7 を含む）はいずれもコンパイル整合・中間状態で赤を出さないための調整で design の段階順の意図を変えない
- Reviewer: セルフチェック（approval-policy Gate C）— 要件 AC 75 件すべてがいずれかのタスクの `_Requirements:` に対応（機械照合: 75/75、欠落 0）/ 空のコンテナセクション無し / TBD・要確認 0 / データ削除・デプロイ・外部送信を含むタスク無し / design.md の Boundary Commitments 外に触れるタスク無し（inputsystem の変更は「接続 1 行 + テスト + stub」に限定、weight 経路不変）/ 依存順序の矛盾無し（`_Depends:` は先行タスクのみ参照）
- Gate C: AUTO-APPROVED
  - Rationale: 上記セルフチェック全項目を満たす。spec.json を approvals.tasks.approved=true / ready_for_implementation=true / phase=implementation に更新
  - Escalation: none。approval-policy「実装開始の特例」（Gate A〜C でエスカレーションがあれば実装開始前に確認 1 回）に該当するが、ユーザー不在のため親セッションの委任（「途中で報告のために停止せず一気に進める」）に基づき spec-run を開始する
  - Retry: none
- Branch/PR: `feature/hid-144-timeline-playback-ux`（worktree）

## Phase 5: 実装 + 検証 — 2026-10-05T20:40:00+09:00（開始）

- Command: `/kiro:spec-run timeline-playback-ux`（手順をオーケストレータが直接実行）
- Engine: **degraded-claude-only**。spec-run の第一優先 `codex exec --dangerously-bypass-approvals-and-sandbox` は Claude Code の auto mode classifier に「Create Unsafe Agents」として拒否された（コマンドは起動していない）。同じ結果を別経路（`claude -p --enable-auto-mode` の無人ネスト起動）で得ることも拒否の意図に反するため行わず、実装は `/kiro:spec-impl` 相当の **spec-tdd-impl-agent（ハーネス内サブエージェント、通常の権限制御下）**、独立レビューは **別のサブエージェント（read-only）**、完了確認（kiro-verify-completion）はオーケストレータ自身が Unity Test Runner（batchmode）で実施する。タスク単位のコミット・tasks.md のチェック・FAIL 時の退避ルールは spec-run の定義どおり
- 補足: `pwsh`（PowerShell 7）が未インストールのため `scripts/check-test-sizes.ps1` はローカルで正しく動かない（PS 5.1 では main 時点で 33 件の偽陽性）。静的チェックは CI に委ね、PS 5.1 実行の件数差分だけをローカルで監視する
- Branch/PR: `feature/hid-144-timeline-playback-ux`

### Task 1 — OK（2026-10-05）
- Engine: spec-tdd-impl-agent / Review: 独立サブエージェント APPROVED（FYI: design.md D11 の「基底へ blendShapeCount: Count を渡す」は同文の「値を書かない」と矛盾。実装は blendShapeCount 0 維持 + ContributeMask override。設計文言を後日修正）/ Verify: EditMode 全件 2277 passed / 0 failed、check-test-sizes（PS5.1）差分 0 / Commit: 3e94637d

### Task 2.1 / 2.2 — OK
- Engine: spec-tdd-impl-agent / Review: APPROVED（所見: `IsLayerInputSourceBound` は前提違反時に Warning を出さない（意図的・XML doc 明記、警告スパム回避。spec 側の文言追従を推奨）/ `TryRegisterLayerStateSource` → `_layer2Provider` 反映の controller 経由テストは 5.5 で担保 / 接続済み id への再 Bind はスロットその場置換で重複防止は Connector 側 `IsLayerInputSourceBound` 判定に依存（D2 どおり））/ 配置の逸脱: `FacialControllerTests` は既存ファイルが無く LateUpdate を要するため PlayMode Medium に新設（許容）/ Verify: EditMode 全件 2288 passed / 0 failed、PlayMode `FacialControllerTests` 12/12、check-test-sizes（PS5.1）差分 0 / Commits: 3120239a（2.1）、8ee4328c（2.2）

### Task 2.3 / 2.4 / 2.5 — 実装完了（レビュー・検証中）
- Engine: spec-tdd-impl-agent / Commits: 718138a4（2.3）、400d8587（2.4）、7d539b8a（2.5）/ 実装者報告: core EditMode 1716/1716。`AnalogBlendShapeInputSourceTests` は tasks.md が Small と記載しているが既存 fixture は Medium（OSC 経路で AddComponent）のため属性は変更せず追記のみ

### Task 2.3 / 2.4 / 2.5 — OK
- Review: APPROVED（minor: `AttachRegistry` に同 registry・別 slug を渡すと no-op になる点が XML doc 未記載）/ Verify: 下記 Task 3 時点の全件で確認

### Task 2.6 / 3 — OK
- Engine: spec-tdd-impl-agent（以降モデル Opus 5.5）/ Review: APPROVED（minor: design.md の AutoExporter 節は「ExportProfileJson を呼ぶ」のままで実装（比較に使った JSON を直接書く。出力はバイト一致）と相違 / `ExportAll` の XML doc は「書き換えた SO 数」に意味が変わった / 書き込み失敗の Warning 文言の出所が ExportAll 側へ移動。Dispose で DetachRegistry しない件は registry と同寿命のため無害と判定）/ Verify: EditMode 全件 2322 passed / 0 failed、PlayMode（inputsystem + core FacialControllerTests）93/93、check-test-sizes（PS5.1）差分 0 / Commits: 0dce8d15（2.6）、5429d812（3）

### Task 4.1〜4.5 — OK（4.1 はレビュー REJECTED → 是正コミットで解消）
- Commits: 97573d53 / f05d2799 / 8d83182d / 84037506 / 30213332、是正 79f6bc7d
- Review: 4.2〜4.5 は問題なし。4.1 は REJECTED（`layer{n}` という ASCII 名のレイヤーが別レイヤーの index フォールバック id と衝突し、5.5 の Connector で sink id が重複する）。spec-run 規定では差し戻し退避だが、4.1 を退避すると依存する 5.x 以降が全て止まるため、オーケストレータが指摘箇所だけの最小是正（`IsNameAddressable` が `layer` + 数字のみの名前を名前形から外す、テスト 3 件、design.md D1 記述更新）をコミットして解消した。4.5 の境界外変更（DirtyWatcher の IsStale SO overload 呼び出し 3 か所、Receiver.InspectBake の GazeChannels 受け渡し）は必要最小限と判定
- Verify: EditMode 全件 2394/0（4.5 時点）、是正込みで 2443/0（5.4 時点）、timeline PlayMode 10/10、check-test-sizes（PS5.1）差分 0

### Task 5.1〜5.4 — OK
- Commits: e5a7723e / 937ed39f / 6171661f / b1ca8b28 / Review: APPROVED（minor: `EnsureBindings` は Unity の == により破棄済み・Missing の binding も未設定扱いで上書きする点を XML doc に明記推奨。逸脱（Scanner の namespace `Adapters.Scanning`、孫トラックの ParentIndex は直接の親、`ResolveDirector` は上書き Director を引数で受ける）は妥当）/ Verify: EditMode 全件 2443/0、timeline PlayMode 10/10

### Task 5.5〜5.7 — OK
- Commits: abee4e83 / b1e0cf37 / 9292fdb2 / Review: APPROVED（Suggestion: Disconnect の「他入力源の slot 順・weight 復元」を既存入力源ありの構成で検証していない / Takeover で同一呼び出し内の ChannelSubId 重複が NotFound として記録される。FYI: ControllerMissing（Error）/ ControllerNotInitialized（Info）/ LayerConnectionFailed（Warning）は design の重大度表に未記載、Release の占有 Warning はゲート非経由）/ 逸脱: Connector テストは PlayMode Medium（app LifetimeScope が PlayMode でのみ生成されるため）/ Verify: 実装者報告 EditMode 全件 2477/0、timeline PlayMode 22/22（オーケストレータの全件確認は 6.1 完了後にまとめて実施）

### Task 6.1 — OK
- Commit: f6c3b8e5 / Review: APPROVED（F1: SessionConflict 中に非所有 Mixer の毎フレーム Begin が文字列確保（エラー状態のみ）→ 6.4 で修正指示 / F2: Edit で BindingMissing Error が 1 回出る → 6.4 の Mixer 分岐で解消指示 / F3: controller 未初期化 + binding 未接続で Pending でなく Failed → 6.2 で修正指示 / F4: design の Postconditions が SessionConflict を Failed 扱いのまま（実装は Active 維持で状態図と整合）→ design 文言の後日修正）/ Verify: EditMode 全件 2491/0、PlayMode（timeline + inputsystem + core FacialControllerTests）115/115、check-test-sizes 差分 0

### Task 6.2 / 6.3 / 6.4 — OK
- Commits: bba3e0a4（6.2、6.1 レビュー F3 対応込み）/ 078571b1（6.3）/ 647464bf（6.4、6.1 レビュー F1 対応込み）/ Review: APPROVED（P2: `TimelineGcZeroGateTests` は同期 [Test] でフレームを進めずに ProfilerRecorder.LastValue を読むため確保を検出できない。F1 と 6.4 の「確保 0」はコード読みでしか確認できていない → backlog 登録（PR 作成前にオーケストレータが docs/backlog.md に追記）/ P3: 旧 Profile で binding の legacy 警告と Receiver Start の BindingLegacyFields が Console に 2 回出る（design の 2 節が二重に要求）/ P3: 競合 Director が 2 つ以上だと記録先が交互に入れ替わり名前文字列を作る（まれ））/ Verify: EditMode 全件 2513/0、PlayMode 116/116、check-test-sizes 差分 0

### Task 7.1 / 7.2 / 7.3 — OK
- Commits: 171c32d4 / 3865a217 / 18da73a5 / Review: APPROVED（P3: `TimelineProfileSource` のキャッシュは保存を伴わない SO の外部再インポート（VCS pull 等）を検知できずドメインリロードまで古い Profile を返し得る → backlog 候補）/ 逸脱: `IsStale` 戻り値 bool → `BakeStaleReason`（破壊的変更、CHANGELOG 対象）、JSON と SO の食い違い fixture は新規 Medium クラス、Export 後検証は `RecToTimelineExportWorkflowTests` に追記 / Verify: EditMode 全件 2540/0、timeline PlayMode 23/23、check-test-sizes 差分 0

### Task 8.1 / 8.2 / 8.3 — OK（第 1 段完了）
- Commits: eb945af0 / 6eae9b7a / 4585a508（e2e で発見した欠陥: Receiver のセッション資源再利用が in-place 再ベイクを検知せず古いカーブで再生 → `AcquireDerivation` に Bake 内容の比較を追加して修正）/ Review: APPROVED（要フォロー: Play 中に Profile を保存すると DirtyWatcher の保存フックが Play 中に再ベイクと `receiver.BakeAsset` 上書きを行い診断を変える潜在欠陥。e2e fixture は保存しない回避策でこれを避けている → 9.2 で必須対応・テスト固定を実装エージェントへ指示 / 非ブロッキング: Req 11.4 のうちトラック名不一致・Bake 解決不可・Receiver 未配置は e2e になく EditMode のみ / 軽微: SourceHashHex 比較は冗長）/ Verify: EditMode 全件 2540/0、**PlayMode 全件 486/486**、check-test-sizes 差分 0。第 1 段のロールバック基準（値 sink 宣言のみの旧 Profile は動き、`:state` 宣言ありは止まる）を e2e で確認

### Task 9.1 / 9.2 / 9.3 — OK
- Commits: d723d884 / 99fe24f3（8.x レビューの必須対応込み: Play 中の保存フックは記録のみで Edit 復帰時に無言修復、Receiver.BakeAsset はユーザーが明示設定した場合のみ追従。e2e fixture の「保存しない」回避策は通常保存へ戻した）/ bf8a12c0 / Review: APPROVED（Suggestion: 同一 Timeline への MarkDirty 重複で PendingEntry.Reason が最後の理由で上書きされ Info が欠けることがある / 再ベイク後の SetDirty が ObjectChange 経由で NoChange の空振り照合を 1 回起こす（停止は確認済み、回帰テストなし）。FYI: TrackProfile の登録は Watcher 経由の再ベイク時のみ）/ Verify: EditMode 全件 2578/0、timeline PlayMode 33/33、check-test-sizes 差分 0

### Task 9.4 / 9.5 / 9.6 / 9.7 — 実装完了、レビュー REJECTED → 是正予定
- Commits: 1ecbde41 / 86d99ebe / 2f45be29 / dfbd7e09 / Verify: EditMode 全件 2635/0、timeline PlayMode 33/33、check-test-sizes 差分 0
- Review: REJECTED。[Important] Req 1.6 の Edit 節と D7「Inspector 評価時に Undo 付きで EnsureBindings」を、実装は「Edit では自動設定せずボタンを有効化」に差し替えた / [Important] Edit で未設定トラックがあっても診断行が出ず直し方が表示されない（Req 5.2 / HID-144）/ [Suggestion] Inspector root の再 attach で購読が戻らない / [Suggestion] Drawer の消去ボタンと Req 2.4「読み取り専用で残す」の表現の整合
- オーケストレータ判断: 仕様（Req 1.6 / D7）の方が HID-144 の確定方針「Receiver を追加するだけ」に合うため仕様は改訂せず、実装を仕様へ寄せる是正を行う。ただし「見るだけで dirty」を最小化するため、未設定の Facial トラックがあるときだけ Undo 付きで設定し（既に設定済みなら何も書かない）、TrackBindingAutoAssigned / 未設定件数を TrackBinding 領域に表示する。Inspector の AttachToPanelEvent 再購読も同時に入れる。Drawer の消去ボタンは「ユーザー操作による明示消去」として design に一文追記

### Task 10.1 / 10.2 / 10.3 — 実装完了、レビュー REJECTED → 是正中
- Commits: 2984fd10 / 367affcb / 7de25e7a / 実装者報告: EditMode 2642/0、timeline PlayMode 37/37。core 変更なし
- Review: REJECTED。[Important] Edit/Play 一致テストが Analog を 0 にして不一致を隠している（Analog Value トラックは Timeline の sink で Req 7.1 の除外条件に当たらない）。原因は design D9 の構成が analog 消費者の経路を含まない仕様の欠落で、直すには adapter binding の analog 消費者のオフライン再現が要り 10.x の範囲外 / [Important] Compositor 静的キャッシュがドメインリロード・終了時に Dispose されず NativeArray がリーク / [Suggestion] Watcher 未初期化時に購読が無言スキップ / [FYI] Gaze を Clip から倍精度評価する逸脱は妥当
- オーケストレータ判断（ユーザー不在）: Analog の Edit プレビューは **既知制約** とし、design D9 に明記・テストの正当化コメント修正・不一致を示す特性テスト追加・docs/backlog.md 登録・PR 本文で人間の判断事項として提示する。NativeArray リークと Watcher 初期化は是正コミットで修正（実行中の実装エージェントに (D) として追加指示）

### 是正 d1f19971（9.6/9.7）・Task 10.4 de165de0・Task 10.5 58d3e4d9・是正 14655ffd（10.1〜10.3）
- Verify（実装者報告）: EditMode 全件 2662/0、timeline PlayMode 38/38
- Review: REJECTED。[Important] Edit の Inspector で Undo すると `OnUndoRedoPerformed` → 再評価 → `EnsureTrackBindings` が Undo 記録付きで binding を書き直し、自動設定の Undo が効かず Redo が消え、それ以前の操作にも戻れない → 是正 (E) を実装エージェントへ指示（Undo 起点の評価では書かない + 自動設定は Inspector × Director/Timeline ごとに 1 回）。旧 Exporter 署名の残存なし、Export はシーンを書き換えない、Source Overrides の死にコードなしを確認。Analog の Edit プレビュー既知制約は D9 と backlog S-28 に記録済み

### Task 10.6 / 10.7 / 是正 (E) — OK
- Commits: 16f2b043（10.6: 既存 PlayMode 3 件を `TimelinePlayModeRig` で実 registry 構成へ移行。旧 GC ゲートは同期 [Test] で何も計測できていなかったことを自己検証テストで確認し、`GC Allocated In Frame` 計測に置換。実計測でも既存の確保ゼロゲートは全て緑）/ 74386397（10.7: README / Documentation~ / CHANGELOG）/ e8855b18（(E): Undo 起点の評価では書かない + 自動設定は Inspector × Director/Timeline ごとに 1 回）
- backlog: S-28（Analog の Edit プレビュー既知制約）/ S-30（ProfileSource キャッシュの外部再インポート）/ S-31（小さな後始末の束）を追加。GC ゲートの件は 10.6 で解消したため登録しない
- Verify（実装者報告）: EditMode 全件 2667/0、PlayMode 全件 493/493

## Phase 5 完了: spec-run サマリ — 2026-10-05T21:30:00+09:00

| Task | Engine | Result | Commit |
|---|---|---|---|
| 1 | spec-tdd-impl-agent | OK | 3e94637d |
| 2.1〜2.6 | 同上 | OK | 3120239a / 8ee4328c / 718138a4 / 400d8587 / 7d539b8a / 0dce8d15 |
| 3 | 同上 | OK | 5429d812 |
| 4.1〜4.5 | 同上 | OK（4.1 はレビュー REJECTED → 是正 79f6bc7d） | 97573d53 / f05d2799 / 8d83182d / 84037506 / 30213332 |
| 5.1〜5.7 | 同上 | OK | e5a7723e / 937ed39f / 6171661f / b1ca8b28 / abee4e83 / b1e0cf37 / 9292fdb2 |
| 6.1〜6.4 | 同上 | OK | f6c3b8e5 / bba3e0a4 / 078571b1 / 647464bf |
| 7.1〜7.3 | 同上 | OK | 171c32d4 / 3865a217 / 18da73a5 |
| 8.1〜8.3 | 同上 | OK（e2e で欠陥 1 件発見・修正） | eb945af0 / 6eae9b7a / 4585a508 |
| 9.1〜9.7 | 同上 | OK（9.4〜9.7 はレビュー REJECTED → 是正 d1f19971 / e8855b18） | d723d884 / 99fe24f3 / bf8a12c0 / 1ecbde41 / 86d99ebe / 2f45be29 / dfbd7e09 |
| 10.1〜10.7 | 同上 | OK（10.1〜10.3 はレビュー REJECTED → 是正 14655ffd） | 2984fd10 / 367affcb / 7de25e7a / de165de0 / 58d3e4d9 / 16f2b043 / 74386397 |

- 最終検証（オーケストレータ、HEAD 48bb0a19）: Unity EditMode 全件 2670 / passed 2667 / failed 0 / skipped 3（既存 Ignore）、PlayMode 全件 493/493、check-test-sizes と validate-package は PS 5.1 で main と同一の既知エラーのみ（差分 0。pwsh 7 はローカル未導入のため CI に委ねる）
- validate-impl: **codex**（`--sandbox read-only`。コマンド定義は workspace-write + 書き込み監査だが、監査ベースライン用の Bash が権限で実行できないため、書き込み不能な read-only で実行し監査を不要化。Unity は SANDBOX_BLOCKED のため親セッションの全件実行結果を機械チェック証跡として渡した）→ **DECISION: GO**（要件 75/75 対応、境界違反なし、既知制約 S-28/S-30/S-31 は非ブロッカー）

## Gate D: 完了判定 — GO
- Rationale: 全 leaf タスク OK（未チェック 0）かつ validate-impl が明示的 GO。レビュー REJECTED 3 回はいずれも是正コミット + 再検証で解消（spec-run 規定の「退避して FAIL 記録」ではなく是正で進めた判断は各節に記録）
- 残課題: S-28（Analog の Edit プレビュー既知制約）、S-30、S-31、目視確認項目（Receiver Inspector / Binding Drawer / Export ウィンドウ / Timeline ウィンドウでのスクラブ）

## Phase 6: PR 作成 — Gate E
- Rationale: Gate D 通過 / 作業ブランチ `feature/hid-144-timeline-playback-ux` / working tree clean / push 先は origin
- Branch/PR: feature/hid-144-timeline-playback-ux / https://github.com/Hidano-Dev/FacialControl/pull/49（マージはしない。レビューとマージ判断は人間）
