# Agentic SDLC and Spec-Driven Development

Kiro-style Spec-Driven Development on an agentic SDLC

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

## Project Memory
Project memory keeps persistent guidance (steering, specs notes, component docs) so Codex honors your standards each run. Treat it as the long-lived source of truth for patterns, conventions, and decisions.

- Use `.kiro/steering/` for project-wide policies: architecture principles, naming schemes, security constraints, tech stack decisions, api standards, etc.
- Use local `AGENTS.md` files for feature or library context (e.g. `src/lib/payments/AGENTS.md`): describe domain assumptions, API contracts, or testing conventions specific to that folder. Codex auto-loads these when working in the matching path.
- Specs notes stay with each spec (under `.kiro/specs/`) to guide specification-level workflows.

## Project Context

### Paths
- Steering: `.kiro/steering/`
- Specs: `.kiro/specs/`

### Steering vs Specification

**Steering** (`.kiro/steering/`) - Guide AI with project-wide rules and context
**Specs** (`.kiro/specs/`) - Formalize development process for individual features

### Active Specifications
- Check `.kiro/specs/` for active specifications
- Use `$kiro-spec-status [feature-name]` to check progress

## Development Guidelines
- Think in English, generate responses in Japanese. All Markdown content written to project files (e.g., requirements.md, design.md, tasks.md, research.md, validation reports) MUST be written in the target language configured for this specification (see spec.json.language).

## Minimal Workflow
- Phase 0 (optional): `$kiro-steering`, `$kiro-steering-custom`
- Discovery: `$kiro-discovery "idea"` — determines action path, writes brief.md + roadmap.md for multi-spec projects
- Phase 1 (Specification):
  - Single spec: `$kiro-spec-quick {feature} [--auto]` or step by step:
    - `$kiro-spec-init "description"`
    - `$kiro-spec-requirements {feature}`
    - `$kiro-validate-gap {feature}` (optional: for existing codebase)
    - `$kiro-spec-design {feature} [-y]`
    - `$kiro-validate-design {feature}` (optional: design review)
    - `$kiro-spec-tasks {feature} [-y]`
  - Multi-spec: `$kiro-spec-batch` — creates all specs from roadmap.md in parallel by dependency wave
- Phase 2 (Implementation): `$kiro-impl {feature} [tasks]`
  - Without task numbers: autonomous mode (subagent per task + independent review + final validation)
  - With task numbers: manual mode (selected tasks in main context, still reviewer-gated before completion)
  - `$kiro-validate-impl {feature}` (standalone re-validation)
- Progress check: `$kiro-spec-status {feature}` (use anytime)

## Skills Structure
Skills are located in `.agents/skills/kiro-*/SKILL.md`
- Each skill is a directory with a `SKILL.md` file
- Use `/skills` to inspect currently available skills
- Invoke a skill directly with `$kiro-<skill-name>`
- `kiro-review` — task-local adversarial review protocol used by reviewer subagents
- `kiro-debug` — root-cause-first debug protocol used by debugger subagents
- `kiro-verify-completion` — fresh-evidence gate before success or completion claims
- **If there is even a 1% chance a skill applies to the current task, invoke it.** Do not skip skills because the task seems simple.

## Collaboration Modes (Optional)
Enable collaboration modes in `~/.codex/config.toml` to let Codex choose focused execution modes for longer tasks:

```toml
[features]
collaboration_modes = true
```

## Multi-Agent (Experimental)
If multi-agent is available, use it to parallelize independent research and validation within skills. Enable in `~/.codex/config.toml`:

```toml
[features]
multi_agent = true
```

Skills with "Parallel Research" sections list independent work items that benefit from sub-agent spawning when this feature is active.

## Development Rules
- 3-phase approval workflow: Requirements → Design → Tasks → Implementation
- Human review required each phase; use `-y` only for intentional fast-track
- Keep steering current and verify alignment with `$kiro-spec-status`
- Follow the user's instructions precisely, and within that scope act autonomously: gather the necessary context and complete the requested work end-to-end in this run, asking questions only when essential information is missing or the instructions are critically ambiguous.

## Steering Configuration
- Load entire `.kiro/steering/` as project memory
- Default files: `product.md`, `tech.md`, `structure.md`
- Custom files are supported (managed via `$kiro-steering-custom`)
