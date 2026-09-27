// Deterministic ontology term linter — the blocking half of the drift defence.
//
// Enforces, across the repository's Markdown, the exact-name discipline the ontology mandates:
//
//   1. Unknown-term check: a backticked PascalCase identifier that is not defined in the ontology
//      (and not allowlisted) is an error. Catches invented synonyms and misspellings.
//   2. Banned-alias check: prose that paraphrases a named External System instead of naming it is
//      an error, unless the match is quoted — quoting marks a meta-mention, such as the ontology's
//      own "do not say this" instruction.
//
// It cannot catch unbackticked paraphrase drift ("Approved -> Sent -> Paid"). That is the job of
// the optional advisory reviewer in review-ontology-drift.mjs.
//
// Everything repository-specific lives in ontology.config.json. This file is identical in every
// repository that installs the ontology kit; do not edit it locally.
//
// Run: node scripts/check-ontology-terms.mjs [--also <path>...]

import { execFileSync } from "node:child_process";
import { existsSync, readFileSync, statSync } from "node:fs";
import { isAbsolute, join, resolve } from "node:path";
import { pathToFileURL } from "node:url";

import { loadConfig } from "./ontology-config.mjs";

// Requires at least one lowercase letter after the leading capital, so an all-caps token
// (`README`, `PATH`, `API`, `JSON`, `HTTP`, `ID`) does not read as PascalCase. Measured across
// 120 neutral technical Markdown files, 37.5% of distinct backticked-cap matches were all-caps
// tokens, not domain concepts — with the shipped starter config's allowlist empty, that is a wall
// of false positives on an adopter's very first run, which is exactly how a control gets silenced
// before it ever earns trust. The same regex drives both canonical extraction and this check, so
// an all-caps ontology term (`SLA`, `KYC`) simply loses coverage rather than becoming a false
// positive — no inconsistency is introduced. That loss is accepted deliberately: in a linter that
// ships byte-identical to every repository, false positives are more damaging than missed
// detections.
const PASCAL_CASE = /^[A-Z][A-Za-z0-9]*[a-z][A-Za-z0-9]*$/;

// A banned-alias match immediately preceded or followed by one of these is a meta-mention (the
// ontology or a spec quoting the forbidden phrase in order to forbid it) rather than paraphrase
// drift, and is exempt. Covers straight double quotes, both curly-quote directions, and backticks
// — inline code is at least as idiomatic as double quotes for writing a forbidden literal in
// Markdown, and it is the style this kit's own templates use throughout.
//
// Deliberately EXCLUDES straight and curly apostrophes ('/'): those occur as ordinary English
// possessives in prose (e.g. "the billing platform's API"), and treating them as quote markers
// would silently exempt exactly the paraphrase drift this check exists to catch.
const QUOTE_CHARS = new Set(['"', "“", "”", "`"]);

// Matches a fence-open-or-close candidate line for either fence style, allowing up to three
// leading spaces per CommonMark. Group 1 is the run of fence characters (3 or more); group 2 is
// whatever follows on the same line — an info string for an opening fence, or (for a line to
// count as a close) nothing but trailing whitespace.
const FENCE_LINE_RE = /^\s{0,3}(`{3,}|~{3,})(.*)$/;

/**
 * @param {string} line
 * @returns {{ char: string, len: number } | null}
 */
function matchFenceOpen(line) {
  const match = line.match(FENCE_LINE_RE);
  return match ? { char: match[1][0], len: match[1].length } : null;
}

/**
 * CommonMark closes a fence only with a line consisting of (up to three leading spaces, then) a
 * run of the SAME fence character, AT LEAST AS LONG as the run that opened it, followed by nothing
 * but optional trailing whitespace — no info string. A shorter run, the other fence character, or
 * a run followed by anything else is not a close: it is just more fenced content, which is exactly
 * what lets an outer fence safely contain an inner fenced example (e.g. a four-backtick block
 * documenting a three-backtick block) without the inner markers prematurely closing the outer one.
 * @param {string} line
 * @param {string} char
 * @param {number} len
 * @returns {boolean}
 */
function isFenceClose(line, char, len) {
  const match = line.match(FENCE_LINE_RE);
  return !!match && match[1][0] === char && match[1].length >= len && match[2].trim() === "";
}

/**
 * @param {string} text
 * @returns {{ term: string, index: number }[]}
 */
export function backtickedTerms(text) {
  const out = [];
  for (const match of text.matchAll(/`([^`\n]+)`/g)) {
    out.push({ term: match[1], index: match.index });
  }
  return out;
}

/**
 * The canonical vocabulary: every backticked PascalCase term the ontology defines or names.
 * @param {string} ontologyMarkdown
 * @returns {Set<string>}
 */
export function canonicalTerms(ontologyMarkdown) {
  const canonical = new Set(
    backtickedTerms(ontologyMarkdown)
      .map(({ term }) => term)
      .filter((term) => PASCAL_CASE.test(term)),
  );
  if (canonical.size === 0) {
    throw new Error(
      "no canonical terms found in the ontology — it must name its concepts as backticked " +
        "PascalCase terms (for example `Invoice`)",
    );
  }
  return canonical;
}

/**
 * @typedef {{ file: string, line: number, column: number, message: string }} Violation
 */

/**
 * Banned-alias check for a single line. Quoted matches are meta-mentions, so they are allowed —
 * this is what lets the ontology state its own prohibition without failing on it. Unlike
 * `checkUnknownTermsOnLine`, this is called even when `file === ontologyPath`: the ontology's own
 * narrative prose is exactly where a paraphrase is most likely to slip in, so it gets no exemption.
 *
 * Matching is plain case-insensitive substring matching — there is no word-boundary check.
 * Configured phrases are expected to be multi-word paraphrases ("the billing platform"), for which
 * substring matching is exact. A bare short token (e.g. `crm`) will match inside a larger word
 * (`scrmsystem`); the fix is to configure the multi-word phrase people actually write, not to add a
 * boundary check. A boundary check is deliberately NOT applied: it would stop catching plurals
 * (`the billing platforms` is currently caught by design; a trailing `\b` would lose it), and that
 * loss would be unfixable from the config, unlike the misconfiguration a boundary check would
 * guard against.
 *
 * @param {{ line: string, lineNo: number, file: string,
 *           config: { bannedAliases: {phrase: string, nameInstead: string[]}[] },
 *           ontologyPath: string }} input
 * @returns {Violation[]}
 */
function checkBannedAliasesOnLine({ line, lineNo, file, config, ontologyPath }) {
  /** @type {Violation[]} */
  const violations = [];

  // Deliberately NOT de-duplicated per line (contrast the unknown-term check's `seenOnLine`):
  // the spec requires every occurrence to be reported, because each occurrence is a separate
  // piece of prose someone has to rewrite, whereas a repeated unknown term is one fix. Do not
  // "fix" this into a dedup to match the other check — that would be a regression.
  const lower = line.toLowerCase();
  for (const alias of config.bannedAliases) {
    const phrase = alias.phrase.toLowerCase();
    let from = 0;
    for (;;) {
      const at = lower.indexOf(phrase, from);
      if (at === -1) break;
      from = at + phrase.length;
      // Read both neighbours from `lower`, not `line`: every member of QUOTE_CHARS is
      // case-invariant, so this is equivalent, and it avoids a desync. `line.toLowerCase()` can
      // change string length (e.g. "İ".toLowerCase() is two UTF-16 code units), which would shift
      // every index taken from `lower` relative to `line` and misalign this lookup.
      const before = lower[at - 1] ?? "";
      const after = lower[at + phrase.length] ?? "";
      if (QUOTE_CHARS.has(before) || QUOTE_CHARS.has(after)) continue;
      const suggestion = alias.nameInstead.length
        ? `name ${alias.nameInstead.map((name) => `\`${name}\``).join(" or ")} instead`
        : `name the External System from ${ontologyPath} instead`;
      violations.push({
        file,
        line: lineNo,
        column: at + 1,
        // Echoes the configured phrase's casing (`alias.phrase`), not the text the user actually
        // wrote — this ties the violation back to the entry in ontology.config.json, and slicing
        // the matched text out of `line` instead would reintroduce the same index-desync trap the
        // comment above just fixed.
        message: `"${alias.phrase}" is a paraphrase of an External System named in ${ontologyPath} — ${suggestion}`,
      });
    }
  }
  return violations;
}

/**
 * Unknown-term check for a single line. One violation per (line, term): the same unknown term
 * repeated on one line is one fix to make, not several identical, triple-counted messages.
 *
 * @param {{ line: string, lineNo: number, file: string, canonical: Set<string>,
 *           allowlist: Set<string>, canonicalByLowerCase: Map<string, string>,
 *           ontologyPath: string }} input
 * @returns {Violation[]}
 */
function checkUnknownTermsOnLine({ line, lineNo, file, canonical, allowlist, canonicalByLowerCase, ontologyPath }) {
  /** @type {Violation[]} */
  const violations = [];
  const seenOnLine = new Set();
  for (const { term, index } of backtickedTerms(line)) {
    if (!PASCAL_CASE.test(term) || canonical.has(term) || allowlist.has(term)) continue;
    if (seenOnLine.has(term)) continue;
    seenOnLine.add(term);

    let message =
      `\`${term}\` is not defined in ${ontologyPath} — ` +
      `use the canonical name, add the concept to the ontology first, ` +
      `or (if it is not a domain concept) add it to the allowlist in ontology.config.json ` +
      `with a reason`;

    // Case-insensitive-only hint: the most common real drift is a capitalisation slip
    // (`SentToVENDOR` vs. `SentToVendor`), and naming the canonical spelling turns a lookup into a
    // one-word edit. Deliberately not fuzzy/edit-distance matching — that was considered and
    // rejected as scope creep for a file that ships everywhere.
    const canonicalMatch = canonicalByLowerCase.get(term.toLowerCase());
    if (canonicalMatch) {
      message += ` (did you mean \`${canonicalMatch}\`? — same term, different capitalisation)`;
    }

    violations.push({ file, line: lineNo, column: index + 1, message });
  }
  return violations;
}

/**
 * @param {{ file: string, text: string, canonical: Set<string>,
 *           config: { allowlist: {term: string}[], bannedAliases: {phrase: string, nameInstead: string[]}[] },
 *           ontologyPath: string }} input
 * @returns {Violation[]}
 */
export function lintFile({ file, text, canonical, config, ontologyPath }) {
  const allowlist = new Set(config.allowlist.map((entry) => entry.term));
  const canonicalByLowerCase = new Map();
  for (const term of canonical) {
    canonicalByLowerCase.set(term.toLowerCase(), term);
  }

  /** @type {Violation[]} */
  const violations = [];
  // Tracks whether the line under inspection is inside a fenced code block, and if so, which
  // character opened it and how long that opening run was — both needed to recognise the matching
  // close (see isFenceClose) rather than any line that merely looks like a fence marker. Declared
  // here, not inside the loop body below, so a banned-alias check appended to this same forEach
  // can read it too and skip fenced content the same way.
  let inFence = false;
  let fenceChar = null;
  let fenceLen = 0;

  text.split("\n").forEach((line, index) => {
    const lineNo = index + 1;

    // Fenced code blocks are excluded from both checks: a JS/TS template literal in a documented
    // example (`` `Ready` `` inside ```js ... ```) and a nested Markdown example that itself
    // demonstrates ontology prose (`` `LegacyBill` `` inside ```md ... ```) are both real,
    // legitimate content, not a domain-term or banned-alias violation.
    if (inFence) {
      if (isFenceClose(line, fenceChar, fenceLen)) {
        inFence = false;
        fenceChar = null;
        fenceLen = 0;
      }
      return;
    }
    const opening = matchFenceOpen(line);
    if (opening) {
      inFence = true;
      fenceChar = opening.char;
      fenceLen = opening.len;
      return;
    }

    violations.push(...checkBannedAliasesOnLine({ line, lineNo, file, config, ontologyPath }));

    if (file === ontologyPath) return;

    violations.push(
      ...checkUnknownTermsOnLine({ line, lineNo, file, canonical, allowlist, canonicalByLowerCase, ontologyPath }),
    );
  });

  return violations;
}

/**
 * Minimal glob support for ignorePaths: `*` within a path segment, `**` across segments.
 * Everything else is matched literally, so a `.` or `+` in a filename cannot become a wildcard.
 * @param {string} glob
 * @returns {RegExp}
 */
export function globToRegExp(glob) {
  let pattern = "";
  for (let index = 0; index < glob.length; index++) {
    const char = glob[index];
    if (char === "*") {
      if (glob[index + 1] === "*") {
        pattern += ".*";
        index++;
      } else {
        pattern += "[^/]*";
      }
    } else if (char === "?") {
      pattern += "[^/]";
    } else {
      pattern += char.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
    }
  }
  return new RegExp(`^${pattern}$`);
}

/**
 * @param {string[]} argv
 * @returns {{ alsoPaths: string[] }}
 */
export function parseArgs(argv) {
  const alsoPaths = [];
  let collecting = false;
  for (const arg of argv) {
    if (arg === "--also") {
      collecting = true;
      continue;
    }
    if (arg.startsWith("--")) {
      throw new Error(`unknown argument "${arg}" — usage: check-ontology-terms.mjs [--also <path>...]`);
    }
    if (!collecting) {
      throw new Error(`unexpected argument "${arg}" — paths must follow --also`);
    }
    alsoPaths.push(arg);
  }
  return { alsoPaths };
}

/**
 * Normalises a repository-relative path the same way ontology-config.mjs normalises
 * `ontologyPath`: strip a leading `./` and convert backslashes to forward slashes. `--also`
 * paths arrive on the command line exactly as the user typed them (e.g. `./docs/ontology.md`),
 * while `git ls-files` output is already in this normalised form — without this, a normalised
 * and an unnormalised spelling of the same file would be treated as two different files.
 * @param {string} path
 * @returns {string}
 */
function normalisePath(path) {
  return path.replace(/\\/g, "/").replace(/^(?:\.\/)+/, "");
}

/**
 * Tracked Markdown, minus ignorePaths, plus any explicitly supplied --also paths.
 * @param {{ cwd?: string, ignorePaths: string[], alsoPaths: string[] }} options
 * @returns {string[]}
 */
export function discoverFiles({ cwd = process.cwd(), ignorePaths, alsoPaths }) {
  const tracked = execFileSync("git", ["-C", cwd, "ls-files", "*.md"], {
    encoding: "utf8",
    maxBuffer: 10 * 1024 * 1024,
  })
    .split("\n")
    .filter(Boolean);

  const ignored = ignorePaths.map(globToRegExp);
  const files = tracked.filter((file) => !ignored.some((pattern) => pattern.test(file)));

  for (const rawPath of alsoPaths) {
    const path = normalisePath(rawPath);
    // resolve, not join: an absolute --also path is the documented case (the agent protocol tells
    // people to lint a drafted PR body held in a scratch file outside the repository), and join
    // would concatenate it onto cwd and then report the real file as missing. A path outside the
    // working tree is deliberately allowed for exactly that reason.
    const absolute = resolve(cwd, path);
    if (!existsSync(absolute)) {
      throw new Error(`--also path "${path}" does not exist`);
    }
    // Reject a directory here rather than letting the caller's readFileSync fail with a bare
    // EISDIR further along, where nothing names the argument that caused it.
    if (statSync(absolute).isDirectory()) {
      throw new Error(`--also path "${path}" is a directory — pass the Markdown file itself`);
    }
    if (!files.includes(path)) files.push(path);
  }
  return files;
}

/**
 * @param {{ cwd?: string, argv?: string[] }} [options]
 * @returns {{ violations: Violation[], filesChecked: number, canonicalCount: number }}
 */
export function run(options = {}) {
  const cwd = options.cwd ?? process.cwd();
  const { alsoPaths } = parseArgs(options.argv ?? []);
  const config = loadConfig({ cwd });
  const canonical = canonicalTerms(readFileSync(resolve(cwd, config.ontologyPath), "utf8"));
  const files = discoverFiles({ cwd, ignorePaths: config.ignorePaths, alsoPaths });

  /** @type {Violation[]} */
  const violations = [];
  for (const file of files) {
    violations.push(
      ...lintFile({
        file,
        text: readFileSync(resolve(cwd, file), "utf8"),
        canonical,
        config,
        ontologyPath: config.ontologyPath,
      }),
    );
  }
  return { violations, filesChecked: files.length, canonicalCount: canonical.size };
}

const HELP =
  "usage: node scripts/check-ontology-terms.mjs [--also <path>...]\n" +
  "\n" +
  "Checks this repository's tracked Markdown against docs/ontology.md (or wherever\n" +
  "ontology.config.json's ontologyPath points): a backticked PascalCase term not defined there is\n" +
  "an error, and prose paraphrasing a named External System instead of naming it is an error.\n" +
  "\n" +
  "  --also <path>...  Also lint these paths — untracked files, such as a drafted pull-request\n" +
  "                     body saved to a scratch file, that git ls-files would not otherwise see.\n" +
  "                     Accepts one or more paths, absolute or repository-relative.\n" +
  "  --help             Print this message and exit 0.\n" +
  "\n" +
  "Exit codes: 0 clean, 1 violations found, 2 the check could not run (e.g. ontology.config.json\n" +
  "is missing or invalid) — kept distinct from 1 so \"the config is broken\" never looks like\n" +
  "\"the prose is wrong\".\n";

// Exit codes: 0 clean, 1 violations found, 2 the check could not run (config or parse failure).
// A build red for "the config is broken" is a different problem from one red for "the prose is
// wrong", and conflating them wastes the reader's time.
if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  const argv = process.argv.slice(2);
  if (argv.includes("--help") || argv.includes("-h")) {
    console.log(HELP);
  } else {
    try {
      const { violations, filesChecked, canonicalCount } = run({ argv });
      if (violations.length > 0) {
        console.error(`ontology term check: ${violations.length} violation(s)\n`);
        for (const violation of violations) {
          console.error(`  ${violation.file}:${violation.line}:${violation.column}  ${violation.message}`);
        }
        process.exit(1);
      }
      console.log(
        `ontology term check: OK — ${filesChecked} files checked against ${canonicalCount} canonical terms`,
      );
    } catch (error) {
      console.error(`ontology term check could not run: ${error instanceof Error ? error.message : error}`);
      process.exit(2);
    }
  }
}
