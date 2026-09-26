using ĐACN.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using System.Web.Mvc;

namespace ĐACN.Controllers
{
    public class KhachHangController : BaseController
    {
        private readonly ĐACN.Services.MapRoutingService _mapService = new ĐACN.Services.MapRoutingService();
        private readonly ĐACN.Services.CacheService _cacheService = new ĐACN.Services.CacheService();

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


        private dynamic GetRouteDataOSRM(double startLat, double startLng, double endLat, double endLng)
        {
            try
            {
                string coordinates = $"{startLng.ToString(CultureInfo.InvariantCulture)},{startLat.ToString(CultureInfo.InvariantCulture)};{endLng.ToString(CultureInfo.InvariantCulture)},{endLat.ToString(CultureInfo.InvariantCulture)}";
                string url = $"http://router.project-osrm.org/route/v1/driving/{coordinates}?overview=full&geometries=geojson";

                var json = Task.Run(async () =>
                {
                    using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                    {
                        request.Headers.TryAddWithoutValidation("User-Agent", "TapFoodDeliveryApp/1.0");
                        var response = await _sharedHttpClient.SendAsync(request).ConfigureAwait(false);
                        if (response.IsSuccessStatusCode)
                        {
                            return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        }
                    }
                    return null;
                }).GetAwaiter().GetResult();

                if (string.IsNullOrEmpty(json)) return null;

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




        [HttpGet]
        public JsonResult GetToppings(string id)
        {
            var toppings = DACN.Models.Customizations.CustomizationService.GetCustomizationForDish(id);
            return Json(new { success = true, data = toppings }, JsonRequestBehavior.AllowGet);
        }

        public async Task<ActionResult> XemMenu(string id)
        {
            if (!KiemTraDangNhap()) { TempData["Msg"] = "Vui lòng đăng nhập!"; return RedirectToAction("TrangChu", "Home"); }
            var nhaHang = await db.NhaHangs.Include("TaiKhoan").FirstOrDefaultAsync(n => n.MaNH == id);
            if (nhaHang == null) return HttpNotFound();

            if (nhaHang.TaiKhoan == null || nhaHang.TaiKhoan.TrangThai == false)
            {
                TempData["Msg"] = "Nhà hàng này hiện đang bị khóa và không thể đặt món.";
                return RedirectToAction("TrangChu", "Home");
            }

            string menuCacheKey = $"Menu_NhaHang_{id}";
            var dsMon = await _cacheService.GetOrSetAsync(menuCacheKey, 5, async () =>
            {
                var mons = await db.MonAns.Where(m => m.MaNH == id).ToListAsync();
                return mons.Select(m => new MonAnViewModel { MaMon = m.MaMon, TenMon = m.TenMon, Gia = m.Gia ?? 0, MoTa = m.MoTa, HinhAnh = m.HinhAnh }).ToList();
            });
            
            var danhGias = await db.DanhGiaNhaHangs.Where(dg => dg.MaNH == id).Include(dg => dg.KhachHang).OrderByDescending(dg => dg.ThoiGian).ToListAsync();
            var listReviews = danhGias
                .Select(dg => new ReviewDisplayModel { TenKH = dg.KhachHang != null ? dg.KhachHang.TenKH : "Khách ẩn danh", SoSao = dg.SoSao ?? 5, BinhLuan = dg.BinhLuan, ThoiGian = dg.ThoiGian ?? DateTime.Now }).ToList();
                
            ViewBag.NhaHang = nhaHang;
            ViewBag.DanhSachDanhGia = listReviews;
            ViewBag.DiemTrungBinh = listReviews.Any() ? Math.Round(listReviews.Average(x => x.SoSao), 1) : 0;
            ViewBag.TongLuotDanhGia = listReviews.Count;
            return View(dsMon);
        }

        [HttpPost]
        public async Task<JsonResult> ThemVaoGio(string id, string note, decimal extraPrice = 0)
        {
            if (!KiemTraDangNhap())
            {
                return Json(new { success = false, message = "Vui lòng đăng nhập!" }, JsonRequestBehavior.AllowGet);
            }

            if (extraPrice < 0) extraPrice = 0;

            XoaLichSuQuaHan();
            var mon = db.MonAns.Include("NhaHang.TaiKhoan").FirstOrDefault(m => m.MaMon == id);
            if (mon == null)
            {
                return Json(new { success = false, message = "Món ăn không tồn tại." }, JsonRequestBehavior.AllowGet);
            }

            if (mon.TrangThai == false)
            {
                return Json(new { success = false, message = "Món ăn này hiện đang tạm ngừng bán." }, JsonRequestBehavior.AllowGet);
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
                  && ((x.Note ?? "") == normalizedNote)
                  && x.DonGia == (mon.Gia ?? 0) + extraPrice);

            if (lsgh == null)
            {
                var allMaGH = db.LichSuGioHangs
                    .Where(x => x.MaGH.StartsWith("GH"))
                    .Select(x => x.MaGH)
                    .ToList();

                int nextId = 1;
                if (allMaGH.Any())
                {
                    nextId = allMaGH.Select(m => {
                        if (m.Length > 2 && int.TryParse(m.Substring(2), out int val)) return val;
                        return 0;
                    }).DefaultIfEmpty(0).Max() + 1;
                }
                
                decimal finalPrice = (mon.Gia ?? 0) + extraPrice;
                
                db.LichSuGioHangs.Add(new LichSuGioHang
                {
                    MaGH = "GH" + (nextId < 100000 ? nextId.ToString("D5") : nextId.ToString()),
                    MaKH = maKH,
                    MaNH = mon.MaNH,
                    MaMon = mon.MaMon,
                    SoLuong = 1,
                    DonGia = finalPrice,
                    TongTien = finalPrice,
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

            await db.SaveChangesAsync();
            return Json(new { success = true }, JsonRequestBehavior.AllowGet);
        }

        [HttpPost]
        public async Task<ActionResult> CapNhatSoLuong(string maGH, int soLuong)
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
        public async Task<ActionResult> XoaKhoiGio(string maGH)
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
        public async Task<JsonResult> CapNhatGhiChu(string maGH, string note)
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


        [HttpPost]
        public async Task<ActionResult> ApDungVoucher(string maVoucher, string loaiYeuCau, decimal phiShip = 0)
        {
            if (!KiemTraDangNhap()) return Json(new { success = false, message = "Vui lòng đăng nhập!" });
            
            if (string.IsNullOrWhiteSpace(maVoucher))
            {
                if (loaiYeuCau == "Mon") { Session["AppliedVoucherMon"] = null; Session["DiscountAmountMon"] = null; }
                else if (loaiYeuCau == "Ship") { Session["AppliedVoucherShip"] = null; Session["DiscountAmountShip"] = null; }
                return Json(new { success = true, message = "Đã gỡ mã", discount = 0 });
            }
            
            string maKH = Session["MaKH"] as string;
            // Tính tổng tiền các món trong giỏ
            decimal tongTienMon = db.LichSuGioHangs.Where(x => x.MaKH == maKH).Sum(x => (decimal?)x.TongTien) ?? 0m;
            
            if (tongTienMon == 0) return Json(new { success = false, message = "Giỏ hàng rỗng!" });

            var result = ĐACN.Models.VoucherStore.TinhToanGiamGia(maVoucher, loaiYeuCau, tongTienMon, phiShip);
            
            if (result.Success)
            {
                if (loaiYeuCau == "Mon") { Session["AppliedVoucherMon"] = maVoucher; Session["DiscountAmountMon"] = result.DiscountAmount; }
                else if (loaiYeuCau == "Ship") { Session["AppliedVoucherShip"] = maVoucher; Session["DiscountAmountShip"] = result.DiscountAmount; }
            }
            else
            {
                if (loaiYeuCau == "Mon") { Session["AppliedVoucherMon"] = null; Session["DiscountAmountMon"] = null; }
                else if (loaiYeuCau == "Ship") { Session["AppliedVoucherShip"] = null; Session["DiscountAmountShip"] = null; }
            }

            return Json(new { success = result.Success, message = result.Message, discount = result.DiscountAmount });
        }

        public async Task<ActionResult> XemGioHang()
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

                    dynamic route = await _mapService.GetRouteDataORSAsync(nhLat, nhLng, khLat, khLng);


                    if (route == null) route = GetRouteDataOSRM(nhLat, nhLng, khLat, khLng);

                    if (route != null)
                    {
                        try { dist = (double)route.GetType().GetProperty("distance").GetValue(route, null); } catch { }
                    }
                    else
                    {

                        dist = _mapService.CalculateHaversineDistance(nhLat, nhLng, khLat, khLng);
                    }


                    khoangCach = Math.Round(dist / 1000.0, 1);
                    phiShip = TinhPhiShipMoi(dist);
                }
            }


            ViewBag.PhiShip = phiShip;
            ViewBag.PhiDichVu = phiDichVu;
            ViewBag.KhoangCach = khoangCach;
            decimal tongTienHang = cart.Sum(x => (decimal)x.ThanhTien);
            
            decimal discountAmountMon = 0;
            if (Session["DiscountAmountMon"] != null) discountAmountMon = (decimal)Session["DiscountAmountMon"];
            
            decimal discountAmountShip = 0;
            if (Session["DiscountAmountShip"] != null) discountAmountShip = (decimal)Session["DiscountAmountShip"];

            // Recalculate ship discount just in case phiShip changed
            if (Session["AppliedVoucherShip"] != null)
            {
                var recalc = ĐACN.Models.VoucherStore.TinhToanGiamGia((string)Session["AppliedVoucherShip"], "Ship", tongTienHang, phiShip);
                if (recalc.Success) discountAmountShip = recalc.DiscountAmount;
            }

            ViewBag.TongTienHang = tongTienHang;
            ViewBag.TongThanhToan = tongTienHang + phiShip + phiDichVu - discountAmountMon - discountAmountShip;
            
            // Đảm bảo không âm
            if (ViewBag.TongThanhToan < 0) ViewBag.TongThanhToan = 0;

            ViewBag.GiamGiaMon = discountAmountMon;
            ViewBag.GiamGiaShip = discountAmountShip;
            ViewBag.MaVoucherMon = Session["AppliedVoucherMon"] as string;
            ViewBag.MaVoucherShip = Session["AppliedVoucherShip"] as string;
            
            ViewBag.DanhSachVoucher = ĐACN.Models.VoucherStore.DanhSachVoucher.Where(x => x.IsActive).ToList();

            ViewBag.DiaChiGiao = kh?.DiaChi;
            ViewBag.SDT = kh?.SDT;

            return View(cart);
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> DatHang(string maNH, string selectedItems, string diaChi, string phuongXa, string quanHuyen, string tinhTP, string sdt, string phuongThucTT)
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

                dynamic route = await _mapService.GetRouteDataORSAsync(nhLat, nhLng, latKH, lngKH);
                if (route == null) route = GetRouteDataOSRM(nhLat, nhLng, latKH, lngKH);

                double dist = 0;
                if (route != null)
                {
                    try { dist = (double)route.GetType().GetProperty("distance").GetValue(route, null); } catch { }
                }
                else
                {
                    dist = _mapService.CalculateHaversineDistance(nhLat, nhLng, latKH, lngKH);
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
            
            decimal discountAmountMon = 0;
            if (Session["DiscountAmountMon"] != null) discountAmountMon = (decimal)Session["DiscountAmountMon"];
            
            decimal discountAmountShip = 0;
            if (Session["DiscountAmountShip"] != null) discountAmountShip = (decimal)Session["DiscountAmountShip"];

            // Recalculate ship discount just in case phiShip changed (due to address recalculation on checkout)
            if (Session["AppliedVoucherShip"] != null)
            {
                var recalc = ĐACN.Models.VoucherStore.TinhToanGiamGia((string)Session["AppliedVoucherShip"], "Ship", tongTienHang, phiShip);
                if (recalc.Success) discountAmountShip = recalc.DiscountAmount;
            }
            
            decimal tongCong = tongTienHang + totalShippingFee - discountAmountMon - discountAmountShip;
            if (tongCong < 0) tongCong = 0;

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
                        TrangThai = phuongThucTT == "VNPay" ? "Chờ thanh toán VNPay" : "Chờ xác nhận",
                        TongTien = tongCong,
                        ThoiGianDat = DateTime.Now,
                        Latitude = latKH,
                        Longitude = lngKH,
                        ShipFee = totalShippingFee
                    };
                    db.DonHangs.Add(don);

                    foreach (var item in cart)
                    {
                        db.ChiTietDonHangs.Add(new ChiTietDonHang
                        {
                            MaDon = maDon,
                            MaMon = item.MaMon,
                            SoLuong = item.SoLuong,
                            DonGia = item.DonGia, // Lấy nguyên DonGia từ Giỏ hàng (đã cộng Topping)
                            Note = item.Note
                        });
                    }
                    db.LichSuGioHangs.RemoveRange(cart);
                    db.SaveChanges();
                    transaction.Commit();
                    
                    // Xoá session voucher sau khi đặt hàng thành công
                    Session["AppliedVoucherMon"] = null;
                    Session["DiscountAmountMon"] = null;
                    Session["AppliedVoucherShip"] = null;
                    Session["DiscountAmountShip"] = null;
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
            else if (phuongThucTT == "VNPay")
            {
                string vnp_Returnurl = System.Configuration.ConfigurationManager.AppSettings["vnp_Returnurl"];
                string vnp_Url = System.Configuration.ConfigurationManager.AppSettings["vnp_Url"];
                string vnp_TmnCode = System.Configuration.ConfigurationManager.AppSettings["vnp_TmnCode"];
                string vnp_HashSecret = System.Configuration.ConfigurationManager.AppSettings["vnp_HashSecret"];

                ĐACN.Models.VnPayLibrary vnpay = new ĐACN.Models.VnPayLibrary();

                vnpay.AddRequestData("vnp_Version", ĐACN.Models.VnPayLibrary.VERSION);
                vnpay.AddRequestData("vnp_Command", "pay");
                vnpay.AddRequestData("vnp_TmnCode", vnp_TmnCode);
                vnpay.AddRequestData("vnp_Amount", (tongCong * 100).ToString("0")); 
                vnpay.AddRequestData("vnp_CreateDate", DateTime.Now.ToString("yyyyMMddHHmmss"));
                vnpay.AddRequestData("vnp_CurrCode", "VND");
                vnpay.AddRequestData("vnp_IpAddr", ĐACN.Models.Utils.GetIpAddress());
                vnpay.AddRequestData("vnp_Locale", "vn");
                vnpay.AddRequestData("vnp_OrderInfo", "Thanh toan don hang:" + maDon);
                vnpay.AddRequestData("vnp_OrderType", "other");
                vnpay.AddRequestData("vnp_ReturnUrl", vnp_Returnurl);
                vnpay.AddRequestData("vnp_TxnRef", maDon);

                string paymentUrl = vnpay.CreateRequestUrl(vnp_Url, vnp_HashSecret);
                return Redirect(paymentUrl);
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

        public async Task<ActionResult> ThanhToanQR(string maDon, decimal tongTien)
        {
            if (!KiemTraDangNhap()) return RedirectToAction("TrangChu", "Home");
            ViewBag.MaDon = maDon;
            ViewBag.TongTien = tongTien;
            ViewBag.QRCode = $"https://img.vietqr.io/image/970422-000012345678-compact2.png?amount={(int)tongTien}&addInfo={maDon}&accountName=TAPFOOD%20COMPANY";
            return View();
        }

        [HttpPost] 
        public async Task<ActionResult> XacNhanThanhToanQR(string maDon) 
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
        public async Task<JsonResult> GetDistanceNhaHangToKhachHang(string maNH, string diaChi, string phuongXa, string quanHuyen, string tinhTP, bool? luuDiaChi)
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

            if (string.IsNullOrEmpty(maNH))
            {
                return Json(new { success = false, message = "Vui lòng chọn món ăn trong giỏ hàng để tính phí giao hàng." }, JsonRequestBehavior.AllowGet);
            }

            var nh = db.NhaHangs.Find(maNH);
            if (nh == null)
            {
                return Json(new { success = false, message = "Không tìm thấy thông tin nhà hàng." }, JsonRequestBehavior.AllowGet);
            }
            double nhLat = nh.Latitude ?? 0, nhLng = nh.Longitude ?? 0;
            if (nhLat == 0)
            {
                var c = ValidateAddressRealtime(nh.DiaChi, nh.DiaChi);
                if (c.isValid) { nhLat = c.lat.Value; nhLng = c.lng.Value; }
                else return Json(new { success = false, message = "Không xác định được vị trí nhà hàng." }, JsonRequestBehavior.AllowGet);
            }


            var routeData = await _mapService.GetRouteDataORSAsync(nhLat, nhLng, check.lat.Value, check.lng.Value);
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
                dist = _mapService.CalculateHaversineDistance(nhLat, nhLng, check.lat.Value, check.lng.Value);
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


        public async Task<ActionResult> DonHangCuaToi()
        {
            if (!KiemTraDangNhap()) { TempData["Msg"] = "Vui lòng đăng nhập để xem đơn hàng!"; return RedirectToAction("TrangChu", "Home"); }
            string maKH = Session["MaKH"] as string;
            if (string.IsNullOrEmpty(maKH)) { var tk = Session["TaiKhoan"] as TaiKhoan; if (tk != null) { var kh = db.KhachHangs.FirstOrDefault(k => k.MaTK == tk.MaTK); if (kh != null) maKH = kh.MaKH; } }
            if (string.IsNullOrEmpty(maKH)) return RedirectToAction("TrangChu", "Home");

            var donCuaKhach = db.DonHangs.AsNoTracking().Where(d => d.MaKH == maKH).OrderByDescending(d => d.ThoiGianDat).ToList();

            var donHangDangXuLy = donCuaKhach.Where(d => d.TrangThai == "Chờ xác nhận" || d.TrangThai == "Đang giao" || d.TrangThai == "Đang lấy món").Select(d => new DonHangModel { MaDon = d.MaDon, MaKH = d.MaKH, MaNH = d.MaNH, TrangThai = d.TrangThai, TongTien = d.TongTien ?? 0, ThoiGianDat = d.ThoiGianDat ?? DateTime.Now }).ToList();
            var lichSuDonHang = donCuaKhach.Where(d => d.TrangThai != "Chờ xác nhận" && d.TrangThai != "Đang giao" && d.TrangThai != "Đang lấy món").Select(d => new DonHangModel { MaDon = d.MaDon, MaKH = d.MaKH, MaNH = d.MaNH, TrangThai = d.TrangThai, TongTien = d.TongTien ?? 0, ThoiGianDat = d.ThoiGianDat ?? DateTime.Now }).ToList();

            return View(new DonHangTongHopViewModel { DonHangDangXuLy = donHangDangXuLy, LichSuDonHang = lichSuDonHang });
        }

        public async Task<ActionResult> TheoDoiDonHang(string maDon)
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
        public async Task<JsonResult> GetTrackingInfo(string maDon)
        {
            if (!KiemTraDangNhap()) return Json(new { success = false, message = "Chưa đăng nhập" }, JsonRequestBehavior.AllowGet);
            string maKH = Session["MaKH"] as string;
            var don = db.DonHangs.Include(d => d.NhaHang).Include(d => d.KhachHang).FirstOrDefault(d => d.MaDon == maDon && d.MaKH == maKH);
            if (don == null) return Json(new { success = false, message = "Không tìm thấy đơn hoặc không có quyền" }, JsonRequestBehavior.AllowGet);


            double restLat = don.NhaHang?.Latitude ?? 0; double restLng = don.NhaHang?.Longitude ?? 0;
            if (restLat == 0 && don.NhaHang != null) { var c = await _mapService.GeoCodeORSAsync(don.NhaHang.DiaChi); if (c.lat.HasValue) { restLat = c.lat.Value; restLng = c.lng.Value; } }
            var restaurant = (restLat != 0) ? new { lat = restLat, lng = restLng, name = don.NhaHang?.TenNH } : null;


            double custLat = don.Latitude ?? 0; double custLng = don.Longitude ?? 0;
            if (custLat == 0) { var c = await _mapService.GeoCodeORSAsync(don.DiaChiGiaoHang); if (c.lat.HasValue) { custLat = c.lat.Value; custLng = c.lng.Value; } }
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
        public async Task<JsonResult> GetShipperRoute(string maDon)
        {
            var donHang = db.DonHangs.Include(d => d.NhaHang).FirstOrDefault(d => d.MaDon == maDon);
            if (donHang == null) return Json(new { success = false, message = "Không tìm thấy đơn hàng" }, JsonRequestBehavior.AllowGet);

            string currentMaKH = Session["MaKH"] as string;
            string currentMaShipper = Session["MaShipper"] as string;
            string currentMaNH = Session["MaNH"] as string;
            var tkSession = Session["TaiKhoan"] as TaiKhoan;

            bool isAuthorized = (tkSession?.VaiTro == "Admin")
                || (!string.IsNullOrEmpty(currentMaKH) && donHang.MaKH == currentMaKH)
                || (!string.IsNullOrEmpty(currentMaShipper) && donHang.MaShipper == currentMaShipper)
                || (!string.IsNullOrEmpty(currentMaNH) && donHang.MaNH == currentMaNH);

            if (!isAuthorized)
            {
                return Json(new { success = false, message = "Bạn không có quyền xem thông tin đơn hàng này" }, JsonRequestBehavior.AllowGet);
            }


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
                if (endLat == 0 && donHang.NhaHang != null) { var c = await _mapService.GeoCodeORSAsync(donHang.NhaHang.DiaChi); if (c.lat.HasValue) { endLat = c.lat.Value; endLng = c.lng.Value; } }
            }
            else if (status.Contains("đang giao"))
            {
                routeType = "ToCustomer";
                endLat = donHang.Latitude ?? 0; endLng = donHang.Longitude ?? 0;
                if (endLat == 0 && !string.IsNullOrEmpty(donHang.DiaChiGiaoHang)) { var c = await _mapService.GeoCodeORSAsync(donHang.DiaChiGiaoHang); if (c.lat.HasValue) { endLat = c.lat.Value; endLng = c.lng.Value; } }
            }

            if (startLat == 0 || startLng == 0 || endLat == 0 || endLng == 0) return Json(new { success = false, message = "Thiếu tọa độ" }, JsonRequestBehavior.AllowGet);


            object routeGeometry = null;
            double distanceMeters = 0;


            var routeData = await _mapService.GetRouteDataORSAsync(startLat, startLng, endLat, endLng);


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

                distanceMeters = _mapService.CalculateHaversineDistance(startLat, startLng, endLat, endLng);
                routeGeometry = GenerateManhattanRoute(startLat, startLng, endLat, endLng);
            }

            double distanceKm = Math.Round(distanceMeters / 1000.0, 1);
            double averageSpeedKmH = 30.0;
            double estimatedMinutes = Math.Ceiling(((distanceMeters / 1000.0) / averageSpeedKmH) * 60);
            if (estimatedMinutes < 1) estimatedMinutes = 1;

            return Json(new { success = true, route = routeGeometry, distanceText = $"{distanceKm} km", durationText = $"{estimatedMinutes} phút", statusText = routeType == "ToRestaurant" ? "Shipper đang đến nhà hàng" : "Shipper đang giao tới bạn", routeType = routeType }, JsonRequestBehavior.AllowGet);
        }

        public async Task<ActionResult> VietDanhGia(string maDon)
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
        public async Task<ActionResult> LuuDanhGia(DanhGiaViewModel model)
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
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("Lỗi lưu đánh giá: " + ex.Message); TempData["Msg"] = "Lỗi khi lưu đánh giá. Vui lòng thử lại sau."; return RedirectToAction("VietDanhGia", new { maDon = model.MaDon }); }
        }


        [HttpGet]
        public async Task<ActionResult> HoSo()
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
        public async Task<ActionResult> CapNhatHoSo(string tenKH, string sdt, string diaChi, HttpPostedFileBase hinhAnh)
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
        public async Task<ActionResult> CaiDat()
        {
            if (!KiemTraDangNhap())
            {
                TempData["Msg"] = "Vui lòng đăng nhập.";
                return RedirectToAction("TrangChu", "Home");
            }
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> DoiMatKhau(string matKhauCu, string matKhauMoi, string xacNhanMatKhau)
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
            await db.SaveChangesAsync();
            TempData["Msg"] = "Đổi mật khẩu thành công.";
            return RedirectToAction("CaiDat");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> XoaTaiKhoan()
        {
            if (!KiemTraDangNhap()) return RedirectToAction("TrangChu", "Home");

            var sessionTK = Session["TaiKhoan"] as TaiKhoan;
            var tk = db.TaiKhoans.Find(sessionTK.MaTK);

            if (tk != null)
            {
                tk.TrangThai = false;
                await db.SaveChangesAsync();

                Session.Clear();
                Session.Abandon();
                if (Response.Cookies["ASP.NET_SessionId"] != null)
                {
                    Response.Cookies["ASP.NET_SessionId"].Expires = DateTime.Now.AddDays(-1);
                }

                TempData["Msg"] = "Tài khoản của bạn đã được xóa thành công.";
                return RedirectToAction("TrangChu", "Home");
            }

            TempData["Msg"] = "Lỗi khi xóa tài khoản.";
            return RedirectToAction("CaiDat");
        }


        [HttpGet]
        public async Task<JsonResult> LaySoLuongGioHang()
        {
            if (!KiemTraDangNhap())
                return Json(new { success = false, soLuong = 0 }, JsonRequestBehavior.AllowGet);

            string maKH = Session["MaKH"] as string;
            XoaLichSuQuaHan();
            var tongSoLuong = db.LichSuGioHangs.Where(x => x.MaKH == maKH).Sum(x => (int?)x.SoLuong) ?? 0;
            return Json(new { success = true, soLuong = tongSoLuong }, JsonRequestBehavior.AllowGet);
        }

        [HttpGet]
        public async Task<JsonResult> LayThongTinGioHang()
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


        public async Task<ActionResult> MonAnTheoLoai(string maLoai)
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
                    TongLuotMua = db.DonHangs.Count(d => d.MaNH == nh.MaNH),
                    Rating = db.DanhGiaNhaHangs.Where(dg => dg.MaNH == nh.MaNH).Average(dg => (double?)dg.SoSao) ?? 5.0
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

            ViewBag.TatCaLoai = db.LoaiMonAns.Select(l => new LoaiMonAnViewModel
            {
                MaLoai = l.MaLoai,
                TenLoai = l.TenLoai,
                HinhAnh = l.HinhAnh
            }).ToList();

            return View(model);
        }


        [HttpPost]
        public async Task<JsonResult> LuuDanhGiaShipper(string maDon, string maShipper, int soSao, string binhLuan)
        {
            if (!KiemTraDangNhap())
                return Json(new { success = false, message = "Vui lòng đăng nhập!" }, JsonRequestBehavior.AllowGet);

            if (soSao < 1) soSao = 1;
            if (soSao > 5) soSao = 5;

            string maKH = Session["MaKH"] as string;
            try
            {
                var donHang = db.DonHangs.FirstOrDefault(d => d.MaDon == maDon && d.MaKH == maKH);
                if (donHang == null)
                    return Json(new { success = false, message = "Không tìm thấy đơn hàng!" }, JsonRequestBehavior.AllowGet);

                if (donHang.TrangThai != "Hoàn thành")
                    return Json(new { success = false, message = "Bạn chỉ có thể đánh giá đơn hàng đã hoàn thành!" }, JsonRequestBehavior.AllowGet);

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
                await db.SaveChangesAsync();

                // Cập nhật điểm đánh giá trung bình cho Shipper
                try
                {
                    var shipperObj = db.Shippers.FirstOrDefault(s => s.MaShipper == maShipper);
                    if (shipperObj != null)
                    {
                        var allRatings = db.DanhGiaShippers
                            .Where(d => d.MaShipper == maShipper && d.SoSao != null)
                            .Select(d => (double)d.SoSao.Value)
                            .ToList();
                        if (allRatings.Any())
                        {
                            shipperObj.DiemDanhGia = (decimal)Math.Round(allRatings.Average(), 1);
                            db.Entry(shipperObj).State = EntityState.Modified;
                            await db.SaveChangesAsync();
                        }
                    }
                }
                catch { }

                return Json(new { success = true, message = "Đánh giá shipper thành công!" }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Lỗi lưu đánh giá: {ex.Message}");
                return Json(new { success = false, message = "Lỗi khi lưu đánh giá. Vui lòng thử lại sau." }, JsonRequestBehavior.AllowGet);
            }
        }

        [HttpPost]
        public async Task<JsonResult> LuuDanhGiaNhaHang(string maDon, string maNH, int soSao, string binhLuan, HttpPostedFileBase hinhAnhFile = null)
        {
            if (!KiemTraDangNhap())
                return Json(new { success = false, message = "Vui lòng đăng nhập!" }, JsonRequestBehavior.AllowGet);

            if (soSao < 1) soSao = 1;
            if (soSao > 5) soSao = 5;

            string maKH = Session["MaKH"] as string;
            try
            {
                var donHang = db.DonHangs.FirstOrDefault(d => d.MaDon == maDon && d.MaKH == maKH);
                if (donHang == null)
                    return Json(new { success = false, message = "Không tìm thấy đơn hàng!" }, JsonRequestBehavior.AllowGet);

                if (donHang.TrangThai != "Hoàn thành")
                    return Json(new { success = false, message = "Bạn chỉ có thể đánh giá đơn hàng đã hoàn thành!" }, JsonRequestBehavior.AllowGet);


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
                LogError(ex, "DanhGiaNhaHang");
                return Json(new { success = false, message = "Lỗi khi lưu đánh giá. Vui lòng thử lại sau." }, JsonRequestBehavior.AllowGet);
            }
        }

        [HttpPost]
        public async Task<JsonResult> BoQuaDanhGia(string maDon)
        {
            if (!KiemTraDangNhap())
                return Json(new { success = false }, JsonRequestBehavior.AllowGet);


            return Json(new { success = true }, JsonRequestBehavior.AllowGet);
        }

        [HttpPost]
        public async Task<JsonResult> BoQuaDanhGiaNhaHang(string maDon)
        {
            if (!KiemTraDangNhap())
                return Json(new { success = false }, JsonRequestBehavior.AllowGet);


            return Json(new { success = true }, JsonRequestBehavior.AllowGet);
        }

        [HttpGet]
        public async Task<JsonResult> LayThongTinDanhGiaNhaHang(string maDon)
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
        public async Task<JsonResult> LayDonTiepTheoCanDanhGia(string[] skippedOrders)
        {
            if (!KiemTraDangNhap())
                return Json(new { success = false }, JsonRequestBehavior.AllowGet);

            string maKH = Session["MaKH"] as string;

            DateTime limitDate = DateTime.Now.AddDays(-7);

            var donCanDanhGiaList = db.DonHangs
                .Include(d => d.Shipper)
                .Where(d => d.MaKH == maKH &&
                             (d.TrangThai == "Hoàn thành" || d.TrangThai == "Hoàn tất") &&
                             !string.IsNullOrEmpty(d.MaShipper) &&
                             d.ThoiGianDat >= limitDate)
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


        public async Task<ActionResult> Logout()
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
        public async Task<JsonResult> GetOrderStatus(string maDon)
        {
            var don = db.DonHangs.FirstOrDefault(d => d.MaDon == maDon);
            if (don != null)
            {
                return Json(new { success = true, status = don.TrangThai }, JsonRequestBehavior.AllowGet);
            }
            return Json(new { success = false }, JsonRequestBehavior.AllowGet);
        }

        public async Task<ActionResult> VNPayReturn()
        {
            if (Request.QueryString.Count > 0)
            {
                string vnp_HashSecret = System.Configuration.ConfigurationManager.AppSettings["vnp_HashSecret"];
                var vnpayData = Request.QueryString;
                ĐACN.Models.VnPayLibrary vnpay = new ĐACN.Models.VnPayLibrary();

                foreach (string s in vnpayData)
                {
                    if (!string.IsNullOrEmpty(s) && s.StartsWith("vnp_"))
                    {
                        vnpay.AddResponseData(s, vnpayData[s]);
                    }
                }
                string vnp_ResponseCode = vnpay.GetResponseData("vnp_ResponseCode");
                string vnp_SecureHash = Request.QueryString["vnp_SecureHash"];

                bool checkSignature = vnpay.ValidateSignature(vnp_SecureHash, vnp_HashSecret);
                if (checkSignature)
                {
                    if (vnp_ResponseCode == "00")
                    {
                        // Thanh toán thành công
                        string maDon = vnpay.GetResponseData("vnp_TxnRef");
                        var donHang = db.DonHangs.FirstOrDefault(d => d.MaDon == maDon);
                        if (donHang != null)
                        {
                            // Kiểm tra số tiền trả về từ VNPay
                            string amountStr = vnpay.GetResponseData("vnp_Amount");
                            long vnpAmount = 0;
                            long.TryParse(amountStr, out vnpAmount);
                            long expectedAmount = (long)(donHang.TongTien ?? 0) * 100;

                            if (expectedAmount > 0 && Math.Abs(vnpAmount - expectedAmount) > 1000)
                            {
                                TempData["Msg"] = "Số tiền thanh toán VNPay không khớp với đơn hàng!";
                                return RedirectToAction("DonHangCuaToi");
                            }

                            if (donHang.TrangThai == "Chờ thanh toán VNPay")
                            {
                                donHang.TrangThai = "Chờ xác nhận";
                                await db.SaveChangesAsync();

                                try
                                {
                                    var context = Microsoft.AspNet.SignalR.GlobalHost.ConnectionManager.GetHubContext<ĐACN.Hubs.DeliveryHub>();
                                    context.Clients.Group("NhaHang_" + donHang.MaNH).notifyNewOrder($"Có đơn hàng VNPay mới: {maDon}");
                                }
                                catch { }
                            }
                        }
                        TempData["Msg"] = "Thanh toán VNPay thành công!";
                        return RedirectToAction("TheoDoiDonHang", new { maDon = maDon });
                    }
                    else
                    {
                        // Thanh toán lỗi
                        string maDon = vnpay.GetResponseData("vnp_TxnRef");
                        var donHang = db.DonHangs.FirstOrDefault(d => d.MaDon == maDon);
                        if (donHang != null && donHang.TrangThai == "Chờ thanh toán VNPay")
                        {
                            donHang.TrangThai = "Đã hủy";
                            db.SaveChanges();
                        }
                        TempData["Msg"] = "Thanh toán VNPay thất bại hoặc bị hủy.";
                        return RedirectToAction("DonHangCuaToi");
                    }
                }
                else
                {
                    TempData["Msg"] = "Chữ ký VNPay không hợp lệ.";
                    return RedirectToAction("DonHangCuaToi");
                }
            }
            return RedirectToAction("TrangChu", "Home");
        }

    }
}