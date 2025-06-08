using CryptoReportingApp.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Distributed;
using Newtonsoft.Json;
using Polly.CircuitBreaker;
using Polly;
using System.Net.Http.Headers;
using System.Linq;
using System;
using System.Globalization;

// Controller for crypto analytics
public class AnalyticsController : Controller
{
    private readonly HttpClient _httpClient;
    private readonly IDistributedCache _cache;
    private readonly AsyncCircuitBreakerPolicy _circuitBreakerPolicy;
    private readonly IAsyncPolicy<HttpResponseMessage> _retryPolicy;

    // Constructor sets up HTTP client, cache, retry and circuit breaker
    public AnalyticsController(IDistributedCache cache)
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; MyApp/1.0)");
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _cache = cache;

        _circuitBreakerPolicy = Policy
            .Handle<HttpRequestException>()
            .Or<TimeoutException>()
            .CircuitBreakerAsync(
                exceptionsAllowedBeforeBreaking: 3,
                durationOfBreak: TimeSpan.FromMinutes(5),
                onBreak: (ex, breakDelay) =>
                {
                    Console.WriteLine($"Circuit broken! Exception: {ex.Message}");
                },
                onReset: () =>
                {
                    Console.WriteLine("Circuit reset!");
                });

        _retryPolicy = Policy<HttpResponseMessage>
            .Handle<HttpRequestException>()
            .Or<TimeoutException>()
            .OrResult(r => (int)r.StatusCode >= 400)
            .WaitAndRetryAsync(
                retryCount: 2,
                sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Pow(3, retryAttempt)),
                onRetry: (outcome, delay, retryCount, context) =>
                {
                    Console.WriteLine($"Retry {retryCount} after {delay.TotalSeconds} seconds due to: {outcome.Exception?.Message ?? outcome.Result?.StatusCode.ToString()}");
                });
    }

    // Main page – shows crypto prices (uses cache or fetches from API)
    public async Task<IActionResult> Index(string currency = "bitcoin", string period = "1h")
    {
        ViewBag.Currency = currency;
        ViewBag.Period = period;

        var cacheKey = "CryptoPrices";
        var cachedData = await _cache.GetStringAsync(cacheKey);

        if (string.IsNullOrEmpty(cachedData))
        {
            try
            {
                var apiUrl = "https://api.coingecko.com/api/v3/coins/markets?ids=bitcoin,ethereum,tether&vs_currency=usd&price_change_percentage=1h,24h,7d";

                var response = await _retryPolicy.ExecuteAsync(() =>
                {
                    return _httpClient.GetAsync(apiUrl);
                });

                if (_circuitBreakerPolicy.CircuitState == CircuitState.Open)
                {
                    throw new BrokenCircuitException("Circuit is open");
                }

                if (!response.IsSuccessStatusCode)
                {
                    var errorMessage = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"Failed to fetch data. StatusCode: {response.StatusCode}, Response: {errorMessage}");
                    throw new HttpRequestException($"API request failed with status: {response.StatusCode}, Response: {errorMessage}");
                }

                var responseContent = await response.Content.ReadAsStringAsync();
                var priceData = JsonConvert.DeserializeObject<List<dynamic>>(responseContent);

                var bitcoinData = priceData.FirstOrDefault(c => c.id == "bitcoin");
                var ethereumData = priceData.FirstOrDefault(c => c.id == "ethereum");
                var tetherData = priceData.FirstOrDefault(c => c.id == "tether");

                var cacheData = new
                {
                    bitcoin = new
                    {
                        price = bitcoinData?.current_price,
                        change1h = bitcoinData?.price_change_percentage_1h_in_currency,
                        change24h = bitcoinData?.price_change_percentage_24h_in_currency,
                        change7d = bitcoinData?.price_change_percentage_7d_in_currency
                    },
                    ethereum = new
                    {
                        price = ethereumData?.current_price,
                        change1h = ethereumData?.price_change_percentage_1h_in_currency,
                        change24h = ethereumData?.price_change_percentage_24h_in_currency,
                        change7d = ethereumData?.price_change_percentage_7d_in_currency
                    },
                    tether = new
                    {
                        price = tetherData?.current_price,
                        change1h = tetherData?.price_change_percentage_1h_in_currency,
                        change24h = tetherData?.price_change_percentage_24h_in_currency,
                        change7d = tetherData?.price_change_percentage_7d_in_currency
                    }
                };

                await _cache.SetStringAsync(cacheKey, JsonConvert.SerializeObject(cacheData), new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10)
                });

                ViewBag.BitcoinPrice = bitcoinData?.current_price;
                ViewBag.BitcoinChange1h = bitcoinData?.price_change_percentage_1h_in_currency;
                ViewBag.BitcoinChange24h = bitcoinData?.price_change_percentage_24h_in_currency;
                ViewBag.BitcoinChange7d = bitcoinData?.price_change_percentage_7d_in_currency;

                ViewBag.EthereumPrice = ethereumData?.current_price;
                ViewBag.EthereumChange1h = ethereumData?.price_change_percentage_1h_in_currency;
                ViewBag.EthereumChange24h = ethereumData?.price_change_percentage_24h_in_currency;
                ViewBag.EthereumChange7d = ethereumData?.price_change_percentage_7d_in_currency;

                ViewBag.TetherPrice = tetherData?.current_price;
                ViewBag.TetherChange1h = tetherData?.price_change_percentage_1h_in_currency;
                ViewBag.TetherChange24h = tetherData?.price_change_percentage_24h_in_currency;
                ViewBag.TetherChange7d = tetherData?.price_change_percentage_7d_in_currency;
            }
            catch (BrokenCircuitException)
            {
                return await GetCachedOrFallbackData("Crypto data service is temporarily unavailable. Showing cached data.");
            }
            catch (Exception ex)
            {
                return await GetCachedOrFallbackData($"An error occurred: {ex.Message}");
            }
        }
        else
        {
            var priceData = JsonConvert.DeserializeObject<dynamic>(cachedData);

            ViewBag.BitcoinPrice = priceData.bitcoin.price;
            ViewBag.BitcoinChange1h = priceData.bitcoin.change1h;
            ViewBag.BitcoinChange24h = priceData.bitcoin.change24h;
            ViewBag.BitcoinChange7d = priceData.bitcoin.change7d;

            ViewBag.EthereumPrice = priceData.ethereum.price;
            ViewBag.EthereumChange1h = priceData.ethereum.change1h;
            ViewBag.EthereumChange24h = priceData.ethereum.change24h;
            ViewBag.EthereumChange7d = priceData.ethereum.change7d;

            ViewBag.TetherPrice = priceData.tether.price;
            ViewBag.TetherChange1h = priceData.tether.change1h;
            ViewBag.TetherChange24h = priceData.tether.change24h;
            ViewBag.TetherChange7d = priceData.tether.change7d;
        }

        return View();
    }

    // If API is down -щ show cached or fallback data
    private async Task<IActionResult> GetCachedOrFallbackData(string errorMessage = null)
    {
        if (!string.IsNullOrEmpty(errorMessage))
        {
            ViewBag.ErrorMessage = errorMessage;
        }

        var cacheKey = "CryptoPrices";
        var cachedData = await _cache.GetStringAsync(cacheKey);

        if (!string.IsNullOrEmpty(cachedData))
        {
            var priceData = JsonConvert.DeserializeObject<dynamic>(cachedData);

            ViewBag.BitcoinPrice = priceData.bitcoin.price;
            ViewBag.BitcoinChange1h = priceData.bitcoin.change1h;
            ViewBag.BitcoinChange24h = priceData.bitcoin.change24h;
            ViewBag.BitcoinChange7d = priceData.bitcoin.change7d;

            ViewBag.EthereumPrice = priceData.ethereum.price;
            ViewBag.EthereumChange1h = priceData.ethereum.change1h;
            ViewBag.EthereumChange24h = priceData.ethereum.change24h;
            ViewBag.EthereumChange7d = priceData.ethereum.change7d;

            ViewBag.TetherPrice = priceData.tether.price;
            ViewBag.TetherChange1h = priceData.tether.change1h;
            ViewBag.TetherChange24h = priceData.tether.change24h;
            ViewBag.TetherChange7d = priceData.tether.change7d;
        }
        else
        {
            ViewBag.BitcoinPrice = "N/A";
            ViewBag.BitcoinChange1h = "N/A";
            ViewBag.BitcoinChange24h = "N/A";
            ViewBag.BitcoinChange7d = "N/A";

            ViewBag.EthereumPrice = "N/A";
            ViewBag.EthereumChange1h = "N/A";
            ViewBag.EthereumChange24h = "N/A";
            ViewBag.EthereumChange7d = "N/A";

            ViewBag.TetherPrice = "N/A";
            ViewBag.TetherChange1h = "N/A";
            ViewBag.TetherChange24h = "N/A";
            ViewBag.TetherChange7d = "N/A";
        }

        return View("Index");
    }

    // Returns 90-day chart data (used for graphs)
    [HttpGet]
    public async Task<IActionResult> GetHistoricalData(string id = "bitcoin")
    {
        try
        {
            var cacheKey = $"HistoricalData_{id}";
            var cachedData = await _cache.GetStringAsync(cacheKey);

            if (!string.IsNullOrEmpty(cachedData))
            {
                return Content(cachedData, "application/json");
            }

            var apiUrl = $"https://api.coingecko.com/api/v3/coins/{id}/market_chart?vs_currency=usd&days=90";

            var response = await _retryPolicy.ExecuteAsync(() => _httpClient.GetAsync(apiUrl));

            if (!response.IsSuccessStatusCode)
            {
                return StatusCode((int)response.StatusCode, "Failed to fetch historical data");
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            var historicalData = JsonConvert.DeserializeObject<dynamic>(responseContent);

            var prices = new List<decimal>();
            var dates = new List<string>();

            var englishCulture = new CultureInfo("en-US");
            int counter = 0;
            foreach (var item in historicalData.prices)
            {
                if (counter++ % 7 == 0) 
                {
                    decimal price = item[1];
                    prices.Add(price);

                    var timestamp = (long)item[0];
                    var date = DateTimeOffset.FromUnixTimeMilliseconds(timestamp).DateTime;
                    dates.Add(date.ToString("MMM dd", englishCulture));
                }
            }

            var result = new
            {
                prices = prices,
                dates = dates
            };

            var jsonResult = JsonConvert.SerializeObject(result);

            await _cache.SetStringAsync(cacheKey, jsonResult, new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(6)
            });

            return Content(jsonResult, "application/json");
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Error fetching historical data: {ex.Message}");
        }
    }

}