using SubscriptionService.Core.Dtos;
using SubscriptionService.Core.Entities;
using SubscriptionService.Core.Exceptions;
using SubscriptionService.Core.Interfaces;

namespace SubscriptionService.Core.Services;

public class PaymentsService(IPaymentRepository payments, ISubscriptionRepository subscriptions, IUnitOfWork uow)
{
    /// <summary>
    /// Создаёт платёж. Если указана подписка, её next_payment_date сдвигается на период вперёд.
    /// Платёж и подписка сохраняются одной транзакцией.
    /// </summary>
    public async Task<PaymentResponse> CreateAsync(Guid userId, PaymentRequest request)
    {
        if (request.Amount is not > 0)
            throw new BadRequestException("Amount must be greater than zero.");
        if (request.PaidAt is null)
            throw new BadRequestException("paid_at is required.");

        Guid? subscriptionId = null;
        if (request.SubscriptionId is { } id)
        {
            var subscription = await subscriptions.GetAsync(userId, id)
                               ?? throw new NotFoundException("Subscription not found.");
            subscription.NextPaymentDate = PeriodCalculator.Add(subscription.NextPaymentDate, subscription.Period);
            subscriptionId = subscription.Id;
        }

        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            SubscriptionId = subscriptionId,
            Amount = PeriodCalculator.Round(request.Amount.Value),
            PaidAt = request.PaidAt.Value
        };
        payments.Add(payment);
        await uow.SaveChangesAsync();

        return PaymentResponse.From(payment);
    }

    public async Task<List<PaymentResponse>> ListAsync(Guid userId, DateOnly? dateFrom, DateOnly? dateTo, Guid? subscriptionId)
    {
        if (dateFrom > dateTo)
            throw new BadRequestException("from must not be later than to.");

        var list = await payments.ListAsync(userId, dateFrom, dateTo, subscriptionId);
        return list.Select(PaymentResponse.From).ToList();
    }

    public async Task DeleteAsync(Guid userId, Guid id)
    {
        var payment = await payments.GetAsync(userId, id)
                      ?? throw new NotFoundException("Payment not found.");
        payments.Remove(payment);
        await uow.SaveChangesAsync();
    }
}
