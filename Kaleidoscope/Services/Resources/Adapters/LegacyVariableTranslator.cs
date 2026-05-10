using Kaleidoscope.Models.Resources;

namespace Kaleidoscope.Services.Resources.Adapters;

/// <summary>
/// Forward-looking complement to <see cref="ResourceCatalog.ParseLegacyVariableName"/>.
/// At runtime the legacy paths still ask "give me points for variable Item_5057" — this
/// class converts that magic-string lookup into a structured query specification against
/// the new resource_history table.
///
/// Internally just delegates to ResourceCatalog so there is exactly one definition of
/// the variable-name grammar in the codebase.
/// </summary>
public static class LegacyVariableTranslator
{
    /// <summary>
    /// Resolve a legacy variable name to the (OwnerKind, OwnerId, Container, ItemId) tuple
    /// used by resource_history queries. Returns null for unknown variable names.
    /// </summary>
    public static ResourceCatalog.LegacyVariableMapping? Translate(string variable, ulong characterId)
        => ResourceCatalog.ParseLegacyVariableName(variable, characterId);
}
