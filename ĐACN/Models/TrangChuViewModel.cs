 namespace ĐACN.Models
{
    using System.Collections.Generic;

    public class TrangChuViewModel
    {
        public List<LoaiMonAnViewModel> DanhMuc { get; set; }
        public List<NhaHangViewModel> NhaHang { get; set; }
        public List<NhaHangViewModel> RecommendedNhaHang { get; set; }
    }

    public class LoaiMonAnViewModel
    {
        public string MaLoai { get; set; }
        public string TenLoai { get; set; }
        public string HinhAnh { get; set; } // thêm để hiển thị hình ảnh
    }

    public class NhaHangViewModel
    {
        public string MaNH { get; set; }
        public string TenNH { get; set; }
        public string DiaChi { get; set; }
        public string TrangThai { get; set; }
        public string HinhAnh { get; set; }
        public double Rating { get; set; }
        public int TongLuotMua { get; set; }
        public double Score { get; set; }
        public string SDT { get; set; }

        // Thêm các thuộc tính mới
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public double KhoangCachKm { get; set; } // Để hiển thị khoảng cách
    }
}
