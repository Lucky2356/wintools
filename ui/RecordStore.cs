using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace Wintools
{
    // One JSON file per change inside WintoolsData: atomic replacement, no links, bounded size. Each module validates its own schema after Load.
    internal sealed class RecordStore
    {
        private readonly string folder, label;
        private readonly int limit;
        internal RecordStore(string folder, string label, int limit)
        {
            this.folder = folder;
            this.label = label;
            this.limit = limit;
        }

        internal string Folder
        {
            get
            {
                return Path.Combine(Program.Data, folder);
            }
        }

        internal void Save(string path, object value)
        {
            var bytes = new UTF8Encoding(false).GetBytes(new JavaScriptSerializer().Serialize(value));
            if (bytes.Length > limit)
                throw new IOException(Lang.T("Запись истории (") + label + Lang.T(") слишком велика."));
            Write(path, bytes);
        }

        internal void Write(string path, byte[] bytes)
        {
            Program.SafeDirectory(Folder);
            Directory.CreateDirectory(Folder);
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new IOException(Lang.T("История (") + label + Lang.T(") является ссылкой."));
            try
            {
                using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    file.Write(bytes, 0, bytes.Length);
                    file.Flush(true);
                }

                if (File.Exists(path))
                    File.Replace(temporary, path, null);
                else
                    File.Move(temporary, path);
            }
            finally
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
        }

        internal T Load<T>(string path)
        {
            Program.SafeDirectory(Folder);
            var info = new FileInfo(path);
            if (info.Length > limit || (info.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException(Lang.T("Некорректный файл истории (") + label + ").");
            return new JavaScriptSerializer().Deserialize<T>(File.ReadAllText(path));
        }

        internal T[] All<T>(Func<string, T> read, Func<T, string> time)
        {
            if (!Directory.Exists(Folder))
                return new T[0];
            Program.SafeDirectory(Folder);
            return Directory.GetFiles(Folder, "*.json").Select(p => read(Path.GetFileNameWithoutExtension(p))).OrderByDescending(time, StringComparer.Ordinal).ToArray();
        }
    }
}
