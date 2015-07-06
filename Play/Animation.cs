using System;
using System.Collections.Generic;
using System.Text;

using helpers;
using helpers.extensions;
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
			private Rectangle _stBitmapSize;
			private Bitmap _cBitmap;
			private BitmapData _cBitmapData;
			private List<byte[]> _aCache;
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
			private bool bIsClosed;
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
			public bool bEOF { get; private set; }
			public ulong nFramesTotal { get; private set; }
			public ulong nFrameCurrent { get; private set; }
			public Size stDimensions
			{
				get
				{
					return _stBitmapSize.Size;
				}
			}
			public Size stDimensionsResized { get; set; }
			System.Threading.Thread _cThreadFramesGettingWorker;
			private int nQueueSize;
			private int _nQueueLength;
			private int nFirstNotNull;
			private object _oWorkerLock;
			private object _oCloseLock;
			public int nQueueLength
			{
				get
				{
					return _nQueueLength;
				}
			}

			private File()
			{
				_oWorkerLock = new object();
				_oCloseLock = new object();
				_sFolder = null;
				_aFiles = null;
				_bIsDirectory = null;
				nFrameCurrent = 0;
				nFramesTotal = 0;
				_stBitmapSize = Rectangle.Empty;
				stDimensionsResized = Size.Empty;
				bEOF = false;
				_aCache = null;
				nQueueSize = Preferences.nQueueAnimationLength;
				_nQueueLength = -1;
				bIsClosed = false;
			}
			public File(string sFolder)
				:this()
			{
				if (sFolder.IsNullOrEmpty())
					throw new Exception("folder name can't be empty");
				_sFolder = sFolder;

				Type eType = GetType();

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
				bEOF = false;  //TODO пока очередь работает только 1 луп, если лупов больше, то это мост к плавному заполнению _aCache , что сэкономит время. Доделать
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
				ZipArchive cZip = new ZipArchive(System.IO.File.OpenRead(_sFolder), ZipArchiveMode.Read);
				foreach (ZipArchiveEntry cZAE in cZip.Entries)
					if (!cZAE.Name.StartsWith("."))
						return cZAE.Name;
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

			private Type GetType()
			{
				Type eRetVal = Type.Unknown;
				try
				{
					FileInfo cFIfirst = null;
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
					eRetVal = StringToType(cFIfirst.Extension);
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

				if (bIsDirectory)
				{
					if (bIsZipFolder)
					{
						string sName;
						_ahZipEntries = new Dictionary<string, ZipArchiveEntry>();
						ZipArchive cZip = new ZipArchive(System.IO.File.OpenRead(_sFolder), ZipArchiveMode.Read);
						foreach (ZipArchiveEntry cZAE in cZip.Entries)
						{
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
			private void Close()
			{
				lock (_oCloseLock)
				{
					if (bIsClosed)
						return;
					bIsClosed = true;
				}
				if (null != _cThreadFramesGettingWorker)
				{
					_cThreadFramesGettingWorker.Abort();
					_cThreadFramesGettingWorker.Join();
				}
				if (_eType != Type.Unknown)
				{
					_aFiles = null;
					if (null != _aCache)
						new System.Threading.Thread(() =>
						{
							System.Threading.Thread.CurrentThread.IsBackground = true;
							try
							{
								for (int nI = 0; nI < _aCache.Count; nI++)
								{
									if (_aCache[nI] != null)
									{
										_aCache[nI] = null;
										System.Threading.Thread.Sleep(5);
										GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized);
									}
								}
								_aCache = null;
							}
							catch { }
						}).Start();
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
				if (null != _aCache)
				{
					(new Logger()).WriteWarning("Cashe is not empty!!! Cache aborted");
					return;
				}
				_aCache = new List<byte[]>();
				try
				{
					if (null != _aFiles)
					{
						byte[] aBytes = null;
						for (int nIndx = 0; _aFiles.Length > nIndx; nIndx++)
						{
							BitmapDataSet(_aFiles[nIndx]);

							aBytes = new byte[_cBitmapData.Stride * _cBitmapData.Height];
							Marshal.Copy(_cBitmapData.Scan0, aBytes, 0, aBytes.Length);
							_cBitmap.UnlockBits(_cBitmapData);
							_cBitmap.Dispose();
							_aCache.Add(aBytes);
						}
						//						GC.Collect();
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
				if (null != _aCache)
				{
					(new Logger()).WriteWarning("Cashe is not empty!!! DynamicQueueStart aborted");
					return;
				}
				lock (_oWorkerLock)
				{
					if (null != _cThreadFramesGettingWorker)
					{
						(new Logger()).WriteWarning("worker was already started!!! DynamicQueueStart aborted");
						return;
					}
					_cThreadFramesGettingWorker = new System.Threading.Thread(FramesGettingWorker);
					_cThreadFramesGettingWorker.IsBackground = true;
					_cThreadFramesGettingWorker.Priority = System.Threading.ThreadPriority.Normal;
					_cThreadFramesGettingWorker.Start();

					while (nQueueSize > nQueueLength && (null == _aCache || _aFiles.Length > _aCache.Count))
					{
						if (nQueueLength > 5)  // быстрее освобождать prepare, как в ffmpeg
							break;
						System.Threading.Thread.Sleep(1);
					}
				}
			}
			private void FramesGettingWorker(object cState)
			{
				try
				{
					(new Logger()).WriteNotice("dynamic queue started: [" + _sFolder + "]");
					_nQueueLength = 0;
					byte[] aBytes = null;
					nFirstNotNull = 0;
					_aCache = new List<byte[]>();
					while (_aFiles.Length > _aCache.Count)
					{
						for (int ni = nFirstNotNull; ni < (int)nFrameCurrent; ni++)
						{
							lock (_aCache)
							{
								nFirstNotNull = ni + 1;
								_aCache[ni] = null;
								GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized);
							}
						}
						while (nQueueSize > _nQueueLength && _aFiles.Length > _aCache.Count)
						{
							BitmapDataSet(_aFiles[_aCache.Count]);

							aBytes = new byte[_cBitmapData.Stride * _cBitmapData.Height];
							Marshal.Copy(_cBitmapData.Scan0, aBytes, 0, aBytes.Length);
							_cBitmap.UnlockBits(_cBitmapData);
							_cBitmap.Dispose();

							lock (_aCache)
							{
								_aCache.Add(aBytes);
								_nQueueLength++;
							}
							System.Threading.Thread.Sleep(0);
						}
						System.Threading.Thread.Sleep(2);
					}
				}
				catch (Exception ex)
				{
					(new Logger()).WriteError(ex);
				}
				(new Logger()).WriteNotice("dynamic queue stopped: [" + _sFolder + "][count=" + (null == _aCache ? "null" : "" + _aCache.Count) + "][]");
			}
			public void Idle()
			{
				nFrameCurrent = 0;
				if (null != _cThreadFramesGettingWorker)
				{
					if (_cThreadFramesGettingWorker.IsAlive)
					{
						_cThreadFramesGettingWorker.Abort();
						_cThreadFramesGettingWorker.Join();
					}
					_aCache = null;
					_cThreadFramesGettingWorker = null;
                }
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
							if (null == _aCache)
							{
								BitmapDataSet(_aFiles[nFrameCurrent]);

								cPixelsMap.CopyIn(_cBitmapData.Scan0, _cBitmapData.Stride * _cBitmapData.Height);
								_cBitmap.UnlockBits(_cBitmapData);
								_cBitmap.Dispose();
								//								GC.Collect();
							}
							else
							{
								if (0 <= nQueueLength)
								{
									if (0 == nQueueLength && 0 < (ulong)_aFiles.Length - nFrameCurrent)
										(new Logger()).WriteWarning("animation queue length is empty!![" + nQueueLength + "][" + _sFolder + "]");
									else if (3 > nQueueLength && 3 <= (ulong)_aFiles.Length - nFrameCurrent)
										(new Logger()).WriteNotice("animation queue length is less than 3! [" + nQueueLength + "][" + _sFolder + "]");
									else if ((Preferences.nQueueAnimationLength / 2 > nQueueLength) && (Preferences.nQueueAnimationLength / 2) <= _aFiles.Length - (int)nFrameCurrent)
										(new Logger()).WriteDebug3("animation queue length [" + nQueueLength + "][" + _sFolder + "]");
								}
								while (nQueueLength == 0)
									System.Threading.Thread.Sleep(10); 
                                lock (_aCache)
								{
									cPixelsMap.CopyIn(_aCache[(int)nFrameCurrent]);
									if (-1 < _nQueueLength)
										_nQueueLength--;
								}
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

		override public ulong nFramesTotal
		{
			get
			{
				if (0 < nLoopsQty)
					return nFramesPerLoopTotal * nLoopsQty;
				return ulong.MaxValue;
			}
		}
		public override ulong nDuration
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
		private ulong nFrameCurrentPhysical;
		private bool bResize 
		{
			get
			{
				if (0 < nPixelAspectRatio)
					return true;
				else
					return false;
			}
		}
		override public ulong nFrameCurrent
		{
			get
			{
				return (ulong)(nFrameCurrentPhysical + (nFramesPerLoopTotal * nLoopCurrent));
			}
		}
		public bool bKeepAlive { get; set; }
		public ushort nLoopsQty { get; set; }  //0 for looping forever
		public ushort nLoopCurrent { get; private set; }
		public ulong nLoopFrame
		{
			get
			{
				return nFrameCurrentPhysical;
			}
		}
		public ulong nFramesPerLoopTotal
		{
			get
			{
				return _cFile.nFramesTotal;
			}
		}
		public float nPixelAspectRatio { get; set; }

		internal Animation(EffectType eType)
			: base(eType)
		{
			try
			{
				nLoopsQty = 1;
				nLoopCurrent = 0;
				bKeepAlive = true;
				nPixelAspectRatio = 0;
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
		}
		private Area AreaResize(int nImageWidth, int nImageHeight, int nCanvasWidth, int nCanvasHeight, float nPixelAspectRatio)
		{
			Area stRetVal;
			nImageWidth = (int)Math.Round(((double)nImageWidth) * nPixelAspectRatio, 0);
			int nCrop;
			if (nImageWidth * nCanvasHeight > nImageHeight * nCanvasWidth)
			{
				nCrop = (int)Math.Round((double)(nImageHeight * nCanvasWidth) / (double)nImageWidth, 0);
				stRetVal = new Area(0, (short)Math.Round((double)(nCanvasHeight - nCrop) / 2, 0), (ushort)nCanvasWidth, (ushort)nCrop);
				_cFile.stDimensionsResized = new Size(nCanvasWidth, nCrop);
			}
			else
			{
				nCrop = (int)Math.Round((double)(nImageWidth * nCanvasHeight) / (double)nImageHeight, 0);
				stRetVal = new Area((short)Math.Round((double)(nCanvasWidth - nCrop) / 2, 0), 0, (ushort)nCrop, (ushort)nCanvasHeight);
				_cFile.stDimensionsResized = new Size(nCrop, nCanvasHeight);
			}
			return stRetVal;
		}
		override public void Prepare()
		{
			try
			{
				if (EffectStatus.Idle != ((IEffect)this).eStatus)
					return;

				if (bResize && Area.stEmpty != stArea)
				{
					stArea = AreaResize(_cFile.stDimensions.Width, _cFile.stDimensions.Height, stArea.nWidth, stArea.nHeight, nPixelAspectRatio);
					_cFile.stDimensionsResized = new Size(stArea.nWidth, stArea.nHeight);
				}
				else if (bResize && Area.stEmpty == stArea)
				{
					stArea = AreaResize(_cFile.stDimensions.Width, _cFile.stDimensions.Height, _cFile.stDimensions.Width, _cFile.stDimensions.Height, nPixelAspectRatio);
					_cFile.stDimensionsResized = new Size(stArea.nWidth, stArea.nHeight);
					stArea = new Area(0, 0, stArea.nWidth, stArea.nHeight);
				}
				else
					stArea = new Area(stArea.nLeft, stArea.nTop, (ushort)_cFile.stDimensions.Width, (ushort)_cFile.stDimensions.Height);

				if (null == _cPixelsMap)
				{
					_cFile.Open();
					if (bKeepAlive)
						_cFile.Cache();
//#if DEBUG
					else
					    _cFile.DynamicQueueStart();   //DNF
//#endif

					_cPixelsMap = new PixelsMap(bCUDA, new Area(0, 0, stArea.nWidth, stArea.nHeight), PixelsMap.Format.ARGB32);
					_cPixelsMap.bKeepAlive = true;
					if (1 > _cPixelsMap.nLength)
						(new Logger()).WriteNotice("1 > _cPixelMap.nLength. animation.prepare");
					_cPixelsMap.Allocate();
					nDuration = nFramesTotal;
				}
                else
                {
                    nFrameCurrentPhysical = 0;   //это если препаре делают после стопа.
                    nLoopCurrent = 0;
                }
                base.Prepare();
			}
			catch (Exception ex)
			{
				(new Logger()).WriteError(ex);
				(new Logger()).WriteWarning("[bKeepAlive = " + (null == _cPixelsMap ? "null" : "" + _cPixelsMap.bKeepAlive) + "][nDuration = " + nDuration + "]");
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
			_cFile.Idle();
		}
		override public void Dispose()
		{
			if (null != _cPixelsMap)
				Baetylus.PixelsMapDispose(_cPixelsMap, true);
			nFrameCurrentPhysical = 0;
			nLoopCurrent = 0;
			_cFile.Dispose();
			base.Dispose();
		}
		override public PixelsMap FrameNext()
		{
			base.Action();
			try
			{
				//if (!Advance())
					//return null;
				_cPixelsMap.Move(0, 0);
				_cFile.VideoFrameNext(_cPixelsMap);
				_cPixelsMap.Move(stArea.nLeft, stArea.nTop);
				Advance();
			}
			catch (Exception ex)
			{
				(new Logger()).WriteError(ex);
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

		protected bool Advance()
		{
			nFrameCurrentPhysical++;
			if (nFrameCurrentPhysical >= nDuration || nFrameCurrentPhysical >= _cFile.nFramesTotal)
			{
				nLoopCurrent++;
				nFrameCurrentPhysical = 0;
				if (0 < nLoopsQty && nLoopCurrent >= nLoopsQty)
				{
					Stop();
					return false;
				}
				_cFile.Reset(); //чтобы брать файлы снова с диска.
			}
			return true;
		}
	}
}
