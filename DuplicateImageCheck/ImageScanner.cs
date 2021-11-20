using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CoenM.ImageHash;
using CoenM.ImageHash.HashAlgorithms;
using System.IO;
using System.Text.Json;
using System.Globalization;

namespace DuplicateImageCheck
{
	class ImageScanner
	{
		private readonly string _cacheFilename = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DuplicateImageCheck", "imagehashes.json");

		protected class CacheFolder
		{
			public Dictionary<string, ulong> ImageHashes { get; set; } = new Dictionary<string, ulong>();
		}

		protected class CacheFile
		{
			public Dictionary<string, CacheFolder> Folders { get; set; } = new Dictionary<string, CacheFolder>();
		}

		private async Task<CacheFile> ReadCacheFile()
		{
			var result = new CacheFile();

			if (!File.Exists(_cacheFilename))
				return result;

			string cache = await File.ReadAllTextAsync(_cacheFilename);
			result = JsonSerializer.Deserialize<CacheFile>(cache);

			return result;
		}

		private async Task SaveCacheFile(CacheFile cacheFile)
		{
			string cache = JsonSerializer.Serialize(cacheFile);
			Directory.CreateDirectory(Path.GetDirectoryName(_cacheFilename));
			await File.WriteAllTextAsync(_cacheFilename, cache);
		}

		public delegate void OnStatusChangedDelegate(string status);
		public event OnStatusChangedDelegate OnStatusChanged;

		public class ImageMatch
		{
			public string FileName1 { get; set; }
			public string FileName2 { get; set; }
			public double Similarity { get; set; }
		}

		public async Task<List<ImageMatch>> Process(string folder, double threshold)
		{
			var result = new List<ImageMatch>();

			if (!Directory.Exists(folder))
				return result;

			//Load cache and find the folder in the cache (if it's there) 
			CacheFile cache = await ReadCacheFile();
			CacheFolder imageFolderHashes;

			if (cache.Folders.ContainsKey(folder))
			{
				imageFolderHashes = cache.Folders[folder];
			}
			else
			{
				imageFolderHashes = new CacheFolder();
				cache.Folders.Add(folder, imageFolderHashes);
			}

			OnStatusChanged?.Invoke("Cache loaded");

			//Find all files in folder, remove missing files from cache and find new files to add to cache
			var validExtensions = new HashSet<string> { ".jpeg", ".jpg", ".png", ".bmp", ".tga", ".webp" };

			var allFiles = new HashSet<string>(Directory.EnumerateFiles(folder)
				.Where((string fileName) =>
				{
					string ext = Path.GetExtension(fileName).ToLower(CultureInfo.InvariantCulture);
					return validExtensions.Contains(ext);
				})
				.Select((string fileName) =>
				{
					return Path.GetFileName(fileName);
				})
				.ToList());

			bool saveCache = false;
			int oldCacheSize = imageFolderHashes.ImageHashes.Count;

			//Remove deleted files from cache
			imageFolderHashes.ImageHashes = new Dictionary<string, ulong>(
				imageFolderHashes.ImageHashes.Where(h => allFiles.Contains(h.Key))
				.ToList());

			if (imageFolderHashes.ImageHashes.Count != oldCacheSize)
				saveCache = true;

			var filesToProcess = allFiles.Where(f => !imageFolderHashes.ImageHashes.ContainsKey(f)).ToList();

			var hash = new PerceptualHash();

			if (filesToProcess.Count > 0)
			{
				await Task.Run(() =>
				{
					for (int i = 0; i < filesToProcess.Count; i++)
					{
						using (FileStream fileStream = File.OpenRead(Path.Combine(folder, filesToProcess[i])))
						{
							ulong hashcode = hash.Hash(fileStream);
							imageFolderHashes.ImageHashes.Add(filesToProcess[i], hashcode);
						}

						OnStatusChanged?.Invoke($"Processing {(i + 1).ToString().PadLeft(4, '0')} of {filesToProcess.Count.ToString().PadLeft(4, '0')}");
					}
				});

				saveCache = true;
			}

			if (saveCache)
			{
				await SaveCacheFile(cache);
			}

			OnStatusChanged?.Invoke("Comparing images");

			foreach (KeyValuePair<string, ulong> kvp in imageFolderHashes.ImageHashes)
			{
				foreach (KeyValuePair<string, ulong> kvp2 in imageFolderHashes.ImageHashes)
				{
					if (kvp.Key != kvp2.Key)
					{
						double similarity = CompareHash.Similarity(kvp.Value, kvp2.Value);

						if (similarity >= threshold)
						{
							bool found = false;

							foreach (ImageMatch m in result)
							{
								if (m.FileName1 == kvp2.Key)
								{
									found = true;
									break;
								}
							}

							if (!found)
							{
								result.Add(new ImageMatch()
								{
									FileName1 = kvp.Key,
									FileName2 = kvp2.Key,
									Similarity = similarity
								});
							}
						}
					}
				}
			}

			OnStatusChanged?.Invoke("Done");
			return result;
		}
	}
}
