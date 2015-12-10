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
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

using MeGUI.core.util;

namespace MeGUI
{
    public enum StreamType : ushort { None = 0, Stderr = 1, Stdout = 2 }

    public abstract class CommandlineJobProcessor<TJob> : IJobProcessor
        where TJob : Job
    {
        #region variables
        protected TJob job;
        protected DateTime startTime;
        protected bool isProcessing = false;
        protected Process proc = new Process(); // the encoder process
        protected string executable; // path and filename of the commandline encoder to be used
        protected ManualResetEvent mre = new ManualResetEvent(true); // lock used to pause encoding
        protected ManualResetEvent stdoutDone = new ManualResetEvent(false);
        protected ManualResetEvent stderrDone = new ManualResetEvent(false);
        protected StatusUpdate su;
        protected LogItem log;
        protected LogItem stdoutLog;
        protected LogItem stderrLog;
        protected Thread readFromStdErrThread;
        protected Thread readFromStdOutThread;
        protected List<string> tempFiles = new List<string>();
        protected bool bRunSecondTime = false;
        protected bool bWaitForExit = false;

        #endregion

        #region temp file utils
        protected void writeTempTextFile(string filePath, string text)
        {
            using (Stream temp = new FileStream(filePath, System.IO.FileMode.Create))
            {
                using (TextWriter avswr = new StreamWriter(temp, System.Text.Encoding.Default))
                {
                    avswr.WriteLine(text);
                }
            }
            tempFiles.Add(filePath);
        }
        private void deleteTempFiles()
        {
            foreach (string filePath in tempFiles)
                safeDelete(filePath);
        }

        private static void safeDelete(string filePath)
        {
            try
            {
                File.Delete(filePath);
            }
            catch
            {
                // Do Nothing
            }
        }
        #endregion

        protected virtual void checkJobIO()
        {
            Util.ensureExists(job.Input);
        }

        protected virtual void doExitConfig()
        {
            if (su.HasError || su.WasAborted)
                return;

            if (!String.IsNullOrEmpty(job.Output))
            {
                if (File.Exists(job.Output))
                {
                    MediaInfoFile oInfo = new MediaInfoFile(job.Output, ref log);
                }
                else if (Path.GetExtension(job.Output).ToLower().Equals(".avi"))
                {
                    string strFile = Path.GetFileNameWithoutExtension(job.Output) + " (1).avi";
                    strFile = Path.Combine(Path.GetDirectoryName(job.Output), strFile);
                    if (File.Exists(strFile))
                    {
                        MediaInfoFile oInfo = new MediaInfoFile(strFile, ref log);
                    }
                } 
            }
        }

        // returns true if the exit code yields a meaningful answer
        protected virtual bool checkExitCode
        {
            get { return true; }
        }

        protected virtual void getErrorLine()
        {
            return;
        }

        protected abstract string Commandline
        {
            get;
        }

        /// <summary>
        /// handles the encoder process existing
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        protected void proc_Exited(object sender, EventArgs e)
        {
            mre.Set();  // Make sure nothing is waiting for pause to stop
            stdoutDone.WaitOne(); // wait for stdout to finish processing
            stderrDone.WaitOne(); // wait for stderr to finish processing

            // check the exitcode
            if (checkExitCode && proc.ExitCode != 0) 
            {
                getErrorLine();
                string strError = WindowUtil.GetErrorText(proc.ExitCode);
                if (!su.WasAborted)
                {
                    su.HasError = true;
                    log.LogEvent("Process exits with error: " + strError, ImageType.Error);
                }
                else
                {
                    log.LogEvent("Process exits with error: " + strError);
                }
            }

            if (bRunSecondTime)
            {
                bRunSecondTime = false;
                start();
            }
            else
            {
                su.IsComplete = true;
                doExitConfig();
                StatusUpdate(su);
            }

            bWaitForExit = false;
        }

        #region IVideoEncoder overridden Members

        public void setup(Job job2, StatusUpdate su, LogItem log)
        {
            Debug.Assert(job2 is TJob, "Job is the wrong type");

            this.log = log;
            TJob job = (TJob)job2;
            this.job = job;

            // This enables relative paths, etc
            if (!File.Exists(executable))
                executable = Path.Combine(System.Windows.Forms.Application.StartupPath, executable);

            Util.ensureExists(executable);

            this.su = su;
            checkJobIO();
        }

        public void start()
        {
            proc = new Process();
            ProcessStartInfo pstart = new ProcessStartInfo();
            pstart.FileName = executable;
            pstart.Arguments = Commandline;
            pstart.RedirectStandardOutput = true;
            pstart.RedirectStandardError = true;
            pstart.WindowStyle = ProcessWindowStyle.Minimized;
            pstart.CreateNoWindow = true;
            pstart.UseShellExecute = false;
            proc.StartInfo = pstart;
            proc.EnableRaisingEvents = true;
            proc.Exited += new EventHandler(proc_Exited);
            bWaitForExit = false;
            log.LogValue("Job command line", '"' + pstart.FileName + "\" " + pstart.Arguments);

            try
            {
                bool started = proc.Start();
                startTime = DateTime.Now;
                isProcessing = true;
                log.LogEvent("Process started");
                stdoutLog = log.Info(string.Format("[{0:G}] {1}", DateTime.Now, "Standard output stream"));
                stderrLog = log.Info(string.Format("[{0:G}] {1}", DateTime.Now, "Standard error stream"));
                readFromStdErrThread = new Thread(new ThreadStart(readStdErr));
                readFromStdOutThread = new Thread(new ThreadStart(readStdOut));
                readFromStdOutThread.Start();
                readFromStdErrThread.Start();
                new System.Windows.Forms.MethodInvoker(this.RunStatusCycle).BeginInvoke(null, null);
                this.changePriority(MainForm.Instance.Settings.ProcessingPriority);
            }
            catch (Exception e)
            {
                throw new JobRunException(e);
            }
        }

        public void stop()
        {
            if (proc != null && !proc.HasExited)
            {
                try
                {
                    bWaitForExit = true;
                    mre.Set(); // if it's paused, then unpause
                    su.WasAborted = true;
                    proc.Kill();
                    while (bWaitForExit) // wait until the process has terminated without locking the GUI
                    {
                        System.Windows.Forms.Application.DoEvents();
                        System.Threading.Thread.Sleep(100);
                    }
                    proc.WaitForExit();
                    return;
                }
                catch (Exception e)
                {
                    throw new JobRunException(e);
                }
            }
            else
            {
                if (proc == null)
                    throw new JobRunException("Encoder process does not exist");
                else
                    throw new JobRunException("Encoder process has already existed");
            }
        }

        public void pause()
        {
            if (!canPause)
                throw new JobRunException("Can't pause this kind of job.");
            if (!mre.Reset())
                throw new JobRunException("Could not reset mutex. pause failed");
        }

        public void resume()
        {
            if (!canPause)
                throw new JobRunException("Can't resume this kind of job.");
            if (!mre.Set())
                throw new JobRunException("Could not set mutex. pause failed");
        }

        public bool isRunning()
        {
            return (proc != null && !proc.HasExited);
        }

        public void changePriority(ProcessPriority priority)
        {
            if (isRunning())
            {
                try
                {
    				switch (priority)
					{
						case ProcessPriority.IDLE:
	    						proc.PriorityClass = ProcessPriorityClass.Idle;
								break;
						case ProcessPriority.BELOW_NORMAL:
								proc.PriorityClass = ProcessPriorityClass.BelowNormal;
								break;
		    			case ProcessPriority.NORMAL:
			    				proc.PriorityClass = ProcessPriorityClass.Normal;
				    			break;
						case ProcessPriority.ABOVE_NORMAL:
					    		proc.PriorityClass = ProcessPriorityClass.AboveNormal;
								break;
						case ProcessPriority.HIGH:
						    	proc.PriorityClass = ProcessPriorityClass.RealTime;
								break;
					}
                    VistaStuff.SetProcessPriority(proc.Handle, proc.PriorityClass);
                    MainForm.Instance.Settings.ProcessingPriority = priority;
				    return;
                }
                catch (Exception e) // process could not be running anymore
                {
                    throw new JobRunException(e);
                }
            }
            else 
            {
                if (proc == null)
                    throw new JobRunException("Process has not been started yet");
                else
                {
                    Debug.Assert(proc.HasExited);
                    throw new JobRunException("Process has exited");
                }
            }
        }

        public virtual bool canPause
        {
            get { return true; }
        }


        #endregion
        #region reading process output
        protected virtual void readStream(StreamReader sr, ManualResetEvent rEvent, StreamType str)
        {
            string line;
            if (proc != null)
            {
                try
                {
                    while ((line = sr.ReadLine()) != null)
                    {
                        mre.WaitOne();
                        ProcessLine(line, str, ImageType.Information);
                    }
                }
                catch (Exception e)
                {
                    ProcessLine("Exception in readStream. Line cannot be processed. " + e.Message, str, ImageType.Error);
                }
                rEvent.Set();
            }
        }
        protected void readStdOut()
        {
            StreamReader sr = null;
            try
            {
                sr = proc.StandardOutput;
            }
            catch (Exception e)
            {
                log.LogValue("Exception getting IO reader for stdout", e, ImageType.Error);
                stdoutDone.Set();
                return;
            }
            readStream(sr, stdoutDone, StreamType.Stdout);
        }
        protected void readStdErr()
        {
            StreamReader sr = null;
            try
            {
                sr = proc.StandardError;
            }
            catch (Exception e)
            {
                log.LogValue("Exception getting IO reader for stderr", e, ImageType.Error);
                stderrDone.Set();
                return;
            }
            readStream(sr, stderrDone, StreamType.Stderr);
        }

        public virtual void ProcessLine(string line, StreamType stream, ImageType oType)
        {
            if (String.IsNullOrEmpty(line.Trim()))
                return;

            if (stream == StreamType.Stdout)
                stdoutLog.LogEvent(line, oType);
            if (stream == StreamType.Stderr)
                stderrLog.LogEvent(line, oType);

            if (oType == ImageType.Error)
                su.HasError = true;
        }

        #endregion
        #region status updates
        public event JobProcessingStatusUpdateCallback StatusUpdate;
        protected void RunStatusCycle()
        {
            while (isRunning())
            {
                su.TimeElapsed = DateTime.Now - startTime;
                su.CurrentFileSize = FileSize.Of2(job.Output);

                doStatusCycleOverrides();
                su.FillValues();
                if (StatusUpdate != null && proc != null && !proc.HasExited)
                    StatusUpdate(su);
                Thread.Sleep(1000);

            }
        }

        protected virtual void doStatusCycleOverrides()
        { }
        #endregion
    }
}