using Dalamud.Plugin.Services;
using Kaleidoscope.Models;
using OtterGui.Services;

namespace Kaleidoscope.Services;

/// <summary>
/// Manages favorites for items, currencies, and characters.
/// Favorites are persisted via the configuration service.
/// </summary>
public sealed class FavoritesService : IDisposable, IService
{
    private readonly IPluginLog _log;
    private readonly ConfigurationService _configService;
    
    /// <summary>
    /// Event fired when any favorite changes.
    /// </summary>
    public event Action? OnFavoritesChanged;

    public FavoritesService(IPluginLog log, ConfigurationService configService)
    {
        _log = log;
        _configService = configService;
    }

    public void Dispose()
    {
        // No unmanaged resources
    }

    #region Generic Helpers

    /// <summary>
    /// Adds an item to a favorites set and notifies if changed.
    /// </summary>
    private bool AddToSet<T>(HashSet<T> set, T item) where T : notnull
    {
        if (set.Add(item))
        {
            _configService.MarkDirty();
            OnFavoritesChanged?.Invoke();
            return true;
        }
        return false;
    }

    /// <summary>
    /// Removes an item from a favorites set and notifies if changed.
    /// </summary>
    private bool RemoveFromSet<T>(HashSet<T> set, T item) where T : notnull
    {
        if (set.Remove(item))
        {
            _configService.MarkDirty();
            OnFavoritesChanged?.Invoke();
            return true;
        }
        return false;
    }

    /// <summary>
    /// Toggles an item's presence in a favorites set.
    /// </summary>
    /// <returns>True if item is now in favorites, false if removed.</returns>
    private bool ToggleInSet<T>(HashSet<T> set, T item) where T : notnull
    {
        if (set.Contains(item))
        {
            RemoveFromSet(set, item);
            return false;
        }
        else
        {
            AddToSet(set, item);
            return true;
        }
    }

    #endregion

    #region Items
    
    /// <summary>
    /// Checks if an item is marked as favorite.
    /// </summary>
    public bool ContainsItem(uint itemId)
        => _configService.Config.FavoriteItems.Contains(itemId);

    /// <summary>
    /// Adds an item to favorites.
    /// </summary>
    public bool AddItem(uint itemId)
        => AddToSet(_configService.Config.FavoriteItems, itemId);

    /// <summary>
    /// Removes an item from favorites.
    /// </summary>
    public bool RemoveItem(uint itemId)
        => RemoveFromSet(_configService.Config.FavoriteItems, itemId);

    /// <summary>
    /// Toggles an item's favorite status.
    /// </summary>
    public bool ToggleItem(uint itemId)
        => ToggleInSet(_configService.Config.FavoriteItems, itemId);

    /// <summary>
    /// Gets all favorite item IDs.
    /// </summary>
    public IReadOnlySet<uint> FavoriteItems => _configService.Config.FavoriteItems;

    #endregion

    #region Currencies

    /// <summary>
    /// Checks if a currency (TrackedDataType) is marked as favorite.
    /// </summary>
    public bool ContainsCurrency(TrackedDataType type)
        => _configService.Config.FavoriteCurrencies.Contains(type);

    /// <summary>
    /// Adds a currency to favorites.
    /// </summary>
    public bool AddCurrency(TrackedDataType type)
        => AddToSet(_configService.Config.FavoriteCurrencies, type);

    /// <summary>
    /// Removes a currency from favorites.
    /// </summary>
    public bool RemoveCurrency(TrackedDataType type)
        => RemoveFromSet(_configService.Config.FavoriteCurrencies, type);

    /// <summary>
    /// Toggles a currency's favorite status.
    /// </summary>
    public bool ToggleCurrency(TrackedDataType type)
        => ToggleInSet(_configService.Config.FavoriteCurrencies, type);

    /// <summary>
    /// Gets all favorite currency types.
    /// </summary>
    public IReadOnlySet<TrackedDataType> FavoriteCurrencies => _configService.Config.FavoriteCurrencies;

    #endregion

    #region Characters

    /// <summary>
    /// Checks if a character is marked as favorite.
    /// </summary>
    public bool ContainsCharacter(ulong characterId)
        => _configService.Config.FavoriteCharacters.Contains(characterId);

    /// <summary>
    /// Adds a character to favorites.
    /// </summary>
    public bool AddCharacter(ulong characterId)
        => AddToSet(_configService.Config.FavoriteCharacters, characterId);

    /// <summary>
    /// Removes a character from favorites.
    /// </summary>
    public bool RemoveCharacter(ulong characterId)
        => RemoveFromSet(_configService.Config.FavoriteCharacters, characterId);

    /// <summary>
    /// Toggles a character's favorite status.
    /// </summary>
    public bool ToggleCharacter(ulong characterId)
        => ToggleInSet(_configService.Config.FavoriteCharacters, characterId);

    /// <summary>
    /// Gets all favorite character IDs.
    /// </summary>
    public IReadOnlySet<ulong> FavoriteCharacters => _configService.Config.FavoriteCharacters;

    #endregion
}
