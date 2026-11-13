using System.Globalization;
using SubscriptionService.Core.Dtos;
using SubscriptionService.Core.Entities;
using SubscriptionService.Core.Exceptions;
using SubscriptionService.Core.Interfaces;

namespace SubscriptionService.Core.Services;

public class AnalyticsService(IPaymentRepository payments, ISubscriptionRepository subscriptions, IClock clock)
{
    public const string NoSubscriptionName = "No subscription";

    /// <summary>Расходы за период, сгруппированные по подпискам. Share — доля группы в процентах.</summary>
    public async Task<SummaryResponse> SummaryAsync(Guid userId, DateOnly? dateFrom, DateOnly? dateTo)
    {
        if (dateFrom > dateTo)
            throw new BadRequestException("from must not be later than to.");

        var list = await payments.ListAsync(userId, dateFrom, dateTo, null);
        var total = PeriodCalculator.Round(list.Sum(p => p.Amount));

        var groups = list
            .GroupBy(p => p.SubscriptionId)
            .Select(g =>
            {
                var amount = PeriodCalculator.Round(g.Sum(p => p.Amount));
                var share = total == 0 ? 0 : PeriodCalculator.Round(amount / total * 100);
                return new SummaryGroup(g.First().Subscription?.Name ?? NoSubscriptionName, amount, share);
            })
            .OrderByDescending(g => g.Amount)
            .ToList();

        return new SummaryResponse(total, groups);
    }

    /// <summary>
    /// Прогноз расходов по активным подпискам на months календарных месяцев, начиная с текущего.
    /// Учитываются списания от сегодняшней даты и позже.
    /// </summary>
    public async Task<List<ForecastItem>> ForecastAsync(Guid userId, int months)
    {
        if (months < 1 || months > 12)
            throw new BadRequestException("months must be between 1 and 12.");

        var today = clock.Today;
        var firstMonth = new DateOnly(today.Year, today.Month, 1);
        var endExclusive = firstMonth.AddMonths(months);
        var totals = new decimal[months];

        var active = await subscriptions.ListAsync(userId, SubscriptionStatus.Active);
        foreach (var subscription in active)
        {
            for (var k = 0; ; k++)
            {
                var date = PeriodCalculator.Add(subscription.NextPaymentDate, subscription.Period, k);
                if (date >= endExclusive)
                    break;
                if (date < today)
                    continue;

                var index = (date.Year - firstMonth.Year) * 12 + date.Month - firstMonth.Month;
                totals[index] += subscription.Amount;
            }
        }

        return Enumerable.Range(0, months)
            .Select(i => new ForecastItem(
                firstMonth.AddMonths(i).ToString("yyyy-MM", CultureInfo.InvariantCulture),
                PeriodCalculator.Round(totals[i])))
            .ToList();
    }

    /// <summary>Самые дорогие активные подписки в пересчёте на месяц.</summary>
    public async Task<List<TopItem>> TopAsync(Guid userId, int limit)
    {
        if (limit < 1 || limit > 50)
            throw new BadRequestException("limit must be between 1 and 50.");

        var active = await subscriptions.ListAsync(userId, SubscriptionStatus.Active);
        return active
            .Select(s => new TopItem(s.Id, s.Name, PeriodCalculator.ToMonthly(s.Amount, s.Period)))
            .OrderByDescending(t => t.MonthlyCost)
            .Take(limit)
            .ToList();
    }
}
