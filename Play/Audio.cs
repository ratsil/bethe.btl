using System;
using System.Collections.Generic;
using System.Text;

using helpers;

namespace BTL.Play
{
	public class Audio : EffectAudio
	{
		private class File
		{
			private string _sFile;

			private ffmpeg.net.File.Input _cFile;
			private ffmpeg.net.Format.Audio _cFormat;
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
			public ulong nFrameCurrent { get; private set; }
			public byte[] aChannels { get; set; }

			private File()
			{
				_sFile = null;
				nFramesTotal = ulong.MaxValue;
				nFrameCurrent = 0;
				aChannels = null; //комментарии в IAudio
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
					Close();
				}
				catch (Exception ex)
				{
					(new Logger()).WriteError(ex);
				}
			}

			public void Open()
			{
				_cFile = new ffmpeg.net.File.Input(_sFile, nFrameStart);
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
				_cFormat = new ffmpeg.net.Format.Audio((int)Preferences.nAudioSamplesRate, nAudioChannelsQty, eAVSampleFormat);
				nFramesTotal = _cFile.nFramesQty;
				//_cFile.Prepare(nDuration);
				_cFile.Prepare(null, _cFormat);
			}

			//audio test      private
			public void Close()
			{
				_cFile.Dispose();
			}
			public void Reset()
			{
				nFramesTotal = ulong.MaxValue;
				nFrameCurrent = 0;
				Close();
			}

			public byte[] FrameNext()
			{
				byte[] aRetVal = null;
				ffmpeg.net.Frame cFrame = _cFile.FrameNextAudioGet();
				if (null != cFrame)
				{
					aRetVal = cFrame.aBytes;
					cFrame.Dispose();
				}
				if (null != aRetVal && 0 < aRetVal.Length)
					nFrameCurrent++;
				if (bEOF && null == cFrame)
					return null;
				return aRetVal;
			}

			public ulong nDuration { get; set; }
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
				_cFile.nFrameStart = value;
				base.nFrameStart = value;
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
				if (null != _cFile)
					_cFile.nDuration = value;
			}
		}
		override public ulong nFrameCurrent
		{
			get
			{
				return _cFile.nFrameCurrent;
			}
		}
		public bool? bFileInMemory   // не знаю как бы это сделать нормально...
		{
			get
			{
				if (null != _cFile)
					return _cFile.bFileInMemory;
				else
					return null;
			}
		}

		private Audio()
			: base(EffectType.Audio)
		{
			try
			{
				_cFile = null;
			}
			catch
			{
				Fail();
				throw;
			}
		}
		public Audio(string sFile)
			: this()
		{
			try
			{
				_cFile = new File(sFile);
				_cFile.aChannels = aChannels;
			}
			catch
			{
				Fail();
				throw;
			}
		}
		~Audio()
		{
		}

		override public void Idle()
		{
			base.Idle();
			_cFile.Reset();
		}
		override public void Prepare()
		{
			try
			{
				_cFile.Open();
				(new Logger()).WriteDebug3("audio prepared [hc:" + GetHashCode() + "]");
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
			(new Logger()).WriteDebug3("audio started [hc:" + GetHashCode() + "]");
		}
		override public void Stop()
		{
			base.Stop();
			(new Logger()).WriteDebug3("audio stopped [hc:" + GetHashCode() + "]");
		}

		override public byte[] FrameNext()
		{
			byte[] aRetVal = _cFile.FrameNext();
			ulong nDuration = this.nDuration;
			nDuration = nDuration < _cFile.nFramesTotal ? nDuration : _cFile.nFramesTotal;
			if ((null == aRetVal && _cFile.bEOF) || ((_cFile.nFrameCurrent == nDuration)))
			{
				if (_cFile.bEOF && _cFile.nFrameCurrent != nDuration)
					(new Logger()).WriteError(new Exception("audio has been stopped abnormal - duration has not been reached [hc:" + GetHashCode() + "] [duration: " + nDuration + "][cf: " + _cFile.nFrameCurrent + "]"));
				Stop();
			}
			return aRetVal;
		}


		//audio test
		public override void Dispose()
		{
			base.Dispose();
			_cFile.Close();
		}
	}
}
