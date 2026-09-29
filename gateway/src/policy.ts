import { access, mkdir, readFile, writeFile } from "node:fs/promises";
import path from "node:path";
import type { PermissionLevel } from "./audit.js";

export interface PolicyConfig {
  version: 1;
  permissions: Record<PermissionLevel, boolean>;
  allowedProcesses: string[];
  deniedProcesses: string[];
  maxTextLength: number;
}

export interface PolicyStatus {
  config: PolicyConfig;
  emergencyStop: boolean;
  policyFile: string;
  emergencyStopFile: string;
}

const dataDir =
  process.env.MCP_PC_DATA_DIR ??
  path.join(process.env.LOCALAPPDATA ?? process.cwd(), "MCP-PC");

const policyFile = path.join(dataDir, "policy.json");
const emergencyStopFile = path.join(dataDir, "STOP");

const defaultPolicy: PolicyConfig = {
  version: 1,
  permissions: {
    READ: true,
    ACTION: true,
    DANGEROUS: false,
  },
  allowedProcesses: [],
  deniedProcesses: ["CredentialUIBroker", "LockApp", "LogonUI"],
  maxTextLength: 4000,
};

async function fileExists(file: string): Promise<boolean> {
  try {
    await access(file);
    return true;
  } catch {
    return false;
  }
}

export async function ensurePolicy(): Promise<PolicyConfig> {
  await mkdir(dataDir, { recursive: true });

  if (!(await fileExists(policyFile))) {
    await writeFile(policyFile, `${JSON.stringify(defaultPolicy, null, 2)}\n`, "utf8");
    return defaultPolicy;
  }

  return readPolicy();
}

export async function readPolicy(): Promise<PolicyConfig> {
  await mkdir(dataDir, { recursive: true });

  try {
    const parsed = JSON.parse(await readFile(policyFile, "utf8")) as Partial<PolicyConfig>;
    return {
      version: 1,
      permissions: {
        READ: parsed.permissions?.READ ?? true,
        ACTION: parsed.permissions?.ACTION ?? true,
        DANGEROUS: parsed.permissions?.DANGEROUS ?? false,
      },
      allowedProcesses: parsed.allowedProcesses ?? [],
      deniedProcesses: parsed.deniedProcesses ?? defaultPolicy.deniedProcesses,
      maxTextLength: Math.min(Math.max(parsed.maxTextLength ?? 4000, 1), 20000),
    };
  } catch (error) {
    if ((error as NodeJS.ErrnoException).code === "ENOENT") {
      return ensurePolicy();
    }
    throw error;
  }
}

export async function getPolicyStatus(): Promise<PolicyStatus> {
  const config = await ensurePolicy();
  return {
    config,
    emergencyStop: await fileExists(emergencyStopFile),
    policyFile,
    emergencyStopFile,
  };
}

export async function assertPermission(
  permission: PermissionLevel,
  processName?: string | null,
): Promise<PolicyConfig> {
  const status = await getPolicyStatus();

  if (permission !== "READ" && status.emergencyStop) {
    throw new Error(
      `MCP-PC emergency stop is active. Remove ${status.emergencyStopFile} locally to resume ACTION tools.`,
    );
  }

  if (!status.config.permissions[permission]) {
    throw new Error(`MCP-PC policy denies permission level ${permission}.`);
  }

  if (permission !== "READ" && processName) {
    const normalized = processName.toLowerCase();
    const denied = status.config.deniedProcesses.some(
      (value) => value.toLowerCase() === normalized,
    );

    if (denied) {
      throw new Error(`MCP-PC policy denies actions for process ${processName}.`);
    }

    if (
      status.config.allowedProcesses.length > 0 &&
      !status.config.allowedProcesses.some((value) => value.toLowerCase() === normalized)
    ) {
      throw new Error(
        `MCP-PC policy does not allow actions for process ${processName}.`,
      );
    }
  }

  return status.config;
}
