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
		static private Preferences _cInstance = new Preferences();
		static public MergingMethod stMerging
        {
			get
			{
				return _cInstance._stMerging;
			}
		}
        static public byte nDeviceTarget
		{
			get
			{
				return _cInstance._nDeviceTarget;
			}
		}
        static public byte nTargetChannel
        {
            get
            {
                return _cInstance._nTargetChannel;
            }
        }
        static public string sDeviceMake
        {
            get
            {
                return _cInstance._sDeviceMake;
            }
        }
        static public ushort nFPS
        {
            get
            {
                if (_cInstance._nFPS == 0)
                    throw new Exception("fps is 0. must be greater.");
                return _cInstance._nFPS;
            }
            set
            {
                _cInstance._nFPS = value; // info from board
            }
        }
        static public int nFrameDuration
        {
            get
            {
                if (_cInstance._nFrameDuration == 0)
                {
                    _cInstance._nFrameDuration = 1000 / nFPS;
                }
                return _cInstance._nFrameDuration;
            }
        }
        static public int nGCFramesInterval
		{
			get
			{
				return _cInstance._nGCFramesInterval;
			}
		}

		static public TimeSpan tsNextFrameTimeout
		{
			get
			{
				return _cInstance._tsNextFrameTimeout;
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
        static public byte nAudioChannelsQtyFfmpeg
        {
            get
            {
                return _cInstance._nAudioChannelsQtyFfmpeg;
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
        static public byte nAudioByteDepthToSend
        {
            get
            {
                return _cInstance._nAudioByteDepthToSend;
            }
        }

        static public uint nAudioSamplesPerFrame
        {
            get
            {
                if (_cInstance._nAudioSamplesPerFrame == 0)
                {
                    _cInstance._nAudioSamplesPerFrame = nAudioSamplesRate / nFPS;
                }
                return _cInstance._nAudioSamplesPerFrame;
            }
        }
        static public uint nAudioBytesPerFrame
        {
            get
            {
                if (_cInstance._nAudioBytesPerFrame == 0)
                {
                    _cInstance._nAudioBytesPerFrame = nAudioSamplesPerFrame * nAudioBytesPerSample;
                }
                return _cInstance._nAudioBytesPerFrame;
            }
        }
        static public uint nAudioBytesPerFramePerChannel
        {
            get
            {
                if (_cInstance._nAudioBytesPerFramePerChannel == 0)
                {
                    _cInstance._nAudioBytesPerFramePerChannel = (nAudioSamplesRate * nAudioByteDepth) / nFPS; ;
                }
                return _cInstance._nAudioBytesPerFramePerChannel;
            }
        }
        static public bool bVideo
		{
			get
			{
				return _cInstance._bVideo;
			}
		}
		static public bool bAnamorphic
		{
			get
			{
				return _cInstance._bAnamorph;
			}
		}
        static public bool bBackgroundAlpha
        {
            get
            {
                return _cInstance._bBackgroundAlpha;
            }
        }
        static public byte nClockBiasInFrames
		{
			get
			{
				return _cInstance._nClockBiasInFrames;
			}
		}
		//static public byte nQueueBaetylusLength
		//{
		//	get
		//	{
		//		return _cInstance._nQueueBaetylusLength;
		//		//#if SCR || PROMPTER
		//		//                return 5;
		//		//#else
		//		//                return 25;
		//		//#endif
		//	}
		//}
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
		static public long nQueuePacketsLength
		{
			get
			{
				return _cInstance._nQueuePacketsLength;
			}
		}
		static public byte nDecodingThreads
		{
			get
			{
				return _cInstance._nDecodingThreads;
			}
		}
		static public bool bClearScreenOnEmpty
		{
			get
			{
				return _cInstance._bClearScreenOnEmpty;
			}
		}
		static public string sDebugFolder
		{
			get
			{
				return _cInstance._sDebugFolder;
			}
		}

        static public void Reload()
        {
            _cInstance = new Preferences();
        }

        private MergingMethod _stMerging;
        private byte _nDeviceTarget;
        private string _sDeviceMake;
        private byte _nTargetChannel;
        private ushort _nFPS;
		private int _nFrameDuration;
		private int _nGCFramesInterval;

		private TimeSpan _tsNextFrameTimeout;
		private bool _bAudio;
		private uint _nAudioSamplesRate;
		private byte _nAudioChannelsQty;
        private byte _nAudioChannelsQtyFfmpeg;
        private byte _nAudioBitDepth;
		private byte _nAudioByteDepth;
		private byte _nAudioBytesPerSample;
        private byte _nAudioBitDepthToSend;
        private byte _nAudioByteDepthToSend;
        private uint _nAudioSamplesPerFrame;
		private uint _nAudioBytesPerFrame;
		private uint _nAudioBytesPerFramePerChannel;

		private bool _bVideo;
		private bool _bAnamorph;
        private bool _bBackgroundAlpha;

        private byte _nClockBiasInFrames;
		//private byte _nQueueBaetylusLength;
		private ushort _nQueueFfmpegLength;
		private byte _nQueueAnimationLength;
		private long _nQueuePacketsLength;
		private byte _nDecodingThreads;
		private string _sDebugFolder;

		private bool _bClearScreenOnEmpty;

		public Preferences()
			: base("//btl")
		{
		}
		override protected void LoadXML(XmlNode cXmlNode)
		{
			if (null == cXmlNode)  // || _bInitialized
				return;

            _stMerging = new MergingMethod(cXmlNode);
            _bClearScreenOnEmpty = cXmlNode.AttributeOrDefaultGet<bool>("cls", false);
			_sDebugFolder= cXmlNode.AttributeOrDefaultGet<string>("debug_folder", "");
			XmlNode cNodeChild;
			XmlNode cNodeDevice = cXmlNode.NodeGet("device");
            _nDeviceTarget = cNodeDevice.AttributeGet<byte>("target");
            _sDeviceMake = cNodeDevice.AttributeValueGet("make");
            if (_sDeviceMake == "aja")
            {
                _nTargetChannel = cNodeDevice.AttributeGet<byte>("target_channel");
            }

            if (_bAudio = (null != (cNodeChild = cNodeDevice.NodeGet("audio", false))))
            {
                _nAudioSamplesRate = cNodeChild.AttributeGet<uint>("rate");
                _nAudioChannelsQty = cNodeChild.AttributeGet<byte>("channels");
                _nAudioChannelsQtyFfmpeg = cNodeChild.AttributeGet<byte>("channels_ffmpeg");
                _nAudioBitDepth = cNodeChild.AttributeGet<byte>("bits");
                _nAudioByteDepth = (byte)(_nAudioBitDepth / 8);
                _nAudioBytesPerSample = (byte)(_nAudioByteDepth * _nAudioChannelsQty);
                _nAudioBitDepthToSend = cNodeChild.AttributeOrDefaultGet<byte>("bits_send", _nAudioByteDepth);
                _nAudioByteDepthToSend = (byte)(_nAudioBitDepthToSend / 8);
            }
            if (_bVideo = (null != (cNodeChild = cNodeDevice.NodeGet("video", false))))
			{
				_bAnamorph = cNodeChild.AttributeOrDefaultGet<bool>("anamorph", false);
                _bBackgroundAlpha = cNodeChild.AttributeOrDefaultGet<bool>("alpha", false);
            }

            cNodeChild = cNodeDevice.NodeGet("queue");
            _nClockBiasInFrames = cNodeChild.AttributeGet<byte>("clock_bias");
            //_nQueueBaetylusLength = cNodeChild.AttributeGet<byte>("btl");
			_nQueueFfmpegLength = cNodeChild.AttributeGet<ushort>("ffmpeg");
			_nQueueAnimationLength = cNodeChild.AttributeGet<byte>("animation");
			_nQueuePacketsLength = cNodeChild.AttributeGet<long>("packets");  // bytes qty  200 000 000
			_nDecodingThreads = cNodeChild.AttributeGet<byte>("threads");
			_tsNextFrameTimeout = cNodeChild.AttributeGet<TimeSpan>("frame_timeout");
            if (_tsNextFrameTimeout.TotalSeconds<=0)
                (new Logger()).WriteWarning("btl.pref: [timeout<=0 " + _tsNextFrameTimeout.TotalSeconds + " s]");
            _nGCFramesInterval = cNodeChild.AttributeGet<int>("gc_interval");
		}
	}
}
