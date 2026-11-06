using System;
using System.IO;
using Sobre.Models;

namespace Sobre.Services
{
    /// <summary>
    /// Minimal append-only diagnostics file at %LOCALAPPDATA%\Sobre\sobre.log.
    /// Deliberately never throws: logging must not be able to take the app down.
    /// </summary>
    public static class Log
    {
        public static string Path => System.IO.Path.Combine(Store.DataDirectory, "sobre.log");

        public static void Error(string context, Exception ex)
        {
            Write("ERROR", $"{context}: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
        }

        public static void Error(Exception ex) => Write("ERROR", $"{ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");

        public static void Info(string message) => Write("INFO", message);

        private static void Write(string level, string message)
        {
            try
            {
                Directory.CreateDirectory(Store.DataDirectory);
                File.AppendAllText(Path, $"[{DateTimeOffset.Now:O}] {level} {message}\n\n");
            }
            catch
            {
                // never let diagnostics break the app
            }
        }
    }
}
