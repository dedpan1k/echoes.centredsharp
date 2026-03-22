using CentrED.Blueprints;
using Hexa.NET.ImGui;
using static CentrED.Application;

namespace CentrED.UI.Windows;

/// <summary>
/// Displays the blueprint tree maintained by the map's <see cref="BlueprintManager"/> and
/// exposes the tiles belonging to the currently selected blueprint entry.
/// </summary>
public class BlueprintsWindow : Window
{
    public override string Name => "Blueprints";

    // The manager is pulled from the active game state so the window always reflects the
    // current map session rather than caching a potentially stale reference.
    private BlueprintManager _manager => CEDGame.MapManager.BlueprintManager;
    private string _filter = "";

    /// <summary>
    /// Returns the tiles for the currently selected blueprint entry.
    /// Folder nodes and the no-selection state both resolve to an empty list.
    /// </summary>
    public List<BlueprintTile> Active => _selectedNode?.Tiles ?? [];

    /// <summary>
    /// Draws the blueprint browser. The tree is only available while connected because the
    /// underlying map session and blueprint manager are tied to the active client state.
    /// </summary>
    protected override void InternalDraw()
    {
        if (!CEDClient.Running)
        {
            ImGui.Text(LangManager.Get(LangEntry.NOT_CONNECTED));
            return;
        }

        // The filter is applied recursively so parent folders remain visible when any nested
        // blueprint entry matches the typed text.
        ImGui.InputText("##Filter", ref _filter, 64);
        if (ImGui.BeginTable("##bg", 1, ImGuiTableFlags.RowBg)){
            foreach (var blueprintNode in _manager.Root.Children)
            {
                DrawTreeNode(blueprintNode);
            }
            ImGui.EndTable();
        }
    }
    
    private BlueprintTreeEntry? _selectedNode;

    /// <summary>
    /// Returns whether the given node or any of its descendants should remain visible under
    /// the current filter text.
    /// </summary>
    private bool ShouldDraw(BlueprintTreeEntry node)
    {
        if (node.Children.Count == 0)
        {
            return node.Name.Contains(_filter, StringComparison.InvariantCultureIgnoreCase);
        }

        // Parent folders remain visible when at least one child matches so the matching leaf
        // still has a navigable path in the tree.
        return node.Children.Aggregate(false, (current, child) => current | ShouldDraw(child));
    }

    /// <summary>
    /// Draws a single blueprint tree entry and recursively draws any visible descendants.
    /// </summary>
    private void DrawTreeNode(BlueprintTreeEntry node)
    {
        if (!ShouldDraw(node))
        {
            return;
        }
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGuiTreeNodeFlags tree_flags = ImGuiTreeNodeFlags.None;
        tree_flags |= ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.OpenOnDoubleClick;// Standard opening mode as we are likely to want to add selection afterwards
        tree_flags |= ImGuiTreeNodeFlags.NavLeftJumpsToParent;  // Left arrow support
        tree_flags |= ImGuiTreeNodeFlags.SpanFullWidth;         // Span full width for easier mouse reach
        tree_flags |= ImGuiTreeNodeFlags.DrawLinesToNodes;      // Always draw hierarchy outlines
        if (node == _selectedNode)
            tree_flags |= ImGuiTreeNodeFlags.Selected;

        // Unloaded entries and tile-bearing entries are treated as leaves from the UI's point
        // of view. Loading is triggered lazily once the node receives focus.
        if (!node.Loaded || node.Tiles.Count > 0)
            tree_flags |= ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.Bullet;
        
        var nodeOpen = ImGui.TreeNodeEx(node.Name, tree_flags);
        if (ImGui.IsItemFocused())
        {
            _selectedNode = node;

            // Selecting a node also requests its data so tile-backed blueprints become
            // immediately available through the Active property.
            _selectedNode.Load();
        }
        if (nodeOpen)
        {
            foreach (var child in node.Children)
            {
                DrawTreeNode(child);
            }
            ImGui.TreePop();
        }
    }
}