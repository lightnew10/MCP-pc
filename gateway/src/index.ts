import { McpServer } from "@modelcontextprotocol/server";
import { serveStdio } from "@modelcontextprotocol/server/stdio";
import * as z from "zod/v4";
import { callAgent } from "./agentClient.js";
import {
  readRecentAudit,
  writeAudit,
  type PermissionLevel,
} from "./audit.js";
import {
  assertPermission,
  ensurePolicy,
  getPolicyStatus,
} from "./policy.js";

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

function textResult(value: unknown) {
  return {
    content: [{ type: "text" as const, text: JSON.stringify(value, null, 2) }],
  };
}

async function authorizeAction(args: Record<string, unknown>): Promise<void> {
  const windowId = typeof args.windowId === "string" ? args.windowId : undefined;

  if (!windowId) {
    await assertPermission("ACTION");
    return;
  }

  const windows = await callAgent<WindowInfo[]>("windows.list", {});
  const target = windows.find(
    (window) => window.id.toLowerCase() === windowId.toLowerCase(),
  );

  if (!target) {
    throw new Error(`Window does not exist or is not visible: ${windowId}`);
  }

  await assertPermission("ACTION", target.processName);
}

async function auditedAgentCall<T>(
  tool: string,
  method: string,
  args: Record<string, unknown>,
  permission: PermissionLevel = "READ",
  auditArguments: Record<string, unknown> = args,
): Promise<T> {
  const started = Date.now();

  try {
    if (permission === "ACTION") {
      await authorizeAction(args);
    } else {
      await assertPermission(permission);
    }

    const result = await callAgent<T>(method, args);
    await writeAudit({
      timestamp: new Date().toISOString(),
      client: "mcp",
      tool,
      permission,
      arguments: auditArguments,
      status: "success",
      durationMs: Date.now() - started,
    });
    return result;
  } catch (error) {
    await writeAudit({
      timestamp: new Date().toISOString(),
      client: "mcp",
      tool,
      permission,
      arguments: auditArguments,
      status: "error",
      durationMs: Date.now() - started,
      error: errorMessage(error),
    });
    throw error;
  }
}

function createServer(): McpServer {
  const server = new McpServer(
    { name: "mcp-pc", version: "0.3.0" },
    {
      instructions:
        "Windows observation and permission-gated desktop ACTION tools. ACTION tools are window-targeted and use coordinates relative to the selected window. DANGEROUS capabilities such as shell execution, file writes, process termination, and elevation are not exposed.",
    },
  );

  server.registerTool(
    "desktop.list_windows",
    {
      title: "List desktop windows",
      description:
        "List visible top-level Windows desktop windows with ids, process metadata, and bounds.",
      inputSchema: z.object({}),
      annotations: {
        readOnlyHint: true,
        destructiveHint: false,
        openWorldHint: false,
      },
    },
    async () => {
      try {
        return textResult(
          await auditedAgentCall<WindowInfo[]>(
            "desktop.list_windows",
            "windows.list",
            {},
          ),
        );
      } catch (error) {
        return {
          isError: true,
          content: [{ type: "text", text: errorMessage(error) }],
        };
      }
    },
  );

  server.registerTool(
    "desktop.get_active_window",
    {
      title: "Get active window",
      description: "Return the current foreground Windows desktop window.",
      inputSchema: z.object({}),
      annotations: {
        readOnlyHint: true,
        destructiveHint: false,
        openWorldHint: false,
      },
    },
    async () => {
      try {
        return textResult(
          await auditedAgentCall<WindowInfo | null>(
            "desktop.get_active_window",
            "windows.active",
            {},
          ),
        );
      } catch (error) {
        return {
          isError: true,
          content: [{ type: "text", text: errorMessage(error) }],
        };
      }
    },
  );

  server.registerTool(
    "desktop.get_cursor_position",
    {
      title: "Get cursor position",
      description: "Return the current cursor position in virtual-desktop coordinates.",
      inputSchema: z.object({}),
      annotations: {
        readOnlyHint: true,
        destructiveHint: false,
        openWorldHint: false,
      },
    },
    async () => {
      try {
        return textResult(
          await auditedAgentCall(
            "desktop.get_cursor_position",
            "cursor.position",
            {},
          ),
        );
      } catch (error) {
        return {
          isError: true,
          content: [{ type: "text", text: errorMessage(error) }],
        };
      }
    },
  );

  server.registerTool(
    "desktop.capture_screen",
    {
      title: "Capture desktop",
      description: "Capture the complete Windows virtual desktop and return a PNG image.",
      inputSchema: z.object({}),
      annotations: {
        readOnlyHint: true,
        destructiveHint: false,
        openWorldHint: false,
      },
    },
    async () => {
      try {
        const shot = await auditedAgentCall<ScreenshotResult>(
          "desktop.capture_screen",
          "screen.capture",
          {},
        );
        return {
          content: [
            {
              type: "text" as const,
              text: JSON.stringify(
                {
                  width: shot.width,
                  height: shot.height,
                  captureMode: shot.captureMode,
                },
                null,
                2,
              ),
            },
            {
              type: "image" as const,
              data: shot.imageBase64,
              mimeType: shot.mimeType,
            },
          ],
        };
      } catch (error) {
        return {
          isError: true,
          content: [{ type: "text", text: errorMessage(error) }],
        };
      }
    },
  );

  server.registerTool(
    "desktop.capture_window",
    {
      title: "Capture window",
      description:
        "Capture a specific non-minimized window and return a PNG image. The Windows agent prefers an occlusion-resistant window render and falls back to the visible screen region when needed.",
      inputSchema: z.object({
        windowId: z
          .string()
          .min(1)
          .describe("Window id returned by desktop.list_windows"),
      }),
      annotations: {
        readOnlyHint: true,
        destructiveHint: false,
        openWorldHint: false,
      },
    },
    async ({ windowId }) => {
      try {
        const args = { windowId };
        const shot = await auditedAgentCall<ScreenshotResult>(
          "desktop.capture_window",
          "screen.captureWindow",
          args,
        );
        return {
          content: [
            {
              type: "text" as const,
              text: JSON.stringify(
                {
                  windowId,
                  width: shot.width,
                  height: shot.height,
                  captureMode: shot.captureMode,
                },
                null,
                2,
              ),
            },
            {
              type: "image" as const,
              data: shot.imageBase64,
              mimeType: shot.mimeType,
            },
          ],
        };
      } catch (error) {
        return {
          isError: true,
          content: [{ type: "text", text: errorMessage(error) }],
        };
      }
    },
  );

  const windowTargetSchema = {
    windowId: z
      .string()
      .min(1)
      .describe("Window id returned by desktop.list_windows"),
  };

  const pointSchema = {
    ...windowTargetSchema,
    x: z
      .number()
      .int()
      .min(0)
      .describe("X coordinate relative to the captured window"),
    y: z
      .number()
      .int()
      .min(0)
      .describe("Y coordinate relative to the captured window"),
  };

  server.registerTool(
    "desktop.focus_window",
    {
      title: "Focus window",
      description: "Bring an allowed target window to the foreground.",
      inputSchema: z.object(windowTargetSchema),
      annotations: {
        readOnlyHint: false,
        destructiveHint: false,
        openWorldHint: false,
      },
    },
    async ({ windowId }) => {
      try {
        return textResult(
          await auditedAgentCall(
            "desktop.focus_window",
            "windows.focus",
            { windowId },
            "ACTION",
          ),
        );
      } catch (error) {
        return {
          isError: true,
          content: [{ type: "text", text: errorMessage(error) }],
        };
      }
    },
  );

  server.registerTool(
    "desktop.move_mouse",
    {
      title: "Move mouse",
      description:
        "Move the mouse to coordinates relative to an allowed target window.",
      inputSchema: z.object(pointSchema),
      annotations: {
        readOnlyHint: false,
        destructiveHint: false,
        openWorldHint: false,
      },
    },
    async ({ windowId, x, y }) => {
      try {
        return textResult(
          await auditedAgentCall(
            "desktop.move_mouse",
            "input.moveMouse",
            { windowId, x, y },
            "ACTION",
          ),
        );
      } catch (error) {
        return {
          isError: true,
          content: [{ type: "text", text: errorMessage(error) }],
        };
      }
    },
  );

  const mouseButton = z.enum(["left", "right", "middle"]).default("left");

  server.registerTool(
    "desktop.click",
    {
      title: "Click",
      description:
        "Click at coordinates relative to an allowed target window.",
      inputSchema: z.object({ ...pointSchema, button: mouseButton }),
      annotations: {
        readOnlyHint: false,
        destructiveHint: false,
        openWorldHint: false,
      },
    },
    async ({ windowId, x, y, button }) => {
      try {
        return textResult(
          await auditedAgentCall(
            "desktop.click",
            "input.click",
            { windowId, x, y, button },
            "ACTION",
          ),
        );
      } catch (error) {
        return {
          isError: true,
          content: [{ type: "text", text: errorMessage(error) }],
        };
      }
    },
  );

  server.registerTool(
    "desktop.double_click",
    {
      title: "Double click",
      description:
        "Double-click at coordinates relative to an allowed target window.",
      inputSchema: z.object({ ...pointSchema, button: mouseButton }),
      annotations: {
        readOnlyHint: false,
        destructiveHint: false,
        openWorldHint: false,
      },
    },
    async ({ windowId, x, y, button }) => {
      try {
        return textResult(
          await auditedAgentCall(
            "desktop.double_click",
            "input.doubleClick",
            { windowId, x, y, button },
            "ACTION",
          ),
        );
      } catch (error) {
        return {
          isError: true,
          content: [{ type: "text", text: errorMessage(error) }],
        };
      }
    },
  );

  server.registerTool(
    "desktop.scroll",
    {
      title: "Scroll",
      description:
        "Scroll at coordinates relative to an allowed target window. Positive delta scrolls up and negative delta scrolls down.",
      inputSchema: z.object({
        ...pointSchema,
        delta: z.number().int().min(-1200).max(1200).refine((value) => value !== 0),
      }),
      annotations: {
        readOnlyHint: false,
        destructiveHint: false,
        openWorldHint: false,
      },
    },
    async ({ windowId, x, y, delta }) => {
      try {
        return textResult(
          await auditedAgentCall(
            "desktop.scroll",
            "input.scroll",
            { windowId, x, y, delta },
            "ACTION",
          ),
        );
      } catch (error) {
        return {
          isError: true,
          content: [{ type: "text", text: errorMessage(error) }],
        };
      }
    },
  );

  server.registerTool(
    "desktop.type_text",
    {
      title: "Type text",
      description:
        "Focus an allowed target window and type Unicode text. Text content is redacted from the audit log.",
      inputSchema: z.object({
        ...windowTargetSchema,
        text: z.string().min(1).max(20000),
      }),
      annotations: {
        readOnlyHint: false,
        destructiveHint: false,
        openWorldHint: false,
      },
    },
    async ({ windowId, text }) => {
      try {
        return textResult(
          await auditedAgentCall(
            "desktop.type_text",
            "input.typeText",
            { windowId, text },
            "ACTION",
            { windowId, textLength: text.length, text: "[REDACTED]" },
          ),
        );
      } catch (error) {
        return {
          isError: true,
          content: [{ type: "text", text: errorMessage(error) }],
        };
      }
    },
  );

  server.registerTool(
    "desktop.press_key",
    {
      title: "Press key",
      description:
        "Focus an allowed target window and press one named key such as Enter, Escape, Tab, F5, or a letter.",
      inputSchema: z.object({
        ...windowTargetSchema,
        key: z.string().min(1).max(32),
      }),
      annotations: {
        readOnlyHint: false,
        destructiveHint: false,
        openWorldHint: false,
      },
    },
    async ({ windowId, key }) => {
      try {
        return textResult(
          await auditedAgentCall(
            "desktop.press_key",
            "input.pressKey",
            { windowId, key },
            "ACTION",
          ),
        );
      } catch (error) {
        return {
          isError: true,
          content: [{ type: "text", text: errorMessage(error) }],
        };
      }
    },
  );

  server.registerTool(
    "desktop.key_combination",
    {
      title: "Press key combination",
      description:
        "Focus an allowed target window and press a key with Ctrl, Alt, Shift, or Win modifiers.",
      inputSchema: z.object({
        ...windowTargetSchema,
        key: z.string().min(1).max(32),
        modifiers: z
          .array(z.enum(["ctrl", "alt", "shift", "win"]))
          .max(4)
          .default([]),
      }),
      annotations: {
        readOnlyHint: false,
        destructiveHint: false,
        openWorldHint: false,
      },
    },
    async ({ windowId, key, modifiers }) => {
      try {
        return textResult(
          await auditedAgentCall(
            "desktop.key_combination",
            "input.keyCombination",
            { windowId, key, modifiers },
            "ACTION",
          ),
        );
      } catch (error) {
        return {
          isError: true,
          content: [{ type: "text", text: errorMessage(error) }],
        };
      }
    },
  );

  server.registerTool(
    "policy.get_status",
    {
      title: "Get MCP-PC policy status",
      description:
        "Read the local permission policy and emergency-stop status. This tool cannot disable the emergency stop.",
      inputSchema: z.object({}),
      annotations: {
        readOnlyHint: true,
        destructiveHint: false,
        openWorldHint: false,
      },
    },
    async () => {
      const started = Date.now();
      try {
        await assertPermission("READ");
        const status = await getPolicyStatus();
        await writeAudit({
          timestamp: new Date().toISOString(),
          client: "mcp",
          tool: "policy.get_status",
          permission: "READ",
          arguments: {},
          status: "success",
          durationMs: Date.now() - started,
        });
        return textResult(status);
      } catch (error) {
        await writeAudit({
          timestamp: new Date().toISOString(),
          client: "mcp",
          tool: "policy.get_status",
          permission: "READ",
          arguments: {},
          status: "error",
          durationMs: Date.now() - started,
          error: errorMessage(error),
        });
        return {
          isError: true,
          content: [{ type: "text", text: errorMessage(error) }],
        };
      }
    },
  );

  server.registerTool(
    "audit.get_recent_logs",
    {
      title: "Get recent audit logs",
      description:
        "Read recent MCP-PC audit records. Screenshot bytes and typed text are not written to the audit log.",
      inputSchema: z.object({
        limit: z.number().int().min(1).max(500).default(50),
      }),
      annotations: {
        readOnlyHint: true,
        destructiveHint: false,
        openWorldHint: false,
      },
    },
    async ({ limit }) => {
      try {
        await assertPermission("READ");
        return textResult(await readRecentAudit(limit));
      } catch (error) {
        return {
          isError: true,
          content: [{ type: "text", text: errorMessage(error) }],
        };
      }
    },
  );

  return server;
}

void ensurePolicy().catch((error) => {
  console.error(`Unable to initialize MCP-PC policy: ${errorMessage(error)}`);
});

void serveStdio(createServer);
console.error("MCP-PC gateway v0.3.0 listening on stdio");
