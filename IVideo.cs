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
        MergingMethod stMergingMethod { get; set; }
        IVideo iMask { get; set; }
        Play.Mask cMask { get; set; }
        byte nInDissolve { get; set; }
        byte nOutDissolve { get; set; }
        byte nMaxOpacity { get; set; }
        byte nPixelsMapSyncIndex { get; set; }
        Area stBase { get; set; }

        PixelsMap FrameNext();
        void Skip();
		Dock.Offset OffsetAbsoluteGet();
    }
}
