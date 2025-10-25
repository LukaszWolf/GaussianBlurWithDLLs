using System.IO;
using System.Runtime.InteropServices;
using System.Windows;         // ważne dla Window
using System.Windows.Controls;
using System.Windows.Media;
using System.Xml; // jeśli masz zdarzenia z TextBox itd.
using MessageBox = System.Windows.MessageBox;
using WinForms = System.Windows.Forms; // alias


namespace GaussianBlur
{
    public partial class MainWindow : Window
    {
        Boolean asmChoice = false;
        Boolean cChoice = false;
        int result;
        private string _imagesFolder = string.Empty;
        private int _imageCount = 0;

        // rozszerzenia jakie uznajemy za obraz
        private static readonly HashSet<string> ImageExts = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".png", ".bmp", ".tif", ".tiff", ".gif", ".webp" };

        [DllImport(@"C:\Users\MSI\source\repos\GaussianBlur\x64\Release\DllAsm.dll",
           CallingConvention = CallingConvention.StdCall)]
        private static extern void MyProc1(IntPtr imagePtr, int width, int height, int stride);

        [DllImport(@"C:\Users\MSI\source\repos\GaussianBlur\x64\Release\DllC.dll")]
        static extern int MyProc2(int a, int b);
        public MainWindow()
        {
            InitializeComponent(); // łączy z XAML
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

                // Szybkie liczenie tylko w bieżącym folderze (bez podfolderów)
                _imageCount = Directory.EnumerateFiles(_imagesFolder, "*.*", SearchOption.TopDirectoryOnly)
                                       .Count(p => ImageExts.Contains(Path.GetExtension(p)));

                System.Windows.MessageBox.Show($"Wybrano folder:\n{_imagesFolder}\n\nLiczba obrazów: {_imageCount}",
                               "Folder wybrany",
                               MessageBoxButton.OK, MessageBoxImage.Information);
                // jeśli chcesz gdzieś pokazać wynik w UI, np. w TextBlock:
                tbFilePath.Text = $"Folder: {_imagesFolder}  |  Plików: {_imageCount}";
            }
        }

        private void btnChooseCDll_Click(object sender, RoutedEventArgs e)
        {
            cChoice = !cChoice;
            if (cChoice)
            {
                asmChoice = false;
                btnChooseCDll.Background = System.Windows.Media.Brushes.LightCyan;
                btnChooseAsmDll.Background = System.Windows.Media.Brushes.LightGray;
            }
            else btnChooseCDll.Background = System.Windows.Media.Brushes.LightGray;
        }

        private void btnChooseAsmDll_Click(object sender, RoutedEventArgs e)
        {
            asmChoice = !asmChoice;

            if (asmChoice)
            {
                cChoice = false;
                btnChooseAsmDll.Background = System.Windows.Media.Brushes.LightCyan;
                btnChooseCDll.Background = System.Windows.Media.Brushes.LightGray;
            }
            else btnChooseAsmDll.Background = System.Windows.Media.Brushes.LightGray;

        }
        private void btnRunProcessing_Click(object sender, RoutedEventArgs e)
        {
            var firstImage = Directory.EnumerateFiles(_imagesFolder, "*.*")
                                      .FirstOrDefault(f => ImageExts.Contains(Path.GetExtension(f)));

            if (firstImage == null)
            {
                MessageBox.Show("No supported image found.");
                return;
            }

            using (var bmp = new System.Drawing.Bitmap(firstImage))
            {
                var rect = new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height);
                var data = bmp.LockBits(rect,
                    System.Drawing.Imaging.ImageLockMode.ReadWrite,
                    System.Drawing.Imaging.PixelFormat.Format32bppArgb);

                IntPtr ptr = data.Scan0;

                // 🔧 Call the ASM function
                MyProc1(ptr, bmp.Width, bmp.Height, data.Stride);

                bmp.UnlockBits(data);

                string outPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                    "processed_center_red.png");
                bmp.Save(outPath, System.Drawing.Imaging.ImageFormat.Png);

                MessageBox.Show($"Saved:\n{outPath}");
            }
        }

    }
}