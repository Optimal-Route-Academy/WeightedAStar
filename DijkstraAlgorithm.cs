using System;
using System.Collections.Generic;

namespace ConsoleApp3
{
    /// <summary>
    /// Bir baslangic dugumunden graf'taki diger tum dugumlere en kisa yollari
    /// hesaplamak icin Dijkstra'nin algoritmasini uygular.
    /// Adjacency List kullanarak bellek verimli calisir.
    /// </summary>
    public class DijkstraAlgoritmasi
    {
        private readonly int dugumSayisi;
        private double[] mesafeler;
        private int[] oncekiDugumler;
        private int baslangicDugumu;
        private readonly Dictionary<string, int> nodeIdToIndexMap;

        /// <summary>
        /// Dijkstra algoritmasi icin gerekli olan temel parametreleri ayarlar.
        /// </summary>
        public DijkstraAlgoritmasi(int dugumSayisi)
        {
            this.dugumSayisi = dugumSayisi;
            this.nodeIdToIndexMap = null;
        }

        /// <summary>
        /// Dijkstra algoritmasi icin gerekli olan temel parametreleri ayarlar (Node ID destegi ile).
        /// </summary>
        public DijkstraAlgoritmasi(int dugumSayisi, Dictionary<string, int> nodeIdToIndexMap)
        {
            this.dugumSayisi = dugumSayisi;
            this.nodeIdToIndexMap = nodeIdToIndexMap;
        }

        /// <summary>
        /// Algoritmayi calistirarak en kisa yollari hesaplar - Adjacency List ile (bellek verimli)
        /// </summary>
        public void Calistir(Dictionary<int, List<Edge>> adjacencyList, int baslangicDugumu)
        {
            this.baslangicDugumu = baslangicDugumu;
            mesafeler = new double[dugumSayisi];
            oncekiDugumler = new int[dugumSayisi];
            bool[] ziyaretEdildi = new bool[dugumSayisi];

            for (int i = 0; i < dugumSayisi; i++)
            {
                mesafeler[i] = double.PositiveInfinity;
                oncekiDugumler[i] = -1;
                ziyaretEdildi[i] = false;
            }

            mesafeler[baslangicDugumu] = 0;

            // Priority Queue kullanarak performans optimizasyonu
            var priorityQueue = new SortedSet<(double mesafe, int dugum)>();
            priorityQueue.Add((0, baslangicDugumu));

            while (priorityQueue.Count > 0)
            {
                var current = priorityQueue.Min;
                priorityQueue.Remove(current);
                int currentNode = current.dugum;

                if (ziyaretEdildi[currentNode]) continue;
                ziyaretEdildi[currentNode] = true;

                // Sadece gercek komsular uzerinde don (Adjacency List avantaji)
                if (!adjacencyList.ContainsKey(currentNode))
                    continue;

                foreach (var edge in adjacencyList[currentNode])
                {
                    int neighbor = edge.ToNodeIndex;
                    double kenarAgirligi = edge.Weight;

                    if (!ziyaretEdildi[neighbor])
                    {
                        double yeniMesafe = mesafeler[currentNode] + kenarAgirligi;
                        if (yeniMesafe < mesafeler[neighbor])
                        {
                            // Eski deger varsa kaldir
                            if (!double.IsPositiveInfinity(mesafeler[neighbor]))
                            {
                                priorityQueue.Remove((mesafeler[neighbor], neighbor));
                            }

                            mesafeler[neighbor] = yeniMesafe;
                            oncekiDugumler[neighbor] = currentNode;
                            priorityQueue.Add((yeniMesafe, neighbor));
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Node ID kullanarak algoritmayi calistirir
        /// </summary>
        public void Calistir(Dictionary<int, List<Edge>> adjacencyList, string baslangicNodeId)
        {
            if (nodeIdToIndexMap == null || !nodeIdToIndexMap.ContainsKey(baslangicNodeId))
            {
                throw new ArgumentException($"Node ID bulunamadi: {baslangicNodeId}");
            }

            int baslangicIndex = nodeIdToIndexMap[baslangicNodeId];
            Calistir(adjacencyList, baslangicIndex);
        }

        /// <summary>
        /// Algoritma calistiktan sonra hesaplanan en kisa yollari ve mesafeleri konsola yazdirir.
        /// </summary>
        public void SonuclariYazdir()
        {
            if (mesafeler == null || oncekiDugumler == null)
            {
                Console.WriteLine("Once Calistir() metodunu cagirmalisiniz.");
                return;
            }

            Console.WriteLine("\nDijkstra Algoritmasi Sonuclari");
            Console.WriteLine("=================================");
            Console.WriteLine($"Baslangic Dugumu: {baslangicDugumu + 1}\n");

            for (int i = 0; i < dugumSayisi; i++)
            {
                Console.Write($"Hedef Dugum: {i + 1} \t Maliyet: ");
                if (double.IsPositiveInfinity(mesafeler[i]))
                {
                    Console.WriteLine("Ulasilamiyor");
                    continue;
                }
                Console.Write($"{mesafeler[i]:F2} \t\t Yol: ");

                Stack<int> yol = new Stack<int>();
                int mevcutDugum = i;
                while (mevcutDugum != -1)
                {
                    yol.Push(mevcutDugum);
                    mevcutDugum = oncekiDugumler[mevcutDugum];
                }

                bool ilk = true;
                while (yol.Count > 0)
                {
                    if (!ilk) Console.Write(" -> ");
                    Console.Write(yol.Pop() + 1);
                    ilk = false;
                }
                Console.WriteLine();
            }
        }

        /// <summary>
        /// Sadece belirli bir hedef dugum icin sonucu yazdirir
        /// </summary>
        public void SonuclariYazdir(int hedefDugum)
        {
            if (mesafeler == null || oncekiDugumler == null)
            {
                Console.WriteLine("Once Calistir() metodunu cagirmalisiniz.");
                return;
            }

            Console.WriteLine();
            Console.WriteLine($"  Baslangic: Dugum {baslangicDugumu + 1}");
            Console.WriteLine($"  Hedef: Dugum {hedefDugum + 1}");
            Console.Write($"  Maliyet: ");

            if (double.IsPositiveInfinity(mesafeler[hedefDugum]))
            {
                Console.WriteLine("Ulasilamiyor");
                return;
            }

            Console.WriteLine($"{mesafeler[hedefDugum]:F2}");

            Stack<int> yol = new Stack<int>();
            int mevcutDugum = hedefDugum;
            while (mevcutDugum != -1)
            {
                yol.Push(mevcutDugum);
                mevcutDugum = oncekiDugumler[mevcutDugum];
            }

            Console.Write($"  Yol: ");
            bool ilk = true;
            while (yol.Count > 0)
            {
                if (!ilk) Console.Write(" -> ");
                Console.Write(yol.Pop() + 1);
                ilk = false;
            }
            Console.WriteLine();
        }

        /// <summary>
        /// Node ID kullanarak sonuc yazdirir
        /// </summary>
        public void SonuclariYazdir(string hedefNodeId)
        {
            if (nodeIdToIndexMap == null || !nodeIdToIndexMap.ContainsKey(hedefNodeId))
            {
                Console.WriteLine($"Node ID bulunamadi: {hedefNodeId}");
                return;
            }

            int hedefIndex = nodeIdToIndexMap[hedefNodeId];
            SonuclariYazdir(hedefIndex);
        }

        /// <summary>
        /// Belirli bir hedefe en kisa yolu dondurur
        /// </summary>
        public List<int> EnKisaYoluAl(int hedefDugum)
        {
            if (mesafeler == null || oncekiDugumler == null)
            {
                throw new InvalidOperationException("Once Calistir() metodunu cagirmalisiniz.");
            }

            if (double.IsPositiveInfinity(mesafeler[hedefDugum]))
            {
                return null;
            }

            var yol = new List<int>();
            int mevcutDugum = hedefDugum;
            while (mevcutDugum != -1)
            {
                yol.Insert(0, mevcutDugum);
                mevcutDugum = oncekiDugumler[mevcutDugum];
            }
            return yol;
        }

        /// <summary>
        /// Belirli bir hedefe olan mesafeyi dondurur
        /// </summary>
        public double MesafeAl(int hedefDugum)
        {
            if (mesafeler == null)
            {
                throw new InvalidOperationException("Once Calistir() metodunu cagirmalisiniz.");
            }
            return mesafeler[hedefDugum];
        }
    }
}
