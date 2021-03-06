using System;
using System.Collections.Generic;
using System.IO;
using Ionic.Zip;
using System.Diagnostics;
using System.Linq;
using System.Xml.Linq;
using System.Xml;
using ePubFixer;

namespace ePubFixer
{
    public class OpfDocument : Document
    {
        #region Properties
        protected internal override string fileOutName { get; set; }
        protected internal override Stream fileOutStream { get; set; }
        protected internal override Stream fileExtractStream { get; set; }
        protected override string nsIfEmpty { get; set; }
        protected override XElement NewTOC { get; set; }
        protected override XElement NewNavMap { get; set; }
        protected override XElement OldTOC { get; set; }
        protected override XNamespace ns { get; set; }

        public List<System.Xml.Linq.XElement> SpineElements
        {
            get
            {
                return GetXmlElement("spine").Elements().ToList();
            }
        }

        public List<System.Xml.Linq.XAttribute> SpineIDs
        {
            get
            {
                return SpineElements.Attributes("idref").ToList();
            }
        }

        public List<System.Xml.Linq.XElement> ManifestElements
        {
            get
            {
                return GetManifest().Elements().ToList();
            }
        }

        #endregion

        #region Constructor
        public OpfDocument()
            : base()
        {
            //fileOutStream = new MemoryStream();
            nsIfEmpty = "\"http://www.idpf.org/2007/opf\"";

            OldTOC = null;
            SetFile();
            //return;
        }
        #endregion

        #region Update
        internal override void UpdateFile()
        {
            SetFile();
            XElement xml = GetXmlElement("spine");
            //if (xml == null)
            //    return false;

#if !CLI
            using (frmSpineEdit frm = new frmSpineEdit(xml))
            {
                //frmSpineEdit frm = new frmSpineEdit(xml);
                frm.Save += new EventHandler<ExportTocEventArgs>(frm_Save);
                frm.ShowDialog();
            } 
#endif
        }

        private void frm_Save(object sender, ExportTocEventArgs e)
        {
            Save(e.XML);
            e.Message = SaveMessage;
        }

        private void Save(XElement xml)
        {
            NewNavMap = xml;

            if (NewNavMap == null)
                return;

            ReplaceSpine(NewNavMap);
        }


        #endregion

        #region Initial Setup
        private bool SetFile()
        {
            fileExtractStream = base.GetStream(Variables.OPFfile);
            try
            {
                string Text = ByteToString(fileExtractStream);

                if (String.IsNullOrEmpty(Text))
                {
                    System.Windows.Forms.MessageBox.Show("Content File is Empty", Variables.BookName);
                    return false;
                }

                Text = FixMetadata(Text);
                OldTOC = XElement.Parse(Text.Trim());
                ns = OldTOC.Name.Namespace;
                return true;
            } catch (Exception ex)
            {

                System.Windows.Forms.MessageBox.Show("Invalid Content File\n" + ex.Message, Variables.BookName);
                return false;
            }
        }

        #endregion

        #region Create Entries in OPF File
        #region Manifest
        internal void AddTocEntry()
        {
            List<string> fileList = GetFilesList("application/x-dtbncx+xml");

            if (fileList.Count == 0)
            {
                AddEntryToManifest("application/x-dtbncx+xml", "toc.ncx", "toc.ncx");
            }
        }

        private void AddEntryToManifest(string MediaType, string FileName, string id)
        {
            XElement tocElement = new XElement(ns + "item",
                                                    new XAttribute("href", FileName),
                                                    new XAttribute("media-type", MediaType),
                                                    new XAttribute("id", id));
            if (OldTOC != null)
            {
                NewTOC = new XElement(OldTOC);
                //FixOpfDeclaration(NewTOC, "manifest").Add(tocElement);
                NewTOC.Element(ns + "manifest").Add(tocElement);
                base.WriteXML();
                base.UpdateZip();
                SetFile();

            } else
            {
                return;
            }
        }

        internal void AddHtmlFile(string filename)
        {
            string id = Guid.NewGuid().ToString();
            AddEntryToManifest("application/xhtml+xml", filename, filename);

            //Also Add it to Spine
            AddSpineElement(filename);
        }
        #endregion

        #region Guide
        private void AddEntryToGuide(string File, string type, string Title)
        {
            using (new HourGlass())
            {
                //Load existing Guide
                XElement Guide = GetXmlElement("guide");

                if (Guide == null)
                    Guide = new XElement(ns + "guide");

                //remove any Reference already existing
                IEnumerable<string> ExistingRef = GetGuideRefList(type);
                if (ExistingRef != null)
                {
                    Guide.Descendants().Where(x => x.Attribute("type").Value == type &&
                        ExistingRef.Contains(x.Attribute("href").Value)).Remove();
                }


                //Add new item to Guide
                Guide.Add(
                    new XElement(ns + "reference",
                        new XAttribute("type", type),
                        new XAttribute("title", Title),
                        new XAttribute("href", File)));

                //Replace and update new guide
                ReplaceSection(Guide, "guide");
            }
        }

        internal void AddCoverRef(string CoverFile)
        {
            AddEntryToGuide(CoverFile, "cover", "Cover");
        }

        internal void AddTOCContentRef(string ContentFile)
        {
            AddEntryToGuide(ContentFile, "toc", "Table of Content");
        }
        #endregion

        #region Spine
        private void AddSpineElement(string id)
        {
            XElement Spine = GetXmlElement("spine");

            if (Spine == null)
                Spine = new XElement(ns + "spine", new XAttribute("toc", GetNCXid()));

            Spine.Add(
                new XElement(ns + "itemref",
                    new XAttribute("idref", id)));

            ReplaceSpine(Spine);
        }
        #endregion

        #endregion

        #region Replace
        internal XElement ReplaceSection(XElement newSection, string section)
        {
            if (OldTOC != null)
            {
                NewTOC = new XElement(OldTOC);
                XElement Section = NewTOC.Element(ns + section);
                XElement Section2 = NewTOC.Element(section);

                if (Section == null && Section2 != null)
                    Section = Section2;

                if (Section == null)
                {
                    //If Section does not exist Add it
                    NewTOC.Add(newSection);
                } else
                {
                    Section.ReplaceWith(newSection);
                }

                base.WriteXML();
                base.UpdateZip();
                SetFile();
            } else
            {
                return null;
            }
            return NewTOC;
        }

        internal XElement ReplaceManifest(XElement newManifest)
        {
            return ReplaceSection(newManifest, "manifest");
        }

        internal XElement ReplaceSpine(XElement newSpine)
        {
            return ReplaceSection(newSpine, "spine");
        }
        #endregion

        #region Get FilesList & General OPF Info
        private XElement GetManifest()
        {
            //XElement ret = new XElement(OldTOC.Element(ns + "manifest"));
            //return ret;
            return GetXmlElement("manifest");
        }

        private List<string> GetFilesList(Predicate<string> filter)
        {
            try
            {
                Dictionary<XAttribute, int> dict = new Dictionary<XAttribute, int>();
                foreach (XAttribute item in SpineIDs)
                {
                    dict.Add(item, dict.Count + 1);
                }

                var ret = (from i in GetManifest().Elements(ns + "item")
                           join j in dict on i.Attribute("id").Value equals j.Key.Value into g
                           from k in g.DefaultIfEmpty(new KeyValuePair<XAttribute, int>(i.Attribute("id"), dict.Last().Value + 1))
                           where filter.Invoke(i.Attribute("media-type").Value.ToLower()) || filter.Invoke(i.Attribute("href").Value.ToLower())
                           orderby k.Value
                           select Utils.VerifyFilenameEncoding(i.Attribute("href").Value)).ToList();

                return ret;
            } catch (Exception)
            {
                return null;
            }


        }

        internal Dictionary<string, string> GetFilesList()
        {
            try
            {
                Dictionary<string, string> ret = (from i in GetManifest().Elements(ns + "item")
                                                  select i).ToDictionary(x => x.Attribute("id").Value, x => Utils.VerifyFilenameEncoding(x.Attribute("href").Value));


                return ret;
            } catch (Exception)
            {

                return null;
            }
        }

        internal List<string> GetFilesList(string filter)
        {
            return GetFilesList(x => x.ToLower().Contains(filter.ToLower()) || x.ToLower().EndsWith(filter.ToLower()));
        }

        public string GetNCXfilename()
        {
            XElement spine = GetXmlElement("spine");
            XAttribute att = spine != null ? spine.Attribute("toc") : null;
            string nCXfile = "ncx";
            if (att != null)
            {
                string id = att.Value;
                nCXfile = Utils.GetSrc(id);
            } else
            {
                System.Windows.Forms.MessageBox.Show("There is no reference to your TOC in the \"spine\"\n" +
                    "Please open the reading order editor and save to fix the problem.\n" +
                    "If you do not your TOC might not be accesible in Calibre and/or Reader/Software", "TOC reference missing",
                    System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Information);

                //No reference of TOC in spine look for mime-type
                var list = GetFilesList("application/x-dtbncx+xml");
                string mime = list != null ? list.FirstOrDefault() : "";

                if (!String.IsNullOrEmpty(mime))
                    nCXfile = mime;

                //If all else fails uses the ncx extension to find the file
            }
            return nCXfile;
        }

        public string GetNCXid()
        {
            string mime = GetFilesList("application/x-dtbncx+xml").FirstOrDefault();

            if (!String.IsNullOrEmpty(mime))
            {
                return Utils.GetId(mime);
            }

            return "";
        }

        public string GetSpineRefAtIndex(int index)
        {
            string ret = string.Empty;
            if (index <= SpineIDs.Count - 1)
            {
                ret = GetFilesList()[SpineIDs[index].Value];
            }

            return ret;

        }

        private IEnumerable<string> GetGuideRefList(string type)
        {
            XElement ManifestGuide = GetXmlElement("guide");
            IEnumerable<string> coverRef = null;

            if (ManifestGuide != null)
            {
                coverRef = (from g in ManifestGuide.Elements(ns + "reference")
                            where g.Attribute("type").Value == type
                            select g.Attribute("href").Value);
            } else
            {
                coverRef = new List<string>();
            }
            return coverRef;
        }

        private string GetGuideRef(string type)
        {
            return GetGuideRefList(type).FirstOrDefault();
        }

        public string GetCoverRef()
        {
            return GetGuideRef("cover");
        }

        public string GetHtmlTOCRef()
        {
            string s = GetGuideRef("toc");

            if (!string.IsNullOrEmpty(s))
            {
                return s.Split('#')[0];
            }

            return "";
        }

        public List<string> GetFilesFromOPF()
        {
            try
            {
                List<string> ret = (from i in GetFilesList()
                                    select i.Value.StartsWith("../") ? i.Value.Substring(3) : Path.Combine(Variables.OPFpath, i.Value)).ToList();

                return ret;
            } catch (Exception)
            {

                return new List<string>();
            }
        }

        #endregion
    }

}

