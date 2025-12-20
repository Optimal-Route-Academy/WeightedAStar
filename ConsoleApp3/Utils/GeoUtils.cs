using System;

namespace ConsoleApp3.Utils
{
    /// <summary>
    /// Cografi hesaplamalar icin yardimci metotlar icerir.
    /// </summary>
    public static class GeoUtils
    {
        private const double EarthRadiusMeters = 6371000.0;

        /// <summary>
        /// Iki cografi koordinat arasindaki mesafeyi Haversine formulunu kullanarak metre cinsinden hesaplar.
        /// </summary>
        /// <param name="lat1">Birinci noktanin enlemi (derece).</param>
        /// <param name="lon1">Birinci noktanin boylami (derece).</param>
        /// <param name="lat2">Ikinci noktanin enlemi (derece).</param>
        /// <param name="lon2">Ikinci noktanin boylami (derece).</param>
        /// <returns>Iki nokta arasindaki mesafe (metre).</returns>
        public static double HaversineDistance(double lat1, double lon1, double lat2, double lon2)
        {
            var dLat = ToRadians(lat2 - lat1);
            var dLon = ToRadians(lon2 - lon1);

            var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                    Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) *
                    Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

            var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            var distance = EarthRadiusMeters * c;

            return distance;
        }

        /// <summary>
        /// Dereceyi radyana cevirir.
        /// </summary>
        private static double ToRadians(double angle)
        {
            return Math.PI * angle / 180.0;
        }
    }
}
