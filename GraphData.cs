using System;
using System.Collections.Generic;
using System.Linq;


namespace ConsoleApp3
{
    
    /// Graf kenarini temsil eder (Adjacency List icin)
    
    public struct Edge
    {
        public int ToNodeIndex { get; }
        public string ToNodeId { get; set; }
        public double Weight { get; }
        // Yeni: Yol ID - Eger bu kenar bir yola (road/way) aitse
        public string RoadId { get; set; }
        // Yeni: Lane ID
        public int LaneId { get; set; }
        public Edge(int toNodeIndex, double weight)
        {
            ToNodeIndex = toNodeIndex;
            Weight = weight;
        }
        public Edge(string toNodeId, double weight)
        {
            ToNodeId = toNodeId;
            Weight = weight;
            ToNodeIndex = -1;
        }
    }
    public class JunctionInfo
    {
        public string Id { get; set; }
        // Gelen Road ID -> List of bağlanan Road IDs
        public Dictionary<string, List<string>> Connections { get; set; } = new Dictionary<string, List<string>>();
    }

    /// Ayristirilmis harita verilerinden olusturulan grafi temsil eder.
    /// Adjacency List kullanarak bellek verimli calisir.

    public class GraphData
    {
        public int NodeCount { get; private set; }
        public int EdgeCount { get; private set; }
        /// Komsuluk listesi - buyuk graflar icin verimli
        /// Key: Dugum indeksi, Value: Bu dugumden cikan kenarlarin listesi

        public Dictionary<int, List<Edge>> AdjacencyList { get; private set; }
        /// Dugum koordinatlari  
        public Dictionary<string, Point>> Point[] NodeCoordinates { get; private set; }
        public Point[] NodeCoordinatesArray { get; private set; }
        //Orijinal dosyadaki ID'leri (orn: OSM node ID, XODR junction ID) dizi indeksine esler.
        public Dictionary<string, int> NodeIdToIndexMap { get; private set; }
        // Graf'taki toplam kenar sayisi   
        public int EdgeCount { get; } 
        /// Adjacency List ile GraphData olusturur (bellek verimli)
        public GraphData(Dictionary<int, List<Edge>> adjacencyList, Point[] nodeCoordinates, Dictionary<string, int> nodeIdToIndexMap)
        {
            AdjacencyList = adjacencyList;
            NodeCoordinates = nodeCoordinates;
            NodeIdToIndexMap = nodeIdToIndexMap;

            // Kenar sayisini hesapla
            int edgeCount = 0;
            if (adjacencyList != null)
            {
                foreach (var edges in adjacencyList.Values)
                {
                    edgeCount += edges.Count;
                }
            }
            EdgeCount = edgeCount;
        }

        
        /// Belirli bir dugumun komsularini dondurur
        
        public IEnumerable<Edge> GetNeighbors(int nodeIndex)
        {
            if (AdjacencyList != null && AdjacencyList.TryGetValue(nodeIndex, out var edges))
            {
                return edges;
            }
            return System.Array.Empty<Edge>();
        }

        
        /// Iki dugum arasindaki kenar agirligini dondurur
        
        public double GetEdgeWeight(int fromNode, int toNode)
        {
            if (AdjacencyList != null && AdjacencyList.TryGetValue(fromNode, out var edges))
            {
                foreach (var edge in edges)
                {
                    if (edge.ToNodeIndex == toNode)
                        return edge.Weight;
                }
            }
            return double.PositiveInfinity;
        }

        
        /// Iki dugum arasinda kenar var mi kontrol eder
        
        public bool HasEdge(int fromNode, int toNode)
        {
            return !double.IsPositiveInfinity(GetEdgeWeight(fromNode, toNode));
        }
    }
}