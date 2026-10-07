using System.Net;

namespace MapleDay.Core;

public sealed class NexonApiException(HttpStatusCode statusCode, string? code, string message)
    : Exception(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
    public string? Code { get; } = code;
    public bool IsAuthenticationError => Code is "OPENAPI00002" or "OPENAPI00005"
        || StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;
}
