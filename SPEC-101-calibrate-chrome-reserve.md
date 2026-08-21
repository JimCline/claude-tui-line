# SPEC-101 — `--calibrate`: measure the real chrome reserve on this machine

Status: **OPEN for implementation.** Design closed. NEEDS-EVIDENCE items in §9
(E1 blocking) and §12.10 (E4 blocking for the auto-prompt half only).

**Amendment 1** — §10's product question was raised with Jim and answered: he
wants manual `--calibrate` **plus** a prompt on first run on a machine **plus** a
prompt when the Claude Code version changes. That scope is designed in **§12**,
which also **replaces §5.2** and revises §10. §§1–11 below are otherwise
unchanged from the original. The amendment additionally overturns a fact §12.1
would have got wrong: Claude Code *does* send its version.

Follows SPEC-98 (`SPEC-98-stacked-fill-pane-overflow.md`), which shipped
`DefaultChromeReserve = 4`. This spec does **not** modify SPEC-98, its constant,
or anything it shipped. It is purely additive.

---

## 1. Goal

`DefaultChromeReserve = 4` is a measured constant, but it was measured **once**,
on **one machine**, against **one Claude Code version**, at **one terminal
width**. SPEC-98 §2.3 records the consequence explicitly: the truncation
boundary lives outside this process, so **no automated test in this repo can
ever detect that the constant has rotted.** A future Claude Code release that
changes its statusline padding by one column silently reintroduces the exact
`…` truncation bug SPEC-98 fixed, and the suite stays green.

`--calibrate` is the answer to that: a human-in-the-loop procedure that measures
the real budget on the user's own machine and writes the result to
`layout.chromeReserve` in their config.

Non-goal: changing `DefaultChromeReserve`. It remains the fallback for everyone
who never calibrates.

---

## 2. Why this cannot be a self-contained command

This is the single most important constraint in the spec, and the one most
likely to be designed away by accident.

**The process cannot observe its own rendered output.** SPEC-98's E2 was
withdrawn for exactly this reason and the Implementor was right to refuse it.
Claude Code reads our stdout, applies its own indent and its own truncation, and
draws the result. Nothing we can run — not from the hook, not from a terminal —
can read back what was drawn.

Two corollaries that constrain every part of the design below:

1. **The measurement must happen on the hook path**, because that is the only
   code path whose output Claude Code renders. A `--calibrate` invocation typed
   into a terminal is *not* rendered by Claude Code at all; it is ordinary
   stdout. It therefore cannot measure anything by itself.
2. **A human must report the observation.** There is no way around this. The
   command's job is to make that report as small, as unambiguous, and as
   hard-to-get-wrong as possible — a single digit.

This forces a **stateful, multi-invocation protocol**: the terminal command and
the hook are different processes at different times, and they communicate
through a file on disk.

---

## 3. The measurement method

### 3.1 The ruler

In ruler phase, the hook emits a row of **exactly `COLUMNS` characters**, where
the character at 1-based emitted column `i` is the ASCII digit `(i mod 10)`.

At `COLUMNS = 90` that row is `1234567890123…0` — column 85 holds `5`, column 90
holds `0`.

Because the emitted width is `COLUMNS` (i.e. a chrome reserve of zero), the row
is guaranteed to overflow Claude Code's budget whenever the true reserve is
greater than zero. The user sees the row truncated, with the ellipsis at the
boundary.

The user reports **the last digit they can read** — the digit immediately before
the ellipsis. Call its emitted column `P`.

### 3.2 The arithmetic, and why it is indent-free

```
chromeReserve = COLUMNS − P − 1
```

Check against SPEC-98's measured case: `COLUMNS = 90`, ellipsis at emitted
column 86, so the last readable digit is `5` at `P = 85`, giving
`90 − 85 − 1 = 4`. ✓ Agrees with the value SPEC-98 shipped.

**Note what does not appear in that formula: Claude Code's left indent.**

SPEC-98's most dangerous error was a formula that double-counted the indent and
derived a reserve *smaller* than the one already shipped; it was caught only
because the Implementor refused to ship a constant that contradicted its own
spec's reasoning. The ruler is immune to that class of error **by
construction**: the digits are characters *we* emitted, so `P` is measured in
*our own emitted columns*. The indent shifts the ruler rightward on screen but
does not renumber it. Both halves of the chrome — the left indent and the right
margin — are captured in the single quantity `COLUMNS − P − 1`, and the indent
never has to be known, named, or counted.

Implementors and reviewers: if you find yourself adding or subtracting an indent
term anywhere in this calculation, you have reintroduced SPEC-98's bug. There is
no indent term.

### 3.3 Resolving the digit to a column

A digit is only `P mod 10`, so it must be resolved against a window. Constrain
the reserve to `0 ≤ chromeReserve ≤ 9`, which puts `P` in
`[COLUMNS − 10, COLUMNS − 1]` — exactly ten consecutive columns, whose digits
are a permutation of `0`–`9`. **The digit is therefore unique within the window,
with no ambiguity.**

```
P = COLUMNS − 1 − ((COLUMNS − 1 − d) mod 10)
```

where `d` is the reported digit. Worked example: `COLUMNS = 90`, `d = 5` →
`(90 − 1 − 5) = 84`, `84 mod 10 = 4`, `P = 89 − 4 = 85`. ✓

The `0 ≤ reserve ≤ 9` assumption is the one place this method can silently
produce a wrong answer (a true reserve of 14 would be reported as 4). §3.4 is
what catches that.

### 3.4 The verify phase — non-optional

After computing a candidate `R`, the hook emits **two** probe rows:

| row | emitted width | content | expected on screen |
| --- | --- | --- | --- |
| A | `COLUMNS − R` | `A` + `─`×(W−2) + `A` | ends in `A`, **no** ellipsis |
| B | `COLUMNS − R + 1` | `B` + `─`×(W−2) + `B` | ends in **ellipsis** |

This reproduces E6's two-sided bracket — the experiment that actually closed
SPEC-98 — automatically: one width that fits and one that does not, differing by
a single column, pinning `R` from both sides.

**Verify is not a nicety and must not be made skippable.** It is the only check
on two assumptions that the ruler phase takes on faith:

- the `0 ≤ reserve ≤ 9` window of §3.3;
- SPEC-98's measured finding that the ellipsis **replaces** the boundary cell
  rather than being appended past it. If a future Claude Code appends instead,
  §3.2's formula is off by one and verify is what reveals it.

### 3.5 Two rules every probe row must obey

- **Never end a probe row in whitespace.** SPEC-98 established that Claude Code
  trims trailing whitespace *before* deciding overflow — that is precisely why
  the `content`-sized pane hid the bug while the `fill` pane exposed it. A probe
  row ending in a space would measure the wrong thing. Every row above ends in a
  non-space glyph.
- **Probe rows are plain ASCII, unstyled: no ANSI, no Spectre markup.** Widths
  must be exact and digits must be legible. (The `─` in the verify rows is the
  one non-ASCII glyph; see E1.)

### 3.6 Calibration bypasses the layout pipeline

The probe rows are written **raw to stdout**, one per line. They do not go
through `SurfaceLayout`, `RowLayout`, `PaneTreeRenderer`, `PaneBorderRenderer`,
or `Compositor`.

Rationale: a calibrator that runs through the code it is calibrating cannot
detect that code being wrong. In particular it must not subtract a chrome
reserve — the ruler's whole premise is that it is emitted at full `COLUMNS`.

---

## 4. Files to touch

| file | change |
| --- | --- |
| `src/ClaudeTuiLine/Program.cs` | new `--calibrate` mode + sub-flags in `RunCli`'s switch (lines 489–557) and mode dispatch (lines 613–654); probe branch on the hook path in `RunAsync` (§5.1); nudge append (§12.4) |
| `src/ClaudeTuiLine/CalibrateCommand.cs` | **new.** All calibration logic: state I/O, ruler/probe row generation, digit→reserve arithmetic, phase transitions, config write |
| `src/ClaudeTuiLine/CalibrationRecord.cs` | **new.** §12.2 durable record I/O and trigger evaluation |
| `src/ClaudeTuiLine/StatusInput.cs` | add `Version` property (§12.1) |
| `src/ClaudeTuiLineShared/ConfigPath.cs` | add `ResolveCalibrationStatePath()` and `ResolveCalibrationRecordPath()` (§6.1, §12.2) |
| `src/ClaudeTuiLineShared/` (writer location) | see §6.3 — conditional |
| `src/ClaudeTuiLine/Config.cs` | add `layout.calibrationPrompt` (§12.7) |
| test project containing `ConfigTests.cs` | **new** `CalibrateTests.cs` (§8) and `CalibrationPromptTests.cs` (§12.9) |
| `SPEC.md`, `README.md` | document the `--calibrate` flow and the prompt |
| `src/ClaudeTuiLine/SchemaCommand.cs` | mention `--calibrate` in the `chromeReserve` description (§7) |

**Must not change:** `DefaultChromeReserve` (still `4`); `SurfaceLayout.cs`;
anything in the layout, sizing, or rendering path; the hook's rendered rows when
neither calibration nor a prompt is active (§12.5).

---

## 5. Hook-path integration

### 5.1 Where the probe branch goes

Insert **immediately after the stdin read** in `RunAsync` — after the
`try { rawInput = await Console.In.ReadToEndAsync(); } catch { rawInput = null; }`
block at Program.cs lines 20–27, and **before** `ParseInput` at line 29.

Three reasons this exact position, all load-bearing:

- **After the stdin read**, so Claude Code's pipe is always drained. Exiting
  without consuming stdin risks EPIPE in the parent.
- **Before `ConfigResolution.LoadRenderConfig`** (line 35), so the ruler renders
  even when the user's config is unreadable. Someone with a broken config is
  exactly the person who may be trying to diagnose widths. (Writing the result
  into an unreadable config is separately refused — §6.4.)
- **Before the git probe, Engram telemetry, and item building**, none of which
  calibration needs. The branch returns, so none of that work is done.

The branch reads `COLUMNS` from the environment itself. This does **not** violate
SPEC-V2-FRAMEWORK §2.5 ("COLUMNS read exactly once, at the surface-sizing
root"), because the calibration branch *returns* and never reaches the
surface-sizing root — COLUMNS is still read exactly once per process. State this
in a comment at the branch; a reviewer checking that invariant will otherwise
flag it.

If `COLUMNS` is unset or unparseable, the branch emits a single row reading
`claude-tui-line: calibration needs COLUMNS` and returns 0.

### 5.2 Cost and output when calibration is not running

> **Amendment 1: this section is superseded by §12.5.** It survives here only so
> the original single-invariant statement is legible. Implement §12.5.

One `File.Exists` on the state path. The hook runs on every statusline render,
so this must stay at one stat call — do not read, parse, or deserialize anything
when the file is absent. When absent, output must be **byte-identical** to
today's; the existing golden tests are the guard.

### 5.3 Rows emitted, per phase

Both phases lead with a label row so a forgotten calibration is
self-explaining on screen rather than mysterious:

```
ctl calibrate: report last readable digit
```

Keep the label **≤ 60 characters** so it does not itself truncate. **Omit the
label row entirely when `COLUMNS < 70`**, so a narrow pane still gets an
uncorrupted ruler.

- **ruler phase:** label row, then the §3.1 ruler.
- **verify phase:** label row (`ctl calibrate: does row A end in "A", row B in "…"?`),
  then row A, then row B, per §3.4.

Both phases are multi-row throughout. This is deliberate: multi-row is the case
that actually bit in SPEC-98 and the case the uniform reserve was chosen for. See
§9 E3 for the single-row question this leaves open.

### 5.4 The hook writes `COLUMNS` back into the state

In ruler phase the hook records the `COLUMNS` value it actually saw into the
state file.

This is necessary, not incidental: the reserve arithmetic needs `COLUMNS`, and
the `--calibrate --saw` invocation runs in a *different* terminal process whose
own `COLUMNS` may differ from the one Claude Code passed the hook. Deriving
`COLUMNS` from the calibrating terminal instead would be a silent
wrong-answer bug. Take it from the state file, never from the CLI's environment.

**This rule generalises**, and §12.6 applies it a second time: *any* fact known
only to the hook must reach the CLI through a file, never through the CLI's own
environment. The hook and the CLI are different processes in different contexts,
and every quantity that differs between them is a silent-wrong-answer bug
waiting to happen.

---

## 6. CLI surface

All sub-flags below are valid **only** together with `--calibrate`; using one
without it is a usage error via the existing `WriteUsageError`. `--calibrate`
participates in the existing `modeCount` mutual exclusion alongside `--check`,
`--preview`, etc. It accepts the existing `--json` modifier for machine-readable
output.

| invocation | precondition | effect |
| --- | --- | --- |
| `--calibrate` | — | write state `phase=ruler`; print instructions. Restarts/overwrites any calibration in progress |
| `--calibrate --saw <0-9>` | `phase=ruler` **and** state has `observedColumns` | compute `R` (§3.2–3.3); write state `phase=verify, candidate=R`; print instructions |
| `--calibrate --no-ellipsis` | `phase=ruler` | the ruler was not truncated at all → report reserve `0`, **write nothing**, clear state. §6.5 |
| `--calibrate --set <0-9>` | any | manual escape hatch: set `candidate` and go to `phase=verify` without a ruler reading |
| `--calibrate --confirm` | `phase=verify` | write `layout.chromeReserve = candidate` to config; clear state; update the §12.2 record; print `old → new` |
| `--calibrate --reject` | `phase=verify` | verify failed: print §6.6 diagnosis; clear state; **write nothing** |
| `--calibrate --cancel` | any | clear state; write nothing |
| `--calibrate --status` | any | print current phase/candidate, or "no calibration in progress" |
| `--calibrate --dismiss` | any | §12.6 — suppress the prompt for the current Claude Code version |

Between every step the user must **cause a statusline redraw** in a live Claude
Code session — the statusline updates on activity, not on file change. Every
instruction message must say so explicitly; this is the step users will get
stuck on.

### 6.1 State file

Path: the directory of `ConfigPath.ResolveConfigPath()` (the env-aware,
no-override form), filename `claude-tui-line.calibration.json`.

```json
{
  "phase": "ruler",
  "expiresAt": "2026-08-20T18:31:00Z",
  "observedColumns": 90,
  "candidate": 4
}
```

`observedColumns` is absent until the hook has rendered a ruler at least once;
`candidate` is absent until `phase=verify`.

### 6.2 `--calibrate` rejects `--config`

Deliberate, and it removes a whole class of bug rather than handling it.

The hook is invoked with **no arguments** (`args.Length == 0` → `RunAsync`), so
it can never know about a `--config` override and would look for state at the
default location. Allowing `--calibrate --config X` would let the CLI and the
hook disagree about where state lives, producing a calibration that appears to
start and then never shows a ruler — with no error anywhere.

Reject it with a usage error that says why. Both processes always derive the
state path the same way, via §6.1.

### 6.3 Where the atomic writer lives — conditional, decision-free

An atomic temp-file-then-rename writer already exists at
`src/ClaudeTuiLineMcp/ConfigFile.cs:38-39`, but it is in the MCP project, not
reachable from the CLI. Apply this rule mechanically:

- **If `ClaudeTuiLineMcp.csproj` already has a project reference to
  `ClaudeTuiLineShared`:** move `WriteAtomic` into `ClaudeTuiLineShared` and have
  `ClaudeTuiLineMcp/ConfigFile.cs` delegate to it. One implementation, both
  callers.
- **If it does not:** do **not** add a project reference. Implement the writer
  privately in `CalibrateCommand.cs` and file a follow-up ticket noting the
  duplication.

Do not decide this on taste — check the `.csproj` and follow the branch.

All three files this spec writes — config, calibration state, and the §12.2
record — go through that one writer.

### 6.4 The config write

Read-modify-write preserving everything else in the file:

- Parse the existing config as a **`JsonNode`**, not a typed model. This is how
  `ConfigTools.SetConfig` preserves unrecognized keys
  (`src/ClaudeTuiLineMcp/ConfigTools.cs:88`), and calibration must preserve them
  identically. Deserializing to `Config` and re-serializing would silently drop
  every key the typed model does not know.
- Set `layout.chromeReserve`, creating the `layout` object if absent.
- Serialize with `WriteIndented = true` (matching `ConfigTools.cs:88`) and write
  atomically per §6.3.
- **If the config file exists but does not parse, refuse to write** and tell the
  user to fix it first. Never overwrite a file we could not read — that would
  destroy a config whose only problem was a typo.
- If the config file does not exist, create it containing only
  `{"layout":{"chromeReserve":R}}`.
- Print the old value (or "unset, default 4") and the new one.

No plumbing beyond this is needed: `RunAsync` already reads
`topLevel.ChromeReserve` and passes it to `SurfaceLayout.ComputeWidth`
(Program.cs:74). The new value takes effect on the next redraw.

### 6.5 No ellipsis in ruler phase

A reserve of `0` is a legitimate result, but it is also what a stale state file,
a non-redrawn statusline, or a terminal wider than `COLUMNS` looks like. Report
`0`, explain those alternatives, and **write nothing**. Writing a `0` reserve on
a false negative would truncate every statusline the user has.

### 6.6 Verify failure

Print, do not guess:

- **Row A truncated** → the true reserve is larger than `R`; retry with
  `--calibrate --set <R+1>`.
- **Row B not truncated** → the true reserve is smaller; retry with
  `--calibrate --set <R−1>`.
- **Both wrong / neither row visible** → the ellipsis may no longer replace the
  boundary cell (§3.4), or the statusline did not redraw. Point at SPEC-98 §2.

Never auto-advance the candidate on failure. A failing verify means an
assumption broke, and silently stepping ±1 would paper over exactly the signal
the phase exists to produce.

### 6.7 Expiry

`expiresAt` = 30 minutes from `--calibrate`. An expired state file is ignored by
the hook (which renders normally) and reported as expired by the CLI. This
prevents a forgotten calibration from permanently replacing someone's
statusline with a ruler.

---

## 7. Discoverability

`layout.chromeReserve` existed as a user override long before SPEC-98 and
nobody knew — which is why E6 was cheap once found and expensive to find.
Extend `SchemaCommand.cs`'s `chromeReserve` description to name `--calibrate`
as the way to determine the right value. Keep it free of any hardcoded number,
per the change already made for SPEC-98.

---

## 8. Verification

New `CalibrateTests.cs`:

1. **Ruler shape** — for `COLUMNS` ∈ {40, 64, 70, 89, 90, 100, 137}: length is
   exactly `COLUMNS`; character at 1-based `i` is `(i mod 10)`; the row is pure
   ASCII digits; it does not end in whitespace.
2. **Digit → reserve round-trip** — for every `COLUMNS` above × every `R` in
   0..9: generate the ruler, take the digit at column `COLUMNS − R − 1`, feed it
   through §3.3's resolution, assert the recovered reserve equals `R`. This is
   the test that would have caught SPEC-98's formula error.
3. **SPEC-98 anchor** — `COLUMNS = 90`, `d = 5` → reserve `4`, asserted as a
   literal with a comment citing SPEC-98's measurement.
4. **Verify row widths** — row A width `== COLUMNS − R`, row B width
   `== COLUMNS − R + 1`; neither ends in whitespace; widths counted as
   **characters, not bytes** (SPEC-98: `awk '{print length}'` counts bytes and
   silently misreports rows containing box-drawing glyphs).
5. **Label suppression** — `COLUMNS < 70` emits no label row; `≥ 70` does, and
   the label is ≤ 60 characters.
6. **Config write preserves unknown keys** — write a config containing keys the
   typed model does not know, run the write, assert they survive verbatim.
7. **Refuses to write an unparseable config** (§6.4) and **refuses to write on
   `--no-ellipsis`** (§6.5).
8. **Expiry** — an expired state file yields normal statusline output.
9. **Absent state file** — hook output byte-identical to a run with no
   calibration; existing golden tests are the primary guard here.
10. **Phase preconditions** — `--saw` without `phase=ruler`, `--confirm` without
    `phase=verify`, `--saw` before the hook has recorded `observedColumns`, and
    any sub-flag without `--calibrate` all produce usage errors, not writes.

**What these tests cannot do**, and what the test file's own comment must say:
they verify the *arithmetic and the plumbing*, never that the resulting number
is correct. The boundary being measured is outside this process. That is the
whole reason `--calibrate` exists, and it is why E1 below is blocking.

---

## 9. NEEDS-EVIDENCE

**E1 (BLOCKING — implementation is not complete without it).** Does Claude Code
render a probe row verbatim? Specifically: are pure-ASCII digit rows passed
through without markdown interpretation, whitespace collapsing, or re-wrapping;
and does the `─` (U+2500) in the verify rows render single-width?

*Run:* start a calibration, cause a redraw in a live Claude Code session, look
at the statusline.
*Decides:* ruler renders clean → ship as specced. Digits mangled or `─` renders
wrong-width → the probe glyph set must change (fall back to ASCII `-` for the
verify rows, which is the safe choice if there is any doubt) and §3.1/§3.4 need
amending.

This requires a human looking at a real pane. **Do not attempt to satisfy it
from inside the process** — that is SPEC-98's withdrawn E2 and it is
unrunnable. Route it as a smoke observation with Jim watching.

**E2 (non-blocking).** Is the chrome reserve constant in columns across terminal
widths, or does it scale? SPEC-98 assumed constant (a symmetric two-column
margin either side) and measured at exactly one width.

*Run:* calibrate at two clearly different widths, e.g. `COLUMNS` 90 and 140, and
compare.
*Decides:* same `R` → the constant-column model holds and a scalar
`chromeReserve` is the right shape. Different `R` → a scalar is the wrong shape
and both SPEC-98 and this spec need a follow-up. Worth running once, but not a
blocker for shipping `--calibrate`.

**E3 (out of scope, noted).** SPEC-98 §2.3 left unresolved whether Claude Code
budgets single-row statuslines differently from multi-row ones (H2 vs H3). That
question was made *moot in production* by shipping a uniform reserve, not
answered. This spec calibrates the multi-row case only (§5.3). A single-row
ruler variant would settle H2/H3 cheaply once this machinery exists — but it is
a separate ticket, and adding it here would expand the CLI surface for a
question production no longer depends on.

---

## 10. Decisions made here, and the one that was not mine

**Made:**

- Human-in-the-loop, not automated — §2 makes automation impossible, not merely
  inconvenient.
- A ruler read once, rather than a binary search over candidate reserves. One
  round trip instead of ~3-4, and every round trip is a context switch for a
  human staring at a pane.
- Writes the **user config**, never `DefaultChromeReserve`. The default remains
  the tested fallback for everyone who never calibrates.
- Verify is mandatory (§3.4) — it is the automated form of the two-sided bracket
  that actually closed SPEC-98, and the only guard on two unstated assumptions.
- `--calibrate` rejects `--config` (§6.2) — eliminates a silent-failure class
  rather than handling it.
- Multi-row probes only (§9 E3).

**Was not mine — now answered (Amendment 1):**

Whether calibration should ever prompt on its own initiative. Originally specced
manual-only and flagged for Jim, because it is a product-behaviour call: it
means the tool putting something on the user's statusline that the user did not
ask for.

**Jim's answer: do all three** — manual `--calibrate`, a prompt on first run on a
machine, and a prompt when the Claude Code version changes. Designed in §12.

One number inside that design is still worth his eye rather than mine: the
**prompt window** (§12.3), currently 7 days. It is a one-line change and it
governs how long a user who ignores the prompt keeps seeing it.

---

## 11. Risk for the Implementor

- **The formula has no indent term.** §3.2. If you add one, you have
  reintroduced the bug SPEC-98 nearly shipped.
- **Never end a probe row in whitespace.** §3.5. Claude Code trims before
  measuring, so a whitespace-terminated row measures the wrong quantity —
  this is the exact mechanism that hid the original bug in the `content` pane.
- **Count characters, not bytes**, in every width assertion. §8.4.
- **Take `COLUMNS` from the state file, not the calibrating terminal.** §5.4 —
  and the same rule again for the version, §12.6.
- **Do not deserialize the config to a typed model to write it.** §6.4 — it
  drops unknown keys silently.
- **Do not auto-advance the candidate when verify fails.** §6.6 — a failed
  verify is a broken assumption, and stepping ±1 hides the signal.
- The quiet hook path must stay cheap and must not perturb rendered rows. §12.5.

---

# 12. Amendment 1 — the auto-prompt

Adds: prompt on first run on a machine; prompt when the Claude Code version
changes. Replaces §5.2.

## 12.1 The version IS available — a grep of `StatusInput` is misleading

`StatusInput` (`src/ClaudeTuiLine/StatusInput.cs:5`) models 13 top-level
properties and **none of them is a version**, so the obvious search says Claude
Code sends no version and this feature is impossible.

**That conclusion is wrong.** A real captured payload in this repo,
`tests/ClaudeTuiLine.Tests/fixtures/real_captured_workspace.json`, contains:

```json
{
  "cwd": "…",
  "workspace": { "current_dir": "…", "project_dir": "…", "added_dirs": [], "repo": {…} },
  "version": "2.1.233"
}
```

Claude Code sends a **top-level `version`**. This tool has simply never modelled
it — exactly as it has never modelled `workspace.current_dir`,
`workspace.project_dir`, or `workspace.added_dirs`, all also present in that
capture and all also discarded.

The absence of a field from `StatusInput` is evidence about *this tool*, not
about Claude Code. Do not treat the model as a description of the payload.

**Change required:** add to `StatusInput`

```csharp
[JsonPropertyName("version")] public string? Version { get; set; }
```

Nullable, and every consumer must treat `null` as **"version unknown"**, never as
a version value. `null` must never compare equal to a recorded version, and must
never be written into the record as if it were one.

## 12.2 The durable record — a second file, deliberately not merged

Two files, two lifetimes:

| file | lifetime | purpose |
| --- | --- | --- |
| `claude-tui-line.calibration.json` (§6.1) | transient, 30-min expiry | a calibration procedure in progress |
| `claude-tui-line.calibration-record.json` | **durable, no expiry** | what we have calibrated, prompted for, and been dismissed on |

**Do not merge them.** They fail differently: a stale or corrupt transient file
should cost you nothing but a re-run, whereas losing the record resurrects the
first-run prompt and forgets a dismissal. Merging means one file's failure mode
becomes the other's.

Record lives **beside the config**, per §6.1's directory — **not** in
`ItemCache.ResolveCacheDir()`. The cache directory is disposable by design
(`~/.cache/claude-tui-line/items`, or `$TMPDIR` when `HOME` is unset); clearing
a cache must not re-nag the user or lose a dismissal. Durable state belongs with
the config.

```json
{
  "calibratedVersion": "2.1.233",
  "calibratedReserve": 4,
  "promptedForVersion": "2.1.240",
  "promptFirstSeen": "2026-08-20T18:00:00Z",
  "dismissedVersion": "2.1.240"
}
```

Every field is optional. An **absent file means "first run on this machine"** —
that is the entire first-run detection mechanism, and it needs no separate
marker.

## 12.3 When the prompt shows

Evaluate in this order; the first rule that fires decides.

1. `layout.calibrationPrompt == false`, or `CLAUDE_TUI_LINE_NO_NUDGE` is set →
   **no prompt**, and **no record file is read or written** (§12.7).
2. A calibration is in progress (§6.1 state present and unexpired) → **no
   prompt.** The user is already doing the thing; nagging them to start it is
   absurd.
3. Record absent → **prompt** (first run).
4. `version` is `null` → **no prompt** beyond rule 3. Unknown version cannot
   establish a change. Never guess, and never treat `null` as a new version.
5. `version == calibratedVersion` → **no prompt.** Already calibrated here.
6. `version == dismissedVersion` → **no prompt.** Declined for this version.
7. `promptFirstSeen` is more than the **prompt window** (7 days) ago → **no
   prompt.** Ignoring it for a week is a decline.
8. Otherwise → **prompt.**

Rule 7 is the safety net that makes this bounded rather than perpetual: even a
user who never dismisses and never calibrates stops seeing it. The 7-day figure
is the tunable §10 flags for Jim.

**Record writes are rare, not per-render.** The hook writes the record only when
`promptedForVersion != version` — i.e. on the first render after a version
change, or the first render ever. Every subsequent render reads and writes
nothing new. Two panes redrawing simultaneously can race that write; the atomic
writer of §6.3 makes the loser harmless, since both write the same content.

## 12.4 How the prompt renders: one appended row

**The hook has exactly one output channel.** SPEC-98 §9.2.1 states it directly —
"the render path's only output channel is this one row". There is no
notification side-channel, no stderr the user sees, no toast. So a prompt is
necessarily *some* intrusion into the statusline, and the only question is how
much.

- **Replacing** the statusline (what the ruler phase does) is right for a
  procedure the user explicitly started, and wrong for an unsolicited nudge.
- **Appending one row below** the rendered statusline is the least destructive
  option: the user's configured surface renders intact and *unshifted*, and the
  block simply grows by one line.

**Decision: append exactly one row, after the normal render.** Rejected
alternatives, with reasons, so they are not re-litigated: prepending (shifts the
user's whole layout up by a row); replacing (hostile for something unrequested);
stderr (unverifiable, and likely surfaced as an error).

The nudge row:

- is **plain unstyled ASCII**, no ANSI and no Spectre markup. Since it is
  appended after the pipeline rather than through it, hand-written escapes would
  be measured by nothing — and that is where width bugs come from.
- is **≤ 48 characters**, suggested text
  `calibrate widths: claude-tui-line --calibrate`.
- is **suppressed entirely when the surface width is < 50** rather than
  truncated.

That length cap is not cosmetic. **The nudge exists because the reserve may be
wrong, so it can be truncated by the very condition it is reporting.** Keeping it
far shorter than any plausible miscalibration is what stops it from failing
exactly when it is needed. A truncated nudge is worse than no nudge.

**Insertion point:** unlike the §5.1 probe branch, this is *late* — after rows
have been produced by the pipeline and immediately before they are written to
stdout. The two are at opposite ends of `RunAsync` and must not be conflated.
They are also mutually exclusive by rule 2 of §12.3.

## 12.5 Revised invariants — replaces §5.2

The original "byte-identical when no state file" is too weak now. It is replaced
by **two** invariants, and the pair is stronger than the one it replaces:

1. **Quiet path.** No calibration state and no prompt → rendered rows are
   **byte-identical to today's**. Existing golden tests are the guard, and they
   must run with the prompt disabled via `CLAUDE_TUI_LINE_NO_NUDGE` (§12.7) so
   they keep testing what they were written to test.
2. **Prompt path.** Prompt active → output equals the quiet-path output **plus
   exactly one appended row**. Every pre-existing row must be byte-identical.

Invariant 2 is the one that matters and it is directly testable: *the nudge may
not perturb any row of the user's statusline.* A prompt that changes an existing
row is a bug by definition, not a judgement call.

**Cost, stated honestly rather than claimed to be free:**

- The §5.1 probe check stays one `File.Exists`, on a path that is normally
  absent.
- The prompt check is gated on the config toggle first, which is already in
  memory — so an opted-out user pays **nothing**.
- For everyone else it is one small JSON read per render. The hook already
  reads and parses the config on every render, so this is the same class of cost,
  not a new one. Do not pretend otherwise in comments.

## 12.6 Dismissal, and the version the CLI cannot know

`--calibrate --dismiss` records that the user declined for the current version.
`--calibrate --confirm` (§6) implicitly dismisses by setting `calibratedVersion`.

**The CLI does not know the Claude Code version.** It arrives on stdin, to the
*hook*, in a different process. A terminal invocation of `--dismiss` has no
payload and no way to obtain it.

Therefore: **`--dismiss` copies `promptedForVersion` from the record into
`dismissedVersion`.** It must never attempt to discover the version itself —
not from an environment variable, not by shelling out to `claude --version`, not
from a PATH lookup. This is §5.4's rule applied a second time, and it is the same
failure mode: a value that differs between the hook's context and the terminal's
is a silent wrong answer.

If the record has no `promptedForVersion`, `--dismiss` reports that there is
nothing to dismiss and writes nothing.

## 12.7 Opt-out

Two mechanisms, different audiences:

- **`layout.calibrationPrompt`** (bool, default `true`) — the user-facing
  setting. Placed under `layout` beside `chromeReserve` so everything
  calibration-related is in the one place a user will look for it, even though
  a prompt is arguably not itself a layout concern.
- **`CLAUDE_TUI_LINE_NO_NUDGE`** — env var, for golden tests, CI, screenshots,
  and demos. Set (to anything non-empty) suppresses the prompt.

Either one short-circuits at rule 1 of §12.3, before any record file is touched.

## 12.8 Blast radius on the release that ships this

Every existing user has no record file, so **rule 3 fires for all of them**:
everyone sees the prompt once, for up to the 7-day window, on upgrade.

This is deliberate and correct — a user calibrated against an old Claude Code is
precisely who the feature is for — but it is a user-visible change to every
installation, and it should be a release-note line, not a surprise. It is also
the reason the window length (§12.3 rule 7) is worth Jim's eye.

## 12.9 Verification — new `CalibrationPromptTests.cs`

1. **`Version` round-trips** — deserialize
   `tests/ClaudeTuiLine.Tests/fixtures/real_captured_workspace.json` and assert
   `Version == "2.1.233"`. Use the real capture, not a synthetic payload; the
   whole point is that the real shape carries a field the synthetic one does not.
2. **Trigger table** — one case per rule in §12.3, each asserting prompt/no-prompt.
   Include `version == null` explicitly (rule 4) and assert `null` never matches
   a recorded version.
3. **Invariant 1** — no state, prompt disabled → rows byte-identical to the
   existing goldens.
4. **Invariant 2** — prompt active → row count is exactly `quiet + 1`, and rows
   `[0..n-1]` are byte-identical to the quiet render. Assert this **row by row**,
   not on the joined string; a joined comparison can hide a perturbation that
   another row's change compensates for.
5. **Nudge width** — ≤ 48 characters, counted as characters not bytes; suppressed
   below surface width 50; does not end in whitespace (§3.5 applies to it too).
6. **Record write frequency** — two consecutive renders at the same version
   produce exactly one record write, not two.
7. **`--dismiss`** — copies `promptedForVersion`; suppresses on the next render;
   a *later* version re-prompts; with no `promptedForVersion` it writes nothing.
8. **Prompt window** — a `promptFirstSeen` older than the window suppresses the
   prompt (rule 7).
9. **Cache dir independence** — deleting `ItemCache.ResolveCacheDir()` does not
   resurrect the prompt or lose a dismissal (§12.2).
10. **A calibration in progress suppresses the prompt** (rule 2).

## 12.10 NEEDS-EVIDENCE for the auto-prompt

**E4 (BLOCKING for the auto-prompt half only — §§1–11 can ship without it).**
Is `version` present in **every** real payload, or only some? The finding in
§12.1 rests on **one** captured fixture. SPEC-98's E4 captured **9 real payloads
across two payload shapes**; that capture set can answer this directly, and
whoever holds it should grep it rather than re-capturing.

*Decides:* present in all → §12.3's rules 4–6 work as written. Present in only
some → rule 4 (`null` = unknown) already handles it safely, but the prompt will
be flaky across shapes and §12.3 needs a note saying so. Absent from the shape
Jim's setup actually sends → the version-change half does not function for him
and this must go back to Jim before implementation, because he asked for it
specifically.

Note the asymmetry: the design is **already safe** under every outcome, because
rule 4 refuses to guess. E4 decides whether the feature *works*, not whether it
is *safe*.

**E5 (non-blocking).** Does Claude Code cap the number of statusline rows it
renders? SPEC-98 observed 6 rows rendering fine, so the cap is ≥ 6 if it exists.
The appended nudge row takes a user from N to N+1.

*Run:* with the prompt active on an already-tall statusline, confirm the nudge
row is visible and nothing was dropped off the bottom.
*Decides:* visible → fine. A row is dropped → appending is the wrong mechanism
for tall statuslines and §12.4 needs a height guard (suppress the nudge above
some row count). Fold this into E1's observation session; it costs one extra
glance.
