using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using System.Globalization;
using ConsoleApp3;

namespace ConsoleApp3.Parsers
{
    
    /// OpenDRIVE (.xodr) dosyalarini ayristirarak graf modeli olusturur.
    /// Yol başlangıç/bitiş noktaları ve kavşak bağlantıları düğüm olarak modellenir.
    
    public class XodrParser
    {
        // Sabitler - Magic string'leri önlemek için
        private const string ROAD_PREFIX = "road:";
        private const string JUNCTION_PREFIX = "junction:";
        private const string START_SUFFIX = ":start";
        private const string END_SUFFIX = ":end";
        private const string JUNCTION_ID_DEFAULT = "-1";
        private const double LINK_CONNECTION_DISTANCE = 0.1; // Düğümler arası minimal mesafe

        private readonly CultureInfo culture = CultureInfo.InvariantCulture;

        
        /// Yol uc noktalarini ve koordinatlarini temsil eder
        
        private class RoadInfo
        {
            public string RoadId { get; set; }
            public string StartNodeId { get; set; }
            public string EndNodeId { get; set; }
            public Point StartPoint { get; set; }
            public Point EndPoint { get; set; }
            public double Length { get; set; }
            public string JunctionId { get; set; }
            
            // Yon bilgileri - Serit analizine gore belirlenir
            public bool CanGoForward { get; set; }  // Sag seritler (Start -> End)
            public bool CanGoBackward { get; set; } // Sol seritler (End -> Start)
            
            public RoadLinkInfo LinkInfo { get; set; }

            // Yeni: Serit nesnelerini tutar
            public List<LaneInfo> Lanes { get; set; } = new List<LaneInfo>();

        }

        // Yeni: Serit bilgilerini tutar
        private class LaneInfo
        {
            public int LaneId { get; set; }
            public string Type { get; set; }
            public LaneLinkInfo Link { get; set; }
            public bool IsDrivable => Type == "driving";
        }

        // Yeni: Serit baglanti bilgileri
        private class LaneLinkInfo
        {
            public List<int> PredecessorIds { get; set; } = new List<int>();
            public List<int> SuccessorIds { get; set; } = new List<int>();
        }

        /// Yol bağlantı bilgilerini saklar

        private class RoadLinkInfo
        {
            public LinkElementInfo Predecessor { get; set; }
            public LinkElementInfo Successor { get; set; }
        }

        private class LinkElementInfo
        {
            public string ElementType { get; set; }
            public string ElementId { get; set; }
            public string ContactPoint { get; set; }
        }

        public GraphData Parse(string filePath)
        {
            Console.WriteLine("XODR dosyası ayrıştırılıyor...");
            XDocument doc = XDocument.Load(filePath);
            var roads = doc.Descendants("road").ToList();
            var junctions = doc.Descendants("junction").ToList();

            Console.WriteLine($"Bulunan yol sayısı: {roads.Count}, kavşak sayısı: {junctions.Count}");

            // 1. Adım: Tüm yol bilgilerini tek geçişte topla
            var roadInfos = ParseRoadInformation(roads);

            // 2. Adım: Kavşak koordinatlarını hesapla
            var junctionCoords = CalculateJunctionCoordinates(roadInfos, junctions, roads);

            // 3. Adım: Tüm düğüm koordinatlarını birleştir
            // NOT: Serit bazli modelde dugumler seritlerin kendisi veya uclari olacak.
            // Simdilik mevcut yapiyi koruyarak serit baglantilarini ekleyelim.
            var nodeCoords = BuildNodeCoordinatesMap(roadInfos, junctionCoords);

            // 4. Adım: Bağlantıları oluştur (Artik Serit Bazli)
            var connections = BuildConnections(roadInfos, roads, junctions, nodeCoords);

            // 5. Adım: İstatistikleri yazdır
            PrintStatistics(roadInfos.Count, junctionCoords.Count, connections);

            // Road mapping olustur
            var roadMapping = roadInfos.ToDictionary(
                r => r.RoadId,
                r => (StartId: r.StartNodeId, EndId: r.EndNodeId));
            
            // Junction mapping olustur
            var junctionMapping = new Dictionary<string, JunctionInfo>();
            foreach (var junc in junctions)
            {
                string id = GetAttributeValue(junc, "id");
                if (string.IsNullOrEmpty(id)) continue;
                
                var info = new JunctionInfo { Id = id };
                
                foreach (var conn in junc.Elements("connection"))
                {
                    string incoming = GetAttributeValue(conn, "incomingRoad");
                    string connecting = GetAttributeValue(conn, "connectingRoad");
                    
                    if (!string.IsNullOrEmpty(incoming) && !string.IsNullOrEmpty(connecting))
                    {
                        if (!info.Connections.ContainsKey(incoming))
                            info.Connections[incoming] = new List<string>();
                        
                        info.Connections[incoming].Add(connecting);
                    }
                }
                junctionMapping[id] = info;
            }

            // Lane mapping olustur (RoadId, LaneId -> Node ID map)
            var laneMapping = new Dictionary<(string, int), string>();
            
            foreach(var road in roadInfos)
            {
                foreach(var lane in road.Lanes)
                {
                    bool isRightLane = lane.LaneId < 0;
                    bool useStartNodeAsEntry = isRightLane; 
                    string nodeId = GetLaneNodeId(road.RoadId, lane.LaneId, useStartNodeAsEntry);
                    laneMapping[(road.RoadId, lane.LaneId)] = nodeId;
                }
            }


            // 6. Adım: GraphData nesnesini oluştur ve döndür
            return CreateGraphData(nodeCoords, connections, roadMapping, junctionMapping, laneMapping);
        }

        
        /// Tüm yol bilgilerini tek geçişte ayrıştırır (performans optimizasyonu)
        
        private List<RoadInfo> ParseRoadInformation(List<XElement> roads)
        {
            var roadInfos = new List<RoadInfo>(roads.Count);

            foreach (var road in roads)
            {
                string roadId = GetAttributeValue(road, "id");
                if (string.IsNullOrEmpty(roadId)) continue;

                var planView = road.Element("planView");
                if (planView == null) continue;

                var geometries = planView.Elements("geometry")
                    .OrderBy(g => ParseDouble(g, "s"))
                    .ToList();

                if (geometries.Count == 0) continue;

                // Yonleri analiz et
                bool canForward, canBackward;
                AnalyzeRoadDirections(road, out canForward, out canBackward);

                var roadInfo = new RoadInfo
                {
                    RoadId = roadId,
                    StartNodeId = $"{ROAD_PREFIX}{roadId}{START_SUFFIX}",
                    EndNodeId = $"{ROAD_PREFIX}{roadId}{END_SUFFIX}",
                    Length = ParseDouble(road, "length"),
                    JunctionId = GetAttributeValue(road, "junction"),
                    CanGoForward = canForward,
                    CanGoBackward = canBackward
                };

                // Başlangıç ve bitiş koordinatlarını hesapla
                Point startPt, endPt;
                CalculateRoadEndpoints(geometries, out startPt, out endPt);

                // Elevation profilini oku (Z koordinati icin)
                var elevationProfile = road.Element("elevationProfile");
                double startZ = 0;
                double endZ = 0;

                if (elevationProfile != null)
                {
                    var elevation = elevationProfile.Element("elevation");
                    if (elevation != null)
                    {
                        // s=0 noktasindaki 'a' katsayisi baslangic yuksekligidir
                        double a = ParseDoubleFromAttribute(elevation, "a");
                        startZ = a;

                        // Yolun sonundaki yuksekligi egime gore hesapla
                        double b = ParseDoubleFromAttribute(elevation, "b"); // Egim (Slope)
                        double c = ParseDoubleFromAttribute(elevation, "c"); // Quadratic katsayi
                        double d = ParseDoubleFromAttribute(elevation, "d"); // Cubic katsayi
                        double roadLength = roadInfo.Length;

                        // Kubik polinom: z(s) = a + b*s + c*s^2 + d*s^3
                        endZ = a + b * roadLength + c * roadLength * roadLength + d * roadLength * roadLength * roadLength;
                    }
                }

                // Point olusturulurken Z koordinatini kullan
                roadInfo.StartPoint = new Point(startPt.X, startPt.Y, startZ);
                roadInfo.EndPoint = new Point(endPt.X, endPt.Y, endZ);

                // Link bilgilerini parse et
                var link = road.Element("link");
                if (link != null)
                {
                    roadInfo.LinkInfo = ParseRoadLinkInfo(link);
                }

                // SERITLERI PARSE ET
                var lanes = road.Element("lanes");
                if (lanes != null)
                {
                    // Genelde son laneSection gecerlidir ama karmasik yollarda birden cok olabilir.
                    // Basitlik icin tum laneSection'lardaki unique lane'leri alalim veya sadece ilk/son.
                    // XODR standardinda serit sayisi degisebilir.
                    // Biz en genis/kapsamli olani veya herbirini ayri segment gibi dusunmalıyız.
                    // Proje kapsami geregi genellikle tek bir laneSection varsayalim veya hepsini birlestirelim.

                    var laneSections = lanes.Elements("laneSection").ToList();
                    foreach (var ls in laneSections)
                    {
                        // Sol (-), Sag (+)? Hayir: XODR'da Sol (+), Sag (-)
                        // left elements
                        var left = ls.Element("left");
                        if (left != null) ParseLanes(left, roadInfo, 1); // Positive IDs

                        var right = ls.Element("right");
                        if (right != null) ParseLanes(right, roadInfo, -1); // Negative IDs
                    }
                }


                roadInfos.Add(roadInfo);
            }

            return roadInfos;
        }

        private void ParseLanes(XElement sideElement, RoadInfo roadInfo, int sign)
        {
            var laneElements = sideElement.Elements("lane");
            foreach (var lane in laneElements)
            {
                int id = (int)ParseDoubleFromAttribute(lane, "id"); // int olmali
                string type = GetAttributeValue(lane, "type");

                // Sadece driving seritlerini al
                if (type != "driving") continue;

                var lInfo = new LaneInfo
                {
                    LaneId = id,
                    Type = type,
                    Link = new LaneLinkInfo()
                };

                var link = lane.Element("link");
                if (link != null)
                {
                    var preds = link.Elements("predecessor");
                    foreach (var p in preds) lInfo.Link.PredecessorIds.Add((int)ParseDoubleFromAttribute(p, "id"));

                    var succs = link.Elements("successor");
                    foreach (var s in succs) lInfo.Link.SuccessorIds.Add((int)ParseDoubleFromAttribute(s, "id"));
                }

                roadInfo.Lanes.Add(lInfo);
            }
        }


        /// Yol bağlantı bilgilerini parse eder

        private RoadLinkInfo ParseRoadLinkInfo(XElement linkElement)
        {
            var linkInfo = new RoadLinkInfo();

            var pred = linkElement.Element("predecessor");
            if (pred != null)
            {
                linkInfo.Predecessor = new LinkElementInfo
                {
                    ElementType = GetAttributeValue(pred, "elementType"),
                    ElementId = GetAttributeValue(pred, "elementId"),
                    ContactPoint = GetAttributeValue(pred, "contactPoint")
                };
            }

            var succ = linkElement.Element("successor");
            if (succ != null)
            {
                linkInfo.Successor = new LinkElementInfo
                {
                    ElementType = GetAttributeValue(succ, "elementType"),
                    ElementId = GetAttributeValue(succ, "elementId"),
                    ContactPoint = GetAttributeValue(succ, "contactPoint")
                };
            }

            return linkInfo;
        }

        
        /// Geometrilerden yolun baslangic ve bitis noktalarini hesaplar
        
        private void CalculateRoadEndpoints(List<XElement> geometries, out Point startPoint, out Point endPoint)
        {
            var firstGeom = geometries.First();
            double startX = ParseDouble(firstGeom, "x");
            double startY = ParseDouble(firstGeom, "y");
            startPoint = new Point(startX, startY, 0);

            var lastGeom = geometries.Last();
            double lastX = ParseDouble(lastGeom, "x");
            double lastY = ParseDouble(lastGeom, "y");
            double lastLen = ParseDouble(lastGeom, "length");
            double lastHdg = ParseDouble(lastGeom, "hdg");

            // Geometri tipine gore bitis noktasini hesapla
            double endX, endY;

            if (lastGeom.Element("line") != null)
            {
                // Duz cizgi icin basit hesaplama
                endX = lastX + lastLen * Math.Cos(lastHdg);
                endY = lastY + lastLen * Math.Sin(lastHdg);
            }
            else if (lastGeom.Element("arc") != null)
            {
                // Arc icin egri hesaplama
                var arc = lastGeom.Element("arc");
                double curvature = ParseDoubleFromAttribute(arc, "curvature");

                if (Math.Abs(curvature) > 1e-10)
                {
                    double radius = 1.0 / curvature;
                    double angle = lastLen * curvature;

                    endX = lastX + radius * (Math.Sin(lastHdg + angle) - Math.Sin(lastHdg));
                    endY = lastY - radius * (Math.Cos(lastHdg + angle) - Math.Cos(lastHdg));
                }
                else
                {
                    // Curvature sifira yakinsa duz cizgi gibi davran
                    endX = lastX + lastLen * Math.Cos(lastHdg);
                    endY = lastY + lastLen * Math.Sin(lastHdg);
                }
            }
            else if (lastGeom.Element("spiral") != null)
            {
                // Spiral (Clothoid) icin Euler Integrasyonu ile yaklasik hesaplama
                var spiral = lastGeom.Element("spiral");
                double curvStart = ParseDoubleFromAttribute(spiral, "curvStart");
                double curvEnd = ParseDoubleFromAttribute(spiral, "curvEnd");

                // Euler Integrasyonu - kucuk adimlarla ilerleme
                double x = lastX;
                double y = lastY;
                double h = lastHdg;
                double s = 0;
                double ds = 0.5; // 0.5 metrelik adimlarla hesapla (hassasiyet icin)

                while (s < lastLen)
                {
                    if (s + ds > lastLen) ds = lastLen - s;

                    // O andaki egrilik (Lineer enterpolasyon)
                    double k = curvStart + (curvEnd - curvStart) * (s / lastLen);

                    // Koordinatlari guncelle
                    x += ds * Math.Cos(h);
                    y += ds * Math.Sin(h);
                    h += ds * k;

                    s += ds;
                }
                endX = x;
                endY = y;
            }
            else if (lastGeom.Element("poly3") != null)
            {
                // Poly3 (Kubik polinom) icin Euler Integrasyonu
                var poly3 = lastGeom.Element("poly3");
                double a = ParseDoubleFromAttribute(poly3, "a");
                double b = ParseDoubleFromAttribute(poly3, "b");
                double c = ParseDoubleFromAttribute(poly3, "c");
                double d = ParseDoubleFromAttribute(poly3, "d");

                double x = lastX;
                double y = lastY;
                double h = lastHdg;
                double s = 0;
                double ds = 0.5;

                while (s < lastLen)
                {
                    if (s + ds > lastLen) ds = lastLen - s;

                    // v(s) = a + b*s + c*s^2 + d*s^3 (lateral offset)
                    double v = a + b * s + c * s * s + d * s * s * s;
                    double dv = b + 2 * c * s + 3 * d * s * s; // turev

                    // Koordinatlari guncelle
                    x += ds * Math.Cos(h);
                    y += ds * Math.Sin(h);
                    h += Math.Atan(dv) * ds / lastLen; // yaklasik aci degisimi

                    s += ds;
                }
                endX = x;
                endY = y;
            }
            else if (lastGeom.Element("paramPoly3") != null)
            {
                // ParamPoly3 icin parametrik hesaplama
                var paramPoly3 = lastGeom.Element("paramPoly3");
                double aU = ParseDoubleFromAttribute(paramPoly3, "aU");
                double bU = ParseDoubleFromAttribute(paramPoly3, "bU");
                double cU = ParseDoubleFromAttribute(paramPoly3, "cU");
                double dU = ParseDoubleFromAttribute(paramPoly3, "dU");
                double aV = ParseDoubleFromAttribute(paramPoly3, "aV");
                double bV = ParseDoubleFromAttribute(paramPoly3, "bV");
                double cV = ParseDoubleFromAttribute(paramPoly3, "cV");
                double dV = ParseDoubleFromAttribute(paramPoly3, "dV");
                string pRange = paramPoly3.Attribute("pRange")?.Value ?? "normalized";

                double p = (pRange == "arcLength") ? lastLen : 1.0;

                // u(p) ve v(p) parametrik denklemler
                double u = aU + bU * p + cU * p * p + dU * p * p * p;
                double v = aV + bV * p + cV * p * p + dV * p * p * p;

                // Lokal koordinatlari global koordinatlara donustur
                endX = lastX + u * Math.Cos(lastHdg) - v * Math.Sin(lastHdg);
                endY = lastY + u * Math.Sin(lastHdg) + v * Math.Cos(lastHdg);
            }
            else
            {
                // Bilinmeyen geometri tipleri icin duz cizgi tahmini
                endX = lastX + lastLen * Math.Cos(lastHdg);
                endY = lastY + lastLen * Math.Sin(lastHdg);
            }

            endPoint = new Point(endX, endY, 0);
        }

       
        /// Yolun ileri (sag seritler) ve geri (sol seritler) yon izinlerini analiz eder.
        /// OpenDRIVE'da sag seritler (negatif ID) ileri yonu, sol seritler (pozitif ID) geri yonu temsil eder.
       
        private void AnalyzeRoadDirections(XElement road, out bool canGoForward, out bool canGoBackward)
        {
            var lanes = road.Element("lanes");
            if (lanes == null)
            {
                // Bilgi yoksa varsayilan cift yon kabul et
                canGoForward = true;
                canGoBackward = true;
                return;
            }

            var laneSection = lanes.Element("laneSection");
            if (laneSection == null)
            {
                canGoForward = true;
                canGoBackward = true;
                return;
            }

            // Sag tarafta (negatif ID) driving lane varsa -> Ileri (Start->End) gidilebilir
            canGoForward = laneSection.Element("right")?
                .Elements("lane")
                .Any(l => GetAttributeValue(l, "type") == "driving") ?? false;

            // Sol tarafta (pozitif ID) driving lane varsa -> Geri (End->Start) gidilebilir
            canGoBackward = laneSection.Element("left")?
                .Elements("lane")
                .Any(l => GetAttributeValue(l, "type") == "driving") ?? false;

            // Eger ikisi de yoksa (orn. sadece yaya yolu), guvenli varsayilan olarak ileri acilabilir
            if (!canGoForward && !canGoBackward)
            {
                canGoForward = true;
            }
        }

      
        /// Kavşak koordinatlarını bağlı yolların ortalama konumlarından hesaplar
        
        private Dictionary<string, Point> CalculateJunctionCoordinates(
            List<RoadInfo> roadInfos,
            List<XElement> junctions,
            List<XElement> roads)
        {
            var junctionPoints = new Dictionary<string, List<Point>>();
            var roadInfoMap = roadInfos.ToDictionary(r => r.RoadId);

            // Her kavşak için bağlı yolların noktalarını topla
            foreach (var junc in junctions)
            {
                string juncId = GetAttributeValue(junc, "id");
                if (string.IsNullOrEmpty(juncId)) continue;
                junctionPoints[juncId] = new List<Point>();
            }

            // RoadInfo'dan link bilgilerini kullanarak kavşak noktalarını topla
            foreach (var roadInfo in roadInfos)
            {
                if (roadInfo.LinkInfo != null)
                {
                    // Predecessor kavşak mı?
                    if (roadInfo.LinkInfo.Predecessor != null &&
                        roadInfo.LinkInfo.Predecessor.ElementType == "junction")
                    {
                        string juncId = roadInfo.LinkInfo.Predecessor.ElementId;
                        if (junctionPoints.ContainsKey(juncId))
                            junctionPoints[juncId].Add(roadInfo.StartPoint);
                    }

                    // Successor kavşak mı?
                    if (roadInfo.LinkInfo.Successor != null &&
                        roadInfo.LinkInfo.Successor.ElementType == "junction")
                    {
                        string juncId = roadInfo.LinkInfo.Successor.ElementId;
                        if (junctionPoints.ContainsKey(juncId))
                            junctionPoints[juncId].Add(roadInfo.EndPoint);
                    }
                }
            }

            // Her kavşak için ortalama koordinat hesapla
            var result = new Dictionary<string, Point>();
            foreach (var kvp in junctionPoints)
            {
                if (kvp.Value.Count > 0)
                {
                    double avgX = kvp.Value.Average(p => p.X);
                    double avgY = kvp.Value.Average(p => p.Y);
                    double avgZ = kvp.Value.Average(p => p.Z);
                    result[kvp.Key] = new Point(avgX, avgY, avgZ);
                }
                else
                {
                    // Hiç bağlantı yoksa varsayılan değer
                    result[kvp.Key] = new Point(0, 0, 0);
                }
            }

            return result;
        }



        
        /// Tüm düğüm koordinatlarını tek bir dictionary'de birleştirir
        
        private Dictionary<string, Point> BuildNodeCoordinatesMap(List<RoadInfo> roadInfos, Dictionary<string, Point> junctionCoords)
        {
            // Toplam düğüm sayısını önceden bil (memory allocation optimizasyonu)
            // Her serit icin giris ve cikis dugumu olusturacagiz.
            // Junctionlar da dugum olarak kalabilir veya serit uclari junction'da birlesebilir.
            var nodeCoords = new Dictionary<string, Point>();

            foreach (var roadInfo in roadInfos)
            {
                // Road level nodes (fallback/legacy)
                nodeCoords[roadInfo.StartNodeId] = roadInfo.StartPoint;
                nodeCoords[roadInfo.EndNodeId] = roadInfo.EndPoint;

                // Lane level nodes
                foreach (var lane in roadInfo.Lanes)
                {
                    // Unique Node IDs for Lane Ends
                    string laneStartId = GetLaneNodeId(roadInfo.RoadId, lane.LaneId, true);
                    string laneEndId = GetLaneNodeId(roadInfo.RoadId, lane.LaneId, false);

                    // Koordinatlar: Road'un Start/End pointleri ile ayni kabul ediyoruz (basitlestirilmis).
                    // Gercekte yanal ofset vardir ama topoloji icin bu yeterli.
                    // Lane yonune gore Start/End atamasi yapmaliyiz:
                    // Right lanes (negative): Start -> End yönünde gider. Giris=StartPoint, Cikis=EndPoint.
                    // Left lanes (positive): End -> Start yönünde gider. Giris=EndPoint, Cikis=StartPoint.
                    // ANCAK: Biz "StartNodeId" ve "EndNodeId" terimlerini topolojik olarak degil, geometrik olarak kullanalim.
                    // Yani road geometriesindeki s=0 noktasi "Start", s=L noktasi "End".
                    // Baglantilari kurarken yonu dikkate alacagiz.

                    nodeCoords[laneStartId] = roadInfo.StartPoint;
                    nodeCoords[laneEndId] = roadInfo.EndPoint;
                }
            }

            // Kavşak düğümlerini ekle
            foreach (var kvp in junctionCoords)
            {
                nodeCoords[$"{JUNCTION_PREFIX}{kvp.Key}"] = kvp.Value;
            }

            return nodeCoords;
        }

        private string GetLaneNodeId(string roadId, int laneId, bool isStart)
        {
            return $"road:{roadId}:lane:{laneId}:{(isStart ? "start" : "end")}";
        }

        /// Tüm bağlantıları oluşturur (yol içi, yollar arası, kavşak içi)

        private List<Tuple<string, string, double, string, int>> BuildConnections(
            List<RoadInfo> roadInfos,
            List<XElement> roads,
            List<XElement> junctions,
            Dictionary<string, Point> nodeCoords)
        {
            // Tuple: FromNode, ToNode, Weight, RoadId, LaneId
            var connections = new List<Tuple<string, string, double, string, int>>();
            var roadInfoMap = roadInfos.ToDictionary(r => r.RoadId);

            // 1. Yol içi bağlantılar (Lane Start geometry -> Lane End geometry)
            AddIntraLaneConnections(connections, roadInfos);

            // 2. Yollar arası bağlantılar (predecessor/successor)
            AddInterLaneConnections(connections, roadInfos, roadInfoMap);

            // 3. Kavşak içi bağlantılar
            AddJunctionLaneConnections(connections, junctions, roadInfoMap);

            return connections;
        }

        private void AddIntraLaneConnections(List<Tuple<string, string, double, string, int>> connections, List<RoadInfo> roadInfos)
        {
            foreach (var road in roadInfos)
            {
                if (!string.IsNullOrEmpty(road.JunctionId) && road.JunctionId != "-1") continue; // Junction yollarini ayri isle

                foreach (var lane in road.Lanes)
                {
                    string startNode = GetLaneNodeId(road.RoadId, lane.LaneId, true);
                    string endNode = GetLaneNodeId(road.RoadId, lane.LaneId, false);

                    // Right Lane (Negative): Geometrik Start -> Geometrik End (Forward)
                    if (lane.LaneId < 0)
                    {
                        connections.Add(Tuple.Create(startNode, endNode, road.Length, road.RoadId, lane.LaneId));
                    }
                    // Left Lane (Positive): Geometrik End -> Geometrik Start (Backward)
                    else if (lane.LaneId > 0)
                    {
                        connections.Add(Tuple.Create(endNode, startNode, road.Length, road.RoadId, lane.LaneId));
                    }
                }
            }
        }

        private void AddInterLaneConnections(
             List<Tuple<string, string, double, string, int>> connections,
             List<RoadInfo> roadInfos,
             Dictionary<string, RoadInfo> roadInfoMap)
        {
            foreach (var road in roadInfos)
            {
                foreach (var lane in road.Lanes)
                {
                    // Şeridin akış yönündeki ÇIKIŞ düğümü hangisi?
                    // Right(Neg) -> EndNodeId, Left(Pos) -> StartNodeId
                    string myExitNodeId = GetLaneNodeId(road.RoadId, lane.LaneId, lane.LaneId < 0 ? false : true);
                    
                    // Şeridin akış yönündeki SONRAKI yola/şeride bağlantısı var mı?
                    // Right Lane: Giderken Successor road'a bakar.
                    // Left Lane: Giderken Predecessor road'a bakar (çünkü ters yönde ilerliyor).

                    LinkElementInfo nextRoadLink = (lane.LaneId < 0) ? road.LinkInfo?.Successor : road.LinkInfo?.Predecessor;

                    if (nextRoadLink != null && nextRoadLink.ElementType == "road" && roadInfoMap.ContainsKey(nextRoadLink.ElementId))
                    {
                        var nextRoad = roadInfoMap[nextRoadLink.ElementId];
                        
                        // Hangi şeritlere bağlanıyoruz? Lane Link bilgisini kullan.
                        // Lane successor/predecessor ID'leri.
                        // Right Lane (Neg) -> Lane Successor Ids
                        // Left Lane (Pos) -> Lane Predecessor Ids
                        // DIKKAT: XODR lane link yönü kafa karıştırıcıdır. 
                        // Genelde "successor" elementi, yolun successor ucundaki şeridi gösterir.
                        // "predecessor" elementi, yolun predecessor ucundaki şeridi gösterir.

                        List<int> targetLaneIds = new List<int>();
                        // Biz geometrik olarak Successor ucuna gidiyorsak (Right Lane), lane'in successor linkine bakariz.
                        if (lane.LaneId < 0 && lane.Link != null) targetLaneIds.AddRange(lane.Link.SuccessorIds);
                        
                        // Biz geometrik olarak Predecessor ucuna gidiyorsak (Left Lane), lane'in predecessor linkine bakariz.
                        if (lane.LaneId > 0 && lane.Link != null) targetLaneIds.AddRange(lane.Link.PredecessorIds);

                        foreach (var targetLaneId in targetLaneIds)
                        {
                            // Target Lane'in GIRIS dugumunu bulmaliyiz.
                            // Target Lane Id < 0 (Right) ise Giris = StartNode
                            // Target Lane Id > 0 (Left) ise Giris = EndNode
                            string targetEntryNodeId = GetLaneNodeId(nextRoad.RoadId, targetLaneId, targetLaneId < 0 ? true : false);
                            
                            // Bağlantıyı ekle (Weight = 0 veya çok küçük)
                            connections.Add(Tuple.Create(myExitNodeId, targetEntryNodeId, LINK_CONNECTION_DISTANCE, (string)null, 0));
                        }
                    }
                }
            }
        }
        
        private void AddJunctionLaneConnections(
            List<Tuple<string, string, double, string, int>> connections,
            List<XElement> junctions,
            Dictionary<string, RoadInfo> roadInfoMap)
        {
            foreach (var junction in junctions)
            {
                var connectionElements = junction.Elements("connection");
                foreach (var conn in connectionElements)
                {
                    string incomingRoadId = GetAttributeValue(conn, "incomingRoad");
                    string connectingRoadId = GetAttributeValue(conn, "connectingRoad");
                    string contactPoint = GetAttributeValue(conn, "contactPoint");

                    if (!roadInfoMap.ContainsKey(incomingRoadId) || !roadInfoMap.ContainsKey(connectingRoadId)) continue;

                    var incomingRoad = roadInfoMap[incomingRoadId];
                    var connectingRoad = roadInfoMap[connectingRoadId];

                    var laneLinks = conn.Elements("laneLink");
                    foreach (var link in laneLinks)
                    {
                        int fromLaneId = (int)ParseDoubleFromAttribute(link, "from");
                        int toLaneId = (int)ParseDoubleFromAttribute(link, "to");

                        // 1. Incoming Road Lane Exit -> Connecting Road Lane Entry
                        string incomingExitNodeId = GetLaneNodeId(incomingRoadId, fromLaneId, fromLaneId < 0 ? false : true);
                        
                        // Bağlantı yolunun giriş düğümü. 
                        // contactPoint "start" ise connecting Road'un başına bağlanıyoruz.
                        // Eğer connecting road içinde forward (right lane) gidiyorsak giriş START.
                        // Eğer connecting road içinde backward (left lane) gidiyorsak giriş END.
                        // AMA: Junction connecting roads genelde tek yönlüdür ve sürüş yönü tanımlıdır.
                        // Basit mantık: Contact point ne diyorsa o uçtan giriyoruz.
                        bool contactIsStart = (contactPoint != "end"); // Default start
                        
                        // Connecting road'un lane'i (toLaneId).
                        // Eğer toLaneId < 0 (Right) ise akış Start->End.
                        // Eğer toLaneId > 0 (Left) ise akış End->Start.
                        
                        // Connecting Road üzerinde hareket:
                        // "Start" ucundan girdiysek ve "Right" (Neg) şeritteysek: Flow uyumlu. Entry=Start.
                        // "Start" ucundan girdiysek ve "Left" (Pos) şeritteysek: Flow ters? (Genelde olmaz, kavşak içi yollar tek yönlüdür).
                        
                        // Daha sağlam mantık:
                        // Connecting Road'un giriş geometrik ucu: contactIsStart ? Start : End.
                        string connectingEntryNodeId = GetLaneNodeId(connectingRoadId, toLaneId, contactIsStart);
                        
                        connections.Add(Tuple.Create(incomingExitNodeId, connectingEntryNodeId, LINK_CONNECTION_DISTANCE, (string)null, 0));

                        // 2. Connecting Road İçi Hareket
                        // Connecting road'un çıkış ucu: connectionStart'ın tersi.
                        string connectingExitNodeId = GetLaneNodeId(connectingRoadId, toLaneId, !contactIsStart);
                        
                        // Connecting road edge'i ekle
                        connections.Add(Tuple.Create(connectingEntryNodeId, connectingExitNodeId, connectingRoad.Length, connectingRoadId, toLaneId));

                        // 3. Connecting Road Exit -> Next Road Entry
                        // Bu kısım normal InterLaneConnections tarafından halledilmeli çünkü connecting road normal bir road entitysi.
                        // Onun successor/predecessor linkleri parse edildiğinde otomatik bağlanacak.
                    }
                }
            }
        }

        /// İstatistikleri yazdırır
        
        private void PrintStatistics(int roadCount, int junctionCount, List<Tuple<string, string, double, string, int>> connections)
        {
            int roadNodeCount = roadCount * 2;
            int totalNodeCount = roadNodeCount + junctionCount;

            Console.WriteLine($"Oluşturulan düğüm sayısı: {totalNodeCount} (yol: {roadNodeCount}, kavşak: {junctionCount})");
            Console.WriteLine($"Oluşturulan bağlantı sayısı: {connections.Count}");

            if (connections.Count > 0)
            {
                double minLength = connections.Min(c => c.Item3);
                double maxLength = connections.Max(c => c.Item3);
                double avgLength = connections.Average(c => c.Item3);
                Console.WriteLine($"Kenar uzunluk istatistikleri: Min: {minLength:F2}, Maks: {maxLength:F2}, Ort: {avgLength:F2}");
            }
        }

        
        /// GraphData nesnesini olusturur - Adjacency List ile (bellek verimli)
        
        private GraphData CreateGraphData(
            Dictionary<string, Point> nodeCoords, 
            List<Tuple<string, string, double, string, int>> connections,
            Dictionary<string, (string StartId, string EndId)> roadMapping,
            Dictionary<string, JunctionInfo> junctionMapping,
            Dictionary<(string, int), string> laneMapping)
        {
            // Adjacency List olustur (String ID tabanli)
            var adjList = new Dictionary<string, List<Edge>>();

            foreach (var conn in connections)
            {
                string fromId = conn.Item1;
                string toId = conn.Item2;
                double weight = conn.Item3;
                string roadId = conn.Item4;
                int laneId = conn.Item5;

                // Eger dugum koordinatlari listemizde bu ID varsa ekle (Tutarlilik kontrolu)
                if (nodeCoords.ContainsKey(fromId) && nodeCoords.ContainsKey(toId))
                {
                    if (!adjList.ContainsKey(fromId))
                        adjList[fromId] = new List<Edge>();

                    var newEdge = new Edge(toId, weight);
                    newEdge.RoadId = roadId;
                    newEdge.LaneId = laneId;
                    adjList[fromId].Add(newEdge);
                }
            }

            // Index haritasi olmadan, direkt veriyi veriyoruz
            var graph = new GraphData(adjList, nodeCoords, roadMapping, junctionMapping);
            graph.SetLaneNodeMapping(laneMapping);
            return graph;
        }

        #region XML Parsing Helper Methods

        
        /// XML attribute değerini güvenli şekilde okur
       
        private string GetAttributeValue(XElement element, string attributeName)
        {
            return element?.Attribute(attributeName)?.Value ?? string.Empty;
        }

        
        /// XML attribute değerini double olarak parse eder
       
        private double ParseDouble(XElement element, string attributeName, double defaultValue = 0.0)
        {
            string value = GetAttributeValue(element, attributeName);
            if (string.IsNullOrEmpty(value)) return defaultValue;

            if (double.TryParse(value, NumberStyles.Any, culture, out double result))
                return result;

            return defaultValue;
        }

        
        /// XAttribute'tan double değer parse eder
        
        private double ParseDoubleFromAttribute(XElement element, string attributeName, double defaultValue = 0.0)
        {
            var attr = element?.Attribute(attributeName);
            if (attr == null) return defaultValue;

            if (double.TryParse(attr.Value, NumberStyles.Any, culture, out double result))
                return result;

            return defaultValue;
        }

        #endregion
    }
}

