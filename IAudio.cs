using System;
using System.Collections.Generic;
using System.Text;

using helpers;

namespace BTL
{
    public interface IAudio
    {
		/// привязка по каналам... каналы начинаются с 0... 
		/// обычное стерео: new byte[]{0,1};
		/// т.е. первый канал эффекта(aChannels[0]) идет на первый канал девайса(0), второй канал эффекта(aChannels[1]) идет на второй канал девайса(1)
		/// кроме того, у нас автоматически есть специальные значения:
		///		1. aChannels==null - значение по умолчанию... все каналы эффекта идут на соответствующие каналы девайса. кол-во каналов можно узнать разделив длину буфера семплов (в байтах) для одного кадра на (Preferences.nAudioBytesPerFramePerChannel)
		///		2. aChannels.Length==0 - mute (что логично)
		byte[] aChannels { get; set; }

		byte[] FrameNext();
		void Skip();
	}
}
