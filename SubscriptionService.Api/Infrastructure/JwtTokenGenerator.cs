using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using SubscriptionService.Core.Entities;
using SubscriptionService.Core.Interfaces;

namespace SubscriptionService.Api.Infrastructure;

public class JwtSettings
{
    public string Key { get; set; } = "";
    public string Issuer { get; set; } = "SubscriptionService";
    public string Audience { get; set; } = "SubscriptionService";
    public int ExpireMinutes { get; set; } = 60;
}

public class JwtTokenGenerator(Microsoft.Extensions.Options.IOptions<JwtSettings> options, IClock clock) : ITokenGenerator
{
    private readonly JwtSettings _settings = options.Value;

    public string Generate(User user)
    {
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.Key)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _settings.Issuer,
            audience: _settings.Audience,
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, user.Email)
            ],
            expires: clock.UtcNow.AddMinutes(_settings.ExpireMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
