# Templator-GPT — product and technical design

## Intent

A quiet Windows workspace for repetitive business email. Choose a reusable
recipe, fill the details, review the result, and open a draft in your mail app.
The first use case is requesting SAP project creation after receiving an order.
Templator never sends email. It has no accounts, AI calls, telemetry, or runtime
network dependency. “GPT” identifies this independently built repository, not
a requirement for an AI service.

## Fresh implementation decisions

This solution is built from the requirements, not from the previous Templator
application. The original briefs contained contradictory SDK versions and old
repository paths. This document supersedes those implementation assumptions.

- C# / .NET 10 LTS, WPF, no third-party runtime packages. Native Windows is a
  good fit for a small, offline utility and OS mail/clipboard integration.
- Three projects: a UI-independent core, a WPF application, and executable
  regression tests. UI integration checks exercise the actual WPF controls.
- System fonts, vector shapes, warm paper preview, charcoal workspace, mint
  accent. Standard window chrome retains snapping, resizing, and accessibility.
- Compose and Edit template are separate modes. Daily use should not expose
  raw placeholders or definition controls unnecessarily.
- The primary action is **Open draft**, accurately describing what it does.
  Opening a handler is not proof that a mail client accepted every field.
- A framework-dependent portable build is the default; a self-contained
  Windows build is an optional distribution target.

.NET 10 is supported through November 2028. .NET 8 and 9 are still supported
as of September 2026; the old notes claiming otherwise were incorrect.
Sources: [support policy](https://dotnet.microsoft.com/en-us/platform/support/policy),
[WPF .NET 10](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/whats-new/net100).

## Interaction

The library sidebar offers search, New, Duplicate, Delete, and Move up/down.
Import, Export, and Settings are secondary actions at the bottom. Search matches
name and subject. Ctrl+F focuses it; Ctrl+N creates a template; Ctrl+Enter opens
a valid draft; Ctrl+S immediately saves pending work.

Compose shows labeled variable inputs with required markers and example hints.
A reading pane shows resolved To, Cc, subject, and body. Substituted text is
subtly highlighted; missing required values remain visible in an amber style.
The footer shows missing fields, URL size, save state, and draft/copy actions.
Reset values requires confirmation. English and Finnish starter recipes contain
no saved personal data or prefilled recipients.

Edit template exposes name, language (English/Finnish metadata, not automatic
translation), To, Cc, subject, and a plain-text body editor. A variable definition
section supports adding keys, labels, example hints, required flags, and deletion.
Every piece of user content can be changed in the GUI. No JSON editing is needed.

## Variable contract

- Placeholders: `{{key}}`, with optional whitespace inside braces; keys are
  case-sensitive ASCII letters, digits, and underscores.
- Discover across **all four fields**: To, Cc, subject, and body, in that order.
- New keys receive a humanized label and are required by default. Starter Cc
  and delivery date are explicitly optional.
- Definitions retain their order, metadata, and saved values. A persisted
  `customized` flag distinguishes deliberate definitions from derived ones.
- Prune an unreferenced definition only when it has neither a value nor a
  customization. Retained unused definitions are marked as such and never
  block opening a draft. Deleting a referenced definition re-derives it.
- Rendering is a single pass: a value containing `{{text}}` stays literal.
- Required means non-whitespace. Missing optional values render as empty text.
- Values persist per template; settings defaults only prefill the conventional
  `recipient`/`cc_list` fields in newly created templates. Reset stays empty.

## Persistence and recovery

Data lives in `%APPDATA%\Templator-GPT`, deliberately separate from other apps.
`TEMPLATOR_DATA_DIR` can override this for testing or portable use.

`templates.json` is a versioned UTF-8 document:

```json
{
  "version": 1,
  "templates": [{
    "id": "c0c11d35-9a2c-4f11-932a-111111111111",
    "name": "Example", "language": "en",
    "to": "{{recipient}}", "cc": "", "subject": "Hello {{customer}}",
    "body": "Thank you.",
    "variables": [
      {"key":"recipient", "label":"Recipient", "example":"team@example.com", "required":true, "customized":true},
      {"key":"customer", "label":"Customer", "example":"Acme Oy", "required":true, "customized":false}
    ],
    "values": {}
  }],
  "settings": {"defaultTo":"", "defaultCc":"", "mailtoLengthThreshold":1800}
}
```

Edits debounce to disk; closing flushes pending changes. A failure leaves the
in-memory work intact and displays an actionable error. Closing after a save
failure requires an explicit decision to discard unsaved work.

Write a unique same-directory temporary file, flush it to disk, then atomically
replace the destination while keeping its previous contents in `.bak`. A file
lock prevents simultaneous writers. Malformed data is preserved under a unique
`.bad-<timestamp>` filename before starting an empty library with a recovery
notice. Access failures and unknown schema versions stop loading without
rewriting the original. Window bounds are independent and disposable; off-screen
positions fall back to the current work area.

Import validates the whole document first and adds independent copies with new
IDs. Existing templates and settings are never overwritten. Export includes all
saved values: it is a personal backup, and may contain business information.
The same document format with one template is a single-template export/import.
Limits: 10 MB per document, 1,000 templates, 500 definitions per template,
200,000 characters per template source field. Reject invalid structures rather
than silently normalizing broken user data.

## Draft handoff

Require all referenced required values and at least one valid To or Cc address.
Support plain ASCII addresses separated by commas or semicolons; display names,
internationalized addresses, control characters, and header injection are
rejected with a specific explanation. Subject is a single line. Body and values
support full Unicode, including Finnish and emoji. Bcc is deliberately absent.

`mailto:` uses UTF-8 percent encoding (spaces `%20`), CRLF body breaks, encoded
recipient separators, and no empty Cc parameter. Launch only through the Windows
shell. If a handler fails, attempt to copy the body and report both outcomes.

Default advisory URL threshold: 1,800 characters (configurable 256–30,000).
Long drafts offer:

1. Copy body and open headers only, if headers fit the threshold.
2. Save and open an `.eml` file. MIME has base64 UTF-8 body lines and correctly
   folded RFC 2047 subject words without splitting Unicode code points.
3. Open the full URL anyway, explicitly acknowledging possible truncation.

`.eml` includes `X-Unsent: 1`, but editable-draft behavior depends on the mail
client/version. Always keep Copy body and Copy full text available. There is no
SMTP, Outlook COM dependency, automatic delivery, or claim that all Outlook
versions handle the same draft format identically.

## Verification and delivery

Build with warnings as errors. Regression checks cover variable lifecycle,
nonrecursive rendering, Unicode, recipient validation, injection, URI/MIME
round trips, persistence, corruption recovery, unknown versions, import/clone
isolation, backup replacement, and concurrent access. WPF smoke checks cover
real data bindings, editing, save/restart, and rendering at supported sizes.

Outlook interoperability is a manual release check; no test sends email. Record
what actually ran in `docs/VERIFICATION.md` and keep limitations explicit.
GitHub Actions builds and verifies on Windows. Publish this independent solution
to `Vkuparin/Templator-GPT` as a private repository unless requested otherwise.
