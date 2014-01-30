namespace ZetaHtmlEditControl
{
    using Helper;
    using Sgml;
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.IO;
    using System.Net;
    using System.Text.RegularExpressions;
    using System.Xml;

    internal sealed class HtmlConversionHelper :
        IDisposable
    {
        private readonly List<string> _cleanPaths = new List<string>();

        internal string ConvertGetHtml(
            string html,
            Uri baseUri,
            string saveFolderPath,
            string imagesFolderPathPlaceHolder)
        {
            if (string.IsNullOrEmpty(html))
            {
                return html;
            }
            else
            {
                if (string.IsNullOrEmpty(saveFolderPath))
                {
                    saveFolderPath =
                        Path.Combine(
                            Path.GetTempPath(),
                            Guid.NewGuid().ToString());
                    Directory.CreateDirectory(saveFolderPath);

                    if (!_cleanPaths.Contains(saveFolderPath))
                    {
                        _cleanPaths.Add(saveFolderPath);
                    }
                }
                else
                {
                    if (!Directory.Exists(saveFolderPath))
                    {
                        Directory.CreateDirectory(saveFolderPath);
                    }
                }

                var imageInfos = FindImgs(html);

                foreach (var imageInfo in imageInfos)
                {
                    // pfad bauen
                    var filePath = Path.Combine(saveFolderPath, Guid.NewGuid().ToString());

                    // holen
                    byte[] image = null;
                    if (!imageInfo.Source.StartsWith(Uri.UriSchemeHttp) &&
                        !imageInfo.Source.StartsWith(Uri.UriSchemeHttps) &&
                        !imageInfo.Source.StartsWith(Uri.UriSchemeFtp))
                    {
                        // 2006-12-03, Uwe Keim.
                        var readFrom = imageInfo.Source.StartsWith(Uri.UriSchemeFile) ? PathHelper.ConvertFileUrlToFilePath(imageInfo.Source) : imageInfo.Source;

                        var pf = GetPathFromFile(readFrom, baseUri);

                        if (File.Exists(pf))
                        {
                            image = File.ReadAllBytes(pf);
                        }
                    }
                    else
                    {
                        image = new WebClient().DownloadData(getWebAddressFromFile(imageInfo.Source, baseUri));
                    }

                    if (image != null && IsValidImage(image))
                    {
                        //schreiben
                        File.WriteAllBytes(filePath, image);
                        checkResizeImage(filePath, imageInfo.Width, imageInfo.Height);

                        //ersetzen
                        var pattern = string.Format(
                            @"([""']){0}([""'])",
                            escapeRegularExpressionCharacters(imageInfo.Source));

                        var fileUrlPath =
                            PathHelper.ConvertFilePathToFileUrl(filePath);

                        // If requested to have placeholder, put it now.
                        if (!string.IsNullOrEmpty(imagesFolderPathPlaceHolder))
                        {
                            var folderUrlPath =
                                PathHelper.ConvertFilePathToFileUrl(saveFolderPath);

                            fileUrlPath =
                                PathHelper.CombineVirtual(
                                    imagesFolderPathPlaceHolder,
                                    fileUrlPath.Substring(folderUrlPath.Length));
                        }

                        var replacement = string.Format(
                            @"$1{0}$2",
                            fileUrlPath);

                        html = Regex.Replace(
                            html,
                            pattern,
                            replacement,
                            RegexOptions.IgnoreCase);
                    }
                }

                return html;
            }
        }

        private static bool IsValidImage(byte[] aImage)
        {
            try
            {
                using (MemoryStream ms = new MemoryStream())
                using (Image im = Image.FromStream(ms)) { }
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static void checkResizeImage(string imageFilePath, int width, int height)
        {
            // 2013-05-20, Uwe Keim:
            // Das Bild so in der Gr��e anpassen, wie es im HTML auch tats�chlich angegeben wurde.

            if (width > 0 && height > 0 && !string.IsNullOrEmpty(imageFilePath) && File.Exists(imageFilePath))
            {
                using (var img = ImageHelper.LoadImage(imageFilePath))
                {
                    if (img.Width != width || img.Height != height)
                    {
                        using (var img2 = ImageScaler.ScaleImage(img, width, height))
                        {
                            ImageHelper.SaveImage(img2, imageFilePath);
                        }
                    }
                }
            }
        }

        internal string[] GetContainedImageFileNames(
            string html,
            string imagesFolderPathPlaceHolder)
        {
            if (string.IsNullOrEmpty(html) ||
                string.IsNullOrEmpty(imagesFolderPathPlaceHolder))
            {
                return new string[] { };
            }
            else
            {
                var result = new List<string>();

                var imgs = FindImgs(html);
                foreach (var img in imgs)
                {
                    if (img.Source.StartsWith(imagesFolderPathPlaceHolder, StringComparison.InvariantCultureIgnoreCase))
                    {
                        var s = img.Source.Substring(imagesFolderPathPlaceHolder.Length).Trim('\\', '/');
                        result.Add(s);
                    }
                }

                return result.ToArray();
            }
        }

        internal string ConvertSetHtml(
            string html,
            string saveFolderPath,
            string imagesFolderPathPlaceHolder)
        {
            if (string.IsNullOrEmpty(html) ||
                string.IsNullOrEmpty(imagesFolderPathPlaceHolder))
            {
                return html;
            }
            else
            {
                var folderUrlPath = PathHelper.ConvertFilePathToFileUrl(saveFolderPath).TrimEnd('/');

                imagesFolderPathPlaceHolder = imagesFolderPathPlaceHolder.TrimEnd('/');

                html = html.Replace(imagesFolderPathPlaceHolder, folderUrlPath);

                return html;
            }
        }

        internal class ImageInfo
        {
            public string Source { get; set; }
            public int Width { get; set; }
            public int Height { get; set; }
        }

        internal static ImageInfo[] FindImgs(
            string htmlCode)
        {
            var r =
                new SgmlReader
                    {
                        DocType = @"HTML",
                        InputStream = new StringReader(htmlCode)
                    };
            var al = new List<ImageInfo>();

            //find <img src=""
            while (r.Read())
            {
                if (r.NodeType == XmlNodeType.Element)
                {
                    if (string.Compare(r.Name, @"img", StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        if (r.HasAttributes)
                        {
                            var ii = new ImageInfo();

                            while (r.MoveToNextAttribute())
                            {
                                switch (r.Name.ToLowerInvariant())
                                {
                                    case @"src":
                                        ii.Source = r.Value;
                                        break;
                                    case @"width":
                                        ii.Width = ConvertHelper.ToInt32(r.Value);
                                        break;
                                    case @"height":
                                        ii.Height = ConvertHelper.ToInt32(r.Value);
                                        break;
                                }
                            }

                            // --

                            if (!string.IsNullOrEmpty(ii.Source))
                            {
                                al.Add(ii);
                            }
                        }
                    }
                }
            }

            return al.ToArray();
        }

        private static string getWebAddressFromFile(
            string file,
            Uri baseUri)
        {
            if (file.StartsWith(@"http") || file.StartsWith(@"https"))
            {
                return file;
            }
            else if (file.IndexOf(@"\", StringComparison.Ordinal) == 0)
            {
                return baseUri + file;
            }
            else if (file.IndexOf(@"/", StringComparison.Ordinal) == 0)
            {
                return baseUri + file;
            }
            else if (file.StartsWith(@"file")
                || file.Substring(1, 2) == @":\"
                || baseUri.AbsolutePath == @"about:blank")
            {
                return file;
            }
            else
            {
                return baseUri + @"/" + file;
            }
        }

        private static string escapeRegularExpressionCharacters(
            string text)
        {
            text = text.Replace(@"\", @"\\");
            text = text.Replace(@"+", @"\+");
            text = text.Replace(@"?", @"\?");
            text = text.Replace(@".", @"\.");
            text = text.Replace(@"*", @"\*");
            text = text.Replace(@"^", @"\^");
            text = text.Replace(@"$", @"\$");
            text = text.Replace(@"(", @"\(");
            text = text.Replace(@")", @"\)");
            text = text.Replace(@"[", @"\[");
            text = text.Replace(@"]", @"\]");
            text = text.Replace(@"{", @"\{");
            text = text.Replace(@"}", @"\}");
            text = text.Replace(@"|", @"\|");
            return text;
        }

        void IDisposable.Dispose()
        {
            endClean();
        }

        ~HtmlConversionHelper()
        {
            endClean();
        }

        private void endClean()
        {
            foreach (var s in _cleanPaths)
            {
                if (!string.IsNullOrEmpty(s))
                {
                    if (Directory.Exists(s))
                    {
                        Directory.Delete(s, true);
                    }
                }
            }
            _cleanPaths.Clear();
        }

        internal static string GetPathFromFile(
            string s,
            Uri baseUri)
        {
            // 2006-12-03 Uwe Keim, fix for not copying images.
            if (string.Compare(baseUri.OriginalString, @"about:blank",
                StringComparison.OrdinalIgnoreCase) == 0)
            {
                return s;
            }
            else
            {
                var result = Path.Combine(baseUri.AbsolutePath, s);

                return result;
            }
        }
    }
}