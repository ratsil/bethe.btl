using System;
using System.Collections.Generic;
using System.Text;

namespace BTL
{
	public delegate void AVFrameArrivedCallback(int nBytesVideoQty, IntPtr pBytesVideo, int nBytesAudioQty, IntPtr pBytesAudio);
	public delegate Device.Frame NextFrameCallback();
	public interface IDevice
    {
		event AVFrameArrivedCallback AVFrameArrived;

		event NextFrameCallback NextVideoFrame;

		void TurnOn();
		Device.Frame.Video FrameBufferGet();
	}
}
