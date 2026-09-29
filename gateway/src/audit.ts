import { appendFile, mkdir, readFile } from "node:fs/promises";
import path from "node:path";

export type PermissionLevel = "READ" | "ACTION" | "DANGEROUS";

export interface AuditEntry {
  timestamp: string;
  client: string;
  tool: string;
  permission: PermissionLevel;
  arguments: Record<string, unknown>;
  status: "success" | "error";
  durationMs: number;
  error?: string;
}

const dataDir =
  process.env.MCP_PC_DATA_DIR ??
  path.join(process.env.LOCALAPPDATA ?? process.cwd(), "MCP-PC");

const auditFile = path.join(dataDir, "audit.jsonl");

export async function writeAudit(entry: AuditEntry): Promise<void> {
  await mkdir(dataDir, { recursive: true });
  await appendFile(auditFile, `${JSON.stringify(entry)}\n`, "utf8");
}

export async function readRecentAudit(limit = 50): Promise<AuditEntry[]> {
  const safeLimit = Math.min(Math.max(limit, 1), 500);

  try {
    const content = await readFile(auditFile, "utf8");
    return content
      .split(/\r?\n/)
      .filter(Boolean)
      .slice(-safeLimit)
      .map((line) => JSON.parse(line) as AuditEntry);
  } catch (error) {
    if ((error as NodeJS.ErrnoException).code === "ENOENT") return [];
    throw error;
  }
}
