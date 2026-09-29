using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace McpPc.Agent;

internal static class AgentTrayController
{
    public static void Start()
    {
        if (!Environment.UserInteractive ||
            string.Equals(
                Environment.GetEnvironmentVariable("MCP_PC_NO_TRAY"),
                "1",
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var thread = new Thread(() =>
        {
            ApplicationConfiguration.Initialize();
            Application.Run(new AgentTrayContext());
        })
        {
            IsBackground = true,
            Name = "MCP-PC Tray"
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    private sealed class AgentTrayContext : ApplicationContext
    {
        private readonly NotifyIcon _notifyIcon;
        private readonly ToolStripMenuItem _statusItem;
        private readonly ToolStripMenuItem _stopItem;
        private readonly System.Windows.Forms.Timer _refreshTimer;

        public AgentTrayContext()
        {
            _statusItem = new ToolStripMenuItem
            {
                Enabled = false
            };

            _stopItem = new ToolStripMenuItem("Emergency STOP")
            {
                CheckOnClick = false
            };
            _stopItem.Click += (_, _) => ToggleEmergencyStop();

            var openPolicy = new ToolStripMenuItem("Open policy.json");
            openPolicy.Click += (_, _) => OpenPath(AgentPolicyService.PolicyFilePath);

            var openAudit = new ToolStripMenuItem("Open audit log");
            openAudit.Click += (_, _) => OpenPath(AgentPolicyService.AuditFilePath);

            var openFolder = new ToolStripMenuItem("Open MCP-PC data folder");
            openFolder.Click += (_, _) => OpenPath(AgentPolicyService.DataDirectoryPath);

            var exit = new ToolStripMenuItem("Exit MCP-PC agent");
            exit.Click += (_, _) =>
            {
                _notifyIcon.Visible = false;
                Environment.Exit(0);
            };

            var menu = new ContextMenuStrip();
            menu.Items.Add(_statusItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(_stopItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(openPolicy);
            menu.Items.Add(openAudit);
            menu.Items.Add(openFolder);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(exit);

            _notifyIcon = new NotifyIcon
            {
                Icon = SystemIcons.Shield,
                Text = "MCP-PC v0.3.0",
                ContextMenuStrip = menu,
                Visible = true
            };

            _notifyIcon.DoubleClick += (_, _) => OpenPath(AgentPolicyService.DataDirectoryPath);

            _refreshTimer = new System.Windows.Forms.Timer
            {
                Interval = 1000,
                Enabled = true
            };
            _refreshTimer.Tick += (_, _) => RefreshStatus();

            RefreshStatus();
        }

        private void RefreshStatus()
        {
            try
            {
                var policy = AgentPolicyService.GetPolicy();
                var stopped = AgentPolicyService.IsEmergencyStopActive;

                _statusItem.Text = stopped
                    ? "Status: STOPPED"
                    : policy.Permissions.Action
                        ? "Status: ACTION enabled"
                        : "Status: READ only";

                _stopItem.Checked = stopped;
                _stopItem.Text = stopped
                    ? "Emergency STOP (active) - click to resume"
                    : "Emergency STOP - click to block ACTION";

                _notifyIcon.Text = stopped
                    ? "MCP-PC v0.3.0 - STOPPED"
                    : policy.Permissions.Action
                        ? "MCP-PC v0.3.0 - ACTION enabled"
                        : "MCP-PC v0.3.0 - READ only";
            }
            catch
            {
                _statusItem.Text = "Status: policy error";
                _notifyIcon.Text = "MCP-PC v0.3.0 - policy error";
            }
        }

        private void ToggleEmergencyStop()
        {
            try
            {
                AgentPolicyService.SetEmergencyStop(!AgentPolicyService.IsEmergencyStopActive);
                RefreshStatus();
            }
            catch (Exception exception)
            {
                MessageBox.Show(
                    exception.Message,
                    "MCP-PC",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private static void OpenPath(string path)
        {
            try
            {
                if (!File.Exists(path) && !Directory.Exists(path))
                {
                    if (string.Equals(path, AgentPolicyService.AuditFilePath, StringComparison.OrdinalIgnoreCase))
                    {
                        Directory.CreateDirectory(AgentPolicyService.DataDirectoryPath);
                        File.WriteAllText(path, string.Empty);
                    }
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
            }
            catch (Exception exception)
            {
                MessageBox.Show(
                    exception.Message,
                    "MCP-PC",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _refreshTimer.Dispose();
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
