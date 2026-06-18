using Kaleidoscope.Models.Resources;
using Kaleidoscope.Services;
using Xunit;

namespace Kaleidoscope.Tests.Services;

public class AutoRetainerFcPointsSyncServiceTests
{
    private static Resource Existing(DateTime updatedAt) => new()
    {
        Key       = new ResourceKey { OwnerId = 1, OwnerKind = OwnerKind.FreeCompany, ItemId = 1, Slot = -1 },
        Quantity  = 100,
        UpdatedAt = updatedAt,
    };

    [Fact]
    public void ShouldRecord_NoExistingValue_RecordsRegardlessOfForce()
    {
        var entryTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        Assert.True(AutoRetainerFcPointsSyncPolicy.ShouldRecord(null, entryTime, force: false));
        Assert.True(AutoRetainerFcPointsSyncPolicy.ShouldRecord(null, entryTime, force: true));
    }

    [Fact]
    public void ShouldRecord_StoredValueIsFresher_SkipsWhenNotForced()
    {
        var stored = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var older  = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        Assert.False(AutoRetainerFcPointsSyncPolicy.ShouldRecord(Existing(stored), older, force: false));
    }

    [Fact]
    public void ShouldRecord_StoredValueIsFresher_RecordsWhenForced()
    {
        // The manual import is authoritative even over a fresher (possibly wrong) live value.
        var stored = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var older  = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        Assert.True(AutoRetainerFcPointsSyncPolicy.ShouldRecord(Existing(stored), older, force: true));
    }

    [Fact]
    public void ShouldRecord_ConfigValueIsNewer_RecordsWhenNotForced()
    {
        var stored = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var newer  = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        Assert.True(AutoRetainerFcPointsSyncPolicy.ShouldRecord(Existing(stored), newer, force: false));
    }

    [Fact]
    public void ShouldRecord_EqualTimestamps_SkipsWhenNotForced()
    {
        // Background re-read of unchanged config must be a no-op.
        var ts = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        Assert.False(AutoRetainerFcPointsSyncPolicy.ShouldRecord(Existing(ts), ts, force: false));
    }
}
