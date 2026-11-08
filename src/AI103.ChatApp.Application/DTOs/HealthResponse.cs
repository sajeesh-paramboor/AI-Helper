namespace AI103.ChatApp.Application.DTOs;

/// <summary>
/// Response body of GET /api/chat/health. Reports API liveness only; it does not call Azure.
/// </summary>
public sealed record HealthResponse(string Status, DateTimeOffset TimestampUtc);
