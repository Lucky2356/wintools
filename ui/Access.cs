using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Wintools {
    internal static class Access {
        private static string PathName { get { return Path.Combine(Program.Data,"github-access.bin"); } }
        internal static string Load() {
            if(!File.Exists(PathName))return null;
            try{return Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(PathName),null,DataProtectionScope.CurrentUser));}
            catch(CryptographicException){throw new IOException("Доступ GitHub сохранён для другого пользователя Windows. Введите токен заново в настройках.");}
        }
        internal static void Save(string token) {
            token=(token??"").Trim();
            if(token.Length==0){if(File.Exists(PathName))File.Delete(PathName);return;}
            if(token.Length>512 || token.IndexOfAny(new[]{'\r','\n',' ','\t'})>=0)throw new ArgumentException("Введите только токен GitHub, без пробелов и заголовков.");
            Program.SafeDirectory(Program.Data);Directory.CreateDirectory(Program.Data);
            var temp=PathName+"."+Guid.NewGuid().ToString("N")+".tmp";
            try{File.WriteAllBytes(temp,ProtectedData.Protect(Encoding.UTF8.GetBytes(token),null,DataProtectionScope.CurrentUser));if(File.Exists(PathName))File.Replace(temp,PathName,null);else File.Move(temp,PathName);}
            finally{if(File.Exists(temp))File.Delete(temp);}
        }
    }
}
