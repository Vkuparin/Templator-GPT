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
- Four projects: a UI-independent core, a WPF application, core regression
  tests, and WPF integration tests exercising actual controls.
- System fonts, vector shapes, warm paper preview, charcoal workspace, mint
  accent. Standard window chrome retains snapping, resizing, and accessibility.
- One visual email editor replaces the separate Compose/Edit modes. To, Cc,
  subject, and body are edited directly on the paper; no raw syntax is exposed.
- A palette of named variables supports create-first, drag-to-insert, and click
  insertion at the last email cursor position. Clicking an inserted chip edits
  its shared value. Variable properties live in a small dialog.
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

The email paper is a plain-text WYSIWYG editor, including To, Cc, subject,
and body. Existing text is directly editable; blank messages show a writing hint.
Template name is editable in the header. The language selector changes metadata,
not content. All edits update the reusable template and save automatically.
The footer communicates readiness or long-draft options in ordinary language.

The variable palette separates reusable details from the message itself:

1. Create a variable with a friendly name, optional example hint, and required flag.
2. Drag its name to a position in any email field, or place the cursor and click
   the name. Drop coordinates determine insertion position; dragging near the
   top/bottom of the email scrolls long messages.
3. Fill the palette input, or click a chip to edit its value. Every occurrence
   updates together without moving the text cursor or clearing undo history.
4. Edit properties through the palette's Edit action. Internal keys never appear.

Empty chips display their names; required empty chips use amber and filled chips
use green. Optional empty chips remain visible in the editor but produce empty
text in the outgoing email. Chips add editing affordances, not email formatting.
New templates are blank, with only settings-provided literal To/Cc prefilled.

## Visual editor contract

`TokenEditor` uses WPF FlowDocument/RichTextBox. Literal text is represented by
runs/paragraphs; each variable is an atomic InlineUIContainer tagged with its
stable key. Serialization recovers semantic source, never the rendered value.
The existing JSON schema is preserved, and legacy placeholders become chips on
load. Placeholder whitespace is canonicalized for document comparisons only.

- To/Cc/subject reject Enter and normalize pasted line breaks to spaces. Normal
  address and subject validation still runs before draft handoff.
- Body paragraph breaks and soft breaks round-trip as newline characters.
- Local typing keeps the same document, cursor, and WPF undo history. Value/label
  changes refresh only chip appearance. Template switches and external structure
  replacement reset document history so undo cannot affect another template.
- Insertions are one undo operation. Backspace/selection deletion removes a whole
  chip, and undo restores its identity and click behavior via a routed handler.
- Copy/cut provide resolved Unicode text to other apps and a private string-only
  fragment for this template. Same-template paste retains chips only if their
  definitions still exist; other paste uses plain text, never foreign RTF/XAML.
- Variable drag payloads are string-only JSON with template ID and key. Reject
  stale, foreign-template, malformed, and deleted-variable payloads.
- No rich formatting, HTML, images, or embedded external content is accepted.

## Variable contract

The following is an internal storage contract, not a syntax users need to learn.

- Source uses `{{key}}`, with optional whitespace inside braces. Keys are
  case-sensitive ASCII letters, digits, and underscores.
- Friendly names generate safe keys automatically (including Finnish names).
  Collisions receive numeric suffixes. Renaming a label never changes its key.
- Discover existing references across all four fields: To, Cc, subject, and body.
- Definitions, order, metadata, and values persist independently of references.
  Removing the last chip keeps the variable in the palette and marks it unused.
  The legacy `customized` flag remains compatible; it no longer controls pruning.
- Explicit variable deletion removes its definition, saved value, and every use
  from all four source fields after confirmation. It is not silently re-derived.
- Required unused variables do not block sending. Referenced required fields must
  contain non-whitespace values. Missing optional values render as empty text.
- Rendering is single-pass: placeholder-like content inside a value stays literal.
- Values are per template. Reset clears variable values, not literal text or
  addresses typed directly into the email editor.

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
