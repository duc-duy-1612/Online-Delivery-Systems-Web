using System;
using System.Web;
using System.Web.Mvc;

namespace ĐACN.Filters
{
    /// <summary>
    /// Custom Exception Filter để xử lý lỗi toàn cục
    /// Không bao giờ hiển thị chi tiết lỗi kỹ thuật cho người dùng cuối.
    /// </summary>
    public class CustomExceptionFilter : FilterAttribute, IExceptionFilter
    {
        public void OnException(ExceptionContext filterContext)
        {
            if (filterContext.ExceptionHandled)
                return;

            Exception ex = filterContext.Exception;
            string controllerName = filterContext.RouteData.Values["controller"]?.ToString();
            string actionName = filterContext.RouteData.Values["action"]?.ToString();

            // Log lỗi chi tiết vào server log (không hiện cho user)
            System.Diagnostics.Debug.WriteLine($"EXCEPTION in {controllerName}/{actionName}: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"Stack Trace: {ex.StackTrace}");
            if (ex.InnerException != null)
                System.Diagnostics.Debug.WriteLine($"Inner: {ex.InnerException.Message}");

            // Ngăn chặn vòng lặp vô hạn nếu lỗi xảy ra ngay trong ErrorController
            if (string.Equals(controllerName, "Error", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            filterContext.ExceptionHandled = true;

            // Chọn thông báo thân thiện cho user (KHÔNG chứa chi tiết kỹ thuật)
            string userMessage = "Đã xảy ra lỗi. Vui lòng thử lại sau.";

            if (ex is System.Data.Entity.Infrastructure.DbUpdateException)
            {
                userMessage = "Lỗi cơ sở dữ liệu. Vui lòng thử lại sau.";
            }
            else if (ex is System.Data.SqlClient.SqlException sqlEx)
            {
                if (sqlEx.Number == 2601 || sqlEx.Number == 2627)
                    userMessage = "Dữ liệu đã tồn tại trong hệ thống.";
                else if (sqlEx.Number == 547)
                    userMessage = "Không thể xóa dữ liệu này vì đang được sử dụng ở nơi khác.";
                else
                    userMessage = "Lỗi cơ sở dữ liệu. Vui lòng thử lại sau.";
            }
            else if (ex is UnauthorizedAccessException)
            {
                filterContext.Result = new RedirectResult("~/Account/Login");
                return;
            }

            if (filterContext.HttpContext.Request.IsAjaxRequest())
            {
                filterContext.Result = new JsonResult
                {
                    Data = new { success = false, message = userMessage },
                    JsonRequestBehavior = JsonRequestBehavior.AllowGet
                };
            }
            else
            {
                filterContext.Controller.TempData["Error"] = userMessage;
                filterContext.Controller.TempData["Controller"] = controllerName;
                filterContext.Controller.TempData["Action"] = actionName;
                filterContext.Result = new RedirectResult("~/Error/Index");
            }
        }
    }
}

