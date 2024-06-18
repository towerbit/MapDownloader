using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using GMap.NET;
using GMap.NET.MapProviders;
using log4net;

namespace GMapDownload
{
    public class TileDownloader
    {
        private static readonly ILog log = LogManager.GetLogger(typeof(TileDownloader));

        public event EventHandler<TileDownloadEventArgs> PrefetchTileStart;
        public event EventHandler<TileDownloadEventArgs> PrefetchTileComplete;
        public event EventHandler<TileDownloadEventArgs> PrefetchTileProgress;

        private int retry = 3;
        public int Retry
        {
            get { return retry; }
            set { retry = value; }
        }

        private string tilePath;
        public string TilePath
        {
            get { return tilePath; }
            set { tilePath = value; }
        }

        public bool IsComplete
        {
            get { return isComplete; }
            set { isComplete = value; }
        }

        private GMapProvider provider;
        private int threadNum = 5;
        private bool isComplete = true;
        private Thread[] thread;

        private object locker = new object();
        private volatile int downloadSize = 0; // Download complete number

        private int allTileSize;  //总数
        private ConcurrentQueue<DownloadLevelTile> downloadFailedTiles = new ConcurrentQueue<DownloadLevelTile>();

        private System.Timers.Timer updateUiTimer;  // UI Update thread 
        private Thread downloadFailedThread;         // Retry failed download thread

        public TileDownloader(int threadNum)
        {
            this.threadNum = threadNum;
            this.thread = new Thread[threadNum];
            updateUiTimer = new System.Timers.Timer(300);
            updateUiTimer.Elapsed += new System.Timers.ElapsedEventHandler(updateUiTimer_Elapsed);
            downloadFailedThread = new Thread(DownloadFailedTiles);
        }

        #region 预置的输出格式

        public static string[] TileWriteFormats => new string[]
        {
            FORMAT_DEFAULT,
            FORMAT_NORMAL,
            FORMAT_SPECIAL
        };

        /// <summary>
        /// 默认瓦片格式
        /// </summary>
        private const string FORMAT_DEFAULT = "/L{z:D2}/R{y:x8}/L{x:x8}.png";
        /// <summary>
        /// 常用瓦片格式
        /// </summary>
        private const string FORMAT_NORMAL = "/{z}/{x}_{y}.png";
        /// <summary>
        /// 特定瓦片格式
        /// </summary>
        private const string FORMAT_SPECIAL = "/{z}/{x}/{y}/x={x}&y={y}&z={z}.png";
        #endregion

        private string _tileWriteFormat = convertToTileFormat(FORMAT_DEFAULT);

        private static string convertFromTileFormat(string format) => format.Replace("{0", "{z")
                                                                            .Replace("{1", "{x")
                                                                            .Replace("{2", "{y");
        private static string convertToTileFormat(string format) => format.Replace("{z", "{0")
                                                                          .Replace("{x", "{1")
                                                                          .Replace("{y", "{2");
        /// <summary>
        /// 自定义输出格式： 0-z(zoom), 1-x, 2-y, 一般应以 / 开头
        /// </summary>
        public string TileWriteFormat 
        {
            get => convertFromTileFormat(_tileWriteFormat);
            set 
            {
                if (!string.IsNullOrEmpty(value))
                {
                    _tileWriteFormat = value;
                    if (!_tileWriteFormat.StartsWith("/"))
                        _tileWriteFormat = "/" + _tileWriteFormat;
                    _tileWriteFormat = convertToTileFormat(_tileWriteFormat);
                }
            }
        }

        // Update progress
        void updateUiTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            if (!IsComplete)
            {
                ReportProgress();
            }
            else
            {
                GMaps.Instance.UseMemoryCache = true;
                GMaps.Instance.CacheOnIdleRead = true;
                System.Timers.Timer timer = sender as System.Timers.Timer;
                if (timer != null)
                {
                    timer.Stop();
                }
                ReportProgress();
                ReportComplete();
            }
        }

        public void StartDownload(TileDownloaderArgs tileDownloaderArgs)
        {
            GMaps.Instance.UseMemoryCache = false;
            GMaps.Instance.CacheOnIdleRead = false;
            
            isComplete = false;
            downloadSize = 0;

            provider = tileDownloaderArgs.MapProvider;
            List<DownloadLevelTile> downloadTiles = tileDownloaderArgs.DownloadTiles;

            allTileSize = downloadTiles.Count;
            int singelNum = (int)(allTileSize / threadNum);
            int remainder = (int)(allTileSize % threadNum);

            if (PrefetchTileStart != null)
            {
                PrefetchTileStart(this, new TileDownloadEventArgs(0));
            }

            if (singelNum == 0)
            {
                threadNum = 1;
            }
            for (int i = 0; i < threadNum; i++)
            {
                int startIndex = i * singelNum;
                int endIndex = startIndex + singelNum - 1;
                if (remainder != 0 && (threadNum - 1) == i)
                {
                    endIndex = allTileSize - 1;
                }
                DownloadThreadArgs args = new DownloadThreadArgs(downloadTiles.GetRange(startIndex, endIndex - startIndex + 1));
                thread[i] = new Thread(new ParameterizedThreadStart(Download));
                thread[i].Start(args);
            }

            updateUiTimer.Start();
            downloadFailedThread.Start();
        }

        private void ReportProgress()
        {
            if (PrefetchTileProgress != null)
            {
                PrefetchTileProgress(null, new TileDownloadEventArgs(allTileSize, downloadSize));
            }
        }

        private void ReportComplete()
        {
            if (PrefetchTileComplete != null)
            {
                PrefetchTileComplete(null, new TileDownloadEventArgs(100));
            }
        }

        private void Download(object obj)
        {
            try
            {
                DownloadThreadArgs args = obj as DownloadThreadArgs;
                List<DownloadLevelTile> threadDownloadLevelTiles = args.DownloadLevelTiles;
                int retryCount = 0;
                for (int i = 0; i < threadDownloadLevelTiles.Count; ++i)
                {
                    GPoint p = threadDownloadLevelTiles[i].TilePoint;
                    int zoom = threadDownloadLevelTiles[i].TileZoom;
                    try
                    {
                        if (CacheTiles(zoom, p, provider))
                        {
                            retryCount = 0;
                            lock (locker)
                            {
                                ++downloadSize;
                                if (downloadSize == allTileSize)
                                {
                                    IsComplete = true;
                                }
                            }
                        }
                        else
                        {
                            if (++retryCount <= retry)
                            {
                                --i;
                                continue;
                            }
                            else
                            {
                                retryCount = 0;
                                downloadFailedTiles.Enqueue(threadDownloadLevelTiles[i]);
                            }
                        }
                    }
                    catch (Exception exception)
                    {
                        log.Error(exception);
                    }
                }
                log.Info("One thread download complete.");
            }
            catch (Exception ex)
            {
                log.Error(ex);
            }
        }

        private void DownloadFailedTiles()
        {
            while (!IsComplete)
            {
                if (downloadFailedTiles.IsEmpty)
                {
                    Thread.Sleep(5000);
                    continue;
                }
                DownloadLevelTile tile = null;
                downloadFailedTiles.TryDequeue(out tile);
                if (tile != null)
                {
                    GPoint p = tile.TilePoint;
                    int zoom = tile.TileZoom;
                    try
                    {
                        if (CacheTiles(zoom, p, provider))
                        {
                            lock (locker)
                            {
                                ++downloadSize;
                                if(downloadSize == allTileSize)
                                {
                                    IsComplete = true;
                                }
                            }
                        }
                        else
                        {
                            downloadFailedTiles.Enqueue(tile);
                        }
                    }
                    catch (Exception ex)
                    {
                    }
                }
            }
            log.Info("Download failed tiles complete.");
        }

        private bool CacheTiles(int zoom, GPoint p, GMapProvider provider)
        {
            foreach (var pr in provider.Overlays)
            {
                PureImage img;
                try
                {
                    img = pr.GetTileImage(p, zoom);
                    if (img != null)
                    {
                        // if the tile path is not null, write the tile to disk
                        if (tilePath != null)
                        {
                            WriteTileToDisk(img, zoom, p);
                        }
                        else
                        {
                            GMaps.Instance.PrimaryCache.PutImageToCache(img.Data.ToArray(), pr.DbId, p, zoom);
                        }
                        img.Dispose();
                        img = null;
                    }
                    else
                    {
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// 写入瓦片文件
        /// </summary>
        /// <param name="img"></param>
        /// <param name="zoom"></param>
        /// <param name="p"></param>
        private void WriteTileToDisk(PureImage img, int zoom, GPoint p)
        {
            if(string.IsNullOrEmpty(_tileWriteFormat))
                throw new ArgumentNullException("tilePathFormat can not be null or empty");
            
            DirectoryInfo di = new DirectoryInfo(tilePath + "/_alllayers");
            long x = p.X;
            long y = p.Y;
            string filename = di.FullName + string.Format(_tileWriteFormat, zoom, x, y);
            string dir = Path.GetDirectoryName(filename);
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            
            FileStream fs = new FileStream(filename, FileMode.Create, FileAccess.Write);
            BinaryWriter sw = new BinaryWriter(fs);
            //读出图片字节数组至byte[]
            byte[] imageByte = img.Data.ToArray();
            sw.Write(imageByte);
            sw.Close();
            fs.Close();
        }
    }
}
