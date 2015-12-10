﻿// ****************************************************************************
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
using System.Text.RegularExpressions;

using MeGUI.core.util;

namespace MeGUI
{
    public class DGIIndexer : CommandlineJobProcessor<DGIIndexJob>
    {
        public static readonly JobProcessorFactory Factory = new JobProcessorFactory(new ProcessorFactory(init), "DGIIndexer");

        private static IJobProcessor init(MainForm mf, Job j)
        {
            if (j is DGIIndexJob) 
                return new DGIIndexer(mf.Settings.DGIndexNV.Path);
            return null;
        }

        public DGIIndexer(string executableName)
        {
            UpdateCacher.CheckPackage("dgindexnv");
            executable = executableName;
        }

        public override void ProcessLine(string line, StreamType stream, ImageType oType)
        {
            if (Regex.IsMatch(line, "^[0-9]{1,3}$", RegexOptions.Compiled))
            {
                su.PercentageDoneExact = Int32.Parse(line);
                return;
            }

            if (line.Contains("Project"))
                su.Status = "Creating DGI...";
            else
                su.Status = "Creating " + line;
            base.startTime = DateTime.Now;
            base.ProcessLine(line, stream, oType);
        }

        protected override void checkJobIO()
        {
            try
            {
                if (!String.IsNullOrEmpty(job.Output))
                    FileUtil.ensureDirectoryExists(Path.GetDirectoryName(job.Output));
            }
            finally
            {
                base.checkJobIO();
            }
            su.Status = "Creating DGI...";
        }

        protected override string Commandline
        {
            get
            {
                StringBuilder sb = new StringBuilder();
                sb.Append("-i \"" + job.Input + "\"");
                if (MainForm.Instance.Settings.AutoLoadDG && Path.GetExtension(job.Input).ToLowerInvariant().Equals(".vob"))
                {
                    string strFile = Path.GetFileNameWithoutExtension(job.Input);
                    int iNumber = 0;
                    if (int.TryParse(strFile.Substring(strFile.Length - 1), out iNumber))
                    {
                        while (++iNumber < 10)
                        {
                            string strNewFile = "";
                            strNewFile = Path.Combine(Path.GetDirectoryName(job.Input), strFile.Substring(0, strFile.Length - 1) + iNumber.ToString() + ".vob");
                            if (File.Exists(strNewFile))
                                sb.Append(",\"" + strNewFile + "\"");
                            else
                                break;
                        }
                    }
                }
                if (job.DemuxVideo)
                    sb.Append(" -od \"" + job.Output + "\" -e -h");
                else 
                    sb.Append(" -o \"" + job.Output + "\" -e -h");
                if (job.DemuxMode == 2)
                    sb.Append(" -a"); // demux everything
                return sb.ToString();
            }
        }

        protected override void doExitConfig()
        {
            if (!File.Exists(job.Output))
                su.HasError = true;

            base.doExitConfig();
        }
    }
}