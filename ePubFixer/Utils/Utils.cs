﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using Ionic.Zip;
using Aga.Controls.Tree;
using System.IO;
using HtmlAgilityPack;
using System.Text.RegularExpressions;
using ePubFixer.Properties;

namespace ePubFixer
{
    public static class Utils
    {
        #region Fields
        public static event EventHandler<ExtractProgressArgs> ExtractProgress;
        static ExtractProgressArgs Progress;
        private static Dictionary<string, string> FileList = null;

        public static void NewFilename(bool BackupDone)
        {
            Variables.FileDecrypted = false;
            Variables.HeaderTextInFile = new Dictionary<string, List<string>>();
            Variables.AnchorTextInFile = new Dictionary<string, List<string>>();
            Variables.AnchorsInFile = new Dictionary<string, List<string>>();
            FileList = null;
            Variables.BackupDone = BackupDone;
            Variables.OPFfile = string.Empty;
            Variables.NCXFile = string.Empty;
        }

        public static void NewFilename()
        {
            NewFilename(false);
        }
        #endregion

        #region Get Icon & Title
        public static string GetTitle()
        {
            Assembly ass = Assembly.GetExecutingAssembly();
            var name = ass.GetName();
            String Version = "v" + ass.GetName().Version.Major + ".";
            Version += ass.GetName().Version.Minor;
            if (ass.GetName().Version.Build > 0)
            {
                Version += "." + ass.GetName().Version.Build;
            }

            // assembly title attribute
            String Title = ((AssemblyTitleAttribute)ass.GetCustomAttributes(
                        typeof(AssemblyTitleAttribute), false)[0]).Title;

#if DRM
            Title += " (DRM Removal)";
#endif

            return Title + " " + Version;

        }

        public static Icon GetIcon()
        {
            string str = new AssemblyName(Assembly.GetExecutingAssembly().FullName).Name;
            return new Icon(Assembly.GetExecutingAssembly().GetManifestResourceStream(str + ".epub.ico"));
        }
        #endregion

        #region Preview

        public static void CloseOpenedForms()
        {
            Variables.OpenedForm.ForEach(item => item.Close());
            Variables.OpenedForm = new List<Form>();
        }

        public static void ShowPreview(this Form frm, string filename, string Chapter)
        {

            try
            {
                frmPreview frmPreview = new frmPreview(filename, Chapter);
                //frm.AddOwnedForm(frmPreview);
                Variables.OpenedForm.Add(frmPreview);
                frmPreview.Show();
            }
            catch (Exception)
            {

            }
        }

        public static void ShowPreview(this Form frm, string filename)
        {
            ShowPreview(frm, filename, "Preview");
        }

        private static string ConvertToHTML(string file)
        {
            HtmlAgilityPack.HtmlDocument html = new HtmlAgilityPack.HtmlDocument();
            html.Load(file, Encoding.UTF8);

            //Find the image needed
            HtmlNodeCollection imgTag = html.DocumentNode.SelectNodes("//image ");
            if (imgTag != null)
            {
                foreach (HtmlNode item in imgTag)
                {
                    //Get the path for the Images
                    string tag = "xlink:href";
                    string src = item.GetAttributeValue(tag, "");

                    //Replace for XHTML
                    string inString = tag + "=\"" + src + "\"";
                    string outString = "src=\"" + src + "\"";
                    string newHtml = item.OuterHtml.Replace(inString, outString);
                    HtmlNode newNode = HtmlNode.CreateNode(newHtml);
                    item.ParentNode.ReplaceChild(newNode, item);
                }
            }

            #region Change Extension
            string ext = Path.GetExtension(file);
            if (ext == ".xhtml")
            {
                string newFile = Path.ChangeExtension(file, ".html");
                file = newFile;
            }

            if (ext != ".html")
            {
                string newFile = file + ".html";
                file = newFile;
            }
            #endregion

            File.WriteAllText(file, html.DocumentNode.OuterHtml, Encoding.UTF8);

            return file;
        }
        #endregion

        #region Get Content.opf Info
        public static string GetSrc(string idRef)
        {
            OpfDocument doc = new OpfDocument();
            Dictionary<string, string> srcTag = doc.GetFilesList();

            string src;
            if (srcTag.TryGetValue(idRef, out src))
            {
                return src;
            }
            return null;
        }

        public static string GetId(string Src)
        {
            OpfDocument doc = new OpfDocument();
            Dictionary<string, string> srcTag = doc.GetFilesList();

            return GetId(Src, srcTag);
        }

        public static string GetId(string Src, Dictionary<string, string> srcTag)
        {
            foreach (var item in srcTag)
            {
                if (item.Value == Src)
                {
                    return item.Key;
                }
            }

            return Guid.NewGuid().ToString();
        }

        public static Dictionary<string, string> GetFileList()
        {
            if (FileList == null)
            {
                OpfDocument doc = new OpfDocument();
                FileList = doc.GetFilesList();
            }

            return FileList;
        }
        #endregion

        #region Verify Files
        public static bool VerifyFileExists(string filename)
        {
            string file = GetFilePathInsideZipOPF(filename);
            return string.IsNullOrEmpty(file) ? false : true;
        }


        public static System.Collections.ObjectModel.Collection<Node> RemoveNonExistantNode(System.Collections.ObjectModel.Collection<Node> NodeCollection)
        {
            for (int i = NodeCollection.Count - 1; i >= 0; i--)
            {
                OpfDocument opfDoc = new OpfDocument();
                var htmlFiles = opfDoc.GetFilesList("html");

                Node item = NodeCollection[i];
                NavDetails nav = item.Tag as NavDetails;

                if (!Utils.VerifyFileExists(nav.File) | !htmlFiles.Contains(nav.File))
                {
                    NodeCollection.Remove(item);
                }

                RemoveNonExistantNode(item.Nodes);
            }

            return NodeCollection;
        }
        #endregion

        #region Zip
        #region ZipExtract
        internal static void ExtractZip()
        {
            try
            {
                using (ZipFile zip = ZipFile.Read(Variables.Filename))
                {
                    Progress = new ExtractProgressArgs();
                    Progress.TotalToTransfer = Convert.ToInt32(zip.Sum(e => e.UncompressedSize));
                    zip.ExtractProgress += new EventHandler<ExtractProgressEventArgs>(zip_ExtractProgress);
                    Old = 0; New = 0;
                    foreach (ZipEntry item in zip)
                    {

                        item.Extract(Variables.TempFolder, ExtractExistingFileAction.OverwriteSilently);
                        //Progress.Transferred += Convert.ToInt32(item.UncompressedSize);
                        ////System.Threading.Thread.Sleep(100);

                        //if (ExtractProgress!=null)
                        //{
                        //    ExtractProgress(item, Progress);
                        //}
                    }
                }
            }
            catch (Exception)
            {
                //NOTE File in use message (extract)
            }
        }

        static long Old;
        static long New;
        static void zip_ExtractProgress(object sender, ExtractProgressEventArgs e)
        {
            if (e.EventType == ZipProgressEventType.Extracting_EntryBytesWritten)
            {
                New = e.BytesTransferred;
                Progress.Transferred += New - Old;
                Old = e.BytesTransferred;

                if (ExtractProgress != null)
                {
                    ExtractProgress(e.CurrentEntry, Progress);
                }

            } else if (e.EventType == ZipProgressEventType.Extracting_AfterExtractEntry)
            {
                Old = 0;
            }
        }
        internal static void DeleteTemp()
        {
            try
            {
                if (Directory.Exists(Variables.TempFolder))
                {
                    Directory.Delete(Variables.TempFolder, true);
                }
            }
            catch (Exception)
            {

            }
        }

        internal static string GetTempFilePath(string filename)
        {
            string file = GetFilePathInsideZipOPF(filename);
            file = file.Replace('/', '\\');
            file = Variables.TempFolder + @"\" + file;

            file = ConvertToHTML(file);

            return file;
        }

        #endregion

        internal static string GetFilePathInsideZipOPF(string filename)
        {
            string file = "";
            if (!String.IsNullOrEmpty(filename))
            {
                using (ZipFile zip = ZipFile.Read(Variables.Filename))
                {
                    file = (from z in zip
                            where z.FileName == Variables.OPFpath + filename
                            select z.FileName).FirstOrDefault();
                }
            }

            return file;
        }

        internal static string GetFilePathInsideZip(string filename)
        {
            string file = "";
            if (!String.IsNullOrEmpty(filename))
            {
                using (ZipFile zip = ZipFile.Read(Variables.Filename))
                {
                    file = (from z in zip
                            where z.FileName.EndsWith(filename)
                            select z.FileName).FirstOrDefault();
                }
            }

            return file;
        }

        #endregion

        #region Decryption
        private static void ShowEncryptedMessage()
        {
            MessageBox.Show("This file is protected by DRM\n\"" + Variables.BookName + "\"",
                                    "File Protected", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        internal static bool IsEncrypted
        {
            get
            {
                List<string> AdeptFiles = new List<string>() { "META-INF/rights.xml", "META-INF/encryption.xml" };


                foreach (var item in AdeptFiles)
                {
                    if (string.IsNullOrEmpty(GetFilePathInsideZip(item)))
                        return false;
                }

#if !DRM
                ShowEncryptedMessage();
#else
                if (!Properties.Settings.Default.Decrypt)
                {
                    ShowEncryptedMessage();
                }
#endif

                return true;

            }
        }

        public static string SaveDirectory { get; set; }
        internal static bool DecryptFile()
        {
#if DRM
            try
            {
                if (Properties.Settings.Default.Decrypt && IsEncrypted && !Variables.FileDecrypted)
                {
                    FolderBrowserDialog save = new FolderBrowserDialog();
                    save.Description = "Please select the folder to save the Decrypted files into.";

                    if (string.IsNullOrEmpty(SaveDirectory))
                    {
                        if (save.ShowDialog() == DialogResult.OK)
                            SaveDirectory = save.SelectedPath;
                        else
                            return false;
                    }

                    string NewFilePath = SaveDirectory + "\\" + Variables.BookName;
                    if (File.Exists(NewFilePath) &&
                        MessageBox.Show("File Already Exists, Do you want to replace it?\n\"" + Variables.BookName + "\"",
                        "File Exists", MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation) == DialogResult.No)
                    {
                        return false;
                    }

                    string ProtectedFilePath = Variables.Filename;
                    Variables.Filename = NewFilePath;
                    Variables.Filenames[Variables.Filenames.IndexOf(ProtectedFilePath)] = NewFilePath;
                    using (new HourGlass())
                    {
                        Variables.FileDecrypted = Drm.Adept.Epub.Strip(ProtectedFilePath, NewFilePath);
                        if (Variables.FileDecrypted)
                        {
                            Properties.Settings.Default.RecentFiles.Add(NewFilePath);
                            SaveRecentFilesSettings(Properties.Settings.Default.RecentFiles.Cast<string>().ToList());
                            
                        }
                        return Variables.FileDecrypted;
                    }

                }
                return false;
            }
            catch (Exception e)
            {
                Variables.FileDecrypted = false;
                MessageBox.Show("File could not be decrypted\n" + e.Message, "Decryption Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }

#else
            return false;
#endif
        }



        #endregion

        public static void SaveRecentFilesSettings(List<string> LastFiles)
        {
            int Qty = LastFiles.Count > 10 ? 10 : LastFiles.Count;
            LastFiles = LastFiles.GetRange(LastFiles.Count - Qty, Qty);
            Properties.Settings.Default.RecentFiles.Clear();

            foreach (string item in LastFiles)
            {

                if (!Properties.Settings.Default.RecentFiles.Contains(item))
                    Properties.Settings.Default.RecentFiles.Add(item);
                else
                {
                    int index = LastFiles.IndexOf(item);
                    Properties.Settings.Default.RecentFiles.RemoveAt(index);
                    Properties.Settings.Default.RecentFiles.Add(item);
                }
            }

            Properties.Settings.Default.Save();
        }
    }
}
