using System;
using System.Collections.Generic;
using System.Text;
using System.Drawing.Drawing2D;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Xml;
using helpers.extensions;
using helpers;

namespace BTL.Play
{
    public class Clock : Text
    {
        private DateTime _dtDateTime;
		private DateTime _dtNextSync;
        private string _sTimeFormat;
        public string sSuffix;
        public Clock(string sTimeFormat, Font cFont, float nBorderWidth, Color stColor, Color stColorBorder)
			: base(DateTime.MinValue.ToString(sTimeFormat), cFont, nBorderWidth, stColor, stColorBorder)
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
		public Clock(XmlNode cXmlNode)
			: base(cXmlNode)
		{
			try
			{
				eType = EffectType.Clock;
				_sTimeFormat = cXmlNode.AttributeOrDefaultGet<string>("format", "HH:mm");
				XmlNode cNodeChild;
				if (null != (cNodeChild = cXmlNode.NodeGet("suffix", false)))
				{
					if (null == cNodeChild.FirstChild || null == cNodeChild.FirstChild.Value || 1 > cNodeChild.FirstChild.Value.Length)
						sSuffix = "";
					else
						sSuffix = cNodeChild.FirstChild.Value.FromXML();
				}
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
			SyncClock();
		}

		override public PixelsMap FrameNext()
		{
			if (DateTime.Now > _dtNextSync)
			{
				SyncClock();
			}
			_dtDateTime = _dtDateTime.AddMilliseconds(Preferences.nFrameDuration);  // постепенно остают, если работают месяцами безостановочно - надо подправлять иногда
			sText = _dtDateTime.ToString(_sTimeFormat) + sSuffix;
            return base.FrameNext();
        }
		private void SyncClock()
		{
			_dtDateTime = DateTime.Now.AddMilliseconds(Preferences.nClockBiasInFrames * Preferences.nFrameDuration);  // поправка на очереди кадров. 50 кадров очередь в _aqAVBuffer = 50*40=2000 мс
			_dtNextSync = _dtDateTime.AddDays(1);
			(new Logger()).WriteNotice("SyncClock: [clock_bias=" + Preferences.nClockBiasInFrames + "][time=" + _dtDateTime.ToString("yyyy-MM-dd HH:mm:ss") + "][nextsync=" + _dtNextSync.ToString("yyyy-MM-dd HH:mm:ss") + "]");
		}
	}
}
