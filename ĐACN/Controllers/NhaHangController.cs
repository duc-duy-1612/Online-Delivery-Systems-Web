using ĐACN.Models;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Web;
using System.Web.Mvc;

namespace ĐACN.Controllers
{
    public class NhaHangController : BaseController
    {



        private readonly ĐACN.Services.CacheService _cacheService = new ĐACN.Services.CacheService();

        private bool CheckLogin()
        {
            var tk = Session["TaiKhoan"] as TaiKhoan;

            if (tk == null || tk.VaiTro != "NhaHang" || tk.TrangThai != true)
                return false;

            if (Session["MaNH"] == null)
            {
                var nh = db.NhaHangs.FirstOrDefault(n => n.MaTK == tk.MaTK);
                if (nh != null)
                {
                    Session["MaNH"] = nh.MaNH;
                }
                else
                {
                    return false;
                }
            }

            return true;
        }

        private ActionResult RedirectLogin()
        {
            return RedirectToAction("Login", "Account");
        }





        public ActionResult Index()
        {
            return RedirectToAction("ThongKe");
        }


        public ActionResult ThongKe(string timeRange = "Ngày", string statType = "Thống kê doanh thu", DateTime? tuNgay = null, DateTime? denNgay = null)
        {
            if (!CheckLogin()) return RedirectLogin();

            var maNH = Session["MaNH"]?.ToString();
            if (string.IsNullOrEmpty(maNH)) return RedirectToAction("TrangChu", "Home");

            ViewBag.CurrentTimeRange = timeRange;
            ViewBag.CurrentStatType = statType;
            ViewBag.ChartLabel = statType == "Thống kê doanh thu" ? "Doanh thu (VNĐ)" : "Số lượng (đơn)";
            ViewBag.TuNgay = tuNgay?.ToString("yyyy-MM-dd");
            ViewBag.DenNgay = denNgay?.ToString("yyyy-MM-dd");

            var queryDonHang = db.DonHangs.Where(d => d.MaNH == maNH && d.ThoiGianDat != null);

            // TÍNH TOÁN CÁC CHỈ SỐ KPI TỔNG QUAN
            DateTime today = DateTime.Today;
            DateTime yesterday = today.AddDays(-1);

            var donHomNay = queryDonHang.Where(d => d.ThoiGianDat >= today).ToList();
            var donHomQua = queryDonHang.Where(d => d.ThoiGianDat >= yesterday && d.ThoiGianDat < today).ToList();

            double doanhThuHomNay = donHomNay.Where(d => d.TrangThai == "Hoàn thành" || d.TrangThai == "Hoàn tất").Sum(d => (double?)d.TongTien) ?? 0;
            double doanhThuHomQua = donHomQua.Where(d => d.TrangThai == "Hoàn thành" || d.TrangThai == "Hoàn tất").Sum(d => (double?)d.TongTien) ?? 0;
            
            ViewBag.DoanhThuHomNay = doanhThuHomNay;
            ViewBag.PhanTramTangTruong = doanhThuHomQua == 0 ? 100 : Math.Round(((doanhThuHomNay - doanhThuHomQua) / doanhThuHomQua) * 100, 1);
            ViewBag.DonHangHomNay = donHomNay.Count;
            ViewBag.DonChoXuLy = queryDonHang.Count(d => d.TrangThai == "Chờ xác nhận" || d.TrangThai == "Đang chuẩn bị");

            // ÁP DỤNG BỘ LỌC NGÀY CHO BIỂU ĐỒ & TOP
            var filteredDonHang = queryDonHang;
            if (tuNgay.HasValue) filteredDonHang = filteredDonHang.Where(d => d.ThoiGianDat >= tuNgay.Value);
            if (denNgay.HasValue) {
                var denNgayCuoiNgay = denNgay.Value.AddDays(1).AddTicks(-1);
                filteredDonHang = filteredDonHang.Where(d => d.ThoiGianDat <= denNgayCuoiNgay);
            }

            ViewBag.TongDoanhThuLoc = filteredDonHang.Where(d => d.TrangThai == "Hoàn thành" || d.TrangThai == "Hoàn tất").Sum(d => (double?)d.TongTien) ?? 0;

            // DOANH THU THEO DANH MỤC
            var donHangIds = filteredDonHang.Select(d => d.MaDon).ToList();
            var doanhThuTheoDanhMuc = db.ChiTietDonHangs
                .Where(ct => donHangIds.Contains(ct.MaDon))
                .GroupBy(ct => ct.MonAn.MaLoai)
                .Select(g => new
                {
                    Loai = g.FirstOrDefault().MonAn.LoaiMonAn.TenLoai,
                    DoanhThu = g.Sum(x => x.SoLuong * x.DonGia)
                }).ToList();

            ViewBag.LabelsLoai = doanhThuTheoDanhMuc.Select(x => x.Loai).ToList();
            ViewBag.DataDoanhThuLoai = doanhThuTheoDanhMuc.Select(x => x.DoanhThu).ToList();

            // DOANH THU THEO THỜI GIAN
            List<string> labelsThoiGian = new List<string>();
            List<double> dataThoiGian = new List<double>();

            if (timeRange == "Ngày")
            {
                var grouped = filteredDonHang
                    .GroupBy(d => new { d.ThoiGianDat.Value.Year, d.ThoiGianDat.Value.Month, d.ThoiGianDat.Value.Day })
                    .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month).ThenBy(g => g.Key.Day)
                    .ToList()
                    .Select(g => new {
                        Label = $"{g.Key.Day:00}/{g.Key.Month:00}/{g.Key.Year}",
                        Value = statType == "Thống kê doanh thu" ? (double?)g.Sum(x => x.TongTien) : (double?)g.Count()
                    }).ToList();
                labelsThoiGian = grouped.Select(x => x.Label).ToList();
                dataThoiGian = grouped.Select(x => (double)(x.Value ?? 0)).ToList();
            }
            else if (timeRange == "Quý")
            {
                var grouped = filteredDonHang
                    .GroupBy(d => new { d.ThoiGianDat.Value.Year, Quy = (d.ThoiGianDat.Value.Month - 1) / 3 + 1 })
                    .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Quy)
                    .ToList()
                    .Select(g => new {
                        Label = $"Quý {g.Key.Quy}/{g.Key.Year}",
                        Value = statType == "Thống kê doanh thu" ? (double?)g.Sum(x => x.TongTien) : (double?)g.Count()
                    }).ToList();
                labelsThoiGian = grouped.Select(x => x.Label).ToList();
                dataThoiGian = grouped.Select(x => (double)(x.Value ?? 0)).ToList();
            }
            else // Mặc định là Tháng
            {
                var grouped = filteredDonHang
                    .GroupBy(d => new { d.ThoiGianDat.Value.Year, d.ThoiGianDat.Value.Month })
                    .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month)
                    .ToList()
                    .Select(g => new {
                        Label = $"Tháng {g.Key.Month:00}/{g.Key.Year}",
                        Value = statType == "Thống kê doanh thu" ? (double?)g.Sum(x => x.TongTien) : (double?)g.Count()
                    }).ToList();
                labelsThoiGian = grouped.Select(x => x.Label).ToList();
                dataThoiGian = grouped.Select(x => (double)(x.Value ?? 0)).ToList();
            }

            ViewBag.LabelsThoiGian = labelsThoiGian;
            ViewBag.DataDoanhThu = dataThoiGian;

            // TRẠNG THÁI ĐƠN HÀNG
            var listTrangThai = filteredDonHang.Select(d => d.TrangThai).ToList();
            int dangXuLy = listTrangThai.Count(t => t != "Hoàn thành" && t != "Hoàn tất" && t != "Đã hủy" && t != "Hủy");
            int daGiao = listTrangThai.Count(t => t == "Hoàn thành" || t == "Hoàn tất");
            int daHuy = listTrangThai.Count(t => t == "Đã hủy" || t == "Hủy");

            ViewBag.DataTrangThaiDonHang = new List<int> { dangXuLy, daGiao, daHuy };

            // MÓN ĂN BÁN CHẠY
            var topSanPham = db.ChiTietDonHangs
                .Where(ct => donHangIds.Contains(ct.MaDon))
                .GroupBy(ct => ct.MaMon)
                .Select(g => new ĐACN.Models.TopSanPhamViewModel
                {
                    TenMon = g.FirstOrDefault().MonAn.TenMon,
                    Gia = g.FirstOrDefault().MonAn.Gia,
                    SoLuongBan = g.Sum(x => x.SoLuong)
                })
                .OrderByDescending(x => x.SoLuongBan)
                .Take(5)
                .ToList();

            ViewBag.TopSanPham = topSanPham;

            return View();
        }


        public ActionResult ThongTinCuaHang(bool? edit)
        {
            if (!CheckLogin()) return RedirectLogin();

            string maNH = Session["MaNH"]?.ToString();
            var nhaHang = db.NhaHangs.FirstOrDefault(n => n.MaNH == maNH);

            if (nhaHang == null) 
            {
                Session.Clear();
                return RedirectToAction("Login", "Account");
            }

            var tk = db.TaiKhoans.FirstOrDefault(t => t.MaTK == nhaHang.MaTK);
            ViewBag.MatKhauHienTai = tk?.MatKhau ?? "";
            ViewBag.EditMode = edit ?? false;

            return View(nhaHang);
        }

        [HttpPost]
        public ActionResult CapNhatThongTinCuaHang(ĐACN.Models.NhaHang model, HttpPostedFileBase HinhAnhMoi, string MatKhauCu, string MatKhauMoi, string XacNhanMatKhau)
        {
            if (!CheckLogin()) return RedirectLogin();

            var maNH = Session["MaNH"]?.ToString();
            if (model.MaNH != maNH) return HttpNotFound();

            var nhaHang = db.NhaHangs.FirstOrDefault(n => n.MaNH == maNH);
            if (nhaHang == null) return HttpNotFound();

            var tk = db.TaiKhoans.FirstOrDefault(t => t.MaTK == nhaHang.MaTK);


            if (!string.IsNullOrEmpty(MatKhauMoi))
            {
                if (tk.MatKhau != MatKhauCu)
                {
                    ViewBag.Error = "Mật khẩu cũ không chính xác!";
                    ViewBag.MatKhauHienTai = tk.MatKhau;
                    ViewBag.EditMode = true;
                    return View("ThongTinCuaHang", nhaHang);
                }
                if (MatKhauMoi != XacNhanMatKhau)
                {
                    ViewBag.Error = "Xác nhận mật khẩu không khớp!";
                    ViewBag.MatKhauHienTai = tk.MatKhau;
                    ViewBag.EditMode = true;
                    return View("ThongTinCuaHang", nhaHang);
                }
                tk.MatKhau = MatKhauMoi;
            }


            nhaHang.TenNH = model.TenNH;
            nhaHang.DiaChi = model.DiaChi;
            nhaHang.SDT = model.SDT;
            nhaHang.MoTa = model.MoTa;

            db.SaveChanges();
            ViewBag.Success = "Cập nhật thông tin thành công!";
            ViewBag.MatKhauHienTai = tk?.MatKhau ?? "";
            ViewBag.EditMode = false;

            return View("ThongTinCuaHang", nhaHang);
        }


        [HttpPost]
        public ActionResult TaiLenAnhCuaHang(string MaNH, HttpPostedFileBase HinhAnhMoi)
        {
            if (!CheckLogin()) return RedirectLogin();

            var maNHDangNhap = Session["MaNH"]?.ToString();
            if (MaNH != maNHDangNhap) return HttpNotFound();

            var nhaHang = db.NhaHangs.FirstOrDefault(n => n.MaNH == maNHDangNhap);
            if (nhaHang == null) return HttpNotFound();

            if (HinhAnhMoi == null || HinhAnhMoi.ContentLength == 0)
            {
                ViewBag.Error = "Vui lòng chọn file ảnh!";
                ViewBag.EditMode = true;
                var tk = db.TaiKhoans.FirstOrDefault(t => t.MaTK == nhaHang.MaTK);
                ViewBag.MatKhauHienTai = tk?.MatKhau ?? "";
                return View("ThongTinCuaHang", nhaHang);
            }

            string errorMsg;
            if (!ValidateImageFile(HinhAnhMoi, out errorMsg))
            {
                ViewBag.Error = errorMsg;
                ViewBag.EditMode = true;
                var tk = db.TaiKhoans.FirstOrDefault(t => t.MaTK == nhaHang.MaTK);
                ViewBag.MatKhauHienTai = tk?.MatKhau ?? "";
                return View("ThongTinCuaHang", nhaHang);
            }


            if (!string.IsNullOrEmpty(nhaHang.HinhAnh))
            {
                var oldPath = Server.MapPath("~/images/nhahang/" + nhaHang.HinhAnh);
                if (System.IO.File.Exists(oldPath))
                {
                    try
                    {
                        System.IO.File.Delete(oldPath);
                    }
                    catch
                    {

                    }
                }
            }


            string fileName = Path.GetFileNameWithoutExtension(HinhAnhMoi.FileName) + "_" + DateTime.Now.Ticks + Path.GetExtension(HinhAnhMoi.FileName);
            string savePath = Server.MapPath("~/images/nhahang/" + fileName);
            HinhAnhMoi.SaveAs(savePath);
            nhaHang.HinhAnh = fileName;

            db.SaveChanges();

            ViewBag.Success = "Tải ảnh thành công!";
            ViewBag.EditMode = true;
            var tk2 = db.TaiKhoans.FirstOrDefault(t => t.MaTK == nhaHang.MaTK);
            ViewBag.MatKhauHienTai = tk2?.MatKhau ?? "";

            return View("ThongTinCuaHang", nhaHang);
        }


        [HttpPost]
        public JsonResult ToggleTrangThaiMonAn(string id, bool isChecked)
        {
            if (!CheckLogin()) return Json(new { success = false, message = "Chưa đăng nhập" });
            var maNH = Session["MaNH"]?.ToString();
            var mon = db.MonAns.FirstOrDefault(m => m.MaMon == id && m.MaNH == maNH);
            if (mon != null)
            {
                mon.TrangThai = isChecked;
                db.SaveChanges();
                _cacheService.Remove("Menu_" + maNH);
                return Json(new { success = true });
            }
            return Json(new { success = false, message = "Không tìm thấy món ăn" });
        }

        public ActionResult QuanLySanPham()
        {
            if (!CheckLogin()) return RedirectLogin();

            var maNH = Session["MaNH"]?.ToString();
            if (string.IsNullOrEmpty(maNH)) return RedirectLogin();

            var monAn = db.MonAns
                              .Where(m => m.MaNH == maNH)
                              .Include(m => m.LoaiMonAn)
                              .ToList();

            return View(monAn);
        }

        [HttpGet]
        public ActionResult ThemSanPham()
        {
            if (!CheckLogin()) return RedirectLogin();
            ViewBag.MaLoai = new SelectList(db.LoaiMonAns.ToList(), "MaLoai", "TenLoai");
            return View();
        }

        [HttpPost]
        public ActionResult ThemSanPham(MonAn monAn, HttpPostedFileBase HinhAnhFile)
        {
            if (!CheckLogin()) return RedirectLogin();

            try
            {

                var maNH = Session["MaNH"]?.ToString();
                if (string.IsNullOrEmpty(maNH))
                {
                    TempData["Error"] = "Không tìm thấy thông tin nhà hàng!";
                    ViewBag.MaLoai = new SelectList(db.LoaiMonAns.ToList(), "MaLoai", "TenLoai", monAn.MaLoai);
                    return View(monAn);
                }


                if (!string.IsNullOrWhiteSpace(monAn.MaLoai))
                {
                    var loaiTonTai = db.LoaiMonAns.Any(l => l.MaLoai == monAn.MaLoai);
                    if (!loaiTonTai)
                    {
                        TempData["Error"] = $"Danh mục '{monAn.MaLoai}' không tồn tại trong hệ thống!";
                        ViewBag.MaLoai = new SelectList(db.LoaiMonAns.ToList(), "MaLoai", "TenLoai", monAn.MaLoai);
                        return View(monAn);
                    }
                }


                var nhaHangTonTai = db.NhaHangs.Any(n => n.MaNH == maNH);
                if (!nhaHangTonTai)
                {
                    TempData["Error"] = $"Nhà hàng '{maNH}' không tồn tại trong hệ thống!";
                    ViewBag.MaLoai = new SelectList(db.LoaiMonAns.ToList(), "MaLoai", "TenLoai", monAn.MaLoai);
                    return View(monAn);
                }


                if (HinhAnhFile != null && HinhAnhFile.ContentLength > 0)
                {

                    string errorMsg;
                    if (!ValidateImageFile(HinhAnhFile, out errorMsg))
                    {
                        TempData["Error"] = errorMsg;
                        ViewBag.MaLoai = new SelectList(db.LoaiMonAns.ToList(), "MaLoai", "TenLoai", monAn.MaLoai);
                        return View(monAn);
                    }

                    var ext = Path.GetExtension(HinhAnhFile.FileName).ToLower();

                    string fileName = Path.GetFileNameWithoutExtension(HinhAnhFile.FileName) + "_" + DateTime.Now.Ticks + ext;
                    string path = Path.Combine(Server.MapPath("~/images/monan"), fileName);


                    var directory = Path.GetDirectoryName(path);
                    if (!Directory.Exists(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    HinhAnhFile.SaveAs(path);
                    monAn.HinhAnh = fileName;
                }


                var maMonMoi = TaoMaMonTuTang();


                var maMonTrung = db.MonAns.Any(m => m.MaMon == maMonMoi);
                if (maMonTrung)
                {
                    var existingCodes = new HashSet<string>(db.MonAns.Where(m => m.MaMon.StartsWith("MA")).Select(m => m.MaMon));
                    string maMonTmp = maMonMoi;
                    int so = 1;
                    if (int.TryParse(maMonTmp.Substring(2), out int parsedSo)) so = parsedSo;
                    while (existingCodes.Contains(maMonTmp) && so < 9999)
                    {
                        so++;
                        maMonTmp = "MA" + so.ToString("D3");
                    }
                    maMonMoi = maMonTmp;
                }

                monAn.MaMon = maMonMoi;
                monAn.TrangThai = true;
                monAn.MaNH = maNH;

                db.MonAns.Add(monAn);
                db.SaveChanges();
                _cacheService.Remove("Menu_" + maNH);

                TempData["Success"] = "Thêm món ăn thành công!";
                return RedirectToAction("QuanLySanPham");
            }
            catch (System.Data.Entity.Infrastructure.DbUpdateException dbEx)
            {

                string errorMsg = "Lỗi khi thêm sản phẩm: ";
                var innerEx = dbEx.InnerException;
                while (innerEx != null)
                {
                    errorMsg += innerEx.Message + " ";
                    innerEx = innerEx.InnerException;
                }
                TempData["Error"] = errorMsg;
            }
            catch (System.Data.SqlClient.SqlException sqlEx)
            {
                System.Diagnostics.Debug.WriteLine("Lỗi SQL khi thêm món: " + sqlEx.Message);
                string errorMsg = "Lỗi cơ sở dữ liệu: ";
                if (sqlEx.Number == 2601 || sqlEx.Number == 2627)
                {
                    errorMsg += "Mã món đã tồn tại!";
                }
                else if (sqlEx.Number == 547)
                {
                    errorMsg += "Dữ liệu không hợp lệ. Vui lòng kiểm tra danh mục và nhà hàng.";
                }
                else
                {
                    errorMsg += "Vui lòng thử lại sau.";
                }
                TempData["Error"] = errorMsg;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Lỗi khi thêm món: " + ex.Message);
                TempData["Error"] = "Lỗi khi thêm món ăn. Vui lòng thử lại sau.";
            }

            ViewBag.MaLoai = new SelectList(db.LoaiMonAns.ToList(), "MaLoai", "TenLoai", monAn.MaLoai);
            return View(monAn);
        }



        private string TaoMaMonTuTang()
        {
            var maNH = Session["MaNH"]?.ToString();
            if (string.IsNullOrEmpty(maNH))
            {

                return TaoMaMonTuTangGlobal();
            }


            var monAnCuaNhaHang = db.MonAns
                .Where(m => m.MaNH == maNH && m.MaMon.StartsWith("MA") && m.MaMon.Length == 5)
                .Select(m => m.MaMon)
                .ToList();

            if (!monAnCuaNhaHang.Any())
            {

                if (!db.MonAns.Any(m => m.MaMon == "MA001"))
                {
                    return "MA001";
                }

                return TaoMaMonTuTangGlobal();
            }


            var maxMa = monAnCuaNhaHang
                .Where(m => m.Length == 5 && m.Substring(0, 2) == "MA")
                .Select(m =>
                {
                    if (int.TryParse(m.Substring(2), out int so))
                        return so;
                    return 0;
                })
                .DefaultIfEmpty(0)
                .Max();

            int soMoi = maxMa + 1;
            string maMonMoi = "MA" + soMoi.ToString("D3");


            if (!db.MonAns.Any(m => m.MaMon == maMonMoi))
            {
                return maMonMoi;
            }


            return TaoMaMonTuTangGlobal();
        }


        private string TaoMaMonTuTangGlobal()
        {
            var maxMaMon = db.MonAns
                .Where(m => m.MaMon.StartsWith("MA") && m.MaMon.Length == 5)
                .OrderByDescending(m => m.MaMon)
                .Select(m => m.MaMon)
                .FirstOrDefault();

            if (string.IsNullOrEmpty(maxMaMon))
            {
                return "MA001";
            }

            if (int.TryParse(maxMaMon.Substring(2), out int so))
            {
                return "MA" + (so + 1).ToString("D3");
            }

            return "MA" + (DateTime.Now.Ticks % 1000).ToString("D3");
        }

        [HttpGet]
        public ActionResult ChinhSuaSanPham(string id)
        {
            if (!CheckLogin()) return RedirectLogin();
            var maNH = Session["MaNH"]?.ToString();
            var mon = db.MonAns.FirstOrDefault(m => m.MaMon == id && m.MaNH == maNH);
            if (mon == null) return HttpNotFound();

            ViewBag.MaLoai = new SelectList(db.LoaiMonAns.ToList(), "MaLoai", "TenLoai", mon.MaLoai);
            return View(mon);
        }

        [HttpPost]
        public ActionResult ChinhSuaSanPham(MonAn monAn, HttpPostedFileBase HinhAnhFile)
        {
            if (!CheckLogin()) return RedirectLogin();
            var maNH = Session["MaNH"]?.ToString();
            try
            {
                var old = db.MonAns.FirstOrDefault(m => m.MaMon == monAn.MaMon && m.MaNH == maNH);
                if (old == null) return HttpNotFound();

                old.TenMon = monAn.TenMon;
                old.MaLoai = monAn.MaLoai;
                old.Gia = monAn.Gia;
                old.MoTa = monAn.MoTa;
                old.TrangThai = monAn.TrangThai;

                if (HinhAnhFile != null && HinhAnhFile.ContentLength > 0)
                {

                    string errorMsg;
                    if (!ValidateImageFile(HinhAnhFile, out errorMsg))
                    {
                        TempData["Error"] = errorMsg;
                        ViewBag.MaLoai = new SelectList(db.LoaiMonAns.ToList(), "MaLoai", "TenLoai", monAn.MaLoai);
                        return View(monAn);
                    }

                    string fileName = Path.GetFileNameWithoutExtension(HinhAnhFile.FileName) + "_" + DateTime.Now.Ticks + Path.GetExtension(HinhAnhFile.FileName);
                    string path = Path.Combine(Server.MapPath("~/images/monan"), fileName);
                    HinhAnhFile.SaveAs(path);
                    old.HinhAnh = fileName;
                }

                db.SaveChanges();
                _cacheService.Remove("Menu_" + maNH);
                TempData["Success"] = "Cập nhật thành công!";
                return RedirectToAction("QuanLySanPham");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Lỗi cập nhật món: " + ex.Message);
                TempData["Error"] = "Lỗi khi cập nhật. Vui lòng thử lại sau.";
                ViewBag.MaLoai = new SelectList(db.LoaiMonAns.ToList(), "MaLoai", "TenLoai", monAn.MaLoai);
                return View(monAn);
            }
        }

        public ActionResult XoaSanPham(string id)
        {
            if (!CheckLogin()) return RedirectLogin();
            var maNH = Session["MaNH"]?.ToString();
            try
            {
                var mon = db.MonAns.FirstOrDefault(m => m.MaMon == id && m.MaNH == maNH);
                if (mon == null) return HttpNotFound();
                
                mon.TrangThai = false; // Soft delete: Ngừng bán thay vì xóa cứng
                db.SaveChanges();
                _cacheService.Remove("Menu_" + maNH);
                TempData["Success"] = "Đã chuyển sản phẩm sang trạng thái ngừng bán!";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Lỗi xóa món: " + ex.Message);
                TempData["Error"] = "Lỗi khi xóa. Vui lòng thử lại sau.";
            }
            return RedirectToAction("QuanLySanPham");
        }


        [HttpGet]
        public ActionResult DanhSachDonHang(DateTime? tuNgay, DateTime? denNgay)
        {
            if (!CheckLogin()) return RedirectLogin();

            string maNH = Session["MaNH"]?.ToString();
            if (string.IsNullOrEmpty(maNH)) return RedirectLogin();

            var query = db.DonHangs
                              .Where(d => d.MaNH == maNH);

            if (tuNgay.HasValue) query = query.Where(d => d.ThoiGianDat >= tuNgay.Value);
            if (denNgay.HasValue)
            {
                DateTime endOfDay = denNgay.Value.Date.AddDays(1).AddTicks(-1);
                query = query.Where(d => d.ThoiGianDat <= endOfDay);
            }


            var rawList = query
                .Select(d => new
                {
                    d.MaDon,
                    TenKhachHang = d.KhachHang != null ? d.KhachHang.TenKH : null,
                    d.ThoiGianDat,
                    d.TongTien,
                    d.TrangThai,
                    TenShipper = d.Shipper != null ? d.Shipper.TenShipper : null,
                    ChiTiet = d.ChiTietDonHangs.Select(ct => new
                    {
                        TenMon = ct.MonAn != null ? ct.MonAn.TenMon : "Món ăn",
                        SoLuong = ct.SoLuong ?? 1,
                        GhiChu = ct.Note
                    })
                })
                .OrderByDescending(d => d.ThoiGianDat)
                .ToList();

            var donHangs = rawList.Select(d => new ĐACN.Models.DonHangListViewModel
            {
                MaDon = d.MaDon,
                TenKhachHang = d.TenKhachHang,
                ThoiGianDat = d.ThoiGianDat,
                TongTien = d.TongTien,
                TrangThai = d.TrangThai,
                TenShipper = d.TenShipper,
                DanhSachMon = d.ChiTiet.Select(ct => new ĐACN.Models.DonHangItemSummary
                {
                    TenMon = ct.TenMon,
                    SoLuong = ct.SoLuong,
                    GhiChu = ct.GhiChu
                }).ToList()
            }).ToList();

            ViewBag.TuNgay = tuNgay?.ToString("yyyy-MM-dd");
            ViewBag.DenNgay = denNgay?.ToString("yyyy-MM-dd");

            return View(donHangs);
        }


        [HttpGet]
        [OutputCache(NoStore = true, Duration = 0)]
        public JsonResult GetDanhSachDonHangUpdate(DateTime? tuNgay, DateTime? denNgay)
        {
            if (!CheckLogin())
                return Json(new { success = false, message = "Chưa đăng nhập" }, JsonRequestBehavior.AllowGet);

            string maNH = Session["MaNH"]?.ToString();
            if (string.IsNullOrEmpty(maNH))
                return Json(new { success = false, message = "Không tìm thấy nhà hàng" }, JsonRequestBehavior.AllowGet);

            var query = db.DonHangs.Where(d => d.MaNH == maNH);

            if (tuNgay.HasValue) query = query.Where(d => d.ThoiGianDat >= tuNgay.Value);
            if (denNgay.HasValue)
            {
                DateTime endOfDay = denNgay.Value.Date.AddDays(1).AddTicks(-1);
                query = query.Where(d => d.ThoiGianDat <= endOfDay);
            }

            var donHangs = query
                .Select(d => new
                {
                    MaDon = d.MaDon,
                    TenKhachHang = d.KhachHang != null ? d.KhachHang.TenKH : null,
                    ThoiGianDat = d.ThoiGianDat,
                    TongTien = d.TongTien,
                    TrangThai = d.TrangThai,
                    TenShipper = d.Shipper != null ? d.Shipper.TenShipper : null
                })
                .OrderByDescending(d => d.ThoiGianDat)
                .ToList();

            return Json(new
            {
                success = true,
                orders = donHangs.Select(d => new
                {
                    d.MaDon,
                    d.TenKhachHang,
                    ThoiGianDat = d.ThoiGianDat.HasValue ? d.ThoiGianDat.Value.ToString("dd/MM/yyyy HH:mm") : "",
                    TongTien = d.TongTien ?? 0,
                    d.TrangThai,
                    d.TenShipper,
                    TrangThaiDisplay = d.TrangThai == "True" ? "Hoàn tất" : (d.TrangThai ?? "Đang xử lý"),
                    TrangThaiClass = d.TrangThai == "True" ? "bg-success" : "bg-warning text-dark"
                })
            }, JsonRequestBehavior.AllowGet);
        }


        [HttpGet]
        public ActionResult ChiTietDonHang(string id)
        {
            if (!CheckLogin()) return RedirectLogin();
            var maNH = Session["MaNH"]?.ToString();

            var donHang = db.DonHangs
                                 .Include(d => d.KhachHang)
                                 .Include(d => d.Shipper)
                                 .FirstOrDefault(d => d.MaDon == id && d.MaNH == maNH);

            if (donHang == null) return HttpNotFound();



            if (donHang.Shipper != null)
            {
                var shipperName = donHang.Shipper.TenShipper;
            }

            var chiTiet = db.ChiTietDonHangs
                                 .Include(ct => ct.MonAn)
                                 .Where(ct => ct.MaDon == id)
                                 .ToList();

            ViewBag.ChiTiet = chiTiet;
            return View(donHang);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult CapNhatTrangThaiTuChiTiet(string maDon, string trangThai)
        {
            if (!CheckLogin()) return RedirectLogin();
            var maNH = Session["MaNH"]?.ToString();

            var don = db.DonHangs.FirstOrDefault(d => d.MaDon == maDon && d.MaNH == maNH);
            if (don != null)
            {
                don.TrangThai = trangThai;
                try
                {
                    db.SaveChanges();
                    TempData["Msg"] = "Đã cập nhật trạng thái đơn hàng!";

                    // SignalR Notification
                    try
                    {
                        var context = Microsoft.AspNet.SignalR.GlobalHost.ConnectionManager.GetHubContext<ĐACN.Hubs.DeliveryHub>();
                        if (trangThai == "Đã xác nhận" && string.IsNullOrEmpty(don.MaShipper))
                        {
                            ĐACN.Services.OrderDispatcher.DispatchOrder(don.MaDon, don.MaNH);
                        }
                    }
                    catch { }
                }
                catch (System.Data.Entity.Validation.DbEntityValidationException dbEx)
                {
                    string errorMsg = "";
                    foreach (var validationErrors in dbEx.EntityValidationErrors)
                    {
                        foreach (var validationError in validationErrors.ValidationErrors)
                        {
                            errorMsg += string.Format("Property: {0} Error: {1} | ", validationError.PropertyName, validationError.ErrorMessage);
                        }
                    }
                    TempData["Msg"] = "Lỗi validation: " + errorMsg;
                }
                catch (Exception ex)
                {
                    Exception inner = ex;
                    while (inner.InnerException != null) inner = inner.InnerException;
                    TempData["Msg"] = "Lỗi: " + inner.Message;
                }
            }
            return RedirectToAction("DanhSachDonHang");
        }


        public ActionResult XemDanhGia()
        {
            if (!CheckLogin()) return RedirectLogin();

            string maNH = Session["MaNH"]?.ToString();
            if (string.IsNullOrEmpty(maNH)) return RedirectLogin();


            var rawReviews = db.DanhGiaNhaHangs
                                 .Include(d => d.KhachHang)
                                 .Include(d => d.DonHang)
                                 .Where(d => d.MaNH == maNH)
                                 .OrderByDescending(d => d.ThoiGian)
                                 .ToList();


            var danhGia = rawReviews.Select(r => new ĐACN.Models.DanhGiaNhaHangDisplayViewModel
            {
                MaDon = r.MaDon,
                TenKhachHang = r.KhachHang?.TenKH ?? "Khách hàng",
                Diem = r.SoSao,

                NhanXet = r.BinhLuan,
                ThoiGianDat = r.DonHang?.ThoiGianDat,
                ThoiGianDanhGia = r.ThoiGian
            }).ToList();

            ViewBag.SoLuongDanhGia = danhGia.Count;


            double diemTB = 0;
            if (danhGia.Any())
            {
                diemTB = (double)danhGia.Where(x => x.Diem.HasValue).Average(x => x.Diem.Value);
            }
            ViewBag.DiemTrungBinh = Math.Round(diemTB, 1);

            return View(danhGia);
        }


        public ActionResult PhanHoi()
        {
            if (!CheckLogin()) return RedirectLogin();

            string maNH = Session["MaNH"]?.ToString();
            if (string.IsNullOrEmpty(maNH)) return RedirectLogin();



            var rawReviews = db.DanhGiaNhaHangs
                                 .Include(d => d.KhachHang)
                                 .Where(d => d.MaNH == maNH)
                                 .OrderByDescending(d => d.ThoiGian)
                                 .ToList();


            // Pre-fetch related order timestamps to eliminate N+1 database queries
            var validMaDons = rawReviews
                .Where(r => !string.IsNullOrEmpty(r.MaDon) && !r.MaDon.StartsWith("RATE_"))
                .Select(r => r.MaDon)
                .Distinct()
                .ToList();

            var donHangDict = db.DonHangs
                .Where(d => validMaDons.Contains(d.MaDon))
                .Select(d => new { d.MaDon, d.ThoiGianDat })
                .ToDictionary(d => d.MaDon, d => d.ThoiGianDat);

            var danhGia = new List<ĐACN.Models.DanhGiaNhaHangDisplayViewModel>();
            foreach (var r in rawReviews)
            {
                DateTime? thoiGianDat = null;
                if (!string.IsNullOrEmpty(r.MaDon) && donHangDict.TryGetValue(r.MaDon, out var tg))
                {
                    thoiGianDat = tg;
                }

                danhGia.Add(new ĐACN.Models.DanhGiaNhaHangDisplayViewModel
                {
                    MaDon = r.MaDon,
                    TenKhachHang = r.KhachHang?.TenKH ?? "Khách hàng",
                    Diem = r.SoSao,
                    NhanXet = r.BinhLuan,
                    ThoiGianDat = thoiGianDat,
                    ThoiGianDanhGia = r.ThoiGian
                });
            }

            ViewBag.SoLuongDanhGia = danhGia.Count;


            double diemTB = 0;
            if (danhGia.Any())
            {
                diemTB = (double)danhGia.Where(x => x.Diem.HasValue).Average(x => x.Diem.Value);
            }
            ViewBag.DiemTrungBinh = Math.Round(diemTB, 1);

            return View(danhGia);
        }





        [HttpGet]
        public ActionResult DanhMuc()
        {
            if (!CheckLogin()) return RedirectLogin();

            var danhMuc = db.LoaiMonAns.OrderBy(l => l.TenLoai).ToList();
            ViewBag.AllLoaiMonAn = danhMuc;

            return View(danhMuc);
        }


        [HttpPost]
        public ActionResult ThemDanhMuc(string MaLoai, string TenLoai, HttpPostedFileBase HinhAnh)
        {
            if (!CheckLogin()) return RedirectLogin();


            if (string.IsNullOrWhiteSpace(MaLoai))
            {
                TempData["Error"] = "Mã danh mục không được để trống!";
                return RedirectToAction("DanhMuc");
            }

            var tonTai = db.LoaiMonAns.Any(l => l.MaLoai.Trim().ToLower() == MaLoai.Trim().ToLower());
            if (tonTai)
            {
                TempData["Error"] = $"Mã danh mục '{MaLoai}' đã tồn tại! Vui lòng chọn mã khác.";
                return RedirectToAction("DanhMuc");
            }


            if (!string.IsNullOrWhiteSpace(TenLoai))
            {
                var tenTrung = db.LoaiMonAns.Any(l =>
                    l.TenLoai != null &&
                    l.TenLoai.Trim().ToLower() == TenLoai.Trim().ToLower());
                if (tenTrung)
                {
                    TempData["Error"] = $"Tên danh mục '{TenLoai}' đã tồn tại! Vui lòng chọn tên khác.";
                    return RedirectToAction("DanhMuc");
                }
            }

            try
            {
                var loaiMoi = new LoaiMonAn
                {
                    MaLoai = MaLoai.Trim(),
                    TenLoai = TenLoai?.Trim()
                };


                if (HinhAnh != null && HinhAnh.ContentLength > 0)
                {

                    string errorMsg;
                    if (!ValidateImageFile(HinhAnh, out errorMsg))
                    {
                        TempData["Error"] = errorMsg;
                        return RedirectToAction("DanhMuc");
                    }

                    var ext = Path.GetExtension(HinhAnh.FileName).ToLower();

                    var fileName = Path.GetFileNameWithoutExtension(HinhAnh.FileName) + "_" + DateTime.Now.Ticks + ext;
                    var path = Path.Combine(Server.MapPath("~/images/danhmuc"), fileName);


                    var directory = Path.GetDirectoryName(path);
                    if (!Directory.Exists(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    HinhAnh.SaveAs(path);
                    loaiMoi.HinhAnh = fileName;
                }

                db.LoaiMonAns.Add(loaiMoi);
                db.SaveChanges();
                _cacheService.Remove("Home_DanhMucList");

                TempData["Success"] = $"Đã thêm danh mục '{TenLoai}' thành công!";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Lỗi thêm danh mục: " + ex.Message);
                TempData["Error"] = "Lỗi khi thêm danh mục. Vui lòng thử lại sau.";
            }

            return RedirectToAction("DanhMuc");
        }


        [HttpPost]
        public ActionResult SuaDanhMuc(string MaLoai, string TenLoai, HttpPostedFileBase HinhAnh)
        {
            if (!CheckLogin()) return RedirectLogin();

            if (string.IsNullOrWhiteSpace(MaLoai))
            {
                TempData["Error"] = "Mã danh mục không được để trống!";
                return RedirectToAction("DanhMuc");
            }

            var loai = db.LoaiMonAns.FirstOrDefault(l => l.MaLoai == MaLoai);
            if (loai == null)
            {
                TempData["Error"] = "Không tìm thấy danh mục cần sửa!";
                return RedirectToAction("DanhMuc");
            }


            if (!string.IsNullOrWhiteSpace(TenLoai))
            {
                var tenTrung = db.LoaiMonAns.Any(l =>
                    l.MaLoai != MaLoai &&
                    l.TenLoai != null &&
                    l.TenLoai.Trim().ToLower() == TenLoai.Trim().ToLower());
                if (tenTrung)
                {
                    TempData["Error"] = $"Tên danh mục '{TenLoai}' đã được sử dụng bởi danh mục khác!";
                    return RedirectToAction("DanhMuc");
                }
            }

            try
            {
                loai.TenLoai = TenLoai?.Trim();


                if (HinhAnh != null && HinhAnh.ContentLength > 0)
                {

                    string errorMsg;
                    if (!ValidateImageFile(HinhAnh, out errorMsg))
                    {
                        TempData["Error"] = errorMsg;
                        return RedirectToAction("DanhMuc");
                    }

                    var ext = Path.GetExtension(HinhAnh.FileName).ToLower();


                    if (!string.IsNullOrEmpty(loai.HinhAnh))
                    {
                        var oldPath = Server.MapPath("~/images/danhmuc/" + loai.HinhAnh);
                        if (System.IO.File.Exists(oldPath))
                        {
                            System.IO.File.Delete(oldPath);
                        }
                    }

                    var fileName = Path.GetFileNameWithoutExtension(HinhAnh.FileName) + "_" + DateTime.Now.Ticks + ext;
                    var path = Path.Combine(Server.MapPath("~/images/danhmuc"), fileName);

                    var directory = Path.GetDirectoryName(path);
                    if (!Directory.Exists(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    HinhAnh.SaveAs(path);
                    loai.HinhAnh = fileName;
                }

                db.SaveChanges();
                _cacheService.Remove("Home_DanhMucList");
                TempData["Success"] = $"Đã cập nhật danh mục '{TenLoai}' thành công!";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Lỗi cập nhật danh mục: " + ex.Message);
                TempData["Error"] = "Lỗi khi cập nhật danh mục. Vui lòng thử lại sau.";
            }

            return RedirectToAction("DanhMuc");
        }


        [HttpGet]
        public ActionResult XoaDanhMuc(string id)
        {
            if (!CheckLogin()) return RedirectLogin();

            if (string.IsNullOrWhiteSpace(id))
            {
                TempData["Error"] = "Mã danh mục không hợp lệ!";
                return RedirectToAction("DanhMuc");
            }

            var loai = db.LoaiMonAns.Include(l => l.MonAns).FirstOrDefault(l => l.MaLoai == id);
            if (loai == null)
            {
                TempData["Error"] = "Không tìm thấy danh mục cần xóa!";
                return RedirectToAction("DanhMuc");
            }


            var maNH = Session["MaNH"]?.ToString();
            var monAnDangDung = loai.MonAns.Any(m => m.MaNH == maNH);
            if (monAnDangDung)
            {
                var soMonAn = loai.MonAns.Count(m => m.MaNH == maNH);
                TempData["Error"] = $"Không thể xóa danh mục '{loai.TenLoai}' vì đang có {soMonAn} món ăn đang sử dụng danh mục này!";
                return RedirectToAction("DanhMuc");
            }


            if (loai.MonAns.Any())
            {
                var tongSoMonAn = loai.MonAns.Count;
                TempData["Error"] = $"Không thể xóa danh mục '{loai.TenLoai}' vì đang có {tongSoMonAn} món ăn đang sử dụng danh mục này trong hệ thống!";
                return RedirectToAction("DanhMuc");
            }

            try
            {

                if (!string.IsNullOrEmpty(loai.HinhAnh))
                {
                    var imagePath = Server.MapPath("~/images/danhmuc/" + loai.HinhAnh);
                    if (System.IO.File.Exists(imagePath))
                    {
                        System.IO.File.Delete(imagePath);
                    }
                }

                db.LoaiMonAns.Remove(loai);
                db.SaveChanges();
                _cacheService.Remove("Home_DanhMucList");

                TempData["Success"] = $"Đã xóa danh mục '{loai.TenLoai}' thành công!";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Lỗi xóa danh mục: " + ex.Message);
                TempData["Error"] = "Lỗi khi xóa danh mục. Vui lòng thử lại sau.";
            }

            return RedirectToAction("DanhMuc");
        }





        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult CapNhatTrangThaiAjax(string maDon, string trangThai, int? prepTimePhut = null)
        {
            if (!CheckLogin()) return Json(new { success = false, message = "Chưa đăng nhập" });

            var maNH = Session["MaNH"]?.ToString();
            var donHang = db.DonHangs.FirstOrDefault(d => d.MaDon == maDon && d.MaNH == maNH);
            
            if (donHang == null) return Json(new { success = false, message = "Không tìm thấy đơn hàng" });
            if (donHang.TrangThai == "Đã hủy" || donHang.TrangThai == "Hủy") return Json(new { success = false, message = "Đơn đã hủy" });

            donHang.TrangThai = trangThai;
            db.SaveChanges();

            // Trigger Shipper Dispatch khi nhà hàng xác nhận đơn hoặc làm món xong
            if ((trangThai == "Đã xác nhận" || trangThai == "Đang làm món" || trangThai == "Đang lấy món") && string.IsNullOrEmpty(donHang.MaShipper))
            {
                try
                {
                    ĐACN.Services.OrderDispatcher.DispatchOrder(donHang.MaDon, donHang.MaNH);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[CapNhatTrangThaiAjax] DispatchOrder error: {ex.Message}");
                }
            }

            // SignalR Notification
            try
            {
                var hubDelivery = Microsoft.AspNet.SignalR.GlobalHost.ConnectionManager.GetHubContext<ĐACN.Hubs.DeliveryHub>();
                if (trangThai == "Đã xác nhận" || trangThai == "Đang làm món")
                {
                    int mins = prepTimePhut ?? 15;
                    hubDelivery.Clients.Group("KhachHang_" + donHang.MaKH).notifyNewOrder($"Nhà hàng đã xác nhận đơn {maDon} (Dự kiến xong trong {mins} phút)");
                }
                else if (trangThai == "Đang lấy món")
                {
                    hubDelivery.Clients.Group("KhachHang_" + donHang.MaKH).notifyNewOrder($"Món ăn đơn #{maDon} đã nấu xong, đang chờ tài xế đến lấy!");
                    if (!string.IsNullOrEmpty(donHang.MaShipper))
                    {
                        hubDelivery.Clients.Group("Shipper_" + donHang.MaShipper).notifyNewOrder($"Món ăn đơn #{maDon} đã nấu xong, bạn có thể tới quán lấy ngay!");
                    }
                }
            }
            catch { }
            
            // Push Notification qua SignalR cho Khách Hàng
            string notifTitle = "Trạng thái đơn hàng: " + maDon;
            string notifMessage = "Đơn hàng của bạn đã chuyển sang trạng thái: " + trangThai;
            string notifType = "info";
            
            if (trangThai == "Đang lấy món") notifType = "warning";
            else if (trangThai == "Đang giao") notifType = "info";
            else if (trangThai == "Sẵn sàng / Đang giao") notifType = "success";
            
            ĐACN.Hubs.NotificationHub.NotifyOrderUpdate("KH_" + donHang.MaKH, notifTitle, notifMessage, notifType);
            
            return Json(new { success = true, message = "Cập nhật thành công" });
        }

        [HttpPost]
        public ActionResult CapNhatLamXong(string maDon)
        {
            if (!CheckLogin()) return RedirectLogin();

            if (string.IsNullOrWhiteSpace(maDon))
            {
                TempData["Error"] = "Mã đơn hàng không hợp lệ!";
                return RedirectToAction("DanhSachDonHang");
            }

            var maNH = Session["MaNH"]?.ToString();
            if (string.IsNullOrEmpty(maNH))
            {
                TempData["Error"] = "Không tìm thấy thông tin nhà hàng!";
                return RedirectToAction("DanhSachDonHang");
            }

            var donHang = db.DonHangs.FirstOrDefault(d => d.MaDon == maDon && d.MaNH == maNH);
            if (donHang == null)
            {
                TempData["Error"] = "Không tìm thấy đơn hàng hoặc bạn không có quyền cập nhật đơn hàng này!";
                return RedirectToAction("DanhSachDonHang");
            }

            if (donHang.TrangThai == "Đã hủy" || donHang.TrangThai == "Hủy" || donHang.TrangThai == "Hoàn thành" || donHang.TrangThai == "Đang lấy món")
            {
                TempData["Error"] = "Trạng thái đơn hàng không hợp lệ (đơn đã bị hủy hoặc đã được cập nhật trước đó)!";
                return RedirectToAction("DanhSachDonHang");
            }

            try
            {

                donHang.TrangThai = "Đang lấy món";
                db.SaveChanges();

                // SignalR Notification
                try
                {
                    var context = Microsoft.AspNet.SignalR.GlobalHost.ConnectionManager.GetHubContext<ĐACN.Hubs.DeliveryHub>();
                    if (!string.IsNullOrEmpty(donHang.MaShipper))
                    {
                        context.Clients.Group("Shipper_" + donHang.MaShipper).notifyNewOrder($"Món ăn cho đơn {maDon} đã làm xong, tới lấy ngay!");
                    }
                    else
                    {
                        context.Clients.Group("Shippers").notifyNewOrder($"Có đơn đã làm xong cần giao ngay ({maDon})");
                        ĐACN.Services.OrderDispatcher.DispatchOrder(donHang.MaDon, donHang.MaNH);
                    }
                }
                catch { }

                TempData["Success"] = $"Đã cập nhật trạng thái đơn hàng {maDon} thành 'Đang lấy món'!";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Lỗi cập nhật trạng thái: " + ex.Message);
                TempData["Error"] = "Lỗi khi cập nhật trạng thái. Vui lòng thử lại sau.";
            }

            return RedirectToAction("DanhSachDonHang");
        }

        [HttpGet]
        public ActionResult ToppingMenu(string id)
        {
            if (!CheckLogin()) return RedirectToAction("Login", "Account");
            string maNH = Session["MaNH"] as string;
            
            var mon = db.MonAns.FirstOrDefault(m => m.MaMon == id && m.MaNH == maNH);
            if (mon == null)
            {
                TempData["Error"] = "Không tìm thấy món ăn hoặc bạn không có quyền!";
                return RedirectToAction("QuanLySanPham");
            }
            
            ViewBag.MonAn = mon;
            var options = DACN.Models.Customizations.CustomizationService.GetCustomizationForDish(id);
            return View(options);
        }

        [HttpPost]
        public JsonResult SaveToppingMenu(string id, List<DACN.Models.Customizations.CustomizationGroup> groups)
        {
            if (!CheckLogin()) return Json(new { success = false, message = "Lỗi xác thực" });
            string maNH = Session["MaNH"] as string;
            
            var mon = db.MonAns.FirstOrDefault(m => m.MaMon == id && m.MaNH == maNH);
            if (mon == null)
            {
                return Json(new { success = false, message = "Không tìm thấy món ăn" });
            }

            if (groups == null) groups = new List<DACN.Models.Customizations.CustomizationGroup>();
            
            try
            {
                DACN.Models.Customizations.CustomizationService.SaveCustomizationForDish(id, groups);
                return Json(new { success = true, message = "Lưu thành công!" });
            }
            catch(Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }
    }
}