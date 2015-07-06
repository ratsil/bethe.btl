using System;
using System.Collections.Generic;
using System.Text;

using helpers;

namespace BTL
{
    public interface IAudio
    {
		/// �������� �� �������... ������ ���������� � 0... 
		/// ������� ������: new byte[]{0,1};
		/// �.�. ������ ����� �������(aChannels[0]) ���� �� ������ ����� �������(0), ������ ����� �������(aChannels[1]) ���� �� ������ ����� �������(1)
		/// ����� ����, � ��� ������������� ���� ����������� ��������:
		///		1. aChannels==null - �������� �� ���������... ��� ������ ������� ���� �� ��������������� ������ �������. ���-�� ������� ����� ������ �������� ����� ������ ������� (� ������) ��� ������ ����� �� (Preferences.nAudioBytesPerFramePerChannel)
		///		2. aChannels.Length==0 - mute (��� �������)
		byte[] aChannels { get; set; }

		byte[] FrameNext();
		void Skip();
	}
}
