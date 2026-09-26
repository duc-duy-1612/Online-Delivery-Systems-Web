using System;
using System.Collections.Generic;

namespace ĐACN.Models
{
    public class DonHangItemSummary
    {
        public string TenMon { get; set; }
        public int SoLuong { get; set; }
        public string GhiChu { get; set; }
    }

    public class DonHangListViewModel
    {
        public string MaDon { get; set; }
        public string TenKhachHang { get; set; }
        public DateTime? ThoiGianDat { get; set; }
        public decimal? TongTien { get; set; }
        public string TrangThai { get; set; }
        public string TenShipper { get; set; } // Lưu tên shipper dưới dạng string để tránh proxy issue
        public List<DonHangItemSummary> DanhSachMon { get; set; } = new List<DonHangItemSummary>();
        public int? ThoiGianChuanBiPhut { get; set; } = 15;
    }
}

