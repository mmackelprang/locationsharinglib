using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using LocationSharingLib.Exceptions;
using Xunit;
using Microsoft.Extensions.Logging;
using System.Threading;

namespace LocationSharingLib.Tests;

public class ServiceTests
{
    private static HttpClient BuildMockClient(string payload, bool validAuth = true)
    {
        // Build minimal JSON array structure with required indices. Index 6 is auth field heuristic.
        var arr = new object?[]{ new object?[]{}, null,null,null,null,null, validAuth ? "VALID" : "GgA=" }; // index 0 shared entries empty
        var json = JsonSerializer.Serialize(arr);
        var text = ")]}'" + json; // mimic prefix removed in parsing
        return new HttpClient(new FakeHttpMessageHandler(text));
    }

    private static HttpClient BuildRichPersonClient()
    {
        // Build one person entry with rich data at shared entries (root[0])
        var person = new object?[]
        {
            null, //0
            new object?[]{null, new object?[]{null, 10.123456, 45.654321}, 1700000000000, 15, "123 Sample Street", null, "US"}, //1
            null, //2
            null, //3
            null, //4
            null, //5
            new object?[]{"id123","http://example/avatar.jpg","John Doe","Johnny"}, //6 (identity & names)
            null, //7
            null, //8
            null, //9
            null, //10
            null, //11
            null, //12
            new object?[]{true, 87} //13 battery/charging
        };
        var root = new object?[]{ new object?[]{ person }, null,null,null,null,null, "VALID" };
        var json = JsonSerializer.Serialize(root);
        var text = ")]}'" + json;
        return new HttpClient(new FakeHttpMessageHandler(text));
    }

    private sealed class FlakyHandler : HttpMessageHandler
    {
        private int _failuresRemaining;
        private readonly string _successPayload;
        public int Attempts { get; private set; }
        public FlakyHandler(int failures, string successPayload)
        { _failuresRemaining = failures; _successPayload = successPayload; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Attempts++;
            if (_failuresRemaining-- > 0)
            {
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable){ Content = new StringContent("temporary") });
            }
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK){ Content = new StringContent(_successPayload) });
        }
    }

    [Fact]
    public async Task PersonParsingIncludesBatteryChargingAddress()
    {
        var tmp = System.IO.Path.GetTempFileName();
        System.IO.File.WriteAllText(tmp, ".google.com TRUE / TRUE 0 __Secure-1PSID value\n");
        try
        {
            var svc = new Service(tmp, "user@example.com", BuildRichPersonClient(), logger: LoggingConfig.CreateLoggerFactory().CreateLogger<Service>());
            var people = await svc.GetSharedPeopleAsync();
            var p = Assert.Single(people);
            Assert.Equal("John Doe", p.FullName);
            Assert.Equal(45.654321, p.Latitude);
            Assert.Equal(10.123456, p.Longitude);
            Assert.Equal("123 Sample Street", p.Address);
            Assert.Equal("US", p.CountryCode);
            Assert.True(p.Charging);
            Assert.Equal(87, p.BatteryLevel);
        }
        finally { System.IO.File.Delete(tmp); }
    }

    [Fact]
    public async Task RetrySucceedsAfterTransientFailures()
    {
        var tmp = System.IO.Path.GetTempFileName();
        System.IO.File.WriteAllText(tmp, ".google.com TRUE / TRUE 0 __Secure-1PSID value\n");
        try
        {
            var root = new object?[]{ new object?[]{}, null,null,null,null,null, "VALID" };
            var json = JsonSerializer.Serialize(root);
            var text = ")]}'" + json;
            var flaky = new FlakyHandler(2, text); // fail twice then succeed
            var client = new HttpClient(flaky);
            var svc = new Service(tmp, "user@example.com", client, maxRetries:5, logger: LoggingConfig.CreateLoggerFactory().CreateLogger<Service>());
            var people = await svc.GetSharedPeopleAsync();
            Assert.Empty(people);
            Assert.True(flaky.Attempts >= 3);
        }
        finally { System.IO.File.Delete(tmp); }
    }

    private sealed class MultiPhaseHandler : HttpMessageHandler
    {
        private int _callCount;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _callCount++;
            if (_callCount == 1)
            {
                // Fast auth validation response using valid JSON
                var root = new object?[]{ new object?[]{}, null,null,null,null,null, "VALID" };
                var json = ")]}'" + JsonSerializer.Serialize(root);
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK){ Content = new StringContent(json) };
            }
            await Task.Delay(500, cancellationToken);
            var root2 = new object?[]{ new object?[]{}, null,null,null,null,null, "VALID" };
            var json2 = ")]}'" + JsonSerializer.Serialize(root2);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK){ Content = new StringContent(json2) };
        }
    }

    [Fact]
    public async Task CancellationTokenCancelsInFlightRequest()
    {
        var tmp = System.IO.Path.GetTempFileName();
        System.IO.File.WriteAllText(tmp, ".google.com TRUE / TRUE 0 __Secure-1PSID value\n");
        try
        {
            var client = new HttpClient(new MultiPhaseHandler());
            var svc = new Service(tmp, "user@example.com", client, logger: LoggingConfig.CreateLoggerFactory().CreateLogger<Service>(), disableCache:true);
            using var cts = new CancellationTokenSource(50);
            await Assert.ThrowsAsync<TaskCanceledException>(async () => await svc.GetSharedPeopleAsync(cts.Token));
        }
        finally { System.IO.File.Delete(tmp); }
    }

    [Fact]
    public void MissingCookieFileThrows()
    {
        Assert.Throws<InvalidCookieFileException>(() => new Service("c://does/not/exist.txt", "user@example.com"));
    }

    [Fact]
    public void InvalidCookiesThrow()
    {
        var tmp = System.IO.Path.GetTempFileName();
        System.IO.File.WriteAllText(tmp, "# empty file without valid cookies\n");
        try
        {
            Assert.Throws<InvalidCookiesException>(() => new Service(tmp, "user@example.com"));
        }
        finally { System.IO.File.Delete(tmp); }
    }

    [Fact]
    public void ValidCookiesButBadAuthThrows()
    {
        var tmp = System.IO.Path.GetTempFileName();
        System.IO.File.WriteAllText(tmp, ".google.com TRUE / TRUE 0 __Secure-1PSID value\n");
        try
        {
            Assert.Throws<InvalidCookiesException>(() => new Service(tmp, "user@example.com", BuildMockClient(string.Empty, validAuth:false)));
        }
        finally { System.IO.File.Delete(tmp); }
    }

    [Fact]
    public async Task ValidCookiesAndAuthSucceeds()
    {
        var tmp = System.IO.Path.GetTempFileName();
        System.IO.File.WriteAllText(tmp, ".google.com TRUE / TRUE 0 __Secure-1PSID value\n");
        try
        {
            var svc = new Service(tmp, "user@example.com", BuildMockClient(string.Empty, validAuth:true));
            var people = await svc.GetSharedPeopleAsync();
            Assert.Empty(people); // empty array as per mock
        }
        finally { System.IO.File.Delete(tmp); }
    }
}
