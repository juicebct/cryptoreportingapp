using Microsoft.AspNetCore.Mvc;
using System;

namespace CryptoReportingApp.Models
{
    public enum TransactionType
    {
        Purchase,
        Sale
    }

    public class Transaction
    {
        public TransactionType Type { get; set; }
        public string CryptoName { get; set; }
        public DateTime Date { get; set; }
        public decimal Quantity { get; set; }
        public decimal PricePerUnit { get; set; }

        public decimal Total => Quantity * PricePerUnit;
    }
}
