# Templator

**A little less repetition.** A local Windows workspace for emails you write
again and again. Fill the details, review the message, and open a draft in your
own mail app.

![Templator compose workspace](docs/images/compose.png)

Built independently from the product brief in this repository. C# / .NET 10,
native WPF, no third-party runtime packages. No AI service, account, telemetry,
or network calls. Templator **never sends mail**.

## Start

Windows 10/11 x64. Download **Templator.v1.2.0.zip** from the
[latest release](https://github.com/Vkuparin/Templator-GPT/releases/latest),
extract it and run `Templator.exe`. This standalone build includes the runtime;
no installer or administrator rights are needed. The smaller `-portable` ZIP
requires the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/10.0).

Build from source with the .NET 10 SDK:

```powershell
dotnet run --project src/Templator
```

## Use

1. Choose an English or Finnish starter, or create a blank email template.
2. Click **To**, **Cc**, **Subject**, or the **Message** on the paper to edit it
   directly. Click the template title to rename it. Edits save automatically.
3. Under **Variables**, choose **+ New** and enter a friendly name such as
   “Customer name.” An example hint and required flag are optional settings.
4. **Drag the variable's name into the email** at the desired position. You can
   use it in the message, subject, To, or Cc. Alternatively, place the text cursor
   first and click the variable's name to insert it there.
5. Fill its value in the palette, or **click a chip in the email**. Every use of
   that variable updates together. Then choose **Open draft** and review in your
   own mail app before sending.

No placeholder syntax is needed. Empty chips show their names; filled chips show
actual values. Required empty chips are amber. Optional empty chips remain visible
so you can edit them, but contribute no text to the outgoing email.

Use **Edit** beside a variable to rename it, change its hint/required setting, or
delete it. Removing a chip from the email keeps the variable available for later.
Deleting the variable itself asks for confirmation and removes all its uses and
saved value. Unused variables never block opening a draft.

The email editor supports ordinary text editing, selection, undo/redo, and paste.
Chips behave as single editable objects: Backspace removes a whole chip, and undo
restores it. Copying text to another app uses the filled values. Copying between
fields of the same template retains the variables. Pasting from another template
or app uses plain text. Formatting is intentionally plain text to match the draft
handoff; there is no HTML formatting toolbar.

The language choice is metadata; it does not translate your text. The interface
is in English. Existing backups load directly into the visual editor without a
migration or re-entering values.

To and Cc accept literal addresses or variables. Use plain email addresses,
separated by commas or semicolons. Display names and internationalized email
addresses are not supported; message content supports full Unicode.

- **Duplicate** makes an independent copy, including remembered values.
- **Reset to variable names** clears filled values and restores the named chips,
  keeping the template text and definitions intact. **Undo reset** restores the
  previous values until you change a variable's value/definition or close the app.
- Right-click a library item to move it up/down, duplicate, or delete it.
- **Import** adds copies from a backup without replacing existing templates or
  settings. **Export** backs up the entire library, including saved values.
- **Settings** prefills To/Cc in new blank templates and provides a configurable
  long-draft warning threshold.

| Shortcut | Action |
| --- | --- |
| Ctrl+F | Search library |
| Ctrl+N | New template |
| Ctrl+S | Save now / retry failed save |
| Ctrl+Z / Ctrl+Y | Undo / redo in the email editor |
| Ctrl+Enter | Open a valid draft |

For long messages, choose **Copy body + open draft**, save an **.eml draft**, or
explicitly open the full link anyway. Copy body/full text also work independently.
Mail clients have different link limits and `.eml` editing behavior; the threshold
is advisory. The clipboard fallback is usually the most predictable choice.

**Open draft can be used repeatedly.** After discarding or sending a draft in your
mail app, edit the email here and click it again for a fresh request. Each click
validates the current content; incomplete or invalid fields get an explanation
instead of a disabled button. Opening a draft never clears the template or values.
Templator cannot track whether you sent or discarded a draft in another app.

## Update without losing templates

1. Close Templator. Optionally use **Export** first for an extra backup elsewhere.
2. Extract the new release ZIP into a new folder, or replace the old executable.
3. Run the new `Templator.exe` using the same Windows account. Your library and
   settings load from the same `%APPDATA%\Templator-GPT` folder automatically.

Release ZIPs contain the application and README, never your library. Do not delete
the data folder when updating. If you use `TEMPLATOR_DATA_DIR`, keep that override
the same. Version 1.2.0 uses the existing version-1 JSON format, with no migration.

Before this version opens an existing valid library, it creates
`backups/templates-before-1.2.0.json` inside the data folder. Later saves and
restarts never overwrite this snapshot. If creating it fails, startup stops with
an error and leaves the original library untouched. Settings → **Open data folder**
locates it. You can import the snapshot, or close the app and copy it over
`templates.json` for an exact restoration. Keep a separate exported backup when
moving computers; local backups do not protect against disk loss.

## Local data and recovery

Data is stored at `%APPDATA%\Templator-GPT`, separate from any older Templator
installation. This app never reads that older folder automatically.

- `templates.json`: templates, definitions, values, and settings.
- `templates.json.bak`: previous successful save, retained during atomic replacement.
- `backups/templates-before-<app-version>.json`: original library at first launch
  of that version with an existing store; retained independently of autosaves.
- `templates.json.bad-<timestamp>`: original malformed file preserved for recovery.
- `window-state.json`: window bounds. Invalid/off-screen bounds are ignored.
- `crash.log`: last-chance exception diagnostics, if needed.
- `store.lock`: prevents simultaneous writers; the empty file may remain after exit.

Edits save after a short pause. The sidebar distinguishes saving, saved, and
failed states. If saving fails, work stays in memory; retry with Ctrl+S or export
it before closing. The app asks before discarding unsaved changes.

To recover, use **Import** and select `templates.json.bak` or another exported
backup. Import always creates copies. For exact restoration, close the app, copy
your backup over `templates.json`, then reopen it. Future schema versions are
left untouched and require a compatible app.

Templates and values are plain local JSON, not encrypted. Exported files and
clipboard contents can contain business information. There is no runtime
networking in Templator; your chosen mail app handles its own connections.

For a separate workspace or testing:

```powershell
$env:TEMPLATOR_DATA_DIR = "$PWD\scratch-workspace"
dotnet run --project src/Templator
```

## Verify and package

```powershell
./scripts/Verify.ps1
./scripts/Publish.ps1                 # single EXE; Desktop Runtime required
./scripts/Publish.ps1 -SelfContained  # includes Microsoft's Windows runtime
```

The normal build uses installed SDK/reference packs and no external packages.
The standalone publish may download Microsoft runtime packs from NuGet.
ZIPs appear in `artifacts`. GitHub Actions verifies the app on Windows and
publishes a portable build artifact for each successful main-branch run.

Tests use isolated temporary workspaces and a fake mail/clipboard platform.
They do not open Outlook, send mail, or touch your normal data. UI tests create
real WPF windows off-screen and render PNGs under `artifacts/ui` for inspection.

## Project map

| Location | Responsibility |
| --- | --- |
| `src/Templator.Core` | Store, variable lifecycle, rendering, URI/MIME generation |
| `src/Templator` | WPF interface, presentation state, Windows integration |
| `tests/Templator.Tests` | Core regression suite |
| `tests/Templator.UiTests` | Actual WPF bindings, lifecycle, dialogs, and renders |
| `docs/DESIGN.md` | Product contract and design decisions |
| `docs/VERIFICATION.md` | Verification evidence and manual release checks |

The app is deliberately small: no Outlook COM dependency, HTML email, Bcc,
attachments, cloud sync, automatic updates, or automatic sending.
