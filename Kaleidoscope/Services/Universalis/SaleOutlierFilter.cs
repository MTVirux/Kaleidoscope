using Kaleidoscope.Models.Universalis;

namespace Kaleidoscope.Services.Universalis;

/// <summary>
/// Pure, stateless outlier-decision logic for market-board sales.
/// Shared by the live price feed and the sales tools so the reference-price and
/// discrepancy maths live in exactly one place. Safe to call from any thread.
/// </summary>
public static class SaleOutlierFilter
{
    /// <summary>
    /// Blends a listing reference and a sale reference into a single reference price.
    /// Averages the two when both are available, otherwise uses whichever is present.
    /// Returns 0 when neither is available.
    /// </summary>
    public static double ComputeReferencePrice(double listingReference, double saleReference)
    {
        if (listingReference > 0 && saleReference > 0)
            return (listingReference + saleReference) / 2.0;
        if (listingReference > 0)
            return listingReference;
        if (saleReference > 0)
            return saleReference;
        return 0;
    }

    /// <summary>
    /// Fixed-percentage outlier check: flags a price that deviates from the reference by more
    /// than <paramref name="thresholdFraction"/> in either direction. Returns false (not an
    /// outlier) when there is no usable reference price.
    /// </summary>
    public static bool IsRatioOutlier(double price, double referencePrice, double thresholdFraction, out string reason)
    {
        reason = string.Empty;
        if (referencePrice <= 0)
            return false;

        var ratio = price / referencePrice;
        var minRatio = 1.0 - thresholdFraction;
        var maxRatio = 1.0 + thresholdFraction;
        if (ratio < minRatio || ratio > maxRatio)
        {
            var effectiveThreshold = (int)(thresholdFraction * 100);
            reason = $"{(ratio * 100 - 100):+0;-0}% from reference (threshold: {effectiveThreshold}%)";
            return true;
        }

        return false;
    }

    /// <summary>
    /// Full sale-outlier decision used by the live price feed. Resolves listing/sale references
    /// (median or average per settings), then applies either standard-deviation or fixed-percentage
    /// filtering with optional bulk-sale leniency.
    /// </summary>
    /// <param name="pricePerUnit">The sale's unit price.</param>
    /// <param name="quantity">The sale's stack size.</param>
    /// <param name="isHq">Whether the sale is high quality.</param>
    /// <param name="listing">Cached lowest listings for the item/world, or null.</param>
    /// <param name="sales">Cached recent sales for the item/world, or null.</param>
    /// <param name="settings">Price-tracking settings driving the decision.</param>
    /// <param name="bulkSaleMaxLeniencyQuantity">Stack size at which bulk leniency reaches its maximum.</param>
    /// <param name="referencePrice">The resolved reference price (0 when no reference data).</param>
    /// <param name="reason">Human-readable reason when the sale is an outlier.</param>
    /// <returns>True when the sale should be treated as an outlier and ignored.</returns>
    public static bool IsOutlier(
        int pricePerUnit,
        int quantity,
        bool isHq,
        ListingsCacheEntry? listing,
        RecentSalesCacheEntry? sales,
        PriceTrackingSettings settings,
        int bulkSaleMaxLeniencyQuantity,
        out double referencePrice,
        out string reason)
    {
        reason = string.Empty;

        double listingRef = 0;
        if (listing != null)
        {
            listingRef = settings.UseMedianForReference
                ? (isHq ? listing.MedianPriceHq : listing.MedianPriceNq)
                : (isHq ? listing.AveragePriceHq : listing.AveragePriceNq);
        }

        double saleRef = 0;
        double saleStdDev = 0;
        double saleMean = 0;
        if (sales != null)
        {
            saleRef = settings.UseMedianForReference
                ? (isHq ? sales.MedianPriceHq : sales.MedianPriceNq)
                : (isHq ? sales.AveragePriceHq : sales.AveragePriceNq);
            saleStdDev = isHq ? sales.StdDevHq : sales.StdDevNq;
            saleMean = isHq ? sales.AveragePriceHq : sales.AveragePriceNq;
        }

        referencePrice = ComputeReferencePrice(listingRef, saleRef);
        if (referencePrice <= 0)
            return false;

        if (settings.UseStdDevFilter && saleStdDev > 0 && saleMean > 0)
        {
            // Standard deviation-based filtering
            var zScore = Math.Abs(pricePerUnit - saleMean) / saleStdDev;
            if (zScore > settings.StdDevThreshold)
            {
                reason = $"z-score {zScore:F2} > {settings.StdDevThreshold:F1}";
                return true;
            }

            return false;
        }

        // Fixed percentage threshold filtering
        var threshold = settings.SaleDiscrepancyThreshold / 100.0;

        // Adjust threshold for bulk sales if enabled: more quantity = more lenient, up to max.
        if (settings.AdjustForBulkSales && quantity > 1)
        {
            var quantityFactor = 1.0 + (Math.Min(quantity, bulkSaleMaxLeniencyQuantity) / (double)bulkSaleMaxLeniencyQuantity) * (settings.BulkSaleMaxLeniency - 1.0);
            threshold *= quantityFactor;
        }

        return IsRatioOutlier(pricePerUnit, referencePrice, threshold, out reason);
    }
}
