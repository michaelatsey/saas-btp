namespace SaasBtp.Api;

/// <summary>The body of <c>GET /api/v1/ping</c>.</summary>
/// <param name="Status">Always <c>ok</c>: the endpoint only proves that the versioned API answers.</param>
internal sealed record PingResponse(string Status);
