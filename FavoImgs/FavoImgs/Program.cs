using CoreTweet;
using CoreTweet.Core;
using FavoImgs.Data;
using FavoImgs.Security;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FavoImgs
{
    internal static class Program
    {
        private static readonly string DataPath =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FavoImgs");

        private static void Initialize()
        {
            InitializeDataDirectory();
        }

        private static void InitializeDataDirectory()
        {
            if (String.IsNullOrEmpty(DataPath))
                throw new DirectoryNotFoundException();

            if (!Directory.Exists(DataPath))
                Directory.CreateDirectory(DataPath);
        }

        private static string ShowTweet(Status tweet)
        {
            return String.Format("{0} (@{1})  -- {2}\n{3}",
                tweet.User.Name, tweet.User.ScreenName, tweet.CreatedAt.LocalDateTime, tweet.Text);
        }

        private static void ShowAppInfo()
        {
            var version = Assembly.GetEntryAssembly().GetName().Version;

            Console.WriteLine("FavoImgs {0}, Copyright (c) 2014, Azyu (@_uyza_)", version);
            Console.WriteLine("http://github.com/azyu/FavoImgs");
            Console.WriteLine("============================================================");
            Console.WriteLine();
        }

        private static string ShowFolderBrowserDialog()
        {
            var b = new FolderBrowserDialog {Description = "Select folder to save..."};

            return b.ShowDialog() == DialogResult.OK
                ? b.SelectedPath
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "FavoImgs");
        }

        private static void CheckDownloadPath()
        {
            if (String.IsNullOrEmpty(Settings.Current.DownloadPath))
            {
                string downloadPath = ShowFolderBrowserDialog();
                Settings.Current.DownloadPath = downloadPath;
            }

            Console.WriteLine("[] Download Path: {0}\n", Settings.Current.DownloadPath);
        }

        private static string GetSubDirectoryName(string basePath, DirectoryNamingConvention convention,
            DateTimeOffset createdAt, string screenName)
        {
            string retpath;
            switch (convention)
            {
                default:
                    retpath = basePath;
                    break;

                case DirectoryNamingConvention.Date:
                    retpath = Path.Combine(basePath, createdAt.LocalDateTime.ToString("yyyyMMdd"));
                    break;

                case DirectoryNamingConvention.ScreenName:
                    retpath = Path.Combine(basePath, screenName);
                    break;

                case DirectoryNamingConvention.Date_ScreenName:
                    retpath = Path.Combine(basePath, createdAt.LocalDateTime.ToString("yyyyMMdd"), screenName);
                    break;

                case DirectoryNamingConvention.ScreenName_Date:
                    retpath = Path.Combine(basePath, screenName, createdAt.LocalDateTime.ToString("yyyyMMdd"));
                    break;
            }

            return retpath;
        }

        private static bool IsImageFile(string uri)
        {
            const string pattern = @"^.*\.(jpg|JPG|gif|GIF|png|PNG)$";
            return Regex.IsMatch(uri, pattern);
        }

        private static string ModifyImageUri(string uri)
        {
            var retval = String.Empty;

            // Twitter image
            if (uri.Contains("twimg.com"))
            {
                retval = uri + ":large";
            }

            return retval;
        }


        private static Tokens GetTwitterToken(string consumerKey, string consumerSecret, string accessToken,
            string accessTokenSecret)
        {
            Tokens tokens;

            if (String.IsNullOrEmpty(accessToken) || String.IsNullOrEmpty(accessTokenSecret))
            {
                var session = OAuth.Authorize(consumerKey, consumerSecret);
                var url = session.AuthorizeUri;
                Process.Start(url.ToString());

                Console.Write("ENTER PIN: ");
                var pin = Console.ReadLine();

                tokens = session.GetTokens(pin);

                Settings.Current.AccessToken = RijndaelEncryption.EncryptRijndael(tokens.AccessToken);
                Settings.Current.AccessTokenSecret = RijndaelEncryption.EncryptRijndael(tokens.AccessTokenSecret);
                Settings.Current.Save();
            }
            else
            {
                tokens = Tokens.Create(consumerKey, consumerSecret, accessToken, accessTokenSecret);
            }

            return tokens;
        }

        [STAThread]
        private static int Main()
        {
            ShowAppInfo();
            Initialize();

            Settings.Load();
            CheckDownloadPath();

            var consumerKey = Settings.Current.ConsumerKey;
            var consumerSecret = Settings.Current.ConsumerSecret;
            var accessToken = Settings.Current.AccessToken;
            var accessTokenSecret = Settings.Current.AccessTokenSecret;

            try
            {
                if (!String.IsNullOrEmpty(accessToken))
                    accessToken = RijndaelEncryption.DecryptRijndael(accessToken);

                if (!String.IsNullOrEmpty(accessTokenSecret))
                    accessTokenSecret = RijndaelEncryption.DecryptRijndael(accessTokenSecret);
            }
            catch (Exception)
            {
                Console.WriteLine("Cannot read OAuth Token!");
                accessToken = null;
                accessTokenSecret = null;
            }

            Tokens tokens;
            try
            {
                tokens = GetTwitterToken(consumerKey, consumerSecret, accessToken, accessTokenSecret);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                return 1;
            }

            var downloadPath = Settings.Current.DownloadPath;
            if (!Directory.Exists(downloadPath))
                Directory.CreateDirectory(downloadPath);

            var files = Directory.GetFiles(downloadPath, "*.*",
                            SearchOption.AllDirectories)
                            .Where(s => s.EndsWith(".mp4") || s.EndsWith(".jpg") || s.EndsWith(".png"));


            const int count = 200;
            long maxId = 0;
            var bRemainTweet = true;

            ListedResponse<Status> favorites = null;

            while (bRemainTweet)
            {
                var arguments = new Dictionary<string, object> {{"count", count}};
                if (maxId != 0) arguments.Add("max_id", maxId - 1);

                try
                {
                    favorites = tokens.Favorites.List(arguments);
                }
                catch (TwitterException ex)
                {
                    // Too many request: Twitter limit exceeded
                    if (ex.Status == (HttpStatusCode)429)
                    {
                        Console.WriteLine("Twitter API limit에 걸렸습니다. " +
                                          "60초 뒤에 재시도 합니다~");
                        if (favorites != null)
                        {
                            Console.WriteLine("limit이 풀리는 시간은 아래와 같습니다");
                            Console.Write(favorites.RateLimit.Reset.LocalDateTime
                                .ToString(CultureInfo.InvariantCulture));
                            
                        }
                        Thread.Sleep(600000); // 60 초 동안 쉰다 
                        continue;
                    }
                    Console.WriteLine(ex.Message);
                    return 1;
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                    return 1;
                }
                    
                // update max id
                maxId = favorites.Max(twt => twt.Id);

                Parallel.ForEach(favorites, twt =>
                {
                    var pathnames = new List<string>();
                    var uris = new List<string>();

                    if (twt.ExtendedEntities != null && twt.ExtendedEntities.Media != null)
                    {
                        foreach (var uri in twt.ExtendedEntities.Media.Select(media => media.MediaUrl))
                        {
                            var pathname = Path.Combine(downloadPath, uri.Segments.Last());
                            if (File.Exists(pathname)) continue;
                            pathnames.Add(pathname);
                            uris.Add(ModifyImageUri(uri.ToString()));
                        }
                    }
                    else if (twt.Entities.Urls != null)
                    {
                        foreach (var url in twt.Entities.Urls)
                        {
                            try
                            {
                                var uri = url.ExpandedUrl;
                                var htmlCode = "";
                                try
                                {
                                    var htmlwc = new WebClient();
                                    htmlCode = htmlwc.DownloadString(uri);
                                }
                                // Ex: http://www.hibrain.net/hibrainWebApp/servlet/ExtraBoardManager;jsessionid=cbf601fa30d525c2695a62ba45f1af11a728c9fa9859.e34Sc30Qb3mSc40ObxiSchiSbNb0n6jAmljGr5XDqQLvpAe?extraboardCmd=view&menu_id=29&extraboard_id=132163&group_id=132129&program_code=10&list_type=list&pageno=1
                                catch (WebException)
                                {
                                    continue;
                                }

                                var doc = new HtmlAgilityPack.HtmlDocument();
                                doc.LoadHtml(htmlCode);

                                var nodes = doc.DocumentNode.SelectNodes("//source");
                                if (nodes == null) continue;

                                foreach (var link in nodes)
                                {
                                    if (!link.Attributes.Any(x => x.Name == "type" && x.Value == "video/mp4"))
                                        continue;

                                    var attributes = link.Attributes.Where(x => x.Name == "video-src").ToList();
                                    foreach (var att in attributes)
                                    {
                                        var pathname = Path.Combine(downloadPath, att.Value.Split('/').Last());
                                        if (File.Exists(pathname)) continue;
                                        pathnames.Add(pathname);
                                        uris.Add(att.Value);
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine(ex.ToString());
                            }
                        }
                    }

                    var twtxt = ShowTweet(twt);
                    Console.WriteLine(twtxt);

                    var wc = new WebClient();
                    for (var i = 0; i < uris.Count; i++)
                    {
                        try
                        {
                            Console.WriteLine(" - Downloading... {0} (Twitter image)", uris[i].ToString());
                            wc.DownloadFile(uris[i], pathnames[i]);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine(ex.Message);
                        }
                    }

                    Console.WriteLine();
                });

                Console.WriteLine("Limit: {0}/{1}, Reset: {2}",
                    favorites.RateLimit.Remaining,
                    favorites.RateLimit.Limit,
                    favorites.RateLimit.Reset.LocalDateTime.ToString(CultureInfo.InvariantCulture));

                if (favorites.Count < count)
                    bRemainTweet = false;
            }

            Console.WriteLine("Press ENTER to exit...");
            Console.ReadLine();

            Settings.Current.Save();
            return 0;
        }
    }
}