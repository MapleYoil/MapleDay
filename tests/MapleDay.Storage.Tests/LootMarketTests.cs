using System.Net;
using System.Net.Http;
using System.Text;
using MapleDay.Services;

namespace MapleDay.Storage.Tests;
public sealed class LootMarketTests
{
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(send(request));
    }
    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    [Fact]
    public async Task Signed_session_is_required_and_only_transformed_server_prices_are_cached()
    {
        var reads=0; var sessions=0; var signed=0;
        using var http=new HttpClient(new Handler(request => {
            if(request.Method==HttpMethod.Post)
            {
                sessions++; Assert.EndsWith("/market/session",request.RequestUri!.AbsoluteUri);
                return Json($"{{\"token\":\"test-session\",\"expiresAt\":{DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds()}}}");
            }
            reads++; Assert.EndsWith("/market/prices",request.RequestUri!.AbsoluteUri);
            Assert.Equal("test-session",request.Headers.Authorization!.Parameter);
            Assert.False(request.Headers.Contains("x-nxopen-api-key"));
            return Json("""{"fetchedAt":"2026-10-09T00:00:00Z","prices":[{"itemId":"1113329","variant":"컨티뉴어스 링","region":"normal","priceEok":26,"date":"2026-10-09"}]}""");
        })) { BaseAddress=new("https://server.morialuluka.com/api/mapleday/") };
        using var client=new LootMarketClient(http, data => { signed++; Assert.Contains("\n",Encoding.UTF8.GetString(data)); return [1,2,3]; });
        var snapshot=await client.GetAsync(); Assert.Same(snapshot,await client.GetAsync());
        Assert.Equal(1,reads); Assert.Equal(1,sessions); Assert.Equal(1,signed);
        var price=Assert.Single(snapshot.ForItem("1113329","오로라"));
        Assert.Equal(26,price.PriceEok); Assert.Equal(2600000000L,price.WholeEokPrice); Assert.Contains("26억 메소",price.Label);
        Assert.Empty(snapshot.ForItem("1113329","챌린저스"));
    }
    [Fact]
    public async Task Authentication_failure_never_reads_prices()
    {
        var requests=0;
        using var http=new HttpClient(new Handler(request => { requests++; Assert.Equal(HttpMethod.Post,request.Method); return new(HttpStatusCode.Unauthorized); })) { BaseAddress=new("https://server.morialuluka.com/api/mapleday/") };
        using var client=new LootMarketClient(http,_=>[1]);
        await Assert.ThrowsAsync<HttpRequestException>(()=>client.GetAsync()); Assert.Equal(1,requests);
    }
    [Theory]
    [InlineData(0L,"0억 메소")]
    [InlineData(1L,"1억 메소")]
    [InlineData(26L,"26억 메소")]
    public void Price_contract_is_whole_eok_before_display_and_apply(long priceEok,string label)
    {
        var price=new LootMarketPrice("a","아이템","normal",priceEok,new(2026,10,9));
        Assert.Equal(priceEok*100000000,price.WholeEokPrice); Assert.Contains(label,price.Label);
        Assert.Single(new LootMarketSnapshot(null,[price]).ForItem("a","오로라"));
    }
}
