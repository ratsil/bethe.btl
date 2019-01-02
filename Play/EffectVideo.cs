using System;
using System.Collections.Generic;
using System.Text;

using System.Drawing.Drawing2D;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using helpers;
using helpers.extensions;
using System.Xml;
using helpers.extensions;

namespace BTL.Play
{
    abstract public class EffectVideo : Effect, IVideo
    {
		public event EventDelegate FrameMaking;
		virtual public Area stArea { get; set; }
        virtual public bool bOpacity { get; set; }
        virtual public MergingMethod stMergingMethod { get; set; }
        virtual public IVideo iMask
		{
			get
			{
				return _iMask;
			}
			set
			{
                _iMask = value;
                if (null == _iMask.cMask)
                    _iMask.cMask = new Mask() { eMaskType = DisCom.Alpha.mask };
			}
		}
		virtual public Play.Mask cMask { get; set; }
		virtual public byte nInDissolve { get; set; }
		virtual public byte nOutDissolve { get; set; }
		virtual public byte nMaxOpacity { get; set; }
        virtual public byte nPixelsMapSyncIndex { get; set; }
        private IVideo _iMask;
		public Area stBase { get; set; }
		private Dock _cDock;
        public Dock cDock
		{
			set
			{
				(new Logger()).WriteDebug4("in");
				_cDock = value;
				stArea = stArea.Dock(stBase, value);
			}
			get
			{
				return _cDock;
			}
		}
		internal byte nCurrentOpacity
		{
			get
			{
				if (1 < nInDissolve && nFrameCurrent <= nInDissolve)
					return ((float)(nFrameCurrent) * nMaxOpacity / nInDissolve).ToByte();
				if (1 < nOutDissolve && nDuration + 1 - nFrameCurrent <= nOutDissolve)
					return ((float)(nDuration + 1 - nFrameCurrent) * nMaxOpacity / nOutDissolve).ToByte();
				return nMaxOpacity;
			}
		}
		internal EffectVideo(EffectType eType)
			: base(eType)
		{
			try
			{
                stMergingMethod = Preferences.stMerging;
                stBase = Baetylus.Helper.stCurrentBTLArea;
				nFrameCurrent = 0;
				stArea = new Area(0, 0, 0, 0);
				nMaxOpacity = 255;
				nInDissolve = 0;
				nOutDissolve = 0;
                nPixelsMapSyncIndex = byte.MaxValue;
            }
			catch
			{
				Fail();
				throw;
			}
		}
		new internal void LoadXML(XmlNode cXmlNode)
		{
			base.LoadXML(cXmlNode);

            if (null != cXmlNode.AttributeValueGet("cuda", false) || null != cXmlNode.AttributeValueGet("merging", false))
                stMergingMethod = new MergingMethod(cXmlNode);
            else
                stMergingMethod = iContainer == null || iContainer.stMergingMethod == null ? Preferences.stMerging : iContainer.stMergingMethod.Value;

            nInDissolve = cXmlNode.AttributeOrDefaultGet<byte>("in_dissolve", 0);
			nOutDissolve = cXmlNode.AttributeOrDefaultGet<byte>("out_dissolve", 0);
			nMaxOpacity = cXmlNode.AttributeOrDefaultGet<byte>("max_opacity", 255);

			bOpacity = cXmlNode.AttributeOrDefaultGet<bool>("opacity", false);
            cMask = Play.Mask.MaskLoad(cXmlNode);

            XmlNode cNodeChild;
            if (null == cMask && null != (cNodeChild = cXmlNode.NodeGet("mask", false)))
            {
                this.iMask = (IVideo)EffectGet(cNodeChild.ChildNodes[0]);
            }

            stArea = stArea.LoadXML(cXmlNode);
			Dock cDockTMP = new Dock();
			cDockTMP.LoadXML(cXmlNode);
			cDock = cDockTMP;
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
        MergingMethod IVideo.stMergingMethod
        {
            get
            {
                return this.stMergingMethod;
            }
            set
            {
                this.stMergingMethod = value;
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
        Mask IVideo.cMask
        {
            get
            {
                return this.cMask;
            }
            set
            {
                this.cMask = value;
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
        byte IVideo.nPixelsMapSyncIndex
        {
            get
            {
                return this.nPixelsMapSyncIndex;
            }
            set
            {
                this.nPixelsMapSyncIndex = value;
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
		Area IVideo.stBase
		{
			get
			{
				return this.stBase;
			}
			set
			{
				this.stBase = value;
			}
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
    public class Mask
    {
        public DisCom.Alpha eMaskType;
        //public string sTargetEffectName;  // не реализовано. то же что и imask по сути, но можно много масок вешать и с кейфреймами и т.д.
        static public Mask MaskLoad(XmlNode cXmlNode)
        {
            if (null == cXmlNode.AttributeValueGet("mask_type", false))
                return null;
            Mask cRetVal = new Mask();
            cRetVal.eMaskType = cXmlNode.AttributeGet<DisCom.Alpha>("mask_type");
            return cRetVal;
        }
    }
}
