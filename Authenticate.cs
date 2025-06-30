//using Microsoft.Azure.Functions.Worker;
//using Microsoft.Azure.Functions.Worker.Http;
//using Microsoft.Extensions.Logging;
//using System.IdentityModel.Tokens.Jwt;
//using Microsoft.IdentityModel.Tokens;
//using Microsoft.IdentityModel.Protocols;
//using Microsoft.IdentityModel.Protocols.OpenIdConnect;
//using System.Net;
//using System.Threading.Tasks;
//using System.Linq;

//namespace SmarTrakFuctions
//{
//    public class Authenticate
//    {
//        private readonly ILogger<Authenticate> _logger;
//        private readonly string _tenantId;
//        private readonly string _audience;
//        private readonly string _authority;

//        public Authenticate(ILogger<Authenticate> logger)
//        {
//            _logger = logger;
//            _tenantId = Environment.GetEnvironmentVariable("TenantId") ?? throw new InvalidOperationException("TenantId environment variable is not set.");
//            _audience = Environment.GetEnvironmentVariable("Audience") ?? throw new InvalidOperationException("Audience environment variable is not set.");
//            _authority = $"https://login.microsoftonline.com/{_tenantId}/v2.0";
//        }

//        [Function("Authenticate")]
//        public async Task<HttpResponseData> Run(
//            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = null)] Microsoft.Azure.Functions.Worker.Http.HttpRequestData req)
//        {
//            try
//            {
//                // Extract token from Authorization header
//                if (!req.Headers.TryGetValues("Authorization", out var authHeaders) || !authHeaders.Any())
//                {
//                    _logger.LogError("No Authorization header provided");
//                    return CreateErrorResponse(req, HttpStatusCode.Unauthorized, "No token provided");
//                }

//                var authHeader = authHeaders.First();
//                if (!authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
//                {
//                    _logger.LogError("Invalid Authorization header format");
//                    return CreateErrorResponse(req, HttpStatusCode.Unauthorized, "Invalid token format");
//                }

//                var token = authHeader.Substring("Bearer ".Length).Trim();

//                // Fetch OpenID Connect configuration
//                var configurationManager = new ConfigurationManager<OpenIdConnectConfiguration>(
//                    $"{_authority}/.well-known/openid-configuration",
//                    new OpenIdConnectConfigurationRetriever());
//                var openIdConfig = await configurationManager.GetConfigurationAsync();

//                // Configure token validation parameters
//                var validationParameters = new TokenValidationParameters
//                {
//                    ValidateIssuer = true,
//                    ValidIssuer = _authority,
//                    ValidateAudience = true,
//                    ValidAudience = _audience,
//                    ValidateLifetime = true,
//                    ValidateIssuerSigningKey = true,
//                    IssuerSigningKeys = openIdConfig.SigningKeys,
//                    ClockSkew = TimeSpan.FromMinutes(5)
//                };

//                // Validate token
//                var handler = new JwtSecurityTokenHandler();
//                var principal = handler.ValidateToken(token, validationParameters, out var validatedToken);

//                // Extract user info and roles
//                var userId = principal.FindFirst("sub")?.Value;
//                var email = principal.FindFirst("email")?.Value ?? principal.FindFirst("preferred_username")?.Value ?? "N/A";
//                var roles = principal.FindAll("roles").Select(r => r.Value).ToList();

//                // Return success response
//                var response = req.CreateResponse(HttpStatusCode.OK);
//                await response.WriteAsJsonAsync(new
//                {
//                    message = "Authentication successful",
//                    userId,
//                    email,
//                    roles
//                });
//                return response;
//            }
//            catch (SecurityTokenException ex)
//            {
//                _logger.LogError($"Token validation failed: {ex.Message}");
//                return CreateErrorResponse(req, HttpStatusCode.Unauthorized, $"Invalid token: {ex.Message}");
//            }
//            catch (Exception ex)
//            {
//                _logger.LogError($"Unexpected error: {ex.Message}");
//                return CreateErrorResponse(req, HttpStatusCode.InternalServerError, "Internal server error");
//            }
//        }

//        private HttpResponseData CreateErrorResponse(Microsoft.Azure.Functions.Worker.Http.HttpRequestData req, HttpStatusCode statusCode, string message)
//        {
//            var response = req.CreateResponse(statusCode);
//            response.WriteAsJsonAsync(new { error = message });
//            return response;
//        }
//    }
//}