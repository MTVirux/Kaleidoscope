using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Kaleidoscope.Services;
using System.Numerics;
using System.Text;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.ConfigWindow.ConfigCategories;

/// <summary>
/// Developer category for executing raw SQL queries against the plugin database.
/// Provides a SQL editor, table browser, and query results display.
/// </summary>
public sealed class SqlQueryCategory
{
    private readonly CurrencyTrackerService _currencyTrackerService;

    private KaleidoscopeDbService DbService => _currencyTrackerService.DbService;

    // SQL editor state
    private string _sqlQuery = "SELECT * FROM sqlite_master WHERE type='table' ORDER BY name;";
    private int _maxRows = 1000;

    // Query result state
    private KaleidoscopeDbService.RawQueryResult? _lastResult;
    private string? _selectedTable;
    private List<string>? _tableNames;
    private List<(string Name, string Type, bool NotNull, string? DefaultValue, bool IsPrimaryKey)>? _tableSchema;

    // Example queries for quick access
    private static readonly (string Name, string Query)[] ExampleQueries =
    {
        ("List all tables", "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name;"),
        ("Count all series", "SELECT COUNT(*) as count FROM series;"),
        ("Series by character", "SELECT s.id, s.variable, cn.game_name, s.character_id FROM series s LEFT JOIN character_names cn ON s.character_id = cn.character_id ORDER BY s.variable;"),
        ("Recent data points (100)", "SELECT p.id, s.variable, p.timestamp, p.value FROM points p JOIN series s ON p.series_id = s.id ORDER BY p.timestamp DESC LIMIT 100;"),
        ("Database size info", "SELECT page_count * page_size as size_bytes FROM pragma_page_count(), pragma_page_size();"),
        ("Character names", "SELECT character_id, game_name, display_name FROM character_names ORDER BY game_name;"),
        ("Item prices (top 50)", "SELECT * FROM item_prices ORDER BY last_updated DESC LIMIT 50;"),
        ("Inventory value history (100)", "SELECT * FROM inventory_value_history ORDER BY timestamp DESC LIMIT 100;"),
        ("Sale records (100)", "SELECT * FROM sale_records ORDER BY timestamp DESC LIMIT 100;"),
        ("Inventory caches", "SELECT id, character_id, source_type, retainer_id, timestamp FROM inventory_cache ORDER BY timestamp DESC LIMIT 50;"),
    };

    private static readonly Vector4 HeaderColor = new(1f, 0.8f, 0.2f, 1f);
    private static readonly Vector4 WarningColor = new(1f, 0.4f, 0.4f, 1f);
    private static readonly Vector4 SuccessColor = new(0.4f, 1f, 0.4f, 1f);
    private static readonly Vector4 InfoColor = new(0.6f, 0.8f, 1f, 1f);
    private static readonly Vector4 NullColor = new(0.5f, 0.5f, 0.5f, 1f);
    private static readonly Vector4 PkColor = new(1f, 0.9f, 0.4f, 1f);

    public SqlQueryCategory(CurrencyTrackerService currencyTrackerService)
    {
        _currencyTrackerService = currencyTrackerService;
    }

    public void Draw()
    {
        ImGui.TextColored(HeaderColor, "Developer Tool - SQL Query Editor");
        ImGui.Separator();
        ImGui.Spacing();

        DrawWarning();
        ImGui.Spacing();

        // Two-column layout: left for table browser, right for query editor and results
        var availWidth = ImGui.GetContentRegionAvail().X;
        var tableBrowserWidth = 200f;

        ImGui.BeginChild("##table_browser", new Vector2(tableBrowserWidth, 0), true);
        DrawTableBrowser();
        ImGui.EndChild();

        ImGui.SameLine();

        ImGui.BeginChild("##query_area", new Vector2(availWidth - tableBrowserWidth - 10, 0), false);
        DrawQueryEditor();
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        DrawQueryResults();
        ImGui.EndChild();
    }

    private void DrawWarning()
    {
        ImGui.PushStyleColor(ImGuiCol.Text, WarningColor);
        ImGui.TextWrapped("WARNING: Direct SQL access can corrupt your data. Use with caution. " +
                         "Modification queries (INSERT/UPDATE/DELETE) are irreversible.");
        ImGui.PopStyleColor();
    }

    private void DrawTableBrowser()
    {
        ImGui.TextUnformatted("Tables");
        ImGui.Separator();

        // Refresh button
        if (ImGui.Button("Refresh"))
        {
            _tableNames = null;
            _selectedTable = null;
            _tableSchema = null;
        }

        ImGui.Spacing();

        // Load table names if needed
        _tableNames ??= DbService.GetTableNames();

        // Table list
        foreach (var table in _tableNames)
        {
            var isSelected = _selectedTable == table;
            if (ImGui.Selectable(table, isSelected))
            {
                _selectedTable = table;
                _tableSchema = DbService.GetTableSchema(table);
            }

            // Right-click context menu
            if (ImGui.BeginPopupContextItem($"table_ctx_{table}"))
            {
                if (ImGui.MenuItem($"SELECT * FROM {table} LIMIT 100"))
                {
                    _sqlQuery = $"SELECT * FROM {table} LIMIT 100;";
                    ExecuteQuery();
                }
                if (ImGui.MenuItem($"SELECT COUNT(*) FROM {table}"))
                {
                    _sqlQuery = $"SELECT COUNT(*) as count FROM {table};";
                    ExecuteQuery();
                }
                ImGui.EndPopup();
            }
        }

        // Show schema for selected table
        if (_selectedTable != null && _tableSchema != null)
        {
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.TextColored(InfoColor, $"Schema: {_selectedTable}");
            ImGui.Spacing();

            foreach (var col in _tableSchema)
            {
                var prefix = col.IsPrimaryKey ? "🔑 " : "   ";
                var nullStr = col.NotNull ? "" : "?";
                if (col.IsPrimaryKey)
                {
                    ImGui.TextColored(PkColor, $"{prefix}{col.Name}");
                }
                else
                {
                    ImGui.TextUnformatted($"{prefix}{col.Name}");
                }
                ImGui.SameLine();
                ImGui.TextColored(NullColor, $" ({col.Type}{nullStr})");
            }
        }
    }

    private void DrawQueryEditor()
    {
        ImGui.TextUnformatted("SQL Query");

        // Example queries dropdown
        ImGui.SameLine();
        if (ImGui.BeginCombo("##examples", "Examples...", ImGuiComboFlags.NoPreview))
        {
            foreach (var (name, query) in ExampleQueries)
            {
                if (ImGui.Selectable(name))
                {
                    _sqlQuery = query;
                }
            }
            ImGui.EndCombo();
        }

        ImGui.Spacing();

        // SQL input - multiline text box
        var inputHeight = 120f;
        ImGui.InputTextMultiline("##sql_input", ref _sqlQuery, 8192, new Vector2(-1, inputHeight));

        ImGui.Spacing();

        // Controls row
        ImGui.SetNextItemWidth(100);
        ImGui.InputInt("Max Rows", ref _maxRows);
        _maxRows = Math.Clamp(_maxRows, 1, 10000);

        ImGui.SameLine();

        if (ImGui.Button("Execute (F5)") || (ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows) && ImGui.IsKeyPressed(ImGuiKey.F5)))
        {
            ExecuteQuery();
        }

        ImGui.SameLine();

        if (ImGui.Button("Clear Results"))
        {
            _lastResult = null;
        }

        ImGui.SameLine();

        if (ImGui.Button("Copy Query"))
        {
            ImGui.SetClipboardText(_sqlQuery);
        }

        // Show database path
        ImGui.SameLine();
        ImGui.TextColored(NullColor, $"DB: {DbService.DbPath ?? "N/A"}");
    }

    private void ExecuteQuery()
    {
        _lastResult = DbService.ExecuteRawQuery(_sqlQuery, _maxRows);
    }

    private void DrawQueryResults()
    {
        ImGui.TextUnformatted("Results");

        if (_lastResult == null)
        {
            ImGui.TextColored(NullColor, "No query executed yet.");
            return;
        }

        // Status line
        if (_lastResult.Success)
        {
            if (_lastResult.IsSelectQuery)
            {
                ImGui.TextColored(SuccessColor, $"✓ {_lastResult.Rows.Count} rows returned in {_lastResult.ExecutionTimeMs:F2}ms");
            }
            else
            {
                ImGui.TextColored(SuccessColor, $"✓ {_lastResult.RowsAffected} rows affected in {_lastResult.ExecutionTimeMs:F2}ms");
            }
        }
        else
        {
            ImGui.TextColored(WarningColor, $"✗ Error: {_lastResult.ErrorMessage}");
            return;
        }

        // Copy results button
        if (_lastResult.IsSelectQuery && _lastResult.Rows.Count > 0)
        {
            ImGui.SameLine();
            if (ImGui.Button("Copy as TSV"))
            {
                CopyResultsAsTsv();
            }
        }

        ImGui.Spacing();

        // Results table
        if (_lastResult.IsSelectQuery && _lastResult.Columns.Count > 0)
        {
            DrawResultsTable();
        }
    }

    private void DrawResultsTable()
    {
        if (_lastResult == null || _lastResult.Columns.Count == 0) return;

        var tableFlags = ImGuiTableFlags.Borders |
                        ImGuiTableFlags.RowBg |
                        ImGuiTableFlags.Resizable |
                        ImGuiTableFlags.ScrollX |
                        ImGuiTableFlags.ScrollY |
                        ImGuiTableFlags.Sortable |
                        ImGuiTableFlags.Hideable;

        // Calculate available height for the table
        var availHeight = ImGui.GetContentRegionAvail().Y;

        if (ImGui.BeginTable("##results_table", _lastResult.Columns.Count, tableFlags, new Vector2(0, availHeight)))
        {
            // Setup columns
            foreach (var col in _lastResult.Columns)
            {
                ImGui.TableSetupColumn(col, ImGuiTableColumnFlags.None);
            }
            ImGui.TableSetupScrollFreeze(0, 1); // Freeze header row
            ImGui.TableHeadersRow();

            // Draw rows
            foreach (var row in _lastResult.Rows)
            {
                ImGui.TableNextRow();
                for (int i = 0; i < row.Count; i++)
                {
                    ImGui.TableNextColumn();
                    var value = row[i];
                    if (value == null)
                    {
                        ImGui.TextColored(NullColor, "NULL");
                    }
                    else
                    {
                        // Truncate long values for display
                        var displayValue = value.Length > 100 ? value[..100] + "..." : value;
                        ImGui.TextUnformatted(displayValue);

                        // Show full value in tooltip if truncated
                        if (value.Length > 100 && ImGui.IsItemHovered())
                        {
                            ImGui.SetTooltip(value);
                        }
                    }
                }
            }

            ImGui.EndTable();
        }
    }

    private void CopyResultsAsTsv()
    {
        if (_lastResult == null || _lastResult.Columns.Count == 0) return;

        var sb = new StringBuilder();

        // Header row
        sb.AppendLine(string.Join("\t", _lastResult.Columns));

        // Data rows
        foreach (var row in _lastResult.Rows)
        {
            var values = row.Select(v => v ?? "NULL");
            sb.AppendLine(string.Join("\t", values));
        }

        ImGui.SetClipboardText(sb.ToString());
    }
}
