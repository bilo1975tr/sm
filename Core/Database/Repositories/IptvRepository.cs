using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using StreamMesh.Models;

namespace StreamMesh.Core.Database.Repositories
{
    public class IptvRepository
    {
        private readonly string _connectionString;

        public IptvRepository(string connectionString)
        {
            _connectionString = connectionString;
        }

        public List<IptvAccount> GetAllIptvAccounts()
        {
            var list = new List<IptvAccount>();
            using (var connection = new SqliteConnection(_connectionString))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT Id, Name, ServerUrl, Username, Password, Status, ExpiryDate FROM IptvAccounts";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    list.Add(new IptvAccount {
                        Id = reader.GetString(0), Name = reader.GetString(1), ServerUrl = reader.GetString(2),
                        Username = reader.GetString(3),
                        Password = Decrypt(reader.GetString(4)),
                        Status = reader.GetString(5),
                        ExpiryDate = DateTime.TryParse(reader.GetString(6), out DateTime dt) ? dt : DateTime.MinValue
                    });
                }
            }
            return list;
        }

        public void SaveIptvAccount(IptvAccount acc)
        {
            using (var connection = new SqliteConnection(_connectionString))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "INSERT INTO IptvAccounts (Id, Name, ServerUrl, Username, Password, Status, ExpiryDate) VALUES (@Id, @N, @U, @Un, @P, @S, @E) ON CONFLICT(Id) DO UPDATE SET Name=@N, ServerUrl=@U, Username=@Un, Password=@P, Status=@S, ExpiryDate=@E";
                cmd.Parameters.AddWithValue("@Id", acc.Id); cmd.Parameters.AddWithValue("@N", acc.Name);
                cmd.Parameters.AddWithValue("@U", acc.ServerUrl); cmd.Parameters.AddWithValue("@Un", acc.Username);
                cmd.Parameters.AddWithValue("@P", Encrypt(acc.Password)); cmd.Parameters.AddWithValue("@S", acc.Status);
                cmd.Parameters.AddWithValue("@E", acc.ExpiryDate.ToString("o"));
                cmd.ExecuteNonQuery();
            }
        }

        public void RemoveIptvAccount(string id)
        {
            using (var connection = new SqliteConnection(_connectionString))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "DELETE FROM IptvAccounts WHERE Id = @Id";
                cmd.Parameters.AddWithValue("@Id", id); cmd.ExecuteNonQuery();
            }
        }

        private string Encrypt(string clearText)
        {
            if (string.IsNullOrEmpty(clearText)) return "";
            try
            {
                byte[] clearBytes = Encoding.Unicode.GetBytes(clearText);
                using (var encryptor = System.Security.Cryptography.Aes.Create())
                {
                    var pdb = new System.Security.Cryptography.Rfc2898DeriveBytes("StreamMesh_Safe_Pass_2024", new byte[] { 0x49, 0x76, 0x61, 0x6e, 0x20, 0x4d, 0x65, 0x64, 0x76, 0x65, 0x64, 0x65, 0x76 }, 1000, System.Security.Cryptography.HashAlgorithmName.SHA256);
                    encryptor.Key = pdb.GetBytes(32);
                    encryptor.IV = pdb.GetBytes(16);
                    using (var ms = new MemoryStream())
                    {
                        using (var cs = new System.Security.Cryptography.CryptoStream(ms, encryptor.CreateEncryptor(), System.Security.Cryptography.CryptoStreamMode.Write))
                        {
                            cs.Write(clearBytes, 0, clearBytes.Length);
                            cs.Close();
                        }
                        return Convert.ToBase64String(ms.ToArray());
                    }
                }
            }
            catch { return clearText; }
        }

        private string Decrypt(string cipherText)
        {
            if (string.IsNullOrEmpty(cipherText)) return "";
            if (cipherText.Length < 8) return cipherText;

            try
            {
                byte[] cipherBytes = Convert.FromBase64String(cipherText);
                using (var encryptor = System.Security.Cryptography.Aes.Create())
                {
                    var pdb = new System.Security.Cryptography.Rfc2898DeriveBytes("StreamMesh_Safe_Pass_2024", new byte[] { 0x49, 0x76, 0x61, 0x6e, 0x20, 0x4d, 0x65, 0x64, 0x76, 0x65, 0x64, 0x65, 0x76 }, 1000, System.Security.Cryptography.HashAlgorithmName.SHA256);
                    encryptor.Key = pdb.GetBytes(32);
                    encryptor.IV = pdb.GetBytes(16);
                    using (var ms = new MemoryStream())
                    {
                        using (var cs = new System.Security.Cryptography.CryptoStream(ms, encryptor.CreateDecryptor(), System.Security.Cryptography.CryptoStreamMode.Write))
                        {
                            cs.Write(cipherBytes, 0, cipherBytes.Length);
                            cs.Close();
                        }
                        return Encoding.Unicode.GetString(ms.ToArray());
                    }
                }
            }
            catch { return cipherText; }
        }
    }
}
