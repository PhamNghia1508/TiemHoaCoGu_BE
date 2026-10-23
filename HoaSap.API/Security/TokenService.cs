using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using HoaSap.Domain.Entities;
using Microsoft.IdentityModel.Tokens;

namespace HoaSap.API.Security;

public static class TokenService
{
    public static string NewOpaqueToken() => Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
    public static string AccessToken(Customer user, IConfiguration config, out DateTime expires)
    {
        expires = DateTime.UtcNow.AddMinutes(config.GetValue("Jwt:ExpirationMinutes", 60));
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), new Claim(ClaimTypes.Role, user.Role),
            new Claim(SecurityStampService.ClaimType, SecurityStampService.Derive(user.PasswordHash, config["Jwt:Key"]!)), new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")) };
        var jwt = new JwtSecurityToken(config["Jwt:Issuer"], config["Jwt:Audience"], claims, expires: expires,
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Jwt:Key"]!)), SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }
}
