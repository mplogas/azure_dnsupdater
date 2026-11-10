using System.Text;
using DNSUpdater.Function;
using DNSUpdater.Library.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace DNSUpdater.Tests;

public class UpdateFunctionTests
{
    private const string Hostname = "home.example.com";
    private const string Ip = "203.0.113.10";
    private const string ValidQuery = $"?hostname={Hostname}&myip={Ip}";

    private readonly IDnsService dnsService = Substitute.For<IDnsService>();
    private readonly CapturingLogger<Update> logger = new();
    private readonly Update function;

    public UpdateFunctionTests()
    {
        var factory = Substitute.For<IDnsServiceFactory>();
        factory.GetDnsService().Returns(dnsService);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authorization:0:user"] = "router",
                ["Authorization:0:secret"] = "s3cret",
                ["Authorization:1:user"] = "backup",
                ["Authorization:1:secret"] = "other-secret",
            })
            .Build();

        function = new Update(factory, config, logger);
    }

    [Theory]
    [InlineData(UpdateStatus.good, 200)]
    [InlineData(UpdateStatus.nochg, 200)]
    [InlineData(UpdateStatus.nohost, 409)]
    [InlineData(UpdateStatus.notfqdn, 409)]
    [InlineData(UpdateStatus.othererr, 409)]
    public async Task ValidRequest_MapsServiceResultToResponse(UpdateStatus status, int expectedStatusCode)
    {
        dnsService.Update(Hostname, Ip).Returns(status);

        var result = await function.RunAsync(Request(ValidQuery));

        AssertResponse(result, expectedStatusCode, status.ToString());
    }

    [Fact]
    public async Task SecondConfiguredUser_IsAccepted()
    {
        dnsService.Update(Hostname, Ip).Returns(UpdateStatus.good);

        var result = await function.RunAsync(Request(ValidQuery, BasicAuth("backup", "other-secret")));

        AssertResponse(result, 200, "good");
    }

    [Theory]
    [InlineData("router", "wrong")]
    [InlineData("unknown", "s3cret")]
    [InlineData("backup", "s3cret")]
    public async Task WrongCredentials_Returns401Badauth(string user, string secret)
    {
        var result = await function.RunAsync(Request(ValidQuery, BasicAuth(user, secret)));

        AssertResponse(result, 401, "badauth");
        await dnsService.DidNotReceiveWithAnyArgs().Update(default!, default!);
    }

    [Theory]
    [InlineData($"?myip={Ip}", true, true)]
    [InlineData($"?hostname={Hostname}", true, true)]
    [InlineData(ValidQuery, false, true)]
    [InlineData(ValidQuery, true, false)]
    public async Task MissingParameterOrHeader_Returns400Othererr(string query, bool withAuthorization, bool withUserAgent)
    {
        var request = Request(query,
            withAuthorization ? BasicAuth("router", "s3cret") : null,
            withUserAgent ? "inadyn/2.12" : null);

        var result = await function.RunAsync(request);

        AssertResponse(result, 400, "othererr");
        await dnsService.DidNotReceiveWithAnyArgs().Update(default!, default!);
    }

    [Theory]
    [InlineData("router", "s3cret")]
    [InlineData("router", "typo-of-the-password")]
    public async Task Request_NeverLogsTheCredentials(string user, string secret)
    {
        dnsService.Update(Hostname, Ip).Returns(UpdateStatus.good);
        var authorization = BasicAuth(user, secret);

        await function.RunAsync(Request(ValidQuery, authorization));

        Assert.NotEmpty(logger.Entries);
        var encoded = authorization["Basic ".Length..];
        Assert.DoesNotContain(logger.Entries, entry => entry.Contains(encoded) || entry.Contains(secret));
    }

    [Fact]
    public async Task Request_LogsQueryValuesWithoutLineBreaks()
    {
        await function.RunAsync(Request("?hostname=home%0Aforged-entry.example.com&myip=203.0.113.10%0D%0Aforged-entry"));

        Assert.NotEmpty(logger.Entries);
        Assert.DoesNotContain(logger.Entries, entry => entry.Contains("\nforged-entry"));
    }

    [Fact]
    public async Task ServiceThrows_Returns500()
    {
        dnsService.Update(Hostname, Ip).ThrowsAsync(new InvalidOperationException("boom"));

        var result = await function.RunAsync(Request(ValidQuery));

        AssertResponse(result, 500, null);
    }

    private static HttpRequest Request(string query) => Request(query, BasicAuth("router", "s3cret"));

    /// <summary>Builds a request; a null header value leaves that header out.</summary>
    private static HttpRequest Request(string query, string? authorization, string? userAgent = "inadyn/2.12")
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = "/nic/update";
        context.Request.QueryString = new QueryString(query);
        if (authorization != null)
            context.Request.Headers.Authorization = authorization;
        if (userAgent != null)
            context.Request.Headers.UserAgent = userAgent;
        return context.Request;
    }

    private static string BasicAuth(string user, string secret) =>
        "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{secret}"));

    private static void AssertResponse(IActionResult result, int statusCode, string? body)
    {
        Assert.Equal(statusCode, Assert.IsAssignableFrom<IStatusCodeActionResult>(result).StatusCode);
        Assert.Equal(body, (result as ObjectResult)?.Value);
    }
}
