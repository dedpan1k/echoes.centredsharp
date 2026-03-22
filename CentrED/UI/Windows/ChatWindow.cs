using System.Numerics;
using CentrED.Client;
using Hexa.NET.ImGui;
using static CentrED.LangEntry;

namespace CentrED.UI.Windows;

/// <summary>
/// Displays the connected client list and the shared chat log for the current editing
/// session, including basic client navigation actions.
/// </summary>
public class ChatWindow : Window
{
    /// <summary>
    /// Immutable UI representation of a single chat or presence event line.
    /// </summary>
    private record struct ChatMessage(string User, string Message, DateTime Time);

    /// <summary>
    /// Subscribes the window to chat and connection events so the UI can maintain a local,
    /// append-only history for the active session.
    /// </summary>
    public ChatWindow()
    {
        Application.CEDClient.ChatMessage += (user, message) =>
        {
            ChatMessages.Add(new ChatMessage(user, message, DateTime.Now));

            // New inbound messages request an auto-scroll on the next draw so the newest line
            // is visible even if the chat view is already open.
            _scrollToBottom = true;
            if (!Show)
            {
                // The title displays a "new messages" marker while the window is hidden.
                _unreadMessages = true;
            }
        };

        // Presence events are surfaced in the same transcript as normal chat to provide a
        // lightweight session history without a separate system log.
        Application.CEDClient.Disconnected += () => ChatMessages.Clear();
        Application.CEDClient.ClientConnected += user => ChatMessages.Add(new ChatMessage(user, LangManager.Get(CONNECTED), DateTime.Now));
        Application.CEDClient.ClientDisconnected += user => ChatMessages.Add(new ChatMessage(user, LangManager.Get(DISCONNECTED), DateTime.Now));
    }
    
    /// <summary>
    /// Window title with an inline unread marker while the window is hidden.
    /// The ID suffix keeps the ImGui window identity stable even as the visible title changes.
    /// </summary>
    public override string Name => LangManager.Get(CHAT_WINDOW) + (_unreadMessages ? $"({LangManager.Get(NEW_MESSAGES)})" : "") + "###Chat";

    /// <summary>
    /// Clears the unread state once the user brings the chat window back into view.
    /// </summary>
    public override void OnShow()
    {
        _unreadMessages = false;
    }

    private bool _unreadMessages;

    // Messages are kept in arrival order and rendered directly each frame.
    private List<ChatMessage> ChatMessages = new();

    // Set when the next frame should snap the chat log to the latest message.
    private bool _scrollToBottom = true;
    
    private string _chatInput = "";

    /// <summary>
    /// Draws the client list on the left and the chat transcript plus input field on the
    /// right.
    /// </summary>
    protected override void InternalDraw()
    {
        var clients = Application.CEDClient.Clients;
        
        // Size the client list wide enough for the longest visible user name while enforcing
        // a sensible minimum width.
        var maxNameSize = clients.Count == 0 ? 0 : Application.CEDClient.Clients.Max(s => ImGui.CalcTextSize(s).X);
        if(ImGui.BeginChild("Client List", new Vector2(Math.Max(150, maxNameSize), 0), ImGuiChildFlags.Borders))
        {
            ImGui.Text(LangManager.Get(USERS));
            ImGui.Separator();
            foreach (var client in clients)
            {
                ImGui.Selectable(client);
                if (client == Application.CEDClient.Username)
                {
                    // The local user is marked inline and does not expose client-targeted
                    // actions like teleporting to their own position.
                    ImGui.SameLine();
                    ImGui.TextDisabled("(" + LangManager.Get(YOU) + ")");
                    continue;
                }

                // Double-click is the fast path for jumping to another connected client.
                if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                {
                    Application.CEDClient.Send(new GotoClientPosPacket(client));
                }

                // Right-click exposes the same action in a context menu for discoverability.
                if (ImGui.IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
                {
                    ImGui.OpenPopup($"{client}Popup");
                }
                if (ImGui.BeginPopup($"{client}Popup"))
                {
                    if (ImGui.Button($"{LangManager.Get(GO_TO)}##{client}"))
                    {
                        Application.CEDClient.Send(new GotoClientPosPacket(client));
                    }
                    ImGui.EndPopup();
                }
            }
        }
        ImGui.EndChild();
        ImGui.SameLine();
        
        ImGui.BeginGroup();
        ImGui.Text(LangManager.Get(CHAT));
        ImGui.Separator();
        if (!Application.CEDClient.Running)
        {
            ImGui.TextDisabled(LangManager.Get(NOT_CONNECTED));    
        }
        else
        {
            // Reserve enough horizontal space so the text box and send button stay aligned on
            // the last row of the group.
            var sendButtonSize = ImGui.CalcTextSize(LangManager.Get(SEND) + "  ") + ImGui.GetStyle().FramePadding * 2;
            var inputPosY = ImGui.GetWindowSize().Y - ImGui.GetStyle().WindowPadding.Y - sendButtonSize.Y;

            var availSpace = ImGui.GetContentRegionAvail();
            var childSpace = availSpace with { Y = availSpace.Y - sendButtonSize.Y - ImGui.GetStyle().WindowPadding.Y};
            if(ImGui.BeginChild("Chat", childSpace))
            {
                foreach (var message in ChatMessages)
                {
                    ImGui.Text($"{message.Time:T}[{message.User}]: {message.Message}");
                }

                // Leave an anchor item at the bottom so SetScrollHereY has a stable target.
                ImGui.Dummy(Vector2.One);
                if (_scrollToBottom)
                {
                    ImGui.SetScrollHereY(1.0f);
                    _scrollToBottom = false;
                }
            }
            ImGui.EndChild();

            ImGui.SetCursorPosY(inputPosY);
            ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X - sendButtonSize.X - ImGui.GetStyle().ItemSpacing.X);
            ImGui.InputText("##ChatInput", ref _chatInput, 256);
            ImGui.SameLine();

            // Enter and the send button both route through the same packet send path.
            if (ImGui.Button(LangManager.Get(SEND), sendButtonSize) || ImGui.IsKeyPressed(ImGuiKey.Enter))
            {
                Application.CEDClient.Send(new ChatMessagePacket(_chatInput));
                _chatInput = "";
            }
        }
        ImGui.EndGroup();
    }
}