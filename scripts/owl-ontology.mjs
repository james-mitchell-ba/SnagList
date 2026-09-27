// Machine-readable OWL/TTL ontology support. ontology.ttl is the source of truth; ontology.md is
// generated from it — see build-ontology.mjs, the CLI entry point that drives this module.
//
// This file's Turtle reader (parseTurtle, added in a later task) only ever has to read Turtle this
// same module's own writer (serializeTurtle) produced. It is deliberately not a general RDF/Turtle
// implementation — see that function's own comment for what that scope limit buys.

export const KIT_NS = "https://ontology-kit.dev/schema#";
export const OWL_NS = "http://www.w3.org/2002/07/owl#";
export const RDFS_NS = "http://www.w3.org/2000/01/rdf-schema#";

/**
 * Column headers for each table-shaped section, in table order. The first column is always the
 * row's identifying, backticked name (or, for Relationships, the "From" reference).
 * @type {Record<string, string[]>}
 */
export const SECTION_COLUMNS = {
  "External Systems": ["Name", "What it is", "Description"],
  "Subsystems": ["Name", "What it is", "Responsibility", "Boundary"],
  "Aggregate Roots": ["Name", "Description", "Repository Interface"],
  "Entities": ["Name", "Properties", "Description"],
  "Value Objects": ["Name", "Properties", "Description"],
  "Domain Events": ["Name", "Raising Aggregate", "Payload Properties", "Description"],
  "Enums": ["Name", "Values", "Description"],
  "Use Cases": ["Name", "Actor", "Description"],
  "Relationships": ["From", "Relationship", "To", "Cardinality"],
};

/**
 * The fixed kit: class every row of a table-shaped section becomes a subclass of. Relationships
 * has none — its rows become owl:ObjectProperty individuals, not classes; see modelToTriples.
 * @type {Record<string, string>}
 */
export const SECTION_CLASS = {
  "External Systems": "ExternalSystem",
  "Subsystems": "Subsystem",
  "Aggregate Roots": "AggregateRoot",
  "Entities": "Entity",
  "Value Objects": "ValueObject",
  "Domain Events": "DomainEvent",
  "Enums": "Enum",
  "Use Cases": "UseCase",
};

/** Section rendering/parsing order — must match the shipped ontology.md template's own order. */
export const SECTION_ORDER = [
  "External Systems",
  "Subsystems",
  "Aggregate Roots",
  "Entities",
  "Value Objects",
  "Domain Events",
  "Enums",
  "Use Cases",
  "Relationships",
  "Business Rules & Invariants",
];

/**
 * Strips a single pair of surrounding backticks, e.g. "`ExampleEntity`" -> "ExampleEntity".
 * Returns null when the cell is not exactly one backticked term, so callers can tell "absent"
 * apart from "empty".
 * @param {string | undefined} cell
 * @returns {string | null}
 */
export function stripBacktickTerm(cell) {
  if (!cell) return null;
  const match = cell.trim().match(/^`([^`]+)`$/);
  return match ? match[1] : null;
}

/**
 * Splits a comma-separated cell of backticked terms, e.g. "`id`, `status`" -> ["id", "status"].
 * A blank or missing cell returns [].
 * @param {string | undefined} cell
 * @returns {string[]}
 */
export function splitBacktickList(cell) {
  if (!cell || !cell.trim()) return [];
  return cell
    .split(",")
    .map((part) => stripBacktickTerm(part))
    .filter((term) => term !== null);
}

/**
 * Turns free text into an IRI-safe local-name segment: letters, digits and underscores only,
 * every run of anything else collapsed to a single underscore, leading/trailing underscores
 * trimmed. Used for the parts of the ontology whose source text is not already a clean
 * identifier (enum individuals, relationship property names).
 * @param {string} text
 * @returns {string}
 */
export function slugify(text) {
  return text
    .trim()
    .replace(/[^A-Za-z0-9]+/g, "_")
    .replace(/^_+|_+$/g, "");
}

/** Escapes a JS string as the body of a Turtle double-quoted string literal (no surrounding quotes). */
export function escapeTurtleString(text) {
  return text.replace(/\\/g, "\\\\").replace(/"/g, '\\"').replace(/\n/g, "\\n");
}

/** Inverse of escapeTurtleString. */
export function unescapeTurtleString(text) {
  return text.replace(/\\n/g, "\n").replace(/\\"/g, '"').replace(/\\\\/g, "\\");
}

/**
 * @typedef {{ name: string, cells: Record<string, string> }} Row
 * @typedef {{ concept: string, text: string }} Invariant
 * @typedef {{ projectName: string, sections: Record<string, Row[]>, invariants: Invariant[] }} OntologyModel
 */

const TITLE_RE = /^# (.+?) — Application Ontology\s*$/m;
const HEADING_RE = /^##\s+(.+?)\s*$/m;
const INVARIANT_RE = /^-\s+\*\*`([^`]+)`\*\*\s+—\s+(.*)$/;

/**
 * Parses the human-readable ontology.md into a structured model. A section header this module
 * does not recognise (e.g. a spliced "## Ontology protocol") is skipped, not an error — this
 * reader only ever needs to see the ontology's own sections. A recognised section absent from the
 * text is simply absent from `sections`, matching "delete the sections that do not apply".
 * @param {string} text
 * @returns {OntologyModel}
 */
export function parseOntologyMarkdown(text) {
  const titleMatch = text.match(TITLE_RE);
  const projectName = titleMatch ? titleMatch[1] : "";

  /** @type {Record<string, Row[]>} */
  const sections = {};
  /** @type {Invariant[]} */
  const invariants = [];

  for (const block of text.split(/\n---\n/)) {
    const headingMatch = block.match(HEADING_RE);
    if (!headingMatch) continue;
    const name = headingMatch[1].trim();

    if (name === "Business Rules & Invariants") {
      for (const line of block.split("\n")) {
        const match = line.match(INVARIANT_RE);
        if (match) invariants.push({ concept: match[1], text: match[2].trim() });
      }
      continue;
    }

    const columns = SECTION_COLUMNS[name];
    if (!columns) continue;
    sections[name] = parseTableRows(block, columns);
  }

  return { projectName, sections, invariants };
}

/**
 * @param {string} block - text of one "## Heading" section, including the heading line
 * @param {string[]} columns
 * @returns {Row[]}
 */
function parseTableRows(block, columns) {
  const tableLines = block.split("\n").filter((line) => line.trim().startsWith("|"));
  // tableLines[0] is the header row, tableLines[1] the "| --- | --- |" separator; data is [2..].
  /** @type {Row[]} */
  const rows = [];
  for (const line of tableLines.slice(2)) {
    const cellValues = line
      .trim()
      .replace(/^\|/, "")
      .replace(/\|$/, "")
      .split("|")
      .map((cell) => cell.trim());
    if (cellValues.length < columns.length) continue;
    const name = stripBacktickTerm(cellValues[0]);
    if (name === null) continue;
    /** @type {Record<string, string>} */
    const cells = {};
    for (let index = 1; index < columns.length; index++) {
      cells[columns[index]] = cellValues[index];
    }
    rows.push({ name, cells });
  }
  return rows;
}

/** @typedef {{ subject: string, predicate: string, object: string, objectType: "iri" | "literal" }} Triple */

/**
 * Converts a parsed ontology.md model into the triples that describe it, applying the kit's
 * vocabulary mapping (see the design spec's "OWL vocabulary" section). This is the migration
 * direction: an existing ontology.md becomes a new ontology.ttl.
 * @param {OntologyModel} model
 * @returns {Triple[]}
 */
export function modelToTriples(model) {
  /** @type {Triple[]} */
  const triples = [];
  const literal = (subject, predicate, text) => {
    if (!text || !text.trim()) return;
    triples.push({ subject, predicate, object: text.trim(), objectType: "literal" });
  };
  const iri = (subject, predicate, object) => {
    triples.push({ subject, predicate, object, objectType: "iri" });
  };

  literal(":", "kit:projectName", model.projectName);

  // Invariants live on their concept's own subject, so they are emitted inline with that
  // concept's class triples rather than in a trailing loop: serializeTurtle groups triples by
  // subject, so a trailing loop would move an invariant's position across a serialize/parse
  // round trip and parseTurtle would no longer be serializeTurtle's exact inverse.
  /** @type {Map<string, string[]>} */
  const invariantsByConcept = new Map();
  for (const invariant of model.invariants) {
    if (!invariantsByConcept.has(invariant.concept)) invariantsByConcept.set(invariant.concept, []);
    invariantsByConcept.get(invariant.concept).push(invariant.text);
  }
  const flushInvariants = (concept) => {
    for (const text of invariantsByConcept.get(concept) ?? []) {
      literal(`:${concept}`, "kit:invariant", text);
    }
    invariantsByConcept.delete(concept);
  };

  for (const sectionName of SECTION_ORDER) {
    const rows = model.sections[sectionName];
    if (!rows) continue;

    if (sectionName === "Relationships") {
      addRelationshipTriples(rows, iri, literal);
      continue;
    }

    const kitClass = SECTION_CLASS[sectionName];
    for (const row of rows) {
      const subject = `:${row.name}`;
      iri(subject, "a", "owl:Class");
      iri(subject, "rdfs:subClassOf", `kit:${kitClass}`);
      literal(subject, "rdfs:label", row.name);
      literal(subject, "rdfs:comment", row.cells.Description);
      literal(subject, "kit:whatItIs", row.cells["What it is"]);
      literal(subject, "kit:responsibility", row.cells.Responsibility);
      literal(subject, "kit:boundary", row.cells.Boundary);
      literal(subject, "kit:repositoryInterface", row.cells["Repository Interface"]);
      literal(subject, "kit:actor", row.cells.Actor);
      for (const prop of splitBacktickList(row.cells.Properties)) literal(subject, "kit:hasProperty", prop);
      for (const prop of splitBacktickList(row.cells["Payload Properties"])) literal(subject, "kit:hasProperty", prop);
      const raisedBy = stripBacktickTerm(row.cells["Raising Aggregate"]);
      if (raisedBy) iri(subject, "kit:raisedBy", `:${raisedBy}`);
      flushInvariants(row.name);

      if (sectionName === "Enums") {
        for (const value of splitBacktickList(row.cells.Values)) {
          const individual = `:${row.name}_${slugify(value)}`;
          iri(individual, "a", subject);
          literal(individual, "rdfs:label", value);
        }
      }
    }
  }

  // Invariants naming a concept with no class row have no inline home; emit them last.
  for (const invariant of model.invariants) {
    if (invariantsByConcept.has(invariant.concept)) {
      literal(`:${invariant.concept}`, "kit:invariant", invariant.text);
    }
  }

  return triples;
}

/**
 * @param {Row[]} rows
 * @param {(subject: string, predicate: string, object: string) => void} iri
 * @param {(subject: string, predicate: string, text: string | undefined) => void} literal
 */
function addRelationshipTriples(rows, iri, literal) {
  /** @type {Map<string, Set<string>>} */
  const pairsByLocalName = new Map();
  for (const row of rows) {
    const fromName = row.name;
    const toName = stripBacktickTerm(row.cells.To) ?? row.cells.To;
    const relText = row.cells.Relationship ?? "";
    const pairKey = `${fromName}|${toName}`;
    const baseName = slugify(relText) || "relationship";

    let localName = baseName;
    let suffix = 2;
    while (pairsByLocalName.has(localName) && !pairsByLocalName.get(localName).has(pairKey)) {
      localName = `${baseName}_${suffix}`;
      suffix++;
    }
    if (!pairsByLocalName.has(localName)) pairsByLocalName.set(localName, new Set());
    pairsByLocalName.get(localName).add(pairKey);

    const subject = `:${localName}`;
    iri(subject, "a", "owl:ObjectProperty");
    iri(subject, "rdfs:domain", `:${fromName}`);
    iri(subject, "rdfs:range", `:${toName}`);
    literal(subject, "rdfs:label", relText);
    literal(subject, "kit:cardinality", row.cells.Cardinality);
  }
}

/**
 * Serializes triples as Turtle: the four fixed prefixes, then one blank-line-separated block per
 * subject, each a semicolon-joined predicate-object list ending in a period. Every logical
 * predicate-object pair is written on its own physical line — literal newlines are already escaped
 * by the time they reach here — which is what lets parseTurtle read this format back with a plain
 * line-oriented parser instead of a full tokenizer.
 * @param {Triple[]} triples
 * @param {{ namespaceUri: string }} options
 * @returns {string}
 */
export function serializeTurtle(triples, { namespaceUri }) {
  /** @type {Map<string, Triple[]>} */
  const bySubject = new Map();
  for (const triple of triples) {
    if (!bySubject.has(triple.subject)) bySubject.set(triple.subject, []);
    bySubject.get(triple.subject).push(triple);
  }

  const header =
    `@prefix : <${namespaceUri}> .\n` +
    `@prefix kit: <${KIT_NS}> .\n` +
    `@prefix owl: <${OWL_NS}> .\n` +
    `@prefix rdfs: <${RDFS_NS}> .\n`;

  const blocks = [];
  for (const [subject, subjectTriples] of bySubject) {
    const lines = subjectTriples.map((triple, index) => {
      const objectText =
        triple.objectType === "literal" ? `"${escapeTurtleString(triple.object)}"` : triple.object;
      const terminator = index === subjectTriples.length - 1 ? "." : ";";
      const prefix = index === 0 ? `${subject} ${triple.predicate} ` : `    ${triple.predicate} `;
      return `${prefix}${objectText} ${terminator}`;
    });
    blocks.push(lines.join("\n"));
  }

  return blocks.length ? `${header}\n${blocks.join("\n\n")}\n` : `${header}\n`;
}

/**
 * Parses the Turtle subset this module's own serializeTurtle produces: @prefix lines (skipped —
 * every token in the body is already written in prefixed form, so nothing needs expanding), then
 * blank-line-separated subject blocks of "subject predicate object ;\n    predicate object ;\n
 * ... predicate object .". This is not a general Turtle parser — it relies on every
 * predicate-object pair sitting on its own physical line, which serializeTurtle guarantees by
 * escaping literal newlines. It is meant only to read this same module's own output back.
 * @param {string} text
 * @returns {Triple[]}
 */
export function parseTurtle(text) {
  /** @type {Triple[]} */
  const triples = [];
  const blocks = text
    .split(/\n\s*\n/)
    .map((block) => block.trim())
    .filter((block) => block && !block.startsWith("@prefix"));

  for (const block of blocks) {
    const lines = block.split("\n").map((line) => line.trim()).filter(Boolean);
    let subject = null;
    for (const rawLine of lines) {
      const terminator = rawLine.at(-1);
      if (terminator !== ";" && terminator !== ".") {
        throw new Error(`malformed Turtle line (expected ";" or "." at end): "${rawLine}"`);
      }
      const body = rawLine.slice(0, -1).trim();
      let rest = body;
      if (subject === null) {
        const firstSpace = body.indexOf(" ");
        if (firstSpace === -1) throw new Error(`malformed Turtle line (no subject found): "${rawLine}"`);
        subject = body.slice(0, firstSpace);
        rest = body.slice(firstSpace + 1).trim();
      }
      const predicateSpace = rest.indexOf(" ");
      if (predicateSpace === -1) throw new Error(`malformed Turtle line (no predicate/object split): "${rawLine}"`);
      const predicate = rest.slice(0, predicateSpace);
      const objectToken = rest.slice(predicateSpace + 1).trim();
      if (objectToken.startsWith('"')) {
        if (!objectToken.endsWith('"') || objectToken.length < 2) {
          throw new Error(`malformed Turtle string literal: "${rawLine}"`);
        }
        triples.push({
          subject,
          predicate,
          object: unescapeTurtleString(objectToken.slice(1, -1)),
          objectType: "literal",
        });
      } else {
        triples.push({ subject, predicate, object: objectToken, objectType: "iri" });
      }
    }
  }
  return triples;
}

/**
 * Converts triples back into an ontology model — the inverse of modelToTriples. This is the
 * render direction: ontology.ttl becomes the ontology.md that gets written to disk.
 * @param {Triple[]} triples
 * @returns {OntologyModel}
 */
export function triplesToModel(triples) {
  /** @type {Map<string, Triple[]>} */
  const bySubject = new Map();
  for (const triple of triples) {
    if (!bySubject.has(triple.subject)) bySubject.set(triple.subject, []);
    bySubject.get(triple.subject).push(triple);
  }
  const get = (subjectTriples, predicate) => subjectTriples.find((t) => t.predicate === predicate)?.object;

  let projectName = "";
  /** @type {Record<string, Row[]>} */
  const sections = {};
  /** @type {Map<string, string>} */
  const classSection = new Map();

  // Pass 1: classes (every table-shaped section except Relationships and enum individuals).
  for (const [subject, subjectTriples] of bySubject) {
    if (subject === ":") {
      projectName = get(subjectTriples, "kit:projectName") ?? "";
      continue;
    }
    const type = get(subjectTriples, "a");
    if (type !== "owl:Class") continue;

    const subClass = get(subjectTriples, "rdfs:subClassOf");
    const sectionName = Object.entries(SECTION_CLASS).find(([, kitClass]) => subClass === `kit:${kitClass}`)?.[0];
    if (!sectionName) continue;
    classSection.set(subject, sectionName);

    const name = subject.slice(1);
    /** @type {Record<string, string>} */
    const cells = { Description: get(subjectTriples, "rdfs:comment") ?? "" };
    const whatItIs = get(subjectTriples, "kit:whatItIs");
    if (whatItIs !== undefined) cells["What it is"] = whatItIs;
    const responsibility = get(subjectTriples, "kit:responsibility");
    if (responsibility !== undefined) cells.Responsibility = responsibility;
    const boundary = get(subjectTriples, "kit:boundary");
    if (boundary !== undefined) cells.Boundary = boundary;
    const repositoryInterface = get(subjectTriples, "kit:repositoryInterface");
    if (repositoryInterface !== undefined) cells["Repository Interface"] = repositoryInterface;
    const actor = get(subjectTriples, "kit:actor");
    if (actor !== undefined) cells.Actor = actor;

    const properties = subjectTriples.filter((t) => t.predicate === "kit:hasProperty").map((t) => t.object);
    if (sectionName === "Domain Events") {
      if (properties.length) cells["Payload Properties"] = properties.map((p) => `\`${p}\``).join(", ");
      const raisedBy = get(subjectTriples, "kit:raisedBy");
      if (raisedBy) cells["Raising Aggregate"] = `\`${raisedBy.slice(1)}\``;
    } else if (properties.length) {
      cells.Properties = properties.map((p) => `\`${p}\``).join(", ");
    }

    if (!sections[sectionName]) sections[sectionName] = [];
    sections[sectionName].push({ name, cells });
  }

  // Pass 2: relationships.
  /** @type {Row[]} */
  const relationshipRows = [];
  for (const [, subjectTriples] of bySubject) {
    if (get(subjectTriples, "a") !== "owl:ObjectProperty") continue;
    const domain = get(subjectTriples, "rdfs:domain")?.slice(1) ?? "";
    const range = get(subjectTriples, "rdfs:range")?.slice(1) ?? "";
    relationshipRows.push({
      name: domain,
      cells: {
        Relationship: get(subjectTriples, "rdfs:label") ?? "",
        To: `\`${range}\``,
        Cardinality: get(subjectTriples, "kit:cardinality") ?? "",
      },
    });
  }
  if (relationshipRows.length) sections.Relationships = relationshipRows;

  // Pass 3: enum individuals, grouped back onto their enum class's Values cell, in file order.
  /** @type {Map<string, string[]>} */
  const valuesByEnumClass = new Map();
  for (const [subject, subjectTriples] of bySubject) {
    const type = get(subjectTriples, "a");
    if (type === "owl:Class" || type === "owl:ObjectProperty" || !type) continue;
    if (classSection.get(type) !== "Enums") continue;
    if (!valuesByEnumClass.has(type)) valuesByEnumClass.set(type, []);
    valuesByEnumClass.get(type).push(get(subjectTriples, "rdfs:label") ?? "");
  }
  for (const row of sections.Enums ?? []) {
    const values = valuesByEnumClass.get(`:${row.name}`) ?? [];
    row.cells.Values = values.map((v) => `\`${v}\``).join(", ");
  }

  // Invariants: kit:invariant triples on any subject, in file order.
  /** @type {Invariant[]} */
  const invariants = [];
  for (const [subject, subjectTriples] of bySubject) {
    if (subject === ":") continue;
    for (const triple of subjectTriples) {
      if (triple.predicate === "kit:invariant") invariants.push({ concept: subject.slice(1), text: triple.object });
    }
  }

  return { projectName, sections, invariants };
}

const PREAMBLE = (projectName) => `# ${projectName} — Application Ontology

<!-- GENERATED FROM ontology.ttl. Edit that file, not this one. -->

> **AI instructions:** Read this file in full before starting any task that touches domain
> concepts. To change it, edit \`ontology.ttl\` and run \`node scripts/build-ontology.mjs\`. This
> file must never lag behind the code. See the Ontology protocol section in this repository's
> agent-instruction file.

Every domain concept is named here exactly once, as a backticked PascalCase term. Code, specs,
plans and prose use those names; \`ontology.ttl\` is the source, this file and they are the
consumers. A term that appears in Markdown but not here fails the deterministic check in
\`scripts/check-ontology-terms.mjs\`.

**Delete the sections that do not apply to this repository from \`ontology.ttl\`**, and remove the
matching names from \`sections\` in \`ontology.config.json\`. An empty section is worse than an
absent one — the optional semantic reviewer treats an empty required section as a configuration
error.
`;

/**
 * Renders a model as the human-readable ontology.md: the fixed preamble above, then each
 * non-empty section from SECTION_ORDER as a Markdown table (or, for Business Rules & Invariants,
 * bullets), separated by "---" lines. This is the render direction's other half — the inverse of
 * parseOntologyMarkdown for any model that only uses columns this vocabulary defines.
 * @param {OntologyModel} model
 * @returns {string}
 */
export function renderOntologyMarkdown(model) {
  const parts = [PREAMBLE(model.projectName).trimEnd()];

  for (const sectionName of SECTION_ORDER) {
    if (sectionName === "Business Rules & Invariants") {
      if (!model.invariants.length) continue;
      const bullets = model.invariants.map((inv) => `- **\`${inv.concept}\`** — ${inv.text}`).join("\n");
      parts.push(`## ${sectionName}\n\n${bullets}\n`);
      continue;
    }
    const rows = model.sections[sectionName];
    if (!rows || !rows.length) continue;
    const columns = SECTION_COLUMNS[sectionName];
    const header = `| ${columns.join(" | ")} |`;
    const separator = `| ${columns.map(() => "---").join(" | ")} |`;
    const dataLines = rows.map((row) => {
      const cellValues = columns.map((column, index) => (index === 0 ? `\`${row.name}\`` : row.cells[column] ?? ""));
      return `| ${cellValues.join(" | ")} |`;
    });
    parts.push(`## ${sectionName}\n\n${[header, separator, ...dataLines].join("\n")}\n`);
  }

  return `${parts.join("\n---\n\n").trimEnd()}\n`;
}
