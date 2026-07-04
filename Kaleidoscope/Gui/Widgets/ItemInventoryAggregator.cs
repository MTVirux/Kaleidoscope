using Kaleidoscope.Models.Inventory;
using Kaleidoscope.Services.Characters;
using Kaleidoscope.Services.Inventory;
using Kaleidoscope.Services.Universalis;

namespace Kaleidoscope.Gui.Widgets;

/// <summary>
/// Aggregated inventory distribution for a single item across all characters and retainers.
/// </summary>
public sealed class ItemInventoryAggregation
{
    public List<ItemInventoryRow> Rows { get; init; } = new();
    public long UnitPrice { get; init; }
    public int TotalQuantity { get; init; }
    public long TotalValue { get; init; }
}

/// <summary>
/// Pure aggregation of cached inventories into per-character/retainer rows for an item.
/// Rendering-free: consumed by <see cref="ItemDetailsPopup"/>'s inventory tab.
/// </summary>
public static class ItemInventoryAggregator
{
    /// <summary>
    /// Builds the per-character/retainer distribution rows and totals for an item.
    /// </summary>
    public static ItemInventoryAggregation Aggregate(
        uint itemId,
        InventoryCacheService inventoryCacheService,
        SalePriceCacheService? salePriceCacheService,
        CharacterDataService? characterDataService)
    {
        var rows = new List<ItemInventoryRow>();
        var totalQuantity = 0;
        var totalValue = 0L;

        // Get all inventories across all characters
        var allInventories = inventoryCacheService.GetAllInventories();

        // Get the current price for this item using cache
        long unitPrice = 0;
        if (salePriceCacheService != null)
        {
            var prices = salePriceCacheService.GetLatestSalePrices(
                new[] { (int)itemId },
                includedWorldIds: null);
            if (prices.TryGetValue((int)itemId, out var price))
            {
                unitPrice = price.LastSaleNq > 0 ? price.LastSaleNq : price.LastSaleHq;
            }
        }

        // Group by character, then by player/retainer
        var characterGroups = allInventories
            .GroupBy(i => i.CharacterId)
            .OrderBy(g => g.First().Name ?? string.Empty);

        foreach (var charGroup in characterGroups)
        {
            var characterId = charGroup.Key;
            var characterName = string.Empty;
            var worldName = string.Empty;

            // Get character display name
            if (characterDataService != null)
            {
                var charInfo = characterDataService.GetCharacter(characterId);
                if (charInfo != null)
                {
                    characterName = charInfo.Name;
                    worldName = charInfo.WorldName ?? string.Empty;
                }
            }

            // Fallback to inventory cache name
            if (string.IsNullOrEmpty(characterName))
            {
                var playerCache = charGroup.FirstOrDefault(c => c.SourceType == InventorySourceType.Player);
                characterName = playerCache?.Name ?? $"Character {characterId}";
                worldName = playerCache?.World ?? string.Empty;
            }

            // Calculate player inventory quantity
            var playerInventories = charGroup.Where(c => c.SourceType == InventorySourceType.Player);
            var playerQuantity = playerInventories
                .SelectMany(c => c.Items)
                .Where(i => i.ItemId == itemId)
                .Sum(i => i.Quantity);

            // Calculate retainer quantities
            var retainerData = charGroup
                .Where(c => c.SourceType == InventorySourceType.Retainer)
                .Select(r => new
                {
                    RetainerId = r.RetainerId,
                    RetainerName = r.Name,
                    Quantity = r.Items.Where(i => i.ItemId == itemId).Sum(i => i.Quantity)
                })
                .Where(r => r.Quantity > 0)
                .OrderBy(r => r.RetainerName)
                .ToList();

            // Skip this character if they have no items
            var totalCharQuantity = playerQuantity + retainerData.Sum(r => r.Quantity);
            if (totalCharQuantity == 0)
                continue;

            // Add character row (showing player inventory only)
            if (playerQuantity > 0)
            {
                var playerValue = unitPrice * playerQuantity;
                rows.Add(new ItemInventoryRow
                {
                    CharacterId = characterId,
                    CharacterName = characterName,
                    WorldName = worldName,
                    IsRetainer = false,
                    Quantity = playerQuantity,
                    UnitPrice = unitPrice,
                    TotalValue = playerValue
                });
                totalQuantity += playerQuantity;
                totalValue += playerValue;
            }
            else
            {
                // Add a character header row with 0 quantity if they only have retainer items
                rows.Add(new ItemInventoryRow
                {
                    CharacterId = characterId,
                    CharacterName = characterName,
                    WorldName = worldName,
                    IsRetainer = false,
                    Quantity = 0,
                    UnitPrice = 0,
                    TotalValue = 0
                });
            }

            // Add retainer rows
            foreach (var retainer in retainerData)
            {
                var retainerValue = unitPrice * retainer.Quantity;
                rows.Add(new ItemInventoryRow
                {
                    CharacterId = characterId,
                    CharacterName = characterName,
                    WorldName = worldName,
                    IsRetainer = true,
                    RetainerId = retainer.RetainerId,
                    RetainerName = retainer.RetainerName,
                    Quantity = retainer.Quantity,
                    UnitPrice = unitPrice,
                    TotalValue = retainerValue
                });
                totalQuantity += retainer.Quantity;
                totalValue += retainerValue;
            }
        }

        return new ItemInventoryAggregation
        {
            Rows = rows,
            UnitPrice = unitPrice,
            TotalQuantity = totalQuantity,
            TotalValue = totalValue
        };
    }
}
