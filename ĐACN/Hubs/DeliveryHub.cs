using System.Threading.Tasks;
using Microsoft.AspNet.SignalR;
using System.Diagnostics;
using ĐACN.Models;

namespace ĐACN.Hubs
{
    public class DeliveryHub : Hub
    {
        // Khách hàng join group theo Mã Khách Hàng (ví dụ: KhachHang_KH001)
        // Nhà hàng join group theo Mã Nhà Hàng (ví dụ: NhaHang_NH001)
        // Shipper join group theo "Shippers" để nhận thông báo đơn mới, và "Shipper_SP001" cho tin cá nhân
        public Task JoinGroup(string groupName)
        {
            if (string.IsNullOrWhiteSpace(groupName)) return Task.CompletedTask;

            var httpContext = Context.Request.GetHttpContext();
            var tk = httpContext?.Session?["TaiKhoan"] as TaiKhoan;

            // Kiểm tra bảo mật phân quyền nhóm SignalR
            if (groupName.StartsWith("KhachHang_"))
            {
                var maKH = httpContext?.Session?["MaKH"] as string;
                if (tk == null || (tk.VaiTro != "Admin" && (tk.VaiTro != "KhachHang" || groupName != "KhachHang_" + maKH)))
                {
                    Debug.WriteLine($"SignalR Security: Connection {Context.ConnectionId} bị từ chối vào {groupName}");
                    return Task.CompletedTask;
                }
            }
            else if (groupName.StartsWith("NhaHang_"))
            {
                var maNH = httpContext?.Session?["MaNH"] as string;
                if (tk == null || (tk.VaiTro != "Admin" && (tk.VaiTro != "NhaHang" || groupName != "NhaHang_" + maNH)))
                {
                    Debug.WriteLine($"SignalR Security: Connection {Context.ConnectionId} bị từ chối vào {groupName}");
                    return Task.CompletedTask;
                }
            }
            else if (groupName.StartsWith("Shipper_"))
            {
                var maShipper = httpContext?.Session?["MaShipper"] as string;
                if (tk == null || (tk.VaiTro != "Admin" && (tk.VaiTro != "Shipper" || groupName != "Shipper_" + maShipper)))
                {
                    Debug.WriteLine($"SignalR Security: Connection {Context.ConnectionId} bị từ chối vào {groupName}");
                    return Task.CompletedTask;
                }
            }
            else if (groupName == "Shippers")
            {
                if (tk == null || (tk.VaiTro != "Shipper" && tk.VaiTro != "Admin"))
                {
                    Debug.WriteLine($"SignalR Security: Connection {Context.ConnectionId} bị từ chối vào {groupName}");
                    return Task.CompletedTask;
                }
            }

            Debug.WriteLine($"SignalR: Connection {Context.ConnectionId} joined group {groupName}");
            return Groups.Add(Context.ConnectionId, groupName);
        }

        public Task LeaveGroup(string groupName)
        {
            Debug.WriteLine($"SignalR: Connection {Context.ConnectionId} left group {groupName}");
            return Groups.Remove(Context.ConnectionId, groupName);
        }
    }
}
