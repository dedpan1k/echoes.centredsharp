using System.Numerics;
using Hexa.NET.ImGui;
using static CentrED.LangEntry;

namespace CentrED.UI;

/// <summary>
/// Small collection of higher-level ImGui helpers used throughout the editor to keep UI
/// code concise and consistent.
/// </summary>
public static class ImGuiEx
{
    /// <summary>
    /// Minimum size commonly used for resizable windows that should remain usable.
    /// </summary>
    public static readonly Vector2 MIN_SIZE = new Vector2(100, 100);

    /// <summary>
    /// Minimum size used when only the height should be constrained.
    /// </summary>
    public static readonly Vector2 MIN_HEIGHT = new Vector2(0, 100);

    /// <summary>
    /// Minimum size used when only the width should be constrained.
    /// </summary>
    public static readonly Vector2 MIN_WIDTH = new Vector2(100, 0);

    /// <summary>
    /// Shows an immediate tooltip for the most recently submitted ImGui item.
    /// Use <c>ImGui.SetItemTooltip</c> instead when delayed tooltip behavior is preferred.
    /// </summary>
    public static void Tooltip(string text)
    {
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(text);
        }
    }

    /// <summary>
    /// Draws a two-state switch with a default size.
    /// </summary>
    public static bool TwoWaySwitch(string leftLabel, string rightLabel, ref bool value)
    {
        return TwoWaySwitch(leftLabel, rightLabel, ref value, new Vector2(80, 18));
    }
    
    /// <summary>
    /// Draws a simple segmented toggle with two labels and returns whether the control was
    /// clicked this frame.
    /// </summary>
    public static bool TwoWaySwitch(string leftLabel, string rightLabel, ref bool value, Vector2 size, bool rounding = true)
    {
        ImGui.Text(leftLabel);
        ImGui.SameLine();
        var pos = ImGui.GetCursorPos();
        var wpos = ImGui.GetCursorScreenPos();

        // The highlighted half moves to the right when the value is enabled.
        if (value)
            wpos.X += size.X / 2;

        // The visible control is just a button with a hidden ID; an empty label would not
        // produce a valid interactive item in ImGui.
        var result = ImGui.Button($" ##{leftLabel}{rightLabel}", size);
        if (result)
        {
            value = !value;
        }
        var padding = ImGui.GetStyle().FramePadding;
        ImGui.SetCursorPos(pos);

        // Draw the active half manually so the control reads like a compact two-option switch
        // while still behaving like a normal button for input.
        ImGui.GetWindowDrawList().AddRectFilled
            (wpos + padding, 
             wpos + new Vector2(size.X / 2, size.Y) - padding, 
             ImGui.GetColorU32(new Vector4(.8f, .8f, 1, 0.5f)), 
             rounding ? ImGui.GetStyle().FrameRounding : 0);
        ImGui.SameLine();
        ImGui.Text(rightLabel);
        return result;
    }

    /// <summary>
    /// Integer drag helper that adds wheel and +/- button stepping around ImGui's scalar drag.
    /// </summary>
    public static unsafe bool DragInt(string label, ref int value, float v_speed, int v_min, int v_max, int v_step = 1, string format = "%d")
    {
        fixed (void* valuePtr = &value)
        {
            return DragScalar(label, ImGuiDataType.S32, valuePtr, v_speed, &v_min, &v_max, &v_step, format);
        }
    }

    /// <summary>
    /// Floating-point drag helper that adds wheel and +/- button stepping around ImGui's
    /// scalar drag.
    /// </summary>
    public static unsafe bool DragFloat(string label, ref float value, float v_speed, float v_min, float v_max, float v_step = 1, string format = "%.2f%%")
    {
        fixed (void* valuePtr = &value)
        {
            return DragScalar(label, ImGuiDataType.Float, valuePtr, v_speed, &v_min, &v_max,  &v_step, format);
        }
    }

    /// <summary>
    /// Shared implementation for the custom drag controls that reserve space for decrement
    /// and increment buttons next to the scalar editor.
    /// </summary>
    private static unsafe bool DragScalar(string label, ImGuiDataType type, void* pData, float v_speed, void* pMin, void* pMax, void* pStep, string format)
    {
        var io = ImGui.GetIO();
        var buttonWidth = ImGui.GetFrameHeight();
        var buttonSize = new Vector2(buttonWidth, buttonWidth);
        var spacing = ImGui.GetStyle().ItemInnerSpacing.X;

        // Keep the whole widget width stable by carving out space for the two step buttons
        // before asking ImGui to draw the drag field itself.
        var inputWidth = ImGui.CalcItemWidth() - (buttonWidth + spacing) * 2;
        ImGui.SetNextItemWidth(inputWidth);
        ImGui.PushID(label);
        var result = ImGui.DragScalar("##", type, pData, v_speed, pMin, pMax, format );

        // Mouse wheel stepping is limited to the hovered drag field so scrolling elsewhere in
        // the window does not unexpectedly mutate the value.
        if (ImGui.IsItemHovered() && io.MouseWheel < 0)
        {
            ImGuiP.DataTypeApplyOp(type, '-', pData, pData, pStep);
        }
        if (ImGui.IsItemHovered() && io.MouseWheel > 0)
        {
            ImGuiP.DataTypeApplyOp(type, '+', pData, pData, pStep);
        }
        ImGui.SameLine(0, spacing);
        ImGui.PushItemFlag(ImGuiItemFlags.ButtonRepeat, true);
        if (ImGui.Button("-", buttonSize))
        {
            ImGuiP.DataTypeApplyOp(type, '-', pData, pData, pStep);
            result = true;
        }
        ImGui.SameLine(0, spacing);
        if (ImGui.Button("+", buttonSize))
        {
            ImGuiP.DataTypeApplyOp(type, '+', pData, pData, pStep);
            result = true;
        }
        ImGui.PopItemFlag();
        ImGui.SameLine();
        ImGui.Text(label);
        ImGui.PopID();

        // Clamp after every interaction path so dragging, wheel changes, and button presses
        // all obey the same bounds.
        ImGuiP.DataTypeClamp(type, pData, pMin, pMax);
        return result;
    }

    /// <summary>
    /// Unsigned 16-bit scalar input that clamps the edited value to the provided bounds.
    /// </summary>
    public static unsafe bool InputUInt16(string label, ref ushort value, ushort minValue = ushort.MinValue, ushort maxValue = ushort.MaxValue)
    {
        fixed (ushort* ptr = &value)
        {
            var result = ImGui.InputScalar(label, ImGuiDataType.U16, ptr);
            value = Math.Clamp(value, minValue, maxValue);
            return result;
        }
    }
    
    /// <summary>
    /// Unsigned 32-bit scalar input that clamps the edited value to the provided bounds.
    /// </summary>
    public static unsafe bool InputUInt32
        (string label, ref uint value, uint minValue = uint.MinValue, uint maxValue = uint.MaxValue)
    {
        fixed (uint* ptr = &value)
        {
            var result = ImGui.InputScalar(label, ImGuiDataType.U32, ptr);
            value = Math.Clamp(value, minValue, maxValue);
            return result;
        }
    }

    /// <summary>
    /// Left-label variant of <c>ImGui.InputText</c> used for forms that should read like a
    /// label/value pair instead of ImGui's default inline label placement.
    /// </summary>
    public static bool InputText(string label, string labelId, ref string value, UIntPtr bufSize)
    {
        ImGui.Text(label);
        ImGui.SameLine();
        return ImGui.InputText(labelId, ref value, 32);
    }

    /// <summary>
    /// Convenience overload for a confirm/cancel popup using localized default button text.
    /// </summary>
    public static bool ConfirmButton(string label, string prompt)
    {
        return ConfirmButton(label, prompt, LangManager.Get(CONFIRM), LangManager.Get(CANCEL));
    }
    
    /// <summary>
    /// Draws a button that opens a modal confirmation popup and returns true only when the
    /// affirmative action is chosen.
    /// </summary>
    public static bool ConfirmButton(string label, string prompt, string yText, string nText)
    {
        var result = false;
        if (ImGui.Button(label))
        {
            ImGui.OpenPopup(label);
        }
        if (ImGui.BeginPopupModal(label, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.Text(prompt);

            // Keep both confirmation buttons the same width so the modal remains balanced even
            // when the translated strings differ in length.
            var buttonWidth = Math.Max(ImGui.CalcTextSize(yText).X, ImGui.CalcTextSize(nText).X) + ImGui.GetStyle().FramePadding.X * 2;
            if (ImGui.Button(yText, new Vector2(buttonWidth, 0)))
            {
                result = true;
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button(nText, new Vector2(buttonWidth, 0)))
            {
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }
        return result;
    }

    /// <summary>
    /// Creates the bottom status bar as a viewport side bar with the styling expected by the
    /// rest of the editor.
    /// </summary>
    public static bool BeginStatusBar()
    {
        var style = ImGui.GetStyle();

        // Align the sidebar like a menu/status surface while respecting the platform safe area.
        ImGuiP.ImGuiNextWindowData().MenuBarOffsetMinVal = new Vector2
            (style.DisplaySafeAreaPadding.X, Math.Max(style.DisplaySafeAreaPadding.Y - style.FramePadding.Y, 0));
        var flags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoInputs;
        var height = ImGui.GetFrameHeight();
        ImGui.PushStyleVar(ImGuiStyleVar.WindowMinSize, new Vector2(0, height));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(6,4));
        var isOpen =  ImGuiP.BeginViewportSideBar("##StatusBar", ImGui.GetMainViewport(), ImGuiDir.Down, height, flags);
        ImGuiP.ImGuiNextWindowData().MenuBarOffsetMinVal = Vector2.Zero;
        if (!isOpen)
        {
            EndStatusBar();
            return false;
        }
        return isOpen;
    }

    /// <summary>
    /// Creates an interactive top toolbar as a viewport side bar directly beneath the main menu.
    /// </summary>
    /// <param name="height">Requested toolbar height.</param>
    public static bool BeginToolbarBar(float height)
    {
        var style = ImGui.GetStyle();

        ImGuiP.ImGuiNextWindowData().MenuBarOffsetMinVal = new Vector2
            (style.DisplaySafeAreaPadding.X, Math.Max(style.DisplaySafeAreaPadding.Y - style.FramePadding.Y, 0));
        var flags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.HorizontalScrollbar;
        ImGui.PushStyleVar(ImGuiStyleVar.WindowMinSize, new Vector2(0, height));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        var isOpen = ImGuiP.BeginViewportSideBar("##ToolbarBar", ImGui.GetMainViewport(), ImGuiDir.Up, height, flags);
        ImGuiP.ImGuiNextWindowData().MenuBarOffsetMinVal = Vector2.Zero;
        if (!isOpen)
        {
            EndToolbarBar();
            return false;
        }
        return isOpen;
    }

    /// <summary>
    /// Ends a status bar begun by <see cref="BeginStatusBar"/> and restores the pushed style.
    /// </summary>
    public static void EndStatusBar()
    {
        ImGui.End();
        ImGui.PopStyleVar(2);
    }

    /// <summary>
    /// Ends a toolbar bar begun by <see cref="BeginToolbarBar"/> and restores the pushed style.
    /// </summary>
    public static void EndToolbarBar()
    {
        ImGui.End();
        ImGui.PopStyleVar(2);
    }
    
    /// <summary>
    /// Starts a drag source that carries a packed array of unsigned short identifiers.
    /// </summary>
    public static void DragDropSource(string type, ICollection<ushort> ids)
    {
        if (ImGui.BeginDragDropSource())
        {
            if (ImGui.GetDragDropPayload().IsNull)
            {
                // Copy the selection into a temporary contiguous array because ImGui expects a
                // raw buffer payload rather than an arbitrary managed collection.
                var selectedIds = ids.ToArray();
                unsafe
                {
                    fixed (ushort* idsPtr = selectedIds)
                    {
                        ImGui.SetDragDropPayload(type, idsPtr, (uint)(sizeof(ushort) * selectedIds.Length));
                    }
                }
            }
            else
            {
                // After the payload exists, show a summary instead of re-uploading it every frame.
                ImGui.Text($"{ids.Count} Items");
            }
            ImGui.EndDragDropSource();
        }
    }
    
    /// <summary>
    /// Accepts a drag payload containing unsigned short identifiers and copies it into a
    /// managed buffer that remains valid after the current ImGui frame.
    /// </summary>
    public static bool DragDropTarget(string type, out ReadOnlySpan<ushort> ids)
    {
        var res = false;
        ids = [];
        if (ImGui.BeginDragDropTarget())
        {
            var payloadPtr = ImGui.AcceptDragDropPayload(type);
            unsafe
            {
                if (payloadPtr != ImGuiPayloadPtr.Null)
                {
                    var elementCount = payloadPtr.DataSize / sizeof(ushort);

                    // The payload memory is owned by ImGui, so copy it before returning from
                    // the current frame.
                    var result = new ushort[elementCount];
                    new Span<ushort>(payloadPtr.Data, elementCount).CopyTo(result);
                    ids = result;
                    res = true;
                }
            }
            ImGui.EndDragDropTarget();
        }
        return res;
    }
}