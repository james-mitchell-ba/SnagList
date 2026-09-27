#!/usr/bin/env node
// Ontology build tool. ontology.ttl is the source of truth; ontology.md is generated from it.
// Installed by the ontology kit — this file is identical in every repository, like
// check-ontology-terms.mjs.
//
// Usage:
//   node scripts/build-ontology.mjs           migrate (only if ontology.ttl does not exist yet),
//                                              then regenerate ontology.md if it is stale
//   node scripts/build-ontology.mjs --force    regenerate ontology.md unconditionally
//   node scripts/build-ontology.mjs --check    report staleness without writing (what CI runs)
//   node scripts/build-ontology.mjs --help

import { existsSync, readFileSync, writeFileSync } from "node:fs";
import { resolve } from "node:path";
import { pathToFileURL } from "node:url";

import { loadConfig } from "./ontology-config.mjs";
import {
  modelToTriples,
  parseOntologyMarkdown,
  parseTurtle,
  renderOntologyMarkdown,
  serializeTurtle,
  triplesToModel,
} from "./owl-ontology.mjs";

/** @param {string} ontologyPath */
export function ttlPathFor(ontologyPath) {
  return ontologyPath.replace(/\.md$/, ".ttl");
}

/**
 * @param {{ cwd?: string, check?: boolean, force?: boolean }} [options]
 * @returns {{ migrated: boolean, changed: boolean, mdPath: string, ttlPath: string, rendered?: string }}
 */
export function run(options = {}) {
  const cwd = options.cwd ?? process.cwd();
  const config = loadConfig({ cwd });
  const relativeTtlPath = ttlPathFor(config.ontologyPath);
  const mdPath = resolve(cwd, config.ontologyPath);
  const ttlPath = resolve(cwd, relativeTtlPath);

  let migrated = false;
  if (!existsSync(ttlPath)) {
    const model = parseOntologyMarkdown(readFileSync(mdPath, "utf8"));
    const triples = modelToTriples(model);
    writeFileSync(ttlPath, serializeTurtle(triples, { namespaceUri: config.namespaceUri }));
    migrated = true;
  }

  const triples = parseTurtle(readFileSync(ttlPath, "utf8"));
  const rendered = renderOntologyMarkdown(triplesToModel(triples));
  const existing = existsSync(mdPath) ? readFileSync(mdPath, "utf8") : null;
  const changed = existing !== rendered;

  if (!options.check && (changed || options.force)) {
    writeFileSync(mdPath, rendered);
  }

  return { migrated, changed, mdPath: config.ontologyPath, ttlPath: relativeTtlPath, rendered };
}

/** @param {string[]} argv */
export function parseCliArgs(argv) {
  if (argv.includes("--help") || argv.includes("-h")) return { help: true };
  const check = argv.includes("--check");
  const force = argv.includes("--force");
  if (check && force) {
    throw new Error("--check and --force are contradictory — --check never writes, --force always writes");
  }
  const known = new Set(["--check", "--force", "--help", "-h"]);
  for (const arg of argv) {
    if (arg.startsWith("--") && !known.has(arg)) {
      throw new Error(`unknown argument "${arg}" — usage: build-ontology.mjs [--check | --force]`);
    }
  }
  return { check, force };
}

const HELP =
  "usage: node scripts/build-ontology.mjs [--check | --force]\n" +
  "\n" +
  "ontology.ttl is the source of truth; ontology.md is generated from it.\n" +
  "\n" +
  "  (no flags)  Migrate ontology.md into ontology.ttl if ontology.ttl does not exist yet, then\n" +
  "              regenerate ontology.md from ontology.ttl if it is out of date.\n" +
  "  --force     Regenerate ontology.md from ontology.ttl unconditionally.\n" +
  "  --check     Report whether ontology.md is stale relative to ontology.ttl, without writing.\n" +
  "              Exits 1 if stale, printing what the regenerated file would contain. This is what\n" +
  "              CI runs.\n" +
  "  --help      Print this message and exit 0.\n";

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  const argv = process.argv.slice(2);
  try {
    const options = parseCliArgs(argv);
    if (options.help) {
      console.log(HELP);
    } else {
      const result = run(options);
      if (result.migrated) console.log(`ontology build: migrated ${result.mdPath} into ${result.ttlPath}`);
      if (options.check) {
        if (result.changed) {
          console.error(`ontology build: ${result.mdPath} is stale relative to ${result.ttlPath}\n`);
          console.error(result.rendered);
          process.exit(1);
        }
        console.log(`ontology build: ${result.mdPath} is up to date with ${result.ttlPath}`);
      } else if (result.changed || options.force) {
        console.log(`ontology build: regenerated ${result.mdPath} from ${result.ttlPath}`);
      } else {
        console.log(`ontology build: ${result.mdPath} already matches ${result.ttlPath}`);
      }
    }
  } catch (error) {
    console.error(`ontology build: ${error instanceof Error ? error.message : error}`);
    process.exit(2);
  }
}
