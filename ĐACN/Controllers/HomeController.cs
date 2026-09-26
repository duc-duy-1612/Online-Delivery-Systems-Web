using ĐACN.Models;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Web;
using System.Web.Mvc;
using System.Threading.Tasks;

namespace ĐACN.Controllers
{
    public class HomeController : BaseController
    {
        private readonly ĐACN.Services.CacheService _cacheService = new ĐACN.Services.CacheService();
        private static readonly string ORS_API_KEY = System.Configuration.ConfigurationManager.AppSettings["ORS_API_KEY"];

        private async Task<(double? lat, double? lng)> GeoCodeORSAsync(string address)
        {
            if (string.IsNullOrEmpty(address)) return (null, null);
            try
            {
                string cleanedAddress = RemoveVietnameseSigns(address).Trim();
                if (!cleanedAddress.ToLower().Contains("vietnam") && !cleanedAddress.ToLower().Contains("viet nam"))
                    cleanedAddress += ", Vietnam";

                _sharedHttpClient.DefaultRequestHeaders.Remove("User-Agent");
                _sharedHttpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "TapFoodApp");
                var url = $"https://api.openrouteservice.org/geocode/search?api_key={ORS_API_KEY}&text={Uri.EscapeDataString(cleanedAddress)}&size=1";
                var response = await _sharedHttpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var obj = JObject.Parse(json);
                    var features = obj["features"] as JArray;
                    if (features != null && features.Count > 0)
                    {
                        var coords = features[0]["geometry"]["coordinates"];
                        return (coords[1].Value<double>(), coords[0].Value<double>());
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[GeoCodeORSAsync] Error geocoding address '{address}': {ex.Message}");
            }
            return (null, null);
        }

        private double CalculateDistance(double lat1, double lon1, double lat2, double lon2)
        {
            if (lat1 == 0 || lon1 == 0 || lat2 == 0 || lon2 == 0) return 999;
            double R = 6371;
            double dLat = (lat2 - lat1) * (Math.PI / 180);
            double dLon = (lon2 - lon1) * (Math.PI / 180);
            double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                       Math.Cos(lat1 * (Math.PI / 180)) * Math.Cos(lat2 * (Math.PI / 180)) *
                       Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            return Math.Round(R * c, 1);
        }


        private async Task<(List<NhaHangViewModel> nhaHangData, List<NhaHangViewModel> recommended)> ProcessHomeDataAsync(string sort, double? lat, double? lng, string search)
        {
            if ((lat == null || lng == null) && Session["MaKH"] != null)
            {
                string maKH = Session["MaKH"] as string;
                var kh = await db.KhachHangs.FindAsync(maKH);
                if (kh != null)
                {
                    if ((kh.Latitude == null || kh.Latitude == 0) && !string.IsNullOrEmpty(kh.DiaChi))
                    {
                        var coords = await GeoCodeORSAsync(kh.DiaChi);
                        if (coords.lat.HasValue)
                        {
                            kh.Latitude = coords.lat; kh.Longitude = coords.lng; await db.SaveChangesAsync();
                        }
                    }
                    lat = kh.Latitude ?? lat;
                    lng = kh.Longitude ?? lng;
                }
            }

            var nhaHangData = await LoadNhaHangDataAsync(lat, lng);

            if (!string.IsNullOrEmpty(search))
            {
                string keyword = RemoveVietnameseSigns(search).ToLower().Trim();
                var searchResult = nhaHangData.Where(x => RemoveVietnameseSigns(x.TenNH).ToLower().Contains(keyword) || RemoveVietnameseSigns(x.DiaChi).ToLower().Contains(keyword)).ToList();

                var maNHHoatDong = nhaHangData.Select(nh => nh.MaNH).ToList();
                var monAns = await db.MonAns.Where(m => maNHHoatDong.Contains(m.MaNH)).ToListAsync();
                var maNHTheoMon = monAns.Where(m => RemoveVietnameseSigns(m.TenMon).ToLower().Contains(keyword)).Select(m => m.MaNH).Distinct().ToList();
                var searchMonData = nhaHangData.Where(nh => maNHTheoMon.Contains(nh.MaNH)).ToList();
                searchResult.AddRange(searchMonData);
                nhaHangData = searchResult.GroupBy(x => x.MaNH).Select(g => g.First()).ToList();
            }

            nhaHangData = ApplySort(nhaHangData, sort);

            List<NhaHangViewModel> recommendedNhaHang = null;
            if (Session["MaKH"] != null)
            {
                string maKH = Session["MaKH"] as string;
                var recentOrders = await db.ChiTietDonHangs
                    .Where(c => c.DonHang.MaKH == maKH)
                    .OrderByDescending(c => c.DonHang.ThoiGianDat)
                    .Take(20)
                    .ToListAsync();

                var topCategory = recentOrders
                    .GroupBy(c => c.MonAn.MaLoai)
                    .OrderByDescending(g => g.Count())
                    .Select(g => g.Key)
                    .FirstOrDefault();

                if (topCategory != null)
                {
                    var maNHList = await db.MonAns.Where(m => m.MaLoai == topCategory).Select(m => m.MaNH).Distinct().ToListAsync();
                    recommendedNhaHang = nhaHangData
                        .Where(n => maNHList.Contains(n.MaNH))
                        .OrderByDescending(n => n.Score)
                        .Take(4)
                        .ToList();
                }
            }
            return (nhaHangData, recommendedNhaHang);
        }

        public async Task<ActionResult> TrangChu(string sort = "default", double? lat = null, double? lng = null, string search = "")
        {
            var data = await ProcessHomeDataAsync(sort, lat, lng, search);

            var loais = await db.LoaiMonAns.ToListAsync();
            var model = new TrangChuViewModel
            {
                DanhMuc = loais.Select(x => new LoaiMonAnViewModel
                {
                    MaLoai = x.MaLoai,
                    TenLoai = x.TenLoai,
                    HinhAnh = string.IsNullOrEmpty(x.HinhAnh) ? GetImageNameByMaLoai(x.MaLoai, x.TenLoai) : x.HinhAnh
                }).ToList(),
                NhaHang = new List<NhaHangViewModel>(), // Sẽ load qua AJAX
                RecommendedNhaHang = new List<NhaHangViewModel>() // Sẽ load qua AJAX
            };

            ViewBag.CurrentSort = sort;
            ViewBag.Search = search;
            return View(model);
        }

        [HttpGet]
        public async Task<ActionResult> GetHomeData(string sort = "default", double? lat = null, double? lng = null, string search = "")
        {
            var data = await ProcessHomeDataAsync(sort, lat, lng, search);

            string htmlRecommended = "";
            string htmlNhaHang = "";

            if (data.recommended != null && data.recommended.Any())
            {
                htmlRecommended = RenderPartialViewToString("_NhaHangNoiBatPartial", data.recommended);
            }
            htmlNhaHang = RenderPartialViewToString("_NhaHangNoiBatPartial", data.nhaHangData);

            return Json(new { recommended = htmlRecommended, nhahang = htmlNhaHang }, JsonRequestBehavior.AllowGet);
        }

        // Helper method to render partial view to string
        protected string RenderPartialViewToString(string viewName, object model)
        {
            if (string.IsNullOrEmpty(viewName))
                viewName = ControllerContext.RouteData.GetRequiredString("action");
            ViewData.Model = model;
            using (var sw = new System.IO.StringWriter())
            {
                var viewResult = ViewEngines.Engines.FindPartialView(ControllerContext, viewName);
                var viewContext = new ViewContext(ControllerContext, viewResult.View, ViewData, TempData, sw);
                viewResult.View.Render(viewContext, sw);
                return sw.GetStringBuilder().ToString();
            }
        }

        public async Task<ActionResult> DanhMuc()
        {
            var danhMucList = await _cacheService.GetOrSetAsync("Home_DanhMucList", 30, async () =>
            {
                var loais = await db.LoaiMonAns.ToListAsync();
                return loais.Select(x => new LoaiMonAnViewModel
                {
                    MaLoai = x.MaLoai,
                    TenLoai = x.TenLoai,
                    HinhAnh = string.IsNullOrEmpty(x.HinhAnh) ? GetImageNameByMaLoai(x.MaLoai, x.TenLoai) : x.HinhAnh
                }).ToList();
            });

            return View(danhMucList);
        }

        public async Task<ActionResult> NhaHang(string sort = "default", double? lat = null, double? lng = null)
        {
            if ((lat == null || lng == null) && Session["MaKH"] != null)
            {
                string maKH = Session["MaKH"] as string;
                var kh = await db.KhachHangs.FindAsync(maKH);
                if (kh != null)
                {
                    lat = kh.Latitude; lng = kh.Longitude;
                }
            }

            var nhaHangData = await LoadNhaHangDataAsync(lat, lng);
            nhaHangData = ApplySort(nhaHangData, sort);

            return View(nhaHangData);
        }

        public async Task<ActionResult> _NhaHangNoiBatPartial(List<NhaHangViewModel> data = null)
        {
            if (data != null) return PartialView(data);
            var defaultData = (await LoadNhaHangDataAsync()).OrderByDescending(x => x.Score).Take(8).ToList();
            return PartialView(defaultData);
        }

        public ActionResult _DanhMucPartial()
        {
            var danhMucList = _cacheService.GetOrSet("Home_DanhMucList", 30, () =>
            {
                var loais = db.LoaiMonAns.ToList();
                return loais.Select(x => new LoaiMonAnViewModel
                {
                    MaLoai = x.MaLoai,
                    TenLoai = x.TenLoai,
                    HinhAnh = string.IsNullOrEmpty(x.HinhAnh) ? GetImageNameByMaLoai(x.MaLoai, x.TenLoai) : x.HinhAnh
                }).ToList();
            });
            return PartialView(danhMucList);
        }

        private string GetImageNameByMaLoai(string maLoai, string tenLoai)
        {
            if (string.IsNullOrEmpty(maLoai) && string.IsNullOrEmpty(tenLoai))
                return "com.png";


            if (!string.IsNullOrEmpty(maLoai))
            {
                string maLoaiLower = maLoai.ToLower().Trim();
                var imageMap = new Dictionary<string, string>
                {
                    { "anvat", "an_vat.png" },
                    { "an_vat", "an_vat.png" },
                    { "bun", "bun.png" },
                    { "chay", "chay.png" },
                    { "com", "com.png" },
                    { "haisan", "haisan.png" },
                    { "hai_san", "haisan.png" },
                    { "lau", "lau.png" },
                    { "mi", "mi.png" },
                    { "pho", "pho.png" },
                    { "pizza", "pizza.png" },
                    { "steak", "steak.png" },
                    { "sushi", "sushi.png" },
                    { "trasua", "trasua.png" },
                    { "tra_sua", "trasua.png" }
                };

                if (imageMap.ContainsKey(maLoaiLower))
                    return imageMap[maLoaiLower];
            }


            if (!string.IsNullOrEmpty(tenLoai))
            {
                string tenLoaiLower = tenLoai.ToLower().Trim();
                if (tenLoaiLower.Contains("ăn vặt") || tenLoaiLower.Contains("an vat") || tenLoaiLower == "anvat") return "an_vat.png";
                if (tenLoaiLower.Contains("bún") || tenLoaiLower.Contains("bun")) return "bun.png";
                if (tenLoaiLower.Contains("chay")) return "chay.png";
                if (tenLoaiLower.Contains("cơm") || tenLoaiLower.Contains("com")) return "com.png";
                if (tenLoaiLower.Contains("hải sản") || tenLoaiLower.Contains("haisan") || tenLoaiLower.Contains("hai san")) return "haisan.png";
                if (tenLoaiLower.Contains("lẩu") || tenLoaiLower.Contains("lau")) return "lau.png";
                if (tenLoaiLower.Contains("mì") || tenLoaiLower.Contains("mi")) return "mi.png";
                if (tenLoaiLower.Contains("phở") || tenLoaiLower.Contains("pho")) return "pho.png";
                if (tenLoaiLower.Contains("pizza")) return "pizza.png";
                if (tenLoaiLower.Contains("steak")) return "steak.png";
                if (tenLoaiLower.Contains("sushi")) return "sushi.png";
                if (tenLoaiLower.Contains("trà sữa") || tenLoaiLower.Contains("trasua") || tenLoaiLower.Contains("tra sua")) return "trasua.png";
            }

            return "com.png";
        }


        [HttpPost]
        public JsonResult Login(string username, string password)
        {
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password)) 
                return Json(new { success = false, message = "Vui lòng nhập đầy đủ thông tin." });

            var tk = db.TaiKhoans.FirstOrDefault(x => x.TenDangNhap == username);
            if (tk == null) 
                return Json(new { success = false, message = "Sai tên đăng nhập hoặc mật khẩu." });

            if (tk.TrangThai != true)
                return Json(new { success = false, message = "Tài khoản của bạn đã bị khóa hoặc chưa được kích hoạt." });

            bool isPasswordValid = false;
            try
            {
                if (!string.IsNullOrEmpty(tk.MatKhau) && (tk.MatKhau.StartsWith("$2a$") || tk.MatKhau.StartsWith("$2b$")))
                {
                    isPasswordValid = BCrypt.Net.BCrypt.Verify(password, tk.MatKhau);
                }
                else
                {
                    isPasswordValid = (tk.MatKhau == password);
                }
            }
            catch
            {
                isPasswordValid = (tk.MatKhau == password);
            }

            if (!isPasswordValid)
                return Json(new { success = false, message = "Sai tên đăng nhập hoặc mật khẩu." });

            Session["TaiKhoan"] = tk;
            if (tk.VaiTro == "KhachHang")
            {
                var maKH = db.KhachHangs.FirstOrDefault(k => k.MaTK == tk.MaTK)?.MaKH;
                if (!string.IsNullOrEmpty(maKH)) Session["MaKH"] = maKH;
            }
            else if (tk.VaiTro == "Shipper")
            {
                var maShipper = db.Shippers.FirstOrDefault(s => s.MaTK == tk.MaTK)?.MaShipper;
                if (!string.IsNullOrEmpty(maShipper)) Session["MaShipper"] = maShipper;
            }
            else if (tk.VaiTro == "NhaHang")
            {
                var maNH = db.NhaHangs.FirstOrDefault(n => n.MaTK == tk.MaTK)?.MaNH;
                if (!string.IsNullOrEmpty(maNH)) Session["MaNH"] = maNH;
            }

            return Json(new { success = true });
        }

        public ActionResult Logout()
        {
            Session.Clear();
            return RedirectToAction("TrangChu");
        }

        public ActionResult KhachHangDangXuat()
        {
            Session.Clear();
            return RedirectToAction("TrangChu");
        }

        private async Task<List<NhaHangViewModel>> LoadNhaHangDataAsync(double? userLat = null, double? userLng = null)
        {
            string cacheKey = "Home_NhaHangData_Raw_v2";
            var cachedData = await _cacheService.GetOrSetAsync(cacheKey, 5, async () =>
            {
                var nhaHangList = await db.NhaHangs
                    .Include("TaiKhoan")
                    .Where(nh => nh.TaiKhoan != null && nh.TaiKhoan.TrangThai == true)
                    .ToListAsync();
                
                var danhGiaList = await db.DanhGiaNhaHangs
                                    .GroupBy(dg => dg.MaNH)
                                    .Select(g => new { MaNH = g.Key, AvgRating = g.Average(d => d.SoSao) })
                                    .ToListAsync();
                                    
                var luotMuaList = await db.DonHangs
                                    .Where(d => d.TrangThai == "Hoàn thành" || d.TrangThai == "Hoàn tất" || d.TrangThai == "True")
                                    .GroupBy(d => d.MaNH)
                                    .Select(g => new { MaNH = g.Key, LuotMua = g.Select(d => d.MaDon).Distinct().Count() })
                                    .ToListAsync();
                                    
                var danhGiaDict = danhGiaList.ToDictionary(dg => dg.MaNH, dg => dg.AvgRating ?? 0);
                var luotMuaDict = luotMuaList.ToDictionary(l => l.MaNH, l => l.LuotMua);
                var maxLuotMua = luotMuaDict.Values.Any() ? luotMuaDict.Values.Max() : 1;
                if (maxLuotMua <= 0) maxLuotMua = 1;

                return nhaHangList.Select(x =>
                {
                    double rating = danhGiaDict.TryGetValue(x.MaNH, out var r) ? r : 0;
                    int luotMua = luotMuaDict.TryGetValue(x.MaNH, out var lm) ? lm : 0;
                    double score = (rating * 0.6) + (((double)luotMua / maxLuotMua) * 4);
                    
                    return new NhaHangViewModel { 
                        MaNH = x.MaNH, 
                        TenNH = x.TenNH, 
                        DiaChi = x.DiaChi, 
                        TrangThai = x.TrangThai, 
                        HinhAnh = x.HinhAnh, 
                        Rating = Math.Round(rating, 1), 
                        TongLuotMua = luotMua, 
                        Score = score,
                        Latitude = x.Latitude,
                        Longitude = x.Longitude
                    };
                }).ToList();
            });

            // Copy list từ cache để tính khoảng cách riêng cho mỗi User (nếu có tọa độ)
            var result = cachedData.Select(x => new NhaHangViewModel
            {
                MaNH = x.MaNH,
                TenNH = x.TenNH,
                DiaChi = x.DiaChi,
                TrangThai = x.TrangThai,
                HinhAnh = x.HinhAnh,
                Rating = x.Rating,
                TongLuotMua = x.TongLuotMua,
                Score = x.Score,
                Latitude = x.Latitude,
                Longitude = x.Longitude,
                KhoangCachKm = 999
            }).ToList();

            // Nếu user có tọa độ, tính theo tọa độ user. Nếu chưa có, lấy tọa độ trung tâm TP.HCM (10.7769, 106.7009)
            double refLat = userLat ?? 10.7769;
            double refLng = userLng ?? 106.7009;

            foreach (var x in result)
            {
                double rLat = x.Latitude.HasValue && x.Latitude.Value > 0 ? x.Latitude.Value : 10.7769;
                double rLng = x.Longitude.HasValue && x.Longitude.Value > 0 ? x.Longitude.Value : 106.7009;

                // Trường hợp tọa độ ngoài miền Nam (vĩ độ > 15) nhưng địa chỉ ở TP.HCM
                if (rLat > 15 && !string.IsNullOrEmpty(x.DiaChi) && (x.DiaChi.Contains("Hồ Chí Minh") || x.DiaChi.Contains("TP. HCM") || x.DiaChi.Contains("Quận") || x.DiaChi.Contains("Phường")))
                {
                    rLat = 10.7769; rLng = 106.7009;
                }

                double dist = CalculateDistance(refLat, refLng, rLat, rLng);
                if (dist <= 0 || dist >= 999)
                {
                    dist = 2.5; // fallback khoảng cách hợp lý nếu lỗi tính toán
                }
                x.KhoangCachKm = dist;
            }

            return result;
        }

        private List<NhaHangViewModel> ApplySort(List<NhaHangViewModel> data, string sort)
        {
            switch (sort)
            {
                case "near": return data.Where(x => x.KhoangCachKm > 0 && x.KhoangCachKm <= 8).OrderBy(x => x.KhoangCachKm).ToList();
                case "rating": return data.OrderByDescending(x => x.Rating).ThenByDescending(x => x.TongLuotMua).ToList();
                case "bestseller": return data.OrderByDescending(x => x.TongLuotMua).ThenByDescending(x => x.Rating).ToList();
                default: return data.OrderByDescending(x => x.Score).ToList();
            }
        }

        public ActionResult AntigravityDemo()
        {
            return Redirect("~/Scripts/antigravity/demo.html");
        }
    }
}