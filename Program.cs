using System.Net.Http.Headers;
using System.Text;
using Newtonsoft.Json.Linq;

namespace TmobileRefresh;

internal static class Program
{
    private const string DefaultUserAgent = "Odido 8.0.0 (Android 14; 14)";
    private const string DefaultBasicAuthToken = "OWhhdnZhdDZobTBiOTYyaTo=";
    private const string DefaultClientId = "9havvat6hm0b962i";
    private const string DefaultScope = "usage+readfinancial+readsubscription+readpersonal+readloyalty+changesubscription+weblogin";
    private static readonly string[] DefaultApiBaseUrls =
    {
        "https://capi.odido.nl",
        "https://capi.t-mobile.nl"
    };
    private const string DefaultBundleCode = "A0DAY01";

    private static async Task<int> Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: TmobileRefresh <username> <password> [bundleCode] [estimatedBytesPerMs]");
            return 1;
        }

        var options = new AppOptions(
            Username: args[0],
            Password: args[1],
            BundleCode: args.ElementAtOrDefault(2) ?? DefaultBundleCode,
            EstimatedBytesPerMs: ParseEstimatedSpeed(args.ElementAtOrDefault(3)));

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
            Console.WriteLine("Stopping...");
        };

        try
        {
            using var api = new TMobileApiClient();
            var session = await api.CreateSessionAsync(options, cts.Token);
            await api.RunAutoTopUpLoopAsync(options, session, cts.Token);
            return 0;
        }
        catch (OperationCanceledException)
        {
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Fatal error: {ex.Message}");
            return 2;
        }
    }

    private static int ParseEstimatedSpeed(string? value)
    {
        if (int.TryParse(value, out var parsed) && parsed > 0)
        {
            return parsed;
        }

        // 25 bytes/ms ~= 25 KB/s. This is intentionally conservative to top up before depletion.
        return 25;
    }

    private sealed record AppOptions(string Username, string Password, string BundleCode, int EstimatedBytesPerMs);

    private sealed record Session(string AccessToken, string SubscriptionUrl);

    private sealed record ApiProfile(string BaseUrl, string ClientId, string BasicAuthToken, string Scope);

    private sealed class TMobileApiClient : IDisposable
    {
        private readonly HttpClient _client;

        public TMobileApiClient()
        {
            _client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
            _client.DefaultRequestHeaders.UserAgent.ParseAdd(Environment.GetEnvironmentVariable("ODIDO_USER_AGENT") ?? DefaultUserAgent);
            _client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        }

        public async Task<Session> CreateSessionAsync(AppOptions options, CancellationToken cancellationToken)
        {
            Exception? lastError = null;
            foreach (var profile in GetApiProfiles())
            {
                try
                {
                    var authorizationCode = await RequestAuthorizationCodeAsync(options, profile, cancellationToken);
                    var accessToken = await RequestAccessTokenAsync(authorizationCode, profile, cancellationToken);

                    _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                    var linkedSubscriptions = await GetJsonWithRedirectRetryAsync($"{profile.BaseUrl}/account/current?resourcelabel=LinkedSubscriptions", cancellationToken);
                    var linkedUrl = linkedSubscriptions.SelectToken("Resources[0].Url")?.ToString();

                    if (string.IsNullOrWhiteSpace(linkedUrl))
                    {
                        throw new InvalidOperationException("No linked subscription URL found for this account.");
                    }

                    var subscriptionDetails = await GetJsonAsync(linkedUrl, cancellationToken);
                    var subscriptionUrl = subscriptionDetails.SelectToken("subscriptions[0].SubscriptionURL")?.ToString();

                    if (string.IsNullOrWhiteSpace(subscriptionUrl))
                    {
                        throw new InvalidOperationException("No subscription URL found for this account.");
                    }

                    Console.WriteLine($"Authenticated using API base URL: {profile.BaseUrl}");
                    return new Session(accessToken, subscriptionUrl);
                }
                catch (Exception ex)
                {
                    lastError = ex;
                }
            }

            throw new InvalidOperationException("Failed to authenticate against all configured Odido API endpoints.", lastError);
        }

        public async Task RunAutoTopUpLoopAsync(AppOptions options, Session session, CancellationToken cancellationToken)
        {
            var estimatedBytesPerMs = options.EstimatedBytesPerMs;

            while (!cancellationToken.IsCancellationRequested)
            {
                var roamingBundlesUrl = $"{session.SubscriptionUrl}/roamingbundles";
                var roamingBundles = await GetJsonAsync(roamingBundlesUrl, cancellationToken);

                var hasBundle = ContainsBundle(roamingBundles, options.BundleCode);
                var remainingBytes = GetRemainingBytes(roamingBundles);
                Console.WriteLine($"Remaining bytes: {remainingBytes}");

                if (!hasBundle || remainingBytes < 400_000)
                {
                    await TopUpAsync(session, options.BundleCode, cancellationToken);
                    remainingBytes = 2_000_000;
                }

                if (remainingBytes == 0)
                {
                    estimatedBytesPerMs = Math.Max(1, estimatedBytesPerMs + 1);
                }

                var delayMs = Math.Max(30_000, remainingBytes / Math.Max(1, estimatedBytesPerMs));
                Console.WriteLine($"Sleeping for {delayMs / 1000} seconds...");
                await Task.Delay(delayMs, cancellationToken);
            }
        }

        private static IReadOnlyList<ApiProfile> GetApiProfiles()
        {
            var explicitBase = Environment.GetEnvironmentVariable("ODIDO_API_BASE_URL");
            var baseUrls = string.IsNullOrWhiteSpace(explicitBase)
                ? DefaultApiBaseUrls
                : new[] { explicitBase };

            var clientId = Environment.GetEnvironmentVariable("ODIDO_CLIENT_ID") ?? DefaultClientId;
            var basicAuthToken = Environment.GetEnvironmentVariable("ODIDO_BASIC_AUTH_TOKEN") ?? DefaultBasicAuthToken;
            var scope = Environment.GetEnvironmentVariable("ODIDO_SCOPE") ?? DefaultScope;

            return baseUrls
                .Select(baseUrl => new ApiProfile(baseUrl.TrimEnd('/'), clientId, basicAuthToken, scope))
                .ToArray();
        }

        private async Task<string> RequestAuthorizationCodeAsync(AppOptions options, ApiProfile profile, CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{profile.BaseUrl}/login?response_type=code")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["Username"] = options.Username,
                    ["Password"] = options.Password,
                    ["ClientId"] = profile.ClientId,
                    ["Scope"] = profile.Scope
                })
            };

            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", profile.BasicAuthToken);
            using var response = await _client.SendAsync(request, cancellationToken);

            if (!response.Headers.TryGetValues("AuthorizationCode", out var values))
            {
                throw new InvalidOperationException("Could not get authorization code. Check username/password.");
            }

            var code = values.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(code))
            {
                throw new InvalidOperationException("Authorization code header was empty.");
            }

            return code;
        }

        private async Task<string> RequestAccessTokenAsync(string authorizationCode, ApiProfile profile, CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{profile.BaseUrl}/createtoken")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["AuthorizationCode"] = authorizationCode
                })
            };

            using var response = await _client.SendAsync(request, cancellationToken);

            if (!response.Headers.TryGetValues("AccessToken", out var values))
            {
                throw new InvalidOperationException("Could not retrieve access token.");
            }

            var token = values.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(token))
            {
                throw new InvalidOperationException("Access token header was empty.");
            }

            return token;
        }

        private async Task<JObject> GetJsonWithRedirectRetryAsync(string url, CancellationToken cancellationToken)
        {
            for (var attempt = 0; attempt < 3; attempt++)
            {
                using var response = await _client.GetAsync(url, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    continue;
                }

                return await ReadJsonAsync(response, cancellationToken);
            }

            throw new HttpRequestException($"Failed to retrieve {url} after retries.");
        }

        private async Task<JObject> GetJsonAsync(string url, CancellationToken cancellationToken)
        {
            using var response = await _client.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();
            return await ReadJsonAsync(response, cancellationToken);
        }

        private static async Task<JObject> ReadJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
        {
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            var json = JObject.Parse(text);
            return json;
        }

        private static bool ContainsBundle(JObject roamingBundles, string bundleCode)
        {
            var bundles = roamingBundles.SelectToken("Bundles") as JArray;
            if (bundles is null)
            {
                return false;
            }

            return bundles
                .Select(bundle => bundle.SelectToken("BuyingCode")?.ToString())
                .Any(code => string.Equals(code, bundleCode, StringComparison.OrdinalIgnoreCase));
        }

        private static int GetRemainingBytes(JObject roamingBundles)
        {
            var bundle = roamingBundles.SelectToken("Bundles[1]");
            return bundle?.SelectToken("Remaining.Value")?.Value<int>() ?? 0;
        }

        private async Task TopUpAsync(Session session, string bundleCode, CancellationToken cancellationToken)
        {
            Console.WriteLine("Requesting new package...");
            var payload = new JObject(
                new JProperty("Bundles",
                    new JArray(
                        new JObject(new JProperty("BuyingCode", bundleCode)))))
                .ToString();

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{session.SubscriptionUrl}/roamingbundles")
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };

            using var response = await _client.SendAsync(request, cancellationToken);
            Console.WriteLine($"Top-up request returned: {(int)response.StatusCode} {response.StatusCode}");
        }

        public void Dispose() => _client.Dispose();
    }
}
