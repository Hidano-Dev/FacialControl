# FacialControl パッケージバリデーションスクリプト
# P16-02: package.json / asmdef 依存方向 / .meta ファイル整合性チェック

param(
    [string]$PackagePath = "FacialControl/Packages/com.hidano.facialcontrol"
)

$ErrorActionPreference = "Stop"
$script:errors = @()
$script:warnings = @()

function Add-ValidationError {
    param([string]$Message)
    $script:errors += $Message
    Write-Host "  [ERROR] $Message" -ForegroundColor Red
}

function Add-ValidationWarning {
    param([string]$Message)
    $script:warnings += $Message
    Write-Host "  [WARN]  $Message" -ForegroundColor Yellow
}

function Write-Section {
    param([string]$Title)
    Write-Host ""
    Write-Host "=== $Title ===" -ForegroundColor Cyan
}

# ============================================================
# 1. package.json バリデーション
# ============================================================
function Test-PackageJson {
    Write-Section "package.json バリデーション"

    $packageJsonPath = Join-Path $PackagePath "package.json"

    if (-Not (Test-Path $packageJsonPath)) {
        Add-ValidationError "package.json が見つかりません: $packageJsonPath"
        return
    }

    try {
        $pkg = Get-Content $packageJsonPath -Raw | ConvertFrom-Json
    }
    catch {
        Add-ValidationError "package.json の JSON パースに失敗: $_"
        return
    }

    # 必須フィールドの存在チェック
    $requiredFields = @("name", "version", "displayName", "description", "unity")
    foreach ($field in $requiredFields) {
        if (-Not $pkg.PSObject.Properties[$field]) {
            Add-ValidationError "package.json に必須フィールド '$field' がありません"
        }
        elseif ([string]::IsNullOrWhiteSpace($pkg.$field)) {
            Add-ValidationError "package.json の '$field' が空です"
        }
    }

    # パッケージ名の形式チェック（com.xxx.xxx）
    if ($pkg.name -and $pkg.name -notmatch '^com\.[a-z][a-z0-9]*(\.[a-z][a-z0-9]*)+$') {
        Add-ValidationError "package.json の name '$($pkg.name)' が UPM 命名規則に違反しています (com.xxx.xxx 形式)"
    }

    # 期待するパッケージ名
    if ($pkg.name -and $pkg.name -ne "com.hidano.facialcontrol") {
        Add-ValidationError "package.json の name が 'com.hidano.facialcontrol' ではありません: '$($pkg.name)'"
    }

    # バージョン形式チェック（SemVer）
    if ($pkg.version -and $pkg.version -notmatch '^\d+\.\d+\.\d+(-[a-zA-Z0-9.]+)?$') {
        Add-ValidationError "package.json の version '$($pkg.version)' が SemVer 形式ではありません"
    }

    # Unity 最小バージョンチェック
    if ($pkg.unity -and $pkg.unity -notmatch '^\d{4}\.\d+$') {
        Add-ValidationError "package.json の unity '$($pkg.unity)' が正しい形式ではありません (例: 6000.3)"
    }

    # ライセンスチェック
    if (-Not $pkg.PSObject.Properties["license"]) {
        Add-ValidationWarning "package.json に license フィールドがありません"
    }

    # author チェック
    if (-Not $pkg.PSObject.Properties["author"]) {
        Add-ValidationWarning "package.json に author フィールドがありません"
    }

    # dependencies チェック
    if ($pkg.PSObject.Properties["dependencies"]) {
        $deps = $pkg.dependencies
        # InputSystem 依存の確認
        if (-Not $deps.PSObject.Properties["com.unity.inputsystem"]) {
            Add-ValidationWarning "package.json に com.unity.inputsystem 依存がありません"
        }
        # uOsc 依存の確認
        if (-Not $deps.PSObject.Properties["com.hidano.uosc"]) {
            Add-ValidationWarning "package.json に com.hidano.uosc 依存がありません"
        }
    }
    else {
        Add-ValidationWarning "package.json に dependencies フィールドがありません"
    }

    Write-Host "  [OK] package.json の基本構造は有効です" -ForegroundColor Green
}

# ============================================================
# 2. asmdef 依存方向バリデーション
# ============================================================
function Test-AsmdefDependencies {
    Write-Section "asmdef 依存方向バリデーション"

    $asmdefFiles = Get-ChildItem -Path $PackagePath -Filter "*.asmdef" -Recurse

    if ($asmdefFiles.Count -eq 0) {
        Add-ValidationError "asmdef ファイルが見つかりません"
        return
    }

    Write-Host "  検出された asmdef: $($asmdefFiles.Count) 個"

    # 期待する asmdef 名リスト
    $expectedAsmdefs = @(
        "Hidano.FacialControl.Domain",
        "Hidano.FacialControl.Application",
        "Hidano.FacialControl.Adapters",
        "Hidano.FacialControl.Editor",
        "Hidano.FacialControl.Tests.EditMode",
        "Hidano.FacialControl.Tests.PlayMode"
    )

    $asmdefMap = @{}
    foreach ($file in $asmdefFiles) {
        try {
            $asmdef = Get-Content $file.FullName -Raw | ConvertFrom-Json
            $asmdefMap[$asmdef.name] = @{
                Path       = $file.FullName
                References = @($asmdef.references)
                Data       = $asmdef
            }
        }
        catch {
            Add-ValidationError "asmdef パースエラー: $($file.FullName) - $_"
        }
    }

    # 全 asmdef が存在するか確認
    foreach ($expected in $expectedAsmdefs) {
        if (-Not $asmdefMap.ContainsKey($expected)) {
            Add-ValidationError "期待される asmdef '$expected' が見つかりません"
        }
    }

    # レイヤー定義（上位が下位に依存してはいけない）
    $layerOrder = @{
        "Hidano.FacialControl.Domain"      = 0
        "Hidano.FacialControl.Application"  = 1
        "Hidano.FacialControl.Adapters"     = 2
        "Hidano.FacialControl.Editor"       = 3
    }

    # Domain 層の依存チェック: 他のプロジェクト内 asmdef を参照していないこと
    if ($asmdefMap.ContainsKey("Hidano.FacialControl.Domain")) {
        $domainRefs = $asmdefMap["Hidano.FacialControl.Domain"].References
        $projectRefs = $domainRefs | Where-Object { $_ -match "^Hidano\.FacialControl\." }
        if ($projectRefs.Count -gt 0) {
            Add-ValidationError "Domain 層が他のプロジェクト内 asmdef を参照しています: $($projectRefs -join ', ')"
        }
        Write-Host "  [OK] Domain 層: 他レイヤーへの依存なし" -ForegroundColor Green
    }

    # Application 層の依存チェック: Domain のみ参照可
    if ($asmdefMap.ContainsKey("Hidano.FacialControl.Application")) {
        $appRefs = $asmdefMap["Hidano.FacialControl.Application"].References
        $projectRefs = $appRefs | Where-Object { $_ -match "^Hidano\.FacialControl\." }
        $invalidRefs = $projectRefs | Where-Object {
            $_ -ne "Hidano.FacialControl.Domain"
        }
        if ($invalidRefs.Count -gt 0) {
            Add-ValidationError "Application 層が不正なレイヤーを参照しています: $($invalidRefs -join ', ')"
        }
        if ($projectRefs -notcontains "Hidano.FacialControl.Domain") {
            Add-ValidationWarning "Application 層が Domain を参照していません"
        }
        Write-Host "  [OK] Application 層: Domain のみ参照" -ForegroundColor Green
    }

    # Adapters 層の依存チェック: Domain, Application のみ参照可（Editor は不可）
    if ($asmdefMap.ContainsKey("Hidano.FacialControl.Adapters")) {
        $adapterRefs = $asmdefMap["Hidano.FacialControl.Adapters"].References
        $projectRefs = $adapterRefs | Where-Object { $_ -match "^Hidano\.FacialControl\." }
        $invalidRefs = $projectRefs | Where-Object {
            $_ -ne "Hidano.FacialControl.Domain" -and $_ -ne "Hidano.FacialControl.Application"
        }
        if ($invalidRefs.Count -gt 0) {
            Add-ValidationError "Adapters 層が不正なレイヤーを参照しています: $($invalidRefs -join ', ')"
        }
        Write-Host "  [OK] Adapters 層: Domain + Application のみ参照" -ForegroundColor Green
    }

    # Editor 層の依存チェック: Domain, Application, Adapters のみ参照可
    if ($asmdefMap.ContainsKey("Hidano.FacialControl.Editor")) {
        $editorRefs = $asmdefMap["Hidano.FacialControl.Editor"].References
        $projectRefs = $editorRefs | Where-Object { $_ -match "^Hidano\.FacialControl\." }
        $invalidRefs = $projectRefs | Where-Object {
            $_ -ne "Hidano.FacialControl.Domain" -and
            $_ -ne "Hidano.FacialControl.Application" -and
            $_ -ne "Hidano.FacialControl.Adapters"
        }
        if ($invalidRefs.Count -gt 0) {
            Add-ValidationError "Editor 層が不正なレイヤーを参照しています: $($invalidRefs -join ', ')"
        }

        # Editor 層は includePlatforms に "Editor" が必要
        $editorData = $asmdefMap["Hidano.FacialControl.Editor"].Data
        if (-Not ($editorData.includePlatforms -contains "Editor")) {
            Add-ValidationError "Editor asmdef の includePlatforms に 'Editor' がありません"
        }
        Write-Host "  [OK] Editor 層: 正しい依存方向 + Editor プラットフォーム制約" -ForegroundColor Green
    }

    # テスト asmdef のチェック
    foreach ($testAsmdef in @("Hidano.FacialControl.Tests.EditMode", "Hidano.FacialControl.Tests.PlayMode")) {
        if ($asmdefMap.ContainsKey($testAsmdef)) {
            $testData = $asmdefMap[$testAsmdef].Data

            # overrideReferences が true であること
            if ($testData.overrideReferences -ne $true) {
                Add-ValidationError "${testAsmdef}: overrideReferences が true ではありません"
            }

            # UNITY_INCLUDE_TESTS の defineConstraints
            if ($testData.defineConstraints -notcontains "UNITY_INCLUDE_TESTS") {
                Add-ValidationError "${testAsmdef}: defineConstraints に 'UNITY_INCLUDE_TESTS' がありません"
            }

            # nunit.framework.dll の参照
            if ($testData.precompiledReferences -notcontains "nunit.framework.dll") {
                Add-ValidationError "${testAsmdef}: precompiledReferences に 'nunit.framework.dll' がありません"
            }

            # UnityEngine.TestRunner / UnityEditor.TestRunner の参照
            $refs = $asmdefMap[$testAsmdef].References
            if ($refs -notcontains "UnityEngine.TestRunner") {
                Add-ValidationError "${testAsmdef}: UnityEngine.TestRunner への参照がありません"
            }
            if ($refs -notcontains "UnityEditor.TestRunner") {
                Add-ValidationError "${testAsmdef}: UnityEditor.TestRunner への参照がありません"
            }

            Write-Host "  [OK] $testAsmdef`: テスト設定が正しい" -ForegroundColor Green
        }
    }

    # EditMode テストは includePlatforms に "Editor" が必要
    if ($asmdefMap.ContainsKey("Hidano.FacialControl.Tests.EditMode")) {
        $editModeData = $asmdefMap["Hidano.FacialControl.Tests.EditMode"].Data
        if (-Not ($editModeData.includePlatforms -contains "Editor")) {
            Add-ValidationError "EditMode テスト asmdef の includePlatforms に 'Editor' がありません"
        }
    }
}

# ============================================================
# 3. .meta ファイル整合性バリデーション
# ============================================================
function Test-MetaFileIntegrity {
    Write-Section ".meta ファイル整合性バリデーション"

    # パッケージ内の全ファイル/ディレクトリを取得（.meta を持たないものと隠しフォルダを除外）
    $allItems = Get-ChildItem -Path $PackagePath -Recurse -Force | Where-Object {
        $relativePath = $_.FullName.Substring($PackagePath.Length)
        # Documentation~ など末尾 ~ のフォルダは Unity が import せず .meta を持たないので中身ごと除外する。
        # ただし Samples~ は、import 時に GUID を保つため中身（サンプルのアセット）に .meta を同梱しているので、
        # Samples~ 自身とサンプルのルートフォルダ（直下のフォルダ）だけを除外し、中身は検査対象に残す
        $relativePath -notmatch '[\\/](?!Samples~)[^\\/]*~([\\/]|$)' -and
        $relativePath -notmatch '[\\/]Samples~$' -and
        -not ($_.PSIsContainer -and $relativePath -match '[\\/]Samples~[\\/][^\\/]+$') -and
        # .meta ファイル自体を除外
        $_.Extension -ne ".meta" -and
        # 隠しフォルダ（.で始まる）を除外
        $relativePath -notmatch '[\\/]\.'
    }

    $missingMeta = @()
    $orphanMeta = @()

    # 各ファイル/ディレクトリに対応する .meta ファイルが存在するか確認
    foreach ($item in $allItems) {
        $metaPath = "$($item.FullName).meta"
        if (-Not (Test-Path $metaPath)) {
            $relativePath = $item.FullName.Substring((Resolve-Path $PackagePath).Path.Length + 1)
            $missingMeta += $relativePath
        }
    }

    # 孤立した .meta ファイルの検出（対応するファイル/ディレクトリが存在しない）
    # 孤立 .meta と GUID 重複は Samples~ の中身も含めて検査する（Documentation~ 等は除外）
    $allMetaFiles = Get-ChildItem -Path $PackagePath -Filter "*.meta" -Recurse -Force | Where-Object {
        $relativePath = $_.FullName.Substring($PackagePath.Length)
        $relativePath -notmatch '[\\/](?!Samples~)[^\\/]*~([\\/]|$)'
    }

    foreach ($metaFile in $allMetaFiles) {
        $targetPath = $metaFile.FullName -replace '\.meta$', ''
        if (-Not (Test-Path $targetPath)) {
            $relativePath = $metaFile.FullName.Substring((Resolve-Path $PackagePath).Path.Length + 1)
            $orphanMeta += $relativePath
        }
    }

    if ($missingMeta.Count -gt 0) {
        Add-ValidationError ".meta ファイルが不足しています ($($missingMeta.Count) 件):"
        foreach ($path in $missingMeta) {
            Write-Host "    - $path" -ForegroundColor Red
        }
    }
    else {
        Write-Host "  [OK] 全てのアセットに .meta ファイルが存在します" -ForegroundColor Green
    }

    if ($orphanMeta.Count -gt 0) {
        Add-ValidationWarning "孤立した .meta ファイルがあります ($($orphanMeta.Count) 件):"
        foreach ($path in $orphanMeta) {
            Write-Host "    - $path" -ForegroundColor Yellow
        }
    }
    else {
        Write-Host "  [OK] 孤立した .meta ファイルはありません" -ForegroundColor Green
    }

    # .meta ファイル内の guid 重複チェック
    Write-Host ""
    Write-Host "  guid 重複チェック..."
    $guidMap = @{}
    foreach ($metaFile in $allMetaFiles) {
        $content = Get-Content $metaFile.FullName -Raw
        if ($content -match 'guid:\s*([a-f0-9]{32})') {
            $guid = $Matches[1]
            if ($guidMap.ContainsKey($guid)) {
                $relativePath1 = $guidMap[$guid].Substring((Resolve-Path $PackagePath).Path.Length + 1)
                $relativePath2 = $metaFile.FullName.Substring((Resolve-Path $PackagePath).Path.Length + 1)
                Add-ValidationError "guid が重複しています: $guid"
                Write-Host "    - $relativePath1" -ForegroundColor Red
                Write-Host "    - $relativePath2" -ForegroundColor Red
            }
            else {
                $guidMap[$guid] = $metaFile.FullName
            }
        }
    }

    if ($guidMap.Count -gt 0) {
        $duplicateCount = ($guidMap.Values | Group-Object | Where-Object { $_.Count -gt 1 }).Count
        if ($duplicateCount -eq 0) {
            Write-Host "  [OK] guid の重複はありません ($($guidMap.Count) 件チェック済み)" -ForegroundColor Green
        }
    }
}

# ============================================================
# メイン実行
# ============================================================
Write-Host "FacialControl パッケージバリデーション" -ForegroundColor Cyan
Write-Host "パッケージパス: $PackagePath"
Write-Host ""

# パッケージディレクトリの存在確認
if (-Not (Test-Path $PackagePath)) {
    Write-Host "[FATAL] パッケージディレクトリが見つかりません: $PackagePath" -ForegroundColor Red
    exit 1
}

# パスを解決
$PackagePath = (Resolve-Path $PackagePath).Path

Test-PackageJson
Test-AsmdefDependencies
Test-MetaFileIntegrity

# ============================================================
# 結果サマリ
# ============================================================
Write-Section "バリデーション結果"

if ($script:warnings.Count -gt 0) {
    Write-Host "  警告: $($script:warnings.Count) 件" -ForegroundColor Yellow
}

if ($script:errors.Count -gt 0) {
    Write-Host "  エラー: $($script:errors.Count) 件" -ForegroundColor Red
    Write-Host ""
    Write-Host "バリデーション失敗" -ForegroundColor Red
    exit 1
}
else {
    Write-Host "  エラー: 0 件" -ForegroundColor Green
    Write-Host ""
    Write-Host "バリデーション成功" -ForegroundColor Green
    exit 0
}
