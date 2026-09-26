using System;
using System.Collections.Generic;
using System.Linq;

namespace ĐACN.Models
{
    public class Voucher
    {
        public string MaVoucher { get; set; }
        public string LoaiVoucher { get; set; } // "Mon" hoặc "Ship"
        public int PhanTramGiam { get; set; } // Giảm bao nhiêu % (10 = 10%)
        public decimal GiamToiDa { get; set; } // Giảm tối đa bao nhiêu tiền
        public decimal DieuKienToiThieu { get; set; } // Đơn hàng tối thiểu để được giảm
        public string MoTa { get; set; }
        public bool IsActive { get; set; }
    }

    public static class VoucherStore
    {
        private static string _filePath = System.Web.Hosting.HostingEnvironment.MapPath("~/App_Data/vouchers.json");
        private static List<Voucher> _danhSachVoucher = null;
        private static readonly object _syncLock = new object();

        public static List<Voucher> DanhSachVoucher
        {
            get
            {
                lock (_syncLock)
                {
                    if (_danhSachVoucher == null)
                    {
                        LoadVouchers();
                    }
                    return _danhSachVoucher;
                }
            }
        }

        public static void LoadVouchers()
        {
            lock (_syncLock)
            {
                if (System.IO.File.Exists(_filePath))
                {
                    string json = System.IO.File.ReadAllText(_filePath);
                    _danhSachVoucher = Newtonsoft.Json.JsonConvert.DeserializeObject<List<Voucher>>(json) ?? new List<Voucher>();
                }
                else
                {
                    // Default hardcoded vouchers if file doesn't exist
                    _danhSachVoucher = new List<Voucher>()
                    {
                        new Voucher { MaVoucher = "TAPFOOD50", LoaiVoucher = "Mon", PhanTramGiam = 50, GiamToiDa = 50000, DieuKienToiThieu = 0, MoTa = "Giảm 50% món (tối đa 50K) cho mọi đơn", IsActive = true },
                        new Voucher { MaVoucher = "GIAM20K", LoaiVoucher = "Mon", PhanTramGiam = 100, GiamToiDa = 20000, DieuKienToiThieu = 100000, MoTa = "Giảm trực tiếp 20K cho đơn từ 100K", IsActive = true },
                        new Voucher { MaVoucher = "VIP200", LoaiVoucher = "Mon", PhanTramGiam = 20, GiamToiDa = 200000, DieuKienToiThieu = 500000, MoTa = "Giảm 20% (tối đa 200K) cho đơn từ 500K", IsActive = true },
                        new Voucher { MaVoucher = "FREESHIP", LoaiVoucher = "Ship", PhanTramGiam = 100, GiamToiDa = 15000, DieuKienToiThieu = 50000, MoTa = "Freeship (Tối đa 15K) cho đơn từ 50K", IsActive = true },
                        new Voucher { MaVoucher = "FREESHIPEXTRA", LoaiVoucher = "Ship", PhanTramGiam = 100, GiamToiDa = 50000, DieuKienToiThieu = 200000, MoTa = "Freeship Extra (Tối đa 50K) cho đơn từ 200K", IsActive = true }
                    };
                    SaveVouchersInternal(); // Create the file
                }
            }
        }

        private static void SaveVouchersInternal()
        {
            if (_danhSachVoucher != null)
            {
                var dir = System.IO.Path.GetDirectoryName(_filePath);
                if (!System.IO.Directory.Exists(dir))
                {
                    System.IO.Directory.CreateDirectory(dir);
                }
                string json = Newtonsoft.Json.JsonConvert.SerializeObject(_danhSachVoucher, Newtonsoft.Json.Formatting.Indented);
                System.IO.File.WriteAllText(_filePath, json);
            }
        }

        public static void SaveVouchers()
        {
            lock (_syncLock)
            {
                SaveVouchersInternal();
            }
        }

        public static void AddVoucher(Voucher v)
        {
            lock (_syncLock)
            {
                DanhSachVoucher.Add(v);
                SaveVouchersInternal();
            }
        }

        public static void UpdateVoucher(string oldMaVoucher, Voucher v)
        {
            lock (_syncLock)
            {
                var existing = DanhSachVoucher.FirstOrDefault(x => x.MaVoucher.Equals(oldMaVoucher, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    existing.MaVoucher = v.MaVoucher; // Allow changing the code
                    existing.LoaiVoucher = v.LoaiVoucher;
                    existing.PhanTramGiam = v.PhanTramGiam;
                    existing.GiamToiDa = v.GiamToiDa;
                    existing.DieuKienToiThieu = v.DieuKienToiThieu;
                    existing.MoTa = v.MoTa;
                    existing.IsActive = v.IsActive;
                    SaveVouchersInternal();
                }
            }
        }

        public static void DeleteVoucher(string maVoucher)
        {
            lock (_syncLock)
            {
                var existing = DanhSachVoucher.FirstOrDefault(x => x.MaVoucher.Equals(maVoucher, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    DanhSachVoucher.Remove(existing);
                    SaveVouchersInternal();
                }
            }
        }

        public static void ToggleVoucherStatus(string maVoucher)
        {
            lock (_syncLock)
            {
                var existing = DanhSachVoucher.FirstOrDefault(x => x.MaVoucher.Equals(maVoucher, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    existing.IsActive = !existing.IsActive;
                    SaveVouchersInternal();
                }
            }
        }

        // Hàm kiểm tra và tính toán số tiền được giảm
        public static (bool Success, string Message, decimal DiscountAmount, string LoaiVoucher) TinhToanGiamGia(string maVoucher, string loaiYeuCau, decimal tongTienMon, decimal phiShip = 0)
        {
            var voucher = DanhSachVoucher.FirstOrDefault(v => v.MaVoucher.Equals(maVoucher, StringComparison.OrdinalIgnoreCase) && v.LoaiVoucher == loaiYeuCau);
            
            if (voucher == null || !voucher.IsActive)
            {
                return (false, "Mã không hợp lệ hoặc không đúng loại!", 0, "");
            }

            if (tongTienMon < voucher.DieuKienToiThieu)
            {
                return (false, $"Đơn hàng chưa đạt mức {voucher.DieuKienToiThieu:#,0}đ!", 0, voucher.LoaiVoucher);
            }

            decimal soTienGiam = 0;
            if (voucher.LoaiVoucher == "Mon")
            {
                soTienGiam = tongTienMon * ((decimal)voucher.PhanTramGiam / 100);
            }
            else if (voucher.LoaiVoucher == "Ship")
            {
                soTienGiam = phiShip * ((decimal)voucher.PhanTramGiam / 100);
            }
            
            if (soTienGiam > voucher.GiamToiDa)
            {
                soTienGiam = voucher.GiamToiDa;
            }

            return (true, $"Đã giảm {soTienGiam:#,0}đ", soTienGiam, voucher.LoaiVoucher);
        }
    }
}
