using System;
using System.Configuration;
using System.Net;
using System.Net.Mail;
using System.Security.Cryptography;

namespace ĐACN.Services
{
    public interface IEmailService
    {
        string GenerateSecureOtp(int length = 6);
        bool SendOtpEmail(string toEmail, string otp, out string errorMessage);
    }

    public class EmailService : IEmailService
    {
        private readonly string _smtpHost;
        private readonly int _smtpPort;
        private readonly string _fromEmail;
        private readonly string _password;

        public EmailService()
        {
            _smtpHost = ConfigurationManager.AppSettings["SmtpHost"] ?? "smtp.gmail.com";
            
            if (!int.TryParse(ConfigurationManager.AppSettings["SmtpPort"], out _smtpPort))
            {
                _smtpPort = 587;
            }

            _fromEmail = Environment.GetEnvironmentVariable("SMTP_FROM") 
                         ?? ConfigurationManager.AppSettings["SmtpFrom"] 
                         ?? "tapfood.delivery.contact@gmail.com";

            _password = Environment.GetEnvironmentVariable("SMTP_PASSWORD") 
                        ?? ConfigurationManager.AppSettings["SmtpPassword"];
        }

        public string GenerateSecureOtp(int length = 6)
        {
            if (length <= 0) length = 6;
            
            using (var rng = RandomNumberGenerator.Create())
            {
                byte[] bytes = new byte[4];
                rng.GetBytes(bytes);
                uint val = BitConverter.ToUInt32(bytes, 0);
                
                int min = (int)Math.Pow(10, length - 1);
                int max = (int)Math.Pow(10, length);
                int otp = (int)(min + (val % (max - min)));
                return otp.ToString();
            }
        }

        public bool SendOtpEmail(string toEmail, string otp, out string errorMessage)
        {
            errorMessage = string.Empty;
            try
            {
                if (string.IsNullOrWhiteSpace(_password))
                {
                    errorMessage = "Chưa cấu hình mật khẩu SMTP trong hệ thống.";
                    return false;
                }

                string subject = "Mã xác nhận quên mật khẩu - TapFood";
                string body = $"Mã OTP của bạn là: {otp}. Mã này sẽ hết hạn sau 5 phút.";

                using (var smtp = new SmtpClient(_smtpHost))
                {
                    smtp.Port = _smtpPort;
                    smtp.Credentials = new NetworkCredential(_fromEmail, _password);
                    smtp.EnableSsl = true;
                    
                    using (var mail = new MailMessage(_fromEmail, toEmail, subject, body))
                    {
                        smtp.Send(mail);
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[EmailService Error]: {ex.Message}");
                errorMessage = "Không thể gửi email OTP lúc này. Vui lòng thử lại sau.";
                return false;
            }
        }
    }
}
