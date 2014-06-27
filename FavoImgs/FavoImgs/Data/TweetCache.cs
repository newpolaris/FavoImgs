﻿#region

using System;
using System.Data.SQLite;
using System.IO;
using CoreTweet;

#endregion

namespace FavoImgs.Data
{
    public static class TweetCache
    {
        private static readonly string cachePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FavoImgs",
            "Tweets.db");

        public static bool IsCreated()
        {
            return File.Exists(cachePath);
        }

        public static void Create()
        {
            try
            {
                if (!File.Exists(cachePath))
                    SQLiteConnection.CreateFile(cachePath);

                var connstr = String.Format("Data Source={0};Version=3", cachePath);
                var conn = new SQLiteConnection(connstr);
                conn.Open();

                var cmd = new SQLiteCommand(conn);

                var query = "CREATE TABLE [Favorites] (" +
                               "[Id] bigint PRIMARY KEY NOT NULL," +
                               "[CreatedAt] datetime NOT NULL," +
                               "[UserId] bigint NOT NULL," +
                               "[Text] nvarchar(140) NOT NULL," +
                               "[State] int NOT NULL);";

                cmd.CommandText = query;
                cmd.ExecuteNonQuery();

                query =
                    "CREATE TABLE [MediaUris] (" +
                    "[Id] bigint NOT NULL," +
                    "[Uri] nvarchar(256) NOT NULL);" +
                    "CREATE INDEX [IX_Id] ON [MediaUris] ([Id]);";

                cmd.CommandText = query;
                cmd.ExecuteNonQuery();

                conn.Close();
            }
            catch
            {
                throw;
            }
        }

        public static bool IsExist(long Id)
        {
            try
            {
                string connstr = String.Format("Data Source={0};Version=3", cachePath);
                var conn = new SQLiteConnection(connstr);
                conn.Open();

                var cmd = new SQLiteCommand(conn);

                string query = String.Empty;

                query = @"SELECT count(*) FROM [Favorites] WHERE [Id] = @Id";
                cmd.CommandText = query;
                cmd.Parameters.AddWithValue("@Id", Id);

                int count = Convert.ToInt32(cmd.ExecuteScalar());
                return (count != 0);
            }
            catch
            {
                throw;
            }
        }

        public static long GetLastestId()
        {
            try
            {
                string connstr = String.Format("Data Source={0};Version=3", cachePath);
                var conn = new SQLiteConnection(connstr);
                conn.Open();

                var cmd = new SQLiteCommand(conn);

                string query = String.Empty;

                query = @"SELECT Id FROM [Favorites] order by Id DESC limit 1";
                cmd.CommandText = query;

                Int64 Id = Convert.ToInt64(cmd.ExecuteScalar());
                return Id;
            }
            catch
            {
                throw;
            }
        }

        public static bool ResetImageTaken()
        {
            try
            {
                string connstr = String.Format("Data Source={0};Version=3", cachePath);
                var conn = new SQLiteConnection(connstr);
                conn.Open();

                var cmd = new SQLiteCommand(conn);

                string query = String.Empty;

                query = @"UPDATE [Favorites] SET [State] = 0";
                cmd.CommandText = query;
                int rowcount = cmd.ExecuteNonQuery();

                return (rowcount != 0);
            }
            catch
            {
                throw;
            }
        }

        public static bool SetImageTaken(long Id)
        {
            try
            {
                string connstr = String.Format("Data Source={0};Version=3", cachePath);
                var conn = new SQLiteConnection(connstr);
                conn.Open();

                var cmd = new SQLiteCommand(conn);

                string query = String.Empty;

                query = @"UPDATE [Favorites] SET [State] = 1 WHERE [Id] = @Id";
                cmd.CommandText = query;
                cmd.Parameters.AddWithValue("@Id", Id);
                int rowcount = cmd.ExecuteNonQuery();

                return (rowcount != 0);
            }
            catch
            {
                throw;
            }
        }

        public static bool IsImageTaken(long Id)
        {
            try
            {
                string connstr = String.Format("Data Source={0};Version=3", cachePath);
                var conn = new SQLiteConnection(connstr);
                conn.Open();

                var cmd = new SQLiteCommand(conn);

                string query = String.Empty;

                query = @"SELECT [State] FROM [Favorites] WHERE [Id] = @Id";
                cmd.CommandText = query;
                cmd.Parameters.AddWithValue("@Id", Id);

                int state = Convert.ToInt32(cmd.ExecuteScalar());
                return (state != 0);
            }
            catch
            {
                throw;
            }
        }

        public static long GetOldestId()
        {
            try
            {
                string connstr = String.Format("Data Source={0};Version=3", cachePath);
                var conn = new SQLiteConnection(connstr);
                conn.Open();

                var cmd = new SQLiteCommand(conn);

                string query = String.Empty;

                query = @"SELECT Id FROM [Favorites] order by Id ASC limit 1";
                cmd.CommandText = query;

                Int64 Id = Convert.ToInt64(cmd.ExecuteScalar());
                return Id;
            }
            catch
            {
                throw;
            }
        }

        public static void Add(Status status)
        {
            try
            {
                string connstr = String.Format("Data Source={0};Version=3", cachePath);
                var conn = new SQLiteConnection(connstr);
                conn.Open();

                var cmd = new SQLiteCommand(conn);

                string query = String.Empty;

                query = @"INSERT INTO [Favorites] ([Id], [CreatedAt], [UserId], [Text], [State])
                VALUES (@Id, @CreateAt, @UserId, @Text, 0)";
                cmd.CommandText = query;
                cmd.Parameters.AddWithValue("@Id", status.Id);
                cmd.Parameters.AddWithValue("@CreateAt", status.CreatedAt);
                cmd.Parameters.AddWithValue("@UserId", status.User.Id);
                cmd.Parameters.AddWithValue("@Text", status.Text);

                cmd.ExecuteNonQuery();

                if (status.ExtendedEntities != null && status.ExtendedEntities.Media != null)
                {
                    foreach (var media in status.ExtendedEntities.Media)
                    {
                        query = "INSERT INTO [MediaUris] ([Id], [Uri]) VALUES (@Id, @Uri)";

                        cmd.CommandText = query;
                        cmd.Parameters.AddWithValue("@Id", status.Id);
                        cmd.Parameters.AddWithValue("@Uri", media.MediaUrl);
                        cmd.ExecuteNonQuery();
                    }
                }

                conn.Close();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }
    }
}