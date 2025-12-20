using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using System.Globalization;

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
            var nodeCoords = BuildNodeCoordinatesMap(roadInfos, junctionCoords);

            // 4. Adım: Bağlantıları oluştur
            var connections = BuildConnections(roadInfos, roads, junctions, nodeCoords);

            // 5. Adım: İstatistikleri yazdır
            PrintStatistics(roadInfos.Count, junctionCoords.Count, connections);

            // 6. Adım: GraphData nesnesini oluştur ve döndür
            return CreateGraphData(nodeCoords, connections);
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

                roadInfos.Add(roadInfo);
            }

            return roadInfos;
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
            int totalNodes = (roadInfos.Count * 2) + junctionCoords.Count;
            var nodeCoords = new Dictionary<string, Point>(totalNodes);

            // Yol düğümlerini ekle
            foreach (var roadInfo in roadInfos)
            {
                nodeCoords[roadInfo.StartNodeId] = roadInfo.StartPoint;
                nodeCoords[roadInfo.EndNodeId] = roadInfo.EndPoint;
            }

            // Kavşak düğümlerini ekle
            foreach (var kvp in junctionCoords)
            {
                nodeCoords[$"{JUNCTION_PREFIX}{kvp.Key}"] = kvp.Value;
            }

            return nodeCoords;
        }

        
        /// Tüm bağlantıları oluşturur (yol içi, yollar arası, kavşak içi)
        
        private List<Tuple<string, string, double>> BuildConnections(
            List<RoadInfo> roadInfos,
            List<XElement> roads,
            List<XElement> junctions,
            Dictionary<string, Point> nodeCoords)
        {
            var connections = new List<Tuple<string, string, double>>();
            var roadInfoMap = roadInfos.ToDictionary(r => r.RoadId);

            // 1. Yol içi bağlantılar (start -> end)
            AddIntraRoadConnections(connections, roadInfos);

            // 2. Yollar arası bağlantılar (predecessor/successor)
            AddInterRoadConnections(connections, roadInfos, roadInfoMap, nodeCoords);

            // 3. Kavşak içi bağlantılar
            AddJunctionConnections(connections, junctions, roadInfoMap, roads);

            return connections;
        }

        
        /// Yol ici baglantilari ekler - Yon izinlerine gore (ileri ve/veya geri)
        
        private void AddIntraRoadConnections(List<Tuple<string, string, double>> connections, List<RoadInfo> roadInfos)
        {
            int forwardCount = 0;
            int backwardCount = 0;

            foreach (var roadInfo in roadInfos)
            {
                // Kavsak icindeki baglanti yollarini atla (Junction fonksiyonu hallediyor)
                if (!string.IsNullOrEmpty(roadInfo.JunctionId) && roadInfo.JunctionId != JUNCTION_ID_DEFAULT)
                {
                    continue;
                }

                // 1. Ileri Yon (Start -> End) izni varsa ekle
                if (roadInfo.CanGoForward)
                {
                    connections.Add(Tuple.Create(roadInfo.StartNodeId, roadInfo.EndNodeId, roadInfo.Length));
                    forwardCount++;
                }

                // 2. Geri Yon (End -> Start) izni varsa ekle
                if (roadInfo.CanGoBackward)
                {
                    connections.Add(Tuple.Create(roadInfo.EndNodeId, roadInfo.StartNodeId, roadInfo.Length));
                    backwardCount++;
                }
            }

            Console.WriteLine($"Yol ici baglantilar: Ileri yon: {forwardCount}, Geri yon: {backwardCount}");
        }

        
        /// Yollar arasi baglantilari ekler - Yon izinlerine gore cift yonlu baglanti destekler
        
        private void AddInterRoadConnections(
            List<Tuple<string, string, double>> connections,
            List<RoadInfo> roadInfos,
            Dictionary<string, RoadInfo> roadInfoMap,
            Dictionary<string, Point> nodeCoords)
        {
            foreach (var roadInfo in roadInfos)
            {
                if (roadInfo.LinkInfo == null) continue;

                // --- PREDECESSOR ISLEME (Yolun 'Start' ucundaki baglanti) ---
                if (roadInfo.LinkInfo.Predecessor != null)
                {
                    var pred = roadInfo.LinkInfo.Predecessor;
                    string linkedNodeId = GetLinkedNodeId(pred, roadInfoMap, nodeCoords, true);

                    if (linkedNodeId != null)
                    {
                        // Eger Ileri gidebiliyorsak: Predecessor -> Bizim Start (Giris)
                        if (roadInfo.CanGoForward)
                        {
                            connections.Add(Tuple.Create(linkedNodeId, roadInfo.StartNodeId, LINK_CONNECTION_DISTANCE));
                        }

                        // Eger Geri gidebiliyorsak: Bizim Start -> Predecessor (Cikis)
                        if (roadInfo.CanGoBackward)
                        {
                            connections.Add(Tuple.Create(roadInfo.StartNodeId, linkedNodeId, LINK_CONNECTION_DISTANCE));
                        }
                    }
                }

                // --- SUCCESSOR ISLEME (Yolun 'End' ucundaki baglanti) ---
                if (roadInfo.LinkInfo.Successor != null)
                {
                    var succ = roadInfo.LinkInfo.Successor;
                    string linkedNodeId = GetLinkedNodeId(succ, roadInfoMap, nodeCoords, false);

                    if (linkedNodeId != null)
                    {
                        // Eger Ileri gidebiliyorsak: Bizim End -> Successor (Cikis)
                        if (roadInfo.CanGoForward)
                        {
                            connections.Add(Tuple.Create(roadInfo.EndNodeId, linkedNodeId, LINK_CONNECTION_DISTANCE));
                        }

                        // Eger Geri gidebiliyorsak: Successor -> Bizim End (Giris)
                        if (roadInfo.CanGoBackward)
                        {
                            connections.Add(Tuple.Create(linkedNodeId, roadInfo.EndNodeId, LINK_CONNECTION_DISTANCE));
                        }
                    }
                }
            }
        }

        
        /// Yardimci Metod: Baglanilacak Node ID'sini bulur
        
        private string GetLinkedNodeId(LinkElementInfo linkInfo, Dictionary<string, RoadInfo> roadInfoMap, 
            Dictionary<string, Point> nodeCoords, bool isPredecessorLink)
        {
            if (linkInfo.ElementType == "road" && roadInfoMap.ContainsKey(linkInfo.ElementId))
            {
                var linkedRoad = roadInfoMap[linkInfo.ElementId];

                // contactPoint belirtilmemisse varsayilan mantik
                if (string.IsNullOrEmpty(linkInfo.ContactPoint))
                {
                    // Predecessor genelde diger yolun End'ine baglanir
                    // Successor genelde diger yolun Start'ina baglanir
                    return isPredecessorLink ? linkedRoad.EndNodeId : linkedRoad.StartNodeId;
                }

                return linkInfo.ContactPoint == "start" ? linkedRoad.StartNodeId : linkedRoad.EndNodeId;
            }
            else if (linkInfo.ElementType == "junction")
            {
                string junctionNodeId = $"{JUNCTION_PREFIX}{linkInfo.ElementId}";
                if (nodeCoords.ContainsKey(junctionNodeId))
                {
                    return junctionNodeId;
                }
            }
            return null;
        }

        
        /// Kavsak ici baglantilari ekler - Ters yonlu baglanti yollarini da destekler
        
        private void AddJunctionConnections(
            List<Tuple<string, string, double>> connections,
            List<XElement> junctions,
            Dictionary<string, RoadInfo> roadInfoMap,
            List<XElement> roads)
        {
            int junctionConnectionCount = 0;
            int skippedConnections = 0;
            int reverseTraversalCount = 0;

            foreach (var junction in junctions)
            {
                string junctionId = GetAttributeValue(junction, "id");
                if (string.IsNullOrEmpty(junctionId)) continue;

                var junctionConnections = junction.Elements("connection").ToList();

                foreach (var connection in junctionConnections)
                {
                    string incomingRoadId = GetAttributeValue(connection, "incomingRoad");
                    string connectingRoadId = GetAttributeValue(connection, "connectingRoad");
                    string contactPoint = GetAttributeValue(connection, "contactPoint"); // "start" veya "end"

                    if (string.IsNullOrEmpty(incomingRoadId) || string.IsNullOrEmpty(connectingRoadId))
                    {
                        skippedConnections++;
                        continue;
                    }

                    if (!roadInfoMap.ContainsKey(incomingRoadId) || !roadInfoMap.ContainsKey(connectingRoadId))
                    {
                        skippedConnections++;
                        continue;
                    }

                    var incomingRoadInfo = roadInfoMap[incomingRoadId];
                    var connectingRoadInfo = roadInfoMap[connectingRoadId];

                    // --- ADIM 1: GIRIS BAGLANTISI (Incoming Road -> Connecting Road) ---

                    // Gelen yolun hangi ucundayiz?
                    // Eger gelen yolun Successor'i bu kavsaksa, yolun sonunda (EndNode) bitmisizdir.
                    // Eger gelen yolun Predecessor'i bu kavsaksa, yolun basinda (StartNode) bitmisizdir (ters yon).
                    string fromNodeId = incomingRoadInfo.EndNodeId; // Varsayilan

                    if (incomingRoadInfo.LinkInfo?.Predecessor?.ElementType == "junction" &&
                        incomingRoadInfo.LinkInfo.Predecessor.ElementId == junctionId)
                    {
                        fromNodeId = incomingRoadInfo.StartNodeId;
                    }
                    else if (incomingRoadInfo.LinkInfo?.Successor?.ElementType == "junction" &&
                             incomingRoadInfo.LinkInfo.Successor.ElementId == junctionId)
                    {
                        fromNodeId = incomingRoadInfo.EndNodeId;
                    }

                    // Baglanti yolunun hangi ucuna baglaniyoruz?
                    string entryNodeId;
                    string exitNodeId;
                    bool isTraversingForward; // Yol icinde Start->End mi gidiyoruz?

                    if (string.IsNullOrEmpty(contactPoint) || contactPoint == "start")
                    {
                        // Start'tan giriyoruz, End'e dogru gidecegiz (Ileri Yon)
                        entryNodeId = connectingRoadInfo.StartNodeId;
                        exitNodeId = connectingRoadInfo.EndNodeId;
                        isTraversingForward = true;
                    }
                    else // contactPoint == "end"
                    {
                        // End'den giriyoruz, Start'a dogru gidecegiz (Ters Yon)
                        entryNodeId = connectingRoadInfo.EndNodeId;
                        exitNodeId = connectingRoadInfo.StartNodeId;
                        isTraversingForward = false;
                        reverseTraversalCount++;
                    }

                    // 1. Baglantiyi Ekle: Gelen Yol -> Baglanti Yolu Girisi
                    connections.Add(Tuple.Create(fromNodeId, entryNodeId, LINK_CONNECTION_DISTANCE));

                    // --- ADIM 2: IC BAGLANTI (Traversing Connecting Road) ---

                    // Baglanti yolunun icindeki hareketi ekle (Giris -> Cikis)
                    connections.Add(Tuple.Create(entryNodeId, exitNodeId, connectingRoadInfo.Length));

                    // --- ADIM 3: CIKIS BAGLANTISI (Connecting Road -> Next Road) ---

                    // Simdi baglanti yolundan ciktik, sirada ne var?
                    // Eger ileri (Start->End) gittiysek, siradaki yol Successor'dir.
                    // Eger geri (End->Start) gittiysek, siradaki yol Predecessor'dir.

                    LinkElementInfo nextLink = isTraversingForward
                        ? connectingRoadInfo.LinkInfo?.Successor
                        : connectingRoadInfo.LinkInfo?.Predecessor;

                    if (nextLink != null && nextLink.ElementType == "road" && roadInfoMap.ContainsKey(nextLink.ElementId))
                    {
                        var nextRoadInfo = roadInfoMap[nextLink.ElementId];

                        // Siradaki yolun hangi ucuna baglaniyoruz?
                        // nextLink.ContactPoint "start" ise StartNode'una, "end" ise EndNode'una baglanriz.
                        string nextRoadTargetNodeId;

                        if (string.IsNullOrEmpty(nextLink.ContactPoint) || nextLink.ContactPoint == "start")
                        {
                            nextRoadTargetNodeId = nextRoadInfo.StartNodeId;
                        }
                        else
                        {
                            nextRoadTargetNodeId = nextRoadInfo.EndNodeId;
                        }

                        // 3. Baglantiyi Ekle: Baglanti Yolu Cikisi -> Siradaki Yol
                        connections.Add(Tuple.Create(exitNodeId, nextRoadTargetNodeId, LINK_CONNECTION_DISTANCE));
                    }

                    junctionConnectionCount++;
                }
            }

            Console.WriteLine($"Kavsak ici baglanti sayisi: {junctionConnectionCount}");
            if (reverseTraversalCount > 0)
                Console.WriteLine($"  Ters yonlu baglanti (End->Start): {reverseTraversalCount}");
            if (skippedConnections > 0)
                Console.WriteLine($"  Atlanan baglanti: {skippedConnections}");
        }

        
        /// İstatistikleri yazdırır
        
        private void PrintStatistics(int roadCount, int junctionCount, List<Tuple<string, string, double>> connections)
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
        
        private GraphData CreateGraphData(Dictionary<string, Point> nodeCoords, List<Tuple<string, string, double>> connections)
        {
            var allNodeIds = nodeCoords.Keys.OrderBy(id => id).ToList();
            var nodeIdToIndex = new Dictionary<string, int>(allNodeIds.Count);

            for (int i = 0; i < allNodeIds.Count; i++)
            {
                nodeIdToIndex[allNodeIds[i]] = i;
            }

            var nodeCoordinates = new Point[allNodeIds.Count];
            for (int i = 0; i < allNodeIds.Count; i++)
            {
                nodeCoordinates[i] = nodeCoords[allNodeIds[i]];
            }

            // Adjacency List olustur (matris yerine - bellek verimli)
            var adjList = new Dictionary<int, List<Edge>>();

            foreach (var conn in connections)
            {
                if (nodeIdToIndex.TryGetValue(conn.Item1, out int fromIdx) &&
                    nodeIdToIndex.TryGetValue(conn.Item2, out int toIdx))
                {
                    if (!adjList.ContainsKey(fromIdx))
                        adjList[fromIdx] = new List<Edge>();

                    adjList[fromIdx].Add(new Edge(toIdx, conn.Item3));
                }
            }

            return new GraphData(adjList, nodeCoordinates, nodeIdToIndex);
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

