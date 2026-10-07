# AGENTS.md

Project-specific guidance for coding agents (Codex etc.). Project conventions, architecture and file-management rules live in `CLAUDE.md`; follow them as well. Work items are tracked as Linear issues (team Hidano / project FacialControl).

## Unity Editor (IMPORTANT)
- Unity Editor executable for ALL batchmode / test commands on the Windows dev machine: `D:/UnityEditors/6000.3.19f1/Editor/Unity.exe`
- CI runs the same Test Runner commands on the Linux self-hosted runner (label `linux-unity`, Unity 6000.3.19f1 at `$UNITY_EDITORS_ROOT/6000.3.19f1/Editor/Unity`, Xvfb) through `.github/actions/run-unity-tests`; the action checks `ProjectVersion.txt` against the editor version before launching. Windows-only behaviour (path separators, Windows-only APIs) is not covered by CI and is still verified locally.
- Unity project path: `./FacialControl` (= `D:\Personal\Repositries\FacialControl\FacialControl`)
- Do NOT use any other version under `D:\UnityEditors` (e.g. 6000.3.10f1). Using a different version rewrites `ProjectSettings/ProjectVersion.txt` and triggers a full reimport. If `ProjectVersion.txt` does not say 6000.3.19f1, that is drift caused by a wrong editor — never "fix" the editor choice to match the file.

## Test Execution (IMPORTANT)
- The ONLY sanctioned way to run tests is Unity Test Runner in batchmode (locally on Windows, or in CI via `.github/actions/run-unity-tests` on the Linux runner):
  `& "D:/UnityEditors/6000.3.19f1/Editor/Unity.exe" -batchmode -nographics -projectPath <repo>/FacialControl -runTests -testPlatform EditMode|PlayMode [-testFilter <fullname>] -testResults <abs-path>.xml -logFile <abs-path>.log`
- Without a local Unity (e.g. the cloud worker), do not run tests by other means; push and use the PR's CI jobs (`ci.yml`) as the test evidence, reading `test-results/*.xml` / `*.log` from the run artifacts.
- Tests are classified by size (`[SmallTest]` / `[MediumTest]` / `[LargeTest]`, see `docs/testing.md`). Add `-testCategory Small` (or `Medium`) to run one size; run `pwsh ./scripts/check-test-sizes.ps1` before pushing to catch undeclared sizes without launching Unity.
- NEVER pass `-quit` together with `-runTests` — it makes Unity exit immediately without running tests (no XML is produced). The test runner exits by itself when the run finishes.
- NEVER load project/test DLLs (`Library/ScriptAssemblies/*.dll`) into PowerShell via `[System.Reflection.Assembly]::LoadFrom` + `Activator.CreateInstance` to invoke NUnit methods directly. This pattern is flagged by Windows Defender as a trojan (fileless-malware heuristic), gets blocked, and bypasses Unity Test Runner semantics (SetUp/TearDown, LogAssert, Unity APIs). If a test run seems to produce no XML, fix the command line (usually the `-quit` mistake) instead of switching to reflection.

