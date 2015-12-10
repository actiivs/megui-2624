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
using System.Text;

using MeGUI.core.plugins.interfaces;

namespace MeGUI
{
	/// <summary>
	/// Summary description for AviSynthSettings.
	/// </summary>
    public sealed class AviSynthSettings : GenericSettings
	{
        public static string ID = "AviSynth";
        public string SettingsID { get { return ID; } }

        private string template;
        private ResizeFilterType resizeMethod;
        private DenoiseFilterType denoiseMethod;
        private mod16Method mod16Method;
        private modValue modValueUsed;
        private bool deinterlace, denoise, ivtc, mpeg2deblock, colourCorrect, dss2, resize, upsize;
        private decimal acceptableAspectError;

        public void FixFileNames(System.Collections.Generic.Dictionary<string, string> _) { }

        public AviSynthSettings()
        {
            this.Template = "<input>\r\n<deinterlace>\r\n<crop>\r\n<resize>\r\n<denoise>\r\n"; // Default -- will act as it did before avs profiles
            this.ResizeMethod = ResizeFilterType.Lanczos; // Lanczos
            this.DenoiseMethod = 0; // UnDot
            this.Deinterlace = false;
            this.Denoise = false;
            this.Resize = true;
            this.IVTC = false;
            this.MPEG2Deblock = false;
            this.ColourCorrect = true;
            this.Mod16Method = mod16Method.none;
            this.DSS2 = false;
            this.Upsize = false;
            this.ModValue = modValue.mod8;
            this.acceptableAspectError = 1;
        }

        public AviSynthSettings(string template, ResizeFilterType resizeMethod, bool resize, bool upsize, DenoiseFilterType denoiseMethod,
            bool denoise, bool mpeg2deblock, bool colourCorrect, mod16Method method, bool dss2, modValue modValueUsed, decimal acceptableAspectError)
        {
            this.Template = template;
            this.Resize = resize;
            this.ResizeMethod = resizeMethod;
            this.DenoiseMethod = denoiseMethod;
            this.Deinterlace = deinterlace;
            this.Denoise = denoise;
            this.IVTC = ivtc;
            this.MPEG2Deblock = mpeg2deblock;
            this.ColourCorrect = colourCorrect;
            this.Mod16Method = method;
            this.DSS2 = dss2;
            this.Upsize = upsize;
            this.ModValue = modValueUsed;
            this.acceptableAspectError = acceptableAspectError;
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as GenericSettings);
        }

        public bool Equals(GenericSettings other)
        {
            return other == null ? false : PropertyEqualityTester.AreEqual(this, other);
        }

        public override int GetHashCode()
        {
            return base.GetHashCode();
        }

        object ICloneable.Clone()
        {
            return Clone();
        }

        GenericSettings GenericSettings.Clone()
        {
            return this.MemberwiseClone() as AviSynthSettings;
        }

        public AviSynthSettings Clone()
        {
            return this.MemberwiseClone() as AviSynthSettings;
        }

        public mod16Method Mod16Method
        {
            get { return mod16Method; }
            set { mod16Method = value; }
        }

        public modValue ModValue
        {
            get { return modValueUsed; }
            set { modValueUsed = value; }
        }
        
        public bool Resize
        {
            get { return resize; }
            set { resize = value; }
        }

        public bool Upsize
        {
            get { return upsize; }
            set { upsize = value; }
        }

		public string Template
		{
			get {return template;}
			set {
				string[] lines = value.Split('\r', '\n');
				StringBuilder script = new StringBuilder();
				script.EnsureCapacity(value.Length);
				foreach (string line in lines)
				{
					if (line.Length>0)
					{
						script.Append(line);
						script.Append("\r\n");
					}
				}
				template = script.ToString();}
		}

		public ResizeFilterType ResizeMethod
		{
			get { return resizeMethod; }
			set { resizeMethod = value; }
		}

		public DenoiseFilterType DenoiseMethod
		{
			get { return denoiseMethod; }
			set { denoiseMethod = value; }
		}

		public bool Deinterlace
		{
			get { return deinterlace; }
			set { deinterlace = value; }
		}

		public bool Denoise
		{
			get { return denoise; }
			set { denoise = value; }
		}

		public bool IVTC
		{
			get { return ivtc; }
			set { ivtc = value; }
		}

		public bool MPEG2Deblock
		{
			get { return mpeg2deblock; }
			set { mpeg2deblock = value; }
		}

		public bool ColourCorrect
		{
			get { return colourCorrect; }
			set { colourCorrect = value; }
		}

        public bool DSS2
        {
            get { return dss2; }
            set { dss2 = value; }
        }

        /// <summary>
        /// Maximum aspect error (%) to allow in anamorphic resizing.
        /// </summary>
        public decimal AcceptableAspectError
        {
            get { return acceptableAspectError; }
            set { acceptableAspectError = value; }
        }

        #region GenericSettings Members


        public string[] RequiredFiles
        {
            get { return new string[0]; }
        }

        public string[] RequiredProfiles
        {
            get { return new string[0]; }
        }

        #endregion
    }
}
