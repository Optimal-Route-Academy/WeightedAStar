using ConsoleApp3.Parsers;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;


namespace ConsoleApp3
{
    
    /// Graf kenarini temsil eder (Adjacency List icin)
    
    public class Edge
    {
        public int ToNodeIndex { get; set; }
        public string ToNodeId { get; set; }
        public double Weight { get; set; }
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
        //Gelen Yol ID -> Bağlantılı Yol ID'leri Listesi
        public Dictionary<string, List<string>> Connections { get; set; } = new Dictionary<string, List<string>>();
    }


    public class GraphData
    {
        public int NodeCount { get; private set; }
        public int EdgeCount { get; private set; }
        public Dictionary<int, List<Edge>> AdjacencyList { get; private set; }
        public Dictionary<string, Point> NodeCoordinates { get; private set; }
        public Point[] NodeCoordinatesArray { get; private set; }
        public Dictionary<string, int> NodeIdToIndexMap { get; private set; }
        // Yol ID -> (BaşlangıçDüğümEndeksi, BitişDüğümEndeksi) eşlemesinin şeritleri destekleyecek şekilde güncellenmesi mi gerekiyor?
        // Aslında, şeritlerle birlikte muhtemelen bir bileşik anahtar (composite key) eşlemesine ihtiyacımız olacak.
        // Geriye dönük uyumluluk veya gerekirse basit yol bulma işlemleri için RoadIdToNodeIndices yapısını koruyalım,
        // ama şerit seviyesi (lane-level) için RoadId + LaneId -> DüğümEndeksi yapısına ihtiyacımız var.

        //Harita: (Yol ID, Şerit ID) -> Düğüm İndisi(Mantıksal Başlangıç)
        public Dictionary<(string RoadId, int LaneId), int> LaneNodeIndices { get; private set; }

        public Dictionary<string, (int StartIndex, int EndIndex)> RoadIdToNodeIndices { get; private set; }
        public Dictionary<string, JunctionInfo> Junctions { get; private set; }

        public GraphData(Dictionary<int, List<Edge>> adjacencyList, Point[] coordinates, Dictionary<string, int> nodeIdToIndex, Dictionary<string, (string StartId, string EndId)> roadIdToRefIds = null)
        {
            AdjacencyList = adjacencyList;
            NodeCoordinatesArray = coordinates;
            NodeIdToIndexMap = nodeIdToIndex;
            NodeCount = coordinates.Length;

            //ID(Kimlik) sorgulamaları için NodeCoordinates sözlüğünü(dictionary) verilerle doldur.
            NodeCoordinates = new Dictionary<string, Point>();
            foreach (var kvp in nodeIdToIndex)
            {
                if (kvp.Value >= 0 && kvp.Value < coordinates.Length)
                    NodeCoordinates[kvp.Key] = coordinates[kvp.Value];
            }

            EdgeCount = 0;
            foreach (var list in adjacencyList.Values) EdgeCount += list.Count;

            BuildRoadIndexMap(roadIdToRefIds);
            Junctions = new Dictionary<string, JunctionInfo>();
            LaneNodeIndices = new Dictionary<(string, int), int>();
        }


        //XodrParser için Yapıcı Metot(Constructor)
        public GraphData(Dictionary<string, List<Edge>> adjListString, Dictionary<string, Point> nodeCoords,
            Dictionary<string, (string StartId, string EndId)> roadIdToRefIds = null,
            Dictionary<string, JunctionInfo> junctions = null)
        {
            //string-tabanlı düğüm ID'lerinden integer-tabanlı indekslere dönüşüm yapan bir graf oluşturma metodudu olacaktır. Daha tamamlanmamıştır 
            BuildRoadIndexMap(roadIdToRefIds);
        }
        private void BuildRoadIndexMap(Dictionary<string, (string StartId, string EndId)> roadIdToRefIds)
        {
            RoadIdToNodeIndices = new Dictionary<string, (int StartIndex, int EndIndex)>();

            if (roadIdToRefIds == null) return;

            foreach (var kvp in roadIdToRefIds)
            {
                string roadId = kvp.Key;
                string startRef = kvp.Value.StartId;
                string endRef = kvp.Value.EndId;

                if (NodeIdToIndexMap.ContainsKey(startRef) && NodeIdToIndexMap.ContainsKey(endRef))
                {
                    RoadIdToNodeIndices[roadId] = (NodeIdToIndexMap[startRef], NodeIdToIndexMap[endRef]);
                }
            }
        }
    }
}