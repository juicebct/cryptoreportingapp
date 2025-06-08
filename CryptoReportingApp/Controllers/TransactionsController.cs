using Microsoft.AspNetCore.Mvc;
using CryptoReportingApp.Models;
using System;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Generic;

namespace CryptoReportingApp.Controllers
{
    // Controller for Transactions
    public class TransactionsController : Controller
    {
        private static readonly string FilePath = "portfolio.json";
        private static Portfolio _portfolio = new Portfolio();

        // Loads transactions from file
        private async Task LoadTransactionsAsync()
        {
            await _portfolio.LoadFromFileAsync(FilePath);
        }

        // Main page – shows filtered/sorted transactions
        public async Task<IActionResult> Index(string searchQuery = "", string currencyFilter = "", string typeFilter = "", DateTime? dateFilter = null, string sortBy = "", string sortDirection = "")
        {
            await LoadTransactionsAsync();

            var transactions = _portfolio.Transactions;

            if (!string.IsNullOrEmpty(searchQuery))
            {
                transactions = transactions.Where(t =>
                    t.CryptoName.Contains(searchQuery, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            if (!string.IsNullOrEmpty(currencyFilter))
            {
                transactions = transactions.Where(t =>
                    t.CryptoName.Equals(currencyFilter, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            if (!string.IsNullOrEmpty(typeFilter))
            {
                transactions = transactions.Where(t =>
                    t.Type.ToString().Equals(typeFilter, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            if (dateFilter.HasValue)
            {
                transactions = transactions.Where(t => t.Date.Date == dateFilter.Value.Date).ToList();
            }

            ViewData["SearchQuery"] = searchQuery;
            ViewData["DateFilter"] = dateFilter?.ToString("yyyy-MM-dd") ?? "";

            // Sort logic
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

            ViewBag.Transactions = transactions;

            return View();
        }

        // Show form to add new transaction
        public IActionResult Create()
        {
            return View();
        }

        // Handle form submission to add a transaction
        [HttpPost]
        public async Task<IActionResult> Create(Transaction tx)
        {
            if (ModelState.IsValid)
            {
                await LoadTransactionsAsync();
                _portfolio.AddTransaction(tx);
                await _portfolio.SaveToFileAsync(FilePath);
                return RedirectToAction(nameof(Index));
            }
            return View(tx);
        }

        // Add transaction from raw form inputs
        [HttpPost]
        public async Task<IActionResult> AddTransaction(string CryptoName, string Type, decimal Quantity, decimal PricePerUnit, DateTime Date)
        {
            var tx = new Transaction
            {
                CryptoName = CryptoName,
                Type = (TransactionType)Enum.Parse(typeof(TransactionType), Type),
                Quantity = Quantity,
                PricePerUnit = PricePerUnit,
                Date = Date
            };

            await LoadTransactionsAsync();
            _portfolio.AddTransaction(tx);
            await _portfolio.SaveToFileAsync(FilePath);

            return RedirectToAction(nameof(Index));
        }

        // Remove transaction by index
        [HttpPost]
        public async Task<IActionResult> DeleteTransaction(int transactionIndex)
        {
            await LoadTransactionsAsync();

            var transactionsList = _portfolio.Transactions.ToList();

            if (transactionIndex >= 0 && transactionIndex < transactionsList.Count)
            {
                transactionsList.RemoveAt(transactionIndex);
            }

            // Rebuild sections after deletion
            _portfolio = new Portfolio();
            foreach (var tx in transactionsList)
            {
                _portfolio.AddTransaction(tx);
            }

            await _portfolio.SaveToFileAsync(FilePath);

            return RedirectToAction(nameof(Index));
        }

        // Download transactions as a JSON file
        [HttpGet]
        public async Task<IActionResult> Download()
        {
            await LoadTransactionsAsync();
            var json = System.Text.Json.JsonSerializer.Serialize(_portfolio.Transactions, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            var bytes = System.Text.Encoding.UTF8.GetBytes(json);
            return File(bytes, "application/json", "transactions.json");
        }

        // Upload JSON file with transactions
        [HttpPost]
        public async Task<IActionResult> Upload(IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                TempData["UploadError"] = "File is empty or missing.";
                return RedirectToAction(nameof(Index));
            }

            using var reader = new StreamReader(file.OpenReadStream());
            var content = await reader.ReadToEndAsync();

            try
            {
                var transactions = System.Text.Json.JsonSerializer.Deserialize<List<Transaction>>(content);

                if (transactions == null || !transactions.Any())
                {
                    TempData["UploadError"] = "No valid transactions found in the file.";
                    return RedirectToAction(nameof(Index));
                }
                // Validate each transaction
                foreach (var tx in transactions)
                {
                    if (string.IsNullOrWhiteSpace(tx.CryptoName) ||
                        !Enum.IsDefined(typeof(TransactionType), tx.Type) ||
                        tx.Quantity <= 0 ||
                        tx.PricePerUnit <= 0 ||
                        tx.Date == default)
                    {
                        TempData["UploadError"] = "One or more transactions have missing or invalid fields.";
                        return RedirectToAction(nameof(Index));
                    }
                }

                _portfolio = new Portfolio();
                foreach (var tx in transactions)
                {
                    _portfolio.AddTransaction(tx);
                }

                await _portfolio.SaveToFileAsync(FilePath);
                TempData["UploadSuccess"] = "Transactions uploaded successfully.";
            }
            catch
            {
                TempData["UploadError"] = "Invalid JSON file format.";
            }

            return RedirectToAction(nameof(Index));
        }
    }
}
