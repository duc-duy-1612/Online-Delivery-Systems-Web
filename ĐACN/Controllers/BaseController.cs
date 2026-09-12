using ĐACN.Models;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Web;
using System.Web.Mvc;
using System.Configuration;

namespace ĐACN.Controllers
{
    public abstract class BaseController : Controller, IDisposable
    {
        protected readonly FoodDeliveryDBEntities db = new FoodDeliveryDBEntities();
        private bool _disposed = false;

        // Shared HttpClient - reuse instance to prevent Socket Exhaustion
        protected static readonly HttpClient _sharedHttpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

        protected static readonly string ORS_API_KEY = ConfigurationManager.AppSettings["ORS_API_KEY"];


        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    if (db != null)
                    {
                        db.Dispose();
                    }
                }
                _disposed = true;
            }
            base.Dispose(disposing);
        }

        public new void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }


        protected string LayDiaChiIP()
        {
            string ip = Request.ServerVariables["HTTP_X_FORWARDED_FOR"];
            if (string.IsNullOrEmpty(ip))
                ip = Request.ServerVariables["REMOTE_ADDR"];
            return ip;
        }

        protected string RemoveVietnameseSigns(string str)
        {
            if (string.IsNullOrWhiteSpace(str)) return str;
            string[] VietNamChar = new string[]
            {
                "aAeEoOuUiIdDyY",
                "áàạảãâấầậẩẫăắằặẳẵ",
                "ÁÀẠẢÃÂẤẦẬẨẪĂẮẰẶẲẴ",
                "éèẹẻẽêếềệểễ",
                "ÉÈẸẺẼÊẾỀỆỂỄ",
                "óòọỏõôốồộổỗơớờợởỡ",
                "ÓÒỌỎÕÔỐỒỘỔỖƠỚỜỢỞỠ",
                "úùụủũưứừựửữ",
                "ÚÙỤỦŨƯỨỪỰỬỮ",
                "íìịỉĩ",
                "ÍÌỊỈĨ",
                "đ",
                "Đ",
                "ýỳỵỷỹ",
                "ÝỲỴỶỸ"
            };
            for (int i = 1; i < VietNamChar.Length; i++)
            {
                for (int j = 0; j < VietNamChar[i].Length; j++)
                    str = str.Replace(VietNamChar[i][j], VietNamChar[0][i - 1]);
            }
            return str.ToLower();
        }

        protected bool ValidateImageFile(HttpPostedFileBase file, out string errorMessage)
        {
            errorMessage = "";
            if (file == null || file.ContentLength == 0)
            {
                errorMessage = "File không được để trống.";
                return false;
            }

            if (file.ContentLength > 5 * 1024 * 1024)
            {
                errorMessage = "Kích thước file không được vượt quá 5MB.";
                return false;
            }

            var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp" };
            var ext = Path.GetExtension(file.FileName).ToLower();
            if (!allowedExtensions.Contains(ext))
            {
                errorMessage = "Chỉ chấp nhận file ảnh (jpg, jpeg, png, gif, bmp).";
                return false;
            }

            var allowedMimeTypes = new[] { "image/jpeg", "image/jpg", "image/png", "image/gif", "image/bmp" };
            var mimeType = file.ContentType.ToLower();
            if (!allowedMimeTypes.Contains(mimeType))
            {
                errorMessage = "File không phải là ảnh hợp lệ. MIME type: " + mimeType;
                return false;
            }

            return true;
        }

        protected bool ValidatePhoneNumber(string phoneNumber, out string errorMessage)
        {
            errorMessage = "";
            if (string.IsNullOrWhiteSpace(phoneNumber))
            {
                errorMessage = "Số điện thoại không được để trống.";
                return false;
            }

            string cleanedPhone = new string(phoneNumber.Where(char.IsDigit).ToArray());

            if (cleanedPhone.Length != 10)
            {
                errorMessage = "Số điện thoại không hợp lí";
                return false;
            }

            return true;
        }

        protected (bool isValid, string message, double? lat, double? lng) ValidateAddressRealtime(string specificStreet, string fullAddress)
        {
            if (string.IsNullOrWhiteSpace(fullAddress)) return (false, "Vui lòng nhập địa chỉ.", null, null);
            if (fullAddress.Length < 10) return (false, "Địa chỉ quá ngắn.", null, null);

            if (Regex.IsMatch(fullAddress.Replace(",", "").Replace(" ", ""), @"^([a-zA-Z0-9])\1+$"))
                return (false, "Địa chỉ không hợp lệ.", null, null);

            (bool success, JObject data) CallApi(string query)
            {
                try
                {
                    _sharedHttpClient.DefaultRequestHeaders.Remove("User-Agent");
                    _sharedHttpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "TapFoodDelivery/1.0");
                    string search = Uri.EscapeDataString(query);
                    string url = $"https://api.openrouteservice.org/geocode/search?api_key={ORS_API_KEY}&text={search}&size=1&boundary.country=VN";
                    var response = _sharedHttpClient.GetAsync(url).Result;
                    if (response.IsSuccessStatusCode)
                    {
                        var json = response.Content.ReadAsStringAsync().Result;
                        return (true, JObject.Parse(json));
                    }
                }
                catch { }
                return (false, null);
            }

            var apiResult = CallApi(fullAddress);
            
            // Fallback 1: Remove house number / specific street info (first comma segment)
            if (!apiResult.success || apiResult.data == null || (apiResult.data["features"] as JArray)?.Count == 0)
            {
                var parts = fullAddress.Split(',');
                if (parts.Length > 1)
                {
                    string fallbackAddress = string.Join(",", parts.Skip(1)).Trim();
                    apiResult = CallApi(fallbackAddress);
                }
            }

            // Fallback 2: Remove ward info, just search district/city
            if (!apiResult.success || apiResult.data == null || (apiResult.data["features"] as JArray)?.Count == 0)
            {
                var parts = fullAddress.Split(',');
                if (parts.Length > 2)
                {
                    string fallbackAddress2 = string.Join(",", parts.Skip(2)).Trim();
                    apiResult = CallApi(fallbackAddress2);
                }
            }

            if (apiResult.success && apiResult.data != null)
            {
                var features = apiResult.data["features"] as JArray;
                if (features != null && features.Count > 0)
                {
                    var props = features[0]["properties"];
                    var geometry = features[0]["geometry"]["coordinates"];
                    double lng = geometry[0].Value<double>();
                    double lat = geometry[1].Value<double>();

                    string apiName = props["name"]?.ToString() ?? "Khu vực không xác định";

                    return (true, $"Định vị tại {apiName}", lat, lng);
                }
            }

            return (false, "Không tìm thấy địa chỉ trên bản đồ.", null, null);
        }

        protected JsonResult JsonError(string message, object additionalData = null)
        {
            var response = new { success = false, message = message };
            if (additionalData != null)
            {
                return Json(new { success = false, message = message, data = additionalData }, JsonRequestBehavior.AllowGet);
            }
            return Json(response, JsonRequestBehavior.AllowGet);
        }

        protected ActionResult HandleError(string message, string redirectAction = "Index", string redirectController = null)
        {
            TempData["Msg"] = message;
            if (!string.IsNullOrEmpty(redirectController))
            {
                return RedirectToAction(redirectAction, redirectController);
            }
            return RedirectToAction(redirectAction);
        }

        protected void LogError(Exception ex, string context = "")
        {

            System.Diagnostics.Debug.WriteLine($"ERROR [{context}]: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"Stack Trace: {ex.StackTrace}");

            if (ex.InnerException != null)
            {
                System.Diagnostics.Debug.WriteLine($"Inner Exception: {ex.InnerException.Message}");
            }
        }
    }
}