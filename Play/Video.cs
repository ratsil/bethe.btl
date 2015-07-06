using System;
using System.Collections.Generic;
using System.Text;

using helpers;

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
			byte[] _aAudioMap;
            private bool _bClosed;
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
				ffmpeg.net.File.Input.nCacheSize = Preferences.nQueueFfmpegLength;
				nFramesTotal = ulong.MaxValue;
                nFrameCurrentVideo = 0;
                nFrameCurrentAudio = 0;
				aAudioChannels = null; //комментарии в IAudio
            }

            public File(string sFile)
                : this()
            {
                if (null == sFile || !System.IO.File.Exists(sFile))
                    throw new Exception("no such file! " + sFile);
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

            public void Open(bool bCUDA)
            {
				// TODO  нужно выцепл€ть размеры кадра из видео.
				_bClosed = false;
                if (null == _cFile)
				{
					_cFile = new ffmpeg.net.File.Input(_sFile, nFrameStart);
				}
				(new Logger()).WriteDebug("[video_file " + GetHashCode() + "][ffmpeg_file " + _cFile.GetHashCode() + "]");
				Area stDeviceArea = Baetylus.Helper.cBoard.stArea;
                _cFormatVideo = new ffmpeg.net.Format.Video(stArea.nWidth, stArea.nHeight, ffmpeg.net.PixelFormat.AV_PIX_FMT_BGRA);

				_cPixelsMap = new PixelsMap(bCUDA, stArea, PixelsMap.Format.BGRA32);
                _cPixelsMap.bBackgroundClear = true;
                _cPixelsMap.bKeepAlive = true;
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
				int nAudioChannelsQty = Preferences.nAudioChannelsQty;
				switch (Preferences.nAudioChannelsQty)
				{
					case 8:
						nAudioChannelsQty = 2;
						break;
				}
				_cFormatAudio = new ffmpeg.net.Format.Audio((int)Preferences.nAudioSamplesRate, nAudioChannelsQty, eAVSampleFormat);
                nFramesTotal = _cFile.nFramesQty;
				//_cFile.Prepare(nDuration);
                _cFile.Prepare(_cFormatVideo, _cFormatAudio);
            }
			public void Close()
			{
				if (_bClosed)
				{
					(new Logger()).WriteDebug3("in - already closed " + GetHashCode());
					return;
				}
				_bClosed = true;
				(new Logger()).WriteDebug3("in " + GetHashCode());
				Logger.Timings cTimings = new Logger.Timings("video:file:");
				if (null != _cPixelsMap)
					Baetylus.PixelsMapDispose(_cPixelsMap, true);

				_aAudioMap = null;

				cTimings.Restart("pm disposing");

				new System.Threading.Thread(() =>
				{
					System.Threading.Thread.CurrentThread.IsBackground = true;
					if (null != _cFile)
						_cFile.Dispose();
				}).Start();

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
				Logger.Timings cTimings = new Logger.Timings("btl:video:file");
				try
				{
					_cPixelsMap.Move(stArea.nLeft, stArea.nTop);  // были проблемы в транзишене. т.к. PM многоразовый, то кто-то его мог мувнуть (плейлист) и на место не класть. 
					ffmpeg.net.Frame cFrame = _cFile.FrameNextVideoGet();   //_cFormatVideo, 
					cTimings.Restart("ffmpeg_framenext");  
					if (null != cFrame)
					{
						int nOffset = 0;
						if (720 == _cFormatVideo.nWidth && 576 == _cFormatVideo.nHeight)  // инверси€ полей нужна только на sd
							nOffset = _cFormatVideo.nWidth * _cFormatVideo.nBitsPerPixel / 8;
						_cPixelsMap.CopyIn(cFrame.pBytes + nOffset, cFrame.nLength - nOffset);  // сдвигаем фрейм на строчку вверх (это если не HD, а PAL)
						cTimings.Restart("copy_in");
						cFrame.Dispose();
						cTimings.Restart("dispose");
						//GC.Collect(1, GCCollectionMode.Forced);  // худший вариант был
						GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized);  // этот лучший был
						//GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced);   // гипотеза (“ј  и оказалось!!!!) - т.к. сборка спонтанна€ высвобождает сразу много мусора и и лочит этим copyin и вышибает пробки у Ѕлэкћэджика
						cTimings.Restart("GCcollect");
						nFrameCurrentVideo++;
					}
					else if (bEOF)
						return null;
					cTimings.Stop("framenext > 20ms","eof_getting", 20);
					return _cPixelsMap;
				}
				catch (Exception ex)
				{
					(new Logger()).WriteError(ex);
				}
                return null;
            }
            public byte[] FrameNextAudio()
            {
				byte[] aRetVal = null;
				try
				{
					ffmpeg.net.Frame cFrame = _cFile.FrameNextAudioGet();  //_cFormatAudio
					if (null != cFrame)
					{
						aRetVal = cFrame.aBytes;
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
        override public ulong nDuration
        {
            get
            {
                return base.nDuration;
            }
            set
            {
                ulong nFT = nFramesTotal;
                if (value > nFT)
                    value = nFT;
                base.nDuration = value;
            }
        }
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
		public bool? bFileInMemory   // не знаю как бы это сделать нормально... //EMERGENCY:l а это еще актуально?
		{
			get
			{
				if (null != _cFile)
					return _cFile.bFileInMemory;
				else
					return null;
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
        public Dock cDock
        {
            set
            {
                stArea = stArea.Dock(stArea, value);
            }
        }

        protected Video()
            : base(EffectType.Video)
        {
            try
            {
				(new Logger()).WriteDebug3("in");
                _cFile = null;
                stArea = Baetylus.Helper.cBaetylus.cBoard.stArea;
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
        ~Video()
        {
        }

		protected void Init(string sFile)
		{
			_cFile = new File(sFile);
			_cFile.aAudioChannels = aChannelsAudio;
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

				(new Logger()).WriteDebug2("video prepare [old area:" + stArea.nWidth + "  " + stArea.nHeight + "]");
				(new Logger()).WriteDebug2("video prepare [file area:" + _cFile.stArea.nWidth + "  " + _cFile.stArea.nHeight + "]");
				(new Logger()).WriteDebug2("video prepare [file w h:" + _cFile.nWidthOriginal + "  " + _cFile.nHeightOriginal + "]");
				//DNF
				FitIn eFitIn = FitIn.Crop; // типа из настроек пришло ))
				if (_cFile.nHeightOriginal != stArea.nHeight || _cFile.nWidthOriginal != stArea.nWidth) // если видео не совпало с контейнером
				{
					if (_cFile.nHeightOriginal == 576 && _cFile.nWidthOriginal == 720) // if video format is PAL = 768x576 in square pixels   рассмотрим частный случай, т.к. он восновном и будет только.
					{
						if (eFitIn == FitIn.Crop)
						{
							nK = (float)stArea.nWidth / 768; // коэффициент    (делаем правильно именно дл€ пала с учетом аспекта пиксел€ у пала)
							nNewSize = (ushort)Math.Round(nK * _cFile.nHeightOriginal); // нова€ высота при раст€гивании по ширине
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
								nNewSize = (ushort)Math.Round(nK * _cFile.nHeightOriginal); // нова€ высота при раст€гивании по ширине
								nHalfDelta = (short)Math.Round((float)(stArea.nHeight - nNewSize) / 2);
								stArea = new Area(0, nHalfDelta, stArea.nWidth, nNewSize);
							}
							else
							{
								nK = (float)stArea.nHeight / _cFile.nHeightOriginal; // коэффициент
								nNewSize = (ushort)Math.Round(nK * _cFile.nWidthOriginal); // нова€ высота при раст€гивании по ширине
								nHalfDelta = (short)Math.Round((float)(stArea.nWidth - nNewSize) / 2);
								stArea = new Area(nHalfDelta, 0, nNewSize, stArea.nHeight);
							}
						}
					}
				}
				//DNF
				(new Logger()).WriteDebug2("video prepare [new area:" + stArea.nWidth + "  " + stArea.nHeight + "]");

				_cFile.stArea = stArea;
				_cFile.Open(bCUDA);
				if (ulong.MaxValue == nDuration)
					nDuration = _cFile.nFramesTotal - nFrameStart;
				(new Logger()).WriteDebug2("video prepared [video_hc:" + GetHashCode() + "][file_hc:" + _cFile.GetHashCode() + "]");
				base.Prepare();
			}
			catch
			{
				Fail();
				throw;
			}
        }
        override public void Start()
        {
            base.Start();
            (new Logger()).WriteDebug("video started [hc:" + GetHashCode() + "]");
        }
        override public void Stop()
        {
			if (eStatus!= EffectStatus.Stopped)
				base.Stop();
			_cFile.Close();
            (new Logger()).WriteDebug("video stopped [hc:" + GetHashCode() + "]");
        }

        override public PixelsMap FrameNextVideo()
        {
			Logger.Timings cTimings = new Logger.Timings("btl:video"); 
            PixelsMap cRetVal = _cFile.FrameNextVideo();
			cTimings.Restart("file_nextframe");

            ulong nDuration = this.nDuration;
            nDuration = nDuration < _cFile.nFramesTotal ? nDuration : _cFile.nFramesTotal;
			if ((null == cRetVal && _cFile.bEOF) || (nFrameCurrentVideo >= nDuration && (nFrameCurrentAudio >= nDuration || nFrameCurrentAudio == 0)))
			{
				if (_cFile.bEOF && (nFrameCurrentVideo != nDuration || nFrameCurrentAudio != nDuration))
					(new Logger()).WriteWarning("video has been stopped abnormal [hc:" + GetHashCode() + "] [duration: " + nDuration + "] [current_video: " + _cFile.nFrameCurrentVideo + "] [current_audio: " + _cFile.nFrameCurrentAudio + "]");
				(new Logger()).WriteDebug3("FrameNextVideo(): video stopped [hc:" + GetHashCode() + "]" + "[frames total:" + _cFile.nFramesTotal + "]" + "[nFrameCurrentVideo:" + _cFile.nFrameCurrentVideo + "]" + "[nFrameCurrentAudio:" + _cFile.nFrameCurrentAudio + "][eof: " + _cFile.bEOF + "]");
				Stop();
			}
            (new Logger()).WriteDebug4("video returns a frame [hc:" + GetHashCode() + "]");
			cTimings.Stop("video:framenext > 40ms", "eof_getting and dur_getting", 40);
            return cRetVal;
        }

        override public byte[] FrameNextAudio()
        {
            byte[] aRetVal = _cFile.FrameNextAudio();
            ulong nDuration = this.nDuration;
			nDuration = nDuration < _cFile.nFramesTotal ? nDuration : _cFile.nFramesTotal;
			if ((null == aRetVal && _cFile.bEOF) || (nFrameCurrentVideo >= nDuration && (nFrameCurrentAudio >= nDuration || nFrameCurrentVideo == 0)))
			{
				if (_cFile.bEOF && (nFrameCurrentVideo != nDuration || nFrameCurrentAudio != nDuration))
					(new Logger()).WriteWarning("video has been stopped abnormal - duration has not been reached [hc:" + GetHashCode() + "] [duration: " + nDuration + "] [current_video: " + nFrameCurrentVideo + "] [current_audio: " + nFrameCurrentAudio + "]");
				(new Logger()).WriteDebug3("FrameNextAudio(): video stopped [hc:" + GetHashCode() + "]" + "[frames total:" + _cFile.nFramesTotal + "]" + "[nFrameCurrentVideo:" + _cFile.nFrameCurrentVideo + "]" + "[nFrameCurrentAudio:" + _cFile.nFrameCurrentAudio + "][eof: " + _cFile.bEOF + "]");
				Stop();
			}
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
