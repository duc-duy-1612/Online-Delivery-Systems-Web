using ĐACN.Models;
using ĐACN.Services;
using System;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Web;
using System.Web.Mvc;
using Newtonsoft.Json.Linq;

namespace ĐACN.Controllers
{
    public class AccountController : BaseController
    {
        private readonly IEmailService _emailService = new EmailService();
        private static readonly object _idGenLock = new object();

        // Rate limit: lưu số lần login sai theo IP
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (int count, DateTime firstAttempt)> _loginAttempts 
            = new System.Collections.Concurrent.ConcurrentDictionary<string, (int count, DateTime firstAttempt)>();
        private const int MAX_LOGIN_ATTEMPTS = 5;
        private const int LOCKOUT_MINUTES = 15;

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
            // Rate limit check
            string clientIP = LayDiaChiIP();
            if (_loginAttempts.TryGetValue(clientIP, out var attempts))
            {
                if (attempts.count >= MAX_LOGIN_ATTEMPTS && (DateTime.Now - attempts.firstAttempt).TotalMinutes < LOCKOUT_MINUTES)
                {
                    int remaining = LOCKOUT_MINUTES - (int)(DateTime.Now - attempts.firstAttempt).TotalMinutes;
                    return Json(new { success = false, message = $"Đăng nhập sai quá nhiều lần. Vui lòng thử lại sau {remaining} phút." });
                }
                // Reset nếu đã hết thời gian lockout
                if ((DateTime.Now - attempts.firstAttempt).TotalMinutes >= LOCKOUT_MINUTES)
                    _loginAttempts.TryRemove(clientIP, out _);
            }

            var tk = db.TaiKhoans.FirstOrDefault(x => x.TenDangNhap == username);

            if (tk == null)
            {
                RecordFailedLogin(clientIP);
                return Json(new { success = false, message = "Tên đăng nhập hoặc mật khẩu không đúng." });
            }

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
            {
                RecordFailedLogin(clientIP);
                return Json(new { success = false, message = "Tên đăng nhập hoặc mật khẩu không đúng." });
            }

            if (tk.TrangThai == false)
                return Json(new { success = false, message = "Tài khoản của bạn đang bị khóa." });

            Session["TaiKhoan"] = tk;

            if (tk.VaiTro == UserRoles.KhachHang)
            {
                var maKH = db.KhachHangs
                             .Where(k => k.MaTK == tk.MaTK)
                             .Select(k => k.MaKH)
                             .FirstOrDefault();

                if (!string.IsNullOrEmpty(maKH))
                    Session["MaKH"] = maKH;
            }
            else if (tk.VaiTro == UserRoles.Shipper)
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

            // Login thành công - xóa bộ đếm login sai
            _loginAttempts.TryRemove(clientIP, out _);

            return Json(new { success = true, role = tk.VaiTro });
        }

        private void RecordFailedLogin(string ip)
        {
            _loginAttempts.AddOrUpdate(ip,
                k => (1, DateTime.Now),
                (k, existing) => (existing.count + 1, existing.firstAttempt));
        }

        [HttpPost]
        public JsonResult GoogleLogin(string credential)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(credential))
                    return Json(new { success = false, message = "Token không hợp lệ." });

                // Xác thực an toàn với Google TokenInfo API (Kiểm tra chữ ký số, hạn dùng, audience)
                string jsonString = "";
                try
                {
                    using (var client = new WebClient { Encoding = Encoding.UTF8 })
                    {
                        jsonString = client.DownloadString("https://oauth2.googleapis.com/tokeninfo?id_token=" + Uri.EscapeDataString(credential));
                    }
                }
                catch (Exception tokenEx)
                {
                    System.Diagnostics.Debug.WriteLine("Xác thực token Google thất bại: " + tokenEx.Message);
                    return Json(new { success = false, message = "Xác thực token Google không thành công hoặc token đã hết hạn." });
                }

                var json = JObject.Parse(jsonString);
                string email = json["email"]?.ToString();
                string name = json["name"]?.ToString();
                string googleId = json["sub"]?.ToString();
                string aud = json["aud"]?.ToString();
                string emailVerified = json["email_verified"]?.ToString();

                string configuredClientId = ConfigurationManager.AppSettings["Google:ClientId"];
                if (!string.IsNullOrEmpty(configuredClientId) && aud != configuredClientId)
                {
                    return Json(new { success = false, message = "Token không khớp với ứng dụng của hệ thống." });
                }

                if (string.IsNullOrEmpty(email))
                    return Json(new { success = false, message = "Không lấy được email từ Google." });

                if (emailVerified != null && emailVerified.ToLower() != "true")
                    return Json(new { success = false, message = "Email Google chưa được xác thực." });

                // Check if account already exists (by email as username)
                var tk = db.TaiKhoans.FirstOrDefault(x => x.TenDangNhap == email);

                if (tk != null)
                {
                    // Existing account — log them in
                    if (tk.TrangThai == false)
                        return Json(new { success = false, message = "Tài khoản của bạn đang bị khóa." });

                    Session["TaiKhoan"] = tk;

                    if (tk.VaiTro == UserRoles.KhachHang)
                    {
                        var maKH = db.KhachHangs
                                     .Where(k => k.MaTK == tk.MaTK)
                                     .Select(k => k.MaKH)
                                     .FirstOrDefault();
                        if (!string.IsNullOrEmpty(maKH))
                            Session["MaKH"] = maKH;
                    }

                    return Json(new { success = true, role = tk.VaiTro });
                }
                else
                {
                    // New account — auto-create KhachHang
                    string maTK = TaoMaTaiKhoanTuTang();
                    var taiKhoan = new TaiKhoan
                    {
                        MaTK = maTK,
                        TenDangNhap = email,
                        MatKhau = BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString()), // random password
                        VaiTro = UserRoles.KhachHang,
                        TrangThai = true
                    };
                    db.TaiKhoans.Add(taiKhoan);

                    string maKH = TaoMaKhachHangTuTang();
                    var kh = new KhachHang
                    {
                        MaKH = maKH,
                        TenKH = name ?? email.Split('@')[0],
                        SDT = "",
                        DiaChi = "",
                        MaTK = maTK
                    };
                    db.KhachHangs.Add(kh);
                    db.SaveChanges();

                    Session["TaiKhoan"] = taiKhoan;
                    Session["MaKH"] = maKH;

                    return Json(new { success = true, role = UserRoles.KhachHang, isNew = true });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Lỗi đăng nhập Google: " + ex.Message);
                return Json(new { success = false, message = "Lỗi xử lý đăng nhập Google. Vui lòng thử lại sau." });
            }
        }

        public ActionResult Logout()
        {
            var tk = Session["TaiKhoan"] as TaiKhoan;
            string vaiTro = tk?.VaiTro;

            Session.Clear();
            Session.Abandon();

            // Xóa session cookie
            if (Response.Cookies["ASP.NET_SessionId"] != null)
            {
                Response.Cookies["ASP.NET_SessionId"].Expires = DateTime.Now.AddDays(-1);
            }

            if (vaiTro == UserRoles.KhachHang)
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
                    TrangThai = role == UserRoles.KhachHang ? true : (bool?)false
                };
                db.TaiKhoans.Add(taiKhoan);

                if (role == UserRoles.KhachHang)
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
                else if (role == UserRoles.NhaHang)
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
                        TrangThai = StoreStatuses.DaDongCua,
                        HinhAnh = fileName
                    };
                    db.NhaHangs.Add(nhaHang);
                    db.SaveChanges();

                    return Json(new { success = true, message = "Đăng ký thành công! Tài khoản của bạn đang chờ Admin xác nhận. Vui lòng đăng nhập sau khi được duyệt." });
                }
                else if (role == UserRoles.Shipper)
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
                System.Diagnostics.Debug.WriteLine("Lỗi validation đăng ký: " + fullErrorMessage);
                return Json(new { success = false, message = "Thông tin đăng ký không hợp lệ. Vui lòng kiểm tra lại." });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Lỗi đăng ký: " + ex.Message);
                return Json(new { success = false, message = "Có lỗi xảy ra khi đăng ký. Vui lòng thử lại sau." });
            }
        }

        private string TaoMaTaiKhoanTuTang()
        {
            lock (_idGenLock)
            {
                var topIds = db.TaiKhoans
                    .Where(t => t.MaTK.StartsWith("TK"))
                    .OrderByDescending(t => t.MaTK.Length)
                    .ThenByDescending(t => t.MaTK)
                    .Select(t => t.MaTK)
                    .Take(20)
                    .ToList();

                if (!topIds.Any())
                {
                    return "TK001";
                }

                var maxMa = topIds
                    .Select(m => (m.Length > 2 && int.TryParse(m.Substring(2), out int so)) ? so : 0)
                    .DefaultIfEmpty(0)
                    .Max();

                int soMoi = maxMa + 1;
                return "TK" + (soMoi < 1000 ? soMoi.ToString("D3") : soMoi.ToString());
            }
        }

        private string TaoMaKhachHangTuTang()
        {
            lock (_idGenLock)
            {
                var topIds = db.KhachHangs
                    .Where(k => k.MaKH.StartsWith("KH"))
                    .OrderByDescending(k => k.MaKH.Length)
                    .ThenByDescending(k => k.MaKH)
                    .Select(k => k.MaKH)
                    .Take(20)
                    .ToList();

                if (!topIds.Any())
                {
                    return "KH001";
                }

                var maxMa = topIds
                    .Select(m => (m.Length > 2 && int.TryParse(m.Substring(2), out int so)) ? so : 0)
                    .DefaultIfEmpty(0)
                    .Max();

                int soMoi = maxMa + 1;
                return "KH" + (soMoi < 1000 ? soMoi.ToString("D3") : soMoi.ToString());
            }
        }

        private string TaoMaNhaHangTuTang()
        {
            lock (_idGenLock)
            {
                var topIds = db.NhaHangs
                    .Where(n => n.MaNH.StartsWith("NH"))
                    .OrderByDescending(n => n.MaNH.Length)
                    .ThenByDescending(n => n.MaNH)
                    .Select(n => n.MaNH)
                    .Take(20)
                    .ToList();

                if (!topIds.Any())
                {
                    return "NH001";
                }

                var maxMa = topIds
                    .Select(m => (m.Length > 2 && int.TryParse(m.Substring(2), out int so)) ? so : 0)
                    .DefaultIfEmpty(0)
                    .Max();

                int soMoi = maxMa + 1;
                return "NH" + (soMoi < 1000 ? soMoi.ToString("D3") : soMoi.ToString());
            }
        }

        private string TaoMaShipperTuTang()
        {
            lock (_idGenLock)
            {
                var topIds = db.Shippers
                    .Where(s => s.MaShipper.StartsWith("SP"))
                    .OrderByDescending(s => s.MaShipper.Length)
                    .ThenByDescending(s => s.MaShipper)
                    .Select(s => s.MaShipper)
                    .Take(20)
                    .ToList();

                if (!topIds.Any())
                {
                    return "SP001";
                }

                var maxMa = topIds
                    .Select(m => (m.Length > 2 && int.TryParse(m.Substring(2), out int so)) ? so : 0)
                    .DefaultIfEmpty(0)
                    .Max();

                int soMoi = maxMa + 1;
                return "SP" + (soMoi < 1000 ? soMoi.ToString("D3") : soMoi.ToString());
            }
        }


        [HttpGet]
        public ActionResult ForgotPassword()
        {
            return View();
        }

        [HttpPost]
        public JsonResult SendOTP(string email)
        {
            var tk = db.TaiKhoans.FirstOrDefault(x => x.TenDangNhap == email);
            if (tk == null)
            {
                return Json(new { success = false, message = "Email này chưa được đăng ký tài khoản." });
            }

            string otp = _emailService.GenerateSecureOtp(6);
            
            Session["OTP_" + email] = otp;
            Session["OTP_Time_" + email] = DateTime.Now;

            if (_emailService.SendOtpEmail(email, otp, out string errorMessage))
            {
                return Json(new { success = true, message = "Đã gửi mã OTP đến email của bạn." });
            }

            return Json(new { success = false, message = errorMessage });
        }

        [HttpPost]
        public JsonResult VerifyOTP(string email, string otp)
        {
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(otp))
            {
                return Json(new { success = false, message = "Thông tin xác thực không hợp lệ." });
            }

            if (Session["OTP_" + email] == null || Session["OTP_Time_" + email] == null)
            {
                return Json(new { success = false, message = "Mã OTP đã hết hạn hoặc không tồn tại." });
            }

            string savedOtp = Session["OTP_" + email].ToString();
            DateTime timeSaved = (DateTime)Session["OTP_Time_" + email];

            if ((DateTime.Now - timeSaved).TotalMinutes > 5)
            {
                Session.Remove("OTP_" + email);
                Session.Remove("OTP_Time_" + email);
                Session.Remove("OTP_Verified_" + email);
                return Json(new { success = false, message = "Mã OTP đã hết hạn (quá 5 phút)." });
            }

            if (savedOtp == otp)
            {
                Session["OTP_Verified_" + email] = true;
                return Json(new { success = true });
            }

            return Json(new { success = false, message = "Mã OTP không chính xác." });
        }

        [HttpPost]
        public JsonResult ResetPassword(string email, string newPassword)
        {
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(newPassword))
            {
                return Json(new { success = false, message = "Thông tin không hợp lệ." });
            }

            if (newPassword.Length < 6)
            {
                return Json(new { success = false, message = "Mật khẩu mới phải từ 6 ký tự trở lên." });
            }

            if (Session["OTP_" + email] == null || Session["OTP_Verified_" + email] == null || (bool)Session["OTP_Verified_" + email] != true)
            {
                return Json(new { success = false, message = "Phiên giao dịch không hợp lệ hoặc mã OTP chưa được xác thực." });
            }

            var tk = db.TaiKhoans.FirstOrDefault(x => x.TenDangNhap == email);
            if (tk == null)
            {
                return Json(new { success = false, message = "Không tìm thấy tài khoản." });
            }

            tk.MatKhau = BCrypt.Net.BCrypt.HashPassword(newPassword);
            db.SaveChanges();

            Session.Remove("OTP_" + email);
            Session.Remove("OTP_Time_" + email);
            Session.Remove("OTP_Verified_" + email);

            return Json(new { success = true });
        }

        private ActionResult RedirectTheoVaiTro(string vaiTro)
        {
            switch (vaiTro)
            {
                case UserRoles.Shipper:
                    return RedirectToAction("Index", "Shipper");
                case UserRoles.Admin:
                    return RedirectToAction("DanhSachCuaHang", "Admin");
                case UserRoles.NhaHang:
                    return RedirectToAction("ThongKe", "NhaHang");
                default:
                    return RedirectToAction("TrangChu", "Home");
            }
        }
    }
}