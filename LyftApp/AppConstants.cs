using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Shyft;
using Windows.UI;

namespace LyftApp
{
    public static class AppConstants
    {
        public static ShyftSandboxClient ShyftClient;

        public static Color LyftOffWhite { get { return GetColor("#F3F3F5"); } }
        public static Color LyftCharcoal { get { return GetColor("#333347"); } }
        public static Color LyftPink { get { return GetColor("#FF00BF"); } }
        public static Color LyftMulberry { get { return GetColor("#352384"); } }

        public static Color GetColor(string hex)
        {
            hex = hex.Replace("#", string.Empty);
            byte r = Convert.ToByte(hex.Substring(0, 2), 16);
            byte g = Convert.ToByte(hex.Substring(2, 2), 16);
            byte b = Convert.ToByte(hex.Substring(4, 2), 16);
            return Color.FromArgb(255, r, g, b);
        }
    }
}
