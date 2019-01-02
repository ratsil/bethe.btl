using System;
using System.Collections.Generic;
using System.Text;
using helpers;
using helpers.extensions;
using System.Xml;

namespace BTL.Play
{
    public class Video : EffectVideoAudio
    {
        private class File
        {
            private string _sFile;

            private ffmpeg.net.File.Input _cFile;
            private ffmpeg.net.Format.Video _cFormatVideo;
            private ffmpeg.net.Format.Audio _cFormatAudio;
            private PixelsMap _cPixelsMap;
            private PixelsMap.Triple _cPMDuo;
            public PixelsMap.Triple cPMDuo
            {
                get
                {
                    return _cPMDuo;
                }
            }
            byte[] _aAudioMap;
            private bool _bClosed;
			private object _oCloseLock;
			Logger.Timings cTimings;
            public bool bEOF
			{
				get
				{
					if (null != _cFile)
						return _cFile.bFileEnd; // _bFileEnd
					else
						return false;
				}
			}
            public bool bFileInMemory
            {
                get
                {
                    if (null != _cFile)
                        return _cFile.bCached;
                    else
                        return false;
                }
            }
            public bool bFilePrepared
            {
                get
                {
                    if (null != _cFile)
                        return _cFile.bPrepared;
                    else
                        return false;
                }
            }
            public ulong nFrameStart { get; set; }
            public ulong nFramesTotal { get; private set; }
            public ulong nFrameCurrentVideo { get; private set; }
            public ulong nFrameCurrentAudio { get; private set; }
			public byte[] aAudioChannels { get; set; }
			public Area stArea { get; set; }
            public Area stContainerArea { get; set; }
            private Area stPixelArea { get; set; }
            public ushort nWidthOriginal
			{ 
				get
				{
					if (null == _cFile)
						_cFile = new ffmpeg.net.File.Input(_sFile, nFrameStart);
					return _cFile.cFormatVideo.nWidth;
				}
			}
			public ushort nHeightOriginal
			{
				get
				{
					if (null == _cFile)
						_cFile = new ffmpeg.net.File.Input(_sFile, nFrameStart);
					return _cFile.cFormatVideo.nHeight;
				}
			}
			public int nAspectRatio_dividend
			{
				get
				{
					if (null == _cFile)
						_cFile = new ffmpeg.net.File.Input(_sFile, nFrameStart);
					return _cFile.cFormatVideo.nAspectRatio_dividend;
				}
			}
			public int nAspectRatio_divider
			{
				get
				{
					if (null == _cFile)
						_cFile = new ffmpeg.net.File.Input(_sFile, nFrameStart);
					return _cFile.cFormatVideo.nAspectRatio_divider;
				}
			}
			public int nFileQueueLength
			{
				get
				{
					if (null != _cFile)
						return _cFile.nCueueLength;
					else
						return -2;
				}
			}
			public string sFile
			{
				get
				{
					return _sFile;
				}
			}
			public bool bFramesStarvation
			{
				get
				{
					return _cFile.bFramesStarvation;
				}
			}

            private File()
			{
				_sFile = null;
				if (ffmpeg.net.File.Input.nCacheSizeCommon == 0)
				{
					ffmpeg.net.File.Input.nCacheSizeCommon = Preferences.nQueueFfmpegLength;
					ffmpeg.net.File.Input.nBlockSizeCommon = Preferences.nQueuePacketsLength;
					ffmpeg.net.File.Input.nDecodingThreads = Preferences.nDecodingThreads;
					ffmpeg.net.File.Input.sDebugFolder = Preferences.sDebugFolder;
				}
				nFramesTotal = ulong.MaxValue;
				nFrameCurrentVideo = 0;
                nFrameCurrentAudio = 0;
				aAudioChannels = null; //комментарии в IAudio
				cTimings = new Logger.Timings("btl:video:file");
				if (ffmpeg.net.File.Input.nBTLBufferOneThird < 1)
				{
					ffmpeg.net.File.Input.nBTLBufferOneThird = BTL.Baetylus.nBufferOneThird;
					ffmpeg.net.File.Input.nBTLBufferTwoThird = BTL.Baetylus.nBufferTwoThird;
				}
            }

            public File(string sFile)
                : this()
            {
                if (null == sFile || !System.IO.File.Exists(sFile))
                    throw new Exception("no such file! " + sFile);
                _oCloseLock = new object();
                _sFile = sFile;
            }
            ~File()
            {
				try
                {
					(new Logger()).WriteDebug3("destructor_in " + GetHashCode());
					Close();
                }
				catch (Exception ex)
				{
					(new Logger()).WriteError(ex);
				}
            }

            public void Open(MergingMethod stMergingMethod, ffmpeg.net.File.Input.PlaybackMode ePlaybackMode)
            {
				// TODO  нужно выцепл¤ть размеры кадра из видео.
				_bClosed = false;
                if (null == _cFile)
				{
					_cFile = new ffmpeg.net.File.Input(_sFile, nFrameStart);
					_cFile.tsTimeout = Preferences.tsNextFrameTimeout;
				}
				(new Logger()).WriteDebug("[video_file open " + GetHashCode() + "][ffmpeg_file " + _cFile.GetHashCode() + "]");
                //Area stDeviceArea = Baetylus.Helper.stCurrentBTLArea;
                _cFormatVideo = new ffmpeg.net.Format.Video(stArea.nWidth, stArea.nHeight, ffmpeg.net.PixelFormat.AV_PIX_FMT_BGRA, ffmpeg.net.AVFieldOrder.AV_FIELD_TT);

                short nTop = stArea.nTop;
                ushort nHei = stArea.nHeight;
                if (stArea.nTop < 0) // если верх выше границы, то не будем копировать лишние строки в пиксельсмэп
                    nTop = 0;
                if (nTop < stContainerArea.nHeight && stArea.nHeight + stArea.nTop > stContainerArea.nHeight) // если низ ниже, то не будем копировать лишние строки в пиксельсмэп
                    nHei = (ushort)(stContainerArea.nHeight - (ushort)nTop);

                stPixelArea = new Area(stArea.nLeft, nTop, stArea.nWidth, nHei);

                _cPMDuo = new PixelsMap.Triple(stMergingMethod, stPixelArea, PixelsMap.Format.BGRA32, true, Baetylus.PixelsMapDispose);
                ffmpeg.net.AVSampleFormat eAVSampleFormat;
				switch (Preferences.nAudioBitDepth)
				{
					case 32:
                        eAVSampleFormat = ffmpeg.net.AVSampleFormat.AV_SAMPLE_FMT_S32;
						break;
					case 16:
					default:
                        eAVSampleFormat = ffmpeg.net.AVSampleFormat.AV_SAMPLE_FMT_S16;
						break;
				}
				int nAudioChannelsQty = Preferences.nAudioChannelsQtyFfmpeg;
				_cFormatAudio = new ffmpeg.net.Format.Audio((int)Preferences.nAudioSamplesRate, nAudioChannelsQty, eAVSampleFormat);
                nFramesTotal = _cFile.nFramesQty;
				//_cFile.Prepare(nDuration);
                _cFile.Prepare(_cFormatVideo, _cFormatAudio, ePlaybackMode);
            }
			public void Close()
			{
				lock (_oCloseLock)
				{
					if (_bClosed)
						return;
					_bClosed = true;
				}
				(new Logger()).WriteDebug3("in " + GetHashCode());
				Logger.Timings cTimings = new Logger.Timings("video:file:");

                Baetylus.PixelsMapDispose(_cPMDuo, true);
				_aAudioMap = null;

				cTimings.Restart("pm disposing");
				if (null != _cFile)
					_cFile.Dispose();
				cTimings.Stop("close > 20", "file disposing", 20);

				(new Logger()).WriteDebug4("out");
			}
			public void Reset()
			{
				(new Logger()).WriteDebug3("in " + GetHashCode());
				nFramesTotal = ulong.MaxValue;
				nFrameCurrentVideo = 0;
                nFrameCurrentAudio = 0;
                Close();
            }

            public PixelsMap FrameNextVideo()
			{
				cTimings.TotalRenew();
				try
				{
                    _cPixelsMap = _cPMDuo.cCurrent;
                    _cPixelsMap.Move(stPixelArea.nLeft, stPixelArea.nTop);  // были проблемы в транзишене. т.к. PM многоразовый, то кто-то его мог мувнуть (плейлист) и на место не класть. 
					ffmpeg.net.Frame cFrame = _cFile.FrameNextVideoGet();   //_cFormatVideo, 
					cTimings.Restart("ffmpeg_framenext");
                    if (null != cFrame)
                    {
                        int nOffset = 0;
                        int nOffsetBot = 0;
                        int nLine = _cFormatVideo.nWidth * _cFormatVideo.nBitsPerPixel / 8;
                        int nBotDiff = _cFormatVideo.nHeight - (stContainerArea.nHeight - stArea.nTop);
                        if (720 == _cFormatVideo.nWidth && 576 == _cFormatVideo.nHeight)  // инверси¤ полей нужна только на sd
                            nOffset = nLine;

                        if (stArea.nTop < 0)
                            nOffset += (-stArea.nTop) * nLine;
                        if (nBotDiff > 0)
                            nOffsetBot = nBotDiff * nLine;

                        _cPixelsMap.CopyIn(cFrame.pBytes + nOffset, cFrame.nLength - nOffset - nOffsetBot);  // сдвигаем фрейм на строчку вверх (это если не HD, а PAL)
                        cTimings.Restart("copy_in");
						cFrame.Dispose();
						cTimings.Restart("dispose");
						//GC.Collect(1, GCCollectionMode.Force_d);  // худший вариант был
                        //GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized);  // этот лучший был  ------- уехало в байтилус  -------
						//GC.Collect(GC.MaxGeneration, GCCollectionMode.Force_d);   // гипотеза (“ј  и оказалось!!!!) - т.к. сборка спонтанна¤ высвобождает сразу много мусора и и лочит этим copyin и вышибает пробки у Ѕлэкћэджика
						//cTimings.Restart("GCcollect. " + System.Runtime.GCSettings.LatencyMode + ".");
						nFrameCurrentVideo++;
					}
					else if (bEOF)
						return null;
					cTimings.Stop("framenext","eof_getting", 20);
					ffmpeg.net.File.Input.nBTLCurrentBuffer = Baetylus.nCurrentDeviceBufferCount;
                    return _cPixelsMap;
				}
				catch (Exception ex)
				{
					(new Logger()).WriteError(ex);
					_cFile.Close();
				}
                return null;
            }
            public Bytes FrameNextAudio()
            {
				Bytes aRetVal = null;
				try
				{
					ffmpeg.net.Frame cFrame = _cFile.FrameNextAudioGet();  //_cFormatAudio
					if (null != cFrame)
					{
						aRetVal = Baetylus._cBinM.BytesGet(cFrame.nLength, 4);
						cFrame.CopyBytesTo(aRetVal.aBytes);
						cFrame.Dispose();
					}
					if (null != aRetVal && 0 < aRetVal.Length)
						nFrameCurrentAudio++;
				}
				catch (Exception ex)
				{
					(new Logger()).WriteError(ex);
				}
                return aRetVal;
            }

			public void SkipVideo()
			{
				nFrameCurrentVideo++;
			}
			public void SkipAudio()
			{
				nFrameCurrentAudio++;
			}
		}

		private File _cFile;
		Logger.Timings cTimings;
        private object _oLock;
        private bool _bStopped;

        override public ulong nFramesTotal
        {
            get
            {
                return _cFile.nFramesTotal;
            }
        }
        override public ulong nFrameStart
        {
            get
            {
                return base.nFrameStart;
            }
            set
            {
                base.nFrameStart = value;
                _cFile.nFrameStart = base.nFrameStart;
            }
        }
		//override public ulong nDuration
		//{
		//	get
		//	{
		//		return base.nDuration;
		//	}
		//	set
		//	{
		//		ulong nFT = nFramesTotal;
		//		if (value > nFT)
		//			value = nFT;
		//		base.nDuration = value;
		//	}
		//}
		override public ulong nFrameCurrentVideo
        {
            get
            {
                return _cFile.nFrameCurrentVideo;
            }
        }
        override public ulong nFrameCurrentAudio
        {
            get
            {
                return _cFile.nFrameCurrentAudio;
            }
        }

		public ushort nWidthOriginal
		{
			get
			{
				return _cFile.nWidthOriginal;
			}
		}
		public ushort nHeightOriginal
		{
			get
			{
				return _cFile.nHeightOriginal;
			}
		}
		public int nAspectRatio_dividend
		{
			get
			{
				return _cFile.nAspectRatio_dividend;
			}
		}
		public int nAspectRatio_divider
		{
			get
			{
				return _cFile.nAspectRatio_divider;
			}
		}
		public string sFile
		{
			get
			{
				return _cFile.sFile;
			}
		}
		public int nFileQueueLength
		{
			get
			{
				if (null != _cFile)
					return _cFile.nFileQueueLength;
				else
					return
						-1;
			}
		}
		public bool bFramesStarvation
		{
			get
			{
				return _cFile.bFramesStarvation;
			}
		}
        public ffmpeg.net.File.Input.PlaybackMode ePlaybackMode;

        protected Video()
            : base(EffectType.Video)
        {
            try
            {
                _oLock = new object();
                (new Logger()).WriteDebug3("in");
                _cFile = null;
                stArea = Baetylus.Helper.stCurrentBTLArea;
				cTimings = new Logger.Timings("btl:video");
            }
            catch
            {
                Fail();
                throw;
            }
        }
        public Video(string sFile)
            : this()
        {
            try
            {
				Init(sFile);
            }
            catch
            {
                Fail();
                throw;
            }
        }
		public Video(XmlNode cXmlNode)
			: this()
		{
			try
			{
                ePlaybackMode = cXmlNode.AttributeOrDefaultGet<ffmpeg.net.File.Input.PlaybackMode>("pb_mode", ffmpeg.net.File.Input.PlaybackMode.RealTime);
                string sFile = cXmlNode.AttributeValueGet("file");
				Init(sFile);
				LoadXML(cXmlNode);
				_cFile.aAudioChannels = aChannelsAudio;
                _cFile.stContainerArea = stArea;

                short nCropHorizontal = cXmlNode.AttributeGet<short>("crop_horizontal");
				short nCropVertical = cXmlNode.AttributeGet<short>("crop_vertical");
				float nAspect_Ratio = cXmlNode.AttributeGet<float>("aspect_ratio");

				float nW = nWidthOriginal;
				float nH = nHeightOriginal;
				nH = nAspectRatio_divider == 0 ? nH : nH * nAspectRatio_dividend / nAspectRatio_divider;  // приведение к 1:1
				nW = nW * nAspect_Ratio;   // учЄт аспекта пиксел¤ результата

				float nWnewPre = stArea.nWidth + 2 * nCropHorizontal;
				float nHnewPre = stArea.nHeight + 2 * nCropVertical;

				float nK = Math.Min(nW / nWnewPre, nH / nHnewPre);
				float nWnew = nW / nK;
				float nHnew = nH / nK;

				int nCropHoriz = (int)((nWnew - nWnewPre) / 2 + nCropHorizontal);
				int nCropVert = (int)((nHnew - nHnewPre) / 2 + nCropVertical);

				stArea = new Area((short)(stArea.nLeft - nCropHoriz), (short)(stArea.nTop - nCropVert), (ushort)nWnew, (ushort)nHnew);
			}
			catch
			{
				Fail();
				throw;
			}
		}

		~Video()
		{
            try
            {
                Stop();
            }
            catch (Exception ex)
            {
                (new Logger()).WriteError(ex);
            }
        }

		protected void Init(string sFile)
		{
			_cFile = new File(sFile);
			_cFile.aAudioChannels = aChannelsAudio;
			if (stArea.nWidth == 0 || stArea.nHeight == 0) 
				stArea = new Area(stArea.nLeft, stArea.nTop, nWidthOriginal, nHeightOriginal);
            _cFile.stContainerArea = stArea;
        }
		override public void Idle()
        {
            base.Idle();
            _cFile.Reset();
        }
		enum FitIn
		{ 
			Crop,
			LetterBox, //TODO не реализовано
			Fill  //TODO не реализовано
		}
        override public void Prepare()
        {
			try
			{
				float nK;
				ushort nNewSize;
				short nHalfDelta;

                (new Logger()).WriteDebug2("video prepare [container area:" + _cFile.stContainerArea.nWidth + "  " + _cFile.stContainerArea.nHeight + "]");
                (new Logger()).WriteDebug2("video prepare [old area:" + stArea.nWidth + "  " + stArea.nHeight + "]");
				(new Logger()).WriteDebug2("video prepare [file area:" + _cFile.stArea.nWidth + "  " + _cFile.stArea.nHeight + "]");
				(new Logger()).WriteDebug2("video prepare [file orig w h:" + _cFile.nWidthOriginal + "  " + _cFile.nHeightOriginal + "]");

                FitIn eFitIn = FitIn.Crop; // типа из настроек пришло ))
                if (_cFile.nHeightOriginal != stArea.nHeight || _cFile.nWidthOriginal != stArea.nWidth || Preferences.bAnamorphic) // если видео не совпало с контейнером
				{
					if (stArea.nWidth == 1920 && stArea.nHeight == 1080) // container is HD
					{
						if (_cFile.nHeightOriginal == 576 && _cFile.nWidthOriginal == 720) // if video format is PAL = 768x576 in square pixels   рассмотрим частный случай, т.к. он восновном и будет только.
						{
							if (eFitIn == FitIn.Crop)
							{
								nK = (float)stArea.nWidth / 768; // коэффициент    (делаем правильно именно дл¤ пала с учетом аспекта пиксел¤ у пала)
								nNewSize = (ushort)Math.Round(nK * _cFile.nHeightOriginal); // нова¤ высота при раст¤гивании по ширине
								nHalfDelta = (short)Math.Round(((float)(stArea.nHeight - nNewSize) / 2));
								stArea = new Area(0, nHalfDelta, stArea.nWidth, nNewSize);
								//stArea = new Area(0, -180, 1920, 1440);  должно быть так, если правильно посчитано будет...
							}
						}
						else  // if video is not container's size and not PAL
						{
							if (eFitIn == FitIn.Crop)
							{
								if (_cFile.nWidthOriginal > _cFile.nHeightOriginal)
								{
									nK = (float)stArea.nWidth / _cFile.nWidthOriginal; // коэффициент
									nNewSize = (ushort)Math.Round(nK * _cFile.nHeightOriginal); // нова¤ высота при раст¤гивании по ширине
									nHalfDelta = (short)Math.Round((float)(stArea.nHeight - nNewSize) / 2);
									stArea = new Area(0, nHalfDelta, stArea.nWidth, nNewSize);
								}
								else
								{
									nK = (float)stArea.nHeight / _cFile.nHeightOriginal; // коэффициент
									nNewSize = (ushort)Math.Round(nK * _cFile.nWidthOriginal); // нова¤ высота при раст¤гивании по ширине
									nHalfDelta = (short)Math.Round((float)(stArea.nWidth - nNewSize) / 2);
									stArea = new Area(nHalfDelta, 0, nNewSize, stArea.nHeight);
								}
							}
						}
					}
					else if (stArea.nWidth == 720 && stArea.nHeight == 576) // container is PAL
					{
						if (Preferences.bAnamorphic)  // we should change normal PAL to anamorphic
						{
							if (_cFile.nWidthOriginal == 720 && _cFile.nHeightOriginal == 576 && _cFile.nAspectRatio_dividend == 15 && _cFile.nAspectRatio_divider == 16) // not anamorph video.  This is normal DV-PAL
							{
								stArea = new Area(0, -96, 720, 768);  //анаморф 45/64   720x576   = 1/1  720x405          
							}                                        // обычный 15/16   720x576   = 1/1  720x540    высота Y_new = (Y*15/16)*64/45
						}                                           //  1/0, т.е. если пиксели были квадратные, то Y_new = Y*64/45
					}
                }
				(new Logger()).WriteDebug2("video prepare [new file area:" + stArea.nWidth + "  " + stArea.nHeight + "]");

				_cFile.stArea = stArea;
                _cFile.Open(stMergingMethod, ePlaybackMode);
                if (!_cFile.bFilePrepared)
                    throw new Exception("file was not prepared correctly [" + sFile + "]");
                if (ulong.MaxValue == nDuration)
					nDuration = _cFile.nFramesTotal - nFrameStart;
				(new Logger()).WriteDebug2("video prepared [video_hc:" + nID + "][file_hc:" + _cFile.GetHashCode() + "]");
				base.Prepare();
			}
			catch (Exception ex)
			{
                (new Logger()).WriteError(ex);
                Fail();
				throw;
			}
        }
        override public void Start()
        {
            base.Start();
            (new Logger()).WriteDebug("video started [hc:" + nID + "][f:" + sFile + "]");
        }
        override public void Start(IContainer iContainer)
        {
            base.Start(iContainer);
            (new Logger()).WriteDebug("video started with container [hc:" + nID + "][f:" + sFile + "]");
        }
        override public void Stop()
        {
            lock (_oLock)
            {
                if (_bStopped)
                    return;
                _bStopped = true;
            }
            if (eStatus == EffectStatus.Preparing || eStatus == EffectStatus.Running)
                base.Stop();
			_cFile.Close();
            (new Logger()).WriteDebug("video stopped [hc:" + nID + "]");
        }

        override public PixelsMap FrameNextVideo()
        {
			cTimings.TotalRenew();
            if (null == _cFile.cPMDuo.Switch(nPixelsMapSyncIndex))
                return null;

            PixelsMap cRetVal = _cFile.FrameNextVideo();
			cTimings.Restart("file_nextframe");

            ulong nDuration = this.nDuration;
            nDuration = nDuration < _cFile.nFramesTotal ? nDuration : _cFile.nFramesTotal;
			if ((null == cRetVal && _cFile.bEOF) || (nFrameCurrentVideo >= nDuration && (nFrameCurrentAudio >= nDuration || nFrameCurrentAudio == 0)))
			{
				if (_cFile.bEOF && (nFrameCurrentVideo != nDuration || nFrameCurrentAudio != nDuration))
					(new Logger()).WriteWarning("video has been stopped abnormal [hc:" + nID + "] [duration: " + nDuration + "] [current_video: " + _cFile.nFrameCurrentVideo + "] [current_audio: " + _cFile.nFrameCurrentAudio + "][cRetVal=" + (cRetVal == null ? "null" : "NOT null") + "][EOF=" + _cFile.bEOF + "]");
				(new Logger()).WriteDebug3("FrameNextVideo(): video stopped [hc:" + nID + "]" + "[frames total:" + _cFile.nFramesTotal + "]" + "[nFrameCurrentVideo:" + _cFile.nFrameCurrentVideo + "]" + "[nFrameCurrentAudio:" + _cFile.nFrameCurrentAudio + "][cRetVal=" + (cRetVal == null ? "null" : "NOT null") + "][eof: " + _cFile.bEOF + "]");
				Stop();
			}
            (new Logger()).WriteDebug4("video returns a frame [hc:" + nID + "]");
			cTimings.Stop("video:framenext > 40ms", "eof_getting and dur_getting", 40);

            if (null != cRetVal)
            {
                cRetVal.nAlphaConstant = nCurrentOpacity;
                if (null != cMask)
                    cRetVal.eAlpha = cMask.eMaskType;
            }

            return cRetVal;
        }

        override public Bytes FrameNextAudio()
        {
            Bytes aRetVal = _cFile.FrameNextAudio();
            ulong nDuration = this.nDuration;
			nDuration = nDuration < _cFile.nFramesTotal ? nDuration : _cFile.nFramesTotal;
			if ((null == aRetVal && _cFile.bEOF) || (nFrameCurrentVideo >= nDuration && (nFrameCurrentAudio >= nDuration || nFrameCurrentVideo == 0)))
			{
				if (_cFile.bEOF && (nFrameCurrentVideo != nDuration || nFrameCurrentAudio != nDuration))
					(new Logger()).WriteWarning("video has been stopped abnormal - duration has not been reached [hc:" + nID + "] [duration: " + nDuration + "] [current_video: " + nFrameCurrentVideo + "] [current_audio: " + nFrameCurrentAudio + "][aRetVal=" + (aRetVal == null ? "null" : "NOT null") + "][eof: " + _cFile.bEOF + "]");
				(new Logger()).WriteDebug3("FrameNextAudio(): video stopped [hc:" + nID + "]" + "[frames total:" + _cFile.nFramesTotal + "]" + "[nFileCurrentVideo:" + _cFile.nFrameCurrentVideo + "]" + "[nFileCurrentAudio:" + _cFile.nFrameCurrentAudio + "][aRetVal=" + (aRetVal == null ? "null" : "NOT null") + "][eof: " + _cFile.bEOF + "]");
				Stop();
			}
			if (nCurrentLevel < 1)
				aRetVal = Transition.TransitionAudioFrame(aRetVal, Transition.TypeAudio.crossfade, 1 - nCurrentLevel);
			return aRetVal;
        }

		override public void SkipVideo()
		{
			base.SkipVideo();
			_cFile.SkipVideo();
		}
		override public void SkipAudio()
		{
			base.SkipAudio();
			_cFile.SkipAudio();
		}
	}
}
