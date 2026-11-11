using System;
using System.IO;
using System.Text;

// Disk operations are independent from Unity, so recovery can be tested without opening a scene.
public static class SaveFileStore
{
    public static T Read<T>(string path, Func<string, T> parse, Action<string> warn) where T : class
    {
        foreach (string candidate in new[] { path, path + ".bak" })
        {
            if (!File.Exists(candidate)) continue;
            try
            {
                T value = parse(File.ReadAllText(candidate));
                if (value == null) throw new InvalidDataException("Empty save data.");
                if (candidate != path) warn?.Invoke("Recovered save from backup: " + candidate);
                return value;
            }
            catch (Exception e) when (e is InvalidDataException || e is IOException || e is UnauthorizedAccessException || e is ArgumentException)
            { warn?.Invoke("Could not read " + candidate + ": " + e.Message); }
        }
        return null;
    }

    public static void Write(string path, string json)
    {
        string temp = path + ".tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            byte[] bytes = new UTF8Encoding(false).GetBytes(json);
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush(true);
        }
        // Do not overwrite the valid destination if replacement fails: keep it and report the error.
        if (File.Exists(path)) File.Replace(temp, path, path + ".bak");
        else File.Move(temp, path);
    }
}

