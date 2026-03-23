using System.Numerics;
using System.Text;
using CentrED.Server;
using Hexa.NET.ImGui;
using static CentrED.Application;

namespace CentrED.UI.Windows;

/// <summary>
/// Manages the embedded local server process, including config-file selection, start/stop
/// control, and live log display.
/// </summary>
public class ServerWindow : Window
{
    private static readonly Vector2 DefaultWindowSize = new(620f, 340f);
    private static readonly Vector2 PreferredFooterDrawerSize = new(620f, 430f);

    /// <summary>
    /// Fixed title for the local server control window.
    /// </summary>
    public override string Name => "Local Server";

    /// <summary>
    /// Preferred footer drawer size for the local server controls.
    /// </summary>
    public Vector2 PreferredDrawerSize => PreferredFooterDrawerSize;

    /// <summary>
    /// Current footer summary text for the local server drawer trigger.
    /// </summary>
    public string StatusText => _statusText;

    /// <summary>
    /// Current footer summary color for the local server drawer trigger.
    /// </summary>
    public Vector4 StatusColor => _statusColor;

    // UI-local copies of the server config path and current status presentation.
    private string _configPath = Config.Instance.ServerConfigPath;
    private Vector4 _statusColor = ImGuiColor.Red;
    private string _statusText = "Stopped";

    // The log is tailed from disk so the server can write independently of the UI thread.
    private StreamReader? _logReader;
    private StringBuilder _log = new();
    private const int LOG_BUFFER_SIZE = 10000;
    private Server.Config.ConfigRoot? _config;

    /// <summary>
    /// Loads the last used server configuration so the window is immediately usable when opened.
    /// </summary>
    public ServerWindow()
    {
        TryReadConfigFile();
    }

    /// <summary>
    /// Attempts to parse the configured server XML file and updates the cached config object plus
    /// the log/status text used by the window.
    /// </summary>
    private bool TryReadConfigFile()
    {
        try
        {
            try
            {
                // Keep the parsed config cached so the Start action does not need to re-read the file.
                _config = Server.Config.ConfigRoot.Read(_configPath);
                Config.Instance.ServerConfigPath = _configPath;
                _log.Clear();
                _log.Append("Config file valid.");
            }
            catch (InvalidOperationException e)
            {
                // Invalid XML/config schema details are surfaced directly in the log pane.
                _log.Clear();
                _log.Append(e);
                throw;
            }
        }
        catch (Exception)
        {
            _config = null;
            return false;
        }
        return true;
    }

    /// <summary>
    /// Draws the config picker, start/stop controls, and the rolling server log output.
    /// </summary>
    protected override void InternalDraw()
    {
        ImGui.SetWindowSize(DefaultWindowSize, ImGuiCond.FirstUseEver);
        DrawContents();
    }

    /// <summary>
    /// Draws the local server controls inside a footer drawer.
    /// </summary>
    public void DrawDrawerContents()
    {
        DrawContents();
    }

    private void DrawContents()
    {
        if (ImGui.InputText("Config File", ref _configPath, 512))
        {
            TryReadConfigFile();
        }
        ImGui.SameLine();
        if (ImGui.Button("..."))
        {
            if (TinyFileDialogs.TryOpenFile
                    ("Select Server Config", Environment.CurrentDirectory, ["*.xml"], null, false, out var newPath))
            {
                // Picking a new config immediately re-validates it before the user tries to start the server.
                _configPath = newPath;
                TryReadConfigFile();
            }
        }
        if (Application.CEDServer is { Running: true })
        {
            if (ImGui.Button("Stop"))
            {
                // Disconnect any local client first so the shutdown path does not leave a stale connection.
                CEDClient.Disconnect();
                Application.CEDServer.Quit = true;
                _statusColor = ImGuiColor.Red;
                _statusText = "Stopped";
            }
        }
        else
        {
            // Start is blocked while the server is booting or when the config file failed to parse.
            ImGui.BeginDisabled(_statusText == "Starting" || _config == null);
            if (ImGui.Button("Start"))
            {
                if (Application.CEDServer != null)
                {
                    // Dispose any previous instance before replacing it with a fresh server.
                    Application.CEDServer.Dispose();
                }

                _log.Clear();
                Config.Instance.ServerConfigPath = _configPath;

                new Task
                (
                    () =>
                    {
                        try
                        {
                            _statusColor = ImGuiColor.Blue;
                            _statusText = "Starting";

                            // The server writes to a shared on-disk log file, while the window tails
                            // the same file through _logReader.
                            var logWriter = new StreamWriter
                                (File.Open("cedserver.log", FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
                                {
                                    AutoFlush = true
                                };
                            _logReader = new StreamReader
                                (File.Open("cedserver.log", FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
                            Application.CEDServer = new CEDServer(_config!, logWriter);
                            _statusColor = ImGuiColor.Green;
                            _statusText = "Running";

                            // Run blocks for the server lifetime, so it stays on the background task.
                            Application.CEDServer.Run();
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine("Server stopped");
                            Console.WriteLine(e);
                            _statusColor = ImGuiColor.Red;
                            _statusText = "Stopped";
                        }
                    }
                ).Start();
            }
            ImGui.EndDisabled();
        }
        ImGui.SameLine();
        ImGui.TextColored(_statusColor, _statusText);

        ImGui.Separator();
        ImGui.Text("Server Log:"u8);
        if (ImGui.BeginChild("ServerLogRegion"))
        {
            if (_logReader != null)
            {
                // Drain any newly written lines each frame to keep the log view live.
                do
                {
                    var line = _logReader.ReadLine();
                    if (line == null)
                        break;
                    _log.AppendLine(line);

                    // Follow the tail while new log lines arrive.
                    ImGui.SetScrollY(ImGui.GetScrollMaxY());
                } while (true);
            }
            if (_log.Length > LOG_BUFFER_SIZE)
            {
                // Cap the in-memory log buffer so a long-running server does not grow UI memory indefinitely.
                _log.Remove(0, _log.Length - LOG_BUFFER_SIZE);
            }
            ImGui.TextUnformatted(_log.ToString());
        }
        ImGui.EndChild();
    }
}