using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using StarResonanceDpsAnalysis.Assets;

namespace StarResonanceDpsAnalysis.WinForm.Extends
{
    public static class ProfessionThemeExtends
    {
        private static readonly Color DefaultColor = Color.FromArgb(0x67, 0xAE, 0xF6);
        private static readonly Dictionary<string, Color> LightThemeProfessionColorDict = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Color> DarkThemeProfessionColorDict = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Image> ProfessionImageDict = new(StringComparer.OrdinalIgnoreCase);

        private sealed record ProfessionThemeResources(Color LightColor, Color DarkColor, Image Image, params string[] Synonyms);

        private static readonly Dictionary<string, ProfessionThemeResources> ProfessionResources = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Marksman"] = new(
                LightColor: Color.FromArgb(0xff, 0xfc, 0xa3),
                DarkColor: Color.FromArgb(0x8e, 0x8b, 0x47),
                Image: HandledAssets.神射手,
                Synonyms: new[] { "神射手", "狼弓", "鹰弓" }),

            ["Verdant Oracle"] = new(
                LightColor: Color.FromArgb(0x78, 0xff, 0x95),
                DarkColor: Color.FromArgb(0x63, 0x9c, 0x70),
                Image: HandledAssets.森语者,
                Synonyms: new[] { "森语者", "惩戒", "愈合" }),

            ["Stormblade"] = new(
                LightColor: Color.FromArgb(0xb8, 0xa3, 0xff),
                DarkColor: Color.FromArgb(0x70, 0x62, 0x9c),
                Image: HandledAssets.雷影剑士,
                Synonyms: new[] { "雷影剑士", "居合", "月刃" }),

            ["Frost Mage"] = new(
                LightColor: Color.FromArgb(0xaa, 0xa6, 0xff),
                DarkColor: Color.FromArgb(0x79, 0x77, 0x9c),
                Image: HandledAssets.冰魔导师,
                Synonyms: new[] { "冰魔导师", "冰矛", "射线" }),

            ["Wind Knight"] = new(
                LightColor: Color.FromArgb(0xab, 0xfa, 0xff),
                DarkColor: Color.FromArgb(0x79, 0x9a, 0x9c),
                Image: HandledAssets.青岚骑士,
                Synonyms: new[] { "青岚骑士", "重装", "空枪" }),

            ["Heavy Guardian"] = new(
                LightColor: Color.FromArgb(0x8e, 0xe3, 0x92),
                DarkColor: Color.FromArgb(0x53, 0x77, 0x58),
                Image: HandledAssets.巨刃守护者,
                Synonyms: new[] { "巨刃守护者", "岩盾", "格挡" }),

            ["Shield Knight"] = new(
                LightColor: Color.FromArgb(0xbf, 0xe6, 0xff),
                DarkColor: Color.FromArgb(0x9c, 0x9b, 0x75),
                Image: HandledAssets.神盾骑士,
                Synonyms: new[] { "神盾骑士", "防盾", "光盾" }),

            ["Soul Musician"] = new(
                LightColor: Color.FromArgb(0xff, 0x53, 0x53),
                DarkColor: Color.FromArgb(0x9c, 0x53, 0x53),
                Image: HandledAssets.灵魂乐手,
                Synonyms: new[] { "灵魂乐手", "协奏", "狂音" })
        };

        static ProfessionThemeExtends()
        {
            foreach (var (canonical, resource) in ProfessionResources)
            {
                LightThemeProfessionColorDict[canonical] = resource.LightColor;
                DarkThemeProfessionColorDict[canonical] = resource.DarkColor;
                ProfessionImageDict[canonical] = resource.Image;

                foreach (var synonym in resource.Synonyms)
                {
                    LightThemeProfessionColorDict[synonym] = resource.LightColor;
                    DarkThemeProfessionColorDict[synonym] = resource.DarkColor;
                    ProfessionImageDict[synonym] = resource.Image;
                }
            }
        }

        private static Bitmap? _emptyBitmap;
        public static Bitmap EmptyBitmap
        {
            get
            {
                if (_emptyBitmap == null)
                {
                    _emptyBitmap = new Bitmap(1, 1, PixelFormat.Format32bppArgb);
                    using var g = Graphics.FromImage(_emptyBitmap);
                    g.Clear(Color.Transparent);
                }

                return _emptyBitmap;
            }
        }

        public static Color GetProfessionThemeColor(this string professionName, bool isLightTheme)
        {
            if (TryGetProfessionThemeColor(professionName, isLightTheme, out var color))
            {
                return color;
            }

            return DefaultColor;
        }

        public static bool TryGetProfessionThemeColor(this string professionName, bool isLightTheme, out Color color) 
        {
            var dic = isLightTheme
                ? LightThemeProfessionColorDict
                : DarkThemeProfessionColorDict;

            return dic.TryGetValue(professionName, out color);
        }

        public static Image GetProfessionImage(this string professionName, Image? def = null)
        {
            if (ProfessionImageDict.TryGetValue(professionName, out var image))
            {
                return image;
            }

            return def ?? EmptyBitmap;
        }

        public static Bitmap GetProfessionBitmap(this string professionName, Bitmap? def = null) 
        {
            if (ProfessionImageDict.TryGetValue(professionName, out var image))
            {
                return (Bitmap)image;
            }

            return def ?? EmptyBitmap;
        }
    }
}
