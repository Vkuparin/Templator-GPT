# Templator 1.2.0

- **Repeatable Open draft:** stays available while a template is selected. Every
  click validates the current email and explains anything missing or invalid.
  Edit after sending/discarding in your mail app and request another draft.
- **Reset to variable names:** a prominent button clears filled values while
  preserving the reusable template. Undo reset restores values until a subsequent
  variable edit or app close.
- **Search editing fix:** changing a matching subject no longer detaches the
  selected template or crashes the editor.
- **Safer upgrades:** the first launch with an existing library preserves its
  original bytes in `backups/templates-before-1.2.0.json`. Autosave never replaces
  this snapshot. Existing version-1 templates, identities, values and settings
  remain compatible.

## Downloads

- **Templator.v1.2.0.zip** — Windows x64, runtime included. Recommended.
- **Templator.v1.2.0-portable.zip** — smaller; requires .NET 10 Desktop Runtime.
- **SHA256SUMS.txt** — checksums for both ZIPs.

## Updating

Close Templator, extract the new ZIP, and run `Templator.exe` with the same Windows
account. Templates stay in `%APPDATA%\Templator-GPT`, outside the application
folder. Keep the same `TEMPLATOR_DATA_DIR` override if you use one. Do not delete
the data folder. Export is available for an additional backup before updating.

Settings → Open data folder locates the automatic snapshot. Import it as copies,
or close the app and copy it over `templates.json` to restore the exact library.
The README includes full update and recovery instructions.

## Verification

Release build: zero warnings/errors. 38 core and 47 real WPF integration checks
pass, including repeat requests after edits, invalid-draft explanations, reset
and undo, filtered editing, and update backup preservation/failure handling.
Mail handoff tests use a fake handler; the user's exact mail-app sequence was
not reproduced, and no automated test opens Outlook or sends email.
