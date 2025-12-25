using Kaleidoscope.Services;

namespace Kaleidoscope;

/// <summary>
/// Helper class for applying consistent character sorting across the application.
/// </summary>
public static class CharacterSortHelper
{
    /// <summary>
    /// Sorts a list of character IDs in-place according to the configured sort order.
    /// </summary>
    /// <param name="characters">The list of character IDs to sort.</param>
    /// <param name="configService">Configuration service for sort order settings.</param>
    /// <param name="autoRetainerService">AutoRetainer service for AR sort order.</param>
    /// <param name="getName">Function to get display name for a character ID.</param>
    public static void ApplySortOrder(
        List<ulong> characters,
        ConfigurationService? configService,
        AutoRetainerIpcService? autoRetainerService,
        Func<ulong, string> getName)
    {
        if (characters == null || characters.Count <= 1) return;

        var sortOrder = configService?.Config.CharacterSortOrder ?? CharacterSortOrder.Alphabetical;
        ApplySortOrderInternal(characters, sortOrder, autoRetainerService, getName);
    }

    /// <summary>
    /// Returns a sorted enumerable of items based on character sort order.
    /// </summary>
    /// <typeparam name="T">The type of items to sort.</typeparam>
    /// <param name="items">The items to sort.</param>
    /// <param name="configService">Configuration service for sort order settings.</param>
    /// <param name="autoRetainerService">AutoRetainer service for AR sort order.</param>
    /// <param name="getCharacterId">Function to get character ID from an item.</param>
    /// <param name="getName">Function to get display name from an item.</param>
    /// <returns>Sorted enumerable of items.</returns>
    public static IEnumerable<T> SortByCharacter<T>(
        IEnumerable<T> items,
        ConfigurationService? configService,
        AutoRetainerIpcService? autoRetainerService,
        Func<T, ulong> getCharacterId,
        Func<T, string> getName)
    {
        if (items == null) return Enumerable.Empty<T>();
        
        var itemList = items.ToList();
        if (itemList.Count <= 1) return itemList;

        var sortOrder = configService?.Config.CharacterSortOrder ?? CharacterSortOrder.Alphabetical;

        return sortOrder switch
        {
            CharacterSortOrder.Alphabetical => 
                itemList.OrderBy(x => getName(x), StringComparer.OrdinalIgnoreCase),
            
            CharacterSortOrder.ReverseAlphabetical => 
                itemList.OrderByDescending(x => getName(x), StringComparer.OrdinalIgnoreCase),
            
            CharacterSortOrder.AutoRetainer => 
                SortByAutoRetainerOrder(itemList, autoRetainerService, getCharacterId, getName),
            
            _ => itemList.OrderBy(x => getName(x), StringComparer.OrdinalIgnoreCase)
        };
    }

    private static IEnumerable<T> SortByAutoRetainerOrder<T>(
        List<T> items,
        AutoRetainerIpcService? autoRetainerService,
        Func<T, ulong> getCharacterId,
        Func<T, string> getName)
    {
        var arOrder = autoRetainerService?.GetRegisteredCharacterIds();
        if (arOrder != null && arOrder.Count > 0)
        {
            var orderLookup = new Dictionary<ulong, int>();
            for (var i = 0; i < arOrder.Count; i++)
            {
                orderLookup[arOrder[i]] = i;
            }

            return items.OrderBy(x =>
            {
                var cid = getCharacterId(x);
                if (orderLookup.TryGetValue(cid, out var order))
                    return (order, string.Empty);
                // Items not in AR go to the end, sorted alphabetically
                return (int.MaxValue, getName(x));
            }, Comparer<(int order, string name)>.Create((a, b) =>
            {
                var orderCompare = a.order.CompareTo(b.order);
                if (orderCompare != 0) return orderCompare;
                return string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase);
            }));
        }

        // Fall back to alphabetical
        return items.OrderBy(x => getName(x), StringComparer.OrdinalIgnoreCase);
    }

    private static void ApplySortOrderInternal(
        List<ulong> characters,
        CharacterSortOrder sortOrder,
        AutoRetainerIpcService? autoRetainerService,
        Func<ulong, string> getName)
    {
        switch (sortOrder)
        {
            case CharacterSortOrder.Alphabetical:
                characters.Sort((a, b) =>
                    string.Compare(getName(a), getName(b), StringComparison.OrdinalIgnoreCase));
                break;

            case CharacterSortOrder.ReverseAlphabetical:
                characters.Sort((a, b) =>
                    string.Compare(getName(b), getName(a), StringComparison.OrdinalIgnoreCase));
                break;

            case CharacterSortOrder.AutoRetainer:
                var arOrder = autoRetainerService?.GetRegisteredCharacterIds();
                if (arOrder != null && arOrder.Count > 0)
                {
                    var orderLookup = new Dictionary<ulong, int>();
                    for (var i = 0; i < arOrder.Count; i++)
                    {
                        orderLookup[arOrder[i]] = i;
                    }

                    characters.Sort((a, b) =>
                    {
                        var hasA = orderLookup.TryGetValue(a, out var orderA);
                        var hasB = orderLookup.TryGetValue(b, out var orderB);

                        if (hasA && hasB)
                            return orderA.CompareTo(orderB);
                        if (hasA)
                            return -1;
                        if (hasB)
                            return 1;

                        // Both not in AR, sort alphabetically
                        return string.Compare(getName(a), getName(b), StringComparison.OrdinalIgnoreCase);
                    });
                }
                else
                {
                    // Fall back to alphabetical
                    characters.Sort((a, b) =>
                        string.Compare(getName(a), getName(b), StringComparison.OrdinalIgnoreCase));
                }
                break;
        }
    }
}
