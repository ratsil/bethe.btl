using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Xml;
using helpers;
using helpers.extensions;

namespace BTL
{
	public class Preferences : helpers.Preferences
	{
		public class DownStreamKeyer
		{
			public byte nLevel;
			public bool bInternal;

			public DownStreamKeyer()
			{
				nLevel = 255;
				bInternal = true;
			}
		}
		static private Preferences _cInstance = new Preferences();

		static public bool bCUDA
		{
			get
			{
				return _cInstance._bCUDA;
			}
		}

		static public byte nDeviceTarget
		{
			get
			{
				return _cInstance._nDeviceTarget;
			}
		}
		static public bool bDeviceInput
		{
			get
			{
				return _cInstance._bDeviceInput;
			}
		}
		static public ushort nFPS
		{
			get
			{
				return _cInstance._nFPS;
			}
		}
		static public int nFrameDuration
		{
			get
			{
				return _cInstance._nFrameDuration;
			}
		}


		static public bool bAudio
		{
			get
			{
				return _cInstance._bAudio;
			}
		}
		static public uint nAudioSamplesRate
		{
			get
			{
				return _cInstance._nAudioSamplesRate;
			}
		}
		static public byte nAudioChannelsQty
		{
			get
			{
				return _cInstance._nAudioChannelsQty;
			}
		}
		static public byte nAudioBitDepth
		{
			get
			{
				return _cInstance._nAudioBitDepth;
			}
		}
		static public byte nAudioByteDepth
		{
			get
			{
				return _cInstance._nAudioByteDepth;
			}
		}
		static public byte nAudioBytesPerSample
		{
			get
			{
				return _cInstance._nAudioBytesPerSample;
			}
		}
		static public uint nAudioSamplesPerFrame
		{
			get
			{
				return _cInstance._nAudioSamplesPerFrame;
			}
		}
		static public uint nAudioBytesPerFrame
		{
			get
			{
				return _cInstance._nAudioBytesPerFrame;
			}
		}
		static public uint nAudioBytesPerFramePerChannel
		{
			get
			{
				return _cInstance._nAudioBytesPerFramePerChannel;
			}
		}
		static public short nAudioVolumeChangeInDB
		{
			get
			{
				return _cInstance._nAudioVolumeChangeInDB;
			}
		}
		static public float nAudioVolumeChange
		{
			get
			{
				return _cInstance._nAudioVolumeChange;
			}
		}

		static public bool bVideo
		{
			get
			{
				return _cInstance._bVideo;
			}
		}
		static public string sVideoFormat
		{
			get
			{
				return _cInstance._sVideoFormat;
			}
		}
		static public DeckLinkAPI._BMDPixelFormat ePixelFormat
		{
			get
			{
				return _cInstance._ePixelFormat;
				//for input
				//return DeckLinkAPI._BMDPixelFormat.bmdFormat8BitYUV;
				//for output
				//return DeckLinkAPI._BMDPixelFormat.bmdFormat8BitBGRA;
			}
		}
		static public DownStreamKeyer cDownStreamKeyer
		{
			get
			{
				return _cInstance._cDownStreamKeyer;
			}
			set
			{
				_cInstance._cDownStreamKeyer = value;
			}
		}

		static public byte nQueueDeviceLength
		{
			get
			{
				return _cInstance._nQueueDeviceLength;
			}
		}
		static public byte nQueueBaetylusLength
		{
			get
			{
				return _cInstance._nQueueBaetylusLength;
				//#if SCR || PROMPTER
				//                return 5;
				//#else
				//                return 25;
				//#endif
			}
		}
		static public ushort nQueueFfmpegLength
		{
			get
			{
				return _cInstance._nQueueFfmpegLength;
			}
		}
		static public byte nQueueAnimationLength
		{
			get
			{
				return _cInstance._nQueueAnimationLength;
			}
		}

		static public byte nQueueBias   // экспериментальная величина. На сколько видео должно быть в буфере больше чем аудио, чтобы был синхрон.
		{
			get
			{
				return _cInstance._nQueueBias;
			}
		}
		static public byte nQueueBiasSlip   // экспериментальная величина. На сколько может эта разность отклонятся от намеченной
		{
			get
			{
				return _cInstance._nQueueBiasSlip;
//#if SCR || DEBUG || PROMPTER
//                return 4;
//#else
//                return 7;
//#endif
			}
		}
		static public bool bClearScreenOnEmpty
		{
			get
			{
				return _cInstance._bClearScreenOnEmpty;
			}
		}

		static public byte nQueueBiasControlDelay   // экспериментальная величина. Через какое время включится контроль за разностью.
		{
			get
			{
				return _cInstance._nQueueBiasControlDelay;
			}
		}

		private bool _bCUDA;

		private byte _nDeviceTarget;
		private bool _bDeviceInput;
		private ushort _nFPS;
		private int _nFrameDuration;

		private bool _bAudio;
		private uint _nAudioSamplesRate;
		private byte _nAudioChannelsQty;
		private byte _nAudioBitDepth;
		private byte _nAudioByteDepth;
		private byte _nAudioBytesPerSample;
		private uint _nAudioSamplesPerFrame;
		private uint _nAudioBytesPerFrame;
		private uint _nAudioBytesPerFramePerChannel;
		private short _nAudioVolumeChangeInDB;
		private float _nAudioVolumeChange;

		private bool _bVideo;
		private string _sVideoFormat;
		private DeckLinkAPI._BMDPixelFormat _ePixelFormat;
		private DownStreamKeyer _cDownStreamKeyer;

		private byte _nQueueDeviceLength;
		private byte _nQueueBaetylusLength;
		private byte _nQueueBias;
		private byte _nQueueBiasSlip;
		private byte _nQueueBiasControlDelay;
		private ushort _nQueueFfmpegLength;
		private byte _nQueueAnimationLength;

		private bool _bClearScreenOnEmpty;

		public Preferences()
			: base("//btl")
		{
		}
		override protected void LoadXML(XmlNode cXmlNode)
		{
            if (null == cXmlNode || _bInitialized)
				return;
			_bCUDA = cXmlNode.AttributeGet<bool>("cuda");
			_bClearScreenOnEmpty = cXmlNode.AttributeGet<bool>("cls", false);

			XmlNode cNodeChild;
			XmlNode cNodeDevice = cXmlNode.NodeGet("device");
            _nDeviceTarget = cNodeDevice.AttributeGet<byte>("target");
            _bDeviceInput = ("input" == cNodeDevice.AttributeValueGet("type"));
            _nFPS = cNodeDevice.AttributeGet<ushort>("fps");
			_nFrameDuration = 1000 / _nFPS;

			if (!_bDeviceInput)
			{
                if (_bAudio = (null != (cNodeChild = cNodeDevice.NodeGet("audio"))))
				{
                    _nAudioSamplesRate = cNodeChild.AttributeGet<uint>("rate");
                    _nAudioChannelsQty = cNodeChild.AttributeGet<byte>("channels");
                    _nAudioBitDepth = cNodeChild.AttributeGet<byte>("bits");
					_nAudioByteDepth = (byte)(_nAudioBitDepth / 8);
					_nAudioBytesPerSample = (byte)(_nAudioByteDepth * _nAudioChannelsQty);
					_nAudioSamplesPerFrame = _nAudioSamplesRate / _nFPS;
					_nAudioBytesPerFrame = _nAudioSamplesPerFrame * _nAudioBytesPerSample;
					_nAudioBytesPerFramePerChannel = (_nAudioSamplesRate * _nAudioByteDepth) / _nFPS;
					try
					{
                        _nAudioVolumeChangeInDB = cNodeChild.AttributeGet<short>("volume_change");
						_nAudioVolumeChange = (float)Math.Pow(10, (float)_nAudioVolumeChangeInDB / 20);
					}
					catch { }
				}
			}

            if (_bVideo = (null != (cNodeChild = cNodeDevice.NodeGet("video"))))
			{
                _sVideoFormat = cNodeChild.AttributeValueGet("format").ToLower();
                string sValue = cNodeChild.AttributeValueGet("pixels").ToLower();
				bool bFound = false;
				foreach(DeckLinkAPI._BMDPixelFormat ePF in Enum.GetValues(typeof(DeckLinkAPI._BMDPixelFormat)))
				{
					if(ePF.ToString().ToLower().Contains(sValue))
					{
						if (bFound)
						{
							bFound = false;
							break;
						}
						_ePixelFormat = ePF;
						bFound = true;
					}
				}
				if(!bFound)
					throw new Exception("указан некорректный формат пикселей [pixels:" + sValue + "][" + cNodeChild.Name + "]"); //TODO LANG
				if (!_bDeviceInput)
				{
					_cDownStreamKeyer = null;
                    if (null != (cNodeChild = cNodeChild.NodeGet("keyer", false)))
					{
						_cDownStreamKeyer = new DownStreamKeyer();
						try
						{
                            _cDownStreamKeyer.nLevel = cNodeChild.AttributeGet<byte>("level");
						}
						catch
						{
							throw new Exception("указан некорректный формат уровня DSK [level][" + cNodeChild.Name + "]"); //TODO LANG
						}
						try
						{
                            _cDownStreamKeyer.bInternal = ("internal" == cNodeChild.AttributeValueGet("type"));
						}
						catch
						{
							throw new Exception("указан некорректный тип DSK [type][" + cNodeChild.Name + "]"); //TODO LANG
						}
					}
				}
			}

            cNodeChild = cNodeDevice.NodeGet("queue");
            _nQueueDeviceLength = cNodeChild.AttributeGet<byte>("device");
            _nQueueBaetylusLength = cNodeChild.AttributeGet<byte>("btl");
			_nQueueFfmpegLength = cNodeChild.AttributeGet<ushort>("ffmpeg");
			_nQueueAnimationLength = cNodeChild.AttributeGet<byte>("animation");
            cNodeChild = cNodeChild.NodeGet("bias");
            _nQueueBiasSlip = cNodeChild.AttributeGet<byte>("slip");
            _nQueueBiasControlDelay = cNodeChild.AttributeGet<byte>("delay");
			_nQueueBias = cNodeChild.InnerText.ToByte();
		}
	}
}
