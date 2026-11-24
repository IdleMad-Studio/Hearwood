# AGENTS.md

Commands, architecture and gotchas for this repo are in `CLAUDE.md`. Read it first.

## Delegating work (subagents)

The main agent orchestrates delegated work and stays responsible for integration, verification and the result. Two sub-agents are available through the `subagent` tool:

- `explore`: read-only codebase hunting, such as finding files by pattern, locating symbols and their callers, or answering "how does X work". State how thorough it should be (`quick`, `medium` or `very thorough`). In this repo, `explore` is the right choice for:
  - the Pocket engine and playback pipeline (`src/Hearwood/Pocket`, `Text`, `Audio`, `SpeechStream.cs`, `SpeechPlayer.cs`)
  - WinForms UI wiring (`TrayApp.cs`, `MainWindow.cs`)
- `general`: self-contained, multi-step work that can run alongside the main agent, such as bounded refactors across files or independent investigations.

Routing:

- Make small, well-defined, low-risk edits directly. Briefing and reviewing a delegation costs more than the edit.
- Send read-only context-gathering to `explore`, and independent work that can run in parallel to `general`. Run independent sub-agents at the same time. Use `background: true` only for work that is genuinely parallel. Never poll: you are notified when it finishes.
- Never delegate architecture or design decisions (what `CLAUDE.md`'s Architecture section records), changes to the pinned model or native-library versions, or broad integration. The main agent owns those, plus the final review of the diff.

Briefs and model choice:

- Child sessions start with **fresh context**, so the brief must stand on its own: the goal, the relevant paths, the constraints and the acceptance criteria. Ask for a report of changed files, checks run and unresolved issues.
- Pick the sub-agent `model` by how complex the task is, using Claude models only:
  - `haiku`: quick, mechanical, read-only work, such as locating files, symbols or callers, listing call sites, or `explore` at `quick` thoroughness.
  - `sonnet`: the default for most delegated work, such as `explore` at `medium` or `very thorough`, bounded refactors, writing tests, and independent investigations with a clear brief.
  - `opus`: hard delegated work only. Examples are subtle multi-file changes, debugging threading or the audio pipeline (Sonic stretching, word-position tracking, native ptts calls), or anything where a wrong result is costly to detect. Use it sparingly.
  - If the user names a specific model, use that one instead.
- Start at the cheapest tier that fits the task. Move up only when the brief genuinely needs deeper reasoning. After a weak result, fix the brief first rather than moving up out of reflex.
- If a sub-agent type is unavailable, do the task directly or use the closest agent of the same scope, and say so.
