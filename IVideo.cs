using System;
using System.Collections.Generic;
using System.Text;

using helpers;

namespace BTL
{
    public interface IVideo
    {
		event BTL.Play.Effect.EventDelegate FrameMaking;
		Area stArea { get; set; }
        bool bOpacity { get; set; }
        bool bCUDA { get; set; }
        IVideo iMask { get; set; }
		byte nInDissolve { get; set; }
		byte nOutDissolve { get; set; }
		byte nMaxOpacity { get; set; }

        PixelsMap FrameNext();
        void Skip();
		Dock.Offset OffsetAbsoluteGet();
    }
}
