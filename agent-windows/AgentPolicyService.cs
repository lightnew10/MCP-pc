using System.Text.Json;

namespace McpPc.Agent;

internal static class AgentPolicyService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private static readonly string DataDirectory =
        Environment.GetEnvironmentVariable("MCP_PC_DATA_DIR")
        ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MCP-PC");

    private static readonly string PolicyFile = Path.Combine(DataDirectory, "policy.json");
    private static readonly string EmergencyStopFile = Path.Combine(DataDirectory, "STOP");
    private static readonly string AuditFile = Path.Combine(DataDirectory, "audit.jsonl");

    public static string DataDirectoryPath => DataDirectory;
    public static string PolicyFilePath => PolicyFile;
    public static string AuditFilePath => AuditFile;
    public static string EmergencyStopFilePath => EmergencyStopFile;
    public static bool IsEmergencyStopActive => File.Exists(EmergencyStopFile);

    private static readonly AgentPolicy DefaultPolicy = new(
        1,
        new PermissionPolicy(true, true, false),
        [],
        ["CredentialUIBroker", "LockApp", "LogonUI"],
        4000);

    public static void EnsureDefaultPolicy()
    {
        Directory.CreateDirectory(DataDirectory);
        if (File.Exists(PolicyFile)) return;

        File.WriteAllText(
            PolicyFile,
            JsonSerializer.Serialize(DefaultPolicy, JsonOptions) + Environment.NewLine);
    }

    public static AgentPolicy GetPolicy()
    {
        EnsureDefaultPolicy();

        var json = File.ReadAllText(PolicyFile);
        var policy = JsonSerializer.Deserialize<AgentPolicy>(json, JsonOptions)
            ?? throw new InvalidOperationException("MCP-PC policy.json is invalid.");

        return policy with
        {
            AllowedProcesses = policy.AllowedProcesses ?? [],
            DeniedProcesses = policy.DeniedProcesses ?? [],
            MaxTextLength = Math.Clamp(policy.MaxTextLength, 1, 20000)
        };
    }

    public static void AssertActionAllowed(string? processName)
    {
        if (File.Exists(EmergencyStopFile))
        {
            throw new InvalidOperationException(
                $"MCP-PC emergency stop is active. Remove {EmergencyStopFile} locally to resume ACTION tools.");
        }

        var policy = GetPolicy();
        if (!policy.Permissions.Action)
        {
            throw new InvalidOperationException("MCP-PC policy denies permission level ACTION.");
        }

        if (string.IsNullOrWhiteSpace(processName)) return;

        if (policy.DeniedProcesses.Any(
            value => string.Equals(value, processName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"MCP-PC policy denies actions for process {processName}.");
        }

        if (policy.AllowedProcesses.Count > 0 &&
            !policy.AllowedProcesses.Any(
                value => string.Equals(value, processName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"MCP-PC policy does not allow actions for process {processName}.");
        }
    }

    public static int GetMaxTextLength() => GetPolicy().MaxTextLength;

    public static void SetEmergencyStop(bool active)
    {
        Directory.CreateDirectory(DataDirectory);

        if (active)
        {
            File.WriteAllText(
                EmergencyStopFile,
                $"MCP-PC emergency stop enabled locally at {DateTimeOffset.UtcNow:O}{Environment.NewLine}");
            return;
        }

        if (File.Exists(EmergencyStopFile))
        {
            File.Delete(EmergencyStopFile);
        }
    }
}

internal sealed record PermissionPolicy(bool Read, bool Action, bool Dangerous);

internal sealed record AgentPolicy(
    int Version,
    PermissionPolicy Permissions,
    IReadOnlyList<string> AllowedProcesses,
    IReadOnlyList<string> DeniedProcesses,
    int MaxTextLength);
