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



        private bool CheckLogin()
        {
            var tk = Session["TaiKhoan"] as TaiKhoan;


            if (tk == null)
            {
                var cookieIP = Request.Cookies["TapFoodLoginIP"];
                var cookieUser = Request.Cookies["TapFoodUser"];

                if (cookieIP != null && cookieUser != null)
                {

                    if (cookieIP.Value == LayDiaChiIP())
                    {
                        var userInDb = db.TaiKhoans.FirstOrDefault(x => x.TenDangNhap == cookieUser.Value);

                        if (userInDb != null && userInDb.VaiTro == "NhaHang" && userInDb.TrangThai == true)
                        {
                            Session["TaiKhoan"] = userInDb;
                            tk = userInDb;
                        }
                    }
                }
            }


            if (tk == null || tk.VaiTro != "NhaHang")
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


        public ActionResult ThongKe(string timeRange = "Tháng", string statType = "Thống kê doanh thu")
        {
            if (!CheckLogin()) return RedirectLogin();

            var maNH = Session["MaNH"]?.ToString();
            if (string.IsNullOrEmpty(maNH)) return RedirectToAction("TrangChu", "Home");

            ViewBag.CurrentTimeRange = timeRange;
            ViewBag.CurrentStatType = statType;
            ViewBag.ChartLabel = statType == "Thống kê doanh thu" ? "Doanh thu (VNĐ)" : "Số lượng (đơn)";

            var doanhThuTheoDanhMuc = db.ChiTietDonHangs
                .Where(ct => ct.DonHang.MaNH == maNH)
                .GroupBy(ct => ct.MonAn.MaLoai)
                .Select(g => new
                {
                    Loai = g.FirstOrDefault().MonAn.LoaiMonAn.TenLoai,
                    DoanhThu = g.Sum(x => x.SoLuong * x.DonGia)
                }).ToList();

            ViewBag.LabelsLoai = doanhThuTheoDanhMuc.Select(x => x.Loai).ToList();
            ViewBag.DataDoanhThuLoai = doanhThuTheoDanhMuc.Select(x => x.DoanhThu).ToList();

            var queryDonHang = db.DonHangs.Where(d => d.MaNH == maNH && d.ThoiGianDat != null);
            
            List<string> labelsThoiGian = new List<string>();
            List<double> dataThoiGian = new List<double>();

            if (timeRange == "Ngày")
            {
                var grouped = queryDonHang
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
                var grouped = queryDonHang
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
                var grouped = queryDonHang
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


            var listTrangThai = db.DonHangs
                .Where(d => d.MaNH == maNH)
                .Select(d => d.TrangThai)
                .ToList();

            int dangXuLy = listTrangThai.Count(t => t != "Hoàn thành" && t != "Hoàn tất" && t != "Đã hủy" && t != "Hủy");
            int daGiao = listTrangThai.Count(t => t == "Hoàn thành" || t == "Hoàn tất");
            int daHuy = listTrangThai.Count(t => t == "Đã hủy" || t == "Hủy");

            ViewBag.DataTrangThaiDonHang = new List<int> { dangXuLy, daGiao, daHuy };


            var topSanPham = db.ChiTietDonHangs
                .Where(ct => ct.DonHang.MaNH == maNH)
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

            if (nhaHang == null) return HttpNotFound();

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

                    int attempt = 0;
                    string maMonTmp = maMonMoi;
                    while (db.MonAns.Any(m => m.MaMon == maMonTmp) && attempt < 100)
                    {

                        var soStr = maMonTmp.Substring(2);
                        if (int.TryParse(soStr, out int so))
                        {
                            so++;
                            maMonTmp = "MA" + so.ToString("D3");
                        }
                        else
                        {
                            maMonTmp = "MA" + (DateTime.Now.Ticks % 1000000).ToString("D6").Substring(0, 3);
                        }
                        attempt++;
                    }
                    maMonMoi = maMonTmp;
                }

                monAn.MaMon = maMonMoi;
                monAn.TrangThai = true;
                monAn.MaNH = maNH;

                db.MonAns.Add(monAn);
                db.SaveChanges();

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

                string errorMsg = "Lỗi database: ";
                if (sqlEx.Number == 2601 || sqlEx.Number == 2627)
                {
                    errorMsg += "Mã món đã tồn tại!";
                }
                else if (sqlEx.Number == 547)
                {
                    errorMsg += "Dữ liệu không hợp lệ (Foreign Key constraint): ";
                    if (sqlEx.Message.Contains("MaLoai"))
                        errorMsg += "Danh mục không tồn tại!";
                    else if (sqlEx.Message.Contains("MaNH"))
                        errorMsg += "Nhà hàng không tồn tại!";
                    else
                        errorMsg += sqlEx.Message;
                }
                else
                {
                    errorMsg += sqlEx.Message;
                }
                TempData["Error"] = errorMsg;
            }
            catch (Exception ex)
            {

                string errorMsg = "Lỗi khi thêm: " + ex.Message;
                var innerEx = ex.InnerException;
                int depth = 0;
                while (innerEx != null && depth < 5)
                {
                    errorMsg += " | " + innerEx.Message;
                    innerEx = innerEx.InnerException;
                    depth++;
                }
                TempData["Error"] = errorMsg;
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

            var tatCaMaMon = db.MonAns
                .Where(m => m.MaMon.StartsWith("MA") && m.MaMon.Length == 5)
                .Select(m => m.MaMon)
                .ToList();

            if (!tatCaMaMon.Any())
            {
                return "MA001";
            }


            var maxMa = tatCaMaMon
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
            return "MA" + soMoi.ToString("D3");
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
                TempData["Success"] = "Cập nhật thành công!";
                return RedirectToAction("QuanLySanPham");
            }
            catch (Exception ex)
            {
                TempData["Error"] = "Lỗi cập nhật: " + ex.Message;
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
                TempData["Success"] = "Đã chuyển sản phẩm sang trạng thái ngừng bán!";
            }
            catch (Exception ex)
            {
                TempData["Error"] = "Lỗi xóa: " + ex.Message;
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


            var donHangs = query
                .Select(d => new ĐACN.Models.DonHangListViewModel
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
                    db.Database.ExecuteSqlCommand("IF OBJECT_ID('CHK_DonHang_TrangThai', 'C') IS NOT NULL ALTER TABLE DonHang DROP CONSTRAINT CHK_DonHang_TrangThai");
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


            var danhGia = new List<ĐACN.Models.DanhGiaNhaHangDisplayViewModel>();
            foreach (var r in rawReviews)
            {

                DateTime? thoiGianDat = null;
                if (!string.IsNullOrEmpty(r.MaDon) && !r.MaDon.StartsWith("RATE_"))
                {

                    var donHang = db.DonHangs.FirstOrDefault(d => d.MaDon == r.MaDon);
                    thoiGianDat = donHang?.ThoiGianDat;
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

                TempData["Success"] = $"Đã thêm danh mục '{TenLoai}' thành công!";
            }
            catch (Exception ex)
            {
                TempData["Error"] = "Lỗi khi thêm danh mục: " + ex.Message;
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
                TempData["Success"] = $"Đã cập nhật danh mục '{TenLoai}' thành công!";
            }
            catch (Exception ex)
            {
                TempData["Error"] = "Lỗi khi cập nhật danh mục: " + ex.Message;
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

                TempData["Success"] = $"Đã xóa danh mục '{loai.TenLoai}' thành công!";
            }
            catch (Exception ex)
            {
                TempData["Error"] = "Lỗi khi xóa danh mục: " + ex.Message;
            }

            return RedirectToAction("DanhMuc");
        }





        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult CapNhatTrangThaiAjax(string maDon, string trangThai)
        {
            if (!CheckLogin()) return Json(new { success = false, message = "Chưa đăng nhập" });

            var maNH = Session["MaNH"]?.ToString();
            var donHang = db.DonHangs.FirstOrDefault(d => d.MaDon == maDon && d.MaNH == maNH);
            
            if (donHang == null) return Json(new { success = false, message = "Không tìm thấy đơn hàng" });
            if (donHang.TrangThai == "Đã hủy" || donHang.TrangThai == "Hủy") return Json(new { success = false, message = "Đơn đã hủy" });

            donHang.TrangThai = trangThai;
            db.SaveChanges();
            
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
                    }
                }
                catch { }

                TempData["Success"] = $"Đã cập nhật trạng thái đơn hàng {maDon} thành 'Đang lấy món'!";
            }
            catch (Exception ex)
            {
                TempData["Error"] = "Lỗi khi cập nhật trạng thái: " + ex.Message;
            }

            return RedirectToAction("DanhSachDonHang");
        }
    }
}