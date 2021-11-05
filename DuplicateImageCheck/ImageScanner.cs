using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CoenM.ImageHash;
using CoenM.ImageHash.HashAlgorithms;
using System.IO;
using System.Text.Json;

namespace DuplicateImageCheck
{
	class ImageScanner
	{
		private readonly string cacheFilename = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DuplicateImageCheck", "imagehashes.json");

		protected class CacheFolder
		{
			public Dictionary<string, ulong> imageHashes { get; set; } = new Dictionary<string, ulong>();
		}

		protected class CacheFile
		{
			public Dictionary<string, CacheFolder> folders { get; set; } = new Dictionary<string, CacheFolder>();
		}

		private async Task<CacheFile> ReadCacheFile()
		{
			var result = new CacheFile();

			if (!File.Exists(cacheFilename))
				return result;

			string cache = await File.ReadAllTextAsync(cacheFilename);
			result = JsonSerializer.Deserialize<CacheFile>(cache);

			return result;
		}

		private async Task SaveCacheFile(CacheFile cacheFile)
		{
			string cache = JsonSerializer.Serialize(cacheFile);
			Directory.CreateDirectory(Path.GetDirectoryName(cacheFilename));
			await File.WriteAllTextAsync(cacheFilename, cache);
		}

		public delegate void OnStatusChangedDelegate(string status);
		public event OnStatusChangedDelegate OnStatusChanged;

		public class ImageMatch
		{
			public string fileName1 { get; set; }
			public string fileName2 { get; set; }
			public double similarity { get; set; }
		}

		public async Task<List<ImageMatch>> Process(string folder, double threshold)
		{
			var result = new List<ImageMatch>();

			if (!Directory.Exists(folder))
				return result;

			//Load cache and find the folder in the cache (if it's there) 
			CacheFile cache = await ReadCacheFile();
			CacheFolder imageFolderHashes;
			
			if(cache.folders.ContainsKey(folder))
			{
				imageFolderHashes = cache.folders[folder];
			}
			else
			{
				imageFolderHashes = new CacheFolder();
				cache.folders.Add(folder, imageFolderHashes);
			}

			OnStatusChanged?.Invoke("Cache loaded");

			//Find all files in folder, remove missing files from cache and find new files to add to cache
			var allFiles = Directory.EnumerateFiles(folder)
				.Where(f => f.ToLower().EndsWith(".jpeg") || f.ToLower().EndsWith(".jpg") ||
				f.ToLower().EndsWith(".png") || f.ToLower().EndsWith(".webp"))
				.ToList();

			bool saveCache = false;
			int oldCacheSize = imageFolderHashes.imageHashes.Count;

			//Remove deleted files from cache
			imageFolderHashes.imageHashes = new Dictionary<string, ulong>(imageFolderHashes.imageHashes.Where(
				   h => allFiles.Where(s => s.EndsWith(h.Key)).Count() > 0).ToList());

			if (imageFolderHashes.imageHashes.Count != oldCacheSize)
				saveCache = true;

			var filesToProcess = allFiles.Where(f => !imageFolderHashes.imageHashes.ContainsKey(Path.GetFileName(f))).ToList();

			var hash = new PerceptualHash();

			if (filesToProcess.Count > 0)
			{
				await Task.Run(() =>
				{
					for (int i = 0; i < filesToProcess.Count; i++)
					{
						using (FileStream fileStream = File.OpenRead(filesToProcess[i]))
						{
							ulong hashcode = hash.Hash(fileStream);
							imageFolderHashes.imageHashes.Add(Path.GetFileName(filesToProcess[i]), hashcode);
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

			foreach(KeyValuePair<string, ulong> kvp in imageFolderHashes.imageHashes)
			{
				foreach (KeyValuePair<string, ulong> kvp2 in imageFolderHashes.imageHashes)
				{
					if(kvp.Key != kvp2.Key)
					{
						double similarity = CompareHash.Similarity(kvp.Value, kvp2.Value);

						if(similarity >= threshold)
						{
							bool found = false;

							foreach(ImageMatch m in result)
							{
								if(m.fileName1 == kvp2.Key)
								{
									found = true;
									break;
								}
							}

							if (!found)
							{
								result.Add(new ImageMatch()
								{
									fileName1 = kvp.Key,
									fileName2 = kvp2.Key,
									similarity = similarity
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
