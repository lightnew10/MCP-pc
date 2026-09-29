import net from "node:net";
import { randomUUID } from "node:crypto";

const PIPE_PATH = process.env.MCP_PC_PIPE ?? "\\\\.\\pipe\\mcp-pc-agent";
const REQUEST_TIMEOUT_MS = Number(process.env.MCP_PC_AGENT_TIMEOUT_MS ?? 10_000);

interface AgentResponse<T> {
  id: string;
  ok: boolean;
  result?: T;
  error?: string;
}

export async function callAgent<T>(
  method: string,
  params: Record<string, unknown> = {},
): Promise<T> {
  const id = randomUUID();

  return await new Promise<T>((resolve, reject) => {
    const socket = net.createConnection(PIPE_PATH);
    let buffer = "";
    let settled = false;

    const finish = (callback: () => void) => {
      if (settled) return;
      settled = true;
      clearTimeout(timer);
      callback();
    };

    const timer = setTimeout(() => {
      socket.destroy();
      finish(() => reject(new Error(`Windows agent request timed out after ${REQUEST_TIMEOUT_MS} ms`)));
    }, REQUEST_TIMEOUT_MS);

    socket.on("connect", () => {
      socket.write(`${JSON.stringify({ id, method, params })}\n`);
    });

    socket.on("data", (chunk) => {
      buffer += chunk.toString("utf8");
      const newlineIndex = buffer.indexOf("\n");
      if (newlineIndex < 0) return;

      const line = buffer.slice(0, newlineIndex).trim();
      socket.end();

      try {
        const response = JSON.parse(line) as AgentResponse<T>;
        if (response.id !== id) {
          throw new Error("Windows agent returned a mismatched response id");
        }
        if (!response.ok) {
          throw new Error(response.error ?? "Windows agent request failed");
        }
        finish(() => resolve(response.result as T));
      } catch (error) {
        finish(() => reject(error instanceof Error ? error : new Error(String(error))));
      }
    });

    socket.on("error", (error) => {
      finish(() => reject(new Error(`Windows agent unavailable at ${PIPE_PATH}: ${error.message}`)));
    });

    socket.on("close", () => {
      if (!settled) {
        finish(() => reject(new Error("Windows agent closed the connection without a response")));
      }
    });
  });
}
