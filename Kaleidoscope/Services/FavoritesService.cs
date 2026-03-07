using Kaleidoscope.Models;
using OtterGui.Services;

namespace Kaleidoscope.Services;

/// <summary>
/// Manages favorites for items, currencies, and characters.
/// Favorites are persisted via the configuration service.
/// </summary>
public sealed class FavoritesService : IService
{
    private readonly ConfigurationService _configService;
    
    /// <summary>
    /// Event fired when any favorite changes.
    /// </summary>
    public event Action? OnFavoritesChanged;

    public FavoritesService(ConfigurationService configService)
    {
        _configService = configService;
    }

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

    public bool ContainsItem(uint itemId)
        => _configService.Config.FavoriteItems.Contains(itemId);

    public bool AddItem(uint itemId)
        => AddToSet(_configService.Config.FavoriteItems, itemId);

    public bool RemoveItem(uint itemId)
        => RemoveFromSet(_configService.Config.FavoriteItems, itemId);

    public bool ToggleItem(uint itemId)
        => ToggleInSet(_configService.Config.FavoriteItems, itemId);

    public IReadOnlySet<uint> FavoriteItems => _configService.Config.FavoriteItems;

    public bool ContainsCurrency(TrackedDataType type)
        => _configService.Config.FavoriteCurrencies.Contains(type);

    public bool AddCurrency(TrackedDataType type)
        => AddToSet(_configService.Config.FavoriteCurrencies, type);

    public bool RemoveCurrency(TrackedDataType type)
        => RemoveFromSet(_configService.Config.FavoriteCurrencies, type);

    public bool ToggleCurrency(TrackedDataType type)
        => ToggleInSet(_configService.Config.FavoriteCurrencies, type);

    public IReadOnlySet<TrackedDataType> FavoriteCurrencies => _configService.Config.FavoriteCurrencies;

    public bool ContainsCharacter(ulong characterId)
        => _configService.Config.FavoriteCharacters.Contains(characterId);

    public bool AddCharacter(ulong characterId)
        => AddToSet(_configService.Config.FavoriteCharacters, characterId);

    public bool RemoveCharacter(ulong characterId)
        => RemoveFromSet(_configService.Config.FavoriteCharacters, characterId);

    public bool ToggleCharacter(ulong characterId)
        => ToggleInSet(_configService.Config.FavoriteCharacters, characterId);

    public IReadOnlySet<ulong> FavoriteCharacters => _configService.Config.FavoriteCharacters;
}
