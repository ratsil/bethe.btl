using System;
using System.Collections.Generic;
using System.Text;
using System.Drawing.Drawing2D;
using System.Drawing;
using System.Drawing.Imaging;

using helpers;

namespace BTL.Play
{

	public class Transition : ContainerVideoAudio
    {
        public enum TypeVideo
        {
            cut,
            dissolve,
            wipe,
            iris
        }
        public enum TypeAudio
        { 
            cut,
            crossfade
        }

        private IEffect _cEffectSource, _cEffectTarget;
        private ulong _nFramesCounterVideo = 0;
        private ulong _nFramesCounterAudio = 0;
        //-----------------
        public bool bSourceNonStop { get; set; }
		public bool bTargetNonStop { get; set; }
        public TypeVideo eTransitionTypeVideo { get; set; }
        public TypeAudio eTransitionTypeAudio { get; set; }
        private PixelsMap _cPixelsMap;
        private PixelsMap.Triple _cPMDuo;
        override public ulong nFrameCurrentVideo
		{
			get
			{
				return _nFramesCounterVideo;
			}
		}
		override public ulong nFrameCurrentAudio
		{
			get
			{
				return _nFramesCounterAudio;
			}
		}
		
		public Transition()
            : base(EffectType.Transition)
        {
			try
			{
                base.nEffectsQty = 2;
				_nFramesCounterVideo = 0;
				_nFramesCounterAudio = 0;
				nDuration = 10;
				bSourceNonStop = false;
				bTargetNonStop = true;
				eTransitionTypeVideo = TypeVideo.dissolve;
				eTransitionTypeAudio = TypeAudio.crossfade;
			}
			catch
			{
				Fail();
				throw;
			}
        }
        public Transition(Effect cEffectSource, Effect cEffectTarget)
            : this()
        {
			try
            {
                if (null == cEffectSource) // TODO проверять видео или только аудио.....
                    _cEffectSource = new Composite(1, 1);
                else
                    _cEffectSource = cEffectSource;
                if (null == cEffectTarget)
                    _cEffectTarget = new Composite(1, 1);
                else
                    _cEffectTarget = cEffectTarget;
                #region z-layer
                if (_cEffectSource.nLayer > _cEffectTarget.nLayer)
                    ((IEffect)this).nLayer = _cEffectTarget.nLayer;
                else
					((IEffect)this).nLayer = _cEffectSource.nLayer;
				#endregion
			}
			catch
			{
				Fail();
				throw;
			}
		}

        ~Transition()
        {
			try
			{
                Baetylus.PixelsMapDispose(_cPMDuo, true);
            }
			catch (Exception ex)
			{
				(new Logger()).WriteError(ex);
			}
		}

		override public void Prepare()
        {
			base.Prepare();
            if (0 == nDuration)
                nDuration = 1;


            if (stMergingMethod.eDeviceType == MergingDevice.DisCom)
                PixelsMap.DisComInit();




            if (EffectStatus.Idle == _cEffectSource.eStatus || EffectStatus.Stopped == _cEffectSource.eStatus)
                _cEffectSource.Prepare();
            if (EffectStatus.Idle == _cEffectTarget.eStatus || EffectStatus.Stopped == _cEffectTarget.eStatus)
                _cEffectTarget.Prepare();

            if (_cEffectSource is IVideo && _cEffectTarget is IVideo)
            {
                stArea = SumOfAreas(((IVideo)_cEffectSource).stArea, ((IVideo)_cEffectTarget).stArea);
                if (null != _cPMDuo && stArea != _cPMDuo.cFirst.stArea)
                {
                    Baetylus.PixelsMapDispose(_cPMDuo, true);
                    _cPMDuo = null;
                }
                if (null == _cPMDuo)
                {
                    //_cPixelsMap = new PixelsMap(stMergingMethod, stArea, PixelsMap.Format.ARGB32);
                    _cPMDuo = new PixelsMap.Triple(new MergingMethod(stMergingMethod.eDeviceType, 0), stArea, PixelsMap.Format.ARGB32, true, Baetylus.PixelsMapDispose);  //MergingDevice.DisCom
                    if (1 > _cPMDuo.cFirst.nLength)
                        (new Logger()).WriteNotice("1 > _cPixelsMap.nLength. transition.prepare");
                    _cPMDuo.Allocate();
                }
                _cPMDuo.RenewFirstTime();
                nPixelsMapSyncIndex = byte.MaxValue;
            }
        }
        override public void Idle()
        {
            _nFramesCounterVideo = 0;
            _nFramesCounterAudio = 0;
            base.Idle();
        }
        override public void Start(IContainer iContainer)
        {
			(new Logger()).WriteDebug3("in");
            base.Start(null);

			Dictionary<IEffect, ContainerAction> aCAs = new Dictionary<IEffect, ContainerAction>();
			aCAs.Add(this, ContainerAction.Add);
            if (EffectStatus.Running == _cEffectSource.eStatus && EffectStatus.Running == _cEffectTarget.eStatus)
			{
				this.iContainer = _cEffectSource.iContainer;
                if (_cEffectTarget.iContainer != this.iContainer)
					throw new Exception("container of the transitioning effects must be the same.");
				aCAs.Add(_cEffectSource, ContainerAction.Remove);
				aCAs.Add(_cEffectTarget, ContainerAction.Remove);
			}
			else if (EffectStatus.Running == _cEffectSource.eStatus)
			{
                this.iContainer = _cEffectSource.iContainer;
				aCAs.Add(_cEffectSource, ContainerAction.Remove);
			}
			else if (EffectStatus.Running == _cEffectTarget.eStatus)
			{
                this.iContainer = _cEffectTarget.iContainer;
				aCAs.Add(_cEffectTarget, ContainerAction.Remove);
			}
            else if (null == this.iContainer)
                this.iContainer = BTL.Baetylus.Helper.cBaetylus;

			if (EffectStatus.Preparing == _cEffectSource.eStatus)
				_cEffectSource.Start(null);
			if (EffectStatus.Preparing == _cEffectTarget.eStatus)
                _cEffectTarget.Start(null);

			_cEffectSource.iContainer = this;
			_cEffectTarget.iContainer = this;
			iContainer.EffectsProcess(aCAs);
			(new Logger()).WriteDebug3("return");
		}
        public override void Stop()
        {
            Dictionary<IEffect, ContainerAction> aCAs = new Dictionary<IEffect, ContainerAction>();
            if (bSourceNonStop)
            {
                _cEffectSource.iContainer = iContainer;
                aCAs.Add(_cEffectSource, ContainerAction.Add);
            }
            else
            {
                if (EffectStatus.Preparing == _cEffectSource.eStatus)
                    _cEffectSource.Idle();
                if (EffectStatus.Running == _cEffectSource.eStatus)
                    _cEffectSource.Stop();
            }
            if (bTargetNonStop)
            {
                _cEffectTarget.iContainer = iContainer;
                aCAs.Add(_cEffectTarget, ContainerAction.Add);
            }
            else
            {
                if (EffectStatus.Preparing == _cEffectTarget.eStatus)
                    _cEffectTarget.Idle();
                if (EffectStatus.Running == _cEffectTarget.eStatus)
                    _cEffectTarget.Stop();
            }
            aCAs.Add(this, ContainerAction.Remove);
			iContainer.EffectsProcess(aCAs);
            base.Stop();
		}

        override public PixelsMap FrameNextVideo()
        {
			if (!(_cEffectSource is IVideo) && !(_cEffectTarget is IVideo))
				return null;
            if (nDuration == _nFramesCounterVideo)
                return null;
            _nFramesCounterVideo++;
            List<PixelsMap> aFrames = new List<PixelsMap>();
            PixelsMap cFrame;
            Dictionary<PixelsMap, byte> nAlphaConstantOld = new Dictionary<PixelsMap, byte>();
            float nProgress = (float)_nFramesCounterVideo / (float)nDuration;
            bool bIfLayersHave255Alpha = true;
            IVideo iVideo;
            if (_cEffectSource is IVideo)
            {
                iVideo = (IVideo)_cEffectSource;
                iVideo.nPixelsMapSyncIndex = nPixelsMapSyncIndex;
                if (null != (cFrame = iVideo.FrameNext()))
                {
                    if (cFrame.nAlphaConstant<255)
                        bIfLayersHave255Alpha = false;
                    nAlphaConstantOld.Add(cFrame, cFrame.nAlphaConstant);
					//if(null != iVideo.iMask)
     //               {
     //                   aFrames.Add(iVideo.iMask.FrameNext());
     //                   aFrames[aFrames.Count - 1].eAlpha = DisCom.Alpha.mask;
     //               }
                    aFrames.Add(TransitionVideoFrame(cFrame, eTransitionTypeVideo, nProgress));
                }
            }
            if (_cEffectTarget is IVideo)
            {
                if (0.5 == nProgress)  // избегаем коллизии ровной середины
                    nProgress = 0.501F;
                iVideo = (IVideo)_cEffectTarget;
                iVideo.nPixelsMapSyncIndex = nPixelsMapSyncIndex;
                if (null != (cFrame = iVideo.FrameNext()))
                {
                    if (cFrame.nAlphaConstant<255)
                        bIfLayersHave255Alpha = false;
                    nAlphaConstantOld.Add(cFrame, cFrame.nAlphaConstant);
                    //if (null != iVideo.iMask)
                    //{
                    //    aFrames.Add(iVideo.iMask.FrameNext());
                    //    aFrames[aFrames.Count - 1].eAlpha = DisCom.Alpha.mask;
                    //}
                    aFrames.Add(TransitionVideoFrame(cFrame, eTransitionTypeVideo, 1 - nProgress));
                }
            }
            if (2 == aFrames.Count && aFrames[0].stArea == aFrames[1].stArea && bIfLayersHave255Alpha)
                aFrames[0].nAlphaConstant = byte.MaxValue;

            //PixelsMap cRetVal = PixelsMap.Merge(stArea, aFrames); 
            //EMERGENCY:l тут мы, между прочим, для каждого кадра делаем пикселсмэп, а это очень неэффективно...
            // И не надо отправлять в мердж, если эффект только один (уход в черное) - просто меняем ему конст альфу
            PixelsMap cRetVal = _cPMDuo.Switch(nPixelsMapSyncIndex);
            if (cRetVal == null) return null;

            cRetVal.Merge(aFrames);

            if (null != cRetVal && null != cMask)
                cRetVal.eAlpha = cMask.eMaskType;
            foreach (PixelsMap cPM in aFrames)
            {
                if (nAlphaConstantOld.ContainsKey(cPM))
                    cPM.nAlphaConstant = nAlphaConstantOld[cPM];
				Baetylus.PixelsMapDispose(cPM);
            }
			if (EffectStatus.Running  != _cEffectTarget.eStatus
				|| (nDuration == _nFramesCounterVideo 
					&& (_nFramesCounterVideo == _nFramesCounterAudio 
						|| (!(_cEffectSource is IAudio) && !(_cEffectTarget is IAudio))
					)
				)
			)
				Stop();
            return cRetVal;
        }
		override public Bytes FrameNextAudio()
        {
			if (!(_cEffectSource is IAudio) && !(_cEffectTarget is IAudio))
				return null;
			if (nDuration == _nFramesCounterAudio)
				return null;

            _nFramesCounterAudio++;
            List<Bytes> aAFrames = new List<Bytes>();
            Bytes aAFrame;
            float nProgress = (float)_nFramesCounterAudio / (float)nDuration;
            if (_cEffectSource is IAudio)
            {
				if (_cEffectSource.eStatus == EffectStatus.Running)
				{
					if (null != (aAFrame = ((IAudio)_cEffectSource).FrameNext()))
						aAFrames.Add(TransitionAudioFrame(aAFrame, eTransitionTypeAudio, nProgress));
				}
            }
            if (_cEffectTarget is IAudio)
            {
                if (0.5 == nProgress)  // избегаем коллизии ровной середины
                    nProgress = 0.501F;
				if (_cEffectTarget.eStatus == EffectStatus.Running)
				{

					if (null != (aAFrame = ((IAudio)_cEffectTarget).FrameNext()))
						aAFrames.Add(TransitionAudioFrame(aAFrame, eTransitionTypeAudio, 1 - nProgress));
				}
            }
			if (EffectStatus.Running != _cEffectTarget.eStatus
				|| (nDuration == _nFramesCounterAudio
					&& (_nFramesCounterVideo == _nFramesCounterAudio
						|| (!(_cEffectSource is IVideo) && !(_cEffectTarget is IVideo))
					)
				)
			)
                Stop();
			if (2 == aAFrames.Count)
			{
				byte[] aSourceChannels, aTargetChannels;
				if (((IAudio)_cEffectSource).aChannels == null)
				{
					aSourceChannels = new byte[aAFrames[0].Length / Preferences.nAudioBytesPerFramePerChannel];
					if (2 == aSourceChannels.Length)
					{
						aSourceChannels[0] = 0;
						aSourceChannels[1] = 1;
					}
				}
				else
					aSourceChannels = ((IAudio)_cEffectSource).aChannels;

				if (((IAudio)_cEffectTarget).aChannels == null)
				{
					aTargetChannels = new byte[aAFrames[1].Length / Preferences.nAudioBytesPerFramePerChannel];
					if (2 == aTargetChannels.Length)
					{
						aTargetChannels[0] = 0;
						aTargetChannels[1] = 1;
					}
				}
				else
					aTargetChannels = ((IAudio)_cEffectTarget).aChannels;

				if (aTargetChannels.Length == aSourceChannels.Length && 2 == aTargetChannels.Length
					&& aTargetChannels[0] == aSourceChannels[0]
					&& aTargetChannels[1] == aSourceChannels[1]
				)
					return MergeAudioSamples(aAFrames[0], aAFrames[1]);
				else
					return aAFrames[1];
			}
			else if (1 == aAFrames.Count)
				return aAFrames[0];
            else
                return null;
        }

        internal void EffectSourceSet(IEffect cEffectSource)
        {
            if (null == cEffectSource)
                _cEffectSource = new Composite(1, 1);
            else
                _cEffectSource = cEffectSource;
        }
        internal void EffectTargetSet(IEffect cEffectTarget)
        {
            if (null == cEffectTarget)
            {
                _cEffectTarget = new Composite(1, 1);
                bTargetNonStop = false;
            }
            else
                _cEffectTarget = cEffectTarget;
        }

        private PixelsMap TransitionVideoFrame(PixelsMap cFrame,TypeVideo eTransitionType, float nProgress)
        {
            if (0 > cFrame.nAlphaConstant)
                cFrame.nAlphaConstant = 255;  // = 250
            switch (eTransitionType)
            {
                case TypeVideo.cut:
                    if (nProgress > 0.5)
                        cFrame.nAlphaConstant = 0;
                    break;
                case TypeVideo.dissolve:
                    cFrame.nAlphaConstant = (byte)((float)cFrame.nAlphaConstant - (float)cFrame.nAlphaConstant * nProgress + 0.5);
                    break;
                //default:
            }
            return cFrame;
        }
        public static Bytes TransitionAudioFrame(Bytes aAudioSamples, TypeAudio eTransitionType, float nProgress)
        {
			switch (eTransitionType)
            {
                case TypeAudio.cut:
                        if (nProgress < 0.5)
                        {
                            return aAudioSamples;
                        }
                        else
                            return null;
                case TypeAudio.crossfade:
						short nCoeff;
                        for (int nByteIndx = 0; nByteIndx < aAudioSamples.Length; nByteIndx += 2)
                        {
                            nCoeff = (short)((aAudioSamples.aBytes[nByteIndx + 1] << 8) + aAudioSamples.aBytes[nByteIndx]);
                            nCoeff = (short)(nCoeff * (1 - nProgress));
                            aAudioSamples.aBytes[nByteIndx] = (byte)nCoeff;
                            aAudioSamples.aBytes[nByteIndx + 1] = (byte)(nCoeff >> 8);
                        }
                        break;
                default:
						throw new NotImplementedException("unknown audio transition type");
            }
            return aAudioSamples;
        }
        static public Area SumOfAreas(Area a1, Area a2)
        {
            Area aRetVal = new Area();
			aRetVal.nLeft = 0; // a1.nLeft < a2.nLeft ? a1.nLeft : a2.nLeft;
			aRetVal.nTop = 0; // a1.nTop < a2.nTop ? a1.nTop : a2.nTop;
            aRetVal.nWidth = a1.nRight < a2.nRight ? (ushort)(a2.nRight - aRetVal.nLeft + 1) : (ushort)(a1.nRight - aRetVal.nLeft + 1);
			aRetVal.nWidth += a1.nLeft < 0 || a2.nLeft < 0 ? (ushort)0 : (ushort)Math.Min(a1.nLeft, a2.nLeft);
            aRetVal.nHeight = a1.nBottom < a2.nBottom ? (ushort)(a2.nBottom - aRetVal.nTop + 1) : (ushort)(a1.nBottom - aRetVal.nTop + 1);
			aRetVal.nHeight += a1.nTop <= 0 || a2.nTop <= 0 ? (ushort)0 : (ushort)Math.Min(a1.nTop, a2.nTop);
            return aRetVal;
        }
        Bytes MergeAudioSamples(Bytes a1, Bytes a2)
        {
            int nSampleRate = (int)Preferences.nAudioSamplesRate;
			int nChannelsQty = 2; //Preferences.nAudioChannelsQty;
			int nBytesPerChannel = Preferences.nAudioByteDepth;
			int nFramesRate = Preferences.nFPS;
			int nBytesQty = nSampleRate * nChannelsQty * nBytesPerChannel / nFramesRate;
            Bytes aRetVal = Baetylus._cBinM.BytesGet(nBytesQty, 3);
            // нужна проверка формата!!!!!!!!!!!!
            // пока что считаем {16 бит 48000 герц стерео} = 7680 байт
            if (null == a1 || nBytesQty != a1.Length)
            {
                return a2;
            }
            else if (null == a2 || nBytesQty != a2.Length)
            {
                return a1;
            }
            short k1, k2, rez;
            int tmp;
            for (int ii = 0; ii < nBytesQty; ii += 2)
            {
                k1 = (short)((a1.aBytes[ii + 1] << 8) + a1.aBytes[ii]);
                k2 = (short)((a2.aBytes[ii + 1] << 8) + a2.aBytes[ii]);
                tmp = k1 + k2;
                if (Int16.MaxValue < tmp) 
                    tmp = Int16.MaxValue;
                else if (Int16.MinValue > tmp) 
                    tmp = Int16.MinValue;
                rez = (short)tmp;
                aRetVal.aBytes[ii] = (byte)rez;
                aRetVal.aBytes[ii + 1] = (byte)(rez >> 8);
            }
            return aRetVal;
        }
    }
}
