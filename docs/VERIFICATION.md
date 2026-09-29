# Verification record

Version 1.2.0 verified locally on 29 September 2026, Windows 11 x64, .NET SDK 10.0.401.
This is a fresh implementation in Templator-GPT; no source or user data from the
older Templator application was read.

## Automated evidence

`./scripts/Verify.ps1` completed successfully:

- Release solution build: **0 warnings, 0 errors**.
- Core regression suite: **38 passed, 0 failed**.
- WPF UI/integration suite: **47 passed, 0 failed**.
- WPF binding trace: **no errors**.

Core checks cover all-field discovery, required/optional and unused variables,
retention, friendly-name key generation, nonrecursive rendering, Unicode, recipient and header
validation, URI/MIME round trips, MIME folding, atomic replacement, previous-save
backups, corruption preservation, unsupported versions, locking, clone isolation,
structural/size validation, and source-file preservation.
Upgrade checks cover byte-identical snapshots, preservation across repeated saves
and restarts, a separate snapshot for the next version, failed-backup startup
protection, and rejection of future schemas before snapshot creation.

WPF checks use actual controls, FlowDocuments, buttons, bindings, radio buttons,
variable creation/value dialogs, settings, and long-draft dialogs. They cover:

- Direct To/Cc/subject/body editing and blank new-template behavior.
- Friendly variable creation, human labels, required/unused status, and rename.
- Position-based dropping between words using the same coordinate/hit-test path
  as the drop handler, insertion into all four fields, and palette click insertion.
- Repeated chip values, preserved caret/document/undo state on value changes.
- Backspace, selected-chip deletion, undo/redo, and clicking a restored chip.
- Internal semantic clipboard data, resolved external text, partial selection,
  plain-text paste, paragraph/soft-break round trips, and header newline handling.
- Foreign/stale drag rejection, cross-template paste, and removed definitions.
- Existing placeholder whitespace, semantic save/restart, and all-use deletion.
- Search, clone/reorder/delete, empty library, import/export, reset/defaults,
  failed-save retry, URI fallbacks, MIME persistence, fake clipboard/handler errors.
- Repeated Open draft requests, edits between requests, explicit invalid-draft
  explanations, reset/undo through real buttons, and undo invalidation on new values.
- Editing a filtered subject without detaching selection. Running this regression
  with the old collection-refresh logic reproduced a RichTextBox document-change
  exception; the corrected logic passes.

The user's exact post-send disabled-button sequence was not reproduced. A valid
draft could already be requested repeatedly under the fake handler. The button
now remains available with a selected template and validates on every click,
reporting the reason if it cannot open. The mail app is outside the test boundary.

No email client or delivery is invoked. The shared drop logic and rendered
coordinates are automated; a physical mouse/OLE drag gesture was not driven.
Screenshots from actual WPF controls were inspected at 1360×880 and 1050×680
logical window sizes, including the new variable dialog. Reference images contain
fictional data only. Caption wrapping was corrected during visual inspection.

## Packaged executable checks

Both `./scripts/Publish.ps1` and `./scripts/Publish.ps1 -SelfContained` succeeded.
Each output has one application executable plus a README. The standalone build
downloaded Microsoft's runtime packs; it has no third-party app packages.

Both executables were launched with isolated `TEMPLATOR_DATA_DIR` values, checked
for their main window, and closed through the normal window-close path:

| Package | EXE size | File version | Exit code |
| --- | ---: | --- | ---: |
| Portable (Desktop Runtime required) | 306,935 bytes | 1.2.0.0 | 0 |
| Standalone (runtime included) | 139,837,045 bytes | 1.2.0.0 | 0 |

Both packaged apps loaded an existing test library and created the 1.2.0 snapshot.
Original, backup and post-exit library hashes matched byte-for-byte. No normal
AppData library was read or changed. ZIP SHA-256 checksums accompany the release.

## Manual interoperability checks still to perform

The installed Outlook version has **not** been driven by these tests. Before
using real business content, review a draft with fictional data in your mail app:

- To-only and Cc-only addressing; multiple addresses.
- Finnish characters and emoji in subject/body.
- Short mailto: all expected fields survive.
- Long-draft clipboard path: paste body into the draft.
- .eml path: inspect the message and whether your Outlook version allows editing.
- Cancel/discard the draft. No test requires sending a message.

Native file-picker interaction, screen-reader behavior, high-contrast mode, and
multi-monitor/DPI changes were not manually verified. JSON parsing/file output
behind the pickers is covered. Bounds outside the virtual desktop are rejected;
complex monitor arrangements may still need a manual reposition.

## Reproduce

```powershell
./scripts/Verify.ps1
./scripts/Publish.ps1
./scripts/Publish.ps1 -SelfContained
```

GitHub Actions runs the same build and test suites on a Windows runner and
uploads the portable ZIP and rendered UI images. Check the actual workflow run
for remote results; the local evidence above does not imply a remote pass.
