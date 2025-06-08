using Microsoft.AspNetCore.Mvc;

namespace CryptoReportingApp.Models
{
    public class Cryptocurrency : Asset
    {
        public decimal CurrentPrice { get; set; }
    }
}
