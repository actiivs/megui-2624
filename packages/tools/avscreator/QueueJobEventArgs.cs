using System;
using System.IO;

namespace MeGUI
{
    public class QueueJobEventArgs : EventArgs
    {
        protected EventHandler QueuingJob_Local;
        protected EventHandler QueuingJob_Remote;

        public decimal Fps { get; set; }
        public string FilenameWithoutExtension { get; set; }
        public string SourceFilename { get; set; }

        public virtual void Execute(MainForm main)
        {
            if ((SourceFilename.Contains("D:\\") || SourceFilename.Contains("E:\\") || SourceFilename.Contains("F:\\") || SourceFilename.Contains("G:\\")) && File.Exists(SourceFilename))
            {
                QueuingJob_Local = (o, args) =>
                {
                    main.VideoEncodingComponent.PlayerClosed -= QueuingJob_Local;
                    main.VideoEncodingComponent.SelectProfile("x264: Slow - 2000k");
                    main.VideoEncodingComponent.QueueJob();

                    var mw = new MuxWindow(main.PackageSystem.MuxerProviders["mkvmerge"], main);
                    var videoOutput = string.Format("{0}.264", FilenameWithoutExtension);
                    var audioOutput = SourceFilename;
                    var output = string.Format("{0}.mkv", FilenameWithoutExtension);
                    mw.QueueMuxJob(videoOutput, audioOutput, output, Fps);
                };
                main.VideoEncodingComponent.PlayerClosed += QueuingJob_Local;
            }
            else
            {
                QueuingJob_Remote = (o, args) =>
                {
                    main.VideoEncodingComponent.PlayerClosed -= QueuingJob_Remote;
                    RemoveVideoExtension(SourceFilename);
                };
                main.VideoEncodingComponent.PlayerClosed += QueuingJob_Remote;
            }
        }

        public static void RemoveVideoExtension(string filePath)
        {
            try
            {

                if (filePath.Contains(".mkv"))
                    File.Move(filePath, filePath.Replace(".mkv", ""));
                else if (filePath.Contains(".mp4"))
                    File.Move(filePath, filePath.Replace(".mp4", ""));
                else if (filePath.Contains(".wmv"))
                    File.Move(filePath, filePath.Replace(".wmv", ""));
                else if (filePath.Contains(".avi"))
                    File.Move(filePath, filePath.Replace(".avi", ""));
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }
    }
}
