using System;

namespace ĐACN.Services
{
    public static class GeoUtils
    {
        public const double EarthRadiusKm = 6371.0;
        public const double EarthRadiusMeters = 6371000.0;

        /// <summary>
        /// Calculates distance in Kilometers between two coordinates using Haversine formula
        /// </summary>
        public static double CalculateDistanceInKm(double lat1, double lon1, double lat2, double lon2)
        {
            var dLat = ToRadians(lat2 - lat1);
            var dLon = ToRadians(lon2 - lon1);
            var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                    Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) *
                    Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            return EarthRadiusKm * c;
        }

        /// <summary>
        /// Calculates distance in Meters between two coordinates
        /// </summary>
        public static double CalculateDistanceInMeters(double lat1, double lon1, double lat2, double lon2)
        {
            return CalculateDistanceInKm(lat1, lon1, lat2, lon2) * 1000.0;
        }

        public static double ToRadians(double angle)
        {
            return Math.PI * angle / 180.0;
        }
    }
}
