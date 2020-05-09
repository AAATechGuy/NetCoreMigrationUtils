using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace MigrationUtils
{
    public class FindConflictsProgram
    {
        public void FindAllReferencedAssemblies(
            string folder,
            string referencedAssemblyName,
            bool ignoreSystemLibs,
            int depth = 0,
            HashSet<string> duplicateCheckMap = null)
        {
            var assemblies = GetAllAssemblies(folder, rootFilter: null);

            if (duplicateCheckMap == null)
            {
                Log.Info("");
                Log.Info($"===================> Assemblies referenced by {referencedAssemblyName}");
                duplicateCheckMap = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            referencedAssemblyName = referencedAssemblyName.Replace(".dll", "");
            var assembly = assemblies.Where(r => string.Equals(r.GetName().Name, referencedAssemblyName, StringComparison.OrdinalIgnoreCase)).SingleOrDefault();

            if (assembly != null)
            {
                if (ignoreSystemLibs && !File.Exists(Path.Combine(folder, assembly.GetName().Name + ".dll")))
                {
                    return;
                }

                if (duplicateCheckMap.Contains(assembly.GetName().Name))
                {
                    Log.Info($"{new String(' ', depth * 2)}{assembly.ToIdString()} ^");
                    return;
                }

                Log.Info($"{new String(' ', depth * 2)}{assembly.ToIdString()}");
                duplicateCheckMap.Add(assembly.GetName().Name);

                foreach (var assemblyRef in assembly.GetReferencedAssemblies())
                {
                    FindAllReferencedAssemblies(folder, assemblyRef.Name, ignoreSystemLibs, depth + 1, duplicateCheckMap);
                }
            }
        }

        public IEnumerable<string> FindReferencingAssemblies(string folder, string referencedAssemblyName, bool partialCompare = true, string[] rootFilter = null)
        {
            referencedAssemblyName = referencedAssemblyName.Replace(".dll", "");

            var comparer = partialCompare
                ? new Func<string, string, bool>((str, subStr) => str.IndexOf(subStr, StringComparison.OrdinalIgnoreCase) != -1)
                : new Func<string, string, bool>((str, subStr) => string.Equals(str, subStr, StringComparison.OrdinalIgnoreCase));

            var assemblies = GetAllAssemblies(folder, rootFilter);

            var references = GetReferencesFromAllAssemblies(assemblies);

            var referencingAssemblies = references.Where(r => comparer(r.ReferencedAssembly.Name, referencedAssemblyName))
                .GroupBy(r => r.ReferencedAssembly.Name, StringComparer.OrdinalIgnoreCase);

            foreach (var referencingAssembly in referencingAssemblies)
            {
                Console.Out.WriteLine($"{Environment.NewLine}=========> Assemblies referencing for {referencingAssembly.Key}");

                foreach (var referencingAssemblyItem in referencingAssembly)
                {
                    Console.Out.WriteLine("{0} references {1}",
                                          referencingAssemblyItem.Assembly.Name.PadRight(70),
                                          referencingAssemblyItem.ReferencedAssembly.Name);

                    yield return referencingAssemblyItem.Assembly.Name;
                }
            }
        }

        public void FindConflictingReferences(string folder, bool ignoreSystemLibs, string[] rootFilter)
        {
            var assemblies = GetAllAssemblies(folder, rootFilter);

            var references = GetReferencesFromAllAssemblies(assemblies);

            var groupsOfConflicts = FindReferencesWithTheSameShortNameButDiffererntFullNames(references);

            foreach (var group in groupsOfConflicts)
            {
                Console.Out.WriteLine(Environment.NewLine + "=========> Possible conflicts for {0}:", group.Key);
                if (!ignoreSystemLibs || (!group.Key.Equals("mscorlib") && !group.Key.StartsWith("System")))
                {
                    foreach (var reference in group)
                    {
                        Console.Out.WriteLine("{0} references {1}",
                                              reference.Assembly.Name.PadRight(70),
                                              reference.ReferencedAssembly.FullName);
                    }
                }
                else
                {
                    Console.Out.WriteLine("ignore");
                }
            }
        }

        private IEnumerable<IGrouping<string, Reference>> FindReferencesWithTheSameShortNameButDiffererntFullNames(List<Reference> references)
        {
            return from reference in references
                   group reference by reference.ReferencedAssembly.Name
                       into referenceGroup
                   where referenceGroup.ToList().Select(reference => reference.ReferencedAssembly.FullName).Distinct().Count() > 1
                   select referenceGroup;
        }

        private List<Reference> GetReferencesFromAllAssemblies(List<Assembly> assemblies)
        {
            var references = new List<Reference>();
            foreach (var assembly in assemblies)
            {
                foreach (var referencedAssembly in assembly.GetReferencedAssemblies())
                {
                    references.Add(new Reference
                    {
                        Assembly = assembly.GetName(),
                        ReferencedAssembly = referencedAssembly
                    });
                }
            }
            return references;
        }

        private static Dictionary<string, Dictionary<string, Assembly>> FolderToAssembliesMap
            = new Dictionary<string, Dictionary<string, Assembly>>(StringComparer.OrdinalIgnoreCase);

        internal static List<Assembly> GetAllAssemblies(string path, string[] rootFilter)
            => GetAllAssembliesV2(path, rootFilter).Select(x => x.Value).ToList();

        internal static Dictionary<string, Assembly> GetAllAssembliesV2(string path, string[] rootFilter)
        {
            if (FolderToAssembliesMap.ContainsKey(path))
            {
                // TODO: cache by filter
                return FilterByRoot(FolderToAssembliesMap[path], rootFilter);
            }

            var files = new List<FileInfo>();
            var directoryToSearch = new DirectoryInfo(path);
            files.AddRange(directoryToSearch.GetFiles("*.dll", SearchOption.AllDirectories));
            files.AddRange(directoryToSearch.GetFiles("*.exe", SearchOption.AllDirectories));
            var result = files
                .ConvertAll(file =>
                {
                    try
                    {
                        return Assembly.LoadFile(file.FullName);
                    }
                    catch (Exception ex)
                    {
                        Log.Info($"Error loading [{file.Name}] : {ex.Message}");
                        return null;
                    }
                })
                .Where(dll => dll != null)
                .GroupBy(x => x.GetName().Name, StringComparer.OrdinalIgnoreCase)
                .Select(x => x.First())
                .OrderBy(x => x.GetName().Name)
                .ToDictionary(x => x.GetName().Name, StringComparer.OrdinalIgnoreCase);

            var metaPath = Path.GetFullPath("meta.log");
            Log.Info($"listing read assemblies at: \"{metaPath}\"");
            File.WriteAllLines(metaPath, result.Keys.OrderBy(x => x).ToArray());

            FolderToAssembliesMap.Add(path, result);
            return FilterByRoot(result, rootFilter);
        }

        private static Dictionary<string, Assembly> FilterByRoot(Dictionary<string, Assembly> assemblies, string[] rootFilter)
        {
            if (rootFilter == null || rootFilter.Length == 0)
                return assemblies;

            var rootAssemblies = rootFilter.Where(r => assemblies.ContainsKey(r)).ToArray();
            if (rootAssemblies.Length != rootAssemblies.Length)
                throw new InvalidOperationException("invalid root filters specified");

            var filteredAssemblies = rootFilter.Select(a => assemblies[a])
                .ToDictionary(x => x.GetName().Name, StringComparer.OrdinalIgnoreCase);

            var queue = new Queue<string>(rootFilter);
            while (queue.Count != 0)
            {
                var assemblyName = queue.Dequeue();
                if (assemblies.ContainsKey(assemblyName))
                {
                    var assembly = assemblies[assemblyName];
                    if (!filteredAssemblies.ContainsKey(assemblyName))
                        filteredAssemblies.Add(assemblyName, assembly);

                    foreach (var refAssembly in assembly.GetReferencedAssemblies())
                    {
                        queue.Enqueue(refAssembly.Name);
                    }
                }
            }

            return filteredAssemblies;
        }

        internal static Dictionary<string, Assembly[]> GetAssembliesRefByMap(string sourceAssembliesFolder, string[] rootFilter)
        {
            var assemblies = GetAllAssemblies(sourceAssembliesFolder, rootFilter);
            var assembliesMap = GetAllAssembliesV2(sourceAssembliesFolder, rootFilter);

            var allAssemblies = assemblies
                .SelectMany(a => a.GetReferencedAssemblies().Select(r => Tuple.Create(a.GetName(), r)));

            var assemblyLookup = assemblies.ToDictionary(x => x.GetName().Name, x => x.GetName(), StringComparer.OrdinalIgnoreCase);
            foreach (var refAssembly in allAssemblies.Select(x => x.Item2))
            {
                if (!assemblyLookup.ContainsKey(refAssembly.Name))
                {
                    assemblyLookup.Add(refAssembly.Name, refAssembly);
                }
            }

            var assemblyRefMap = allAssemblies
                .GroupBy(refInfo => refInfo.Item2.Name)
                .OrderBy(g => g.Key)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(gi => assembliesMap[gi.Item1.Name]).OrderBy(gi => gi.GetName().Name).ToArray());

            return assemblyRefMap;
        }

        private class Reference
        {
            public AssemblyName Assembly { get; set; }
            public AssemblyName ReferencedAssembly { get; set; }
        }
    }
}