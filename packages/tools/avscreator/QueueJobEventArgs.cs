using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MeGUI
{
    public class QueueJobEventArgs : EventArgs
    {
        public decimal Fps { get; set; }
        public string FilenameWithoutExtension { get; set; }
        public string SourceFilename { get; set; }
    }
}
