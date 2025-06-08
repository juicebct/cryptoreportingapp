using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace CryptoReportingApp.Models
{
    public class Portfolio : IStorable
    {
        private List<Transaction> _transactions = new();


        public IReadOnlyList<Transaction> Transactions => _transactions.AsReadOnly();

        public void AddTransaction(Transaction tx) => _transactions.Add(tx);
        public void RemoveTransaction(Transaction tx) => _transactions.Remove(tx);

        public void RemoveTransactionAt(int index)
        {
            if (index >= 0 && index < _transactions.Count)
            {
                _transactions.RemoveAt(index);
            }
        }

        public Dictionary<string, decimal> GetTokenQuantities()
        {
            var dict = new Dictionary<string, decimal>();

            foreach (var tx in _transactions)
            {
                if (!dict.ContainsKey(tx.CryptoName))
                    dict[tx.CryptoName] = 0;

                dict[tx.CryptoName] += tx.Type == TransactionType.Purchase ? tx.Quantity : -tx.Quantity;
            }

            return dict;
        }

        public decimal CalculateRealizedProfit()
        {
            var profitByToken = Transactions
                .GroupBy(tx => tx.CryptoName)
                .Select(g =>
                {
                    decimal totalBuy = g
                        .Where(tx => tx.Type == TransactionType.Purchase)
                        .Sum(tx => tx.Quantity * tx.PricePerUnit);

                    decimal totalSell = g
                        .Where(tx => tx.Type == TransactionType.Sale)
                        .Sum(tx => tx.Quantity * tx.PricePerUnit);

                    return totalSell - totalBuy;
                });

            return profitByToken.Sum();
        }


        public decimal CalculateTotalProfit(Dictionary<string, decimal> currentPrices)
        {
            decimal profit = 0;

            foreach (var tx in _transactions)
            {
                var name = tx.CryptoName.ToLower();
                if (!currentPrices.ContainsKey(name)) continue;

                var currentPrice = currentPrices[name];

                if (tx.Type == TransactionType.Purchase)
                    profit += (currentPrice - tx.PricePerUnit) * tx.Quantity;
                else
                    profit += (tx.PricePerUnit - currentPrice) * tx.Quantity;
            }

            return profit;
        }

        public async Task SaveToFileAsync(string filePath)
        {
            var json = JsonConvert.SerializeObject(_transactions, Formatting.Indented);
            await File.WriteAllTextAsync(filePath, json);
        }

        public async Task LoadFromFileAsync(string filePath)
        {
            if (File.Exists(filePath))
            {
                var json = await File.ReadAllTextAsync(filePath);
                _transactions = JsonConvert.DeserializeObject<List<Transaction>>(json) ?? new List<Transaction>();
            }
        }


    }
}
