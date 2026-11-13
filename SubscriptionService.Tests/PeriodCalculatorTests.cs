using SubscriptionService.Core.Entities;
using SubscriptionService.Core.Services;
using Xunit;
using static SubscriptionService.Tests.Support.TestData;

namespace SubscriptionService.Tests;

public class PeriodCalculatorTests
{
    [Theory]
    [InlineData("1.005", "1.01")]
    [InlineData("2.345", "2.35")]
    [InlineData("-1.005", "-1.01")]
    [InlineData("10", "10")]
    public void Round_UsesTwoDigitsAwayFromZero(string value, string expected) =>
        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture),
            PeriodCalculator.Round(decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture)));

    [Fact]
    public void Add_Week_AddsSevenDaysPerPeriod()
    {
        Assert.Equal(D("2026-11-20"), PeriodCalculator.Add(D("2026-11-13"), BillingPeriod.Week));
        Assert.Equal(D("2026-11-27"), PeriodCalculator.Add(D("2026-11-13"), BillingPeriod.Week, 2));
    }

    [Fact]
    public void Add_Month_ClampsToLastDayOfShortMonth() =>
        Assert.Equal(D("2026-02-28"), PeriodCalculator.Add(D("2026-01-31"), BillingPeriod.Month));

    [Fact]
    public void Add_Year_HandlesLeapDay() =>
        Assert.Equal(D("2029-02-28"), PeriodCalculator.Add(D("2028-02-29"), BillingPeriod.Year));

    [Fact]
    public void Add_UnknownPeriod_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => PeriodCalculator.Add(D("2026-11-13"), (BillingPeriod)99));

    [Fact]
    public void NextPaymentDate_StartInFuture_ReturnsStart() =>
        Assert.Equal(D("2026-12-01"), PeriodCalculator.NextPaymentDate(D("2026-12-01"), BillingPeriod.Month, D("2026-11-13")));

    [Fact]
    public void NextPaymentDate_StartIsToday_ReturnsToday() =>
        Assert.Equal(D("2026-11-13"), PeriodCalculator.NextPaymentDate(D("2026-11-13"), BillingPeriod.Week, D("2026-11-13")));

    [Fact]
    public void NextPaymentDate_MonthlyStartedLongAgo_ReturnsNextFutureDate() =>
        Assert.Equal(D("2026-12-01"), PeriodCalculator.NextPaymentDate(D("2026-01-01"), BillingPeriod.Month, D("2026-11-13")));

    [Fact]
    public void NextPaymentDate_MonthEnd_DoesNotDriftAfterShortMonth() =>
        // 31 янв → 28 фев → 31 мар (счёт от исходной даты, а не от предыдущей)
        Assert.Equal(D("2026-03-31"), PeriodCalculator.NextPaymentDate(D("2026-01-31"), BillingPeriod.Month, D("2026-03-01")));

    [Fact]
    public void NextPaymentDate_Weekly_ReturnsNextWeekday() =>
        Assert.Equal(D("2026-11-19"), PeriodCalculator.NextPaymentDate(D("2026-10-01"), BillingPeriod.Week, D("2026-11-13")));

    [Fact]
    public void NextPaymentDate_Yearly_ReturnsNextAnniversary() =>
        Assert.Equal(D("2027-02-28"), PeriodCalculator.NextPaymentDate(D("2024-02-29"), BillingPeriod.Year, D("2026-11-13")));

    [Theory]
    [InlineData(BillingPeriod.Week, "100", "433.33")]
    [InlineData(BillingPeriod.Month, "250.555", "250.56")]
    [InlineData(BillingPeriod.Year, "1000", "83.33")]
    public void ToMonthly_ConvertsToMonthlyCost(BillingPeriod period, string amount, string expected) =>
        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture),
            PeriodCalculator.ToMonthly(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture), period));

    [Fact]
    public void ToMonthly_UnknownPeriod_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => PeriodCalculator.ToMonthly(100m, (BillingPeriod)99));
}
