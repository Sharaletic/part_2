namespace SubscriptionService.Core.Entities;

public class Subscription
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Name { get; set; } = "";
    public decimal Amount { get; set; }
    public BillingPeriod Period { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly NextPaymentDate { get; set; }
    public SubscriptionStatus Status { get; set; }
    public string? Category { get; set; }
    public string? Note { get; set; }
}
