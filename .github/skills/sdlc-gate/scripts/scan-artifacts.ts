#!/usr/bin/env node
/**
 * Data boundary scan for lifecycle artifacts.
 *
 * Scans text files under .copilot-tracking/ (or given paths) for secrets and
 * personal identifiers that must not be committed. Files whose frontmatter
 * declares `contains_customer_data: true` are reported but not failed, per the
 * data-classification instructions. Exit 0 clean, 1 findings, 2 usage error.
 * Node 24+ built-ins only. Complements, does not replace, a full secret
 * scanner such as gitleaks over the whole repository.
 */

import { parseArgs } from "node:util";
import { existsSync, readFileSync, readdirSync, statSync } from "node:fs";
import { extname, join, relative, resolve } from "node:path";

const EXIT_SUCCESS = 0;
const EXIT_FAILURE = 1;
const EXIT_ERROR = 2;

const TEXT_SUFFIXES = new Set([".md", ".json", ".yml", ".yaml", ".txt", ".csv", ".bicepparam", ".env"]);
const SKIP_DIRS = new Set([".git", "node_modules", "audit"]);
const DEFAULT_ALLOWED_DOMAINS = ["example.com", "example.org", "contoso.com", "fabrikam.com", "microsoft.com"];
const PLACEHOLDER = /\{\{[^}]+\}\}/g;

type Rule = { id: string; severity: "secret" | "pii"; pattern: RegExp };
const RULES: Rule[] = [
  { id: "private-key", severity: "secret", pattern: /-----BEGIN [A-Z ]*PRIVATE KEY-----/g },
  { id: "connection-string", severity: "secret", pattern: /\b(AccountKey|SharedAccessKey|SharedAccessSignature|Password|Pwd)\s*=\s*[^;\s"'`]{8,}/gi },
  { id: "aws-access-key", severity: "secret", pattern: /\bAKIA[0-9A-Z]{16}\b/g },
  { id: "github-token", severity: "secret", pattern: /\bgh[pousr]_[A-Za-z0-9]{36,}\b/g },
  { id: "jwt", severity: "secret", pattern: /\beyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\b/g },
  { id: "api-key-prefix", severity: "secret", pattern: /\b(sk|pk|rk)-[A-Za-z0-9]{20,}\b/g },
  { id: "azure-sas", severity: "secret", pattern: /[?&]sig=[A-Za-z0-9%+/=]{20,}/g },
  { id: "email", severity: "pii", pattern: /\b[A-Za-z0-9._%+-]+@([A-Za-z0-9-]+\.)+[A-Za-z]{2,}\b/g },
];

type Finding = { file: string; line: number; rule: string; severity: string; sample: string };

function* walk(dir: string): Generator<string> {
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    const full = join(dir, entry.name);
    if (entry.isDirectory()) {
      if (!SKIP_DIRS.has(entry.name)) yield* walk(full);
    } else if (entry.isFile() && TEXT_SUFFIXES.has(extname(entry.name).toLowerCase())) {
      yield full;
    }
  }
}

function declaresCustomerData(content: string): boolean {
  const m = /^---\r?\n([\s\S]*?)\r?\n---/.exec(content);
  return !!m && /^contains_customer_data:\s*true\s*$/m.test(m[1]!);
}

function redact(sample: string): string {
  return sample.length <= 6 ? "***" : `${sample.slice(0, 4)}***${sample.slice(-2)}`;
}

function isGateApproverEmail(rel: string, line: string): boolean {
  return /\/gates\/[a-z]+\.json$/.test(rel) && /"approved_by"\s*:/.test(line);
}

function scanFile(root: string, path: string, allowedDomains: Set<string>): { findings: Finding[]; declared: boolean } {
  const rel = relative(root, path).replaceAll("\\", "/");
  const content = readFileSync(path, "utf8");
  const declared = declaresCustomerData(content);
  const findings: Finding[] = [];
  content.split(/\r?\n/).forEach((rawLine, i) => {
    // Role-based placeholders such as {{customer-admin}} are the sanctioned redaction form.
    const line = rawLine.replaceAll(PLACEHOLDER, "");
    for (const rule of RULES) {
      for (const m of line.matchAll(rule.pattern)) {
        const sample = m[0];
        if (rule.id === "email") {
          const domain = sample.split("@")[1]!.toLowerCase();
          if (allowedDomains.has(domain) || isGateApproverEmail(rel, line)) continue;
        }
        findings.push({ file: rel, line: i + 1, rule: rule.id, severity: rule.severity, sample: redact(sample) });
      }
    }
  });
  return { findings, declared };
}

function main(): number {
  const { values, positionals } = parseArgs({
    allowPositionals: true,
    options: {
      "repo-root": { type: "string", default: process.cwd() },
      "allow-domain": { type: "string", multiple: true, default: [] },
      json: { type: "boolean", default: false },
    },
  });
  const root = resolve(values["repo-root"]!);
  const targets = (positionals.length ? positionals : [".copilot-tracking"]).map((p) => resolve(root, p));
  const allowedDomains = new Set([...DEFAULT_ALLOWED_DOMAINS, ...values["allow-domain"]!.map((d) => d.toLowerCase())]);

  const findings: Finding[] = [];
  const declaredFiles: string[] = [];
  let scanned = 0;
  for (const target of targets) {
    if (!existsSync(target)) continue;
    const files = statSync(target).isDirectory() ? [...walk(target)] : [target];
    for (const file of files) {
      scanned++;
      const { findings: f, declared } = scanFile(root, file, allowedDomains);
      if (declared) {
        declaredFiles.push(relative(root, file).replaceAll("\\", "/"));
        continue;
      }
      findings.push(...f);
    }
  }

  if (values.json) {
    console.log(JSON.stringify({ scanned, findings, declared_customer_data: declaredFiles }, null, 2));
  } else {
    console.log(`scanned ${scanned} file(s); ${findings.length} finding(s)`);
    for (const f of findings) console.log(`  ${f.severity.padEnd(6)} ${f.rule.padEnd(18)} ${f.file}:${f.line}  ${f.sample}`);
    for (const d of declaredFiles) console.log(`  note   declared           ${d}  (contains_customer_data: true; excluded from export)`);
  }
  return findings.length ? EXIT_FAILURE : EXIT_SUCCESS;
}

process.exitCode = main();
