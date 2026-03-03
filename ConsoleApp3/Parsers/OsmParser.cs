using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using ConsoleApp3.Utils;
using ConsoleApp3;

namespace ConsoleApp3.Parsers
{
    
    /// .osm (OpenStreetMap) dosyalarini ayristirarak bir graf modeli olusturur.
    /// Gercek OSM node'lari dugum olarak alinir, ardisik node'lar arasi mesafe kenar olarak eklenir.
    
    public class OsmParser
    {
        
        /// Bir .osm dosyasini ayristirir ve graf verisi olusturur.
        
        public GraphData Parse(string filePath)
        {
            Console.WriteLine("OSM dosyasi ayristiriliyor...");
            XDocument doc = XDocument.Load(filePath);
            var ns = doc.Root.Name.Namespace;
            
            // 1. Adim: Tum dugumleri (node) ve koordinatlarini oku
            Console.WriteLine("OSM node'lari okunuyor...");
            var nodeElements = doc.Descendants(ns + "node").ToList();
            Console.WriteLine($"Bulunan toplam OSM node sayisi: {nodeElements.Count}");
            
            var nodeCoords = new Dictionary<string, Point>();
            foreach (var node in nodeElements)
            {
                string nodeId = node.Attribute("id")?.Value;
                if (string.IsNullOrEmpty(nodeId)) continue;
                
                double lon = double.Parse(node.Attribute("lon")?.Value ?? "0");
                double lat = double.Parse(node.Attribute("lat")?.Value ?? "0");
                double ele = node.Attribute("ele") != null ? double.Parse(node.Attribute("ele").Value) : 0;
                
                nodeCoords[nodeId] = new Point(lon, lat, ele);
            }
            Console.WriteLine($"Koordinat cikarilan node sayisi: {nodeCoords.Count}");

            // 2. Adim: Sadece "highway" olarak etiketlenmis yollari (way) bul
            var highways = doc.Descendants(ns + "way")
                .Where(w => w.Elements(ns + "tag").Any(t => (string)t.Attribute("k") == "highway"))
                .ToList();
            Console.WriteLine($"Bulunan highway sayisi: {highways.Count}");

            // 3. Adim: Graf icin gerekli dugum kumesini bul (way'lerde kullanilan node'lar)
            var usedNodeIds = new HashSet<string>();
            foreach (var way in highways)
            {
                foreach (var nd in way.Elements(ns + "nd"))
                {
                    string refId = nd.Attribute("ref")?.Value;
                    if (!string.IsNullOrEmpty(refId) && nodeCoords.ContainsKey(refId))
                    {
                        usedNodeIds.Add(refId);
                    }
                }
            }
            Console.WriteLine($"Highway'lerde kullanilan benzersiz node sayisi: {usedNodeIds.Count}");

            // 4. Adim: Kullanilan node'lari dugum listesine ekle
            var nodeList = usedNodeIds.ToList();
            var nodeIdToIndex = new Dictionary<string, int>();
            for (int i = 0; i < nodeList.Count; i++)
            {
                nodeIdToIndex[nodeList[i]] = i;
            }

            // 5. Adim: Adjacency List olustur (matris yerine - bellek verimli)
            var adjacencyList = new Dictionary<int, List<Edge>>();

            var coordinates = new Point[nodeList.Count];
            for (int i = 0; i < nodeList.Count; i++)
            {
                coordinates[i] = nodeCoords[nodeList[i]];
            }

            // 6. Adim: Yollari isle ve baglantilari ekle
            int oneWayCount = 0;
            int twoWayCount = 0;
            int reverseOnlyCount = 0;
            int implicitOneWayCount = 0;
            int totalEdgeCount = 0;

            foreach (var way in highways)
            {
                // Explicit oneway etiketi kontrolu
                var onewayTag = way.Elements(ns + "tag").FirstOrDefault(t => (string)t.Attribute("k") == "oneway");
                string onewayValue = onewayTag?.Attribute("v")?.Value;
                bool isOneWayForward = onewayValue == "yes" || onewayValue == "true" || onewayValue == "1";
                bool isOneWayReverse = onewayValue == "-1";
                bool isTwoWay = !isOneWayForward && !isOneWayReverse;

                // Implicit oneway kurallari (OSM standartlari)
                // junction=roundabout -> her zaman tek yonlu (saat yonunun tersi)
                // highway=motorway veya motorway_link -> genelde tek yonlu
                var junctionTag = way.Elements(ns + "tag").FirstOrDefault(t => (string)t.Attribute("k") == "junction")?.Attribute("v")?.Value;
                var highwayTag = way.Elements(ns + "tag").FirstOrDefault(t => (string)t.Attribute("k") == "highway")?.Attribute("v")?.Value;

                bool isRoundabout = junctionTag == "roundabout" || junctionTag == "circular";
                bool isMotorway = highwayTag == "motorway" || highwayTag == "motorway_link";
                bool isTrunk = highwayTag == "trunk" || highwayTag == "trunk_link";

                // Implicit oneway: Roundabout ve Motorway
                if (isTwoWay && (isRoundabout || isMotorway))
                {
                    isOneWayForward = true;
                    isTwoWay = false;
                    implicitOneWayCount++;
                }

                // Istatistik
                if (isOneWayForward) oneWayCount++;
                else if (isOneWayReverse) reverseOnlyCount++;
                else twoWayCount++;

                // Way icindeki ardisik node'lari bagla
                var nodeRefs = way.Elements(ns + "nd")
                    .Select(nd => nd.Attribute("ref")?.Value)
                    .Where(id => !string.IsNullOrEmpty(id) && nodeCoords.ContainsKey(id))
                    .ToList();

                for (int i = 0; i < nodeRefs.Count - 1; i++)
                {
                    string fromId = nodeRefs[i];
                    string toId = nodeRefs[i + 1];


                    if (!nodeIdToIndex.TryGetValue(fromId, out int fromIdx) || 
                        !nodeIdToIndex.TryGetValue(toId, out int toIdx))
                        continue;

                    // Iki node arasi mesafeyi Haversine ile hesapla (metre)
                    double distance = GeoUtils.HaversineDistance(
                        coordinates[fromIdx].Y, coordinates[fromIdx].X,  // lat, lon
                        coordinates[toIdx].Y, coordinates[toIdx].X);

                    if (isOneWayReverse)
                    {
                        // Sadece ters yon
                        if (!adjacencyList.ContainsKey(toIdx))
                            adjacencyList[toIdx] = new List<Edge>();
                        
                        var edge = new Edge(fromIdx, distance);
                        edge.RoadId = way.Attribute("id")?.Value;
                        adjacencyList[toIdx].Add(edge);
                        totalEdgeCount++;
                    }
                    else if (isOneWayForward)
                    {
                        // Sadece ileri yon
                        if (!adjacencyList.ContainsKey(fromIdx))
                            adjacencyList[fromIdx] = new List<Edge>();
                        
                        var edge = new Edge(toIdx, distance);
                        edge.RoadId = way.Attribute("id")?.Value;
                        adjacencyList[fromIdx].Add(edge);
                        totalEdgeCount++;
                    }
                    else // isTwoWay
                    {
                        string wayId = way.Attribute("id")?.Value;
                        
                        // Cift yon
                        if (!adjacencyList.ContainsKey(fromIdx))
                            adjacencyList[fromIdx] = new List<Edge>();
                        
                        var edge1 = new Edge(toIdx, distance);
                        edge1.RoadId = wayId;
                        adjacencyList[fromIdx].Add(edge1);

                        if (!adjacencyList.ContainsKey(toIdx))
                            adjacencyList[toIdx] = new List<Edge>();
                        
                        var edge2 = new Edge(fromIdx, distance);
                        edge2.RoadId = wayId;
                        adjacencyList[toIdx].Add(edge2);
                        totalEdgeCount += 2;
                    }
                }
            }
            
            Console.WriteLine($"OSM graf olusturuldu. Dugum sayisi: {nodeList.Count}, kenar sayisi: {totalEdgeCount}");
            Console.WriteLine($"Yol tipi dagilimi: Iki yonlu: {twoWayCount}, Tek yonlu ileri: {oneWayCount}, Tek yonlu geri: {reverseOnlyCount}");
            if (implicitOneWayCount > 0)
            {
                Console.WriteLine($"  (Roundabout/Motorway implicit tek yon: {implicitOneWayCount})");
            }

            // Road Mapping olustur (Way ID -> Start/End Refs)
            var wayMapping = new Dictionary<string, (string StartId, string EndId)>();
            foreach (var way in highways)
            {
                string wayId = way.Attribute("id")?.Value;
                if(string.IsNullOrEmpty(wayId)) continue;

                var refs = way.Elements(ns + "nd")
                              .Select(nd => nd.Attribute("ref")?.Value)
                              .Where(r => !string.IsNullOrEmpty(r) && nodeCoords.ContainsKey(r))
                              .ToList();

                if (refs.Count >= 2)
                {
                    wayMapping[wayId] = (refs.First(), refs.Last());
                }
            }

            return new GraphData(adjacencyList, coordinates, nodeIdToIndex, wayMapping);
        }
    }
}
