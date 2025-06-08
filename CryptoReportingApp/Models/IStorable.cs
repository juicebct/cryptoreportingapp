using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;

namespace CryptoReportingApp.Models
{
    public interface IStorable
    {
        Task SaveToFileAsync(string filePath);
        Task LoadFromFileAsync(string filePath);
    }
}