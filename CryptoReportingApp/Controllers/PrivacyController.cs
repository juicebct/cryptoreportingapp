using Microsoft.AspNetCore.Mvc;

namespace CryptoReportingApp.Controllers
{
    public class PrivacyController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
