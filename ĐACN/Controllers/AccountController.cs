using ĐACN.Models;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Web;
using System.Web.Mvc;

namespace ĐACN.Controllers
{
    public class AccountController : BaseController
    {

        [HttpGet]
        public ActionResult Login()
        {
            var tk = Session["TaiKhoan"] as TaiKhoan;
            if (tk != null)
            {
                return RedirectTheoVaiTro(tk.VaiTro);
            }

            return View();
        }

        [HttpPost]
        public JsonResult Login(string username, string password)
        {
            var tk = db.TaiKhoans.FirstOrDefault(x => x.TenDangNhap == username);

            if (tk == null)
                return Json(new { success = false, message = "Tên đăng nhập hoặc mật khẩu không đúng." });

            bool isPasswordValid = false;
            
            if (tk.MatKhau.StartsWith("$2a$") || tk.MatKhau.StartsWith("$2b$") || tk.MatKhau.StartsWith("$2y$"))
            {
                isPasswordValid = BCrypt.Net.BCrypt.Verify(password, tk.MatKhau);
            }
            else
            {
                if (tk.MatKhau == password)
                {
                    isPasswordValid = true;
                    tk.MatKhau = BCrypt.Net.BCrypt.HashPassword(password);
                    db.SaveChanges();
                }
            }

            if (!isPasswordValid)
                return Json(new { success = false, message = "Tên đăng nhập hoặc mật khẩu không đúng." });

            if (tk.TrangThai == false)
                return Json(new { success = false, message = "Tài khoản của bạn đang bị khóa." });

            Session["TaiKhoan"] = tk;

            if (tk.VaiTro == "KhachHang")
            {
                var maKH = db.KhachHangs
                             .Where(k => k.MaTK == tk.MaTK)
                             .Select(k => k.MaKH)
                             .FirstOrDefault();

                if (!string.IsNullOrEmpty(maKH))
                    Session["MaKH"] = maKH;
            }
            else if (tk.VaiTro == "Shipper")
            {
                var shipper = db.Shippers
                               .Where(s => s.MaTK == tk.MaTK)
                               .FirstOrDefault();

                if (shipper != null)
                {
                    Session["MaShipper"] = shipper.MaShipper;
                    Session["Shipper"] = shipper;
                }
            }

            return Json(new { success = true, role = tk.VaiTro });
        }

        public ActionResult Logout()
        {
            var tk = Session["TaiKhoan"] as TaiKhoan;
            string vaiTro = tk?.VaiTro;

            Session.Clear();

            if (vaiTro == "KhachHang")
                return RedirectToAction("TrangChu", "Home");
            else
                return RedirectToAction("Login", "Account");
        }

        public ActionResult DangXuat()
        {
            return Logout();
        }

        [HttpGet]
        public ActionResult Register()
        {
            return View();
        }

        [HttpPost]
        public JsonResult Register(string username, string password, string role,
            string tenKH = null, string sdt = null, string diaChi = null,
            string tenNH = null, string tenShipper = null, string bienSoXe = null,
            HttpPostedFileBase hinhAnhFile = null)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
                return Json(new { success = false, message = "Tên đăng nhập và mật khẩu không được để trống." });

            if (string.IsNullOrWhiteSpace(role))
                return Json(new { success = false, message = "Vui lòng chọn vai trò!" });

            if (db.TaiKhoans.Any(t => t.TenDangNhap == username))
                return Json(new { success = false, message = "Tên đăng nhập đã tồn tại!" });

            try
            {
                string maTK = TaoMaTaiKhoanTuTang();
                var taiKhoan = new TaiKhoan
                {
                    MaTK = maTK,
                    TenDangNhap = username,
                    MatKhau = BCrypt.Net.BCrypt.HashPassword(password),
                    VaiTro = role,
                    TrangThai = role == "KhachHang" ? true : (bool?)false
                };
                db.TaiKhoans.Add(taiKhoan);

                if (role == "KhachHang")
                {
                    if (string.IsNullOrWhiteSpace(tenKH) || string.IsNullOrWhiteSpace(sdt) || string.IsNullOrWhiteSpace(diaChi))
                        return Json(new { success = false, message = "Vui lòng điền đầy đủ thông tin!" });

                    string phoneError;
                    if (!ValidatePhoneNumber(sdt, out phoneError))
                        return Json(new { success = false, message = phoneError });

                    string specificStreet = diaChi.Split(',')[0].Trim();
                    var addressCheck = ValidateAddressRealtime(specificStreet, diaChi);
                    if (!addressCheck.isValid)
                    {
                        return Json(new { success = false, message = $"Lỗi địa chỉ: {addressCheck.message}" });
                    }

                    string maKH = TaoMaKhachHangTuTang();
                    var kh = new KhachHang
                    {
                        MaKH = maKH,
                        TenKH = tenKH,
                        SDT = sdt,
                        DiaChi = diaChi,
                        MaTK = maTK
                    };
                    db.KhachHangs.Add(kh);
                    db.SaveChanges();

                    return Json(new { success = true, message = "Đăng ký thành công! Vui lòng đăng nhập." });
                }
                else if (role == "NhaHang")
                {
                    if (string.IsNullOrWhiteSpace(tenNH) || string.IsNullOrWhiteSpace(diaChi) || string.IsNullOrWhiteSpace(sdt))
                        return Json(new { success = false, message = "Vui lòng điền đầy đủ thông tin nhà hàng!" });

                    string phoneError;
                    if (!ValidatePhoneNumber(sdt, out phoneError))
                        return Json(new { success = false, message = phoneError });

                    string specificStreet = diaChi.Split(',')[0].Trim();
                    var addressCheck = ValidateAddressRealtime(specificStreet, diaChi);
                    if (!addressCheck.isValid)
                    {
                        return Json(new { success = false, message = $"Lỗi địa chỉ: {addressCheck.message}" });
                    }

                    if (hinhAnhFile == null || hinhAnhFile.ContentLength == 0)
                        return Json(new { success = false, message = "Vui lòng chọn hình ảnh nhà hàng!" });

                    string errorMsg;
                    if (!ValidateImageFile(hinhAnhFile, out errorMsg))
                        return Json(new { success = false, message = errorMsg });

                    var ext = Path.GetExtension(hinhAnhFile.FileName).ToLower();

                    string fileName = Path.GetFileNameWithoutExtension(hinhAnhFile.FileName) + "_" + DateTime.Now.Ticks + ext;
                    string folderPath = Server.MapPath("~/images/nhahang/");
                    Directory.CreateDirectory(folderPath);
                    string savePath = Path.Combine(folderPath, fileName);
                    hinhAnhFile.SaveAs(savePath);

                    string maNH = TaoMaNhaHangTuTang();
                    var nhaHang = new NhaHang
                    {
                        MaNH = maNH,
                        TenNH = tenNH,
                        DiaChi = diaChi,
                        SDT = sdt,
                        MaTK = maTK,
                        TrangThai = "Đã đóng cửa",
                        HinhAnh = fileName
                    };
                    db.NhaHangs.Add(nhaHang);
                    db.SaveChanges();

                    return Json(new { success = true, message = "Đăng ký thành công! Tài khoản của bạn đang chờ Admin xác nhận. Vui lòng đăng nhập sau khi được duyệt." });
                }
                else if (role == "Shipper")
                {
                    if (string.IsNullOrWhiteSpace(tenShipper) || string.IsNullOrWhiteSpace(sdt) || string.IsNullOrWhiteSpace(bienSoXe))
                        return Json(new { success = false, message = "Vui lòng điền đầy đủ thông tin!" });

                    string phoneError;
                    if (!ValidatePhoneNumber(sdt, out phoneError))
                        return Json(new { success = false, message = phoneError });

                    if (hinhAnhFile == null || hinhAnhFile.ContentLength == 0)
                        return Json(new { success = false, message = "Vui lòng chọn hình ảnh!" });

                    string errorMsg;
                    if (!ValidateImageFile(hinhAnhFile, out errorMsg))
                        return Json(new { success = false, message = errorMsg });

                    var ext = Path.GetExtension(hinhAnhFile.FileName).ToLower();

                    string fileName = Path.GetFileNameWithoutExtension(hinhAnhFile.FileName) + "_" + DateTime.Now.Ticks + ext;
                    string folderPath = Server.MapPath("~/images/shipper/");
                    Directory.CreateDirectory(folderPath);
                    string savePath = Path.Combine(folderPath, fileName);
                    hinhAnhFile.SaveAs(savePath);

                    string maShipper = TaoMaShipperTuTang();
                    var shipper = new Shipper
                    {
                        MaShipper = maShipper,
                        TenShipper = tenShipper,
                        SDT = sdt,
                        BienSoXe = bienSoXe,
                        MaTK = maTK,
                        HinhAnh = "/images/shipper/" + fileName
                    };
                    db.Shippers.Add(shipper);
                    db.SaveChanges();

                    return Json(new { success = true, message = "Đăng ký thành công! Tài khoản của bạn đang chờ Admin xác nhận. Vui lòng đăng nhập sau khi được duyệt." });
                }
                else
                {
                    return Json(new { success = false, message = "Vai trò không hợp lệ!" });
                }
            }
            catch (System.Data.Entity.Validation.DbEntityValidationException dbEx)
            {
                var errorMessages = dbEx.EntityValidationErrors
                        .SelectMany(x => x.ValidationErrors)
                        .Select(x => x.ErrorMessage);
                var fullErrorMessage = string.Join("; ", errorMessages);
                return Json(new { success = false, message = "Lỗi validation: " + fullErrorMessage });
            }
            catch (Exception ex)
            {
                string inner = ex.InnerException != null ? ex.InnerException.Message : "";
                if (ex.InnerException != null && ex.InnerException.InnerException != null)
                {
                    inner += " | " + ex.InnerException.InnerException.Message;
                }
                return Json(new { success = false, message = "Có lỗi xảy ra khi đăng ký: " + ex.Message + " | Chi tiết: " + inner });
            }
        }

        private string TaoMaTaiKhoanTuTang()
        {
            var tatCaMaTK = db.TaiKhoans
                .Where(t => t.MaTK.StartsWith("TK") && t.MaTK.Length == 5)
                .Select(t => t.MaTK)
                .ToList();

            if (!tatCaMaTK.Any())
            {
                return "TK001";
            }

            var maxMa = tatCaMaTK
                .Where(m => m.Length == 5 && m.Substring(0, 2) == "TK")
                .Select(m =>
                {
                    if (int.TryParse(m.Substring(2), out int so))
                        return so;
                    return 0;
                })
                .DefaultIfEmpty(0)
                .Max();

            int soMoi = maxMa + 1;
            return "TK" + soMoi.ToString("D3");
        }

        private string TaoMaKhachHangTuTang()
        {
            var tatCaMaKH = db.KhachHangs
                .Where(k => k.MaKH.StartsWith("KH") && k.MaKH.Length == 5)
                .Select(k => k.MaKH)
                .ToList();

            if (!tatCaMaKH.Any())
            {
                return "KH001";
            }

            var maxMa = tatCaMaKH
                .Where(m => m.Length == 5 && m.Substring(0, 2) == "KH")
                .Select(m =>
                {
                    if (int.TryParse(m.Substring(2), out int so))
                        return so;
                    return 0;
                })
                .DefaultIfEmpty(0)
                .Max();

            int soMoi = maxMa + 1;
            return "KH" + soMoi.ToString("D3");
        }

        private string TaoMaNhaHangTuTang()
        {
            var tatCaMaNH = db.NhaHangs
                .Where(n => n.MaNH.StartsWith("NH") && n.MaNH.Length == 5)
                .Select(n => n.MaNH)
                .ToList();

            if (!tatCaMaNH.Any())
            {
                return "NH001";
            }

            var maxMa = tatCaMaNH
                .Where(m => m.Length == 5 && m.Substring(0, 2) == "NH")
                .Select(m =>
                {
                    if (int.TryParse(m.Substring(2), out int so))
                        return so;
                    return 0;
                })
                .DefaultIfEmpty(0)
                .Max();

            int soMoi = maxMa + 1;
            return "NH" + soMoi.ToString("D3");
        }

        private string TaoMaShipperTuTang()
        {
            var tatCaMaSP = db.Shippers
                .Where(s => s.MaShipper.StartsWith("SP") && s.MaShipper.Length == 5)
                .Select(s => s.MaShipper)
                .ToList();

            if (!tatCaMaSP.Any())
            {
                return "SP001";
            }

            var maxMa = tatCaMaSP
                .Where(m => m.Length == 5 && m.Substring(0, 2) == "SP")
                .Select(m =>
                {
                    if (int.TryParse(m.Substring(2), out int so))
                        return so;
                    return 0;
                })
                .DefaultIfEmpty(0)
                .Max();

            int soMoi = maxMa + 1;
            return "SP" + soMoi.ToString("D3");
        }

        private void SendVerificationEmail(string toEmail, string username)
        {
            string from = "your_email@gmail.com";
            string password = "your_app_password";
            string subject = "Xác thực tài khoản TapFood Delivery";
            string body = $"Xin chào {username},\n\nTài khoản của bạn đã được tạo thành công!";

            var smtp = new SmtpClient("smtp.gmail.com")
            {
                Port = 587,
                Credentials = new NetworkCredential(from, password),
                EnableSsl = true
            };
            smtp.Send(from, toEmail, subject, body);
        }


        private ActionResult RedirectTheoVaiTro(string vaiTro)
        {
            switch (vaiTro)
            {
                case "Shipper":
                    return RedirectToAction("Index", "Shipper");
                case "Admin":
                    return RedirectToAction("DanhSachCuaHang", "Admin");
                case "NhaHang":
                    return RedirectToAction("ThongKe", "NhaHang");
                default:
                    return RedirectToAction("TrangChu", "Home");
            }
        }
    }
}