#!/usr/bin/env node
/**
 * Build a requirement traceability matrix from REQ-nnn identifiers.
 *
 * Requirements are declared as headings in requirements.md. Every other file
 * in the scanned roots is searched for references. The matrix shows, per
 * requirement, which plans, tests, source files, and change records cite it.
 * Orphan requirements (no test or no implementation) fail the check when
 * requested. Node 24+ built-ins only.
 */

import { parseArgs } from "node:util";
import { existsSync, mkdirSync, readFileSync, readdirSync, writeFileSync } from "node:fs";
import { extname, join, relative, resolve } from "node:path";

const EXIT_SUCCESS = 0;
const EXIT_FAILURE = 1;
const EXIT_ERROR = 2;

const REQ_ID = /\bREQ-\d{3,4}\b/g;
const HEADING_REQ = /^#{2,4}\s+(REQ-\d{3,4})\b.*$/;
const SKIP_DIRS = new Set([".git", "node_modules", "bin", "obj", "dist", "build", ".venv", "__pycache__"]);
const TEXT_SUFFIXES = new Set([".md", ".cs", ".ts", ".tsx", ".js", ".jsx", ".bicep", ".bicepparam", ".json", ".yml", ".yaml", ".ps1", ".py", ".sql", ".feature"]);

const CATEGORY_RULES: [string, RegExp][] = [
  ["test", /(^|\/)(tests?|__tests__|spec)(\/|$)|\.(test|spec)\.|Tests?\.cs$/i],
  ["plan", /\.copilot-tracking\/plans\//],
  ["changes", /\.copilot-tracking\/changes\//],
  ["review", /\.copilot-tracking\/reviews\//],
  ["evidence", /\.copilot-tracking\/sdlc\/[^/]+\/evidence\//],
  ["workitem", /\.copilot-tracking\/(workitems|github-issues|jira-issues)\/|\.copilot-tracking\/sdlc\/[^/]+\/workitems\.json$/],
  ["source", /\.(cs|ts|tsx|js|jsx|bicep|bicepparam|sql)$/],
];

type Matrix = Map<string, Map<string, Set<string>>>;

function categorize(relPath: string): string {
  return CATEGORY_RULES.find(([, re]) => re.test(relPath))?.[0] ?? "other";
}

function* walk(dir: string): Generator<string> {
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    if (entry.isDirectory()) {
      if (!SKIP_DIRS.has(entry.name)) yield* walk(join(dir, entry.name));
    } else if (entry.isFile() && TEXT_SUFFIXES.has(extname(entry.name).toLowerCase())) {
      yield join(dir, entry.name);
    }
  }
}

function toPosix(root: string, path: string): string {
  return relative(root, path).replaceAll("\\", "/");
}

function buildMatrix(root: string, requirementsFile: string, scanRoots: string[]): { titles: Map<string, string>; matrix: Matrix } {
  const titles = new Map<string, string>();
  for (const line of readFileSync(requirementsFile, "utf8").split(/\r?\n/)) {
    const m = HEADING_REQ.exec(line);
    if (m) titles.set(m[1]!, line.replace(/^#+/, "").trim());
  }
  if (!titles.size) throw new Error(`No \`## REQ-nnn\` headings found in ${requirementsFile}`);

  const matrix: Matrix = new Map([...titles.keys()].map((req) => [req, new Map()]));
  const reqRel = toPosix(root, requirementsFile);
  for (const base of scanRoots) {
    if (!existsSync(base)) continue;
    for (const path of walk(base)) {
      const rel = toPosix(root, path);
      if (rel === reqRel) continue;
      const content = readFileSync(path, "utf8");
      for (const req of new Set(content.match(REQ_ID) ?? [])) {
        const cats = matrix.get(req);
        if (!cats) continue;
        const category = categorize(rel);
        if (!cats.has(category)) cats.set(category, new Set());
        cats.get(category)!.add(rel);
      }
    }
  }
  return { titles, matrix };
}

function count(cats: Map<string, Set<string>>, category: string): number {
  return cats.get(category)?.size ?? 0;
}

function statusOf(cats: Map<string, Set<string>>): string {
  if (!count(cats, "source")) return "unimplemented";
  if (!count(cats, "test")) return "untested";
  // A requirement implemented without a plan task naming it was never scheduled or reviewed as work (L-005).
  if (!count(cats, "plan")) return "unplanned";
  return "traced";
}

function renderMarkdown(project: string, titles: Map<string, string>, matrix: Matrix): string {
  const today = new Date().toISOString().slice(0, 10);
  const lines = [
    "---",
    `title: Requirement Trace ${project}`,
    `description: Traceability matrix for ${project} generated on ${today}`,
    `ms.date: ${today}`,
    "---",
    "",
    "## Traceability Matrix",
    "",
    "| Requirement | Plan | Source | Tests | Changes | Review | Status |",
    "|-------------|------|--------|-------|---------|--------|--------|",
  ];
  for (const [req, title] of titles) {
    const cats = matrix.get(req)!;
    lines.push(
      `| ${title} | ${count(cats, "plan")} | ${count(cats, "source")} | ${count(cats, "test")} | ${count(cats, "changes")} | ${count(cats, "review")} | ${statusOf(cats)} |`,
    );
  }
  lines.push("", "## References", "");
  for (const req of titles.keys()) {
    lines.push(`### ${req}`, "");
    const refs = [...matrix.get(req)!].flatMap(([c, paths]) => [...paths].map((p) => `${c}\u0000${p}`)).sort();
    lines.push(...(refs.length ? refs.map((r) => r.split("\u0000")).map(([c, p]) => `* ${c}: [${p}](${p})`) : ["* No references found"]), "");
  }
  lines.push("> AI-assisted content; review and validate before use.", "");
  return lines.join("\n");
}

function main(): number {
  const { values } = parseArgs({
    options: {
      project: { type: "string" },
      "repo-root": { type: "string", default: process.cwd() },
      requirements: { type: "string" },
      scan: { type: "string", multiple: true },
      "fail-on-orphans": { type: "boolean", default: false },
      write: { type: "boolean", default: false },
      json: { type: "boolean", default: false },
    },
  });
  if (!values.project) {
    console.error("Usage: node trace-requirements.ts --project <slug> [--repo-root p] [--requirements f] [--scan dir ...] [--fail-on-orphans] [--write] [--json]");
    return EXIT_ERROR;
  }
  const root = resolve(values["repo-root"]!);
  const projectDir = join(root, ".copilot-tracking", "sdlc", values.project);
  const requirements = resolve(root, values.requirements ?? join(projectDir, "requirements.md"));
  if (!existsSync(requirements)) {
    console.error(`ERROR: Requirements file not found: ${requirements}`);
    return EXIT_ERROR;
  }
  const scanRoots = (values.scan?.length ? values.scan : ["src", "tests", "docs", ".copilot-tracking"]).map((p) => resolve(root, p));

  let titles: Map<string, string>;
  let matrix: Matrix;
  try {
    ({ titles, matrix } = buildMatrix(root, requirements, scanRoots));
  } catch (error) {
    console.error(`ERROR: ${(error as Error).message}`);
    return EXIT_ERROR;
  }

  const orphans = [...titles.keys()].filter((r) => statusOf(matrix.get(r)!) !== "traced");
  const markdown = renderMarkdown(values.project, titles, matrix);
  if (values.json) {
    console.log(JSON.stringify(Object.fromEntries([...matrix].map(([r, cats]) => [r, Object.fromEntries([...cats].map(([c, p]) => [c, [...p].sort()]))])), null, 2));
  } else {
    console.log(markdown);
  }
  if (values.write) {
    const out = join(projectDir, "evidence", `trace-${new Date().toISOString().slice(0, 10)}.md`);
    mkdirSync(join(out, ".."), { recursive: true });
    writeFileSync(out, markdown, "utf8");
    console.error(`INFO: Wrote ${toPosix(root, out)}`);
  }
  if (orphans.length) {
    console.error(`WARNING: Requirements not fully traced (plan, source, and tests): ${orphans.join(", ")}`);
    if (values["fail-on-orphans"]) return EXIT_FAILURE;
  }
  return EXIT_SUCCESS;
}

process.exitCode = main();
