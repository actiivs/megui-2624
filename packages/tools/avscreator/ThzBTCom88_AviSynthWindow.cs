using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MeGUI.packages.tools.avscreator
{
	class ThzBTCom88_AviSynthWindow : ThzBTCom_AviSynthWindow
	{
		public ThzBTCom88_AviSynthWindow(MainForm mainForm) : base(mainForm)
		{
		}

		public ThzBTCom88_AviSynthWindow(MainForm mainForm, string videoInput) : base(mainForm, videoInput)
		{
		}

		public ThzBTCom88_AviSynthWindow(MainForm mainForm, string videoInput, string indexFile) : base(mainForm, videoInput, indexFile)
		{
		}

		protected override void showScript(bool bForce)
		{
			if (bForce)
				scriptRefresh++;
			if (scriptRefresh < 1)
				return;

			string oldScript = avisynthScript.Text;
			avisynthScript.Text = this.generateScript();

			if (file != null && videoOutput != null)
			{
				avisynthScript.Text = avisynthScript.Text.Insert(0,
#if x64
					string.Format(
					"LoadPlugin(\"E:\\Software\\Video Tool\\MeGUI_2624_x64\\tools\\avisynth_plugin\\masktools2.dll\"){0}" +
					"LoadPlugin(\"E:\\Software\\Video Tool\\MeGUI_2624_x64\\tools\\avisynth_plugin\\FFT3DFilter.dll\"){0}" +
					"LoadPlugin(\"E:\\Software\\Video Tool\\MeGUI_2624_x64\\tools\\avisynth_plugin\\xlogo.dll\"){0}" +
					"LoadPlugin(\"E:\\Software\\Video Tool\\MeGUI_2624_x64\\tools\\avisynth_plugin\\RemoveGrain.dll\"){0}", Environment.NewLine));
#else
                    string.Format(
                    "LoadPlugin(\"E:\\Software\\Video Tool\\MeGUI_2624_x86\\tools\\avisynth_plugin\\masktools2.dll\"){0}" +
                    "LoadPlugin(\"E:\\Software\\Video Tool\\MeGUI_2624_x86\\tools\\avisynth_plugin\\FFT3DFilter.dll\"){0}" +
                    "LoadPlugin(\"E:\\Software\\Video Tool\\MeGUI_2624_x86\\tools\\avisynth_plugin\\xlogo.dll\"){0}" +
                    "LoadPlugin(\"E:\\Software\\Video Tool\\MeGUI_2624_x86\\tools\\avisynth_plugin\\RemoveGrain.dll\"){0}", Environment.NewLine));
#endif

				var left = string.Format("left = last.LanczosResize(1280,720){1}res = xlogo(left, \"G:\\Logo\\88Thz_x_1120_y_646_2.bmp\", X=1120, Y=646, alpha=0){1}{1}return res", file.VideoInfo.FrameCount - 3, Environment.NewLine);
				avisynthScript.Text = avisynthScript.Text.Replace("#deinterlace", string.Format("{0}{1}", Environment.NewLine, left));
				var cropIndex = avisynthScript.Text.IndexOf("#crop", 0, StringComparison.OrdinalIgnoreCase);
				if (cropIndex > 0)
					avisynthScript.Text = avisynthScript.Text.Remove(cropIndex);
			}

			if (!oldScript.Equals(avisynthScript.Text))
				chAutoPreview_CheckedChanged(null, null);
		}
	}

	public class ThzBTCom88_AviSynthWindowTool : MeGUI.core.plugins.interfaces.ITool
	{

		#region ITool Members

		public string Name
		{
			get { return "ThzBTCom88 AVS Script Creator"; }
		}

		public void Run(MainForm info)
		{
			info.ClosePlayer();
			var asw = new ThzBTCom88_AviSynthWindow(info);
			asw.OpenScript += new OpenScriptCallback(info.Video.openVideoFile);
			asw.QueuingJob += (sender, args) =>
			{
				args.Execute(info);
			};
			asw.Show();
		}

		public Shortcut[] Shortcuts
		{
			get { return new Shortcut[] { Shortcut.Alt3 }; }
		}

		#endregion

		#region IIDable Members

		public string ID
		{
			get { return "ThzBTCom88_AvsCreator"; }
		}

		#endregion
	}
}
