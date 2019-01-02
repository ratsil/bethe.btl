using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using helpers;
using System.Xml;
using helpers.extensions;


namespace BTL.Play
{
    abstract public class EffectAudio : Effect, IAudio
    {
		virtual public byte nInFade { get; set; }
		virtual public byte nOutFade { get; set; }
		internal float nCurrentLevel
		{
			get
			{
				if (1 < nInFade && nFrameCurrent <= nInFade)
					return (((float)nFrameCurrent) / nInFade);
				if (1 < nOutFade && nDuration + 1 - nFrameCurrent <= nOutFade)
					return ((float)(nDuration + 1 - nFrameCurrent) / nOutFade);
				return 1;
			}
		}
		private byte[] _aChannels;

		virtual public byte[] aChannels
		{
			get
			{
				return _aChannels;
			}
			set
			{
				_aChannels = value;
			}
		}

		internal EffectAudio(EffectType eType)
            : base(eType)
        {
			try
			{
				aChannels = null; //комментарии в IAudio
			}
			catch
			{
				Fail();
				throw;
			}
		}
		~EffectAudio()
        {
        }

		#region IAudio
		byte[] IAudio.aChannels
		{
			get
			{
				return this.aChannels;
			}
			set
			{
				this.aChannels = value;
			}
		}
		byte IAudio.nInFade
		{
			get
			{
				return this.nInFade;
			}
			set
			{
				this.nInFade = value;
			}
		}
		byte IAudio.nOutFade
		{
			get
			{
				return this.nOutFade;
			}
			set
			{
				this.nOutFade = value;
			}
		}
		Bytes IAudio.FrameNext()
		{
			return this.FrameNext();
		}
		void IAudio.Skip()
		{
			this.Skip();
		}
		#endregion
		new internal void LoadXML(XmlNode cXmlNode)
		{
			base.LoadXML(cXmlNode);
			LoadXMLChannels(cXmlNode);
        }
		internal void LoadXMLChannels(XmlNode cXmlNode)
		{
			cXmlNode = cXmlNode.NodeGet("channels", false);
			nInFade = cXmlNode.AttributeOrDefaultGet<byte>("in_dissolve", 0);
			nOutFade = cXmlNode.AttributeOrDefaultGet<byte>("out_dissolve", 0);

			if (null != cXmlNode)
			{
				byte[] aChannels = new byte[cXmlNode.SelectNodes("channel").Count];
				XmlNode cNodeChild;
				while (null != (cNodeChild = cXmlNode.NodeGet("channel", false)))
				{
					aChannels[cNodeChild.AttributeGet<byte>("source")] = cNodeChild.AttributeGet<byte>("target");
					cXmlNode.RemoveChild(cNodeChild);
				}
				this.aChannels = aChannels.ToArray();
			}
			else
				this.aChannels = null;
		}
		virtual public Bytes FrameNext()
        {
			nFrameCurrent++;
            return null;
        }
		virtual public void Skip()
		{
			nFrameCurrent++;
			if (nFrameCurrent > nDuration && EffectStatus.Running == eStatus)
				this.Stop();
		}
	}
}
