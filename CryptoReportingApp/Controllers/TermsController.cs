using Microsoft.AspNetCore.Mvc;

namespace CryptoReportingApp.Controllers
{
    public class TermsController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
