using Dalamud.Plugin.Services;
using Kaleidoscope.Models;
using OtterGui.Services;

namespace Kaleidoscope.Services;

/// <summary>
/// Manages favorites for items, currencies, and characters.
/// Favorites are persisted via the configuration service.
/// </summary>
public sealed class FavoritesService : IService, IDisposable
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
    {
        if (_configService.Config.FavoriteItems.Add(itemId))
        {
            _configService.Save();
            OnFavoritesChanged?.Invoke();
            return true;
        }
        return false;
    }

    /// <summary>
    /// Removes an item from favorites.
    /// </summary>
    public bool RemoveItem(uint itemId)
    {
        if (_configService.Config.FavoriteItems.Remove(itemId))
        {
            _configService.Save();
            OnFavoritesChanged?.Invoke();
            return true;
        }
        return false;
    }

    /// <summary>
    /// Toggles an item's favorite status.
    /// </summary>
    public bool ToggleItem(uint itemId)
    {
        if (ContainsItem(itemId))
        {
            RemoveItem(itemId);
            return false;
        }
        else
        {
            AddItem(itemId);
            return true;
        }
    }

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
    {
        if (_configService.Config.FavoriteCurrencies.Add(type))
        {
            _configService.Save();
            OnFavoritesChanged?.Invoke();
            return true;
        }
        return false;
    }

    /// <summary>
    /// Removes a currency from favorites.
    /// </summary>
    public bool RemoveCurrency(TrackedDataType type)
    {
        if (_configService.Config.FavoriteCurrencies.Remove(type))
        {
            _configService.Save();
            OnFavoritesChanged?.Invoke();
            return true;
        }
        return false;
    }

    /// <summary>
    /// Toggles a currency's favorite status.
    /// </summary>
    public bool ToggleCurrency(TrackedDataType type)
    {
        if (ContainsCurrency(type))
        {
            RemoveCurrency(type);
            return false;
        }
        else
        {
            AddCurrency(type);
            return true;
        }
    }

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
    {
        if (_configService.Config.FavoriteCharacters.Add(characterId))
        {
            _configService.Save();
            OnFavoritesChanged?.Invoke();
            return true;
        }
        return false;
    }

    /// <summary>
    /// Removes a character from favorites.
    /// </summary>
    public bool RemoveCharacter(ulong characterId)
    {
        if (_configService.Config.FavoriteCharacters.Remove(characterId))
        {
            _configService.Save();
            OnFavoritesChanged?.Invoke();
            return true;
        }
        return false;
    }

    /// <summary>
    /// Toggles a character's favorite status.
    /// </summary>
    public bool ToggleCharacter(ulong characterId)
    {
        if (ContainsCharacter(characterId))
        {
            RemoveCharacter(characterId);
            return false;
        }
        else
        {
            AddCharacter(characterId);
            return true;
        }
    }

    /// <summary>
    /// Gets all favorite character IDs.
    /// </summary>
    public IReadOnlySet<ulong> FavoriteCharacters => _configService.Config.FavoriteCharacters;

    #endregion
}
