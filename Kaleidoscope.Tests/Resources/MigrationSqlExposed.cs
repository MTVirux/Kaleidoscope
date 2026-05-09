namespace Kaleidoscope.Tests.Resources;

/// <summary>
/// Test-only re-exports of migration SQL constants. The constants are internal in
/// MigrationSql and that file is compiled into the test assembly via &lt;Compile Include&gt;,
/// so internal is visible within the same assembly — no InternalsVisibleTo needed.
/// </summary>
internal static class MigrationSqlExposed
{
    public const string BackfillResourcesFromInventoryItemsSql =
        Kaleidoscope.Services.Database.MigrationSql.BackfillResourcesFromInventoryItemsSql;
}
