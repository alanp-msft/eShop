#!/usr/bin/env node
/**
 * Release evidence: bind a version to the commit, gate approvals, and evidence that justify it.
 *
 *   build   write release/manifest-<version>.json (commit, gate records, evidence hashes, requirement status)
 *           and draft release/release-notes-<version>.md from requirements, change records, and work items;
 *           creates release/rollback-<version>.md from a template only when absent, for a person to complete
 *   verify  re-hash everything a manifest references and report drift (chain of custody after the fact)
 *
 * Exit 0 ok, 1 manifest incomplete or drifted, 2 usage error. Node 24+ built-ins only.
 */

import { parseArgs } from "node:util";
import { existsSync, globSync, mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { createHash } from "node:crypto";
import { join, relative, resolve } from "node:path";
import { spawnSync } from "node:child_process";

const EXIT_SUCCESS = 0;
const EXIT_FAILURE = 1;
const EXIT_ERROR = 2;

type Hashed = { path: string; sha256: string };
type GateRef = Hashed & { gate: string; decision: string; approved_by: string; approved_at: string; conditions: string[] };
type ReqStatus = { id: string; title: string; work_item?: string; trace: string };
type Manifest = {
  project: string; version: string; built_at: string; commit: string; branch: string; risk_tier: string;
  gates: GateRef[]; evidence: Hashed[]; requirements: ReqStatus[]; changes: Hashed[]; complete: boolean; missing: string[];
};

const today = (): string => new Date().toISOString().slice(0, 10);
const posix = (root: string, p: string): string => relative(root, p).replaceAll("\\", "/");
const sha = (p: string): string => createHash("sha256").update(readFileSync(p)).digest("hex");

function git(root: string, ...args: string[]): string {
  const r = spawnSync("git", args, { cwd: root, encoding: "utf8" });
  return r.status === 0 ? r.stdout.trim() : "";
}

function frontmatter(path: string): Record<string, string> {
  const fm = /^---\r?\n([\s\S]*?)\r?\n---/.exec(readFileSync(path, "utf8"))?.[1] ?? "";
  const out: Record<string, string> = {};
  for (const line of fm.split(/\r?\n/)) { const kv = /^([A-Za-z_-]+):\s*(.*)$/.exec(line); if (kv) out[kv[1]!] = kv[2]!.replace(/^['"]|['"]$/g, "").trim(); }
  return out;
}

function latest(root: string, pattern: string): string | undefined {
  const m = globSync(pattern, { cwd: root }).sort().at(-1);
  return m ? join(root, m) : undefined;
}

function requirementStatus(root: string, dir: string): ReqStatus[] {
  const reqFile = join(dir, "requirements.md");
  if (!existsSync(reqFile)) return [];
  const mapping = existsSync(join(dir, "workitems.json")) ? (JSON.parse(readFileSync(join(dir, "workitems.json"), "utf8")) as { items: Record<string, { id: string }> }).items : {};
  const trace = latest(root, `${posix(root, dir)}/evidence/trace-*.md`);
  const traceRows = new Map<string, string>();
  if (trace) for (const m of readFileSync(trace, "utf8").matchAll(/^\| (REQ-\d+)[^|]*\|(?:[^|]*\|){5}\s*(\w+) \|$/gm)) traceRows.set(m[1]!, m[2]!);
  const out: ReqStatus[] = [];
  for (const m of readFileSync(reqFile, "utf8").matchAll(/^## (REQ-\d{3,4})\s*(.*)$/gm)) {
    out.push({ id: m[1]!, title: m[2]!.trim(), work_item: mapping[m[1]!]?.id, trace: traceRows.get(m[1]!) ?? "untraced" });
  }
  return out;
}

function buildManifest(root: string, project: string, version: string): Manifest {
  const dir = join(root, ".copilot-tracking", "sdlc", project);
  const rel = posix(root, dir);
  const charter = frontmatter(join(dir, "charter.md"));
  const tier = charter.risk_tier ?? "unknown";
  const gatesNeeded = tier === "high" ? ["design", "pr", "release"] : tier === "medium" ? ["design", "pr"] : ["pr"];
  const missing: string[] = [];

  const gates: GateRef[] = [];
  for (const g of gatesNeeded) {
    const p = join(dir, "gates", `${g}.json`);
    if (!existsSync(p)) { missing.push(`gate approval: ${g}`); continue; }
    const rec = JSON.parse(readFileSync(p, "utf8")) as { decision: string; approved_by: string; approved_at: string; conditions?: string[] };
    if (!rec.decision.startsWith("approved")) missing.push(`gate ${g} decision is ${rec.decision}`);
    gates.push({ gate: g, decision: rec.decision, approved_by: rec.approved_by, approved_at: rec.approved_at, conditions: rec.conditions ?? [], path: posix(root, p), sha256: sha(p) });
  }

  const evidence: Hashed[] = [];
  const wanted: [string, string, boolean][] = [
    ["tests", `${rel}/evidence/tests-*.md`, true],
    ["trace", `${rel}/evidence/trace-*.md`, true],
    ["security", `${rel}/evidence/security-*.md`, tier !== "low"],
    ["workitems", `${rel}/evidence/workitems-*.md`, tier !== "low"],
  ];
  for (const [name, glob, required] of wanted) {
    const p = latest(root, glob);
    if (!p) { if (required) missing.push(`evidence: ${name}`); continue; }
    const fm = frontmatter(p);
    if (name === "security" && fm.status !== "pass") missing.push(`security evidence status is ${fm.status}`);
    evidence.push({ path: posix(root, p), sha256: sha(p) });
  }

  const changes = globSync(".copilot-tracking/changes/*/*-changes.md", { cwd: root }).map((c) => c.replaceAll("\\", "/")).sort().map((c) => ({ path: c, sha256: sha(join(root, c)) }));
  const requirements = requirementStatus(root, dir);
  for (const r of requirements) if (r.trace !== "traced") missing.push(`requirement ${r.id} is ${r.trace}`);

  return {
    project, version, built_at: new Date().toISOString().replace(/\.\d{3}Z$/, "+00:00"),
    commit: git(root, "rev-parse", "HEAD") || "unknown", branch: git(root, "rev-parse", "--abbrev-ref", "HEAD") || "unknown",
    risk_tier: tier, gates, evidence, requirements, changes, complete: missing.length === 0, missing,
  };
}

function renderNotes(m: Manifest, root: string): string {
  const changeSummaries = m.changes.map((c) => {
    const text = readFileSync(join(root, c.path), "utf8");
    const title = /^#\s+(.+)$/m.exec(text)?.[1] ?? frontmatter(join(root, c.path)).title ?? c.path;
    return `* ${title} ([${c.path.split("/").at(-1)}](${c.path}))`;
  });
  // Known issues come from what people accepted: approval conditions and suppressed scanner findings (L-005).
  const conditions = m.gates.flatMap((g) => g.conditions.map((c) => `* ${g.gate} gate condition: ${c}`));
  const security = m.evidence.find((e) => /\/security-[^/]+\.md$/.test(e.path));
  const suppressed: string[] = [];
  if (security) {
    const text = readFileSync(join(root, security.path), "utf8");
    const section = /^## Suppressed Findings\r?\n([\s\S]*?)(?=^## |\Z)/m.exec(text)?.[1] ?? "";
    for (const row of section.matchAll(/^\| (\w+) \| ([^|]+) \| `([^`]+)` \| ([^|]*) \|/gm)) suppressed.push(`* Suppressed ${row[1]} finding \`${row[3]}\` (${row[2]!.trim()}) at ${row[4]!.trim() || "repository"}`);
  }
  const known = [...conditions, ...suppressed];
  return [
    "---",
    `title: Release notes ${m.project} ${m.version}`,
    `description: Requirements delivered, changes included, and approvals for ${m.project} ${m.version}`,
    `ms.date: ${today()}`,
    `version: ${m.version}`,
    `commit: ${m.commit}`,
    "---",
    "",
    "## Summary",
    "",
    `Release \`${m.version}\` of \`${m.project}\` built from commit \`${m.commit.slice(0, 12)}\` on \`${m.branch}\` (risk tier ${m.risk_tier}). Replace this paragraph with the customer-facing summary.`,
    "",
    "## Requirements",
    "",
    "| Requirement | Work item | Trace |",
    "|-------------|-----------|-------|",
    ...m.requirements.map((r) => `| ${r.id} ${r.title} | ${r.work_item ?? ""} | ${r.trace} |`),
    "",
    "## Changes",
    "",
    ...(changeSummaries.length ? changeSummaries : ["* No change records found under .copilot-tracking/changes/"]),
    "",
    "## Approvals",
    "",
    ...m.gates.map((g) => `* ${g.gate} gate: ${g.decision} by ${g.approved_by} on ${g.approved_at.slice(0, 10)}`),
    "",
    "## Known Issues and Conditions",
    "",
    ...(known.length ? known : ["* None recorded: no approval conditions and no suppressed security findings."]),
    "* Add known defects carried into this release.",
    "",
    `Evidence manifest: [manifest-${m.version}.json](manifest-${m.version}.json)`,
    "",
    "> AI-assisted content; review and validate before use.",
    "",
  ].join("\n");
}

function rollbackTemplate(m: Manifest): string {
  return [
    "---",
    `title: Rollback plan ${m.project} ${m.version}`,
    `description: Steps to return ${m.project} to the previous release if ${m.version} must be withdrawn`,
    `ms.date: ${today()}`,
    `version: ${m.version}`,
    "status: draft",
    "---",
    "",
    "## Trigger",
    "",
    "Describe the observable conditions (error rate, failed health checks, data issue) that start a rollback and who decides.",
    "",
    "## Steps",
    "",
    "1. Previous release identifier and where its artifacts live.",
    "2. Deployment rollback command or pipeline stage, per environment.",
    "3. Database or schema reversal, or confirmation that the change is backward compatible.",
    "4. Feature flags to disable and configuration to revert.",
    "5. Verification: health checks and the smoke test that proves the previous release is serving.",
    "",
    "## Data and Compatibility",
    "",
    "State whether data written by this release remains readable by the previous one.",
    "",
    "## Communication",
    "",
    "Who is told, through which channel, and the incident record to open.",
    "",
    "Set `status: ready` in the frontmatter once the release manager has reviewed this plan.",
    "",
    "> AI-assisted content; review and validate before use.",
    "",
  ].join("\n");
}

function verifyManifest(root: string, path: string): { drifted: string[]; missing: string[] } {
  const m = JSON.parse(readFileSync(path, "utf8")) as Manifest;
  const drifted: string[] = []; const missing: string[] = [];
  for (const h of [...m.gates, ...m.evidence, ...m.changes]) {
    const p = join(root, h.path);
    if (!existsSync(p)) missing.push(h.path);
    else if (sha(p) !== h.sha256) drifted.push(h.path);
  }
  return { drifted, missing };
}

function main(): number {
  const { values, positionals } = parseArgs({
    allowPositionals: true,
    options: { project: { type: "string" }, version: { type: "string" }, "repo-root": { type: "string", default: process.cwd() }, json: { type: "boolean", default: false } },
  });
  const command = positionals[0] ?? "build";
  if (!values.project || !values.version || !["build", "verify"].includes(command)) {
    console.error("Usage: node build-release.ts --project <slug> --version <v> [--repo-root p] {build [--json] | verify}");
    return EXIT_ERROR;
  }
  const root = resolve(values["repo-root"]!);
  const releaseDir = join(root, ".copilot-tracking", "sdlc", values.project, "release");
  const manifestPath = join(releaseDir, `manifest-${values.version}.json`);
  try {
    if (command === "verify") {
      if (!existsSync(manifestPath)) { console.error(`ERROR: no manifest at ${posix(root, manifestPath)}`); return EXIT_ERROR; }
      const { drifted, missing } = verifyManifest(root, manifestPath);
      for (const d of drifted) console.log(`drifted  ${d}`);
      for (const d of missing) console.log(`missing  ${d}`);
      console.log(drifted.length || missing.length ? `manifest ${values.version}: ${drifted.length} drifted, ${missing.length} missing` : `manifest ${values.version}: all referenced files intact`);
      return drifted.length || missing.length ? EXIT_FAILURE : EXIT_SUCCESS;
    }
    const m = buildManifest(root, values.project, values.version);
    mkdirSync(releaseDir, { recursive: true });
    writeFileSync(manifestPath, `${JSON.stringify(m, null, 2)}\n`, "utf8");
    const notesPath = join(releaseDir, `release-notes-${values.version}.md`);
    writeFileSync(notesPath, renderNotes(m, root), "utf8");
    const rollbackPath = join(releaseDir, `rollback-${values.version}.md`);
    const rollbackCreated = !existsSync(rollbackPath);
    if (rollbackCreated) writeFileSync(rollbackPath, rollbackTemplate(m), "utf8");
    if (values.json) console.log(JSON.stringify(m, null, 2));
    else {
      console.log(`${m.complete ? "COMPLETE" : "INCOMPLETE"} ${values.project} ${values.version} @ ${m.commit.slice(0, 12)}`);
      for (const x of m.missing) console.log(`  missing: ${x}`);
      console.log(`  wrote ${posix(root, manifestPath)}, ${posix(root, notesPath)}${rollbackCreated ? `, ${posix(root, rollbackPath)} (template; complete it)` : ""}`);
    }
    return m.complete ? EXIT_SUCCESS : EXIT_FAILURE;
  } catch (error) {
    console.error(`ERROR: ${(error as Error).message}`);
    return EXIT_ERROR;
  }
}

process.exitCode = main();
