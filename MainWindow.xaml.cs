using System;
using System.Collections.Generic;
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
        private bool asmChoice = true;  // jeśli masz przełączniki C/ASM, ustawisz to przy kliknięciu
        private string _imagesFolder = string.Empty;
        private int _imageCount = 0;

        // Obsługiwane rozszerzenia obrazów
        private static readonly HashSet<string> ImageExts = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".png", ".bmp", ".tif", ".tiff", ".gif", ".webp" };

        // ─────────────────────────────────────────────────────────────────────
        //  D L L   (NOWA SYGNATURA: width, linesForThread, stride, startY)
        // ─────────────────────────────────────────────────────────────────────
        [DllImport(@"C:\Users\MSI\source\repos\GaussianBlur\x64\Release\DllAsm.dll",
            CallingConvention = CallingConvention.StdCall)]
        private static extern void MyProc1(
    IntPtr imagePtr,
    int width,
    int stride,        // <-- R8D
    int startY,        // <-- R9D
    int linesForThread // <-- 5-ty (nieużywany w rejestrach, ale zaraz zobaczysz, że i tak go weźmiemy z R11)
);

        public MainWindow()
        {
            InitializeComponent();
        }

        // ─────────────────────────────────────────────────────────────────────
        //  PODZIAŁ WYSOKOŚCI NA WĄTKI  → [base, base, …] + reszta
        //  i aktualizacja tbLines
        // ─────────────────────────────────────────────────────────────────────
        private int[] BuildLinesVector(int imageHeight, int numThreads)
        {
            if (numThreads <= 0) throw new ArgumentOutOfRangeException(nameof(numThreads));

            int baseLines = Math.DivRem(imageHeight, numThreads, out int rest);
            var lines = Enumerable.Range(0, numThreads)
                                  .Select(i => baseLines + (i < rest ? 1 : 0))
                                  .ToArray();

            tbLines.Text = $"[{string.Join(", ", lines)}]";
            tbLines.Text += " h: "+imageHeight;
            return lines;
        }

        // sumy prefixowe startY dla wątków
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

        // ─────────────────────────────────────────────────────────────────────
        //  WYBÓR FOLDERU
        // ─────────────────────────────────────────────────────────────────────
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
                tbLines.Text = ""; // wyczyść

                // Jeśli chcesz od razu policzyć podział wierszy używając pierwszego obrazka:
                var first = Directory.EnumerateFiles(_imagesFolder, "*.*")
                                     .FirstOrDefault(f => ImageExts.Contains(Path.GetExtension(f)));
                if (first != null)
                {
                    // pobierz wymiary pierwszego obrazka
                    using var tmp = new Bitmap(first);
                    int threads = Environment.ProcessorCount;
                    var lines = BuildLinesVector(tmp.Height, threads);
                    // wynik trafi do tbLines w BuildLinesVector
                }
            }
        }

        // jeśli masz 2 przyciski C/ASM — tu się przełącza:
        private void btnChooseAsmDll_Click(object sender, RoutedEventArgs e) => asmChoice = true;
        private void btnChooseCDll_Click(object sender, RoutedEventArgs e) => asmChoice = false; // (nie używamy tu C)

        // ─────────────────────────────────────────────────────────────────────
        //  PRZETWARZANIE PO KLIKNIĘCIU RUN
        // ─────────────────────────────────────────────────────────────────────
        private async void btnRunProcessing_Click(object sender, RoutedEventArgs e)
        {
            if (_imageCount == 0 || !Directory.Exists(_imagesFolder))
            {
                MessageBox.Show("Wybierz folder z co najmniej jednym obrazem.", "Brak danych",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var firstImage = Directory.EnumerateFiles(_imagesFolder, "*.*")
                                      .FirstOrDefault(f => ImageExts.Contains(Path.GetExtension(f)));
            if (firstImage == null)
            {
                MessageBox.Show("Nie znaleziono obsługiwanego obrazu w folderze.",
                    "Błąd", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                using var src = new Bitmap(firstImage);

                // GDI+ LockBits wymaga dokładnie 32bppArgb. Jeśli obraz jest inny — konwertujemy.
                Bitmap bmp;
                if (src.PixelFormat != PixelFormat.Format32bppArgb)
                {
                    bmp = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb);
                    using var g = Graphics.FromImage(bmp);
                    g.DrawImage(src, 0, 0, src.Width, src.Height);
                }
                else
                {
                    bmp = (Bitmap)src.Clone();
                }

                using (bmp)
                {
                    int numThreads = Environment.ProcessorCount;
                    var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);

                    // Podział pracy na wątki
                    var linesPerThread = BuildLinesVector(bmp.Height, numThreads);
                    var startRows = BuildStartRows(linesPerThread);

                    // Zablokuj pamięć pikseli
                    var data = bmp.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
                    try
                    {
                        IntPtr basePtr = data.Scan0;
                        int stride = data.Stride;

                        if (!asmChoice)
                        {
                            MessageBox.Show("Wybierz DLL: ASM.", "Info",
                                MessageBoxButton.OK, MessageBoxImage.Information);
                            return;
                        }

                        // Zadania równoległe – każdy wątek dostaje swój pasek wierszy
                        var tasks = new List<Task>(numThreads);
                        for (int i = 0; i < numThreads; i++)
                        {
                            int lines = linesPerThread[i];
                            int startY = startRows[i];

                            if (lines <= 0) continue; // nic do roboty (może się zdarzyć przy bardzo niskich obrazach)

                            tasks.Add(Task.Run(() =>
                            {
                                // Wywołanie ASM: zapisuje TYLKO własny zakres
                                MyProc1(basePtr, bmp.Width, stride, startY, lines);
                            }));
                        }

                        await Task.WhenAll(tasks);
                    }
                    finally
                    {
                        bmp.UnlockBits(data);
                    }

                    // Zapis
                    var outPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                        $"processed_{DateTime.Now:HHmmss}.png");
                    bmp.Save(outPath, ImageFormat.Png);

                    MessageBox.Show($"Saved:\n{outPath}", "OK", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Processing failed:\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
