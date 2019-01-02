using System;
using System.Collections.Generic;
using System.Text;

using helpers;
using helpers.extensions;
using System.Xml;
using System.IO;
using System.IO.Compression;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Linq;

namespace BTL.Play
{
	public class Animation : EffectVideo
	{
		internal class File
		{
			internal enum Type
			{
				Unknown,
				PNG,
				JPG,
				BMP,
				JPEG
			}

			private string _sFolder;
			private Type _eType;
			private string[] _aFiles;
			private Dictionary<string, ZipArchiveEntry> _ahZipEntries;
			private ZipArchive _cZip;
            private Rectangle _stBitmapSize;
			private Bitmap _cBitmap;
			private BitmapData _cBitmapData;
			private List<Bytes> _cCache;
			private bool? _bIsDirectory;
			private bool bIsDirectory
			{
				get
				{
					if (null==_sFolder)
						throw new Exception("folder has not determined yet");
					if (null == _bIsDirectory)
						_bIsDirectory = IsDirectory(_sFolder);
					return _bIsDirectory.Value;
				}
			}
            private bool _bIsClosed;
			private bool _bThreadJoined;
			private bool? _bIsZipFolder;
            private bool bIsZipFolder
			{
				get
				{
					if (null == _sFolder)
						throw new Exception("folder has not determined yet");
					if (null == _bIsZipFolder)
						_bIsZipFolder = Path.GetExtension(_sFolder).ToLower() == ".zip";
					return _bIsZipFolder.Value;
                }
			}
			private System.Threading.Thread _cThreadFramesGettingWorker;
			private bool _bThAbort;
			private int nQueueSize;
			private int _nQueueLength;
			private int _nFirstNotNull;
            private int _nLastNotNull;
            private object _oWorkerLock;
			private object _oCloseLock;
			private object _oCacheLock;
            private Logger.Timings _cTimings;
            private Logger.Timings _cTimings2;
            private int _nAnimationLoopsQty;
            private int _nAnimationLoopsQtyAdaptive  // вместо 0 maxval.   юзаем только для очереди
            {
                get
                {
                    if (nAnimationKeepAlive)  // т.к. набираем в кэш только на первом лупе и потом закрываем очередь
                        return 1;
                    return _nAnimationLoopsQty == 0 ? int.MaxValue : _nAnimationLoopsQty;
                }
            }
            public bool bEOF { get; private set; }
            public ulong nFramesTotal { get; private set; }
            public ulong nFrameCurrent { get; private set; }
            public string sName
            {
                get
                {
                    return _sFolder;
                }
            }
            public Size stDimensions
            {
                get
                {
                    return _stBitmapSize.Size;
                }
            }
            public Size stDimensionsResized { get; set; }
            public int nQueueLength
			{
				get
				{
                    return _nQueueLength;
				}
			}
			public int nCacheCurrent
			{
				get
				{
					lock (_oCacheLock)
					{
						if (null != _cCache && _aFiles.Length <= _cCache.Count)
							return _cCache.Count;
						return _nQueueLength;
					}
                }
            }
            public int nAnimationLoopsQty
            {
                set
                {
                    _nAnimationLoopsQty = value;
                }
            }
            public int nAnimationLoopCurrent { get; set; }
            public bool nAnimationKeepAlive { get; set; }

            static File()
			{
				//_ahBytesStorage = new Dictionary<int, Queue<byte[]>>();
			}
			private File()
			{
				_oWorkerLock = new object();
				_oCloseLock = new object();
				_oCacheLock = new object();
				_sFolder = null;
				_aFiles = null;
				_bIsDirectory = null;
				nFrameCurrent = 0;
				nFramesTotal = 0;
				_stBitmapSize = Rectangle.Empty;
				stDimensionsResized = Size.Empty;
				bEOF = false;
				//_aCache = null;
				nQueueSize = Preferences.nQueueAnimationLength;
				_nQueueLength = -1;
				_bIsClosed = false;
				_bThreadJoined = false;
				_cTimings = new helpers.Logger.Timings("animation.file");
                _cTimings2 = new helpers.Logger.Timings("animation.file.worker");
                _bThAbort = false;
            }
			public File(string sFolder)
				: this()
			{
				if (sFolder.IsNullOrEmpty())
					throw new Exception("folder name can't be empty");
				_sFolder = sFolder;

				Type eType = GetSourceType();

				if (Type.Unknown == eType)
				{
					if (bIsDirectory && bIsZipFolder && !System.IO.File.Exists(sFolder))
						throw new Exception("can't find specified .zip folder [" + sFolder + "]");
					if (bIsDirectory && !Directory.Exists(sFolder))
						throw new Exception("can't find specified folder [" + sFolder + "]");
					if (!bIsDirectory && !System.IO.File.Exists(sFolder))
						throw new Exception("can't find specified file [" + sFolder + "]");
					throw new Exception("can't determine file type [" + sFolder + "]");
				}
				_eType = eType;
				Open();
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
			public void Dispose()
			{
				Close();
			}
			public void Reset()
			{
				bEOF = false;  //TODO можно плавно заполнять _aCache , что сэкономит время. Доделать
				Idle();  // т.е. можно сделать, что если bKeepAlive = true  то можно не в препаре все 1000 картинок всасывать, а через эту очередь плавно набирать.
			}
			private bool IsDirectory(string sFolder)
			{
				FileAttributes cFAttr = System.IO.File.GetAttributes(sFolder);
				if ((cFAttr & FileAttributes.Directory) == FileAttributes.Directory || Path.GetExtension(sFolder).ToLower() == ".zip")
					return true;
				else
					return false;
			}
			private Type StringToType(string sType)
			{
				Type eRetVal= Type.Unknown;
				switch (sType.ToLower())
				{
					case ".png":
						eRetVal = Type.PNG;
						break;
					case ".bmp":
						eRetVal = Type.BMP;
						break;
					case ".jpg":
						eRetVal = Type.JPG;
						break;
					case ".jpeg":
						eRetVal = Type.JPEG;
						break;
				}
				return eRetVal;
			}
			private string GetFirstFileInZip(string sZipFile)
			{
				if (!System.IO.File.Exists(_sFolder))
					return "";
				_cZip = new ZipArchive(System.IO.File.OpenRead(_sFolder), ZipArchiveMode.Read);
				foreach (ZipArchiveEntry cZAE in _cZip.Entries)
					if (!cZAE.Name.StartsWith("."))
					{
						(new Logger()).WriteDebug2("animation: file: GetFirstFileInZip: [file = " + cZAE.Name + "][folder = " + _sFolder + "]");
						return cZAE.Name;
					}
				return "";
			}
			private string GetFirstFileInFolder(string sFolder)
			{
				if (!Directory.Exists(_sFolder))
					return "";
				foreach (FileInfo cFI in new DirectoryInfo(_sFolder).GetFiles("*", SearchOption.TopDirectoryOnly))
					if (!cFI.Name.StartsWith("."))
						return cFI.FullName;
				return "";
			}

			private Type GetSourceType()
			{
				Type eRetVal = Type.Unknown;
				try
				{
					List<FileInfo> aFI = new List<FileInfo>();
					if (bIsDirectory)
					{
						if (bIsZipFolder)
						{
							 return StringToType(Path.GetExtension(GetFirstFileInZip(_sFolder)));
						}
						else
						{
							string sFName = GetFirstFileInFolder(_sFolder).ToLower();
                            string sExt = Path.GetExtension(sFName);
							if (".zip" == sExt)
							{
								_sFolder = sFName;
								_bIsDirectory = true;
								_bIsZipFolder = true;

								return StringToType(Path.GetExtension(GetFirstFileInZip(_sFolder)));
							}
							else
								return StringToType(sExt);
                        }
					}
					else
					{
						if (!System.IO.File.Exists(_sFolder))
							return Type.Unknown;
						else
							return StringToType((new FileInfo(_sFolder)).Extension);
					}
				}
				catch (Exception ex)
				{
					(new Logger()).WriteError(ex);
				}
				return eRetVal;
			}
			public void Open()
			{
				if (_aFiles != null)
					return;
				if (_eType == Type.Unknown)
					throw new Exception("Unexpected file type");
				if (_cZip==null)
					(new Logger()).WriteDebug2("animation: file: open: _cZip is null [folder = " + _sFolder + "]");
				if (bIsDirectory)
				{
					if (bIsZipFolder)
					{
						string sName;
						_ahZipEntries = new Dictionary<string, ZipArchiveEntry>();
						//cZip = new ZipArchive(System.IO.File.OpenRead(_sFolder), ZipArchiveMode.Read);
						foreach (ZipArchiveEntry cZAE in _cZip.Entries)
						{
							if (cZAE == null)
								(new Logger()).WriteDebug2("animation: file: open: cZAE is null [folder = " + _sFolder + "]");
							sName = cZAE.Name.ToLower();
							if (Path.GetExtension(sName) != "." + _eType.ToString().ToLower())
								continue;
							_ahZipEntries.Add(sName, cZAE);
						}
						_aFiles = _ahZipEntries.Keys.ToArray();
                    }
					else
					{
						_aFiles = Directory.GetFiles(_sFolder, "*." + _eType.ToString().ToLower(), SearchOption.TopDirectoryOnly);
					}
					Array.Sort(_aFiles);
				}
				else
					_aFiles = new string[1] { _sFolder };
				
				nFramesTotal = (ulong)_aFiles.Length;
				BitmapSizeSet(GetBitmap(_aFiles[0]), _aFiles[0]);
				return;
			}
			private Bitmap GetBitmap(string sFilename)
			{
				if (bIsZipFolder)
					return new Bitmap(_ahZipEntries[sFilename].Open());
				else
					return new Bitmap(sFilename);
			}
			private void ThreadJoin()
			{
				lock (_oWorkerLock)
				{
					if (_bThreadJoined)
						return;
					_bThreadJoined = true;
				}
				if (null != _cThreadFramesGettingWorker)
				{
					(new Logger()).WriteDebug("waiting for join: [folder = " + _sFolder + "][_bThAbort=" + _bThAbort + "][count=" + (null == _cCache ? "null" : "" + _cCache.Count) + "]");  //[worker state " + _cThreadFramesGettingWorker.ThreadState + "]
					if (!_bThAbort)
						_bThAbort = true;
					try
					{
						_cThreadFramesGettingWorker.Join();
					}
					catch// (Exception ex)
					{
						//(new Logger()).WriteError(ex);
					}
					_cThreadFramesGettingWorker = null;
					(new Logger()).WriteDebug("joined: [" + _sFolder + "]");
				}
			}
			private void CacheDispose()
			{
				lock (_oCacheLock)
				{
					if (null != _cCache)
					{
						for (int nI = 0; nI < _cCache.Count; nI++)
						{
							if (_cCache[nI] != null && _cCache[nI].Length > 0)
							{
								Baetylus._cBinM.BytesBack(_cCache[nI], 20);
								//_aCache[nI] = null;
							}
						}
						_cCache = null;
					}
				}
			}
			private void Close()
			{
				if (_oCloseLock == null)
					return;
				lock (_oCloseLock)
				{
					if (_bIsClosed)
						return;
					_bIsClosed = true;
				}
				(new Logger()).WriteDebug("animation: file: close: [folder = " + _sFolder + "]");
                try
                {
                    ThreadJoin();
                }
                catch (Exception ex)
                {
                    (new Logger()).WriteError(ex);
                }
                if (_cZip != null)
				{
					_ahZipEntries = null;
					_cZip.Dispose();
					_cZip = null;
				}
				if (_eType != Type.Unknown)
				{
					_aFiles = null;
					CacheDispose();
					return;
				}
				else
					throw new Exception("Unexpected file type");
			}
			private void BitmapSizeSet(Bitmap cBitmap, string sFile)
			{
				if (Rectangle.Empty == _stBitmapSize)
				{
					_stBitmapSize = new Rectangle(new Point(0, 0), cBitmap.Size);
					if (0 == _stBitmapSize.Height || 0 == _stBitmapSize.Width)
						(new Logger()).WriteWarning("animation: file: open: _stBitmapSize = 0  [file = " + sFile + "][folder = " + _sFolder + "]");
				}
			}
			private void BitmapDataSet(string sFile)
			{
				_cBitmap = GetBitmap(sFile);
				BitmapSizeSet(_cBitmap, sFile);
				Rectangle stNewSize = _stBitmapSize;
				if (stDimensionsResized != Size.Empty)
				{
					Bitmap cBMResized = new Bitmap(stDimensionsResized.Width, stDimensionsResized.Height);
					using (Graphics cG = Graphics.FromImage(cBMResized))
					{
						cG.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
						cG.DrawImage(_cBitmap, 0, 0, stDimensionsResized.Width, stDimensionsResized.Height);
					}
					_cBitmap = cBMResized;
					stNewSize = new Rectangle(0, 0, stDimensionsResized.Width, stDimensionsResized.Height);
				}
				_cBitmapData = _cBitmap.LockBits(stNewSize, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
			}
			public void Cache()
			{
				if (null != _cCache)
				{
					(new Logger()).WriteWarning("Cashe is not empty!!! Cache aborted");
					return;
				}
				_cCache = new List<Bytes>();
				try
				{
					if (null != _aFiles)
					{
						Bytes cBytes;
						for (int nIndx = 0; _aFiles.Length > nIndx; nIndx++)
						{
							BitmapDataSet(_aFiles[nIndx]);

							cBytes = Baetylus._cBinM.BytesGet(_cBitmapData.Stride * _cBitmapData.Height, 21);   //new byte[_cBitmapData.Stride * _cBitmapData.Height]
							Marshal.Copy(_cBitmapData.Scan0, cBytes.aBytes, 0, cBytes.Length);
							_cBitmap.UnlockBits(_cBitmapData);
							_cBitmap.Dispose();
							_cCache.Add(cBytes);
						}
					}
					else if (null == _aFiles)
					{
						throw new Exception("_aFiles is null in Animation.cs"); //UNDONE
					}
				}
				catch (Exception ex)
				{
					(new Logger()).WriteError(ex);
				}
			}
			public void DynamicQueueStart()
			{
				(new Logger()).WriteNotice("dynamic queue try to start: [" + _sFolder + "]");
				if (null != _cCache)
				{
					(new Logger()).WriteWarning("Cashe is not empty!!! DynamicQueueStart aborted");
					return;
				}
				lock (_oWorkerLock)
				{
					if (null != _cThreadFramesGettingWorker)
					{
						(new Logger()).WriteWarning("worker has already started!!! DynamicQueueStart aborted");
						return;
					}
					_bThAbort = false;
					_bThreadJoined = false;
					_cThreadFramesGettingWorker = new System.Threading.Thread(FramesGettingWorker);
					_cThreadFramesGettingWorker.IsBackground = true;
					_cThreadFramesGettingWorker.Priority = System.Threading.ThreadPriority.AboveNormal;
					_cThreadFramesGettingWorker.Start();

					while (nQueueSize > nQueueLength && (null == _cCache || _aFiles.Length > _cCache.Count))
					{
						if (nQueueLength > 5)  // быстрее освобождать prepare, как в ffmpeg
							break;
						System.Threading.Thread.Sleep(1);
					}
				}
			}
			private void FramesGettingWorker(object cState)
			{
                double nTotalMS = 0;
                int nTotalCount = 0;
                try
                {
					//Logger.Timings cTimings = new helpers.Logger.Timings("animation:FramesGettingWorker");
					(new Logger()).WriteNotice("dynamic queue started: [" + _sFolder + "]");
					_nQueueLength = 0;
					Bytes cBytes;
					_nFirstNotNull = 0;
                    _nLastNotNull = -1;
                    if (null != _cCache)
					{
						(new Logger()).WriteWarning("Cashe is not empty!!! FramesGettingWorker aborted");
						return;
					}
					_cCache = new List<Bytes>();

                    while (!_bThAbort && (_nLastNotNull < _aFiles.Length - 1 || nAnimationLoopCurrent < _nAnimationLoopsQtyAdaptive - 1))
                    {
                        TidyCache();

                        while (!_bThAbort && nQueueSize > _nQueueLength && (_nLastNotNull < _aFiles.Length - 1 || nAnimationLoopCurrent < _nAnimationLoopsQtyAdaptive - 1))
                        {
                            _cTimings2.TotalRenew();
                            _nLastNotNull = _nLastNotNull < _aFiles.Length - 1 ? _nLastNotNull + 1 : 0;
                            BitmapDataSet(_aFiles[_nLastNotNull]);
                            _cTimings2.Restart("1");

                            cBytes = Baetylus._cBinM.BytesGet(_cBitmapData.Stride * _cBitmapData.Height, 23);  //aBytes = new byte[_cBitmapData.Stride * _cBitmapData.Height];
                            _cTimings2.Restart("2");
                            Marshal.Copy(_cBitmapData.Scan0, cBytes.aBytes, 0, cBytes.Length);
                            _cBitmap.UnlockBits(_cBitmapData);
                            _cBitmap.Dispose();
                            _cTimings2.Restart("3");

//#if DEBUG //DNF
//                            if (DateTime.Now > new DateTime(2018, 11, 30, 15, 22, 40))
//                                System.Threading.Thread.Sleep(12300000);
//#endif

                            lock (_oCacheLock)
                            {
                                if (_nLastNotNull == _cCache.Count)
                                    _cCache.Add(cBytes);
                                else if (_nLastNotNull < _cCache.Count)
                                    _cCache[_nLastNotNull] = cBytes;
                                else
                                    throw new Exception("wrong _nLastNotNull value [lnn=" + _nLastNotNull + "][files=" + _aFiles.Length + "][cache=" + _cCache.Count + "]");
                                _nQueueLength++;
                            }
                            nTotalMS += _cTimings2.Stop("frames_get", "while [q=" + _nQueueLength + "]", 80);
                            nTotalCount++;
                            System.Threading.Thread.Sleep(0);
                        }
                        System.Threading.Thread.Sleep(2);
					}
					(new Logger()).WriteNotice("dynamic queue stopped: [" + _sFolder + "][cache_count=" + (null == _cCache ? "null" : "" + _cCache.Count) + "][queue=" + _nQueueLength + "]");
				}
				catch (Exception ex)
				{
					(new Logger()).WriteError(ex);
				}
				finally
				{
					_bThAbort = true;
                    _nQueueLength = -1; // т.к. от очереди уже не зависим и ждать её не надо
                    (new Logger()).WriteNotice("dynamic queue stopped finally: [" + _sFolder + "][per_frame = "+ nTotalMS/nTotalCount + "]");
				}
			}
            private void TidyCache()
            {
                if (nAnimationKeepAlive)
                    return;
                if (_nFirstNotNull > (int)nFrameCurrent)  // захлёст
                {
                    for (int ni = _nFirstNotNull; ni < (nFrameCurrent == 0 ? _cCache.Count - 1 : _cCache.Count); ni++)
                    {
                        TidyCacheByIndex(ni);
                        if (_bThAbort)
                            return;
                    }
                }
                for (int ni = _nFirstNotNull; ni < (int)nFrameCurrent - 1; ni++)  // без захлеста
                {
                    TidyCacheByIndex(ni);
                    if (_bThAbort)
                        return;
                }
            }
            private void TidyCacheByIndex(int nI)
            {
                lock (_oCacheLock)
                {
                    _nFirstNotNull = nI < _cCache.Count - 1 ? nI + 1 : 0;
                    Baetylus._cBinM.BytesBack(_cCache[nI], 22);
                    _cCache[nI] = null;
                }
            }

            public void Idle()
			{
				nFrameCurrent = 0;

				//ThreadJoin();
				//CacheDispose();
			}
			public void VideoFrameNext(PixelsMap cPixelsMap)
			{
				try
				{
					if (null == _aFiles)
						throw new Exception("_aFiles is null in Animation.cs"); //UNDONE
					if (nFrameCurrent < (ulong)_aFiles.Length)
					{
						if (null != cPixelsMap)
						{
							_cTimings.TotalRenew();
							if (null == _cCache)
							{
								BitmapDataSet(_aFiles[nFrameCurrent]);

								cPixelsMap.CopyIn(_cBitmapData.Scan0, _cBitmapData.Stride * _cBitmapData.Height);
								_cBitmap.UnlockBits(_cBitmapData);
								_cBitmap.Dispose();
								_cTimings.Restart("copyin1");
                            }
							else
							{
								if (0 <= nQueueLength)
								{
                                    if (0 == nQueueLength && 0 < (ulong)_aFiles.Length - nFrameCurrent)
                                        (new Logger()).WriteWarning("animation queue length is empty - will just return!![" + nQueueLength + "][total=" + nFramesTotal + "][cur="+ nFrameCurrent + "][" + _sFolder + "]");
                                    else if (3 > nQueueLength && 3 <= (ulong)_aFiles.Length - nFrameCurrent)
                                        (new Logger()).WriteNotice("animation queue length is less than 3! [" + nQueueLength + "][" + _sFolder + "]");
                                    else if ((Preferences.nQueueAnimationLength / 2 > nQueueLength) && (Preferences.nQueueAnimationLength / 2) <= _aFiles.Length - (int)nFrameCurrent)
                                        (new Logger()).WriteDebug3("animation queue length [" + nQueueLength + "][total=" + nFramesTotal + "][cur=" + nFrameCurrent + "][" + _sFolder + "]");
                                }
                                if (0 == nQueueLength || (int)nFrameCurrent >= _cCache.Count)
                                {
                                    bEOF = true;  
                                    return;
                                }

								lock (_oCacheLock)
								{
									cPixelsMap.CopyIn(_cCache[(int)nFrameCurrent].aBytes, false, true);
									if (-1 < _nQueueLength)
										_nQueueLength--;
								}
								_cTimings.Stop("frnext","lock and copyin2", 40);
							}
						}
						nFrameCurrent++;
					}
					else
						bEOF = true;
				}
				catch (Exception ex)
				{
					(new Logger()).WriteError("[" + _aFiles[nFrameCurrent] + "][" + _sFolder + "]", ex);
					bEOF = true;
				}
			}
		}


		private File _cFile;
		private PixelsMap _cPixelsMap;
        private PixelsMap.Triple _cPMDuo;
        private object _oLock;
		private bool _bDisposed;
		Logger.Timings _cTimings;

		override public ulong nFramesTotal
		{
			get
			{
				if (0 < nLoopsQty && null != _cFile)
					return nFramesPerLoopTotal * nLoopsQty;
				return ulong.MaxValue;
			}
		}
		private ulong nFrameCurrentPhysical;
		//override public ulong nFrameCurrent
		//{
		//	get
		//	{
		//		return (ulong)(nFrameCurrentPhysical + (nFramesPerLoopTotal * nLoopCurrent));
		//	}
		//}
		public bool bKeepAlive { get; set; }   // way to not use queue in separate thread; recomended for not long animation or looped animation
		public ushort nLoopsQty { get; set; }  //0 for looping forever
		public ushort nLoopCurrent { get; private set; }
		public bool bTurnOffQueue { get; set; }  // way to not use queue in separate thread; recomended for pre-rendered effects like roll
		public ulong nFramesPerLoopTotal
		{
			get
			{
				if (null == _cFile)
					return ulong.MaxValue;
				return _cFile.nFramesTotal;
			}
		}
		public float nPixelAspectRatio { get; set; } // аспект к которому желаем привести нашу нормальную картинку (с аспектом 1:1)
		public int nCacheCurrent
		{
			get
			{
				return _cFile.nCacheCurrent;
            }
		}
		public Size stFileDimensions
		{
			get
			{
				if (null == _cFile)
					return Size.Empty;
				return _cFile.stDimensions;
			}
		}
		public string sFolder
		{
			get
			{
				return _cFile.sName;
			}
		}
		internal Animation(EffectType eType)
			: base(eType)
		{
			try
			{
				nLoopsQty = 1;
				nLoopCurrent = 0;
				bKeepAlive = true;
				nPixelAspectRatio = 0;
				_cTimings = new helpers.Logger.Timings("animation");
				_bDisposed = false;
				_oLock = new object();
				bTurnOffQueue = false;
			}
			catch
			{
				Fail();
				throw;
			}
		}
		protected Animation()
			: this(EffectType.Animation)
		{ }
        public Animation(string sFolder)
            : this(sFolder, 1)
        { }
        public Animation(string sFolder, ushort nLoopsQty)
            : this(sFolder, nLoopsQty, false)
		{ }
		public Animation(string sFolder, bool bKeepAlive)
			: this(sFolder, 1, bKeepAlive)
		{ }
		public Animation(string sFolder, ushort nLoopsQty, bool bKeepAlive)
			: this()
		{
			try
			{
				this.bKeepAlive = bKeepAlive;
				this.nLoopsQty = nLoopsQty;
				Init(sFolder);
			}
			catch
			{
				Fail();
				throw;
			}
		}
		public Animation(XmlNode cXmlNode)
			: this()
		{
			try
			{
				LoadXML(cXmlNode);
				string sFolder = cXmlNode.AttributeValueGet("folder");
				bKeepAlive = cXmlNode.AttributeOrDefaultGet<bool>("keep_alive", false);
                bTurnOffQueue = cXmlNode.AttributeOrDefaultGet<bool>("turn_off_queue", false);
                nLoopsQty = cXmlNode.AttributeOrDefaultGet<ushort>(new string[] { "loop", "loops" }, 1);
                Init(sFolder);
			}
			catch
			{
				Fail();
				throw;
			}
		}
		~Animation()
		{
			try
			{
				Dispose();
			}
			catch (Exception ex)
			{
				(new Logger()).WriteError(ex);
			}
		}

		protected void Init(string sFolder)
		{
			_cFile = new File(sFolder);
			(new Logger()).WriteNotice("animation:init [hc=" + nID + "][name=" + _cFile.sName + "]");
			if (stArea.nWidth == 0 || stArea.nHeight == 0)
				stArea = new Area(stArea.nLeft, stArea.nTop, (ushort)stFileDimensions.Width, (ushort)stFileDimensions.Height);
		}
		private Area AreaResize(int nImageWidth, int nImageHeight, int nCanvasWidth, int nCanvasHeight, float nPixelAspectRatio)
		{
			Area stRetVal;
			nImageWidth = (int)Math.Round(((double)nImageWidth) * nPixelAspectRatio, 0);
			int nCrop;
			if (nImageWidth * nCanvasHeight > nImageHeight * nCanvasWidth)
			{
				nCrop = (int)Math.Round((double)(nImageHeight * nCanvasWidth) / (double)nImageWidth, 0);
				stRetVal = new Area((short)(0 + stArea.nLeft), (short)(stArea.nTop + Math.Round((double)(nCanvasHeight - nCrop) / 2, 0)), (ushort)nCanvasWidth, (ushort)nCrop);
				//_cFile.stDimensionsResized = new Size(nCanvasWidth, nCrop);
			}
			else
			{
				nCrop = (int)Math.Round((double)(nImageWidth * nCanvasHeight) / (double)nImageHeight, 0);
				stRetVal = new Area((short)(stArea.nLeft + (short)Math.Round((double)(nCanvasWidth - nCrop) / 2, 0)), (short)(stArea.nTop + 0), (ushort)nCrop, (ushort)nCanvasHeight);
				//_cFile.stDimensionsResized = new Size(nCrop, nCanvasHeight);
			}
			return stRetVal;
		}
		override public void Prepare()
		{
			try
			{
				if (EffectStatus.Idle != ((IEffect)this).eStatus)
					return;

                _cFile.nAnimationLoopsQty = nLoopsQty;
                _cFile.nAnimationLoopCurrent = 0;
                _cFile.nAnimationKeepAlive = bKeepAlive;
                if (Area.stEmpty == stArea || stArea.nWidth == 0 || stArea.nWidth == ushort.MaxValue || stArea.nHeight == 0 || stArea.nHeight == ushort.MaxValue)
					stArea = new Area(stArea.nLeft, stArea.nTop, (ushort)stFileDimensions.Width, (ushort)stFileDimensions.Height);

                if (nPixelAspectRatio == 0 && (stArea.nWidth != stFileDimensions.Width || stArea.nHeight != stFileDimensions.Height))
                    nPixelAspectRatio = 1;

                if (nPixelAspectRatio > 0) // && 1 != nPixelAspectRatio
                {
					stArea = AreaResize(stFileDimensions.Width, stFileDimensions.Height, stArea.nWidth, stArea.nHeight, nPixelAspectRatio);
					_cFile.stDimensionsResized = new Size(stArea.nWidth, stArea.nHeight);
				}
				stArea = stArea.Dock(stBase, cDock);

                if (null == _cPMDuo)
                {
                    _cFile.Open();
                    if (bKeepAlive && bTurnOffQueue || _cFile.nFramesTotal < Preferences.nQueueAnimationLength * 1.5f)   // иначе очень неудобно в воркере работать... да и не зачем тогда очередь разводить раз так мало кадров
                        _cFile.Cache();
                    else if (!bTurnOffQueue)
                        _cFile.DynamicQueueStart();

                    _cPMDuo = new PixelsMap.Triple(stMergingMethod, new Area(0, 0, stArea.nWidth, stArea.nHeight), PixelsMap.Format.ARGB32, true, Baetylus.PixelsMapDispose);
                    if (1 > _cPMDuo.cFirst.nLength)
                        (new Logger()).WriteNotice("1 > __cPixelsMap.nLength. animation.prepare");
                    _cPMDuo.Allocate();
					(new Logger()).WriteDebug3("new pixelmap for animation: [id=" + _cPMDuo.cFirst.nID + "][hc=" + this.nID + "][file=" + (null == _cFile ? "null" : _cFile.sName) + "]");
                    _cPMDuo.RenewFirstTime();
                }
                
                nPixelsMapSyncIndex = byte.MaxValue;
                nFrameCurrentPhysical = 0;   //это если препаре делают после стопа.
				nLoopCurrent = 0;
				if (nLoopsQty > 0 && nDuration > nLoopsQty * nFramesTotal)
					nDuration = nLoopsQty * nFramesTotal;

				base.Prepare();
			}
			catch (Exception ex)
			{
				(new Logger()).WriteError(ex);
				(new Logger()).WriteWarning("[bKeepAlive = " + (null == _cPMDuo.cFirst ? "(__cPixelsMap = null)" : "" + _cPMDuo.cFirst.bKeepAlive) + "][nDuration = " + nDuration + "][file=" + (null == _cFile ? "null" : _cFile.sName) + "]");
				throw;
			}
		}
		override public void Start()
		{
			base.Start();
		}
		public override void Idle()
		{
			base.Idle();
			nLoopCurrent = 0;
			nDuration = ulong.MaxValue;
			_cFile.Idle();
		}
		override public void Dispose()
		{
			if (_oLock == null)
				return;
			lock (_oLock)
			{
				if (_bDisposed)
					return;
				_bDisposed = true;
			}
			base.Dispose();
			if (null != _cPMDuo)
			{
				(new Logger()).WriteDebug3("disposing pixelmap in animation: [hc=" + this.nID + "][file=" + (null == _cFile ? "null" : _cFile.sName) + "]");
				Baetylus.PixelsMapDispose(_cPMDuo, true);
			}
            _cPMDuo = null;
			nFrameCurrentPhysical = 0;
			nLoopCurrent = 0;
			_cFile.Dispose();
		}
		override public PixelsMap FrameNext()
		{
			base.FrameNext();
			base.Action();
            try
            {
                _cPixelsMap = _cPMDuo.Switch(nPixelsMapSyncIndex);

                _cTimings.TotalRenew();
                if (null == _cPixelsMap)
                {
                    _cPixelsMap = new PixelsMap(stMergingMethod, new Area(0, 0, stArea.nWidth, stArea.nHeight), PixelsMap.Format.ARGB32);
                    _cPixelsMap.bKeepAlive = false; // на одни раз
                    _cPixelsMap.nIndexTriple = nPixelsMapSyncIndex;
                    _cPixelsMap.Allocate();
                    _cTimings.Restart("newpm");
                }
                _cPixelsMap.Move(0, 0);
				_cTimings.Restart("move1");
				_cFile.VideoFrameNext(_cPixelsMap);
				_cTimings.Restart("frnext");
                if (_cFile.bEOF)
                {
                    base.Stop();
                    return null;
                }
				_cPixelsMap.Move(stArea.nLeft, stArea.nTop);
				_cTimings.Restart("move2");
				Advance();
				_cTimings.Stop("frnext", "advance", 30);
			}
			catch (Exception ex)
			{
				(new Logger()).WriteError(ex);
			}
			if (nFrameCurrent >= nDuration)
			{
				base.Stop();
			}
            if (null != _cPixelsMap)
            {
                _cPixelsMap.nAlphaConstant = nCurrentOpacity;
                if (null != cMask)
                    _cPixelsMap.eAlpha = cMask.eMaskType;
            }
            return _cPixelsMap;
		}
		override public void Skip()
		{
			base.Action();
			try
			{
				_cFile.VideoFrameNext(null);
				Advance();
			}
			catch (Exception ex)
			{
				(new Logger()).WriteError(ex);
			}
		}

		protected void Advance()
		{
			nFrameCurrentPhysical++;
			if (nFrameCurrentPhysical >= _cFile.nFramesTotal)
			{
				nLoopCurrent++;
				nFrameCurrentPhysical = 0;
				_cFile.Reset(); //чтобы брать файлы снова с диска.
                _cFile.nAnimationLoopCurrent = nLoopCurrent;
			}
		}
	}
}
