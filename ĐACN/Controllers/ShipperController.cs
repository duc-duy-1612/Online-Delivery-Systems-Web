using ĐACN.Models;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using System.Web.Mvc;

namespace ĐACN.Controllers
{
    public class ShipperController : BaseController
    {
        private const string ORS_API_KEY = "eyJvcmciOiI1YjNjZTM1OTc4NTExMTAwMDFjZjYyNDgiLCJpZCI6ImFhZWYwMjY0NjIzZTRmNGU4MTE2NGQzYzlmZjJkYTYxIiwiaCI6Im11cm11cjY0In0=";



        private bool IsStreetNameMatching(string inputStreet, string apiStreet, string apiName)
        {
            string normInput = RemoveVietnameseSigns(inputStreet);
            string normApiStreet = RemoveVietnameseSigns(apiStreet);
            string normApiName = RemoveVietnameseSigns(apiName);

            if (!string.IsNullOrEmpty(normApiStreet) && normInput.Contains(normApiStreet)) return true;
            if (!string.IsNullOrEmpty(normApiName) && normInput.Contains(normApiName)) return true;
            if (!string.IsNullOrEmpty(normApiName) && normApiName.Contains(normInput)) return true;

            return false;
        }





        private (double? lat, double? lng) GeoCodeORS(string address)
        {
            if (string.IsNullOrEmpty(address)) return (null, null);
            try
            {

                string street = address.Split(',')[0];
                var res = ValidateAddressRealtime(street, address);
                if (res.isValid) return (res.lat, res.lng);
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("GeoCodeORS Error: " + ex.Message); }
            return (null, null);
        }





        private double CalculateHaversineDistance(double lat1, double lon1, double lat2, double lon2)
        {
            const double R = 6371e3;
            var phi1 = lat1 * Math.PI / 180;
            var phi2 = lat2 * Math.PI / 180;
            var deltaPhi = (lat2 - lat1) * Math.PI / 180;
            var deltaLambda = (lon2 - lon1) * Math.PI / 180;
            var a = Math.Sin(deltaPhi / 2) * Math.Sin(deltaPhi / 2) + Math.Cos(phi1) * Math.Cos(phi2) * Math.Sin(deltaLambda / 2) * Math.Sin(deltaLambda / 2);
            var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            return R * c;
        }

        private dynamic GetRouteDataORS(double startLat, double startLng, double endLat, double endLng)
        {
            try
            {
                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Add("Authorization", ORS_API_KEY);
                    string startParam = $"{startLng.ToString(CultureInfo.InvariantCulture)},{startLat.ToString(CultureInfo.InvariantCulture)}";
                    string endParam = $"{endLng.ToString(CultureInfo.InvariantCulture)},{endLat.ToString(CultureInfo.InvariantCulture)}";
                    var url = $"https://api.openrouteservice.org/v2/directions/driving-car?start={startParam}&end={endParam}";

                    var response = client.GetAsync(url).Result;
                    if (!response.IsSuccessStatusCode) return null;

                    var json = response.Content.ReadAsStringAsync().Result;
                    var obj = JObject.Parse(json);
                    var features = obj["features"] as JArray;
                    if (features == null || features.Count == 0) return null;

                    var coords = features[0]["geometry"]["coordinates"];
                    var summary = features[0]["properties"]["summary"];
                    var routeList = new List<object>();
                    foreach (var c in coords) routeList.Add(new { lat = c[1].Value<double>(), lng = c[0].Value<double>() });

                    return new { route = routeList, distance = summary["distance"].Value<double>(), duration = summary["duration"].Value<double>() };
                }
            }
            catch { return null; }
        }

        private dynamic GetRouteDataOSRM(double startLat, double startLng, double endLat, double endLng)
        {
            try
            {
                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Add("User-Agent", "TapFoodDeliveryApp/1.0");
                    string coordinates = $"{startLng.ToString(CultureInfo.InvariantCulture)},{startLat.ToString(CultureInfo.InvariantCulture)};{endLng.ToString(CultureInfo.InvariantCulture)},{endLat.ToString(CultureInfo.InvariantCulture)}";
                    string url = $"http://router.project-osrm.org/route/v1/driving/{coordinates}?overview=full&geometries=geojson";
                    client.Timeout = TimeSpan.FromSeconds(5);
                    var response = client.GetAsync(url).Result;
                    if (!response.IsSuccessStatusCode) return null;

                    var json = response.Content.ReadAsStringAsync().Result;
                    var obj = JObject.Parse(json);
                    if (obj["routes"] == null || !obj["routes"].Any()) return null;

                    var routeData = obj["routes"][0];
                    var geometry = routeData["geometry"]["coordinates"];
                    var distance = routeData["distance"].Value<double>();
                    var duration = routeData["duration"].Value<double>();

                    var routePoints = new List<object>();
                    foreach (var point in geometry)
                    {
                        routePoints.Add(new { lat = point[1].Value<double>(), lng = point[0].Value<double>() });
                    }

                    return new { route = routePoints, distance = distance, duration = duration };
                }
            }
            catch { return null; }
        }

        private List<object> GenerateManhattanRoute(double startLat, double startLng, double endLat, double endLng)
        {
            var route = new List<object>();
            route.Add(new { lat = startLat, lng = startLng });
            route.Add(new { lat = startLat, lng = endLng });
            route.Add(new { lat = endLat, lng = endLng });
            return route;
        }

        private dynamic GetRouteWithFallback(double startLat, double startLng, double endLat, double endLng)
        {
            var routeData = GetRouteDataORS(startLat, startLng, endLat, endLng);
            if (routeData != null) return routeData;

            routeData = GetRouteDataOSRM(startLat, startLng, endLat, endLng);
            if (routeData != null) return routeData;

            double distanceMeters = CalculateHaversineDistance(startLat, startLng, endLat, endLng);
            var routeGeometry = GenerateManhattanRoute(startLat, startLng, endLat, endLng);
            double averageSpeedKmH = 30.0;
            double durationSeconds = ((distanceMeters / 1000.0) / averageSpeedKmH) * 3600;

            return new { route = routeGeometry, distance = distanceMeters, duration = durationSeconds };
        }




        private bool KiemTraDangNhap()
        {
            var tk = Session["TaiKhoan"] as TaiKhoan;
            if (tk == null || tk.VaiTro != "Shipper" || tk.TrangThai != true) return false;

            if (Session["MaShipper"] == null)
            {
                var shipper = db.Shippers.FirstOrDefault(s => s.MaTK == tk.MaTK);
                if (shipper != null) Session["MaShipper"] = shipper.MaShipper;
            }

            return Session["MaShipper"] != null;
        }

        private Shipper LayShipper()
        {
            string maShipper = Session["MaShipper"] as string;
            if (string.IsNullOrEmpty(maShipper)) return null;

            return db.Shippers.Include("TaiKhoan").FirstOrDefault(s => s.MaShipper == maShipper);
        }



        [HttpGet]
        public ActionResult Login()
        {
            if (KiemTraDangNhap()) return RedirectToAction("Index");
            return RedirectToAction("Login", "Account");
        }



        public ActionResult Index()
        {
            Response.ContentEncoding = Encoding.UTF8;
            Response.Charset = "utf-8";

            if (!KiemTraDangNhap())
            {
                TempData["Message"] = "Vui lòng đăng nhập để tiếp tục.";
                return RedirectToAction("Login", "Account");
            }

            var danhSachMaDon = db.DonHangs
                .Where(d => string.IsNullOrEmpty(d.MaShipper) && d.TrangThai != "Đã hủy" && d.TrangThai != "Hủy")
                .Select(d => d.MaDon)
                .ToList();

            var chiTietMonAn = (from ctdh in db.ChiTietDonHangs
                                join mon in db.MonAns on ctdh.MaMon equals mon.MaMon
                                where danhSachMaDon.Contains(ctdh.MaDon)
                                select new
                                {
                                    ctdh.MaDon,
                                    mon.TenMon,
                                    ctdh.SoLuong
                                }).ToList();

            var orders = (from d in db.DonHangs
                          where string.IsNullOrEmpty(d.MaShipper)
                          join kh in db.KhachHangs on d.MaKH equals kh.MaKH into khGroup
                          from kh in khGroup.DefaultIfEmpty()
                          join nh in db.NhaHangs on d.MaNH equals nh.MaNH into nhGroup
                          from nh in nhGroup.DefaultIfEmpty()
                          select new OrderCardViewModel
                          {
                              MaDon = d.MaDon,
                              TongTien = d.TongTien,
                              ShipFee = d.ShipFee ?? 0,
                              TenNhaHang = nh.TenNH ?? "N/A",
                              DiaChiNhaHang = nh.DiaChi ?? "N/A",
                              TenKhachHang = kh.TenKH ?? "N/A",
                              DiaChiKhachHang = d.DiaChiGiaoHang,
                              TrangThai = d.TrangThai,
                              ThoiGianDat = d.ThoiGianDat,
                              SDTNhaHang = nh.SDT ?? "",
                              SDTKhachHang = d.SDTGiaoHang,
                              SoLuongMon = 0,
                              DanhSachMonTomTat = ""
                          })
                              .OrderByDescending(d => d.ThoiGianDat)
                              .ToList();

            foreach (var order in orders)
            {
                var monAnTrongDon = chiTietMonAn.Where(m => m.MaDon == order.MaDon).ToList();
                order.SoLuongMon = monAnTrongDon.Sum(m => m.SoLuong ?? 0);
                order.DanhSachMonTomTat = string.Join(", ", monAnTrongDon
                    .GroupBy(m => m.TenMon)
                    .Select(g => $"{g.Key} x{g.Sum(x => x.SoLuong ?? 0)}")
                    .Take(3));
            }

            return View(orders);
        }



        public ActionResult Accepted()
        {
            Response.ContentEncoding = Encoding.UTF8;
            Response.Charset = "utf-8";

            if (!KiemTraDangNhap())
            {
                TempData["Message"] = "Vui lòng đăng nhập để tiếp tục.";
                return RedirectToAction("Login", "Account");
            }

            var shipper = LayShipper();
            if (shipper == null)
            {
                TempData["Message"] = "Không tìm thấy thông tin shipper.";
                return RedirectToAction("Login", "Account");
            }

            var danhSachMaDon = db.DonHangs
                .Where(d => d.MaShipper == shipper.MaShipper)
                .Select(d => d.MaDon)
                .ToList();

            var chiTietMonAnData = (from ctdh in db.ChiTietDonHangs
                                    join mon in db.MonAns on ctdh.MaMon equals mon.MaMon
                                    where danhSachMaDon.Contains(ctdh.MaDon)
                                    select new
                                    {
                                        ctdh.MaDon,
                                        MaMon = mon.MaMon,
                                        TenMon = mon.TenMon,
                                        SoLuong = ctdh.SoLuong ?? 0,
                                        DonGia = ctdh.DonGia ?? 0,
                                        HinhAnh = mon.HinhAnh,
                                        MoTa = mon.MoTa
                                    }).ToList();

            var ordersTemp = (from d in db.DonHangs
                              where d.MaShipper == shipper.MaShipper
                              join kh in db.KhachHangs on d.MaKH equals kh.MaKH into khGroup
                              from kh in khGroup.DefaultIfEmpty()
                              join nh in db.NhaHangs on d.MaNH equals nh.MaNH into nhGroup
                              from nh in nhGroup.DefaultIfEmpty()
                              select new AcceptedOrderView
                              {
                                  MaDon = d.MaDon,
                                  TongTien = d.TongTien,
                                  ShipFee = d.ShipFee ?? 0,
                                  TenNhaHang = nh.TenNH ?? "N/A",
                                  DiaChiNhaHang = nh.DiaChi ?? "N/A",
                                  TenKhachHang = kh.TenKH ?? "N/A",
                                  DiaChiKhachHang = d.DiaChiGiaoHang,
                                  SDTKhachHang = d.SDTGiaoHang,
                                  TrangThai = d.TrangThai,
                                  ThoiGianDat = d.ThoiGianDat,
                                  SDTNhaHang = nh.SDT ?? "",
                                  DiaChi = d.DiaChiGiaoHang,
                                  Sdt = d.SDTGiaoHang,
                                  SoLuongMon = 0,
                                  DanhSachMonTomTat = ""
                              }).ToList();

            var orders = ordersTemp
                .Where(o => string.IsNullOrEmpty(o.TrangThai) || !o.TrangThai.Equals("Hoàn thành", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(d => d.ThoiGianDat)
                .ToList();

            foreach (var order in orders)
            {
                var monAnTrongDon = chiTietMonAnData.Where(m => m.MaDon == order.MaDon).ToList();
                order.SoLuongMon = monAnTrongDon.Sum(m => m.SoLuong);
                order.DanhSachMonTomTat = string.Join(", ", monAnTrongDon
                    .GroupBy(m => m.TenMon)
                    .Select(g => $"{g.Key} x{g.Sum(x => x.SoLuong)}")
                    .Take(3));

                order.ChiTietMonAn = monAnTrongDon.Select(x => new ChiTietMonAnViewModel
                {
                    MaMon = x.MaMon,
                    TenMon = x.TenMon,
                    SoLuong = x.SoLuong,
                    DonGia = x.DonGia,
                    ThanhTien = x.SoLuong * x.DonGia,
                    HinhAnh = x.HinhAnh,
                    MoTa = x.MoTa
                }).ToList();
            }

            return View(orders);
        }



        [HttpGet]
        public ActionResult Details(string maDon)
        {
            Response.ContentEncoding = Encoding.UTF8;
            Response.Charset = "utf-8";

            if (!KiemTraDangNhap())
            {
                TempData["Message"] = "Vui lòng đăng nhập để xem chi tiết đơn hàng.";
                return RedirectToAction("Login", "Account");
            }

            if (string.IsNullOrWhiteSpace(maDon))
                return RedirectToAction("Index");

            var shipper = LayShipper();
            if (shipper == null)
            {
                TempData["Message"] = "Không tìm thấy thông tin shipper.";
                return RedirectToAction("Login", "Account");
            }

            var donHangInfo = db.DonHangs
                                 .Where(d => d.MaDon == maDon)
                                 .Select(d => new {
                                     d.MaDon,
                                     d.MaKH,
                                     d.MaNH,
                                     d.MaShipper,
                                     d.TrangThai,
                                     d.TongTien,
                                     d.ShipFee,
                                     d.ThoiGianDat,
                                     d.DiaChiGiaoHang,
                                     d.SDTGiaoHang,

                                     d.Latitude,
                                     d.Longitude
                                 })
                                 .FirstOrDefault();

            if (donHangInfo == null) return HttpNotFound();

            if (!string.IsNullOrEmpty(donHangInfo.MaShipper) && donHangInfo.MaShipper != shipper.MaShipper)
            {
                TempData["Message"] = "Bạn không có quyền xem đơn hàng này.";
                return RedirectToAction("Index");
            }


            var nhaHang = db.NhaHangs.FirstOrDefault(nh => nh.MaNH == donHangInfo.MaNH);
            var khachHang = db.KhachHangs.FirstOrDefault(kh => kh.MaKH == donHangInfo.MaKH);


            if (nhaHang != null && (nhaHang.Latitude == null || nhaHang.Latitude == 0))
            {
                var nhLoc = GeoCodeORS(nhaHang.DiaChi);
                if (nhLoc.lat.HasValue)
                {
                    nhaHang.Latitude = nhLoc.lat.Value;
                    nhaHang.Longitude = nhLoc.lng.Value;
                    db.Entry(nhaHang).State = EntityState.Modified;
                    db.SaveChanges();
                }
            }


            if ((donHangInfo.Latitude == null || donHangInfo.Latitude == 0) && !string.IsNullOrEmpty(donHangInfo.DiaChiGiaoHang))
            {
                var donHangObj = db.DonHangs.FirstOrDefault(d => d.MaDon == maDon);
                if (donHangObj != null)
                {

                    if (khachHang != null && khachHang.Latitude.HasValue && khachHang.Latitude != 0)
                    {
                        donHangObj.Latitude = khachHang.Latitude;
                        donHangObj.Longitude = khachHang.Longitude;
                    }
                    else
                    {

                        var khLoc = GeoCodeORS(donHangInfo.DiaChiGiaoHang);
                        if (khLoc.lat.HasValue)
                        {
                            donHangObj.Latitude = khLoc.lat.Value;
                            donHangObj.Longitude = khLoc.lng.Value;
                        }
                    }
                    db.Entry(donHangObj).State = EntityState.Modified;
                    db.SaveChanges();
                }
            }

            var chiTietData = (from ctdh in db.ChiTietDonHangs
                               join mon in db.MonAns on ctdh.MaMon equals mon.MaMon
                               where ctdh.MaDon == maDon
                               select new
                               {
                                   MaMon = mon.MaMon,
                                   TenMon = mon.TenMon,
                                   SoLuong = ctdh.SoLuong ?? 0,
                                   DonGia = ctdh.DonGia ?? 0,
                                   HinhAnh = mon.HinhAnh,
                                   MoTa = mon.MoTa
                               }).ToList();

            var chiTietMonAn = chiTietData.Select(x => new ChiTietMonAnViewModel
            {
                MaMon = x.MaMon,
                TenMon = x.TenMon,
                SoLuong = x.SoLuong,
                DonGia = x.DonGia,
                ThanhTien = x.SoLuong * x.DonGia,
                HinhAnh = x.HinhAnh,
                MoTa = x.MoTa
            }).ToList();

            var donHang = new DonHangDetailsViewModel
            {
                MaDon = donHangInfo.MaDon,
                MaKH = donHangInfo.MaKH,
                MaNH = donHangInfo.MaNH,
                MaShipper = donHangInfo.MaShipper,
                TrangThai = donHangInfo.TrangThai ?? "Chờ xác nhận",
                TongTien = donHangInfo.TongTien,
                ShipFee = donHangInfo.ShipFee ?? 0,
                ThoiGianDat = donHangInfo.ThoiGianDat,
                KhachHang = new KhachHangInfoViewModel
                {
                    TenKH = khachHang != null ? (khachHang.TenKH ?? "N/A") : "N/A",
                    DiaChi = donHangInfo.DiaChiGiaoHang,
                    SDT = donHangInfo.SDTGiaoHang
                },
                NhaHang = nhaHang != null ? new NhaHangInfoViewModel
                {
                    TenNH = nhaHang.TenNH ?? "N/A",
                    DiaChi = nhaHang.DiaChi ?? "N/A",
                    SDT = nhaHang.SDT ?? "N/A"
                } : null,
                ChiTietMonAn = chiTietMonAn
            };

            return View(donHang);
        }



        [HttpPost]

        public ActionResult Accept(string maDon)
        {
            if (!KiemTraDangNhap())
            {
                TempData["Message"] = "Vui lòng đăng nhập để nhận đơn hàng.";
                return RedirectToAction("Login", "Account");
            }

            if (string.IsNullOrWhiteSpace(maDon)) return RedirectToAction("Index");

            var shipper = LayShipper();
            if (shipper == null)
            {
                TempData["Message"] = "Không tìm thấy thông tin shipper.";
                return RedirectToAction("Index");
            }

            var donDangXuLy = db.DonHangs
                .Where(d => d.MaShipper == shipper.MaShipper &&
                            d.TrangThai != "Hoàn thành" &&
                            d.TrangThai != "Đã hủy")
                .FirstOrDefault();

            if (donDangXuLy != null)
            {
                TempData["Message"] = $"Bạn đang có đơn hàng #{donDangXuLy.MaDon} đang xử lý. Vui lòng hoàn thành đơn này trước khi nhận đơn mới.";
                return RedirectToAction("Accepted");
            }

            var don = db.DonHangs.FirstOrDefault(d => d.MaDon == maDon);

            if (don != null && string.IsNullOrEmpty(don.MaShipper))
            {
                using (var transaction = db.Database.BeginTransaction())
                {
                    try
                    {
                        var donCheck = db.DonHangs.FirstOrDefault(d => d.MaDon == maDon && string.IsNullOrEmpty(d.MaShipper) && d.TrangThai != "Đã hủy" && d.TrangThai != "Hủy");
                        if (donCheck != null)
                        {
                            donCheck.MaShipper = shipper.MaShipper;
                            db.SaveChanges();
                            transaction.Commit();

                            // SignalR Notification
                            try
                            {
                                var context = Microsoft.AspNet.SignalR.GlobalHost.ConnectionManager.GetHubContext<ĐACN.Hubs.DeliveryHub>();
                                context.Clients.Group("NhaHang_" + donCheck.MaNH).notifyNewOrder($"Shipper {shipper.TenShipper} đã nhận giao đơn hàng {maDon}");
                                context.Clients.Group("KhachHang_" + donCheck.MaKH).notifyNewOrder($"Shipper {shipper.TenShipper} đang trên đường đến nhà hàng lấy đơn {maDon}");
                            }
                            catch { }

                            TempData["Message"] = "Bạn đã nhận đơn thành công.";
                        }
                        else
                        {
                            transaction.Rollback();
                            TempData["Message"] = "Đơn đã được nhận bởi shipper khác.";
                        }
                    }
                    catch (Exception ex)
                    {
                        transaction.Rollback();
                        TempData["Message"] = "Lỗi khi nhận đơn: " + ex.Message;
                    }
                }
            }
            else
            {
                TempData["Message"] = "Đơn đã được nhận bởi shipper khác hoặc không tồn tại.";
            }

            return RedirectToAction("Accepted");
        }

        [HttpPost]
        public ActionResult UpdateStatus(string maDon, string trangThai)
        {
            if (!KiemTraDangNhap())
            {
                TempData["Message"] = "Vui lòng đăng nhập để cập nhật trạng thái.";
                return RedirectToAction("Login", "Account");
            }

            if (string.IsNullOrWhiteSpace(maDon) || string.IsNullOrWhiteSpace(trangThai))
                return RedirectToAction("Accepted");

            var shipper = LayShipper();
            if (shipper == null)
            {
                TempData["Message"] = "Không tìm thấy thông tin shipper.";
                return RedirectToAction("Accepted");
            }

            var don = db.DonHangs.FirstOrDefault(d => d.MaDon == maDon && d.MaShipper == shipper.MaShipper);
            if (don == null)
            {
                TempData["Message"] = "Không thể cập nhật trạng thái.";
                return RedirectToAction("Accepted");
            }

            if (trangThai == "Đang giao" && don.TrangThai != "Đang lấy món")
            {
                TempData["Message"] = "Cập nhật thất bại: Đơn hàng phải ở trạng thái 'Đang lấy món' mới có thể chuyển sang 'Đang giao'.";
                return RedirectToAction("Accepted");
            }

            if (trangThai == "Hoàn thành" && don.TrangThai != "Đang giao")
            {
                TempData["Message"] = "Cập nhật thất bại: Đơn hàng phải ở trạng thái 'Đang giao' mới có thể 'Hoàn thành'.";
                return RedirectToAction("Accepted");
            }

            don.TrangThai = trangThai;

            if (trangThai.Equals("Hoàn thành", StringComparison.OrdinalIgnoreCase))
            {
                RealTimeLocationService.ClearLocation(shipper.MaShipper);
            }

            db.SaveChanges();

            // SignalR Notification
            try
            {
                var context = Microsoft.AspNet.SignalR.GlobalHost.ConnectionManager.GetHubContext<ĐACN.Hubs.DeliveryHub>();
                string msg = trangThai == "Đang giao" ? $"Đơn hàng {maDon} đang được giao đến bạn" : $"Đơn hàng {maDon} đã được giao thành công";
                context.Clients.Group("KhachHang_" + don.MaKH).notifyNewOrder(msg);
            }
            catch { }

            if (trangThai.Equals("Hoàn thành", StringComparison.OrdinalIgnoreCase))
            {
                TempData["OrderSuccess"] = "Đơn hàng đã được giao thành công!";
            }
            else
            {
                TempData["Message"] = "Cập nhật trạng thái thành công.";
            }

            return RedirectToAction("Accepted");
        }

        [HttpPost]
        public ActionResult Cancel(string maDon)
        {
            if (!KiemTraDangNhap())
            {
                TempData["Message"] = "Vui lòng đăng nhập để hủy đơn hàng.";
                return RedirectToAction("Login", "Account");
            }

            if (string.IsNullOrWhiteSpace(maDon))
                return RedirectToAction("Accepted");

            var shipper = LayShipper();
            if (shipper == null)
            {
                TempData["Message"] = "Không tìm thấy thông tin shipper.";
                return RedirectToAction("Accepted");
            }

            var don = db.DonHangs.FirstOrDefault(d => d.MaDon == maDon && d.MaShipper == shipper.MaShipper);
            if (don == null)
            {
                TempData["Message"] = "Không thể hủy đơn.";
                return RedirectToAction("Accepted");
            }

            don.MaShipper = null;
            don.TrangThai = "Chờ xác nhận";
            db.SaveChanges();

            RealTimeLocationService.ClearLocation(shipper.MaShipper);

            TempData["Message"] = "Đã hủy nhận đơn. Đơn đã quay lại danh sách chờ.";
            return RedirectToAction("Accepted");
        }



        [HttpPost]
        [OutputCache(NoStore = true, Duration = 0)]
        public ActionResult UpdateLocation(string latitude, string longitude)
        {
            if (!KiemTraDangNhap())
            {
                return Json(new { success = false, message = "Chưa đăng nhập." });
            }

            double lat, lng;
            bool isLatValid = double.TryParse(latitude, NumberStyles.Any, CultureInfo.InvariantCulture, out lat);
            bool isLngValid = double.TryParse(longitude, NumberStyles.Any, CultureInfo.InvariantCulture, out lng);

            if (!isLatValid || !isLngValid || (lat == 0 && lng == 0))
            {
                return Json(new { success = false, message = "Dữ liệu tọa độ không hợp lệ." });
            }

            var maShipper = Session["MaShipper"] as string;

            if (string.IsNullOrWhiteSpace(maShipper))
            {
                return Json(new { success = false, message = "Không tìm thấy Mã Shipper." });
            }


            RealTimeLocationService.UpdateLocation(maShipper, lat, lng);


            try
            {
                var shipper = db.Shippers.FirstOrDefault(s => s.MaShipper == maShipper);
                if (shipper != null)
                {
                    shipper.Latitude = lat;
                    shipper.Longitude = lng;
                    db.Entry(shipper).State = EntityState.Modified;
                }


                var activeOrders = db.DonHangs
                                             .Where(d => d.MaShipper == maShipper &&
                                                         (d.TrangThai == "Đang lấy món" || d.TrangThai == "Đang giao"))
                                             .ToList();

                if (activeOrders.Any())
                {
                    foreach (var order in activeOrders)
                    {
                        order.ShipperLatitude = lat;
                        order.ShipperLongitude = lng;
                        db.Entry(order).State = EntityState.Modified;
                    }
                }

                db.SaveChanges();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"DB Update Error: {ex.Message}");
            }

            return Json(new { success = true, maShipper = maShipper, latitude = lat, longitude = lng });
        }



        [HttpGet]
        public new ActionResult Profile()
        {
            Response.ContentEncoding = Encoding.UTF8;
            Response.Charset = "utf-8";

            if (!KiemTraDangNhap())
            {
                TempData["Message"] = "Vui lòng đăng nhập để xem thông tin cá nhân.";
                return RedirectToAction("Login", "Account");
            }

            var shipper = LayShipper();
            if (shipper == null)
            {
                TempData["Message"] = "Không tìm thấy thông tin shipper.";
                return RedirectToAction("Login", "Account");
            }

            var ratings = db.DanhGiaShippers
                                 .Where(dg => dg.MaShipper == shipper.MaShipper && dg.SoSao.HasValue)
                                 .Select(dg => dg.SoSao.Value)
                                 .ToList();

            double averageRating = 0;
            int soSaoLamTron = 0;

            if (ratings.Any())
            {
                averageRating = ratings.Average();
                var diemLamTron = Math.Round(averageRating, 1);
                soSaoLamTron = (int)Math.Round(averageRating);
                ViewBag.DiemDanhGia = diemLamTron;
            }
            else
            {
                ViewBag.DiemDanhGia = 0;
            }

            ViewBag.SoSaoLamTron = soSaoLamTron;
            ViewBag.SoLanDanhGia = ratings.Count();

            var tongThuNhap = db.DonHangs
                .Where(d => d.MaShipper == shipper.MaShipper && d.TrangThai == "Hoàn thành")
                .Sum(d => (decimal?)d.ShipFee) ?? 0;

            ViewBag.TongThuNhap = tongThuNhap;
            return View(shipper);
        }

        [HttpPost]
        public new ActionResult Profile(string tenShipper, string sdt, string bienSoXe, string username, string password, HttpPostedFileBase avatar)
        {
            if (!KiemTraDangNhap())
            {
                TempData["Message"] = "Vui lòng đăng nhập để cập nhật thông tin.";
                return RedirectToAction("Login", "Account");
            }

            var shipper = LayShipper();
            if (shipper == null)
            {
                TempData["Message"] = "Không tìm thấy thông tin shipper.";
                return RedirectToAction("Login", "Account");
            }

            if (!string.IsNullOrWhiteSpace(tenShipper)) shipper.TenShipper = tenShipper;
            if (!string.IsNullOrWhiteSpace(sdt)) shipper.SDT = sdt;
            if (!string.IsNullOrWhiteSpace(bienSoXe)) shipper.BienSoXe = bienSoXe;

            if (shipper.TaiKhoan != null)
            {
                if (!string.IsNullOrWhiteSpace(username)) shipper.TaiKhoan.TenDangNhap = username;
                if (!string.IsNullOrWhiteSpace(password)) shipper.TaiKhoan.MatKhau = password;
            }

            if (avatar != null && avatar.ContentLength > 0)
            {
                string errorMsg;
                if (!ValidateImageFile(avatar, out errorMsg))
                {
                    TempData["Message"] = errorMsg;
                    return RedirectToAction("Profile");
                }

                var ext = Path.GetExtension(avatar.FileName).ToLower();
                var fileName = Path.GetFileNameWithoutExtension(avatar.FileName) + "_" + DateTime.Now.Ticks + ext;
                var path = Path.Combine(Server.MapPath("~/images/shipper/"), fileName);

                if (!Directory.Exists(Path.GetDirectoryName(path)))
                    Directory.CreateDirectory(Path.GetDirectoryName(path));

                if (!string.IsNullOrEmpty(shipper.HinhAnh))
                {
                    var oldPath = Server.MapPath("~" + shipper.HinhAnh);
                    if (System.IO.File.Exists(oldPath)) System.IO.File.Delete(oldPath);
                }

                avatar.SaveAs(path);
                shipper.HinhAnh = "/images/shipper/" + fileName;
            }

            db.SaveChanges();
            db.Entry(shipper).Reload();
            if (shipper.TaiKhoan != null) db.Entry(shipper.TaiKhoan).Reload();
            Session["Shipper"] = shipper;

            TempData["Message"] = "Cập nhật thông tin thành công.";
            return RedirectToAction("Profile");
        }



        [HttpGet]
        public ActionResult Income()
        {
            if (!KiemTraDangNhap()) return RedirectToAction("Login", "Account");

            var shipper = LayShipper();
            if (shipper == null) return RedirectToAction("Login", "Account");

            var completedOrders = db.DonHangs
                .Where(d => d.MaShipper == shipper.MaShipper && d.TrangThai == "Hoàn thành")
                .Include("KhachHang").Include("NhaHang")
                .OrderByDescending(d => d.ThoiGianDat)
                .Select(d => new
                {
                    d.MaDon,
                    TongTien = d.TongTien,
                    ShipFee = d.ShipFee ?? 0,
                    d.ThoiGianDat,
                    TenKhachHang = d.KhachHang.TenKH,
                    TenNhaHang = d.NhaHang.TenNH
                }).ToList();

            var now = DateTime.Now;
            int diff = (7 + (now.DayOfWeek - DayOfWeek.Monday)) % 7;
            var startOfWeek = now.Date.AddDays(-diff);
            var endOfWeek = startOfWeek.AddDays(6);

            var weekOrders = completedOrders
                .Where(o => o.ThoiGianDat.HasValue &&
                             o.ThoiGianDat.Value.Date >= startOfWeek &&
                             o.ThoiGianDat.Value.Date <= endOfWeek)
                .ToList();
            var thuNhapTuan = weekOrders.Sum(o => o.ShipFee);
            var soDonTuan = weekOrders.Count;

            var monthOrders = completedOrders
                .Where(o => o.ThoiGianDat.HasValue &&
                             o.ThoiGianDat.Value.Month == now.Month &&
                             o.ThoiGianDat.Value.Year == now.Year)
                .ToList();
            var thuNhapThang = monthOrders.Sum(o => o.ShipFee);
            var soDonThang = monthOrders.Count;

            var yearOrders = completedOrders
                .Where(o => o.ThoiGianDat.HasValue &&
                             o.ThoiGianDat.Value.Year == now.Year)
                .ToList();
            var thuNhapNam = yearOrders.Sum(o => o.ShipFee);
            var soDonNam = yearOrders.Count;

            var tongThuNhap = completedOrders.Sum(d => d.ShipFee);
            var tongSoDon = completedOrders.Count;

            ViewBag.TongThuNhap = tongThuNhap;
            ViewBag.TongDonHoanThanh = tongSoDon;
            ViewBag.DonHoanThanh = completedOrders;

            ViewBag.ThuNhapTuan = thuNhapTuan;
            ViewBag.SoDonTuan = soDonTuan;
            ViewBag.ThuNhapThang = thuNhapThang;
            ViewBag.SoDonThang = soDonThang;
            ViewBag.ThuNhapNam = thuNhapNam;
            ViewBag.SoDonNam = soDonNam;
            ViewBag.TongSoDon = tongSoDon;
            ViewBag.StartOfWeek = startOfWeek;
            ViewBag.EndOfWeek = endOfWeek;

            return View();
        }

        [HttpGet]
        public ActionResult History(string week)
        {
            if (!KiemTraDangNhap()) return RedirectToAction("Login", "Account");

            var shipper = LayShipper();
            if (shipper == null) return RedirectToAction("Login", "Account");

            DateTime monday;
            if (!string.IsNullOrEmpty(week) && DateTime.TryParse(week, out DateTime weekDate))
            {
                int daysUntilMonday = ((int)DayOfWeek.Monday - (int)weekDate.DayOfWeek + 7) % 7;
                if (daysUntilMonday == 0 && weekDate.DayOfWeek != DayOfWeek.Monday) daysUntilMonday = 7;
                monday = weekDate.AddDays(-daysUntilMonday).Date;
            }
            else
            {
                DateTime today = DateTime.Now;
                int daysUntilMonday = ((int)DayOfWeek.Monday - (int)today.DayOfWeek + 7) % 7;
                if (daysUntilMonday == 0 && today.DayOfWeek != DayOfWeek.Monday) daysUntilMonday = 7;
                monday = today.AddDays(-daysUntilMonday).Date;
            }

            DateTime sunday = monday.AddDays(6).Date.AddDays(1).AddTicks(-1);

            var weekOrders = db.DonHangs
                .Where(d => d.MaShipper == shipper.MaShipper &&
                             d.ThoiGianDat.HasValue &&
                             d.ThoiGianDat.Value >= monday &&
                             d.ThoiGianDat.Value <= sunday &&
                             d.TrangThai == "Hoàn thành")
                .Select(d => new { d.MaDon, TongTien = d.TongTien ?? 0, ShipFee = d.ShipFee ?? 0, d.ThoiGianDat })
                .ToList();

            var ordersByDate = weekOrders
                .GroupBy(o => o.ThoiGianDat.Value.Date)
                .ToDictionary(g => g.Key, g => new { Count = g.Count(), Total = g.Sum(x => x.ShipFee) });

            ViewBag.Monday = monday;
            ViewBag.OrdersByDate = ordersByDate;

            return View();
        }



        [HttpGet]
        public JsonResult GetOrdersByDate(string date)
        {
            if (!KiemTraDangNhap()) return Json(new { success = false, message = "Vui lòng đăng nhập." }, JsonRequestBehavior.AllowGet);

            var shipper = LayShipper();
            if (shipper == null) return Json(new { success = false, message = "Không tìm thấy thông tin shipper." }, JsonRequestBehavior.AllowGet);

            if (!DateTime.TryParse(date, out DateTime selectedDate))
                return Json(new { success = false, message = "Ngày không hợp lệ." }, JsonRequestBehavior.AllowGet);

            DateTime startDate = selectedDate.Date;
            DateTime endDate = startDate.AddDays(1).AddTicks(-1);

            var orders = db.DonHangs
            .Where(d => d.MaShipper == shipper.MaShipper &&
                         d.ThoiGianDat.HasValue &&
                         d.ThoiGianDat.Value >= startDate &&
                         d.ThoiGianDat.Value <= endDate &&
                         d.TrangThai == "Hoàn thành")
            .Select(d => new
            {
                d.MaDon,
                TongTien = d.TongTien ?? 0,
                ShipFee = d.ShipFee ?? 0,
                ThoiGianDat = d.ThoiGianDat,
                TenKhachHang = d.KhachHang != null ? d.KhachHang.TenKH : "N/A",
                TenNhaHang = d.NhaHang != null ? d.NhaHang.TenNH : "N/A"
            })
            .OrderByDescending(d => d.ThoiGianDat)
            .ToList();

            return Json(new
            {
                success = true,
                orders = orders.Select(o => new
                {
                    o.MaDon,
                    o.TongTien,
                    ShipFee = o.ShipFee,
                    ThoiGianDat = o.ThoiGianDat?.ToString("dd/MM/yyyy HH:mm") ?? "",
                    o.TenKhachHang,
                    o.TenNhaHang,
                    DetailUrl = Url.Action("Details", "Shipper", new { maDon = o.MaDon })
                })
            }, JsonRequestBehavior.AllowGet);
        }

        [HttpGet]
        [OutputCache(NoStore = true, Duration = 0)]
        public JsonResult GetLocation(string maDon)
        {
            if (!KiemTraDangNhap())
                return Json(new { success = false, message = "Chưa đăng nhập" }, JsonRequestBehavior.AllowGet);

            var shipper = LayShipper();
            if (shipper == null)
                return Json(new { success = false, message = "Không tìm thấy shipper" }, JsonRequestBehavior.AllowGet);

            var don = db.DonHangs.FirstOrDefault(x => x.MaDon == maDon && x.MaShipper == shipper.MaShipper);
            if (don == null)
                return Json(new { success = false, message = "Không tìm thấy đơn hàng hoặc không có quyền truy cập" }, JsonRequestBehavior.AllowGet);

            return Json(new { success = true, latitude = don.ShipperLatitude, longitude = don.ShipperLongitude }, JsonRequestBehavior.AllowGet);
        }


        [HttpGet]
        [OutputCache(NoStore = true, Duration = 0)]
        public JsonResult GetShipperRoute(string maDon)
        {
            if (!KiemTraDangNhap())
                return Json(new { success = false, message = "Chưa đăng nhập" }, JsonRequestBehavior.AllowGet);

            var shipper = LayShipper();
            if (shipper == null)
                return Json(new { success = false, message = "Không tìm thấy shipper" }, JsonRequestBehavior.AllowGet);


            var donHang = db.DonHangs
                                 .Include(d => d.NhaHang)
                                 .Include(d => d.KhachHang)
                                 .FirstOrDefault(d => d.MaDon == maDon && d.MaShipper == shipper.MaShipper);

            if (donHang == null)
                return Json(new { success = false, message = "Không tìm thấy đơn hàng hoặc không có quyền truy cập" }, JsonRequestBehavior.AllowGet);


            double shipperLat = 0, shipperLng = 0;

            var shipperLoc = RealTimeLocationService.GetLocation(shipper.MaShipper);
            if (shipperLoc != null)
            {
                shipperLat = shipperLoc.Latitude;
                shipperLng = shipperLoc.Longitude;
            }
            else
            {

                if (shipper.Latitude.HasValue && shipper.Latitude != 0)
                {
                    shipperLat = shipper.Latitude.Value;
                    shipperLng = shipper.Longitude ?? 0;
                }
            }


            double resLat = 0, resLng = 0;
            if (donHang.NhaHang != null)
            {
                if (donHang.NhaHang.Latitude.HasValue && donHang.NhaHang.Latitude != 0)
                {
                    resLat = donHang.NhaHang.Latitude.Value;
                    resLng = donHang.NhaHang.Longitude ?? 0;
                }
                else if (!string.IsNullOrWhiteSpace(donHang.NhaHang.DiaChi))
                {
                    var c = GeoCodeORS(donHang.NhaHang.DiaChi);
                    if (c.lat.HasValue)
                    {
                        resLat = c.lat.Value;
                        resLng = c.lng.Value;
                    }
                }
            }


            double cusLat = 0, cusLng = 0;

            if (donHang.Latitude.HasValue && donHang.Latitude != 0)
            {
                cusLat = donHang.Latitude.Value;
                cusLng = donHang.Longitude ?? 0;
            }

            else if (donHang.KhachHang != null && donHang.KhachHang.Latitude.HasValue && donHang.KhachHang.Latitude != 0)
            {
                cusLat = donHang.KhachHang.Latitude.Value;
                cusLng = donHang.KhachHang.Longitude ?? 0;
            }

            else if (!string.IsNullOrWhiteSpace(donHang.DiaChiGiaoHang))
            {
                var c = GeoCodeORS(donHang.DiaChiGiaoHang);
                if (c.lat.HasValue)
                {
                    cusLat = c.lat.Value;
                    cusLng = c.lng.Value;
                }
            }



            if (shipperLat == 0 || shipperLng == 0)
            {
                // Nếu không tìm thấy tọa độ hiện tại, giả sử shipper đang ở Nhà hàng (chỉ để tính route)
                shipperLat = resLat;
                shipperLng = resLng;
            }

            double startLat = shipperLat;
            double startLng = shipperLng;
            double endLat = 0, endLng = 0;

            string status = (donHang.TrangThai ?? "").ToLower();
            string routeType = "";

            if (status.Contains("lấy món") || status.Contains("chờ"))
            {

                routeType = "ToRestaurant";
                endLat = resLat;
                endLng = resLng;
            }
            else if (status.Contains("đang giao"))
            {

                routeType = "ToCustomer";
                endLat = cusLat;
                endLng = cusLng;
            }


            if (endLat == 0 || endLng == 0)
                return Json(new { success = false, message = "Không thể xác định tọa độ điểm đến (Nhà hàng hoặc Khách hàng thiếu địa chỉ)." }, JsonRequestBehavior.AllowGet);


            var routeData = GetRouteWithFallback(startLat, startLng, endLat, endLng);

            if (routeData == null)
                return Json(new { success = false, message = "Không thể tính toán đường đi." }, JsonRequestBehavior.AllowGet);


            double distanceMeters = 0;
            object routeGeometry = null;

            try
            {
                distanceMeters = (double)routeData.GetType().GetProperty("distance").GetValue(routeData, null);
                routeGeometry = routeData.GetType().GetProperty("route").GetValue(routeData, null);
            }
            catch
            {
                return Json(new { success = false, message = "Lỗi xử lý dữ liệu route." }, JsonRequestBehavior.AllowGet);
            }

            double distanceKm = Math.Round(distanceMeters / 1000.0, 1);
            double averageSpeedKmH = 30.0;
            double estimatedMinutes = Math.Ceiling(((distanceMeters / 1000.0) / averageSpeedKmH) * 60);
            if (estimatedMinutes < 1) estimatedMinutes = 1;

            string statusText = routeType == "ToRestaurant" ? "Đang đến nhà hàng" : "Đang giao đến khách hàng";

            return Json(new
            {
                success = true,
                route = routeGeometry,
                distanceText = $"{distanceKm} km",
                durationText = $"{estimatedMinutes} phút",
                statusText = statusText,
                routeType = routeType,
                shipperPoint = new { lat = shipperLat, lng = shipperLng },
                restaurantPoint = new { lat = resLat, lng = resLng },
                customerPoint = new { lat = cusLat, lng = cusLng }
            }, JsonRequestBehavior.AllowGet);
        }



        public ActionResult Logout()
        {
            var maShipper = Session["MaShipper"] as string;
            if (!string.IsNullOrEmpty(maShipper))
            {
                RealTimeLocationService.ClearLocation(maShipper);
            }

            Session.Clear();

            if (Request.Cookies["TapFoodLoginIP"] != null)
            {
                var c = new HttpCookie("TapFoodLoginIP") { Expires = DateTime.Now.AddDays(-1) };
                Response.Cookies.Add(c);
            }

            if (Request.Cookies["TapFoodUser"] != null)
            {
                var c = new HttpCookie("TapFoodUser") { Expires = DateTime.Now.AddDays(-1) };
                Response.Cookies.Add(c);
            }

            return RedirectToAction("Login", "Account");
        }

    }
}