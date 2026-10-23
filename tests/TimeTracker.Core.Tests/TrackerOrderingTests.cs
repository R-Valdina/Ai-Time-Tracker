namespace TimeTracker.Core.Tests;

/// <summary>Checks natural numeric sorting, stable equal references, and retained date order.</summary>
public sealed class TrackerOrderingTests
{
    [Theory]
    [InlineData(TrackerSortOrder.Ascending, "CARD-1,CARD-2,CARD-10")]
    [InlineData(TrackerSortOrder.Descending, "CARD-10,CARD-2,CARD-1")]
    [InlineData(TrackerSortOrder.Added, "CARD-10,CARD-2,CARD-1")]
    public void NumericReferencesUseNaturalOrder(TrackerSortOrder order, string expected)
    {
        var entries = new[] { Entry("CARD-10"), Entry("CARD-2"), Entry("CARD-1") };
        Assert.Equal(expected.Split(','), TrackerOrdering.Order(entries, order).Select(entry => entry.Label));
        Assert.Equal(new[] { "CARD-10", "CARD-2", "CARD-1" }, entries.Select(entry => entry.Label));
    }

    [Fact]
    public void CaseInsensitiveEqualReferencesKeepTheirOriginalOrder()
    {
        var entries = new[] { Entry("bug-2"), Entry("BUG-2"), Entry("BUG-10"), Entry("FEATURE-1") };
        Assert.Equal(entries, TrackerOrdering.Order(entries, TrackerSortOrder.Ascending));
        Assert.Equal(new[] { entries[3], entries[2], entries[0], entries[1] },
            TrackerOrdering.Order(entries, TrackerSortOrder.Descending));
    }

    [Theory]
    [InlineData(TrackerSortOrder.Ascending, "Z-2,Z-10,A-1")]
    [InlineData(TrackerSortOrder.Descending, "Z-10,Z-2,A-1")]
    public void HistoryKeepsNewerDatesFirst(TrackerSortOrder order, string expected)
    {
        var entries = new[] { Entry("A-1") with { Date = new DateOnly(2026, 10, 19) }, Entry("Z-10"), Entry("Z-2") };
        Assert.Equal(expected.Split(','), TrackerOrdering.Order(entries, order).Select(entry => entry.Label));
    }

    [Fact]
    public void UnsupportedSortOrderIsRejected() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => TrackerOrdering.Order([], (TrackerSortOrder)999));

    /// <summary>Builds an immutable entry with a nonzero total to detect accidental record replacement.</summary>
    private static TrackerEntry Entry(string label) =>
        new(Guid.NewGuid(), label, TrackerColors.All[0], new DateOnly(2026, 10, 20), TimeSpan.FromMinutes(12));
}
