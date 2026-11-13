using SubscriptionService.Core.Entities;

namespace SubscriptionService.Core.Services;

/// <summary>Чистые функции для расчёта дат и сумм. Единое правило округления: 2 знака, от нуля.</summary>
public static class PeriodCalculator
{
    public static decimal Round(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);

    /// <summary>Сдвигает дату на count периодов (отсчёт всегда от исходной даты, чтобы не «плыл» день месяца).</summary>
    public static DateOnly Add(DateOnly date, BillingPeriod period, int count = 1) => period switch
    {
        BillingPeriod.Week => date.AddDays(7 * count),
        BillingPeriod.Month => date.AddMonths(count),
        BillingPeriod.Year => date.AddYears(count),
        _ => throw new ArgumentOutOfRangeException(nameof(period))
    };

    /// <summary>Ближайшая дата списания, не раньше today.</summary>
    public static DateOnly NextPaymentDate(DateOnly start, BillingPeriod period, DateOnly today)
    {
        var n = 0;
        var date = start;
        while (date < today)
        {
            n++;
            date = Add(start, period, n);
        }
        return date;
    }

    /// <summary>Стоимость подписки в пересчёте на месяц.</summary>
    public static decimal ToMonthly(decimal amount, BillingPeriod period) => Round(period switch
    {
        BillingPeriod.Week => amount * 52m / 12m,
        BillingPeriod.Month => amount,
        BillingPeriod.Year => amount / 12m,
        _ => throw new ArgumentOutOfRangeException(nameof(period))
    });
}
