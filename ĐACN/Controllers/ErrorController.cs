using System.Web.Mvc;
using ĐACN.Models;

namespace ĐACN.Controllers
{
    public class ErrorController : Controller
    {
        public ActionResult Index()
        {
            ViewBag.ErrorMessage = TempData["Error"] ?? "Đã xảy ra lỗi. Vui lòng thử lại sau.";
            ViewBag.Controller = TempData["Controller"];
            ViewBag.Action = TempData["Action"];
            
            return View();
        }

        public ActionResult DatabaseError()
        {
            ViewBag.ErrorMessage = TempData["Error"] ?? "Lỗi cơ sở dữ liệu. Vui lòng thử lại sau hoặc liên hệ quản trị viên.";
            ViewBag.Controller = TempData["Controller"];
            ViewBag.Action = TempData["Action"];
            
            return View("Index");
        }
    }
}

