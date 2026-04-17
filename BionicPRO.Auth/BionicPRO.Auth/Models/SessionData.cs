using System.Text.Json.Serialization;

namespace BionicPRO.Auth.Models;

public class SessionData
{
    public string SessionId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public DateTime AccessTokenExpiresAt { get; set; }
    public DateTime RefreshTokenExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime LastAccessedAt { get; set; }

    [JsonIgnore]
    public bool IsAccessTokenValid => DateTime.UtcNow < AccessTokenExpiresAt;

    [JsonIgnore]
    public bool IsRefreshTokenValid => DateTime.UtcNow < RefreshTokenExpiresAt;
}