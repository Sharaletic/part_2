namespace SubscriptionService.Core.Exceptions;

/// <summary>Базовое исключение бизнес-логики. Api превращает его в ответ {code, message}.</summary>
public abstract class AppException : Exception
{
    public string Code { get; }

    protected AppException(string code, string message) : base(message)
    {
        Code = code;
    }
}

public class BadRequestException(string message) : AppException("validation_error", message);

public class NotFoundException(string message) : AppException("not_found", message);

public class ConflictException(string code, string message) : AppException(code, message);

public class UnauthorizedException()
    : AppException("invalid_credentials", "Invalid email or password.");

public class LockedException()
    : AppException("account_locked", "Account is temporarily locked after too many failed login attempts.");
