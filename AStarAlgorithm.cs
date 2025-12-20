using System;
using System.Collections.Generic;
using System.Linq;

namespace ConsoleApp3
{
    /// <summary>
    /// Sezgisel fonksiyonun etkisini bir agirlik parametresi ile ayarlayan
    /// Agirlikli A* (Weighted A*) algoritmasini uygular.
    /// Adjacency List kullanarak bellek verimli calisir.
    /// </summary>
    public class AStarAlgorithm
    {
        private readonly int dugumSayisi;
        private readonly Point[] dugumKoordinatlari;
        private readonly double agirlik;
        private readonly Dictionary<string, int> nodeIdToIndexMap;

        private double[] gScore;
        private double[] fScore;
        private int[] oncekiDugumler;
        private int baslangicDugumu;
        private int hedefDugumu;

        /// <summary>
        /// Agirlikli A* algoritmasini baslatir.
        /// </summary>
        public AStarAlgorithm(int dugumSayisi, Point[] koordinatlar, double agirlik = 1.0)
        {
            if (agirlik < 0) throw new ArgumentOutOfRangeException(nameof(agirlik), "Agirlik negatif olamaz.");

            this.dugumSayisi = dugumSayisi;
            this.dugumKoordinatlari = koordinatlar;
            this.agirlik = agirlik;
            this.nodeIdToIndexMap = null;
        }

        /// <summary>
        /// A* algoritmasini baslatir (Node ID destegi ile).
        /// </summary>
        public AStarAlgorithm(int dugumSayisi, Point[] koordinatlar, Dictionary<string, int> nodeIdToIndexMap, double agirlik = 1.0)
        {
            if (agirlik < 0) throw new ArgumentOutOfRangeException(nameof(agirlik), "Agirlik negatif olamaz.");

            this.dugumSayisi = dugumSayisi;
            this.dugumKoordinatlari = koordinatlar;
            this.agirlik = agirlik;
            this.nodeIdToIndexMap = nodeIdToIndexMap;
        }

        /// <summary>
        /// Heuristic fonksiyonu - iki dugum arasindaki cografi uzakligi hesaplar
        /// </summary>
        private double Heuristic(int from, int to)
        {
            if (dugumKoordinatlari == null) return 0;

            Point fromPoint = dugumKoordinatlari[from];
            Point toPoint = dugumKoordinatlari[to];

            double dx = fromPoint.X - toPoint.X;
            double dy = fromPoint.Y - toPoint.Y;
            double dz = fromPoint.Z - toPoint.Z;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        /// <summary>
        /// Algortimayi calistirir - Adjacency List kullanarak (bellek verimli)
        /// </summary>
        public void Calistir(Dictionary<int, List<Edge>> adjacencyList, int baslangic, int hedef)
        {
            this.baslangicDugumu = baslangic;
            this.hedefDugumu = hedef;

            gScore = new double[dugumSayisi];
            fScore = new double[dugumSayisi];
            oncekiDugumler = new int[dugumSayisi];

            // Priority Queue kullanarak performans optimizasyonu
            var openSet = new SortedSet<(double fScore, int dugum)>();
            var openSetHash = new HashSet<int> { baslangicDugumu };
            var closedSet = new bool[dugumSayisi];

            for (int i = 0; i < dugumSayisi; i++)
            {
                gScore[i] = double.MaxValue;
                fScore[i] = double.MaxValue;
                oncekiDugumler[i] = -1;
            }

            gScore[baslangicDugumu] = 0;
            fScore[baslangicDugumu] = agirlik * Heuristic(baslangicDugumu, hedefDugumu);
            openSet.Add((fScore[baslangicDugumu], baslangicDugumu));

            while (openSet.Count > 0)
            {
                var current = openSet.Min;
                int currentNode = current.dugum;

                if (currentNode == hedefDugumu)
                {
                    return; // Hedefe ulasildi
                }

                openSet.Remove(current);
                openSetHash.Remove(currentNode);
                closedSet[currentNode] = true;

                // Sadece gercek komsular uzerinde don (Adjacency List avantaji)
                if (!adjacencyList.ContainsKey(currentNode))
                    continue;

                foreach (var edge in adjacencyList[currentNode])
                {
                    int neighbor = edge.ToNodeIndex;
                    double yolMaliyeti = edge.Weight;

                    if (closedSet[neighbor]) continue;

                    double tentativeGScore = gScore[currentNode] + yolMaliyeti;

                    if (!openSetHash.Contains(neighbor))
                    {
                        openSetHash.Add(neighbor);
                    }
                    else if (tentativeGScore >= gScore[neighbor])
                    {
                        continue;
                    }

                    // Eski f score varsa kaldir
                    if (fScore[neighbor] != double.MaxValue)
                    {
                        openSet.Remove((fScore[neighbor], neighbor));
                    }

                    oncekiDugumler[neighbor] = currentNode;
                    gScore[neighbor] = tentativeGScore;
                    fScore[neighbor] = gScore[neighbor] + (agirlik * Heuristic(neighbor, hedefDugumu));

                    openSet.Add((fScore[neighbor], neighbor));
                }
            }
        }

        /// <summary>
        /// Node ID kullanarak algoritmayi calistirir
        /// </summary>
        public void Calistir(Dictionary<int, List<Edge>> adjacencyList, string baslangicNodeId, string hedefNodeId)
        {
            if (nodeIdToIndexMap == null)
            {
                throw new InvalidOperationException("Node ID mapping yapilandirilmamis.");
            }

            if (!nodeIdToIndexMap.ContainsKey(baslangicNodeId))
            {
                throw new ArgumentException($"Baslangic Node ID bulunamadi: {baslangicNodeId}");
            }

            if (!nodeIdToIndexMap.ContainsKey(hedefNodeId))
            {
                throw new ArgumentException($"Hedef Node ID bulunamadi: {hedefNodeId}");
            }

            int baslangicIndex = nodeIdToIndexMap[baslangicNodeId];
            int hedefIndex = nodeIdToIndexMap[hedefNodeId];

            Calistir(adjacencyList, baslangicIndex, hedefIndex);
        }

        /// <summary>
        /// Sonuclari ekrana yazdirir
        /// </summary>
        public void SonuclariYazdir()
        {
            if (gScore == null)
            {
                Console.WriteLine("Once Calistir() metodunu cagirmalisiniz.");
                return;
            }

            Console.WriteLine($"  Agirlik Parametresi: {this.agirlik}");
            Console.WriteLine();

            if (gScore[hedefDugumu] == double.MaxValue)
            {
                Console.WriteLine($"  Hedef Dugum {hedefDugumu + 1} -> Ulasilamiyor");
                return;
            }

            Stack<int> yol = new Stack<int>();
            int mevcutDugum = hedefDugumu;
            while (mevcutDugum != -1)
            {
                yol.Push(mevcutDugum);
                mevcutDugum = oncekiDugumler[mevcutDugum];
            }

            int[] yolDizisi = yol.ToArray();

            double fizikselUzunluk = 0;
            for (int i = 0; i < yolDizisi.Length - 1; i++)
            {
                fizikselUzunluk += Heuristic(yolDizisi[i], yolDizisi[i + 1]);
            }

            Console.WriteLine($"  Toplam Maliyet: {gScore[hedefDugumu]:F2}");
            Console.WriteLine($"  Fiziksel Yol Uzunlugu: {fizikselUzunluk:F2}");
            Console.WriteLine($"  Adim Sayisi: {yolDizisi.Length}");
            Console.Write("  Yol: ");
            Console.WriteLine(string.Join(" -> ", yolDizisi.Select(dugum => (dugum + 1).ToString())));
        }

        /// <summary>
        /// En kisa yolu liste olarak dondurur
        /// </summary>
        public List<int> EnKisaYoluAl()
        {
            if (gScore == null)
            {
                throw new InvalidOperationException("Once Calistir() metodunu cagirmalisiniz.");
            }

            if (gScore[hedefDugumu] == double.MaxValue)
            {
                return null;
            }

            var yol = new List<int>();
            int mevcutDugum = hedefDugumu;
            while (mevcutDugum != -1)
            {
                yol.Insert(0, mevcutDugum);
                mevcutDugum = oncekiDugumler[mevcutDugum];
            }
            return yol;
        }

        /// <summary>
        /// Hedefe olan toplam maliyeti dondurur
        /// </summary>
        public double ToplamMaliyetAl()
        {
            if (gScore == null)
            {
                throw new InvalidOperationException("Once Calistir() metodunu cagirmalisiniz.");
            }
            return gScore[hedefDugumu];
        }

        /// <summary>
        /// Yolun fiziksel uzunlugunu hesaplar
        /// </summary>
        public double FizikselUzunlukAl()
        {
            var yol = EnKisaYoluAl();
            if (yol == null || yol.Count < 2) return 0;

            double fizikselUzunluk = 0;
            for (int i = 0; i < yol.Count - 1; i++)
            {
                fizikselUzunluk += Heuristic(yol[i], yol[i + 1]);
            }
            return fizikselUzunluk;
        }
    }
}
