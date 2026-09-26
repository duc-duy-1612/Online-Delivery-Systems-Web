using ĐACN;
using OfficeOpenXml;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Validation;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Text.RegularExpressions;
using System.Web;
using System.Web.Mvc;


using ĐACN.Filters;

namespace ĐACN.Controllers
{
    [AdminAuthorize]
    public class AdminController : BaseController
    {
        private readonly ĐACN.Services.CacheService _cacheService = new ĐACN.Services.CacheService();

        public ActionResult DanhSachCuaHang(string keyword, string status)
        {
            var ds = db.NhaHangs.Include("TaiKhoan").AsQueryable();

            if (!string.IsNullOrEmpty(keyword))
                ds = ds.Where(nh => nh.TenNH.Contains(keyword) || nh.DiaChi.Contains(keyword));

            if (!string.IsNullOrEmpty(status))
            {
                if (status == "Hoạt động")
                    ds = ds.Where(nh => nh.TaiKhoan != null && nh.TaiKhoan.TrangThai == true);
                else if (status == "Bị khóa")
                    ds = ds.Where(nh => nh.TaiKhoan != null && nh.TaiKhoan.TrangThai == false);
            }

            ViewBag.Keyword = keyword;
            ViewBag.Status = status;

            return View(ds.ToList());
        }

        public ActionResult ChiTietNhaHang(string id, bool? edit)
        {
            if (string.IsNullOrEmpty(id))
                return RedirectToAction("DanhSachCuaHang");

            db.Configuration.ProxyCreationEnabled = false;

            var nh = db.NhaHangs.Include("TaiKhoan").FirstOrDefault(n => n.MaNH == id);
            if (nh == null)
                return HttpNotFound();

            ViewBag.EditMode = edit ?? false;
            return View(nh);
        }

        [HttpPost]
        public ActionResult SuaNhaHang(string MaNH, string TenNH, string DiaChi, string SDT, string TrangThai,
                             string TenDangNhap, string MatKhau, string TrangThaiTaiKhoan, HttpPostedFileBase HinhNhaHangFile)
        {
            var nhaHang = db.NhaHangs.Include("TaiKhoan").FirstOrDefault(n => n.MaNH == MaNH);
            if (nhaHang == null)
                return HttpNotFound();

            nhaHang.TenNH = TenNH;
            nhaHang.DiaChi = DiaChi;
            nhaHang.SDT = SDT;
            nhaHang.TrangThai = TrangThai;

            if (HinhNhaHangFile != null && HinhNhaHangFile.ContentLength > 0)
            {
                string errorMsg;
                if (!ValidateImageFile(HinhNhaHangFile, out errorMsg))
                {
                    TempData["Message"] = errorMsg;
                    return RedirectToAction("ChiTietNhaHang", new { id = nhaHang.MaNH, edit = true });
                }

                string folderPath = Server.MapPath("~/images/nhahang/");
                Directory.CreateDirectory(folderPath);

                string fileName = Path.GetFileNameWithoutExtension(HinhNhaHangFile.FileName);
                string extension = Path.GetExtension(HinhNhaHangFile.FileName);
                string newFileName = fileName + "_" + DateTime.Now.Ticks + extension;
                string savePath = Path.Combine(folderPath, newFileName);

                HinhNhaHangFile.SaveAs(savePath);
                nhaHang.HinhAnh = "~/images/nhahang/" + newFileName;
            }

            if (nhaHang.TaiKhoan != null)
            {
                nhaHang.TaiKhoan.TenDangNhap = TenDangNhap;
                if (!string.IsNullOrEmpty(MatKhau))
                    nhaHang.TaiKhoan.MatKhau = BCrypt.Net.BCrypt.HashPassword(MatKhau);

                if (!string.IsNullOrEmpty(TrangThaiTaiKhoan))
                {
                    nhaHang.TaiKhoan.TrangThai = (TrangThaiTaiKhoan == "true");
                }
            }

            db.SaveChanges();
            TempData["Message"] = "Cập nhật thông tin cửa hàng thành công!";
            return RedirectToAction("ChiTietNhaHang", new { id = nhaHang.MaNH });
        }


        [HttpPost]
        public ActionResult XoaNhaHang(string id)
        {
            var nh = db.NhaHangs.Include("TaiKhoan").FirstOrDefault(n => n.MaNH == id);
            if (nh != null && nh.TaiKhoan != null)
            {
                nh.TaiKhoan.TrangThai = false;
                db.SaveChanges();
                TempData["Message"] = "Đã khóa tài khoản nhà hàng thành công!";
            }
            return RedirectToAction("DanhSachCuaHang");
        }

        [HttpGet]
        public ActionResult ThemNhaHang()
        {
            return View();
        }
        [HttpPost]
        public ActionResult ThemNhaHang(NhaHang nh, string TenDangNhap, string MatKhau, HttpPostedFileBase HinhNhaHangFile)
        {
            string phoneError;
            if (!ValidatePhoneNumber(nh.SDT, out phoneError))
            {
                TempData["Message"] = phoneError;
                return View(nh);
            }

            TaiKhoan tk = new TaiKhoan
            {
                MaTK = "TK" + Guid.NewGuid().ToString("N").Substring(0, 6),
                TenDangNhap = TenDangNhap,
                MatKhau = BCrypt.Net.BCrypt.HashPassword(MatKhau),
                VaiTro = "NhaHang",
                TrangThai = true
            };
            db.TaiKhoans.Add(tk);
            db.SaveChanges();

            nh.MaNH = "NH" + Guid.NewGuid().ToString("N").Substring(0, 6);
            nh.MaTK = tk.MaTK;

            string folderPath = Server.MapPath("~/images/nhahang/");
            Directory.CreateDirectory(folderPath);

            if (HinhNhaHangFile != null && HinhNhaHangFile.ContentLength > 0)
            {
                string errorMsg;
                if (!ValidateImageFile(HinhNhaHangFile, out errorMsg))
                {
                    TempData["Message"] = errorMsg;
                    return View(nh);
                }

                string fileName = Path.GetFileNameWithoutExtension(HinhNhaHangFile.FileName);
                string extension = Path.GetExtension(HinhNhaHangFile.FileName);
                string newFileName = fileName + "_" + DateTime.Now.Ticks + extension;
                string savePath = Path.Combine(folderPath, newFileName);

                HinhNhaHangFile.SaveAs(savePath);
                nh.HinhAnh = "~/images/nhahang/" + newFileName;
            }
            else
            {
                nh.HinhAnh = "~/images/default-restaurant.png";
            }

            db.NhaHangs.Add(nh);
            db.SaveChanges();

            TempData["Message"] = "Thêm nhà hàng mới thành công!";
            return RedirectToAction("DanhSachCuaHang");
        }

        public ActionResult CapPhepCuaHang()
        {
            var choXacNhan = db.NhaHangs
                .Include("TaiKhoan")
                .Where(n => n.TrangThai == "Đã đóng cửa" && (n.TaiKhoan == null || n.TaiKhoan.TrangThai == false))
                .OrderByDescending(n => n.MaNH)
                .ToList();
            return View(choXacNhan);
        }

        [HttpPost]
        public ActionResult ChapNhan(string id)
        {
            var nhaHang = db.NhaHangs.Include("TaiKhoan").FirstOrDefault(n => n.MaNH == id);
            if (nhaHang != null)
            {
                if (nhaHang.TaiKhoan != null)
                {
                    nhaHang.TaiKhoan.TrangThai = true;
                }
                nhaHang.TrangThai = "Đang mở cửa";
                db.SaveChanges();

                SendEmailToRestaurant(nhaHang.SDT, nhaHang.TenNH, true);
                TempData["Success"] = $"Đã cấp phép cho cửa hàng {nhaHang.TenNH}. Tài khoản đã được kích hoạt.";
            }
            else
            {
                TempData["Error"] = "Không tìm thấy cửa hàng cần phê duyệt.";
            }
            return RedirectToAction("CapPhepCuaHang");
        }

        [HttpPost]
        public ActionResult TuChoi(string id)
        {
            var nhaHang = db.NhaHangs.Include("TaiKhoan").FirstOrDefault(n => n.MaNH == id);
            if (nhaHang != null)
            {
                string tenNhaHang = nhaHang.TenNH;
                string sdt = nhaHang.SDT;

                if (!string.IsNullOrEmpty(nhaHang.HinhAnh))
                {
                    try
                    {
                        string imagePath = Server.MapPath("~/images/nhahang/" + nhaHang.HinhAnh);
                        if (System.IO.File.Exists(imagePath))
                        {
                            System.IO.File.Delete(imagePath);
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine("Lỗi xóa hình ảnh: " + ex.Message);
                    }
                }

                var tk = nhaHang.TaiKhoan;
                db.NhaHangs.Remove(nhaHang);
                if (tk != null)
                {
                    db.TaiKhoans.Remove(tk);
                }
                db.SaveChanges();

                SendEmailToRestaurant(sdt, tenNhaHang, false);
                TempData["Error"] = $"Đã từ chối và xóa đăng ký cửa hàng {tenNhaHang}";
            }
            return RedirectToAction("CapPhepCuaHang");
        }

        private void SendEmailToRestaurant(string email, string tenNhaHang, bool chapNhan)
        {
            try
            {
                string fromEmail = System.Configuration.ConfigurationManager.AppSettings["SmtpFrom"] ?? "noreply@tapfood.vn";
                string smtpPassword = System.Configuration.ConfigurationManager.AppSettings["SmtpPassword"] ?? "";

                string subject = chapNhan ? "Cửa hàng đã được phê duyệt" : "Cửa hàng bị từ chối";
                string body = chapNhan
                    ? $"Xin chào {tenNhaHang}, cửa hàng của bạn đã được phê duyệt và có thể hoạt động trên hệ thống FoodDelivery."
                    : $"Xin chào {tenNhaHang}, rất tiếc cửa hàng của bạn chưa được phê duyệt trên hệ thống FoodDelivery.";

                MailMessage mail = new MailMessage();
                mail.To.Add(email);
                mail.From = new MailAddress(fromEmail);
                mail.Subject = subject;
                mail.Body = body;
                mail.IsBodyHtml = true;

                SmtpClient smtp = new SmtpClient("smtp.gmail.com");
                smtp.Port = 587;
                smtp.Credentials = new NetworkCredential(fromEmail, smtpPassword);
                smtp.EnableSsl = true;
                smtp.Send(mail);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Không thể gửi email: " + ex.Message);
            }
        }

        public ActionResult DanhSachShipper(string keyword)
        {
            var shippers = db.Shippers.Include("TaiKhoan").AsQueryable();

            if (!string.IsNullOrEmpty(keyword))
            {
                shippers = shippers.Where(s => s.TenShipper.Contains(keyword) || s.SDT.Contains(keyword));
            }

            shippers = shippers.OrderBy(s => s.MaShipper);

            return View(shippers.ToList());
        }

        public ActionResult ChiTietShipper(string id, bool? edit)
        {
            var shipper = db.Shippers.Include("TaiKhoan").FirstOrDefault(s => s.MaShipper == id);
            if (shipper == null)
                return HttpNotFound();

            ViewBag.EditMode = edit ?? false;
            return View(shipper);
        }

        [HttpGet]
        public ActionResult ThemShipper()
        {
            return View();
        }

        [HttpPost]
        public ActionResult ThemShipper(Shipper shipper, string TenDangNhap, string MatKhau, string VaiTro, bool TrangThai, HttpPostedFileBase HinhShipperFile)
        {
            string phoneError;
            if (!ValidatePhoneNumber(shipper.SDT, out phoneError))
            {
                TempData["Message"] = phoneError;
                return View(shipper);
            }

            var taiKhoan = new TaiKhoan
            {
                MaTK = "TK" + DateTime.Now.Ticks,
                TenDangNhap = TenDangNhap,
                MatKhau = BCrypt.Net.BCrypt.HashPassword(MatKhau),
                VaiTro = VaiTro,
                TrangThai = TrangThai
            };
            db.TaiKhoans.Add(taiKhoan);
            db.SaveChanges();

            shipper.MaShipper = "SP" + DateTime.Now.Ticks;
            shipper.MaTK = taiKhoan.MaTK;

            if (HinhShipperFile != null && HinhShipperFile.ContentLength > 0)
            {
                string errorMsg;
                if (!ValidateImageFile(HinhShipperFile, out errorMsg))
                {
                    TempData["Message"] = errorMsg;
                    return View(shipper);
                }

                string fileName = Path.GetFileNameWithoutExtension(HinhShipperFile.FileName);
                string extension = Path.GetExtension(HinhShipperFile.FileName);
                string newFileName = fileName + "_" + DateTime.Now.Ticks + extension;
                string savePath = Path.Combine(Server.MapPath("~/Content/images/shipper/"), newFileName);

                Directory.CreateDirectory(Server.MapPath("~/Content/images/shipper/"));
                HinhShipperFile.SaveAs(savePath);

                shipper.HinhAnh = "/Content/images/shipper/" + newFileName;
            }
            else
            {
                shipper.HinhAnh = "/Content/images/default-avatar.png";
            }

            db.Shippers.Add(shipper);
            db.SaveChanges();

            TempData["Message"] = "Thêm Shipper mới thành công!";
            return RedirectToAction("DanhSachShipper");
        }


        [HttpPost]
        public ActionResult SuaShipper(Shipper model, string TenDangNhap, string MatKhau, string VaiTro, bool TrangThai, HttpPostedFileBase HinhShipperFile)
        {
            var shipper = db.Shippers.Include("TaiKhoan").FirstOrDefault(s => s.MaShipper == model.MaShipper);
            if (shipper == null)
                return HttpNotFound();

            shipper.TenShipper = model.TenShipper;
            shipper.SDT = model.SDT;
            shipper.BienSoXe = model.BienSoXe;

            if (HinhShipperFile != null && HinhShipperFile.ContentLength > 0)
            {
                string errorMsg;
                if (!ValidateImageFile(HinhShipperFile, out errorMsg))
                {
                    TempData["Message"] = errorMsg;
                    return RedirectToAction("ChiTietShipper", new { id = model.MaShipper, edit = true });
                }

                string fileName = Path.GetFileNameWithoutExtension(HinhShipperFile.FileName);
                string extension = Path.GetExtension(HinhShipperFile.FileName);
                string newFileName = fileName + "_" + DateTime.Now.Ticks + extension;
                string savePath = Path.Combine(Server.MapPath("~/Content/images/shipper/"), newFileName);

                Directory.CreateDirectory(Server.MapPath("~/Content/images/shipper/"));

                if (!string.IsNullOrEmpty(shipper.HinhAnh))
                {
                    var oldPath = Server.MapPath("~" + shipper.HinhAnh);
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

                HinhShipperFile.SaveAs(savePath);
                shipper.HinhAnh = "/Content/images/shipper/" + newFileName;
            }


            if (shipper.TaiKhoan != null)
            {
                shipper.TaiKhoan.TenDangNhap = TenDangNhap;
                if (!string.IsNullOrEmpty(MatKhau))
                    shipper.TaiKhoan.MatKhau = BCrypt.Net.BCrypt.HashPassword(MatKhau);
                shipper.TaiKhoan.VaiTro = VaiTro;
                shipper.TaiKhoan.TrangThai = TrangThai;
            }

            db.SaveChanges();

            TempData["Message"] = "Cập nhật Shipper thành công!";
            return RedirectToAction("ChiTietShipper", new { id = model.MaShipper });
        }

        [HttpPost]
        public ActionResult XoaShipper(string id)
        {
            var shipper = db.Shippers.Include("TaiKhoan").FirstOrDefault(s => s.MaShipper == id);
            if (shipper != null && shipper.TaiKhoan != null)
            {
                shipper.TaiKhoan.TrangThai = false;
                db.SaveChanges();
                TempData["Message"] = "Đã khóa tài khoản Shipper thành công!";
            }
            return RedirectToAction("DanhSachShipper");
        }

        public ActionResult DanhSachNguoiDung(string keyword)
        {
            var ds = db.KhachHangs.Include("TaiKhoan").AsQueryable();

            if (!string.IsNullOrEmpty(keyword))
                ds = ds.Where(k => k.TenKH.Contains(keyword) || k.SDT.Contains(keyword));

            ViewBag.Keyword = keyword;
            return View(ds.ToList());
        }

        public ActionResult ChiTietNguoiDung(string id, bool? edit)
        {
            var kh = db.KhachHangs.Find(id);
            ViewBag.EditMode = edit ?? false;
            return View(kh);
        }


        [HttpGet]
        public ActionResult ThemNguoiDung()
        {
            return View();
        }

        [HttpPost]
        public ActionResult ThemNguoiDung(KhachHang kh)
        {
            string tenDangNhap = Request["TenDangNhap"];
            string matKhau = Request["MatKhau"];

            string phoneError;
            if (!ValidatePhoneNumber(kh.SDT, out phoneError))
            {
                TempData["Error"] = phoneError;
                return View(kh);
            }

            TaiKhoan tk = new TaiKhoan
            {
                MaTK = "TK" + Guid.NewGuid().ToString("N").Substring(0, 6),
                TenDangNhap = tenDangNhap,
                MatKhau = BCrypt.Net.BCrypt.HashPassword(matKhau),
                VaiTro = "KhachHang",
                TrangThai = true
            };

            db.TaiKhoans.Add(tk);

            kh.MaKH = "KH" + Guid.NewGuid().ToString("N").Substring(0, 6);
            kh.MaTK = tk.MaTK;

            db.KhachHangs.Add(kh);
            db.SaveChanges();

            TempData["Success"] = "Thêm người dùng mới thành công!";
            return RedirectToAction("DanhSachNguoiDung");
        }

        [HttpGet]
        public ActionResult SuaNguoiDung(string id)
        {
            var kh = db.KhachHangs.Include("TaiKhoan").FirstOrDefault(k => k.MaKH == id);
            if (kh == null) return HttpNotFound();

            return View(kh);
        }
        [HttpPost]
        public ActionResult SuaNguoiDung(KhachHang model, string TrangThai)
        {
            var kh = db.KhachHangs.Include("TaiKhoan").FirstOrDefault(k => k.MaKH == model.MaKH);
            if (kh != null && kh.TaiKhoan != null)
            {
                kh.TaiKhoan.TrangThai = (TrangThai == "true");

                db.SaveChanges();
                TempData["Message"] = "Đã cập nhật trạng thái tài khoản thành công!";
            }

            return RedirectToAction("ChiTietNguoiDung", new { id = model.MaKH });
        }


        [HttpPost]
        public ActionResult XoaNguoiDung(string id)
        {
            var kh = db.KhachHangs.Include("TaiKhoan").FirstOrDefault(k => k.MaKH == id);
            if (kh == null) return HttpNotFound();

            if (kh.TaiKhoan != null)
                kh.TaiKhoan.TrangThai = false;

            db.SaveChanges();

            TempData["Message"] = "Đã khóa tài khoản người dùng thành công!";
            return RedirectToAction("DanhSachNguoiDung");
        }

        public ActionResult DanhMucMonAn()
        {
            var danhMuc = db.LoaiMonAns.OrderBy(l => l.TenLoai).ToList();

            var soMonDict = db.MonAns
                                 .GroupBy(m => m.MaLoai)
                                 .Select(g => new { MaLoai = g.Key, Tong = g.Count() })
                                 .ToDictionary(x => x.MaLoai, x => x.Tong);

            ViewBag.SoMonTheoLoai = soMonDict;
            ViewBag.Success = TempData["Success"];
            ViewBag.Error = TempData["Error"];

            return View(danhMuc);
        }

        [HttpPost]
        public ActionResult ThemDanhMuc(string TenLoai, HttpPostedFileBase HinhAnhFile)
        {
            if (string.IsNullOrWhiteSpace(TenLoai))
            {
                TempData["Error"] = "Tên danh mục không được để trống.";
                return RedirectToAction("DanhMucMonAn");
            }

            if (HinhAnhFile == null || HinhAnhFile.ContentLength == 0)
            {
                TempData["Error"] = "Vui lòng chọn hình ảnh cho danh mục.";
                return RedirectToAction("DanhMucMonAn");
            }

            string errorMsg;
            if (!ValidateImageFile(HinhAnhFile, out errorMsg))
            {
                TempData["Error"] = errorMsg;
                return RedirectToAction("DanhMucMonAn");
            }

            string maLoai = "DM" + Guid.NewGuid().ToString("N").Substring(0, 6).ToUpper();
            string fileName = LuuHinhDanhMuc(HinhAnhFile);

            var loai = new LoaiMonAn
            {
                MaLoai = maLoai,
                TenLoai = TenLoai.Trim(),
                HinhAnh = fileName
            };

            db.LoaiMonAns.Add(loai);
            db.SaveChanges();
            _cacheService.Remove("Home_DanhMucList");

            TempData["Success"] = "Thêm danh mục mới thành công!";
            return RedirectToAction("DanhMucMonAn");
        }

        [HttpPost]
        public ActionResult CapNhatDanhMuc(string MaLoai, string TenLoai, HttpPostedFileBase HinhAnhMoi)
        {
            var loai = db.LoaiMonAns.FirstOrDefault(l => l.MaLoai == MaLoai);
            if (loai == null)
                return HttpNotFound();

            if (string.IsNullOrWhiteSpace(TenLoai))
            {
                TempData["Error"] = "Tên danh mục không được để trống.";
                return RedirectToAction("DanhMucMonAn");
            }

            loai.TenLoai = TenLoai.Trim();

            if (HinhAnhMoi != null && HinhAnhMoi.ContentLength > 0)
            {
                string errorMsg;
                if (!ValidateImageFile(HinhAnhMoi, out errorMsg))
                {
                    TempData["Error"] = errorMsg;
                    return RedirectToAction("DanhMucMonAn");
                }
                loai.HinhAnh = LuuHinhDanhMuc(HinhAnhMoi);
            }

            db.SaveChanges();
            _cacheService.Remove("Home_DanhMucList");
            TempData["Success"] = "Cập nhật danh mục thành công!";
            return RedirectToAction("DanhMucMonAn");
        }

        [HttpPost]
        public ActionResult XoaDanhMuc(string id)
        {
            var loai = db.LoaiMonAns.Include("MonAns").FirstOrDefault(l => l.MaLoai == id);
            if (loai == null)
                return HttpNotFound();

            if (loai.MonAns != null && loai.MonAns.Any())
            {
                TempData["Error"] = "Không thể xóa danh mục đang có món ăn.";
                return RedirectToAction("DanhMucMonAn");
            }

            db.LoaiMonAns.Remove(loai);
            db.SaveChanges();
            _cacheService.Remove("Home_DanhMucList");
            TempData["Success"] = "Đã xóa danh mục.";
            return RedirectToAction("DanhMucMonAn");
        }

        private string LuuHinhDanhMuc(HttpPostedFileBase file)
        {
            string folderPath = Server.MapPath("~/images/danhmuc/");
            Directory.CreateDirectory(folderPath);

            string extension = Path.GetExtension(file.FileName);
            string fileName = "dm_" + Guid.NewGuid().ToString("N") + extension;
            string savePath = Path.Combine(folderPath, fileName);

            file.SaveAs(savePath);
            return fileName;
        }


        public ActionResult CapPhepShipper(string keyword)
        {
            var query = db.Shippers
                .Include(s => s.TaiKhoan)
                .Where(s => s.TaiKhoan != null && s.TaiKhoan.TrangThai == false)
                .AsQueryable();

            if (!string.IsNullOrEmpty(keyword))
            {
                query = query.Where(s =>
                    (s.TenShipper != null && s.TenShipper.Contains(keyword)) ||
                    (s.SDT != null && s.SDT.Contains(keyword)));
            }

            ViewBag.Keyword = keyword;

            var result = query.OrderByDescending(s => s.MaShipper).ToList();

            return View(result);
        }

        [HttpPost]
        public ActionResult ChapNhanShipper(string id)
        {
            var shipper = db.Shippers.Include("TaiKhoan").FirstOrDefault(s => s.MaShipper == id);
            if (shipper == null)
            {
                TempData["Error"] = "Không tìm thấy shipper cần phê duyệt.";
                return RedirectToAction("CapPhepShipper");
            }

            if (shipper.TaiKhoan != null)
            {
                shipper.TaiKhoan.TrangThai = true;
                db.SaveChanges();
                TempData["Success"] = $"Đã chấp nhận và kích hoạt tài khoản cho shipper {shipper.TenShipper}.";
            }
            else
            {
                TempData["Error"] = "Không tìm thấy tài khoản của shipper này.";
            }

            return RedirectToAction("CapPhepShipper");
        }

        [HttpPost]
        public ActionResult TuChoiShipper(string id)
        {
            var shipper = db.Shippers.Include("TaiKhoan").FirstOrDefault(s => s.MaShipper == id);
            if (shipper == null) return HttpNotFound();

            string tenShipper = shipper.TenShipper;

            if (!string.IsNullOrEmpty(shipper.HinhAnh))
            {
                try
                {
                    string imageFileName = shipper.HinhAnh;
                    if (imageFileName.StartsWith("/"))
                    {
                        imageFileName = imageFileName.Replace("/images/shipper/", "");
                    }
                    string imagePath = Server.MapPath("~/images/shipper/" + imageFileName);
                    if (System.IO.File.Exists(imagePath))
                    {
                        System.IO.File.Delete(imagePath);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("Lỗi xóa hình ảnh: " + ex.Message);
                }
            }

            var tk = shipper.TaiKhoan;
            db.Shippers.Remove(shipper);
            if (tk != null)
            {
                db.TaiKhoans.Remove(tk);
            }
            db.SaveChanges();

            TempData["Error"] = $"Đã từ chối và xóa đăng ký shipper {tenShipper}";
            return RedirectToAction("CapPhepShipper");
        }

        public ActionResult ThongKe(string type = "Tháng")
        {
            ViewBag.Type = type;

            var doanhThu = db.DonHangs
                .Where(d => d.ThoiGianDat != null && d.TongTien != null && 
                           (d.TrangThai == "Hoàn thành" || d.TrangThai == "Hoàn tất" || d.TrangThai == "True"))
                .GroupBy(d => new
                {
                    Nam = d.ThoiGianDat.HasValue ? d.ThoiGianDat.Value.Year : 0,
                    Thang = d.ThoiGianDat.HasValue ? d.ThoiGianDat.Value.Month : 0
                })
                .Select(g => new
                {
                    Nam = g.Key.Nam,
                    Thang = g.Key.Thang,
                    DoanhThu = g.Sum(x => (decimal?)x.TongTien) ?? 0,
                    HoaHong = (g.Sum(x => (decimal?)x.TongTien) ?? 0) * 0.1m
                }).ToList();

            if (type == "Quý")
            {
                doanhThu = doanhThu
                    .GroupBy(m => new { m.Nam, Quy = (m.Thang - 1) / 3 + 1 })
                    .Select(g => new
                    {
                        Nam = g.Key.Nam,
                        Thang = g.Key.Quy,
                        DoanhThu = g.Sum(x => x.DoanhThu),
                        HoaHong = g.Sum(x => x.HoaHong)
                    }).ToList();
            }

            var khachHangCount = db.KhachHangs.Count();
            var shipperCount = db.Shippers.Count();
            var nhaHangCount = db.NhaHangs.Count();

            var tongDonHang = db.DonHangs.Count();

            ViewBag.KhachHang = khachHangCount;
            ViewBag.Shipper = shipperCount;
            ViewBag.NhaHang = nhaHangCount;
            ViewBag.DoanhThu = doanhThu;
            ViewBag.TongDonHang = tongDonHang;

            return View();
        }
        [HttpPost]
        public ActionResult ExportThongKe(string format)
        {
            var data = db.DonHangs
                .Where(d => d.ThoiGianDat != null && d.TongTien != null && 
                           (d.TrangThai == "Hoàn thành" || d.TrangThai == "Hoàn tất" || d.TrangThai == "True"))
                .GroupBy(d => new
                {
                    Nam = d.ThoiGianDat.HasValue ? d.ThoiGianDat.Value.Year : 0,
                    Thang = d.ThoiGianDat.HasValue ? d.ThoiGianDat.Value.Month : 0
                })
                .Select(g => new
                {
                    Nam = g.Key.Nam,
                    Thang = g.Key.Thang,
                    TongDoanhThu = g.Sum(x => (decimal?)x.TongTien) ?? 0,
                    HoaHong = (g.Sum(x => (decimal?)x.TongTien) ?? 0) * 0.1m
                })
                .OrderBy(x => x.Nam)
                .ThenBy(x => x.Thang)
                .ToList();

            if (format == "excel")
            {
                ExcelPackage.License.SetNonCommercialPersonal("Team");

                using (var package = new ExcelPackage())
                {
                    var ws = package.Workbook.Worksheets.Add("ThongKe");

                    ws.Cells["A1"].Value = "BÁO CÁO THỐNG KÊ DOANH THU VÀ HOA HỒNG";
                    ws.Cells["A1:D1"].Merge = true;
                    ws.Cells["A1"].Style.Font.Bold = true;
                    ws.Cells["A1"].Style.Font.Size = 16;
                    ws.Cells["A1"].Style.HorizontalAlignment = OfficeOpenXml.Style.ExcelHorizontalAlignment.Center;

                    ws.Cells["A3"].Value = "Năm";
                    ws.Cells["B3"].Value = "Tháng";
                    ws.Cells["C3"].Value = "Doanh thu (VNĐ)";
                    ws.Cells["D3"].Value = "Hoa hồng 10% (VNĐ)";
                    ws.Cells["A3:D3"].Style.Font.Bold = true;

                    int row = 4;
                    foreach (var d in data)
                    {
                        ws.Cells[row, 1].Value = d.Nam;
                        ws.Cells[row, 2].Value = d.Thang;
                        ws.Cells[row, 3].Value = d.TongDoanhThu;
                        ws.Cells[row, 4].Value = d.HoaHong;
                        row++;
                    }

                    ws.Cells.AutoFitColumns();

                    var stream = new MemoryStream();
                    package.SaveAs(stream);
                    stream.Position = 0;

                    return File(stream,
                        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                        "ThongKe_FoodDelivery.xlsx");
                }
            }
            return RedirectToAction("ThongKe");
        }

        public ActionResult AdminViews()
        {
            return View();
        }

        public ActionResult QuickDanhSachCuaHang() => RedirectToAction("DanhSachCuaHang");
        public ActionResult QuickDanhSachNguoiDung() => RedirectToAction("DanhSachNguoiDung");
        public ActionResult QuickDanhSachShipper() => RedirectToAction("DanhSachShipper");

        public ActionResult QuickChiTietNhaHang(string id) => RedirectToAction("ChiTietNhaHang", new { id });
        public ActionResult QuickChiTietNguoiDung(string id) => RedirectToAction("ChiTietNguoiDung", new { id });
        public ActionResult QuickChiTietShipper(string id) => RedirectToAction("ChiTietShipper", new { id });

        public ActionResult QuickThemNhaHang() => RedirectToAction("ThemNhaHang");
        public ActionResult QuickThemNguoiDung() => RedirectToAction("ThemNguoiDung");
        public ActionResult QuickThemShipper() => RedirectToAction("ThemShipper");

        public ActionResult QuickCapPhepCuaHang() => RedirectToAction("CapPhepCuaHang");
        public ActionResult QuickCapPhepShipper() => RedirectToAction("CapPhepShipper");

        public ActionResult QuickThongKe() => RedirectToAction("ThongKe");

        // === VOUCHER MANAGEMENT ===
        public ActionResult QuanLyVoucher()
        {
            var ds = Models.VoucherStore.DanhSachVoucher;
            return View(ds);
        }

        [HttpPost]
        public ActionResult LuuVoucher(Models.Voucher v, bool isEdit, string oldMaVoucher)
        {
            try
            {
                if (isEdit)
                {
                    // Check if new code already exists and it's different from the old code
                    if (!v.MaVoucher.Equals(oldMaVoucher, StringComparison.OrdinalIgnoreCase) && 
                        Models.VoucherStore.DanhSachVoucher.Any(x => x.MaVoucher.Equals(v.MaVoucher, StringComparison.OrdinalIgnoreCase)))
                    {
                        return Json(new { success = false, message = "Mã voucher mới này đã tồn tại!" });
                    }
                    
                    Models.VoucherStore.UpdateVoucher(oldMaVoucher, v);
                    return Json(new { success = true, message = "Cập nhật voucher thành công!" });
                }
                else
                {
                    if (Models.VoucherStore.DanhSachVoucher.Any(x => x.MaVoucher.Equals(v.MaVoucher, StringComparison.OrdinalIgnoreCase)))
                    {
                        return Json(new { success = false, message = "Mã voucher này đã tồn tại!" });
                    }
                    Models.VoucherStore.AddVoucher(v);
                    return Json(new { success = true, message = "Thêm voucher thành công!" });
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Lỗi hệ thống. Vui lòng thử lại sau." });
            }
        }

        [HttpPost]
        public ActionResult XoaVoucher(string maVoucher)
        {
            try
            {
                Models.VoucherStore.DeleteVoucher(maVoucher);
                return Json(new { success = true, message = "Đã xóa voucher!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Lỗi hệ thống. Vui lòng thử lại sau." });
            }
        }

        [HttpPost]
        public ActionResult ToggleTrangThaiVoucher(string maVoucher)
        {
            try
            {
                Models.VoucherStore.ToggleVoucherStatus(maVoucher);
                return Json(new { success = true, message = "Đã cập nhật trạng thái!" });
            }
            catch (Exception ex)
            {
                LogError(ex, "ToggleTrangThaiVoucher");
                return Json(new { success = false, message = "Lỗi hệ thống. Vui lòng thử lại sau." });
            }
        }
    }
}