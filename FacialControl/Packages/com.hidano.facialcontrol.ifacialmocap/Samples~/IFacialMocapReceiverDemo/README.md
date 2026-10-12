# IFacialMocapReceiverDemo

`IFacialMocapReceiverAdapterBinding` の受信専用サンプルです。`IFacialMocapReceiverDemo.unity` を開き、
お手持ちのキャラモデルを Scene に置いた状態で Play すると、`49983` で受信した iFacialMocap (iOS) の
ARKit 互換 BlendShape をモデルの BlendShape に反映します。視線・頭部はモデルのボーンへ結線すると反映されます。

## 同梱されているもの

| ファイル | 役割 |
|---|---|
| `IFacialMocapReceiverDemo.unity` | `FacialController` と `IFacialMocapReceiverDemoProfile` を結線済みの最小 Scene |
| `IFacialMocapReceiverDemoProfile.asset` | `IFacialMocapReceiverAdapterBinding`（slug=`ifm`）を結線済みの `FacialCharacterProfileSO`。Layer は 1 つだけ（iFacialMocap 入力を素通し） |
| `IFacialMocapReceiverDemoSettings.asset` | `IFacialMocapRuntimeSettingsSO` を sub-asset に持つ `AdapterRuntimeSettingsCollectionSO`。Profile の binding から参照されます |
| `IFacialMocapDemoRunInBackground.cs` | `Application.runInBackground = true` を有効化する最小 helper（実装 Scene 用） |
| `IFacialMocapReceiverDemoBootstrap.cs` | `IFacialMocapReceiverHost` を直接起動して受信フレーム概要を Console に出す**診断用**サンプル（受信疎通の単独確認用。実装 Scene には含めません） |
| `IFacialMocapReceiverOptions.json` | Scene 内設定と同等の `IFacialMocapOptionsDto` サンプル（参考用） |

> キャラモデル (FBX / VRM / prefab) は同梱していません。お手持ちのものを用意してください。

## 受信内容

- listen port: `49983`（UDP。iFacialMocap (iOS) アプリ側の送信先 PC ポートと一致させてください）
- BlendShape: マッピング空＝iFacialMocap の 52 BlendShape（`_L`/`_R` サフィックス）を ARKit 正準名
  （`jawOpen`, `eyeBlinkLeft` …）へ自動変換して反映
- 視線: `ifm:gaze.left` / `ifm:gaze.right`（正規化 Vector2）
- 頭部: `ifm:head`（軸 0..2 = オイラー角、3..5 = 位置）
- staleness: 1 秒受信が途絶えると base 表情へ復帰（`failSafeMode=revertToBase`）

## 手順（実装 Scene）

1. **シーンを開く**: Project ウィンドウで `IFacialMocapReceiverDemo.unity` をダブルクリック。Hierarchy に
   `iFacialMocap Receiver Demo Character / Main Camera / Directional Light` が並びます。
2. **モデルを置く**: お手持ちのキャラモデルの prefab を `iFacialMocap Receiver Demo Character` の **子**に
   配置するか、モデルの `SkinnedMeshRenderer` を `FacialController` の `Skinned Mesh Renderers` に割り当てます。
3. **BlendShape 名を合わせる**: モデルの BlendShape 名が ARKit 正準名なら設定不要です。異なる場合は
   `IFacialMocapReceiverDemoProfile.asset` を Inspector で開き、**Adapter Bindings → iFacialMocap Receiver →
   BlendShape Mappings** に「iFacialMocap 名 → メッシュ BlendShape 名」を列挙します。
4. **listen port を必要に応じて変更**: `IFacialMocapReceiverDemoSettings.asset` の sub-asset
   `IFacialMocapReceiverSettings` の `Listen Port` を変更します。端末から直接受ける場合は `Send Handshake` を
   有効化し、`Device Address` に iOS 端末の IP を入力してください。
5. **視線（任意）**: Profile の **Gaze** セクションにある既定チャネル `gaze` を使用します。
   入力源の選択は不要で、可動範囲（と Humanoid 以外のモデルでは目のジョイント名）を設定するだけで iFacialMocap の視線に接続できます。
   `ifm:gaze.left` / `ifm:gaze.right` の id を手入力する必要はありません。頭部は従来どおり
   **Analog Bindings** の `TargetKind=BonePose` で `ifm:head` を頭ボーンへ結線します。詳細は
   パッケージ同梱の `Documentation~/usage.md` を参照してください。
6. **Play**: iFacialMocap (iOS) から `49983` に向けてストリームを送ると、モデルの BlendShape が更新されます。

## 受信疎通だけ先に確認したいとき（診断）

実装の前に「データが届いているか」だけを確認したい場合は、`IFacialMocapReceiverDemoBootstrap` を使います。

1. 空の GameObject に `IFacialMocapReceiverDemoBootstrap` をアタッチします。
2. Inspector で listen ポート（既定 `49983`）を設定します。端末がストリーム未開始なら `Send Handshake` を
   有効化し、`Device Address` に iOS 端末の IP を入力します。
3. Play すると、受信フレーム数・BlendShape 件数・head/eye の有無が 1 秒ごとに Console に出力されます。

> 診断用 Bootstrap は自前で listen ソケットを開くため、実装 Scene（binding が同じポートを開く）と**同居させない**でください。

## トラブルシューティング

- **BlendShape が動かない**: `FacialController` の `Skinned Mesh Renderers` にモデルのメッシュが割り当たっているか、
  `Mappings`（または ARKit 正準名）がモデルの BlendShape 名と一致しているか確認します。
- **ウィンドウ非フォーカス時に止まる**: Scene の `IFacialMocapDemoRunInBackground` が有効か確認します
  （`Application.runInBackground` を有効化します）。
- **受信が来ない**: PC のファイアウォールで UDP `49983` が許可されているか、端末アプリの送信先 IP/ポートが
  PC を指しているか確認します。
