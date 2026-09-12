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

namespace ĐACN.Controllers
{
    public class HomeController : BaseController
    {
        private const string ORS_API_KEY = "eyJvcmciOiI1YjNjZTM1OTc4NTExMTAwMDFjZjYyNDgiLCJpZCI6ImFhZWYwMjY0NjIzZTRmNGU4MTE2NGQzYzlmZjJkYTYxIiwiaCI6Im11cm11cjY0In0=";


        private (double? lat, double? lng) GeoCodeORS(string address)
        {
            if (string.IsNullOrEmpty(address)) return (null, null);            try
            {
                string cleanedAddress = RemoveVietnameseSigns(address).Trim();
                if (!cleanedAddress.ToLower().Contains("vietnam") && !cleanedAddress.ToLower().Contains("viet nam"))
                    cleanedAddress += ", Vietnam";

                _sharedHttpClient.DefaultRequestHeaders.Remove("User-Agent");
                _sharedHttpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "TapFoodApp");
                var url = $"https://api.openrouteservice.org/geocode/search?api_key={ORS_API_KEY}&text={Uri.EscapeDataString(cleanedAddress)}&size=1";
                var response = _sharedHttpClient.GetAsync(url).Result;

                if (response.IsSuccessStatusCode)
                {
                    var json = response.Content.ReadAsStringAsync().Result;
                    var obj = JObject.Parse(json);
                    var features = obj["features"] as JArray;
                    if (features != null && features.Count > 0)
                    {
                        var coords = features[0]["geometry"]["coordinates"];
                        return (coords[1].Value<double>(), coords[0].Value<double>());
                    }
                }
            }
            catch { }
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


        public ActionResult TrangChu(string sort = "default", double? lat = null, double? lng = null, string search = "")
        {
            if ((lat == null || lng == null) && Session["MaKH"] != null)
            {
                string maKH = Session["MaKH"] as string;
                var kh = db.KhachHangs.Find(maKH);
                if (kh != null)
                {
                    if ((kh.Latitude == null || kh.Latitude == 0) && !string.IsNullOrEmpty(kh.DiaChi))
                    {
                        var coords = GeoCodeORS(kh.DiaChi);
                        if (coords.lat.HasValue)
                        {
                            kh.Latitude = coords.lat; kh.Longitude = coords.lng; db.SaveChanges();
                        }
                    }
                    lat = kh.Latitude; lng = kh.Longitude;
                }
            }

            var nhaHangData = LoadNhaHangData(lat, lng);

            if (!string.IsNullOrEmpty(search))
            {
                string keyword = RemoveVietnameseSigns(search).ToLower().Trim();
                var searchResult = nhaHangData.Where(x => RemoveVietnameseSigns(x.TenNH).ToLower().Contains(keyword) || RemoveVietnameseSigns(x.DiaChi).ToLower().Contains(keyword)).ToList();

                var maNHHoatDong = nhaHangData.Select(nh => nh.MaNH).ToList();
                var monAns = db.MonAns.Where(m => maNHHoatDong.Contains(m.MaNH)).ToList();
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
                // Lấy 5 đơn hàng gần nhất để phân tích danh mục (giảm tải memory thay vì lấy toàn bộ ChiTietDonHangs)
                var recentOrders = db.ChiTietDonHangs
                    .Where(c => c.DonHang.MaKH == maKH)
                    .OrderByDescending(c => c.DonHang.ThoiGianDat)
                    .Take(20)
                    .ToList();

                var topCategory = recentOrders
                    .GroupBy(c => c.MonAn.MaLoai)
                    .OrderByDescending(g => g.Count())
                    .Select(g => g.Key)
                    .FirstOrDefault();

                if (topCategory != null)
                {
                    var maNHList = db.MonAns.Where(m => m.MaLoai == topCategory).Select(m => m.MaNH).Distinct().ToList();
                    // Sử dụng lại nhaHangData đã lấy ở trên, tránh gọi LoadNhaHangData() lần 2
                    recommendedNhaHang = nhaHangData
                        .Where(n => maNHList.Contains(n.MaNH))
                        .OrderByDescending(n => n.Score)
                        .Take(4)
                        .ToList();
                }
            }

            var model = new TrangChuViewModel
            {
                DanhMuc = db.LoaiMonAns.Select(x => new LoaiMonAnViewModel
                {
                    MaLoai = x.MaLoai,
                    TenLoai = x.TenLoai,
                    HinhAnh = x.HinhAnh
                }).ToList().Select(x => new LoaiMonAnViewModel
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
        public ActionResult GetHomeData(string sort = "default", double? lat = null, double? lng = null, string search = "")
        {
            if ((lat == null || lng == null) && Session["MaKH"] != null)
            {
                string maKH = Session["MaKH"] as string;
                var kh = db.KhachHangs.Find(maKH);
                if (kh != null)
                {
                    lat = kh.Latitude; lng = kh.Longitude;
                }
            }

            var nhaHangData = LoadNhaHangData(lat, lng);

            if (!string.IsNullOrEmpty(search))
            {
                string keyword = RemoveVietnameseSigns(search).ToLower().Trim();
                var searchResult = nhaHangData.Where(x => RemoveVietnameseSigns(x.TenNH).ToLower().Contains(keyword) || RemoveVietnameseSigns(x.DiaChi).ToLower().Contains(keyword)).ToList();

                var maNHHoatDong = nhaHangData.Select(nh => nh.MaNH).ToList();
                var monAns = db.MonAns.Where(m => maNHHoatDong.Contains(m.MaNH)).ToList();
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
                var recentOrders = db.ChiTietDonHangs
                    .Where(c => c.DonHang.MaKH == maKH)
                    .OrderByDescending(c => c.DonHang.ThoiGianDat)
                    .Take(20)
                    .ToList();

                var topCategory = recentOrders
                    .GroupBy(c => c.MonAn.MaLoai)
                    .OrderByDescending(g => g.Count())
                    .Select(g => g.Key)
                    .FirstOrDefault();

                if (topCategory != null)
                {
                    var maNHList = db.MonAns.Where(m => m.MaLoai == topCategory).Select(m => m.MaNH).Distinct().ToList();
                    recommendedNhaHang = nhaHangData
                        .Where(n => maNHList.Contains(n.MaNH))
                        .OrderByDescending(n => n.Score)
                        .Take(4)
                        .ToList();
                }
            }

            string htmlRecommended = "";
            string htmlNhaHang = "";

            if (recommendedNhaHang != null && recommendedNhaHang.Any())
            {
                htmlRecommended = RenderPartialViewToString("_NhaHangNoiBatPartial", recommendedNhaHang);
            }
            htmlNhaHang = RenderPartialViewToString("_NhaHangNoiBatPartial", nhaHangData);

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

        public ActionResult DanhMuc()
        {

            var danhMucList = db.LoaiMonAns.ToList().Select(x => new LoaiMonAnViewModel
            {
                MaLoai = x.MaLoai,
                TenLoai = x.TenLoai,
                HinhAnh = string.IsNullOrEmpty(x.HinhAnh) ? GetImageNameByMaLoai(x.MaLoai, x.TenLoai) : x.HinhAnh
            }).ToList();

            return View(danhMucList);
        }

        public ActionResult NhaHang(string sort = "default", double? lat = null, double? lng = null)
        {
            if ((lat == null || lng == null) && Session["MaKH"] != null)
            {
                string maKH = Session["MaKH"] as string;
                var kh = db.KhachHangs.Find(maKH);
                if (kh != null) { lat = kh.Latitude; lng = kh.Longitude; }
            }
            var nhaHangData = LoadNhaHangData(lat, lng);
            nhaHangData = ApplySort(nhaHangData, sort);
            ViewBag.CurrentSort = sort;
            return View(nhaHangData);
        }

        public ActionResult _NhaHangNoiBatPartial(List<NhaHangViewModel> data = null)
        {
            if (data != null) return PartialView(data);
            var defaultData = LoadNhaHangData().OrderByDescending(x => x.Score).Take(8).ToList();
            return PartialView(defaultData);
        }

        public ActionResult _DanhMucPartial()
        {
            var danhMucList = db.LoaiMonAns.ToList().Select(x => new LoaiMonAnViewModel
            {
                MaLoai = x.MaLoai,
                TenLoai = x.TenLoai,
                HinhAnh = string.IsNullOrEmpty(x.HinhAnh) ? GetImageNameByMaLoai(x.MaLoai, x.TenLoai) : x.HinhAnh
            }).ToList();
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
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password)) return Json(new { success = false, message = "Vui lòng nhập đầy đủ thông tin." });
            var tk = db.TaiKhoans.FirstOrDefault(x => x.TenDangNhap == username && x.MatKhau == password);
            if (tk == null) return Json(new { success = false, message = "Sai tên đăng nhập hoặc mật khẩu." });
            Session["TaiKhoan"] = tk;
            if (tk.VaiTro == "KhachHang")
            {
                var maKH = db.KhachHangs.FirstOrDefault(k => k.MaTK == tk.MaTK)?.MaKH;
                if (!string.IsNullOrEmpty(maKH)) Session["MaKH"] = maKH;
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

        private List<NhaHangViewModel> LoadNhaHangData(double? userLat = null, double? userLng = null)
        {

            var nhaHangList = db.NhaHangs
                .Include("TaiKhoan")
                .Where(nh => nh.TaiKhoan != null && nh.TaiKhoan.TrangThai == true)
                .ToList();
            
            // Lấy dữ liệu thống kê từ SQL thay vì memory nếu có thể, tạm thời lấy những group cần thiết
            var danhGiaList = db.DanhGiaNhaHangs
                                .GroupBy(dg => dg.MaNH)
                                .Select(g => new { MaNH = g.Key, AvgRating = g.Average(d => d.SoSao) })
                                .ToList();
                                
            var luotMuaDict = db.DonHangs
                                .GroupBy(d => d.MaNH)
                                .Select(g => new { MaNH = g.Key, LuotMua = g.Select(d => d.MaDon).Distinct().Count() })
                                .ToList();
                                
            var maxLuotMua = luotMuaDict.Any() ? luotMuaDict.Max(l => l.LuotMua) : 1;

            return nhaHangList.Select(x =>
            {
                // BỎ GỌI GeoCodeORS() ĐỒNG BỘ Ở ĐÂY ĐỂ TRÁNH TREO WEB.
                // Việc cập nhật tọa độ nên làm ở thao tác của Admin/Nhà hàng khi họ đổi địa chỉ.
                
                double rating = danhGiaList.Where(dg => dg.MaNH == x.MaNH).Select(dg => (double?)(dg.AvgRating)).FirstOrDefault() ?? 0;
                int luotMua = luotMuaDict.Where(l => l.MaNH == x.MaNH).Select(l => l.LuotMua).FirstOrDefault();
                double score = (rating * 0.6) + (((double)luotMua / maxLuotMua) * 4);
                double distance = 999;
                
                if (userLat.HasValue && userLng.HasValue && x.Latitude.HasValue && x.Longitude.HasValue)
                    distance = CalculateDistance(userLat.Value, userLng.Value, x.Latitude.Value, x.Longitude.Value);

                return new NhaHangViewModel { MaNH = x.MaNH, TenNH = x.TenNH, DiaChi = x.DiaChi, TrangThai = x.TrangThai, HinhAnh = x.HinhAnh, Rating = Math.Round(rating, 1), TongLuotMua = luotMua, Score = score, KhoangCachKm = distance };
            }).ToList();
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
    }
}