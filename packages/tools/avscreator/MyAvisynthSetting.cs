using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using MeGUI.core.util;

namespace MeGUI
{
    static class MyAvisynthSetting
    {
        public static Dictionary<string, string> SplitOptions = new Dictionary<string, string>
        {
            {"44x.me", "--split parts:00:01:54-"},
            {"88q.me", "--split parts:00:01:54-"},
            {"6sht.me", "--split parts:00:00:10-"},
            {"7sht.me", "--split parts:00:00:10-"},
        };

        private static readonly List<string> HdSourceKeyword = new List<string>
        {
	        "FHD",
			"HD",
            "1080p",
            "720p",
            "FHD60fps",
            "FHDwmf",
        };

        private static readonly List<string> UncensoredKeyword = new List<string>
        {
            "carib",
            "1pon",
            "mura",
            "10mu",
            "gachi",
            "paco",
            "heyzo",
            "heydouga",
            "pgm_",
        };

        public static bool IsHd(string filename)
        {
            return HdSourceKeyword.Any(keyword => filename.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        public static bool IsUncensored(string filename)
        {
            return UncensoredKeyword.Any(filename.Contains);
        }

        public static bool IsLocal(this string filePath)
        {
            return filePath.Contains("D:\\") || filePath.Contains("E:\\")
                || filePath.Contains("F:\\") || filePath.Contains("G:\\") && File.Exists(filePath);
        }

        public static string GetInputFilename(string inputFileName)
        {
            var path = Path.GetDirectoryName(inputFileName);
            var name = Path.GetFileNameWithoutExtension(inputFileName);
            var ext = Path.GetExtension(inputFileName);

            if (!FileUtil.IsAllLetterUpper(name))
            {
                name = name.ToUpper();
            }

            name = name.Replace("60FPS", string.Empty);
            name = name.Replace("MEOWISO_", string.Empty);
	        name = name.Replace("_6M", string.Empty);

            if (name.Contains(".FHD"))
            {
                name = name.Replace(".FHD", string.Empty);
            }
            else if (name.Contains(".1080P"))
            {
                name = name.Replace(".1080P", string.Empty);
            }

            name = name.Replace("FHD_", "[FHD]");
            name = name.Replace("FHD-", "[FHD]");
            name = name.Replace("[FHD]-", "[FHD]");
            name = name.Replace("[FHDWMF]", "[FHD]");
	        name = name.Replace("FHDWMF-", "[FHD]");
	        name = name.Replace("[THZ.LA]", "[FHD]");
            name = name.Replace("CD1", "A");
            name = name.Replace("CD2", "B");
            name = name.Replace("CD3", "C");
            name = name.Replace("CD4", "D");

            foreach(var option in SplitOptions)
            {
                name = name.Replace($"[{option.Key.ToUpper()}]", string.Empty);
            }

            name = Regex.Replace(name, @"\[\d+\]", string.Empty);

	        if (!name.StartsWith("[FHD]"))
	        {
		        name = name.Insert(0, "[FHD]");
	        }

            var newFilePath = Path.Combine(path, name + ext);
            File.Move(inputFileName, newFilePath);
            return newFilePath;
        }
    }
}
