# npmjs.com 公開手順

## 概要

FacialControl の 9 パッケージを npmjs.com に公開するための手順書。すべてのパッケージは `com.hidano` スコープの scoped registry（`https://registry.npmjs.org`）経由で Unity Package Manager から解決される。

| パッケージ | 依存する com.hidano.* |
|---|---|
| `com.hidano.facialcontrol` | なし（VContainer は OpenUPM から手動追加） |
| `com.hidano.facialcontrol.expression-creator` | core, `com.hidano.scene-view-style-camera-controller` |
| `com.hidano.facialcontrol.routing-editor` | core |
| `com.hidano.facialcontrol.osc` | core, `com.hidano.uosc` |
| `com.hidano.facialcontrol.rec` | core |
| `com.hidano.facialcontrol.inputsystem` | core |
| `com.hidano.facialcontrol.lipsync` | core, `com.hidano.ulipsync-asio` |
| `com.hidano.facialcontrol.timeline` | core, rec |
| `com.hidano.facialcontrol.ifacialmocap` | core, osc |

## 前提条件

1. npmjs.com アカウントを作成済みで、`npm login` で認証済みであること
2. GitHub リポジトリ `NHidano/FacialControl` が公開されていること（package.json の `repository` / `documentationUrl` / `changelogUrl` / `licensesUrl` が GitHub の `main` を指すため）
3. 外部依存が npmjs.com に公開済みであること
   - `com.hidano.uosc` 1.0.0
   - `com.hidano.scene-view-style-camera-controller` 1.0.0
   - `com.hidano.ulipsync-asio` 3.1.5-custom.2
4. 開発プロジェクトで CI（EditMode / PlayMode）が通過していること
5. 9 パッケージの `package.json` の `version` と、相互依存の `dependencies` に書かれたバージョンが一致していること

## リリース前チェックリスト

- [ ] 9 パッケージの `package.json` の `version` が同じリリース番号になっている
- [ ] 各パッケージの `dependencies` 内の `com.hidano.facialcontrol*` が同じリリース番号を指している
- [ ] 各パッケージに `README.md` / `CHANGELOG.md` / `LICENSE.md`（と `.meta`）が揃っている
- [ ] `CHANGELOG.md` にリリース番号と日付の見出しがある
- [ ] `Tests/EditMode/Editor/Inspector/SampleAssetsAreInSyncTests.cs` の `Assets/Samples/FacialControl InputSystem/<version>/...` パスがリリース番号と一致している
- [ ] Git タグ `v<version>`（例: `v1.0.0`）を作成し push している
- [ ] 各パッケージで `npm pack --dry-run` を実行し、`Samples~` / `Documentation~` / `Tests` が含まれ、余計なファイルが含まれていないことを確認している

## 公開順序

依存される側から順に公開する。同じ順序で `npm publish` を実行すれば、Package Manager の依存解決が途中で失敗しない。

```bash
cd FacialControl/Packages

# 1. core
(cd com.hidano.facialcontrol && npm publish)

# 2. core のみに依存するもの
(cd com.hidano.facialcontrol.expression-creator && npm publish)
(cd com.hidano.facialcontrol.routing-editor && npm publish)
(cd com.hidano.facialcontrol.osc && npm publish)
(cd com.hidano.facialcontrol.rec && npm publish)
(cd com.hidano.facialcontrol.inputsystem && npm publish)
(cd com.hidano.facialcontrol.lipsync && npm publish)

# 3. サブパッケージに依存するもの
(cd com.hidano.facialcontrol.timeline && npm publish)      # rec に依存
(cd com.hidano.facialcontrol.ifacialmocap && npm publish)  # osc に依存
```

公開後、`https://www.npmjs.com/package/<パッケージ名>` で各パッケージの README とバージョンを確認する。

## バージョンアップ時の手順

1. 9 パッケージの `package.json` の `version` を上げる
2. 依存側の `dependencies` に書かれた `com.hidano.facialcontrol*` のバージョンを合わせる
3. `CHANGELOG.md` に新しい見出しを追加する
4. `SampleAssetsAreInSyncTests.cs` のサンプルパスを更新する
5. 開発プロジェクトの `Packages/packages-lock.json` を Unity で再生成し、コミットする
6. タグを打って上記の順序で公開する

## ユーザー側のインストール方法

`Packages/manifest.json` に scoped registry を追加し、必要なパッケージだけを `dependencies` に列挙する。core は VContainer（OpenUPM 配布）に依存するため、OpenUPM の scoped registry も併せて追加する。

```json
{
    "scopedRegistries": [
        {
            "name": "npmjs",
            "url": "https://registry.npmjs.org",
            "scopes": [
                "com.hidano"
            ]
        },
        {
            "name": "OpenUPM",
            "url": "https://package.openupm.com",
            "scopes": [
                "jp.hadashikick.vcontainer"
            ]
        }
    ],
    "dependencies": {
        "jp.hadashikick.vcontainer": "1.17.0",
        "com.hidano.facialcontrol": "1.0.0",
        "com.hidano.facialcontrol.inputsystem": "1.0.0",
        "com.hidano.facialcontrol.osc": "1.0.0"
    }
}
```

`com.hidano` スコープにより、`com.hidano.uosc` / `com.hidano.scene-view-style-camera-controller` / `com.hidano.ulipsync-asio` も自動的に npm レジストリから解決される。
