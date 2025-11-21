using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using MessageBox = System.Windows.MessageBox;
using WinForms = System.Windows.Forms;

namespace GaussianBlur
{
    public partial class MainWindow : Window
    {
        private bool asmChoice = false;
        private bool cChoice = false;
        private string _imagesFolder = string.Empty;
        private int _imageCount = 0;
        int threads = Environment.ProcessorCount;
        long time;

        private static readonly HashSet<string> ImageExts = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".png", ".bmp", ".tif", ".tiff", ".gif", ".webp" };

        // ASM – in-place (np. rozjaśnianie)
        //    [DllImport(@"C:\Users\MSI\source\repos\GaussianBlur\x64\Release\DllAsm.dll",
        //    CallingConvention = CallingConvention.StdCall)]
        //    private static extern void MyProc1(
        //    IntPtr src,          // RCX
        //    IntPtr dst,          // RDX
        //    int width,           // R8D
        //    int height,          // R9D
        //    int stride,          // [rsp+40]
        //    int startY,          // [rsp+48]
        //    int linesForThread   // [rsp+50]
        //);
        [DllImport(@"C:\Users\MSI\source\repos\GaussianBlur\x64\Release\DllAsm.dll",
            CallingConvention = CallingConvention.StdCall)]
        public static extern void GaussianBlur5x5asm(
        IntPtr src,
        IntPtr dst,
        int width,
        int height,
        int stride,
        int startY,
        int lines
    );

        // C – prawdziwy Gaussian blur 5x5: SRC -> DST
        [DllImport(@"C:\Users\MSI\source\repos\GaussianBlur\x64\Release\DllC.dll",
            CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "GaussianBlur5x5")] // >>> zmień na "MyProc2", jeśli tak eksportujesz w DLL
        private static extern void GaussianBlur5x5(
            IntPtr src,
            IntPtr dst,
            int width,
            int height,
            int stride,
            int startY,
            int linesForThread);

        public MainWindow()
        {
            InitializeComponent();
        }

        private void tbThreadCount_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!int.TryParse(tbThreadCount.Text, out int value))
                return;

            if (value < 1) value = 1;
            if (value > 64) value = 64;

            threads = value;
            RebuildLinesPreviewIfPossible();

            string fixedText = value.ToString();
            if (tbThreadCount.Text != fixedText)
            {
                tbThreadCount.Text = fixedText;
                tbThreadCount.CaretIndex = tbThreadCount.Text.Length;
            }
        }

        private int[] BuildLinesVector(int imageHeight, int numThreads)
        {
            if (numThreads <= 0) throw new ArgumentOutOfRangeException(nameof(numThreads));

            int baseLines = Math.DivRem(imageHeight, numThreads, out int rest);
            var lines = Enumerable.Range(0, numThreads)
                                  .Select(i => baseLines + (i < rest ? 1 : 0))
                                  .ToArray();

            tbLines.Text = $"[{string.Join(", ", lines)}] h: {imageHeight}";
            return lines;
        }

        private void RebuildLinesPreviewIfPossible()
        {
            if (string.IsNullOrEmpty(_imagesFolder) || !Directory.Exists(_imagesFolder))
                return;

            var first = Directory.EnumerateFiles(_imagesFolder, "*.*")
                                 .FirstOrDefault(f => ImageExts.Contains(Path.GetExtension(f)));
            if (first == null) return;

            using var tmp = new Bitmap(first);
            BuildLinesVector(tmp.Height, threads);
        }

        private static int[] BuildStartRows(int[] linesPerThread)
        {
            var startRows = new int[linesPerThread.Length];
            int acc = 0;
            for (int i = 0; i < linesPerThread.Length; i++)
            {
                startRows[i] = acc;
                acc += linesPerThread[i];
            }
            return startRows;
        }

        private void btnChooseFolder_Click(object sender, RoutedEventArgs e)
        {
            using var dlg = new WinForms.FolderBrowserDialog
            {
                Description = "Wybierz folder z obrazami do przetworzenia"
            };

            if (dlg.ShowDialog() == WinForms.DialogResult.OK && Directory.Exists(dlg.SelectedPath))
            {
                _imagesFolder = dlg.SelectedPath;

                _imageCount = Directory.EnumerateFiles(_imagesFolder, "*.*", SearchOption.TopDirectoryOnly)
                                       .Count(p => ImageExts.Contains(Path.GetExtension(p)));

                tbFilePath.Text = $"Folder: {_imagesFolder}   |   Plików: {_imageCount}";
                tbLines.Text = "";

                var first = Directory.EnumerateFiles(_imagesFolder, "*.*")
                                     .FirstOrDefault(f => ImageExts.Contains(Path.GetExtension(f)));
                if (first != null)
                {
                    using var tmp = new Bitmap(first);
                    BuildLinesVector(tmp.Height, threads);
                }
            }
        }

        private void btnChooseAsmDll_Click(object sender, RoutedEventArgs e)
        {
            btnChooseAsmDll.Background = System.Windows.Media.Brushes.LightBlue;
            btnChooseCDll.Background = System.Windows.Media.Brushes.LightGray;
            asmChoice = true;
            cChoice = false;
        }

        private void btnChooseCDll_Click(object sender, RoutedEventArgs e)
        {
            btnChooseAsmDll.Background = System.Windows.Media.Brushes.LightGray;
            btnChooseCDll.Background = System.Windows.Media.Brushes.LightBlue;
            asmChoice = false;
            cChoice = true;
        }

        // =====================================================================
        //  RUN – tu jest cała zmiana pod GaussianBlur5x5(src, dst, ...)
        // =====================================================================
        private async void btnRunProcessing_Click(object sender, RoutedEventArgs e)
        {
            if (_imageCount == 0 || !Directory.Exists(_imagesFolder))
            {
                MessageBox.Show("Wybierz folder z co najmniej jednym obrazem.");
                return;
            }

            var firstImage = Directory.EnumerateFiles(_imagesFolder, "*.*")
                                      .FirstOrDefault(f => ImageExts.Contains(Path.GetExtension(f)));
            if (firstImage == null)
            {
                MessageBox.Show("Nie znaleziono obsługiwanego obrazu.");
                return;
            }

            try
            {
                using var srcBmpOrig = new Bitmap(firstImage);

                // Wymuszenie 32bppArgb
                Bitmap srcBmp;
                if (srcBmpOrig.PixelFormat != PixelFormat.Format32bppArgb)
                {
                    srcBmp = new Bitmap(srcBmpOrig.Width, srcBmpOrig.Height, PixelFormat.Format32bppArgb);
                    using (var g = Graphics.FromImage(srcBmp))
                        g.DrawImage(srcBmpOrig, 0, 0, srcBmpOrig.Width, srcBmpOrig.Height);
                }
                else
                {
                    srcBmp = (Bitmap)srcBmpOrig.Clone();
                }

                using (srcBmp)
                using (var dstBmp = new Bitmap(srcBmp.Width, srcBmp.Height, PixelFormat.Format32bppArgb))
                {
                    int width = srcBmp.Width;
                    int height = srcBmp.Height;

                    var rect = new Rectangle(0, 0, width, height);

                    // Lock źródła (tylko do odczytu) i celu (tylko zapis)
                    var srcData = srcBmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                    var dstData = dstBmp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

                    try
                    {
                        IntPtr srcPtr = srcData.Scan0;
                        IntPtr dstPtr = dstData.Scan0;
                        int stride = srcData.Stride;

                        int numThreads = threads; // Twój wybór z GUI
                        var linesPerThread = BuildLinesVector(height, numThreads);
                        var startRows = BuildStartRows(linesPerThread);

                        var tasks = new List<Task>(numThreads);
                        System.Diagnostics.Stopwatch sw = Stopwatch.StartNew();
                        for (int i = 0; i < numThreads; i++)
                        {
                            int lines = linesPerThread[i];
                            int startY = startRows[i];

                            if (lines <= 0)
                                continue;

                            tasks.Add(Task.Run(() =>
                            {
                                if (asmChoice)
                                    GaussianBlur5x5asm(srcPtr, dstPtr, width, height, stride, startY, lines);
                                else
                                    GaussianBlur5x5(srcPtr, dstPtr, width, height, stride, startY, lines);
                            }));
                        }

                        await Task.WhenAll(tasks);
                        sw.Stop();
                        time = sw.ElapsedMilliseconds;
                    }
                    finally
                    {
                        srcBmp.UnlockBits(srcData);
                        dstBmp.UnlockBits(dstData);
                    }

                    string outPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                        $"processed{DateTime.Now:HHmmss}.png");

                    dstBmp.Save(outPath, ImageFormat.Png);
                    MessageBox.Show($"Zapisano:\n{outPath}\n Czas przetwarzania: {time} ms");
                    
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Błąd przetwarzania:\n{ex.Message}");
            }
        }


        private void btnThreadsMinus_Click(object sender, RoutedEventArgs e)
        {
            if (threads > 1)
                threads--;

            tbThreadCount.Text = threads.ToString();
            RebuildLinesPreviewIfPossible();
            if (threads == 64)
                btnThreadsPlus.Background = System.Windows.Media.Brushes.Red;
            else
                btnThreadsPlus.Background = System.Windows.Media.Brushes.LightGray;

            if (threads == 1)
                btnThreadsMinus.Background = System.Windows.Media.Brushes.Red;
            else
                btnThreadsMinus.Background = System.Windows.Media.Brushes.LightGray;
        }

        private void btnThreadsPlus_Click(object sender, RoutedEventArgs e)
        {
            if (threads < 64)
                threads++;

            tbThreadCount.Text = threads.ToString();
            RebuildLinesPreviewIfPossible();
            if (threads == 64)
                btnThreadsPlus.Background = System.Windows.Media.Brushes.Red;
            else
                btnThreadsPlus.Background = System.Windows.Media.Brushes.LightGray;

            if (threads == 1)
                btnThreadsMinus.Background = System.Windows.Media.Brushes.Red;
            else
                btnThreadsMinus.Background = System.Windows.Media.Brushes.LightGray;
        }

        private void btnThreadsHw_Click(object sender, RoutedEventArgs e)
        {
            threads = Environment.ProcessorCount;
            tbThreadCount.Text = threads.ToString();
            RebuildLinesPreviewIfPossible();

            if (threads == 64)
                btnThreadsPlus.Background = System.Windows.Media.Brushes.Red;
            else
                btnThreadsPlus.Background = System.Windows.Media.Brushes.LightGray;

            if (threads == 1)
                btnThreadsMinus.Background = System.Windows.Media.Brushes.Red;
            else
                btnThreadsMinus.Background = System.Windows.Media.Brushes.LightGray;
        }
    }
}
