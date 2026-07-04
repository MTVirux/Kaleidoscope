using System.Numerics;
using Dalamud.Bindings.ImGui;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.ConfigWindow;

/// <summary>
/// Shared UI helpers for config-window categories. Centralizes the bind-checkbox,
/// labeled reset-row, confirmation-popup, background-task, and icon idioms that were
/// previously copy-pasted across the category classes.
/// </summary>
public static class ConfigUiHelpers
{
    /// <summary>Base table flags shared by the color/scroll tables in the config categories.</summary>
    private const ImGuiTableFlags ScrollTableFlags =
        ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.ScrollY;

    /// <summary>
    /// Binds a checkbox to a value via getter/setter. The setter is only invoked when the
    /// value changes (it should perform the actual assignment plus any save/side effects).
    /// When <paramref name="tooltip"/> is set it is shown on hover of the checkbox.
    /// </summary>
    /// <returns>True if the value changed this frame.</returns>
    public static bool ConfigCheckbox(string label, Func<bool> getter, Action<bool> setter, string? tooltip = null)
    {
        var value = getter();
        var changed = ImGui.Checkbox(label, ref value);
        if (changed)
            setter(value);
        if (tooltip != null && ImGui.IsItemHovered())
            ImGui.SetTooltip(tooltip);
        return changed;
    }

    /// <summary>
    /// Draws a label + color picker + reset button row. The color is applied live every frame
    /// while dragging (return value is true), but <paramref name="save"/> is only called when
    /// editing finishes or the reset button is pressed — avoiding a synchronous disk write per
    /// drag frame.
    /// </summary>
    /// <returns>True if the color value changed this frame (caller should apply it).</returns>
    public static bool DrawColorRow(string label, ref Vector4 color, Vector4 defaultValue, Action save)
    {
        var changed = false;

        ImGui.TextUnformatted(label);

        ImGui.SameLine(180f);
        ImGui.SetNextItemWidth(200f);
        if (ImGui.ColorEdit4($"##{label}", ref color, ImGuiColorEditFlags.AlphaPreviewHalf | ImGuiColorEditFlags.AlphaBar))
            changed = true;
        if (ImGui.IsItemDeactivatedAfterEdit())
            save();

        ImGui.SameLine();
        if (ImGui.Button($"Reset##{label}"))
        {
            color = defaultValue;
            save();
            changed = true;
        }

        return changed;
    }

    /// <summary>
    /// Draws a label + float slider + reset button row. The value is applied live every frame
    /// while dragging, but <paramref name="save"/> is only called when editing finishes or the
    /// reset button is pressed.
    /// </summary>
    /// <returns>True if the value changed this frame (caller should apply it).</returns>
    public static bool DrawFloatRow(string label, ref float value, float defaultValue, float min, float max, Action save)
    {
        var changed = false;

        ImGui.TextUnformatted(label);

        ImGui.SameLine(180f);
        ImGui.SetNextItemWidth(150f);
        if (ImGui.SliderFloat($"##{label}", ref value, min, max, "%.2f"))
            changed = true;
        if (ImGui.IsItemDeactivatedAfterEdit())
            save();

        ImGui.SameLine();
        if (ImGui.Button($"Reset##{label}"))
        {
            value = defaultValue;
            save();
            changed = true;
        }

        return changed;
    }

    /// <summary>
    /// Draws a label + int slider + reset button row. The value is applied live every frame
    /// while dragging, but <paramref name="save"/> is only called when editing finishes or the
    /// reset button is pressed.
    /// </summary>
    /// <returns>True if the value changed this frame (caller should apply it).</returns>
    public static bool DrawIntRow(string label, ref int value, int defaultValue, int min, int max, Action save)
    {
        var changed = false;

        ImGui.TextUnformatted(label);

        ImGui.SameLine(180f);
        ImGui.SetNextItemWidth(150f);
        if (ImGui.SliderInt($"##{label}", ref value, min, max))
            changed = true;
        if (ImGui.IsItemDeactivatedAfterEdit())
            save();

        ImGui.SameLine();
        if (ImGui.Button($"Reset##{label}"))
        {
            value = defaultValue;
            save();
            changed = true;
        }

        return changed;
    }

    /// <summary>
    /// Renders a modal confirmation popup body. The caller is responsible for calling
    /// <c>ImGui.OpenPopup(id)</c> when the triggering control is activated; this method draws
    /// the modal contents (message, optional extra body, and confirm/cancel buttons).
    /// <paramref name="onConfirm"/> runs when the confirm button is pressed.
    /// </summary>
    public static void ConfirmPopup(
        string id,
        string message,
        Action onConfirm,
        Action? extraBody = null,
        string confirmLabel = "Yes",
        string cancelLabel = "No")
    {
        var open = true;
        if (ImGui.BeginPopupModal(id, ref open, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.TextUnformatted(message);
            extraBody?.Invoke();
            if (ImGui.Button(confirmLabel))
            {
                onConfirm();
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button(cancelLabel))
            {
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }
    }

    /// <summary>
    /// Runs <paramref name="work"/> on a background thread, toggling a running flag around it and
    /// routing the result or exception back to the caller. <paramref name="setRunning"/> is
    /// invoked synchronously with <c>true</c> before the task starts and with <c>false</c> in the
    /// task's finally block. <paramref name="onResult"/>/<paramref name="onError"/> run on the
    /// background thread (matching the previous hand-rolled copies).
    /// </summary>
    public static void RunBackground<T>(
        Action<bool> setRunning,
        Func<T> work,
        Action<T> onResult,
        Action<Exception> onError)
    {
        setRunning(true);
        Task.Run(() =>
        {
            try { onResult(work()); }
            catch (Exception ex) { onError(ex); }
            finally { setRunning(false); }
        });
    }

    /// <summary>
    /// Draws a scrolling table with the shared color-table scaffolding: standard flags, a height
    /// that fills the remaining content region minus <paramref name="bottomMargin"/>, a frozen
    /// header row, and begin/end bookkeeping. The caller supplies column setup and the row body.
    /// </summary>
    public static void DrawColorTable(
        string tableId,
        int columnCount,
        Action setupColumns,
        Action drawBody,
        float bottomMargin = 30f,
        float minHeight = 100f,
        ImGuiTableFlags extraFlags = ImGuiTableFlags.None)
    {
        var availableHeight = ImGui.GetContentRegionAvail().Y - bottomMargin;
        if (availableHeight < minHeight) availableHeight = minHeight;

        if (ImGui.BeginTable(tableId, columnCount, ScrollTableFlags | extraFlags, new Vector2(0, availableHeight)))
        {
            setupColumns();
            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableHeadersRow();
            drawBody();
            ImGui.EndTable();
        }
    }
}
