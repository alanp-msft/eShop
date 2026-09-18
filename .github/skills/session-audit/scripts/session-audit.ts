#!/usr/bin/env node
/**
 * Per-customer agent session audit store backed by SQLite (node:sqlite).
 *
 * Every agent session records who ran which agent, on which host and model,
 * what artifacts it touched, and which approvals it contributed to. The store
 * lives inside the customer repository (gitignored) so data never leaves the
 * engagement boundary. Node 24+ built-ins only.
 */

import { parseArgs } from "node:util";
import { existsSync, mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { createHash, randomUUID } from "node:crypto";
import { join, relative, resolve } from "node:path";
import { userInfo } from "node:os";
import { DatabaseSync } from "node:sqlite";

const EXIT_SUCCESS = 0;
const EXIT_FAILURE = 1;
const EXIT_ERROR = 2;

const HOSTS = ["vscode", "cli", "ci"];
const STAGES = ["intake", "discovery", "architecture", "plan", "implement", "verify", "release", "operate", "learn"];
const ACTIONS = ["created", "updated", "read"];
const OUTCOMES = ["completed", "abandoned", "blocked"];

const SCHEMA_SQL = `
CREATE TABLE IF NOT EXISTS sessions (
    session_id    TEXT PRIMARY KEY,
    project       TEXT NOT NULL,
    agent         TEXT NOT NULL,
    agent_version TEXT,
    host          TEXT NOT NULL CHECK (host IN ('vscode','cli','ci')),
    model         TEXT,
    operator      TEXT,
    stage         TEXT,
    started_at    TEXT NOT NULL,
    ended_at      TEXT,
    outcome       TEXT CHECK (outcome IN ('completed','abandoned','blocked'))
);
CREATE TABLE IF NOT EXISTS artifacts (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    session_id  TEXT NOT NULL REFERENCES sessions(session_id),
    path        TEXT NOT NULL,
    action      TEXT NOT NULL CHECK (action IN ('created','updated','read')),
    sha256      TEXT,
    recorded_at TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS approvals (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    session_id  TEXT NOT NULL REFERENCES sessions(session_id),
    gate        TEXT NOT NULL,
    record_path TEXT NOT NULL,
    recorded_at TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_sessions_project ON sessions(project, started_at);
`;

type SessionRow = Record<string, string | number | null>;
type Ctx = { db: DatabaseSync; project: string; root: string; values: Record<string, string | boolean | string[] | undefined>; positionals: string[] };

function now(): string {
  return new Date().toISOString().replace(/\.\d{3}Z$/, "+00:00");
}

function detectHost(): string {
  if (process.env.GITHUB_ACTIONS || process.env.TF_BUILD) return "ci";
  if (process.env.TERM_PROGRAM === "vscode" || process.env.VSCODE_PID) return "vscode";
  return "cli";
}

function openStore(root: string, project: string): DatabaseSync {
  const path = join(root, ".copilot-tracking", "sdlc", project, "audit", "sessions.db");
  mkdirSync(join(path, ".."), { recursive: true });
  const db = new DatabaseSync(path);
  db.exec(SCHEMA_SQL);
  return db;
}

function sha256OrNull(path: string): string | null {
  return existsSync(path) ? createHash("sha256").update(readFileSync(path)).digest("hex") : null;
}

function requireSession(db: DatabaseSync, sessionId: string): boolean {
  if (!db.prepare("SELECT 1 FROM sessions WHERE session_id = ?").get(sessionId)) {
    console.error(`ERROR: Unknown session: ${sessionId}`);
    return false;
  }
  return true;
}

function str(v: string | boolean | string[] | undefined): string | null {
  return typeof v === "string" ? v : null;
}

function cmdStart({ db, project, values }: Ctx): number {
  const agent = str(values.agent);
  if (!agent) return usage("start requires --agent");
  const host = str(values.host) ?? detectHost();
  const stage = str(values.stage);
  if (!HOSTS.includes(host) || (stage && !STAGES.includes(stage))) return usage("invalid --host or --stage");
  const sessionId = str(values["session-id"]) ?? randomUUID().replaceAll("-", "").slice(0, 12);
  db.prepare(
    "INSERT INTO sessions (session_id, project, agent, agent_version, host, model, operator, stage, started_at) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)",
  ).run(sessionId, project, agent, str(values["agent-version"]), host, str(values.model), str(values.operator) ?? userInfo().username, stage, now());
  console.log(sessionId);
  return EXIT_SUCCESS;
}

function cmdArtifact({ db, root, values, positionals }: Ctx): number {
  const sessionId = str(values["session-id"]);
  const action = str(values.action);
  if (!sessionId || !action || !ACTIONS.includes(action) || !positionals.length) return usage("artifact requires --session-id, --action, and paths");
  if (!requireSession(db, sessionId)) return EXIT_FAILURE;
  const insert = db.prepare("INSERT INTO artifacts (session_id, path, action, sha256, recorded_at) VALUES (?, ?, ?, ?, ?)");
  for (const raw of positionals) {
    const abs = resolve(root, raw);
    const rel = abs.startsWith(root) ? relative(root, abs).replaceAll("\\", "/") : raw;
    insert.run(sessionId, rel, action, sha256OrNull(abs), now());
  }
  return EXIT_SUCCESS;
}

function cmdApproval({ db, values }: Ctx): number {
  const sessionId = str(values["session-id"]);
  const gate = str(values.gate);
  const recordPath = str(values["record-path"]);
  if (!sessionId || !gate || !recordPath) return usage("approval requires --session-id, --gate, --record-path");
  if (!requireSession(db, sessionId)) return EXIT_FAILURE;
  db.prepare("INSERT INTO approvals (session_id, gate, record_path, recorded_at) VALUES (?, ?, ?, ?)").run(sessionId, gate, recordPath, now());
  return EXIT_SUCCESS;
}

function cmdEnd({ db, values }: Ctx): number {
  const sessionId = str(values["session-id"]);
  const outcome = str(values.outcome);
  if (!sessionId || !outcome || !OUTCOMES.includes(outcome)) return usage("end requires --session-id and --outcome");
  if (!requireSession(db, sessionId)) return EXIT_FAILURE;
  db.prepare("UPDATE sessions SET ended_at = ?, outcome = ? WHERE session_id = ?").run(now(), outcome, sessionId);
  return EXIT_SUCCESS;
}

function manifestFor(db: DatabaseSync, session: SessionRow): Record<string, unknown> {
  const sessionId = String(session.session_id);
  const manifest: Record<string, unknown> = Object.fromEntries(Object.entries(session).filter(([, v]) => v !== null));
  manifest.artifacts = (db.prepare("SELECT path, action, sha256 FROM artifacts WHERE session_id = ? ORDER BY id").all(sessionId) as SessionRow[]).map(
    (r) => Object.fromEntries(Object.entries(r).filter(([, v]) => v !== null)),
  );
  manifest.approvals = (db.prepare("SELECT record_path FROM approvals WHERE session_id = ? ORDER BY id").all(sessionId) as SessionRow[]).map(
    (r) => r.record_path,
  );
  return manifest;
}

function cmdExport({ db, project, values }: Ctx): number {
  let rows = db.prepare("SELECT * FROM sessions WHERE project = ? ORDER BY started_at").all(project) as SessionRow[];
  const sessionId = str(values["session-id"]);
  if (sessionId) rows = rows.filter((r) => r.session_id === sessionId);
  const manifests = rows.map((r) => manifestFor(db, r));
  const output = JSON.stringify(sessionId ? (manifests[0] ?? {}) : manifests, null, 2);
  const outPath = str(values.output);
  if (outPath) {
    mkdirSync(join(outPath, ".."), { recursive: true });
    writeFileSync(outPath, `${output}\n`, "utf8");
    console.log(outPath);
  } else {
    console.log(output);
  }
  return EXIT_SUCCESS;
}

function cmdReport({ db, project }: Ctx): number {
  const summary = db
    .prepare(
      `SELECT agent, host, COUNT(*) AS sessions,
       SUM(CASE WHEN outcome = 'completed' THEN 1 ELSE 0 END) AS completed,
       SUM(CASE WHEN ended_at IS NULL THEN 1 ELSE 0 END) AS open
       FROM sessions WHERE project = ? GROUP BY agent, host ORDER BY sessions DESC`,
    )
    .all(project) as SessionRow[];
  if (!summary.length) {
    console.log(`No sessions recorded for ${project}`);
    return EXIT_SUCCESS;
  }
  console.log(`${"agent".padEnd(28)}${"host".padEnd(8)}${"sessions".padStart(9)}${"completed".padStart(11)}${"open".padStart(6)}`);
  for (const r of summary) {
    console.log(
      `${String(r.agent).padEnd(28)}${String(r.host).padEnd(8)}${String(r.sessions).padStart(9)}${String(r.completed).padStart(11)}${String(r.open).padStart(6)}`,
    );
  }
  const total = db
    .prepare("SELECT COUNT(*) AS n FROM artifacts a JOIN sessions s ON a.session_id = s.session_id WHERE s.project = ?")
    .get(project) as SessionRow;
  console.log(`\nartifacts touched: ${total.n}`);
  return EXIT_SUCCESS;
}

/** Learning-stage input. Aggregates by stage and agent only; operator is deliberately excluded so the output cannot be repurposed to rate individuals. */
function cmdMetrics({ db, project, values }: Ctx): number {
  const since = str(values.since) ?? "0000-01-01";
  const byStage = db
    .prepare(
      `SELECT COALESCE(stage, 'unknown') AS stage, agent, COUNT(*) AS sessions,
       SUM(CASE WHEN outcome = 'completed' THEN 1 ELSE 0 END) AS completed,
       SUM(CASE WHEN outcome = 'blocked' THEN 1 ELSE 0 END) AS blocked,
       SUM(CASE WHEN outcome = 'abandoned' THEN 1 ELSE 0 END) AS abandoned,
       SUM(CASE WHEN ended_at IS NULL THEN 1 ELSE 0 END) AS open,
       ROUND(AVG(CASE WHEN ended_at IS NOT NULL THEN (julianday(ended_at) - julianday(started_at)) * 1440 END), 1) AS avg_minutes
       FROM sessions WHERE project = ? AND started_at >= ? GROUP BY stage, agent ORDER BY stage, sessions DESC`,
    )
    .all(project, since) as SessionRow[];
  const rework = db
    .prepare(
      `SELECT a.path, COUNT(*) AS updates FROM artifacts a JOIN sessions s ON a.session_id = s.session_id
       WHERE s.project = ? AND s.started_at >= ? AND a.action = 'updated' GROUP BY a.path HAVING updates > 1 ORDER BY updates DESC LIMIT 10`,
    )
    .all(project, since) as SessionRow[];
  const approvals = db
    .prepare(
      `SELECT ap.gate, COUNT(*) AS recorded FROM approvals ap JOIN sessions s ON ap.session_id = s.session_id
       WHERE s.project = ? AND s.started_at >= ? GROUP BY ap.gate ORDER BY ap.gate`,
    )
    .all(project, since) as SessionRow[];

  if (values.json) {
    console.log(JSON.stringify({ project, since, by_stage: byStage, rework_hotspots: rework, approvals_recorded: approvals }, null, 2));
    return EXIT_SUCCESS;
  }
  if (!byStage.length) {
    console.log(`No sessions recorded for ${project} since ${since}`);
    return EXIT_SUCCESS;
  }
  console.log(`${"stage".padEnd(14)}${"agent".padEnd(24)}${"sessions".padStart(9)}${"done".padStart(6)}${"blocked".padStart(9)}${"aband.".padStart(8)}${"open".padStart(6)}${"avg min".padStart(9)}`);
  for (const r of byStage) {
    console.log(
      `${String(r.stage).padEnd(14)}${String(r.agent).padEnd(24)}${String(r.sessions).padStart(9)}${String(r.completed).padStart(6)}${String(r.blocked).padStart(9)}${String(r.abandoned).padStart(8)}${String(r.open).padStart(6)}${String(r.avg_minutes ?? "-").padStart(9)}`,
    );
  }
  console.log("\nrework hotspots (artifacts updated more than once):");
  for (const r of rework) console.log(`  ${r.updates}x  ${r.path}`);
  if (!rework.length) console.log("  none");
  console.log("\napprovals recorded by gate:");
  for (const r of approvals) console.log(`  ${r.gate}: ${r.recorded}`);
  if (!approvals.length) console.log("  none");
  return EXIT_SUCCESS;
}

const COMMANDS: Record<string, (ctx: Ctx) => number> = {
  start: cmdStart,
  artifact: cmdArtifact,
  approval: cmdApproval,
  end: cmdEnd,
  export: cmdExport,
  report: cmdReport,
  metrics: cmdMetrics,
};

function usage(message?: string): number {
  if (message) console.error(`ERROR: ${message}`);
  console.error(`Usage: node session-audit.ts --project <slug> [--repo-root <path>] <command> [options]
  start    --agent <name> [--stage s] [--model m] [--host {vscode|cli|ci}] [--operator o] [--agent-version v] [--session-id id]
  artifact --session-id <id> --action {created|updated|read} <path> [<path> ...]
  approval --session-id <id> --gate <gate> --record-path <path>
  end      --session-id <id> --outcome {completed|abandoned|blocked}
  export   [--session-id <id>] [--output <file>]
  report
  metrics  [--since YYYY-MM-DD] [--json]   (aggregated by stage and agent; never by operator)`);
  return EXIT_ERROR;
}

function main(): number {
  const { values, positionals } = parseArgs({
    allowPositionals: true,
    options: {
      project: { type: "string" },
      "repo-root": { type: "string", default: process.cwd() },
      agent: { type: "string" },
      "agent-version": { type: "string" },
      host: { type: "string" },
      model: { type: "string" },
      operator: { type: "string" },
      stage: { type: "string" },
      "session-id": { type: "string" },
      action: { type: "string" },
      gate: { type: "string" },
      "record-path": { type: "string" },
      outcome: { type: "string" },
      output: { type: "string" },
      since: { type: "string" },
      json: { type: "boolean", default: false },
    },
  });
  const [command, ...rest] = positionals;
  if (!values.project || !command || !(command in COMMANDS)) return usage();

  const root = resolve(values["repo-root"]!);
  try {
    const db = openStore(root, values.project);
    try {
      return COMMANDS[command]!({ db, project: values.project, root, values, positionals: rest });
    } finally {
      db.close();
    }
  } catch (error) {
    console.error(`ERROR: SQLite error: ${(error as Error).message}`);
    return EXIT_FAILURE;
  }
}

process.exitCode = main();
