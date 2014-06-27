using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using CoreTweet;
using CoreTweet.Core;
using FavoImgs.Data;
using FavoImgs.Security;

namespace FavoImgs
{
    internal static class Program
    {
        private static readonly string dataPath =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FavoImgs");

        private static void Initialize()
        {
            InitializeDataDirectory();

            if (!TweetCache.IsCreated())
                TweetCache.Create();
        }

        private static void InitializeDataDirectory()
        {
            if (String.IsNullOrEmpty(dataPath))
                throw new DirectoryNotFoundException();

            if (!Directory.Exists(dataPath))
                Directory.CreateDirectory(dataPath);
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
                OAuth.OAuthSession session = OAuth.Authorize(consumerKey, consumerSecret);
                Uri url = session.AuthorizeUri;
                Process.Start(url.ToString());

                Console.Write("ENTER PIN: ");
                string pin = Console.ReadLine();

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

            long maxId = 0;

            for (var i = 0; i < 50; ++i)
            {
                var arguments = new Dictionary<string, object> {{"count", 200}};
                if (maxId != 0)
                    arguments.Add("max_id", maxId - 1);

                ListedResponse<Status> favorites;
                try
                {
                    favorites = tokens.Favorites.List(arguments);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                    return 1;
                }

                foreach (var twt in favorites)
                {
                    maxId = maxId == 0 ? twt.Id : Math.Min(maxId, twt.Id);

                    var twtxt = ShowTweet(twt);
                    Console.WriteLine(twtxt);

                    if (!TweetCache.IsExist(twt.Id))
                        TweetCache.Add(twt);
                    else if (TweetCache.IsImageTaken(twt.Id))
                    {
                        Console.WriteLine(" - already taken image. pass...\n");
                        continue;
                    }

                    var dir = GetSubDirectoryName(
                        Settings.Current.DownloadPath,
                        Settings.Current.DirectoryNamingConvention,
                        twt.CreatedAt, twt.User.ScreenName);

                    if (!Directory.Exists(dir))
                        Directory.CreateDirectory(dir);

                    var isAllDownloaded = true;
                    if (twt.Entities.Urls != null)
                    {
                        foreach (var url in twt.Entities.Urls)
                        {
                            var wc = new WebClient();
                            var uri = url.ExpandedUrl;

                            if (!IsImageFile(uri.ToString()))
                                continue;

                            Console.WriteLine(" - Downloading... {0} (Url)", uri.ToString());

                            try
                            {
                                wc.DownloadFile(uri, Path.Combine(dir, uri.Segments.Last()));
                            }
                            catch (Exception ex)
                            {
                                isAllDownloaded = false;
                                Console.WriteLine(ex.Message);
                            }
                        }
                    }

                    if (twt.ExtendedEntities != null && twt.ExtendedEntities.Media != null)
                    {
                        foreach (var media in twt.ExtendedEntities.Media)
                        {
                            var wc = new WebClient();
                            var uri = media.MediaUrl;

                            if (!IsImageFile(uri.ToString()))
                                continue;

                            Console.WriteLine(" - Downloading... {0} (Twitter image)", uri.ToString());

                            try
                            {
                                var newuri = ModifyImageUri(uri.ToString());
                                wc.DownloadFile(newuri, Path.Combine(dir, uri.Segments.Last()));
                            }
                            catch (Exception ex)
                            {
                                isAllDownloaded = false;
                                Console.WriteLine(ex.Message);
                            }
                        }
                    }
                    else if (twt.Entities.Media != null)
                    {
                        foreach (var media in twt.Entities.Media)
                        {
                            var wc = new WebClient();
                            var uri = media.MediaUrl;

                            if (!IsImageFile(uri.ToString()))
                                continue;

                            Console.WriteLine(" - Downloading... {0} (Twitter image)", uri.ToString());

                            try
                            {
                                var newuri = ModifyImageUri(uri.ToString());
                                wc.DownloadFile(newuri, Path.Combine(dir, uri.Segments.Last()));
                            }
                            catch (Exception ex)
                            {
                                isAllDownloaded = false;
                                Console.WriteLine(ex.Message);
                            }
                        }
                    }

                    if (isAllDownloaded)
                        TweetCache.SetImageTaken(twt.Id);

                    Console.WriteLine();
                }

                Console.WriteLine("Limit: {0}/{1}, Reset: {2}",
                    favorites.RateLimit.Remaining,
                    favorites.RateLimit.Limit,
                    favorites.RateLimit.Reset.LocalDateTime.ToString(CultureInfo.InvariantCulture));
            }

            Console.WriteLine("Press ENTER to exit...");
            Console.ReadLine();

            Settings.Current.Save();
            return 0;
        }
    }
}