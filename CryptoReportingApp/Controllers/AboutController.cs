using Microsoft.AspNetCore.Mvc;

namespace CryptoReportingApp.Controllers
{
    public class AboutController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
