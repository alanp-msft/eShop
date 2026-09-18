#!/usr/bin/env node
/**
 * Check whether an SDLC gate is satisfied for a project.
 *
 * Reads the project charter, resolves the gates that apply to its risk tier,
 * verifies required artifacts exist, and validates the approval record when
 * the gate has been signed. Exit 0 passes, 1 fails, 2 invalid invocation or
 * configuration. Node 24+ built-ins only; runs with `node check-gate.ts`.
 */

import { parseArgs } from "node:util";
import { existsSync, globSync, readFileSync } from "node:fs";
import { createHash } from "node:crypto";
import { join, resolve } from "node:path";

const EXIT_SUCCESS = 0;
const EXIT_FAILURE = 1;
const EXIT_ERROR = 2;

const SKILL_ROOT = resolve(import.meta.dirname, "..");
const GATES_FILE = join(SKILL_ROOT, "assets", "gates.json");
const SCHEMA_DIR = join(SKILL_ROOT, "assets", "schemas");
const FRONTMATTER_RE = /^---\s*\r?\n([\s\S]*?)\r?\n---\s*\r?\n/;

type Json = string | number | boolean | null | Json[] | { [key: string]: Json };
type Schema = {
  type?: string;
  required?: string[];
  properties?: Record<string, Schema>;
  items?: Schema;
  enum?: Json[];
  pattern?: string;
};
type Requirement = { artifact: string; glob: string; condition?: string; frontmatter?: Record<string, string>; content?: string };
type Evidence = { artifact: string; path: string; sha256?: string };
type GatesConfig = {
  tiers: Record<string, string[]>;
  gates: Record<string, { required?: Requirement[]; required_when?: Requirement[] }>;
};
type GateResult = {
  project: string;
  gate: string;
  applies: boolean;
  passed: boolean;
  missing: string[];
  present: string[];
  approval: string;
  errors: string[];
};

function loadJson<T>(path: string): T {
  return JSON.parse(readFileSync(path, "utf8")) as T;
}

function parseScalar(raw: string): Json {
  const value = raw.trim();
  if (value.startsWith("[") && value.endsWith("]")) {
    const inner = value.slice(1, -1).trim();
    return inner ? inner.split(",").map(parseScalar) : [];
  }
  if (value.length >= 2 && value[0] === value.at(-1) && (value[0] === "'" || value[0] === '"')) {
    return value.slice(1, -1);
  }
  const lowered = value.toLowerCase();
  if (lowered === "true" || lowered === "yes") return true;
  if (lowered === "false" || lowered === "no") return false;
  if (lowered === "null" || lowered === "~" || lowered === "") return null;
  if (/^-?\d+$/.test(value)) return Number(value);
  return value;
}

/** YAML subset for frontmatter: scalars, inline lists, `- item` lists, one level of nested mappings. */
function parseSimpleYaml(text: string): Record<string, Json> {
  const root: Record<string, Json> = {};
  let currentKey: string | null = null;
  for (const rawLine of text.split(/\r?\n/)) {
    const line = rawLine.split(" #", 1)[0]!.trimEnd();
    if (!line.trim()) continue;
    const indent = line.length - line.trimStart().length;
    const stripped = line.trim();
    if (indent === 0) {
      const idx = stripped.indexOf(":");
      currentKey = stripped.slice(0, idx).trim();
      const value = stripped.slice(idx + 1);
      root[currentKey] = value.trim() ? parseScalar(value) : null;
    } else if (currentKey !== null) {
      if (stripped.startsWith("- ")) {
        const items = Array.isArray(root[currentKey]) ? (root[currentKey] as Json[]) : [];
        items.push(parseScalar(stripped.slice(2)));
        root[currentKey] = items;
      } else {
        const existing = root[currentKey];
        const mapping = existing && typeof existing === "object" && !Array.isArray(existing) ? existing : {};
        const idx = stripped.indexOf(":");
        mapping[stripped.slice(0, idx).trim()] = parseScalar(stripped.slice(idx + 1));
        root[currentKey] = mapping;
      }
    }
  }
  return root;
}

function readFrontmatter(path: string): Record<string, Json> {
  const match = FRONTMATTER_RE.exec(readFileSync(path, "utf8"));
  if (!match) throw new Error(`${path}: missing YAML frontmatter`);
  return parseSimpleYaml(match[1]!);
}

/** Minimal JSON Schema subset: type, required, properties, items, enum, pattern. */
export function validateSchema(instance: Json, schema: Schema, path = "$"): string[] {
  const errors: string[] = [];
  const typeOk: Record<string, (v: Json) => boolean> = {
    object: (v) => typeof v === "object" && v !== null && !Array.isArray(v),
    array: Array.isArray,
    string: (v) => typeof v === "string",
    boolean: (v) => typeof v === "boolean",
    number: (v) => typeof v === "number",
    integer: (v) => Number.isInteger(v),
  };
  if (schema.type && !typeOk[schema.type]?.(instance)) return [`${path}: expected ${schema.type}`];
  if (schema.enum && !schema.enum.includes(instance)) errors.push(`${path}: ${JSON.stringify(instance)} not in ${JSON.stringify(schema.enum)}`);
  if (schema.pattern && typeof instance === "string" && !new RegExp(`^(?:${schema.pattern})$`).test(instance)) {
    errors.push(`${path}: does not match ${schema.pattern}`);
  }
  if (typeof instance === "object" && instance !== null && !Array.isArray(instance)) {
    for (const key of schema.required ?? []) if (!(key in instance)) errors.push(`${path}.${key}: required`);
    for (const [key, sub] of Object.entries(schema.properties ?? {})) {
      if (key in instance) errors.push(...validateSchema(instance[key]!, sub, `${path}.${key}`));
    }
  }
  if (Array.isArray(instance) && schema.items) {
    instance.forEach((item, i) => errors.push(...validateSchema(item, schema.items!, `${path}[${i}]`)));
  }
  return errors;
}

function conditionHolds(condition: string, charter: Record<string, Json>): boolean {
  let m = /^(\w+) in \[([\w, ]+)\]$/.exec(condition);
  if (m) return m[2]!.split(",").map((v) => v.trim()).includes(String(charter[m[1]!]));
  m = /^(\w+) == (\w+)$/.exec(condition);
  if (m) return String(charter[m[1]!]).toLowerCase() === m[2]!.toLowerCase();
  throw new Error(`Unsupported condition: ${condition}`);
}

export function checkGate(repoRoot: string, project: string, gate: string, requireApproval: boolean): GateResult {
  const result: GateResult = { project, gate, applies: true, passed: false, missing: [], present: [], approval: "absent", errors: [] };
  const gates = loadJson<GatesConfig>(GATES_FILE);
  const charterPath = join(repoRoot, ".copilot-tracking", "sdlc", project, "charter.md");
  if (!existsSync(charterPath)) {
    result.errors.push(`Charter not found: ${charterPath}`);
    return result;
  }

  const charter = readFrontmatter(charterPath);
  const schemaErrors = validateSchema(charter, loadJson<Schema>(join(SCHEMA_DIR, "charter.schema.json")));
  if (schemaErrors.length) {
    result.errors.push(...schemaErrors.map((e) => `charter: ${e}`));
    return result;
  }

  const tier = String(charter.risk_tier);
  if (!gates.tiers[tier]?.includes(gate)) {
    result.applies = false;
    result.passed = true;
    return result;
  }

  const definition = gates.gates[gate]!;
  const requirements = [
    ...(definition.required ?? []),
    ...(definition.required_when ?? []).filter((r) => conditionHolds(r.condition!, charter)),
  ];
  for (const req of requirements) {
    const pattern = req.glob.replaceAll("{project}", project);
    const matches = globSync(pattern, { cwd: repoRoot }).sort();
    if (!matches.length) { result.missing.push(`${req.artifact} (${pattern})`); continue; }
    // Dated files sort by name; the newest one is the record that counts.
    const latest = matches.at(-1)!;
    if (req.frontmatter) {
      const fm = readFrontmatter(join(repoRoot, latest));
      const bad = Object.entries(req.frontmatter).filter(([k, v]) => String(fm[k]) !== v);
      if (bad.length) { result.missing.push(`${req.artifact} (${latest}: ${bad.map(([k, v]) => `${k}=${String(fm[k])}, expected ${v}`).join("; ")})`); continue; }
    }
    if (req.content && !new RegExp(req.content, "m").test(readFileSync(join(repoRoot, latest), "utf8"))) {
      result.missing.push(`${req.artifact} (${latest}: content does not match /${req.content}/)`);
      continue;
    }
    result.present.push(req.artifact);
  }

  const approvalPath = join(repoRoot, ".copilot-tracking", "sdlc", project, "gates", `${gate}.json`);
  if (existsSync(approvalPath)) {
    const record = loadJson<Record<string, Json>>(approvalPath);
    const errors = validateSchema(record, loadJson<Schema>(join(SCHEMA_DIR, "gate-approval.schema.json")));
    if (record.gate !== gate || record.project !== project) errors.push("approval record gate/project mismatch");
    if (errors.length) {
      result.errors.push(...errors.map((e) => `approval: ${e}`));
      result.approval = "invalid";
    } else {
      result.approval = String(record.decision);
      // An approval covers specific file contents; if any approved file changed, the decision no longer applies.
      const drifted = (record.evidence as unknown as Evidence[])
        .filter((e) => e.sha256)
        .filter((e) => {
          const abs = join(repoRoot, e.path);
          return !existsSync(abs) || createHash("sha256").update(readFileSync(abs)).digest("hex") !== e.sha256;
        });
      if (drifted.length) {
        result.approval = "stale";
        result.errors.push(...drifted.map((e) => `approval: evidence changed or removed since approval: ${e.path}`));
      }
    }
  }

  const approvalOk = !requireApproval || ["approved", "approved_with_conditions"].includes(result.approval);
  result.passed = !result.missing.length && !result.errors.length && approvalOk;
  return result;
}

function main(): number {
  const { values } = parseArgs({
    options: {
      project: { type: "string" },
      gate: { type: "string" },
      "repo-root": { type: "string", default: process.cwd() },
      "require-approval": { type: "boolean", default: false },
      json: { type: "boolean", default: false },
    },
  });
  const gate = values.gate ?? "";
  if (!values.project || !["design", "plan", "pr", "release", "production"].includes(gate)) {
    console.error("Usage: node check-gate.ts --project <slug> --gate {design|plan|pr|release|production} [--repo-root <path>] [--require-approval] [--json]");
    return EXIT_ERROR;
  }

  let result: GateResult;
  try {
    result = checkGate(resolve(values["repo-root"]!), values.project, gate, values["require-approval"]!);
  } catch (error) {
    console.error(`ERROR: ${(error as Error).message}`);
    return EXIT_ERROR;
  }

  if (values.json) {
    console.log(JSON.stringify(result, null, 2));
  } else {
    const scope = result.applies ? "" : " (gate not required for this risk tier)";
    console.log(`[${result.passed ? "PASS" : "FAIL"}] ${result.project} / ${result.gate}${scope}`);
    for (const item of result.present) console.log(`  present: ${item}`);
    for (const item of result.missing) console.log(`  missing: ${item}`);
    for (const item of result.errors) console.log(`  error:   ${item}`);
    console.log(`  approval: ${result.approval}`);
  }
  return result.passed ? EXIT_SUCCESS : EXIT_FAILURE;
}

process.exitCode = main();
