using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Verse;

namespace Doorstop
{
	internal class Reloader
	{
		internal static void Start()
		{
			var harmony = new Harmony("brrainz.doorstop");
			harmony.PatchAll(Assembly.GetExecutingAssembly());
			instance = new Reloader();
		}

		internal static Reloader instance;
		const string doorstopPrefix = "doorstop_";
		readonly string modsDir;
		static readonly Dictionary<string, MethodBase> reloadableMembers = [];
		static readonly List<FileSystemWatcher> watchers = [];
		static readonly Debouncer changedFiles = new(TimeSpan.FromSeconds(3), basePath =>
		{
			var path = $"{basePath}.dll";
			try
			{
				var assembly = ReloadAssembly(path, true);
				UpdateAssembly(assembly);
				$"{path} reloaded".LogMessage();
			}
			catch (Exception ex)
			{
				ex.ToString().LogError();
			}
		});

		internal Reloader()
		{
			modsDir = Path.Combine(Directory.GetCurrentDirectory(), "Mods");
			DeleteAllFiles();

			watchers.Add(CreateWatcher("dll"));
			watchers.Add(CreateWatcher("pdb"));
		}

		FileSystemWatcher CreateWatcher(string suffix)
		{
			var watcher = new FileSystemWatcher(modsDir)
			{
				Filter = $"*.{suffix}",
				IncludeSubdirectories = true,
				EnableRaisingEvents = true
			};
			watcher.Error += (sender, e) => e.GetException().ToString().LogError();
			watcher.Changed += (object _, FileSystemEventArgs e) =>
			{
				var path = e.FullPath;

				if (path.Replace('\\', '/').Contains("/obj/"))
					return;

				var filename = Path.GetFileNameWithoutExtension(path);
				if (filename.StartsWith(doorstopPrefix))
					return;

				changedFiles.Add(path.WithoutFileExtension());
			};
			return watcher;
		}

		internal static void RewriteAssemblyResolving()
		{
			AppDomain.CurrentDomain.ReflectionOnlyAssemblyResolve += (sender, args) =>
			{
				var requestedAssemblyName = new AssemblyName(args.Name).Name;
				var loadedAssembly = AppDomain.CurrentDomain.GetAssemblies()
					 .FirstOrDefault(a => new AssemblyName(a.FullName).Name == requestedAssemblyName);

				if (loadedAssembly != null)
					return Assembly.ReflectionOnlyLoadFrom(loadedAssembly.Location);

				throw new InvalidOperationException($"Unable to resolve assembly: {args.Name}");
			};
		}

		static void FileChanged(object _, FileSystemEventArgs e)
		{

		}

		internal void DeleteAllFiles()
		{
			DeleteFiles(modsDir, $"{doorstopPrefix}??????????????_*.dll");
			DeleteFiles(modsDir, $"{doorstopPrefix}??????????????_*.pdb");
		}

		static void DeleteFiles(string directory, string searchPattern)
		{
			foreach (var file in Directory.GetFiles(directory, searchPattern, SearchOption.AllDirectories))
				try
				{ File.Delete(file); }
				finally { }
		}

		internal static Assembly LoadOriginalAssembly(string path)
		{
			var originalAssembly = ReloadAssembly(path, false);
			originalAssembly.GetTypes().SelectMany(type => Tools.AllReloadableMembers(type, reflectionOnly: false))
				.Do(member =>
				{
					$"registered {member.FullDescription()} for reloading [{member.Id()}]".LogMessage();
					reloadableMembers[member.Id()] = member;
				});
			return originalAssembly;
		}

		static int n = 0;
		static Assembly ReloadAssembly(string path, bool reflectionOnly)
		{
			var assembliesDir = Path.GetDirectoryName(path);
			var baseName = Path.GetFileNameWithoutExtension(path);
			var filenamePrefix = $"{doorstopPrefix}{DateTime.Now:yyyyMMddHHmmss}_";

			var originalDll = Path.Combine(assembliesDir, $"{baseName}.dll");
			var copyDllPath = Path.Combine(assembliesDir, $"{filenamePrefix}{baseName}.dll");
			Tools.Copy(originalDll, copyDllPath, ++n);

			var dllBytes = File.ReadAllBytes(copyDllPath);
			if (reflectionOnly)
				return Assembly.ReflectionOnlyLoad(dllBytes);

			var originalPdb = Path.Combine(assembliesDir, $"{baseName}.pdb");
			if (File.Exists(originalPdb))
			{
				var copyPdbPath = Path.Combine(assembliesDir, $"{filenamePrefix}{baseName}.pdb");
				File.Copy(originalPdb, copyPdbPath, true);

				var pdbBytes = File.ReadAllBytes(copyPdbPath);
				return AppDomain.CurrentDomain.Load(dllBytes, pdbBytes);
			}

			return AppDomain.CurrentDomain.Load(dllBytes);
		}

		static void UpdateAssembly(Assembly assembly)
		{
			assembly.ReflectAllTypes().SelectMany(type => Tools.AllReloadableMembers(type, reflectionOnly: true)).Do(member =>
			{
				if (reloadableMembers.TryGetValue(member.Id(), out var originalMethod))
					Tools.DetourMethod(originalMethod, member);
			});
		}
	}
}