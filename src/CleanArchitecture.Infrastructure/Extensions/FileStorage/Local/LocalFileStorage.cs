﻿using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CleanArchitecture.Infrastructure.Extensions.FileStorage.Local
{
    public class LocalFileStorage : IFileStorage
    {
        private readonly LocalFileStorageOptions _localFileStorageOptions;

        public LocalFileStorage(IOptions<LocalFileStorageOptions> localFileStorageOptions)
        {
            _localFileStorageOptions = localFileStorageOptions.Value ?? throw new ArgumentNullException(nameof(localFileStorageOptions));
        }

        public Task PrepareAsync(string path)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));

            var tempPath = GetTempPath(path);
            using var fileStream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            return Task.CompletedTask;
        }

        public async Task<Stream?> WriteAsync(string path, Stream stream, long offset, long length)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));
            if (stream == null) throw new ArgumentNullException(nameof(stream));

            long fileLength = 0;
            var tempPath = GetTempPath(path);
            using (var fileStream = new FileStream(tempPath, FileMode.Open, FileAccess.Write, FileShare.None))
            {
                fileStream.Seek(offset, SeekOrigin.Begin);
                await stream.CopyToAsync(fileStream);
                fileLength = fileStream.Length;
            }

            if (fileLength == length)
            {
                var actualPath = GetActualPath(path);
                File.Move(tempPath, actualPath, false);
                return new FileStream(actualPath, FileMode.Open, FileAccess.Read, FileShare.None);
            }
            else if (fileLength > length)
            {
                throw new ArgumentOutOfRangeException(nameof(offset), offset, "Offset exceed the expected length.");
            }
            else
            {
                return null;
            }
        }

        public Task DeleteAsync(string path)
        {
            var tempPath = GetTempPath(path);

            if (File.Exists(tempPath))
                File.Delete(tempPath);

            var actualPath = GetActualPath(path);

            if (File.Exists(actualPath))
                File.Delete(actualPath);

            return Task.CompletedTask;
        }

        string GetTempPath(string path)
        {
            return $"{GetActualPath(path)}.temp";
        }

        string GetActualPath(string path)
        {
            path = $"{_localFileStorageOptions.RootPath}{path.Replace("/", "\\")}";
            string sourceDirectory = Path.GetDirectoryName(path)!;

            if (!Directory.Exists(sourceDirectory))
                Directory.CreateDirectory(sourceDirectory);

            return path;
        }
    }
}