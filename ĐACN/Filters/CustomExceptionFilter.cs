using System;
using System.Web;
using System.Web.Mvc;

namespace ĐACN.Filters
{
    /// <summary>
    /// Custom Exception Filter để xử lý lỗi toàn cục
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

            // Log lỗi (có thể mở rộng với logging framework)
            System.Diagnostics.Debug.WriteLine($"EXCEPTION in {controllerName}/{actionName}: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"Stack Trace: {ex.StackTrace}");

            // Ngăn chặn vòng lặp vô hạn nếu lỗi xảy ra ngay trong ErrorController
            if (string.Equals(controllerName, "Error", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            // Xử lý các loại exception khác nhau
            if (ex is System.Data.Entity.Infrastructure.DbUpdateException dbEx)
            {
                filterContext.ExceptionHandled = true;
                
                // Lấy thông tin chi tiết
                string dbErrorMessage = "Lỗi cơ sở dữ liệu. Vui lòng thử lại sau.";
                string innerMsg = dbEx.InnerException != null ? dbEx.InnerException.Message : dbEx.Message;
                string dbDetailedError = $"Loại lỗi: {dbEx.GetType().Name}\n";
                dbDetailedError += $"Thông báo: {dbErrorMessage}\n";
                dbDetailedError += $"Chi tiết: {innerMsg}\n";
                if (!string.IsNullOrEmpty(dbEx.StackTrace))
                {
                    dbDetailedError += $"\nChi tiết kỹ thuật:\n{dbEx.StackTrace}";
                }
                
                if (filterContext.HttpContext.Request.IsAjaxRequest())
                {
                    filterContext.Result = new JsonResult
                    {
                        Data = new { success = false, message = dbErrorMessage, details = innerMsg },
                        JsonRequestBehavior = JsonRequestBehavior.AllowGet
                    };
                }
                else
                {
                    filterContext.Controller.TempData["Error"] = dbErrorMessage;
                    filterContext.Controller.TempData["ErrorType"] = dbEx.GetType().Name;
                    filterContext.Controller.TempData["ErrorDetails"] = dbDetailedError;
                    filterContext.Controller.TempData["StackTrace"] = dbEx.StackTrace;
                    filterContext.Controller.TempData["InnerException"] = innerMsg;
                    filterContext.Controller.TempData["Controller"] = controllerName;
                    filterContext.Controller.TempData["Action"] = actionName;
                    filterContext.Result = new RedirectResult("~/Error/DatabaseError");
                }
                return;
            }

            if (ex is System.Data.SqlClient.SqlException sqlEx)
            {
                filterContext.ExceptionHandled = true;
                string sqlErrorMessage = "Lỗi cơ sở dữ liệu. Vui lòng thử lại sau.";
                
                // Xử lý các lỗi SQL cụ thể
                if (sqlEx.Number == 2601 || sqlEx.Number == 2627)
                {
                    sqlErrorMessage = "Dữ liệu đã tồn tại trong hệ thống.";
                }
                else if (sqlEx.Number == 547)
                {
                    sqlErrorMessage = "Không thể xóa dữ liệu này vì đang được sử dụng ở nơi khác.";
                }

                if (filterContext.HttpContext.Request.IsAjaxRequest())
                {
                    filterContext.Result = new JsonResult
                    {
                        Data = new { success = false, message = sqlErrorMessage },
                        JsonRequestBehavior = JsonRequestBehavior.AllowGet
                    };
                }
                else
                {
                    // Lưu thông tin chi tiết
                    string sqlDetailedError = $"Loại lỗi: {sqlEx.GetType().Name}\n";
                    sqlDetailedError += $"Thông báo: {sqlErrorMessage}\n";
                    sqlDetailedError += $"Mã lỗi SQL: {sqlEx.Number}\n";
                    sqlDetailedError += $"Chi tiết: {sqlEx.Message}\n";
                    if (!string.IsNullOrEmpty(sqlEx.StackTrace))
                    {
                        sqlDetailedError += $"\nChi tiết kỹ thuật:\n{sqlEx.StackTrace}";
                    }
                    
                    filterContext.Controller.TempData["Error"] = sqlErrorMessage;
                    filterContext.Controller.TempData["ErrorType"] = sqlEx.GetType().Name;
                    filterContext.Controller.TempData["ErrorDetails"] = sqlDetailedError;
                    filterContext.Controller.TempData["StackTrace"] = sqlEx.StackTrace;
                    filterContext.Controller.TempData["InnerException"] = sqlEx.Message;
                    filterContext.Controller.TempData["Controller"] = controllerName;
                    filterContext.Controller.TempData["Action"] = actionName;
                    filterContext.Result = new RedirectResult("~/Error/Index");
                }
                return;
            }

            if (ex is UnauthorizedAccessException)
            {
                filterContext.ExceptionHandled = true;
                filterContext.Result = new RedirectResult("~/Account/Login");
                return;
            }

            // Xử lý lỗi chung
            filterContext.ExceptionHandled = true;
            
            // Lấy thông tin chi tiết về lỗi
            string errorMessage = ex.Message;
            string errorType = ex.GetType().Name;
            string stackTrace = ex.StackTrace;
            string innerException = ex.InnerException != null ? ex.InnerException.Message : null;
            string innerStackTrace = ex.InnerException != null ? ex.InnerException.StackTrace : null;
            
            // Tạo thông báo lỗi chi tiết
            string detailedError = $"Loại lỗi: {errorType}\n";
            detailedError += $"Thông báo: {errorMessage}\n";
            if (!string.IsNullOrEmpty(innerException))
            {
                detailedError += $"Lỗi bên trong: {innerException}\n";
            }
            if (!string.IsNullOrEmpty(stackTrace))
            {
                detailedError += $"\nChi tiết kỹ thuật:\n{stackTrace}";
                if (!string.IsNullOrEmpty(innerStackTrace))
                {
                    detailedError += $"\n\nChi tiết lỗi bên trong:\n{innerStackTrace}";
                }
            }
            
            if (filterContext.HttpContext.Request.IsAjaxRequest())
            {
                filterContext.Result = new JsonResult
                {
                    Data = new { success = false, message = errorMessage, errorType = errorType },
                    JsonRequestBehavior = JsonRequestBehavior.AllowGet
                };
            }
            else
            {
                // Lưu thông tin chi tiết vào TempData
                filterContext.Controller.TempData["Error"] = errorMessage;
                filterContext.Controller.TempData["ErrorType"] = errorType;
                filterContext.Controller.TempData["ErrorDetails"] = detailedError;
                filterContext.Controller.TempData["StackTrace"] = stackTrace;
                filterContext.Controller.TempData["InnerException"] = innerException;
                filterContext.Controller.TempData["Controller"] = controllerName;
                filterContext.Controller.TempData["Action"] = actionName;
                filterContext.Result = new RedirectResult("~/Error/Index");
            }
        }
    }
}

