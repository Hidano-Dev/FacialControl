# FacialControl テストサイズ静的チェック
#
# Unity を起動せずに、テストコードのサイズ宣言（Small / Medium / Large）を検査する。
# CI では Unity のテストジョブより前に実行し、以下を機械的に保証する。
#
#   1. テストメソッド（[Test] / [UnityTest] / [TestCase] / [TestCaseSource] / [Theory]）を含む
#      すべての fixture クラスが、クラスまたは全テストメソッドにサイズ属性を 1 つ持つこと
#   2. Small を宣言したファイル（および Tests/Small/ 配下）が Small で禁止された API を使っていないこと
#   3. Small 用アセンブリ（*.Tests.Small.asmdef）の参照が許可リスト内で、EditMode（Editor のみ）であること
#   4. Tests/Small/ 配下に Medium / Large の宣言がないこと
#
# 実行時の最終確認は Unity 上の TestSizeDeclarationTests（reflection）が担う。
# このスクリプトは正規表現ベースの近似であり、コメント・文字列は除去してから判定する。
#
# 使い方:
#   pwsh ./scripts/check-test-sizes.ps1 [-PackagesPath FacialControl/Packages]

param(
    [string]$PackagesPath = "FacialControl/Packages"
)

$ErrorActionPreference = "Stop"
$script:errors = @()

function Add-CheckError {
    param([string]$Message)
    $script:errors += $Message
    Write-Host "  [ERROR] $Message" -ForegroundColor Red
}

function Write-Section {
    param([string]$Title)
    Write-Host ""
    Write-Host "=== $Title ===" -ForegroundColor Cyan
}

# コメントと文字列を同じ長さの空白に置き換える（行番号・位置を保つ）
function Remove-CommentsAndStrings {
    param([string]$Source)
    $sb = New-Object System.Text.StringBuilder($Source.Length)
    $i = 0
    $n = $Source.Length
    while ($i -lt $n) {
        $c = $Source[$i]
        if ($c -eq '/' -and $i + 1 -lt $n -and $Source[$i + 1] -eq '/') {
            while ($i -lt $n -and $Source[$i] -ne "`n") { [void]$sb.Append(' '); $i++ }
        }
        elseif ($c -eq '/' -and $i + 1 -lt $n -and $Source[$i + 1] -eq '*') {
            $end = $Source.IndexOf('*/', $i + 2)
            if ($end -lt 0) { $end = $n } else { $end += 2 }
            while ($i -lt $end) { if ($Source[$i] -eq "`n") { [void]$sb.Append("`n") } else { [void]$sb.Append(' ') }; $i++ }
        }
        elseif ($c -eq '@' -and $i + 1 -lt $n -and $Source[$i + 1] -eq '"') {
            [void]$sb.Append('  '); $i += 2
            while ($i -lt $n) {
                if ($Source[$i] -eq '"') {
                    if ($i + 1 -lt $n -and $Source[$i + 1] -eq '"') { [void]$sb.Append('  '); $i += 2; continue }
                    break
                }
                if ($Source[$i] -eq "`n") { [void]$sb.Append("`n") } else { [void]$sb.Append(' ') }
                $i++
            }
            if ($i -lt $n) { [void]$sb.Append(' '); $i++ }
        }
        elseif ($c -eq '"' -or $c -eq "'") {
            $q = $c
            [void]$sb.Append(' '); $i++
            while ($i -lt $n -and $Source[$i] -ne $q -and $Source[$i] -ne "`n") {
                if ($Source[$i] -eq '\') { [void]$sb.Append(' '); $i++ }
                if ($i -lt $n) { [void]$sb.Append(' '); $i++ }
            }
            if ($i -lt $n) { [void]$sb.Append(' '); $i++ }
        }
        else {
            [void]$sb.Append($c); $i++
        }
    }
    return $sb.ToString()
}

function Get-LineNumber {
    param([string]$Text, [int]$Index)
    $count = 1
    for ($k = 0; $k -lt $Index -and $k -lt $Text.Length; $k++) { if ($Text[$k] -eq "`n") { $count++ } }
    return $count
}

$sizeAttrRegex = [regex]'\[\s*(SmallTest|MediumTest|LargeTest)\b|\[\s*Category\s*\(\s*(?:TestSizes\.)?(?:"(Small|Medium|Large)"|(Small|Medium|Large)Category)\s*\)'
$testAttrRegex = [regex]'\[\s*(?:NUnit\.Framework\.)?(Test|UnityTest|TestCase|TestCaseSource|Theory)\b'
$classRegex = [regex]'(?m)^(?<indent>[ \t]*)(?<mods>(?:(?:public|internal|private|protected|static|sealed|abstract|partial|new)\s+)*)class\s+(?<name>\w+)(?<generic>\s*<[^>\n]*>)?(?<basepart>\s*:\s*(?<bases>[^{\n]+?))?(?<where>\s+where[^{\n]*)?\s*(?<brace>\{)?\s*$'

# Small で禁止する API（docs/testing.md の定義と同期させる）
$smallBannedPatterns = [ordered]@{
    "AssetDatabase / Resources"      = '\bAssetDatabase\b|\bPrefabUtility\b|\bResources\.\w*Load'
    "ファイル I/O"                    = '\bFile\.\w+\(|\bDirectory\.\w+\(|Path\.GetTempPath|Application\.(persistentDataPath|dataPath|temporaryCachePath|streamingAssetsPath)|\bFileStream\b|\bStreamWriter\b|\bStreamReader\b'
    "PlayerPrefs / EditorPrefs"      = '(?<![\w.])PlayerPrefs\.|\bEditorPrefs\.'
    "ネットワーク"                    = '\bUnityWebRequest\b|\bUdpClient\b|\bSocket\b|\bTcpClient\b|\buOSC\.|\buOscClient\b|\buOscServer\b|\bDns\.\w+'
    "シーンロード"                    = '\bSceneManager\b|\bEditorSceneManager\b|\bLoadScene\b'
    "フレーム待ち"                    = '\bWaitForSeconds\b|\bWaitForSecondsRealtime\b|\bWaitForFixedUpdate\b|\bWaitForEndOfFrame\b|\bWaitUntil\b|\bWaitWhile\b'
    "UnityTest（コルーチン）"         = '\[\s*UnityTest\s*\]'
    "Time.* 直接参照"                 = '\bTime\.(time|deltaTime|unscaledTime|realtimeSinceStartup|frameCount|fixedTime|unscaledDeltaTime|timeAsDouble|unscaledTimeAsDouble|fixedDeltaTime|timeScale)\b'
    "実時間 / スリープ"               = 'DateTime\.(Now|UtcNow)|\bStopwatch\b|Thread\.Sleep|Task\.Delay'
    "EditorWindow / EditorApplication" = '\bEditorWindow\b|GetWindow<|CreateWindow<|\bEditorApplication\.'
    "エンジングローバル状態"          = '\bPlayerLoop\b|\bInputSystem\.\w+|\bInputTestFixture\b|\bPhysics\.'
    "MonoBehaviour ライフサイクル"     = 'AddComponent<\s*(FacialController|OscSender|OscReceiver|OscReceiverHost|FacialTimelineReceiver|RecCharacterBinding|ExpressionInputSourceAdapter|IFacialMocapReceiverHost|uLipSync\.uLipSync|uOSC\.uOscClient|uOSC\.uOscServer|LifetimeScope|FacialControlULipSyncBlendShape)\s*>'
}

$smallAsmdefAllowedReferences = @(
    "Hidano.FacialControl.Domain",
    "Hidano.FacialControl.Application",
    "Hidano.FacialControl.Tests.Shared",
    "Hidano.FacialControl.Testing",
    "Unity.Collections",
    "UnityEngine.TestRunner",
    "UnityEditor.TestRunner"
)

if (-not (Test-Path $PackagesPath)) {
    Write-Host "Packages path not found: $PackagesPath" -ForegroundColor Red
    exit 1
}

$testFiles = Get-ChildItem -Path $PackagesPath -Recurse -Filter *.cs |
    Where-Object { $_.FullName -replace '\\', '/' -match '/Tests/' }

$sizeCounts = [ordered]@{ Small = 0; Medium = 0; Large = 0 }
$fixtureCount = 0
$testAttrCount = 0

Write-Section "1. サイズ宣言の検査"
foreach ($file in $testFiles) {
    $relPath = ($file.FullName -replace '\\', '/')
    $relPath = $relPath.Substring($relPath.IndexOf('/Packages/') + 10)
    $raw = Get-Content -Path $file.FullName -Raw
    if ($null -eq $raw) { continue }
    $src = $raw -replace "`r`n", "`n"
    $masked = Remove-CommentsAndStrings -Source $src

    $testMatches = $testAttrRegex.Matches($masked)
    if ($testMatches.Count -eq 0) { continue }

    # クラス範囲
    $classes = @()
    foreach ($m in $classRegex.Matches($masked)) {
        $open = $masked.IndexOf('{', $m.Index + $m.Length - 1)
        if ($m.Groups['brace'].Success) { $open = $m.Groups['brace'].Index }
        if ($open -lt 0) { continue }
        $depth = 0; $j = $open; $close = -1
        while ($j -lt $masked.Length) {
            if ($masked[$j] -eq '{') { $depth++ }
            elseif ($masked[$j] -eq '}') { $depth--; if ($depth -eq 0) { $close = $j; break } }
            $j++
        }
        if ($close -lt 0) { continue }
        # クラス宣言直前の属性ブロック（属性行・空行・doc コメント行を上へ辿る）
        $lineStart = $m.Index
        $attrText = ""
        $cursor = $lineStart
        while ($cursor -gt 0) {
            $prevLineEnd = $cursor - 1
            $prevLineStart = $masked.LastIndexOf("`n", $prevLineEnd - 1)
            if ($prevLineStart -lt 0) { $prevLineStart = 0 } else { $prevLineStart++ }
            $line = $masked.Substring($prevLineStart, $prevLineEnd - $prevLineStart).Trim()
            if ($line -eq "" -or $line.StartsWith('[')) { $attrText = $line + "`n" + $attrText; $cursor = $prevLineStart; if ($prevLineStart -eq 0) { break } }
            else { break }
        }
        $classes += [pscustomobject]@{
            Name = $m.Groups['name'].Value; Open = $open; Close = $close; Index = $m.Index
            IsAbstract = ($m.Groups['mods'].Value -match '\babstract\b')
            ClassSizes = @($sizeAttrRegex.Matches($attrText) | ForEach-Object { if ($_.Groups[1].Success) { $_.Groups[1].Value -replace 'Test$', '' } elseif ($_.Groups[2].Success) { $_.Groups[2].Value } else { $_.Groups[3].Value } })
        }
    }

    $fixtures = @{}
    foreach ($t in $testMatches) {
        $testAttrCount++
        $containing = $classes | Where-Object { $_.Open -lt $t.Index -and $t.Index -lt $_.Close }
        if (-not $containing) {
            Add-CheckError "$relPath`:$(Get-LineNumber $masked $t.Index) テスト属性を含むクラスを特定できません"
            continue
        }
        $inner = $containing | Sort-Object Open -Descending | Select-Object -First 1
        if (-not $fixtures.ContainsKey($inner.Name)) { $fixtures[$inner.Name] = @{ Class = $inner; Tests = @() } }
        $fixtures[$inner.Name].Tests += $t
    }

    foreach ($entry in $fixtures.Values) {
        $cls = $entry.Class
        if ($cls.IsAbstract) { continue }  # 派生 fixture 側の宣言に委ねる（実行時は TestSizeDeclarationTests が検査）
        $fixtureCount++
        $classSizes = @($cls.ClassSizes | Select-Object -Unique)
        if ($classSizes.Count -gt 1) {
            Add-CheckError "$relPath`:$(Get-LineNumber $masked $cls.Index) クラス $($cls.Name) に複数のサイズが宣言されています: $($classSizes -join ', ')"
            continue
        }
        if ($classSizes.Count -eq 1) {
            # メソッド側に別サイズがあれば矛盾
            foreach ($t in $entry.Tests) {
                $blockStart = $masked.LastIndexOf("`n", $t.Index)
                $block = $masked.Substring($blockStart + 1, [Math]::Min(400, $masked.Length - $blockStart - 1))
                foreach ($sm in $sizeAttrRegex.Matches($block)) {
                    $name = if ($sm.Groups[1].Success) { $sm.Groups[1].Value -replace 'Test$', '' } elseif ($sm.Groups[2].Success) { $sm.Groups[2].Value } else { $sm.Groups[3].Value }
                    if ($name -ne $classSizes[0]) {
                        Add-CheckError "$relPath`:$(Get-LineNumber $masked $t.Index) メソッドのサイズ $name がクラスの $($classSizes[0]) と矛盾しています"
                    }
                }
            }
            $sizeCounts[$classSizes[0]]++
            continue
        }
        # クラスにサイズがない → 各テストメソッドの属性ブロック（テスト属性の前後 6 行）にサイズがあるか
        $lines = $masked -split "`n"
        foreach ($t in $entry.Tests) {
            $ln = Get-LineNumber $masked $t.Index
            $from = [Math]::Max(0, $ln - 7); $to = [Math]::Min($lines.Length - 1, $ln + 5)
            $window = ($lines[$from..$to] | Where-Object { $_.Trim().StartsWith('[') -or $_.Trim() -eq "" }) -join "`n"
            $found = @($sizeAttrRegex.Matches($window) | ForEach-Object { if ($_.Groups[1].Success) { $_.Groups[1].Value -replace 'Test$', '' } elseif ($_.Groups[2].Success) { $_.Groups[2].Value } else { $_.Groups[3].Value } } | Select-Object -Unique)
            if ($found.Count -eq 0) {
                Add-CheckError "$relPath`:$ln クラス $($cls.Name) のテストにサイズが宣言されていません（[SmallTest] / [MediumTest] / [LargeTest]）"
            }
            elseif ($found.Count -gt 1) {
                Add-CheckError "$relPath`:$ln 複数のサイズが宣言されています: $($found -join ', ')"
            }
            else { $sizeCounts[$found[0]]++ }
        }
    }
}
Write-Host "  fixture クラス: $fixtureCount / テスト属性: $testAttrCount"
Write-Host "  サイズ別（クラス単位、メソッド宣言は個別カウント）: Small=$($sizeCounts.Small) Medium=$($sizeCounts.Medium) Large=$($sizeCounts.Large)"

Write-Section "2. Small の禁止 API 検査"
$smallFileCount = 0
foreach ($file in $testFiles) {
    $relPath = ($file.FullName -replace '\\', '/')
    $relPath = $relPath.Substring($relPath.IndexOf('/Packages/') + 10)
    $raw = Get-Content -Path $file.FullName -Raw
    if ($null -eq $raw) { continue }
    $src = $raw -replace "`r`n", "`n"
    $masked = Remove-CommentsAndStrings -Source $src
    $inSmallDir = $relPath -match '/Tests/Small/'
    $declaresSmall = $masked -match '\[\s*SmallTest\b|\[\s*Category\s*\(\s*(?:"Small"|TestSizes\.SmallCategory)\s*\)'
    if (-not ($inSmallDir -or $declaresSmall)) { continue }
    $smallFileCount++
    if ($inSmallDir -and ($masked -match '\[\s*(MediumTest|LargeTest)\b')) {
        Add-CheckError "$relPath Tests/Small/ 配下に Medium / Large の宣言があります"
    }
    foreach ($key in $smallBannedPatterns.Keys) {
        $hit = [regex]::Match($masked, $smallBannedPatterns[$key])
        if ($hit.Success) {
            Add-CheckError "$relPath`:$(Get-LineNumber $masked $hit.Index) Small で禁止された API（$key）: '$($hit.Value.Trim())'。Medium に変更するか Fake へ置き換えてください"
        }
    }
}
Write-Host "  Small 宣言ファイル: $smallFileCount"

Write-Section "3. Small アセンブリ定義の検査"
$smallAsmdefs = Get-ChildItem -Path $PackagesPath -Recurse -Filter *.Tests.Small.asmdef
foreach ($asmdef in $smallAsmdefs) {
    $relPath = ($asmdef.FullName -replace '\\', '/')
    $relPath = $relPath.Substring($relPath.IndexOf('/Packages/') + 10)
    $json = Get-Content -Path $asmdef.FullName -Raw | ConvertFrom-Json
    foreach ($ref in $json.references) {
        if ($smallAsmdefAllowedReferences -notcontains $ref) {
            Add-CheckError "$relPath 許可されていない参照: $ref（Small アセンブリは Domain / Application / Tests.Shared / Testing のみ参照できます）"
        }
    }
    if (-not ($json.includePlatforms.Count -eq 1 -and $json.includePlatforms[0] -eq "Editor")) {
        Add-CheckError "$relPath includePlatforms は [""Editor""]（EditMode）でなければなりません"
    }
    if ($json.precompiledReferences -notcontains "nunit.framework.dll") {
        Add-CheckError "$relPath precompiledReferences に nunit.framework.dll がありません"
    }
    Write-Host "  $($json.name): references=$($json.references.Count)"
}
if ($smallAsmdefs.Count -eq 0) { Write-Host "  (Small アセンブリなし)" }

Write-Section "結果"
if ($script:errors.Count -gt 0) {
    Write-Host "  $($script:errors.Count) 件のエラー" -ForegroundColor Red
    exit 1
}
Write-Host "  OK" -ForegroundColor Green
exit 0
