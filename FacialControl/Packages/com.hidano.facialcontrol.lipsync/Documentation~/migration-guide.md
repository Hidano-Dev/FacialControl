# uLipSyncBlendShape 手結線からの移行ガイド

このガイドは、Character Prefab に `uLipSync.uLipSync`、`uLipSyncBlendShape`、`AudioSource`、`uLipSyncMicrophone` などを直接付けていた構成から、`ULipSyncAdapterBinding` を使う構成へ移行するための手順です。

`ULipSyncAdapterBinding` は再生時に必要な uLipSync 系コンポーネントを Host GameObject へ動的に追加します。移行後の Character Prefab には、uLipSync 系コンポーネントを事前に残さないでください。

## 1. 旧コンポーネントを Prefab から取り外す

移行対象の Character Prefab または Scene 上の Character GameObject を開き、旧手結線で追加していた uLipSync 系コンポーネントを削除します。

削除対象は、主に `uLipSync.uLipSync`、`uLipSyncBlendShape`、`uLipSyncMicrophone`、`uLipSyncAsioInput`、リップシンク専用に追加していた `AudioSource` です。ほかの用途で使っている `AudioSource` まで削除しないように、接続先と参照元を確認してから外してください。

この時点で、Character Prefab は FacialControl の通常コンポーネントとモデル構成だけを持つ状態にします。uLipSync の解析器や入力コンポーネントは、以降 `ULipSyncAdapterBinding` が再生時に追加します。

## 2. Profile に slot と ULipSyncAdapterBinding を追加する

対象キャラクターが参照している `FacialCharacterProfileSO` を Inspector で開き、表情ライブラリタブの **Phoneme slots を初期化 (a/i/u/e/o)** で slot を宣言してから、Adapter Bindings に `uLipSync` binding を追加します。`overlay` レイヤーは自動追加されます。

入力デバイスは Inspector のポップアップで選びます。値は PlayerPrefs に保存され、Profile には残りません。空のままにすると先頭のマイクが使われます。ASIO ドライバ名に一致する場合は ASIO 入力として解決され、一致しない場合は `UnityEngine.Microphone.devices` の通常マイクとして解決されます。同名デバイスが複数ある環境では、`Disambiguator Index` に 0 始まりの列挙順インデックスを指定します。

Analyzer Profile は任意です。未指定の場合は、パッケージ同梱の既定 uLipSync Profile が `Resources` 経由で使用されます。

## 3. 音素 entry を Inspector で配線する

旧 `uLipSyncBlendShape` で設定していた A / I / U / E / O などの音素マップを、`ULipSyncAdapterBinding` の phoneme entry リストへ移します。新規追加時は A〜O の Expression 形式 entry が 5 件プリセットされるので、Profile に口形 Expression があればそれを割り当てるのが最も確実です。

BlendShape を直接動かしていた項目は、BlendShape 形式 entry に切り替えて `Phoneme Id`、`BlendShape Name`、`Max Weight` を入力します。`BlendShape Name` は対象 `SkinnedMeshRenderer` 上の BlendShape 名と完全一致させてください。見つからない名前は起動時に警告され、その entry はスキップされます。

複数の BlendShape を 1 音素でまとめて動かしたい場合は、AnimationClip 形式 entry を使えます。この場合、Clip の終端時刻（`length` が 0 なら先頭）の BlendShape weight だけが初期化時に読み取られます。Clip の時間軸再生、途中キー、イージング、ループは使用されません。

## 4. Scene を再生して動作確認する

Scene を Play し、Console に未解決デバイス、Analyzer Profile 読み込み失敗、未解決 BlendShape 名のエラーや警告が出ていないことを確認します。

Hierarchy または Inspector で Host GameObject を確認し、再生中に `AudioSource`、`uLipSync.uLipSync`、`uLipSyncMicrophone` または `uLipSyncAsioInput` が動的に追加されることを確認します。再生を停止すると、`ULipSyncAdapterBinding` が追加した uLipSync 系コンポーネントは取り外されます。

口形状が旧構成と異なる場合は、phoneme entry の `Max Weight`、BlendShape 名、Analyzer Profile、入力デバイス名を順に確認してください。名前を指定したデバイスが見つからない場合、本パッケージは別のデバイスへ自動フォールバックしません（フォールバックはデバイス名が空のときだけ）。正しいデバイス名を選び直してから再度 Play してください。
