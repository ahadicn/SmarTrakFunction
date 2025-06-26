using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Tokens;
using System;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;

namespace SmarTrakFunctions
{
    public class ValidateToken
    {
        private readonly ILogger<ValidateToken> _logger;

        public ValidateToken(ILogger<ValidateToken> logger)
        {
            _logger = logger;
        }

        [Function("ValidateToken")]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "validate-token")] HttpRequest req)
        {
            _logger.LogInformation("Processing token validation request.");

            if (!req.Headers.TryGetValue("Authorization", out var authHeader) ||
                !authHeader.Any() || !authHeader.First().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Invalid or missing Bearer token.");
                return new UnauthorizedResult();
            }

            var token = authHeader.First().Substring("Bearer ".Length).Trim();
            var principal = await ValidateTokenAsync(token, _logger);

            if (principal == null)
            {
                return new UnauthorizedResult();
            }

            var roles = principal.FindAll(ClaimTypes.Role).Select(r => r.Value.ToLowerInvariant()).ToList();

            if (!roles.Contains("admin") && !roles.Contains("user"))
            {
                _logger.LogWarning("User lacks required roles.");
                return new StatusCodeResult(StatusCodes.Status403Forbidden);
            }

            string userId = principal.Claims.FirstOrDefault(c => c.Type == "oid")?.Value;
            // OR fallback directly from raw JWT:
            if (string.IsNullOrEmpty(userId))
            {
                var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
                userId = jwt.Claims.FirstOrDefault(c => c.Type == "oid")?.Value;
            }
            var name = principal.FindFirst("name")?.Value;
            var email = principal.FindFirst("preferred_username")?.Value;
            _logger.LogInformation($"User: {userId}, Name: {name}, email:{email}, Roles: {string.Join(",", roles)}");

            return new OkObjectResult("Function executed successfully");
        }

        private static async Task<ClaimsPrincipal> ValidateTokenAsync(string token, ILogger log)
        {
            var tenantId = Environment.GetEnvironmentVariable("TenantId");
            var audience = Environment.GetEnvironmentVariable("Audience");
            var domain = Environment.GetEnvironmentVariable("Domain");

            if (string.IsNullOrEmpty(tenantId) || string.IsNullOrEmpty(audience))
            {
                log.LogError("TenantId or Audience environment variable is missing.");
                return null;
            }

            //var stsDiscoveryEndpoint = $"https://login.microsoftonline.com/{tenantId}/v2.0/.well-known/openid-configuration";
            var stsDiscoveryEndpoint = $"https://{domain}.ciamlogin.com/{domain}.onmicrosoft.com/v2.0/.well-known/openid-configuration";

            var configManager = new ConfigurationManager<OpenIdConnectConfiguration>(
                stsDiscoveryEndpoint,
                new OpenIdConnectConfigurationRetriever());

            try
            {
                var config = await configManager.GetConfigurationAsync();

                var tokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = $"https://{tenantId}.ciamlogin.com/{tenantId}/v2.0",
                    ValidateAudience = true,
                    ValidAudiences = new[] { audience },
                    ValidateLifetime = true,
                    RoleClaimType = "roles",
                    NameClaimType = "oid", 
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKeys = config.SigningKeys,
                    ClockSkew = TimeSpan.FromMinutes(5)
                };

                var handler = new JwtSecurityTokenHandler();
                return handler.ValidateToken(token, tokenValidationParameters, out _);
            }
            catch (SecurityTokenException ex)
            {
                log.LogError($"Token validation failed: {ex.Message}");
                return null;
            }
            catch (Exception ex)
            {
                log.LogError($"Unexpected error during token validation: {ex.Message}");
                return null;
            }
        }
    }
}