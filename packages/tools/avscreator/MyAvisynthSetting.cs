using System.IO;

namespace MeGUI
{
    static class MyAvisynthSetting
    {
        public static bool IsLocal(this string filePath)
        {
            return filePath.Contains("D:\\") || filePath.Contains("E:\\")
                || filePath.Contains("F:\\") || filePath.Contains("G:\\") && File.Exists(filePath);
        }
    }
}
