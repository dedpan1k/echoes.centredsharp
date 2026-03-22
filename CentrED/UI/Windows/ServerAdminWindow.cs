using System.Numerics;
using CentrED.Client;
using CentrED.Network;
using Hexa.NET.ImGui;
using static CentrED.Application;
using static CentrED.LangEntry;
using static Hexa.NET.ImGui.ImGuiChildFlags;

namespace CentrED.UI.Windows;

/// <summary>
/// Administrator-only server management window for users, regions, and a small set of global
/// server actions.
/// </summary>
public class ServerAdminWindow : Window
{
    /// <summary>
    /// The window is only available while connected as an administrator or higher.
    /// </summary>
    public override bool Enabled => CEDClient.Running && CEDClient.AccessLevel >= AccessLevel.Administrator;

    /// <summary>
    /// Stable ImGui title/ID pair for the server administration window.
    /// </summary>
    public override string Name => LangManager.Get(SERVER_ADMINISTRATION_WINDOW) + "###ServerAdmin";

    /// <summary>
    /// Refreshes the user and region lists whenever the window is opened.
    /// </summary>
    public override void OnShow()
    {
        if (CEDClient.Running)
        {
            CEDClient.Send(new ListUsersPacket());
            CEDClient.Send(new ListRegionsPacket());
        }
    }

    /// <summary>
    /// Draws the top-level server actions plus the tabbed user and region administration views.
    /// </summary>
    protected override void InternalDraw()
    {
        if (!CEDClient.Running)
        {
            ImGui.Text(LangManager.Get(NOT_CONNECTED));
            return;
        }
        if (ImGui.Button(LangManager.Get(SERVER_SAVE)))
        {
            // Flush forces pending edits to be persisted on the server immediately.
            CEDClient.Flush();
        }
        ImGui.SameLine();
        if (ImGuiEx.ConfirmButton(LangManager.Get(STOP_SERVER), LangManager.Get(STOP_SERVER_PROMPT)))
        {
            // Server shutdown is guarded by a confirmation modal because it affects every client.
            CEDClient.Send(new ServerStopPacket("Server is shutting down"));
        }
        ImGui.Separator();
        if (ImGui.BeginChild("Users/Regions"))
        {
            if (ImGui.BeginTabBar("Users/Regions"))
            {
                DrawUsersTab();
                DrawRegionsTab();
                ImGui.EndTabBar();
            }
        }
        ImGui.EndChild();
    }

    private int users_selected_index = -1;
    // The selected user is resolved lazily from the latest admin list and falls back to default
    // when the selection is out of range.
    private User users_selected => 
        users_selected_index == -1 || users_selected_index >= CEDClient.Admin.Users.Count ? default : CEDClient.Admin.Users[users_selected_index];
    private string users_new_username = "";
    private string users_new_password = "";

    /// <summary>
    /// Draws the user-management tab, including add/delete, access-level changes, password resets,
    /// and region membership toggles.
    /// </summary>
    private void DrawUsersTab()
    {
        if (ImGui.BeginTabItem(LangManager.Get(USERS)))
        {
            if (ImGui.BeginChild("UserList", new(150, 0), Borders | ResizeX))
            {
                for (var i = 0; i < CEDClient.Admin.Users.Count; i++)
                {
                    var user = CEDClient.Admin.Users[i];
                    if (ImGui.Selectable(user.Username, users_selected_index == i))
                    {
                        users_selected_index = i;
                    }
                }
            }
            ImGui.EndChild();
            ImGui.SameLine();
            ImGui.BeginGroup();
            if (ImGui.Button(LangManager.Get(REFRESH)))
            {
                CEDClient.Send(new ListUsersPacket());
            }
            if (ImGui.Button(LangManager.Get(ADD)))
            {
                ImGui.OpenPopup("AddUser");
            }
            if (ImGui.BeginPopupModal("AddUser", ImGuiWindowFlags.AlwaysAutoResize))
            {
                ImGui.InputText(LangManager.Get(USERNAME), ref users_new_username, 32);
                ImGui.InputText(LangManager.Get(PASSWORD), ref users_new_password, 32, ImGuiInputTextFlags.Password);
                
                // User creation is blocked until the name is unique and both fields are populated.
                ImGui.BeginDisabled(string.IsNullOrEmpty(users_new_username) || CEDClient.Admin.Users.Any(u => u.Username == users_new_username) || string.IsNullOrEmpty(users_new_password));
                if (ImGui.Button(LangManager.Get(ADD)))
                {
                    CEDClient.Send(new ModifyUserPacket(users_new_username, users_new_password, AccessLevel.None, []));
                    ImGui.CloseCurrentPopup();
                    users_selected_index = CEDClient.Admin.Users.Count;
                    users_new_username = "";
                    users_new_password = "";
                }
                ImGui.EndDisabled();
                ImGui.SameLine();
                if (ImGui.Button(LangManager.Get(CANCEL)))
                {
                    ImGui.CloseCurrentPopup();
                }
                ImGui.EndPopup();
            }
            ImGui.BeginDisabled(users_selected_index == -1);
            if (ImGuiEx.ConfirmButton(LangManager.Get(DELETE), 
                                      string.Format(LangManager.Get(DELETE_WARNING_1TYPE_2NAME), 
                                                    LangManager.Get(USER), 
                                                    users_selected.Username)))
            {
                CEDClient.Send(new DeleteUserPacket(users_selected.Username));
                users_selected_index -= 1;
            }
            ImGui.EndDisabled();

            ImGui.Separator();
            if (users_selected_index != -1 && users_selected_index < CEDClient.Admin.Users.Count)
            {
                var user = CEDClient.Admin.Users[users_selected_index];
                var names = Enum.GetNames(typeof(AccessLevel));
                var acessLevelIndex = Array.IndexOf(names, user.AccessLevel.ToString());
                ImGui.PushItemWidth(120);
                if (ImGui.Combo(LangManager.Get(ACCESS_LEVEL), ref acessLevelIndex, string.Join('\0', names)))
                {
                    // Access-level updates preserve the rest of the user record and only change the role.
                    if (AccessLevel.TryParse(names[acessLevelIndex], out AccessLevel newAccessLevel))
                    {
                        CEDClient.Send(new ModifyUserPacket(user.Username, "", newAccessLevel, user.Regions));
                    }
                }
                if (ImGui.Button(LangManager.Get(CHANGE_PASSWORD)))
                {
                    ImGui.OpenPopup("ChangePassword");
                }
                if (ImGui.BeginPopupModal
                        ("ChangePassword", ImGuiWindowFlags.AlwaysAutoResize))
                {
                    ImGui.InputText(LangManager.Get(PASSWORD), ref users_new_password, 32, ImGuiInputTextFlags.Password);
                    if (ImGui.Button(LangManager.Get(ADD)))
                    {
                        CEDClient.Send
                            (new ModifyUserPacket(user.Username, users_new_password, user.AccessLevel, user.Regions));
                        ImGui.CloseCurrentPopup();
                        users_new_password = "";
                    }
                    ImGui.SameLine();
                    if (ImGui.Button(LangManager.Get(CANCEL)))
                    {
                        ImGui.CloseCurrentPopup();
                    }
                    ImGui.EndPopup();
                }
                ImGui.Text(LangManager.Get(REGIONS));
                ImGui.Indent();
                if (ImGui.BeginChild("Regions"))
                {
                    foreach (var region in CEDClient.Admin.Regions)
                    {
                        var hasRegion = user.Regions.Contains(region.Name);
                        if (ImGui.Checkbox(region.Name, ref hasRegion))
                        {
                            // Region membership changes are sent as a full replacement list for the user.
                            var newRegionList = hasRegion ?
                                user.Regions.Append(region.Name).ToList() :
                                user.Regions.Where(r => r != region.Name).ToList();
                            CEDClient.Send(new ModifyUserPacket(user.Username, "", user.AccessLevel, newRegionList));
                        }
                    }
                }
                ImGui.EndChild();
            }
            ImGui.EndGroup();
            ImGui.EndTabItem();
        }
    }

    private int regions_selected_index = -1;
    // As with users, the selected region is resolved defensively against the live admin data.
    private Region regions_selected => 
        regions_selected_index == -1 || regions_selected_index >= CEDClient.Admin.Regions.Count ? default : CEDClient.Admin.Regions[regions_selected_index];
    private string regions_new_region_name = "";
    private int regions_area_selected = -1;
    private int regions_x1;
    private int regions_y1;
    private int regions_x2;
    private int regions_y2;

    /// <summary>
    /// Draws the region-management tab, including region CRUD and rectangular area editing.
    /// </summary>
    private void DrawRegionsTab()
    {
        if (ImGui.BeginTabItem(LangManager.Get(REGIONS)))
        {
            if (ImGui.BeginChild("RegionList", Borders | ResizeX))
            {
                for (var i = 0; i < CEDClient.Admin.Regions.Count; i++)
                {
                    var region = CEDClient.Admin.Regions[i];
                    if (ImGui.Selectable(region.Name, regions_selected_index == i))
                    {
                        regions_selected_index = i;
                        regions_area_selected = -1;
                    }
                }   
            }
            ImGui.EndChild();
            ImGui.SameLine();
            ImGui.BeginGroup();
            if (ImGui.Button(LangManager.Get(REFRESH)))
            {
                CEDClient.Send(new ListRegionsPacket());
            }
            if (ImGui.Button(LangManager.Get(ADD) + "##Region"))
            {
                ImGui.OpenPopup("AddRegion");
            }
            if (ImGui.BeginPopupModal("AddRegion", ImGuiWindowFlags.AlwaysAutoResize))
            {
                ImGui.InputText(LangManager.Get(NAME), ref regions_new_region_name, 32);
                // Region names must be unique before the create action is enabled.
                ImGui.BeginDisabled(string.IsNullOrEmpty(regions_new_region_name) || CEDClient.Admin.Regions.Any(r => r.Name == regions_new_region_name));
                if (ImGui.Button(LangManager.Get(ADD)))
                {
                    CEDClient.Send(new ModifyRegionPacket(regions_new_region_name, []));
                    ImGui.CloseCurrentPopup();
                    regions_selected_index = CEDClient.Admin.Regions.Count;
                    regions_new_region_name = "";
                }
                ImGui.EndDisabled();
                ImGui.SameLine();
                if (ImGui.Button(LangManager.Get(CANCEL)))
                {
                    ImGui.CloseCurrentPopup();
                }
                ImGui.EndPopup();
            }
            ImGui.BeginDisabled(regions_selected_index == -1);
            if (ImGuiEx.ConfirmButton
                    (LangManager.Get(DELETE) + "##Region", 
                     string.Format(LangManager.Get(DELETE_WARNING_1TYPE_2NAME),
                                   LangManager.Get(REGION),
                                    regions_selected.Name)))
            {
                CEDClient.Send(new DeleteRegionPacket(regions_selected.Name));
                regions_selected_index -= 1;
            }
            ImGui.EndDisabled();

            ImGui.Separator();
            if(regions_selected_index != -1)
            {
                ImGui.Text(LangManager.Get(AREAS));
                ImGui.SameLine();
                if (ImGui.Button(LangManager.Get(ADD) + "##Area"))
                {
                    // New areas start empty and are then edited in the detail pane on the right.
                    CEDClient.Send(new ModifyRegionPacket(regions_selected.Name, regions_selected.Areas.Append(new RectU16()).ToList()));
                }
                ImGui.SameLine();
                ImGui.BeginDisabled(regions_area_selected == -1);
                if (ImGui.Button(LangManager.Get(DELETE) + "##Area"))
                {
                    CEDClient.Send(new ModifyRegionPacket(regions_selected.Name, regions_selected.Areas.Where((_, i) => i != regions_area_selected).ToList()));
                    regions_area_selected -= 1;
                }
                ImGui.EndDisabled();
                var changedArea = false;
                if(regions_area_selected != -1)
                {
                    var curArea = regions_selected.Areas[regions_area_selected];
                    changedArea = regions_x1 != curArea.X1 || regions_y1 != curArea.Y1 || regions_x2 != curArea.X2 ||
                                      regions_y2 != curArea.Y2;
                }
                // Save is only enabled when the editable coordinates diverge from the selected stored area.
                ImGui.BeginDisabled(!changedArea);
                ImGui.SameLine();
                if (ImGui.Button(LangManager.Get(SAVE)))
                {
                    var newArea = new RectU16
                        ((ushort)regions_x1, (ushort)regions_y1, (ushort)regions_x2, (ushort)regions_y2);
                    regions_selected.Areas[regions_area_selected] = newArea;
                    CEDClient.Send(new ModifyRegionPacket(regions_selected.Name, regions_selected.Areas));
                }
                ImGui.EndDisabled();


                if (ImGui.BeginChild("AreaList", Borders | ResizeX))
                {
                    if (regions_selected_index != -1 && regions_selected_index < CEDClient.Admin.Regions.Count)
                    {
                        var region = CEDClient.Admin.Regions[regions_selected_index];
                        for (var i = 0; i < region.Areas.Count; i++)
                        {
                            var area = region.Areas[i];
                            if (ImGui.Selectable($"{area}##{i}", regions_area_selected == i))
                            {
                                // Selecting an area copies its coordinates into the edit buffer.
                                regions_area_selected = i;
                                regions_x1 = area.X1;
                                regions_y1 = area.Y1;
                                regions_x2 = area.X2;
                                regions_y2 = area.Y2;
                            }
                        }
                    }
                }
                ImGui.EndChild();
                ImGui.SameLine();
                ImGui.BeginGroup();
                if (regions_area_selected != -1)
                {
                    ImGui.NewLine();
                    ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize("X1").X - ImGui.GetStyle().ItemSpacing.X);
                    ImGuiEx.DragInt("X1", ref regions_x1, 1, 0, CEDClient.Width * 8);
                    ImGuiEx.DragInt("Y1", ref regions_y1, 1, 0, CEDClient.Height * 8);
                    ImGuiEx.DragInt("X2", ref regions_x2, 1, 0, CEDClient.Width * 8);
                    ImGuiEx.DragInt("Y2", ref regions_y2, 1, 0, CEDClient.Height * 8);
                    ImGui.PopItemWidth();
                }
                ImGui.EndGroup();
            }
            ImGui.EndGroup();
            ImGui.EndTabItem();
        }
    }

    /// <summary>
    /// Draws all region overlays onto the minimap, highlighting the currently edited area.
    /// </summary>
    public void DrawArea(Vector2 currentPos)
    {
        if (!Show)
            return;
        if (regions_selected == default)
            return;

        for (var i = 0; i < regions_selected.Areas.Count; i++)
        {
            if (i == regions_area_selected)
                continue;

            var area = regions_selected.Areas[i];
            // Non-selected areas use the shared green overlay so the active edit rectangle stands out.
            DrawRect(currentPos, area, ImGuiColor.Green);
        }

        if (regions_area_selected != -1)
        {
            // The current edit buffer is drawn in pink, even before the changes are saved.
            DrawRect(currentPos, new RectU16((ushort)regions_x1, (ushort)regions_y1, (ushort)regions_x2, (ushort)regions_y2), ImGuiColor.Pink);
        }
    }

    /// <summary>
    /// Draws one rectangular region overlay on the minimap.
    /// </summary>
    private void DrawRect(Vector2 currentPos, RectU16 area, Vector4 color)
    {
        ImGui.GetWindowDrawList().AddRect
        (
            // Region coordinates are world-tile units; the minimap reduces them by eight.
            currentPos + new Vector2(area.X1 / 8, area.Y1 / 8),
            currentPos + new Vector2(area.X2 / 8, area.Y2 / 8),
            ImGui.GetColorU32(color)
        );
    }
}