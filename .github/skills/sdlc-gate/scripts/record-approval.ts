#!/usr/bin/env node
/**
 * Write a gate approval record after a human decision.
 *
 * The record hashes each evidence file so later audits can detect drift
 * between what was approved and what shipped. Never call this without an
 * explicit human decision; agents draft, people approve.
 */

import { parseArgs } from "node:util";
import { existsSync, mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { createHash } from "node:crypto";
import { join, relative, resolve } from "node:path";
import { spawnSync } from "node:child_process";

const EXIT_SUCCESS = 0;
const EXIT_FAILURE = 1;
const EXIT_ERROR = 2;

const GATES = ["design", "pr", "release", "production"];
const DECISIONS = ["approved", "approved_with_conditions", "rejected"];

function sha256Of(path: string): string {
  return createHash("sha256").update(readFileSync(path)).digest("hex");
}

/**
 * An approval hash is only meaningful if every clone sees the same bytes. Refuse evidence that git
 * would not commit (ignored) or would rewrite on checkout (working-copy line endings differ from
 * the index), because either makes the approval read as stale everywhere but this machine.
 */
function gitEvidenceProblem(root: string, rel: string): string | undefined {
  const git = (...args: string[]) => spawnSync("git", args, { cwd: root, encoding: "utf8" });
  if (git("rev-parse", "--is-inside-work-tree").status !== 0) return undefined;
  if (git("check-ignore", "-q", rel).status === 0) return `${rel} is gitignored; approvals must reference committed evidence`;
  const eol = git("ls-files", "--eol", "--", rel).stdout.trim();
  const m = /i\/(\S+)\s+w\/(\S+)/.exec(eol);
  if (m && m[1] !== "none" && m[2] !== "none" && m[1] !== m[2]) return `${rel} has ${m[2]} line endings in the working copy but ${m[1]} in git; run git add --renormalize and check the file out again before approving`;
  return undefined;
}

function main(): number {
  const { values } = parseArgs({
    options: {
      project: { type: "string" },
      gate: { type: "string" },
      decision: { type: "string" },
      "approved-by": { type: "string" },
      evidence: { type: "string", multiple: true, default: [] },
      condition: { type: "string", multiple: true, default: [] },
      "session-id": { type: "string" },
      notes: { type: "string" },
      "repo-root": { type: "string", default: process.cwd() },
    },
  });

  const { project, gate, decision } = values;
  const approvedBy = values["approved-by"];
  const sessionId = values["session-id"];
  if (!project || !gate || !GATES.includes(gate) || !decision || !DECISIONS.includes(decision) || !approvedBy || !sessionId || !values.evidence!.length) {
    console.error(
      "Usage: node record-approval.ts --project <slug> --gate {design|pr|release|production} --decision {approved|approved_with_conditions|rejected} --approved-by <identity> --session-id <audit session> --evidence name=path [--evidence ...] [--condition text] [--notes text] [--repo-root path]",
    );
    return EXIT_ERROR;
  }

  const root = resolve(values["repo-root"]!);
  const evidence: { artifact: string; path: string; sha256: string }[] = [];
  for (const entry of values.evidence!) {
    const idx = entry.indexOf("=");
    if (idx < 0) {
      console.error(`ERROR: Evidence must be artifact=path, got ${JSON.stringify(entry)}`);
      return EXIT_ERROR;
    }
    const name = entry.slice(0, idx);
    const path = resolve(root, entry.slice(idx + 1));
    if (!existsSync(path)) {
      console.error(`ERROR: Evidence file not found: ${path}`);
      return EXIT_FAILURE;
    }
    const rel = relative(root, path).replaceAll("\\", "/");
    const problem = gitEvidenceProblem(root, rel);
    if (problem) {
      console.error(`ERROR: ${problem}`);
      return EXIT_FAILURE;
    }
    evidence.push({ artifact: name, path: rel, sha256: sha256Of(path) });
  }

  const record: Record<string, unknown> = {
    project,
    gate,
    decision,
    approved_by: approvedBy,
    approved_at: new Date().toISOString().replace(/\.\d{3}Z$/, "+00:00"),
    conditions: values.condition,
    evidence,
    // Binds the human decision to the agent session that presented the evidence (L-006).
    session_id: sessionId,
  };
  if (values.notes) record.notes = values.notes;

  const out = join(root, ".copilot-tracking", "sdlc", project, "gates", `${gate}.json`);
  mkdirSync(join(out, ".."), { recursive: true });
  writeFileSync(out, `${JSON.stringify(record, null, 2)}\n`, "utf8");
  console.log(relative(root, out).replaceAll("\\", "/"));
  return EXIT_SUCCESS;
}

process.exitCode = main();
