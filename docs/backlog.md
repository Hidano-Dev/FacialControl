# Backlog（後回しタスク一覧）

> **作成日**: 2026-05-04
> **目的**: 「別 PR ネタ」「preview.2 以降」「別 spec で対処」と先送りされた項目を 1 箇所に集約する。
> **対象外**: ロードマップ全体（[`README.md`](../README.md) / [`docs/technical-spec.md`](technical-spec.md) を参照）、着手中・着手予定の作業（Linear の FacialControl プロジェクトを参照）。`.kiro/specs/` は 2026-10-08 に SDD 廃止（HID-152）で削除した。本文中の spec へのリンクは削除前のコミット `8883375` の版を指す。

---

## 運用ルール

1. **新規エントリの書き方**: 1 項目につき以下を 1 ブロックにまとめる
   - 見出し（`### {識別子}: {一行サマリ}`）
   - 出典（どのファイル・どのセッションで後回しが決まったか）
   - 背景（なぜ後回しか）
   - 着手判断のトリガ（いつ拾い直すか）
2. **HANDOVER.md からの昇格**: セッション引き継ぎノート（[`../HANDOVER.md`](../HANDOVER.md)）の「次にやること」優先度低項目は、当該セッションで処理されなかった場合に本ファイルへ昇格させる。HANDOVER.md は次セッションで上書きされる前提。
3. **完了時**: 別 spec 化された / 別 PR でマージされた / 不要と判断された場合は該当ブロックを **削除** し、commit message に削除理由を残す。完了済み項目は本ファイルに残さない。
4. **粒度**: 1 PR 〜 1 spec 相当のサイズで記載。spec 内のタスク粒度（leaf レベル）まで細分化しない。
5. **言及のみで本ファイルに書かない場所**: 機能単位の「preview.2 以降に延期」（`docs/technical-spec.md` 1.5 節）と Addressables 計画（`README.md` `### preview.2 以降の予定`）は既に明示されているため、本ファイルでは要約 + リンクで参照する。

---

## 短期（別 PR / preview.1 内に拾う候補）

### S-20: OscOutputDemo の動作確認完了後、`OscOutputDemoSignalBinding` を削除
- **出典**: 2026-05-25 セッション「`OscOutputDemoSignalBinding` と `OscSenderAdapterBinding` の役割確認」
- **背景**: `OscOutputDemoSignalBinding`（[`Packages/com.hidano.facialcontrol.osc/Samples~/OscOutputDemo/OscOutputDemoBootstrap.cs:28`](../FacialControl/Packages/com.hidano.facialcontrol.osc/Samples~/OscOutputDemo/OscOutputDemoBootstrap.cs)）は demo 専用に sin 波で BlendShape / Gaze を生成する `IInputSource` を登録する AdapterBinding。`OscSenderAdapterBinding` の OSC 送信動作を demo Scene 内で検証するためだけに存在するため、実機で OSC 出力が想定通り動くことを確認できた時点で sample 側からは撤去する。
- **削除時の影響**: `OscOutputDemoProfile.asset` の `RefIds` 内 `OscOutputDemoSignalBinding` エントリも併せて除去が必要。撤去後の demo 入力源の扱い（別の入力源 sample に差し替え / `OscOutputDemo` Scene 自体を撤去 / README で「ユーザー持ち込みの入力源を結線する」誘導に変更）を併せて決定する。
- **トリガ**: `OscOutputDemo` Scene の OSC 出力動作確認が完了したタイミング
- **影響範囲**: `Packages/com.hidano.facialcontrol.osc/Samples~/OscOutputDemo/OscOutputDemoBootstrap.cs`（`OscOutputDemoSignalBinding` / `DemoSignalState` / `DemoBlendShapeSource` / `DemoGazeSource` の削除）, 同 `OscOutputDemoProfile.asset`, 同 `README.md`, 関連 `.meta`

### S-21: OSC PlayMode テスト 4 件の LogAssert 追従（部分クローズ）
- **出典**: 2026-08-09 `osc-gaze-auto-mapping` タスク 4.6 / 8.4 のフル PlayMode 検証。
- **決着**: gaze mapping 未設定ログは本 spec の実装で削除され、当該ログ由来の赤は解消した。残る 2 件の heartbeat ハッシュ期待値（実測 `122830949` / `133301657`）と、別系統の `OscInputSource` 診断ログ追従は本 spec の範囲外として残件化する。
- **残件トリガ**: heartbeat payload のハッシュ仕様または診断ログ契約を変更する OSC テスト改修時。
- **2026-10-09 追記**: heartbeat は送受信とも廃止した（HID-185 / HID-187）。heartbeat ハッシュ期待値の残件は対象が無くなった。

### S-22: CI を実行するランナーが存在しない（`runs-on: self-hosted` が永久 queued）
- **出典**: 2026-09-26 セッション「CI が一晩経っても終わらない」調査（PR #18）。
- **背景**: `.github/workflows/ci.yml` の全ジョブは `runs-on: self-hosted` だが、リポジトリに登録済みのセルフホストランナーは 0 台。P16-01 の「セルフホストランナー（Windows）設定」が未実施のため、CI は一度も成功しておらず全 run が 24 時間後に自動キャンセルされている。開発マシンは用途が多く常時稼働でもないため CI ランナーには不向きと判断し、1.0.0 のマージはローカルの batchmode テスト結果（EditMode 2124 / PlayMode 426、既知 4 件のみ赤）を根拠にした。
- **候補**: (a) 常時稼働の別 Windows マシンをセルフホストランナーとして登録し `UNITY_PATH` を設定する、(b) GitHub ホストランナー + Unity ライセンス認証（game-ci の `unity-test-runner` 等）へ切り替える、(c) 当面 CI を手動トリガー（`workflow_dispatch`）のみにして push / PR での自動起動を止め、queued の山を作らない。
- **トリガ**: 1.0.0 公開後、次のリリースサイクルを始めるとき。それまでは PR のテスト根拠をローカル実行結果として PR 本文に明記する。
- **応急措置（2026-09-29, PR #21）**: 候補 (c) を実施。`ci.yml` の push / pull_request と `ci-large.yml` の夜間 schedule をコメントアウトし、`workflow_dispatch` のみにした。ランナー登録後にコメントを外して戻す。gitleaks（ubuntu-latest）と harness-sync は対象外で従来どおり動く。
- **解消（2026-10-03, HID-72）**: 候補 (a) の Linux 版を実施。常時稼働のオンプレ Ubuntu 機（Xeon 48 コア / 188 GB / Quadro M4000、Unity 6000.3.19f1 + Personal ライセンス、Xvfb）をリポジトリ単位のセルフホストランナー `unity-ci-ubuntu-facialcontrol`（ラベル `linux-unity`）として登録し、`ci.yml` / `ci-large.yml` の Unity ステップを Linux 向け composite action（`.github/actions/run-unity-tests`）に置き換えて自動起動を戻した。Windows 固有の不具合（HID-70 等）は引き続きローカルで確認する。ランナーの構築手順は agentic-dev-harness 側に記録する。

---

### S-23: REC 全入力対応の PlayMode 受け入れテスト補強（Req 9.3 / 9.5 / 9.10）
- **出典**: 2026-10-04 `rec-full-input-coverage`（PR #46）の validate-impl 再検証 Warning #3。
- **背景**: 「再生中のライブ値提供型更新・ライブ Activate が `BlendedOutputSpan` に反映されない」「有効→無効→有効 + mask 変化」「途中再生の値提供型 / 系1 基準」は Small（`RecPlaybackValueProviderSourceTests` / `RecTimelineSeekTests` / `PlaybackUseCaseFourPortTests` / core `ExpressionUseCaseTests`）でロジック単位にのみ固定されており、実 `FacialController` 上の PlayMode 受け入れは `RecordingAndPlayback_ReplaysValueProviderAndExpressionStateFromFrameZero` 1 本に集約されていない。Codex レビューで往復不良が P1 × 8 見つかった経緯から、出力レベルでの受け入れを足す価値が高い。
- **トリガ**: rec パッケージの次の PlayMode テスト改修時、または REC を実機で使い始める前。

### S-24: REC 注入体の registry 操作ヘルパー（`RecRegistryInjection`）の抽出
- **出典**: 2026-10-04 `rec-full-input-coverage`（PR #46）の validate-impl 再検証 Warning #5。
- **背景**: design.md の `RecRegistryInjection`（Replace / Register / Unregister・参照同一性の復元ガード・warn-once）が未抽出で、`RecAnalogInjector` と `RecValueProviderInjector` に同等ロジックが重複している。挙動は同一でテストも緑だが、復元ガードの規則が 2 箇所に分かれる。
- **トリガ**: 注入体を 1 つ追加するとき、または復元ガードの規則を変更するとき。

### S-25: RecEventChunkQueue の free-list が不適合 segment で探索を打ち切る
- **出典**: 2026-10-05 PR #46 Codex 5 回目レビュー（P2、`RecEventChunkQueue.cs:145`）。
- **背景**: float-only の大きなレコード用に byte 容量 0 の segment が作られた後、mask 付きレコード用の segment が必要になると、不適合 segment を free-list の先頭へ戻して探索を止めるため、背後の適合 segment を再利用できず segment を毎回新規確保する（`GrowthCount` と録画中 GC が増える）。記録内容の正しさには影響しない。
- **方針候補**: 不適合 segment を一時退避して探索を続ける、または容量別に free-list を分ける。
- **トリガ**: 録画中の GC スパイクが実機で観測されたとき、または `RecEventChunkQueue` を次に触るとき。

### S-26: StartPlayback 直後の系1イベントが基準スナップに巻き込まれる
- **出典**: 2026-10-05 PR #46 Codex 5 回目レビュー（P2、`LayerUseCase.cs:213`）。
- **背景**: `StartPlayback` が同フレームの `RecCharacterBinding.Update` より前に呼ばれ、最初の tick で到達する Activate / Deactivate（timestamp 0 付近）があると、baseline の `ResetGeneration` が `FacialController.LateUpdate` で消費される前に timed event が適用され、その 1 件が遷移せず即時スナップになる。以降の再生は正常。
- **方針候補**: baseline のスナップを消費してから scheduler を進める、または reset 時点の集合と後続イベントを分離する。
- **トリガ**: 再生開始直後の表情遷移が記録と違うという報告、または `LayerUseCase` のフレーム内順序を次に変更するとき。

### S-27: Aggregator の source ID キャッシュが slot id の変化を追わない
- **出典**: 2026-10-05 PR #46 Codex 5 回目レビュー（P2、`LayerInputSourceAggregator.cs:449`）。
- **背景**: `ResolveCachedSourceId` は source 参照が変わったときだけ ID を再解析する。同じインスタンスを異なる宣言 id で複数スロットに置き、先行スロットを Unregister して compact すると、移動先スロットに同じ参照がキャッシュ済みのため削除済みの宣言 id で観測し続ける。現行 binding は 1 インスタンス 1 キーなので、プロファイルで同じ入力源を複数宣言した構成でのみ到達する。
- **修正案**: キャッシュ判定に `GetSlotId(layerIdx, sourceIdx)` の変化を含める（数行）+ `LayerUseCaseTests` に再現テスト。
- **トリガ**: 次に `LayerInputSourceAggregator` を触るとき（小さいので同乗で拾う）。

### S-30: TimelineProfileSource のキャッシュが保存を伴わない外部再インポートを検知しない
- **出典**: 2026-10-05 `timeline-playback-ux` タスク 7.1〜7.3 のレビュー（P3）。
- **背景**: キャッシュキーは SO instanceID / profile.json の更新時刻 / SO のダーティ状態で、VCS pull などで SO がディスクから再インポートされた場合はドメインリロードまで古い Profile を返し得る。
- **修正案**: `AssetPostprocessor.OnPostprocessAllAssets` で Profile SO の再インポートを拾って無効化する。

### S-31: Timeline 関連の小さな後始末（2026-10-05 timeline-playback-ux のレビュー所見）
- design.md D11 の「基底へ blendShapeCount: Count を渡す」記述を実装（blendShapeCount 0 維持 + ContributeMask override）に合わせる。
- design.md の SessionConflict の扱い（Postconditions は Failed、実装と状態図は Active 維持）を揃える。
- 旧 Profile で legacy フィールド警告が binding OnStart と Receiver Start の 2 経路から出る（design の 2 節が二重に要求）。どちらか一方に寄せる。
- `TimelineEditChangeWatcher` の `PendingEntry.Reason` が最後の理由で上書きされ、BakeReferenceInconsistent 等の Info が欠けることがある（design は理由の集合）。
- `LayerInputSourceAggregator` に ContributeMask 長の不一致防御を入れる（core。Timeline 側は mask 長統一で解消済み）。
- timeline Runtime Domain の既存例外 2 ファイル（`FacialTimelineHashCalculator` / `TimelineStateEventCollector`）の Unity.Timeline 参照を Adapters へ移す。

## 中期（preview.2 以降 / 別 spec 候補）

### M-1: 既知の機能延期（technical-spec.md 1.5 節と同期）
- **出典**: [`docs/technical-spec.md`](technical-spec.md) 「preview.2 以降に延期」表
- **対象機能**:
  - 自動まばたき（`IBlinkTrigger` 実装）
  - 視線制御（Vector3 ターゲット + BlendShape / ボーン両対応）
  - VRM 対応
  - Timeline 統合
  - テクスチャ切替 / UV アニメーションの JSON 対応
  - ホットリロード自動検知
- **方針**: 各機能について preview.2 着手時に独立 spec を切る。本 backlog では存在のみ記録し、詳細は technical-spec.md 1.5 節と各 spec の Non-Goals を権威にする。

### M-2: Addressables / プロファイル JSON ロード経路抽象化
- **出典**: [`README.md`](../README.md) `### preview.2 以降の予定`
- **内容**: `IProfileJsonLoader` を導入して StreamingAssets / TextAsset / Addressables の 3 経路を差し替え可能にする。デフォルトは StreamingAssets 維持で破壊的変更なし。
- **トリガ**: preview.2 マイルストーン着手 / キャラクター Prefab + 表情プロファイルを 1 セットで Addressables 配信したいユーザー要望

### M-3: ARKit ネイティブ受信（`ArKitNativeAnalogSource`）
- **出典**: [`.kiro/specs/analog-input-binding/research.md`](https://github.com/Hidano-Dev/FacialControl/blob/888337502831165b400ce76f048920a1ba102e6a/.kiro/specs/analog-input-binding/research.md) Topic 8 の Follow-up
- **内容**: preview.1 では ARKit データは外部キャプチャアプリ（iFacialMocap / Live Link Face / Hana 等）から OSC 経由で取得する想定。ネイティブ ARKit SDK 受信は未対応。`IAnalogInputSource` を実装するだけで取り込める設計（Req 5.6）にしてあるので、新 source として追加するのみ。
- **トリガ**: iOS ターゲットが正式追加された / OSC 経由のレイテンシが問題化した

### M-4: BonePose 多重 provider のブレンド合成
- **出典**: [`.kiro/specs/analog-input-binding/tasks.md`](https://github.com/Hidano-Dev/FacialControl/blob/888337502831165b400ce76f048920a1ba102e6a/.kiro/specs/analog-input-binding/tasks.md) スコープ外メモ
- **内容**: 現状 `IBonePoseProvider.SetActiveBonePose(in BonePose)` は per-frame 1 回呼び出しで「単一 active BonePose」を組み立てる側にのみ責任を持つ。複数 provider のブレンド合成（例: 視線追従 + 頭部表情 + 物理ジッタ）は preview.2 以降の別 spec で再設計する。
- **トリガ**: 視線 + 頭部 + その他の複合 bone 制御要件が顕在化したとき

### M-5: 視線追従の Vector3 ターゲット指定 / カメラ目線
- **出典**: [`.kiro/specs/analog-input-binding/tasks.md`](https://github.com/Hidano-Dev/FacialControl/blob/888337502831165b400ce76f048920a1ba102e6a/.kiro/specs/analog-input-binding/tasks.md) / [`.kiro/specs/bone-control/design.md`](https://github.com/Hidano-Dev/FacialControl/blob/888337502831165b400ce76f048920a1ba102e6a/.kiro/specs/bone-control/design.md) Non-Goals
- **内容**: preview.1 の analog-input-binding は `BonePose` Euler 直接指定に閉じる。Vector3 ターゲット → Euler 解決と「カメラ目線」自動制御は preview.2 後半の別 spec。
- **トリガ**: VTuber 実機ユースケースで「カメラ目線トグル」需要が定常化

### M-6: BonePoseSO 独立化（複数プロファイル間で共有）
- **出典**: [`.kiro/specs/bone-control/research.md`](https://github.com/Hidano-Dev/FacialControl/blob/888337502831165b400ce76f048920a1ba102e6a/.kiro/specs/bone-control/research.md) §「BonePose は FacialProfileSO 内包」/ [`.kiro/specs/bone-control/gap-analysis.md`](https://github.com/Hidano-Dev/FacialControl/blob/888337502831165b400ce76f048920a1ba102e6a/.kiro/specs/bone-control/gap-analysis.md) §9
- **内容**: preview.1 では `BonePose` は `FacialProfileSO` に内包する形で確定。複数 FacialProfile 間で同じ BonePose を共有したいユースケースが顕在化したら独立 `BonePoseSO` に切り出す。
- **トリガ**: 同じ視線設定を複数キャラクター間で使い回したいという要望が発生

### M-8: Burst / IAnimationJob への差替
- **出典**: [`.kiro/specs/bone-control/research.md`](https://github.com/Hidano-Dev/FacialControl/blob/888337502831165b400ce76f048920a1ba102e6a/.kiro/specs/bone-control/research.md) §「PlayableGraph + LateUpdate」/ [`.kiro/specs/analog-input-binding/design.md`](https://github.com/Hidano-Dev/FacialControl/blob/888337502831165b400ce76f048920a1ba102e6a/.kiro/specs/analog-input-binding/design.md)
- **内容**: preview.1 は通常 C# の範囲で性能目標達成。`IBonePoseSource` / `IBonePoseProvider` インターフェースは安定しているので Burst 化 / `IAnimationJob` への差替は実装差替のみで可能な状態に保つ。
- **トリガ**: パフォーマンス計測でホットスポット化が確認されたとき

### M-9: Editor 上の curve エディタ統合（フル GUI マッピング編集）
- **出典**: [`.kiro/specs/analog-input-binding/research.md`](https://github.com/Hidano-Dev/FacialControl/blob/888337502831165b400ce76f048920a1ba102e6a/.kiro/specs/analog-input-binding/research.md) Topic 4 Follow-up / [`.kiro/specs/analog-input-binding/tasks.md`](https://github.com/Hidano-Dev/FacialControl/blob/888337502831165b400ce76f048920a1ba102e6a/.kiro/specs/analog-input-binding/tasks.md) スコープ外メモ
- **内容**: preview.1 のアナログマッピング編集は「読取専用 Inspector + JSON Import/Export + Humanoid 自動割当ボタン」に留める。フル GUI（curve エディタ統合）は preview.2 以降。
- **トリガ**: ユーザーから「JSON 直編集が辛い」というフィードバックが集まったとき

### M-11: スキーマ migration パス（preview の破壊変更を 1.0 で吸収）
- **出典**: [`.kiro/specs/analog-input-binding/design.md`](https://github.com/Hidano-Dev/FacialControl/blob/888337502831165b400ce76f048920a1ba102e6a/.kiro/specs/analog-input-binding/design.md) `version` field の扱い
- **内容**: preview 中は JSON スキーマの破壊的変更を許容している（CLAUDE.md / 要件方針）。1.0 リリースに向けて `schemaVersion` ベースの migration パスを設計する。analog binding profile の `version` field は preview 中は文字列保持のみで分岐なしの状態。
- **トリガ**: 1.0 リリース直前のスキーマ凍結フェーズ

### M-12: AnalogBindingBinder の責務分割
- **出典**: [`.kiro/specs/analog-input-binding/research.md`](https://github.com/Hidano-Dev/FacialControl/blob/888337502831165b400ce76f048920a1ba102e6a/.kiro/specs/analog-input-binding/research.md) Topic 17 Follow-up
- **内容**: preview.1 の `FacialAnalogInputBinder` は BlendShape / BonePose 両方の binding を 1 MonoBehaviour で扱う。責務肥大が問題化したら `AnalogBlendShapeBinder` / `AnalogBonePoseBinder` の 2 MonoBehaviour に分割する。
- **トリガ**: 該当ファイルの行数が増えてレビュー困難化したとき

### M-13: 複数 Vector2 入力源での gaze 同時駆動（multi-source gaze blending）
- **出典**: `.kiro/specs/gaze-config-promotion/design.md` の重複 `expressionId` 取り扱い決定（2026-05-06 セッション）
- **内容**: `gaze-config-promotion` spec の preview.1 では、`InputSystemAdapterBinding._gazeInputBindings[]` の中で同 `expressionId` が複数あった場合は **warn ログ + 最初の 1 件のみ採用** とする。これは「左スティックと右スティックの両方を同じ gaze に bind」のような multi-source 同時駆動を **preview.1 では非対応** にする選択。理由は `GazeBoneBinding` の現状構造が単一 `IAnalogVector2InputSource` 前提で組まれており、複数源の合成戦略（max-magnitude blend / sum / dead-zone-aware merge / 後勝ち等）を確定するための設計判断が preview.1 のスコープを超えるため。preview.2 以降の別 spec で「multi-source gaze blending」として正式に設計する。要検討項目: 合成戦略 (a) max-magnitude / (b) component-wise sum / (c) priority + fallback / (d) ユーザー設定可能な切替。`GazeBoneBinding` 内部構造を「複数 source 受け」に拡張する変更も同時に必要。
- **トリガ**: 1.0 リリースに向けて実機 VTuber 環境で「複数コントローラ / OSC + InputSystem 同居」要件が顕在化したとき / `gaze-config-promotion` 完了後の sample 拡張で multi-source デモを切るとき
- **影響範囲**: `Runtime/Adapters/Bone/GazeBoneBinding.cs`, `Runtime/Adapters/Bone/GazeBonePoseProvider.cs`, `Packages/com.hidano.facialcontrol.inputsystem/Runtime/Adapters/AdapterBindings/InputSystemAdapterBinding.cs`, 新規合成戦略型, テスト
- **関連**: M-4（BonePose 多重 provider のブレンド合成 — 異なる bone への複数 provider の話で別概念だが、合成戦略の設計はここと共通化できる可能性あり）

### M-15: 命名規約データセット抽象化（`ARKitDetector` のリネーム）
- **出典**: 2026-05-14 セッション「`com.hidano.facialcontrol` 内に ARKit 依存があるか」のアーキ確認
- **2026-09-29 縮小（HID-34）**: `ARKitUseCase` / `ARKitDetectorWindow` / `ARKitEditorService` と `ARKitDetector.GroupByLayer` / `GenerateExpressions` を削除した。これにより下記 (2) と候補対応 (b) (c) は消滅。残るのは (1) / 候補対応 (a) のみ
- **内容**: `Runtime/Domain/Services/ARKitDetector.cs` / `Runtime/Application/UseCases/ARKitUseCase.cs` / `Editor/Windows/ARKitDetectorWindow.cs` / `Editor/Tools/ARKitEditorService.cs` は **Apple ARKit SDK / ARFoundation / `UnityEngine.XR` には一切依存していない**（grep ヒット 0、manifest.json に XR 系パッケージなし）。実体は ARKit 52 + PerfectSync 13 個の **BlendShape 名文字列定数** と、name → layerGroup (eye/mouth/brow/cheek/nose) の Dictionary、および完全一致検出ロジックのみ。バイナリ依存ゼロなので preview.1 リリースのブロッカーではない。
  - ただし以下 2 点が中期的な整理候補:
    - (1) **クラス名・API 名に "ARKit" が固定**されている。将来 VRoid / iFacialMocap 独自命名 / VRM Standard Expressions などの別命名規約データセットが追加された場合、`PerfectSyncDetector` を別途作るか `ARKitDetector` に詰め込み続けるかが曖昧。データセットとロジックを分離して `BlendShapeNamingDetector` + `IBlendShapeNamingScheme`（ARKit / PerfectSync / VRM 等の datasource を差し替え可能）にする方が拡張に強い。
    - (2) 【HID-34 で消滅】`ARKitUseCase.GenerateOscMapping(string[])` が **OSC 互換マッピング (`/avatar/parameters/{name}`)** を生成しているが、OSC binding 本体は `com.hidano.facialcontrol.osc` に分離済み。マッピング生成だけ core 側にあるのは命名上の責務漏れ気味。ただし `OscMapping` / `OscConfiguration` 型自体は Domain にいて profile JSON の `osc` セクションを担うため、Domain 配置の理屈は通る。整理するなら `OscMappingAutoGenerator` のような中立名 + 生成器の所属再考。
  - 候補対応: (a) `ARKitDetector` → `BlendShapeNamingDetector` リネーム + 命名規約データセットを `IBlendShapeNamingScheme` 抽象化、(b) 【消滅】`ARKitUseCase.GenerateOscMapping` を OSC パッケージへ移管、(c) 【消滅】Editor ツール (`ARKitDetectorWindow`) を `BlendShapeNamingDetectorWindow` 化、(d) 将来別 UPM (`com.hidano.facialcontrol.arkit-detection`) への切り出しは現状の規模ではオーバーキルなので不採用。
- **トリガ**: ARKit 以外の命名規約サポート要望が出たとき（VRoid / VRM Standard Expressions / iFacialMocap 独自命名等） / 1.0 リリース前の Public API 凍結タイミング（preview 中なら破壊的リネーム可）
- **影響範囲**: `Runtime/Domain/Services/ARKitDetector.cs`、対応 Small テスト（`Tests/Small/Domain/ARKitDetectorTests.cs`）、OSC パッケージの利用箇所（`RuntimeMappingResolver` / `AddressPresetEstimator`）、Public API 名変更による下流影響
- **関連**: `osc-output-binding` spec（OSC 側の mode 別 mapping / Drawer / Samples は同 spec で回収済み）。HID-34（ARKit 検出ツール削除。(2) / (b) / (c) はこれで消滅）

### M-14: Domain への「動的 Expression driver」概念導入
- **出典**: `.kiro/specs/gaze-config-promotion/` セッションでの user 指摘（2026-05-06）
- **内容**: 現状 `Domain/Models/ExpressionSnapshot` は **AnimationClip サンプリング由来の静的 snapshot**（`BlendShapeSnapshot[]` + `BoneSnapshot[]` を不変保持）として設計されており、bone も BlendShape も first-class concept として表現可能。しかし「Vector2 入力で連続的に bone Euler を算出する gaze」のような **動的駆動 driver** は Domain の語彙に存在せず、`IBonePoseSource` / `GazeBonePoseProvider` という形で Adapters 層に閉じている。「目線操作も Expression の 1 種」という抽象化を Domain で表現するには、Expression を「静的 snapshot を持つもの」と「動的 driver を持つもの」の上位抽象に揃える Domain refactor が必要。具体的には (a) Expression interface を導入し `StaticExpressionSnapshot` と `DynamicExpressionDriver` を sibling として実装する / (b) `IExpressionEvaluator` を Domain に置き、入力に応じた evaluation strategy を expressing する / (c) 現状維持で動的 driver は Adapters のみ、のいずれか。preview.1 では (c) で固定。Domain refactor は preview.2 以降の大型 spec として独立化する。なお現状でも **Domain は BlendShape 前提ではなく `BoneSnapshot` も first-class** であるため、表情ボーンを使うキャラ（口元・眉毛・瞼を bone で動かす）の対応は本 spec の範囲外で既に成立している。
- **トリガ**: gaze 以外の動的 driver（手書き lip sync curve、procedural な微表情ジッタ、analog 駆動の口開き等）が Adapters 層に増えて統一抽象が欲しくなったとき / 1.0 リリースに向けて Domain の安定 API を凍結するタイミング
- **影響範囲**: `Runtime/Domain/Models/Expression.cs`, `Runtime/Domain/Models/ExpressionSnapshot.cs`, 新規 `IExpressionDriver` 等の interface, `Runtime/Adapters/Bone/GazeBonePoseProvider.cs` の Domain への昇格検討, `IFacialCharacterProfile` 経路, runtime 評価経路の再設計
- **関連**: M-8（Burst / IAnimationJob への差替 — Domain 純化の延長線）、M-4（BonePose 多重 provider のブレンド合成）

### M-16: uOSC vendor copy + zero-alloc fork（osc-output-binding spec Phase 10 / 11）→ HID-151 へ移管
- **出典**: [`.kiro/specs/osc-output-binding/tasks.md`](https://github.com/Hidano-Dev/FacialControl/blob/888337502831165b400ce76f048920a1ba102e6a/.kiro/specs/osc-output-binding/tasks.md) "preview.3 milestone（Deferred）" / [`.kiro/specs/osc-output-binding/uosc-modification-plan.md`](https://github.com/Hidano-Dev/FacialControl/blob/888337502831165b400ce76f048920a1ba102e6a/.kiro/specs/osc-output-binding/uosc-modification-plan.md)
- **内容**: `osc-output-binding` spec の Phase 10 / 11 として記載済み。`com.hidano.uosc` を `Library/PackageCache` から `Packages/com.hidano.uosc/` へ vendor copy 化し、`Runtime/Core/Modern/` 配下に Span / ArrayPool / BinaryPrimitives ベースの新 API（`OscMessage` SoA struct, `OscMessagePool`, `OscWriter`, `OscBundleBuilder`, `OscClient`/`OscServer` (ring buffer + Socket.ReceiveFrom worker), `OscPacketParser` (ref struct), `OscMessageView`, `OscAddressHash` (UTF-8 → uint64)）を実装、送受信ホットパスを zero-alloc 化する。完了後に旧 `uOscClient` / `Bundle` / `Message` facade を撤去（Phase 11）。`osc-output-binding` 内の Req 10.1 「`OnLateTick` で毎フレーム 0 byte GC」を完全達成する。preview.2 では本 spec ロジック側のみ GC ゼロを確認し、uOSC 側 string / object[] alloc は baseline 計測に留めている（`OscSenderGCAllocationTests` / `OscReceiverGCAllocationTests` が `*` deferred マーク付き）。
- **トリガ**: 1 sender × 10+ receiver × 1000 BlendShape の高負荷シナリオで GC スパイクが実測課題化したとき / 1.0 凍結前の最終性能検証フェーズ
- **影響範囲**: `Packages/com.hidano.uosc/`（vendor 化）、`Packages/com.hidano.facialcontrol.osc/Runtime/Adapters/OSC/`（新 API への接続切り替え）、対応 PlayMode 性能テスト
- **関連**: `osc-output-binding` spec Req 8.12 / 10.1 / 10.6

### M-18: ベース表情に Layer / OverrideMask を持たせる
- **出典**: ユーザー報告（2026-05-19）「ベース表情もレイヤー設定を編集できた方がよいかも」
- **内容**: 現状ベース表情は「どの Layer にも属さない default」として全 Layer のフォールバック値に使われている。ユーザー回答は **「ベース表情に所属レイヤー / OverrideMask を持たせる」** 方針で、他の Expression と同様に first-class Expression として扱いたい。これにより「特定 Layer ではベース値を別物に差し替える」「ベース表情自体が override で他レイヤーを抑え込む」ような構成が表現可能になる。
  - 設計判断項目: (a) `FacialProfile.BaseExpression` を `Expression` (Layer + OverrideMask 必須) に格上げするか、それとも引き続き「Layer / OverrideMask 任意の特殊枠」として持つか、(b) ベース表情が `Layer` を持つようになると「Layer 内排他」のロジック (`LayerInputSourceAggregator`) でベース表情を例外扱いするか / 通常 Expression として後勝ち / ブレンドの対象にするか、(c) Inspector UI で「ベース表情編集ペイン」を専用タブにするか、通常の Expression 一覧の特別 row にするか。
  - 既存 backlog M-14（Domain への動的 Expression driver 概念導入）と並走する形になりやすい。
- **トリガ**: ユーザーが「ベース表情の差し替え / 上書き」需要を本格的に出したとき / Domain refactor 着手時
- **影響範囲**: `Runtime/Domain/Models/FacialProfile.cs`, `Runtime/Domain/Models/Expression.cs`, `Runtime/Adapters/ScriptableObject/Serializable/ExpressionSerializable.cs`, `Runtime/Adapters/ScriptableObject/FacialCharacterProfileSO.cs`, `Runtime/Application/UseCases/LayerUseCase.cs`, `Editor/Inspector/FacialCharacterProfileSOInspector.cs`, 対応 EditMode / PlayMode テスト一式
- **関連**: M-14（Domain への動的 Expression driver 概念導入）

### M-19: Layer / InputSource / Adapter の関係視認性改善（UI + ドキュメント）
- **出典**: ユーザー報告（2026-05-19）「Layer の入力源の仕様を分かりやすくする」「Layer の入力源と Adapter の関係性を分かりやすくする」
- **内容**: ユーザー回答から具体的な痛点は以下:
  - (1) **どの Layer にどの InputSource が接続されているのか Inspector 上で見えない**。Layer 一覧と InputSources 一覧が別セクション or 別タブに分かれているため、関連付けが追えない。
  - (2) **Adapter → InputSource → Layer の 3 層構造がドキュメント / UI どちらでも明示されていない**。Adapter を 1 つ追加したときに、その設定が Layer のどの InputSource slot とつながるのか Inspector だけでは辿れない。
  - (3) **単一 Adapter から N Layer へ分岐するケース (Lipsync→mouth, Overlay→blink) の振り分け箇所が見えない**。「どこで分岐設定するか」が判別できない。
  - 方針案: (a) Profile Inspector に「Layer 単位ビュー」を追加し、各 Layer の row 内に「貢献している InputSource 名 + 由来 Adapter」を inline 表示。(b) AdapterBinding Drawer に「この Adapter の出力先 Layer / InputSource」のサマリー Box を追加。(c) Layer 削除時に InputSource 側の参照を孤児にしないよう警告。(d) `Documentation~/` 配下に 3 層構造の図示（Mermaid or Markdown 表）を追加し、`README.md` からリンク。
- **トリガ**: preview.1 リリース前の UX 整備、または preview.2 着手時の「設定ミスを減らす」フェーズ
- **影響範囲**: `Editor/Inspector/FacialCharacterProfileSOInspector.cs`, 各 AdapterBinding Drawer（InputSystem / OSC / Lipsync）, `Documentation~/architecture.md` 新設、README.md リンク追加

### M-20: Sub-asset 削除時の AdapterBinding 逆引き確認ダイアログ
- **出典**: `.kiro/specs/adapter-runtime-settings/tasks.md` 0.2 / validate-design Critical Issue #2（Sub-asset 削除時の binding 参照欠落）
- **内容**: `AdapterRuntimeSettingsCollectionSO` の sub-asset を削除すると、`OscReceiverAdapterBinding._settings` / `OscSenderAdapterBinding._settings` など外部の serialized field 参照が null になる可能性がある。本 spec では Project 内の `AdapterBindingBase` 派生を削除前に逆引き検索して一覧表示する確認ダイアログは実装せず、暫定対応として各 Drawer の `_settings == null` HelpBox に「sub-asset 削除で参照欠落した可能性」「再アサインが必要」「未設定時は OSC Adapter が起動しない」ことを表示する。
  - 後続対応案: (a) `AssetDatabase.FindAssets` / `SerializedObject` 走査で削除対象 sub-asset を参照する Profile/Prefab/Scene を列挙、(b) 削除確認ダイアログに参照元一覧と影響範囲を表示、(c) 可能なら対象 binding への ping / selection を提供、(d) 大規模 Project で重くならないよう検索範囲とキャッシュ戦略を設計する。
- **トリガ**: adapter-runtime-settings 実装後に sub-asset 削除 UI を実運用で使い始めるタイミング / preview.1 リリース前の UX リスクレビュー
- **影響範囲**: `AdapterRuntimeSettingsCollectionEditor`、`OscReceiverAdapterBindingDrawer`、`OscSenderAdapterBindingDrawer`、Profile/Prefab/Scene の serialized reference 走査 helper、対応 EditMode (Editor) テスト

### M-21: Adapter 種別の C# 型削除/リネーム時の自動マイグレーション（対応レベル c）
- **出典**: [`.kiro/specs/adapter-runtime-settings/requirements.md`](https://github.com/Hidano-Dev/FacialControl/blob/888337502831165b400ce76f048920a1ba102e6a/.kiro/specs/adapter-runtime-settings/requirements.md) 要件 3.5 / 8.1（スコープ外明示）
- **内容**: `adapter-runtime-settings` spec では「対応レベル b」（Inspector でのエントリ追加/削除 + 新規 C# 型追加）のみをパラメータ消失防止の保証範囲とし、**対応レベル c（C# 型削除/リネーム時の自動マイグレーション）はスコープ外**としている。preview 段階では破壊的変更を許容する前提で進めるが、1.0 リリースに向けては型削除/リネームを検知して既存 sub-asset を新型に救済する仕組みが必要になる。
  - 設計判断項目: (a) Unity の `[MovedFrom]` 属性 / `FormerlySerializedAs` を Base から派生 SO まで一貫して扱えるか検証、(b) 削除型に対しては「孤児 sub-asset」を保持してダンプ JSON に退避する救済パスを設けるか、その場で `AssetDatabase.RemoveObjectFromAsset` で破棄するかの方針決定、(c) `_schemaVersion` をキーに既存 `ToJson`/`FromJson` ラウンドトリップ経路を再利用してマイグレーションを実装、(d) Editor 起動時に Collection を走査して `MissingScript` 化した sub-asset を Inspector に列挙する診断 UI。
- **トリガ**: 1.0 リリース前に Public API（AdapterRuntimeSettings 型階層）を凍結するタイミング / 既存 RuntimeSettings 型のリネーム要望が顕在化したとき
- **影響範囲**: `AdapterRuntimeSettingsBase`, `AdapterRuntimeSettingsCollectionSO.MigrateOnLoad`, 新規 `AdapterRuntimeSettingsTypeMigrator`（Editor）、Collection Inspector の診断ペイン、対応 EditMode (Editor) テスト
- **関連**: M-11（analog binding 側の schema migration パス — 共通の migration 基盤として整合させる余地あり）、M-22（MigrateOnLoad 本実装と `_schemaVersion` 増分規約）

### M-22: AdapterRuntimeSettingsCollectionSO.MigrateOnLoad の本実装と `_schemaVersion` 増分規約策定
- **出典**: [`.kiro/specs/adapter-runtime-settings/requirements.md`](https://github.com/Hidano-Dev/FacialControl/blob/888337502831165b400ce76f048920a1ba102e6a/.kiro/specs/adapter-runtime-settings/requirements.md) 要件 5.4 / 5.5 / 5.6 / [`.kiro/specs/adapter-runtime-settings/tasks.md`](https://github.com/Hidano-Dev/FacialControl/blob/888337502831165b400ce76f048920a1ba102e6a/.kiro/specs/adapter-runtime-settings/tasks.md) 2.2 / 9.2
- **内容**: 本 spec では `AdapterRuntimeSettingsBase._schemaVersion` フィールドと `ToJson` / `FromJson` 仮想 API、および `AdapterRuntimeSettingsCollectionSO.OnEnable` 内の **空 `MigrateOnLoad()` フック** までを v0 として仕込むに留め、マイグレーション処理本体は実装しない（要件 5.5）。後続では以下を確定させる:
  - (a) `_schemaVersion` の **増分規約**: どの粒度（フィールド追加 / フィールド削除 / 既定値変更 / enum 値追加 etc.）でバージョンを上げるかのルール文書化。Sub-asset 単位で持つ `_schemaVersion` と Collection 側で持つ「アグリゲートバージョン」の関係整理。
  - (b) `MigrateOnLoad` の本実装: Sub-asset ごとの `_schemaVersion` を読んで `FromJson` 経路で defaults を埋め直す or 旧フィールド名 → 新フィールド名のマッピングを適用する。マイグレーション失敗時のロールバックと Warning ログ方針を確定。
  - (c) マイグレーションテスト基盤: 過去バージョンの JSON fixture を `Tests/Shared/Fixtures/` に置き、`FromJson` ラウンドトリップで全フィールドが救済されることを検証する EditMode テストの雛形。
- **トリガ**: 本 spec マージ後に最初の破壊的フィールド変更（追加/削除/リネーム）が発生したタイミング / 1.0 凍結フェーズで `_schemaVersion` 規約を確定する必要が出たとき
- **影響範囲**: `AdapterRuntimeSettingsBase`, `AdapterRuntimeSettingsCollectionSO.MigrateOnLoad`, 各派生 SettingsSO の `FromJson` 実装、`Documentation~/adapter-runtime-settings/migration-guide.md`（新設）、`Tests/Shared/Fixtures/` 配下の JSON fixture、EditMode マイグレーションテスト
- **関連**: M-21（型削除/リネーム時の自動マイグレーション — `MigrateOnLoad` は両者の共通基盤）、M-11（analog binding 側の schema migration パス）

### M-23: OscReceiverAdapterBinding と OscSenderAdapterBinding の統合解消（Receiver/Sender Binding 一本化）の再検討
- **状況**: HID-45 で (b) を採用した。`OscReceiverRuntimeSettingsSO` / `OscSenderRuntimeSettingsSO` に分割し、受信ポート・送信先は binding 本体へ移した。`_receiverEnabled` / `_senderEnabled` は廃止（binding の有無で判断）。旧 `OscRuntimeSettingsSO` は移行専用に残している
- **出典**: [`.kiro/specs/adapter-runtime-settings/requirements.md`](https://github.com/Hidano-Dev/FacialControl/blob/888337502831165b400ce76f048920a1ba102e6a/.kiro/specs/adapter-runtime-settings/requirements.md) 要件 8.2（スコープ外明示） / `.kiro/specs/adapter-runtime-settings/design.md` Boundary Context
- **内容**: 本 spec では Receiver/Sender セクションを **1 つの `OscRuntimeSettingsSO`** に統合しつつ、`OscReceiverAdapterBinding` (Receiver) と `OscSenderAdapterBinding` (Sender) の 2 MonoBehaviour 構造は維持する（要件 8.2）。両 Binding が同一 SettingsSO の異なるセクションを参照する形のため、運用上は「片方だけ起動したい」「片方だけ別 SettingsSO を参照したい」というケースで `_receiverEnabled` / `_senderEnabled` トグル + 同一 SO 参照の組み合わせで対応している。preview.2 以降で以下のいずれかを検討する:
  - (a) **統合**: `OscReceiverAdapterBinding` と `OscSenderAdapterBinding` を 1 MonoBehaviour に統合し、Receiver/Sender セクションを単一 binding が両方扱う。MonoBehaviour ライフサイクル管理が単純化される一方、片方だけ disable する operational UX を別途用意する必要あり。
  - (b) **SettingsSO 分割**: `OscRuntimeSettingsSO` を `OscReceiverRuntimeSettingsSO` / `OscSenderRuntimeSettingsSO` の 2 SO に分割し、現行 2 Binding 構造はそのまま維持。SettingsSO 側の責務が明確化される一方、Receiver/Sender 設定の一貫性を担保していた要件 2.7 を別途満たす仕組み（命名規則 / Inspector ヘルパー）が必要。
  - (c) **現状維持**: 2 Binding + 統合 SO + `_receiverEnabled` / `_senderEnabled` トグルの組み合わせで運用を続け、混乱が出たら (a) または (b) に切り替える。
- **トリガ**: Receiver/Sender 別個運用の UX 不満が顕在化したとき / 1.0 リリース前の AdapterBinding API 凍結タイミング / `_receiverEnabled` / `_senderEnabled` トグルの運用負荷が想定以上に高まったとき
- **影響範囲**: `OscReceiverAdapterBinding`, `OscSenderAdapterBinding`, `OscRuntimeSettingsSO`, 各 Drawer、Profile 内 AdapterBindings リスト、対応 EditMode/PlayMode テスト、`Documentation~/adapter-runtime-settings/`
- **関連**: M-20（Sub-asset 削除時の逆引き確認ダイアログ — 統合/分割いずれを採るかで参照関係の管理粒度が変わる）

### M-24: LipSync AnimationClip の Editor 事前検証 / runtime path 補正
- **出典**: S-9（LipSync の AnimationClip 形式が動かない件の根本対応） / [`.kiro/specs/lipsync-animationclip-rework/requirements.md`](https://github.com/Hidano-Dev/FacialControl/blob/888337502831165b400ce76f048920a1ba102e6a/.kiro/specs/lipsync-animationclip-rework/requirements.md) 要件 1.3 / [`.kiro/specs/lipsync-animationclip-rework/design.md`](https://github.com/Hidano-Dev/FacialControl/blob/888337502831165b400ce76f048920a1ba102e6a/.kiro/specs/lipsync-animationclip-rework/design.md) Non-Goals
- **内容**: S-9 の主要対応は `lipsync-animationclip-rework` spec で対応済み。`ExpressionPhonemeEntry` の追加と `AnimationClipPhonemeEntry` sample 失敗時 fallback により、「AnimationClip 形式が rendererPath 不一致などで動かない」体験は本 spec で解消した。一方、候補 (b)「`AnimationUtility.GetCurveBindings` による Editor 事前検証 + runtime path 補正」は本 spec のスコープ外とし、将来課題として残置する。
- **トリガ**: AnimationClip 形式を引き続き主経路として使いたいユーザー要望が増えたとき / rendererPath 不一致を Inspector 上で自動診断・補正したい需要が顕在化したとき / AnimationClip 作成支援ツールの spec を切るとき

### M-25: 表情 active 取得の系1/系2 二重化解消（ExpressionUseCase + 空 LayerExpressionSource の撤去）
- **出典**: 2026-06-08 active-expression-unification 設計セッション（[`.kiro/specs/active-expression-unification/design-notes.md`](https://github.com/Hidano-Dev/FacialControl/blob/888337502831165b400ce76f048920a1ba102e6a/.kiro/specs/active-expression-unification/design-notes.md)）
- **内容**: overlay suppress / layerOverrideMask の active 取得を系2（`ExpressionTriggerInputSource`）ベースに統一する際、回帰最小・preview 期限のため暫定的に系1（`ExpressionUseCase` + sourceIdx=0 の空 `LayerExpressionSource` 約240行）を残す。実機 InputSystem 経路では系1 は populate されず death-weight（系1 と系2 は相互排他で、実機では系2 のみ動く）。将来、系2 に一本化する:
  - (a) `ExpressionUseCase` を系2 のファサード化（`Activate/Deactivate`→系2 `TriggerOn/Off`、`GetActiveExpressions`→系2 読み）または削除。
  - (b) `LayerUseCase` の sourceIdx=0 `LayerExpressionSource` 撤去（系2 が transition を担うため重複）。
  - (c) public API `FacialController.Activate/Deactivate` を系2 に繋ぐ場合の専用（非デバイス）系2 sink の要否。CLAUDE.md「公開 API シグネチャは非破壊に維持」との整合（`LayerUseCase` コンストラクタ変更に注意）。
- **トリガ**: active-expression-unification 実装が実機で安定後 / Domain refactor 時 / preview 後の整理フェーズ
- **影響範囲**: `ExpressionUseCase`, `LayerUseCase`(コンストラクタ/`LayerExpressionSource`), `FacialController.Activate/Deactivate`, `OverlayInputSource` provider 経路, 対応 EditMode/PlayMode テスト
- **関連**: M-18（ベース表情の Layer/OverrideMask 保持）, M-14（Domain への動的 Expression driver 概念導入）
- **影響範囲**: `com.hidano.facialcontrol.lipsync` の Editor 検証 UI、`AnimationClipPhonemeEntry` の Inspector 表示、AnimationClip path 解決 helper、対応 EditMode テスト
- **関連**: `lipsync-animationclip-rework` spec（S-9 本体対応済み）、将来の AnimationClip 作成支援ツール

### M-26: OscReceiverAdapterBinding の Inspector に手入力/自動 mapping の出自表示（Manual/Auto badge）
- **出典**: [`.kiro/specs/osc-receiver-auto-mapping/requirements.md`](https://github.com/Hidano-Dev/FacialControl/blob/888337502831165b400ce76f048920a1ba102e6a/.kiro/specs/osc-receiver-auto-mapping/requirements.md) Requirement 2.5（2026-05-26 設計セッション 論点 7）
- **内容**: `osc-receiver-auto-mapping` spec では heartbeat 駆動で生成された runtime mapping と Inspector 手入力 mapping を runtime 内部状態（`bool[] isAutoMapping` 等）で区別し、出自識別は診断ログ + runtime API のみで提供する。preview 段階では Inspector UI への出自表示（ListView 行の Manual/Auto badge、自動生成 mapping の readonly セクション表示等）は実装しない。follow-up として Inspector 上で「どの mapping が手入力でどれが heartbeat 由来か」を可視化する UI を追加する。
  - 設計判断項目: (a) 既存 `_mappings` ListView 行に badge を出すか、自動生成分を別 readonly セクションに出すか、(b) 自動生成 mapping は PlayMode 中のみ存在する runtime 状態のため、Inspector 表示は PlayMode 限定にするか、(c) 自動生成行の編集抑止ロジック。
- **トリガ**: ユーザーから「auto mapping が何を生成したか Inspector で確認したい」要望が出たとき / preview.2 の UX 整備フェーズ
- **影響範囲**: `Editor/AdapterBindings/OscReceiverAdapterBindingDrawer.cs`、`OscReceiverAdapterBinding` の出自 runtime API、対応 EditMode (Editor) テスト
- **関連**: `osc-receiver-auto-mapping` spec、M-19（Layer / InputSource / Adapter の関係視認性改善）

### M-27: 入力源ルーティング・グラフエディタ（ノードグラフ UI で配線・slug 直書き廃止）
- **出典**: 2026-06-07 設計セッション。spec 一式生成済み（[`.kiro/specs/input-source-routing-graph-editor/requirements.md`](https://github.com/Hidano-Dev/FacialControl/blob/888337502831165b400ce76f048920a1ba102e6a/.kiro/specs/input-source-routing-graph-editor/requirements.md) / [`design.md`](https://github.com/Hidano-Dev/FacialControl/blob/888337502831165b400ce76f048920a1ba102e6a/.kiro/specs/input-source-routing-graph-editor/design.md) / [`research.md`](https://github.com/Hidano-Dev/FacialControl/blob/888337502831165b400ce76f048920a1ba102e6a/.kiro/specs/input-source-routing-graph-editor/research.md)。phase: design-generated, requirements approved）。
- **背景**: 現状 `FacialCharacterProfileSO` のレイヤー入力源は `_layers[].inputSources[].id` の文字列 slug（例 `lipsync-overlay:a`）を Inspector で人間が手打ちする運用。この id は本来 binding が `GetDefaultLayerInputSources()` 等から算出できる確定値であり自由入力ではない。手打ち運用のため binding slug 形（`ulipsync:a`）と overlay 登録 prefix 形（`lipsync-overlay:a`）の取り違えが起き、`InputSourceRegistry.TryResolve` が無言で外れて音素欠落する事故が実際に発生した（HANDOVER / MEMORY の最有力真因）。
- **内容**: id 文字列をソースノードの出力ポートが内部保持し、画面にはラベル（「あ」等）のみ表示。ユーザーはノード間に線（エッジ）を引くだけで配線し、エッジが canonical id を運ぶことで slug 不一致を **UI 操作レベルで物理的に発生不能** にする。確定済み設計判断:
  - UI: パッチベイ/ノードグラフ形式（左=入力源 → 中=レイヤー → 右=合成出力）。配置は **専用エディタウィンドウ**（Inspector は併存）。技術基盤 **UnityEditor.Experimental.GraphView**（experimental リスクは描画薄層に封じ込め）。
  - アーキ: 純粋ロジック層（id 列挙 / 配線↔SO 変換 / 無効 id 検証）＋ GraphView 薄描画層に分離。`SourcePortEnumerator` を id 列挙の**単一真実源**とし、既存 `AdapterBindingsListView.ResolveDefaultInputSources` / Inspector `AddMissingPhonemeSlots` を抽出共用（実装アプローチ A）。ロジック二重化＝slug 不一致再発の温床を設計で禁止。
  - スコープ内: overlay レイヤーへの一括自動配線ボタン（slot 同時宣言含む）、無効 id の赤破線「宙ぶらりんエッジ」可視化。
  - スコープ外: 無効 id のワンクリック自動修復ボタン、ランタイム UI、既存 Inspector 撤去、JSON 経路改修。
  - ★ Editor 時点で `IInputSourceRegistry` は空（runtime populate）。ソースポート列挙に registry を使わず binding 宣言から静的算出する制約あり。
- **⚠️ 着手前の再検証**: 本 spec の design / research は 2026-06-07 時点のコードベースを前提に書かれている。その後の緊急改修でコードが変わっているため、**着手時に design.md の前提（特に `FacialCharacterProfileSOInspector.cs` の入力源編集箇所・`AdapterBindingsListView.ResolveDefaultInputSources`・`LayerDefinitionSerializable`/`InputSourceDeclarationSerializable` の構造・GraphView 可用性）を現行コードと再照合してから tasks 生成に進むこと**。
- **トリガ**: preview.2 以降の UX 整備フェーズ / 手打ち slug 起因の事故が再発したとき / ルーティング設定が複雑化して Inspector の一覧編集では関係が追えなくなったとき
- **影響範囲**: 新規 `Editor/Windows/Routing/`（Graph 薄層 + Logic 純粋層一式）、`Editor/Inspector/AdapterBindings/AdapterBindingsListView.cs`（id 列挙ロジック抽出→ラッパ化）、`Editor/Inspector/FacialCharacterProfileSOInspector.cs`（slot 初期化ロジック抽出 + 「ルーティングを編集」ボタン追加）、対応 EditMode テスト一式
- **関連**: M-19（Layer / InputSource / Adapter の関係視認性改善 — 同じ「関係が見えない」痛点の UI 解。本 spec はその抜本解にあたり、統合 or 棲み分けを着手時に判断）、S-8（slug 編集を candidate ドロップダウン + 手動 override に変更済み — 本 spec はさらに手入力自体を廃する方向）

### M-28: MultiSourceBlendDemo の profile.json 同期ずれ修復（SampleAssetsAreInSyncTests 残 1 件赤）
- **出典**: 2026-06-21 `/kiro:spec-run input-source-routing-graph-editor` 完了後のフル EditMode 検証で検出。**当該 spec とは無関係な pre-existing failure**（spec の 21 コミットがサンプル資産・`Samples~`・`StreamingAssets` を一切変更していないことを `git log 4b1f2cb..HEAD -- <sample paths>` が空であることで確認済み）。
- **背景**: preview.2 移行の過程で `MultiSourceBlendDemo` のサンプル資産が 3 コピー（dev `Assets/StreamingAssets/`、package `Samples~/`、imported `Assets/Samples/...`）間で同期ずれを起こし、`SampleAssetsAreInSyncTests` が 4 件赤だった。**2026-08-25 に imported コピー（`Assets/Samples/` の dev ミラー）を削除**したことで、imported 起因の 3 件（旧 (1) (2) (4)）は解消（テストは対象ファイル不在時に skip-pass する実装）。実測 8 件中 passed=7 / failed=1。
- **残る赤**: `ProfileJson_DevStreamingAssetsAndPackageSample_AreByteIdentical` — `profile.json` が dev(12313B) と package `Samples~`(10637B) で byte 非一致（index 38 で分岐）。
- **2026-09-26 追記**: フル EditMode（1,922 件）で `SampleAssetsAreInSyncTests` は全件緑。残る赤も解消済みのため本項目はクローズ扱い。再発時のみ上記方針を参照。
- **方針**: dev `Assets/StreamingAssets/FacialControl/MultiSourceBlendDemoCharacter/profile.json` を正本とし（既存テストの前提）、`Samples~/MultiSourceBlendDemo/StreamingAssets/` 側を再生成して byte 一致させる。
- **トリガ**: preview.1 リリース前のサンプル最終確認 / サンプル Import 経路の動作確認時 / この赤が他 spec の `spec-run` バッチでノイズ（保守的 FAIL 判定）になっているとき
- **影響範囲**: `Assets/StreamingAssets/FacialControl/MultiSourceBlendDemoCharacter/profile.json`、`Packages/com.hidano.facialcontrol.inputsystem/Samples~/MultiSourceBlendDemo/StreamingAssets/`、`Tests/EditMode/Editor/Inspector/SampleAssetsAreInSyncTests.cs`
- **関連**: `overlay-clip-redesign` / `phoneme-overlay-slots`（preview.2 overlay 移行系）

### M-29: BlendShape ベース gaze の runtime 配線復活（`lookXxxSamples` 消費経路が全入力源で未配線）
- **出典**: 2026-07-02 セッション「gaze 目ボーン適用の FacialController 集約」の調査で判明。同セッションのスコープ決定で「bone 先行、BlendShape gaze は含めない」と先送り。
- **背景**: `GazeBindingConfig` は bone 制御（`leftEyeBonePath` 等）と BlendShape 制御（`lookLeft/Right/Up/DownClip` → Editor 焼き付けの `lookXxxSamples`）の 2 系統を持つが、`lookXxxSamples` から `AnalogBindingEntry` を構築して `AnalogBlendShapeInputSource` に流す runtime 経路（旧 `FacialCharacterSO.BuildAnalogProfileFromGazeConfigs`）が adapter-binding-architecture 移行時に失われ、**現在どの入力源（InputSystem / OSC / iFacialMocap）でも BlendShape ベース gaze は動かない**。目ボーンを持たないモデル（BlendShape で目線表現）は gaze を反映できない。
- **方針**: `FacialController` の集約 gaze 経路（`SetupGazeBoneProvider` と同じ registry 解決）で、`lookXxxSamples` を持つ config から `AnalogBindingEntry` 群を構築し `AnalogBlendShapeInputSource`（値提供型 `IInputSource`、Aggregator 自動駆動）として layer に載せる。入力源非依存の一般機能として実装する。
- **トリガ**: 目ボーン非搭載モデル（BlendShape 目線）ユーザーの gaze 反映要望 / VRM 対応（M-1）着手時（VRM は BlendShape 目線モデルが多い）
- **影響範囲**: `FacialController`（構築経路）、`AnalogBlendShapeInputSource`（再利用）、`GazeBindingConfig.lookXxxSamples`（既存データ）、PlayMode テスト
- **関連**: M-13（multi-source gaze blending）、M-5（Vector3 ターゲット視線）— いずれも bone 経路前提の拡張であり本件（BlendShape 経路の復活）とは独立

### M-31: 瞳の微細動（マイクロサッカード / アイドル時のゆらぎ）のプロシージャル生成
- **出典**: 2026-08-25 セッション「瞳の微細動機能は入っているか」の確認で、実装・spec・backlog のいずれにも存在しない（未実装ではなく**未計画**）ことが判明。
- **背景**: 現状の gaze 経路（`GazeInputReader` → `GazeBoneBinding` → `GazeBonePoseProvider.Apply()`）は外部入力の Vector2 を目ボーンの yaw/pitch へ写すだけで、値を**生成**する仕組みを一切持たない（`saccade` / `jitter` / `noise` 系の識別子は Runtime に 0 ヒット、スムージングも無し）。そのため入力が静止すると瞳も完全静止し、実在の眼球が持つマイクロサッカード・ドリフト・トレモアが再現されず、生気のない見た目になる。VTuber 配信用途では「入力が来ていない時ほど自然に見せたい」場面（雑談中の視線固定、カメラ目線維持）で効く。
- **方針（未確定・着手時に spec 化）**: 入力視線に微細動を**加算**する形が前提。振幅・周波数・シード（キャラごとに位相をずらす）・on/off をキャラクタープロファイル側の設定として持たせ、入力源非依存の一般機能として実装する。毎フレームのヒープ確保ゼロ（決定論的ノイズ関数、乱数オブジェクトの使い回し）を満たすこと。
- **前提**: 「入力視線 + 微細動」の重ね合わせは現行の `IBonePoseProvider.SetActiveBonePose(in BonePose)` が per-frame 単一 active BonePose 前提のため素直に載らない。**M-4（BonePose 多重 provider のブレンド合成）の再設計が事実上の前提条件**（M-4 の内容中「物理ジッタ」がまさに本件に相当）。
- **トリガ**: M-4 着手時に同時検討 / preview.2 の「人間的しぐさ」系（自動まばたき `IBlinkTrigger` 実装、`docs/technical-spec.md` §11）をまとめて拾うタイミング
- **影響範囲**: `Runtime/Adapters/Bone/GazeBonePoseProvider.cs`, `Runtime/Adapters/Bone/GazeBoneBinding.cs`, `GazeChannel`（`gaze-channel-redesign` 後のデータモデル）, Inspector 目線タブ, JSON schema, PlayMode テスト
- **関連**: M-4（多重 provider 合成 — 前提）、M-5（Vector3 ターゲット視線 / カメラ目線 — カメラ目線時こそ微細動が効く）、M-1 の自動まばたき（同じ「人間的しぐさ」カテゴリ）

### M-32: FacialCharacterProfileSOInspector の pending overlay 編集機構のデッドコード整理
- **出典**: 2026-09-26 テスト棚卸し（Editor UI テスト最小化パス）で判明。
- **内容**: `FacialCharacterProfileSOInspector.RunOverlayEditDeferredOrImmediate` は現在 `editCore?.Invoke()` の即時実行のみで、`_pendingOverlayEdits` に積む経路が存在しない。`FlushPendingOverlayEdits` / `OnPlayModeStateChangedFlushOverlayEdits` の pending 処理と `_pendingOverlayEdits` フィールドは実質デッドコード。従来はこの機構を reflection で人工的に検証するテストがあったが、区分 D として削除済み。Play 遷移時に overlay 編集を遅延させる必要が本当に無いなら機構ごと削除し、必要なら積む経路を復活させて振る舞いテスト（Play 遷移前後で編集が失われない）を書く。
- **トリガ**: Inspector 周辺の次回改修時 / 「Play 解除で overlay 設定が戻る」症状が再報告されたとき
- **影響範囲**: `Editor/Inspector/FacialCharacterProfileSOInspector.cs`、`Tests/EditMode/Editor/Inspector/FacialCharacterProfileSOInspectorTests.cs`
- **関連**: `docs/test-policy.md`（区分 D の扱い）

### M-33: OscReceiverAdapterBinding の heartbeat 蓄積 reset 契機と uOSC 経路デッドコード
- **出典**: 2026-09-26 テスト棚卸し（既知の赤 4 件の処置パス）で判明。同 timestamp 再送による名前の重複蓄積は `AccumulateHeartbeatBytes` に重複排除を入れて修正済み（`ContainsHeartbeatNameLocked`）。
- **内容**: (1) heartbeat chunk の蓄積 reset は「bundle timestamp が変わったとき」のみ。bare メッセージ（timestamp key=1 固定）で heartbeat を送る外部送信元が**名前を減らした** heartbeat を再送しても、古い名前が scratch に残り続ける（重複排除は追加分しか救わない）。「処理済み後に同一 key の chunk が来たら reset」等の契機追加が必要だが、chunk が FixedTick を跨ぐ場合に部分 mapping になるリスクがあるため保留。(2) `HandleHeartbeatMessage(uOSC.Message)` 側の `_heartbeatScratch`(List) 経路は `SetMessageFilter` が未配線で処理からも読まれないデッドコード。M-16（uOSC 撤去）と合わせて削除する。
- **トリガ**: bare メッセージで heartbeat を送る外部送信元（VRChat 以外の OSC ツール）対応時 / M-16 着手時
- **影響範囲**: `Packages/com.hidano.facialcontrol.osc/Runtime/Adapters/AdapterBindings/OscReceiverAdapterBinding.cs`、`Tests/EditMode/Adapters/AdapterBindings/OscReceiverAdapterBindingTests.cs`
- **関連**: M-16（uOSC vendor copy + zero-alloc fork）
- **2026-10-09 追記**: heartbeat の受信・蓄積処理ごと削除したため（HID-185）、(1)(2) とも対象が無くなった。クローズ扱い。

---

## 横断フォローアップ（実装着手時に再確認するメモ）

各 spec の `research.md` に `**Follow-up**:` として記載されている、preview.1 内の実装で「忘れずに対処」すべき項目。spec ごとに完結している場合は本ファイルに転記しない。**spec 着手前に該当 spec の `research.md` を必ず読み直す**。

| spec | follow-up が記載されている節 |
|------|-----------------------------|
| `layer-input-source-blending` | research.md Topic 1 / 3 / 4 / 6 / 7（asmdef CI 確認、深度超過テスト、診断 UI、要件文書注記、`using BulkScope` パターン）|
| `inspector-and-data-model-redesign` | research.md Topic で AnimationEvent 経由メタデータ書き戻し / schema v1.0 拒否 / Custom keyframe / OSC adaptor 経路（OSC 側 mapping は `osc-output-binding` spec で回収済み）|
| `analog-input-binding` | research.md Topic 4 / 9 / 10 / 14（curve エディタ → M-9、bone-control internal API、OscRouter 抽出、ARKit native → M-3、Binder 分割 → M-12 等）|
| `bone-control` | research.md §「LateUpdate 競合」「Quaternion.Euler 一致誤差」「BonePoseSO 独立化 → M-6」「Burst → M-8」「BoneWriter 必須化」|

---

## 関連ドキュメント

- 全体ロードマップ: [`README.md`](../README.md)
- 機能延期一覧: [`docs/technical-spec.md`](technical-spec.md) 1.5 節
- 直近セッション引き継ぎ: [`HANDOVER.md`](../HANDOVER.md)
- spec 個別の Out-of-Scope / Non-Goals: 削除前のコミット `8883375` の `.kiro/specs/*/design.md` § Non-Goals
- 完了済み spec の撤去履歴: 各パッケージの `CHANGELOG.md`

---

## 履歴

- 2026-05-04: 新設。HANDOVER.md 優先度低 #5（S-1）/ Phase 6.5 残課題（S-4）/ 各 spec の preview.2 以降宣言（M-3〜M-12）を集約。
- 2026-05-06: spec `adapter-binding-architecture` クローズに伴い follow-up 2 件追加（S-5: Sample SO guid 再生成、S-6: VContainer dependency 宣言）。
- 2026-05-06: spec `gaze-config-promotion` 設計セッションで preview.1 スコープ外と確定した 2 件を追加（M-13: 複数 Vector2 入力源での gaze 同時駆動、M-14: Domain への動的 Expression driver 概念導入）。
- 2026-05-10: ユーザー指示で「LipSync の AnimationClip 形式が動かない件の根本対応」を S-9 として追加（凌ぎの診断ログ / HelpBox は既に main に入っている）。
- 2026-05-14: アーキ確認セッションで M-15（ARKit 検出機能の責務分離 / 命名規約データセット抽象化）を追加。SDK 依存はゼロだが、クラス名 ARKit 固定 / `GenerateOscMapping` の core 残置という整理候補が確認されたもの。
- 2026-09-29: HID-34 で ARKit 検出ツール（Editor 2 本 + `ARKitUseCase`）を削除し、M-15 を「`ARKitDetector` の命名規約データセット抽象化」だけに縮小。
- 2026-05-15: `osc-output-binding` spec へ OSC 送信 / 受信 mode mapping / Drawer / Samples を引き上げたため、M-1 の「OSC マッピング Editor UI」と M-10（OSC アダプタの mapping 移植）を backlog から削除。
- 2026-05-26: `osc-receiver-auto-mapping` spec の設計セッション（validate-design + 論点確定）で 2 件追加。M-30（旧 M-25、現 M-30。Gaze の auto mapping 化 follow-up spec）、M-26（OscReceiverAdapterBinding Inspector の Manual/Auto 出自表示）。
- 2026-05-10: 本セッションで以下を消化して削除: S-1（ボーン参照を相対 path / 単純名併用に拡張）、S-2（旧 schema field 残置なしを確認）、S-3（PlayMode 統合テスト追加）、S-4（Fork 先で確認済みのため不要と判断）、S-6（README に VContainer 依存と OpenUPM 設定例を追記）、S-8（slug 編集 UI を candidate ドロップダウン + 手動 override テキストの 2 段に変更）、M-7（同名ボーン衝突時の警告を追加。S-1 と同 PR で実装）。
- 2026-05-19: ユーザー集中 FB セッションで S-17（A/I/U/E/O Overlay スロット拡張）を追加。中期: M-18（ベース表情の Layer / OverrideMask 保持）, M-19（Layer / InputSource / Adapter 関係視認性改善）。
- 2026-05-19: preview1-polish-pack 完了後の `/kiro:validate-impl` で EditMode 7 件 fail を再検出。同件である M-17 を本ファイルから削除し、`.kiro/specs/overlay-clip-redesign/tasks.md` の Phase 10（10.1〜10.3）として吸収・移動。
- 2026-05-19: adapter-runtime-settings spec の validate-design Critical Issue #2 を受け、M-20（Sub-asset 削除時の AdapterBinding 逆引き確認ダイアログ）を追加。本 spec 内では Drawer HelpBox 警告で暫定対応する方針に整理。
- 2026-05-20: adapter-runtime-settings spec の Phase 9.2（スコープ外項目・将来マイグレーションの集約）を消化し、以下 3 件を追加。M-21（対応レベル c: 型削除/リネーム時の自動マイグレーション）、M-22（`MigrateOnLoad` 本実装と `_schemaVersion` 増分規約策定）、M-23（OscReceiverAdapterBinding / OscSenderAdapterBinding 統合解消の再検討）。
- 2026-05-20: `/kiro:validate-impl adapter-runtime-settings` 検証後の Unity Test Runner で別 spec 由来の PlayMode テスト 2 件 fail を確認。adapter-runtime-settings spec 本体の判定は GO のまま維持し、別ブランチで対処するため S-18（`DeviceHotSwapTests.SwapDevice_MicToMicSecondDevice_ZeroSettlesThenRebinds` の新規 mic index 0 巻き戻り）と S-19（`OscGazeE2ETests.GazeResolver_OscAndInputSystemSameExpressionId_SelectsLexicographicallyFirstSlug` の InputSystem 側 Gaze 値 0）を追加。
- 2026-05-23: S-19 を `OscGazeE2ETests` の `InputTestFixture` 継承で input system 分離して解消。S-18 を `ULipSyncAdapterBinding.AddInputComponent` の `UpdateMicInfo` 後 index 巻き戻り対策 + `ULipSyncProvider` zero-flush 後の初回 target snap ロジックで解消。両 backlog エントリを削除。
- 2026-05-23: S-7 が `analog-input-binding` spec の `AnalogExpressionInputSource` で既に実装済みであることを確認（`BindingMode.Analog` 経路）。backlog から S-7 を削除し、誤って初期化した `.kiro/specs/analog-expression-weight/` ディレクトリも撤去。
- 2026-05-23: S-17 を spec `phoneme-overlay-slots`、S-9 を spec `lipsync-animationclip-rework` として独立化。それぞれ requirements / design / tasks 生成済み (tasks-generated)。本 backlog からは削除し、以降は spec 内で進行管理。
- 2026-05-23: S-9 は `lipsync-animationclip-rework` spec で対応済みとして完了反映。候補 (b)「`AnimationUtility.GetCurveBindings` による Editor 事前検証 + runtime path 補正」は将来課題として M-24 に残置。
- 2026-05-25: `OscOutputDemoSignalBinding` と `OscSenderAdapterBinding` の役割確認セッションで S-20（OscOutputDemo の動作確認完了後の `OscOutputDemoSignalBinding` 撤去）を追加。
- 2026-06-09: 入力源 slug 直書き運用を廃する「入力源ルーティング・グラフエディタ」を M-27 として追加。spec 一式（requirements approved / design-generated）は 2026-06-07 セッションで `.kiro/specs/input-source-routing-graph-editor/` に生成済みだが、その後の緊急改修でコードベースが変化したため将来着手扱いとし、着手時に design 前提の再照合を必須とする注記を付した。
- 2026-06-21: `/kiro:spec-run input-source-routing-graph-editor` で全 21 タスクを codex exec 実行（フォールバック 0）。完了後フル EditMode 検証で `SampleAssetsAreInSyncTests` 4 件赤を検出。git log でサンプル資産が本 spec の 21 コミットで未変更であることを確認し、preview.2 移行由来の pre-existing failure として M-28 を追加。本 spec 実装自体は自テスト緑。
- 2026-08-25: 「瞳の微細動機能は入っているか」の確認セッションで、当該機能が実装・spec・backlog のいずれにも存在しない（未計画）ことを確認し、M-31（瞳の微細動のプロシージャル生成）を追加。M-4（BonePose 多重 provider ブレンド合成）が前提条件である点を明記。
- 2026-08-25: preview.1 残件の棚卸しで `Assets/Samples/` の dev ミラー（`FacialControl InputSystem/0.1.0-preview.2/Multi Source Blend Demo/`）を削除し、`.gitignore` / steering `structure.md` / `work-procedure.md` を「`Samples~/` 単一正本」へ更新。M-28 は 4 件赤 → 1 件赤（dev ⇄ `Samples~` の profile.json byte 不一致）へ縮小。あわせて `com.hidano.facialcontrol.ifacialmocap` の package name 誤り（`jp.co.com.hidano.…`）を修正し、`technical-spec.md` のスコープ節を実装実態（Timeline / REC / lipsync / iFacialMocap / ボーン視線 / OSC 自動マッピングを preview.1 に含む）へ追従させた。
