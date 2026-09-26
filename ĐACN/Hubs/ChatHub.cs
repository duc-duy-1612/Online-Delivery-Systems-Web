using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Microsoft.AspNet.SignalR;
using ĐACN;
using ĐACN.Models;
using ĐACN.Controllers;

namespace ĐACN.Hubs
{
    public class ChatHub : Hub
    {
        // Lưu trữ ConnectionId theo UserId để gửi tin nhắn riêng (Private Message)
        // Trong thực tế nên lưu vào Database hoặc Redis, ở đây dùng bộ nhớ tạm
        private static readonly ConcurrentDictionary<string, string> UserConnections = new ConcurrentDictionary<string, string>();

        public void Connect(string userId)
        {
            if (!string.IsNullOrEmpty(userId))
            {
                UserConnections[userId] = Context.ConnectionId;
            }
        }

        public async Task JoinOrderGroup(string maDon)
        {
            if (string.IsNullOrEmpty(maDon)) return;

            var httpContext = Context.Request.GetHttpContext();
            var tk = httpContext?.Session?["TaiKhoan"] as TaiKhoan;

            // Kiểm tra bảo mật nếu có session đăng nhập
            if (tk != null)
            {
                var maKH = httpContext?.Session?["MaKH"] as string;
                var maNH = httpContext?.Session?["MaNH"] as string;
                var maShipper = httpContext?.Session?["MaShipper"] as string;

                using (var db = new FoodDeliveryDBEntities())
                {
                    var don = db.DonHangs.Find(maDon);
                    if (don != null)
                    {
                        bool isAuthorized = (tk.VaiTro == "Admin") ||
                                            (tk.VaiTro == "KhachHang" && don.MaKH == maKH) ||
                                            (tk.VaiTro == "NhaHang" && don.MaNH == maNH) ||
                                            (tk.VaiTro == "Shipper" && don.MaShipper == maShipper);
                        if (!isAuthorized)
                        {
                            return; // Chặn truy cập trái phép vào group chat của đơn hàng
                        }
                    }
                }
            }

            await Groups.Add(Context.ConnectionId, "Order_" + maDon);
        }

        public async Task SendMessage(string senderId, string senderName, string receiverId, string message, string maDon)
        {
            if (string.IsNullOrEmpty(message)) return;

            var chatMsg = new ĐACN.Models.ChatMessage
            {
                Id = Guid.NewGuid().ToString(),
                SenderId = senderId,
                SenderName = senderName,
                ReceiverId = receiverId,
                Message = message,
                MaDon = maDon,
                Timestamp = DateTime.Now
            };

            // Lưu vào hàng đợi in-memory để đồng bộ với polling HTTP
            ĐACN.Controllers.ChatController.AddMessage(chatMsg);

            var msgObj = new {
                id = chatMsg.Id,
                senderId = chatMsg.SenderId,
                senderName = chatMsg.SenderName,
                receiverId = chatMsg.ReceiverId,
                message = chatMsg.Message,
                timestamp = (long)(chatMsg.Timestamp.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds
            };

            // Nếu có mã đơn, gửi cho tất cả người trong nhóm đơn hàng đó
            if (!string.IsNullOrEmpty(maDon))
            {
                await Clients.Group("Order_" + maDon).receiveMessage(msgObj);
            }
            else
            {
                // Gửi trực tiếp cho người nhận nếu họ đang online
                if (!string.IsNullOrEmpty(receiverId) && UserConnections.TryGetValue(receiverId, out string receiverConnectionId))
                {
                    await Clients.Client(receiverConnectionId).receiveMessage(msgObj);
                }
                
                // Gửi lại cho chính người gửi để hiển thị
                await Clients.Caller.receiveMessage(msgObj);
            }
        }
    }
}
