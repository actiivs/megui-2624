using System.IO;
using System.Linq;

namespace MeGUI
{
    public class CustomQueueJobEventArgs : QueueJobEventArgs
    {
        public bool IsD2V { get; set; }

        public override void Execute(MainForm main)
        {
            if (IsD2V)
            {
                main.VideoEncodingComponent.SelectProfile("x264: Slow - 1200k");
                main.VideoEncodingComponent.QueueJob();

                var srcName = Path.GetFileNameWithoutExtension(SourceFilename);
                var srcFolder = Path.GetDirectoryName(SourceFilename);
                var audioFilePath = Directory.GetFiles(srcFolder).FirstOrDefault(f => f.Contains(srcName) && f.EndsWith(".ac3"));

                if (!string.IsNullOrEmpty(audioFilePath))
                    main.AudioEncodingComponent.openAudioFile(audioFilePath);

                if (main.AudioEncodingComponent.Tabs.Any())
                {
                    main.AudioEncodingComponent.SelectProfile(main.AudioEncodingComponent.Tabs[0], "QAAC: 96Kbps");
                    main.AudioEncodingComponent.QueueJob(main.AudioEncodingComponent.Tabs[0]);

                    MuxWindow mw = new MuxWindow(main.PackageSystem.MuxerProviders["mkvmerge"], main);
                    var videoOutput = string.Format("{0}.264", FilenameWithoutExtension);
                    var audioOutput = main.AudioEncodingComponent.Tabs[0].AudioOutput;
                    var output = string.Format("{0}.mkv", FilenameWithoutExtension);
                    mw.QueueMuxJob(new MuxJobConfig
                    {
                        VideoFilename = videoOutput,
                        AudioFilename = audioOutput,
                        OutputFilename = output,
                        FrameRate = Fps
                    });
                }
            }
            else
            {
                if (SourceFilename.IsLocal())
                {
                    QueuingJob_Local = (o, args) =>
                    {
                        main.VideoEncodingComponent.PlayerClosed -= QueuingJob_Local;
                        main.VideoEncodingComponent.SelectProfile("x264: Slow - 2200k");
                        main.VideoEncodingComponent.QueueJob();

                        string audioOutput = null;
                        if (SourceFilename.Contains(".wmv"))
                        {
                            main.AudioEncodingComponent.openAudioFile(SourceFilename);
                            if (main.AudioEncodingComponent.Tabs.Any())
                            {
                                main.AudioEncodingComponent.SelectProfile(main.AudioEncodingComponent.Tabs[0], "QAAC: 128Kbps");
                                main.AudioEncodingComponent.QueueJob(main.AudioEncodingComponent.Tabs[0]);
                                audioOutput = main.AudioEncodingComponent.Tabs[0].AudioOutput;
                            }
                        }
                        else
                        {
                            audioOutput = SourceFilename;
                        }

                        if (!string.IsNullOrEmpty(audioOutput))
                        {
                            var mw = new MuxWindow(main.PackageSystem.MuxerProviders["mkvmerge"], main);
                            var videoOutput = string.Format("{0}.264", FilenameWithoutExtension);
                            var output = string.Format("{0}.mkv", FilenameWithoutExtension);

                            var splitOptions = MyAvisynthSetting.SplitOptions
                                .Where(s => this.OriginalSourceFilename.Contains(s.Key));

                            mw.QueueMuxJob(new MuxJobConfig
                            {
                                VideoFilename = videoOutput,
                                AudioFilename = audioOutput,
                                OutputFilename = output,
                                FrameRate = Fps,
                                OptionsString = splitOptions.Any() ? splitOptions.First().Value : null
                            });
                        }
                    };
                    main.VideoEncodingComponent.PlayerClosed += QueuingJob_Local;
                }
                else
                {
                    RemoveVideoExtension(SourceFilename);
                }
            }
        }
    }
}
