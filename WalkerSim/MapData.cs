﻿using System;
using System.Drawing;
using System.Xml.Serialization;

namespace WalkerSim
{
    public class MapData
    {
        public class MapInfo
        {
            public string Name;
            public string Description;
            public string Modes;
            public int HeightMapWidth;
            public int HeightMapHeight;
        }

        [XmlRoot("prefabs")]
        public class PrefabsData
        {
            [XmlElement("decoration")]
            public Decoration[] Decorations;
        }

        public class Decoration
        {
            [XmlAttribute("type")]
            public string Type;

            [XmlAttribute("name")]
            public string Name;

            [XmlIgnore]
            public Vector3 Position;

            [XmlAttribute("position")]
            public string PositionString
            {
                get => Position.ToString();
                set
                {
                    Position = Vector3.Parse(value, true);
                }
            }

            [XmlAttribute("rotation")]
            public int Rotation;

            [XmlAttribute("y_is_groundlevel")]
            public bool YIsGroundlevel;
        }

        private Roads _roads;

        public Roads Roads
        {
            get => _roads;
        }

        private MapInfo _info;

        public MapInfo Info
        {
            get => _info;
        }

        private PrefabsData _prefabs;

        public PrefabsData Prefabs
        {
            get => _prefabs;
        }

        private static MapInfo ParseInfo(string path)
        {
            if (!System.IO.File.Exists(path))
            {
                return null;
            }

            var fileData = System.IO.File.ReadAllText(path, System.Text.Encoding.UTF8);
            if (fileData == null)
            {
                return null;
            }

            var info = new MapInfo();

            var doc = new System.Xml.XmlDocument();
            doc.LoadXml(fileData);

            var root = doc.DocumentElement;
            if (root.Name != "MapInfo")
            {
                return null;
            }

            // Read <propert name="X" value="Y" />
            foreach (System.Xml.XmlNode node in root.ChildNodes)
            {
                if (node.Name == "property")
                {
                    var name = node.Attributes.GetNamedItem("name").Value;
                    var value = node.Attributes.GetNamedItem("value").Value;

                    if (name == "Name")
                    {
                        info.Name = value;
                    }
                    else if (name == "Description")
                    {
                        info.Description = value;
                    }
                    else if (name == "Modes")
                    {
                        info.Modes = value;
                    }
                    else if (name == "HeightMapSize")
                    {
                        var split = value.Split(',');
                        if (split.Length != 2)
                        {
                            return null;
                        }

                        info.HeightMapWidth = int.Parse(split[0]);
                        info.HeightMapHeight = int.Parse(split[1]);
                    }
                }
            }

            return info;
        }

        private static Roads LoadRoadSplat(string folderPath)
        {
            var splatPath = System.IO.Path.Combine(folderPath, "splat3_half.png");
            if (!System.IO.File.Exists(splatPath))
            {
                splatPath = System.IO.Path.Combine(folderPath, "splat3_processed.png");
            }
            if (!System.IO.File.Exists(splatPath))
            {
                splatPath = System.IO.Path.Combine(folderPath, "splat3.png");
            }
            if (!System.IO.File.Exists(splatPath))
            {
                return null;
            }

            var img = Image.FromFile(splatPath);
            ImageUtils.RemoveTransparency((Bitmap)img);

            return Roads.LoadFromBitmap((Bitmap)img);
        }

        private static PrefabsData LoadPrefabs(string folderPath)
        {
            var filePath = System.IO.Path.Combine(folderPath, "prefabs.xml");
            var serializer = new XmlSerializer(typeof(PrefabsData));
            try
            {
                using (var reader = new System.IO.StreamReader(filePath))
                {
                    return (PrefabsData)serializer.Deserialize(reader);
                }
            }
            catch (System.Exception)
            {
                return null;
            }
        }

        public static MapData LoadFromFolder(string folderPath)
        {
            // Parse map_info.xml         
            var mapInfoPath = System.IO.Path.Combine(folderPath, "map_info.xml");
            var mapInfo = ParseInfo(mapInfoPath);

            var roads = LoadRoadSplat(folderPath);
            if (roads == null)
            {
                return null;
            }

            var prefabs = LoadPrefabs(folderPath);
            if (prefabs == null)
            {
                return null;
            }

            var res = new MapData();
            res._info = mapInfo;
            res._roads = roads;
            res._prefabs = prefabs;

            // Garbage collect here, the PNGs are sometimes huge.
            GC.Collect();
            GC.WaitForFullGCComplete();

            return res;
        }
    }
}
