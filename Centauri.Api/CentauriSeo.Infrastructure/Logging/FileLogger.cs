using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CentauriSeo.Infrastructure.Logging
{
    public sealed class FileLogger : IDisposable
    {
        private readonly string _filePath;
        private readonly SemaphoreSlim _sem = new(1, 1);
        private bool _disposed;

        public FileLogger(string? filePath = null)
        {
            _filePath = string.IsNullOrWhiteSpace(filePath)
                ? Path.Combine(AppContext.BaseDirectory, "logs", "centauri.log")
                : filePath;

            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
        }

        public Task LogInformationAsync(string message) => LogAsync("INFO", message);
        public Task LogDebugAsync(string message) => LogAsync("DEBUG", message);
        public Task LogWarningAsync(string message) => LogAsync("WARN", message);
        public Task LogErrorAsync(string message) => LogAsync("ERROR", message);

        private async Task LogAsync(string level, string message)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(FileLogger));

            var timestamp = DateTime.UtcNow.ToString("o");
            var sb = new StringBuilder();
            sb.Append('[').Append(timestamp).Append("] ");
            sb.Append('[').Append(level).Append("] ");
            sb.Append(message).AppendLine();

            var bytes = Encoding.UTF8.GetBytes(sb.ToString());

            await _sem.WaitAsync().ConfigureAwait(false);
            try
            {
                // Use FileStream with FileMode.Append for safety and minimal allocations
                using var fs = new FileStream(_filePath, FileMode.Append, FileAccess.Write, FileShare.Read, 4096, useAsync: true);
                await fs.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
                await fs.FlushAsync().ConfigureAwait(false);
            }
            finally
            {
                _sem.Release();
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _sem.Dispose();
            _disposed = true;
        }
    }
}
