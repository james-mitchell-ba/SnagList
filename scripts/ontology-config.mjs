// Loads and validates ontology.config.json — the only per-repository file in the ontology kit.
// Both installed scripts read their repository-specific values through here, which is what lets
// check-ontology-terms.mjs and review-ontology-drift.mjs stay byte-identical across repositories.
//
// Validation is deliberately strict. A silently-ignored typo in this file is a silently weakened
// linter, so an unknown key is an error, and every allowlist entry must carry a written reason.
// The same reasoning is why `??` is never used to fall back on a present-but-`null` field: `null`
// is a value the author wrote, not an absent one, and coalescing it into a default would validate
// a config that looks configured while quietly disabling the check it configures.

import { existsSync, readFileSync } from "node:fs";
import { dirname, isAbsolute, join } from "node:path";

export const CONFIG_FILENAME = "ontology.config.json";

const KNOWN_KEYS = new Set([
  "$schema",
  "ontologyPath",
  "namespaceUri",
  "sections",
  "allowlist",
  "bannedAliases",
  "ignorePaths",
]);

const DEFAULT_NAMESPACE_URI = "https://ontology.example/ontology#";

const WINDOWS_DRIVE_RE = /^[A-Za-z]:[\\/]/;

const KNOWN_SECTION_KEYS = new Set(["required", "optional"]);

/**
 * @typedef {{ term: string, reason: string }} AllowlistEntry
 * @typedef {{ phrase: string, nameInstead: string[] }} BannedAlias
 * @typedef {{ ontologyPath: string, namespaceUri: string, sections: { required: string[], optional: string[] },
 *             allowlist: AllowlistEntry[], bannedAliases: BannedAlias[], ignorePaths: string[] }} OntologyConfig
 */

/**
 * Normalise a path the same way `ontologyPath` must be normalised (backslashes to forward
 * slashes, a leading "./" stripped), then reject it unless it is repository-relative with no
 * ".." segment. Shared by `validateConfig` (for `ontologyPath` read from `ontology.config.json`)
 * and the installer (for `--ontology-path`, before anything is written), so both enforce exactly
 * the same rule from one place rather than two copies drifting apart.
 *
 * Normalise before validating, not after: a Windows-authored "\docs\ontology.md" becomes
 * "/docs/ontology.md", so checking absoluteness first would pass a path that normalisation then
 * turns absolute.
 *
 * @param {string} rawPath
 * @param {string} label - what this path is called in the error message, e.g.
 *   `"ontology.config.json: ontologyPath"` or `"--ontology-path"`.
 * @returns {string}
 */
export function normaliseRepoRelativePath(rawPath, label) {
  const path = rawPath.trim().replace(/\\/g, "/").replace(/^(?:\.\/)+/, "");
  if (isAbsolute(path) || path.startsWith("/") || WINDOWS_DRIVE_RE.test(path)) {
    throw new Error(`${label} must be repository-relative, not absolute: "${rawPath}"`);
  }
  if (path.split("/").includes("..")) {
    throw new Error(`${label} must not contain ".." segments: "${rawPath}"`);
  }
  return path;
}

function requireStringArray(value, label) {
  if (value === undefined) return [];
  if (!Array.isArray(value) || value.some((item) => typeof item !== "string" || !item.trim())) {
    throw new Error(`${CONFIG_FILENAME}: ${label} must be an array of non-empty strings`);
  }
  return value.map((item) => item.trim());
}

function requireEntryArray(value, label) {
  if (value === undefined) return [];
  if (!Array.isArray(value)) {
    throw new Error(`${CONFIG_FILENAME}: ${label} must be an array`);
  }
  return value;
}

/**
 * @param {unknown} raw
 * @param {{ ontologyExists?: (path: string) => boolean }} [deps]
 * @returns {OntologyConfig}
 */
export function validateConfig(raw, deps = {}) {
  const ontologyExists = deps.ontologyExists ?? existsSync;
  if (raw === null || typeof raw !== "object" || Array.isArray(raw)) {
    throw new Error(`${CONFIG_FILENAME}: expected a JSON object`);
  }
  const input = /** @type {Record<string, any>} */ (raw);

  for (const key of Object.keys(input)) {
    if (!KNOWN_KEYS.has(key)) {
      throw new Error(
        `${CONFIG_FILENAME}: unknown key "${key}" — expected one of ${[...KNOWN_KEYS].join(", ")}`,
      );
    }
  }

  if (typeof input.ontologyPath !== "string" || !input.ontologyPath.trim()) {
    throw new Error(`${CONFIG_FILENAME}: ontologyPath is required and must be a non-empty string`);
  }
  // The linter's `file !== ontologyPath` self-exclusion depends on this normalisation happening
  // before the absolute/".." checks below — see normaliseRepoRelativePath's own comment.
  const ontologyPath = normaliseRepoRelativePath(input.ontologyPath, `${CONFIG_FILENAME}: ontologyPath`);
  if (!ontologyExists(ontologyPath)) {
    throw new Error(
      `${CONFIG_FILENAME}: ontologyPath "${ontologyPath}" does not exist — ` +
        `create the ontology file or correct the path`,
    );
  }

  const sections = input.sections === undefined ? {} : input.sections;
  if (sections === null || typeof sections !== "object" || Array.isArray(sections)) {
    throw new Error(`${CONFIG_FILENAME}: sections must be an object`);
  }
  // A typo here (e.g. "optionl" for "optional") would otherwise be silently ignored the same way
  // an unknown top-level key would be — except its effect is worse: `sections.required` and
  // `sections.optional` would both quietly resolve to `[]`, projectOntology would then reject the
  // resulting empty section list (see review-ontology-drift.mjs), but only at the point something
  // actually calls it — a core install that never reads `sections` would never notice at all. Catch
  // the typo here, at the same place every other unknown key is caught.
  for (const key of Object.keys(sections)) {
    if (!KNOWN_SECTION_KEYS.has(key)) {
      throw new Error(
        `${CONFIG_FILENAME}: unknown key "sections.${key}" — expected one of ${[...KNOWN_SECTION_KEYS].join(", ")}`,
      );
    }
  }

  const allowlist = requireEntryArray(input.allowlist, "allowlist").map((entry, index) => {
    if (entry === null || typeof entry !== "object" || Array.isArray(entry)) {
      throw new Error(`${CONFIG_FILENAME}: allowlist entry ${index} must be an object`);
    }
    if (typeof entry.term !== "string" || !entry.term.trim()) {
      throw new Error(`${CONFIG_FILENAME}: allowlist entry ${index} has no term`);
    }
    const term = entry.term.trim();
    if (typeof entry.reason !== "string" || !entry.reason.trim()) {
      throw new Error(
        `${CONFIG_FILENAME}: allowlist entry "${term}" has no reason — ` +
          `every allowlisted term must say why it is not a domain concept`,
      );
    }
    return { term, reason: entry.reason.trim() };
  });

  const bannedAliases = requireEntryArray(input.bannedAliases, "bannedAliases").map((entry, index) => {
    if (entry === null || typeof entry !== "object" || Array.isArray(entry)) {
      throw new Error(`${CONFIG_FILENAME}: bannedAliases entry ${index} must be an object`);
    }
    if (typeof entry.phrase !== "string" || !entry.phrase.trim()) {
      throw new Error(`${CONFIG_FILENAME}: bannedAliases entry ${index} has no phrase`);
    }
    const phrase = entry.phrase.trim();
    return {
      phrase,
      nameInstead: requireStringArray(entry.nameInstead, `bannedAliases["${phrase}"].nameInstead`),
    };
  });

  let namespaceUri = DEFAULT_NAMESPACE_URI;
  if (input.namespaceUri !== undefined) {
    if (typeof input.namespaceUri !== "string" || !input.namespaceUri.trim()) {
      throw new Error(`${CONFIG_FILENAME}: namespaceUri must be a non-empty string`);
    }
    namespaceUri = input.namespaceUri.trim();
  }

  return {
    ontologyPath,
    namespaceUri,
    sections: {
      required: requireStringArray(sections.required, "sections.required"),
      optional: requireStringArray(sections.optional, "sections.optional"),
    },
    allowlist,
    bannedAliases,
    ignorePaths: requireStringArray(input.ignorePaths, "ignorePaths"),
  };
}

/**
 * @param {{ cwd?: string, configPath?: string }} [options]
 * @returns {OntologyConfig}
 */
export function loadConfig(options = {}) {
  const configPath = options.configPath ?? join(options.cwd ?? process.cwd(), CONFIG_FILENAME);
  // The ontology path is repo-relative, so it must resolve against the repository root. `cwd`
  // is that root when given; otherwise fall back to the directory the config itself lives in
  // (not process.cwd()) so that pointing configPath at a config elsewhere on disk still resolves
  // ontologyPath next to it, rather than against whatever directory this process happens to run from.
  const baseDir = options.cwd ?? (options.configPath ? dirname(options.configPath) : process.cwd());
  if (!existsSync(configPath)) {
    throw new Error(
      `${CONFIG_FILENAME} not found at ${configPath} — run the ontology kit installer, ` +
        `or run this script from the repository root`,
    );
  }
  const text = readFileSync(configPath, "utf8").replace(/^\uFEFF/, "");
  let parsed;
  try {
    parsed = JSON.parse(text);
  } catch (error) {
    throw new Error(
      `${configPath} is not valid JSON: ${error instanceof Error ? error.message : error}`,
    );
  }
  return validateConfig(parsed, {
    ontologyExists: (path) => existsSync(isAbsolute(path) ? path : join(baseDir, path)),
  });
}
