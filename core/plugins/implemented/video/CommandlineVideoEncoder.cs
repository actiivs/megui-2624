// ****************************************************************************
// 
// Copyright (C) 2005-2015 Doom9 & al
// 
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
// 
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
// 
// You should have received a copy of the GNU General Public License
// along with this program; if not, write to the Free Software
// Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA  02111-1307  USA
// 
// ****************************************************************************

using System;
using System.IO;
using System.Text;

using MeGUI.core.util;

namespace MeGUI
{
    public delegate void EncoderOutputCallback(string line, int type);

    public abstract class CommandlineVideoEncoder : CommandlineJobProcessor<VideoJob>
    {
        #region variables
        private ulong numberOfFrames;
        private Dar? dar;
        private ulong? currentFrameNumber;
        protected int lastStatusUpdateFramePosition = 0;
        protected int hres = 0, vres = 0;
        protected int fps_n = 0, fps_d = 0;
        protected bool usesSAR = false;
        #endregion

        public CommandlineVideoEncoder() : base()
        {
        }

        #region helper methods
        protected override void checkJobIO()
        {
            base.checkJobIO();
            if (File.Exists(job.Input) && Path.GetExtension(job.Input).ToLowerInvariant().Equals(".avs"))
            {
                string strAVSFile = String.Empty;
                try
                {
                    StreamReader sr = new StreamReader(job.Input, Encoding.Default);
                    strAVSFile = sr.ReadToEnd();
                    sr.Close();
                }
                catch (Exception) {}
                log.LogValue("Avisynth input script", strAVSFile);
            }
            su.Status = "Encoding video...";
            getInputProperties(job);
        }

        /// <summary>
        /// tries to open the video source and gets the number of frames from it, or 
        /// exits with an error
        /// </summary>
        /// <param name="videoSource">the AviSynth script</param>
        /// <param name="error">return parameter for all errors</param>
        /// <returns>true if the file could be opened, false if not</returns>
        protected void getInputProperties(VideoJob job)
        {
            double fps;
            Dar d;
            JobUtil.GetAllInputProperties(job.Input, out numberOfFrames, out fps, out fps_n, out fps_d, out hres, out vres, out d);
            dar = job.DAR;
            su.NbFramesTotal = numberOfFrames;
            su.ClipLength = TimeSpan.FromSeconds((double)numberOfFrames / fps);
        }

        protected override void doExitConfig()
        {
            if (!su.HasError && !su.WasAborted)
                compileFinalStats();

            base.doExitConfig();
        }

        /// <summary>
        /// compiles final bitrate statistics
        /// </summary>
        protected void compileFinalStats()
        {
            try
            {
                if (!string.IsNullOrEmpty(job.Output) && File.Exists(job.Output))
                {
                    FileInfo fi = new FileInfo(job.Output);
                    long size = fi.Length; // size in bytes

                    ulong framecount;
                    double framerate;
                    JobUtil.getInputProperties(out framecount, out framerate, job.Input);

                    double numberOfSeconds = (double)framecount / framerate;
                    long bitrate = (long)((double)(size * 8.0) / (numberOfSeconds * 1000.0));

                    LogItem stats = log.Info(string.Format("[{0:G}] {1}", DateTime.Now, "Final statistics"));

                    if (job.Settings.VideoEncodingType == VideoCodecSettings.VideoEncodingMode.CQ) // CQ mode
                        stats.LogValue("Constant Quantizer Mode", "Quantizer " + job.Settings.BitrateQuantizer + " computed...");
                    else if (job.Settings.VideoEncodingType == VideoCodecSettings.VideoEncodingMode.quality)
                        stats.LogValue("Constant Quality Mode", "Quality " + job.Settings.BitrateQuantizer + " computed...");
                    else
                        stats.LogValue("Video Bitrate Desired", job.Settings.BitrateQuantizer + " kbit/s");

                    stats.LogValue("Video Bitrate Obtained (approximate)", bitrate + " kbit/s");
                }
            }
            catch (Exception e)
            {
                log.LogValue("Exception in compileFinalStats", e, ImageType.Warning);
            }
        }
        #endregion

        protected bool setFrameNumber(string frameString)
        {
            int currentFrameNumber;
            if (int.TryParse(frameString, out currentFrameNumber))
            {
                if (currentFrameNumber < 0)
                    this.currentFrameNumber = 0;
                else
                    this.currentFrameNumber = (ulong)currentFrameNumber;
                 return true;
            }
            return false;
        }

        protected override void doStatusCycleOverrides()
        {
            su.NbFramesDone = currentFrameNumber;
        }
    }
}