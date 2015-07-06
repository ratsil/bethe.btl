using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using helpers;

namespace BTL.Play
{
    abstract public class EffectAudio : Effect, IAudio
    {
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
		byte[] IAudio.FrameNext()
		{
			return this.FrameNext();
		}
		void IAudio.Skip()
		{
			this.Skip();
		}
		#endregion

		virtual public byte[] FrameNext()
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
