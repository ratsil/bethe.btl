using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using helpers.extensions;

using helpers;

namespace BTL.Play
{
    abstract public class EffectVideoAudio : Effect, IVideo, IAudio
    {
        private class Video : EffectVideo
        {
            internal Video(EffectType eType)
                : base(eType)
            { }

            override protected void Fail()
            {
                base.Fail();
            }
            public void FailOverride()
            {
                this.Fail();
            }
        }
        private class Audio : EffectAudio
        {
			override public byte[] aChannels
            {
                get
                {
					return base.aChannels;
                }
                set
                {
					this.aChannelsOverride = value;
                }
            }
			public byte[] aChannelsOverride
            {
                get
                {
					return this.aChannels;
                }
                set
                {
					base.aChannels = value;
                }
            }

            internal Audio(EffectType eType)
                : base(eType)
            { }

            override protected void Fail()
            {
                base.Fail();
            }
            public void FailOverride()
            {
                this.Fail();
            }
        }
        private Video _cEffectVideo;
        private Audio _cEffectAudio;

		public static explicit operator EffectVideo(EffectVideoAudio cEffectVideoAudio)
		{
			return cEffectVideoAudio._cEffectVideo;
		}
		public static explicit operator EffectAudio(EffectVideoAudio cEffectVideoAudio)
		{
			return cEffectVideoAudio._cEffectAudio;
		}
        virtual public Area stArea
        {
            get
            {
                return _cEffectVideo.stArea;
            }
            set
            {
                (new Logger()).WriteDebug3("effect:va:area:set:in");
                _cEffectVideo.stArea = value;
                (new Logger()).WriteDebug4("effect:va:area:set:out");
            }
        }
		public byte[] aChannelsAudio
        {
            get
            {
                return _cEffectAudio.aChannelsOverride;
            }
            set
            {
                _cEffectAudio.aChannelsOverride = value;
            }
        }
        override public ulong nFrameStart
        {
            get
            {
                return _cEffectVideo.nFrameStart;
            }
            set
            {
                _cEffectVideo.nFrameStart = value;
                _cEffectAudio.nFrameStart = value;
            }
        }
        override public ulong nFramesTotal
        {
            get
            {
                return (nFramesTotalVideo > nFramesTotalAudio ? nFramesTotalAudio : nFramesTotalVideo);
            }
        }
        virtual public ulong nFramesTotalVideo
        {
            get
            {
                return _cEffectVideo.nFramesTotal;
            }
        }
        virtual public ulong nFramesTotalAudio
        {
            get
            {
                return _cEffectAudio.nFramesTotal;
            }
        }
		override public ulong nFrameCurrent
        {
            get
            {
				return (nFrameCurrentVideo > nFrameCurrentAudio ? nFrameCurrentVideo : nFrameCurrentAudio);
            }
        }
        virtual public ulong nFrameCurrentVideo
        {
            get
            {
                return _cEffectVideo.nFrameCurrent;
            }
        }
        virtual public ulong nFrameCurrentAudio
        {
            get
            {
                return _cEffectAudio.nFrameCurrent;
            }
        }
        override public ulong nDuration
        {
            get
            {
				return base.nDuration;
            }
            set
            {
				base.nDuration = value;
				_cEffectVideo.nDuration = value;
				_cEffectAudio.nDuration = value;
            }
        }
        virtual public bool bOpacity
        {
            get
            {
                return _cEffectVideo.bOpacity;
            }
            set
            {
                _cEffectVideo.bOpacity = value;
            }
        }
        virtual public bool bCUDA
        {
            get
            {
                return _cEffectVideo.bCUDA;
            }
            set
            {
                _cEffectVideo.bCUDA = value;
            }
        }
        virtual public IVideo iMask
        {
            get
            {
                return _cEffectVideo.iMask;
            }
            set
            {
                _cEffectVideo.iMask = value;
            }
        }
		byte IVideo.nInDissolve
		{
			get
			{
				return _cEffectVideo.nInDissolve;
			}
			set
			{
				_cEffectVideo.nInDissolve = value;
			}
		}
		byte IVideo.nOutDissolve
		{
			get
			{
				return _cEffectVideo.nOutDissolve;
			}
			set
			{
				_cEffectVideo.nOutDissolve = value;
			}
		}
		byte IVideo.nMaxOpacity
		{
			get
			{
				return _cEffectVideo.nMaxOpacity;
			}
			set
			{
				_cEffectVideo.nMaxOpacity = value;
			}
		}
        internal EffectVideoAudio(EffectType eType)
            : base(eType)
        {
            try
            {
                _cEffectVideo = new EffectVideoAudio.Video(eType);
                _cEffectAudio = new EffectVideoAudio.Audio(eType);
            }
            catch
            {
                Fail();
                throw;
            }
        }
        ~EffectVideoAudio()
        {
        }

        #region IVideo
		event EventDelegate IVideo.FrameMaking
		{
			add
			{
				_cEffectVideo.FrameMaking += value;
			}
			remove
			{
				_cEffectVideo.FrameMaking -= value;
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

        PixelsMap IVideo.FrameNext()
        {
            return this.FrameNextVideo();
        }
		void IVideo.Skip()
		{
			this.SkipVideo();
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
        #region IAudio
		byte[] IAudio.aChannels
        {
            get
            {
                return this.aChannelsAudio;
            }
            set
            {
                this.aChannelsAudio = value;
            }
        }

        byte[] IAudio.FrameNext()
        {
            return this.FrameNextAudio();
        }
		void IAudio.Skip()
		{
			this.SkipAudio();
		}
		#endregion

        override public void Dispose()
        {
			Logger.Timings cTimings = new Logger.Timings("btl:effect_v_a:dispose:");
			if (null != _cEffectVideo)
                _cEffectVideo.Dispose();
            if (null != _cEffectAudio)
                _cEffectAudio.Dispose();
            base.Dispose();
			cTimings.Stop("dispose >20ms [" + GetHashCode() + "]", 20);
		}
        override public void Prepare()
        {
            _cEffectVideo.Prepare();
            _cEffectAudio.Prepare();
            base.Prepare();
        }
        override public void Start(IContainer iContainer)
        {
            if (EffectStatus.Idle == eStatus) //это должно быть тут, иначе наш класс запутается в последовательности Prepare->Start
                Prepare();
            _cEffectVideo.Start(null);
            _cEffectAudio.Start(null);
            base.Start(iContainer);
        }
        override public void Stop()
        {
            base.Stop();
            if (EffectStatus.Stopped != _cEffectVideo.eStatus)
                _cEffectVideo.Stop();
            if (EffectStatus.Stopped != _cEffectAudio.eStatus)
                _cEffectAudio.Stop();
        }
        override public void Idle()
        {
            _cEffectVideo.Idle();
            _cEffectAudio.Idle();
            base.Idle();
        }
        override protected void Fail()
        {
            if (null != _cEffectVideo)
            {
                _cEffectVideo.FailOverride();
                _cEffectAudio.FailOverride();
            }
            base.Fail();
        }

        virtual public PixelsMap FrameNextVideo()
        {
			//if (!Preferences.bAudio || (null != aChannelsAudio && 1 > aChannelsAudio.Length))
			//    FrameNextAudio();
			return (null == _cEffectVideo ? null : _cEffectVideo.FrameNext());
		}
		virtual public void SkipVideo()
		{
			if (null != _cEffectVideo)
				_cEffectVideo.Skip();
		}
		virtual public void SkipAudio()
		{
			if (null != _cEffectAudio)
				_cEffectAudio.Skip();
		}
		virtual public byte[] FrameNextAudio()
        {
            return (null == _cEffectAudio? null : _cEffectAudio.FrameNext());
        }
    }
}
