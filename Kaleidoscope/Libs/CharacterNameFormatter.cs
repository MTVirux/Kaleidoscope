namespace Kaleidoscope.Libs;

/// <summary>
/// Centralized utility for formatting character names according to the configured display format.
/// This is the single source of truth for name formatting logic.
/// </summary>
public static class CharacterNameFormatter
{
    /// <summary>
    /// Formats a character name according to the specified format.
    /// </summary>
    /// <param name="fullName">The full character name (e.g., "First Last").</param>
    /// <param name="format">The desired name format.</param>
    /// <returns>The formatted name, or the original name if formatting is not applicable.</returns>
    public static string? FormatName(string? fullName, CharacterNameFormat format)
    {
        if (string.IsNullOrWhiteSpace(fullName))
            return fullName;

        var parts = fullName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return fullName;

        return format switch
        {
            CharacterNameFormat.FullName => fullName,
            CharacterNameFormat.FirstNameOnly => parts[0],
            CharacterNameFormat.LastNameOnly => parts.Length > 1 ? parts[^1] : parts[0],
            CharacterNameFormat.Initials => string.Join(".", parts.Select(p => p.Length > 0 ? p[0].ToString().ToUpperInvariant() : "")) + ".",
            _ => fullName
        };
    }
}
