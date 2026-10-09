using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;

namespace MapleDay.Web;

public sealed class WebHostServer : IAsyncDisposable
{
    private WebApplication? _app;
    private byte[] _snapshot = JsonSerializer.SerializeToUtf8Bytes(WebSnapshot.Empty, JsonOptions);
    private IReadOnlyDictionary<string, byte[]> _images = new Dictionary<string, byte[]>();
    private readonly ConcurrentDictionary<string, DateTimeOffset> _sessions = new();
    private readonly ConcurrentDictionary<string, (DateTimeOffset Until, int Attempts)> _attempts = new();
    private readonly SemaphoreSlim _login = new(1);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public int Port { get; private set; }
    public bool Running => _app is not null;
    public bool Https { get; private set; }
    public string LocalUrl => $"{(Https ? "https" : "http")}://localhost:{Port}";

    public void Publish(WebSnapshot snapshot, IReadOnlyDictionary<string, byte[]> images)
    {
        // Serialize on the UI thread, then exchange an immutable, read-only representation.
        Volatile.Write(ref _images, images);
        Volatile.Write(ref _snapshot, JsonSerializer.SerializeToUtf8Bytes(snapshot, JsonOptions));
    }

    public async Task StartAsync(int port, WebPassword password, string assetsDirectory, X509Certificate2? certificate = null,
        bool loopbackOnly = false, CancellationToken cancellationToken = default)
    {
        if (Running) throw new InvalidOperationException("웹 호스트가 이미 실행 중입니다.");
        if (!password.Valid) throw new ArgumentException("웹 접속 비밀번호를 다시 설정하세요.", nameof(password));
        if (port is < 0 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = [], ApplicationName = typeof(WebHostServer).Assembly.GetName().Name });
        builder.Logging.ClearProviders(); // no passwords, IPs, cookies or browsing history in logs
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.AddServerHeader = false;
            options.Limits.MaxRequestBodySize = 4096;
            options.Limits.MaxConcurrentConnections = 64;
            options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(10);
            options.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(30);
            void Endpoint(Microsoft.AspNetCore.Server.Kestrel.Core.ListenOptions endpoint)
            { if (certificate is not null) endpoint.UseHttps(certificate); }
            if (loopbackOnly) options.Listen(IPAddress.Loopback, port, Endpoint);
            else options.ListenAnyIP(port, Endpoint);
        });
        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; font-src 'self'; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'";
            context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
            if (context.Request.Method == "POST" && !SameOrigin(context)) { context.Response.StatusCode = 403; return; }
            await next(context);
        });
        app.MapGet("/", () => Embedded("index.html", "text/html; charset=utf-8"));
        app.MapGet("/app.js", () => Embedded("app.js", "text/javascript; charset=utf-8"));
        app.MapGet("/style.css", () => Embedded("style.css", "text/css; charset=utf-8"));
        app.MapGet("/pretendard.ttf", () =>
        {
            var font = Path.Combine(assetsDirectory, "Fonts", "PretendardVariable.ttf");
            return File.Exists(font) ? Results.File(font, "font/ttf") : Results.NotFound();
        });
        app.MapGet("/font-license.txt", () =>
        {
            var license = Path.Combine(assetsDirectory, "Fonts", "LICENSE.txt");
            return File.Exists(license) ? Results.File(license, "text/plain; charset=utf-8") : Results.NotFound();
        });
        app.MapPost("/api/login", async (HttpContext context) =>
        {
            var ip = context.Connection.RemoteIpAddress?.ToString() ?? "local";
            var now = DateTimeOffset.UtcNow;
            foreach (var entry in _attempts.Where(item => item.Value.Until <= now)) _attempts.TryRemove(entry.Key, out _);
            if (_attempts.Count >= 1024 || _attempts.TryGetValue(ip, out var limit) && limit.Attempts >= 15)
                return Results.StatusCode(429);
            if (!context.Request.HasJsonContentType()) return Results.StatusCode(415);
            Login? body;
            try { body = await context.Request.ReadFromJsonAsync<Login>(cancellationToken: context.RequestAborted); }
            catch (Exception error) when (error is JsonException or BadHttpRequestException) { return Results.BadRequest(); }
            _attempts.AddOrUpdate(ip, (now.AddMinutes(15), 1), (_, value) => (value.Until, value.Attempts + 1));
            // Password derivation is bounded so concurrent attempts cannot monopolize the desktop's UI/CPU.
            if (!await _login.WaitAsync(0, context.RequestAborted)) return Results.StatusCode(429);
            bool accepted;
            try { accepted = body is { Password: { } value } && password.Verify(value); }
            finally { _login.Release(); }
            if (!accepted) return Results.Unauthorized();
            _attempts.TryRemove(ip, out _);
            foreach (var entry in _sessions.Where(item => item.Value <= now)) _sessions.TryRemove(entry.Key, out _);
            if (_sessions.Count >= 64) return Results.StatusCode(429);
            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            _sessions[TokenHash(token)] = now.AddHours(12);
            context.Response.Cookies.Append("MapleDaySession", token, Cookie(context.Request.IsHttps));
            return Results.Ok();
        });
        app.MapPost("/api/logout", (HttpContext context) =>
        {
            if (context.Request.Cookies.TryGetValue("MapleDaySession", out var token)) _sessions.TryRemove(TokenHash(token), out _);
            context.Response.Cookies.Delete("MapleDaySession", Cookie(context.Request.IsHttps));
            return Results.Ok();
        });
        app.MapGet("/api/snapshot", (HttpContext context) => Authorized(context)
            ? Results.Bytes(Volatile.Read(ref _snapshot), "application/json") : Results.Unauthorized());
        app.MapGet("/images/{id}.png", (HttpContext context, string id) =>
        {
            if (!Authorized(context)) return Results.Unauthorized();
            return Volatile.Read(ref _images).TryGetValue(id, out var bytes) ? Results.Bytes(bytes, "image/png") : Results.NotFound();
        });
        app.MapGet("/assets/{**file}", (HttpContext context, string file) =>
        {
            if (!Authorized(context)) return Results.Unauthorized();
            // Whitelist shipped display assets. Never serve arbitrary files or font licenses as routes.
            if (file.Contains("..", StringComparison.Ordinal) || file.Contains('\\') || file.StartsWith('/')) return Results.NotFound();
            var allowed = file.StartsWith("Scheduler/Bosses/", StringComparison.Ordinal) || file.StartsWith("Scheduler/Difficulty/", StringComparison.Ordinal)
                || file.StartsWith("Worlds/", StringComparison.Ordinal) || file.StartsWith("BossLoot/", StringComparison.Ordinal);
            if (!allowed || !file.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) return Results.NotFound();
            var path = Path.Combine(assetsDirectory, file.Replace('/', Path.DirectorySeparatorChar));
            return File.Exists(path) ? Results.File(path, "image/png") : Results.NotFound();
        });
        try
        {
            await app.StartAsync(cancellationToken);
            var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses;
            Port = new Uri(addresses.First()).Port;
            Https = certificate is not null;
            _app = app;
        }
        catch { await app.DisposeAsync(); throw; }
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not { } app) return;
        _app = null;
        try { await app.StopAsync(TimeSpan.FromSeconds(3)); }
        finally { await app.DisposeAsync(); _sessions.Clear(); _attempts.Clear(); }
    }
    private bool Authorized(HttpContext context) => context.Request.Cookies.TryGetValue("MapleDaySession", out var token)
        && token.Length == 64 && _sessions.TryGetValue(TokenHash(token), out var until) && until > DateTimeOffset.UtcNow;
    private static bool SameOrigin(HttpContext context)
    {
        if (!Uri.TryCreate(context.Request.Headers.Origin.ToString(), UriKind.Absolute, out var origin)) return false;
        return origin.Authority.Equals(context.Request.Host.Value, StringComparison.OrdinalIgnoreCase)
            && origin.Scheme == context.Request.Scheme;
    }
    private static CookieOptions Cookie(bool secure) => new() { HttpOnly = true, Secure = secure, SameSite = SameSiteMode.Strict,
        Path = "/", MaxAge = TimeSpan.FromHours(12), IsEssential = true };
    private static string TokenHash(string value) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)));
    private static IResult Embedded(string file, string contentType)
    {
        using var stream = typeof(WebHostServer).Assembly.GetManifestResourceStream("MapleDay.Web.wwwroot." + file)!;
        using var memory = new MemoryStream(); stream.CopyTo(memory);
        return Results.Bytes(memory.ToArray(), contentType);
    }
    private sealed record Login(string Password);
}
