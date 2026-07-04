using Kaleidoscope.Models.Universalis;
using Kaleidoscope.Services.Universalis;
using Xunit;

namespace Kaleidoscope.Tests.Services.Universalis;

/// <summary>
/// Regression pins for <see cref="SaleOutlierFilter"/>: reference-price blending, the ratio
/// outlier boundaries, the standard-deviation branch (including N&lt;2 and no fall-through), and
/// bulk-sale leniency scaling. These assert the current observed behaviour, not a spec change.
/// </summary>
public class SaleOutlierFilterTests
{
    private static PriceTrackingSettings NewSettings(Action<PriceTrackingSettings>? configure = null)
    {
        var settings = new PriceTrackingSettings();
        configure?.Invoke(settings);
        return settings;
    }

    private static ListingsCacheEntry Listings(params int[] nqPrices)
    {
        var entry = new ListingsCacheEntry();
        entry.SetPrices(nqPrices, isHq: false);
        return entry;
    }

    private static RecentSalesCacheEntry Sales(params int[] nqPrices)
    {
        var entry = new RecentSalesCacheEntry();
        entry.SetPrices(nqPrices, isHq: false);
        return entry;
    }

    // ── ComputeReferencePrice ───────────────────────────────────────────

    [Fact]
    public void ComputeReferencePrice_BothPresent_AveragesThem()
        => Assert.Equal(150.0, SaleOutlierFilter.ComputeReferencePrice(100, 200));

    [Fact]
    public void ComputeReferencePrice_OnlyListing_ReturnsListing()
        => Assert.Equal(100.0, SaleOutlierFilter.ComputeReferencePrice(100, 0));

    [Fact]
    public void ComputeReferencePrice_OnlySale_ReturnsSale()
        => Assert.Equal(200.0, SaleOutlierFilter.ComputeReferencePrice(0, 200));

    [Fact]
    public void ComputeReferencePrice_NoReference_ReturnsZero()
        => Assert.Equal(0.0, SaleOutlierFilter.ComputeReferencePrice(0, 0));

    // ── IsRatioOutlier boundaries ───────────────────────────────────────

    [Fact]
    public void IsRatioOutlier_NoReference_ReturnsFalse()
    {
        Assert.False(SaleOutlierFilter.IsRatioOutlier(100, 0, 0.5, out var reason));
        Assert.Equal(string.Empty, reason);
    }

    [Fact]
    public void IsRatioOutlier_WithinThreshold_ReturnsFalse()
        => Assert.False(SaleOutlierFilter.IsRatioOutlier(120, 100, 0.5, out _));

    [Fact]
    public void IsRatioOutlier_AtUpperBoundary_NotOutlier()
        => Assert.False(SaleOutlierFilter.IsRatioOutlier(150, 100, 0.5, out _));

    [Fact]
    public void IsRatioOutlier_AtLowerBoundary_NotOutlier()
        => Assert.False(SaleOutlierFilter.IsRatioOutlier(50, 100, 0.5, out _));

    [Fact]
    public void IsRatioOutlier_AboveThreshold_ReturnsTrueWithReason()
    {
        Assert.True(SaleOutlierFilter.IsRatioOutlier(200, 100, 0.5, out var reason));
        Assert.Equal("+100% from reference (threshold: 50%)", reason);
    }

    [Fact]
    public void IsRatioOutlier_BelowThreshold_ReturnsTrueWithReason()
    {
        Assert.True(SaleOutlierFilter.IsRatioOutlier(25, 100, 0.5, out var reason));
        Assert.Equal("-75% from reference (threshold: 50%)", reason);
    }

    // ── IsOutlier: reference resolution ─────────────────────────────────

    [Fact]
    public void IsOutlier_NoReferenceData_ReturnsFalse()
    {
        var result = SaleOutlierFilter.IsOutlier(
            100, 1, false, null, null, NewSettings(), 100, out var referencePrice, out var reason);

        Assert.False(result);
        Assert.Equal(0.0, referencePrice);
        Assert.Equal(string.Empty, reason);
    }

    [Fact]
    public void IsOutlier_BlendsListingAndSaleReference()
    {
        var settings = NewSettings(s => s.UseMedianForReference = false);

        var result = SaleOutlierFilter.IsOutlier(
            150, 1, false, Listings(100), Sales(200), settings, 100, out var referencePrice, out _);

        Assert.Equal(150.0, referencePrice);
        Assert.False(result);
    }

    // ── IsOutlier: standard-deviation branch ────────────────────────────

    [Fact]
    public void IsOutlier_StdDevBranch_FlagsHighZScore()
    {
        var settings = NewSettings(s =>
        {
            s.UseStdDevFilter = true;
            s.UseMedianForReference = false;
            s.StdDevThreshold = 2.0;
        });

        // {100,100,100,200}: mean 125, sample std-dev 50.
        var result = SaleOutlierFilter.IsOutlier(
            300, 1, false, null, Sales(100, 100, 100, 200), settings, 100, out var referencePrice, out var reason);

        Assert.True(result);
        Assert.Equal(125.0, referencePrice);
        Assert.Equal("z-score 3.50 > 2.0", reason);
    }

    [Fact]
    public void IsOutlier_StdDevBranch_WithinZScore_DoesNotFallThroughToRatio()
    {
        var settings = NewSettings(s =>
        {
            s.UseStdDevFilter = true;
            s.UseMedianForReference = false;
            s.StdDevThreshold = 2.0;
            s.SaleDiscrepancyThreshold = 1; // would flag under the ratio filter, but must not run
        });

        // z-score |150-125|/50 = 0.5 < 2.0, so the std-dev branch returns without ratio filtering.
        var result = SaleOutlierFilter.IsOutlier(
            150, 1, false, null, Sales(100, 100, 100, 200), settings, 100, out _, out var reason);

        Assert.False(result);
        Assert.Equal(string.Empty, reason);
    }

    [Fact]
    public void IsOutlier_StdDevEnabled_SingleSale_FallsThroughToRatio()
    {
        var settings = NewSettings(s =>
        {
            s.UseStdDevFilter = true;
            s.UseMedianForReference = false;
            s.SaleDiscrepancyThreshold = 50;
        });

        // A single sale gives std-dev 0 (N<2), so std-dev filtering is skipped and the ratio filter runs.
        var result = SaleOutlierFilter.IsOutlier(
            300, 1, false, null, Sales(125), settings, 100, out var referencePrice, out var reason);

        Assert.True(result);
        Assert.Equal(125.0, referencePrice);
        Assert.Contains("from reference", reason);
        Assert.DoesNotContain("z-score", reason);
    }

    // ── IsOutlier: bulk-sale leniency scaling ───────────────────────────

    [Fact]
    public void IsOutlier_Bulk_Quantity1_NoLeniency_Outlier()
    {
        var settings = NewSettings(s =>
        {
            s.AdjustForBulkSales = true;
            s.BulkSaleMaxLeniency = 2.0;
            s.SaleDiscrepancyThreshold = 50;
        });

        // ratio 1.6 vs 50% threshold (max 1.5) -> outlier; no leniency applies at quantity 1.
        var result = SaleOutlierFilter.IsOutlier(
            160, 1, false, Listings(100), null, settings, 100, out var referencePrice, out _);

        Assert.True(result);
        Assert.Equal(100.0, referencePrice);
    }

    [Fact]
    public void IsOutlier_Bulk_MaxQuantity_FullLeniency_NotOutlier()
    {
        var settings = NewSettings(s =>
        {
            s.AdjustForBulkSales = true;
            s.BulkSaleMaxLeniency = 2.0;
            s.SaleDiscrepancyThreshold = 50;
        });

        // At the max leniency quantity the threshold doubles to 100% (max ratio 2.0); 1.6 is inside.
        var result = SaleOutlierFilter.IsOutlier(
            160, 100, false, Listings(100), null, settings, 100, out _, out _);

        Assert.False(result);
    }

    [Fact]
    public void IsOutlier_Bulk_PartialQuantity_ScalesThreshold()
    {
        var settings = NewSettings(s =>
        {
            s.AdjustForBulkSales = true;
            s.BulkSaleMaxLeniency = 2.0;
            s.SaleDiscrepancyThreshold = 50;
        });

        // quantity 50 of 100 -> factor 1.5 -> threshold 75% (max ratio 1.75).
        var withinLeniency = SaleOutlierFilter.IsOutlier(
            160, 50, false, Listings(100), null, settings, 100, out _, out _);
        Assert.False(withinLeniency);

        var beyondLeniency = SaleOutlierFilter.IsOutlier(
            180, 50, false, Listings(100), null, settings, 100, out _, out var reason);
        Assert.True(beyondLeniency);
        Assert.Contains("threshold: 75%", reason);
    }
}
