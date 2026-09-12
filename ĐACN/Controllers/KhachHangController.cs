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
    public class KhachHangController : BaseController
    {
        private const string ORS_API_KEY = "eyJvcmciOiI1YjNjZTM1OTc4NTExMTAwMDFjZjYyNDgiLCJpZCI6ImFhZWYwMjY0NjIzZTRmNGU4MTE2NGQzYzlmZjJkYTYxIiwiaCI6Im11cm11cjY0In0=";

        private const double MAX_DELIVERY_RADIUS = 30.0;


        private bool KiemTraDangNhap()
        {
            return Session["MaKH"] != null;
        }

        private void XoaLichSuQuaHan()
        {
            DateTime han = DateTime.Now.AddDays(-5);
            var oldItems = db.LichSuGioHangs.Where(x => x.ThoiGianChon < han).ToList();
            if (oldItems.Any()) { db.LichSuGioHangs.RemoveRange(oldItems); db.SaveChanges(); }
        }






        private (double? lat, double? lng) GeoCodeORS(string address)
        {

            string street = address.Split(',')[0];
            var res = ValidateAddressRealtime(street, address);
            if (res.isValid) return (res.lat, res.lng);
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
                    var url = $"https://api.openrouteservice.org/v2/directions/driving-car?start={startParam}&end={endParam}&preference=fastest";

                    var response = client.GetAsync(url).Result;
                    if (!response.IsSuccessStatusCode) return null;

                    var json = response.Content.ReadAsStringAsync().Result;
                    var obj = JObject.Parse(json);
                    var features = obj["features"] as JArray;
                    if (features == null || features.Count == 0) return null;

                    var geometry = features[0]["geometry"]["coordinates"];
                    var summary = features[0]["properties"]["summary"];

                    var routePoints = new List<object>();
                    foreach (var point in geometry)
                    {
                        routePoints.Add(new { lat = point[1].Value<double>(), lng = point[0].Value<double>() });
                    }

                    return new
                    {
                        route = routePoints,
                        distance = summary["distance"].Value<double>(),
                        duration = summary["duration"].Value<double>()
                    };
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

                    var routePoints = new List<object>();
                    foreach (var point in geometry)
                    {
                        routePoints.Add(new { lat = point[1].Value<double>(), lng = point[0].Value<double>() });
                    }

                    return new { route = routePoints, distance = routeData["distance"].Value<double>(), duration = routeData["duration"].Value<double>() };
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





        private decimal TinhPhiShipMoi(double distanceInMeters)
        {
            double distanceKm = Math.Round(distanceInMeters / 1000.0, 1);
            decimal baseFee = 15000m;

            if (distanceKm <= 3)
            {
                return baseFee;
            }
            else
            {
                double extraKm = distanceKm - 3;
                decimal extraFee = (decimal)Math.Round(extraKm * 3000);
                return baseFee + extraFee;
            }
        }


        private decimal TinhPhiDichVu()
        {
            int currentHour = DateTime.Now.Hour;
            if (currentHour >= 19)
            {
                return 20000m;
            }
            return 16000m;
        }




        public ActionResult XemMenu(string id)
        {
            if (!KiemTraDangNhap()) { TempData["Msg"] = "Vui lòng đăng nhập!"; return RedirectToAction("TrangChu", "Home"); }
            var nhaHang = db.NhaHangs.Include("TaiKhoan").FirstOrDefault(n => n.MaNH == id);
            if (nhaHang == null) return HttpNotFound();

            if (nhaHang.TaiKhoan == null || nhaHang.TaiKhoan.TrangThai == false)
            {
                TempData["Msg"] = "Nhà hàng này hiện đang bị khóa và không thể đặt món.";
                return RedirectToAction("TrangChu", "Home");
            }

            var dsMon = db.MonAns.Where(m => m.MaNH == id).Select(m => new MonAnViewModel { MaMon = m.MaMon, TenMon = m.TenMon, Gia = m.Gia ?? 0, MoTa = m.MoTa, HinhAnh = m.HinhAnh }).ToList();
            var listReviews = db.DanhGiaNhaHangs.Where(dg => dg.MaNH == id).Include(dg => dg.KhachHang).OrderByDescending(dg => dg.ThoiGian).ToList()
                .Select(dg => new ReviewDisplayModel { TenKH = dg.KhachHang != null ? dg.KhachHang.TenKH : "Khách ẩn danh", SoSao = dg.SoSao ?? 5, BinhLuan = dg.BinhLuan, ThoiGian = dg.ThoiGian ?? DateTime.Now }).ToList();
            ViewBag.NhaHang = nhaHang;
            ViewBag.DanhSachDanhGia = listReviews;
            ViewBag.DiemTrungBinh = listReviews.Any() ? Math.Round(listReviews.Average(x => x.SoSao), 1) : 0;
            ViewBag.TongLuotDanhGia = listReviews.Count;
            return View(dsMon);
        }

        [HttpPost]
        public JsonResult ThemVaoGio(string id, string note)
        {
            if (!KiemTraDangNhap())
            {
                return Json(new { success = false, message = "Vui lòng đăng nhập!" }, JsonRequestBehavior.AllowGet);
            }

            XoaLichSuQuaHan();
            var mon = db.MonAns.Include("NhaHang.TaiKhoan").FirstOrDefault(m => m.MaMon == id);
            if (mon == null)
            {
                return Json(new { success = false, message = "Món ăn không tồn tại." }, JsonRequestBehavior.AllowGet);
            }

            if (mon.NhaHang == null || mon.NhaHang.TaiKhoan == null || mon.NhaHang.TaiKhoan.TrangThai == false)
            {
                return Json(new { success = false, message = "Nhà hàng này hiện đang bị khóa và không thể đặt món." }, JsonRequestBehavior.AllowGet);
            }

            string maKH = Session["MaKH"] as string;

            string normalizedNote = (note ?? "").Trim();


            var lsgh = db.LichSuGioHangs.FirstOrDefault(
                x => x.MaKH == maKH
                  && x.MaMon == mon.MaMon
                  && ((x.Note ?? "") == normalizedNote));

            if (lsgh == null)
            {
                string lastMaGH = db.LichSuGioHangs.OrderByDescending(x => x.MaGH).Select(x => x.MaGH).FirstOrDefault();
                int nextId = lastMaGH != null && lastMaGH.StartsWith("GH") && int.TryParse(lastMaGH.Substring(2), out int currentId) ? currentId + 1 : 1;
                db.LichSuGioHangs.Add(new LichSuGioHang
                {
                    MaGH = "GH" + nextId.ToString().PadLeft(5, '0'),
                    MaKH = maKH,
                    MaNH = mon.MaNH,
                    MaMon = mon.MaMon,
                    SoLuong = 1,
                    DonGia = mon.Gia ?? 0,
                    TongTien = mon.Gia ?? 0,
                    ThoiGianChon = DateTime.Now,
                    Note = normalizedNote
                });
            }
            else
            {
                lsgh.SoLuong += 1;
                lsgh.TongTien = lsgh.SoLuong * lsgh.DonGia;
                lsgh.ThoiGianChon = DateTime.Now;
            }

            db.SaveChanges();
            return Json(new { success = true }, JsonRequestBehavior.AllowGet);
        }

        [HttpPost]
        public ActionResult CapNhatSoLuong(string maGH, int soLuong)
        {
            if (!KiemTraDangNhap()) return RedirectToAction("TrangChu", "Home");
            string maKH = Session["MaKH"] as string;
            var lsgh = db.LichSuGioHangs.FirstOrDefault(x => x.MaKH == maKH && x.MaGH == maGH);
            if (lsgh != null)
            {
                if (soLuong <= 0)
                    db.LichSuGioHangs.Remove(lsgh);
                else
                {
                    lsgh.SoLuong = soLuong;
                    lsgh.TongTien = soLuong * lsgh.DonGia;
                }
                db.SaveChanges();
            }
            return Json(new { success = true }, JsonRequestBehavior.AllowGet);
        }

        [HttpPost]
        public ActionResult XoaKhoiGio(string maGH)
        {
            if (!KiemTraDangNhap()) return Json(new { success = false }, JsonRequestBehavior.AllowGet);
            string maKH = Session["MaKH"] as string;
            var lsgh = db.LichSuGioHangs.FirstOrDefault(x => x.MaKH == maKH && x.MaGH == maGH);
            if (lsgh != null)
            {
                db.LichSuGioHangs.Remove(lsgh);
                db.SaveChanges();
            }
            return Json(new { success = true }, JsonRequestBehavior.AllowGet);
        }

        [HttpPost]
        public JsonResult CapNhatGhiChu(string maGH, string note)
        {
            if (!KiemTraDangNhap())
            {
                return Json(new { success = false, message = "Vui lòng đăng nhập!" }, JsonRequestBehavior.AllowGet);
            }

            string maKH = Session["MaKH"] as string;
            var lsgh = db.LichSuGioHangs.FirstOrDefault(x => x.MaGH == maGH && x.MaKH == maKH);
            if (lsgh == null)
            {
                return Json(new { success = false, message = "Món trong giỏ hàng không tồn tại." }, JsonRequestBehavior.AllowGet);
            }

            lsgh.Note = (note ?? "").Trim();
            db.SaveChanges();

            return Json(new { success = true }, JsonRequestBehavior.AllowGet);
        }


        public ActionResult XemGioHang()
        {
            if (!KiemTraDangNhap()) return RedirectToAction("TrangChu", "Home");
            string maKH = Session["MaKH"] as string; XoaLichSuQuaHan();
            var cart = (from ls in db.LichSuGioHangs
                        join m in db.MonAns on ls.MaMon equals m.MaMon
                        where ls.MaKH == maKH
                        select new CartItem
                        {
                            MaGH = ls.MaGH,
                            MaMon = ls.MaMon,
                            TenMon = m.TenMon,
                            Gia = ls.DonGia,
                            SoLuong = ls.SoLuong,
                            MaNH = ls.MaNH,
                            TenNH = ls.NhaHang.TenNH,
                            ThanhTien = ls.TongTien,
                            HinhAnh = m.HinhAnh,
                            Note = ls.Note
                        }).ToList();
            if (!cart.Any()) return View(cart);


            decimal phiShip = 15000m;
            decimal phiDichVu = TinhPhiDichVu();
            double khoangCach = 0;


            var kh = db.KhachHangs.Find(maKH);
            string diaChiKH = kh?.DiaChi;


            string maNH = cart.FirstOrDefault()?.MaNH;
            var nh = db.NhaHangs.Find(maNH);

            if (nh != null && !string.IsNullOrEmpty(diaChiKH))
            {

                double nhLat = nh.Latitude ?? 0;
                double nhLng = nh.Longitude ?? 0;


                if (nhLat == 0)
                {
                    var c = ValidateAddressRealtime(nh.DiaChi, nh.DiaChi);
                    if (c.isValid) { nhLat = c.lat.Value; nhLng = c.lng.Value; }
                }


                double khLat = 0, khLng = 0;
                var checkKH = ValidateAddressRealtime(diaChiKH, diaChiKH);
                if (checkKH.isValid) { khLat = checkKH.lat.Value; khLng = checkKH.lng.Value; }


                if (nhLat != 0 && khLat != 0)
                {
                    double dist = 0;

                    dynamic route = GetRouteDataORS(nhLat, nhLng, khLat, khLng);


                    if (route == null) route = GetRouteDataOSRM(nhLat, nhLng, khLat, khLng);

                    if (route != null)
                    {
                        try { dist = (double)route.GetType().GetProperty("distance").GetValue(route, null); } catch { }
                    }
                    else
                    {

                        dist = CalculateHaversineDistance(nhLat, nhLng, khLat, khLng);
                    }


                    khoangCach = Math.Round(dist / 1000.0, 1);
                    phiShip = TinhPhiShipMoi(dist);
                }
            }


            ViewBag.PhiShip = phiShip;
            ViewBag.PhiDichVu = phiDichVu;
            ViewBag.KhoangCach = khoangCach;
            ViewBag.TongTienHang = cart.Sum(x => (decimal)x.ThanhTien);
            ViewBag.TongThanhToan = cart.Sum(x => (decimal)x.ThanhTien) + phiShip + phiDichVu;

            ViewBag.DiaChiGiao = kh?.DiaChi;
            ViewBag.SDT = kh?.SDT;

            return View(cart);
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult DatHang(string maNH, string selectedItems, string diaChi, string phuongXa, string quanHuyen, string tinhTP, string sdt, string phuongThucTT)
        {
            if (!KiemTraDangNhap()) { TempData["Msg"] = "Vui lòng đăng nhập!"; return RedirectToAction("TrangChu", "Home"); }
            string maKH = Session["MaKH"] as string;

            if (string.IsNullOrEmpty(maNH) || string.IsNullOrEmpty(selectedItems))
            {
                TempData["Msg"] = "Vui lòng chọn món ăn để thanh toán.";
                return RedirectToAction("XemGioHang");
            }

            var nhaHang = db.NhaHangs.Include("TaiKhoan").FirstOrDefault(n => n.MaNH == maNH);
            if (nhaHang == null || nhaHang.TaiKhoan == null || nhaHang.TaiKhoan.TrangThai == false)
            {
                TempData["Msg"] = "Nhà hàng này hiện đang bị khóa và không thể đặt món.";
                return RedirectToAction("XemGioHang");
            }

            var listMaGH = selectedItems.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).ToList();

            var cart = db.LichSuGioHangs.Where(x => x.MaKH == maKH && x.MaNH == maNH && listMaGH.Contains(x.MaGH)).ToList();
            if (!cart.Any()) { TempData["Msg"] = "Giỏ hàng trống hoặc các món đã chọn không hợp lệ!"; return RedirectToAction("XemGioHang"); }


            string finalDiaChi = diaChi;
            if (!string.IsNullOrEmpty(phuongXa) && !string.IsNullOrEmpty(quanHuyen) && !string.IsNullOrEmpty(tinhTP))
            {
                finalDiaChi = $"{diaChi}, {phuongXa}, {quanHuyen}, {tinhTP}";
            }




            var check = ValidateAddressRealtime(diaChi, finalDiaChi);
            if (!check.isValid)
            {
                TempData["Msg"] = $"Lỗi địa chỉ: {check.message}";
                return RedirectToAction("XemGioHang");
            }

            double latKH = check.lat.Value; double lngKH = check.lng.Value;



            double nhLat = nhaHang.Latitude ?? 0; double nhLng = nhaHang.Longitude ?? 0;
            if (nhLat == 0)
            {
                var nhCheck = ValidateAddressRealtime(nhaHang.DiaChi, nhaHang.DiaChi);
                if (nhCheck.isValid)
                {
                    nhLat = nhCheck.lat.Value;
                    nhLng = nhCheck.lng.Value;
                    nhaHang.Latitude = nhLat;
                    nhaHang.Longitude = nhLng;
                    db.Entry(nhaHang).State = EntityState.Modified;
                    db.SaveChanges();
                }
            }

            decimal phiShip = 15000m;
            decimal phiDichVu = TinhPhiDichVu();

            if (nhLat != 0)
            {

                dynamic route = GetRouteDataORS(nhLat, nhLng, latKH, lngKH);
                if (route == null) route = GetRouteDataOSRM(nhLat, nhLng, latKH, lngKH);

                double dist = 0;
                if (route != null)
                {
                    try { dist = (double)route.GetType().GetProperty("distance").GetValue(route, null); } catch { }
                }
                else
                {
                    dist = CalculateHaversineDistance(nhLat, nhLng, latKH, lngKH);
                }

                if (dist / 1000.0 > MAX_DELIVERY_RADIUS)
                {
                    TempData["Msg"] = $"Địa chỉ quá xa ({Math.Round(dist / 1000, 1)}km). Chỉ giao trong bán kính {MAX_DELIVERY_RADIUS}km.";
                    return RedirectToAction("XemGioHang");
                }


                phiShip = TinhPhiShipMoi(dist);
            }


            decimal totalShippingFee = phiShip + phiDichVu;


            string maDon = "DH" + DateTime.Now.ToString("yyMMddHHmmss") + Guid.NewGuid().ToString("N").Substring(0, 4).ToUpper();

            decimal tongTienHang = cart.Sum(x => (decimal)x.TongTien);
            decimal tongCong = tongTienHang + totalShippingFee;

            using (var transaction = db.Database.BeginTransaction())
            {
                try
                {
                    var don = new DonHang
                    {
                        MaDon = maDon,
                        MaKH = maKH,
                        MaNH = maNH,
                        DiaChiGiaoHang = finalDiaChi,
                        SDTGiaoHang = sdt,
                        TrangThai = "Chờ xác nhận",
                        TongTien = tongCong,
                        ThoiGianDat = DateTime.Now,
                        Latitude = latKH,
                        Longitude = lngKH,
                        ShipFee = totalShippingFee
                    };
                    db.DonHangs.Add(don);

                    foreach (var item in cart)
                    {
                        var monAn = db.MonAns.Find(item.MaMon);
                        db.ChiTietDonHangs.Add(new ChiTietDonHang
                        {
                            MaDon = maDon,
                            MaMon = item.MaMon,
                            SoLuong = item.SoLuong,
                            DonGia = monAn != null ? monAn.Gia : item.DonGia,
                            Note = item.Note
                        });
                        db.LichSuGioHangs.Remove(item);
                    }
                    db.SaveChanges();
                    transaction.Commit();
                }
                catch
                {
                    transaction.Rollback();
                    TempData["Msg"] = "Lỗi khi tạo đơn hàng, vui lòng thử lại!";
                    return RedirectToAction("XemGioHang");
                }
            }

            if (phuongThucTT == "QR")
            {
                TempData["Msg"] = "Hoàn tất đặt món! Vui lòng thanh toán để hoàn tất đơn hàng.";
                return RedirectToAction("ThanhToanQR", new { maDon = maDon, tongTien = tongCong });
            }
            else
            {
                // Thông báo tới Nhà Hàng qua SignalR (COD)
                try
                {
                    var context = Microsoft.AspNet.SignalR.GlobalHost.ConnectionManager.GetHubContext<ĐACN.Hubs.DeliveryHub>();
                    context.Clients.Group("NhaHang_" + maNH).notifyNewOrder($"Có đơn hàng COD mới: {maDon}");
                }
                catch { }

                TempData["OrderSuccess"] = "Đơn hàng của bạn đã được đặt thành công!";
                return RedirectToAction("TrangChu", "Home");
            }
        }

        public ActionResult ThanhToanQR(string maDon, decimal tongTien)
        {
            if (!KiemTraDangNhap()) return RedirectToAction("TrangChu", "Home");
            ViewBag.MaDon = maDon;
            ViewBag.TongTien = tongTien;
            ViewBag.QRCode = $"https://img.vietqr.io/image/970422-000012345678-compact2.png?amount={(int)tongTien}&addInfo={maDon}&accountName=TAPFOOD%20COMPANY";
            return View();
        }

        [HttpPost] 
        public ActionResult XacNhanThanhToanQR(string maDon) 
        {
            if (!KiemTraDangNhap()) return RedirectToAction("TrangChu", "Home");
            string maKH = Session["MaKH"] as string;
            var don = db.DonHangs.FirstOrDefault(d => d.MaDon == maDon && d.MaKH == maKH);
            if (don != null)
            {
                don.TrangThai = "Đã nhận đơn";
                db.SaveChanges();
                
                // Thông báo tới Nhà Hàng qua SignalR
                try
                {
                    var context = Microsoft.AspNet.SignalR.GlobalHost.ConnectionManager.GetHubContext<ĐACN.Hubs.DeliveryHub>();
                    context.Clients.Group("NhaHang_" + don.MaNH).notifyNewOrder($"Có đơn hàng QR mới: {maDon}");
                }
                catch { }
            }
            TempData["Msg"] = "Thanh toán thành công! Đơn hàng đang xử lý."; 
            return RedirectToAction("DonHangCuaToi"); 
        }


        [HttpGet]
        public JsonResult GetDistanceNhaHangToKhachHang(string maNH, string diaChi, string phuongXa, string quanHuyen, string tinhTP, bool? luuDiaChi)
        {

            string fullAddress = $"{diaChi}, {phuongXa}, {quanHuyen}, {tinhTP}";

            var check = ValidateAddressRealtime(diaChi, fullAddress);
            if (!check.isValid) return Json(new { success = false, message = check.message }, JsonRequestBehavior.AllowGet);

            if (luuDiaChi == true && Session["MaKH"] != null)
            {
                string maKH = Session["MaKH"] as string;
                var kh = db.KhachHangs.Find(maKH);
                if (kh != null)
                {
                    kh.DiaChi = fullAddress;
                    db.Entry(kh).State = System.Data.Entity.EntityState.Modified;
                    db.SaveChanges();
                }
            }

            var nh = db.NhaHangs.Find(maNH);
            double nhLat = nh.Latitude ?? 0, nhLng = nh.Longitude ?? 0;
            if (nhLat == 0)
            {
                var c = ValidateAddressRealtime(nh.DiaChi, nh.DiaChi);
                if (c.isValid) { nhLat = c.lat.Value; nhLng = c.lng.Value; }
                else return Json(new { success = false, message = "Không xác định được vị trí nhà hàng." }, JsonRequestBehavior.AllowGet);
            }


            var routeData = GetRouteDataORS(nhLat, nhLng, check.lat.Value, check.lng.Value);
            if (routeData == null) routeData = GetRouteDataOSRM(nhLat, nhLng, check.lat.Value, check.lng.Value);

            double dist = 0;
            if (routeData != null)
            {
                try { dist = (double)routeData.GetType().GetProperty("distance").GetValue(routeData, null); } catch { }

                if (dist / 1000.0 > MAX_DELIVERY_RADIUS)
                {
                    return Json(new { success = false, message = $"Quá xa ({Math.Round(dist / 1000, 1)}km). Chỉ giao < {MAX_DELIVERY_RADIUS}km." }, JsonRequestBehavior.AllowGet);
                }
            }
            else
            {
                dist = CalculateHaversineDistance(nhLat, nhLng, check.lat.Value, check.lng.Value);
            }


            decimal phiShip = TinhPhiShipMoi(dist);
            decimal phiDichVu = TinhPhiDichVu();
            decimal tongPhi = phiShip + phiDichVu;


            return Json(new
            {
                success = true,
                data = new
                {
                    distance = dist,
                    shipFeeOnly = phiShip,
                    serviceFee = phiDichVu,
                    totalFee = tongPhi
                }
            }, JsonRequestBehavior.AllowGet);
        }


        public ActionResult DonHangCuaToi()
        {
            if (!KiemTraDangNhap()) { TempData["Msg"] = "Vui lòng đăng nhập để xem đơn hàng!"; return RedirectToAction("TrangChu", "Home"); }
            string maKH = Session["MaKH"] as string;
            if (string.IsNullOrEmpty(maKH)) { var tk = Session["TaiKhoan"] as TaiKhoan; if (tk != null) { var kh = db.KhachHangs.FirstOrDefault(k => k.MaTK == tk.MaTK); if (kh != null) maKH = kh.MaKH; } }
            if (string.IsNullOrEmpty(maKH)) return RedirectToAction("TrangChu", "Home");

            var tatCaDonHang = db.DonHangs.AsNoTracking().ToList();
            var donCuaKhach = tatCaDonHang.Where(d => d.MaKH != null && d.MaKH.Trim() == maKH.Trim()).OrderByDescending(d => d.ThoiGianDat).ToList();

            var donHangDangXuLy = donCuaKhach.Where(d => d.TrangThai == "Chờ xác nhận" || d.TrangThai == "Đang giao" || d.TrangThai == "Đang lấy món").Select(d => new DonHangModel { MaDon = d.MaDon, MaKH = d.MaKH, MaNH = d.MaNH, TrangThai = d.TrangThai, TongTien = d.TongTien ?? 0, ThoiGianDat = d.ThoiGianDat ?? DateTime.Now }).ToList();
            var lichSuDonHang = donCuaKhach.Where(d => d.TrangThai != "Chờ xác nhận" && d.TrangThai != "Đang giao" && d.TrangThai != "Đang lấy món").Select(d => new DonHangModel { MaDon = d.MaDon, MaKH = d.MaKH, MaNH = d.MaNH, TrangThai = d.TrangThai, TongTien = d.TongTien ?? 0, ThoiGianDat = d.ThoiGianDat ?? DateTime.Now }).ToList();

            return View(new DonHangTongHopViewModel { DonHangDangXuLy = donHangDangXuLy, LichSuDonHang = lichSuDonHang });
        }

        public ActionResult TheoDoiDonHang(string maDon)
        {
            if (!KiemTraDangNhap()) { TempData["Msg"] = "Vui lòng đăng nhập!"; return RedirectToAction("TrangChu", "Home"); }
            string maKH = Session["MaKH"] as string;
            var donHangEntity = db.DonHangs.Include(d => d.KhachHang).Include(d => d.NhaHang).Include(d => d.Shipper).FirstOrDefault(d => d.MaDon == maDon && d.MaKH == maKH);
            if (donHangEntity == null) return HttpNotFound();


            var donHang = new TheoDoiDonHangViewModel
            {
                MaDon = donHangEntity.MaDon,
                TenKH = donHangEntity.KhachHang?.TenKH,
                DiaChi = donHangEntity.DiaChiGiaoHang,
                Sdt = donHangEntity.SDTGiaoHang,
                TenNH = donHangEntity.NhaHang?.TenNH,
                MaShipper = donHangEntity.MaShipper,
                TrangThai = donHangEntity.TrangThai,
                TongTien = donHangEntity.TongTien ?? 0,
                ThoiGianDat = donHangEntity.ThoiGianDat ?? DateTime.Now,
                NhaHangLatitude = donHangEntity.NhaHang?.Latitude,
                NhaHangLongitude = donHangEntity.NhaHang?.Longitude,
                KhachHangLatitude = donHangEntity.Latitude ?? donHangEntity.KhachHang?.Latitude,
                KhachHangLongitude = donHangEntity.Longitude ?? donHangEntity.KhachHang?.Longitude
            };

            var chiTiet = db.ChiTietDonHangs.Where(c => c.MaDon == maDon).Select(c => new ChiTietDonHangModel
            {
                TenMon = c.MonAn.TenMon,
                SoLuong = c.SoLuong ?? 0,
                DonGia = c.DonGia ?? 0,
                TongTien = (c.SoLuong ?? 0) * (c.DonGia ?? 0),
                Note = c.Note ?? ""
            }).ToList();
            return View(new TheoDoiDonHangFullViewModel { DonHang = donHang, ChiTietDonHang = chiTiet });
        }


        [HttpGet]
        [OutputCache(NoStore = true, Duration = 0)]
        public JsonResult GetTrackingInfo(string maDon)
        {
            if (!KiemTraDangNhap()) return Json(new { success = false, message = "Chưa đăng nhập" }, JsonRequestBehavior.AllowGet);
            string maKH = Session["MaKH"] as string;
            var don = db.DonHangs.Include(d => d.NhaHang).Include(d => d.KhachHang).FirstOrDefault(d => d.MaDon == maDon && d.MaKH == maKH);
            if (don == null) return Json(new { success = false, message = "Không tìm thấy đơn hoặc không có quyền" }, JsonRequestBehavior.AllowGet);


            double restLat = don.NhaHang?.Latitude ?? 0; double restLng = don.NhaHang?.Longitude ?? 0;
            if (restLat == 0 && don.NhaHang != null) { var c = GeoCodeORS(don.NhaHang.DiaChi); if (c.lat.HasValue) { restLat = c.lat.Value; restLng = c.lng.Value; } }
            var restaurant = (restLat != 0) ? new { lat = restLat, lng = restLng, name = don.NhaHang?.TenNH } : null;


            double custLat = don.Latitude ?? 0; double custLng = don.Longitude ?? 0;
            if (custLat == 0) { var c = GeoCodeORS(don.DiaChiGiaoHang); if (c.lat.HasValue) { custLat = c.lat.Value; custLng = c.lng.Value; } }
            var customer = (custLat != 0) ? new { lat = custLat, lng = custLng, name = "Khách hàng" } : null;


            double shipLat = 0, shipLng = 0;
            string lastUpdatedTime = DateTime.Now.ToString("HH:mm:ss");

            if (!string.IsNullOrEmpty(don.MaShipper))
            {
                if (don.ShipperLatitude.HasValue && don.ShipperLatitude != 0) { shipLat = don.ShipperLatitude.Value; shipLng = don.ShipperLongitude ?? 0; }
                else { var s = db.Shippers.Find(don.MaShipper); if (s != null && s.Latitude.HasValue) { shipLat = s.Latitude.Value; shipLng = s.Longitude ?? 0; } }
            }
            var shipperMarker = (shipLat != 0) ? new { lat = shipLat, lng = shipLng, maShipper = don.MaShipper, time = lastUpdatedTime } : null;

            return Json(new { success = true, restaurant = restaurant, customer = customer, shipper = shipperMarker, trangThai = don.TrangThai }, JsonRequestBehavior.AllowGet);
        }


        [HttpGet]
        [OutputCache(NoStore = true, Duration = 0)]
        public JsonResult GetShipperRoute(string maDon)
        {
            var donHang = db.DonHangs.Include(d => d.NhaHang).FirstOrDefault(d => d.MaDon == maDon);
            if (donHang == null) return Json(new { success = false, message = "Không tìm thấy đơn hàng" }, JsonRequestBehavior.AllowGet);


            double startLat = 0, startLng = 0;
            if (donHang.ShipperLatitude.HasValue && donHang.ShipperLatitude != 0) { startLat = donHang.ShipperLatitude.Value; startLng = donHang.ShipperLongitude ?? 0; }
            else if (!string.IsNullOrEmpty(donHang.MaShipper)) { var s = db.Shippers.Find(donHang.MaShipper); if (s != null) { startLat = s.Latitude ?? 0; startLng = s.Longitude ?? 0; } }


            double endLat = 0, endLng = 0;
            string status = (donHang.TrangThai ?? "").ToLower();
            string routeType = "";

            if (status.Contains("lấy món") || status.Contains("chờ"))
            {
                routeType = "ToRestaurant";
                endLat = donHang.NhaHang?.Latitude ?? 0; endLng = donHang.NhaHang?.Longitude ?? 0;
                if (endLat == 0 && donHang.NhaHang != null) { var c = GeoCodeORS(donHang.NhaHang.DiaChi); if (c.lat.HasValue) { endLat = c.lat.Value; endLng = c.lng.Value; } }
            }
            else if (status.Contains("đang giao"))
            {
                routeType = "ToCustomer";
                endLat = donHang.Latitude ?? 0; endLng = donHang.Longitude ?? 0;
                if (endLat == 0 && !string.IsNullOrEmpty(donHang.DiaChiGiaoHang)) { var c = GeoCodeORS(donHang.DiaChiGiaoHang); if (c.lat.HasValue) { endLat = c.lat.Value; endLng = c.lng.Value; } }
            }

            if (startLat == 0 || startLng == 0 || endLat == 0 || endLng == 0) return Json(new { success = false, message = "Thiếu tọa độ" }, JsonRequestBehavior.AllowGet);


            object routeGeometry = null;
            double distanceMeters = 0;


            var routeData = GetRouteDataORS(startLat, startLng, endLat, endLng);


            if (routeData == null) routeData = GetRouteDataOSRM(startLat, startLng, endLat, endLng);

            if (routeData != null)
            {
                try
                {
                    distanceMeters = (double)routeData.GetType().GetProperty("distance").GetValue(routeData, null);
                    routeGeometry = routeData.GetType().GetProperty("route").GetValue(routeData, null);
                }
                catch { }
            }
            else
            {

                distanceMeters = CalculateHaversineDistance(startLat, startLng, endLat, endLng);
                routeGeometry = GenerateManhattanRoute(startLat, startLng, endLat, endLng);
            }

            double distanceKm = Math.Round(distanceMeters / 1000.0, 1);
            double averageSpeedKmH = 30.0;
            double estimatedMinutes = Math.Ceiling(((distanceMeters / 1000.0) / averageSpeedKmH) * 60);
            if (estimatedMinutes < 1) estimatedMinutes = 1;

            return Json(new { success = true, route = routeGeometry, distanceText = $"{distanceKm} km", durationText = $"{estimatedMinutes} phút", statusText = routeType == "ToRestaurant" ? "Shipper đang đến nhà hàng" : "Shipper đang giao tới bạn", routeType = routeType }, JsonRequestBehavior.AllowGet);
        }

        public ActionResult VietDanhGia(string maDon)
        {
            if (!KiemTraDangNhap()) { TempData["Msg"] = "Vui lòng đăng nhập!"; return RedirectToAction("TrangChu", "Home"); }
            string maKH = Session["MaKH"] as string;
            var donHang = db.DonHangs.Include(d => d.NhaHang).Include(d => d.Shipper).FirstOrDefault(d => d.MaDon == maDon && d.MaKH == maKH);
            if (donHang == null) return HttpNotFound();
            if (donHang.TrangThai != "Hoàn thành" && donHang.TrangThai != "Hoàn tất") { TempData["Msg"] = "Đơn hàng chưa hoàn thành, không thể đánh giá!"; return RedirectToAction("DonHangCuaToi"); }
            var existingReview = db.DanhGiaNhaHangs.FirstOrDefault(d => d.MaDon == maDon);
            if (existingReview != null) { TempData["Msg"] = "Bạn đã đánh giá đơn hàng này rồi!"; return RedirectToAction("DonHangCuaToi"); }
            var model = new DanhGiaViewModel { MaDon = donHang.MaDon, MaNH = donHang.MaNH, TenNH = donHang.NhaHang?.TenNH, MaShipper = donHang.MaShipper, TenShipper = donHang.Shipper != null ? donHang.Shipper.TenShipper : "Shipper", SoSaoNhaHang = 5, SoSaoShipper = 5 };
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult LuuDanhGia(DanhGiaViewModel model)
        {
            if (!KiemTraDangNhap()) return RedirectToAction("TrangChu", "Home");
            string maKH = Session["MaKH"] as string;

            var donHang = db.DonHangs.FirstOrDefault(d => d.MaDon == model.MaDon && d.MaKH == maKH);
            if (donHang == null)
            {
                TempData["Msg"] = "Đơn hàng không tồn tại hoặc bạn không có quyền đánh giá đơn hàng này!";
                return RedirectToAction("DonHangCuaToi");
            }

            if (donHang.TrangThai != "Hoàn thành" && donHang.TrangThai != "Hoàn tất")
            {
                TempData["Msg"] = "Đơn hàng chưa hoàn thành, không thể đánh giá!";
                return RedirectToAction("DonHangCuaToi");
            }

            var existingReview = db.DanhGiaNhaHangs.FirstOrDefault(d => d.MaDon == model.MaDon);
            if (existingReview != null)
            {
                TempData["Msg"] = "Bạn đã đánh giá đơn hàng này rồi!";
                return RedirectToAction("DonHangCuaToi");
            }

            try
            {
                string maDGNH = "DGN" + Guid.NewGuid().ToString("N").Substring(0, 9).ToUpper();
                var dgNH = new DanhGiaNhaHang { MaDGNH = maDGNH, MaDon = model.MaDon, MaKH = maKH, MaNH = model.MaNH, SoSao = model.SoSaoNhaHang, BinhLuan = model.BinhLuanNhaHang, ThoiGian = DateTime.Now };
                db.DanhGiaNhaHangs.Add(dgNH);
                if (!string.IsNullOrEmpty(model.MaShipper)) { string maDGS = "DGS" + Guid.NewGuid().ToString("N").Substring(0, 9).ToUpper(); var dgShipper = new DanhGiaShipper { MaDG = maDGS, MaDon = model.MaDon, MaKH = maKH, MaShipper = model.MaShipper, SoSao = model.SoSaoShipper, BinhLuan = model.BinhLuanShipper, ThoiGian = DateTime.Now }; db.DanhGiaShippers.Add(dgShipper); }
                db.SaveChanges();
                TempData["Msg"] = "Cảm ơn bạn đã đánh giá dịch vụ!";
                return RedirectToAction("DonHangCuaToi");
            }
            catch (Exception ex) { TempData["Msg"] = "Lỗi khi lưu đánh giá: " + ex.Message; return RedirectToAction("VietDanhGia", new { maDon = model.MaDon }); }
        }


        [HttpGet]
        public ActionResult HoSo()
        {
            if (!KiemTraDangNhap())
            {
                TempData["Msg"] = "Vui lòng đăng nhập để xem hồ sơ.";
                return RedirectToAction("TrangChu", "Home");
            }

            string maKH = Session["MaKH"] as string;
            var kh = db.KhachHangs.Find(maKH);
            if (kh == null) return HttpNotFound();

            return View(kh);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult CapNhatHoSo(string tenKH, string sdt, string diaChi, HttpPostedFileBase hinhAnh)
        {
            if (!KiemTraDangNhap())
            {
                TempData["Msg"] = "Vui lòng đăng nhập để cập nhật hồ sơ.";
                return RedirectToAction("TrangChu", "Home");
            }

            string maKH = Session["MaKH"] as string;
            var kh = db.KhachHangs.Find(maKH);
            if (kh == null) return HttpNotFound();


            if (!string.IsNullOrWhiteSpace(tenKH)) kh.TenKH = tenKH;
            if (!string.IsNullOrWhiteSpace(sdt)) kh.SDT = sdt;


            if (!string.IsNullOrWhiteSpace(diaChi) && kh.DiaChi != diaChi)
            {
                kh.DiaChi = diaChi;
                var geo = ValidateAddressRealtime(diaChi, diaChi);
                if (geo.isValid && geo.lat.HasValue && geo.lng.HasValue)
                {
                    kh.Latitude = geo.lat.Value;
                    kh.Longitude = geo.lng.Value;
                }
            }


            if (hinhAnh != null && hinhAnh.ContentLength > 0)
            {
                string errorMsg;
                if (!ValidateImageFile(hinhAnh, out errorMsg))
                {
                    TempData["Msg"] = errorMsg;
                    return RedirectToAction("HoSo");
                }

                var ext = Path.GetExtension(hinhAnh.FileName).ToLower();
                var fileName = "kh_" + maKH + "_" + DateTime.Now.Ticks + ext;
                var path = Path.Combine(Server.MapPath("~/images/khachhang/"), fileName);

                if (!Directory.Exists(Path.GetDirectoryName(path)))
                    Directory.CreateDirectory(Path.GetDirectoryName(path));


                if (!string.IsNullOrEmpty(kh.HinhAnh))
                {
                    var oldPath = Server.MapPath("~" + kh.HinhAnh);
                    if (System.IO.File.Exists(oldPath)) System.IO.File.Delete(oldPath);
                }

                hinhAnh.SaveAs(path);
                kh.HinhAnh = "/images/khachhang/" + fileName;
            }

            db.SaveChanges();
            TempData["Msg"] = "Cập nhật hồ sơ thành công!";
            return RedirectToAction("HoSo");
        }

        [HttpGet]
        public ActionResult CaiDat()
        {
            if (!KiemTraDangNhap())
            {
                TempData["Msg"] = "Vui lòng đăng nhập.";
                return RedirectToAction("TrangChu", "Home");
            }
            return View();
        }

        [HttpPost]
        public ActionResult DoiMatKhau(string matKhauCu, string matKhauMoi, string xacNhanMatKhau)
        {
            if (!KiemTraDangNhap()) return RedirectToAction("TrangChu", "Home");

            var sessionTK = Session["TaiKhoan"] as TaiKhoan;
            var tk = db.TaiKhoans.Find(sessionTK.MaTK);

            if (tk == null) return RedirectToAction("TrangChu", "Home");

            bool isPasswordValid = false;
            
            if (tk.MatKhau.StartsWith("$2a$") || tk.MatKhau.StartsWith("$2b$") || tk.MatKhau.StartsWith("$2y$"))
            {
                isPasswordValid = BCrypt.Net.BCrypt.Verify(matKhauCu, tk.MatKhau);
            }
            else
            {
                if (tk.MatKhau == matKhauCu)
                {
                    isPasswordValid = true;
                }
            }

            if (!isPasswordValid)
            {
                TempData["Msg"] = "Mật khẩu cũ không chính xác.";
                return RedirectToAction("CaiDat");
            }

            if (string.IsNullOrWhiteSpace(matKhauMoi) || matKhauMoi.Length < 6)
            {
                TempData["Msg"] = "Mật khẩu mới phải từ 6 ký tự trở lên.";
                return RedirectToAction("CaiDat");
            }

            if (matKhauMoi != xacNhanMatKhau)
            {
                TempData["Msg"] = "Xác nhận mật khẩu không khớp.";
                return RedirectToAction("CaiDat");
            }

            tk.MatKhau = BCrypt.Net.BCrypt.HashPassword(matKhauMoi);
            db.SaveChanges();
            TempData["Msg"] = "Đổi mật khẩu thành công.";
            return RedirectToAction("CaiDat");
        }

        [HttpPost]
        public ActionResult XoaTaiKhoan()
        {
            if (!KiemTraDangNhap()) return RedirectToAction("TrangChu", "Home");

            var sessionTK = Session["TaiKhoan"] as TaiKhoan;
            var tk = db.TaiKhoans.Find(sessionTK.MaTK);

            if (tk != null)
            {



                tk.TrangThai = false;
                db.SaveChanges();


                return RedirectToAction("Logout", "Shipper");
            }

            TempData["Msg"] = "Lỗi khi xóa tài khoản.";
            return RedirectToAction("CaiDat");
        }


        [HttpGet]
        public JsonResult LaySoLuongGioHang()
        {
            if (!KiemTraDangNhap())
                return Json(new { success = false, soLuong = 0 }, JsonRequestBehavior.AllowGet);

            string maKH = Session["MaKH"] as string;
            XoaLichSuQuaHan();
            var tongSoLuong = db.LichSuGioHangs.Where(x => x.MaKH == maKH).Sum(x => (int?)x.SoLuong) ?? 0;
            return Json(new { success = true, soLuong = tongSoLuong }, JsonRequestBehavior.AllowGet);
        }

        [HttpGet]
        public JsonResult LayThongTinGioHang()
        {
            if (!KiemTraDangNhap())
                return Json(new { success = false, soLuong = 0, tongTien = 0 }, JsonRequestBehavior.AllowGet);

            string maKH = Session["MaKH"] as string;
            XoaLichSuQuaHan();
            
            var gioHang = (from ls in db.LichSuGioHangs
                           join m in db.MonAns on ls.MaMon equals m.MaMon
                           where ls.MaKH == maKH
                           select new { ls.SoLuong, m.Gia }).ToList();

            var tongSoLuong = gioHang.Sum(x => (int?)x.SoLuong) ?? 0;
            var tongTien = gioHang.Sum(x => ((int?)x.SoLuong ?? 0) * ((double?)x.Gia ?? 0));
            
            return Json(new { success = true, soLuong = tongSoLuong, tongTien = tongTien }, JsonRequestBehavior.AllowGet);
        }


        public ActionResult MonAnTheoLoai(string maLoai)
        {
            if (!KiemTraDangNhap())
            {
                TempData["Msg"] = "Vui lòng đăng nhập!";
                return RedirectToAction("TrangChu", "Home");
            }

            if (string.IsNullOrEmpty(maLoai))
                return HttpNotFound();

            var loaiMonAn = db.LoaiMonAns.Find(maLoai);
            if (loaiMonAn == null)
                return HttpNotFound();


            var maNHList = db.MonAns.Where(m => m.MaLoai == maLoai).Select(m => m.MaNH).Distinct().ToList();
            var nhaHangList = db.NhaHangs
                .Include("TaiKhoan")
                .Where(nh => maNHList.Contains(nh.MaNH) && nh.TaiKhoan != null && nh.TaiKhoan.TrangThai == true)
                .Select(nh => new NhaHangViewModel
                {
                    MaNH = nh.MaNH,
                    TenNH = nh.TenNH,
                    DiaChi = nh.DiaChi,
                    HinhAnh = nh.HinhAnh,
                    TrangThai = nh.TrangThai,
                    TongLuotMua = db.DonHangs.Count(d => d.MaNH == nh.MaNH)
                }).ToList();

            var model = new MonAnTheoLoaiViewModel
            {
                LoaiMonAn = new LoaiMonAnViewModel
                {
                    MaLoai = loaiMonAn.MaLoai,
                    TenLoai = loaiMonAn.TenLoai,
                    HinhAnh = loaiMonAn.HinhAnh
                },
                NhaHang = nhaHangList,
                MonAn = new List<MonAnViewModel>()
            };

            return View(model);
        }


        [HttpPost]
        public JsonResult LuuDanhGiaShipper(string maDon, string maShipper, int soSao, string binhLuan)
        {
            if (!KiemTraDangNhap())
                return Json(new { success = false, message = "Vui lòng đăng nhập!" }, JsonRequestBehavior.AllowGet);

            string maKH = Session["MaKH"] as string;
            try
            {

                var donHang = db.DonHangs.FirstOrDefault(d => d.MaDon == maDon && d.MaKH == maKH);
                if (donHang == null)
                    return Json(new { success = false, message = "Không tìm thấy đơn hàng!" }, JsonRequestBehavior.AllowGet);


                var existing = db.DanhGiaShippers.FirstOrDefault(d => d.MaDon == maDon && d.MaKH == maKH);
                if (existing != null)
                {

                    existing.SoSao = soSao;
                    existing.BinhLuan = binhLuan;
                    existing.ThoiGian = DateTime.Now;
                }
                else
                {

                    string maDG = "DGS" + Guid.NewGuid().ToString("N").Substring(0, 9).ToUpper();
                    var danhGia = new DanhGiaShipper
                    {
                        MaDG = maDG,
                        MaDon = maDon,
                        MaKH = maKH,
                        MaShipper = maShipper,
                        SoSao = soSao,
                        BinhLuan = binhLuan,
                        ThoiGian = DateTime.Now
                    };
                    db.DanhGiaShippers.Add(danhGia);
                }
                db.SaveChanges();
                return Json(new { success = true, message = "Đánh giá shipper thành công!" }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Lỗi: " + ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        [HttpPost]
        public JsonResult LuuDanhGiaNhaHang(string maDon, string maNH, int soSao, string binhLuan, HttpPostedFileBase hinhAnhFile = null)
        {
            if (!KiemTraDangNhap())
                return Json(new { success = false, message = "Vui lòng đăng nhập!" }, JsonRequestBehavior.AllowGet);

            string maKH = Session["MaKH"] as string;
            try
            {

                var donHang = db.DonHangs.FirstOrDefault(d => d.MaDon == maDon && d.MaKH == maKH);
                if (donHang == null)
                    return Json(new { success = false, message = "Không tìm thấy đơn hàng!" }, JsonRequestBehavior.AllowGet);


                string fileName = null;
                if (hinhAnhFile != null && hinhAnhFile.ContentLength > 0)
                {

                    string errorMsg;
                    if (!ValidateImageFile(hinhAnhFile, out errorMsg))
                        return Json(new { success = false, message = errorMsg }, JsonRequestBehavior.AllowGet);


                    var ext = Path.GetExtension(hinhAnhFile.FileName).ToLower();
                    fileName = Path.GetFileNameWithoutExtension(hinhAnhFile.FileName) + "_" + DateTime.Now.Ticks + ext;
                    string folderPath = Server.MapPath("~/images/danhgia/");
                    Directory.CreateDirectory(folderPath);
                    string savePath = Path.Combine(folderPath, fileName);
                    hinhAnhFile.SaveAs(savePath);
                }


                var existing = db.DanhGiaNhaHangs.Where(d => d.MaDon == maDon && d.MaKH == maKH)
                    .OrderByDescending(d => d.ThoiGian).FirstOrDefault();

                if (existing != null)
                {

                    existing.SoSao = soSao;
                    existing.BinhLuan = binhLuan;
                    existing.ThoiGian = DateTime.Now;


                    if (!string.IsNullOrEmpty(fileName))
                    {

                        if (!string.IsNullOrEmpty(existing.HinhAnh))
                        {
                            string oldImagePath = Server.MapPath("~/images/danhgia/" + existing.HinhAnh);
                            if (System.IO.File.Exists(oldImagePath))
                            {
                                System.IO.File.Delete(oldImagePath);
                            }
                        }
                        existing.HinhAnh = fileName;
                    }
                }
                else
                {

                    string maDGNH = "DGN" + Guid.NewGuid().ToString("N").Substring(0, 9).ToUpper();
                    var danhGia = new DanhGiaNhaHang
                    {
                        MaDGNH = maDGNH,
                        MaDon = maDon,
                        MaKH = maKH,
                        MaNH = maNH,
                        SoSao = soSao,
                        BinhLuan = binhLuan,
                        ThoiGian = DateTime.Now,
                        HinhAnh = fileName
                    };
                    db.DanhGiaNhaHangs.Add(danhGia);
                }
                db.SaveChanges();
                return Json(new { success = true, message = "Đánh giá nhà hàng thành công!" }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Lỗi: " + ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        [HttpPost]
        public JsonResult BoQuaDanhGia(string maDon)
        {
            if (!KiemTraDangNhap())
                return Json(new { success = false }, JsonRequestBehavior.AllowGet);


            return Json(new { success = true }, JsonRequestBehavior.AllowGet);
        }

        [HttpPost]
        public JsonResult BoQuaDanhGiaNhaHang(string maDon)
        {
            if (!KiemTraDangNhap())
                return Json(new { success = false }, JsonRequestBehavior.AllowGet);


            return Json(new { success = true }, JsonRequestBehavior.AllowGet);
        }

        [HttpGet]
        public JsonResult LayThongTinDanhGiaNhaHang(string maDon)
        {
            if (!KiemTraDangNhap())
                return Json(new { success = false }, JsonRequestBehavior.AllowGet);

            string maKH = Session["MaKH"] as string;
            var donHang = db.DonHangs.Include(d => d.NhaHang)
                .FirstOrDefault(d => d.MaDon == maDon && d.MaKH == maKH);

            if (donHang == null)
                return Json(new { success = false }, JsonRequestBehavior.AllowGet);


            var danhGiaCu = db.DanhGiaNhaHangs
                .Where(d => d.MaDon == maDon && d.MaKH == maKH)
                .OrderByDescending(d => d.ThoiGian)
                .FirstOrDefault();

            return Json(new
            {
                success = true,
                danhGia = new
                {
                    maDon = donHang.MaDon,
                    maNH = donHang.MaNH,
                    tenNhaHang = donHang.NhaHang?.TenNH ?? "Nhà hàng",
                    thoiGian = donHang.ThoiGianDat?.ToString("dd/MM/yyyy HH:mm") ?? "",
                    soSaoCu = danhGiaCu?.SoSao,
                    binhLuanCu = danhGiaCu?.BinhLuan,
                    hinhAnhCu = danhGiaCu?.HinhAnh
                }
            }, JsonRequestBehavior.AllowGet);
        }

        [HttpGet]
        public JsonResult LayDonTiepTheoCanDanhGia(string[] skippedOrders)
        {
            if (!KiemTraDangNhap())
                return Json(new { success = false }, JsonRequestBehavior.AllowGet);

            string maKH = Session["MaKH"] as string;

            var donCanDanhGiaList = db.DonHangs
                .Include(d => d.Shipper)
                .Where(d => d.MaKH == maKH &&
                             (d.TrangThai == "Hoàn thành" || d.TrangThai == "Hoàn tất") &&
                             !string.IsNullOrEmpty(d.MaShipper))
                .OrderByDescending(d => d.ThoiGianDat)
                .ToList()
                .Where(d => !db.DanhGiaShippers.Any(dg => dg.MaDon == d.MaDon && dg.MaKH == maKH));

            if (skippedOrders != null && skippedOrders.Length > 0)
            {
                donCanDanhGiaList = donCanDanhGiaList.Where(d => !skippedOrders.Contains(d.MaDon.Trim()));
            }

            var donCanDanhGia = donCanDanhGiaList.FirstOrDefault();

            if (donCanDanhGia == null)
                return Json(new { success = false }, JsonRequestBehavior.AllowGet);

            return Json(new
            {
                success = true,
                danhGia = new
                {
                    maDon = donCanDanhGia.MaDon,
                    maShipper = donCanDanhGia.MaShipper,
                    tenShipper = donCanDanhGia.Shipper?.TenShipper ?? "Shipper",
                    thoiGian = donCanDanhGia.ThoiGianDat?.ToString("dd/MM/yyyy HH:mm") ?? ""
                }
            }, JsonRequestBehavior.AllowGet);
        }


        public ActionResult Logout()
        {
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

        [HttpGet]
        public JsonResult GetOrderStatus(string maDon)
        {
            var don = db.DonHangs.FirstOrDefault(d => d.MaDon == maDon);
            if (don != null)
            {
                return Json(new { success = true, status = don.TrangThai }, JsonRequestBehavior.AllowGet);
            }
            return Json(new { success = false }, JsonRequestBehavior.AllowGet);
        }

    }
}