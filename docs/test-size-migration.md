# テストサイズ移行調査（Google Test size 導入）

2026-09-28 時点の全テストを列挙し、EditMode / PlayMode の別・使用 Unity API・外部依存（通信・保存・時間・ファイル）を静的解析で調べて Small / Medium / Large に分類した記録。サイズの定義と書き方は `docs/testing.md`、テストを残すかどうかの判定は `docs/test-policy.md` を参照。

## 調査方法

- 対象: `FacialControl/Packages/*/Tests/**/*.cs`（9 パッケージ、EditMode 9 / PlayMode 7 / Shared 4 / Small 1 アセンブリ）
- アセンブリの実行モードは asmdef の `includePlatforms` から判定（`["Editor"]` のみ → EditMode、それ以外 → PlayMode）
- ファイル単位で以下の API 使用を正規表現で検出し、1 つでも該当すれば Small 不可（Medium）とした。PlayMode アセンブリは定義上すべて Medium

| 検出項目 | 判定に使ったパターン（要約） | Small で禁止する理由 |
|---|---|---|
| AssetDatabase / Resources | `AssetDatabase`, `PrefabUtility`, `Resources.*`（`FindObjectsOfTypeAll` を含む） | アセット DB・ロード済みオブジェクト全体への依存 |
| ファイル I/O | `File.*`, `Directory.*`, `Path.GetTempPath`, `Application.persistentDataPath` 等 | ディスク I/O |
| PlayerPrefs / EditorPrefs | `PlayerPrefs.`（Fake 経由を除く）, `EditorPrefs.` | マシンローカルな永続化 |
| ネットワーク | `UdpClient`, `Socket`, `uOSC.*`, `UnityWebRequest`, `Dns` | ソケット確保（ループバックでも Medium） |
| シーンロード | `SceneManager`, `EditorSceneManager` | シーン依存 |
| フレーム待ち / [UnityTest] | `WaitForSeconds*`, `WaitForFixedUpdate`, `[UnityTest]` | フレーム同期・エディタループ依存 |
| Time.* 直接参照 | `Time.time`, `Time.deltaTime`, `Time.unscaledTime` 等 | 実時間依存（`ITimeProvider` を使う） |
| 実時間 | `DateTime.Now/UtcNow`, `Stopwatch`, `Thread.Sleep` | 非決定的 |
| EditorWindow / EditorApplication | `*EditorWindow`（派生型を含む）, `GetWindow<`, `EditorApplication.` | エディタ UI・ループ依存 |
| 実デバイス | `Microphone.`, `new DefaultMicrophoneDeviceEnumerator` | 実音声デバイスの列挙 |
| エンジングローバル状態 | `InputSystem.*`, `InputTestFixture`, `PlayerLoop`, `Physics.` | プロセス全体の状態を書き換える |
| MonoBehaviour 生成 | `AddComponent<FacialController / OscSender / OscReceiver / FacialTimelineReceiver / RecCharacterBinding / uLipSync 等>` | MonoBehaviour ライフサイクル（OnEnable / OnDestroy）に依存 |
| 性能・GC 計測 | `Tests/*/Performance/`, `*Allocation*`, `*Benchmark*` | 実行環境の負荷に依存し、Small の決定性を満たさない |

`new GameObject()` と `AddComponent<SkinnedMeshRenderer / Animator>`（エンジン組み込みコンポーネント）、`ScriptableObject.CreateInstance`、`Editor.CreateEditor` による Inspector 生成は、MonoBehaviour ライフサイクルに当たらないインメモリ操作として Small に残した（自動判定の前提。後述）。

## サイズ別集計

| アセンブリ | モード | Small（ファイル / テスト） | Medium（ファイル / テスト） | Large |
|---|---|---|---|---|
| `Hidano.FacialControl.ExpressionCreator.Tests.EditMode` | EditMode | 2 / 22 | 1 / 11 | 0 / 0 |
| `Hidano.FacialControl.IFacialMocap.Tests.EditMode` | EditMode | 7 / 30 | 0 / 0 | 0 / 0 |
| `Hidano.FacialControl.IFacialMocap.Tests.PlayMode` | PlayMode | 0 / 0 | 2 / 4 | 0 / 0 |
| `Hidano.FacialControl.InputSystem.Tests.EditMode` | EditMode | 5 / 54 | 2 / 13 | 0 / 0 |
| `Hidano.FacialControl.InputSystem.Tests.PlayMode` | PlayMode | 0 / 0 | 8 / 77 | 0 / 0 |
| `Hidano.FacialControl.LipSync.Tests.EditMode` | EditMode | 7 / 48 | 10 / 60 | 0 / 0 |
| `Hidano.FacialControl.LipSync.Tests.PlayMode` | PlayMode | 0 / 0 | 8 / 43 | 0 / 0 |
| `Hidano.FacialControl.Osc.Tests.EditMode` | EditMode | 28 / 262 | 8 / 82 | 0 / 0 |
| `Hidano.FacialControl.Osc.Tests.PlayMode` | PlayMode | 0 / 0 | 19 / 167 | 0 / 0 |
| `Hidano.FacialControl.Rec.Tests.EditMode` | EditMode | 10 / 64 | 3 / 8 | 0 / 0 |
| `Hidano.FacialControl.Rec.Tests.PlayMode` | PlayMode | 0 / 0 | 2 / 10 | 0 / 0 |
| `Hidano.FacialControl.RoutingEditor.Tests.EditMode` | EditMode | 0 / 0 | 2 / 9 | 0 / 0 |
| `Hidano.FacialControl.Tests.EditMode` | EditMode | 38 / 470 | 18 / 118 | 0 / 0 |
| `Hidano.FacialControl.Tests.PlayMode` | PlayMode | 0 / 0 | 25 / 107 | 0 / 0 |
| `Hidano.FacialControl.Tests.Small` | EditMode | 37 / 617 | 0 / 0 | 0 / 0 |
| `Hidano.FacialControl.Timeline.Tests.EditMode` | EditMode | 12 / 50 | 4 / 20 | 0 / 0 |
| `Hidano.FacialControl.Timeline.Tests.PlayMode` | PlayMode | 0 / 0 | 3 / 10 | 0 / 0 |
| **合計** | | **146 / 1617** | **115 / 739** | **0 / 0** |

テスト数は `[Test]` / `[UnityTest]` / `[TestCase]` / `[TestCaseSource]` / `[Theory]` 属性の出現数（`TestCase` は 1 属性 = 1 件で数えているため Test Runner の表示件数とは多少ずれる）。Large に分類したテストはない。実機ビルドや外部サーバー・実デバイスに接続するテストは現状存在せず、IFacialMocap / OSC の UDP テストはすべてループバックで完結するため Medium とした。

## Small 化を阻んでいる依存

EditMode でありながら Small にできなかった 48 ファイルの阻害要因（重複あり）:

| 阻害要因 | ファイル数 | 代表例と切り離し方 |
|---|---|---|
| AssetDatabase / Resources | 19 | `FacialCharacterProfileSOTests`, `PhonemeSnapshotBuilderTests`, `ArKitOscAdapterBindingTests`。SO を `CreateInstance` で作り、アセット保存部分を Editor 側の薄いラッパへ寄せれば昇格できる |
| ファイル I/O | 11 | `FileProfileRepositoryTests`, `RecFileReaderTests`, `FacialCharacterProfileExporterTests`。`RecStreamWriter` は既に `Func<string, Stream>` を受けるため MemoryStream 化で昇格可能。Exporter / Repository は `ISaveStorage` 相当のファイル抽象が未整備 |
| MonoBehaviour 生成（AddComponent） | 10 | `FacialControllerRendererOwnershipTests`, `FacialTimelineReceiverTests`, `AnalogBlendShapeInputSourceTests`。`FacialController` / `OscReceiver` に埋まったロジック（LayerUseCase 駆動、Renderer 所有権、入力ソース登録）を Humble Object に分離する必要がある |
| 性能・GC 計測 | 5 | `*AllocationTests`, `ManagedAllocationProbeTests`, `AnimationClipExpressionSamplerBenchmarkTests`。計測系は本質的に環境依存 |
| ネットワーク（UDP / Socket / uOSC） | 5 | `OscPortResolverTests`, `OscReceiverPortAutoIncrementTests`, `OscSenderAdapterBindingTests`。ポート空き確認に実ソケットを使う。`IDatagramSender` を導入したので送信側は Fake 化できるが、受信側（uOscServer）と bind 可否判定の抽象は未着手 |
| EditorWindow / EditorApplication | 5 | `ExpressionCreatorWindowTests`, `RoutingEditorLauncherTests`, `FacialCharacterProfileSOInspectorTests`。Editor UI smoke（test-policy の Editor UI 最小方針） |
| PlayerPrefs / EditorPrefs | 3 | `DefaultPlayerPrefsBackendTests`, `LipSyncDeviceStoreTests`。`IPlayerPrefsBackend : ISaveStorage` を通して `InMemorySaveStorage` に置き換え可能。`DefaultPlayerPrefsBackendTests` は本番実装そのものの検証なので Medium のまま |
| エンジングローバル状態（InputSystem / PlayerLoop） | 2 | `InputSystemAdapterBindingTests`。`InputSystem.AddDevice` でプロセス全体の入力状態を変える |
| 実時間（DateTime.Now / Stopwatch） | 2 | `RecStreamWriterTests`（Stopwatch 待ち）、`AnimationClipExpressionSamplerBenchmarkTests` |

### プロダクションコード側の構造的な阻害要因

- **MonoBehaviour に埋まったロジック**: `FacialController`（LateUpdate での出力順・Renderer 所有権・二重配置検出）、`OscReceiver` / `OscReceiverHost`（受信スレッドと入力ソース登録）、`FacialTimelineReceiver`、`RecCharacterBinding` は生成に `AddComponent` が必要で、ロジックをテストするには GameObject を作らざるを得ない。`AdapterBindingHost` のように UseCase をコンポーネント外に持つ構成へ寄せるのが昇格の前提
- **静的な時間参照**: `LayerInputSourceAggregator`（Domain）内部の既定 `ITimeProvider` 実装が `UnityEngine.Time.unscaledTimeAsDouble` を直接参照している。テストからは `ManualTimeProvider` を注入できるため阻害にはなっていないが、Domain 層が `UnityEngine` に依存する原因になっている（`Hidano.FacialControl.Domain.asmdef` は `noEngineReferences: false`）
- **直接のソケット呼び出し**: `OscSender` の bundle 送信（本 PR で `IDatagramSender` に分離済み）、`IFacialMocapReceiverHost` の受信ループ（`UdpClient.Receive` をスレッドで回す。未分離）
- **直接の PlayerPrefs / EditorPrefs 呼び出し**: `DefaultPlayerPrefsBackend`（`ISaveStorage` 経由に整理済み）、`ExpressionCreatorWindow` の `EditorPrefs`（Editor UI 内。未分離）
- **直接のファイル I/O**: `FileProfileRepository`、`FacialCharacterProfileExporter`、`ARKitEditorService`、`RecFileReader`。`RecStreamWriter` のみ `Func<string, Stream>` 注入済み
- **UnityWebRequest**: プロダクション・テストともに使用箇所なし

## Small アセンブリへ移動したファイル

`Hidano.FacialControl.Tests.Small`（参照: Domain / Application / Tests.Shared / Testing / Unity.Collections / TestRunner のみ）へ、EditMode から次の 35 ファイルを移動した。判定は「`using` が Domain / Application / Tests.Shared / System / NUnit / UnityEngine（コア）に収まり、Adapters・Editor・OSC・InputSystem・VContainer・UnityEditor の型名がコード中に現れず、EditMode アセンブリ内の他ファイルのヘルパー型を参照しない」こと。内容・namespace は変更していない（`Hidano.FacialControl.Tests.EditMode.*` のまま）。

- `EditMode/Adapters/GazeSourceContractsTests.cs` → `Small/Adapters/GazeSourceContractsTests.cs`
- `EditMode/Application/ARKitUseCaseTests.cs` → `Small/Application/ARKitUseCaseTests.cs`
- `EditMode/Application/Layer2ActiveExpressionProviderTests.cs` → `Small/Application/Layer2ActiveExpressionProviderTests.cs`
- `EditMode/Application/ProfileUseCaseTests.cs` → `Small/Application/ProfileUseCaseTests.cs`
- `EditMode/Domain/ARKitDetectorTests.cs` → `Small/Domain/ARKitDetectorTests.cs`
- `EditMode/Domain/AdapterSlugTests.cs` → `Small/Domain/AdapterSlugTests.cs`
- `EditMode/Domain/BlendShapeMappingTests.cs` → `Small/Domain/BlendShapeMappingTests.cs`
- `EditMode/Domain/BlendShapeSnapshotTests.cs` → `Small/Domain/BlendShapeSnapshotTests.cs`
- `EditMode/Domain/BoneSnapshotTests.cs` → `Small/Domain/BoneSnapshotTests.cs`
- `EditMode/Domain/ExclusionResolverTests.cs` → `Small/Domain/ExclusionResolverTests.cs`
- `EditMode/Domain/ExpressionSnapshotTests.cs` → `Small/Domain/ExpressionSnapshotTests.cs`
- `EditMode/Domain/ExpressionTests.cs` → `Small/Domain/ExpressionTests.cs`
- `EditMode/Domain/ExpressionTriggerInputSourceBaseTests.cs` → `Small/Domain/ExpressionTriggerInputSourceBaseTests.cs`
- `EditMode/Domain/FacialProfileTests.cs` → `Small/Domain/FacialProfileTests.cs`
- `EditMode/Domain/GazeSourceIdConventionTests.cs` → `Small/Domain/GazeSourceIdConventionTests.cs`
- `EditMode/Domain/IInputSourceContractTests.cs` → `Small/Domain/IInputSourceContractTests.cs`
- `EditMode/Domain/ITimeProviderContractTests.cs` → `Small/Domain/ITimeProviderContractTests.cs`
- `EditMode/Domain/InputSourceIdTests.cs` → `Small/Domain/InputSourceIdTests.cs`
- `EditMode/Domain/InvalidSlotReferenceTests.cs` → `Small/Domain/InvalidSlotReferenceTests.cs`
- `EditMode/Domain/LayerBlenderTests.cs` → `Small/Domain/LayerBlenderTests.cs`
- `EditMode/Domain/LayerDefinitionTests.cs` → `Small/Domain/LayerDefinitionTests.cs`
- `EditMode/Domain/LayerInputSourceAggregatorTests.cs` → `Small/Domain/LayerInputSourceAggregatorTests.cs`
- `EditMode/Domain/LayerInputSourceRegistryTests.cs` → `Small/Domain/LayerInputSourceRegistryTests.cs`
- `EditMode/Domain/LayerInputSourceWeightBufferTests.cs` → `Small/Domain/LayerInputSourceWeightBufferTests.cs`
- `EditMode/Domain/LayerOverrideMaskTests.cs` → `Small/Domain/LayerOverrideMaskTests.cs`
- `EditMode/Domain/LayerSourceWeightEntryTests.cs` → `Small/Domain/LayerSourceWeightEntryTests.cs`
- `EditMode/Domain/ManualTimeProviderTests.cs` → `Small/Domain/ManualTimeProviderTests.cs`
- `EditMode/Domain/OverlaySlotBindingTests.cs` → `Small/Domain/OverlaySlotBindingTests.cs`
- `EditMode/Domain/PhonemeOverlaySlotsTests.cs` → `Small/Domain/PhonemeOverlaySlotsTests.cs`
- `EditMode/Domain/Services/ExpressionResolverTests.cs` → `Small/Domain/Services/ExpressionResolverTests.cs`
- `EditMode/Domain/Services/FacialInputObservationBusTests.cs` → `Small/Domain/Services/FacialInputObservationBusTests.cs`
- `EditMode/Domain/Services/FacialOutputBusTests.cs` → `Small/Domain/Services/FacialOutputBusTests.cs`
- `EditMode/Domain/TransitionCalculatorTests.cs` → `Small/Domain/TransitionCalculatorTests.cs`
- `EditMode/Domain/ValueProviderInputSourceBaseTests.cs` → `Small/Domain/ValueProviderInputSourceBaseTests.cs`
- `EditMode/Domain/WeightOneExactOutputContractTests.cs` → `Small/Domain/WeightOneExactOutputContractTests.cs`

Small 候補 73 ファイルのうち残る 38 ファイルは Adapters / Editor 層の型（DTO、SO、Editor ロジック）を対象にしており、参照制約上 EditMode アセンブリに残した。サイズ属性は `[SmallTest]` で宣言しているため、CI の Small ジョブでは同じく実行される。

## 基底クラスを継承できなかった fixture

`SizedTestFixture` は基底クラスのない fixture すべてに継承させた。既に別の基底を持つ次のクラスはサイズ属性のみ付与している（Small ガードは働かないが、いずれも Medium）。

- `ExpressionInputSourceAdapterTests`（InputTestFixture 派生）— `com.hidano.facialcontrol.inputsystem/Tests/PlayMode/Adapters/InputSources/ExpressionInputSourceAdapterTests.cs`
- `InputActionAnalogSourceTests`（InputTestFixture 派生）— `com.hidano.facialcontrol.inputsystem/Tests/PlayMode/Adapters/InputSources/InputActionAnalogSourceTests.cs`
- `InputSystemAdapterTests`（InputTestFixture 派生）— `com.hidano.facialcontrol.inputsystem/Tests/PlayMode/Adapters/InputSystemAdapterTests.cs`
- `ExpressionInputSourceAdapterAllocationTests`（InputTestFixture 派生）— `com.hidano.facialcontrol.inputsystem/Tests/PlayMode/Performance/ExpressionInputSourceAdapterAllocationTests.cs`
- `ULipSyncAdapterBindingLifecycleTests`（LipSyncDeviceStoreTestBase 派生）— `com.hidano.facialcontrol.lipsync/Tests/PlayMode/Lifecycle/ULipSyncAdapterBindingLifecycleTests.cs`
- `OscGazeE2ETests`（InputTestFixture 派生）— `com.hidano.facialcontrol.osc/Tests/PlayMode/Integration/OscGazeE2ETests.cs`
- `LipSyncDeviceStoreTestBase`（lipsync Tests/Shared）は `SizedTestFixture` を継承させたため、派生の `ULipSyncAdapterBindingLifecycleTests` はガードの対象になる

## 自動判定の前提（レビュー時に確認してほしい点）

1. `new GameObject()` とエンジン組み込みコンポーネント（`SkinnedMeshRenderer`, `Animator`, `PlayableDirector`）の `AddComponent` は Small 可とした。「MonoBehaviour のライフサイクル」には当たらないと解釈したため。厳密に禁止するなら 12 ファイル（`BlendShapeNameProviderTests`, `FacialControllerConflictResolverTests`, `AdapterBuildContextTests` 等）が Medium に落ちる
2. `ScriptableObject.CreateInstance` と `Editor.CreateEditor` による Inspector smoke は Small 可とした（AssetDatabase を触るものは Medium）
3. `Undo.*` / `Selection.*` はエディタのグローバル状態だが、インメモリで完結するため Small 可とした（該当 3 ファイル）
4. EditMode でファイル I/O / AssetDatabase 等を使うテストは「Small 不可」= Medium とした。ユーザー定義では Medium = PlayMode だが、EditMode の Medium も存在することになる。CI の Medium ジョブは EditMode と PlayMode の両方を `-testCategory Medium` で回す
5. `Stopwatch` を使う計測系は Medium とした。Google の Small 定義では sleep のみ禁止だが、負荷で結果が変わる計測は Small の決定性を満たさないと判断した
6. ループバック UDP（`127.0.0.1`）は「外部ネットワーク通信」に当たらないため Medium 可とし、Large にしていない
7. `DeviceHotSwapTests`（lipsync PlayMode）は `uLipSyncMicrophone` コンポーネントを扱うが、`FakeMicrophoneDeviceEnumerator` / `FakeAsioDriverEnumerator` 経由で実デバイスを列挙しないため Medium とした

## レビューでの再分類（PR #21）

初回の静的判定が見落とし、レビュー（Codex）で指摘されて Medium に変更したもの。判定パターンと `scripts/check-test-sizes.ps1` にも同じ補強を入れた。

- `RoutingEditorWindowTests`（routing-editor）: `RoutingEditorWindow.Open` で EditorWindow 派生型を生成し、後始末に `Resources.FindObjectsOfTypeAll` を使う。パターンが `\bEditorWindow\b`（派生型名に不一致）と `Resources.Load` のみだったため見落とした → `\w*EditorWindow\b`、`Resources.\w+` に拡張
- `DefaultDeviceEnumeratorTests`（lipsync）の `GetDeviceNames_DefaultEnumerator_ReturnsStringArray`: 本番 `DefaultMicrophoneDeviceEnumerator` が `Microphone.devices` を読む。テスト側に `Microphone.` が現れないため見落とした → `new DefaultMicrophoneDeviceEnumerator` をパターンに追加し、テストを `DefaultMicrophoneDeviceEnumeratorTests.cs`（Medium）へ分離

## 全ファイルの分類

| ファイル | アセンブリ | モード | テスト数 | サイズ | 判定理由 |
|---|---|---|---|---|---|
| `com.hidano.facialcontrol.expression-creator/Tests/EditMode/ExpressionCreatorWindowTests.cs` | `ExpressionCreator.Tests.EditMode` | EditMode | 11 | Medium | AssetDatabase / Resources, EditorWindow / EditorApplication |
| `com.hidano.facialcontrol.expression-creator/Tests/EditMode/FaceTrackTargetResolverTests.cs` | `ExpressionCreator.Tests.EditMode` | EditMode | 8 | Small | 禁止 API なし |
| `com.hidano.facialcontrol.expression-creator/Tests/EditMode/PreviewRenderWrapperTests.cs` | `ExpressionCreator.Tests.EditMode` | EditMode | 14 | Small | 禁止 API なし |
| `com.hidano.facialcontrol.ifacialmocap/Tests/EditMode/AnalogAxesInputSourceTests.cs` | `IFacialMocap.Tests.EditMode` | EditMode | 7 | Small | 禁止 API なし |
| `com.hidano.facialcontrol.ifacialmocap/Tests/EditMode/EyeGazeConverterTests.cs` | `IFacialMocap.Tests.EditMode` | EditMode | 4 | Small | 禁止 API なし |
| `com.hidano.facialcontrol.ifacialmocap/Tests/EditMode/IFacialMocapBlendShapeCatalogTests.cs` | `IFacialMocap.Tests.EditMode` | EditMode | 5 | Small | 禁止 API なし |
| `com.hidano.facialcontrol.ifacialmocap/Tests/EditMode/IFacialMocapOptionsDtoTests.cs` | `IFacialMocap.Tests.EditMode` | EditMode | 2 | Small | 禁止 API なし |
| `com.hidano.facialcontrol.ifacialmocap/Tests/EditMode/IFacialMocapPacketParserTests.cs` | `IFacialMocap.Tests.EditMode` | EditMode | 7 | Small | 禁止 API なし |
| `com.hidano.facialcontrol.ifacialmocap/Tests/EditMode/IFacialMocapReceiverAdapterBindingGazeTests.cs` | `IFacialMocap.Tests.EditMode` | EditMode | 2 | Small | 禁止 API なし |
| `com.hidano.facialcontrol.ifacialmocap/Tests/EditMode/IFacialMocapRuntimeSettingsSOTests.cs` | `IFacialMocap.Tests.EditMode` | EditMode | 3 | Small | 禁止 API なし |
| `com.hidano.facialcontrol.ifacialmocap/Tests/PlayMode/IFacialMocapReceiverAdapterBindingIntegrationTests.cs` | `IFacialMocap.Tests.PlayMode` | PlayMode | 3 | Medium | PlayMode アセンブリ |
| `com.hidano.facialcontrol.ifacialmocap/Tests/PlayMode/IFacialMocapReceiverHostTests.cs` | `IFacialMocap.Tests.PlayMode` | PlayMode | 1 | Medium | PlayMode アセンブリ（IFacialMocapReceiverHost） |
| `com.hidano.facialcontrol.inputsystem/Tests/EditMode/Adapters/AdapterBindings/InputSystemAdapterBindingDrawerTests.cs` | `InputSystem.Tests.EditMode` | EditMode | 3 | Medium | エンジングローバル状態（InputSystem / PlayerLoop） |
| `com.hidano.facialcontrol.inputsystem/Tests/EditMode/Adapters/AdapterBindings/InputSystemAdapterBindingTests.cs` | `InputSystem.Tests.EditMode` | EditMode | 10 | Medium | エンジングローバル状態（InputSystem / PlayerLoop） |
| `com.hidano.facialcontrol.inputsystem/Tests/EditMode/Adapters/Input/InputDeviceCategorizerTests.cs` | `InputSystem.Tests.EditMode` | EditMode | 5 | Small | 禁止 API なし |
| `com.hidano.facialcontrol.inputsystem/Tests/EditMode/Adapters/InputSources/ToggleStateReconcilerTests.cs` | `InputSystem.Tests.EditMode` | EditMode | 4 | Small | 禁止 API なし |
| `com.hidano.facialcontrol.inputsystem/Tests/EditMode/Adapters/Processors/AnalogProcessorTests.cs` | `InputSystem.Tests.EditMode` | EditMode | 27 | Small | 禁止 API なし |
| `com.hidano.facialcontrol.inputsystem/Tests/EditMode/Domain/InputBindingTests.cs` | `InputSystem.Tests.EditMode` | EditMode | 12 | Small | 禁止 API なし |
| `com.hidano.facialcontrol.inputsystem/Tests/EditMode/Integration/OscControllerBlendingIntegrationTests.cs` | `InputSystem.Tests.EditMode` | EditMode | 6 | Small | 禁止 API なし |
| `com.hidano.facialcontrol.inputsystem/Tests/PlayMode/Adapters/InputSources/ExpressionInputSourceAdapterTests.cs` | `InputSystem.Tests.PlayMode` | PlayMode | 6 | Medium | PlayMode アセンブリ（ExpressionInputSourceAdapter） |
| `com.hidano.facialcontrol.inputsystem/Tests/PlayMode/Adapters/InputSources/InputActionAnalogSourceTests.cs` | `InputSystem.Tests.PlayMode` | PlayMode | 19 | Medium | PlayMode アセンブリ |
| `com.hidano.facialcontrol.inputsystem/Tests/PlayMode/Adapters/InputSystemAdapterTests.cs` | `InputSystem.Tests.PlayMode` | PlayMode | 17 | Medium | PlayMode アセンブリ（FacialController） |
| `com.hidano.facialcontrol.inputsystem/Tests/PlayMode/Adapters/Processors/AnalogProcessorRegistrationTests.cs` | `InputSystem.Tests.PlayMode` | PlayMode | 7 | Medium | PlayMode アセンブリ |
| `com.hidano.facialcontrol.inputsystem/Tests/PlayMode/Integration/InputSystemAdapterBindingIntegrationTests.cs` | `InputSystem.Tests.PlayMode` | PlayMode | 14 | Medium | PlayMode アセンブリ |
| `com.hidano.facialcontrol.inputsystem/Tests/PlayMode/Performance/AnalogProcessorAllocationTests.cs` | `InputSystem.Tests.PlayMode` | PlayMode | 7 | Medium | PlayMode アセンブリ |
| `com.hidano.facialcontrol.inputsystem/Tests/PlayMode/Performance/ExpressionInputSourceAdapterAllocationTests.cs` | `InputSystem.Tests.PlayMode` | PlayMode | 4 | Medium | PlayMode アセンブリ（ExpressionInputSourceAdapter） |
| `com.hidano.facialcontrol.inputsystem/Tests/PlayMode/Performance/ExpressionResolverAllocationTests.cs` | `InputSystem.Tests.PlayMode` | PlayMode | 3 | Medium | PlayMode アセンブリ |
| `com.hidano.facialcontrol.lipsync/Tests/EditMode/Adapters/DefaultDeviceEnumeratorTests.cs` | `LipSync.Tests.EditMode` | EditMode | 4 | Small | 禁止 API なし |
| `com.hidano.facialcontrol.lipsync/Tests/EditMode/Adapters/DefaultMicrophoneDeviceEnumeratorTests.cs` | `LipSync.Tests.EditMode` | EditMode | 1 | Medium | 実デバイス |
| `com.hidano.facialcontrol.lipsync/Tests/EditMode/Adapters/DefaultPlayerPrefsBackendTests.cs` | `LipSync.Tests.EditMode` | EditMode | 7 | Medium | PlayerPrefs / EditorPrefs |
| `com.hidano.facialcontrol.lipsync/Tests/EditMode/Adapters/DeviceResolverTests.cs` | `LipSync.Tests.EditMode` | EditMode | 9 | Small | 禁止 API なし |
| `com.hidano.facialcontrol.lipsync/Tests/EditMode/Adapters/LipSyncDeviceStoreTests.cs` | `LipSync.Tests.EditMode` | EditMode | 15 | Medium | PlayerPrefs / EditorPrefs |
| `com.hidano.facialcontrol.lipsync/Tests/EditMode/Adapters/LipSyncPhonemeOverlayInputSourceTests.cs` | `LipSync.Tests.EditMode` | EditMode | 5 | Small | 禁止 API なし |
| `com.hidano.facialcontrol.lipsync/Tests/EditMode/Adapters/MicLipSyncDemoProfileAssetTests.cs` | `LipSync.Tests.EditMode` | EditMode | 2 | Medium | AssetDatabase / Resources, ファイル I/O |
| `com.hidano.facialcontrol.lipsync/Tests/EditMode/Adapters/PhonemeEntryTests.cs` | `LipSync.Tests.EditMode` | EditMode | 4 | Medium | AssetDatabase / Resources |
| `com.hidano.facialcontrol.lipsync/Tests/EditMode/Adapters/PhonemeSnapshotBuilderTests.cs` | `LipSync.Tests.EditMode` | EditMode | 19 | Medium | AssetDatabase / Resources |
| `com.hidano.facialcontrol.lipsync/Tests/EditMode/Adapters/ULipSyncAdapterBindingTests.cs` | `LipSync.Tests.EditMode` | EditMode | 1 | Small | 禁止 API なし |
| `com.hidano.facialcontrol.lipsync/Tests/EditMode/Adapters/ULipSyncEventBridgeTests.cs` | `LipSync.Tests.EditMode` | EditMode | 3 | Medium | MonoBehaviour 生成（AddComponent）（uLipSync） |
| `com.hidano.facialcontrol.lipsync/Tests/EditMode/Adapters/ULipSyncProviderTests.cs` | `LipSync.Tests.EditMode` | EditMode | 15 | Small | 禁止 API なし |
| `com.hidano.facialcontrol.lipsync/Tests/EditMode/Editor/DeviceDescriptorPopupTests.cs` | `LipSync.Tests.EditMode` | EditMode | 2 | Small | 禁止 API なし |
| `com.hidano.facialcontrol.lipsync/Tests/EditMode/Editor/PhonemeEntryListViewTests.cs` | `LipSync.Tests.EditMode` | EditMode | 12 | Small | 禁止 API なし |
| `com.hidano.facialcontrol.lipsync/Tests/EditMode/Editor/ULipSyncAdapterBindingDrawerTests.cs` | `LipSync.Tests.EditMode` | EditMode | 6 | Medium | AssetDatabase / Resources, PlayerPrefs / EditorPrefs |
| `com.hidano.facialcontrol.lipsync/Tests/EditMode/Performance/LipSyncPhonemeOverlayInputSourceAllocationTests.cs` | `LipSync.Tests.EditMode` | EditMode | 1 | Medium | 性能・GC 計測 |
| `com.hidano.facialcontrol.lipsync/Tests/EditMode/Performance/ULipSyncProviderAllocationTests.cs` | `LipSync.Tests.EditMode` | EditMode | 2 | Medium | 性能・GC 計測 |
| `com.hidano.facialcontrol.lipsync/Tests/PlayMode/HotSwap/DeviceHotSwapTests.cs` | `LipSync.Tests.PlayMode` | PlayMode | 3 | Medium | PlayMode アセンブリ |
| `com.hidano.facialcontrol.lipsync/Tests/PlayMode/Integration/PhonemeOverlayIntegrationTests.cs` | `LipSync.Tests.PlayMode` | PlayMode | 7 | Medium | PlayMode アセンブリ |
| `com.hidano.facialcontrol.lipsync/Tests/PlayMode/Integration/PhonemeOverlayPreemptionTests.cs` | `LipSync.Tests.PlayMode` | PlayMode | 8 | Medium | PlayMode アセンブリ |
| `com.hidano.facialcontrol.lipsync/Tests/PlayMode/Lifecycle/ULipSyncAdapterBindingLifecycleTests.cs` | `LipSync.Tests.PlayMode` | PlayMode | 16 | Medium | PlayMode アセンブリ |
| `com.hidano.facialcontrol.lipsync/Tests/PlayMode/LipSyncPhonemeOverlayRegressionTests.cs` | `LipSync.Tests.PlayMode` | PlayMode | 1 | Medium | PlayMode アセンブリ |
| `com.hidano.facialcontrol.lipsync/Tests/PlayMode/MultiCharacter/TenCharacterIsolationTests.cs` | `LipSync.Tests.PlayMode` | PlayMode | 1 | Medium | PlayMode アセンブリ |
| `com.hidano.facialcontrol.lipsync/Tests/PlayMode/Performance/EndToEndGcAllocationTests.cs` | `LipSync.Tests.PlayMode` | PlayMode | 4 | Medium | PlayMode アセンブリ（FacialControlULipSyncBlendShape, uLipSync） |
| `com.hidano.facialcontrol.lipsync/Tests/PlayMode/Performance/PhonemeOverlayPerformanceTests.cs` | `LipSync.Tests.PlayMode` | PlayMode | 3 | Medium | PlayMode アセンブリ |
| `com.hidano.facialcontrol.osc/Tests/EditMode/Adapters/AdapterBindings/ARKit/ArKitOscAdapterBindingTests.cs` | `Osc.Tests.EditMode` | EditMode | 7 | Medium | AssetDatabase / Resources |
| `com.hidano.facialcontrol.osc/Tests/EditMode/Adapters/AdapterBindings/OscReceiverAdapterBindingTests.cs` | `Osc.Tests.EditMode` | EditMode | 24 | Medium | ネットワーク（UDP / Socket / uOSC） |
| `com.hidano.facialcontrol.osc/Tests/EditMode/Adapters/AdapterBindings/OscSenderAdapterBindingTests.cs` | `Osc.Tests.EditMode` | EditMode | 21 | Medium | ネットワーク（UDP / Socket / uOSC） |
| `com.hidano.facialcontrol.osc/Tests/EditMode/Adapters/InputSources/GazeVector2InputSourceTests.cs` | `Osc.Tests.EditMode` | EditMode | 7 | Small | 禁止 API なし |
| `com.hidano.facialcontrol.osc/Tests/EditMode/Adapters/InputSources/OscInputSourceTests.cs` | `Osc.Tests.EditMode` | EditMode | 17 | Small | 禁止 API なし |
| `com.hidano.facialcontrol.osc/Tests/EditMode/Adapters/OSC/AddressPresetEstimatorTests.cs` | `Osc.Tests.EditMode` | EditMode | 9 | Small | 禁止 API なし |
| `com.hidano.facialcontrol.osc/Tests/EditMode/Adapters/OSC/AddressPresetKindTests.cs` | `Osc.Tests.EditMode` | EditMode | 1 | Small | 禁止 API なし |
| `com.hidano.facialcontrol.osc/Tests/EditMode/Adapters/OSC/GazeAdvertisementResolverTests.cs` | `Osc.Tests.EditMode` | EditMode | 9 | Small | 禁止 API なし |
| `com.hidano.facialcontrol.osc/Tests/EditMode/Adapters/OSC/HeartbeatConsistencyCheckerTests.cs` | `Osc.Tests.EditMode` | EditMode | 10 | Small | 禁止 API なし |
| `com.hidano.facialcontrol.osc/Tests/EditMode/Adapters/OSC/HeartbeatHashHelperTests.cs` | `Osc.Tests.EditMode` | EditMode | 9 | Small | 禁止 API なし |
| `com.hidano.facialcontrol.osc/Tests/EditMode/Adapters/OSC/LoopbackSuppressionPolicyTests.cs` | `Osc.Tests.EditMode` | EditMode | 4 | Small | 禁止 API なし |
| `com.hidano.facialcontrol.osc/Tests/EditMode/Adapters/OSC/ManagedAllocationProbeTests.cs` | `Osc.Tests.EditMode` | EditMode | 2 | Medium | 性能・GC 計測 |
| `com.hidano.facialcontrol.osc/Tests/EditMode/Adapters/OSC/OscAddressFormatterTests.cs` | `Osc.Tests.EditMode` | EditMode | 16 | Small | 禁止 API なし |
| `com.hidano.facialcontrol.osc/Tests/EditMode/Adapters/OSC/OscAddressKeyTableTests.cs` | `Osc.Tests.EditMode` | EditMode | 5 | Small | 禁止 API なし |
| `com.hidano.facialcontrol.osc/Tests/EditMode/Adapters/OSC/OscBundleAccumulatorTests.cs` | `Osc.Tests.EditMode` | EditMode | 7 | Small | 禁止 API なし |
| `com.hidano.facialcontrol.osc/Tests/EditMode/Adapters/OSC/OscBundleBuilderTests.cs` | `Osc.Tests.EditMode` | EditMode | 13 | Medium | ネットワーク（UDP / Socket / uOSC） |
| `com.hidano.facialcontrol.osc/Tests/EditMode/Adapters/OSC/OscDatagramRingTests.cs` | `Osc.Tests.EditMode` | EditMode | 3 | Small | 禁止 API なし |
| `com.hidano.facialcontrol.osc/Tests/EditMode/Adapters/OSC/OscDoubleBufferTests.cs` | `Osc.Tests.EditMode` | EditMode | 26 | Small | 禁止 API なし |
| `com.hidano.facialcontrol.osc/Tests/EditMode/Adapters/OSC/OscMappingEntryDtoTests.cs` | `Osc.Tests.EditMode` | EditMode | 7 | Small | 禁止 API なし |
| `com.hidano.facialcontrol.osc/Tests/EditMode/Adapters/OSC/OscMappingTableTests.cs` | `Osc.Tests.EditMode` | EditMode | 27 | Small | 禁止 API なし |
| `com.hidano.facialcontrol.osc/Tests/EditMode/Adapters/OSC/OscMessageClassifierAllocationTests.cs` | `Osc.Tests.EditMode` | EditMode | 1 | Medium | 性能・GC 計測 |
| `com.hidano.facialcontrol.osc/Tests/EditMode/Adapters/OSC/OscMessageClassifierTests.cs` | `Osc.Tests.EditMode` | EditMode | 3 | Small | 禁止 API なし |
| `com.hidano.facialcontrol.osc/Tests/EditMode/Adapters/OSC/OscMessageSerializerTests.cs` | `Osc.Tests.EditMode` | EditMode | 6 | Small | 禁止 API なし |
| `com.hidano.facialcontrol.osc/Tests/EditMode/Adapters/OSC/OscPacketReaderTests.cs` | `Osc.Tests.EditMode` | EditMode | 11 | Small | 禁止 API なし |
| `com.hidano.facialcontrol.osc/Tests/EditMode/Adapters/OSC/OscPortResolverTests.cs` | `Osc.Tests.EditMode` | EditMode | 8 | Medium | ネットワーク（UDP / Socket / uOSC） |
| `com.hidano.facialcontrol.osc/Tests/EditMode/Adapters/OSC/OscReceiveFoundationTests.cs` | `Osc.Tests.EditMode` | EditMode | 3 | Small | 禁止 API なし |
| `com.hidano.facialcontrol.osc/Tests/EditMode/Adapters/OSC/OscReceiverOptionsDtoTests.cs` | `Osc.Tests.EditMode` | EditMode | 5 | Small | 禁止 API なし |
| `com.hidano.facialcontrol.osc/Tests/EditMode/Adapters/OSC/OscReceiverPortAutoIncrementTests.cs` | `Osc.Tests.EditMode` | EditMode | 6 | Medium | ネットワーク（UDP / Socket / uOSC）, MonoBehaviour 生成（AddComponent）（OscReceiver, OscReceiverHost） |
| `com.hidano.facialcontrol.osc/Tests/EditMode/Adapters/OSC/OscSenderEndpointConfigTests.cs` | `Osc.Tests.EditMode` | EditMode | 3 | Small | 禁止 API なし |
| `com.hidano.facialcontrol.osc/Tests/EditMode/Adapters/OSC/OscSenderOptionsDtoTests.cs` | `Osc.Tests.EditMode` | EditMode | 5 | Small | 禁止 API なし |
| `com.hidano.facialcontrol.osc/Tests/EditMode/Adapters/OSC/PerfectSyncEyeLookTests.cs` | `Osc.Tests.EditMode` | EditMode | 11 | Small | 禁止 API なし |
| `com.hidano.facialcontrol.osc/Tests/EditMode/Adapters/OSC/RuntimeMappingResolverTests.cs` | `Osc.Tests.EditMode` | EditMode | 7 | Small | 禁止 API なし |
| `com.hidano.facialcontrol.osc/Tests/EditMode/Adapters/OSC/SenderIdentityTests.cs` | `Osc.Tests.EditMode` | EditMode | 3 | Small | 禁止 API なし |
| `com.hidano.facialcontrol.osc/Tests/EditMode/Adapters/OSC/ZombieEvictionPolicyTests.cs` | `Osc.Tests.EditMode` | EditMode | 7 | Small | 禁止 API なし |
| `com.hidano.facialcontrol.osc/Tests/EditMode/Adapters/Playable/OscReceiverPlayableTests.cs` | `Osc.Tests.EditMode` | EditMode | 17 | Small | 禁止 API なし |
| `com.hidano.facialcontrol.osc/Tests/EditMode/Adapters/RuntimeSettings/OscRuntimeSettingsSOTests.cs` | `Osc.Tests.EditMode` | EditMode | 24 | Small | 禁止 API なし |
| `com.hidano.facialcontrol.osc/Tests/PlayMode/Adapters/InputSources/ArKitOscAnalogSourceTests.cs` | `Osc.Tests.PlayMode` | PlayMode | 17 | Medium | PlayMode アセンブリ（OscReceiver） |
| `com.hidano.facialcontrol.osc/Tests/PlayMode/Adapters/InputSources/OscFloatAnalogSourceTests.cs` | `Osc.Tests.PlayMode` | PlayMode | 17 | Medium | PlayMode アセンブリ（OscReceiver） |
| `com.hidano.facialcontrol.osc/Tests/PlayMode/Adapters/OSC/OscReceiverAnalogListenerTests.cs` | `Osc.Tests.PlayMode` | PlayMode | 14 | Medium | PlayMode アセンブリ（OscReceiver） |
| `com.hidano.facialcontrol.osc/Tests/PlayMode/Integration/OscBundleAtomicityTests.cs` | `Osc.Tests.PlayMode` | PlayMode | 1 | Medium | PlayMode アセンブリ |
| `com.hidano.facialcontrol.osc/Tests/PlayMode/Integration/OscFailSafeRevertTests.cs` | `Osc.Tests.PlayMode` | PlayMode | 2 | Medium | PlayMode アセンブリ |
| `com.hidano.facialcontrol.osc/Tests/PlayMode/Integration/OscGazeE2ETests.cs` | `Osc.Tests.PlayMode` | PlayMode | 12 | Medium | PlayMode アセンブリ（FacialController, OscReceiver, OscSender） |
| `com.hidano.facialcontrol.osc/Tests/PlayMode/Integration/OscIntegrationTests.cs` | `Osc.Tests.PlayMode` | PlayMode | 10 | Medium | PlayMode アセンブリ（FacialController, OscReceiver, OscSender） |
| `com.hidano.facialcontrol.osc/Tests/PlayMode/Integration/OscLoopbackSuppressionTests.cs` | `Osc.Tests.PlayMode` | PlayMode | 2 | Medium | PlayMode アセンブリ |
| `com.hidano.facialcontrol.osc/Tests/PlayMode/Integration/OscMultiEndpointTests.cs` | `Osc.Tests.PlayMode` | PlayMode | 1 | Medium | PlayMode アセンブリ |
| `com.hidano.facialcontrol.osc/Tests/PlayMode/Integration/OscReceiverAdapterBindingIntegrationTests.cs` | `Osc.Tests.PlayMode` | PlayMode | 26 | Medium | PlayMode アセンブリ（OscReceiver, OscSender） |
| `com.hidano.facialcontrol.osc/Tests/PlayMode/Integration/OscSendReceiveE2ETests.cs` | `Osc.Tests.PlayMode` | PlayMode | 1 | Medium | PlayMode アセンブリ（FacialController） |
| `com.hidano.facialcontrol.osc/Tests/PlayMode/Integration/OscSendReceiveTests.cs` | `Osc.Tests.PlayMode` | PlayMode | 44 | Medium | PlayMode アセンブリ（OscReceiver, OscSender, uOscClient） |
| `com.hidano.facialcontrol.osc/Tests/PlayMode/Integration/OscStalenessAndMixedWeightAtomicityTests.cs` | `Osc.Tests.PlayMode` | PlayMode | 3 | Medium | PlayMode アセンブリ |
| `com.hidano.facialcontrol.osc/Tests/PlayMode/Integration/OscUdpReceiveLoopTests.cs` | `Osc.Tests.PlayMode` | PlayMode | 4 | Medium | PlayMode アセンブリ |
| `com.hidano.facialcontrol.osc/Tests/PlayMode/Integration/OscZombieEvictionTests.cs` | `Osc.Tests.PlayMode` | PlayMode | 1 | Medium | PlayMode アセンブリ |
| `com.hidano.facialcontrol.osc/Tests/PlayMode/Performance/OscBundleMtuTests.cs` | `Osc.Tests.PlayMode` | PlayMode | 1 | Medium | PlayMode アセンブリ |
| `com.hidano.facialcontrol.osc/Tests/PlayMode/Performance/OscReceiverGCAllocationTests.cs` | `Osc.Tests.PlayMode` | PlayMode | 6 | Medium | PlayMode アセンブリ |
| `com.hidano.facialcontrol.osc/Tests/PlayMode/Performance/OscReceiverGCWorkloadSelfValidationTests.cs` | `Osc.Tests.PlayMode` | PlayMode | 1 | Medium | PlayMode アセンブリ |
| `com.hidano.facialcontrol.osc/Tests/PlayMode/Performance/OscSenderGCAllocationTests.cs` | `Osc.Tests.PlayMode` | PlayMode | 4 | Medium | PlayMode アセンブリ |
| `com.hidano.facialcontrol.rec/Tests/EditMode/PlaybackUseCaseTests.cs` | `Rec.Tests.EditMode` | EditMode | 11 | Small | 禁止 API なし |
| `com.hidano.facialcontrol.rec/Tests/EditMode/RecAnalogInjectorTests.cs` | `Rec.Tests.EditMode` | EditMode | 9 | Small | 禁止 API なし |
| `com.hidano.facialcontrol.rec/Tests/EditMode/RecBinaryFormatTests.cs` | `Rec.Tests.EditMode` | EditMode | 5 | Small | 禁止 API なし |
| `com.hidano.facialcontrol.rec/Tests/EditMode/RecCharacterBindingInspectorTests.cs` | `Rec.Tests.EditMode` | EditMode | 1 | Medium | MonoBehaviour 生成（AddComponent）（FacialController, RecCharacterBinding） |
| `com.hidano.facialcontrol.rec/Tests/EditMode/RecDomainContractsTests.cs` | `Rec.Tests.EditMode` | EditMode | 12 | Small | 禁止 API なし |
| `com.hidano.facialcontrol.rec/Tests/EditMode/RecEventChunkQueueTests.cs` | `Rec.Tests.EditMode` | EditMode | 3 | Small | 禁止 API なし |
| `com.hidano.facialcontrol.rec/Tests/EditMode/RecFileReaderTests.cs` | `Rec.Tests.EditMode` | EditMode | 4 | Medium | ファイル I/O |
| `com.hidano.facialcontrol.rec/Tests/EditMode/RecPlaybackAnalogSourceTests.cs` | `Rec.Tests.EditMode` | EditMode | 4 | Small | 禁止 API なし |
| `com.hidano.facialcontrol.rec/Tests/EditMode/RecPlaybackSchedulerTests.cs` | `Rec.Tests.EditMode` | EditMode | 4 | Small | 禁止 API なし |
| `com.hidano.facialcontrol.rec/Tests/EditMode/RecSidecarPathTests.cs` | `Rec.Tests.EditMode` | EditMode | 3 | Small | 禁止 API なし |
| `com.hidano.facialcontrol.rec/Tests/EditMode/RecStreamWriterTests.cs` | `Rec.Tests.EditMode` | EditMode | 3 | Medium | ファイル I/O, 実時間（DateTime.Now / Stopwatch） |
| `com.hidano.facialcontrol.rec/Tests/EditMode/RecTriggerInjectorTests.cs` | `Rec.Tests.EditMode` | EditMode | 6 | Small | 禁止 API なし |
| `com.hidano.facialcontrol.rec/Tests/EditMode/RecordingUseCaseTests.cs` | `Rec.Tests.EditMode` | EditMode | 7 | Small | 禁止 API なし |
| `com.hidano.facialcontrol.rec/Tests/PlayMode/RecCharacterBindingPlayModeTests.cs` | `Rec.Tests.PlayMode` | PlayMode | 7 | Medium | PlayMode アセンブリ（FacialController, RecCharacterBinding） |
| `com.hidano.facialcontrol.rec/Tests/PlayMode/RecGcZeroGateTests.cs` | `Rec.Tests.PlayMode` | PlayMode | 3 | Medium | PlayMode アセンブリ |
| `com.hidano.facialcontrol.routing-editor/Tests/EditMode/RoutingEditorLauncherRegistrationTests.cs` | `RoutingEditor.Tests.EditMode` | EditMode | 2 | Medium | EditorWindow / EditorApplication |
| `com.hidano.facialcontrol.routing-editor/Tests/EditMode/RoutingEditorWindowTests.cs` | `RoutingEditor.Tests.EditMode` | EditMode | 7 | Medium | AssetDatabase / Resources, EditorWindow / EditorApplication |
| `com.hidano.facialcontrol/Tests/EditMode/Adapters/AdapterBindingHostTests.cs` | `Tests.EditMode` | EditMode | 17 | Small | 禁止 API なし |
| `com.hidano.facialcontrol/Tests/EditMode/Adapters/AdapterBuildContextTests.cs` | `Tests.EditMode` | EditMode | 9 | Small | 禁止 API なし |
| `com.hidano.facialcontrol/Tests/EditMode/Adapters/AnimationClipCacheTests.cs` | `Tests.EditMode` | EditMode | 23 | Small | 禁止 API なし |
| `com.hidano.facialcontrol/Tests/EditMode/Adapters/Bone/BoneTransformResolverFallbackTests.cs` | `Tests.EditMode` | EditMode | 3 | Small | 禁止 API なし |
| `com.hidano.facialcontrol/Tests/EditMode/Adapters/FileProfileRepositoryTests.cs` | `Tests.EditMode` | EditMode | 21 | Medium | ファイル I/O |
| `com.hidano.facialcontrol/Tests/EditMode/Adapters/InputSources/AnalogBlendShapeInputSourceTests.cs` | `Tests.EditMode` | EditMode | 11 | Medium | MonoBehaviour 生成（AddComponent）（OscReceiver） |
| `com.hidano.facialcontrol/Tests/EditMode/Adapters/InputSources/AnalogObservationSamplerTests.cs` | `Tests.EditMode` | EditMode | 7 | Small | 禁止 API なし |
| `com.hidano.facialcontrol/Tests/EditMode/Adapters/InputSources/GazeInputReaderTests.cs` | `Tests.EditMode` | EditMode | 3 | Small | 禁止 API なし |
| `com.hidano.facialcontrol/Tests/EditMode/Adapters/InputSources/InputSourceRegistryTests.cs` | `Tests.EditMode` | EditMode | 32 | Small | 禁止 API なし |
| `com.hidano.facialcontrol/Tests/EditMode/Adapters/InputSources/OverlayInputSourceTests.cs` | `Tests.EditMode` | EditMode | 15 | Small | 禁止 API なし |
| `com.hidano.facialcontrol/Tests/EditMode/Adapters/Json/InputSourceDtoTests.cs` | `Tests.EditMode` | EditMode | 13 | Small | 禁止 API なし |
| `com.hidano.facialcontrol/Tests/EditMode/Adapters/Json/IntermediateJsonSchemaV2Tests.cs` | `Tests.EditMode` | EditMode | 6 | Small | 禁止 API なし |
| `com.hidano.facialcontrol/Tests/EditMode/Adapters/Json/OverlaySnapshotDtoRecursionTests.cs` | `Tests.EditMode` | EditMode | 4 | Small | 禁止 API なし |
| `com.hidano.facialcontrol/Tests/EditMode/Adapters/Json/SystemTextJsonParserTests.cs` | `Tests.EditMode` | EditMode | 59 | Small | 禁止 API なし |
| `com.hidano.facialcontrol/Tests/EditMode/Adapters/NativeArrayPoolTests.cs` | `Tests.EditMode` | EditMode | 18 | Small | 禁止 API なし |
| `com.hidano.facialcontrol/Tests/EditMode/Adapters/Playable/FacialControllerConflictResolverTests.cs` | `Tests.EditMode` | EditMode | 7 | Small | 禁止 API なし |
| `com.hidano.facialcontrol/Tests/EditMode/Adapters/Playable/FacialControllerRendererOwnershipTests.cs` | `Tests.EditMode` | EditMode | 9 | Medium | MonoBehaviour 生成（AddComponent）（FacialController） |
| `com.hidano.facialcontrol/Tests/EditMode/Adapters/Playable/SkinnedMeshRendererBlendShapeWriterTests.cs` | `Tests.EditMode` | EditMode | 2 | Small | 禁止 API なし |
| `com.hidano.facialcontrol/Tests/EditMode/Adapters/RuntimeSettings/AdapterRuntimeSettingsBaseTests.cs` | `Tests.EditMode` | EditMode | 4 | Small | 禁止 API なし |
| `com.hidano.facialcontrol/Tests/EditMode/Adapters/RuntimeSettings/AdapterRuntimeSettingsCollectionSOTests.cs` | `Tests.EditMode` | EditMode | 12 | Medium | AssetDatabase / Resources |
| `com.hidano.facialcontrol/Tests/EditMode/Adapters/ScriptableObject/BaseExpressionSerializableTests.cs` | `Tests.EditMode` | EditMode | 4 | Small | 禁止 API なし |
| `com.hidano.facialcontrol/Tests/EditMode/Adapters/ScriptableObject/FacialCharacterProfileConverterPhonemeOverlayTests.cs` | `Tests.EditMode` | EditMode | 3 | Medium | AssetDatabase / Resources |
| `com.hidano.facialcontrol/Tests/EditMode/Adapters/ScriptableObject/FacialCharacterProfileSOGazeAccessorPerformanceTests.cs` | `Tests.EditMode` | EditMode | 1 | Small | 禁止 API なし |
| `com.hidano.facialcontrol/Tests/EditMode/Adapters/ScriptableObject/FacialCharacterProfileSOTests.cs` | `Tests.EditMode` | EditMode | 9 | Medium | AssetDatabase / Resources, ファイル I/O |
| `com.hidano.facialcontrol/Tests/EditMode/Adapters/ScriptableObject/GazeChannelResolverTests.cs` | `Tests.EditMode` | EditMode | 3 | Small | 禁止 API なし |
| `com.hidano.facialcontrol/Tests/EditMode/Adapters/ScriptableObject/Serializable/OverlaySlotBindingSerializableTests.cs` | `Tests.EditMode` | EditMode | 5 | Small | 禁止 API なし |
| `com.hidano.facialcontrol/Tests/EditMode/Application/ExpressionUseCaseTests.cs` | `Tests.EditMode` | EditMode | 38 | Small | 禁止 API なし |
| `com.hidano.facialcontrol/Tests/EditMode/Application/LayerUseCaseTests.cs` | `Tests.EditMode` | EditMode | 67 | Small | 禁止 API なし |
| `com.hidano.facialcontrol/Tests/EditMode/Domain/IAdapterBindingDefaultLayerInputsContractTests.cs` | `Tests.EditMode` | EditMode | 2 | Small | 禁止 API なし |
| `com.hidano.facialcontrol/Tests/EditMode/Editor/AdapterBindingsListViewDefaultLayerInputsTests.cs` | `Tests.EditMode` | EditMode | 1 | Medium | AssetDatabase / Resources |
| `com.hidano.facialcontrol/Tests/EditMode/Editor/AutoExport/FacialCharacterProfileAutoExporterTests.cs` | `Tests.EditMode` | EditMode | 1 | Medium | AssetDatabase / Resources, ファイル I/O |
| `com.hidano.facialcontrol/Tests/EditMode/Editor/AutoExport/FacialCharacterProfileExporterTests.cs` | `Tests.EditMode` | EditMode | 8 | Medium | ファイル I/O |
| `com.hidano.facialcontrol/Tests/EditMode/Editor/BlendShapeNameProviderTests.cs` | `Tests.EditMode` | EditMode | 12 | Small | 禁止 API なし |
| `com.hidano.facialcontrol/Tests/EditMode/Editor/Inspector/AdapterBindings/AdapterBindingDiscoveryTests.cs` | `Tests.EditMode` | EditMode | 10 | Small | 禁止 API なし |
| `com.hidano.facialcontrol/Tests/EditMode/Editor/Inspector/AdapterBindings/AdapterBindingsListViewTests.cs` | `Tests.EditMode` | EditMode | 3 | Medium | AssetDatabase / Resources |
| `com.hidano.facialcontrol/Tests/EditMode/Editor/Inspector/AdapterBindings/FacialCharacterProfileAssetGuardTests.cs` | `Tests.EditMode` | EditMode | 6 | Medium | AssetDatabase / Resources |
| `com.hidano.facialcontrol/Tests/EditMode/Editor/Inspector/FacialCharacterProfileSOInspectorTests.cs` | `Tests.EditMode` | EditMode | 8 | Medium | ファイル I/O, EditorWindow / EditorApplication |
| `com.hidano.facialcontrol/Tests/EditMode/Editor/Inspector/FacialControllerEditorTests.cs` | `Tests.EditMode` | EditMode | 5 | Medium | MonoBehaviour 生成（AddComponent）（FacialController） |
| `com.hidano.facialcontrol/Tests/EditMode/Editor/Inspector/RuntimeSettings/AdapterRuntimeSettingsCollectionEditorTests.cs` | `Tests.EditMode` | EditMode | 4 | Medium | AssetDatabase / Resources |
| `com.hidano.facialcontrol/Tests/EditMode/Editor/Inspector/RuntimeSettings/AdapterRuntimeSettingsTypeRegistryTests.cs` | `Tests.EditMode` | EditMode | 7 | Small | 禁止 API なし |
| `com.hidano.facialcontrol/Tests/EditMode/Editor/Inspector/SampleAssetsAreInSyncTests.cs` | `Tests.EditMode` | EditMode | 8 | Medium | ファイル I/O |
| `com.hidano.facialcontrol/Tests/EditMode/Editor/ListViewFoldoutStatePersistenceTests.cs` | `Tests.EditMode` | EditMode | 5 | Small | 禁止 API なし |
| `com.hidano.facialcontrol/Tests/EditMode/Editor/ProfileCreationTests.cs` | `Tests.EditMode` | EditMode | 23 | Small | 禁止 API なし |
| `com.hidano.facialcontrol/Tests/EditMode/Editor/RendererPathsCacheUpdateTests.cs` | `Tests.EditMode` | EditMode | 7 | Small | 禁止 API なし |
| `com.hidano.facialcontrol/Tests/EditMode/Editor/SampleExpressionsTests.cs` | `Tests.EditMode` | EditMode | 15 | Small | 禁止 API なし |
| `com.hidano.facialcontrol/Tests/EditMode/Editor/Sampling/AnimationClipExpressionSamplerBenchmarkTests.cs` | `Tests.EditMode` | EditMode | 2 | Medium | AssetDatabase / Resources, 実時間（DateTime.Now / Stopwatch）, 性能・GC 計測 |
| `com.hidano.facialcontrol/Tests/EditMode/Editor/Sampling/AnimationClipExpressionSamplerTests.cs` | `Tests.EditMode` | EditMode | 17 | Small | 禁止 API なし |
| `com.hidano.facialcontrol/Tests/EditMode/Editor/Sampling/EditorOnlyVisibilityTests.cs` | `Tests.EditMode` | EditMode | 3 | Medium | AssetDatabase / Resources, ファイル I/O |
| `com.hidano.facialcontrol/Tests/EditMode/Editor/Windows/Routing/Logic/AutoWireServiceTests.cs` | `Tests.EditMode` | EditMode | 2 | Small | 禁止 API なし |
| `com.hidano.facialcontrol/Tests/EditMode/Editor/Windows/Routing/Logic/InvalidIdValidatorTests.cs` | `Tests.EditMode` | EditMode | 2 | Small | 禁止 API なし |
| `com.hidano.facialcontrol/Tests/EditMode/Editor/Windows/Routing/Logic/LayerPriorityNormalizerTests.cs` | `Tests.EditMode` | EditMode | 9 | Small | 禁止 API なし |
| `com.hidano.facialcontrol/Tests/EditMode/Editor/Windows/Routing/Logic/PhonemeSlotInitializerTests.cs` | `Tests.EditMode` | EditMode | 4 | Small | 禁止 API なし |
| `com.hidano.facialcontrol/Tests/EditMode/Editor/Windows/Routing/Logic/RoutingGraphModelBuilderTests.cs` | `Tests.EditMode` | EditMode | 2 | Small | 禁止 API なし |
| `com.hidano.facialcontrol/Tests/EditMode/Editor/Windows/Routing/Logic/SourcePortEnumeratorTests.cs` | `Tests.EditMode` | EditMode | 7 | Small | 禁止 API なし |
| `com.hidano.facialcontrol/Tests/EditMode/Editor/Windows/Routing/Logic/WiringSerializedMapperTests.cs` | `Tests.EditMode` | EditMode | 6 | Small | 禁止 API なし |
| `com.hidano.facialcontrol/Tests/EditMode/Editor/Windows/Routing/RoutingEditorLauncherTests.cs` | `Tests.EditMode` | EditMode | 4 | Medium | EditorWindow / EditorApplication |
| `com.hidano.facialcontrol/Tests/PlayMode/Adapters/AdapterBindingHostLifecycleTests.cs` | `Tests.PlayMode` | PlayMode | 2 | Medium | PlayMode アセンブリ（TestLifetimeScope） |
| `com.hidano.facialcontrol/Tests/PlayMode/Adapters/Bone/BoneTransformResolverTests.cs` | `Tests.PlayMode` | PlayMode | 22 | Medium | PlayMode アセンブリ |
| `com.hidano.facialcontrol/Tests/PlayMode/Adapters/Bone/HumanoidBoneAutoAssignerTests.cs` | `Tests.PlayMode` | PlayMode | 11 | Medium | PlayMode アセンブリ |
| `com.hidano.facialcontrol/Tests/PlayMode/Adapters/Playable/FacialControllerDuplicateOwnershipTests.cs` | `Tests.PlayMode` | PlayMode | 6 | Medium | PlayMode アセンブリ（FacialController） |
| `com.hidano.facialcontrol/Tests/PlayMode/Adapters/Playable/FacialControllerGazeChannelTests.cs` | `Tests.PlayMode` | PlayMode | 1 | Medium | PlayMode アセンブリ（FacialController） |
| `com.hidano.facialcontrol/Tests/PlayMode/Adapters/Playable/FacialControllerOutputPipelineRegressionTests.cs` | `Tests.PlayMode` | PlayMode | 1 | Medium | PlayMode アセンブリ（FacialController） |
| `com.hidano.facialcontrol/Tests/PlayMode/Adapters/UnityTimeProviderTests.cs` | `Tests.PlayMode` | PlayMode | 2 | Medium | PlayMode アセンブリ |
| `com.hidano.facialcontrol/Tests/PlayMode/Domain/BonePoseComposerTests.cs` | `Tests.PlayMode` | PlayMode | 9 | Medium | PlayMode アセンブリ |
| `com.hidano.facialcontrol/Tests/PlayMode/Domain/MultiSourceBlendThreeBindingsTests.cs` | `Tests.PlayMode` | PlayMode | 1 | Medium | PlayMode アセンブリ |
| `com.hidano.facialcontrol/Tests/PlayMode/Integration/ArkitProfileRegressionTests.cs` | `Tests.PlayMode` | PlayMode | 3 | Medium | PlayMode アセンブリ |
| `com.hidano.facialcontrol/Tests/PlayMode/Integration/EmotionLipSyncBlendIntegrationTests.cs` | `Tests.PlayMode` | PlayMode | 2 | Medium | PlayMode アセンブリ |
| `com.hidano.facialcontrol/Tests/PlayMode/Integration/FacialControllerInputSourceWeightTests.cs` | `Tests.PlayMode` | PlayMode | 4 | Medium | PlayMode アセンブリ（FacialController） |
| `com.hidano.facialcontrol/Tests/PlayMode/Integration/LayerLifecycleZeroFadeTests.cs` | `Tests.PlayMode` | PlayMode | 2 | Medium | PlayMode アセンブリ（LayerUseCaseHostBehaviour） |
| `com.hidano.facialcontrol/Tests/PlayMode/Integration/MultiRendererTests.cs` | `Tests.PlayMode` | PlayMode | 12 | Medium | PlayMode アセンブリ（FacialController） |
| `com.hidano.facialcontrol/Tests/PlayMode/Integration/SuppressOverlayRegressionTests.cs` | `Tests.PlayMode` | PlayMode | 3 | Medium | PlayMode アセンブリ |
| `com.hidano.facialcontrol/Tests/PlayMode/Integration/TransitionIntegrationTests.cs` | `Tests.PlayMode` | PlayMode | 8 | Medium | PlayMode アセンブリ |
| `com.hidano.facialcontrol/Tests/PlayMode/Performance/AdapterBindingHostAllocationTests.cs` | `Tests.PlayMode` | PlayMode | 6 | Medium | PlayMode アセンブリ |
| `com.hidano.facialcontrol/Tests/PlayMode/Performance/FacialControllerGcZeroGateTests.cs` | `Tests.PlayMode` | PlayMode | 1 | Medium | PlayMode アセンブリ（FacialController） |
| `com.hidano.facialcontrol/Tests/PlayMode/Performance/FacialControllerLifetimeScopePerformanceTests.cs` | `Tests.PlayMode` | PlayMode | 1 | Medium | PlayMode アセンブリ（TestAppLifetimeScope） |
| `com.hidano.facialcontrol/Tests/PlayMode/Performance/GazeChannelGcZeroGateTests.cs` | `Tests.PlayMode` | PlayMode | 1 | Medium | PlayMode アセンブリ |
| `com.hidano.facialcontrol/Tests/PlayMode/Performance/MultiCharacterAggregatorPerformanceTests.cs` | `Tests.PlayMode` | PlayMode | 1 | Medium | PlayMode アセンブリ |
| `com.hidano.facialcontrol/Tests/PlayMode/Performance/MultiCharacterPerformanceTests.cs` | `Tests.PlayMode` | PlayMode | 3 | Medium | PlayMode アセンブリ（FacialController） |
| `com.hidano.facialcontrol/Tests/PlayMode/Performance/NativeArrayLeakTests.cs` | `Tests.PlayMode` | PlayMode | 3 | Medium | PlayMode アセンブリ（FacialController） |
| `com.hidano.facialcontrol/Tests/PlayMode/Performance/OverlayInputSourcePerformanceTests.cs` | `Tests.PlayMode` | PlayMode | 1 | Medium | PlayMode アセンブリ |
| `com.hidano.facialcontrol/Tests/PlayMode/Performance/SetWeightZeroAllocationTests.cs` | `Tests.PlayMode` | PlayMode | 1 | Medium | PlayMode アセンブリ |
| `com.hidano.facialcontrol/Tests/Small/Adapters/GazeSourceContractsTests.cs` | `Tests.Small` | EditMode | 3 | Small | 禁止 API なし |
| `com.hidano.facialcontrol/Tests/Small/Application/ARKitUseCaseTests.cs` | `Tests.Small` | EditMode | 23 | Small | 禁止 API なし |
| `com.hidano.facialcontrol/Tests/Small/Application/Layer2ActiveExpressionProviderTests.cs` | `Tests.Small` | EditMode | 6 | Small | 禁止 API なし |
| `com.hidano.facialcontrol/Tests/Small/Application/ProfileUseCaseTests.cs` | `Tests.Small` | EditMode | 29 | Small | 禁止 API なし |
| `com.hidano.facialcontrol/Tests/Small/Domain/ARKitDetectorTests.cs` | `Tests.Small` | EditMode | 43 | Small | 禁止 API なし |
| `com.hidano.facialcontrol/Tests/Small/Domain/AdapterSlugTests.cs` | `Tests.Small` | EditMode | 47 | Small | 禁止 API なし |
| `com.hidano.facialcontrol/Tests/Small/Domain/BlendShapeMappingTests.cs` | `Tests.Small` | EditMode | 16 | Small | 禁止 API なし |
| `com.hidano.facialcontrol/Tests/Small/Domain/BlendShapeSnapshotTests.cs` | `Tests.Small` | EditMode | 7 | Small | 禁止 API なし |
| `com.hidano.facialcontrol/Tests/Small/Domain/BoneSnapshotTests.cs` | `Tests.Small` | EditMode | 8 | Small | 禁止 API なし |
| `com.hidano.facialcontrol/Tests/Small/Domain/ExclusionResolverTests.cs` | `Tests.Small` | EditMode | 34 | Small | 禁止 API なし |
| `com.hidano.facialcontrol/Tests/Small/Domain/ExpressionSnapshotTests.cs` | `Tests.Small` | EditMode | 6 | Small | 禁止 API なし |
| `com.hidano.facialcontrol/Tests/Small/Domain/ExpressionTests.cs` | `Tests.Small` | EditMode | 11 | Small | 禁止 API なし |
| `com.hidano.facialcontrol/Tests/Small/Domain/ExpressionTriggerInputSourceBaseTests.cs` | `Tests.Small` | EditMode | 43 | Small | 禁止 API なし |
| `com.hidano.facialcontrol/Tests/Small/Domain/FacialProfileTests.cs` | `Tests.Small` | EditMode | 46 | Small | 禁止 API なし |
| `com.hidano.facialcontrol/Tests/Small/Domain/GazeSourceIdConventionTests.cs` | `Tests.Small` | EditMode | 15 | Small | 禁止 API なし |
| `com.hidano.facialcontrol/Tests/Small/Domain/IInputSourceContractTests.cs` | `Tests.Small` | EditMode | 7 | Small | 禁止 API なし |
| `com.hidano.facialcontrol/Tests/Small/Domain/ITimeProviderContractTests.cs` | `Tests.Small` | EditMode | 3 | Small | 禁止 API なし |
| `com.hidano.facialcontrol/Tests/Small/Domain/InputSourceIdTests.cs` | `Tests.Small` | EditMode | 25 | Small | 禁止 API なし |
| `com.hidano.facialcontrol/Tests/Small/Domain/InvalidSlotReferenceTests.cs` | `Tests.Small` | EditMode | 8 | Small | 禁止 API なし |
| `com.hidano.facialcontrol/Tests/Small/Domain/LayerBlenderTests.cs` | `Tests.Small` | EditMode | 3 | Small | 禁止 API なし |
| `com.hidano.facialcontrol/Tests/Small/Domain/LayerDefinitionTests.cs` | `Tests.Small` | EditMode | 11 | Small | 禁止 API なし |
| `com.hidano.facialcontrol/Tests/Small/Domain/LayerInputSourceAggregatorTests.cs` | `Tests.Small` | EditMode | 38 | Small | 禁止 API なし |
| `com.hidano.facialcontrol/Tests/Small/Domain/LayerInputSourceRegistryTests.cs` | `Tests.Small` | EditMode | 23 | Small | 禁止 API なし |
| `com.hidano.facialcontrol/Tests/Small/Domain/LayerInputSourceWeightBufferTests.cs` | `Tests.Small` | EditMode | 31 | Small | 禁止 API なし |
| `com.hidano.facialcontrol/Tests/Small/Domain/LayerOverrideMaskTests.cs` | `Tests.Small` | EditMode | 4 | Small | 禁止 API なし |
| `com.hidano.facialcontrol/Tests/Small/Domain/LayerSourceWeightEntryTests.cs` | `Tests.Small` | EditMode | 11 | Small | 禁止 API なし |
| `com.hidano.facialcontrol/Tests/Small/Domain/ManualTimeProviderTests.cs` | `Tests.Small` | EditMode | 4 | Small | 禁止 API なし |
| `com.hidano.facialcontrol/Tests/Small/Domain/OverlaySlotBindingTests.cs` | `Tests.Small` | EditMode | 9 | Small | 禁止 API なし |
| `com.hidano.facialcontrol/Tests/Small/Domain/PhonemeOverlaySlotsTests.cs` | `Tests.Small` | EditMode | 8 | Small | 禁止 API なし |
| `com.hidano.facialcontrol/Tests/Small/Domain/Services/ExpressionResolverTests.cs` | `Tests.Small` | EditMode | 6 | Small | 禁止 API なし |
| `com.hidano.facialcontrol/Tests/Small/Domain/Services/FacialInputObservationBusTests.cs` | `Tests.Small` | EditMode | 9 | Small | 禁止 API なし |
| `com.hidano.facialcontrol/Tests/Small/Domain/Services/FacialOutputBusTests.cs` | `Tests.Small` | EditMode | 10 | Small | 禁止 API なし |
| `com.hidano.facialcontrol/Tests/Small/Domain/TransitionCalculatorTests.cs` | `Tests.Small` | EditMode | 42 | Small | 禁止 API なし |
| `com.hidano.facialcontrol/Tests/Small/Domain/ValueProviderInputSourceBaseTests.cs` | `Tests.Small` | EditMode | 9 | Small | 禁止 API なし |
| `com.hidano.facialcontrol/Tests/Small/Domain/WeightOneExactOutputContractTests.cs` | `Tests.Small` | EditMode | 6 | Small | 禁止 API なし |
| `com.hidano.facialcontrol/Tests/Small/Testing/TestSizeDeclarationTests.cs` | `Tests.Small` | EditMode | 2 | Small | 禁止 API なし |
| `com.hidano.facialcontrol/Tests/Small/Testing/TestSizeMechanismTests.cs` | `Tests.Small` | EditMode | 11 | Small | 禁止 API なし |
| `com.hidano.facialcontrol.timeline/Tests/EditMode/FacialTimelineBakeAssetTests.cs` | `Timeline.Tests.EditMode` | EditMode | 3 | Small | 禁止 API なし |
| `com.hidano.facialcontrol.timeline/Tests/EditMode/FacialTimelineHashCalculatorTests.cs` | `Timeline.Tests.EditMode` | EditMode | 6 | Small | 禁止 API なし |
| `com.hidano.facialcontrol.timeline/Tests/EditMode/FacialTimelineReceiverTests.cs` | `Timeline.Tests.EditMode` | EditMode | 10 | Medium | MonoBehaviour 生成（AddComponent）（FacialController, FacialTimelineReceiver） |
| `com.hidano.facialcontrol.timeline/Tests/EditMode/FacialTimelineTrackAssetTests.cs` | `Timeline.Tests.EditMode` | EditMode | 3 | Small | 禁止 API なし |
| `com.hidano.facialcontrol.timeline/Tests/EditMode/FacialTimelineValidatorTests.cs` | `Timeline.Tests.EditMode` | EditMode | 8 | Small | 禁止 API なし |
| `com.hidano.facialcontrol.timeline/Tests/EditMode/FacialTrackMixerBehaviourTests.cs` | `Timeline.Tests.EditMode` | EditMode | 4 | Medium | MonoBehaviour 生成（AddComponent）（FacialTimelineReceiver） |
| `com.hidano.facialcontrol.timeline/Tests/EditMode/RecToTimelineExportWorkflowTests.cs` | `Timeline.Tests.EditMode` | EditMode | 3 | Medium | AssetDatabase / Resources, ファイル I/O, MonoBehaviour 生成（AddComponent）（FacialTimelineReceiver） |
| `com.hidano.facialcontrol.timeline/Tests/EditMode/RecToTimelineExporterTests.cs` | `Timeline.Tests.EditMode` | EditMode | 2 | Small | 禁止 API なし |
| `com.hidano.facialcontrol.timeline/Tests/EditMode/TimelineAdapterBindingTests.cs` | `Timeline.Tests.EditMode` | EditMode | 5 | Small | 禁止 API なし |
| `com.hidano.facialcontrol.timeline/Tests/EditMode/TimelineAnalogInputSourceTests.cs` | `Timeline.Tests.EditMode` | EditMode | 3 | Small | 禁止 API なし |
| `com.hidano.facialcontrol.timeline/Tests/EditMode/TimelineBakeDirtyWatcherTests.cs` | `Timeline.Tests.EditMode` | EditMode | 3 | Medium | AssetDatabase / Resources, MonoBehaviour 生成（AddComponent）（FacialController, FacialTimelineReceiver） |
| `com.hidano.facialcontrol.timeline/Tests/EditMode/TimelineBakeServiceTests.cs` | `Timeline.Tests.EditMode` | EditMode | 5 | Small | 禁止 API なし |
| `com.hidano.facialcontrol.timeline/Tests/EditMode/TimelineBakedValueSinkTests.cs` | `Timeline.Tests.EditMode` | EditMode | 5 | Small | 禁止 API なし |
| `com.hidano.facialcontrol.timeline/Tests/EditMode/TimelineEventStateReconstructorTests.cs` | `Timeline.Tests.EditMode` | EditMode | 3 | Small | 禁止 API なし |
| `com.hidano.facialcontrol.timeline/Tests/EditMode/TimelineExpressionStateSinkTests.cs` | `Timeline.Tests.EditMode` | EditMode | 3 | Small | 禁止 API なし |
| `com.hidano.facialcontrol.timeline/Tests/EditMode/TimelineGazeInputSourceTests.cs` | `Timeline.Tests.EditMode` | EditMode | 4 | Small | 禁止 API なし |
| `com.hidano.facialcontrol.timeline/Tests/PlayMode/TimelineDegradationIntegrationTests.cs` | `Timeline.Tests.PlayMode` | PlayMode | 3 | Medium | PlayMode アセンブリ（FacialTimelineReceiver） |
| `com.hidano.facialcontrol.timeline/Tests/PlayMode/TimelineGcZeroGateTests.cs` | `Timeline.Tests.PlayMode` | PlayMode | 3 | Medium | PlayMode アセンブリ（FacialTimelineReceiver） |
| `com.hidano.facialcontrol.timeline/Tests/PlayMode/TimelineLiveEquivalenceIntegrationTests.cs` | `Timeline.Tests.PlayMode` | PlayMode | 4 | Medium | PlayMode アセンブリ（FacialTimelineReceiver） |

## 今後の昇格候補（Medium → Small）

- `OscSenderAdapterBindingTests` / `OscBundleBuilderTests`: `IDatagramSender` の `FakeDatagramSender` を `OscSender.SetBundleSender` で注入すれば bundle の送信内容をメモリ上で検証できる
- `LipSyncDeviceStoreTests`: `FakePlayerPrefsBackend` を使う fixture は既に Small 相当。`PlayerPrefs.DeleteKey` の後始末を外せば昇格できる
- `RecStreamWriterTests`: `Func<string, Stream>` に `MemoryStream` を渡し、Stopwatch 待ちをイベント同期に置き換える
- `FacialControllerRendererOwnershipTests` 等の `FacialController` 依存: Humble Object 化が前提（docs/testing.md「Small で書けないとき」）
