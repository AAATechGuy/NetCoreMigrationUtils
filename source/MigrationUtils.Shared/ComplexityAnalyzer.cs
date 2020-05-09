using OpenSoftware.DgmlTools;
using OpenSoftware.DgmlTools.Analyses;
using OpenSoftware.DgmlTools.Builders;
using OpenSoftware.DgmlTools.Model;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace MigrationUtils
{
    public static class ComplexityAnalyzer
    {
        public static void CreateGraph(string sourceAssembliesFolder, string skipAssemblyFile, string targetFilepath, string rootCsv = null)
        {
            var rootFilter = rootCsv?.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            var skipAssemblies = GetSkipAssemblies(skipAssemblyFile, sourceAssembliesFolder, rootFilter);

            var allAssemblies = FindConflictsProgram.GetAllAssemblies(sourceAssembliesFolder, rootFilter)
                .Where(x => !skipAssemblies.Contains(x.GetName().Name));
            var assemblies = allAssemblies
                .Where(x => !ShouldSkip(x.GetName().Name, skipAssemblies))
                .ToArray();

            var allAssemblyLinks = allAssemblies.SelectMany(a => a.GetReferencedAssemblies().Select(a2 => Tuple.Create(a, a2)));
            var assemblyLinks = allAssemblyLinks
                .Where(x => !ShouldSkip(x.Item1.GetName().Name, skipAssemblies) && !ShouldSkip(x.Item2.Name, skipAssemblies));

            var builder = CreateBuilder();
            var graph = builder.Build(assemblies, assemblyLinks);

            // write to file
            graph.WriteToFile(targetFilepath);
            Log.Info($"output written to '{targetFilepath}'");
        }

        public static void AnalyzeComplexity(string sourceAssembliesFolder, string skipAssemblyFile, string targetFilepath, string rootCsv)
        {
            var rootFilter = rootCsv?.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            var skipAssemblies = GetSkipAssemblies(skipAssemblyFile, sourceAssembliesFolder, rootFilter);
            var stats = GetComplexityStats(sourceAssembliesFolder, skipAssemblies, rootFilter);

            // print, write to file
            File.WriteAllText(targetFilepath, stats);
            Log.Info($"output written to '{targetFilepath}'");
        }

        private static string GetComplexityStats(string folder, HashSet<string> skipAssemblies, string[] rootFilter)
        {
            var allAssemblies = FindConflictsProgram.GetAllAssemblies(folder, rootFilter);
            var allAssemblyLinks = allAssemblies.SelectMany(a => a.GetReferencedAssemblies().Select(a2 => Tuple.Create(a, a2)));

            var builder = CreateBuilder();
            var graph = builder.Build(allAssemblies, allAssemblyLinks);

            var refData = new Dictionary<string, RefData>(StringComparer.OrdinalIgnoreCase);
            foreach (var link in graph.Links)
            {
                if (!refData.ContainsKey(link.Source)) { refData.Add(link.Source, new RefData()); }
                if (!refData.ContainsKey(link.Target)) { refData.Add(link.Target, new RefData()); }
                refData[link.Target].DirectRefNodes.Add(link.Source);
            }

            foreach (var node in refData)
            {
                node.Value.IndirectRefCount += node.Value.DirectRefNodes.Count;
                foreach (var refNode in node.Value.DirectRefNodes)
                {
                    node.Value.IndirectRefCount += refData[refNode].DirectRefNodes.Count;
                }
            }

            var getCategory = new Func<string, string>((string assemblyName) =>
            {
                var lowPriAssemblyTag = (ShouldSkip(assemblyName, skipAssemblies) ? "ExternalAssemblies | " : "");
                var assembly = allAssemblies.SingleOrDefault(x => x.GetName().Name.Equals(assemblyName, StringComparison.OrdinalIgnoreCase));
                var framework = (assembly != null ? assembly.GetTargetFramework() : "UnknownFramework");
                return lowPriAssemblyTag + framework;
            });

            var groups = refData.GroupBy(x => getCategory(x.Key), StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x.Key);

            // print analysis
            var output = new StringBuilder();

            var uniqueFrameworks = allAssemblies.Select(x => x.GetTargetFramework()).Distinct();
            output.AppendLine(Environment.NewLine + "===> Unique Frameworks");
            output.AppendLine(string.Join(Environment.NewLine, uniqueFrameworks));

            foreach (var group in groups)
            {
                output.AppendLine(Environment.NewLine + $"===> Complexity | {group.Key}");
                var groupItems = group
                    .OrderByDescending(gi => gi.Value.IndirectRefCount)
                    .Select(gi => $"{gi.Key}\t{gi.Value.DirectRefNodes.Count}\t{gi.Value.IndirectRefCount}");
                output.AppendLine(string.Join(Environment.NewLine, groupItems));
            }

            return output.ToString();
        }

        private static HashSet<string> GetSkipAssemblies(string skipAssemblyFile, string sourceAssembliesFolder, string[] rootFilter)
        {
            skipAssemblyFile ??= string.Empty;
            var skipAssemblies = new string[] { };
            if (File.Exists(skipAssemblyFile))
            {
                skipAssemblies = File.ReadAllLines(skipAssemblyFile)
                    .Where(line => !string.IsNullOrWhiteSpace(skipAssemblyFile))
                    .ToArray();
            }
            else
            {
                skipAssemblies = skipAssemblyFile.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            }

            var assemblyMap = FindConflictsProgram.GetAllAssembliesV2(sourceAssembliesFolder, rootFilter);

            // TODO: enhance search
            var allAssemblies = assemblyMap.SelectMany(a => a.Value.GetReferencedAssemblies().Select(x => x.Name)).ToList();
            allAssemblies.AddRange(assemblyMap.Keys);
            var skipAssemblyMap = skipAssemblies
                .SelectMany(skipAssemblyName => allAssemblies.Where(a => a.StartsWith(skipAssemblyName, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(x => x)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var dependenciesOfSkipAssemblies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var processQueue = new Queue<string>(skipAssemblyMap);

            while (processQueue.Count != 0)
            {
                var assemblyName = processQueue.Dequeue();
                if (!dependenciesOfSkipAssemblies.Contains(assemblyName)
                    && assemblyMap.ContainsKey(assemblyName))
                {
                    dependenciesOfSkipAssemblies.Add(assemblyName);
                    var refs = assemblyMap[assemblyName].GetReferencedAssemblies();
                    foreach (var refAssembly in refs)
                    {
                        processQueue.Enqueue(refAssembly.Name);
                    }
                }
            }

            var assembliesRefByMap = FindConflictsProgram.GetAssembliesRefByMap(sourceAssembliesFolder, rootFilter);

            bool changedOnce;
            do
            {
                changedOnce = false;
                foreach (var assemblyName in dependenciesOfSkipAssemblies.ToList())
                {
                    if (assembliesRefByMap.ContainsKey(assemblyName))
                    {
                        var assemblyInfo = assembliesRefByMap[assemblyName];
                        var validRefs = assemblyInfo
                            .Select(x => x.GetName().Name)
                            .Where(x => !skipAssemblyMap.Contains(x))
                            .ToList();

                        if (validRefs.Count == 0)
                        {
                            skipAssemblyMap.Add(assemblyName);
                            dependenciesOfSkipAssemblies.Remove(assemblyName);
                            changedOnce = true;
                        }
                    }
                }
            }
            while (changedOnce);

            return skipAssemblyMap.OrderBy(x => x).ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        [DebuggerDisplay("{DirectRefNodes.Count} {IndirectRefCount}")]
        private class RefData
        {
            public RefData() { }
            public List<string> DirectRefNodes = new List<string>();
            public int IndirectRefCount = 0;
        }

        private static DgmlBuilder CreateBuilder()
        {
            return new DgmlBuilder(
                new HubNodeAnalysis(),
                new NodeReferencedAnalysis(),
                new CategoryColorAnalysis(
                    (".NETStandard,Version=v2.0", Color.Green),
                    (".NETFramework,Version=v4.5.1", Color.White),
                    (".NETFramework,Version=v4.5.2", Color.White),
                    (".NETFramework,Version=v4.7.1", Color.White),
                    (".NETFramework,Version=v4.7.2", Color.White),
                    ("UnknownFramework", Color.White)))
            {
                NodeBuilders = new NodeBuilder[]
                {
                    new NodeBuilder<Assembly>(
                        x => new Node
                        {
                            Id = x.GetName().Name,
                            Label = x.GetName().Name,
                            Category = x.GetTargetFramework(),
                        })
                },
                LinkBuilders = new LinkBuilder[]
                {
                    new LinkBuilder<Tuple<Assembly, AssemblyName>>(
                        x => new Link
                        {
                            Source = x.Item1.GetName().Name,
                            Target = x.Item2.Name
                        })
                },
                CategoryBuilders = new CategoryBuilder[]
                {
                    new CategoryBuilder<Assembly>(
                        x => new Category
                        {
                            Id = x.GetTargetFramework(),
                            Label = x.GetTargetFramework()
                        })
                }
            };
        }

        private static bool ShouldSkip(string assemblyName, HashSet<string> skipAssemblies)
        {
            return skipAssemblies.Contains(assemblyName);
        }
    }
}