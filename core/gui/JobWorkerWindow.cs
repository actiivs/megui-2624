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
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Text;
using System.Windows.Forms;

using MeGUI.core.details;
using MeGUI.core.util;

namespace MeGUI.core.gui
{
    /// <summary>
    /// This class represents a processing 'worker', which processes jobs
    /// one by one. In a single instance of MeGUI, there can be multiple
    /// workers, facilitating parallel job processing.
    /// 
    /// JobControl keeps the job queue, and distributes jobs one by one when
    /// requested by a JobWorker. Each JobWorker also maintains a list of jobs
    /// reserved for that particular worker. Ordinarily, this list is empty, 
    /// and the worker just requests jobs from the queue until none are left.
    /// However, it may be useful to specify 'run this job now' or 'run these
    /// jobs now', in which case they are put onto the reserved jobs list,
    /// and they are run before requesting any from the job queue.
    /// 
    /// This can be useful for running small jobs like muxing or d2v indexing
    /// while a video encode is going in the background: since the user is at
    /// the computer *now*, he/she doesn't want to wait until the video encode
    /// is finished, and can instead select 'run this job in a new worker.'
    /// This will also be how indexing and autodeint jobs are run from the
    /// AviSynth script creator. In fact, it will be possible to process jobs
    /// without them ever being on the main job queue: they can be put directly
    /// onto a worker's local list and run from that.
    /// 
    /// Dependencies are managed by JobControl. Each job has a list of jobs
    /// it depends on; a job will only be distributed to a worker if all the
    /// dependant jobs have been completed successfully.
    /// 
    /// A Worker can run in two modes: RequestNewJobs and CloseOnLocalListCompleted.
    /// RequestNewJobs means that after the the local list is completed, it
    /// requests another job from the Job Control, continuing until all jobs
    /// are completed. In this mode, the JobWorker never closes by itself.
    /// CloseOnLocalListCompleted means that it will complete the jobs on
    /// its local list and then close, without requesting any more jobs from
    /// the JobControl.
    /// 
    /// A Worker must always be in one of five states: Idle, Running, Stopping, Stopped, Postponed.
    /// Idle means that no jobs are currently being processed. Running means
    /// that a job is being processed, and further jobs will continue to be
    /// processed until either there are no more jobs or the worker is closed.
    /// Stopping means that a job is currently being processed, but after this
    /// job is completed, no further jobs will be started. Stopped means that no new
    /// will be processed automatically. Postponed means that because of another running
    /// job in another worker this worker is stopped temporarily.
    /// 
    /// ProcessingThreads can run in several modes, enumerated 
    /// </summary>
    public partial class JobWorker : Form
    {
        private IJobProcessor currentProcessor;
        private TaggedJob currentJob; // The job being processed at the moment
        private ProgressWindow pw;
        private MainForm mainForm;
        private decimal progress;
        private LogItem log;

        #region process window opening and closing
        public void HideProcessWindow()
        {
            if (pw != null)
                Util.ThreadSafeRun(pw, delegate { pw.Hide(); });
        }

        public void ShowProcessWindow()
        {
            if (pw != null)
                Util.ThreadSafeRun(pw, delegate { pw.Show(); });
        }
        /// <summary>
        /// callback for the progress window
        /// this method is called if the abort button in the progress window is called
        /// it stops the encoder cold
        /// </summary>
        private void pw_Abort()
        {
            UserRequestedAbort();
        }

        /// <summary>
        /// catches the ChangePriority event from the progresswindow and forward it to the encoder class
        /// </summary>
        /// <param name="priority"></param>
        private void pw_PriorityChanged(ProcessPriority priority)
        {
            try
            {
                currentProcessor.changePriority(priority);
            }
            catch (JobRunException e)
            {
                log.LogValue("Error attempting to change priority", e, ImageType.Error);
            }
        }
        #endregion

        #region public interface
        public decimal Progress
        {
            get { return progress; }
        }

        private PauseState pauseStatus;
        public PauseState PauseStatus
        {
            get { return pauseStatus; }
        }

        public string StatusString
        {
            get
            {
                if (status == JobWorkerStatus.Idle)
                    return "idle";
                if (status == JobWorkerStatus.Stopped)
                    return "stopped";
                if (status == JobWorkerStatus.Postponed)
                    return "postponed (another worker is processing an audio job)";
                string _status = "running"; 
                if (currentJob != null)
                    _status += string.Format(" {0} ({1:P2})", currentJob.Name, progress/100M);
                if (mode == JobWorkerMode.CloseOnLocalListCompleted)
                    _status += " (delete worker after current job)";
                else if (status == JobWorkerStatus.Stopping)
                    _status += " (stop worker after current job)";
                if (pauseStatus == PauseState.Paused)
                    _status += " (paused)";
                return _status;
            }
        }                        

        private JobWorkerMode mode;
        public JobWorkerMode Mode
        {
            get { return mode; }
            set { mode = value; }
        }

        private JobWorkerStatus status;
        public JobWorkerStatus Status
        {
            get { return status; }
        }

        private bool bIsTemporaryWorker;
        public bool IsTemporaryWorker
        {
            get { return bIsTemporaryWorker; }
            set { bIsTemporaryWorker = value; }
        }

        public void SetStopping()
        {
            if (status == JobWorkerStatus.Running)
                status = JobWorkerStatus.Stopping;
        }

        public void SetRunning()
        {
            if (status == JobWorkerStatus.Stopping)
                status = JobWorkerStatus.Running;
        }

        private string name;
        public new string Name
        {
            get { return name; }
            set
            {
                name = value;
                Text = value;
            }
        }

        public int LocalJobCount
        {
            get { return localJobs.Count; }
        }

        public bool IsEncoding
        {
            get { return status == JobWorkerStatus.Running || status == JobWorkerStatus.Stopping; }
        }

        public bool IsEncodingAudio
        {
            get { return IsEncoding && currentJob != null && currentJob.Job.EncodingMode.Equals("audio"); }
        }
        #endregion

        public event EventHandler WorkerFinishedJobs;

        private Dictionary<string, TaggedJob> localJobs = new Dictionary<string, TaggedJob>();

        public JobWorker(MainForm mf)
        {
            mainForm = mf;

            InitializeComponent();
            jobQueue1.SetStartStopButtonsTogether();
            jobQueue1.RequestJobDeleted = new RequestJobDeleted(GUIDeleteJob);
            jobQueue1.AddMenuItem("Return to main job queue", null, delegate(List<TaggedJob> jobs)
            {
                foreach (TaggedJob j in jobs)
                    mainForm.Jobs.ReleaseJob(j);
            });
            
            pw = new ProgressWindow();
            pw.Abort += new AbortCallback(pw_Abort);
            pw.PriorityChanged += new PriorityChangedCallback(pw_PriorityChanged);
            pw.CreateControl();
            mainForm.RegisterForm(pw);
        }

        #region job run util


        /// <summary>
        /// Postprocesses the given job according to the JobPostProcessors in the mainForm's PackageSystem
        /// </summary>
        /// <param name="job"></param>
        private void postprocessJob(Job job)
        {
            LogItem i = log.LogEvent("Postprocessing");
            foreach (JobPostProcessor pp in mainForm.PackageSystem.JobPostProcessors.Values)
            {
                LogItem plog = pp.PostProcessor(mainForm, job);
                if (plog != null)
                {
                    i.Add(plog);
                }
            }
        }

        /// <summary>
        /// Preprocesses the given job according to the JobPreProcessors in the mainForm's PackageSystem
        /// </summary>
        /// <param name="job"></param>
        private void preprocessJob(Job job)
        {
            LogItem i = log.LogEvent("Preprocessing");
            foreach (JobPreProcessor pp in mainForm.PackageSystem.JobPreProcessors.Values)
            {
                LogItem plog = pp.PreProcessor(mainForm, job);
                if (plog != null)
                {
                    i.Add(plog);
                }
            }
        }

        private IJobProcessor getProcessor(Job job)
        {
            foreach (JobProcessorFactory f in mainForm.PackageSystem.JobProcessors.Values)
            {
                IJobProcessor p = f.Factory(mainForm, job);
                if (p != null)
                {
                    return p;
                }
            }
            log.Error("No processor found");
            return null;
        }
        #endregion

        #region shut down
        internal void ShutDown()
        {
            if (IsEncoding)
                Abort();
            returnJobsToMainQueue();
            mainForm.Jobs.ShutDown(this);
        }

        internal void UserRequestShutDown()
        {
            DialogResult r = MessageBox.Show("Do you really want to delete this job worker?", "Really delete?", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (r == DialogResult.Yes)
                ShutDown();
        }
        #endregion

        private void returnJobsToMainQueue()
        {
            List<TaggedJob> list = new List<TaggedJob>(localJobs.Values);
            foreach (TaggedJob j in list)
                mainForm.Jobs.ReleaseJob(j);
        }

        internal void GUIDeleteJob(TaggedJob j)
        {
            mainForm.Jobs.DeleteJob(j);
        }

        #region gui updates
        private void refreshAll()
        {
            jobQueue1.refreshQueue();
            switch (Status)
            {
                case JobWorkerStatus.Idle:
                    jobQueue1.StartStopMode = StartStopMode.Start;
                    jobQueue1.PauseResumeMode = PauseResumeMode.Disabled;
                    break;

                case JobWorkerStatus.Postponed:
                    jobQueue1.StartStopMode = StartStopMode.Start;
                    jobQueue1.PauseResumeMode = PauseResumeMode.Disabled;
                    break;

                case JobWorkerStatus.Stopped:
                    jobQueue1.StartStopMode = StartStopMode.Start;
                    jobQueue1.PauseResumeMode = PauseResumeMode.Disabled;
                    break;

                case JobWorkerStatus.Running:
                    jobQueue1.StartStopMode = StartStopMode.Stop;
                    jobQueue1.PauseResumeMode = (pauseStatus == PauseState.Paused) ? PauseResumeMode.Resume : PauseResumeMode.Pause;
                    break;

                case JobWorkerStatus.Stopping:
                    jobQueue1.StartStopMode = StartStopMode.Start;
                    jobQueue1.PauseResumeMode = (pauseStatus == PauseState.Paused) ? PauseResumeMode.Resume : PauseResumeMode.Pause;
                    break;
            }
            updateProgress();
            mainForm.Jobs.refresh();
        }

        private void updateProgress()
        {
            if (this.InvokeRequired) 
                Invoke(new MethodInvoker(delegate { jobProgress.Value = (int)Progress; }));
            else 
                jobProgress.Value = (int)Progress;
            if (alive)
                mainForm.Jobs.UpdateProgress(this.Name);
        }
        #endregion

        #region job starting / stopping
        #region abort
        /// <summary>
        /// aborts the currently active job
        /// </summary>
        public void Abort()
        {
            Debug.Assert(IsEncoding);
            if (currentProcessor == null || currentJob.Status == JobStatus.ABORTING) 
                return;
            try
            {
                currentJob.Status = JobStatus.ABORTING;
                refreshAll();
                currentProcessor.stop();
            }
            catch (JobRunException er)
            {
                mainForm.Log.LogValue("Error attempting to stop processing", er, ImageType.Error);
            }
            markJobAborted();
            status = JobWorkerStatus.Stopped;
            refreshAll();
        }

        #endregion

        #region starting jobs
        public void StartEncoding(bool showMessageBoxes)
        {
            status = JobWorkerStatus.Idle;
            JobStartInfo retval = startNextJobInQueue();            
            if (showMessageBoxes)
            {
                if (retval == JobStartInfo.COULDNT_START)
                    MessageBox.Show("Couldn't start processing. Please consult the log for more details", "Processing failed", MessageBoxButtons.OK);
                else if (retval == JobStartInfo.NO_JOBS_WAITING)
                    MessageBox.Show("No jobs are waiting or can be processed at the moment.\r\nOnly one audio job can run at a time and there may be\r\nsome dependencies which have to be fulfilled first.", "No jobs waiting", MessageBoxButtons.OK);
            }
        }

        /// <summary>
        /// Copies completion info into the job: end time, FPS, status.
        /// </summary>
        /// <param name="job">Job to fill with info</param>
        /// <param name="su">StatusUpdate with info</param>
        private void copyInfoIntoJob(TaggedJob job, StatusUpdate su)
        {
            Debug.Assert(su.IsComplete);

            job.End = DateTime.Now;
            job.EncodingSpeed = su.ProcessingSpeed;

            if (su.WasAborted)
                job.Status = JobStatus.ABORTED;
            else if (su.HasError)
                job.Status = JobStatus.ERROR;
        }

        /// <summary>
        /// updates the actual GUI with the status information received as parameter
        /// If the StatusUpdate indicates that the job has ended, the Progress window is closed
        /// and the logging messages from the StatusUpdate object are added to the log tab
        /// if the job mentioned in the statusupdate has a next job name defined, the job is looked
        /// up and processing of that job starts - this applies even in queue encoding mode
        /// the linked jobs will always be encoded first, regardless of their position in the queue
        /// If we're in queue encoding mode, the next nob in the queue is also started
        /// </summary>
        /// <param name="su">StatusUpdate object containing the current encoder stats</param>
        private void UpdateGUIStatus(StatusUpdate su)
        {
            if (su.IsComplete)
            {
                MeGUI.core.util.WindowUtil.AllowSystemPowerdown();
                // so we don't lock up the GUI, we start a new thread
                Thread t = new Thread(new ThreadStart(delegate
                {
                    TaggedJob job = mainForm.Jobs.ByName(su.JobName);
                    JobStartInfo JobInfo = JobStartInfo.JOB_STARTED;

                    copyInfoIntoJob(job, su);
                    progress = 0;
                    HideProcessWindow();

                    // Postprocessing
                    bool jobFailed = (job.Status != JobStatus.PROCESSING);
                    if (!jobFailed)
                    {
                        postprocessJob(job.Job);
                        job.Status = JobStatus.DONE;
                    }

                    currentProcessor = null;
                    currentJob = null;

                    // Logging
                    log.LogEvent("Job completed");
                    log.Collapse();

                    if (!jobFailed && mainForm.Settings.DeleteCompletedJobs)
                        mainForm.Jobs.RemoveCompletedJob(job);
                    else
                        mainForm.Jobs.saveJob(job, mainForm.MeGUIPath);     //AAA: save state more often

                    if (mode == JobWorkerMode.CloseOnLocalListCompleted && shutdownWorkerIfJobsCompleted())
                    {
                        MeGUI.core.util.WindowUtil.AllowSystemPowerdown();
                        JobInfo = JobStartInfo.COULDNT_START;
                    }
                    else if (job.Status == JobStatus.ABORTED)
                    {
                        MeGUI.core.util.WindowUtil.AllowSystemPowerdown();
                        log.LogEvent("Current job was aborted");
                        status = JobWorkerStatus.Stopped;
                        JobInfo = JobStartInfo.COULDNT_START;
                    }
                    else if (status == JobWorkerStatus.Stopping)
                    {
                        MeGUI.core.util.WindowUtil.AllowSystemPowerdown();
                        log.LogEvent("Queue mode stopped");
                        status = JobWorkerStatus.Stopped;
                        JobInfo = JobStartInfo.COULDNT_START;
                    }
                    else
                    {
                        JobInfo = startNextJobInQueue();
                        switch (JobInfo)
                        {
                            case JobStartInfo.JOB_STARTED:
                                MeGUI.core.util.WindowUtil.PreventSystemPowerdown();
                                break;

                            case JobStartInfo.COULDNT_START:
                                MeGUI.core.util.WindowUtil.AllowSystemPowerdown();
                                if (status != JobWorkerStatus.Postponed) 
                                    status = JobWorkerStatus.Idle;
                                break;

                            case JobStartInfo.NO_JOBS_WAITING:
                                MeGUI.core.util.WindowUtil.AllowSystemPowerdown();
                                if (status != JobWorkerStatus.Postponed)
                                    status = JobWorkerStatus.Idle;
                                new Thread(delegate ()
                                {
                                    WorkerFinishedJobs(this, EventArgs.Empty);
                                }).Start();
                                break;
                        }
                    }

                    if (JobInfo == JobStartInfo.JOB_STARTED)
                        Util.ThreadSafeRun(mainForm.Jobs, delegate { mainForm.Jobs.StartIdleWorkers(); });

                    refreshAll();
                }));
                t.IsBackground = true;
                t.Start();
            }
            else // job is not complete yet
            {
                try
                {
                    if (pw.IsHandleCreated && pw.Visible) // the window is there, send the update to the window
                    {
                        TaggedJob job = mainForm.Jobs.ByName(su.JobName);
                        su.JobStatus = job.Status;
                        pw.BeginInvoke(new UpdateStatusCallback(pw.UpdateStatus), su);
                    }
                }
                catch (Exception e)
                {
                    mainForm.Log.LogValue("Error trying to update status while a job is running", e, ImageType.Warning);
                }

                if (su.PercentageDoneExact > 100)
                    progress = 100;
                else
                    progress = su.PercentageDoneExact ?? 0;
                updateProgress();
            }
        }

        /// <summary>
        /// shuts down this worker if no jobs are waiting
        /// </summary>
        /// <returns>true if worker was shut down, false otherwise</returns>
        private bool shutdownWorkerIfJobsCompleted()
        {
            foreach (TaggedJob j in localJobs.Values)
                if (j.Status == JobStatus.WAITING)
                    return false; // do not shut down as jobs are waiting
            
            ShutDown();
            return true;
        }

        private enum ExceptionType { UserSkip, Error };
        private class JobStartException : MeGUIException
        {
            public ExceptionType type;
            public JobStartException(string reason, ExceptionType type) : base(reason) { this.type = type; }
        }
        /// <summary>
        /// starts the job provided as parameters
        /// </summary>
        /// <param name="job">the Job object containing all the parameters</param>
        /// <returns>success / failure indicator</returns>
        private bool startEncoding(TaggedJob job)
        {
            try
            {
                log = mainForm.Log.Info(string.Format("Log for {0} ({1}, {2} -> {3})", job.Name, job.Job.EncodingMode, job.InputFileName, job.OutputFileName));
                log.LogEvent("Started handling job");
                log.Expand();

                status = JobWorkerStatus.Running;
                MeGUI.core.util.WindowUtil.PreventSystemPowerdown();

                //Check to see if output file already exists before encoding.
                if (File.Exists(job.Job.Output) &&
                    (!Path.GetExtension(job.Job.Output).Equals(".lwi") && !Path.GetExtension(job.Job.Output).Equals(".ffindex") &&
                    !Path.GetExtension(job.Job.Output).Equals(".d2v") &&
                    !Path.GetExtension(job.Job.Output).Equals(".dgi")) && !mainForm.DialogManager.overwriteJobOutput(job.Job.Output))
                    throw new JobStartException("File exists and the user doesn't want to overwrite", ExceptionType.UserSkip);

                // Get IJobProcessor
                currentProcessor = getProcessor(job.Job);
                if (currentProcessor == null)
                    throw new JobStartException("No processor could be found", ExceptionType.Error);


                // Preprocess
                preprocessJob(job.Job);

                // Setup
                try
                {
                    currentProcessor.setup(job.Job, new StatusUpdate(job.Name), log);
                }
                catch (JobRunException e)
                {
                    throw new JobStartException("Calling setup of processor failed with error '" + e.Message + "'", ExceptionType.Error);
                }

                if (currentProcessor == null)
                {
                    throw new JobStartException("starting job failed", ExceptionType.Error);
                }

                // Do JobControl setup
                currentProcessor.StatusUpdate += new JobProcessingStatusUpdateCallback(UpdateGUIStatus);

                // Progress window
                pw.setPriority(mainForm.Settings.ProcessingPriority);
                if (mainForm.Settings.OpenProgressWindow && mainForm.Visible)
                    this.ShowProcessWindow();

                job.Status = JobStatus.PROCESSING;
                job.Start = DateTime.Now;
                status = JobWorkerStatus.Running;
                pauseStatus = PauseState.Encoding;
                currentJob = job;

                // Start
                try
                {
                    currentProcessor.start();
                }
                catch (JobRunException e)
                {
                    throw new JobStartException("starting job failed with error '" + e.Message + "'", ExceptionType.Error);
                }

                refreshAll();
                return true;
            }
            catch (JobStartException e)
            {
                this.HideProcessWindow();
                log.LogValue("Error starting job", e);
                if (e.type == ExceptionType.Error)
                    job.Status = JobStatus.ERROR;
                else // ExceptionType.UserSkip
                    job.Status = JobStatus.SKIP;
                currentProcessor = null;
                currentJob = null;
                status = JobWorkerStatus.Idle;
                pauseStatus = PauseState.NotEncoding;
                refreshAll();
                return false;
            }

        }

        private TaggedJob getNextJob()
        {
            foreach (TaggedJob j in jobQueue1.JobList)
                if (j.Status == JobStatus.WAITING && mainForm.Jobs.areDependenciesMet(j))
                    return j;
            if (mode == JobWorkerMode.RequestNewJobs)
                return mainForm.Jobs.getJobToProcess();
            else
                return null;
        }

        private JobStartInfo startNextJobInQueue()
        {
            mainForm.Jobs.ResourceLock.WaitOne(10000, false);

            TaggedJob job = getNextJob();

            if (job == null)
            {
                status = JobWorkerStatus.Idle;
                mainForm.Jobs.ResourceLock.Release();
                return JobStartInfo.NO_JOBS_WAITING;
            }

            while (job != null)
            {
                if (job.Job.EncodingMode.Equals("audio") && mainForm.Jobs.IsAnyWorkerEncodingAudio)
                {
                    // another audio encoding is already in process. postpone the worker
                    status = JobWorkerStatus.Postponed;
                    mainForm.Jobs.ResourceLock.Release();
                    return JobStartInfo.NO_JOBS_WAITING;
                }

                if (startEncoding(job)) // successful
                {
                    mainForm.Jobs.ResourceLock.Release();
                    return JobStartInfo.JOB_STARTED;
                }
                job = getNextJob();
            }
            status = JobWorkerStatus.Idle;
            mainForm.Jobs.ResourceLock.Release();
            return JobStartInfo.COULDNT_START;
        }
        #endregion
        #endregion

        /// <summary>
        /// marks job currently marked as processing as aborted
        /// </summary>
        private void markJobAborted()
        {
            if (currentJob == null)
                return;
            TaggedJob job = currentJob;
            job.Status = JobStatus.ABORTED;
            job.End = DateTime.Now;
            long iCounter = 0;
            
            LogItem i = new LogItem("Deleting aborted output");

            i.LogValue("Delete aborted output set", mainForm.Settings.DeleteAbortedOutput);

            if (mainForm.Settings.DeleteAbortedOutput && File.Exists(job.Job.Output))
            {
                i.LogValue("File to delete", job.Job.Output);
                while (File.Exists(job.Job.Output))
                {
                    try
                    {
                        File.Delete(job.Job.Output);
                    }
                    catch (Exception e)
                    {
                        if (++iCounter >= 3)
                        {
                            i.LogValue("Problem deleting file", e.Message, ImageType.Warning);
                            break;
                        }
                        else
                            System.Threading.Thread.Sleep(2000);
                    }
                }
                if (!File.Exists(job.Job.Output))
                    i.LogEvent("File deleted");
            }
            log.Add(i);
        }

        #region pause / resume
        public void Pause()
        {
            Debug.Assert(pauseStatus == PauseState.Encoding);
            try
            {
                currentProcessor.pause();
                pauseStatus = PauseState.Paused;
                refreshAll();
            }
            catch (JobRunException ex)
            {
                mainForm.Log.LogValue("Error trying to pause encoding", ex, ImageType.Warning);
            }
        }

        public void Resume()
        {
            Debug.Assert(pauseStatus == PauseState.Paused);
            try
            {
                currentProcessor.resume();
                pauseStatus = PauseState.Encoding;
            }
            catch (JobRunException ex)
            {
                mainForm.Log.LogValue("Error trying to resume encoding", ex, ImageType.Warning);
            }
        }
        #endregion
        protected override void OnClosing(CancelEventArgs e)
        {
            if (!alive)
            {
                base.OnClosing(e);
                return;
            }
            e.Cancel = true;
            Hide();
        }
        internal void RemoveJobFromQueue(TaggedJob job)
        {
            localJobs.Remove(job.Name);
            jobQueue1.removeJobFromQueue(job);
        }

        internal void UserRequestedAbort()
        {
            if (currentJob.Status == JobStatus.ABORTED || currentJob.Status == JobStatus.ABORTING)
            {
                MessageBox.Show("Job already aborting. Please wait.", "Abort in progress", MessageBoxButtons.OK);
                return;
            }
            DialogResult r = MessageBox.Show("Do you really want to abort?", "Really abort?", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (r == DialogResult.Yes)
                Abort();
        }


        internal IEnumerable<TaggedJob> Jobs
        {
            get { return jobQueue1.JobList; }
            set { jobQueue1.JobList = value; }
        }

        public void UserRequestedRename()
        {
            try
            {
                string name = InputBox.Show("Please enter the new name for this worker", "Please enter the new name", "New Worker Name");
                if (name != null)
                    setName(name);
            }
            catch (NameTakenException ex)
            {
                MessageBox.Show("The name you entered, '" + ex.Name + "' is already in use. The worker was not renamed.", "Worker not renamed",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

        }

        public void ShutDownWhenFinished()
        {
            shutDownWhenFinishedLocalQueueToolStripMenuItem.Checked = !shutDownWhenFinishedLocalQueueToolStripMenuItem.Checked;
            if (shutDownWhenFinishedLocalQueueToolStripMenuItem.Checked)
            {
                if (localJobs.Count == 0 && status == JobWorkerStatus.Idle)
                    UserRequestShutDown();
                else
                    mode = JobWorkerMode.CloseOnLocalListCompleted;
            }
            else
                mode = JobWorkerMode.RequestNewJobs;
        }


        private void changeNameToolStripMenuItem_Click(object sender, EventArgs e) 
        { 
            UserRequestedRename(); 
        }

        private void setName(string p)
        {
            mainForm.Jobs.RenameWorker(this.name, p); // throws NameTakenException if it fails
        }

        private void shutDownWhenFinishedLocalQueueToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ShutDownWhenFinished();
        }

        private void shutDownWorkerNowToolStripMenuItem_Click(object sender, EventArgs e)
        {
            UserRequestShutDown();
        }

        private void showProgressWindowToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (showProgressWindowToolStripMenuItem.Checked)
                HideProcessWindow();
            else
                ShowProcessWindow();
        }

        internal void RefreshInfo()
        {
            throw new Exception("The method or operation is not implemented.");
        }

        private void jobQueue1_AbortClicked(object sender, EventArgs e)
        {
            UserRequestedAbort();
        }

        private void jobQueue1_StartClicked(object sender, EventArgs e)
        {
            if (Status == JobWorkerStatus.Stopping)
                SetRunning();
            else
            {
                Debug.Assert(Status == JobWorkerStatus.Idle || Status == JobWorkerStatus.Postponed);
                StartEncoding(true);
            }
        }

        private void jobQueue1_StopClicked(object sender, EventArgs e)
        {
            if (Status == JobWorkerStatus.Running)
                SetStopping();
        }


        internal void AddJob(TaggedJob j)
        {
            j.OwningWorker = this.Name;
            jobQueue1.queueJob(j);
            localJobs[j.Name] = j;
        }

        public bool IsProgressWindowAvailable
        { 
            get { return IsEncoding; } 
        }

        public bool IsProgressWindowVisible
        {
            get { return (pw != null && pw.Visible); }
        }

        private void progressWindowToolStripMenuItem_DropDownOpened(object sender, EventArgs e)
        {
            showProgressWindowToolStripMenuItem.Enabled = IsProgressWindowAvailable;
            showProgressWindowToolStripMenuItem.Checked = IsProgressWindowVisible;
        }

        bool alive = true;
        internal void CloseForReal()
        {
            alive = false;
        }

        private void JobWorker_FormClosed(object sender, FormClosedEventArgs e)
        {
        }
    }

    public class NameTakenException : MeGUIException
    {
        private string name;
        public string Name
        {
            get { return name; }
        }
        public NameTakenException(string name)
            : base("Worker name '" + name + "' is already in use.")
        {
            this.name = name;
        }
    }

    public enum PauseState { NotEncoding, Encoding, Paused }
    public enum JobWorkerMode { RequestNewJobs, CloseOnLocalListCompleted }
    public enum JobWorkerStatus { Idle, Running, Stopping, Stopped, Postponed }
    public enum JobsOnQueue { Delete, ReturnToMainQueue }
    public enum IdleReason { FinishedQueue, Stopped, Aborted }
    
}