using System.Web.Mvc;
using ĐACN.Models;

namespace ĐACN.Controllers
{
    public class ErrorController : Controller
    {
        public ActionResult Index()
        {
            ViewBag.ErrorMessage = TempData["Error"] ?? "Đã xảy ra lỗi. Vui lòng thử lại sau.";
            ViewBag.ErrorType = TempData["ErrorType"];
            ViewBag.ErrorDetails = TempData["ErrorDetails"];
            ViewBag.StackTrace = TempData["StackTrace"];
            ViewBag.InnerException = TempData["InnerException"];
            ViewBag.Controller = TempData["Controller"];
            ViewBag.Action = TempData["Action"];
            
            var tk = Session["TaiKhoan"] as TaiKhoan;
            ViewBag.IsAdmin = tk != null && tk.VaiTro == "Admin";
            
            return View();
        }

        public ActionResult DatabaseError()
        {
            ViewBag.ErrorMessage = TempData["Error"] ?? "Lỗi cơ sở dữ liệu. Vui lòng thử lại sau hoặc liên hệ quản trị viên.";
            ViewBag.ErrorType = TempData["ErrorType"];
            ViewBag.ErrorDetails = TempData["ErrorDetails"];
            ViewBag.StackTrace = TempData["StackTrace"];
            ViewBag.InnerException = TempData["InnerException"];
            ViewBag.Controller = TempData["Controller"];
            ViewBag.Action = TempData["Action"];
            
            // Kiểm tra xem có phải admin không
            var tk = Session["TaiKhoan"] as TaiKhoan;
            ViewBag.IsAdmin = tk != null && tk.VaiTro == "Admin";
            
            return View("Index");
        }
    }
}

