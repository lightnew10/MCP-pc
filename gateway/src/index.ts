import { McpServer } from "@modelcontextprotocol/server";
import { serveStdio } from "@modelcontextprotocol/server/stdio";
import * as z from "zod/v4";
import { callAgent } from "./agentClient.js";
import { readRecentAudit, writeAudit } from "./audit.js";

interface WindowInfo {
  id: string;
  title: string;
  pid: number;
  processName?: string;
  bounds: { x: number; y: number; width: number; height: number };
}

interface ScreenshotResult {
  mimeType: "image/png";
  width: number;
  height: number;
  captureMode: string;
  imageBase64: string;
}

function errorMessage(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}

async function auditedAgentCall<T>(
  tool: string,
  method: string,
  args: Record<string, unknown>,
): Promise<T> {
  const started = Date.now();
  try {
    const result = await callAgent<T>(method, args);
    await writeAudit({
      timestamp: new Date().toISOString(),
      client: "mcp",
      tool,
      permission: "READ",
      arguments: args,
      status: "success",
      durationMs: Date.now() - started,
    });
    return result;
  } catch (error) {
    await writeAudit({
      timestamp: new Date().toISOString(),
      client: "mcp",
      tool,
      permission: "READ",
      arguments: args,
      status: "error",
      durationMs: Date.now() - started,
      error: errorMessage(error),
    });
    throw error;
  }
}

function createServer(): McpServer {
  const server = new McpServer(
    { name: "mcp-pc", version: "0.1.0" },
    {
      instructions:
        "Read-only Windows observation tools. Do not infer that mouse, keyboard, shell, process-control, or file-write capabilities exist.",
    },
  );

  server.registerTool(
    "desktop.list_windows",
    {
      title: "List desktop windows",
      description: "List visible top-level Windows desktop windows with ids, process metadata, and bounds.",
      inputSchema: z.object({}),
      annotations: { readOnlyHint: true, destructiveHint: false, openWorldHint: false },
    },
    async () => {
      try {
        const windows = await auditedAgentCall<WindowInfo[]>("desktop.list_windows", "windows.list", {});
        return { content: [{ type: "text", text: JSON.stringify(windows, null, 2) }] };
      } catch (error) {
        return { isError: true, content: [{ type: "text", text: errorMessage(error) }] };
      }
    },
  );

  server.registerTool(
    "desktop.get_active_window",
    {
      title: "Get active window",
      description: "Return the current foreground Windows desktop window.",
      inputSchema: z.object({}),
      annotations: { readOnlyHint: true, destructiveHint: false, openWorldHint: false },
    },
    async () => {
      try {
        const window = await auditedAgentCall<WindowInfo | null>("desktop.get_active_window", "windows.active", {});
        return { content: [{ type: "text", text: JSON.stringify(window, null, 2) }] };
      } catch (error) {
        return { isError: true, content: [{ type: "text", text: errorMessage(error) }] };
      }
    },
  );

  server.registerTool(
    "desktop.capture_screen",
    {
      title: "Capture desktop",
      description: "Capture the complete Windows virtual desktop and return a PNG image.",
      inputSchema: z.object({}),
      annotations: { readOnlyHint: true, destructiveHint: false, openWorldHint: false },
    },
    async () => {
      try {
        const shot = await auditedAgentCall<ScreenshotResult>("desktop.capture_screen", "screen.capture", {});
        return {
          content: [
            {
              type: "text",
              text: JSON.stringify({ width: shot.width, height: shot.height, captureMode: shot.captureMode }, null, 2),
            },
            { type: "image", data: shot.imageBase64, mimeType: shot.mimeType },
          ],
        };
      } catch (error) {
        return { isError: true, content: [{ type: "text", text: errorMessage(error) }] };
      }
    },
  );

  server.registerTool(
    "desktop.capture_window",
    {
      title: "Capture window",
      description: "Capture the visible screen region occupied by a specific non-minimized window and return a PNG image.",
      inputSchema: z.object({ windowId: z.string().min(1).describe("Window id returned by desktop.list_windows") }),
      annotations: { readOnlyHint: true, destructiveHint: false, openWorldHint: false },
    },
    async ({ windowId }) => {
      try {
        const args = { windowId };
        const shot = await auditedAgentCall<ScreenshotResult>("desktop.capture_window", "screen.captureWindow", args);
        return {
          content: [
            {
              type: "text",
              text: JSON.stringify({ windowId, width: shot.width, height: shot.height, captureMode: shot.captureMode }, null, 2),
            },
            { type: "image", data: shot.imageBase64, mimeType: shot.mimeType },
          ],
        };
      } catch (error) {
        return { isError: true, content: [{ type: "text", text: errorMessage(error) }] };
      }
    },
  );

  server.registerTool(
    "audit.get_recent_logs",
    {
      title: "Get recent audit logs",
      description: "Read recent MCP-PC audit records. Screenshot image bytes are never written to the audit log.",
      inputSchema: z.object({ limit: z.number().int().min(1).max(500).default(50) }),
      annotations: { readOnlyHint: true, destructiveHint: false, openWorldHint: false },
    },
    async ({ limit }) => {
      try {
        const logs = await readRecentAudit(limit);
        return { content: [{ type: "text", text: JSON.stringify(logs, null, 2) }] };
      } catch (error) {
        return { isError: true, content: [{ type: "text", text: errorMessage(error) }] };
      }
    },
  );

  return server;
}

void serveStdio(createServer);
console.error("MCP-PC gateway listening on stdio");
