using System;
using System.Collections.Generic;
using System.Text;

using System.Drawing.Drawing2D;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using helpers;
using helpers.extensions;

namespace BTL.Play
{
    abstract public class EffectVideo : Effect, IVideo
    {
		public event EventDelegate FrameMaking;
		virtual public Area stArea { get; set; }
        virtual public bool bOpacity { get; set; }
		virtual public bool bCUDA { get; set; }
        virtual public IVideo iMask { get; set; }
		virtual public byte nInDissolve { get; set; }
		virtual public byte nOutDissolve { get; set; }
		virtual public byte nMaxOpacity { get; set; }
		private Dock _cDock;
        public Dock cDock
		{
			set
			{
				(new Logger()).WriteDebug4("in");
				_cDock = value;
				if (stArea.nLeft == 0 && stArea.nTop == 0)
					stArea = stArea.Dock(Baetylus.Helper.cBaetylus.cBoard.stArea, value);
			}
			get
			{
				return _cDock;
			}
		}
		protected byte nCurrentOpacity //EMERGENCY:l ээээ... а почему у нас дизолв тока на тексте? я думал ты сделал по человечески... чтобы дизолв мог быть на любом эффекте - идём в ту сторону...
		{
			get
			{
				if (nFrameCurrent <= nInDissolve)
					return ((float)(nFrameCurrent) * nMaxOpacity / nInDissolve).ToByte();
				if (nDuration + 1 - nFrameCurrent <= nOutDissolve)
					return ((float)(nDuration + 1 - nFrameCurrent) * nMaxOpacity / nOutDissolve).ToByte();
				return nMaxOpacity;
			}
		}
		internal EffectVideo(EffectType eType)
			: base(eType)
		{
			try
			{
				bCUDA = Preferences.bCUDA;
				nFrameCurrent = 0;
				stArea = new Area(0, 0, 0, 0);
				nMaxOpacity = 255;
				nInDissolve = 0;
				nOutDissolve = 0;
			}
			catch
			{
				Fail();
				throw;
			}
		}
        ~EffectVideo()
        {
        }

		#region IVideo
		event EventDelegate IVideo.FrameMaking
		{
			add
			{
				this.FrameMaking += value;
			}
			remove
			{
				this.FrameMaking -= value;
			}
		}
		Area IVideo.stArea
		{
			get
			{
				return this.stArea;
			}
			set
			{
				this.stArea = value;
			}
		}
        bool IVideo.bOpacity
        {
            get
            {
                return this.bOpacity;
            }
            set
            {
                this.bOpacity = value;
            }
        }
        bool IVideo.bCUDA
        {
            get
            {
                return this.bCUDA;
            }
            set
            {
                this.bCUDA = value;
            }
        }
        IVideo IVideo.iMask
        {
            get
            {
                return this.iMask;
            }
            set
            {
                this.iMask = value;
            }
        }
		byte IVideo.nInDissolve 
		{
			get
			{
				return this.nInDissolve;
			}
			set
			{
				this.nInDissolve = value;
			}
		}
		byte IVideo.nOutDissolve
		{
			get
			{
				return this.nOutDissolve;
			}
			set
			{
				this.nOutDissolve = value;
			}
		}
		byte IVideo.nMaxOpacity
		{
			get
			{
				return this.nMaxOpacity;
			}
			set
			{
				this.nMaxOpacity = value;
			}
		}
        PixelsMap IVideo.FrameNext()
        {
            return this.FrameNext();
        }
        void IVideo.Skip()
        {
            this.Skip();
        }
		Dock.Offset IVideo.OffsetAbsoluteGet()
		{
			Dock.Offset cOffset = new Dock.Offset(this.stArea.nLeft, this.stArea.nTop);
			if (null != this.cContainer && this.cContainer is IVideo)
			{
				Dock.Offset cParent = ((IVideo)this.cContainer).OffsetAbsoluteGet();
				cOffset.nLeft += cParent.nLeft;
				cOffset.nTop += cParent.nTop;
			}
			return cOffset;
		}
		#endregion

		virtual public PixelsMap FrameNext()
        {
            nFrameCurrent++;
			if (null != FrameMaking)
				Effect.EventSend(FrameMaking, this);
            return null;
        }
		virtual public void Skip()
        {
            nFrameCurrent++;
			if (nFrameCurrent > nDuration && EffectStatus.Running == eStatus)
			{
				this.Stop();
			}
        }
    }
}
