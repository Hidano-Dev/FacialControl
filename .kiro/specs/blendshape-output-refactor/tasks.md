# 実装計画

> 本計画は `design.md` の Migration Strategy（実装順 Mermaid）を骨子とする。順序は「出力ライター抽象の新設 → FacialController の writer 配線 → デッド graph 連携の除去 → 撤去 4 クラス＋依存テスト削除 → GC 撲滅（収集/グルーピング非確保化）→ 挙動テストのライブ経路付替え＋GC ゼロ計測 → asmdef コンパイル検証」。
>
> TDD（Red-Green-Refactor）を厳守する。各実装タスクは対応する EditMode/PlayMode テストを伴う。テスト配置は実行時要件で決定する（GC/性能/10 体/リーク/実 graph 撤去確認は PlayMode、ドメイン等価は EditMode）。
>
> `(P)` は直前のピアタスクと並行実行可能であることを示す（並列モード）。境界が重なるタスクには付与しない。`SampleAssetsAreInSyncTests` の 4 件赤は pre-existing（backlog M-28）で本スペック FAIL とは無関係。Editor を閉じてから batchmode テストを実行すること（TestRunner ロック注意）。

- [x] 1. 出力ライター抽象と既定実装の新設
- [x] 1.1 出力ライター抽象（`IBlendShapeOutputWriter`）の契約を定義する
  - 正規化済み（0..1）の BlendShape 出力 Span をフレーム単位で出力先へ反映する差し替え可能な書込み契約を Adapters 層に新設する
  - 出力対象の BlendShape 個数を公開し、Span 長と個数の不一致は短い方に合わせる契約とする
  - `IDisposable` を継承し、出力先リソースの解放点を契約に含める
  - 将来 Job/Burst ベースの実装を同契約へ差し込める表面積に保つ（backlog M-8 の方針）
  - 観測可能な完了条件: 新規インターフェースがコンパイルに通り、`.meta` がランダム 32 桁 hex GUID で生成される
  - _Requirements: 1.2, 1.4, 7.1, 7.2_
  - _Boundary: IBlendShapeOutputWriter_

- [x] 1.2 既定実装（`SkinnedMeshRendererBlendShapeWriter`）のマッピング構築と直書きを実装する
  - 対象 SkinnedMeshRenderer 群と BlendShape 名集合から、出力 index → 対象 Renderer/index マッピングを構築時 1 回だけ生成する（現行 `CollectBlendShapeNames` のロジックを移譲）
  - `Write` で各 index の正規化重みに `×100` スケール変換を適用して `SetBlendShapeWeight` 直書きする。スケール変換（0..1 → 0..100）を本実装に単一所在化する
  - 同名 BlendShape が複数 Renderer に存在する場合の集約反映、2 バイト文字・特殊記号を含む BlendShape 名の文字列一致解決を現行どおり維持する（命名規則を固定しない）
  - 出力先が存在しない index は警告なしでスキップする
  - 毎フレーム `Write` 呼出で新規ヒープ確保を行わない（マッピングは再構築しない）
  - 観測可能な完了条件: 既定実装が抽象を満たし、EditMode のマッピング構築テストが現行 `CollectBlendShapeNames` と等価な解決結果を返す
  - _Requirements: 1.3, 1.6, 5.2, 5.3, 7.2, 7.3_
  - _Boundary: SkinnedMeshRendererBlendShapeWriter_

- [x] 1.3 (P) 出力ライターのマッピング構築・スケール変換の EditMode テストを追加する
  - 同名 BlendShape の複数 Renderer 集約、2 バイト文字/特殊記号名、存在しない名のスキップを Fake/抽象越しに検証する
  - `×100` スケール変換が writer 実装内に閉じ、渡した正規化値（0..1）が 0..100 へ正しく写ることを assert する
  - 観測可能な完了条件: 既定実装の EditMode テストが緑になり、現行マッピングとの等価性が客観的に保証される
  - _Requirements: 5.2, 5.3, 8.1_
  - _Boundary: SkinnedMeshRendererBlendShapeWriter_
  - _Depends: 1.2_

- [x] 2. FacialController を出力ライター経由の出力へ配線する
- [x] 2.1 出力反映を直書きから writer 経由へ置換する
  - 出力反映ループ（`SetBlendShapeWeight` 直書き）を、確定済みの正規化出力 Span を writer へ渡す形に置換する
  - BlendShape 名集合の決定は FacialController に残しつつ、マッピング構築は writer 構築へ移譲する（Renderer 解決後に writer を構築）
  - 出力反映 → OutputBus publish → BoneWriter Apply の LateUpdate 末尾順序を不変に保つ（BoneWriter 経路の従来動作を維持）
  - writer 未構築/未初期化での出力呼出は既存ガード様式に合わせて no-op とする
  - プロファイル切替/破棄時に writer を Dispose する（child scope → writer → LayerUseCase → BoneWriter の解放順序を維持）
  - 観測可能な完了条件: 初期化 → Activate → 1 フレームで writer に出力 Span が渡り、撤去前と同一の最終 BlendShape 重みになる
  - _Requirements: 1.2, 1.5, 1.6, 2.6, 7.2_
  - _Boundary: FacialController_
  - _Depends: 1.1, 1.2_

- [x] 3. デッド PlayableGraph 連携の除去と撤去
- [x] 3.1 FacialController からデッド graph 連携コードを除去する
  - graph 構築・Play 呼出、表情適用/除去の playable 操作、BlendShape 値展開/index 探索、破棄時の graph 解放、`UnityEngine.Playables` の using を除去する
  - Activate/Deactivate はライブ active 操作（系1 ExpressionUseCase）のみ残し、デッド playable 状態変更を除く
  - ネイティブ確保の所有を重みバッファ（既存・構築時 1 回確保/破棄時解放）に単一化し、撤去 graph が持っていたネイティブ確保を消滅させる
  - 観測可能な完了条件: graph 連携を除去した状態で Activate → 1 フレーム → 出力が撤去前と同一であることを PlayMode で確認できる
  - _Requirements: 1.1, 2.4, 2.5_
  - _Boundary: FacialController_
  - _Depends: 2.1_

- [x] 3.2 デッド 4 クラスと専用テストを撤去する
  - デッド状態の Mixer（AnimationStream へ書かない ScriptPlayable）・per-layer playable・現行 graph builder・未使用の PropertyStreamHandle キャッシュの 4 クラスを `.meta` ごと削除する
  - 当該 4 クラス専用の検証テスト（graph builder / per-layer playable / PropertyStreamHandle キャッシュ / Mixer ベース表情）を `.meta` ごと削除する
  - 隣接デッドコード（runtime 未使用の NativeArrayPool / AnimationClipCache）は撤去対象外として現状維持する
  - 観測可能な完了条件: 4 クラスと専用 4 テストが消え、撤去後もコンパイルが通る
  - _Requirements: 1.1, 8.2_
  - _Boundary: FacialControlMixer, LayerPlayable, PlayableGraphBuilder, PropertyStreamHandleCache_
  - _Depends: 3.1_

- [x] 4. 毎フレーム GC アロケーション撲滅（managed 集約経路の脱 List/Dictionary）
- [x] 4.1 アクティブ表情の非確保収集 API を追加する
  - 呼出側が所有・再利用するバッファへ書き込む収集 API（`Clear` → `Add`）を ExpressionUseCase に追加し、メソッド内で新規ヒープ確保を行わない
  - 既存の防御コピー返却 API は外部/Editor 用（毎フレ経路でない）に温存する
  - 観測可能な完了条件: 非確保収集が既存の防御コピー返却と同一順序・同一集合を返すことを EditMode テストで assert する
  - _Requirements: 2.2, 8.1_
  - _Boundary: ExpressionUseCase_
  - _Depends: 3.2_

- [x] 4.2 レイヤー別グルーピングと収集を再利用バッファ化する
  - レイヤー名キーで事前確保した辞書（各 List は毎フレ `Clear` で再利用）と、再利用アクティブ表情バッファを LayerUseCase に導入する
  - 毎フレ経路の先頭で防御コピー収集を非確保収集 API へ、毎フレ Dictionary/List 確保のグルーピングを事前確保辞書ベースへ置換する
  - 事前確保辞書のキーをプロファイルのレイヤー名で構築し、プロファイル切替（パイプライン再構築点）でバッファを再構築する
  - 宣言外レイヤー名の表情がドロップされても消費側のキー走査により下流で読まれず結果不変であること（OQ2）を温存する。追加の正規化は行わない
  - 集約・ブレンド・遷移のアルゴリズム本体、layerOverrideMask 抑制バッファ、最終出力クリア、フィルタロジックは不変に保つ
  - 観測可能な完了条件: 非確保グルーピングが置換前と同一のレイヤー別グルーピング結果を返すことを EditMode テストで assert する
  - _Requirements: 2.1, 2.2, 2.3, 4.1_
  - _Boundary: LayerUseCase_
  - _Depends: 4.1_

- [x] 5. 挙動テストのライブ経路付替えと非回帰検証
- [x] 5.1 (P) 遷移補間・TransitionCurve の非回帰テストをライブ経路へ付替える
  - デッド経路（per-layer playable / Mixer 直叩き）で検証していた遷移テストを、ライブ経路（LayerUseCase の重み更新 → LayerExpressionSource の進行 → 確定出力 Span）の等価検証へ付替える
  - LastWins 遷移・遷移割込（現在の補間値から即時に新遷移開始）・TransitionCurve 上書き・遷移時間 0〜1 秒（既定 0.25 秒）を assert する
  - 観測可能な完了条件: 付替え後の PlayMode 遷移テストが緑になり、遷移補間の出力が変更前と等価である
  - _Requirements: 6.1, 6.2, 6.3, 6.4, 8.3_
  - _Boundary: TransitionIntegrationTests_
  - _Depends: 4.2_

- [x] 5.2 (P) emotion+lipsync ブレンドの非回帰テストをライブ経路へ付替える
  - デッド経路で検証していた優先度ブレンドテストをライブ経路の等価検証へ付替える
  - per-layer の複数入力ソース集約（weighted-sum → clamp01）、優先度ブレンド、LastWins/Blend、ContributeMask 駆動の維持、ExpressionTrigger と ValueProvider 双方の入力受付を assert する
  - 任意スレッドからの重み書込みが SwapIfDirty で安全に取り込まれ、次の出力フレームで反映されることを確認する
  - 観測可能な完了条件: 付替え後の PlayMode ブレンドテストが緑になり、優先度ブレンド結果が変更前と等価である
  - _Requirements: 3.1, 3.2, 3.3, 3.4, 8.3_
  - _Boundary: EmotionLipSyncBlendIntegrationTests_
  - _Depends: 4.2_

- [x] 5.3 (P) 抑制・オーバーレイの非回帰テストを追加する
  - layerOverrideMask 抑制 / overlay suppress / リップシンク音素 overlay の最終 BlendShape 重みが、変更前と同一であることを assert する
  - 抑制・overlay の適用結果が変更前後で等価になることを保証する
  - 観測可能な完了条件: 抑制・overlay の PlayMode 非回帰テストが緑になり、最終重みが変更前と一致する
  - _Requirements: 4.1, 4.2, 4.3, 4.4, 8.3_
  - _Boundary: SuppressOverlayRegressionTests_
  - _Depends: 4.2_

- [x] 5.4 (P) 出力経路（writer 配線・BoneWriter 順序）の非回帰テストを追加する
  - 初期化 → Activate → 1 フレーム → Fake 出力ライターに渡る Span が期待値であることを assert する
  - 出力反映 → OutputBus publish → BoneWriter Apply の順序が維持され、BoneWriter 経路が従来どおり機能することを確認する
  - プロファイル切替後にアクティブ表情が出力へ反映されること（事前確保辞書がプロファイル切替に追従すること）を確認する
  - 観測可能な完了条件: 出力経路の PlayMode 非回帰テストが緑になり、writer 経由出力と BoneWriter 順序が保証される
  - _Requirements: 1.5, 1.6, 2.6, 8.3_
  - _Boundary: FacialController, IBlendShapeOutputWriter_
  - _Depends: 4.2_

- [x] 5.5 (P) ARKit/PerfectSync・プロファイル仕様の非回帰テストを追加する
  - ARKit 52 / PerfectSync の自動検出・プロファイル自動生成の既存挙動の維持を確認する
  - モデルに存在しない BlendShape パラメータが警告なしでスキップされること、命名規則非固定（2 バイト文字・特殊記号）が正しく扱われることを確認する
  - プロファイル（基本 Clip + カテゴリ + リップシンク用 Clip + 遷移時間）に基づく表情合成の既存仕様の維持を確認する
  - 観測可能な完了条件: ARKit/PerfectSync・プロファイル仕様の非回帰テストが緑になり、自動生成・スキップ挙動が変更前と一致する
  - _Requirements: 5.1, 5.2, 5.3, 5.4, 8.3_
  - _Boundary: ArkitProfileRegressionTests_
  - 注記（2026-09-29、HID-34）: ARKit 検出ツールと `ARKitUseCase` の削除に伴い `ArkitProfileRegressionTests` も削除した（自動生成経路そのものが無くなったため）。完全一致検出と未知パラメータのスキップは `ARKitDetectorTests`（Small）が引き続き検証する
  - _Depends: 4.2_

- [x] 6. 毎フレーム GC ゼロ判定ゲートとリーク・10 体の性能検証
- [x] 6.1 毎フレーム GC ゼロ判定ゲートを定義・検証する（最重要）
  - 定常運転の重み更新呼び出しツリー全体を、warmup 後 N フレーム回して `ProfilerRecorder("GC.Alloc") == 0` で判定する正式ゲートを設ける
  - 計測対象は名指しの 2 発生源（アクティブ表情収集の List / レイヤー別グルーピングの Dictionary）だけでなく、重み更新の呼び出しツリー全体（string キー辞書 lookup・入力ソース集約の内部経路等を含む）とする。後から発見された毎フレ確保も Req 2.1 のスコープ内として扱い、赤になったときのスコープ論争を防ぐ
  - 旧 GC 計測テスト（デッド経路の per-layer playable / NativeArrayPool 計測）はライブ経路計測へ刷新する
  - 観測可能な完了条件: ライブ重み更新を warmup 後に N フレーム回し、GC アロケーションが 0 バイトであることが PlayMode テストで緑になる
  - _Requirements: 2.1, 2.2, 2.3, 8.4_
  - _Boundary: GCAllocationTests_
  - _Depends: 4.2_

- [x] 6.2 (P) NativeArray リーク非回帰テストをライブ経路へ付替える
  - 旧 graph 破棄ベースのリーク検証を、LayerUseCase / FacialController の破棄で重みバッファのネイティブ確保が解放されリークしないことの検証へ付替える
  - キャラクター/出力経路の破棄時に確保済みネイティブメモリが解放されることを確認する
  - 観測可能な完了条件: 破棄後にネイティブメモリのリークが無いことが PlayMode テストで緑になる
  - _Requirements: 2.5, 8.4_
  - _Boundary: NativeArrayLeakTests_
  - _Depends: 4.2_

- [x] 6.3 (P) 10 体同時動作の性能非回帰テストをライブ経路へ付替える
  - 旧 graph builder × 10 体の性能テストを、FacialController × 10 のライブ重み更新ループへ付替える
  - 同時 10 体以上の BlendShape 出力が既存どおり成立し、GC ゼロを維持することを確認する（CPU スループット最大化は本スペック対象外）
  - 観測可能な完了条件: 10 体同時のライブ重み更新が成立し、GC ゼロを維持することが PlayMode テストで緑になる
  - _Requirements: 2.6, 8.4_
  - _Boundary: MultiCharacterPerformanceTests_
  - _Depends: 4.2_

- [x] 7. asmdef 参照削減のコンパイル検証とクリーンアーキテクチャ整合（OQ1）
- [x] 7.1 Adapters/Tests asmdef の Unity.Animation 参照削減可否をコンパイルで確定する
  - 撤去後に Adapters asmdef から `Unity.Animation` 参照を外せるかを確認し、外せる場合は除去する（OQ1）
  - テスト asmdef 側の `UnityEngine.Playables` / `Unity.Animation` 参照の残存有無を含めて精査し、不要参照を除去する
  - asmdef 依存方向契約（Adapters → Application → Domain）を破らず、Engine 機能を Adapters 層に封じ込めた状態を保つ
  - エラーハンドリングを Unity 標準ログの範囲に保ち、標準例外を超えるカスタム例外型を新設しないことを確認する
  - 観測可能な完了条件: 参照削減後に EditMode/PlayMode の両方がコンパイル・実行に成功し、不要な Playables/Animation 参照が残っていない
  - _Requirements: 7.2, 7.4, 8.2_
  - _Boundary: Adapters asmdef, Tests asmdef_
  - _Depends: 5.1, 5.2, 5.3, 5.4, 5.5, 6.1, 6.2, 6.3_
