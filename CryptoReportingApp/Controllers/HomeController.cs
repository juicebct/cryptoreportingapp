using CryptoReportingApp.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Distributed;
using Newtonsoft.Json;
using Polly.CircuitBreaker;
using Polly;
using System.Net.Http.Headers;

// Controller for homepage (dashboard)
public class HomeController : Controller
{
    private readonly HttpClient _httpClient;
    private readonly IDistributedCache _cache;
    private readonly AsyncCircuitBreakerPolicy _circuitBreakerPolicy;
    private readonly IAsyncPolicy<HttpResponseMessage> _retryPolicy;
    private static readonly string FilePath = "portfolio.json";
    private static Portfolio _portfolio = new Portfolio();

    // Constructor sets up HTTP client, cache, retry and circuit breaker
    public HomeController(IDistributedCache cache)
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

    // Main page – shows filtered/sorted transactions
    public async Task<IActionResult> Index(
    string searchQuery,
    string currencyFilter,
    string typeFilter,
    string dateFilter,
    string sortBy,
    string sortDirection)

    {

        await _portfolio.LoadFromFileAsync(FilePath);
        var transactions = _portfolio.Transactions;

        var realizedProfit = _portfolio.CalculateRealizedProfit();
        ViewBag.RealizedProfit = realizedProfit;


        var tokenBalances = transactions
            .GroupBy(tx => tx.CryptoName)
            .ToDictionary(g => g.Key, g => g.Sum(tx => tx.Type == TransactionType.Purchase ? tx.Quantity : -tx.Quantity));

        ViewBag.TokenBalances = tokenBalances;

        var currentPrices = new Dictionary<string, decimal>();
        decimal btcPrice = 0, ethPrice = 0, usdtPrice = 0;

        if (ViewBag.BitcoinPrice != null && decimal.TryParse(ViewBag.BitcoinPrice.ToString(), out btcPrice))
            currentPrices["bitcoin"] = btcPrice;
        if (ViewBag.EthereumPrice != null && decimal.TryParse(ViewBag.EthereumPrice.ToString(), out ethPrice))
            currentPrices["ethereum"] = ethPrice;
        if (ViewBag.TetherPrice != null && decimal.TryParse(ViewBag.TetherPrice.ToString(), out usdtPrice))
            currentPrices["tether"] = usdtPrice;

        bool ascending = sortDirection?.ToLower() != "desc";

        transactions = sortBy switch
        {
            "price" => ascending
                ? transactions.OrderBy(tx => tx.PricePerUnit).ToList()
                : transactions.OrderByDescending(tx => tx.PricePerUnit).ToList(),

            "date" => ascending
                ? transactions.OrderBy(tx => tx.Date).ToList()
                : transactions.OrderByDescending(tx => tx.Date).ToList(),

            "quantity" => ascending
                ? transactions.OrderBy(tx => tx.Quantity).ToList()
                : transactions.OrderByDescending(tx => tx.Quantity).ToList(),

            _ => transactions
        };

        ViewData["SortBy"] = sortBy;
        ViewData["SortDirection"] = sortDirection;

        var totalProfit = _portfolio.CalculateTotalProfit(currentPrices);
        ViewBag.TotalProfit = totalProfit;

        ViewData["SearchQuery"] = searchQuery;
        ViewData["CurrencyFilter"] = currencyFilter;
        ViewData["TypeFilter"] = typeFilter;
        ViewData["DateFilter"] = dateFilter;

        if (!string.IsNullOrEmpty(searchQuery))
        {
            transactions = transactions
                .Where(tx => tx.CryptoName.Contains(searchQuery, StringComparison.OrdinalIgnoreCase) ||
                             tx.Type.ToString().Contains(searchQuery, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        if (!string.IsNullOrEmpty(currencyFilter))
        {
            transactions = transactions
                .Where(tx => tx.CryptoName.Equals(currencyFilter, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        if (!string.IsNullOrEmpty(typeFilter))
        {
            transactions = transactions
                .Where(tx => tx.Type.ToString().Equals(typeFilter, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        if (DateTime.TryParse(dateFilter, out DateTime filterDate))
        {
            transactions = transactions
                .Where(tx => tx.Date.Date == filterDate.Date)
                .ToList();
        }

        ViewBag.Transactions = transactions;

        var cacheKey = "CryptoPrices";
        var cachedData = await _cache.GetStringAsync(cacheKey);

        if (string.IsNullOrEmpty(cachedData))
        {
            try
            {
                var apiUrl = "https://api.coingecko.com/api/v3/coins/markets?ids=bitcoin,ethereum,tether&vs_currency=usd&price_change_percentage=1h,24h,7d";
                var response = await _retryPolicy.ExecuteAsync(async () =>
                {
                    return await _httpClient.GetAsync(apiUrl);
                });

                if (_circuitBreakerPolicy.CircuitState == CircuitState.Open)
                {
                    throw new BrokenCircuitException("Circuit is open");
                }

                if (!response.IsSuccessStatusCode)
                {
                    throw new HttpRequestException($"API request failed with status code: {response.StatusCode}");
                }

                var responseContent = await response.Content.ReadAsStringAsync();
                var priceData = JsonConvert.DeserializeObject<List<dynamic>>(responseContent);

                var bitcoinData = priceData.FirstOrDefault(c => c.id == "bitcoin");
                var ethereumData = priceData.FirstOrDefault(c => c.id == "ethereum");
                var tetherData = priceData.FirstOrDefault(c => c.id == "tether");

                ViewBag.BitcoinChange1h = bitcoinData?.price_change_percentage_1h_in_currency?.ToString("0.00");
                ViewBag.BitcoinChange24h = bitcoinData?.price_change_percentage_24h_in_currency?.ToString("0.00");
                ViewBag.BitcoinChange7d = bitcoinData?.price_change_percentage_7d_in_currency?.ToString("0.00");

                ViewBag.EthereumChange1h = ethereumData?.price_change_percentage_1h_in_currency?.ToString("0.00");
                ViewBag.EthereumChange24h = ethereumData?.price_change_percentage_24h_in_currency?.ToString("0.00");
                ViewBag.EthereumChange7d = ethereumData?.price_change_percentage_7d_in_currency?.ToString("0.00");

                ViewBag.TetherChange1h = tetherData?.price_change_percentage_1h_in_currency?.ToString("0.00");
                ViewBag.TetherChange24h = tetherData?.price_change_percentage_24h_in_currency?.ToString("0.00");
                ViewBag.TetherChange7d = tetherData?.price_change_percentage_7d_in_currency?.ToString("0.00");

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

    // If API is down -? show cached or fallback data
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

    // Add transaction from raw form inputs
    [HttpPost]
        public IActionResult AddTransaction(string CryptoName, string Type, decimal Quantity, decimal PricePerUnit, DateTime Date)
        {
            var portfolio = LoadPortfolio();

            var transaction = new Transaction
            {
                CryptoName = CryptoName,
                Type = (TransactionType)Enum.Parse(typeof(TransactionType), Type),
                Quantity = Quantity,
                PricePerUnit = PricePerUnit,
                Date = Date
            };

            portfolio.AddTransaction(transaction);
            SavePortfolio(portfolio);

            return RedirectToAction("Index");
        }

        // Load sections
        private Portfolio LoadPortfolio()
        {
            var portfolio = new Portfolio();
            portfolio.LoadFromFileAsync(FilePath).GetAwaiter().GetResult();
            return portfolio;
        }

        // Save sections
         private void SavePortfolio(Portfolio portfolio)
        {
            portfolio.SaveToFileAsync(FilePath).GetAwaiter().GetResult();
        }

        // Remove transaction
        public IActionResult DeleteTransaction(int transactionIndex)
        {
            var portfolio = LoadPortfolio();

            portfolio.RemoveTransactionAt(transactionIndex);
            SavePortfolio(portfolio);

            return RedirectToAction("Index");
        }

    }