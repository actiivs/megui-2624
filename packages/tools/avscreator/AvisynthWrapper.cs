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
using System.Collections;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;

namespace MeGUI
{
    public enum AviSynthColorspace:int
    {
        Unknown = 0,
        YV12    = -1610612728,
        RGB24   = +1342177281,
        RGB32   = +1342177282,
        YUY2    = -1610612740,
        I420    = -1610612720,
        IYUV    = I420
    }

    [SerializableAttribute]
	public class AviSynthException:ApplicationException
	{
		public AviSynthException(SerializationInfo info, StreamingContext context) : base(info, context)
		{
		}

		public AviSynthException(string message) : base(message)
		{
		}

		public AviSynthException(): base()
		{
		}

		public AviSynthException(string message, Exception innerException) : base(message, innerException)
		{
		}
	}

	public enum AudioSampleType:int
	{
        Unknown=0,
		INT8  = 1,
		INT16 = 2, 
		INT24 = 4,    // Int24 is a very stupid thing to code, but it's supported by some hardware.
		INT32 = 8,
		FLOAT = 16
	};

	public sealed class AviSynthScriptEnvironment:IDisposable
	{
		public AviSynthScriptEnvironment()
		{
        }

        public AviSynthClip OpenScriptFile(string filePath, AviSynthColorspace forceColorspace)
        {
            return new AviSynthClip("Import", filePath, forceColorspace);
        }

        public AviSynthClip ParseScript(string script, AviSynthColorspace forceColorspace)
        {
            return new AviSynthClip("Eval", script, forceColorspace);
        }


		public AviSynthClip OpenScriptFile(string filePath)
		{
            return OpenScriptFile(filePath, AviSynthColorspace.RGB24);
		}

		public AviSynthClip ParseScript(string script)
		{
            return ParseScript(script, AviSynthColorspace.RGB24);
        }		

		public void Dispose()
		{

		}
	}

	/// <summary>
	/// Summary description for AviSynthClip.
	/// </summary>
	public class AviSynthClip: IDisposable
	{
		#region PInvoke related stuff
		[StructLayout(LayoutKind.Sequential)]
		struct AVSDLLVideoInfo
		{
			public int width;
			public int height;
			public int raten;
			public int rated;
			public int aspectn;
			public int aspectd;
			public int interlaced_frame;
			public int top_field_first;
			public int num_frames;
			public AviSynthColorspace pixel_type;

			// Audio
			public int audio_samples_per_second;
			public AudioSampleType sample_type;
			public int nchannels;
			public int num_audio_frames;
			public long num_audio_samples;
		}

        [DllImport("AvisynthWrapper", ExactSpelling = true, SetLastError = false, CharSet = CharSet.Ansi)]
        private static extern int dimzon_avs_init(ref IntPtr avs, string func, string arg, ref AVSDLLVideoInfo vi, ref AviSynthColorspace originalColorspace, ref AudioSampleType originalSampleType, string cs);
        [DllImport("AvisynthWrapper", ExactSpelling = true, SetLastError = false, CharSet = CharSet.Ansi)]
        private static extern int dimzon_avs_init_2(ref IntPtr avs, string func, string arg, ref AVSDLLVideoInfo vi, ref AviSynthColorspace originalColorspace, ref AudioSampleType originalSampleType, string cs);
        [DllImport("AvisynthWrapper", ExactSpelling = true, SetLastError = false, CharSet = CharSet.Ansi)]
        private static extern int dimzon_avs_destroy(ref IntPtr avs);
        [DllImport("AvisynthWrapper", ExactSpelling = true, SetLastError = false, CharSet = CharSet.Ansi)]
        private static extern int dimzon_avs_getlasterror(IntPtr avs, [MarshalAs(UnmanagedType.LPStr)] StringBuilder sb, int len);
        [DllImport("AvisynthWrapper", ExactSpelling = true, SetLastError = false, CharSet = CharSet.Ansi)]
        private static extern int dimzon_avs_getaframe(IntPtr avs, IntPtr buf, long sampleNo, long sampleCount);
        [DllImport("AvisynthWrapper", ExactSpelling = true, SetLastError = false, CharSet = CharSet.Ansi)]
        private static extern int dimzon_avs_getvframe(IntPtr avs, IntPtr buf, int stride, int frm);
        [DllImport("AvisynthWrapper", ExactSpelling = true, SetLastError = false, CharSet = CharSet.Ansi)]
        private static extern int dimzon_avs_getintvariable(IntPtr avs, string name, ref int val);

		#endregion

        private IntPtr _avs;
        private AVSDLLVideoInfo _vi;
        private AviSynthColorspace _colorSpace;
        private AudioSampleType _sampleType;
        private static object _locker = new object();

#if dimzon

        #region syncronization staff

        private class EnvRef
        {
            public IntPtr env;
            public long refCount;
            public object lockObj;
            public int threadID;

            public EnvRef(IntPtr e, int threadID)
            {
                this.env = e;
                refCount = 1;
                lockObj = new object();
                this.threadID = threadID;
            }
        }
        private static Hashtable _threadHash = new Hashtable();

        private EnvRef createNewEnvRef(int threadId)
        {
            //TODO:
            return new EnvRef(new IntPtr(0), threadId);
        }

        private void destroyEnvRef(EnvRef r)
        {
            //TODO:
        }

        private EnvRef addRef()
        {
            int threadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
            lock (_threadHash.SyncRoot)
            {
                EnvRef r;
                if(_threadHash.ContainsKey(threadId))
                {
                    r = (EnvRef)_threadHash[threadId];
                    lock(r.lockObj)
                    {
                        if (0 == r.refCount)
                        {
                            r = createNewEnvRef(threadId);
                            _threadHash.Remove(threadId);
                            _threadHash.Add(threadId, r);
                        }
                        else
                        {
                            ++r.refCount;
                        }
                    }
                }
                else
                {
                    r = createNewEnvRef(threadId);
                    _threadHash.Add(threadId, r);
                }
                return r;
            }
        }

        private void Release()
        {
            if (_avsEnv == null)
                return;
            lock (_avsEnv.lockObj)
            {
                --_avsEnv.refCount;
                if (0 == _avsEnv.refCount)
                {
                    destroyEnvRef(_avsEnv);
                }
            }
            _avsEnv = null;
        }

        private EnvRef _avsEnv;


        #endregion
#endif

        private string getLastError()
        {
            const int errlen = 1024;
            StringBuilder sb = new StringBuilder(errlen);
            sb.Length = dimzon_avs_getlasterror(_avs, sb, errlen);
            return sb.ToString();
        }

		#region Clip Properties

        public bool HasVideo
        {
            get
            {
                return VideoWidth > 0 && VideoHeight > 0;
            }
        }

		public int VideoWidth
		{
			get
			{
				return _vi.width;
			}
		}

		public int VideoHeight
		{
			get
			{
				return _vi.height;
			}
		}

		public int raten
		{
			get
			{
				return _vi.raten;
			}
		}

		public int rated
		{
			get
			{
				return _vi.rated;
			}
		}

		public int aspectn
		{
			get
			{
				return _vi.aspectn;
			}
		}

		public int aspectd
		{
			get
			{
				return _vi.aspectd;
			}
		}

		public int interlaced_frame
		{
			get
			{
				return _vi.interlaced_frame;
			}
		}

		public int top_field_first
		{
			get
			{
				return _vi.top_field_first;
			}
		}

		public int num_frames
		{
			get
			{
				return _vi.num_frames;
			}
		}

		// Audio
        public bool HasAudio
        {
            get
            {
                return _vi.num_audio_samples != 0;
            }
        }

		public int AudioSampleRate
		{
			get
			{
				return _vi.audio_samples_per_second;
			}
		}

		public long SamplesCount
		{
			get
			{
				return _vi.num_audio_samples;
			}
		}

		public AudioSampleType SampleType
		{
			get
			{
				return _vi.sample_type;
			}
		}

		public short ChannelsCount
		{
			get
			{
				return (short)_vi.nchannels;
			}
		}

        public AviSynthColorspace PixelType
        {
            get
            {
                return _vi.pixel_type;
            }
        }

        public AviSynthColorspace OriginalColorspace
        {
            get
            {
                return _colorSpace;
            }
        }

        public AudioSampleType OriginalSampleType
        {
            get
            {
                return _sampleType;
            }
        }

		#endregion

        public int GetIntVariable(string variableName, int defaultValue)
        {
            int v = 0;
            int res = 0;
            res = dimzon_avs_getintvariable(this._avs, variableName, ref v);
            if (res < 0)
                throw new AviSynthException(getLastError());
            return (0 == res) ? v : defaultValue;
        }

		public void ReadAudio(IntPtr addr, long offset, int count)
		{
            if (0 != dimzon_avs_getaframe(_avs, addr, offset, count))
				throw new AviSynthException(getLastError());
		}

		public void ReadAudio(byte buffer, long offset, int count)
		{
			GCHandle h = GCHandle.Alloc(buffer,GCHandleType.Pinned);
			try
			{
				ReadAudio(h.AddrOfPinnedObject(), offset, count);
			}
			finally
			{
				h.Free();
			}
		}

        public void ReadFrame(IntPtr addr, int stride, int frame)
        {
            if (0 != dimzon_avs_getvframe(_avs, addr, stride, frame))
                throw new AviSynthException(getLastError());
        }

        public AviSynthClip(string func, string arg , AviSynthColorspace forceColorspace)
		{
			_vi = new AVSDLLVideoInfo();
            _avs =  new IntPtr(0);
            _colorSpace = AviSynthColorspace.Unknown;
            _sampleType = AudioSampleType.Unknown;
            bool bOpenSuccess = false;

            lock (_locker)
            {
                if (MainForm.Instance.Settings.OpenAVSInThreadDuringSession)
                {
                    Thread t = new Thread(new ThreadStart(delegate
                    {
                        System.Windows.Forms.Application.UseWaitCursor = true;
                        if (0 == dimzon_avs_init_2(ref _avs, func, arg, ref _vi, ref _colorSpace, ref _sampleType, forceColorspace.ToString()))
                            bOpenSuccess = true;
                        System.Windows.Forms.Application.UseWaitCursor = false;
                    }));
                    t.Start();

                    while (t.ThreadState == ThreadState.Running)
                    {
                        System.Windows.Forms.Application.DoEvents();
                        Thread.Sleep(100);
                    }
                }
                else
                {
                    if (0 == dimzon_avs_init_2(ref _avs, func, arg, ref _vi, ref _colorSpace, ref _sampleType, forceColorspace.ToString()))
                        bOpenSuccess = true;
                }
            }

            if (bOpenSuccess == false)
            {
                string err = getLastError();
                Dispose(false);
                throw new AviSynthException(err);
            }
		}

		~AviSynthClip()
		{
			Dispose(false);
		}

		public void Dispose()
		{
			Dispose(true);
		}

        protected virtual void Dispose(bool disposing)
        {
            dimzon_avs_destroy(ref _avs);
            _avs = new IntPtr(0);
            if (disposing)
                GC.SuppressFinalize(this);
        }

		public short BitsPerSample
		{
			get
			{
				return (short)(BytesPerSample*8);
			}
		}

		public short BytesPerSample
		{
			get
			{
				switch (SampleType) 
				{
					case AudioSampleType.INT8:
						return 1;
					case AudioSampleType.INT16:
						return 2;
					case AudioSampleType.INT24:
						return 3;
					case AudioSampleType.INT32:
						return 4;
					case AudioSampleType.FLOAT:
						return 4;
					default:
						throw new ArgumentException(SampleType.ToString());
				}
			}
		}

        public int AvgBytesPerSec
        {
            get
            {
                return AudioSampleRate * ChannelsCount * BytesPerSample;
            }
        }

        public long AudioSizeInBytes
        {
            get
            {
                return SamplesCount * ChannelsCount * BytesPerSample;
            }
        }

	}
}
