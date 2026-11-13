using SubscriptionService.Core.Exceptions;

namespace SubscriptionService.Api.Infrastructure;

public record ErrorResponse(string Code, string Message);

/// <summary>Превращает исключения бизнес-логики в ответы вида {code, message}.</summary>
public class ErrorHandlingMiddleware(RequestDelegate next, ILogger<ErrorHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (AppException ex)
        {
            var status = ex switch
            {
                BadRequestException => StatusCodes.Status400BadRequest,
                NotFoundException => StatusCodes.Status404NotFound,
                ConflictException => StatusCodes.Status409Conflict,
                UnauthorizedException => StatusCodes.Status401Unauthorized,
                LockedException => StatusCodes.Status423Locked,
                _ => StatusCodes.Status400BadRequest
            };
            await Write(context, status, ex.Code, ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception");
            await Write(context, StatusCodes.Status500InternalServerError, "internal_error", "Internal server error.");
        }
    }

    private static Task Write(HttpContext context, int status, string code, string message)
    {
        context.Response.StatusCode = status;
        return context.Response.WriteAsJsonAsync(new ErrorResponse(code, message));
    }
}
