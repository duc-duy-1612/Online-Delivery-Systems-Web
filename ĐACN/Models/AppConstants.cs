namespace ĐACN.Models
{
    public static class UserRoles
    {
        public const string KhachHang = "KhachHang";
        public const string Shipper = "Shipper";
        public const string NhaHang = "NhaHang";
        public const string Admin = "Admin";
    }

    public static class OrderStatuses
    {
        public const string ChoXacNhan = "Chờ xác nhận";
        public const string DaXacNhan = "Đã xác nhận";
        public const string DangChuanBi = "Đang chuẩn bị";
        public const string DangLayMon = "Đang lấy món";
        public const string DangGiao = "Đang giao";
        public const string DaGiao = "Đã giao";
        public const string DaHuy = "Đã hủy";
        public const string Huy = "Hủy";
    }

    public static class StoreStatuses
    {
        public const string DangMoCua = "Đang mở cửa";
        public const string DaDongCua = "Đã đóng cửa";
    }
}
