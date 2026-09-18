#!/usr/bin/env node
/**
 * Triage SARIF scanner output into pr-gate security evidence.
 *
 * Reads one or more SARIF files (CodeQL, Microsoft Security DevOps, gitleaks,
 * Trivy, PSRule, or any SARIF 2.1 producer), normalizes severity, applies the
 * governed suppressions file and the per-tier policy, and writes
 * evidence/security-{date}.md with `status: pass|fail` in frontmatter. The gate
 * requires the latest evidence to be `pass`. Exit 0 pass, 1 blocking findings
 * or missing required tool, 2 usage error. Node 24+ built-ins only.
 */

import { parseArgs } from "node:util";
import { existsSync, mkdirSync, readFileSync, readdirSync, statSync, writeFileSync } from "node:fs";
import { extname, join, relative, resolve } from "node:path";

const EXIT_SUCCESS = 0;
const EXIT_FAILURE = 1;
const EXIT_ERROR = 2;

const SKILL_ROOT = resolve(import.meta.dirname, "..");
const SEVERITIES = ["low", "medium", "high", "critical"] as const;
type Severity = (typeof SEVERITIES)[number];

type Policy = {
  severity_from_score: Record<Exclude<Severity, "low">, number>;
  severity_from_level: Record<string, Severity>;
  tiers: Record<string, { block_at: Severity; required_tools: string[]; honor_sarif_suppressions: boolean }>;
  suppression_max_days: number;
};
type Suppression = { rule: string; path?: string; justification: string; approved_by: string; expires: string; work_item?: string };
type SarifRule = { id: string; properties?: Record<string, unknown>; defaultConfiguration?: { level?: string } };
type SarifResult = {
  ruleId?: string; ruleIndex?: number; level?: string; message?: { text?: string };
  properties?: Record<string, unknown>; suppressions?: { status?: string }[];
  locations?: { physicalLocation?: { artifactLocation?: { uri?: string }; region?: { startLine?: number } } }[];
};
type SarifRun = { tool: { driver: { name: string; semanticVersion?: string; version?: string; rules?: SarifRule[] } }; results?: SarifResult[] };
type Finding = { tool: string; rule: string; severity: Severity; path: string; line: number | undefined; message: string; disposition: "blocking" | "allowed" | "suppressed" | "suppressed_in_source"; reason?: string };

const rank = (s: Severity): number => SEVERITIES.indexOf(s);

function readRiskTier(charterPath: string): string {
  const m = /^risk_tier:\s*['"]?(\w+)/m.exec(readFileSync(charterPath, "utf8"));
  if (!m) throw new Error(`risk_tier not found in ${charterPath}`);
  return m[1]!;
}

function collectSarif(paths: string[]): string[] {
  const out: string[] = [];
  const walk = (p: string): void => {
    if (!existsSync(p)) return;
    if (statSync(p).isDirectory()) for (const e of readdirSync(p)) walk(join(p, e));
    else if (extname(p).toLowerCase() === ".sarif" || p.endsWith(".sarif.json")) out.push(p);
  };
  paths.forEach(walk);
  return out;
}

function severityOf(result: SarifResult, rule: SarifRule | undefined, policy: Policy): Severity {
  const score = Number(result.properties?.["security-severity"] ?? rule?.properties?.["security-severity"]);
  if (!Number.isNaN(score) && score > 0) {
    if (score >= policy.severity_from_score.critical) return "critical";
    if (score >= policy.severity_from_score.high) return "high";
    if (score >= policy.severity_from_score.medium) return "medium";
    return "low";
  }
  const named = String(result.properties?.severity ?? rule?.properties?.["problem.severity"] ?? "").toLowerCase();
  if ((SEVERITIES as readonly string[]).includes(named)) return named as Severity;
  return policy.severity_from_level[result.level ?? rule?.defaultConfiguration?.level ?? "warning"] ?? "medium";
}

function loadSuppressions(path: string, maxDays: number, today: string): { active: Suppression[]; problems: string[] } {
  if (!existsSync(path)) return { active: [], problems: [] };
  const raw = JSON.parse(readFileSync(path, "utf8")) as unknown;
  const problems: string[] = [];
  const active: Suppression[] = [];
  if (!Array.isArray(raw)) return { active, problems: ["suppressions file must be a JSON array"] };
  raw.forEach((s: Partial<Suppression>, i) => {
    const missing = (["rule", "justification", "approved_by", "expires"] as const).filter((k) => !s[k]);
    if (missing.length) { problems.push(`suppression[${i}]: missing ${missing.join(", ")}`); return; }
    if ((s.justification ?? "").length < 20) { problems.push(`suppression[${i}] ${s.rule}: justification too short`); return; }
    if (s.expires! < today) { problems.push(`suppression[${i}] ${s.rule}: expired ${s.expires}`); return; }
    const horizon = new Date(today); horizon.setUTCDate(horizon.getUTCDate() + maxDays);
    if (new Date(s.expires!) > horizon) { problems.push(`suppression[${i}] ${s.rule}: expires more than ${maxDays} days out`); return; }
    active.push(s as Suppression);
  });
  return { active, problems };
}

function triage(sarifFiles: string[], tier: string, policy: Policy, suppressions: Suppression[], repoRoot: string): { findings: Finding[]; tools: Map<string, string> } {
  const tierPolicy = policy.tiers[tier];
  if (!tierPolicy) throw new Error(`No policy for risk_tier ${tier}`);
  const findings: Finding[] = [];
  const tools = new Map<string, string>();
  for (const file of sarifFiles) {
    const doc = JSON.parse(readFileSync(file, "utf8")) as { runs?: SarifRun[] };
    for (const run of doc.runs ?? []) {
      const driver = run.tool.driver;
      tools.set(driver.name, driver.semanticVersion ?? driver.version ?? "");
      const rules = driver.rules ?? [];
      for (const r of run.results ?? []) {
        const rule = r.ruleIndex !== undefined ? rules[r.ruleIndex] : rules.find((x) => x.id === r.ruleId);
        const ruleId = r.ruleId ?? rule?.id ?? "unknown";
        const loc = r.locations?.[0]?.physicalLocation;
        const path = (loc?.artifactLocation?.uri ?? "").replace(/^file:\/+/, "").replace(/^\.\//, "");
        const severity = severityOf(r, rule, policy);
        const f: Finding = {
          tool: driver.name, rule: ruleId, severity, path, line: loc?.region?.startLine,
          message: (r.message?.text ?? "").replace(/\s+/g, " ").trim().slice(0, 200), disposition: "allowed",
        };
        const sup = suppressions.find((s) => s.rule === ruleId && (!s.path || path.startsWith(s.path)));
        const inSource = (r.suppressions ?? []).some((s) => s.status !== "rejected");
        if (sup) { f.disposition = "suppressed"; f.reason = `${sup.approved_by} until ${sup.expires}: ${sup.justification}`; }
        else if (inSource && tierPolicy.honor_sarif_suppressions) { f.disposition = "suppressed_in_source"; }
        else if (rank(severity) >= rank(tierPolicy.block_at)) { f.disposition = "blocking"; }
        findings.push(f);
      }
    }
  }
  void repoRoot;
  return { findings, tools };
}

function render(project: string, tier: string, policy: Policy, findings: Finding[], tools: Map<string, string>, missingTools: string[], problems: string[], sarifFiles: string[], repoRoot: string, today: string): { text: string; pass: boolean } {
  const count = (d: Finding["disposition"], s?: Severity): number => findings.filter((f) => f.disposition === d && (!s || f.severity === s)).length;
  const blocking = count("blocking");
  const pass = blocking === 0 && missingTools.length === 0 && problems.length === 0;
  const tierPolicy = policy.tiers[tier]!;
  const lines = [
    "---",
    `title: Security scan evidence ${project}`,
    `description: Scanner findings triaged against the ${tier} tier policy on ${today}`,
    `ms.date: ${today}`,
    `status: ${pass ? "pass" : "fail"}`,
    `risk_tier: ${tier}`,
    `blocking: ${blocking}`,
    "---",
    "",
    "## Policy",
    "",
    `Tier \`${tier}\`: findings at or above \`${tierPolicy.block_at}\` block; required tools: ${tierPolicy.required_tools.join(", ") || "none"}; SARIF in-source suppressions ${tierPolicy.honor_sarif_suppressions ? "honored" : "ignored"}.`,
    "",
    "## Tools",
    "",
    ...[...tools].map(([n, v]) => `* ${n}${v ? ` ${v}` : ""}`),
    ...missingTools.map((t) => `* MISSING required tool: ${t}`),
    ...(tools.size ? [] : ["* No SARIF runs found"]),
    "",
    `Inputs: ${sarifFiles.map((f) => relative(repoRoot, f).replaceAll("\\", "/") || f).join(", ") || "none"}`,
    "",
    "## Summary",
    "",
    "| Severity | Blocking | Allowed | Suppressed (file) | Suppressed (source) |",
    "|----------|----------|---------|-------------------|---------------------|",
    ...[...SEVERITIES].reverse().map((s) => `| ${s} | ${count("blocking", s)} | ${count("allowed", s)} | ${count("suppressed", s)} | ${count("suppressed_in_source", s)} |`),
    "",
  ];
  if (problems.length) lines.push("## Suppression Problems", "", ...problems.map((p) => `* ${p}`), "");
  const section = (title: string, d: Finding["disposition"]): void => {
    const rows = findings.filter((f) => f.disposition === d);
    if (!rows.length) return;
    lines.push(`## ${title}`, "", "| Severity | Tool | Rule | Location | Message |", "|----------|------|------|----------|---------|");
    for (const f of rows.sort((a, b) => rank(b.severity) - rank(a.severity))) {
      const loc = f.path ? `${f.path}${f.line ? `:${f.line}` : ""}` : "";
      lines.push(`| ${f.severity} | ${f.tool} | \`${f.rule}\` | ${loc} | ${f.message.replaceAll("|", "\\|")}${f.reason ? ` (${f.reason.replaceAll("|", "\\|")})` : ""} |`);
    }
    lines.push("");
  };
  section("Blocking Findings", "blocking");
  section("Suppressed Findings", "suppressed");
  section("Allowed Findings (below threshold)", "allowed");
  lines.push("> AI-assisted content; review and validate before use.", "");
  return { text: lines.join("\n"), pass };
}

function main(): number {
  const { values } = parseArgs({
    options: {
      project: { type: "string" },
      "repo-root": { type: "string", default: process.cwd() },
      sarif: { type: "string", multiple: true, default: [] },
      suppressions: { type: "string" },
      policy: { type: "string", default: join(SKILL_ROOT, "assets", "severity-policy.json") },
      write: { type: "boolean", default: false },
      json: { type: "boolean", default: false },
    },
  });
  if (!values.project || !values.sarif!.length) {
    console.error("Usage: node triage-sarif.ts --project <slug> --sarif <file|dir> [--sarif ...] [--repo-root p] [--suppressions f] [--policy f] [--write] [--json]");
    return EXIT_ERROR;
  }
  const root = resolve(values["repo-root"]!);
  const projectDir = join(root, ".copilot-tracking", "sdlc", values.project);
  const today = new Date().toISOString().slice(0, 10);
  try {
    const tier = readRiskTier(join(projectDir, "charter.md"));
    const policy = JSON.parse(readFileSync(values.policy!, "utf8")) as Policy;
    const sarifFiles = collectSarif(values.sarif!.map((p) => resolve(root, p)));
    const { active, problems } = loadSuppressions(values.suppressions ? resolve(root, values.suppressions) : join(projectDir, "security-suppressions.json"), policy.suppression_max_days, today);
    const { findings, tools } = triage(sarifFiles, tier, policy, active, root);
    const present = [...tools.keys()].map((t) => t.toLowerCase());
    const missingTools = policy.tiers[tier]!.required_tools.filter((t) => !present.some((p) => p.includes(t.toLowerCase())));
    const { text, pass } = render(values.project, tier, policy, findings, tools, missingTools, problems, sarifFiles, root, today);

    if (values.json) {
      console.log(JSON.stringify({ project: values.project, tier, status: pass ? "pass" : "fail", tools: Object.fromEntries(tools), missing_tools: missingTools, suppression_problems: problems, findings }, null, 2));
    } else {
      console.log(text);
    }
    if (values.write) {
      const out = join(projectDir, "evidence", `security-${today}.md`);
      mkdirSync(join(out, ".."), { recursive: true });
      writeFileSync(out, text, "utf8");
      console.error(`INFO: Wrote ${relative(root, out).replaceAll("\\", "/")} (status: ${pass ? "pass" : "fail"})`);
    }
    return pass ? EXIT_SUCCESS : EXIT_FAILURE;
  } catch (error) {
    console.error(`ERROR: ${(error as Error).message}`);
    return EXIT_ERROR;
  }
}

process.exitCode = main();
