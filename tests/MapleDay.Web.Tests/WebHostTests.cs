using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using MapleDay.Web;

namespace MapleDay.Web.Tests;

public sealed class WebHostTests
{
    private const string Password = "testing-mapleday-web";
    private static readonly WebPassword Secret = WebPassword.Create(Password);
    private static HttpClient Client(WebHostServer host, bool tls = false)
    {
        var handler = new HttpClientHandler { UseCookies = true };
        if (tls) handler.ServerCertificateCustomValidationCallback = (_, certificate, _, _) => certificate?.GetNameInfo(X509NameType.SimpleName, false) == "localhost";
        var client = new HttpClient(handler) { BaseAddress = new Uri($"{(tls ? "https" : "http")}://127.0.0.1:{host.Port}") };
        client.DefaultRequestHeaders.Add("Origin", client.BaseAddress.GetLeftPart(UriPartial.Authority));
        return client;
    }
    private static Task<HttpResponseMessage> Login(HttpClient client, string password = Password) => client.PostAsJsonAsync("/api/login", new { password });
    private static async Task<WebHostServer> Start(string? assets = null, X509Certificate2? certificate = null)
    {
        var host = new WebHostServer();
        await host.StartAsync(0, Secret, assets ?? Path.GetTempPath(), certificate, loopbackOnly: true);
        return host;
    }
    [Fact]
    public void Password_is_salted_and_rejects_malformed_hashes()
    {
        var other = WebPassword.Create(Password);
        Assert.NotEqual(Secret, other);
        Assert.True(other.Verify(Password)); Assert.False(other.Verify(Password + "!"));
        Assert.False(new WebPassword("invalid", "hash").Verify(Password));
        Assert.Throws<ArgumentException>(() => WebPassword.Create("short"));
    }
    [Fact]
    public async Task Login_protects_all_data_and_logout_revokes_the_session()
    {
        await using var host = await Start(); using var client = Client(host);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/snapshot")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/images/private.png")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/assets/Worlds/test.png")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Login(client, "incorrect-password")).StatusCode);
        var accepted = await Login(client); accepted.EnsureSuccessStatusCode();
        var cookie = Assert.Single(accepted.Headers.GetValues("Set-Cookie"));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        (await client.GetAsync("/api/snapshot")).EnsureSuccessStatusCode();
        (await client.PostAsync("/api/logout", null)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/snapshot")).StatusCode);
    }
    [Fact]
    public async Task Public_page_has_security_headers_and_only_local_dependencies()
    {
        await using var host = await Start(); using var client = Client(host);
        var response = await client.GetAsync("/"); response.EnsureSuccessStatusCode();
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Contains("frame-ancestors 'none'", Assert.Single(response.Headers.GetValues("Content-Security-Policy")));
        var html = await response.Content.ReadAsStringAsync(); Assert.Contains("width=device-width", html);
        Assert.DoesNotContain("https://", html);
        (await client.GetAsync("/app.js")).EnsureSuccessStatusCode();
        (await client.GetAsync("/style.css")).EnsureSuccessStatusCode();
    }
    [Fact]
    public async Task Cross_origin_and_non_json_login_are_rejected()
    {
        await using var host = await Start(); using var client = Client(host);
        client.DefaultRequestHeaders.Remove("Origin");
        Assert.Equal(HttpStatusCode.Forbidden, (await Login(client)).StatusCode);
        client.DefaultRequestHeaders.Add("Origin", "https://unrelated.example");
        Assert.Equal(HttpStatusCode.Forbidden, (await Login(client)).StatusCode);
        client.DefaultRequestHeaders.Remove("Origin"); client.DefaultRequestHeaders.Add("Origin", client.BaseAddress!.GetLeftPart(UriPartial.Authority));
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await client.PostAsync("/api/login", new StringContent("password"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/login", new StringContent("{", Encoding.UTF8, "application/json"))).StatusCode);
    }
    [Fact]
    public async Task Login_attempts_are_bounded()
    {
        await using var host = await Start(); using var client = Client(host);
        for (var i = 0; i < 15; i++) Assert.Equal(HttpStatusCode.Unauthorized, (await Login(client, "wrong-password")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await Login(client)).StatusCode);
    }
    [Fact]
    public async Task Snapshot_updates_and_images_are_replaced_without_exposing_files()
    {
        var directory = Path.Combine(Path.GetTempPath(), "MapleDay-web-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(directory, "Scheduler", "Bosses"));
        Directory.CreateDirectory(Path.Combine(directory, "BossLoot"));
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(directory, "Scheduler", "Bosses", "test.png"), [1, 2, 3]);
            await File.WriteAllBytesAsync(Path.Combine(directory, "BossLoot", "item.png"), [9, 8, 7]);
            await File.WriteAllTextAsync(Path.Combine(directory, "secrets.json"), "private");
            Directory.CreateDirectory(Path.Combine(directory, "Fonts"));
            await File.WriteAllBytesAsync(Path.Combine(directory, "Fonts", "PretendardVariable.ttf"), [7, 8, 9]);
            await using var host = await Start(directory); using var client = Client(host); (await Login(client)).EnsureSuccessStatusCode();
            using var anonymous = Client(host);
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/assets/BossLoot/item.png")).StatusCode);
            Assert.Equal(new byte[] { 7, 8, 9 }, await anonymous.GetByteArrayAsync("/pretendard.ttf"));
            host.Publish(new("1.0.43.0", DateTimeOffset.UtcNow, []), new Dictionary<string, byte[]> { ["sample"] = [4, 5, 6] });
            var json = await client.GetStringAsync("/api/snapshot"); Assert.Contains("1.0.43.0", json); Assert.DoesNotContain("ocid", json);
            Assert.Equal(new byte[] { 4, 5, 6 }, await client.GetByteArrayAsync("/images/sample.png"));
            Assert.Equal(new byte[] { 1, 2, 3 }, await client.GetByteArrayAsync("/assets/Scheduler/Bosses/test.png"));
            Assert.Equal(new byte[] { 9, 8, 7 }, await client.GetByteArrayAsync("/assets/BossLoot/item.png"));
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/assets/secrets.json")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/assets/Scheduler/Bosses/not-found.png")).StatusCode);
            host.Publish(WebSnapshot.Empty, new Dictionary<string, byte[]>());
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/images/sample.png")).StatusCode);
        }
        finally { Directory.Delete(directory, true); }
    }
    [Fact]
    public async Task ForecastAndLootAreReadOnlySnapshotDataWithoutStorageIdentifiers()
    {
        await using var host = await Start(); using var client = Client(host); (await Login(client)).EnsureSuccessStatusCode();
        var loot = new WebLoot("2026-10-09", "스우", "머신 마크", "BossLoot/100.png", 3, "3인 · 2:1:1 · 내 순번 1", 50);
        host.Publish(new("test", DateTimeOffset.UtcNow, [new("public-id", "테스트", "테스트", "테스트", 291, "291", "", null, 0,
            [], [], [], new(50, 50, 50, [], 8_350_000, 1, [loot]), new("", "", "", 0, []), null)]), new Dictionary<string, byte[]>());
        var snapshot = await client.GetFromJsonAsync<WebSnapshot>("/api/snapshot");
        var income = Assert.Single(snapshot!.Characters).Income;
        Assert.Equal(8_350_000, income.RemainingWeekly); Assert.Equal(loot, Assert.Single(income.Loot!));
        var json = await client.GetStringAsync("/api/snapshot");
        Assert.DoesNotContain("ocid", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("itemId", json, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PostAsJsonAsync("/api/snapshot", snapshot)).StatusCode);
    }
    [Fact]
    public async Task Stop_releases_port_and_old_session_does_not_survive_restart()
    {
        await using var host = await Start(); using var client = Client(host); (await Login(client)).EnsureSuccessStatusCode();
        var port = host.Port;
        await host.DisposeAsync(); Assert.False(host.Running);
        await host.StartAsync(port, Secret, Path.GetTempPath(), loopbackOnly: true);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/snapshot")).StatusCode);
        (await Login(client)).EnsureSuccessStatusCode();
    }
    [Fact]
    public async Task Port_conflict_does_not_leave_running_host()
    {
        await using var host = await Start(); await using var other = new WebHostServer();
        await Assert.ThrowsAsync<IOException>(() => other.StartAsync(host.Port, Secret, Path.GetTempPath(), loopbackOnly: true));
        Assert.False(other.Running);
    }
    [Fact]
    public async Task Https_works_with_secure_session_cookie()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder(); names.AddIpAddress(IPAddress.Loopback); request.CertificateExtensions.Add(names.Build());
        using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
        using var certificate = X509CertificateLoader.LoadPkcs12(generated.Export(X509ContentType.Pfx), null);
        await using var host = await Start(certificate: certificate); using var client = Client(host, true);
        var login = await Login(client); login.EnsureSuccessStatusCode();
        Assert.Contains("secure", Assert.Single(login.Headers.GetValues("Set-Cookie")), StringComparison.OrdinalIgnoreCase);
        (await client.GetAsync("/api/snapshot")).EnsureSuccessStatusCode();
    }
}
