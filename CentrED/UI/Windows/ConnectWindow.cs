using System.Net.Sockets;
using System.Numerics;
using CentrED.IO;
using CentrED.IO.Models;
using CentrED.Utils;
using Hexa.NET.ImGui;
using static CentrED.Application;
using static CentrED.LangEntry;

namespace CentrED.UI.Windows;

/// <summary>
/// Presents the connection form for the collaborative server, including profile selection,
/// credential editing, and Ultima client directory selection.
/// </summary>
public class ConnectWindow : Window
{
    private static readonly Vector2 DefaultWindowSize = new(510f, 250f);
    private static readonly Vector2 PreferredFooterDrawerSize = new(510f, 310f);

    /// <summary>
    /// Stable ImGui title/ID pair for the connection window.
    /// </summary>
    public override string Name => LangManager.Get(CONNECT_WINDOW) + "###Connect";

    /// <summary>
    /// Keeps the standalone connection window at a fixed size.
    /// </summary>
    public override ImGuiWindowFlags WindowFlags => ImGuiWindowFlags.NoResize;

    /// <summary>
    /// The connection window is meant to be visible on first launch so users can establish a
    /// session immediately.
    /// </summary>
    public override WindowState DefaultState => new()
    {
        IsOpen = true
    };

    private const int TextInputLength = 255;

    // The editable form state is initialized from the active profile and can then diverge
    // until the user saves or switches profiles again.
    private int _profileIndex = ProfileManager.Profiles.IndexOf(ProfileManager.ActiveProfile);
    private string _hostname = ProfileManager.ActiveProfile.Hostname;
    private int _port = ProfileManager.ActiveProfile.Port;
    private string _username = ProfileManager.ActiveProfile.Username;
    private string _password = PasswordCrypter.Decrypt(ProfileManager.ActiveProfile.Password);
    private string _clientPath = ProfileManager.ActiveProfile.ClientPath;
    private bool _showPassword;
    private bool _buttonDisabled;
    internal string Info = "Not Connected";
    internal Vector4 InfoColor = ImGuiColor.Red;
    private string _profileName = "";

    /// <summary>
    /// Current status text shown in the footer trigger.
    /// </summary>
    public string StatusText
    {
        get
        {
            UpdateConnectionStatus();
            return Info;
        }
    }

    /// <summary>
    /// Current status color shown in the footer trigger.
    /// </summary>
    public Vector4 StatusColor
    {
        get
        {
            UpdateConnectionStatus();
            return InfoColor;
        }
    }

    /// <summary>
    /// Preferred footer drawer size for the connect form.
    /// </summary>
    public Vector2 PreferredDrawerSize => PreferredFooterDrawerSize;

    /// <summary>
    /// Refreshes the editable fields from the active profile whenever the UI is opened.
    /// </summary>
    public override void OnShow()
    {
        LoadProfile(ProfileManager.ActiveProfile);
    }

    /// <summary>
    /// Draws the profile picker, editable connection fields, and connect/disconnect controls.
    /// </summary>
    protected override void InternalDraw()
    {
        ImGui.SetWindowSize(DefaultWindowSize);
        DrawContents();
    }

    /// <summary>
    /// Draws the connection form inside a footer drawer.
    /// </summary>
    public void DrawDrawerContents()
    {
        DrawContents();
    }

    private void LoadProfile(Profile profile)
    {
        _profileIndex = ProfileManager.Profiles.IndexOf(profile);
        _profileName = profile.Name;
        _hostname = profile.Hostname;
        _port = profile.Port;
        _username = profile.Username;
        _password = PasswordCrypter.Decrypt(profile.Password);
        _clientPath = profile.ClientPath;
    }

    private void UpdateConnectionStatus()
    {
        if (CEDClient.Status != "")
        {
            Info = CEDClient.Status;
            InfoColor = CEDClient.Running ? ImGuiColor.Green : ImGuiColor.Red;
        }
    }

    private void DrawContents()
    {
        UpdateConnectionStatus();

        if (ImGui.Combo(LangManager.Get(PROFILE), ref _profileIndex, ProfileManager.ProfileNames, ProfileManager.Profiles.Count))
        {
            // Switching profiles replaces the working form values with the selected profile's
            // persisted connection settings.
            var profile = ProfileManager.Profiles[_profileIndex];
            LoadProfile(profile);
            Config.Instance.ActiveProfile = profile.Name;
        }
        ImGui.SameLine();
        if (ImGui.Button(LangManager.Get(SAVE)))
        {
            ImGui.OpenPopup("SaveProfile");
        }

        // Saving writes the current form fields back as a reusable named profile.
        if (ImGui.BeginPopup("SaveProfile"))
        {
            ImGui.InputText(LangManager.Get(NAME), ref _profileName, 128);
            if (ImGui.Button(LangManager.Get(SAVE)))
            {
                _profileIndex = ProfileManager.Save
                (
                    new Profile
                    {
                        Name = _profileName,
                        Hostname = _hostname,
                        Port = _port,
                        Username = _username,
                        Password = PasswordCrypter.Encrypt(_password),
                        ClientPath = _clientPath,
                    }
                );

                ImGui.CloseCurrentPopup();
            }
            if (ImGui.Button(LangManager.Get(CANCEL)))
            {
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }
        ImGui.NewLine();
        ImGui.InputText(LangManager.Get(HOSTNAME), ref _hostname, TextInputLength);
        ImGui.InputInt(LangManager.Get(PORT), ref _port);
        ImGui.InputText(LangManager.Get(USERNAME), ref _username, TextInputLength);

        ImGui.InputText
        (
            LangManager.Get(PASSWORD),
            ref _password,
            TextInputLength,
            _showPassword ? ImGuiInputTextFlags.None : ImGuiInputTextFlags.Password
        );
        ImGui.SameLine();
        if (ImGui.Button(_showPassword ? LangManager.Get(HIDE) : LangManager.Get(SHOW)))
        {
            _showPassword = !_showPassword;
        }
        ImGui.InputText(LangManager.Get(UO_DIRECTORY), ref _clientPath, TextInputLength);
        ImGui.SameLine();
        if (ImGui.Button("..."))
        {
            // Seed the folder picker with the current path when available so small fixes do
            // not require navigating from the working directory every time.
            var defaultPath = _clientPath.Length == 0 ? Environment.CurrentDirectory : _clientPath;
            if (TinyFileDialogs.TrySelectFolder(LangManager.Get(SELECT_DIRECTORY), defaultPath, out var newPath))
            {
                _clientPath = newPath;
            }
        }
        ImGui.TextColored(InfoColor, Info);

        // Connecting is blocked until the minimum required fields are present or while a
        // background connection attempt is already in flight.
        ImGui.BeginDisabled
        (
            _hostname.Length == 0 || _password.Length == 0 || _username.Length == 0 || _clientPath.Length == 0 || _buttonDisabled
        );
        if (CEDClient.Running)
        {
            if (ImGui.Button(LangManager.Get(DISCONNECT)))
            {
                CEDClient.Disconnect();
            }
        }
        else
        {
            if (ImGui.Button(LangManager.Get(CONNECT)) || ImGui.IsWindowFocused() && ImGui.IsKeyPressed(ImGuiKey.Enter))
            {
                // The connect path runs on a worker task because loading client data and the
                // initial network connection can both block long enough to freeze the UI.
                _buttonDisabled = true;
                new Task
                (
                    () =>
                    {
                        try
                        {
                            // The status text doubles as a lightweight progress indicator.
                            InfoColor = ImGuiColor.Blue;
                            Info = LangManager.Get(LOADING);
                            CEDGame.MapManager.Load(_clientPath);
                            Info = LangManager.Get(CONNECTING);
                            CEDClient.Connect(_hostname, _port, _username, _password);
                        }
                        catch (SocketException)
                        {
                            Info = LangManager.Get(UNABLE_TO_CONNECT);
                            InfoColor = ImGuiColor.Red;
                        }
                        catch (Exception e)
                        {
                            Info = string.Format(LangManager.Get(UNKNOWN_ERROR_1NAME), e.GetType().Name);
                            InfoColor = ImGuiColor.Red;
                            Console.WriteLine(e);
                        }
                        finally
                        {
                            // Re-enable the button regardless of success so the user can retry
                            // or adjust the settings after a failure.
                            _buttonDisabled = false;
                        }
                    }
                ).Start();
            }
        }
        ImGui.EndDisabled();
    }
}