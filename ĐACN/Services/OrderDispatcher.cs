using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNet.SignalR;
using System.Diagnostics;
using ĐACN.Models;
using ĐACN.Hubs;
using System.Collections.Generic;
using System.Web.Hosting;

namespace ĐACN.Services
{
    public static class OrderDispatcher
    {
        public static void DispatchOrder(string maDon, string maNH)
        {
            if (string.IsNullOrWhiteSpace(maDon) || string.IsNullOrWhiteSpace(maNH)) return;

            HostingEnvironment.QueueBackgroundWorkItem(async cancellationToken => 
            {
                try
                {
                    // Lấy danh sách shippers đang online từ in-memory cache
                    var activeLocations = RealTimeLocationService.GetAllLocations()
                        .Where(l => (DateTime.Now - l.ThoiGianCapNhat).TotalMinutes < 15)
                        .ToList();

                    var context = GlobalHost.ConnectionManager.GetHubContext<DeliveryHub>();

                    string nhaHangTen = null;
                    double nhLat = 0;
                    double nhLng = 0;
                    HashSet<string> validSet = null;
                    HashSet<string> busySet = null;

                    // Truy vấn dữ liệu ban đầu với DbContext ngắn hạn, AsNoTracking để tối ưu hiệu năng
                    using (var db = new FoodDeliveryDBEntities()) 
                    {
                        var don = db.DonHangs.AsNoTracking()
                            .Where(d => d.MaDon == maDon)
                            .Select(d => new { d.MaShipper, d.TrangThai })
                            .FirstOrDefault();

                        if (don == null || !string.IsNullOrEmpty(don.MaShipper)) return;
                        if (don.TrangThai == OrderStatuses.DaHuy || don.TrangThai == OrderStatuses.Huy) return;
                        
                        var nhaHang = db.NhaHangs.AsNoTracking().FirstOrDefault(n => n.MaNH == maNH);
                        if (nhaHang == null) return;

                        nhLat = nhaHang.Latitude ?? 0;
                        nhLng = nhaHang.Longitude ?? 0;
                        if (nhLat == 0) return;
                        nhaHangTen = nhaHang.TenNH ?? maNH;

                        if (!activeLocations.Any())
                        {
                            context.Clients.Group("Shippers").notifyNewOrder($"Có đơn mới cần giao từ nhà hàng {nhaHangTen} ({maDon})");
                            return;
                        }

                        var activeShipperIds = activeLocations.Select(l => l.MaShipper).Distinct().ToList();
                        var validShipperIds = db.Shippers.AsNoTracking()
                            .Where(s => activeShipperIds.Contains(s.MaShipper) && s.TaiKhoan != null && s.TaiKhoan.TrangThai == true)
                            .Select(s => s.MaShipper)
                            .ToList();

                        var busyShipperIds = db.DonHangs.AsNoTracking()
                            .Where(d => validShipperIds.Contains(d.MaShipper) && (d.TrangThai == OrderStatuses.DangLayMon || d.TrangThai == OrderStatuses.DangGiao))
                            .Select(d => d.MaShipper)
                            .Distinct()
                            .ToList();

                        busySet = new HashSet<string>(busyShipperIds);
                        validSet = new HashSet<string>(validShipperIds);
                    }

                    var sortedShippers = activeLocations
                        .Where(loc => validSet.Contains(loc.MaShipper) && !busySet.Contains(loc.MaShipper))
                        .Select(loc => new
                        {
                            Location = loc,
                            Distance = GeoUtils.CalculateDistanceInKm(nhLat, nhLng, loc.Latitude, loc.Longitude)
                        })
                        .OrderBy(x => x.Distance)
                        .ToList();

                    if (!sortedShippers.Any())
                    {
                        context.Clients.Group("Shippers").notifyNewOrder($"Có đơn mới cần giao từ nhà hàng {nhaHangTen} ({maDon})");
                        return;
                    }

                    bool acceptedOrTerminated = false;

                    foreach (var item in sortedShippers) 
                    {
                        if (cancellationToken.IsCancellationRequested) break;

                        if (CheckIfOrderAssignedOrCancelled(maDon))
                        {
                            acceptedOrTerminated = true;
                            break;
                        }

                        // Ping shipper này với khoảng cách km
                        context.Clients.Group("Shipper_" + item.Location.MaShipper).pingOrder(maDon, Math.Round(item.Distance, 1));
                        
                        // Chờ tối đa 15 giây, kiểm tra mỗi 1.5 giây để phản hồi ngay khi shipper nhận
                        for (int i = 0; i < 10; i++)
                        {
                            await Task.Delay(1500, cancellationToken);
                            if (CheckIfOrderAssignedOrCancelled(maDon))
                            {
                                acceptedOrTerminated = true;
                                break;
                            }
                        }

                        if (acceptedOrTerminated) break;
                    }

                    if (!acceptedOrTerminated)
                    {
                        context.Clients.Group("Shippers").notifyNewOrder($"Có đơn mới cần giao từ nhà hàng {nhaHangTen} ({maDon})");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[OrderDispatcher Error]: {ex.Message}");
                }
            });
        }

        private static bool CheckIfOrderAssignedOrCancelled(string maDon)
        {
            try
            {
                using (var db = new FoodDeliveryDBEntities())
                {
                    var status = db.DonHangs.AsNoTracking()
                        .Where(d => d.MaDon == maDon)
                        .Select(d => new { d.MaShipper, d.TrangThai })
                        .FirstOrDefault();

                    if (status == null) return true;
                    return !string.IsNullOrEmpty(status.MaShipper) || status.TrangThai == OrderStatuses.DaHuy || status.TrangThai == OrderStatuses.Huy;
                }
            }
            catch
            {
                return false;
            }
        }
        
        private static double CalculateHaversineDistance(double lat1, double lon1, double lat2, double lon2)
        {
            return GeoUtils.CalculateDistanceInKm(lat1, lon1, lat2, lon2);
        }
    }
}
