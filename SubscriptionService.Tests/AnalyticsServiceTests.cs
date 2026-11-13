using SubscriptionService.Core.Entities;
using SubscriptionService.Core.Exceptions;
using SubscriptionService.Core.Services;
using SubscriptionService.Tests.Support;
using Xunit;
using static SubscriptionService.Tests.Support.TestData;

namespace SubscriptionService.Tests;

public class AnalyticsServiceTests
{
    private readonly FakePaymentRepository _payments = new();
    private readonly FakeSubscriptionRepository _subscriptions = new();
    private readonly FakeClock _clock = new(); // сегодня 2026-11-13
    private readonly AnalyticsService _sut;

    public AnalyticsServiceTests() => _sut = new AnalyticsService(_payments, _subscriptions, _clock);

    private Subscription AddSub(
        string name = "Netflix",
        decimal amount = 100m,
        BillingPeriod period = BillingPeriod.Month,
        string next = "2026-11-20",
        SubscriptionStatus status = SubscriptionStatus.Active)
    {
        var sub = Sub(UserId, name, amount, period, next, status);
        _subscriptions.Items.Add(sub);
        return sub;
    }

    // ---------- Summary ----------

    [Fact]
    public async Task Summary_FromAfterTo_ThrowsBadRequest() =>
        await Assert.ThrowsAsync<BadRequestException>(
            () => _sut.SummaryAsync(UserId, D("2026-11-30"), D("2026-11-01")));

    [Fact]
    public async Task Summary_NoPayments_ReturnsZeroTotalAndNoGroups()
    {
        var result = await _sut.SummaryAsync(UserId, null, null);

        Assert.Equal(0m, result.Total);
        Assert.Empty(result.Groups);
    }

    [Fact]
    public async Task Summary_GroupsBySubscriptionWithSharesSortedByAmount()
    {
        var a = AddSub("Netflix");
        var b = AddSub("Spotify");
        _payments.Items.Add(Pay(UserId, 100m, "2026-11-01", a));
        _payments.Items.Add(Pay(UserId, 120m, "2026-11-05", b));
        _payments.Items.Add(Pay(UserId, 80m, "2026-11-06", b));

        var result = await _sut.SummaryAsync(UserId, null, null);

        Assert.Equal(300m, result.Total);
        Assert.Equal(2, result.Groups.Count);
        Assert.Equal("Spotify", result.Groups[0].Name);
        Assert.Equal(200m, result.Groups[0].Amount);
        Assert.Equal(66.67m, result.Groups[0].Share);
        Assert.Equal("Netflix", result.Groups[1].Name);
        Assert.Equal(33.33m, result.Groups[1].Share);
    }

    [Fact]
    public async Task Summary_PaymentWithoutSubscription_GoesToDefaultGroup()
    {
        _payments.Items.Add(Pay(UserId, 500m, "2026-11-01"));

        var result = await _sut.SummaryAsync(UserId, null, null);

        var group = Assert.Single(result.Groups);
        Assert.Equal(AnalyticsService.NoSubscriptionName, group.Name);
        Assert.Equal(100m, group.Share);
    }

    [Fact]
    public async Task Summary_ZeroTotal_ShareIsZero()
    {
        _payments.Items.Add(Pay(UserId, 0m, "2026-11-01"));

        var result = await _sut.SummaryAsync(UserId, null, null);

        Assert.Equal(0m, result.Total);
        Assert.Equal(0m, Assert.Single(result.Groups).Share);
    }

    [Fact]
    public async Task Summary_AppliesDateRangeAndIgnoresOtherUsers()
    {
        _payments.Items.Add(Pay(UserId, 100m, "2026-10-31"));
        _payments.Items.Add(Pay(UserId, 200m, "2026-11-15"));
        _payments.Items.Add(Pay(UserId, 400m, "2026-12-01"));
        _payments.Items.Add(Pay(OtherUserId, 999m, "2026-11-15"));

        var result = await _sut.SummaryAsync(UserId, D("2026-11-01"), D("2026-11-30"));

        Assert.Equal(200m, result.Total);
    }

    // ---------- Forecast ----------

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    public async Task Forecast_MonthsOutOfRange_ThrowsBadRequest(int months) =>
        await Assert.ThrowsAsync<BadRequestException>(() => _sut.ForecastAsync(UserId, months));

    [Fact]
    public async Task Forecast_MonthlySubscription_ChargesEveryMonthAndLabelsMonths()
    {
        AddSub(amount: 100m, next: "2026-11-20");

        var result = await _sut.ForecastAsync(UserId, 3);

        Assert.Equal(new[] { "2026-11", "2026-12", "2027-01" }, result.Select(r => r.Month));
        Assert.Equal(new[] { 100m, 100m, 100m }, result.Select(r => r.Amount));
    }

    [Fact]
    public async Task Forecast_WeeklySubscription_CountsAllChargesInEachMonth()
    {
        AddSub(amount: 10m, period: BillingPeriod.Week, next: "2026-11-14");

        var result = await _sut.ForecastAsync(UserId, 3);

        // ноябрь: 14, 21, 28; декабрь: 5, 12, 19, 26; январь: 2, 9, 16, 23, 30
        Assert.Equal(new[] { 30m, 40m, 50m }, result.Select(r => r.Amount));
    }

    [Fact]
    public async Task Forecast_OverdueNextPayment_SkipsPastChargesInThisMonth()
    {
        AddSub(amount: 100m, next: "2026-11-01");

        var result = await _sut.ForecastAsync(UserId, 3);

        Assert.Equal(new[] { 0m, 100m, 100m }, result.Select(r => r.Amount));
    }

    [Fact]
    public async Task Forecast_ChargeToday_IsIncluded()
    {
        AddSub(amount: 100m, next: "2026-11-13");

        var result = await _sut.ForecastAsync(UserId, 1);

        Assert.Equal(100m, Assert.Single(result).Amount);
    }

    [Fact]
    public async Task Forecast_YearlySubscription_AppearsOnlyInItsMonth()
    {
        AddSub(amount: 1200m, period: BillingPeriod.Year, next: "2026-12-25");

        var result = await _sut.ForecastAsync(UserId, 3);

        Assert.Equal(new[] { 0m, 1200m, 0m }, result.Select(r => r.Amount));
    }

    [Fact]
    public async Task Forecast_NextPaymentBeyondHorizon_IsIgnored()
    {
        AddSub(amount: 1200m, period: BillingPeriod.Year, next: "2027-06-01");

        var result = await _sut.ForecastAsync(UserId, 2);

        Assert.All(result, r => Assert.Equal(0m, r.Amount));
    }

    [Fact]
    public async Task Forecast_IgnoresPausedSubscriptions()
    {
        AddSub(status: SubscriptionStatus.Paused);

        var result = await _sut.ForecastAsync(UserId, 1);

        Assert.Equal(0m, Assert.Single(result).Amount);
    }

    // ---------- Top ----------

    [Theory]
    [InlineData(0)]
    [InlineData(51)]
    public async Task Top_LimitOutOfRange_ThrowsBadRequest(int limit) =>
        await Assert.ThrowsAsync<BadRequestException>(() => _sut.TopAsync(UserId, limit));

    [Fact]
    public async Task Top_SortsByMonthlyCostAndExcludesInactive()
    {
        AddSub("Monthly", 400m);
        AddSub("Weekly", 100m, BillingPeriod.Week);
        AddSub("Yearly", 1200m, BillingPeriod.Year);
        AddSub("Paused", 9999m, status: SubscriptionStatus.Paused);

        var result = await _sut.TopAsync(UserId, 5);

        Assert.Equal(new[] { "Weekly", "Monthly", "Yearly" }, result.Select(t => t.Name));
        Assert.Equal(new[] { 433.33m, 400m, 100m }, result.Select(t => t.MonthlyCost));
    }

    [Fact]
    public async Task Top_RespectsLimit()
    {
        AddSub("A", 300m);
        AddSub("B", 200m);
        AddSub("C", 100m);

        var result = await _sut.TopAsync(UserId, 2);

        Assert.Equal(new[] { "A", "B" }, result.Select(t => t.Name));
    }
}
