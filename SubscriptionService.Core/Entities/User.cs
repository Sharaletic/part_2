namespace SubscriptionService.Core.Entities;

public class User
{
    public Guid Id { get; set; }
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public int FailedLoginCount { get; set; }
    public DateTime? LockedUntil { get; set; }
}
