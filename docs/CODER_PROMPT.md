# Implementation brief

Build and maintain the independent Templator-GPT solution described in
[DESIGN.md](DESIGN.md). That document is the authoritative product contract.
Do not inspect or depend on the source or data of the older Templator project.

## Priorities

1. Reliable local data and safe draft handoff. Never send email.
2. A focused compose flow with a readable live preview.
3. GUI editing of every template field and variable definition.
4. Clear behavior on invalid data, missing mail handlers, and failed saves.
5. Small dependencies and straightforward, testable architecture.

## Implementation boundaries

- `src/Templator.Core`: data models, variable parsing/rendering, store, mail formats.
- `src/Templator`: WPF styles/views, presentation state, dialogs, OS integration.
- `tests`: deterministic executable regression checks and WPF smoke checks.
- `scripts`: verification and portable packaging commands.
- `docs`: product decisions, architecture, and honest verification evidence.

C# / .NET 10 is chosen intentionally. Dependencies/toolchain changes are allowed
when justified by user value, but do not add infrastructure for hypothetical
scale. Keep runtime behavior offline and avoid web wrappers for this native app.

## Completion bar

- Release build clean with warnings treated as errors.
- Core regression suite and WPF binding/lifecycle checks pass.
- Compose, edit, CRUD, search/reorder, import/export, reset, and settings work.
- Draft guards and all long-message fallbacks work without sending any mail.
- Autosave and recovery protect data; no unsaved changes silently discarded.
- English/Finnish content and Unicode survive store and draft round trips.
- README explains running, use, backup/recovery, privacy, and distribution.
- Artifacts generated locally; source committed and pushed to the new repo.
- Distinguish automated checks from manual mail-client interoperability checks.
