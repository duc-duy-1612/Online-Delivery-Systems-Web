using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Globalization;
using System.Net.Http;
using System.Threading.Tasks;

namespace ĐACN.Services
{
    public class MapRoutingService
    {
        private static readonly HttpClient _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

        private readonly string _orsApiKey;

        public MapRoutingService()
        {
            _orsApiKey = ConfigurationManager.AppSettings["ORS_API_KEY"];
        }

        public async Task<(double? lat, double? lng)> GeoCodeORSAsync(string address)
        {
            if (string.IsNullOrWhiteSpace(address)) return (null, null);
            
            try
            {
                var url = $"https://api.openrouteservice.org/geocode/search?api_key={_orsApiKey}&text={Uri.EscapeDataString(address)}";

                using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    request.Headers.TryAddWithoutValidation("Authorization", _orsApiKey);
                    var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode) return (null, null);

                    var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    var obj = JObject.Parse(json);
                    var features = obj["features"] as JArray;
                    
                    if (features != null && features.Count > 0)
                    {
                        var geometry = features[0]["geometry"]["coordinates"];
                        return (geometry[1].Value<double>(), geometry[0].Value<double>());
                    }
                }
            }
            catch
            {
                // Ignore or log error
            }

            return (null, null);
        }

        public async Task<dynamic> GetRouteDataORSAsync(double startLat, double startLng, double endLat, double endLng)
        {
            try
            {
                string startParam = $"{startLng.ToString(CultureInfo.InvariantCulture)},{startLat.ToString(CultureInfo.InvariantCulture)}";
                string endParam = $"{endLng.ToString(CultureInfo.InvariantCulture)},{endLat.ToString(CultureInfo.InvariantCulture)}";
                var url = $"https://api.openrouteservice.org/v2/directions/driving-car?start={startParam}&end={endParam}&preference=fastest";

                using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    request.Headers.TryAddWithoutValidation("Authorization", _orsApiKey);
                    var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode) return null;

                    var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    var obj = JObject.Parse(json);
                    var features = obj["features"] as JArray;
                    if (features == null || features.Count == 0) return null;

                    var geometry = features[0]["geometry"]["coordinates"];
                    var summary = features[0]["properties"]["summary"];

                    var routePoints = new List<object>();
                    foreach (var point in geometry)
                    {
                        routePoints.Add(new { lat = point[1].Value<double>(), lng = point[0].Value<double>() });
                    }

                    return new
                    {
                        route = routePoints,
                        distance = summary["distance"].Value<double>(),
                        duration = summary["duration"].Value<double>()
                    };
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in GetRouteDataORSAsync: {ex.Message}");
                return null;
            }
        }

        public double CalculateHaversineDistance(double lat1, double lon1, double lat2, double lon2)
        {
            return GeoUtils.CalculateDistanceInMeters(lat1, lon1, lat2, lon2);
        }
    }
}
