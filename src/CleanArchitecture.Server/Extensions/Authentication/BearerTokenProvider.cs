﻿using CleanArchitecture.Core.Utilities;
using CleanArchitecture.Infrastructure.Data;
using CleanArchitecture.Infrastructure.Entities;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace CleanArchitecture.Server.Extensions.Authentication
{
    public class BearerTokenProvider
    {
        public const string XSRF_TOKEN_KEY = "XSRF-TOKEN";
        public string TokenType => "Bearer";

        private readonly IOptionsSnapshot<BearerTokenOptions> _bearerTokenOptions;
        private readonly ILogger<BearerTokenProvider> _logger;
        private readonly AppDbContext _appDbContext;
        private readonly UserManager<User> _userManager;
        private readonly IHttpContextAccessor _contextAccessor;
        private readonly IAntiforgery _antiforgery;
        private readonly AntiforgeryOptions _antiforgeryOptions;

        public BearerTokenProvider(
            IOptionsSnapshot<BearerTokenOptions> authenticationOptions,
            ILogger<BearerTokenProvider> logger,
            AppDbContext appDbContext,
            UserManager<User> userManager,
            IHttpContextAccessor contextAccessor,
            IAntiforgery antiforgery,
            IOptions<AntiforgeryOptions> antiforgeryOptions)
        {
            _bearerTokenOptions = authenticationOptions ?? throw new ArgumentNullException(nameof(authenticationOptions));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _appDbContext = appDbContext ?? throw new ArgumentNullException(nameof(appDbContext));
            _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
            _contextAccessor = contextAccessor ?? throw new ArgumentNullException(nameof(contextAccessor));
            _antiforgery = antiforgery ?? throw new ArgumentNullException(nameof(antiforgery));
            _antiforgeryOptions = antiforgeryOptions.Value ?? throw new ArgumentNullException(nameof(antiforgeryOptions));
        }

        public async Task<BearerTokenData> GenerateTokenAsync(User user)
        {
            if (user == null)
                throw new ArgumentNullException(nameof(user));

            await RemoveExpiredTokensByUserIdAsync(user.Id);

            if (!_bearerTokenOptions.Value.MultipleAuthentication)
                await RemoveTokensByUserIdAsync(user.Id);

            var now = DateTimeOffset.UtcNow;
            var (accessToken, claims) = await GenerateAccessTokenAsync(now, user);
            var refreshToken = GenerateRefreshToken(now);

            _appDbContext.Add(new BearerToken
            {
                UserId = user.Id,

                AccessTokenHash = Algorithm.GenerateHash(accessToken),
                RefreshTokenHash = Algorithm.GenerateHash(refreshToken),

                AccessTokenExpiresAt = now.Add(_bearerTokenOptions.Value.AccessTokenExpiresIn),
                RefreshTokenExpiresAt = now.Add(_bearerTokenOptions.Value.RefeshTokenExpiresIn)
            });
            await _appDbContext.SaveChangesAsync();

            RegenerateAntiForgeryCookies(claims);

            return new BearerTokenData
            {
                TokenType = TokenType,

                AccessToken = accessToken,
                AccessTokenExpiresIn = (long)_bearerTokenOptions.Value.AccessTokenExpiresIn.TotalMilliseconds,

                RefreshToken = refreshToken,
                RefreshTokenExpiresIn = (long)_bearerTokenOptions.Value.RefeshTokenExpiresIn.TotalMilliseconds,
            };
        }

        public async Task<BearerTokenData> RenewTokenAsync(BearerToken token)
        {
            if (token == null)
                throw new ArgumentNullException(nameof(token));

            await RemoveExpiredTokensByUserIdAsync(token.UserId);

            if (!_bearerTokenOptions.Value.MultipleAuthentication)
                await RemoveTokensByUserIdAsync(token.UserId);

            await RemoveTokensWithSameRefreshTokenAsync(token.RefreshTokenHash);

            var now = DateTimeOffset.UtcNow;
            var (accessToken, claims) = await GenerateAccessTokenAsync(now, token.User);
            var refreshToken = GenerateRefreshToken(now);
            _appDbContext.Add(new BearerToken
            {
                UserId = token.UserId,

                AccessTokenHash = Algorithm.GenerateHash(accessToken),
                RefreshTokenHash = Algorithm.GenerateHash(refreshToken),

                AccessTokenExpiresAt = now.Add(_bearerTokenOptions.Value.AccessTokenExpiresIn),
                RefreshTokenExpiresAt = now.Add(_bearerTokenOptions.Value.RefeshTokenExpiresIn)
            });
            await _appDbContext.SaveChangesAsync();

            RegenerateAntiForgeryCookies(claims);

            return new BearerTokenData
            {
                TokenType = TokenType,

                AccessToken = accessToken,
                AccessTokenExpiresIn = (long)_bearerTokenOptions.Value.AccessTokenExpiresIn.TotalMilliseconds,

                RefreshToken = refreshToken,
                RefreshTokenExpiresIn = (long)_bearerTokenOptions.Value.RefeshTokenExpiresIn.TotalMilliseconds,
            };
        }

        public async Task RevokeTokenAsync(BearerToken token)
        {
            if (token == null)
                throw new ArgumentNullException(nameof(token));

            await RemoveExpiredTokensByUserIdAsync(token.UserId);

            if (!_bearerTokenOptions.Value.MultipleAuthentication)
                await RemoveTokensByUserIdAsync(token.UserId);

            await RemoveTokensWithSameRefreshTokenAsync(token.RefreshTokenHash);
            await _appDbContext.SaveChangesAsync();

            DeleteAntiForgeryCookies();
        }

        public Task<BearerToken?> FindTokenAsync(string refreshToken)
        {
            if (refreshToken == null)
                throw new ArgumentNullException(nameof(refreshToken));

            var refreshTokenHash = Algorithm.GenerateHash(refreshToken);
            return _appDbContext.Set<BearerToken>().Include(bt => bt.User).FirstOrDefaultAsync(bt => bt.RefreshTokenHash == refreshTokenHash);
        }

        private async Task<(string AccessToken, IEnumerable<Claim> Claims)> GenerateAccessTokenAsync(DateTimeOffset now, User user)
        {
            if (user == null)
                throw new ArgumentNullException(nameof(user));

            var roleNames = await _userManager.GetRolesAsync(user);

            var claims = new List<Claim>
            {
                new(JwtRegisteredClaimNames.Jti, Algorithm.CreateCryptographicallySecureGuid().ToString(), ClaimValueTypes.String, _bearerTokenOptions.Value.Issuer),

                new(JwtRegisteredClaimNames.Iss, _bearerTokenOptions.Value.Issuer, ClaimValueTypes.String, _bearerTokenOptions.Value.Issuer),

                new(JwtRegisteredClaimNames.Iat, now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture), ClaimValueTypes.Integer64, _bearerTokenOptions.Value.Issuer),

                new(ClaimTypes.NameIdentifier, user.Id.ToString(CultureInfo.InvariantCulture), ClaimValueTypes.String, _bearerTokenOptions.Value.Issuer),

                new(ClaimTypes.Name, user.UserName, ClaimValueTypes.String, _bearerTokenOptions.Value.Issuer),

                new(ClaimTypes.SerialNumber, user.SecurityStamp, ClaimValueTypes.String, _bearerTokenOptions.Value.Issuer),
            };

            foreach (var roleName in roleNames)
                claims.Add(new Claim(ClaimTypes.Role, roleName, ClaimValueTypes.String, _bearerTokenOptions.Value.Issuer));

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_bearerTokenOptions.Value.Secret));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                _bearerTokenOptions.Value.Issuer,
                _bearerTokenOptions.Value.Audience,
                claims,
                now.DateTime,
                now.DateTime.Add(_bearerTokenOptions.Value.AccessTokenExpiresIn),
                creds);
            return (new JwtSecurityTokenHandler().WriteToken(token), claims);
        }

        private string GenerateRefreshToken(DateTimeOffset now)
        {
            var refreshTokenSerial = Algorithm.CreateCryptographicallySecureGuid().ToString()
                .Replace("-", "", StringComparison.Ordinal);

            var claims = new List<Claim>
            {
                new(JwtRegisteredClaimNames.Jti, Algorithm.CreateCryptographicallySecureGuid().ToString(), ClaimValueTypes.String, _bearerTokenOptions.Value.Issuer),

                new(JwtRegisteredClaimNames.Iss, _bearerTokenOptions.Value.Issuer, ClaimValueTypes.String, _bearerTokenOptions.Value.Issuer),

                new(JwtRegisteredClaimNames.Iat, now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture), ClaimValueTypes.Integer64, _bearerTokenOptions.Value.Issuer),

                new(ClaimTypes.SerialNumber, refreshTokenSerial, ClaimValueTypes.String, _bearerTokenOptions.Value.Issuer)
            };

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_bearerTokenOptions.Value.Secret));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                _bearerTokenOptions.Value.Issuer,
                _bearerTokenOptions.Value.Audience,
                claims,
                now.DateTime,
                now.DateTime.Add(_bearerTokenOptions.Value.RefeshTokenExpiresIn),
                creds);

            var refreshToken = new JwtSecurityTokenHandler().WriteToken(token);
            return refreshToken;
        }

        private void RegenerateAntiForgeryCookies(IEnumerable<Claim> claims)
        {
            if (_contextAccessor.HttpContext == null)
                throw new InvalidOperationException($"'{ExpressionHelper.GetName(() => _contextAccessor.HttpContext)}' cannot be null.");

            _contextAccessor.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(claims, JwtBearerDefaults.AuthenticationScheme));
            var tokens = _antiforgery.GetAndStoreTokens(_contextAccessor.HttpContext);
            if (tokens.RequestToken == null)
                throw new InvalidOperationException($"'{ExpressionHelper.GetName(() => tokens.RequestToken)}' cannot be null.");

            _contextAccessor.HttpContext.Response.Cookies.Append(XSRF_TOKEN_KEY, tokens.RequestToken,
                new CookieOptions
                {
                    HttpOnly = false // Now JavaScript is able to read the cookie
                });
        }

        private void DeleteAntiForgeryCookies()
        {
            var cookies = _contextAccessor.HttpContext?.Response.Cookies;
            if (cookies is null)
            {
                return;
            }

            var cookieName = _antiforgeryOptions.Cookie.Name;
            if (string.IsNullOrWhiteSpace(cookieName))
            {
                return;
            }

            cookies.Delete(cookieName);
            cookies.Delete(XSRF_TOKEN_KEY);
        }

        private async Task RemoveTokensByUserIdAsync(long userId)
        {
            var bearerTokens = await _appDbContext.Set<BearerToken>().Where(bt => bt.UserId == userId).ToArrayAsync();
            if (bearerTokens.Any()) _appDbContext.Remove(bearerTokens);
        }

        private async Task RemoveTokensWithSameRefreshTokenAsync(string refreshTokenHash)
        {
            if (refreshTokenHash == null)
                throw new ArgumentNullException(nameof(refreshTokenHash));

            var bearerTokens = await _appDbContext.Set<BearerToken>().Where(bt => bt.RefreshTokenHash == refreshTokenHash).ToArrayAsync();
            if (bearerTokens.Any()) _appDbContext.Remove(bearerTokens);
        }

        private async Task RemoveExpiredTokensByUserIdAsync(long userId)
        {
            var now = DateTimeOffset.UtcNow;
            var bearerTokens = await _appDbContext.Set<BearerToken>().Where(bt => bt.UserId == userId && bt.RefreshTokenExpiresAt < now).ToArrayAsync();
            if (bearerTokens.Any()) _appDbContext.Remove(bearerTokens);
        }

        public async Task<bool> ValidateTokenAsync(string accessToken, long userId)
        {
            if (accessToken == null)
                throw new ArgumentNullException(nameof(accessToken));

            var accessTokenHash = Algorithm.GenerateHash(accessToken);
            var bearerToken = await _appDbContext.Set<BearerToken>().FirstOrDefaultAsync(
                bt => bt.AccessTokenHash == accessTokenHash && bt.UserId == userId);
            return bearerToken?.AccessTokenExpiresAt >= DateTimeOffset.UtcNow;
        }

        public async Task ValidateTokenContextAsync(TokenValidatedContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            var claimsIdentity = context.Principal?.Identity as ClaimsIdentity;
            if (claimsIdentity?.Claims == null || !claimsIdentity.Claims.Any())
            {
                context.Fail("This is not our issued token. It has no claims.");
                return;
            }

            var serialNumberClaim = claimsIdentity.FindFirst(ClaimTypes.SerialNumber);
            if (serialNumberClaim == null)
            {
                context.Fail("This is not our issued token. It has no serial.");
                return;
            }

            var userIdString = claimsIdentity.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!long.TryParse(userIdString, NumberStyles.Number, CultureInfo.InvariantCulture, out var userId))
            {
                context.Fail("This is not our issued token. It has no user-id.");
                return;
            }

            var user = await _userManager.FindByIdAsync(userIdString);
            if (user == null || !string.Equals(user.SecurityStamp, serialNumberClaim.Value, StringComparison.Ordinal))
            {
                // user has changed his/her password/roles/stat/IsActive
                context.Fail("This token is expired. Please login again.");
                return;
            }

            if (!(context.SecurityToken is JwtSecurityToken accessToken) ||
                string.IsNullOrWhiteSpace(accessToken.RawData) ||
                !await ValidateTokenAsync(accessToken.RawData, userId))
            {
                context.Fail("This token is not in our database.");
                return;
            }
        }
    }
}