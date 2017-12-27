using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Devices.Geolocation;

namespace LyftApp
{
    public static class HelperMethods
    {
        public static bool EqualsOther(this BasicGeoposition p1, BasicGeoposition p2)
        {
            return p1.Latitude == p2.Latitude &&
                p1.Longitude == p2.Longitude &&
                p1.Altitude == p2.Altitude;
        }
    }
}
