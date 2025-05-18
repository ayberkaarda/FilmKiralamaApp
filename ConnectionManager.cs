using System;
using System.Data.SqlClient;

namespace FilmKiralama
{
    internal class ConnectionManager
    {
        private static readonly string connectionString = @"Server=localhost\SQLEXPRESS;Database=FilmKiralama;Trusted_Connection=True;";

        public static SqlConnection GetConnection()
        {
            SqlConnection connection = new SqlConnection(connectionString);
            return connection;
        }
    }
}
