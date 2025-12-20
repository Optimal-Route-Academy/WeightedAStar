using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms; // Dosya diyaloðu için eklendi
using ConsoleApp3.Parsers;

namespace ConsoleApp3
{
    public struct Point
    {
        public double X { get; }
        public double Y { get; }
        public double Z { get; }

        public Point(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }
    }

    /// <summary>
    /// A* algoritmasý için isimlendirilmiþ bir aðýrlýk yapýlandýrmasýný temsil eder.
    /// </summary>
    public class AgirlikAyari
    {
        public string Isim { get; }
        public double Deger { get; }

        public AgirlikAyari(string isim, double deger)
        {
            Isim = isim;
            Deger = deger;
        }
    }

    internal class AnaProgram
    {
        private const string SAMPLE_FOLDER = "Sample";
        private const string XODR_FOLDER = "XODR";
        private const string OSM_FOLDER = "OSM";

        // [STAThread] özniteliði, OpenFileDialog gibi Windows Formlarý
        // bileþenlerinin doðru çalýþmasý için gereklidir.
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            
            // Konsol buffer boyutunu artýr - çýktýlarýn kaybolmamasý için
            try
            {
                Console.BufferHeight = 9999; // Maksimum satýr sayýsý
                Console.BufferWidth = 120;   // Geniþlik
            }
            catch
            {
                // Bazý ortamlarda buffer ayarlanamayabilir, önemseme
            }
            
            Console.WriteLine("============================================================");
            Console.WriteLine("        Harita Dosyasi Isleme ve Rota Planlama             ");
            Console.WriteLine("        OpenDRIVE (.xodr) & OpenStreetMap (.osm)           ");
            Console.WriteLine("============================================================");
            
            while (true)
            {
                Console.WriteLine("\n------------------------------------------------------------");
                Console.WriteLine("| MENU                                                     |");
                Console.WriteLine("------------------------------------------------------------");
                Console.WriteLine("| [1] Dosya Sec         - Bilgisayardan harita yukle       |");
                Console.WriteLine("| [2] Sample Harita     - Ornek haritalardan sec           |");
                Console.WriteLine("| [3] Cikis             - Programdan cik                   |");
                Console.WriteLine("------------------------------------------------------------");
                Console.Write("\nSeciminiz (1-3): ");
                
                string userInput = Console.ReadLine()?.Trim();

                switch (userInput)
                {
                    case "1":
                        {
                            string mapFilePath = SelectMapFile();
                            if (!string.IsNullOrEmpty(mapFilePath))
                            {
                                ProcessMapFile(mapFilePath);
                            }
                            else
                            {
                                Console.WriteLine("Dosya secilmedi.");
                            }
                            break;
                        }
                    case "2":
                        {
                            string sampleFilePath = SelectSampleMap();
                            if (!string.IsNullOrEmpty(sampleFilePath))
                            {
                                ProcessMapFile(sampleFilePath);
                            }
                            else
                            {
                                Console.WriteLine("Sample harita secilmedi.");
                            }
                            break;
                        }
                    case "3":
                        Console.WriteLine("\nProgram sonlandiriliyor...");
                        return;
                    default:
                        Console.WriteLine("Gecersiz secim! Lutfen 1, 2 veya 3 girin.");
                        break;
                }
            }
        }

        /// <summary>
        /// Sample klasörünü bulur - önce çalýþma dizininde, sonra proje dizininde arar
        /// </summary>
        private static string FindSampleFolder()
        {
            // 1. Önce çalýþma dizininde ara (bin\Debug\Sample)
            string currentDir = Directory.GetCurrentDirectory();
            string samplePath = Path.Combine(currentDir, SAMPLE_FOLDER);
            if (Directory.Exists(samplePath))
            {
                return samplePath;
            }

            // 2. Proje dizininde ara (3 seviye yukarý: bin\Debug -> bin -> proje)
            string projectDir = Path.GetFullPath(Path.Combine(currentDir, "..", "..", SAMPLE_FOLDER));
            if (Directory.Exists(projectDir))
            {
                return projectDir;
            }

            // 3. Çalýþtýrýlabilir dosyanýn bulunduðu dizinin üst dizininde ara
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            string exeSamplePath = Path.Combine(exeDir, SAMPLE_FOLDER);
            if (Directory.Exists(exeSamplePath))
            {
                return exeSamplePath;
            }

            // 4. Exe dizininin 2 üst dizininde ara (proje kök dizini)
            string projectRootSample = Path.GetFullPath(Path.Combine(exeDir, "..", "..", SAMPLE_FOLDER));
            if (Directory.Exists(projectRootSample))
            {
                return projectRootSample;
            }

            return null;
        }

        /// <summary>
        /// Sample klasöründen harita seçimi yapar
        /// </summary>
        private static string SelectSampleMap()
        {
            // Sample klasörünü bul
            string sampleFolder = FindSampleFolder();
            
            if (string.IsNullOrEmpty(sampleFolder))
            {
                Console.WriteLine($"\n'{SAMPLE_FOLDER}' klasoru bulunamadi!");
                Console.WriteLine($"   Arama yapilan dizinler:");
                Console.WriteLine($"   - {Path.Combine(Directory.GetCurrentDirectory(), SAMPLE_FOLDER)}");
                Console.WriteLine($"   - {Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "..", SAMPLE_FOLDER))}");
                Console.WriteLine($"   - {Path.Combine(AppDomain.CurrentDomain.BaseDirectory, SAMPLE_FOLDER)}");
                Console.WriteLine($"\n   Lutfen Sample klasorunu olusturun ve icine harita dosyalari ekleyin.");
                Console.WriteLine($"   Veya 'CopySampleFiles.bat' dosyasini calistirin.");
                return null;
            }

            // XODR ve OSM dosyalarýný topla
            var xodrPath = Path.Combine(sampleFolder, XODR_FOLDER);
            var osmPath = Path.Combine(sampleFolder, OSM_FOLDER);

            var xodrFiles = Directory.Exists(xodrPath) 
                ? Directory.GetFiles(xodrPath, "*.xodr") 
                : new string[0];
            
            var osmFiles = Directory.Exists(osmPath) 
                ? Directory.GetFiles(osmPath, "*.osm") 
                : new string[0];

            var allSampleFiles = xodrFiles.Concat(osmFiles).ToList();

            if (allSampleFiles.Count == 0)
            {
                Console.WriteLine("\nSample klasorunde hic harita dosyasi bulunamadi!");
                Console.WriteLine($"   XODR klasoru: {xodrPath}");
                Console.WriteLine($"   OSM klasoru: {osmPath}");
                Console.WriteLine($"\n   Lutfen bu klasorlere harita dosyalari ekleyin:");
                Console.WriteLine($"   - .xodr dosyalari icin: {xodrPath}");
                Console.WriteLine($"   - .osm dosyalari icin: {osmPath}");
                Console.WriteLine($"\n   Dosya ekledikten sonra 'CopySampleFiles.bat' calistirin.");
                return null;
            }

            // Sample haritalarý listele
            Console.WriteLine("\n============================================================");
            Console.WriteLine("              SAMPLE HARITALAR                              ");
            Console.WriteLine("============================================================\n");

            for (int i = 0; i < allSampleFiles.Count; i++)
            {
                var file = allSampleFiles[i];
                var fileName = Path.GetFileName(file);
                var fileType = Path.GetExtension(file).ToUpper();
                var fileSize = new FileInfo(file).Length;
                var fileSizeKB = fileSize / 1024.0;
                var fileSizeMB = fileSizeKB / 1024.0;

                string sizeStr = fileSizeMB > 1 
                    ? $"{fileSizeMB:F1} MB" 
                    : $"{fileSizeKB:F1} KB";

                Console.WriteLine($"  [{i + 1}] {fileName}");
                Console.WriteLine($"      Tip: {fileType}  |  Boyut: {sizeStr}");
                Console.WriteLine();
            }

            Console.WriteLine("  [0] Geri Don");
            Console.WriteLine();

            // Kullanicidan secim al
            while (true)
            {
                Console.Write($"Harita secin (0-{allSampleFiles.Count}): ");
                string input = Console.ReadLine()?.Trim();

                if (int.TryParse(input, out int selection))
                {
                    if (selection == 0)
                    {
                        return null; // Geri don
                    }

                    if (selection >= 1 && selection <= allSampleFiles.Count)
                    {
                        string selectedFile = allSampleFiles[selection - 1];
                        Console.WriteLine($"\nSecilen harita: {Path.GetFileName(selectedFile)}");
                        return selectedFile;
                    }
                }

                Console.WriteLine($"Gecersiz secim! Lutfen 0 ile {allSampleFiles.Count} arasinda bir sayi girin.");
            }
        }

        /// <summary>
        /// Kullanicinin bir harita dosyasi secmesi icin bir dosya secim diyalogu acar.
        /// </summary>
        /// <returns>Secilen dosyanin tam yolu veya secim iptal edilirse null.</returns>
        private static string SelectMapFile()
        {
            using (var openFileDialog = new OpenFileDialog())
            {
                openFileDialog.Title = "Harita Dosyasi Secin (.osm, .xodr)";
                openFileDialog.Filter = "Harita Dosyalari (*.osm;*.xodr)|*.osm;*.xodr|OpenDRIVE (*.xodr)|*.xodr|OpenStreetMap (*.osm)|*.osm|Tum Dosyalar (*.*)|*.*";
                openFileDialog.FilterIndex = 1;
                openFileDialog.RestoreDirectory = true;

                // Eger Sample klasoru varsa, baslangic dizini olarak ayarla
                string sampleFolder = FindSampleFolder();
                if (!string.IsNullOrEmpty(sampleFolder))
                {
                    openFileDialog.InitialDirectory = sampleFolder;
                }

                var result = openFileDialog.ShowDialog();
                if (result == DialogResult.OK)
                {
                    return openFileDialog.FileName;
                }
            }
            return null;
        }

        /// <summary>
        /// Secilen harita dosyasini isler, grafi olusturur ve algoritmalari calistirir.
        /// </summary>
        /// <param name="mapFilePath">Islenecek harita dosyasinin yolu.</param>
        private static void ProcessMapFile(string mapFilePath)
        {
            try
            {
                Console.Clear(); // Onceki ciktilari temizle
                
                Console.WriteLine("\n" + new string('=', 60));
                Console.WriteLine($"Dosya: {Path.GetFileName(mapFilePath)}");
                Console.WriteLine($"Yol: {Path.GetDirectoryName(mapFilePath)}");
                Console.WriteLine(new string('=', 60));
                
                Console.WriteLine("\nGraf verisi okunuyor...");
                GraphData graph = ParseMap(mapFilePath);
                
                Console.WriteLine($"Graf basariyla olusturuldu!");
                Console.WriteLine($"  - Dugum sayisi: {graph.NodeCount}");
                Console.WriteLine($"  - Kenar sayisi: {graph.EdgeCount}");
                Console.WriteLine($"  - Koordinat verisi: {(graph.NodeCoordinates != null ? "Var" : "Yok")}");

                // Bellek kullanimi
                long memoryUsed = GC.GetTotalMemory(false) / 1024 / 1024;
                Console.WriteLine($"  - Bellek kullanimi: {memoryUsed} MB");

                if (graph.NodeCount < 2)
                {
                    Console.WriteLine("\nGraf yeterli dugume sahip degil (minimum 2 dugum gerekli).");
                    Console.WriteLine("\nDevam etmek icin bir tusa basin...");
                    Console.ReadKey();
                    return;
                }

                // Kullanicidan baslangic ve bitis dugumleri (1-based) alinir
                Console.WriteLine("\n" + new string('-', 60));
                int baslangicDugumu = GetNodeInput($"Baslangic dugumu (1-{graph.NodeCount}): ", graph.NodeCount) - 1;
                int hedefDugumu = GetNodeInput($"Hedef dugumu (1-{graph.NodeCount}): ", graph.NodeCount) - 1;

                Console.WriteLine("\n" + new string('=', 60));
                Console.WriteLine($"ALGORITMALAR CALISTIRILIYOR");
                Console.WriteLine($"   Baslangic: Dugum {baslangicDugumu + 1}");
                Console.WriteLine($"   Hedef: Dugum {hedefDugumu + 1}");
                Console.WriteLine(new string('=', 60));

                // Dijkstra Algoritmasi (Adjacency List ile)
                Console.WriteLine("\n--- DIJKSTRA ALGORITMASI ---");
                DijkstraAlgoritmasi dijkstra = new DijkstraAlgoritmasi(graph.NodeCount);
                dijkstra.Calistir(graph.AdjacencyList, baslangicDugumu);
                dijkstra.SonuclariYazdir(hedefDugumu);
                Console.WriteLine(new string('-', 60));

                // Agirlikli A* Algoritmasi Testleri
                List<AgirlikAyari> testSenaryolari = new List<AgirlikAyari>
                {
                    new AgirlikAyari("Hizli Rota (Heuristic Odakli)", 0.5),
                    new AgirlikAyari("Dengeli Rota (Standart A*)", 1.0),
                    new AgirlikAyari("Guvenli Rota (Maliyet Odakli)", 2.0)
                };

                foreach (var senaryo in testSenaryolari)
                {
                    Console.WriteLine($"\n--- A* ALGORITMASI: {senaryo.Isim} ---");
                    AStarAlgorithm astar = new AStarAlgorithm(graph.NodeCount, graph.NodeCoordinates, senaryo.Deger);
                    astar.Calistir(graph.AdjacencyList, baslangicDugumu, hedefDugumu);
                    astar.SonuclariYazdir();
                    Console.WriteLine(new string('-', 60));
                }

                Console.WriteLine("\nTum algoritmalar basariyla tamamlandi!");
                Console.WriteLine(new string('=', 60));
                
                // Sonuclari gormek icin bekle
                Console.WriteLine("\nSonuclari gormek icin yukari kaydirin.");
                Console.WriteLine("Ana menuye donmek icin bir tusa basin...");
                Console.ReadKey();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\nHATA: {ex.Message}");
                Console.WriteLine($"   Detay: {ex.StackTrace}");
                Console.WriteLine("\nDevam etmek icin bir tusa basin...");
                Console.ReadKey();
            }
            finally
            {
                // Bellek temizligi - buyuk dosyalar icin onemli
                Console.WriteLine("\nBellek temizleniyor...");
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                
                long memoryAfter = GC.GetTotalMemory(true) / 1024 / 1024;
                Console.WriteLine($"Temizlik sonrasi bellek: {memoryAfter} MB");
            }
        }

        /// <summary>
        /// Verilen dosya yolundaki harita dosyasini uzantisina gore ayristirir.
        /// </summary>
        private static GraphData ParseMap(string filePath)
        {
            string extension = Path.GetExtension(filePath).ToLower();
            
            switch (extension)
            {
                case ".osm":
                    Console.WriteLine("Parser: OpenStreetMap (OSM)");
                    var osmParser = new OsmParser();
                    return osmParser.Parse(filePath);
                case ".xodr":
                    Console.WriteLine("Parser: OpenDRIVE (XODR)");
                    var xodrParser = new XodrParser();
                    return xodrParser.Parse(filePath);
                default:
                    throw new NotSupportedException($"Desteklenmeyen dosya uzantisi: {extension}. Yalnizca .osm ve .xodr desteklenir.");
            }
        }

        // Dijkstra yolunu cikar
        private static int[] GetPathFromDijkstra(DijkstraAlgoritmasi dijkstra, int hedef)
        {
            var path = new List<int>();
            var onceki = typeof(DijkstraAlgoritmasi).GetField("oncekiDugumler", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(dijkstra) as int[];
            int node = hedef;
            while (node != -1)
            {
                path.Insert(0, node);
                node = onceki[node];
            }
            return path.ToArray();
        }

        // A* yolunu cikar
        private static int[] GetPathFromAStar(AStarAlgorithm astar, int hedef)
        {
            var path = new List<int>();
            var onceki = typeof(AStarAlgorithm).GetField("oncekiDugumler", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(astar) as int[];
            int node = hedef;
            while (node != -1)
            {
                path.Insert(0, node);
                node = onceki[node];
            }
            return path.ToArray();
        }

        private static int GetNodeInput(string prompt, int maxNode)
        {
            int node;
            do
            {
                Console.Write(prompt);
                string input = Console.ReadLine();
                if (int.TryParse(input, out node) && node >= 1 && node <= maxNode)
                    return node;
                Console.WriteLine($"Gecersiz giris! 1 ile {maxNode} arasinda bir sayi girin.");
            } while (true);
        }
    }
}
