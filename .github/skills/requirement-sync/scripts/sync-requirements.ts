#!/usr/bin/env node
/**
 * Keep REQ-nnn requirements and tracker work items in step.
 *
 * Requirements are the source of truth in requirements.md. A mapping file,
 * .copilot-tracking/sdlc/{project}/workitems.json, records which work item
 * carries each requirement and a content hash from the last sync. Subcommands:
 *   plan    diff requirements against the mapping (create / update / unchanged / withdrawn / orphan)
 *   apply   run the plan through `gh` (GitHub Issues) or `az boards` (Azure DevOps); --dry-run prints commands
 *   record  register a work item id created by a person or an agent
 *   verify  write evidence/workitems-{date}.md with status synced|drift for the design gate
 * Node 24+ built-ins only; tracker access only through the customer's own CLI credentials.
 */

import { parseArgs } from "node:util";
import { existsSync, mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { createHash } from "node:crypto";
import { join, relative, resolve } from "node:path";
import { spawnSync } from "node:child_process";
import { tmpdir } from "node:os";

const EXIT_SUCCESS = 0;
const EXIT_FAILURE = 1;
const EXIT_ERROR = 2;

type Requirement = { id: string; title: string; acceptance: string; priority: string; source: string; withdrawn: boolean; body: string };
type Mapping = { tracker: "ado" | "github"; items: Record<string, { id: string; url?: string; hash: string; synced_at: string }> };
type Action = "create" | "update" | "unchanged" | "withdrawn" | "orphan";
type PlanRow = { req: string; action: Action; title: string; id?: string; url?: string };
type Charter = { tracker: "ado" | "github"; tracker_project?: string; area_path?: string; tracker_org?: string; tracker_repo?: string };

const today = (): string => new Date().toISOString().slice(0, 10);
const posix = (root: string, p: string): string => relative(root, p).replaceAll("\\", "/");
const FIELD_LINE = /^(Acceptance|Priority|Source|Status):.*$/gm;

/** `gh` and `az` are .exe/.cmd shims on Windows, so a shell is required there; quote each argument ourselves. */
function runCli(argv: string[]): { status: number | null; stdout: string; stderr: string } {
  if (process.platform !== "win32") return spawnSync(argv[0]!, argv.slice(1), { encoding: "utf8" });
  const quoted = argv.map((a) => (/^[\w./:@=-]+$/.test(a) ? a : `"${a.replaceAll('"', '\\"')}"`)).join(" ");
  return spawnSync(quoted, { encoding: "utf8", shell: true });
}

function parseRequirements(path: string): Requirement[] {
  const text = readFileSync(path, "utf8").replace(/^---[\s\S]*?\n---\r?\n/, "");
  const reqs: Requirement[] = [];
  const blocks = text.split(/^(?=## REQ-\d{3,4}\b)/m).filter((b) => b.startsWith("## REQ-"));
  for (const block of blocks) {
    const lines = block.split(/\r?\n/);
    const heading = /^## (REQ-\d{3,4})\s*(.*)$/.exec(lines[0]!)!;
    const field = (name: string): string => (new RegExp(`^${name}:\\s*(.*)$`, "m").exec(block)?.[1] ?? "").trim();
    reqs.push({
      id: heading[1]!, title: heading[2]!.trim(), acceptance: field("Acceptance"), priority: field("Priority"), source: field("Source"),
      withdrawn: /^Status:\s*withdrawn/m.test(block),
      body: lines.slice(1).join("\n").replace(FIELD_LINE, "").replace(/^>.*$/gm, "").replace(/\n{3,}/g, "\n\n").trim(),
    });
  }
  return reqs;
}

function hashOf(r: Requirement): string {
  return createHash("sha256").update(`${r.title}\n${r.acceptance}\n${r.priority}\n${r.withdrawn}`).digest("hex").slice(0, 16);
}

function readCharter(path: string): Charter {
  const fm = /^---\r?\n([\s\S]*?)\r?\n---/.exec(readFileSync(path, "utf8"))?.[1] ?? "";
  const get = (key: string): string | undefined => new RegExp(`^\\s+${key}:\\s*['"]?([^'"\\r\\n]+)`, "m").exec(fm)?.[1]?.trim();
  const tracker = get("tracker");
  if (tracker !== "ado" && tracker !== "github") throw new Error(`charter platforms.tracker must be ado or github, got ${tracker}`);
  return { tracker, tracker_project: get("tracker_project"), area_path: get("area_path"), tracker_org: get("tracker_org"), tracker_repo: get("tracker_repo") };
}

function loadMapping(path: string, tracker: Charter["tracker"]): Mapping {
  if (!existsSync(path)) return { tracker, items: {} };
  const m = JSON.parse(readFileSync(path, "utf8")) as Mapping;
  if (m.tracker !== tracker) throw new Error(`workitems.json tracker ${m.tracker} does not match charter ${tracker}; migrate the mapping first`);
  return m;
}

function saveMapping(path: string, m: Mapping): void {
  mkdirSync(join(path, ".."), { recursive: true });
  writeFileSync(path, `${JSON.stringify(m, null, 2)}\n`, "utf8");
}

function buildPlan(reqs: Requirement[], mapping: Mapping): PlanRow[] {
  const rows: PlanRow[] = [];
  for (const r of reqs) {
    const item = mapping.items[r.id];
    const title = `${r.id} ${r.title}`;
    if (!item) rows.push({ req: r.id, action: r.withdrawn ? "withdrawn" : "create", title });
    else if (item.hash !== hashOf(r)) rows.push({ req: r.id, action: r.withdrawn ? "withdrawn" : "update", title, id: item.id, url: item.url });
    else rows.push({ req: r.id, action: "unchanged", title, id: item.id, url: item.url });
  }
  const known = new Set(reqs.map((r) => r.id));
  for (const [req, item] of Object.entries(mapping.items)) if (!known.has(req)) rows.push({ req, action: "orphan", title: `${req} (not in requirements.md)`, id: item.id, url: item.url });
  return rows;
}

function description(r: Requirement, project: string): string {
  return [
    `<!-- hve4isd:${r.id} project:${project} -->`,
    r.body || r.title,
    "",
    `**Acceptance:** ${r.acceptance || "(none recorded)"}`,
    `**Priority:** ${r.priority || "(none)"}`,
    `**Source:** ${r.source || "(none)"}`,
    r.withdrawn ? "\n**Status:** withdrawn" : "",
    "",
    "> AI-assisted content; review and validate before use.",
  ].join("\n");
}

type Cmd = { argv: string[]; parseId: (stdout: string) => { id: string; url?: string } | undefined };

function commandFor(row: PlanRow, r: Requirement | undefined, charter: Charter, project: string, bodyFile: string): Cmd | undefined {
  if (row.action === "unchanged" || row.action === "orphan" || (row.action === "withdrawn" && !row.id)) return undefined;
  if (charter.tracker === "github") {
    if (!charter.tracker_repo) throw new Error("charter platforms.tracker_repo (owner/name) is required for GitHub Issues");
    if (row.action === "create") return { argv: ["gh", "issue", "create", "--repo", charter.tracker_repo, "--title", row.title, "--body-file", bodyFile, "--label", "requirement"], parseId: (out) => { const m = /\/issues\/(\d+)/.exec(out); return m ? { id: m[1]!, url: out.trim() } : undefined; } };
    const argv = ["gh", "issue", "edit", row.id!, "--repo", charter.tracker_repo, "--title", row.title, "--body-file", bodyFile];
    if (row.action === "withdrawn") argv.push("--add-label", "withdrawn");
    return { argv, parseId: () => ({ id: row.id!, url: row.url }) };
  }
  if (!charter.tracker_org || !charter.tracker_project) throw new Error("charter platforms.tracker_org and tracker_project are required for Azure DevOps");
  const common = ["--org", charter.tracker_org, "-o", "json"];
  const desc = r ? description(r, project).replaceAll("\n", "<br>") : "";
  if (row.action === "create") {
    const argv = ["az", "boards", "work-item", "create", "--type", "User Story", "--title", row.title, "--description", desc, "--project", charter.tracker_project, ...common];
    if (charter.area_path) argv.push("--area", charter.area_path);
    return { argv, parseId: (out) => { try { const j = JSON.parse(out) as { id: number; url?: string }; return { id: String(j.id), url: j.url }; } catch { return undefined; } } };
  }
  const argv = ["az", "boards", "work-item", "update", "--id", row.id!, "--title", row.title, "--description", desc, ...common];
  if (row.action === "withdrawn") argv.push("--state", "Removed");
  return { argv, parseId: () => ({ id: row.id!, url: row.url }) };
}

function renderEvidence(project: string, plan: PlanRow[], mapping: Mapping): { text: string; synced: boolean } {
  const pending = plan.filter((p) => p.action === "create" || p.action === "update" || p.action === "orphan");
  const synced = pending.length === 0;
  const lines = [
    "---",
    `title: Requirement work items ${project}`,
    `description: REQ-nnn to ${mapping.tracker} work item sync state on ${today()}`,
    `ms.date: ${today()}`,
    `status: ${synced ? "synced" : "drift"}`,
    `tracker: ${mapping.tracker}`,
    `pending: ${pending.length}`,
    "---",
    "",
    "## Sync State",
    "",
    "| Requirement | Action | Work item |",
    "|-------------|--------|-----------|",
    ...plan.map((p) => `| ${p.title} | ${p.action} | ${p.id ? (p.url ? `[${p.id}](${p.url})` : p.id) : "" } |`),
    "",
    synced ? "All requirements have an up-to-date work item." : "Run `sync-requirements.ts apply` or record the created items to clear the pending actions.",
    "",
    "> AI-assisted content; review and validate before use.",
    "",
  ];
  return { text: lines.join("\n"), synced };
}

function main(): number {
  const { values, positionals } = parseArgs({
    allowPositionals: true,
    options: {
      project: { type: "string" },
      "repo-root": { type: "string", default: process.cwd() },
      req: { type: "string" },
      id: { type: "string" },
      url: { type: "string" },
      "dry-run": { type: "boolean", default: false },
      write: { type: "boolean", default: false },
      json: { type: "boolean", default: false },
    },
  });
  const command = positionals[0] ?? "plan";
  if (!values.project || !["plan", "apply", "record", "verify"].includes(command)) {
    console.error("Usage: node sync-requirements.ts --project <slug> [--repo-root p] {plan [--json] | apply [--dry-run] | record --req REQ-nnn --id <id> [--url u] | verify [--write]}");
    return EXIT_ERROR;
  }
  const root = resolve(values["repo-root"]!);
  const dir = join(root, ".copilot-tracking", "sdlc", values.project);
  const reqFile = join(dir, "requirements.md");
  const mapFile = join(dir, "workitems.json");
  try {
    const charter = readCharter(join(dir, "charter.md"));
    if (!existsSync(reqFile)) throw new Error(`requirements file not found: ${posix(root, reqFile)}`);
    const reqs = parseRequirements(reqFile);
    if (!reqs.length) throw new Error("no ## REQ-nnn headings found");
    const mapping = loadMapping(mapFile, charter.tracker);

    if (command === "record") {
      const r = reqs.find((x) => x.id === values.req);
      if (!r || !values.id) throw new Error("record needs --req matching a requirement and --id");
      mapping.items[r.id] = { id: values.id, url: values.url, hash: hashOf(r), synced_at: today() };
      saveMapping(mapFile, mapping);
      console.log(`recorded ${r.id} -> ${charter.tracker} ${values.id}`);
      return EXIT_SUCCESS;
    }

    const plan = buildPlan(reqs, mapping);
    if (command === "plan") {
      if (values.json) console.log(JSON.stringify({ project: values.project, tracker: charter.tracker, plan }, null, 2));
      else for (const p of plan) console.log(`${p.action.padEnd(10)} ${p.title}${p.id ? `  [${p.id}]` : ""}`);
      return EXIT_SUCCESS;
    }

    if (command === "apply") {
      const tool = charter.tracker === "github" ? "gh" : "az";
      const available = runCli([tool, "--version"]).status === 0;
      if (!values["dry-run"] && !available) { console.error(`ERROR: ${tool} CLI not found; install and authenticate it, or use --dry-run to print the commands`); return EXIT_ERROR; }
      let failures = 0;
      for (const row of plan) {
        const r = reqs.find((x) => x.id === row.req);
        const bodyFile = join(tmpdir(), `hve4isd-${values.project}-${row.req}.md`);
        const cmd = commandFor(row, r, charter, values.project, bodyFile);
        if (!cmd) { if (row.action === "orphan") console.log(`orphan     ${row.req} [${row.id}]: close or relink by hand; not touched`); continue; }
        if (values["dry-run"]) { console.log(cmd.argv.map((a) => (/\s/.test(a) ? JSON.stringify(a) : a)).join(" ")); continue; }
        if (r && charter.tracker === "github") writeFileSync(bodyFile, description(r, values.project), "utf8");
        const res = runCli(cmd.argv);
        const parsed = res.status === 0 ? cmd.parseId(res.stdout) : undefined;
        if (!parsed) { failures++; console.log(`FAILED     ${row.title}\n${(res.stderr || res.stdout).trim().split(/\r?\n/).slice(-3).map((l) => `           ${l}`).join("\n")}`); continue; }
        if (r) mapping.items[r.id] = { id: parsed.id, url: parsed.url, hash: hashOf(r), synced_at: today() };
        console.log(`${row.action.padEnd(10)} ${row.title}  [${parsed.id}]`);
      }
      if (!values["dry-run"]) saveMapping(mapFile, mapping);
      return failures ? EXIT_FAILURE : EXIT_SUCCESS;
    }

    const { text, synced } = renderEvidence(values.project, plan, mapping);
    console.log(text);
    if (values.write) {
      const out = join(dir, "evidence", `workitems-${today()}.md`);
      mkdirSync(join(out, ".."), { recursive: true });
      writeFileSync(out, text, "utf8");
      console.error(`INFO: Wrote ${posix(root, out)} (status: ${synced ? "synced" : "drift"})`);
    }
    return synced ? EXIT_SUCCESS : EXIT_FAILURE;
  } catch (error) {
    console.error(`ERROR: ${(error as Error).message}`);
    return EXIT_ERROR;
  }
}

process.exitCode = main();
