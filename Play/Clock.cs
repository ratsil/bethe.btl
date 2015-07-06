using System;
using System.Collections.Generic;
using System.Text;
using System.Drawing.Drawing2D;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

using helpers;

namespace BTL.Play
{
    public class Clock : Text
    {
        private DateTime _dtDateTime;
        private string _sTimeFormat;
        public string sSuffix;
        public Clock(string sTimeFormat, Font cFont, float nBorderWidth)
			: base(DateTime.MinValue.ToString(sTimeFormat), cFont, nBorderWidth)
        {
			try
			{
				eType = EffectType.Clock;
				_sTimeFormat = sTimeFormat;
			}
			catch
			{
				Fail();
				throw;
			}
        }
        ~Clock()
        {
        }

        override public void Prepare()
        {
            base.Prepare();
            //_cText._stArea.nLeft = nLeft;
            //_cText._stArea.nTop = nTop;
            //_cText = new Text(_dtDateTime.ToString(_sDateFormat), _cFont);
        }
        override public void Start(IContainer iContainer)
        {
            base.Start(iContainer);
			_dtDateTime = DateTime.Now.AddMilliseconds(Preferences.nQueueBaetylusLength * Preferences.nFrameDuration);  // поправка на очереди кадров. 50 кадров очередь в _aqAVBuffer = 50*40=2000 мс
        }

        override public PixelsMap FrameNext()
        {
			_dtDateTime = _dtDateTime.AddMilliseconds(Preferences.nFrameDuration);
            sText = _dtDateTime.ToString(_sTimeFormat) + sSuffix;
            return base.FrameNext();
        }
    }
}
