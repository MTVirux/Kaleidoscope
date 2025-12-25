namespace Kaleidoscope.Services;

/// <summary>
/// Utility class for sanitizing character and player names for database storage.
/// Centralizes name sanitization logic that was previously duplicated across multiple classes.
/// </summary>
public static class NameSanitizer
{
    /// <summary>
    /// Sanitizes a character name for database storage.
    /// Handles patterns like "You (CharacterName)" by extracting the inner name.
    /// Also strips "You " prefix if present.
    /// </summary>
    /// <param name="raw">The raw name to sanitize.</param>
    /// <returns>The sanitized name, or null if input is null/empty.</returns>
    public static string? Sanitize(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return raw;

        try
        {
            var s = raw.Trim();

            // Look for patterns like "You (Name)" and extract the inner name
            var idxOpen = s.IndexOf('(');
            var idxClose = s.LastIndexOf(')');
            if (idxOpen >= 0 && idxClose > idxOpen)
            {
                var inner = s.Substring(idxOpen + 1, idxClose - idxOpen - 1).Trim();
                if (!string.IsNullOrEmpty(inner)) return inner;
            }

            // If it starts with "You " then strip that prefix
            if (s.StartsWith("You ", StringComparison.OrdinalIgnoreCase))
            {
                var rem = s.Substring(4).Trim();
                if (!string.IsNullOrEmpty(rem)) return rem;
            }

            return s;
        }
        catch
        {
            return raw;
        }
    }

    /// <summary>
    /// Sanitizes a character name for database storage, with fallback to local player name.
    /// If the sanitized name is "You", attempts to resolve the actual player name.
    /// </summary>
    /// <param name="raw">The raw name to sanitize.</param>
    /// <returns>The sanitized name, or null if input is null/empty and no fallback available.</returns>
    public static string? SanitizeWithPlayerFallback(string? raw)
    {
        var sanitized = Sanitize(raw);
        
        // Try to get local player name if it's just "You"
        if (string.Equals(sanitized, "You", StringComparison.OrdinalIgnoreCase))
        {
            var localName = GameStateService.LocalPlayerName;
            if (!string.IsNullOrEmpty(localName))
                return localName;
        }
        
        return sanitized;
    }
}
